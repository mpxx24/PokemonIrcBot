using PokemonIrcBot.Models;

namespace PokemonIrcBot.Services;

public interface IStatsService
{
    Task LoadAsync(CancellationToken ct = default);
    Task RecordResultAsync(BattleResult result, CancellationToken ct = default);
    UserStats? GetUserStats(string nick);
    IReadOnlyList<UserStats> GetAllStats(int minBattles = 0);
    PokemonStats? GetPokemonStats(string name);
    IReadOnlyList<PokemonStats> GetAllPokemonStats();
}
