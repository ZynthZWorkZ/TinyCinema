using System.Text.RegularExpressions;

namespace TinyCinema;

public static class CatalogPlaybackHelper
{
    public static MoviePlayerSource GetEffectivePlaybackSource(Movie movie)
    {
        var parsed = MoviePlayerSourceExtensions.ParseDisplayName(movie.CatalogPlaybackSource);
        if (parsed != MoviePlayerSource.TinyZone)
            return parsed;

        if (VidSrcEmbedBuilder.IsVidSrcUrl(movie.Url))
            return MoviePlayerSource.VidSrc;

        if (movie.Url.Contains("movielair.cc/watch-movie", StringComparison.OrdinalIgnoreCase))
            return MoviePlayerSource.MovieLair;

        return MoviePlayerSource.TinyZone;
    }

    public static string? ResolveVidSrcContentId(Movie movie)
    {
        if (movie.TmdbId is > 0)
            return movie.TmdbId.Value.ToString();

        if (!VidSrcEmbedBuilder.IsVidSrcUrl(movie.Url))
            return null;

        var match = Regex.Match(movie.Url, @"/embed/movie/([^/?#]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static (MoviePlayerSource? Source, string? VidSrcContentId) ResolvePlaybackArgs(Movie movie)
    {
        if (movie.IsTvShow)
            return (null, null);

        var source = GetEffectivePlaybackSource(movie);
        if (source == MoviePlayerSource.TinyZone)
            return (null, null);

        return (source, source == MoviePlayerSource.VidSrc ? ResolveVidSrcContentId(movie) : null);
    }
}
