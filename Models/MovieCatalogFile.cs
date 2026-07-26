namespace TinyCinema;

public sealed class MovieCatalogFile
{
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    public List<MovieCatalogRecord> Movies { get; set; } = [];
}
