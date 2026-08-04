using System.IO;
using System.Text.Json;

namespace TinyCinema;

public static class TvShowEpisodeCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static string CacheDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TinyCinema",
            "TvShowEpisodeCache");

    public static IReadOnlyList<TvEpisodeEntry>? TryLoad(int showId)
    {
        if (!IsCachingEnabled() || showId <= 0)
            return null;

        var path = GetCacheFilePath(showId);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            var cached = JsonSerializer.Deserialize<TvShowEpisodeCacheFile>(json, JsonOptions);
            if (cached?.Episodes == null || cached.Episodes.Count == 0)
                return null;

            return cached.Episodes
                .Where(entry => entry.Season > 0 && entry.Episode > 0 && !string.IsNullOrWhiteSpace(entry.MovieLairUrl))
                .Select(entry => new TvEpisodeEntry
                {
                    Season = entry.Season,
                    Episode = entry.Episode,
                    Title = entry.Title?.Trim() ?? string.Empty,
                    ThumbnailUrl = entry.ThumbnailUrl?.Trim() ?? string.Empty,
                    MovieLairUrl = entry.MovieLairUrl.Trim()
                })
                .OrderBy(entry => entry.Season)
                .ThenBy(entry => entry.Episode)
                .ToList();
        }
        catch
        {
            return null;
        }
    }

    public static void Save(int showId, string showUrl, IReadOnlyList<TvEpisodeEntry> episodes)
    {
        if (!IsCachingEnabled() || showId <= 0 || episodes.Count == 0)
            return;

        try
        {
            Directory.CreateDirectory(CacheDirectory);

            var file = new TvShowEpisodeCacheFile
            {
                ShowId = showId,
                ShowUrl = showUrl.Trim(),
                CachedAtUtc = DateTime.UtcNow,
                Episodes = episodes
                    .Select(entry => new TvShowEpisodeCacheEntry
                    {
                        Season = entry.Season,
                        Episode = entry.Episode,
                        Title = entry.Title,
                        ThumbnailUrl = entry.ThumbnailUrl,
                        MovieLairUrl = entry.MovieLairUrl
                    })
                    .ToList()
            };

            var path = GetCacheFilePath(showId);
            var json = JsonSerializer.Serialize(file, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Cache write failures should not break playback.
        }
    }

    private static bool IsCachingEnabled() => SettingsWindow.GetIsTvShowCachingEnabled();

    private static string GetCacheFilePath(int showId) =>
        Path.Combine(CacheDirectory, $"{showId}.json");

    private sealed class TvShowEpisodeCacheFile
    {
        public int ShowId { get; set; }

        public string ShowUrl { get; set; } = string.Empty;

        public DateTime CachedAtUtc { get; set; }

        public List<TvShowEpisodeCacheEntry> Episodes { get; set; } = [];
    }

    private sealed class TvShowEpisodeCacheEntry
    {
        public int Season { get; set; }

        public int Episode { get; set; }

        public string Title { get; set; } = string.Empty;

        public string ThumbnailUrl { get; set; } = string.Empty;

        public string MovieLairUrl { get; set; } = string.Empty;
    }
}
