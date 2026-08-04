using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyCinema;

public static class MovieLairTvServerSelector
{
    public const string HasServerButtonsScript =
        """
        (() => {
            const group = document.querySelector('.btn-group[aria-label="Server Select"]');
            return !!(group && group.querySelectorAll('button').length > 0);
        })()
        """;

    public const string ListServersScript =
        """
        (() => {
            const group = document.querySelector('.btn-group[aria-label="Server Select"]');
            if (!group)
                return '[]';

            const buttons = Array.from(group.querySelectorAll('button'));
            return JSON.stringify(buttons.map((btn, i) => ({
                index: i + 1,
                label: (btn.textContent || '').trim().replace(/\s+/g, ' '),
                active: btn.classList.contains('active')
            })));
        })()
        """;

    public static string BuildSelectServerScript(int serverIndex)
    {
        return $$"""
            (() => {
                const targetIndex = {{serverIndex}} - 1;
                const group = document.querySelector('.btn-group[aria-label="Server Select"]');
                if (!group)
                    return JSON.stringify({ ok: false, reason: 'no-group' });

                const buttons = Array.from(group.querySelectorAll('button'));
                if (targetIndex < 0 || targetIndex >= buttons.length)
                    return JSON.stringify({ ok: false, reason: 'bad-index', count: buttons.length });

                const btn = buttons[targetIndex];
                if (!btn.disabled)
                    btn.click();

                return JSON.stringify({ ok: true, count: buttons.length, selected: targetIndex + 1 });
            })()
            """;
    }

    public static List<TvServerInfo> ParseServerList(string scriptResult)
    {
        if (string.IsNullOrWhiteSpace(scriptResult) || scriptResult == "null")
            return [];

        try
        {
            var json = scriptResult.Trim();
            if (json.StartsWith('"') && json.EndsWith('"'))
                json = JsonSerializer.Deserialize<string>(json) ?? "[]";

            return JsonSerializer.Deserialize<List<TvServerInfo>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public sealed class TvServerInfo
    {
        [JsonPropertyName("index")]
        public int Index { get; init; }

        [JsonPropertyName("label")]
        public string Label { get; init; } = string.Empty;

        [JsonPropertyName("active")]
        public bool Active { get; init; }
    }
}
