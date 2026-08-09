namespace TinyCinema;

public sealed class TvShowCatalogFile
{
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    public List<TvShowCatalogRecord> Shows { get; set; } = [];
}
