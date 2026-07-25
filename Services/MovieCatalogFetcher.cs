using System.IO;
using System.Net;
using System.Net.Http;
using Serilog;

namespace TinyCinema;

public class MovieCatalogFetchProgress
{
    public int CurrentPage { get; init; }
    public int TotalPages { get; init; }
    public int MoviesFound { get; init; }
    public int MoviesEnriched { get; init; }
    public string Status { get; init; } = string.Empty;
}

public enum MovieCatalogSaveMode
{
    MergeWithExisting,
    Overwrite
}

public class MovieCatalogFetchResult
{
    public int PagesFetched { get; init; }
    public int MoviesDiscovered { get; init; }
    public int MoviesAdded { get; init; }
    public int MoviesSkipped { get; init; }
    public int DetailFailures { get; init; }
    public string OutputPath { get; init; } = string.Empty;
    public string BaseUrlUsed { get; init; } = string.Empty;
    public MovieCatalogSaveMode SaveMode { get; init; }
}

public class MovieCatalogFetcher
{
    private const int MaxPages = 747;
    private const int RequestDelayMs = 600;
    private const int MaxRetries = 1;

    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;

    public MovieCatalogFetcher()
    {
        _cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            AutomaticDecompression = DecompressionMethods.All
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:147.0) Gecko/20100101 Firefox/147.0");
        _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        _httpClient.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
    }

    public async Task<MovieCatalogFetchResult> FetchAsync(
        string preferredBaseUrl,
        int pageCount,
        string outputPath,
        MovieCatalogSaveMode saveMode = MovieCatalogSaveMode.MergeWithExisting,
        IProgress<MovieCatalogFetchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (pageCount < 1 || pageCount > MaxPages)
            throw new ArgumentOutOfRangeException(nameof(pageCount), $"Page count must be between 1 and {MaxPages}.");

        var domainCandidates = TinyZoneHtmlParser.GetDomainFallbacks(preferredBaseUrl);
        var workingBaseUrl = await ResolveWorkingBaseUrlAsync(domainCandidates, cancellationToken);

        var discovered = new List<MovieCatalogEntry>();
        var seenSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var page = 1; page <= pageCount; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new MovieCatalogFetchProgress
            {
                CurrentPage = page,
                TotalPages = pageCount,
                MoviesFound = discovered.Count,
                Status = $"Downloading page {page} of {pageCount}..."
            });

            var pageUrl = TinyZoneHtmlParser.BuildListingPageUrl(workingBaseUrl, page);
            var html = await DownloadStringAsync(pageUrl, cancellationToken);
            var pageMovies = TinyZoneHtmlParser.ParseListingPage(html, workingBaseUrl);

            foreach (var movie in pageMovies)
            {
                var slug = movie.Slug;
                if (string.IsNullOrWhiteSpace(slug) || !seenSlugs.Add(slug))
                    continue;

                discovered.Add(movie);
            }

            if (page < pageCount)
                await Task.Delay(RequestDelayMs, cancellationToken);
        }

        var existingSlugs = saveMode == MovieCatalogSaveMode.MergeWithExisting
            ? LoadExistingSlugs(outputPath)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var moviesToFetch = saveMode == MovieCatalogSaveMode.MergeWithExisting
            ? discovered
                .Where(movie => !string.IsNullOrWhiteSpace(movie.Slug) && !existingSlugs.Contains(movie.Slug))
                .ToList()
            : discovered
                .Where(movie => !string.IsNullOrWhiteSpace(movie.Slug))
                .ToList();
        var moviesToWrite = new List<MovieCatalogEntry>();
        var detailFailures = 0;
        var enrichedCount = 0;

        for (var i = 0; i < moviesToFetch.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var movie = moviesToFetch[i];

            progress?.Report(new MovieCatalogFetchProgress
            {
                CurrentPage = pageCount,
                TotalPages = pageCount,
                MoviesFound = discovered.Count,
                MoviesEnriched = enrichedCount,
                Status = $"Fetching details {i + 1} of {moviesToFetch.Count}: {movie.Title}"
            });

            try
            {
                var detailHtml = await DownloadStringAsync(movie.Url, cancellationToken);
                TinyZoneHtmlParser.EnrichFromDetailPage(movie, detailHtml);
                enrichedCount++;
            }
            catch (Exception ex)
            {
                detailFailures++;
                Log.Warning(ex, "Failed to fetch details for {Title}", movie.Title);
                movie.Genre = string.IsNullOrWhiteSpace(movie.Genre) ? "Unknown" : movie.Genre;
                movie.Country = string.IsNullOrWhiteSpace(movie.Country) ? "Unknown" : movie.Country;
            }

            moviesToWrite.Add(movie);

            if (i < moviesToFetch.Count - 1)
                await Task.Delay(RequestDelayMs, cancellationToken);
        }

        var added = SaveMovies(outputPath, moviesToWrite, saveMode);
        var skipped = discovered.Count - moviesToFetch.Count;

        return new MovieCatalogFetchResult
        {
            PagesFetched = pageCount,
            MoviesDiscovered = discovered.Count,
            MoviesAdded = added,
            MoviesSkipped = skipped,
            DetailFailures = detailFailures,
            OutputPath = outputPath,
            BaseUrlUsed = workingBaseUrl,
            SaveMode = saveMode
        };
    }

    private async Task<string> ResolveWorkingBaseUrlAsync(IReadOnlyList<string> candidates, CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        foreach (var candidate in candidates)
        {
            try
            {
                var testUrl = TinyZoneHtmlParser.BuildListingPageUrl(candidate, 1);
                var html = await DownloadStringAsync(testUrl, cancellationToken);
                if (html.Contains("flw-item", StringComparison.OrdinalIgnoreCase))
                {
                    _cookieContainer.Add(new Uri(candidate), new Cookie("srv", "2", "/", new Uri(candidate).Host));
                    return candidate;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
                Log.Warning(ex, "TinyZone domain candidate failed: {Domain}", candidate);
            }
        }

        throw new InvalidOperationException(
            "Could not reach TinyZone. Try a different domain (ww3, ww4, or ww5).",
            lastError);
    }

    private async Task<string> DownloadStringAsync(string url, CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception ex) when (attempt < MaxRetries)
            {
                lastError = ex;
                await Task.Delay(1000, cancellationToken);
            }
            catch (Exception ex)
            {
                lastError = ex;
                break;
            }
        }

        throw new InvalidOperationException($"Failed to download {url}", lastError);
    }

    private static HashSet<string> LoadExistingSlugs(string outputPath)
    {
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(outputPath))
            return slugs;

        foreach (var line in File.ReadAllLines(outputPath))
        {
            var entry = MovieCatalogEntry.FromFileLine(line);
            if (entry == null)
                continue;

            var slug = entry.Slug;
            if (!string.IsNullOrWhiteSpace(slug))
                slugs.Add(slug);
        }

        return slugs;
    }

    private static int SaveMovies(string outputPath, IReadOnlyList<MovieCatalogEntry> movies, MovieCatalogSaveMode saveMode)
    {
        if (movies.Count == 0)
            return 0;

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var newLines = movies.Select(movie => movie.ToFileLine()).ToList();

        if (saveMode == MovieCatalogSaveMode.Overwrite || !File.Exists(outputPath))
        {
            File.WriteAllLines(outputPath, newLines);
            return movies.Count;
        }

        var existingLines = File.ReadAllLines(outputPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        var mergedLines = newLines.Concat(existingLines).ToList();
        File.WriteAllLines(outputPath, mergedLines);

        return movies.Count;
    }
}
