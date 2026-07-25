namespace TinyCinema;

public static class TvEmbedResolver
{
    public const string EmbedHost = "cloudorchestranova.com";

    public const string FindEmbedIframeScript =
        """
        (() => {
            const selectors = [
                'iframe[src*="cloudorchestranova"]',
                'iframe[data-src*="cloudorchestranova"]',
                'embed[src*="cloudorchestranova"]'
            ];

            for (const selector of selectors) {
                for (const node of document.querySelectorAll(selector)) {
                    const src = node.getAttribute('src') || node.getAttribute('data-src') || '';
                    if (src.includes('cloudorchestranova'))
                        return src.startsWith('http') ? src : new URL(src, window.location.href).toString();
                }
            }

            return '';
        })()
        """;

    public static bool IsEmbedPageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!url.Contains(EmbedHost, StringComparison.OrdinalIgnoreCase))
            return false;

        if (M3U8Capture.IsStreamUrl(url))
            return false;

        if (url.Contains(".js", StringComparison.OrdinalIgnoreCase) ||
            url.Contains(".css", StringComparison.OrdinalIgnoreCase) ||
            url.Contains(".png", StringComparison.OrdinalIgnoreCase) ||
            url.Contains(".jpg", StringComparison.OrdinalIgnoreCase) ||
            url.Contains(".woff", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
