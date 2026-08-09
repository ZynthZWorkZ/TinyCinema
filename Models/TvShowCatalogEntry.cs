using System.Text.RegularExpressions;

namespace TinyCinema;

public class TvShowCatalogEntry
{
    public int TmdbId { get; set; }
    public string Year { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? DescriptionFetchedAt { get; set; }

    public string ShowId
    {
        get
        {
            if (TmdbId > 0)
                return TmdbId.ToString();

            if (string.IsNullOrWhiteSpace(Url))
                return string.Empty;

            var match = Regex.Match(Url, @"/watch-tv/(\d+)/?", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }
    }

    public string ToFileLine()
    {
        return $"{Year} | {Title} | {Url} | {ImageUrl} | {Genre} | {Duration} | {Country}";
    }

    public static TvShowCatalogEntry? FromFileLine(string line)
    {
        var parts = line.Split('|').Select(p => p.Trim()).ToArray();
        if (parts.Length < 7)
            return null;

        var entry = new TvShowCatalogEntry
        {
            Year = parts[0],
            Title = parts[1],
            Url = parts[2],
            ImageUrl = parts[3],
            Genre = parts[4],
            Duration = parts[5],
            Country = parts[6]
        };

        if (int.TryParse(entry.ShowId, out var tmdbId))
            entry.TmdbId = tmdbId;

        return entry;
    }

    public TvShowCatalogRecord ToRecord() => TvShowCatalogRecord.FromEntry(this);

    public static TvShowCatalogEntry FromRecord(TvShowCatalogRecord record) => record.ToEntry();
}
