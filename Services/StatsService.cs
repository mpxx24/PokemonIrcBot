using Microsoft.Extensions.Logging;
using PokemonIrcBot.Configuration;
using PokemonIrcBot.Models;

namespace PokemonIrcBot.Services;

public class StatsService : IStatsService
{
    private readonly ISeasonStorage _storage;
    private readonly SeasonOptions _season;
    private readonly ILogger<StatsService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private SeasonStats _current = new();

    public StatsService(ISeasonStorage storage, SeasonOptions season, ILogger<StatsService> logger)
    {
        _storage = storage;
        _season = season;
        _logger = logger;
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var existing = await _storage.LoadAsync(_season.Id, ct);

        if (existing is not null)
        {
            _current = existing;
            SeedEloIfNeeded();
            _logger.LogInformation(
                "Loaded season {SeasonId} with {Count} users",
                _season.Id, _current.Users.Count);
        }
        else
        {
            _current = new SeasonStats
            {
                SeasonId = _season.Id,
                SeasonName = _season.Name,
                Generations = _season.Generations,
                StartedAt = DateTime.UtcNow,
            };
            _logger.LogInformation("Started new season {SeasonId}", _season.Id);
        }
    }

    public async Task<(int ChallengerDelta, int TargetDelta)> RecordResultAsync(BattleResult result, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            int challengerDelta, targetDelta;

            if (result.IsDraw)
            {
                var c = Ensure(result.Challenger);
                var t = Ensure(result.Target);
                var cElo = c.Elo;
                var tElo = t.Elo;
                c.Draws++;
                t.Draws++;
                c.CurrentStreak = 0;
                t.CurrentStreak = 0;
                c.CurrentLossStreak = 0;
                t.CurrentLossStreak = 0;
                c.Elo = ComputeNewElo(cElo, tElo, 0.5);
                t.Elo = ComputeNewElo(tElo, cElo, 0.5);
                UpdateEloRecords(c);
                UpdateEloRecords(t);
                challengerDelta = c.Elo - cElo;
                targetDelta     = t.Elo - tElo;

                var cp = EnsurePokemon(result.ChallengerPokemon);
                var tp = EnsurePokemon(result.TargetPokemon);
                cp.Draws++;
                tp.Draws++;
                cp.Battles++;
                tp.Battles++;
                cp.CurrentStreak = 0;
                tp.CurrentStreak = 0;
            }
            else
            {
                var winner = Ensure(result.Winner!);
                var loser  = Ensure(result.Loser!);
                var winnerElo = winner.Elo;
                var loserElo  = loser.Elo;
                winner.Wins++;
                loser.Losses++;
                winner.CurrentStreak++;
                if (winner.CurrentStreak > winner.BestStreak)
                    winner.BestStreak = winner.CurrentStreak;
                winner.CurrentLossStreak = 0;
                loser.CurrentStreak = 0;
                loser.CurrentLossStreak++;
                if (loser.CurrentLossStreak > loser.WorstLossStreak)
                    loser.WorstLossStreak = loser.CurrentLossStreak;
                winner.Elo = ComputeNewElo(winnerElo, loserElo, 1.0);
                loser.Elo  = ComputeNewElo(loserElo, winnerElo, 0.0);
                UpdateEloRecords(winner);
                UpdateEloRecords(loser);

                var winnerDelta = winner.Elo - winnerElo;
                var loserDelta  = loser.Elo - loserElo;
                challengerDelta = result.Winner == result.Challenger ? winnerDelta : loserDelta;
                targetDelta     = result.Winner == result.Target     ? winnerDelta : loserDelta;

                var winnerPokemon = result.Winner == result.Challenger
                    ? result.ChallengerPokemon
                    : result.TargetPokemon;
                var loserPokemon = result.Winner == result.Challenger
                    ? result.TargetPokemon
                    : result.ChallengerPokemon;

                var wp = EnsurePokemon(winnerPokemon);
                var lp = EnsurePokemon(loserPokemon);
                wp.Wins++;
                lp.Losses++;
                wp.Battles++;
                lp.Battles++;
                wp.CurrentStreak++;
                if (wp.CurrentStreak > wp.BestStreak)
                    wp.BestStreak = wp.CurrentStreak;
                lp.CurrentStreak = 0;
            }

            Ensure(result.Challenger).Battles++;
            Ensure(result.Target).Battles++;

            await _storage.SaveAsync(_current, ct);

            return (challengerDelta, targetDelta);
        }
        finally
        {
            _lock.Release();
        }
    }

    public UserStats? GetUserStats(string nick)
    {
        _current.Users.TryGetValue(nick.ToLowerInvariant(), out var stats);
        return stats;
    }

    public IReadOnlyList<UserStats> GetAllStats(int minBattles = 0) =>
        _current.Users.Values
            .Where(u => u.Battles >= minBattles)
            .OrderByDescending(u => u.Elo)
            .ToList();

    public PokemonStats? GetPokemonStats(string name)
    {
        _current.Pokemon.TryGetValue(name.ToLowerInvariant(), out var stats);
        return stats;
    }

    public IReadOnlyList<PokemonStats> GetAllPokemonStats() =>
        _current.Pokemon.Values.OrderByDescending(p => p.Wins).ToList();

    private UserStats Ensure(string nick)
    {
        var key = nick.ToLowerInvariant();
        if (!_current.Users.TryGetValue(key, out var stats))
        {
            stats = new UserStats { Nick = nick, Elo = 1000, PeakElo = 1000, TroughElo = 1000 };
            _current.Users[key] = stats;
        }
        return stats;
    }

    private PokemonStats EnsurePokemon(string name)
    {
        var key = name.ToLowerInvariant();
        if (!_current.Pokemon.TryGetValue(key, out var stats))
        {
            stats = new PokemonStats { Name = name };
            _current.Pokemon[key] = stats;
        }
        return stats;
    }

    private static void UpdateEloRecords(UserStats u)
    {
        if (u.Elo > u.PeakElo) u.PeakElo = u.Elo;
        if (u.Elo < u.TroughElo) u.TroughElo = u.Elo;
    }

    private static int ComputeNewElo(int myElo, int opponentElo, double score)
    {
        const int K = 32;
        var expected = 1.0 / (1.0 + Math.Pow(10.0, (opponentElo - myElo) / 400.0));
        return (int)Math.Round(myElo + K * (score - expected));
    }

    private void SeedEloIfNeeded()
    {
        var needsSeeding = _current.Users.Values.Where(u => u.Elo == 0).ToList();
        if (needsSeeding.Count == 0) return;

        var withBattles = needsSeeding.Where(u => u.Battles > 0).ToList();
        if (withBattles.Count == 0)
        {
            foreach (var u in needsSeeding) { u.Elo = 1000; u.PeakElo = 1000; u.TroughElo = 1000; }
            return;
        }

        var minRate = withBattles.Min(u => (double)u.Wins / u.Battles);
        var maxRate = withBattles.Max(u => (double)u.Wins / u.Battles);

        foreach (var u in needsSeeding)
        {
            if (u.Battles == 0) { u.Elo = 1000; u.PeakElo = 1000; u.TroughElo = 1000; continue; }

            var rate = (double)u.Wins / u.Battles;
            var normalized = maxRate > minRate
                ? (rate - minRate) / (maxRate - minRate) - 0.5
                : 0.0;
            u.Elo = (int)Math.Round(1000 + normalized * 64);
            u.PeakElo = u.Elo;
            u.TroughElo = u.Elo;
        }
    }
}
