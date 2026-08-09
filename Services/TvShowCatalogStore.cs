using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyCinema;

public static class TvShowCatalogStore
{
    public static async Task<TvShowCatalogFile> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            return new TvShowCatalogFile();

        if (path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            return LoadLegacyTextFile(path);

        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ParseDocument(document);
    }

    public static async Task<List<Movie>> LoadShowsAsync(string path, CancellationToken cancellationToken = default)
    {
        var catalog = await LoadAsync(path, cancellationToken);
        return catalog.Shows
            .Where(record => !string.IsNullOrWhiteSpace(record.Url))
            .Select(record => record.ToMovie())
            .ToList();
    }

    public static HashSet<int> LoadExistingTmdbIds(string path)
    {
        if (!File.Exists(path))
            return [];

        try
        {
            if (path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                return LoadLegacyTextFile(path).Shows
                    .Select(show => show.TmdbId)
                    .Where(id => id > 0)
                    .ToHashSet();
            }

            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            var catalog = ParseDocument(document);
            return catalog.Shows
                .Select(show => show.TmdbId)
                .Where(id => id > 0)
                .ToHashSet();
        }
        catch
        {
            return [];
        }
    }

    public static bool ShowExists(string path, int tmdbId)
    {
        if (tmdbId <= 0 || !File.Exists(path))
            return false;

        return LoadExistingTmdbIds(path).Contains(tmdbId);
    }

    public static async Task SaveAsync(string path, TvShowCatalogFile catalog, CancellationToken cancellationToken = default)
    {
        catalog.LastUpdated = DateTime.UtcNow;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(catalog, JsonOptions.Default);
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

    public static async Task<int> MergeShowsAsync(
        string path,
        IReadOnlyList<TvShowCatalogRecord> newShows,
        MovieCatalogSaveMode saveMode,
        CancellationToken cancellationToken = default)
    {
        if (newShows.Count == 0)
            return 0;

        var existingCatalog = await LoadAsync(path, cancellationToken);
        var existingById = existingCatalog.Shows
            .Where(show => show.TmdbId > 0)
            .ToDictionary(show => show.TmdbId);

        List<TvShowCatalogRecord> merged;
        var added = 0;

        if (saveMode == MovieCatalogSaveMode.Overwrite)
        {
            merged = newShows
                .Where(show => show.TmdbId > 0)
                .Select(incoming =>
                {
                    if (existingById.TryGetValue(incoming.TmdbId, out var existing))
                        return MergeRecords(existing, incoming);
                    return incoming;
                })
                .ToList();

            added = merged.Count;
        }
        else
        {
            merged = new List<TvShowCatalogRecord>(existingCatalog.Shows);

            foreach (var incoming in newShows)
            {
                if (incoming.TmdbId <= 0)
                    continue;

                if (existingById.ContainsKey(incoming.TmdbId))
                    continue;

                merged.Insert(0, incoming);
                existingById[incoming.TmdbId] = incoming;
                added++;
            }
        }

        var catalog = new TvShowCatalogFile { Shows = merged };
        await SaveAsync(path, catalog, cancellationToken);
        return added;
    }

    public static async Task<bool> AddShowAsync(
        string path,
        TvShowCatalogRecord record,
        CancellationToken cancellationToken = default)
    {
        if (record.TmdbId <= 0)
            return false;

        if (ShowExists(path, record.TmdbId))
            return false;

        var catalog = await LoadAsync(path, cancellationToken);
        catalog.Shows.Insert(0, record);
        await SaveAsync(path, catalog, cancellationToken);
        return true;
    }

    private static TvShowCatalogFile ParseDocument(JsonDocument document)
    {
        var root = document.RootElement;

        if (root.TryGetProperty("shows", out _) || root.TryGetProperty("Shows", out _))
            return JsonSerializer.Deserialize<TvShowCatalogFile>(root.GetRawText(), JsonOptions.Default)
                ?? new TvShowCatalogFile();

        return new TvShowCatalogFile();
    }

    private static TvShowCatalogFile LoadLegacyTextFile(string path)
    {
        var shows = new List<TvShowCatalogRecord>();

        foreach (var line in File.ReadAllLines(path))
        {
            var entry = TvShowCatalogEntry.FromFileLine(line);
            if (entry == null)
                continue;

            shows.Add(TvShowCatalogRecord.FromEntry(entry));
        }

        return new TvShowCatalogFile { Shows = shows };
    }

    private static void PreserveDescription(TvShowCatalogRecord existing, TvShowCatalogRecord incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming.Description) && !string.IsNullOrWhiteSpace(existing.Description))
        {
            incoming.Description = existing.Description;
            incoming.DescriptionFetchedAt = existing.DescriptionFetchedAt;
        }
    }

    private static TvShowCatalogRecord MergeRecords(TvShowCatalogRecord existing, TvShowCatalogRecord incoming)
    {
        PreserveDescription(existing, incoming);

        return new TvShowCatalogRecord
        {
            Title = string.IsNullOrWhiteSpace(incoming.Title) ? existing.Title : incoming.Title,
            Year = string.IsNullOrWhiteSpace(incoming.Year) ? existing.Year : incoming.Year,
            Url = string.IsNullOrWhiteSpace(incoming.Url) ? existing.Url : incoming.Url,
            Poster = string.IsNullOrWhiteSpace(incoming.Poster) ? existing.Poster : incoming.Poster,
            Genre = string.IsNullOrWhiteSpace(incoming.Genre) ? existing.Genre : incoming.Genre,
            Duration = string.IsNullOrWhiteSpace(incoming.Duration) ? existing.Duration : incoming.Duration,
            Country = string.IsNullOrWhiteSpace(incoming.Country) ? existing.Country : incoming.Country,
            TmdbId = incoming.TmdbId > 0 ? incoming.TmdbId : existing.TmdbId,
            Description = incoming.Description,
            DescriptionFetchedAt = incoming.DescriptionFetchedAt ?? existing.DescriptionFetchedAt
        };
    }

    private static class JsonOptions
    {
        public static JsonSerializerOptions Default { get; } = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}
