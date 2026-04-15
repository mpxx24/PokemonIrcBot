using PokemonIrcBot.Models;

namespace PokemonIrcBot.Services;

public interface IBattleService
{
    Task<BattleResult> FightAsync(string challenger, string target, CancellationToken ct = default);
    int CalcDamage(Pokemon attacker, Pokemon defender, int movePower, bool crit);
}
