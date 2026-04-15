using PokemonIrcBot.Models;

namespace PokemonIrcBot.Services;

public interface IPokemonApiClient
{
    Task<Pokemon> GetPokemonAsync(int id, CancellationToken ct = default);
}
