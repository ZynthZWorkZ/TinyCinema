namespace TinyCinema;

public sealed class WatchedEntry
{
    public string Url { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Year { get; set; } = string.Empty;

    public string ImageUrl { get; set; } = string.Empty;

    public string Genre { get; set; } = string.Empty;

    public string Duration { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public CatalogContentType ContentType { get; set; } = CatalogContentType.Movie;

    public DateTime WatchedAtUtc { get; set; } = DateTime.UtcNow;

    public static WatchedEntry FromMovie(Movie movie, DateTime watchedAtUtc) => new()
    {
        Url = movie.Url,
        Title = movie.Title,
        Year = movie.Year,
        ImageUrl = movie.ImageUrl,
        Genre = movie.Genre,
        Duration = movie.Duration,
        Country = movie.Country,
        ContentType = movie.ContentType,
        WatchedAtUtc = watchedAtUtc
    };

    public Movie ToMovie() => new()
    {
        Url = Url,
        Title = Title,
        Year = Year,
        ImageUrl = ImageUrl,
        Genre = Genre,
        Duration = Duration,
        Country = Country,
        ContentType = ContentType,
        IsWatched = true,
        WatchedAtUtc = WatchedAtUtc
    };
}
