using System.Text;

namespace TinyCinema;

public static class SearchPassageBuilder
{
    private const int ApproxMaxChars = 1800;

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
}
