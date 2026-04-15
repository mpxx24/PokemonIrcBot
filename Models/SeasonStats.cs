namespace PokemonIrcBot.Models;

public class SeasonStats
{
    public string SeasonId { get; set; } = string.Empty;
    public string SeasonName { get; set; } = string.Empty;
    public List<int> Generations { get; set; } = [];
    public DateTime StartedAt { get; set; }
    public Dictionary<string, UserStats> Users { get; set; } = [];
}
