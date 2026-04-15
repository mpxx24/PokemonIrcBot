using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using PokemonIrcBot;
using PokemonIrcBot.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Load Key Vault secrets via Managed Identity (KeyVaultUri is set by Bicep as an app setting)
var kvUri = builder.Configuration["KeyVaultUri"];
if (!string.IsNullOrEmpty(kvUri))
    builder.Configuration.AddAzureKeyVault(new Uri(kvUri), new DefaultAzureCredential(), new AzureKeyVaultConfigurationOptions());

builder.Services.Configure<IrcOptions>(builder.Configuration.GetSection("Irc"));
builder.Services.Configure<SeasonOptions>(builder.Configuration.GetSection("Season"));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));

// Pull SASL password from Key Vault secret "IrcSaslPassword" into IrcOptions
builder.Services.PostConfigure<IrcOptions>(options =>
    options.SaslPassword = builder.Configuration["IrcSaslPassword"] ?? string.Empty);

// Connection string is auto-discovered from APPLICATIONINSIGHTS_CONNECTION_STRING env var (set by Bicep)
builder.Services.AddApplicationInsightsTelemetryWorkerService();
builder.Services.AddHealthChecks();
builder.Services.AddPokemonIrcBotServices();

var app = builder.Build();

// Required by Azure App Service warmup probe
app.MapHealthChecks("/health");

await app.RunAsync();
