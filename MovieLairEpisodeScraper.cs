using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyCinema;

public static class MovieLairEpisodeScraper
{
    public const string ReadSeasonsScript =
        """
        (() => {
            const select = document.querySelector('select.seasonSelect');
            if (!select)
                return '[]';

            const seasons = Array.from(select.querySelectorAll('option'))
                .map((option) => parseInt(option.value, 10))
                .filter((season) => Number.isFinite(season) && season > 0);

            return JSON.stringify(seasons);
        })()
        """;

    public const string ScrapeVisibleEpisodesScript =
        """
        (() => {
            const episodes = [];
            const seen = new Set();
            const showIdMatch = window.location.pathname.match(/\/watch-tv\/(\d+)/);
            const showId = showIdMatch ? showIdMatch[1] : null;
            const baseOrigin = window.location.origin;

            function normalizeUrl(href, season, episode) {
                if (href) {
                    try {
                        const parsed = new URL(href, window.location.href);
                        if (!parsed.pathname.includes('/watch-tv/'))
                            return '';

                        if (showId) {
                            const hrefShowId = parsed.pathname.match(/\/watch-tv\/(\d+)/)?.[1];
                            if (hrefShowId && hrefShowId !== showId)
                                return '';
                        }

                        return parsed.toString();
                    } catch {}
                }

                if (!showId)
                    return '';

                return `${baseOrigin}/watch-tv/${showId}?season=${season}&episode=${episode}`;
            }

            function readEpisodeTitle(anchor) {
                const cardText = anchor.querySelector('.card-text');
                if (cardText) {
                    const text = cardText.textContent.replace(/\s+/g, ' ').trim();
                    if (text)
                        return text;
                }

                const image = anchor.querySelector('img.episode, img.card-img-top, img');
                if (image) {
                    const alt = (image.getAttribute('alt') || '').trim();
                    if (alt)
                        return alt;
                }

                return '';
            }

            function addEpisode(season, episode, title, href) {
                const s = parseInt(season, 10);
                const e = parseInt(episode, 10);
                if (!Number.isFinite(s) || !Number.isFinite(e) || s <= 0 || e <= 0)
                    return;

                const key = `${s}-${e}`;
                if (seen.has(key))
                    return;

                seen.add(key);
                const url = normalizeUrl(href, s, e);
                if (!url)
                    return;

                episodes.push({
                    season: s,
                    episode: e,
                    title: (title || '').replace(/\s+/g, ' ').trim(),
                    movieLairUrl: url
                });
            }

            const root = document.querySelector('.seasons .episodes');
            if (!root)
                return JSON.stringify(episodes);

            root.querySelectorAll('a[href*="season="][href*="episode="]').forEach((anchor) => {
                try {
                    const parsed = new URL(anchor.href, window.location.href);
                    const season = parsed.searchParams.get('season');
                    const episode = parsed.searchParams.get('episode');
                    if (season && episode)
                        addEpisode(season, episode, readEpisodeTitle(anchor), parsed.toString());
                } catch {}
            });

            episodes.sort((a, b) => a.season - b.season || a.episode - b.episode);
            return JSON.stringify(episodes);
        })()
        """;

    public static string BuildSelectSeasonScript(int season) =>
        $$"""
        (() => {
            const select = document.querySelector('select.seasonSelect');
            if (!select)
                return 'missing';

            const target = '{{season}}';
            const setter = Object.getOwnPropertyDescriptor(window.HTMLSelectElement.prototype, 'value')?.set;
            if (setter)
                setter.call(select, target);
            else
                select.value = target;

            for (const option of select.options) {
                option.selected = option.value === target;
            }

            select.dispatchEvent(new Event('input', { bubbles: true }));
            select.dispatchEvent(new Event('change', { bubbles: true }));
            return select.value === target ? 'ok' : 'failed';
        })()
        """;

    public static List<int> ParseSeasonList(string scriptResult)
    {
        if (string.IsNullOrWhiteSpace(scriptResult) || scriptResult == "null")
            return [];

        try
        {
            var json = scriptResult.Trim();
            if (json.StartsWith('"') && json.EndsWith('"'))
                json = JsonSerializer.Deserialize<string>(json) ?? "[]";

            return JsonSerializer.Deserialize<List<int>>(json)?
                .Where(season => season > 0)
                .Distinct()
                .OrderBy(season => season)
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static List<TvEpisodeEntry> ParseScrapeResult(string scriptResult)
    {
        if (string.IsNullOrWhiteSpace(scriptResult) || scriptResult == "null")
            return [];

        List<ScrapedEpisode>? scraped;
        try
        {
            var json = scriptResult.Trim();
            if (json.StartsWith('"') && json.EndsWith('"'))
                json = JsonSerializer.Deserialize<string>(json) ?? string.Empty;

            scraped = JsonSerializer.Deserialize<List<ScrapedEpisode>>(json);
        }
        catch
        {
            return [];
        }

        if (scraped == null || scraped.Count == 0)
            return [];

        return scraped
            .Where(item => item.Season > 0 && item.Episode > 0 && !string.IsNullOrWhiteSpace(item.MovieLairUrl))
            .Select(item => new TvEpisodeEntry
            {
                Season = item.Season,
                Episode = item.Episode,
                Title = item.Title?.Trim() ?? string.Empty,
                MovieLairUrl = item.MovieLairUrl.Trim()
            })
            .ToList();
    }

    private sealed class ScrapedEpisode
    {
        [JsonPropertyName("season")]
        public int Season { get; set; }

        [JsonPropertyName("episode")]
        public int Episode { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("movieLairUrl")]
        public string MovieLairUrl { get; set; } = string.Empty;
    }
}
