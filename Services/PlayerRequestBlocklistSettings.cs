namespace TinyCinema;

public static class PlayerRequestBlocklistSettings
{
    public const string SettingPrefix = "PlayerRequestBlocklist=";

    public static readonly string[] DefaultBlockedHosts =
    [
        "llvpn.com",
        "usrpubtrk.com",
        "static.cloudflareinsights.com"
    ];

    public static readonly string[] DefaultBlockedPathPatterns =
    [
        "/tag.min.js",
        "/btag.min.js",
        "/ut/hb.php"
    ];

    private static readonly string[] DefaultAllowedHostMarkers =
    [
        "movielair",
        "tinyzone"
    ];

    private static string[] _cachedBlockedHosts = DefaultBlockedHosts;

    public static void RefreshCache()
    {
        _cachedBlockedHosts = GetBlockedHosts().ToArray();
    }

    public static IReadOnlyList<string> GetBlockedHosts()
    {
        var configured = SettingsWindow.GetPlayerRequestBlocklistRaw();
        var merged = new List<string>(DefaultBlockedHosts);

        foreach (var host in ParseConfiguredHosts(configured))
        {
            if (!merged.Any(existing => existing.Equals(host, StringComparison.OrdinalIgnoreCase)))
                merged.Add(host);
        }

        return merged;
    }

    public static bool ShouldBlock(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (M3U8Capture.IsStreamUrl(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var absolute))
            return false;

        if (absolute.Scheme is not ("http" or "https"))
            return false;

        var host = absolute.Host;

        if (IsAllowedHost(host))
            return _cachedBlockedHosts.Any(marker => HostMatches(host, marker));

        if (_cachedBlockedHosts.Any(marker => HostMatches(host, marker) || url.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return true;

        return DefaultBlockedPathPatterns.Any(pattern =>
            url.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    public static string FormatForDisplay(string? storedValue)
    {
        if (string.IsNullOrWhiteSpace(storedValue))
            return "";

        return string.Join(
            Environment.NewLine,
            ParseConfiguredHosts(storedValue));
    }

    public static string FormatForStorage(string? displayValue)
    {
        var hosts = ParseConfiguredHosts(displayValue).ToList();
        return hosts.Count == 0 ? "" : string.Join("|", hosts);
    }

    private static bool IsAllowedHost(string host)
    {
        if (DefaultAllowedHostMarkers.Any(marker => host.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return true;

        return PlayerEmbedHostSettings.GetHosts().Any(marker => HostMatches(host, marker));
    }

    private static bool HostMatches(string host, string marker)
    {
        return host.Equals(marker, StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith("." + marker, StringComparison.OrdinalIgnoreCase) ||
               host.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ParseConfiguredHosts(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        foreach (var part in value.Split(['|', ',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var normalized = NormalizeHost(part);
            if (!string.IsNullOrEmpty(normalized))
                yield return normalized;
        }
    }

    private static string NormalizeHost(string raw)
    {
        var trimmed = raw.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return "";

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
            return absolute.Host;

        if (trimmed.StartsWith("//", StringComparison.Ordinal))
            trimmed = trimmed[2..];

        var slashIndex = trimmed.IndexOf('/');
        if (slashIndex >= 0)
            trimmed = trimmed[..slashIndex];

        return trimmed.Trim().TrimStart('.');
    }
}
