namespace TinyCinema;

public static class TvEmbedResolver
{
    public const string EmbedHost = "cloudorchestranova.com";

    public static string BuildFindEmbedIframeScript() =>
        PlayerEmbedHostSettings.BuildFindEmbedIframeScript();

    public static bool IsEmbedPageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!PlayerEmbedHostSettings.GetHosts().Any(marker => url.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (M3U8Capture.IsStreamUrl(url))
            return false;

        if (url.Contains(".js", StringComparison.OrdinalIgnoreCase) ||
            url.Contains(".css", StringComparison.OrdinalIgnoreCase) ||
            url.Contains(".png", StringComparison.OrdinalIgnoreCase) ||
            url.Contains(".jpg", StringComparison.OrdinalIgnoreCase) ||
            url.Contains(".woff", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public static bool IsMovieLairWatchUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var absolute))
            return false;

        return absolute.Host.Contains("movielair", StringComparison.OrdinalIgnoreCase) &&
               absolute.AbsolutePath.Contains("/watch-tv/", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldPlayInlineOnMovieLair(string? url) => IsMovieLairWatchUrl(url);

    public static bool IsExternalPlayerPageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (IsMovieLairWatchUrl(url) || IsEmbedPageUrl(url))
            return true;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var absolute))
            return false;

        if (absolute.Scheme is not ("http" or "https"))
            return false;

        if (absolute.Host.Contains("movielair", StringComparison.OrdinalIgnoreCase))
            return false;

        if (M3U8Capture.IsStreamUrl(url))
            return false;

        if (url.Contains(".js", StringComparison.OrdinalIgnoreCase) ||
            url.Contains(".css", StringComparison.OrdinalIgnoreCase) ||
            url.Contains(".png", StringComparison.OrdinalIgnoreCase) ||
            url.Contains(".jpg", StringComparison.OrdinalIgnoreCase) ||
            url.Contains(".woff", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
