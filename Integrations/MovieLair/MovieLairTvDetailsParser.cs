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

    public static TvShowCatalogEntry? ParseCatalogEntry(string html, string url)
    {
        var showId = ExtractShowId(url);
        if (showId is not > 0)
            return null;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var entry = new TvShowCatalogEntry
        {
            TmdbId = showId.Value,
            Url = MovieLairTmdbClient.BuildWatchUrl(showId.Value)
        };

        var titleNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'aboutShow')]//h1")
            ?? doc.DocumentNode.SelectSingleNode("//h1");
        entry.Title = HtmlEntity.DeEntitize(titleNode?.InnerText ?? string.Empty).Trim();

        var ogImage = doc.DocumentNode
            .SelectSingleNode("//meta[@property='og:image' or @property='twitter:image']")
            ?.GetAttributeValue("content", string.Empty)
            .Trim();
        if (!string.IsNullOrWhiteSpace(ogImage))
            entry.ImageUrl = ogImage;

        var poster = doc.DocumentNode.SelectSingleNode("//img[contains(@class,'moviePoster')]");
        if (string.IsNullOrWhiteSpace(entry.ImageUrl) && poster != null)
        {
            entry.ImageUrl = poster.GetAttributeValue("src", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(entry.ImageUrl))
                entry.ImageUrl = poster.GetAttributeValue("data-src", string.Empty).Trim();
        }

        var aboutCol = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'aboutShow')]");
        if (aboutCol != null)
        {
            var aboutText = HtmlEntity.DeEntitize(aboutCol.InnerText);

            var releasedMatch = Regex.Match(aboutText, @"Released\s+(\d{4})", RegexOptions.IgnoreCase);
            if (releasedMatch.Success)
                entry.Year = releasedMatch.Groups[1].Value;

            var seasonsMatch = Regex.Match(aboutText, @"Seasons:\s*(\d+)\s*Episodes:\s*(\d+)", RegexOptions.IgnoreCase);
            if (seasonsMatch.Success)
            {
                entry.Duration = $"{seasonsMatch.Groups[1].Value} seasons, {seasonsMatch.Groups[2].Value} episodes";
            }

            var genreLinks = aboutCol.SelectNodes(".//ul[contains(@class,'list-group')]//a");
            if (genreLinks != null)
            {
                entry.Genre = string.Join(", ", genreLinks
                    .Select(link => HtmlEntity.DeEntitize(link.InnerText).Trim())
                    .Where(text => !string.IsNullOrWhiteSpace(text)));
            }
        }

        if (string.IsNullOrWhiteSpace(entry.Title))
            return null;

        entry.Year = string.IsNullOrWhiteSpace(entry.Year) ? "Unknown" : entry.Year;
        entry.Genre = string.IsNullOrWhiteSpace(entry.Genre) ? "Unknown" : entry.Genre;
        entry.Duration = string.IsNullOrWhiteSpace(entry.Duration) ? "Unknown" : entry.Duration;
        entry.Country = string.IsNullOrWhiteSpace(entry.Country) ? "Unknown" : entry.Country;
        entry.ImageUrl ??= string.Empty;

        return entry;
    }

    public static string NormalizeWatchUrl(string url)
    {
        var showId = ExtractShowId(url);
        return showId is > 0 ? MovieLairTmdbClient.BuildWatchUrl(showId.Value) : url.Trim();
    }
}
