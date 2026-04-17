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

    public async Task RecordResultAsync(BattleResult result, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (result.IsDraw)
            {
                var c = Ensure(result.Challenger);
                var t = Ensure(result.Target);
                c.Draws++;
                t.Draws++;
                c.CurrentStreak = 0;
                t.CurrentStreak = 0;

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
                winner.Wins++;
                loser.Losses++;
                winner.CurrentStreak++;
                if (winner.CurrentStreak > winner.BestStreak)
                    winner.BestStreak = winner.CurrentStreak;
                loser.CurrentStreak = 0;

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

    public IReadOnlyList<UserStats> GetAllStats() =>
        _current.Users.Values.OrderByDescending(u => u.Wins).ToList();

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
            stats = new UserStats { Nick = nick };
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
}
