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

    public string Description { get; set; } = string.Empty;

    public DateTime? DescriptionFetchedAt { get; set; }

    public string Director { get; set; } = string.Empty;

    public List<string> Cast { get; set; } = [];

    public DateTime? DirectorCastFetchedAt { get; set; }

    public string Slug => TinyZoneHtmlParser.ExtractMovieSlug(Url);

    public MovieCatalogRecord ToRecord() => MovieCatalogRecord.FromEntry(this);
}
