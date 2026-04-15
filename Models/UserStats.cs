namespace PokemonIrcBot.Models;

public class UserStats
{
    public string Nick { get; set; } = string.Empty;
    public int Battles { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public int CurrentStreak { get; set; }
    public int BestStreak { get; set; }
}
