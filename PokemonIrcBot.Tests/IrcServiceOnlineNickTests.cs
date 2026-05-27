using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using PokemonIrcBot.Configuration;
using PokemonIrcBot.Services;

namespace PokemonIrcBot.Tests;

[TestFixture]
public class IrcServiceOnlineNickTests
{
    // ─── ParseNamesListNicks (static) ───────────────────────────────────────

    [Test]
    public void ParseNamesListNicks_PlainNicks_ReturnsAll()
    {
        var nicks = IrcService.ParseNamesListNicks("alice bob charlie");
        Assert.That(nicks, Is.EquivalentTo(new[] { "alice", "bob", "charlie" }));
    }

    [Test]
    public void ParseNamesListNicks_OpPrefixed_StripsAtSign()
    {
        var nicks = IrcService.ParseNamesListNicks("@alice @bob");
        Assert.That(nicks, Is.EquivalentTo(new[] { "alice", "bob" }));
    }

    [Test]
    public void ParseNamesListNicks_VoicePrefixed_StripsPlus()
    {
        var nicks = IrcService.ParseNamesListNicks("+alice +bob");
        Assert.That(nicks, Is.EquivalentTo(new[] { "alice", "bob" }));
    }

    [Test]
    public void ParseNamesListNicks_MixedPrefixes_StripsAll()
    {
        var nicks = IrcService.ParseNamesListNicks("@alice +bob %charlie &dave ~eve frank");
        Assert.That(nicks, Is.EquivalentTo(new[] { "alice", "bob", "charlie", "dave", "eve", "frank" }));
    }

    [Test]
    public void ParseNamesListNicks_EmptyString_ReturnsEmpty()
    {
        var nicks = IrcService.ParseNamesListNicks(string.Empty);
        Assert.That(nicks, Is.Empty);
    }

    // ─── Instance helpers: IsNickOnline / TrackNick* / ClearOnlineNicks ─────

    private static IrcService CreateSut() => new(
        new IrcOptions(),
        new Mock<IBattleService>().Object,
        new Mock<IStatsService>().Object,
        new TelemetryClient(TelemetryConfiguration.CreateDefault()),
        NullLogger<IrcService>.Instance);

    [Test]
    public void IsNickOnline_NickNotTracked_ReturnsFalse()
    {
        var sut = CreateSut();
        Assert.That(sut.IsNickOnline("alice"), Is.False);
    }

    [Test]
    public void IsNickOnline_AfterJoin_ReturnsTrue()
    {
        var sut = CreateSut();
        sut.TrackNickJoined("alice");
        Assert.That(sut.IsNickOnline("alice"), Is.True);
    }

    [Test]
    public void IsNickOnline_AfterJoinThenPart_ReturnsFalse()
    {
        var sut = CreateSut();
        sut.TrackNickJoined("alice");
        sut.TrackNickLeft("alice");
        Assert.That(sut.IsNickOnline("alice"), Is.False);
    }

    [Test]
    public void IsNickOnline_CaseInsensitive()
    {
        var sut = CreateSut();
        sut.TrackNickJoined("Alice");
        Assert.That(sut.IsNickOnline("alice"), Is.True);
        Assert.That(sut.IsNickOnline("ALICE"), Is.True);
    }

    [Test]
    public void IsNickOnline_AfterNickRename_OldNickGone_NewNickOnline()
    {
        var sut = CreateSut();
        sut.TrackNickJoined("alice");
        sut.TrackNickRenamed("alice", "alicia");
        Assert.That(sut.IsNickOnline("alice"), Is.False);
        Assert.That(sut.IsNickOnline("alicia"), Is.True);
    }

    [Test]
    public void IsNickOnline_AfterNamesListParsed_AllNicksOnline()
    {
        var sut = CreateSut();
        sut.TrackNamesListNicks("@alice +bob charlie");
        Assert.That(sut.IsNickOnline("alice"), Is.True);
        Assert.That(sut.IsNickOnline("bob"), Is.True);
        Assert.That(sut.IsNickOnline("charlie"), Is.True);
    }

    [Test]
    public void IsNickOnline_AfterClear_ReturnsFalse()
    {
        var sut = CreateSut();
        sut.TrackNickJoined("alice");
        sut.ClearOnlineNicks();
        Assert.That(sut.IsNickOnline("alice"), Is.False);
    }
}
