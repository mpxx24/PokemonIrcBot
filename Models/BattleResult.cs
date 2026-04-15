namespace PokemonIrcBot.Models;

public record BattleResult(
    string Challenger,
    string Target,
    string ChallengerPokemon,
    string TargetPokemon,
    string? Winner,
    string? Loser,
    bool IsDraw,
    IReadOnlyList<BattleRound> Rounds)
{
    public IReadOnlyList<string> ChallengerTypes { get; init; } = [];
    public IReadOnlyList<string> TargetTypes { get; init; } = [];
}
