namespace PokemonIrcBot.Models;

public record BattleRound(
    string AttackerPokemon,
    string DefenderPokemon,
    string MoveName,
    int Damage,
    bool Crit,
    int AttackerHpAfter,
    int DefenderHpAfter,
    double TypeMultiplier = 1.0);
