namespace PokemonIrcBot.Models;

public record Pokemon(
    int Id,
    string Name,
    int Hp,
    int Attack,
    int Defense,
    int Speed,
    IReadOnlyList<string> MoveNames)
{
    public IReadOnlyList<string> Types { get; init; } = [];
}
