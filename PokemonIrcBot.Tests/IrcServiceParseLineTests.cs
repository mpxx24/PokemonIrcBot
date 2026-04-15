using NUnit.Framework;
using PokemonIrcBot.Services;

namespace PokemonIrcBot.Tests;

[TestFixture]
public class IrcServiceParseLineTests
{
    // CAP LS — single/final line (no asterisk third param)
    [Test]
    public void ParseLine_CapLsSingleLine_ParsedCorrectly()
    {
        var (prefix, command, parameters, trailing) =
            IrcService.ParseLine(":irc.libera.chat CAP * LS :sasl multi-prefix");

        Assert.That(command, Is.EqualTo("CAP"));
        Assert.That(parameters, Is.EqualTo(new[] { "*", "LS" }));
        Assert.That(trailing, Is.EqualTo("sasl multi-prefix"));
        Assert.That(prefix, Is.EqualTo("irc.libera.chat"));
    }

    // CAP LS — continuation line (asterisk as third param signals more lines follow)
    [Test]
    public void ParseLine_CapLsMultiLineContinuation_ParsedCorrectly()
    {
        var (_, command, parameters, trailing) =
            IrcService.ParseLine(":irc.libera.chat CAP * LS * :sasl");

        Assert.That(command, Is.EqualTo("CAP"));
        Assert.That(parameters, Is.EqualTo(new[] { "*", "LS", "*" }));
        Assert.That(trailing, Is.EqualTo("sasl"));
    }

    // Helper: the multi-line continuation marker is the third parameter being "*"
    [Test]
    public void ParseLine_CapLsMultiLine_ThirdParamIsAsterisk()
    {
        var (_, _, parameters, _) =
            IrcService.ParseLine(":irc.libera.chat CAP * LS * :cap1 cap2");

        Assert.That(parameters.ElementAtOrDefault(2), Is.EqualTo("*"));
    }

    // CAP LS — final multi-line line (no asterisk, just the trailing)
    [Test]
    public void ParseLine_CapLsFinalMultiLine_NoAsteriskInParams()
    {
        var (_, _, parameters, trailing) =
            IrcService.ParseLine(":irc.libera.chat CAP * LS :cap3 cap4");

        Assert.That(parameters.ElementAtOrDefault(2), Is.Null);
        Assert.That(trailing, Is.EqualTo("cap3 cap4"));
    }

    // CAP ACK :sasl
    [Test]
    public void ParseLine_CapAckSasl_ParsedCorrectly()
    {
        var (_, command, parameters, trailing) =
            IrcService.ParseLine(":irc.libera.chat CAP * ACK :sasl");

        Assert.That(command, Is.EqualTo("CAP"));
        Assert.That(parameters.ElementAtOrDefault(1), Is.EqualTo("ACK"));
        Assert.That(trailing, Is.EqualTo("sasl"));
    }

    // AUTHENTICATE + — server ready for credentials
    [Test]
    public void ParseLine_AuthenticatePlus_ParsedCorrectly()
    {
        var (_, command, parameters, trailing) =
            IrcService.ParseLine("AUTHENTICATE +");

        Assert.That(command, Is.EqualTo("AUTHENTICATE"));
        Assert.That(parameters.FirstOrDefault(), Is.EqualTo("+"));
        Assert.That(trailing, Is.Null);
    }

    // 903 — SASL success
    [Test]
    public void ParseLine_SaslSuccess903_ParsedCorrectly()
    {
        var (_, command, _, _) =
            IrcService.ParseLine(":irc.libera.chat 903 PokemonBot :SASL authentication successful");

        Assert.That(command, Is.EqualTo("903"));
    }

    // PING :server
    [Test]
    public void ParseLine_Ping_ParsedCorrectly()
    {
        var (prefix, command, parameters, trailing) =
            IrcService.ParseLine("PING :irc.libera.chat");

        Assert.That(command, Is.EqualTo("PING"));
        Assert.That(prefix, Is.Null);
        Assert.That(trailing, Is.EqualTo("irc.libera.chat"));
    }

    // NOTICE from NickServ confirming ghost
    [Test]
    public void ParseLine_NickServGhostNotice_ParsedCorrectly()
    {
        var (prefix, command, parameters, trailing) =
            IrcService.ParseLine(":NickServ!NickServ@services.libera.chat NOTICE PokemonBot_ :PokemonBot has been ghosted.");

        Assert.That(command, Is.EqualTo("NOTICE"));
        Assert.That(prefix, Is.EqualTo("NickServ!NickServ@services.libera.chat"));
        Assert.That(parameters.FirstOrDefault(), Is.EqualTo("PokemonBot_"));
        Assert.That(trailing, Does.Contain("ghosted"));
    }
}
