namespace TinyCinema;

public class MovieCatalogEntry
{
    public string Year { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;

    public string Slug => TinyZoneHtmlParser.ExtractMovieSlug(Url);

    public string ToFileLine()
    {
        return $"{Year} | {Title} | {Url} | {ImageUrl} | {Genre} | {Duration} | {Country}";
    }

    public static MovieCatalogEntry? FromFileLine(string line)
    {
        var parts = line.Split('|').Select(p => p.Trim()).ToArray();
        if (parts.Length < 7)
            return null;

        return new MovieCatalogEntry
        {
            Year = parts[0],
            Title = parts[1],
            Url = parts[2],
            ImageUrl = parts[3],
            Genre = parts[4],
            Duration = parts[5],
            Country = parts[6]
        };
    }
}
