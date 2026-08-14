using System.Text.Json.Serialization;

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

    [JsonPropertyName("slug")]
    public string? StoredSlug { get; set; }

    [JsonPropertyName("playbackSource")]
    public string? PlaybackSource { get; set; }

    [JsonPropertyName("tmdbId")]
    public int? TmdbId { get; set; }

    [JsonIgnore]
    public string Slug
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(StoredSlug))
                return StoredSlug;

            var fromUrl = TinyZoneHtmlParser.ExtractMovieSlug(Url);
            if (!string.IsNullOrWhiteSpace(fromUrl))
                return fromUrl;

            if (TmdbId is > 0)
                return $"tmdb-{TmdbId}";

            return string.Empty;
        }
    }

    public MoviePlayerSource GetPlaybackSource() =>
        MoviePlayerSourceExtensions.ParseDisplayName(PlaybackSource);

    public bool IsLoadable() =>
        !string.IsNullOrWhiteSpace(Url) ||
        TmdbId is > 0 ||
        GetPlaybackSource() is MoviePlayerSource.MovieLair or MoviePlayerSource.VidSrc;

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
        DirectorCastFetchedAt = entry.DirectorCastFetchedAt,
        StoredSlug = entry.StoredSlug,
        PlaybackSource = entry.PlaybackSource,
        TmdbId = entry.TmdbId
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
        DirectorCastFetchedAt = DirectorCastFetchedAt,
        StoredSlug = StoredSlug,
        PlaybackSource = PlaybackSource,
        TmdbId = TmdbId
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
        ContentType = CatalogContentType.Movie,
        CatalogPlaybackSource = PlaybackSource,
        TmdbId = TmdbId,
        CatalogSlug = StoredSlug
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
        Cast = movie.Cast.ToList(),
        PlaybackSource = movie.CatalogPlaybackSource,
        TmdbId = movie.TmdbId,
        StoredSlug = movie.CatalogSlug
    };
}
