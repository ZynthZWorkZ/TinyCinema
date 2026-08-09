namespace TinyCinema;

public static class VidSrcEmbedBuilder
{
    public const string BaseUrl = "https://vsembed.ru";

    public static bool IsVidSrcUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        return Uri.TryCreate(url, UriKind.Absolute, out var absolute) &&
               absolute.Host.Contains("vsembed.ru", StringComparison.OrdinalIgnoreCase);
    }

    public static string? PickContentId(int? tmdbId, string? imdbId)
    {
        if (!string.IsNullOrWhiteSpace(imdbId))
            return imdbId.Trim();

        return tmdbId is > 0 ? tmdbId.Value.ToString() : null;
    }

    public static string BuildMovieEmbedUrl(string contentId)
    {
        var id = contentId.Trim().Trim('/');
        return $"{BaseUrl}/embed/movie/{id}";
    }

    public static string BuildTvEmbedUrl(string contentId, int season, int episode)
    {
        var id = contentId.Trim().Trim('/');
        return $"{BaseUrl}/embed/tv/{id}/{season}/{episode}";
    }

    public static string BuildTvSeriesEmbedUrl(string contentId) =>
        $"{BaseUrl}/embed/tv/{contentId.Trim().Trim('/')}";
}
