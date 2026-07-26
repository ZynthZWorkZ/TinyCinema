using System.IO;
using System.Text.Json;

namespace TinyCinema;

public static class WatchedStore
{
    private static readonly string WatchedFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TinyCinema",
        "watched.json");

    private static Dictionary<string, WatchedEntry>? _cache;

    public static bool IsWatched(string url)
    {
        EnsureLoaded();
        return _cache!.ContainsKey(url);
    }

    public static IReadOnlyList<WatchedEntry> GetAllEntries()
    {
        EnsureLoaded();
        return _cache!.Values
            .OrderByDescending(entry => entry.WatchedAtUtc)
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static (int Total, int Movies, int TvShows) GetStats()
    {
        EnsureLoaded();
        var entries = _cache!.Values;
        return (
            entries.Count,
            entries.Count(entry => entry.ContentType == CatalogContentType.Movie),
            entries.Count(entry => entry.ContentType == CatalogContentType.TvShow));
    }

    public static void SetWatched(Movie movie, bool isWatched)
    {
        EnsureLoaded();

        if (isWatched)
        {
            var watchedAt = _cache!.TryGetValue(movie.Url, out var existing)
                ? existing.WatchedAtUtc
                : DateTime.UtcNow;

            var entry = WatchedEntry.FromMovie(movie, watchedAt);
            _cache[movie.Url] = entry;
            movie.IsWatched = true;
            movie.WatchedAtUtc = watchedAt;
        }
        else
        {
            _cache!.Remove(movie.Url);
            movie.IsWatched = false;
            movie.WatchedAtUtc = null;
        }

        Save();
    }

    public static void ApplyToMovies(IEnumerable<Movie> movies)
    {
        EnsureLoaded();
        foreach (var movie in movies)
        {
            if (_cache!.TryGetValue(movie.Url, out var entry))
            {
                movie.IsWatched = true;
                movie.WatchedAtUtc = entry.WatchedAtUtc;
            }
            else
            {
                movie.IsWatched = false;
                movie.WatchedAtUtc = null;
            }
        }
    }

    public static Movie ResolveMovie(WatchedEntry entry, IReadOnlyDictionary<string, Movie> catalogByUrl)
    {
        if (catalogByUrl.TryGetValue(entry.Url, out var catalogMovie))
        {
            catalogMovie.IsWatched = true;
            catalogMovie.WatchedAtUtc = entry.WatchedAtUtc;
            return catalogMovie;
        }

        return entry.ToMovie();
    }

    private static void EnsureLoaded()
    {
        if (_cache != null)
            return;

        _cache = new Dictionary<string, WatchedEntry>(StringComparer.Ordinal);
        if (!File.Exists(WatchedFile))
            return;

        try
        {
            var json = File.ReadAllText(WatchedFile);
            var entries = JsonSerializer.Deserialize<List<WatchedEntry>>(json);
            if (entries == null)
                return;

            foreach (var entry in entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Url)))
                _cache[entry.Url] = entry;
        }
        catch
        {
            _cache = new Dictionary<string, WatchedEntry>(StringComparer.Ordinal);
        }
    }

    private static void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(WatchedFile);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var entries = _cache!.Values
                .OrderByDescending(entry => entry.WatchedAtUtc)
                .ToList();
            var json = JsonSerializer.Serialize(entries);
            File.WriteAllText(WatchedFile, json);
        }
        catch
        {
            // Ignore persistence errors.
        }
    }
}
