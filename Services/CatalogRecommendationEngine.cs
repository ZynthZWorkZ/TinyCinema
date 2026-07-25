namespace TinyCinema;

public sealed class ExploreRecommendationRow
{
    public required string Title { get; init; }
    public required IReadOnlyList<Movie> Items { get; init; }
}

public sealed class ExploreRecommendations
{
    public required IReadOnlyList<ExploreRecommendationRow> Rows { get; init; }
    public string? HintText { get; init; }
}

public static class CatalogRecommendationEngine
{
    private const int MaxRowItems = 12;
    private const int ColdStartThreshold = 3;

    public static ExploreRecommendations BuildLocal(
        IReadOnlyList<Movie> catalog,
        CatalogTasteProfile taste)
    {
        var usedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<ExploreRecommendationRow>();

        var continueRow = BuildContinueWatchingRow(catalog, taste, usedUrls);
        if (continueRow != null)
            rows.Add(continueRow);

        var isColdStart = taste.TotalWeightedSignals < ColdStartThreshold;

        if (isColdStart)
        {
            var discover = BuildDiscoverRow(catalog, usedUrls);
            if (discover != null)
                rows.Add(discover);
        }
        else
        {
            var forYou = BuildScoredRow("For You", catalog, taste, usedUrls, take: 16);
            if (forYou != null)
                rows.Add(forYou);

            var favoritesRow = BuildFavoritesSimilarRow(catalog, taste, usedUrls);
            if (favoritesRow != null)
                rows.Add(favoritesRow);
        }

        return new ExploreRecommendations
        {
            Rows = rows,
            HintText = isColdStart
                ? "Play and favorite titles to personalize your For You page."
                : null
        };
    }

    public static async Task<IReadOnlyList<ExploreRecommendationRow>> BuildTmdbRowsAsync(
        IReadOnlyList<Movie> catalog,
        IReadOnlyList<UserInteractionEntry> interactions,
        HashSet<string> usedUrls,
        CancellationToken cancellationToken = default)
    {
        var apiKey = SettingsWindow.GetTmdbApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            return [];

        var seeds = interactions
            .Where(entry => entry.EventType is InteractionEventType.Play or InteractionEventType.Continue or InteractionEventType.Favorite)
            .OrderByDescending(entry => entry.TimestampUtc)
            .GroupBy(entry => entry.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(3)
            .ToList();

        var rows = new List<ExploreRecommendationRow>();

        foreach (var seed in seeds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var similarItems = seed.ContentType == CatalogContentType.TvShow
                    ? await TmdbClient.GetSimilarTvItemsAsync(seed.Title, seed.Year, MovieLairTvDetailsParser.ExtractShowId(seed.Url), apiKey, cancellationToken)
                    : await TmdbClient.GetSimilarMovieItemsAsync(seed.Title, seed.Year, apiKey, cancellationToken);

                if (similarItems.Count == 0)
                    continue;

                var matches = MapSimilarToCatalog(similarItems, catalog, usedUrls, MaxRowItems);
                if (matches.Count == 0)
                    continue;

                rows.Add(new ExploreRecommendationRow
                {
                    Title = $"Because you watched {seed.Title}",
                    Items = matches
                });
            }
            catch
            {
                // Skip failed TMDB lookups; local rows still work.
            }
        }

        return rows;
    }

    private static ExploreRecommendationRow? BuildContinueWatchingRow(
        IReadOnlyList<Movie> catalog,
        CatalogTasteProfile taste,
        HashSet<string> usedUrls)
    {
        var historyEntries = TvShowWatchHistory.GetAllEntries()
            .OrderByDescending(entry => entry.WatchedAtUtc)
            .ToList();

        if (historyEntries.Count == 0)
            return null;

        var items = new List<Movie>();
        foreach (var history in historyEntries)
        {
            var match = catalog.FirstOrDefault(movie =>
                movie.Url.Equals(history.ShowUrl, StringComparison.OrdinalIgnoreCase));
            if (match == null || !usedUrls.Add(match.Url))
                continue;

            items.Add(match);
            if (items.Count >= MaxRowItems)
                break;
        }

        return items.Count == 0
            ? null
            : new ExploreRecommendationRow { Title = "Continue Watching", Items = items };
    }

    private static ExploreRecommendationRow? BuildDiscoverRow(
        IReadOnlyList<Movie> catalog,
        HashSet<string> usedUrls)
    {
        var genresSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var picks = new List<Movie>();

        foreach (var movie in catalog.OrderBy(_ => Guid.NewGuid()))
        {
            if (!usedUrls.Add(movie.Url))
                continue;

            var primaryGenre = movie.Genre.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(primaryGenre) && !genresSeen.Add(primaryGenre))
                continue;

            picks.Add(movie);
            if (picks.Count >= MaxRowItems)
                break;
        }

        if (picks.Count < 4)
        {
            foreach (var movie in catalog)
            {
                if (picks.Count >= MaxRowItems)
                    break;
                if (usedUrls.Add(movie.Url))
                    picks.Add(movie);
            }
        }

