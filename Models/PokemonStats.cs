namespace PokemonIrcBot.Models;

public class PokemonStats
{
    public string Name { get; set; } = string.Empty;
    public int Battles { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public int CurrentStreak { get; set; }
    public int BestStreak { get; set; }
}
