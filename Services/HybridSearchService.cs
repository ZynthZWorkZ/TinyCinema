using System.Text.RegularExpressions;

namespace TinyCinema;

public static class HybridSearchService
{
    private const float SemanticWeightStrong = 0.45f;
    private const float SemanticWeightWeak = 0.12f;
    private const float SemanticMinInclude = 0.42f;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "and", "or", "of", "in", "on", "at", "to", "for", "is"
    };

    public static List<Movie> FilterAndRank(
        IReadOnlyList<Movie> catalog,
        string searchText,
        SearchIndexData? index,
        EmbeddingModelService? embeddingModel,
        string selectedContentType,
        string selectedGenre,
        string selectedCountry,
        bool showFavoritesOnly)
    {
        var candidates = catalog.Where(movie =>
            PassesNavFilters(movie, selectedContentType, selectedGenre, selectedCountry, showFavoritesOnly));

        var normalizedSearch = searchText.Trim();
        if (string.IsNullOrWhiteSpace(normalizedSearch))
            return candidates.ToList();

        var (titleQuery, yearQuery) = SplitQueryYear(normalizedSearch);
        var terms = SplitSearchTerms(normalizedSearch);
        if (terms.Length == 0)
            return candidates.ToList();

        float[]? queryVector = null;
        if (embeddingModel != null && index != null && normalizedSearch.Length >= 3)
        {
            try
            {
                queryVector = embeddingModel.EmbedQuery(normalizedSearch);
            }
            catch
            {
                queryVector = null;
            }
        }

        return candidates
            .Select(movie =>
            {
                var titleMatch = EvaluateTitleMatch(movie, normalizedSearch, titleQuery, yearQuery, terms);
                var peopleScore = ScorePeople(movie, normalizedSearch, terms);
                var keywordScore = ScoreKeywords(movie, terms);
                var semanticScore = queryVector != null && index!.TryGetVector(movie.Url, out var vector)
                    ? DotProduct(queryVector, vector)
                    : 0f;

                var semanticWeight = titleMatch.Tier >= 4
                    ? 0.02f
                    : titleMatch.Tier >= 2
                        ? SemanticWeightWeak
                        : peopleScore >= 0.85f
                            ? SemanticWeightWeak
                            : SemanticWeightStrong;

                var sortKey =
                    titleMatch.Tier * 1_000_000f +
                    (titleMatch.YearMatched ? 50_000f : 0f) +
                    peopleScore * 10_000f +
                    titleMatch.Score * 1_000f +
                    keywordScore * 100f +
                    semanticScore * semanticWeight * 10f;

                return (movie, sortKey, titleTier: titleMatch.Tier, peopleScore, keywordScore, semanticScore);
            })
            .Where(entry =>
                entry.titleTier > 0 ||
                entry.peopleScore >= 0.5f ||
                entry.keywordScore >= 0.85f ||
                (queryVector != null && entry.semanticScore >= SemanticMinInclude))
            .OrderByDescending(entry => entry.sortKey)
            .ThenBy(entry => entry.movie.Title, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.movie)
            .ToList();
    }

    public static bool MatchesExtended(Movie movie, string searchText)
    {
        var normalizedSearch = searchText.Trim();
        if (string.IsNullOrWhiteSpace(normalizedSearch))
            return true;

        var (titleQuery, yearQuery) = SplitQueryYear(normalizedSearch);
        var terms = SplitSearchTerms(normalizedSearch);
        if (terms.Length == 0)
            return true;

        var titleMatch = EvaluateTitleMatch(movie, normalizedSearch, titleQuery, yearQuery, terms);
        if (titleMatch.Tier > 0)
            return true;

        if (ScorePeople(movie, normalizedSearch, terms) >= 0.5f)
            return true;

        return terms.All(term => ContainsTerm(movie, term));
    }

    private static (string TitlePart, string? YearPart) SplitQueryYear(string query)
    {
        var match = Regex.Match(query.Trim(), @"^(.*?)\s+(19\d{2}|20\d{2})$");
        if (!match.Success)
            return (query, null);

        var titlePart = match.Groups[1].Value.Trim();
        return string.IsNullOrWhiteSpace(titlePart) ? (query, null) : (titlePart, match.Groups[2].Value);
    }

    private static bool PassesNavFilters(
        Movie movie,
        string selectedContentType,
        string selectedGenre,
        string selectedCountry,
        bool showFavoritesOnly)
    {
        if (!string.IsNullOrEmpty(selectedContentType))
        {
            if (selectedContentType == "Movies" && movie.ContentType != CatalogContentType.Movie)
                return false;
            if (selectedContentType == "TV Shows" && movie.ContentType != CatalogContentType.TvShow)
                return false;
        }

        if (!string.IsNullOrEmpty(selectedGenre) &&
            !movie.Genre.Split(',').Select(g => g.Trim()).Contains(selectedGenre))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(selectedCountry) &&
            !movie.Country.Split(',').Select(c => c.Trim()).Contains(selectedCountry))
        {
            return false;
        }

        if (showFavoritesOnly && !movie.IsFavorite)
            return false;

        return true;
    }

    private static string[] SplitSearchTerms(string searchText) =>
        searchText
            .ToLowerInvariant()
            .Split([' ', '-', '_', '.', ',', ':', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length >= 2)
            .ToArray();

    private static string[] MeaningfulTerms(string[] terms) =>
        terms.Where(term => !StopWords.Contains(term)).ToArray();

    private static string NormalizeForCompare(string text) =>
        Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9\s]", " ")
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim();

    private static (int Tier, float Score, bool YearMatched) EvaluateTitleMatch(
        Movie movie,
        string rawQuery,
        string titleQuery,
        string? yearQuery,
        string[] terms)
    {
        var normalizedQuery = NormalizeForCompare(rawQuery);
        var normalizedTitleQuery = NormalizeForCompare(titleQuery);
        var normalizedTitle = NormalizeForCompare(movie.Title);
        var year = movie.Year.ToLowerInvariant();

        var yearMatched = !string.IsNullOrWhiteSpace(yearQuery) &&
                          year.Contains(yearQuery, StringComparison.Ordinal);

        if (normalizedTitle == normalizedQuery || normalizedTitle == normalizedTitleQuery)
            return (5, 1f, yearMatched);

        if (normalizedTitle.StartsWith(normalizedQuery, StringComparison.Ordinal) ||
            normalizedQuery.StartsWith(normalizedTitle, StringComparison.Ordinal) ||
            normalizedTitle.StartsWith(normalizedTitleQuery, StringComparison.Ordinal) ||
            normalizedTitleQuery.StartsWith(normalizedTitle, StringComparison.Ordinal))
        {
            return (4, 0.95f, yearMatched);
        }

        if (normalizedTitle.Contains(normalizedQuery, StringComparison.Ordinal) ||
            normalizedQuery.Contains(normalizedTitle, StringComparison.Ordinal) ||
            normalizedTitle.Contains(normalizedTitleQuery, StringComparison.Ordinal))
        {
            return (4, 0.9f, yearMatched);
        }

        var meaningfulTerms = MeaningfulTerms(terms);
        if (meaningfulTerms.Length == 0)
            meaningfulTerms = terms;

        if (meaningfulTerms.Length > 0 && meaningfulTerms.All(term => normalizedTitle.Contains(term, StringComparison.Ordinal)))
            return (3, 0.85f, yearMatched);

        var titleScore = ScoreTitleTerms(normalizedTitle, terms, year);
        if (titleScore >= 0.75f)
            return (2, titleScore, yearMatched);

        if (titleScore >= 0.45f)
            return (1, titleScore, yearMatched);

        return (0, titleScore, yearMatched);
    }

    private static float ScoreTitleTerms(string normalizedTitle, string[] terms, string year)
    {
        if (terms.Length == 0)
            return 0f;

        float score = 0f;
        foreach (var term in terms)
        {
            if (normalizedTitle.Contains(term, StringComparison.Ordinal))
                score += 1f;
            else if (IsPartialWordMatch(normalizedTitle, term))
                score += 0.6f;
            else if (IsFuzzyMatch(normalizedTitle, term))
                score += 0.35f;

            if (year.Contains(term, StringComparison.Ordinal))
                score += 0.25f;
        }

        return Math.Min(score / terms.Length, 1f);
    }

    private static float ScorePeople(Movie movie, string rawQuery, string[] terms)
    {
        if (movie.ContentType != CatalogContentType.Movie)
            return 0f;

        var query = rawQuery.ToLowerInvariant();
        var director = movie.Director.ToLowerInvariant();
        float score = 0f;

        if (!string.IsNullOrWhiteSpace(director))
        {
            if (DirectorOrCastExactMatch(director, query))
                score = Math.Max(score, 1f);
            else if (terms.All(term => director.Contains(term, StringComparison.Ordinal)))
                score = Math.Max(score, 0.85f);
        }

        foreach (var actor in movie.Cast)
        {
            var name = actor.ToLowerInvariant();
            if (DirectorOrCastExactMatch(name, query))
            {
                score = Math.Max(score, 1f);
                continue;
            }

            if (terms.All(term => name.Contains(term, StringComparison.Ordinal)))
                score = Math.Max(score, 0.85f);
        }

        return Math.Min(score, 1f);
    }

    private static bool DirectorOrCastExactMatch(string name, string query)
    {
        if (name.Contains(query, StringComparison.Ordinal))
            return true;

        var normalizedName = NormalizeForCompare(name);
        var normalizedQuery = NormalizeForCompare(query);
        return normalizedName == normalizedQuery ||
               normalizedName.StartsWith(normalizedQuery, StringComparison.Ordinal) ||
               normalizedQuery.StartsWith(normalizedName, StringComparison.Ordinal);
    }

    private static float ScoreKeywords(Movie movie, string[] terms)
    {
        var haystack = BuildKeywordHaystack(movie);
        var hits = 0;

        foreach (var term in terms)
        {
            if (haystack.Contains(term, StringComparison.Ordinal))
                hits++;
        }

        return hits == 0 ? 0f : Math.Min((float)hits / terms.Length, 1f);
    }

    private static bool ContainsTerm(Movie movie, string term)
    {
        var haystack = BuildKeywordHaystack(movie);
        return haystack.Contains(term, StringComparison.Ordinal);
    }

    private static string BuildKeywordHaystack(Movie movie)
    {
        var cast = movie.Cast.Count > 0 ? string.Join(' ', movie.Cast) : string.Empty;
        return string.Join(' ',
                movie.Title,
                movie.Year,
                movie.Genre,
                movie.Country,
                movie.Duration,
                movie.Description,
                movie.Director,
                cast)
            .ToLowerInvariant();
    }

    private static float DotProduct(float[] left, float[] right)
    {
        var length = Math.Min(left.Length, right.Length);
        var sum = 0f;
        for (var i = 0; i < length; i++)
            sum += left[i] * right[i];
        return sum;
    }

    private static bool IsPartialWordMatch(string text, string term)
    {
        var words = text.Split([' ', '-', '_', '.', ','], StringSplitOptions.RemoveEmptyEntries);
        return words.Any(word =>
            word.StartsWith(term, StringComparison.Ordinal) ||
            term.StartsWith(word, StringComparison.Ordinal));
    }

    private static bool IsFuzzyMatch(string text, string term)
    {
        if (term.Length < 3)
            return false;

        var maxDistance = term.Length <= 4 ? 1 : 2;
        var words = text.Split([' ', '-', '_', '.', ','], StringSplitOptions.RemoveEmptyEntries);
        return words.Any(word => LevenshteinDistance(word, term) <= maxDistance);
    }

    private static int LevenshteinDistance(string source, string target)
    {
        if (source.Length == 0)
            return target.Length;
        if (target.Length == 0)
            return source.Length;

        var distances = new int[source.Length + 1, target.Length + 1];
        for (var i = 0; i <= source.Length; i++)
            distances[i, 0] = i;
        for (var j = 0; j <= target.Length; j++)
            distances[0, j] = j;

        for (var i = 1; i <= source.Length; i++)
        {
            for (var j = 1; j <= target.Length; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + cost);
            }
        }

        return distances[source.Length, target.Length];
    }
}