        return picks.Count == 0
            ? null
            : new ExploreRecommendationRow { Title = "Discover", Items = picks };
    }

    private static ExploreRecommendationRow? BuildFavoritesSimilarRow(
        IReadOnlyList<Movie> catalog,
        CatalogTasteProfile taste,
        HashSet<string> usedUrls)
    {
        if (taste.FavoriteUrls.Count == 0 && taste.GenreWeights.Count == 0)
            return null;

        var favorites = catalog.Where(movie => taste.FavoriteUrls.Contains(movie.Url)).ToList();
        var targetGenres = favorites
            .SelectMany(movie => movie.Genre.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(genre => !string.IsNullOrWhiteSpace(genre))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var items = catalog
            .Where(movie => !taste.FavoriteUrls.Contains(movie.Url))
            .Select(movie => new
            {
                Movie = movie,
                Score = ScoreGenreOverlap(movie, targetGenres) + ScoreCatalogItem(movie, taste) * 0.35
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .Select(item => item.Movie)
            .Where(movie => usedUrls.Add(movie.Url))
            .Take(MaxRowItems)
            .ToList();

        return items.Count == 0
            ? null
            : new ExploreRecommendationRow { Title = "More like your favorites", Items = items };
    }

    private static ExploreRecommendationRow? BuildScoredRow(
        string title,
        IReadOnlyList<Movie> catalog,
        CatalogTasteProfile taste,
        HashSet<string> usedUrls,
        int take)
    {
        var items = catalog
            .Select(movie => new { Movie = movie, Score = ScoreCatalogItem(movie, taste) })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => taste.RecentUrls.Contains(item.Movie.Url) ? 1 : 0)
            .Select(item => item.Movie)
            .Where(movie => usedUrls.Add(movie.Url))
            .Take(take)
            .ToList();

        return items.Count == 0 ? null : new ExploreRecommendationRow { Title = title, Items = items };
    }

    private static double ScoreCatalogItem(Movie movie, CatalogTasteProfile taste)
    {
        if (taste.RecentUrls.Contains(movie.Url))
            return 0;

        var score = 0.0;

        foreach (var genre in movie.Genre.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (taste.GenreWeights.TryGetValue(genre.Trim(), out var genreWeight))
                score += Math.Min(40, genreWeight * 4);
        }

        foreach (var country in movie.Country.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (taste.CountryWeights.TryGetValue(country.Trim(), out var countryWeight))
                score += Math.Min(15, countryWeight * 3);
        }

        if (taste.PreferredYearCenter.HasValue && int.TryParse(movie.Year, out var year))
        {
            var distance = Math.Abs(year - taste.PreferredYearCenter.Value);
            if (distance <= 3)
                score += 10;
            else if (distance <= 8)
                score += 5;
        }

        var prefersMovies = taste.MoviePreferenceRatio >= 0.5;
        if (movie.ContentType == CatalogContentType.Movie && prefersMovies)
            score += 10 * taste.MoviePreferenceRatio;
        else if (movie.IsTvShow && !prefersMovies)
            score += 10 * (1 - taste.MoviePreferenceRatio);

        if (taste.FavoriteUrls.Contains(movie.Url))
            score += 20;

        if (taste.ContinueWatchingUrls.Contains(movie.Url))
            score += 15;

        return score;
    }

    private static double ScoreGenreOverlap(Movie movie, HashSet<string> targetGenres)
    {
        if (targetGenres.Count == 0)
            return 0;

        return movie.Genre.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Count(genre => targetGenres.Contains(genre.Trim())) * 12;
    }

    private static List<Movie> MapSimilarToCatalog(
        IReadOnlyList<TmdbSimilarItem> similarItems,
        IReadOnlyList<Movie> catalog,
        HashSet<string> usedUrls,
        int maxItems)
    {
        var matches = new List<Movie>();

        foreach (var similar in similarItems)
        {
            var match = FindCatalogMatch(catalog, similar.Title, similar.Year);
            if (match == null || !usedUrls.Add(match.Url))
                continue;

            matches.Add(match);
            if (matches.Count >= maxItems)
                break;
        }

        return matches;
    }

    private static Movie? FindCatalogMatch(IReadOnlyList<Movie> catalog, string title, string? year)
    {
        var normalizedTitle = NormalizeTitle(title);
        var yearValue = int.TryParse(year, out var parsedYear) ? parsedYear : (int?)null;

        return catalog
            .Select(movie => new { Movie = movie, Score = ScoreCatalogTitle(movie, normalizedTitle, yearValue) })
            .Where(item => item.Score >= 80)
            .OrderByDescending(item => item.Score)
            .Select(item => item.Movie)
            .FirstOrDefault();
    }

    private static int ScoreCatalogTitle(Movie movie, string normalizedTitle, int? year)
    {
        var score = 0;
        if (NormalizeTitle(movie.Title) == normalizedTitle)
            score += 100;
        else if (NormalizeTitle(movie.Title).Contains(normalizedTitle, StringComparison.Ordinal) ||
                 normalizedTitle.Contains(NormalizeTitle(movie.Title), StringComparison.Ordinal))
            score += 60;

        if (year.HasValue && int.TryParse(movie.Year, out var movieYear) && Math.Abs(movieYear - year.Value) <= 1)
            score += 25;

        return score;
    }

    private static string NormalizeTitle(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}

public sealed class TmdbSimilarItem
{
    public required string Title { get; init; }
    public string? Year { get; init; }
}
