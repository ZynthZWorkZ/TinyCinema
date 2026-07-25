namespace TinyCinema;

public sealed class CatalogTasteProfile
{
    public Dictionary<string, double> GenreWeights { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> CountryWeights { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public double MoviePreferenceRatio { get; init; } = 0.5;
    public int? PreferredYearCenter { get; init; }
    public HashSet<string> RecentUrls { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> FavoriteUrls { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ContinueWatchingUrls { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public int TotalWeightedSignals { get; init; }

    public static CatalogTasteProfile Build(
        IReadOnlyList<UserInteractionEntry> interactions,
        IReadOnlyList<Movie> catalog)
    {
        var genreWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var countryWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var recentUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var favoriteUrls = catalog.Where(movie => movie.IsFavorite).Select(movie => movie.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var continueUrls = TvShowWatchHistory.GetAllEntries()
            .Select(entry => entry.ShowUrl)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        double movieWeight = 0;
        double tvWeight = 0;
        double yearWeightSum = 0;
        double yearSignalSum = 0;
        var totalWeightedSignals = 0;

        foreach (var entry in interactions.OrderByDescending(item => item.TimestampUtc).Take(150))
        {
            var weight = UserInteractionTracker.GetEventWeight(entry.EventType);
            totalWeightedSignals += weight;
            recentUrls.Add(entry.Url);

            foreach (var genre in SplitTags(entry.Genre))
                genreWeights[genre] = genreWeights.GetValueOrDefault(genre) + weight;

            foreach (var country in SplitTags(entry.Country))
                countryWeights[country] = countryWeights.GetValueOrDefault(country) + weight;

            if (entry.ContentType == CatalogContentType.Movie)
                movieWeight += weight;
            else
                tvWeight += weight;

            if (int.TryParse(entry.Year, out var yearValue))
            {
                yearWeightSum += yearValue * weight;
                yearSignalSum += weight;
            }
        }

        foreach (var favorite in catalog.Where(movie => movie.IsFavorite))
        {
            var weight = UserInteractionTracker.GetEventWeight(InteractionEventType.Favorite);
            foreach (var genre in SplitTags(favorite.Genre))
                genreWeights[genre] = genreWeights.GetValueOrDefault(genre) + weight;
            foreach (var country in SplitTags(favorite.Country))
                countryWeights[country] = countryWeights.GetValueOrDefault(country) + weight;
        }

        var contentTotal = movieWeight + tvWeight;
        var movieRatio = contentTotal > 0 ? movieWeight / contentTotal : 0.5;
        int? preferredYear = yearSignalSum > 0 ? (int)Math.Round(yearWeightSum / yearSignalSum) : null;

        return new CatalogTasteProfile
        {
            GenreWeights = genreWeights,
            CountryWeights = countryWeights,
            MoviePreferenceRatio = movieRatio,
            PreferredYearCenter = preferredYear,
            RecentUrls = recentUrls,
            FavoriteUrls = favoriteUrls,
            ContinueWatchingUrls = continueUrls,
            TotalWeightedSignals = totalWeightedSignals + (favoriteUrls.Count * UserInteractionTracker.GetEventWeight(InteractionEventType.Favorite))
        };
    }

    private static IEnumerable<string> SplitTags(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            yield break;

        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(part))
                yield return part;
        }
    }
}
