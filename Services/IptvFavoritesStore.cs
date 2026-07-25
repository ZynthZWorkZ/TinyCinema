using System.IO;
using System.Text.Json;

namespace TinyCinema;

public static class IptvFavoritesStore
{
    private static readonly string FavoritesFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TinyCinema",
        "iptv-favorites.json");

    private static Dictionary<string, IptvChannel>? _cache;

    public static IReadOnlyList<IptvChannel> GetFavoriteChannels()
    {
        EnsureLoaded();
        return _cache!.Values.OrderBy(channel => channel.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool IsFavorite(string uniqueId)
    {
        EnsureLoaded();
        return _cache!.ContainsKey(uniqueId);
    }

    public static void SetFavorite(IptvChannel channel, bool isFavorite)
    {
        EnsureLoaded();
        if (isFavorite)
            _cache![channel.UniqueId] = Clone(channel);
        else
            _cache!.Remove(channel.UniqueId);

        Save();
    }

    public static void ApplyFavorites(IEnumerable<IptvChannel> channels)
    {
        EnsureLoaded();
        foreach (var channel in channels)
            channel.IsFavorite = _cache!.ContainsKey(channel.UniqueId);
    }

    private static IptvChannel Clone(IptvChannel channel) => new()
    {
        Name = channel.Name,
        StreamUrl = channel.StreamUrl,
        LogoUrl = channel.LogoUrl,
        GroupTitle = channel.GroupTitle,
        TvgId = channel.TvgId,
        CategoryName = channel.CategoryName,
        CategorySlug = channel.CategorySlug,
        IsGeoBlocked = channel.IsGeoBlocked,
        IsNot247 = channel.IsNot247,
        IsFavorite = true
    };

    private static void EnsureLoaded()
    {
        if (_cache != null)
            return;

        _cache = new Dictionary<string, IptvChannel>(StringComparer.Ordinal);
        if (!File.Exists(FavoritesFile))
            return;

        try
        {
            var json = File.ReadAllText(FavoritesFile);
            var entries = JsonSerializer.Deserialize<List<CachedIptvChannel>>(json);
            if (entries == null)
                return;

            foreach (var entry in entries)
            {
                var channel = entry.ToChannel();
                _cache[channel.UniqueId] = channel;
            }
        }
        catch
        {
            _cache = new Dictionary<string, IptvChannel>(StringComparer.Ordinal);
        }
    }

    private static void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(FavoritesFile);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var entries = _cache!.Values
                .Select(CachedIptvChannel.FromChannel)
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var json = JsonSerializer.Serialize(entries);
            File.WriteAllText(FavoritesFile, json);
        }
        catch
        {
            // Ignore persistence errors.
        }
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
            IsNot247 = IsNot247,
            IsFavorite = true
        };
    }
}
