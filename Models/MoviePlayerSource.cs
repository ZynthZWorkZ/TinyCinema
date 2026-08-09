namespace TinyCinema;

public enum MoviePlayerSource
{
    TinyZone,
    MovieLair,
    VidSrc
}

public static class MoviePlayerSourceExtensions
{
    public static string GetDisplayName(this MoviePlayerSource source) => source switch
    {
        MoviePlayerSource.MovieLair => "MovieLair",
        MoviePlayerSource.VidSrc => "VidSrc",
        _ => "TinyZone"
    };

    public static MoviePlayerSource ParseDisplayName(string? value)
    {
        if (string.Equals(value?.Trim(), "MovieLair", StringComparison.OrdinalIgnoreCase))
            return MoviePlayerSource.MovieLair;

        if (string.Equals(value?.Trim(), "VidSrc", StringComparison.OrdinalIgnoreCase))
            return MoviePlayerSource.VidSrc;

        return MoviePlayerSource.TinyZone;
    }
}
