using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PokemonIrcBot.Configuration;
using PokemonIrcBot.Models;

namespace PokemonIrcBot.Services;

public class IrcService : BackgroundService
{
    private readonly IrcOptions _irc;
    private readonly IBattleService _battles;
    private readonly IStatsService _stats;
    private readonly TelemetryClient _telemetry;
    private readonly ILogger<IrcService> _logger;

    private StreamWriter? _writer;
    private bool _pendingNickClaim;
    private readonly HashSet<string> _activeBattlers = [];
    private readonly Dictionary<string, DateTimeOffset> _cooldowns = [];
    private readonly SemaphoreSlim _battleLock = new(1, 1);
    private readonly HashSet<string> _onlineNicks = new(StringComparer.OrdinalIgnoreCase);

    public IrcService(
        IrcOptions irc,
        IBattleService battles,
        IStatsService stats,
        TelemetryClient telemetry,
        ILogger<IrcService> logger)
    {
        _irc = irc;
        _battles = battles;
        _stats = stats;
        _telemetry = telemetry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _stats.LoadAsync(stoppingToken);

        int delaySeconds = _irc.ReconnectDelaySeconds;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Connecting to {Host}:{Port}", _irc.Host, _irc.Port);
                _telemetry.TrackEvent("IrcConnecting", new Dictionary<string, string>
                {
                    ["host"] = _irc.Host,
                    ["port"] = _irc.Port.ToString(),
                });

                await ConnectAndRunAsync(stoppingToken);
                delaySeconds = _irc.ReconnectDelaySeconds;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IRC connection lost. Reconnecting in {Delay}s", delaySeconds);
                _telemetry.TrackException(ex);

                try { await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken); }
                catch (OperationCanceledException) { break; }

