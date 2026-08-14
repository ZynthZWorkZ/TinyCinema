using System.Text.RegularExpressions;

namespace TinyCinema;

public static class WhatsOnCatalogMatcher
{
    public static Dictionary<string, Movie> BuildMatchIndex(IReadOnlyList<Movie> localMovies)
    {
        var index = new Dictionary<string, Movie>(StringComparer.OrdinalIgnoreCase);

        foreach (var movie in localMovies)
        {
            if (movie.ContentType != CatalogContentType.Movie)
                continue;

            var key = BuildMatchKey(movie.Title, movie.Year);
            if (!index.ContainsKey(key))
                index[key] = movie;
        }

        return index;
    }

    public static IReadOnlyList<WhatsOnMovieEntry> BuildEntries(
        WhatsOnNetflixCatalog catalog,
        IReadOnlyList<Movie> localMovies)
    {
        var matchIndex = BuildMatchIndex(localMovies);

        return catalog.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .Select(item => BuildEntry(item, matchIndex))
            .OrderByDescending(entry => entry.IsInCatalog)
            .ThenBy(entry => entry.Item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static WhatsOnMovieEntry BuildEntry(WhatsOnNetflixItem item, Dictionary<string, Movie> matchIndex)
    {
        var key = BuildMatchKey(item.Title, item.Year);
        matchIndex.TryGetValue(key, out var matched);

        return new WhatsOnMovieEntry
        {
            Item = item,
            IsInCatalog = matched != null,
            CatalogMovie = matched
        };
    }

    public static Movie ToHeroMovie(WhatsOnMovieEntry entry)
    {
        if (entry.CatalogMovie != null)
            return entry.CatalogMovie;

        var movie = new Movie
        {
            Title = entry.Item.Title,
            Year = entry.Item.Year ?? string.Empty,
            Url = entry.Item.NetflixLink ?? string.Empty,
            ImageUrl = entry.Item.Image ?? string.Empty,
            Genre = entry.Item.Genre ?? string.Empty,
            Duration = entry.Item.Runtime ?? string.Empty,
            Country = string.Empty,
            Description = entry.Item.Description ?? string.Empty,
            ContentType = CatalogContentType.Movie
        };

        return movie;
    }

    public static bool IsInCatalog(WhatsOnNetflixItem item, Dictionary<string, Movie> matchIndex) =>
        matchIndex.ContainsKey(BuildMatchKey(item.Title, item.Year));

    public static int CountInCatalog(IEnumerable<WhatsOnNetflixItem> items, Dictionary<string, Movie> matchIndex) =>
        items.Count(item => IsInCatalog(item, matchIndex));

    public static string BuildMatchKey(string title, string? year) =>
        $"{NormalizeTitle(title)}|{NormalizeYear(year) ?? string.Empty}";

    private static string NormalizeTitle(string title) =>
        Regex.Replace(title.ToLowerInvariant(), @"[^a-z0-9]", string.Empty);

    private static string? NormalizeYear(string? year)
    {
        if (string.IsNullOrWhiteSpace(year))
            return null;

        var match = Regex.Match(year, @"\d{4}");
        return match.Success ? match.Value : year.Trim();
    }
}
