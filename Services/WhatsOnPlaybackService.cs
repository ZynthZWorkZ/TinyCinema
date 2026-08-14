using System.Windows;
using System.Windows.Input;

namespace TinyCinema;

public static class WhatsOnPlaybackService
{
    public static async Task PlayAsync(
        Window owner,
        WhatsOnMovieEntry entry,
        Action<Movie, MoviePlayerSource?, string?> openPlayer)
    {
        if (entry.CatalogMovie != null)
        {
            openPlayer(entry.CatalogMovie, null, null);
            return;
        }

        var apiKey = SettingsWindow.GetTmdbApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show(
                "This title is not in your Movies.json catalog.\n\nAdd your TMDB API key in Settings to play it through VidSrc.",
                "TMDB API Key Required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var previousCursor = owner.Cursor;
        owner.Cursor = Cursors.Wait;

        try
        {
            var movieId = await TmdbClient.ResolveMovieIdAsync(entry.Item.Title, entry.Item.Year, apiKey);
            if (movieId is not > 0)
            {
                MessageBox.Show(
                    $"Could not find \"{entry.Item.Title}\" on TMDB for VidSrc playback.",
                    "Title Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var imdbId = await TmdbClient.GetMovieImdbIdAsync(movieId.Value, apiKey);
            var contentId = VidSrcEmbedBuilder.PickContentId(movieId, imdbId);
            if (string.IsNullOrWhiteSpace(contentId))
            {
                MessageBox.Show(
                    $"Could not resolve a VidSrc ID for \"{entry.Item.Title}\".",
                    "Playback Unavailable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var syntheticMovie = new Movie
            {
                Title = entry.Item.Title,
                Year = entry.Item.Year ?? string.Empty,
                Url = VidSrcEmbedBuilder.BuildMovieEmbedUrl(contentId),
                ImageUrl = entry.Item.Image ?? string.Empty,
                Genre = entry.Item.Genre ?? string.Empty,
                Duration = entry.Item.Runtime ?? string.Empty,
                Country = string.Empty,
                Description = entry.Item.Description ?? string.Empty,
                ContentType = CatalogContentType.Movie
            };

            openPlayer(syntheticMovie, MoviePlayerSource.VidSrc, contentId);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to start playback:\n{ex.Message}",
                "Playback Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            owner.Cursor = previousCursor;
        }
    }
}
