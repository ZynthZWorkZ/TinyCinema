namespace TinyCinema;

public sealed class WhatsOnImportProgress
{
    public int Processed { get; init; }
    public int Total { get; init; }
    public int Added { get; init; }
    public int Skipped { get; init; }
    public int Failed { get; init; }
    public string Status { get; init; } = string.Empty;
}

public sealed class WhatsOnImportResult
{
    public bool Added { get; init; }
    public bool AlreadyExists { get; init; }
    public bool TmdbNotFound { get; init; }
    public string Title { get; init; } = string.Empty;
    public MovieCatalogRecord? Record { get; init; }
}

public sealed class WhatsOnBulkImportResult
{
    public int Added { get; init; }
    public int Skipped { get; init; }
    public int Failed { get; init; }
    public int Total { get; init; }
}

public static class WhatsOnCatalogImporter
{
    public static async Task<WhatsOnImportResult> ImportEntryAsync(
        WhatsOnNetflixItem item,
        string catalogPath,
        string? tmdbApiKey,
        MoviePlayerSource preferredSource = MoviePlayerSource.VidSrc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tmdbApiKey))
        {
            return new WhatsOnImportResult
            {
                TmdbNotFound = true,
                Title = item.Title
            };
        }

        var record = await BuildRecordAsync(item, tmdbApiKey, preferredSource, cancellationToken);
        if (record == null)
        {
            return new WhatsOnImportResult
            {
                TmdbNotFound = true,
                Title = item.Title
            };
        }

        if (await AlreadyExistsAsync(catalogPath, record, cancellationToken))
        {
            return new WhatsOnImportResult
            {
                AlreadyExists = true,
                Title = record.Title,
                Record = record
            };
        }

        var added = await MovieCatalogStore.AddMovieAsync(catalogPath, record, cancellationToken);
        return new WhatsOnImportResult
        {
            Added = added,
            AlreadyExists = !added,
            Title = record.Title,
            Record = record
        };
    }

    public static async Task<WhatsOnBulkImportResult> ImportGapsAsync(
        string catalogPath,
        IReadOnlyList<Movie> localCatalog,
        IReadOnlyList<WhatsOnNetflixItem> netflixItems,
        string? tmdbApiKey,
        MoviePlayerSource preferredSource = MoviePlayerSource.VidSrc,
        IProgress<WhatsOnImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tmdbApiKey))
            throw new InvalidOperationException("A TMDB API key is required to import Netflix titles.");

        var matchIndex = WhatsOnCatalogMatcher.BuildMatchIndex(localCatalog);
        var gaps = netflixItems
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .Where(item => !WhatsOnCatalogMatcher.IsInCatalog(item, matchIndex))
            .ToList();

        var existingCatalog = await MovieCatalogStore.LoadAsync(catalogPath, cancellationToken);
        var existingSlugs = existingCatalog.Movies
            .Where(movie => !string.IsNullOrWhiteSpace(movie.Slug))
            .Select(movie => movie.Slug)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingTmdbIds = existingCatalog.Movies
            .Where(movie => movie.TmdbId is > 0)
            .Select(movie => movie.TmdbId!.Value)
            .ToHashSet();

        var toAdd = new List<MovieCatalogRecord>();
        var added = 0;
        var skipped = 0;
        var failed = 0;
        var processed = 0;

        foreach (var item in gaps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processed++;

            progress?.Report(new WhatsOnImportProgress
            {
                Processed = processed,
                Total = gaps.Count,
                Added = added,
                Skipped = skipped,
                Failed = failed,
                Status = $"Looking up \"{item.Title}\" on TMDB..."
            });

            MovieCatalogRecord? record;
            try
            {
                record = await BuildRecordAsync(item, tmdbApiKey, preferredSource, cancellationToken);
            }
            catch
            {
                failed++;
                continue;
            }

            if (record == null)
            {
                failed++;
                continue;
            }

            if (existingSlugs.Contains(record.Slug) ||
                (record.TmdbId is > 0 && existingTmdbIds.Contains(record.TmdbId.Value)))
            {
                skipped++;
                continue;
            }

            toAdd.Add(record);
            existingSlugs.Add(record.Slug);
            if (record.TmdbId is > 0)
                existingTmdbIds.Add(record.TmdbId.Value);
            added++;

            if (toAdd.Count >= 50)
            {
                await MovieCatalogStore.MergeMoviesAsync(
                    catalogPath,
                    toAdd,
                    MovieCatalogSaveMode.MergeWithExisting,
                    cancellationToken);
                toAdd.Clear();
            }

            await Task.Delay(250, cancellationToken);
        }

        if (toAdd.Count > 0)
        {
            await MovieCatalogStore.MergeMoviesAsync(
                catalogPath,
                toAdd,
                MovieCatalogSaveMode.MergeWithExisting,
                cancellationToken);
        }

        progress?.Report(new WhatsOnImportProgress
        {
            Processed = gaps.Count,
            Total = gaps.Count,
            Added = added,
            Skipped = skipped,
            Failed = failed,
            Status = "Import complete."
        });

        return new WhatsOnBulkImportResult
        {
            Added = added,
            Skipped = skipped,
            Failed = failed,
            Total = gaps.Count
        };
    }

    public static async Task<MovieCatalogRecord?> BuildRecordAsync(
        WhatsOnNetflixItem item,
        string tmdbApiKey,
        MoviePlayerSource preferredSource = MoviePlayerSource.VidSrc,
        CancellationToken cancellationToken = default)
    {
        var movieId = await TmdbClient.ResolveMovieIdAsync(item.Title, item.Year, tmdbApiKey, cancellationToken);
        if (movieId is not > 0)
            return null;

        string url;
        string playbackSourceName;

        if (preferredSource == MoviePlayerSource.MovieLair)
        {
            url = MovieLairTmdbClient.BuildMovieWatchUrl(movieId.Value);
            playbackSourceName = "movielair";
        }
        else
        {
            var imdbId = await TmdbClient.GetMovieImdbIdAsync(movieId.Value, tmdbApiKey, cancellationToken);
            var contentId = VidSrcEmbedBuilder.PickContentId(movieId, imdbId);
            if (string.IsNullOrWhiteSpace(contentId))
                return null;

            url = VidSrcEmbedBuilder.BuildMovieEmbedUrl(contentId);
            playbackSourceName = "vidsrc";
        }

        return new MovieCatalogRecord
        {
            Title = item.Title.Trim(),
            Year = string.IsNullOrWhiteSpace(item.Year) ? "Unknown" : item.Year.Trim(),
            Url = url,
            Poster = item.Image?.Trim() ?? string.Empty,
            Genre = string.IsNullOrWhiteSpace(item.Genre) ? string.Empty : item.Genre.Trim(),
            Duration = NormalizeDuration(item.Runtime),
            Country = string.Empty,
            Description = item.Description?.Trim() ?? string.Empty,
            DescriptionFetchedAt = string.IsNullOrWhiteSpace(item.Description) ? null : DateTime.UtcNow,
            PlaybackSource = playbackSourceName,
            TmdbId = movieId,
            StoredSlug = $"tmdb-{movieId}"
        };
    }

    private static async Task<bool> AlreadyExistsAsync(
        string catalogPath,
        MovieCatalogRecord record,
        CancellationToken cancellationToken)
    {
        var catalog = await MovieCatalogStore.LoadAsync(catalogPath, cancellationToken);
        return catalog.Movies.Any(existing =>
            existing.Slug.Equals(record.Slug, StringComparison.OrdinalIgnoreCase) ||
            (record.TmdbId is > 0 && existing.TmdbId == record.TmdbId));
    }

    private static string NormalizeDuration(string? runtime)
    {
        if (string.IsNullOrWhiteSpace(runtime))
            return string.Empty;

        var trimmed = runtime.Trim();
        if (trimmed.EndsWith("min", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        if (int.TryParse(trimmed, out var minutes))
            return $"{minutes}min";

        return TinyZoneHtmlParser.NormalizeDuration(trimmed);
    }
}
