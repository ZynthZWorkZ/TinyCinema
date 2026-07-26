namespace TinyCinema;

public sealed class MovieCatalogRecord
{
    public string Title { get; set; } = string.Empty;

    public string Year { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Poster { get; set; } = string.Empty;

    public string Genre { get; set; } = string.Empty;

    public string Duration { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTime? DescriptionFetchedAt { get; set; }

    public string Director { get; set; } = string.Empty;

    public List<string> Cast { get; set; } = [];

    public DateTime? DirectorCastFetchedAt { get; set; }

    public string Slug => TinyZoneHtmlParser.ExtractMovieSlug(Url);

    public static MovieCatalogRecord FromEntry(MovieCatalogEntry entry) => new()
    {
        Title = entry.Title,
        Year = entry.Year,
        Url = entry.Url,
        Poster = entry.ImageUrl,
        Genre = entry.Genre,
        Duration = entry.Duration,
        Country = entry.Country,
        Description = entry.Description,
        DescriptionFetchedAt = entry.DescriptionFetchedAt,
        Director = entry.Director,
        Cast = entry.Cast.ToList(),
        DirectorCastFetchedAt = entry.DirectorCastFetchedAt
    };

    public MovieCatalogEntry ToEntry() => new()
    {
        Title = Title,
        Year = Year,
        Url = Url,
        ImageUrl = Poster,
        Genre = Genre,
        Duration = Duration,
        Country = Country,
        Description = Description,
        DescriptionFetchedAt = DescriptionFetchedAt,
        Director = Director,
        Cast = Cast.ToList(),
        DirectorCastFetchedAt = DirectorCastFetchedAt
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
        Director = Director,
        Cast = Cast.ToList(),
        ContentType = CatalogContentType.Movie
    };

    public static MovieCatalogRecord FromMovie(Movie movie) => new()
    {
        Title = movie.Title,
        Year = movie.Year,
        Url = movie.Url,
        Poster = movie.ImageUrl,
        Genre = movie.Genre,
        Duration = movie.Duration,
        Country = movie.Country,
        Description = movie.Description,
        Director = movie.Director,
        Cast = movie.Cast.ToList()
    };
}
