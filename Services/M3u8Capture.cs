namespace TinyCinema;

public static class M3U8Capture
{
    public static bool IsStreamUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        return url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("application/vnd.apple.mpegurl", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("/hls/", StringComparison.OrdinalIgnoreCase);
    }

    public static string PickBestUrl(IEnumerable<string> urls)
    {
        return urls
            .Where(IsStreamUrl)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(url => url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(url => url.Length)
            .FirstOrDefault() ?? string.Empty;
    }
}
