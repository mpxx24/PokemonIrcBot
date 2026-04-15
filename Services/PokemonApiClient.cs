using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PokemonIrcBot.Models;

namespace PokemonIrcBot.Services;

public class PokemonApiClient : IPokemonApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _http;
    private readonly ILogger<PokemonApiClient> _logger;
    private readonly Dictionary<int, Pokemon> _cache = [];

    public PokemonApiClient(HttpClient http, ILogger<PokemonApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<Pokemon> GetPokemonAsync(int id, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(id, out var cached))
            return cached;

        _logger.LogDebug("Fetching Pokemon #{Id} from PokeAPI", id);

        var response = await _http.GetAsync($"pokemon/{id}", ct);
        response.EnsureSuccessStatusCode();

        var dto = await JsonSerializer.DeserializeAsync<PokemonDto>(
            await response.Content.ReadAsStreamAsync(ct), JsonOptions, ct)
            ?? throw new InvalidOperationException($"PokeAPI returned null for Pokemon #{id}");

        var pokemon = MapToPokemon(dto);
        _cache[id] = pokemon;
        return pokemon;
    }

    private static Pokemon MapToPokemon(PokemonDto dto)
    {
        int Stat(string name) =>
            dto.Stats.FirstOrDefault(s => s.Stat.Name == name)?.BaseStat ?? 50;

        var moveNames = dto.Moves
            .Select(m => m.Move.Name)
            .Take(10)
            .ToList();

        var types = dto.Types
            .OrderBy(t => t.Slot)
            .Select(t => t.Type.Name)
            .ToList();

        return new Pokemon(
            Id: dto.Id,
            Name: dto.Name,
            Hp: Stat("hp"),
            Attack: Stat("attack"),
            Defense: Stat("defense"),
            Speed: Stat("speed"),
            MoveNames: moveNames)
        {
            Types = types,
        };
    }

    // PokeAPI DTOs — private, only used for deserialization
    private sealed record PokemonDto(
        int Id,
        string Name,
        List<StatEntryDto> Stats,
        List<MoveEntryDto> Moves,
        List<TypeEntryDto> Types);

    private sealed record StatEntryDto(
        [property: JsonPropertyName("base_stat")] int BaseStat,
        StatDto Stat);

    private sealed record StatDto(string Name);

    private sealed record MoveEntryDto(MoveDto Move);

    private sealed record MoveDto(string Name);

    private sealed record TypeEntryDto(int Slot, TypeDto Type);

    private sealed record TypeDto(string Name);
}
