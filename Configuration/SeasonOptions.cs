namespace PokemonIrcBot.Configuration;

public class SeasonOptions
{
    public string Id { get; set; } = "season-1";
    public string Name { get; set; } = "Season 1";
    public List<int> Generations { get; set; } = [1];
}
