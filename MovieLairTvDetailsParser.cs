using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace TinyCinema;

public static class MovieLairTvDetailsParser
{
    public static int? ExtractShowId(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var match = Regex.Match(url, @"/watch-tv/(\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var showId)
            ? showId
            : null;
    }

    public static (string Description, string Tagline) ParseDescription(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var aboutCol = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'aboutShow')]//div[contains(@class,'text-light')]")
            ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'aboutShow')]");

        if (aboutCol == null)
            return (string.Empty, string.Empty);

        var tagline = HtmlEntity.DeEntitize(
            aboutCol.SelectSingleNode(".//p[contains(@class,'lead')]")?.InnerText ?? string.Empty).Trim();

        var description = string.Empty;
        var paragraphs = aboutCol.SelectNodes(".//p");
        if (paragraphs != null)
        {
            foreach (var paragraph in paragraphs)
            {
                var className = paragraph.GetAttributeValue("class", string.Empty);
                if (className.Contains("lead", StringComparison.OrdinalIgnoreCase))
                    continue;

                var text = HtmlEntity.DeEntitize(paragraph.InnerText).Trim();
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                if (text.StartsWith("Watch ", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (text.StartsWith("Seasons:", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (text.Equals("Genre:", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (text.Contains("/10", StringComparison.Ordinal) && text.Contains("Released", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (text.Length > description.Length)
                    description = text;
            }
        }

        if (!string.IsNullOrWhiteSpace(tagline) && !string.IsNullOrWhiteSpace(description))
            return ($"{tagline}\n\n{description}", tagline);

        if (!string.IsNullOrWhiteSpace(description))
            return (description, tagline);

        return (tagline, tagline);
    }
}
