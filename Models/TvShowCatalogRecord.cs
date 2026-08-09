namespace TinyCinema;

public sealed class TvShowCatalogRecord
{
    public string Title { get; set; } = string.Empty;

    public string Year { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Poster { get; set; } = string.Empty;

    public string Genre { get; set; } = string.Empty;

    public string Duration { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public int TmdbId { get; set; }

    public string Description { get; set; } = string.Empty;

    public DateTime? DescriptionFetchedAt { get; set; }

    public static TvShowCatalogRecord FromEntry(TvShowCatalogEntry entry) => new()
    {
        Title = entry.Title,
        Year = entry.Year,
        Url = entry.Url,
        Poster = entry.ImageUrl,
        Genre = entry.Genre,
        Duration = entry.Duration,
        Country = entry.Country,
        TmdbId = entry.TmdbId,
        Description = entry.Description,
        DescriptionFetchedAt = entry.DescriptionFetchedAt
    };

    public TvShowCatalogEntry ToEntry() => new()
    {
        Title = Title,
        Year = Year,
        Url = Url,
        ImageUrl = Poster,
        Genre = Genre,
        Duration = Duration,
        Country = Country,
        TmdbId = TmdbId,
        Description = Description,
        DescriptionFetchedAt = DescriptionFetchedAt
    };

    public Movie ToMovie() => new()
    {
        Title = Title,
        Year = Year,
        Url = Url,
        ImageUrl = Poster,
        Genre = Genre,
        Duration = Duration,
        Country = Country,
        Description = Description,
        ContentType = CatalogContentType.TvShow
    };
}
