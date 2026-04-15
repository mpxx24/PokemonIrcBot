using PokemonIrcBot.Models;

namespace PokemonIrcBot.Services;

public interface ISeasonStorage
{
    Task<SeasonStats?> LoadAsync(string seasonId, CancellationToken ct = default);
    Task SaveAsync(SeasonStats stats, CancellationToken ct = default);
}
