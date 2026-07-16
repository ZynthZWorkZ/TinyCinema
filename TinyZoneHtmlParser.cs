using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace TinyCinema;

public static class TinyZoneHtmlParser
{
    private static readonly string[] DomainPrefixes = ["ww5", "ww4", "ww3"];

    public static string NormalizeDuration(string duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
            return "0min";

        var trimmed = duration.Trim();
        if (trimmed.EndsWith("min", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        var match = Regex.Match(trimmed, @"^(\d+)\s*m$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"{match.Groups[1].Value}min";

        return trimmed;
    }

    public static string ExtractMovieSlug(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        var match = Regex.Match(url, @"/movie/([^/]+)/?", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    public static string BuildListingPageUrl(string baseUrl, int page)
    {
        var normalized = baseUrl.TrimEnd('/');
        return page <= 1 ? $"{normalized}/movie/" : $"{normalized}/movie/{page}/";
    }

    public static IReadOnlyList<string> GetDomainFallbacks(string preferredBaseUrl)
    {
        var results = new List<string>();
        var match = Regex.Match(preferredBaseUrl, @"(https?://)(ww\d+)(\.tinyzone\.org)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            results.Add(preferredBaseUrl.TrimEnd('/'));
            return results;
        }

        var prefix = match.Groups[1].Value;
        var suffix = match.Groups[3].Value;
        var preferred = match.Groups[2].Value.ToLowerInvariant();

        results.Add($"{prefix}{preferred}{suffix}");

        foreach (var domain in DomainPrefixes)
        {
            var candidate = $"{prefix}{domain}{suffix}";
            if (!results.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                results.Add(candidate);
        }

        return results;
    }

    public static List<MovieCatalogEntry> ParseListingPage(string html, string pageBaseUrl)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var movies = new List<MovieCatalogEntry>();
        var items = doc.DocumentNode.SelectNodes("//div[contains(@class,'flw-item')]");
        if (items == null)
            return movies;

        foreach (var item in items)
        {
            var link = item.SelectSingleNode(".//h3[contains(@class,'film-name')]//a");
            if (link == null)
                continue;

            var title = HtmlEntity.DeEntitize(link.GetAttributeValue("title", link.InnerText)).Trim();
            var href = link.GetAttributeValue("href", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(href))
                continue;

            var url = MakeAbsoluteUrl(pageBaseUrl, href);
            var imageNode = item.SelectSingleNode(".//img[contains(@class,'film-poster-img')]");
            var imageUrl = imageNode?.GetAttributeValue("data-src", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(imageUrl))
                imageUrl = imageNode?.GetAttributeValue("src", string.Empty).Trim() ?? string.Empty;

            imageUrl = MakeAbsoluteUrl(pageBaseUrl, imageUrl);

            var infoSpans = item.SelectNodes(".//div[contains(@class,'film-infor')]//span");
            var year = string.Empty;
            var duration = string.Empty;

            if (infoSpans != null)
            {
                var values = infoSpans
                    .Where(span => !span.GetAttributeValue("class", string.Empty).Contains("fi-ql"))
                    .Select(span => HtmlEntity.DeEntitize(span.InnerText).Trim())
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToList();

                if (values.Count > 0)
                    year = values[0];
                if (values.Count > 1)
                    duration = NormalizeDuration(values[1]);
            }

            movies.Add(new MovieCatalogEntry
            {
                Year = year,
                Title = title,
                Url = url,
                ImageUrl = imageUrl,
                Duration = duration
            });
        }

        return movies;
    }

    public static void EnrichFromDetailPage(MovieCatalogEntry entry, string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var titleNode = doc.DocumentNode.SelectSingleNode("//h2[contains(@class,'heading-name')]");
        if (titleNode != null)
        {
            var title = HtmlEntity.DeEntitize(titleNode.InnerText).Trim();
            if (!string.IsNullOrWhiteSpace(title))
                entry.Title = title;
        }

        var description = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'description')]");
        if (description == null)
            description = doc.DocumentNode.SelectSingleNode("//div[@class='description']");

        entry.Genre = GetRowLineValue(doc, "Genre:");
        entry.Country = GetRowLineValue(doc, "Country:");

        var duration = GetRowLineValue(doc, "Duration:");
        if (!string.IsNullOrWhiteSpace(duration))
            entry.Duration = NormalizeDuration(duration);

        var year = GetRowLineValue(doc, "Released:");
        if (!string.IsNullOrWhiteSpace(year))
            entry.Year = year;

        var poster = doc.DocumentNode.SelectSingleNode("//img[contains(@class,'film-poster-img')]");
        if (poster != null)
        {
            var posterUrl = poster.GetAttributeValue("src", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(posterUrl))
                posterUrl = poster.GetAttributeValue("data-src", string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(posterUrl))
                entry.ImageUrl = MakeAbsoluteUrl(entry.Url, posterUrl);
        }
    }

    private static string GetRowLineValue(HtmlDocument doc, string label)
    {
        var row = doc.DocumentNode
            .SelectNodes("//div[contains(@class,'row-line')]")
            ?.FirstOrDefault(node => node.InnerText.Contains(label, StringComparison.OrdinalIgnoreCase));

        if (row == null)
            return string.Empty;

        var links = row.SelectNodes(".//a");
        if (links != null && links.Count > 0)
        {
            return string.Join(", ", links
                .Select(link => HtmlEntity.DeEntitize(link.InnerText).Trim())
                .Where(text => !string.IsNullOrWhiteSpace(text)));
        }

        var text = HtmlEntity.DeEntitize(row.InnerText).Trim();
        if (text.StartsWith(label, StringComparison.OrdinalIgnoreCase))
            text = text[label.Length..].Trim();

        return text;
    }

    private static string MakeAbsoluteUrl(string baseUrl, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return value;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            return value;

        if (Uri.TryCreate(baseUri, value, out var absolute))
            return absolute.ToString();

        return value;
    }
}
