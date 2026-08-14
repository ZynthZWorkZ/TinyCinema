using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyCinema;

public static class MovieCatalogStore
{
    public static async Task<MovieCatalogFile> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            return new MovieCatalogFile();

        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ParseDocument(document);
    }

    public static async Task<List<Movie>> LoadMoviesAsync(string path, CancellationToken cancellationToken = default)
    {
        var catalog = await LoadAsync(path, cancellationToken);
        return catalog.Movies
            .Where(record => record.IsLoadable())
            .Select(record => record.ToMovie())
            .ToList();
    }

    public static HashSet<string> LoadExistingSlugs(string path)
    {
        if (!File.Exists(path))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            var catalog = ParseDocument(document);
            return catalog.Movies
                .Select(movie => movie.Slug)
                .Where(slug => !string.IsNullOrWhiteSpace(slug))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static bool MovieExists(string path, string slug)
    {
        if (string.IsNullOrWhiteSpace(slug) || !File.Exists(path))
            return false;

        return LoadExistingSlugs(path).Contains(slug);
    }

    public static async Task SaveAsync(string path, MovieCatalogFile catalog, CancellationToken cancellationToken = default)
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

    public static async Task<int> MergeMoviesAsync(
        string path,
        IReadOnlyList<MovieCatalogRecord> newMovies,
        MovieCatalogSaveMode saveMode,
        CancellationToken cancellationToken = default)
    {
        if (newMovies.Count == 0)
            return 0;

        var existingCatalog = await LoadAsync(path, cancellationToken);
        var existingBySlug = existingCatalog.Movies
            .Where(movie => !string.IsNullOrWhiteSpace(movie.Slug))
            .ToDictionary(movie => movie.Slug, StringComparer.OrdinalIgnoreCase);

        var existingTmdbIds = existingCatalog.Movies
            .Where(movie => movie.TmdbId is > 0)
            .Select(movie => movie.TmdbId!.Value)
            .ToHashSet();

        List<MovieCatalogRecord> merged;
        var added = 0;

        if (saveMode == MovieCatalogSaveMode.Overwrite)
        {
            merged = newMovies
                .Where(movie => !string.IsNullOrWhiteSpace(movie.Slug))
                .Select(incoming =>
                {
                    if (existingBySlug.TryGetValue(incoming.Slug, out var existing))
                        return MergeRecords(existing, incoming);
                    return incoming;
                })
                .ToList();

            added = merged.Count;
        }
        else
        {
            merged = new List<MovieCatalogRecord>(existingCatalog.Movies);

            foreach (var incoming in newMovies)
            {
                if (string.IsNullOrWhiteSpace(incoming.Slug))
                    continue;

                if (existingBySlug.ContainsKey(incoming.Slug))
                    continue;

                if (incoming.TmdbId is > 0 && existingTmdbIds.Contains(incoming.TmdbId.Value))
                    continue;

                merged.Insert(0, incoming);
                existingBySlug[incoming.Slug] = incoming;
                if (incoming.TmdbId is > 0)
                    existingTmdbIds.Add(incoming.TmdbId.Value);
                added++;
            }
        }

        var catalog = new MovieCatalogFile { Movies = merged };
        await SaveAsync(path, catalog, cancellationToken);
        return added;
    }

    public static async Task<bool> AddMovieAsync(
        string path,
        MovieCatalogRecord record,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(record.Slug))
            return false;

        if (MovieExists(path, record.Slug))
            return false;

        var catalog = await LoadAsync(path, cancellationToken);
        catalog.Movies.Insert(0, record);
        await SaveAsync(path, catalog, cancellationToken);
        return true;
    }

    public static async Task MigrateLegacyFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            return;

        var catalog = await LoadAsync(path, cancellationToken);
        await SaveAsync(path, catalog, cancellationToken);
    }

    private static MovieCatalogFile ParseDocument(JsonDocument document)
    {
        var root = document.RootElement;

        if (root.TryGetProperty("movies", out _) || root.TryGetProperty("Movies", out _))
            return JsonSerializer.Deserialize<MovieCatalogFile>(root.GetRawText(), JsonOptions.Default)
                ?? new MovieCatalogFile();

        if (root.TryGetProperty("entries", out var entries) || root.TryGetProperty("Entries", out entries))
        {
            var legacyEntries = JsonSerializer.Deserialize<List<LegacyMovieCatalogEntry>>(entries.GetRawText(), JsonOptions.Default)
                ?? [];

            return new MovieCatalogFile
            {
                LastUpdated = ReadLegacyTimestamp(root),
                Movies = legacyEntries.Select(entry => entry.ToRecord()).ToList()
            };
        }

        return new MovieCatalogFile();
    }

    private static DateTime ReadLegacyTimestamp(JsonElement root)
    {
        if (root.TryGetProperty("lastUpdatedUtc", out var lastUpdatedUtc) &&
            lastUpdatedUtc.TryGetDateTime(out var utc))
            return utc;

        if (root.TryGetProperty("LastUpdatedUtc", out lastUpdatedUtc) &&
            lastUpdatedUtc.TryGetDateTime(out utc))
            return utc;

        return DateTime.UtcNow;
    }

    private static void PreserveDescription(MovieCatalogRecord existing, MovieCatalogRecord incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming.Description) && !string.IsNullOrWhiteSpace(existing.Description))
        {
            incoming.Description = existing.Description;
            incoming.DescriptionFetchedAt = existing.DescriptionFetchedAt;
        }
    }

    private static void PreserveDirectorCast(MovieCatalogRecord existing, MovieCatalogRecord incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming.Director) && !string.IsNullOrWhiteSpace(existing.Director))
            incoming.Director = existing.Director;

        if (incoming.Cast.Count == 0 && existing.Cast.Count > 0)
            incoming.Cast = existing.Cast.ToList();

        incoming.DirectorCastFetchedAt ??= existing.DirectorCastFetchedAt;
    }

    private static MovieCatalogRecord MergeRecords(MovieCatalogRecord existing, MovieCatalogRecord incoming)
    {
        PreserveDescription(existing, incoming);
        PreserveDirectorCast(existing, incoming);

        return new MovieCatalogRecord
        {
            Title = string.IsNullOrWhiteSpace(incoming.Title) ? existing.Title : incoming.Title,
            Year = string.IsNullOrWhiteSpace(incoming.Year) ? existing.Year : incoming.Year,
            Url = string.IsNullOrWhiteSpace(incoming.Url) ? existing.Url : incoming.Url,
            Poster = string.IsNullOrWhiteSpace(incoming.Poster) ? existing.Poster : incoming.Poster,
            Genre = string.IsNullOrWhiteSpace(incoming.Genre) ? existing.Genre : incoming.Genre,
            Duration = string.IsNullOrWhiteSpace(incoming.Duration) ? existing.Duration : incoming.Duration,
            Country = string.IsNullOrWhiteSpace(incoming.Country) ? existing.Country : incoming.Country,
            Description = incoming.Description,
            DescriptionFetchedAt = incoming.DescriptionFetchedAt ?? existing.DescriptionFetchedAt,
            Director = incoming.Director,
            Cast = incoming.Cast.ToList(),
            DirectorCastFetchedAt = incoming.DirectorCastFetchedAt,
            StoredSlug = string.IsNullOrWhiteSpace(incoming.StoredSlug) ? existing.StoredSlug : incoming.StoredSlug,
            PlaybackSource = incoming.PlaybackSource ?? existing.PlaybackSource,
            TmdbId = incoming.TmdbId ?? existing.TmdbId
        };
    }

    private sealed class LegacyMovieCatalogEntry
    {
        public string Url { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Year { get; set; } = string.Empty;

        public string Genre { get; set; } = string.Empty;

        public string Duration { get; set; } = string.Empty;

        public string Country { get; set; } = string.Empty;

        public string ImageUrl { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public DateTime? FetchedAtUtc { get; set; }

        public MovieCatalogRecord ToRecord() => new()
        {
            Title = Title,
            Year = Year,
            Url = Url,
            Poster = ImageUrl,
            Genre = Genre,
            Duration = Duration,
            Country = Country,
            Description = Description,
            DescriptionFetchedAt = FetchedAtUtc
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
