using NUnit.Framework;
using PokemonIrcBot.Models;
using PokemonIrcBot.Services;

namespace PokemonIrcBot.Tests;

[TestFixture]
public class IrcServiceFormatPokeTagTests
{
    private static PokemonStats MakeStats(string name, int wins, int losses, int draws = 0) =>
        new() { Name = name, Wins = wins, Losses = losses, Draws = draws, Battles = wins + losses + draws };

    private static IReadOnlyList<PokemonStats> MakeStandings(params PokemonStats[] entries) =>
        entries.ToList();

    [Test]
    public void FormatPokeTag_NotInStandings_ReturnsEmpty()
    {
        var standings = MakeStandings(MakeStats("bulbasaur", 5, 1));
        Assert.That(IrcService.FormatPokeTag("pikachu", standings), Is.EqualTo(""));
    }

    [Test]
    public void FormatPokeTag_Top10WithLosses_ReturnsRankOnly()
    {
        var standings = MakeStandings(
            MakeStats("charizard", 10, 2),
            MakeStats("pikachu", 8, 1),
            MakeStats("bulbasaur", 5, 3));

        Assert.That(IrcService.FormatPokeTag("pikachu", standings), Is.EqualTo(" [#2]"));
    }

    [Test]
    public void FormatPokeTag_Rank1_ReturnsHashOne()
    {
        var standings = MakeStandings(MakeStats("charizard", 10, 2));
        Assert.That(IrcService.FormatPokeTag("charizard", standings), Is.EqualTo(" [#1]"));
    }

    [Test]
    public void FormatPokeTag_Rank10Boundary_ReturnsTag()
    {
        var standings = MakeStandings(
            MakeStats("p1", 20, 1),
            MakeStats("p2", 18, 1),
            MakeStats("p3", 16, 1),
            MakeStats("p4", 14, 1),
            MakeStats("p5", 12, 1),
            MakeStats("p6", 10, 1),
            MakeStats("p7", 8, 1),
            MakeStats("p8", 6, 1),
            MakeStats("p9", 4, 1),
            MakeStats("p10", 2, 1));

        Assert.That(IrcService.FormatPokeTag("p10", standings), Is.EqualTo(" [#10]"));
    }

    [Test]
    public void FormatPokeTag_Rank11_ReturnsEmpty()
    {
        var standings = MakeStandings(
            MakeStats("p1", 20, 1),
            MakeStats("p2", 18, 1),
            MakeStats("p3", 16, 1),
            MakeStats("p4", 14, 1),
            MakeStats("p5", 12, 1),
            MakeStats("p6", 10, 1),
            MakeStats("p7", 8, 1),
            MakeStats("p8", 6, 1),
            MakeStats("p9", 4, 1),
            MakeStats("p10", 2, 1),
            MakeStats("p11", 1, 1));

        Assert.That(IrcService.FormatPokeTag("p11", standings), Is.EqualTo(""));
    }

    [Test]
    public void FormatPokeTag_UndefeatedNotTop10_ReturnsUndefeatedOnly()
    {
        var top10 = Enumerable.Range(1, 10)
            .Select(i => MakeStats($"p{i}", 20 - i, 1))
            .ToArray();
        var standings = MakeStandings([.. top10, MakeStats("newbie", 2, 0)]);

        Assert.That(IrcService.FormatPokeTag("newbie", standings), Is.EqualTo(" [UNDEFEATED]"));
    }

    [Test]
    public void FormatPokeTag_Top10AndUndefeated_ReturnsBothTags()
    {
        var standings = MakeStandings(
            MakeStats("charizard", 10, 0),
            MakeStats("pikachu", 8, 1));

        Assert.That(IrcService.FormatPokeTag("charizard", standings), Is.EqualTo(" [#1 UNDEFEATED]"));
    }

    [Test]
    public void FormatPokeTag_ZeroBattles_ReturnsEmpty()
    {
        // A Pokemon with 0 battles is not "undefeated" in any meaningful sense
        var standings = MakeStandings(new PokemonStats { Name = "mewtwo", Battles = 0, Wins = 0, Losses = 0 });
        Assert.That(IrcService.FormatPokeTag("mewtwo", standings), Is.EqualTo(""));
    }

    [Test]
    public void FormatPokeTag_NameMatchIsCaseInsensitive()
    {
        var standings = MakeStandings(MakeStats("Charizard", 10, 2));
        Assert.That(IrcService.FormatPokeTag("charizard", standings), Is.EqualTo(" [#1]"));
    }
}
