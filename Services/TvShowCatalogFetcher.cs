using System.IO;
using Serilog;

namespace TinyCinema;

public class TvShowCatalogFetchProgress
{
    public int CurrentPage { get; init; }
    public int TotalPages { get; init; }
    public int ShowsFound { get; init; }
    public int ShowsEnriched { get; init; }
    public string Status { get; init; } = string.Empty;
}

public class TvShowCatalogFetchResult
{
    public int PagesFetched { get; init; }
    public int ShowsDiscovered { get; init; }
    public int ShowsAdded { get; init; }
    public int ShowsSkipped { get; init; }
    public int DetailFailures { get; init; }
    public string OutputPath { get; init; } = string.Empty;
    public string CategoryUrlUsed { get; init; } = string.Empty;
    public MovieCatalogSaveMode SaveMode { get; init; }
}

public class TvShowCatalogFetcher
{
    private const int MaxPages = 500;
    private const int RequestDelayMs = 250;

    public async Task<TvShowCatalogFetchResult> FetchAsync(
        string categoryBaseUrl,
        int pageCount,
        string outputPath,
        string tmdbApiKey,
        MovieCatalogSaveMode saveMode = MovieCatalogSaveMode.MergeWithExisting,
        IProgress<TvShowCatalogFetchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tmdbApiKey))
            throw new InvalidOperationException("Add your TMDB API key in Settings before fetching TV shows.");

        if (pageCount < 1 || pageCount > MaxPages)
            throw new ArgumentOutOfRangeException(nameof(pageCount), $"Page count must be between 1 and {MaxPages}.");

        var genreId = MovieLairTmdbClient.ExtractGenreId(categoryBaseUrl)
            ?? throw new InvalidOperationException(
                "Could not read a TMDB genre ID from that URL. Expected a MovieLair category like https://movielair.cc/shows/10759/");

        var workingCategoryUrl = categoryBaseUrl.TrimEnd('/');
        var genreNames = await MovieLairTmdbClient.GetTvGenreNamesAsync(tmdbApiKey, cancellationToken);

        var discovered = new List<TvShowCatalogEntry>();
        var seenIds = new HashSet<int>();

        for (var page = 1; page <= pageCount; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new TvShowCatalogFetchProgress
            {
                CurrentPage = page,
                TotalPages = pageCount,
                ShowsFound = discovered.Count,
                Status = $"Fetching TMDB page {page} of {pageCount}..."
            });

            var discoverPage = await MovieLairTmdbClient.DiscoverTvAsync(
                genreId,
                page,
                tmdbApiKey,
                cancellationToken);

            foreach (var result in discoverPage.Results)
            {
                if (result.Id <= 0 || !seenIds.Add(result.Id))
                    continue;

                discovered.Add(MovieLairTmdbClient.MapDiscoverResult(result, genreNames));
            }

            if (discoverPage.Results.Count == 0)
                break;

            if (page < pageCount)
                await Task.Delay(RequestDelayMs, cancellationToken);
        }

        var existingIds = saveMode == MovieCatalogSaveMode.MergeWithExisting
            ? TvShowCatalogStore.LoadExistingTmdbIds(outputPath)
            : new HashSet<int>();
        var showsToFetch = saveMode == MovieCatalogSaveMode.MergeWithExisting
            ? discovered
                .Where(show => show.TmdbId > 0 && !existingIds.Contains(show.TmdbId))
                .ToList()
            : discovered
                .Where(show => show.TmdbId > 0)
                .ToList();
        var showsToWrite = new List<TvShowCatalogEntry>();
        var detailFailures = 0;
        var enrichedCount = 0;

        for (var i = 0; i < showsToFetch.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var show = showsToFetch[i];

            progress?.Report(new TvShowCatalogFetchProgress
            {
                CurrentPage = pageCount,
                TotalPages = pageCount,
                ShowsFound = discovered.Count,
                ShowsEnriched = enrichedCount,
                Status = $"Fetching details {i + 1} of {showsToFetch.Count}: {show.Title}"
            });

            try
            {
                var details = await MovieLairTmdbClient.GetTvDetailsAsync(show.TmdbId, tmdbApiKey, cancellationToken);
                if (details != null)
                {
                    MovieLairTmdbClient.EnrichFromDetails(show, details);
                    enrichedCount++;
                }
                else
                {
                    detailFailures++;
                    ApplyFallbackValues(show);
                }
            }
            catch (Exception ex)
            {
                detailFailures++;
                Log.Warning(ex, "Failed to fetch TMDB details for {Title}", show.Title);
                ApplyFallbackValues(show);
            }

            showsToWrite.Add(show);

            if (i < showsToFetch.Count - 1)
                await Task.Delay(RequestDelayMs, cancellationToken);
        }

        var added = await TvShowCatalogStore.MergeShowsAsync(
            outputPath,
            showsToWrite.Select(show => show.ToRecord()).ToList(),
            saveMode,
            cancellationToken);
        var skipped = discovered.Count - showsToFetch.Count;

        return new TvShowCatalogFetchResult
        {
            PagesFetched = pageCount,
            ShowsDiscovered = discovered.Count,
            ShowsAdded = added,
            ShowsSkipped = skipped,
            DetailFailures = detailFailures,
            OutputPath = outputPath,
            CategoryUrlUsed = workingCategoryUrl,
            SaveMode = saveMode
        };
    }

    private static void ApplyFallbackValues(TvShowCatalogEntry show)
    {
        show.Genre = string.IsNullOrWhiteSpace(show.Genre) ? "Unknown" : show.Genre;
        show.Country = string.IsNullOrWhiteSpace(show.Country) ? "Unknown" : show.Country;
        show.Duration = string.IsNullOrWhiteSpace(show.Duration) ? "Unknown" : show.Duration;
        show.Year = string.IsNullOrWhiteSpace(show.Year) ? "Unknown" : show.Year;
    }
}
