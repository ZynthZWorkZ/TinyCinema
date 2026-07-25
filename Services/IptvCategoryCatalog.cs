namespace TinyCinema;

public static class IptvCategoryCatalog
{
    private static readonly IptvCategory[] Categories =
    [
        Category("Animation", 172),
        Category("Auto", 30),
        Category("Business", 81),
        Category("Classic", 111),
        Category("Comedy", 223),
        Category("Cooking", 54),
        Category("Culture", 193),
        Category("Documentary", 242),
        Category("Education", 249),
        Category("Entertainment", 925),
        Category("Family", 62),
        Category("General", 2860),
        Category("Interactive", 1),
        Category("Kids", 398),
        Category("Legislative", 195),
        Category("Lifestyle", 137),
        Category("Movies", 776),
        Category("Music", 781),
        Category("News", 1020),
        Category("Outdoor", 75),
        Category("Public", 39),
        Category("Relax", 9),
        Category("Religious", 782),
        Category("Science", 22),
        Category("Series", 560),
        Category("Shop", 82),
        Category("Sports", 522),
        Category("Travel", 55),
        Category("Weather", 19),
        Category("Undefined", 3631)
    ];

    public static IReadOnlyList<IptvCategory> GetAll() => Categories;

    public static IptvCategory? TryGetBySlug(string slug) =>
        Categories.FirstOrDefault(category =>
            string.Equals(category.Slug, slug, StringComparison.OrdinalIgnoreCase));

    private static IptvCategory Category(string name, int count)
    {
        var slug = name.ToLowerInvariant();
        return new IptvCategory
        {
            Name = name,
            Slug = slug,
            ListedChannelCount = count,
            PlaylistUrl = $"https://iptv-org.github.io/iptv/categories/{slug}.m3u"
        };
    }
}
