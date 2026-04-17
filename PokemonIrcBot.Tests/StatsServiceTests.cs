using Moq;
using NUnit.Framework;
using PokemonIrcBot.Configuration;
using PokemonIrcBot.Models;
using PokemonIrcBot.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace PokemonIrcBot.Tests;

[TestFixture]
public class StatsServiceTests
{
    private Mock<ISeasonStorage> _storageMock = null!;
    private StatsService _sut = null!;
    private SeasonOptions _season = null!;

    [SetUp]
    public async Task SetUp()
    {
        _storageMock = new Mock<ISeasonStorage>();
        _season = new SeasonOptions { Id = "season-1", Name = "Spring 2026", Generations = [1, 2] };

        _storageMock.Setup(s => s.LoadAsync("season-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeasonStats?)null);
        _storageMock.Setup(s => s.SaveAsync(It.IsAny<SeasonStats>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _sut = new StatsService(_storageMock.Object, _season, NullLogger<StatsService>.Instance);
        await _sut.LoadAsync();
    }

    [Test]
    public void GetUserStats_UnknownNick_ReturnsNull()
    {
        var result = _sut.GetUserStats("nobody");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RecordResult_Win_UpdatesWinnerStats()
    {
        var result = new BattleResult("alice", "bob", "bulbasaur", "charmander", "alice", "bob", false, []);

        await _sut.RecordResultAsync(result);

        var alice = _sut.GetUserStats("alice");
        Assert.That(alice, Is.Not.Null);
        Assert.That(alice!.Wins, Is.EqualTo(1));
        Assert.That(alice.Losses, Is.EqualTo(0));
        Assert.That(alice.Draws, Is.EqualTo(0));
        Assert.That(alice.Battles, Is.EqualTo(1));
    }

    [Test]
    public async Task RecordResult_Win_UpdatesLoserStats()
    {
        var result = new BattleResult("alice", "bob", "bulbasaur", "charmander", "alice", "bob", false, []);

        await _sut.RecordResultAsync(result);

        var bob = _sut.GetUserStats("bob");
        Assert.That(bob, Is.Not.Null);
        Assert.That(bob!.Losses, Is.EqualTo(1));
        Assert.That(bob.Wins, Is.EqualTo(0));
        Assert.That(bob.Battles, Is.EqualTo(1));
    }

    [Test]
    public async Task RecordResult_Draw_UpdatesDrawForBoth()
    {
        var result = new BattleResult("alice", "bob", "bulbasaur", "charmander", null, null, true, []);

        await _sut.RecordResultAsync(result);

        var alice = _sut.GetUserStats("alice");
        var bob = _sut.GetUserStats("bob");
        Assert.That(alice!.Draws, Is.EqualTo(1));
        Assert.That(alice.Wins, Is.EqualTo(0));
        Assert.That(bob!.Draws, Is.EqualTo(1));
        Assert.That(bob.Wins, Is.EqualTo(0));
    }

    [Test]
    public async Task RecordResult_MultipleBattles_AccumulatesCorrectly()
    {
        await _sut.RecordResultAsync(new BattleResult("alice", "bob", "p1", "p2", "alice", "bob", false, []));
        await _sut.RecordResultAsync(new BattleResult("alice", "bob", "p1", "p2", "bob", "alice", false, []));
        await _sut.RecordResultAsync(new BattleResult("alice", "bob", "p1", "p2", null, null, true, []));

        var alice = _sut.GetUserStats("alice");
        Assert.That(alice!.Battles, Is.EqualTo(3));
        Assert.That(alice.Wins, Is.EqualTo(1));
        Assert.That(alice.Losses, Is.EqualTo(1));
        Assert.That(alice.Draws, Is.EqualTo(1));
    }

    [Test]
    public async Task GetAllStats_ReturnsAllUsers()
    {
        await _sut.RecordResultAsync(new BattleResult("alice", "bob", "p1", "p2", "alice", "bob", false, []));
        await _sut.RecordResultAsync(new BattleResult("carol", "bob", "p1", "p2", "carol", "bob", false, []));

        var all = _sut.GetAllStats();

        Assert.That(all.Count, Is.EqualTo(3));
        Assert.That(all.Select(u => u.Nick), Does.Contain("alice").And.Contain("bob").And.Contain("carol"));
    }

    [Test]
    public async Task RecordResult_PersistsToStorage()
    {
        var result = new BattleResult("alice", "bob", "bulbasaur", "charmander", "alice", "bob", false, []);

        await _sut.RecordResultAsync(result);

        _storageMock.Verify(s => s.SaveAsync(It.IsAny<SeasonStats>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RecordResult_Win_IncrementsCurrentStreak()
    {
        await _sut.RecordResultAsync(new BattleResult("alice", "bob", "p1", "p2", "alice", "bob", false, []));

        var alice = _sut.GetUserStats("alice");
        Assert.That(alice!.CurrentStreak, Is.EqualTo(1));
        Assert.That(alice.BestStreak, Is.EqualTo(1));
    }

    [Test]
    public async Task RecordResult_ConsecutiveWins_AccumulatesStreak()
    {
        await _sut.RecordResultAsync(new BattleResult("alice", "bob", "p1", "p2", "alice", "bob", false, []));
        await _sut.RecordResultAsync(new BattleResult("alice", "carol", "p1", "p2", "alice", "carol", false, []));
        await _sut.RecordResultAsync(new BattleResult("alice", "dave", "p1", "p2", "alice", "dave", false, []));

        var alice = _sut.GetUserStats("alice");
        Assert.That(alice!.CurrentStreak, Is.EqualTo(3));
        Assert.That(alice.BestStreak, Is.EqualTo(3));
    }

    [Test]
    public async Task RecordResult_LossResetsCurrentStreak()
    {
        await _sut.RecordResultAsync(new BattleResult("alice", "bob", "p1", "p2", "alice", "bob", false, []));
        await _sut.RecordResultAsync(new BattleResult("alice", "bob", "p1", "p2", "alice", "bob", false, []));
        await _sut.RecordResultAsync(new BattleResult("alice", "bob", "p1", "p2", "bob", "alice", false, []));

        var alice = _sut.GetUserStats("alice");
        Assert.That(alice!.CurrentStreak, Is.EqualTo(0));
        Assert.That(alice.BestStreak, Is.EqualTo(2));
    }

    [Test]
    public async Task RecordResult_DrawResetsCurrentStreak()
    {
        await _sut.RecordResultAsync(new BattleResult("alice", "bob", "p1", "p2", "alice", "bob", false, []));
        await _sut.RecordResultAsync(new BattleResult("alice", "bob", "p1", "p2", null, null, true, []));

        var alice = _sut.GetUserStats("alice");
        Assert.That(alice!.CurrentStreak, Is.EqualTo(0));
        Assert.That(alice.BestStreak, Is.EqualTo(1));
    }

    [Test]
    public async Task RecordResult_BestStreak_NeverDecreasesAfterLoss()
    {
        await _sut.RecordResultAsync(new BattleResult("alice", "bob", "p1", "p2", "alice", "bob", false, []));
        await _sut.RecordResultAsync(new BattleResult("alice", "bob", "p1", "p2", "alice", "bob", false, []));
        await _sut.RecordResultAsync(new BattleResult("alice", "bob", "p1", "p2", "alice", "bob", false, []));
        // Now 3-streak; take a loss
        await _sut.RecordResultAsync(new BattleResult("alice", "bob", "p1", "p2", "bob", "alice", false, []));
        // New win — streak = 1, best should still be 3
        await _sut.RecordResultAsync(new BattleResult("alice", "bob", "p1", "p2", "alice", "bob", false, []));

        var alice = _sut.GetUserStats("alice");
        Assert.That(alice!.CurrentStreak, Is.EqualTo(1));
        Assert.That(alice.BestStreak, Is.EqualTo(3));
    }

    [Test]
    public async Task LoadAsync_ExistingStats_RestoresUsers()
    {
        var existingStats = new SeasonStats
        {
            SeasonId = "season-1",
            SeasonName = "Spring 2026",
            Generations = [1, 2],
            StartedAt = DateTime.UtcNow,
            Users = new Dictionary<string, UserStats>
            {
                ["alice"] = new UserStats { Nick = "alice", Battles = 5, Wins = 3, Losses = 1, Draws = 1 }
            }
        };

        _storageMock.Setup(s => s.LoadAsync("season-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingStats);

        var sut = new StatsService(_storageMock.Object, _season, NullLogger<StatsService>.Instance);
        await sut.LoadAsync();

        var alice = sut.GetUserStats("alice");
        Assert.That(alice, Is.Not.Null);
        Assert.That(alice!.Wins, Is.EqualTo(3));
        Assert.That(alice.Battles, Is.EqualTo(5));
    }

    // --- Pokemon stats tests ---

    [Test]
    public void GetPokemonStats_UnknownName_ReturnsNull()
    {
        var result = _sut.GetPokemonStats("missingno");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RecordResult_Win_UpdatesWinnerPokemonStats()
    {
        var result = new BattleResult("alice", "bob", "bulbasaur", "charmander", "alice", "bob", false, []);

        await _sut.RecordResultAsync(result);

        var bulba = _sut.GetPokemonStats("bulbasaur");
        Assert.That(bulba, Is.Not.Null);
        Assert.That(bulba!.Wins, Is.EqualTo(1));
        Assert.That(bulba.Losses, Is.EqualTo(0));
        Assert.That(bulba.Draws, Is.EqualTo(0));
        Assert.That(bulba.Battles, Is.EqualTo(1));
    }

    [Test]
    public async Task RecordResult_Win_UpdatesLoserPokemonStats()
    {
        var result = new BattleResult("alice", "bob", "bulbasaur", "charmander", "alice", "bob", false, []);

        await _sut.RecordResultAsync(result);

        var charm = _sut.GetPokemonStats("charmander");
        Assert.That(charm, Is.Not.Null);
        Assert.That(charm!.Losses, Is.EqualTo(1));
        Assert.That(charm.Wins, Is.EqualTo(0));
        Assert.That(charm.Battles, Is.EqualTo(1));
    }

    [Test]
    public async Task RecordResult_Draw_UpdatesDrawForBothPokemon()
    {
        var result = new BattleResult("alice", "bob", "bulbasaur", "charmander", null, null, true, []);

        await _sut.RecordResultAsync(result);

        var bulba = _sut.GetPokemonStats("bulbasaur");
        var charm = _sut.GetPokemonStats("charmander");
        Assert.That(bulba!.Draws, Is.EqualTo(1));
        Assert.That(bulba.Wins, Is.EqualTo(0));
        Assert.That(charm!.Draws, Is.EqualTo(1));
        Assert.That(charm.Wins, Is.EqualTo(0));
    }

    [Test]
    public async Task RecordResult_Win_IncrementsWinnerPokemonStreak()
    {
        await _sut.RecordResultAsync(new BattleResult("alice", "bob", "bulbasaur", "charmander", "alice", "bob", false, []));

        var bulba = _sut.GetPokemonStats("bulbasaur");
        Assert.That(bulba!.CurrentStreak, Is.EqualTo(1));
        Assert.That(bulba.BestStreak, Is.EqualTo(1));
    }

    [Test]
    public async Task RecordResult_ConsecutivePokemonWins_AccumulatesStreak()
    {
        await _sut.RecordResultAsync(new BattleResult("alice", "bob", "bulbasaur", "charmander", "alice", "bob", false, []));
        await _sut.RecordResultAsync(new BattleResult("alice", "carol", "bulbasaur", "squirtle", "alice", "carol", false, []));
        await _sut.RecordResultAsync(new BattleResult("alice", "dave", "bulbasaur", "pidgey", "alice", "dave", false, []));

        var bulba = _sut.GetPokemonStats("bulbasaur");
        Assert.That(bulba!.CurrentStreak, Is.EqualTo(3));
        Assert.That(bulba.BestStreak, Is.EqualTo(3));
    }

    [Test]
    public async Task RecordResult_PokemonLoss_ResetsStreak()
    {
        await _sut.RecordResultAsync(new BattleResult("alice", "bob", "bulbasaur", "charmander", "alice", "bob", false, []));
        await _sut.RecordResultAsync(new BattleResult("alice", "bob", "bulbasaur", "charmander", "alice", "bob", false, []));
        await _sut.RecordResultAsync(new BattleResult("alice", "bob", "bulbasaur", "charmander", "bob", "alice", false, []));

        var bulba = _sut.GetPokemonStats("bulbasaur");
        Assert.That(bulba!.CurrentStreak, Is.EqualTo(0));
        Assert.That(bulba.BestStreak, Is.EqualTo(2));
    }

    [Test]
    public async Task RecordResult_PokemonDraw_ResetsStreak()
    {
        await _sut.RecordResultAsync(new BattleResult("alice", "bob", "bulbasaur", "charmander", "alice", "bob", false, []));
        await _sut.RecordResultAsync(new BattleResult("alice", "bob", "bulbasaur", "charmander", null, null, true, []));

        var bulba = _sut.GetPokemonStats("bulbasaur");
        Assert.That(bulba!.CurrentStreak, Is.EqualTo(0));
        Assert.That(bulba.BestStreak, Is.EqualTo(1));
    }

    [Test]
    public async Task GetAllPokemonStats_ReturnsAllPokemon()
    {
        await _sut.RecordResultAsync(new BattleResult("alice", "bob", "bulbasaur", "charmander", "alice", "bob", false, []));
        await _sut.RecordResultAsync(new BattleResult("carol", "bob", "squirtle", "charmander", "carol", "bob", false, []));

        var all = _sut.GetAllPokemonStats();

        Assert.That(all.Count, Is.EqualTo(3));
        Assert.That(all.Select(p => p.Name), Does.Contain("bulbasaur").And.Contain("charmander").And.Contain("squirtle"));
    }
}
