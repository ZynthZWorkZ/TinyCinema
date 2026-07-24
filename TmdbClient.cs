using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyCinema;

public static class TmdbClient
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("https://api.themoviedb.org/3/"),
        Timeout = TimeSpan.FromSeconds(20)
    };

    private static readonly Dictionary<string, string?> TrailerCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string?> OpeningCreditsCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<TmdbSimilarItem>> SimilarMoviesCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<TmdbSimilarItem>> SimilarTvCache = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<IReadOnlyList<TmdbSimilarItem>> GetSimilarMovieItemsAsync(
        string title,
        string year,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(apiKey))
            return [];

        var movieId = await SearchMovieIdAsync(title, year, apiKey, cancellationToken);
        if (movieId == null)
            return [];

        return await GetSimilarMovieItemsByIdAsync(movieId.Value, apiKey, cancellationToken);
    }

    public static async Task<IReadOnlyList<TmdbSimilarItem>> GetSimilarTvItemsAsync(
        string title,
        string year,
        int? tmdbId,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return [];

        var showId = tmdbId is > 0 ? tmdbId : await SearchTvIdAsync(title, year, apiKey, cancellationToken);
        if (showId == null)
            return [];

        return await GetSimilarTvItemsByIdAsync(showId.Value, apiKey, cancellationToken);
    }

    private static async Task<IReadOnlyList<TmdbSimilarItem>> GetSimilarMovieItemsByIdAsync(
        int movieId,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"movie|{movieId}";
        if (SimilarMoviesCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var url = $"movie/{movieId}/similar?api_key={Uri.EscapeDataString(apiKey)}&language=en-US&page=1";
        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<TmdbSearchResponse>(stream, JsonOptions, cancellationToken);
        var items = payload?.Results?
            .Select(result => new TmdbSimilarItem
            {
                Title = result.Title,
                Year = ExtractYear(result.ReleaseDate)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .ToList() ?? [];

        SimilarMoviesCache[cacheKey] = items;
        return items;
    }

    private static async Task<IReadOnlyList<TmdbSimilarItem>> GetSimilarTvItemsByIdAsync(
        int tvId,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"tv|{tvId}";
        if (SimilarTvCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var url = $"tv/{tvId}/similar?api_key={Uri.EscapeDataString(apiKey)}&language=en-US&page=1";
        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<TmdbTvSearchResponse>(stream, JsonOptions, cancellationToken);
        var items = payload?.Results?
            .Select(result => new TmdbSimilarItem
            {
                Title = result.Name,
                Year = ExtractYear(result.FirstAirDate)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .ToList() ?? [];

        SimilarTvCache[cacheKey] = items;
        return items;
    }

    private static string? ExtractYear(string? dateValue)
    {
        if (string.IsNullOrWhiteSpace(dateValue) || dateValue.Length < 4)
            return null;

        return dateValue[..4];
    }

    public static async Task<string?> GetTrailerVideoKeyAsync(
        string title,
        string year,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(apiKey))
            return null;

        var cacheKey = $"v2|{title.Trim()}|{year?.Trim()}";
        if (TrailerCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var movieId = await SearchMovieIdAsync(title, year, apiKey, cancellationToken);
        if (movieId == null)
        {
            TrailerCache[cacheKey] = null;
            return null;
        }

        var videoKey = await GetBestTrailerVideoKeyAsync(movieId.Value, apiKey, cancellationToken);
        TrailerCache[cacheKey] = videoKey;
        return videoKey;
    }

    public static async Task<string?> GetTvOpeningCreditsVideoKeyAsync(
        int? tmdbId,
        string title,
        string year,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        var cacheKey = $"v1|{tmdbId}|{title.Trim()}|{year?.Trim()}";
        if (OpeningCreditsCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var showId = tmdbId;
        if (showId is not > 0)
            showId = await SearchTvIdAsync(title, year, apiKey, cancellationToken);

        if (showId == null)
        {
            OpeningCreditsCache[cacheKey] = null;
            return null;
        }

        var videoKey = await GetBestOpeningCreditsVideoKeyAsync(showId.Value, apiKey, cancellationToken);
        OpeningCreditsCache[cacheKey] = videoKey;
        return videoKey;
    }

    private static async Task<int?> SearchTvIdAsync(
        string title,
        string year,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString(title.Trim());
        var url = $"search/tv?api_key={Uri.EscapeDataString(apiKey)}&query={query}&include_adult=false";
        if (int.TryParse(year, out var yearValue))
            url += $"&first_air_date_year={yearValue}";

        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var search = await JsonSerializer.DeserializeAsync<TmdbTvSearchResponse>(stream, JsonOptions, cancellationToken);
        if (search?.Results == null || search.Results.Count == 0)
            return null;

        return PickBestTvId(search.Results, title, year);
    }

    private static int? PickBestTvId(List<TmdbTvSearchResult> results, string title, string year)
    {
        if (results.Count == 1)
            return results[0].Id;

        var normalizedTitle = Normalize(title);
        var yearPrefix = int.TryParse(year, out var yearValue) ? yearValue.ToString() : null;

        var ranked = results
            .Select(result => new
            {
                Result = result,
                Score = ScoreTvSearchResult(result, normalizedTitle, yearPrefix)
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Result.Popularity)
            .ToList();

        return ranked[0].Score > 0 ? ranked[0].Result.Id : results[0].Id;
    }

    private static int ScoreTvSearchResult(TmdbTvSearchResult result, string normalizedTitle, string? yearPrefix)
    {
        var score = 0;
        if (Normalize(result.Name) == normalizedTitle)
            score += 100;

        if (!string.IsNullOrEmpty(result.OriginalName) &&
            Normalize(result.OriginalName) == normalizedTitle)
            score += 80;

        if (!string.IsNullOrEmpty(yearPrefix) &&
            result.FirstAirDate?.StartsWith(yearPrefix, StringComparison.Ordinal) == true)
            score += 40;

        return score;
    }

    private static async Task<string?> GetBestOpeningCreditsVideoKeyAsync(
        int tvId,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var url = $"tv/{tvId}/videos?api_key={Uri.EscapeDataString(apiKey)}";
        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var videos = await JsonSerializer.DeserializeAsync<TmdbVideosResponse>(stream, JsonOptions, cancellationToken);
        if (videos?.Results == null || videos.Results.Count == 0)
            return null;

        var youtubeVideos = videos.Results
            .Where(v => string.Equals(v.Site, "YouTube", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var openingCredits = youtubeVideos
            .Where(v => string.Equals(v.Type, "Opening Credits", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.Official)
            .ThenByDescending(v => v.PublishedAt)
            .FirstOrDefault()
            ?? youtubeVideos
                .Where(v => v.Name.Contains("opening credits", StringComparison.OrdinalIgnoreCase) ||
                            v.Name.Contains("title sequence", StringComparison.OrdinalIgnoreCase) ||
                            v.Name.Contains("intro", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(v => v.Official)
                .ThenByDescending(v => v.PublishedAt)
                .FirstOrDefault();

        return openingCredits == null || string.IsNullOrWhiteSpace(openingCredits.Key)
            ? null
            : openingCredits.Key;
    }

    private static async Task<int?> SearchMovieIdAsync(
        string title,
        string year,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString(title.Trim());
        var url = $"search/movie?api_key={Uri.EscapeDataString(apiKey)}&query={query}&include_adult=false";
        if (int.TryParse(year, out var yearValue))
            url += $"&year={yearValue}";

        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var search = await JsonSerializer.DeserializeAsync<TmdbSearchResponse>(stream, JsonOptions, cancellationToken);
        if (search?.Results == null || search.Results.Count == 0)
            return null;

        return PickBestMovieId(search.Results, title, year);
    }

    private static int? PickBestMovieId(List<TmdbSearchResult> results, string title, string year)
    {
        if (results.Count == 1)
            return results[0].Id;

        var normalizedTitle = Normalize(title);
        var yearPrefix = int.TryParse(year, out var yearValue) ? yearValue.ToString() : null;

        var ranked = results
            .Select(result => new
            {
                Result = result,
                Score = ScoreSearchResult(result, normalizedTitle, yearPrefix)
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Result.Popularity)
            .ToList();

        return ranked[0].Score > 0 ? ranked[0].Result.Id : results[0].Id;
    }

    private static int ScoreSearchResult(TmdbSearchResult result, string normalizedTitle, string? yearPrefix)
    {
        var score = 0;
        if (Normalize(result.Title) == normalizedTitle)
            score += 100;

        if (!string.IsNullOrEmpty(result.OriginalTitle) &&
            Normalize(result.OriginalTitle) == normalizedTitle)
            score += 80;

        if (!string.IsNullOrEmpty(yearPrefix) &&
            result.ReleaseDate?.StartsWith(yearPrefix, StringComparison.Ordinal) == true)
            score += 40;

        return score;
    }

    private static async Task<string?> GetBestTrailerVideoKeyAsync(
        int movieId,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var url = $"movie/{movieId}/videos?api_key={Uri.EscapeDataString(apiKey)}";
        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var videos = await JsonSerializer.DeserializeAsync<TmdbVideosResponse>(stream, JsonOptions, cancellationToken);
        if (videos?.Results == null || videos.Results.Count == 0)
            return null;

        var youtubeVideos = videos.Results
            .Where(v => string.Equals(v.Site, "YouTube", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var trailer = youtubeVideos
            .Where(v => string.Equals(v.Type, "Trailer", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.Official)
            .ThenByDescending(v => v.PublishedAt)
            .FirstOrDefault()
            ?? youtubeVideos
                .Where(v => string.Equals(v.Type, "Teaser", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(v => v.Official)
                .ThenByDescending(v => v.PublishedAt)
                .FirstOrDefault()
            ?? youtubeVideos
                .OrderByDescending(v => v.Official)
                .ThenByDescending(v => v.PublishedAt)
                .FirstOrDefault();

        return trailer == null || string.IsNullOrWhiteSpace(trailer.Key)
            ? null
            : trailer.Key;
    }

    private static string Normalize(string value)
    {
        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class TmdbSearchResponse
    {
        [JsonPropertyName("results")]
        public List<TmdbSearchResult> Results { get; set; } = [];
    }

    private sealed class TmdbSearchResult
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("original_title")]
        public string? OriginalTitle { get; set; }

        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; set; }

        [JsonPropertyName("popularity")]
        public double Popularity { get; set; }
    }

    private sealed class TmdbTvSearchResponse
    {
        [JsonPropertyName("results")]
        public List<TmdbTvSearchResult> Results { get; set; } = [];
    }

    private sealed class TmdbTvSearchResult
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("original_name")]
        public string? OriginalName { get; set; }

        [JsonPropertyName("first_air_date")]
        public string? FirstAirDate { get; set; }

        [JsonPropertyName("popularity")]
        public double Popularity { get; set; }
    }

    private sealed class TmdbVideosResponse
    {
        [JsonPropertyName("results")]
        public List<TmdbVideo> Results { get; set; } = [];
    }

    private sealed class TmdbVideo
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = "";

        [JsonPropertyName("site")]
        public string Site { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("official")]
        public bool Official { get; set; }

        [JsonPropertyName("published_at")]
        public DateTime? PublishedAt { get; set; }
    }
}
