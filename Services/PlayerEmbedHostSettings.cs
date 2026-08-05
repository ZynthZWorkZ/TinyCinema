using System.Text.Json;

namespace TinyCinema;

public static class PlayerEmbedHostSettings
{
    public const string SettingPrefix = "PlayerEmbedHosts=";

    public static readonly string[] DefaultHosts =
    [
        "cloudorchestranova.com",
        "cloudnestra.com",
        "vsembed.ru"
    ];

    public static IReadOnlyList<string> GetHosts()
    {
        var configured = SettingsWindow.GetPlayerEmbedHostsRaw();
        var merged = new List<string>(DefaultHosts);

        foreach (var host in ParseConfiguredHosts(configured))
        {
            if (!merged.Any(existing => existing.Equals(host, StringComparison.OrdinalIgnoreCase)))
                merged.Add(host);
        }

        return merged;
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

    public static string BuildFindEmbedIframeScript()
    {
        var hostsJson = JsonSerializer.Serialize(GetHosts());
        return $$"""
            (() => {
                const hosts = {{hostsJson}};

                function normalizeSrc(raw) {
                    const src = (raw || '').trim();
                    if (!src || src === 'about:blank')
                        return '';

                    try {
                        return src.startsWith('http') ? src : new URL(src, window.location.href).toString();
                    } catch {
                        return src;
                    }
                }

                function matchesKnownHost(url) {
                    return hosts.some((host) => url.includes(host));
                }

                function readNodeSrc(node) {
                    return normalizeSrc(node.getAttribute('src') || node.getAttribute('data-src') || '');
                }

                const primary = document.querySelector('#iframe-embed');
                if (primary) {
                    const src = readNodeSrc(primary);
                    if (src) {
                        if (matchesKnownHost(src))
                            return src;

                        if (src.startsWith('http') && !src.includes('movielair.cc'))
                            return src;
                    }
                }

                for (const host of hosts) {
                    const selectors = [
                        `iframe[src*="${host}"]`,
                        `iframe[data-src*="${host}"]`,
                        `embed[src*="${host}"]`
                    ];

                    for (const selector of selectors) {
                        for (const node of document.querySelectorAll(selector)) {
                            const src = readNodeSrc(node);
                            if (src && matchesKnownHost(src))
                                return src;
                        }
                    }
                }

                return '';
            })()
            """;
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
