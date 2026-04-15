using Microsoft.Extensions.Logging;
using PokemonIrcBot.Configuration;
using PokemonIrcBot.Models;

namespace PokemonIrcBot.Services;

public class BattleService : IBattleService
{
    private static readonly Dictionary<int, (int Min, int Max)> GenRanges = new()
    {
        { 1, (1,   151) },
        { 2, (152, 251) },
        { 3, (252, 386) },
        { 4, (387, 493) },
        { 5, (494, 649) },
        { 6, (650, 721) },
        { 7, (722, 809) },
        { 8, (810, 905) },
        { 9, (906, 1025) },
    };

    private readonly IPokemonApiClient _apiClient;
    private readonly SeasonOptions _season;
    private readonly ILogger<BattleService> _logger;

    public BattleService(IPokemonApiClient apiClient, SeasonOptions season, ILogger<BattleService> logger)
    {
        _apiClient = apiClient;
        _season = season;
        _logger = logger;
    }

    public async Task<BattleResult> FightAsync(string challenger, string target, CancellationToken ct = default)
    {
        if (string.Equals(challenger, target, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A trainer cannot battle themselves.", nameof(target));

        var rand = new Random();
        var (p1, p2) = await PickTwoDifferentPokemonAsync(rand, ct);

        _logger.LogInformation(
            "Battle starting: {Challenger} ({P1}) vs {Target} ({P2})",
            challenger, p1.Name, target, p2.Name);

        return SimulateBattle(challenger, target, p1, p2, rand);
    }

    public int CalcDamage(Pokemon attacker, Pokemon defender, int movePower, bool crit)
    {
        // Simplified Gen 1 damage formula at level 50
        double base_ = (22.0 * movePower * attacker.Attack / defender.Defense / 50.0) + 2;
        if (crit) base_ *= 1.5;
        return Math.Max(1, (int)base_);
    }

    private const int MaxRounds = 50;

    private BattleResult SimulateBattle(string challenger, string target, Pokemon p1, Pokemon p2, Random rand)
    {
        int hp1 = p1.Hp;
        int hp2 = p2.Hp;

        // Higher speed goes first; tie broken by coin flip
        bool p1First = p1.Speed > p2.Speed || (p1.Speed == p2.Speed && rand.Next(2) == 0);

        var rounds = new List<BattleRound>();

        while (hp1 > 0 && hp2 > 0 && rounds.Count < MaxRounds)
        {
            if (p1First)
            {
                var (dmg1, move1, crit1, tm1) = Attack(p1, p2, rand);
                hp2 = Math.Max(0, hp2 - dmg1);
                rounds.Add(new BattleRound(p1.Name, p2.Name, move1, dmg1, crit1, hp1, hp2, tm1));
                if (hp2 <= 0) break;

                var (dmg2, move2, crit2, tm2) = Attack(p2, p1, rand);
                hp1 = Math.Max(0, hp1 - dmg2);
                rounds.Add(new BattleRound(p2.Name, p1.Name, move2, dmg2, crit2, hp2, hp1, tm2));
            }
            else
            {
                var (dmg2, move2, crit2, tm2) = Attack(p2, p1, rand);
                hp1 = Math.Max(0, hp1 - dmg2);
                rounds.Add(new BattleRound(p2.Name, p1.Name, move2, dmg2, crit2, hp2, hp1, tm2));
                if (hp1 <= 0) break;

                var (dmg1, move1, crit1, tm1) = Attack(p1, p2, rand);
                hp2 = Math.Max(0, hp2 - dmg1);
                rounds.Add(new BattleRound(p1.Name, p2.Name, move1, dmg1, crit1, hp1, hp2, tm1));
            }
        }

        if (hp1 > 0 && hp2 <= 0)
            return new BattleResult(challenger, target, p1.Name, p2.Name, challenger, target, false, rounds)
                { ChallengerTypes = p1.Types, TargetTypes = p2.Types };

        if (hp2 > 0 && hp1 <= 0)
            return new BattleResult(challenger, target, p1.Name, p2.Name, target, challenger, false, rounds)
                { ChallengerTypes = p1.Types, TargetTypes = p2.Types };

        return new BattleResult(challenger, target, p1.Name, p2.Name, null, null, true, rounds)
            { ChallengerTypes = p1.Types, TargetTypes = p2.Types };
    }

    private static (int Damage, string MoveName, bool Crit, double TypeMultiplier) Attack(
        Pokemon attacker, Pokemon defender, Random rand)
    {
        string moveName = attacker.MoveNames.Count > 0
            ? attacker.MoveNames[rand.Next(attacker.MoveNames.Count)]
            : "attacks";

        int movePower = rand.Next(40, 101); // simulate move power 40-100
        bool crit = rand.Next(16) == 0;     // ~6.25% crit chance
        double modifier = (rand.Next(16) + 85) / 100.0; // 0.85–1.0

        // Attacker's primary type determines move type (STAB assumption)
        var attackingType = attacker.Types.Count > 0 ? attacker.Types[0] : string.Empty;
        double typeMultiplier = TypeChart.GetMultiplier(attackingType, defender.Types);

        double damage = (22.0 * movePower * attacker.Attack / defender.Defense / 50.0) + 2;
        if (crit) damage *= 1.5;
        damage *= modifier;
        damage *= typeMultiplier;

        return (Math.Max(0, (int)damage), moveName, crit, typeMultiplier);
    }

    private async Task<(Pokemon, Pokemon)> PickTwoDifferentPokemonAsync(Random rand, CancellationToken ct)
    {
        var pool = BuildPool();

        int id1 = pool[rand.Next(pool.Count)];
        int id2;
        do { id2 = pool[rand.Next(pool.Count)]; } while (id2 == id1);

        var p1 = await _apiClient.GetPokemonAsync(id1, ct);
        var p2 = await _apiClient.GetPokemonAsync(id2, ct);
        return (p1, p2);
    }

    private List<int> BuildPool()
    {
        var pool = new List<int>();
        foreach (var gen in _season.Generations)
        {
            if (!GenRanges.TryGetValue(gen, out var range))
            {
                _logger.LogWarning("Unknown generation {Gen} in season config — skipping", gen);
                continue;
            }
            for (int i = range.Min; i <= range.Max; i++)
                pool.Add(i);
        }

        if (pool.Count == 0)
            throw new InvalidOperationException("No Pokemon available for the configured generations.");

        return pool;
    }
}
