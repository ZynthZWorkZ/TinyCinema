using System.Text.RegularExpressions;

namespace TinyCinema;

public static class M3uPlaylistParser
{
    private static readonly Regex AttributeRegex = new(
        @"([\w-]+)=""([^""]*)""",
        RegexOptions.Compiled);

    public static IReadOnlyList<IptvChannel> Parse(string content, IptvCategory category)
    {
        var channels = new List<IptvChannel>();
        IptvChannel? pending = null;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim('\r', ' ', '\t');
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                pending = ParseExtInf(line, category);
                continue;
            }

            if (line.StartsWith('#') || pending == null)
                continue;

            pending = FinishChannel(pending, ResolveStreamUrl(line, category.PlaylistUrl));
            channels.Add(pending);
            pending = null;
        }

        return channels;
    }

    private static IptvChannel ParseExtInf(string line, IptvCategory category)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AttributeRegex.Matches(line))
            attributes[match.Groups[1].Value] = match.Groups[2].Value;

        var commaIndex = line.LastIndexOf(',');
        var rawName = commaIndex >= 0 ? line[(commaIndex + 1)..].Trim() : "Unknown Channel";
        var isGeoBlocked = rawName.Contains("[Geo-blocked]", StringComparison.OrdinalIgnoreCase);
        var isNot247 = rawName.Contains("[Not 24/7]", StringComparison.OrdinalIgnoreCase);
        var name = rawName
            .Replace("[Geo-blocked]", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("[Not 24/7]", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        attributes.TryGetValue("tvg-logo", out var logoUrl);
        attributes.TryGetValue("group-title", out var groupTitle);
        attributes.TryGetValue("tvg-id", out var tvgId);

        return new IptvChannel
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Unknown Channel" : name,
            StreamUrl = string.Empty,
            LogoUrl = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim(),
            GroupTitle = string.IsNullOrWhiteSpace(groupTitle) ? null : groupTitle.Trim(),
            TvgId = string.IsNullOrWhiteSpace(tvgId) ? null : tvgId.Trim(),
            CategoryName = category.Name,
            CategorySlug = category.Slug,
            IsGeoBlocked = isGeoBlocked,
            IsNot247 = isNot247
        };
    }

    private static IptvChannel FinishChannel(IptvChannel channel, string streamUrl) =>
        new()
        {
            Name = channel.Name,
            StreamUrl = streamUrl,
            LogoUrl = channel.LogoUrl,
            GroupTitle = channel.GroupTitle,
            TvgId = channel.TvgId,
            CategoryName = channel.CategoryName,
            CategorySlug = channel.CategorySlug,
            IsGeoBlocked = channel.IsGeoBlocked,
            IsNot247 = channel.IsNot247
        };

    private static string ResolveStreamUrl(string line, string playlistUrl)
    {
        var url = line.Trim();
        if (Uri.TryCreate(url, UriKind.Absolute, out _))
            return url;

        if (!Uri.TryCreate(playlistUrl, UriKind.Absolute, out var playlistUri))
            return url;

        if (Uri.TryCreate(playlistUri, url, out var resolved))
            return resolved.ToString();

        return url;
    }
}
