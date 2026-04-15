namespace PokemonIrcBot.Configuration;

public class IrcOptions
{
    public string Host { get; set; } = "irc.libera.chat";
    public int Port { get; set; } = 6697;
    public bool UseTls { get; set; } = true;
    public string Nick { get; set; } = "PokemonBot";
    public string RealName { get; set; } = "Pokemon Battle Bot";
    public string Channel { get; set; } = "#pokemon-battles";
    public int ReconnectDelaySeconds { get; set; } = 10;
    public int MaxReconnectDelaySeconds { get; set; } = 300;

    /// <summary>SASL PLAIN password — loaded from Key Vault at runtime. Empty = no SASL.</summary>
    public string SaslPassword { get; set; } = string.Empty;

    /// <summary>Seconds a user must wait after a battle ends before starting another. 0 = no cooldown.</summary>
    public int BattleCooldownSeconds { get; set; } = 60;
}
