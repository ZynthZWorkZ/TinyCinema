using System.IO;
using System.Text.Json;

namespace TinyCinema;

public enum InteractionEventType
{
    View,
    Play,
    Continue,
    Favorite
}

public sealed class UserInteractionEntry
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Year { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public CatalogContentType ContentType { get; set; }
    public InteractionEventType EventType { get; set; }
    public DateTime TimestampUtc { get; set; }
}

public static class UserInteractionTracker
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly string HistoryFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TinyCinema",
        "interaction_history.json");

    private static readonly Dictionary<InteractionEventType, int> EventWeights = new()
    {
        [InteractionEventType.View] = 1,
        [InteractionEventType.Play] = 5,
        [InteractionEventType.Continue] = 4,
        [InteractionEventType.Favorite] = 4
    };

    public static int GetEventWeight(InteractionEventType eventType) =>
        EventWeights.TryGetValue(eventType, out var weight) ? weight : 1;

    public static void Record(Movie movie, InteractionEventType eventType)
    {
        if (string.IsNullOrWhiteSpace(movie.Url))
            return;

        try
        {
            var entries = LoadAll();
            entries.Add(new UserInteractionEntry
            {
                Url = movie.Url.Trim(),
                Title = movie.Title.Trim(),
                Year = movie.Year.Trim(),
                Genre = movie.Genre.Trim(),
                Country = movie.Country.Trim(),
                ContentType = movie.ContentType,
                EventType = eventType,
                TimestampUtc = DateTime.UtcNow
            });

            WriteAll(entries);
        }
        catch
        {
            // Tracking failures should not break the app.
        }
    }

    public static IReadOnlyList<UserInteractionEntry> GetRecent(int maxEntries = 200)
    {
        return LoadAll()
            .OrderByDescending(entry => entry.TimestampUtc)
            .Take(maxEntries)
            .ToList();
    }

    public static int GetWeightedInteractionCount()
    {
        return LoadAll().Sum(entry => GetEventWeight(entry.EventType));
    }

    private static List<UserInteractionEntry> LoadAll()
    {
        if (!File.Exists(HistoryFile))
            return [];

        try
        {
            var json = File.ReadAllText(HistoryFile);
            return JsonSerializer.Deserialize<List<UserInteractionEntry>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void WriteAll(List<UserInteractionEntry> entries)
    {
        var directory = Path.GetDirectoryName(HistoryFile);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var trimmed = entries
            .OrderByDescending(entry => entry.TimestampUtc)
            .Take(1000)
            .ToList();

        File.WriteAllText(HistoryFile, JsonSerializer.Serialize(trimmed, JsonOptions));
    }
}
