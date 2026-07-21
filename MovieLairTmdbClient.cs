using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TinyCinema;

public static class MovieLairTmdbClient
{
    private const string MovieLairBaseUrl = "https://movielair.cc";
    private const string TmdbImageBaseUrl = "https://image.tmdb.org/t/p/w500";

    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("https://api.themoviedb.org/3/"),
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static Dictionary<int, string>? _genreNames;

    public static int? ExtractGenreId(string categoryUrl)
    {
        if (string.IsNullOrWhiteSpace(categoryUrl))
            return null;

        var match = Regex.Match(categoryUrl, @"/shows/(\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var genreId)
            ? genreId
            : null;
    }

    public static string BuildWatchUrl(int tmdbId) => $"{MovieLairBaseUrl}/watch-tv/{tmdbId}/";

    public static async Task<TmdbDiscoverTvPage> DiscoverTvAsync(
        int genreId,
        int page,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        var url =
            $"discover/tv?api_key={Uri.EscapeDataString(apiKey)}&with_genres={genreId}&page={page}&sort_by=popularity.desc";

        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var pageResult = await JsonSerializer.DeserializeAsync<TmdbDiscoverTvPage>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("TMDB returned an empty discover response.");

        return pageResult;
    }

    public static async Task<TmdbTvDetails?> GetTvDetailsAsync(
        int tmdbId,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        var url = $"tv/{tmdbId}?api_key={Uri.EscapeDataString(apiKey)}";

        using var response = await Http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<TmdbTvDetails>(stream, JsonOptions, cancellationToken);
    }

    public static async Task<IReadOnlyDictionary<int, string>> GetTvGenreNamesAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (_genreNames != null)
            return _genreNames;

        var url = $"genre/tv/list?api_key={Uri.EscapeDataString(apiKey)}";

        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var genreList = await JsonSerializer.DeserializeAsync<TmdbGenreListResponse>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("TMDB returned an empty genre list.");

        _genreNames = genreList.Genres
            .Where(genre => genre.Id > 0 && !string.IsNullOrWhiteSpace(genre.Name))
            .ToDictionary(genre => genre.Id, genre => genre.Name, comparer: EqualityComparer<int>.Default);

        return _genreNames;
    }

    public static TvShowCatalogEntry MapDiscoverResult(
        TmdbDiscoverTvResult result,
        IReadOnlyDictionary<int, string> genreNames)
    {
        var year = string.Empty;
        if (!string.IsNullOrWhiteSpace(result.FirstAirDate) && result.FirstAirDate.Length >= 4)
            year = result.FirstAirDate[..4];

        var genres = result.GenreIds
            .Where(genreNames.ContainsKey)
            .Select(id => genreNames[id])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var country = result.OriginCountry?.FirstOrDefault() ?? string.Empty;

        return new TvShowCatalogEntry
        {
            TmdbId = result.Id,
            Year = year,
            Title = result.Name.Trim(),
            Url = BuildWatchUrl(result.Id),
            ImageUrl = string.IsNullOrWhiteSpace(result.PosterPath)
                ? string.Empty
                : $"{TmdbImageBaseUrl}{result.PosterPath}",
            Genre = genres.Count > 0 ? string.Join(", ", genres) : "Unknown",
            Country = string.IsNullOrWhiteSpace(country) ? "Unknown" : country
        };
    }

    public static void EnrichFromDetails(TvShowCatalogEntry entry, TmdbTvDetails details)
    {
        if (!string.IsNullOrWhiteSpace(details.Name))
            entry.Title = details.Name.Trim();

        if (!string.IsNullOrWhiteSpace(details.FirstAirDate) && details.FirstAirDate.Length >= 4)
            entry.Year = details.FirstAirDate[..4];

        if (details.Genres.Count > 0)
        {
            entry.Genre = string.Join(", ", details.Genres
                .Select(genre => genre.Name.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase));
        }

        if (details.NumberOfSeasons > 0 || details.NumberOfEpisodes > 0)
        {
            entry.Duration = details.NumberOfSeasons > 0 && details.NumberOfEpisodes > 0
                ? $"{details.NumberOfSeasons} seasons, {details.NumberOfEpisodes} episodes"
                : details.NumberOfSeasons > 0
                    ? $"{details.NumberOfSeasons} seasons"
                    : $"{details.NumberOfEpisodes} episodes";
        }

        var country = details.OriginCountry?.FirstOrDefault()
            ?? details.ProductionCountries?.FirstOrDefault()?.Name;
        if (!string.IsNullOrWhiteSpace(country))
            entry.Country = country;

        if (string.IsNullOrWhiteSpace(entry.ImageUrl) && !string.IsNullOrWhiteSpace(details.PosterPath))
            entry.ImageUrl = $"{TmdbImageBaseUrl}{details.PosterPath}";
    }

    public sealed class TmdbDiscoverTvPage
    {
        [JsonPropertyName("page")]
        public int Page { get; set; }

        [JsonPropertyName("total_pages")]
        public int TotalPages { get; set; }

        [JsonPropertyName("results")]
        public List<TmdbDiscoverTvResult> Results { get; set; } = [];
    }

    public sealed class TmdbDiscoverTvResult
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("first_air_date")]
        public string? FirstAirDate { get; set; }

        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; set; }

        [JsonPropertyName("genre_ids")]
        public List<int> GenreIds { get; set; } = [];

        [JsonPropertyName("origin_country")]
        public List<string>? OriginCountry { get; set; }
    }

    public sealed class TmdbTvDetails
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("first_air_date")]
        public string? FirstAirDate { get; set; }

        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; set; }

        [JsonPropertyName("number_of_seasons")]
        public int NumberOfSeasons { get; set; }

        [JsonPropertyName("number_of_episodes")]
        public int NumberOfEpisodes { get; set; }

        [JsonPropertyName("origin_country")]
        public List<string>? OriginCountry { get; set; }

        [JsonPropertyName("production_countries")]
        public List<TmdbProductionCountry>? ProductionCountries { get; set; }

        [JsonPropertyName("genres")]
        public List<TmdbNamedGenre> Genres { get; set; } = [];

        [JsonPropertyName("overview")]
        public string Overview { get; set; } = string.Empty;

        [JsonPropertyName("tagline")]
        public string? Tagline { get; set; }

        [JsonPropertyName("vote_average")]
        public double VoteAverage { get; set; }
    }

    private sealed class TmdbGenreListResponse
    {
        [JsonPropertyName("genres")]
        public List<TmdbNamedGenre> Genres { get; set; } = [];
    }

    public sealed class TmdbNamedGenre
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    public sealed class TmdbProductionCountry
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}
