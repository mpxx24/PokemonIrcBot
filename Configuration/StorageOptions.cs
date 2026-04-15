namespace PokemonIrcBot.Configuration;

public class StorageOptions
{
    /// <summary>Used in production — Managed Identity authenticates via DefaultAzureCredential.</summary>
    public string BlobEndpoint { get; set; } = string.Empty;

    /// <summary>Used in local development (Azurite or a real storage account with key access).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    public string ContainerName { get; set; } = "pokemon-bot";
}
