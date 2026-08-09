using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace TinyCinema;

public sealed class ManualCatalogAddResult
{
    public bool Added { get; init; }
    public bool AlreadyExists { get; init; }
    public string Title { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
}

public static class ManualCatalogAdder
{
    private static readonly HttpClient Http = CreateHttpClient();

    public static async Task<ManualCatalogAddResult> AddMovieAsync(
        string url,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var normalizedUrl = NormalizeMovieUrl(url);
        if (string.IsNullOrWhiteSpace(normalizedUrl) || !Regex.IsMatch(normalizedUrl, @"/movie/", RegexOptions.IgnoreCase))
        {
            throw new ArgumentException("Enter a valid TinyZone movie URL (must contain /movie/).");
        }

        var slug = TinyZoneHtmlParser.ExtractMovieSlug(normalizedUrl);
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Could not read the movie slug from that URL.");

        if (MovieCatalogStore.MovieExists(outputPath, slug))
        {
            return new ManualCatalogAddResult
            {
                AlreadyExists = true,
                OutputPath = outputPath
            };
        }

        var html = await DownloadStringAsync(normalizedUrl, cancellationToken);
        var entry = new MovieCatalogEntry { Url = normalizedUrl };
        TinyZoneHtmlParser.EnrichFromDetailPage(entry, html);

        if (string.IsNullOrWhiteSpace(entry.Title))
            throw new InvalidOperationException("Could not read movie details from that page.");

        entry.Genre = string.IsNullOrWhiteSpace(entry.Genre) ? "Unknown" : entry.Genre;
        entry.Country = string.IsNullOrWhiteSpace(entry.Country) ? "Unknown" : entry.Country;
        entry.Duration = string.IsNullOrWhiteSpace(entry.Duration) ? "Unknown" : entry.Duration;
        entry.Year = string.IsNullOrWhiteSpace(entry.Year) ? "Unknown" : entry.Year;
        entry.ImageUrl ??= string.Empty;

        var added = await MovieCatalogStore.AddMovieAsync(outputPath, entry.ToRecord(), cancellationToken);
        if (!added)
        {
            return new ManualCatalogAddResult
            {
                AlreadyExists = true,
                OutputPath = outputPath
            };
        }

        return new ManualCatalogAddResult
        {
            Added = true,
            Title = entry.Title,
            OutputPath = outputPath
        };
    }

    public static async Task<ManualCatalogAddResult> AddTvShowAsync(
        string url,
        string outputPath,
        string? tmdbApiKey,
        CancellationToken cancellationToken = default)
    {
        var showId = MovieLairTvDetailsParser.ExtractShowId(url);
        if (showId is not > 0)
            throw new ArgumentException("Enter a valid MovieLair TV show URL (must contain /watch-tv/{id}).");

        if (TvShowCatalogStore.ShowExists(outputPath, showId.Value))
        {
            return new ManualCatalogAddResult
            {
                AlreadyExists = true,
                OutputPath = outputPath
            };
        }

        var normalizedUrl = MovieLairTmdbClient.BuildWatchUrl(showId.Value);
        TvShowCatalogEntry? entry = null;

        if (!string.IsNullOrWhiteSpace(tmdbApiKey))
        {
            var details = await MovieLairTmdbClient.GetTvDetailsAsync(showId.Value, tmdbApiKey, cancellationToken);
            if (details != null)
            {
                entry = new TvShowCatalogEntry
                {
                    TmdbId = showId.Value,
                    Url = normalizedUrl,
                    Title = details.Name.Trim(),
                    ImageUrl = string.IsNullOrWhiteSpace(details.PosterPath)
                        ? string.Empty
                        : $"https://image.tmdb.org/t/p/w500{details.PosterPath}"
                };
                MovieLairTmdbClient.EnrichFromDetails(entry, details);
            }
        }

        if (entry == null || string.IsNullOrWhiteSpace(entry.Title))
        {
            var html = await DownloadStringAsync(normalizedUrl, cancellationToken);
            entry = MovieLairTvDetailsParser.ParseCatalogEntry(html, normalizedUrl)
                ?? throw new InvalidOperationException("Could not read TV show details from that page.");
        }

        entry.Year = string.IsNullOrWhiteSpace(entry.Year) ? "Unknown" : entry.Year;
        entry.Genre = string.IsNullOrWhiteSpace(entry.Genre) ? "Unknown" : entry.Genre;
        entry.Duration = string.IsNullOrWhiteSpace(entry.Duration) ? "Unknown" : entry.Duration;
        entry.Country = string.IsNullOrWhiteSpace(entry.Country) ? "Unknown" : entry.Country;
        entry.ImageUrl ??= string.Empty;
        entry.Url = normalizedUrl;
        entry.TmdbId = showId.Value;

        var added = await TvShowCatalogStore.AddShowAsync(outputPath, entry.ToRecord(), cancellationToken);
        if (!added)
        {
            return new ManualCatalogAddResult
            {
                AlreadyExists = true,
                OutputPath = outputPath
            };
        }

        return new ManualCatalogAddResult
        {
            Added = true,
            Title = entry.Title,
            OutputPath = outputPath
        };
    }

    private static string NormalizeMovieUrl(string url)
    {
        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
            return trimmed;

        return absolute.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/";
    }

    private static async Task<string> DownloadStringAsync(string url, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            uri.Host.Contains("movielair", StringComparison.OrdinalIgnoreCase))
        {
            Http.DefaultRequestHeaders.Remove("Cookie");
            var cookieHandler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                AutomaticDecompression = DecompressionMethods.All
            };
            cookieHandler.CookieContainer.Add(uri, new Cookie("srv", "2", "/", uri.Host));

            using var movielairClient = new HttpClient(cookieHandler) { Timeout = TimeSpan.FromSeconds(30) };
            movielairClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:147.0) Gecko/20100101 Firefox/147.0");
            movielairClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            movielairClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");

            using var response = await movielairClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var defaultResponse = await Http.SendAsync(request, cancellationToken);
        defaultResponse.EnsureSuccessStatusCode();
        return await defaultResponse.Content.ReadAsStringAsync(cancellationToken);
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            AutomaticDecompression = DecompressionMethods.All
        };

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:147.0) Gecko/20100101 Firefox/147.0");
        client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");

        return client;
    }
}
