using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PokemonIrcBot.Configuration;
using PokemonIrcBot.Services;

namespace PokemonIrcBot;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPokemonIrcBotServices(this IServiceCollection services)
    {
        // Resolve typed options as plain instances for injection
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<IrcOptions>>().Value);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<SeasonOptions>>().Value);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<StorageOptions>>().Value);

        // Storage — registered as hosted service so init runs after Kestrel is up
        services.AddSingleton<BlobSeasonStorage>();
        services.AddSingleton<ISeasonStorage>(sp => sp.GetRequiredService<BlobSeasonStorage>());
        services.AddHostedService(sp => sp.GetRequiredService<BlobSeasonStorage>());

        // PokeAPI typed HTTP client
        services.AddHttpClient<IPokemonApiClient, PokemonApiClient>(client =>
        {
            client.BaseAddress = new Uri("https://pokeapi.co/api/v2/");
            client.DefaultRequestHeaders.Add("User-Agent", "PokemonIrcBot/1.0");
        });

        // Services
        services.AddSingleton<IBattleService, BattleService>();
        services.AddSingleton<IStatsService, StatsService>();

        // Background service
        services.AddHostedService<IrcService>();

        return services;
    }
}
