using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyCinema;

public static class WhatsOnNetflixStore
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string GetCatalogPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TinyCinema",
        "WhatsOn",
        "netflix-movies.json");

    public static bool IsStale(WhatsOnNetflixCatalog? catalog) =>
        catalog == null || DateTime.UtcNow - catalog.FetchedAt.ToUniversalTime() >= RefreshInterval;

    public static async Task<WhatsOnNetflixCatalog?> TryLoadAsync(CancellationToken cancellationToken = default)
    {
        var path = GetCatalogPath();
        if (!File.Exists(path))
            return null;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<WhatsOnNetflixCatalog>(stream, JsonOptions, cancellationToken);
    }

    public static async Task SaveAsync(WhatsOnNetflixCatalog catalog, CancellationToken cancellationToken = default)
    {
        var path = GetCatalogPath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        catalog.FetchedAt = catalog.FetchedAt == default ? DateTime.UtcNow : catalog.FetchedAt.ToUniversalTime();

        var json = JsonSerializer.Serialize(catalog, JsonOptions);
        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, json, cancellationToken);

        try
        {
            if (File.Exists(path))
                File.Replace(tempPath, path, destinationBackupFileName: null);
            else
                File.Move(tempPath, path);
        }
        catch (IOException)
        {
            await File.WriteAllTextAsync(path, json, cancellationToken);
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
