namespace TinyCinema;

public enum MoviePlayerSource
{
    TinyZone,
    MovieLair
}

public static class MoviePlayerSourceExtensions
{
    public static string GetDisplayName(this MoviePlayerSource source) => source switch
    {
        MoviePlayerSource.MovieLair => "MovieLair",
        _ => "TinyZone"
    };

    public static MoviePlayerSource ParseDisplayName(string? value) =>
        string.Equals(value?.Trim(), "MovieLair", StringComparison.OrdinalIgnoreCase)
            ? MoviePlayerSource.MovieLair
            : MoviePlayerSource.TinyZone;
}
