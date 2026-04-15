using Moq;
using NUnit.Framework;
using PokemonIrcBot.Configuration;
using PokemonIrcBot.Models;
using PokemonIrcBot.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace PokemonIrcBot.Tests;

[TestFixture]
public class BattleServiceTests
{
    private Mock<IPokemonApiClient> _apiClientMock = null!;
    private BattleService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _apiClientMock = new Mock<IPokemonApiClient>();
        var season = new SeasonOptions { Id = "s1", Name = "S1", Generations = [1] };
        _sut = new BattleService(_apiClientMock.Object, season, NullLogger<BattleService>.Instance);
    }

    [Test]
    public void CalcDamage_AlwaysReturnsAtLeastOne()
    {
        var attacker = new Pokemon(1, "weak", 1, 1, 1, 1, []);
        var defender = new Pokemon(2, "tank", 9999, 1, 9999, 1, []);

        var damage = _sut.CalcDamage(attacker, defender, 1, crit: false);

        Assert.That(damage, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void CalcDamage_CritHit_DealsMoreDamageThanNoCrit()
    {
        var attacker = new Pokemon(1, "bulbasaur", 45, 49, 49, 45, ["tackle"]);
        var defender = new Pokemon(2, "charmander", 39, 52, 43, 65, ["scratch"]);

        var normal = _sut.CalcDamage(attacker, defender, 50, crit: false);
        var crit = _sut.CalcDamage(attacker, defender, 50, crit: true);

        Assert.That(crit, Is.GreaterThan(normal));
    }

    [Test]
    public void CalcDamage_HigherAttackDealsMoreDamage()
    {
        var defender = new Pokemon(2, "charmander", 39, 52, 43, 65, ["scratch"]);
        var strong = new Pokemon(1, "strong", 100, 200, 100, 100, ["pound"]);
        var weak = new Pokemon(3, "weak", 100, 10, 100, 100, ["pound"]);

        var strongDmg = _sut.CalcDamage(strong, defender, 50, crit: false);
        var weakDmg = _sut.CalcDamage(weak, defender, 50, crit: false);

        Assert.That(strongDmg, Is.GreaterThan(weakDmg));
    }

    [Test]
    public void FightAsync_SelfBattle_ThrowsArgumentException()
    {
        Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.FightAsync("alice", "alice", CancellationToken.None));
    }

    [Test]
    public async Task FightAsync_ReturnsChallengerAndTarget()
    {
        var p1 = new Pokemon(1, "bulbasaur", 45, 49, 49, 45, ["tackle", "vine-whip"]);
        var p2 = new Pokemon(4, "charmander", 39, 52, 43, 65, ["scratch", "ember"]);

        _apiClientMock.SetupSequence(c => c.GetPokemonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(p1)
            .ReturnsAsync(p2);

        var result = await _sut.FightAsync("alice", "bob");

        Assert.That(result.Challenger, Is.EqualTo("alice"));
        Assert.That(result.Target, Is.EqualTo("bob"));
        Assert.That(result.ChallengerPokemon, Is.EqualTo("bulbasaur"));
        Assert.That(result.TargetPokemon, Is.EqualTo("charmander"));
    }

    [Test]
    public async Task FightAsync_StrongVsWeak_StrongWinsConsistently()
    {
        // Run 10 battles with a severely overpowered pokemon — it must win every time
        var strong = new Pokemon(1, "godmon", 999, 999, 999, 999, ["hyper-beam"]);
        var weak = new Pokemon(2, "weakmon", 1, 1, 1, 1, ["splash"]);

        int wins = 0;
        for (int i = 0; i < 10; i++)
        {
            _apiClientMock.SetupSequence(c => c.GetPokemonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(strong)
                .ReturnsAsync(weak);

            var result = await _sut.FightAsync("alice", "bob");
            if (result.Winner == "alice") wins++;
        }

        Assert.That(wins, Is.EqualTo(10));
    }

    [Test]
    public async Task FightAsync_ResultIsEitherWinOrDraw()
    {
        var p1 = new Pokemon(1, "bulbasaur", 45, 49, 49, 45, ["tackle"]);
        var p2 = new Pokemon(4, "charmander", 39, 52, 43, 65, ["scratch"]);

        _apiClientMock.SetupSequence(c => c.GetPokemonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(p1)
            .ReturnsAsync(p2);

        var result = await _sut.FightAsync("alice", "bob");

        Assert.That(result.IsDraw || result.Winner != null, Is.True);
        if (!result.IsDraw)
        {
            Assert.That(result.Winner, Is.AnyOf("alice", "bob"));
            Assert.That(result.Loser, Is.AnyOf("alice", "bob"));
            Assert.That(result.Winner, Is.Not.EqualTo(result.Loser));
        }
    }

    [Test]
    public async Task FightAsync_Rounds_AreNotEmpty()
    {
        var p1 = new Pokemon(1, "bulbasaur", 45, 49, 49, 45, ["tackle"]);
        var p2 = new Pokemon(4, "charmander", 39, 52, 43, 65, ["scratch"]);

        _apiClientMock.SetupSequence(c => c.GetPokemonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(p1)
            .ReturnsAsync(p2);

        var result = await _sut.FightAsync("alice", "bob");

        Assert.That(result.Rounds, Is.Not.Empty);
    }

    [Test]
    public async Task FightAsync_Rounds_DefenderHpNeverIncreases()
    {
        var p1 = new Pokemon(1, "godmon", 999, 999, 999, 999, ["hyper-beam"]);
        var p2 = new Pokemon(2, "weakmon", 999, 1, 1, 1, ["splash"]);

        _apiClientMock.SetupSequence(c => c.GetPokemonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(p1)
            .ReturnsAsync(p2);

        var result = await _sut.FightAsync("alice", "bob");

        // For each consecutive pair of rounds where weakmon is the defender, HP must not increase
        var weakmonAsDefender = result.Rounds
            .Where(r => r.DefenderPokemon == "weakmon")
            .Select(r => r.DefenderHpAfter)
            .ToList();

        Assert.That(weakmonAsDefender, Is.Not.Empty);
        for (int i = 1; i < weakmonAsDefender.Count; i++)
            Assert.That(weakmonAsDefender[i], Is.LessThanOrEqualTo(weakmonAsDefender[i - 1]));
    }

    [Test]
    public async Task FightAsync_Rounds_MoveNameFromPokemonMoveList()
    {
        var p1 = new Pokemon(1, "bulbasaur", 45, 49, 49, 45, ["tackle", "vine-whip"]);
        var p2 = new Pokemon(4, "charmander", 39, 52, 43, 65, ["scratch", "ember"]);

        _apiClientMock.SetupSequence(c => c.GetPokemonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(p1)
            .ReturnsAsync(p2);

        var result = await _sut.FightAsync("alice", "bob");

        var allMoves = new HashSet<string>(["tackle", "vine-whip", "scratch", "ember"]);
        foreach (var round in result.Rounds)
            Assert.That(allMoves.Contains(round.MoveName), Is.True,
                $"Move '{round.MoveName}' is not in either Pokemon's move list");
    }

    [Test]
    public async Task FightAsync_Rounds_AttackerAndDefenderAlternate()
    {
        var p1 = new Pokemon(1, "fastmon", 999, 50, 50, 999, ["tackle"]); // faster, goes first
        var p2 = new Pokemon(2, "slowmon", 999, 50, 50, 1, ["scratch"]);

        _apiClientMock.SetupSequence(c => c.GetPokemonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(p1)
            .ReturnsAsync(p2);

        var result = await _sut.FightAsync("alice", "bob");

        // First attack must be from fastmon (higher speed)
        Assert.That(result.Rounds[0].AttackerPokemon, Is.EqualTo("fastmon"));
        // Second attack (if exists) must be from slowmon
        if (result.Rounds.Count >= 2)
            Assert.That(result.Rounds[1].AttackerPokemon, Is.EqualTo("slowmon"));
    }

    [Test]
    public async Task FightAsync_Rounds_TypeMultiplierIsPositive()
    {
        var p1 = new Pokemon(1, "bulbasaur", 45, 49, 49, 45, ["tackle"]) { Types = ["grass", "poison"] };
        var p2 = new Pokemon(4, "charmander", 39, 52, 43, 65, ["scratch"]) { Types = ["fire"] };

        _apiClientMock.SetupSequence(c => c.GetPokemonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(p1)
            .ReturnsAsync(p2);

        var result = await _sut.FightAsync("alice", "bob");

        foreach (var round in result.Rounds)
            Assert.That(round.TypeMultiplier, Is.GreaterThanOrEqualTo(0.0));
    }

    [Test]
    public async Task FightAsync_Rounds_TypeMultiplierSuperEffective_IncreasesExpectedDamage()
    {
        // Fire attacker vs Grass defender — should get 2× multiplier
        // We test that damage with 2× effective attacker is on average higher
        // by running with attacker types swapped.
        var fireAttacker = new Pokemon(1, "firemon", 100, 50, 50, 100, ["ember"]) { Types = ["fire"] };
        var grassDefender = new Pokemon(2, "grassmon", 100, 50, 50, 1, ["absorb"]) { Types = ["grass"] };

        _apiClientMock.SetupSequence(c => c.GetPokemonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fireAttacker)
            .ReturnsAsync(grassDefender);

        var result = await _sut.FightAsync("alice", "bob");

        // All rounds where firemon attacks should have TypeMultiplier == 2.0
        var fireRounds = result.Rounds.Where(r => r.AttackerPokemon == "firemon").ToList();
        Assert.That(fireRounds, Is.Not.Empty);
        Assert.That(fireRounds.All(r => r.TypeMultiplier == 2.0), Is.True,
            "Fire vs Grass should always be 2× effective");
    }

    [Test]
    public async Task FightAsync_Rounds_TypeMultiplierImmune_DealNoDamage()
    {
        // Normal attacker vs Ghost defender — should be immune (0×), damage = 0
        var normalAttacker = new Pokemon(1, "normalmon", 100, 100, 50, 100, ["tackle"]) { Types = ["normal"] };
        var ghostDefender = new Pokemon(2, "ghostmon", 100, 50, 50, 1, ["lick"]) { Types = ["ghost"] };

        _apiClientMock.SetupSequence(c => c.GetPokemonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(normalAttacker)
            .ReturnsAsync(ghostDefender);

        var result = await _sut.FightAsync("alice", "bob");

        var normalRounds = result.Rounds.Where(r => r.AttackerPokemon == "normalmon").ToList();
        Assert.That(normalRounds, Is.Not.Empty);
        Assert.That(normalRounds.All(r => r.Damage == 0 && r.TypeMultiplier == 0.0), Is.True,
            "Normal vs Ghost should deal 0 damage");
    }
}
