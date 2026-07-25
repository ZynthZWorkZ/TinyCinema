using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace TinyCinema;

public sealed class IptvPlaylistService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TinyCinema",
        "IptvCache");

    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);

    public async Task<IReadOnlyList<IptvChannel>> LoadChannelsAsync(
        IptvCategory category,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && TryLoadFromCache(category.Slug, out var cached))
            return cached;

        var request = new HttpRequestMessage(HttpMethod.Get, category.PlaylistUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", "TinyCinema/1.0");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var channels = M3uPlaylistParser.Parse(content, category);
        SaveToCache(category.Slug, channels);
        return channels;
    }

    private static bool TryLoadFromCache(string slug, out IReadOnlyList<IptvChannel> channels)
    {
        channels = Array.Empty<IptvChannel>();
        var path = GetCachePath(slug);
        if (!File.Exists(path))
            return false;

        try
        {
            var json = File.ReadAllText(path);
            var cached = JsonSerializer.Deserialize<CachedIptvPlaylist>(json);
            if (cached?.Channels == null || cached.CachedAtUtc == default)
                return false;

            if (DateTime.UtcNow - cached.CachedAtUtc > CacheLifetime)
                return false;

            channels = cached.Channels
                .Select(dto => dto.ToChannel())
                .ToList();
            return channels.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void SaveToCache(string slug, IReadOnlyList<IptvChannel> channels)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var payload = new CachedIptvPlaylist
            {
                CachedAtUtc = DateTime.UtcNow,
                Channels = channels.Select(CachedIptvChannel.FromChannel).ToList()
            };

            var json = JsonSerializer.Serialize(payload);
            File.WriteAllText(GetCachePath(slug), json);
        }
        catch
        {
            // Cache is optional.
        }
    }

    private static string GetCachePath(string slug) =>
        Path.Combine(CacheDirectory, $"{slug}.json");

    private sealed class CachedIptvPlaylist
    {
        public DateTime CachedAtUtc { get; set; }

        public List<CachedIptvChannel> Channels { get; set; } = [];
    }

    private sealed class CachedIptvChannel
    {
        public string Name { get; set; } = string.Empty;

        public string StreamUrl { get; set; } = string.Empty;

        public string? LogoUrl { get; set; }

        public string? GroupTitle { get; set; }

        public string? TvgId { get; set; }

        public string CategoryName { get; set; } = string.Empty;

        public string CategorySlug { get; set; } = string.Empty;

        public bool IsGeoBlocked { get; set; }

        public bool IsNot247 { get; set; }

        public static CachedIptvChannel FromChannel(IptvChannel channel) => new()
        {
            Name = channel.Name,
            StreamUrl = channel.StreamUrl,
            LogoUrl = channel.LogoUrl,
            GroupTitle = channel.GroupTitle,
            TvgId = channel.TvgId,
            CategoryName = channel.CategoryName,
            CategorySlug = channel.CategorySlug,
            IsGeoBlocked = channel.IsGeoBlocked,
            IsNot247 = channel.IsNot247
        };

        public IptvChannel ToChannel() => new()
        {
            Name = Name,
            StreamUrl = StreamUrl,
            LogoUrl = LogoUrl,
            GroupTitle = GroupTitle,
            TvgId = TvgId,
            CategoryName = CategoryName,
            CategorySlug = CategorySlug,
            IsGeoBlocked = IsGeoBlocked,
            IsNot247 = IsNot247
        };
    }
}
