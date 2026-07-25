using System.IO;
using System.Text.Json;

namespace TinyCinema;

public sealed class TvWatchHistoryEntry
{
    public string ShowUrl { get; set; } = string.Empty;
    public int ShowId { get; set; }
    public string ShowTitle { get; set; } = string.Empty;
    public int Season { get; set; }
    public int Episode { get; set; }
    public string EpisodeTitle { get; set; } = string.Empty;
    public DateTime WatchedAtUtc { get; set; }

    public string DisplayLabel => string.IsNullOrWhiteSpace(EpisodeTitle)
        ? $"S{Season} E{Episode}"
        : $"S{Season} E{Episode} · {EpisodeTitle}";
}

public static class TvShowWatchHistory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly string HistoryFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TinyCinema",
        "tv_watch_history.json");

    public static IReadOnlyList<TvWatchHistoryEntry> GetAllEntries() => LoadAll();

    public static TvWatchHistoryEntry? TryGet(string showUrl)
    {
        if (string.IsNullOrWhiteSpace(showUrl))
            return null;

        var entries = LoadAll();
        return entries
            .Where(entry => entry.ShowUrl.Equals(showUrl, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.WatchedAtUtc)
            .FirstOrDefault();
    }

    public static void Save(string showUrl, string showTitle, TvEpisodeEntry episode)
    {
        if (string.IsNullOrWhiteSpace(showUrl) || episode.Season <= 0 || episode.Episode <= 0)
            return;

        try
        {
            var entries = LoadAll();
            entries.RemoveAll(entry => entry.ShowUrl.Equals(showUrl, StringComparison.OrdinalIgnoreCase));

            entries.Add(new TvWatchHistoryEntry
            {
                ShowUrl = showUrl.Trim(),
                ShowId = MovieLairTvDetailsParser.ExtractShowId(showUrl) ?? 0,
                ShowTitle = showTitle.Trim(),
                Season = episode.Season,
                Episode = episode.Episode,
                EpisodeTitle = episode.Title?.Trim() ?? string.Empty,
                WatchedAtUtc = DateTime.UtcNow
            });

            WriteAll(entries);
        }
        catch
        {
            // History write failures should not break playback.
        }
    }

    private static List<TvWatchHistoryEntry> LoadAll()
    {
        if (!File.Exists(HistoryFile))
            return [];

        try
        {
            var json = File.ReadAllText(HistoryFile);
            return JsonSerializer.Deserialize<List<TvWatchHistoryEntry>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void WriteAll(List<TvWatchHistoryEntry> entries)
    {
        var directory = Path.GetDirectoryName(HistoryFile);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var sorted = entries
            .OrderByDescending(entry => entry.WatchedAtUtc)
            .Take(500)
            .ToList();

        File.WriteAllText(HistoryFile, JsonSerializer.Serialize(sorted, JsonOptions));
    }
}
