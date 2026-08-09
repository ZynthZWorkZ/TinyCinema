using System.Text;
using System.Text.RegularExpressions;

namespace TinyCinema;

public static class SearchPassageBuilder
{
    private const int ApproxMaxChars = 1800;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "of", "in", "on", "at", "to", "for", "is", "are", "was", "were",
        "with", "from", "by", "as", "that", "this", "their", "his", "her", "its", "who", "when", "after",
        "before", "into", "over", "under", "about", "through", "passage", "title", "genre", "country",
        "director", "cast", "unknown"
    };

    public static string BuildQuery(string userQuery) => $"query: {userQuery.Trim()}";

    public static string BuildPassage(MovieCatalogRecord record)
    {
        var builder = new StringBuilder("passage: ");
        builder.Append($"Title: {record.Title}");

        if (!string.IsNullOrWhiteSpace(record.Year) &&
            !record.Year.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append($" ({record.Year})");
        }

        if (!string.IsNullOrWhiteSpace(record.Genre) &&
            !record.Genre.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append(". Genre: ");
            builder.Append(record.Genre);
        }

        if (!string.IsNullOrWhiteSpace(record.Country) &&
            !record.Country.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append(". Country: ");
            builder.Append(record.Country);
        }

        if (!string.IsNullOrWhiteSpace(record.Director))
        {
            builder.Append(". Director: ");
            builder.Append(record.Director);
        }

        if (record.Cast.Count > 0)
        {
            builder.Append(". Cast: ");
            builder.Append(string.Join(", ", record.Cast));
        }

        if (!string.IsNullOrWhiteSpace(record.Description))
        {
            builder.Append(". ");
            builder.Append(record.Description);
        }

        var text = builder.ToString();
        if (text.Length <= ApproxMaxChars)
            return text;

        return text[..ApproxMaxChars];
    }

    public static string BuildPassage(Movie movie) => BuildPassage(MovieCatalogRecord.FromMovie(movie));

    public static SearchPassageDescription Describe(MovieCatalogRecord record)
    {
        var fields = new List<SearchPassageField>();
        var titleValue = record.Title;

        if (!string.IsNullOrWhiteSpace(record.Year) &&
            !record.Year.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            titleValue += $" ({record.Year})";
        }

        fields.Add(new SearchPassageField { Key = "Title", Value = titleValue });

        if (!string.IsNullOrWhiteSpace(record.Genre) &&
            !record.Genre.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            fields.Add(new SearchPassageField { Key = "Genre", Value = record.Genre });
        }

        if (!string.IsNullOrWhiteSpace(record.Country) &&
            !record.Country.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            fields.Add(new SearchPassageField { Key = "Country", Value = record.Country });
        }

        if (!string.IsNullOrWhiteSpace(record.Director))
        {
            fields.Add(new SearchPassageField { Key = "Director", Value = record.Director });
        }

        if (record.Cast.Count > 0)
        {
            fields.Add(new SearchPassageField
            {
                Key = "Cast",
                Value = string.Join(", ", record.Cast)
            });
        }

        if (!string.IsNullOrWhiteSpace(record.Description))
        {
            fields.Add(new SearchPassageField
            {
                Key = "Description",
                Value = TruncateForDisplay(record.Description, 320)
            });
        }

        if (fields.Count == 1)
        {
            fields.Add(new SearchPassageField
            {
                Key = "Note",
                Value = "Only title/year available — enrich catalog for better semantic search."
            });
        }

        var fullPassage = BuildPassage(record);
        var preview = fullPassage.Length <= 600
            ? fullPassage
            : fullPassage[..600] + "…";

        return new SearchPassageDescription
        {
            Fields = fields,
            Preview = preview,
            SearchKeywords = ExtractSearchKeywords(record)
        };
    }

    public static SearchPassageDescription WithModelTokens(
        SearchPassageDescription description,
        IReadOnlyList<string> modelTokens) =>
        new()
        {
            Fields = description.Fields,
            Preview = description.Preview,
            SearchKeywords = description.SearchKeywords,
            ModelTokens = modelTokens
        };

    public static IReadOnlyList<string> ExtractSearchKeywords(MovieCatalogRecord record)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddToken(keywords, record.Year);

        if (!string.IsNullOrWhiteSpace(record.Genre) &&
            !record.Genre.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var part in record.Genre.Split([',', '/', '|', ';'], StringSplitOptions.RemoveEmptyEntries))
                AddToken(keywords, part);
        }

        AddToken(keywords, record.Country);
        AddToken(keywords, record.Director);

        foreach (var actor in record.Cast)
        {
            AddToken(keywords, actor);
            foreach (var part in actor.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                AddToken(keywords, part);
        }

        AddWordsFromText(keywords, record.Title);
        AddWordsFromText(keywords, record.Description);

        return keywords
            .OrderBy(keyword => keyword, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddWordsFromText(HashSet<string> keywords, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        foreach (Match match in Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}']+"))
        {
            var word = match.Value.Trim('\'');
            AddToken(keywords, word);
        }
    }

    private static void AddToken(HashSet<string> keywords, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;

        var normalized = token.Trim().ToLowerInvariant();
        if (normalized.Length < 2 || StopWords.Contains(normalized))
            return;

        keywords.Add(normalized);
    }

    private static string TruncateForDisplay(string text, int maxChars)
    {
        var trimmed = text.Trim();
        if (trimmed.Length <= maxChars)
            return trimmed;

        return trimmed[..maxChars] + "…";
    }
}
