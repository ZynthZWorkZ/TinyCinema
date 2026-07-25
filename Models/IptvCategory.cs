using System.Windows.Media;
using FontAwesome.WPF;

namespace TinyCinema;

public sealed class IptvCategory
{
    public required string Name { get; init; }

    public required string Slug { get; init; }

    public int ListedChannelCount { get; init; }

    public required string PlaylistUrl { get; init; }

    public bool IsFavorites => string.Equals(Slug, "__favorites__", StringComparison.Ordinal);

    public FontAwesomeIcon CategoryIcon => IptvCategoryVisuals.GetIcon(Slug);

    public Brush AccentBrush => IptvCategoryVisuals.GetAccentBrush(Slug);

    public string DisplayLabel => ListedChannelCount > 0
        ? $"{Name} ({ListedChannelCount:N0})"
        : Name;
}
