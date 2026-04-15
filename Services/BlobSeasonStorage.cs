using System.Text.Json;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PokemonIrcBot.Configuration;
using PokemonIrcBot.Models;

namespace PokemonIrcBot.Services;

public class BlobSeasonStorage : ISeasonStorage, IHostedService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly StorageOptions _storageOptions;
    private readonly ILogger<BlobSeasonStorage> _logger;
    private BlobContainerClient _container = null!;

    public BlobSeasonStorage(StorageOptions storageOptions, ILogger<BlobSeasonStorage> logger)
    {
        _storageOptions = storageOptions;
        _logger = logger;
    }

    public async Task InitialiseAsync(CancellationToken ct = default)
    {
        BlobServiceClient client = !string.IsNullOrEmpty(_storageOptions.BlobEndpoint)
            ? new BlobServiceClient(new Uri(_storageOptions.BlobEndpoint), new DefaultAzureCredential())
            : new BlobServiceClient(_storageOptions.ConnectionString);

        _container = client.GetBlobContainerClient(_storageOptions.ContainerName);
        await _container.CreateIfNotExistsAsync(cancellationToken: ct);
        _logger.LogInformation("Blob container '{Container}' ready", _storageOptions.ContainerName);
    }

    public async Task<SeasonStats?> LoadAsync(string seasonId, CancellationToken ct = default)
    {
        var blob = _container.GetBlobClient(BlobPath(seasonId));

        if (!await blob.ExistsAsync(ct))
        {
            _logger.LogInformation("No existing stats found for season {SeasonId}", seasonId);
            return null;
        }

        var response = await blob.DownloadContentAsync(ct);
        var stats = JsonSerializer.Deserialize<SeasonStats>(response.Value.Content.ToStream(), JsonOptions);
        _logger.LogInformation("Loaded stats for season {SeasonId}", seasonId);
        return stats;
    }

    public async Task SaveAsync(SeasonStats stats, CancellationToken ct = default)
    {
        var blob = _container.GetBlobClient(BlobPath(stats.SeasonId));
        var json = JsonSerializer.SerializeToUtf8Bytes(stats, JsonOptions);
        await blob.UploadAsync(BinaryData.FromBytes(json), overwrite: true, ct);
        _logger.LogDebug("Saved stats for season {SeasonId}", stats.SeasonId);
    }

    private static string BlobPath(string seasonId) => $"seasons/{seasonId}/stats.json";

    // IHostedService — runs InitialiseAsync during app startup, after Kestrel is listening
    public async Task StartAsync(CancellationToken ct) => await InitialiseAsync(ct);
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