                delaySeconds = Math.Min(delaySeconds * 2, _irc.MaxReconnectDelaySeconds);
            }
        }

        _logger.LogInformation("IrcService stopped");
    }

    private async Task ConnectAndRunAsync(CancellationToken ct)
    {
        _pendingNickClaim = false;
        _onlineNicks.Clear();
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(_irc.Host, _irc.Port, ct);

        Stream stream;
        if (_irc.UseTls)
        {
            var sslStream = new SslStream(tcpClient.GetStream(), leaveInnerStreamOpen: false);
            await sslStream.AuthenticateAsClientAsync(_irc.Host);
            stream = sslStream;
        }
        else
        {
            stream = tcpClient.GetStream();
        }

        await using (stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            await using (writer)
            {
                _writer = writer;

                // Start CAP negotiation before NICK/USER so the server holds registration open
                await SendRawAsync("CAP LS 302", ct);
                await SendRawAsync($"NICK {_irc.Nick}", ct);
                await SendRawAsync($"USER {_irc.Nick} 0 * :{_irc.RealName}", ct);

                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line is null)
                    {
                        _logger.LogWarning("Server closed connection");
                        break;
                    }

                    _logger.LogDebug("< {Line}", line);
                    await HandleLineAsync(line, ct);
                }

                _writer = null;
            }
        }
    }

    private async Task HandleLineAsync(string line, CancellationToken ct)
    {
        var (prefix, command, parameters, trailing) = ParseLine(line);

        switch (command)
        {
            case "PING":
                await SendRawAsync($"PONG :{trailing ?? parameters.FirstOrDefault()}", ct);
                break;

            case "CAP":
                var capSubCmd = parameters.ElementAtOrDefault(1);
                if (capSubCmd == "LS" && parameters.ElementAtOrDefault(2) != "*")
                {
                    // Final (or only) LS line — request what we need, or end negotiation
                    if (!string.IsNullOrEmpty(_irc.SaslPassword))
                    {
                        _telemetry.TrackEvent("SaslAttempting");
                        await SendRawAsync("CAP REQ :sasl", ct);
                    }
                    else
                    {
                        _telemetry.TrackEvent("SaslSkipped");
                        await SendRawAsync("CAP END", ct);
                    }
                }
                else if (capSubCmd == "ACK" && trailing?.Contains("sasl") == true)
                {
                    _telemetry.TrackEvent("SaslCAPAck");
                    await SendRawAsync("AUTHENTICATE PLAIN", ct);
                }
                break;

            case "AUTHENTICATE":
                // AUTHENTICATE + — server ready for credentials
                if (parameters.FirstOrDefault() == "+")
                {
                    _telemetry.TrackEvent("SaslSendingCredentials");
                    var payload = Convert.ToBase64String(
                        System.Text.Encoding.UTF8.GetBytes($"\0{_irc.Nick}\0{_irc.SaslPassword}"));
                    await SendRawAsync($"AUTHENTICATE {payload}", ct);
                }
                break;

            case "903": // SASL authentication successful
                _telemetry.TrackEvent("SaslSuccess");
                _logger.LogWarning("SASL authentication successful");
                await SendRawAsync("CAP END", ct);
                break;

            case "904": // SASL authentication failed
                _telemetry.TrackEvent("SaslFailed");
                _logger.LogError("SASL authentication failed — check IrcSaslPassword in Key Vault");
                throw new Exception("SASL authentication failed");

            case "001": // RPL_WELCOME — server accepted us
                _logger.LogInformation("Connected to IRC as {Nick}, joining {Channel}", _irc.Nick, _irc.Channel);
                _telemetry.TrackEvent("IrcConnected", new Dictionary<string, string> { ["nick"] = _irc.Nick });
                await SendRawAsync($"JOIN {_irc.Channel}", ct);
                break;

            case "433": // ERR_NICKNAMEINUSE
                _logger.LogWarning("Nick {Nick} in use, sending GHOST to reclaim", _irc.Nick);
                _pendingNickClaim = true;
                await SendRawAsync($"PRIVMSG NickServ :GHOST {_irc.Nick}", ct);
                break;

            case "NOTICE":
                var noticeFrom = ExtractNick(prefix);
                if (_pendingNickClaim
                    && string.Equals(noticeFrom, "NickServ", StringComparison.OrdinalIgnoreCase)
                    && trailing?.Contains("ghosted", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _pendingNickClaim = false;
                    _logger.LogInformation("Ghost complete, reclaiming nick {Nick} and joining {Channel}", _irc.Nick, _irc.Channel);
                    await SendRawAsync($"NICK {_irc.Nick}", ct);
                    await SendRawAsync($"JOIN {_irc.Channel}", ct);
                }
                break;

            case "353": // RPL_NAMREPLY — initial nick list when joining
                if (trailing is not null)
                    TrackNamesListNicks(trailing);
                break;

            case "JOIN":
                var joiner = ExtractNick(prefix);
                if (joiner is not null)
                    TrackNickJoined(joiner);
                break;

            case "PART":
            case "QUIT":
                var leaver = ExtractNick(prefix);
                if (leaver is not null)
                    TrackNickLeft(leaver);
                break;

            case "KICK":
                // KICK #channel kicked_nick :reason
                var kicked = parameters.ElementAtOrDefault(1);
                if (kicked is not null)
                    TrackNickLeft(kicked);
                break;

            case "NICK":
                var oldNick = ExtractNick(prefix);
                var newNick = trailing ?? parameters.FirstOrDefault();
                if (oldNick is not null && newNick is not null)
                    TrackNickRenamed(oldNick, newNick);
                break;

            case "PRIVMSG":
                var target = parameters.FirstOrDefault();
                var senderNick = ExtractNick(prefix);
                if (target == _irc.Channel && senderNick is not null && trailing is not null)
                    await HandleChannelMessageAsync(senderNick, trailing.Trim(), ct);
                break;
        }
    }

    private async Task HandleChannelMessageAsync(string sender, string message, CancellationToken ct)
    {
        if (!message.StartsWith('!'))
            return;

        var parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLowerInvariant();

        switch (command)
        {
            case "!battle":
                if (parts.Length < 2)
                {
                    await SayAsync($"{sender}: Usage: !battle <nick>", ct);
                    return;
                }
                await HandleBattleAsync(sender, parts[1], ct);
                break;

            case "!stats":
                var nickForStats = parts.Length >= 2 ? parts[1] : sender;
                await HandleStatsAsync(nickForStats, ct);
                break;

            case "!standings":
                await HandleStandingsAsync(ct);
                break;

            case "!pokestats":
                var pokeName = parts.Length >= 2 ? parts[1] : null;
                if (pokeName is null)
                {
                    await SayAsync($"{sender}: Usage: !pokestats <pokemon>", ct);
                    return;
                }
                await HandlePokeStatsAsync(pokeName, ct);
                break;

            case "!pokestandings":
                await HandlePokeStandingsAsync(ct);
                break;

            case "!help":
                await HandleHelpAsync(ct);
                break;
        }
    }

    private async Task HandleBattleAsync(string challenger, string target, CancellationToken ct)
    {
        if (string.Equals(challenger, target, StringComparison.OrdinalIgnoreCase))
        {
            await SayAsync($"{challenger}: You can't battle yourself!", ct);
            return;
        }

        if (!_onlineNicks.Contains(target))
        {
            await SayAsync($"{challenger}: {target} is not in the channel.", ct);
            return;
        }

        await _battleLock.WaitAsync(ct);
        try
        {
            var chalKey = challenger.ToLowerInvariant();
            var targetKey = target.ToLowerInvariant();

            if (_activeBattlers.Contains(chalKey))
            {
                await SayAsync($"{challenger}: You're already in a battle!", ct);
                return;
            }
            if (_activeBattlers.Contains(targetKey))
            {
                await SayAsync($"{challenger}: {target} is already in a battle!", ct);
                return;
            }

            if (_irc.BattleCooldownSeconds > 0
                && _cooldowns.TryGetValue(chalKey, out var chalReady)
                && DateTimeOffset.UtcNow < chalReady)
            {
                var secs = (int)(chalReady - DateTimeOffset.UtcNow).TotalSeconds + 1;
                await SayAsync($"{challenger}: Cooldown — wait {secs}s before battling again.", ct);
                return;
            }

            _activeBattlers.Add(chalKey);
            _activeBattlers.Add(targetKey);
        }
        finally
        {
            _battleLock.Release();
        }

        try
        {
            await SayAsync($"Battle starting: {challenger} vs {target}! Choosing Pokemon...", ct);

            var result = await _battles.FightAsync(challenger, target, ct);

            await AnnounceResultAsync(result, ct);
            await _stats.RecordResultAsync(result, ct);

            _telemetry.TrackEvent("BattleCompleted", new Dictionary<string, string>
            {
                ["challenger"] = challenger,
                ["target"] = target,
                ["isDraw"] = result.IsDraw.ToString(),
                ["winner"] = result.Winner ?? "draw",
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Battle between {Challenger} and {Target} failed", challenger, target);
            await SayAsync($"Something went wrong during the battle. Try again later.", ct);
        }
        finally
        {
            await _battleLock.WaitAsync(ct);
            try
            {
                _activeBattlers.Remove(challenger.ToLowerInvariant());
                _activeBattlers.Remove(target.ToLowerInvariant());

                if (_irc.BattleCooldownSeconds > 0)
                {
                    var ready = DateTimeOffset.UtcNow.AddSeconds(_irc.BattleCooldownSeconds);
                    _cooldowns[challenger.ToLowerInvariant()] = ready;
                }
            }
            finally
            {
                _battleLock.Release();
            }
        }
    }

    private async Task AnnounceResultAsync(BattleResult result, CancellationToken ct)
    {
        var challengerPokemon = Capitalise(result.ChallengerPokemon);
        var targetPokemon = Capitalise(result.TargetPokemon);

        var chalTypes = FormatTypes(result.ChallengerTypes);
        var targTypes = FormatTypes(result.TargetTypes);

        var pokeStandings = _stats.GetAllPokemonStats();
        var chalTag = FormatPokeTag(result.ChallengerPokemon, pokeStandings);
        var targTag = FormatPokeTag(result.TargetPokemon, pokeStandings);

        await SayAsync(
            $"{result.Challenger} chose {challengerPokemon}{chalTypes}{chalTag} | {result.Target} chose {targetPokemon}{targTypes}{targTag}",
            ct);

        foreach (var round in result.Rounds)
        {
            var attacker  = Capitalise(round.AttackerPokemon);
            var defender  = Capitalise(round.DefenderPokemon);
            var move      = FormatMoveName(round.MoveName);
            var crit      = round.Crit ? " (CRIT!)" : string.Empty;
            var typeNote  = FormatTypeEffectiveness(round.TypeMultiplier);
            var hpNote    = round.DefenderHpAfter <= 0
                ? $"{defender} fainted!"
                : $"{defender}: {round.DefenderHpAfter} HP";

            await SayAsync($"{attacker} used {move} → {round.Damage} dmg{crit}{typeNote} → {hpNote}", ct);
        }

        if (result.IsDraw)
        {
            await SayAsync(
                $"Both Pokemon fainted at the same time — it's a DRAW! Well fought, {result.Challenger} and {result.Target}!",
                ct);
        }
        else
        {
            var winnerPokemon = result.Winner == result.Challenger ? result.ChallengerPokemon : result.TargetPokemon;
            var loserPokemon  = result.Winner == result.Challenger ? result.TargetPokemon : result.ChallengerPokemon;
            await SayAsync(
                $"{result.Winner} wins! {Capitalise(winnerPokemon)} defeated {Capitalise(loserPokemon)}.",
                ct);
        }
    }

    private static string FormatMoveName(string name) =>
        string.Join(' ', name.Split('-').Select(Capitalise));

    private static string FormatTypes(IReadOnlyList<string> types) =>
        types.Count == 0 ? string.Empty : $" [{string.Join("/", types.Select(Capitalise))}]";

    private static string FormatTypeEffectiveness(double multiplier) => multiplier switch
    {
        0.0  => " [Immune!]",
        0.25 => " [Not very effective... 0.25×]",
        0.5  => " [Not very effective... 0.5×]",
        2.0  => " [Super effective! 2×]",
        4.0  => " [Super effective! 4×]",
        _    => string.Empty,
    };

    internal static string FormatPokeTag(string name, IReadOnlyList<PokemonStats> allStats)
    {
        int rank = -1;
        bool undefeated = false;

        for (int i = 0; i < allStats.Count; i++)
        {
            var s = allStats[i];
            if (!string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (i < 10 && s.Battles > 0) rank = i + 1;
            undefeated = s.Battles > 0 && s.Losses == 0;
            break;
        }

        if (rank < 0 && !undefeated) return "";
        if (rank >= 0 && undefeated) return $" [#{rank} UNDEFEATED]";
        if (rank >= 0) return $" [#{rank}]";
        return " [UNDEFEATED]";
    }

    private static string GetRank(int elo) => elo switch
    {
        < 950  => "Youngster",
        < 1050 => "Bug Catcher",
        < 1150 => "Gym Trainer",
        < 1300 => "Gym Leader",
        _      => "Elite Four",
    };

    private async Task HandleStatsAsync(string nick, CancellationToken ct)
    {
        var stats = _stats.GetUserStats(nick);
        if (stats is null)
        {
            await SayAsync($"No stats found for {nick} this season.", ct);
            return;
        }

        var winRate = stats.Battles > 0
            ? (int)(stats.Wins * 100.0 / stats.Battles)
            : 0;

        var rank   = GetRank(stats.Elo);
        var streak = stats.CurrentStreak > 0 ? $" | Streak: {stats.CurrentStreak}W" : string.Empty;

        await SayAsync(
            $"{stats.Nick} [{rank}] ELO:{stats.Elo} — Battles: {stats.Battles} | W:{stats.Wins} L:{stats.Losses} D:{stats.Draws} | {winRate}% wins{streak} | Best: {stats.BestStreak}W",
            ct);
    }

    private async Task HandleStandingsAsync(CancellationToken ct)
    {
        var all = _stats.GetAllStats(minBattles: 5);
        if (all.Count == 0)
        {
            await SayAsync("No battles recorded this season yet.", ct);
            return;
        }

        await SayAsync("=== Season Standings ===", ct);
        int pos = 1;
        foreach (var u in all.Take(10))
        {
            var winRate = u.Battles > 0 ? (int)(u.Wins * 100.0 / u.Battles) : 0;
            var rank    = GetRank(u.Elo);
            await SayAsync(
                $"#{pos} {u.Nick} [{rank}] ELO:{u.Elo} — W:{u.Wins} L:{u.Losses} D:{u.Draws} ({winRate}% wins) | Best streak: {u.BestStreak}W",
                ct);
            pos++;
        }
    }

    private async Task HandlePokeStatsAsync(string name, CancellationToken ct)
    {
        var stats = _stats.GetPokemonStats(name);
        if (stats is null)
        {
            await SayAsync($"No stats found for {Capitalise(name)} this season.", ct);
            return;
        }

        var winRate = stats.Battles > 0
            ? (int)(stats.Wins * 100.0 / stats.Battles)
            : 0;

        var streak = stats.CurrentStreak > 0 ? $" | Streak: {stats.CurrentStreak}W" : string.Empty;

        await SayAsync(
            $"{Capitalise(stats.Name)} — Battles: {stats.Battles} | W:{stats.Wins} L:{stats.Losses} D:{stats.Draws} | {winRate}% wins{streak} | Best: {stats.BestStreak}W",
            ct);
    }

    private async Task HandlePokeStandingsAsync(CancellationToken ct)
    {
        var all = _stats.GetAllPokemonStats();
        if (all.Count == 0)
        {
            await SayAsync("No Pokemon battles recorded this season yet.", ct);
            return;
        }

        await SayAsync("=== Pokemon Standings ===", ct);
        int pos = 1;
        foreach (var p in all.Take(10))
        {
            var winRate = p.Battles > 0 ? (int)(p.Wins * 100.0 / p.Battles) : 0;
            await SayAsync(
                $"#{pos} {Capitalise(p.Name)} — W:{p.Wins} L:{p.Losses} D:{p.Draws} ({winRate}% wins) | Best streak: {p.BestStreak}W",
                ct);
            pos++;
        }
    }

    private async Task HandleHelpAsync(CancellationToken ct)
    {
        await SayAsync("Commands: !battle <nick> | !stats [nick] | !standings | !pokestats <pokemon> | !pokestandings | !help", ct);
    }

    private async Task SayAsync(string message, CancellationToken ct) =>
        await SendRawAsync($"PRIVMSG {_irc.Channel} :{message}", ct);

    private async Task SendRawAsync(string line, CancellationToken ct)
    {
        if (_writer is null) return;
        _logger.LogDebug("> {Line}", line);
        await _writer.WriteLineAsync(line.AsMemory(), ct);
    }

    internal static (string? Prefix, string Command, string[] Parameters, string? Trailing) ParseLine(string line)
    {
        string? prefix = null;
        if (line.StartsWith(':'))
        {
            int spaceIdx = line.IndexOf(' ');
            prefix = line[1..spaceIdx];
            line = line[(spaceIdx + 1)..];
        }

        string? trailing = null;
        int trailingIdx = line.IndexOf(" :");
        if (trailingIdx >= 0)
        {
            trailing = line[(trailingIdx + 2)..];
            line = line[..trailingIdx];
        }

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (prefix, parts[0], parts[1..], trailing);
    }

    private static string? ExtractNick(string? prefix) =>
        prefix?.Split('!')[0];

    // ── Online-nick tracking ────────────────────────────────────────────────

    /// <summary>Strips IRC mode prefixes (@, +, %, &amp;, ~) from each entry in a 353 RPL_NAMREPLY trailing.</summary>
    internal static string[] ParseNamesListNicks(string trailing) =>
        trailing.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(n => n.TrimStart('@', '+', '%', '&', '~'))
                .Where(n => n.Length > 0)
                .ToArray();

    internal bool IsNickOnline(string nick) => _onlineNicks.Contains(nick);

    internal void TrackNickJoined(string nick) => _onlineNicks.Add(nick);

    internal void TrackNickLeft(string nick) => _onlineNicks.Remove(nick);

    internal void TrackNickRenamed(string oldNick, string newNick)
    {
        _onlineNicks.Remove(oldNick);
        _onlineNicks.Add(newNick);
    }

    internal void TrackNamesListNicks(string trailing)
    {
        foreach (var nick in ParseNamesListNicks(trailing))
            _onlineNicks.Add(nick);
    }

    internal void ClearOnlineNicks() => _onlineNicks.Clear();

    private static string Capitalise(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

