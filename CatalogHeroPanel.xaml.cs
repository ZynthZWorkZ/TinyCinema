using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FontAwesome.WPF;

namespace TinyCinema;

public partial class CatalogHeroPanel : UserControl
{
    public Movie? CurrentMovie { get; private set; }

    public event RoutedEventHandler? PlayRequested;
    public event RoutedEventHandler? ContinueRequested;
    public event RoutedEventHandler? FavoriteRequested;
    public event RoutedEventHandler? TrailerRequested;
    public event RoutedEventHandler? OpeningCreditsRequested;
    public event RoutedEventHandler? InfoRequested;
    public event RoutedEventHandler? UrlRequested;
    public event RoutedEventHandler? RokuRequested;

    public CatalogHeroPanel()
    {
        InitializeComponent();
    }

    public void SetMovie(Movie? movie)
    {
        CurrentMovie = movie;

        if (movie == null)
        {
            ShowEmptyState("Select a title to see details");
            return;
        }

        EmptyStatePanel.Visibility = Visibility.Collapsed;
        ContentPanel.Visibility = Visibility.Visible;

        TitleText.Text = movie.Title;
        YearText.Text = movie.Year;
        TypeText.Text = movie.ContentTypeLabel;
        GenreText.Text = FormatMetadataValue(movie.Genre);
        DurationText.Text = FormatDuration(movie);
        CountryText.Text = FormatCountry(movie.Country);

        GenreText.Visibility = string.IsNullOrEmpty(GenreText.Text) ? Visibility.Collapsed : Visibility.Visible;
        DurationText.Visibility = string.IsNullOrEmpty(DurationText.Text) ? Visibility.Collapsed : Visibility.Visible;
        CountryText.Visibility = string.IsNullOrEmpty(CountryText.Text) ? Visibility.Collapsed : Visibility.Visible;

        InfoButtonLabel.Text = movie.IsTvShow ? "TV Details" : "More Info";
        TrailerButton.Visibility = movie.IsTvShow ? Visibility.Collapsed : Visibility.Visible;
        OpeningCreditsButton.Visibility = movie.IsTvShow ? Visibility.Visible : Visibility.Collapsed;

        UpdateFavoriteVisual(movie.IsFavorite);
        SetContinueState(movie.IsTvShow ? TvShowWatchHistory.TryGet(movie.Url) : null);
        UpdateBackdrop(movie);
    }

    public void SetDescription(string description)
    {
        DescriptionLoadingPanel.Visibility = Visibility.Collapsed;
        DescriptionText.Visibility = Visibility.Visible;

        if (string.IsNullOrWhiteSpace(description))
        {
            DescriptionText.Text = "No description available.";
            DescriptionText.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            return;
        }

        DescriptionText.Text = description;
        DescriptionText.Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225));
    }

    public void SetDescriptionLoading(bool isLoading)
    {
        if (isLoading)
        {
            DescriptionLoadingPanel.Visibility = Visibility.Visible;
            DescriptionText.Visibility = Visibility.Collapsed;
            DescriptionText.Text = string.Empty;
        }
        else
        {
            DescriptionLoadingPanel.Visibility = Visibility.Collapsed;
            DescriptionText.Visibility = Visibility.Visible;
        }
    }

    public void ShowEmptyState(string message)
    {
        CurrentMovie = null;
        EmptyStatePanel.Visibility = Visibility.Visible;
        ContentPanel.Visibility = Visibility.Collapsed;
        EmptyStateText.Text = message;
        BackdropImage.Source = null;
        SetDescriptionLoading(false);
        DescriptionText.Text = string.Empty;
        SetContinueState(null);
    }

    public TvWatchHistoryEntry? ContinueHistory { get; private set; }

    public void SetContinueState(TvWatchHistoryEntry? history)
    {
        ContinueHistory = history;

        if (history == null || CurrentMovie?.IsTvShow != true)
        {
            ContinueButton.Visibility = Visibility.Collapsed;
            return;
        }

        ContinueButton.Visibility = Visibility.Visible;
        ContinueLabel.Text = $"Continue · {history.DisplayLabel}";
    }

    public void UpdateFavoriteVisual(bool isFavorite)
    {
        FavoriteIcon.Icon = isFavorite ? FontAwesomeIcon.Heart : FontAwesomeIcon.HeartOutline;
        FavoriteIcon.Foreground = new SolidColorBrush(
            isFavorite ? Color.FromRgb(220, 38, 38) : Colors.White);
        FavoriteLabel.Text = isFavorite ? "Favorited" : "Favorite";
    }

    private void UpdateBackdrop(Movie movie)
    {
        if (movie.CachedImage != null)
        {
            BackdropImage.Source = movie.CachedImage;
            return;
        }

        if (!string.IsNullOrWhiteSpace(movie.ImageUrl))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(movie.ImageUrl, UriKind.Absolute);
                bitmap.EndInit();
                BackdropImage.Source = bitmap;
                return;
            }
            catch
            {
                // Fall through to placeholder.
            }
        }

        BackdropImage.Source = null;
    }

    private static string FormatMetadataValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return value.Trim();
    }

    private static string FormatCountry(string? country)
    {
        var value = FormatMetadataValue(country);
        return string.IsNullOrEmpty(value) ? string.Empty : value;
    }

    private static string FormatDuration(Movie movie)
    {
        var value = FormatMetadataValue(movie.Duration);
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (movie.IsTvShow && !value.Contains("season", StringComparison.OrdinalIgnoreCase))
            return $"{value} seasons";

        return value;
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e) => PlayRequested?.Invoke(this, e);
    private void ContinueButton_Click(object sender, RoutedEventArgs e) => ContinueRequested?.Invoke(this, e);
    private void FavoriteButton_Click(object sender, RoutedEventArgs e) => FavoriteRequested?.Invoke(this, e);
    private void TrailerButton_Click(object sender, RoutedEventArgs e) => TrailerRequested?.Invoke(this, e);
    private void OpeningCreditsButton_Click(object sender, RoutedEventArgs e) => OpeningCreditsRequested?.Invoke(this, e);
    private void InfoButton_Click(object sender, RoutedEventArgs e) => InfoRequested?.Invoke(this, e);
    private void UrlButton_Click(object sender, RoutedEventArgs e) => UrlRequested?.Invoke(this, e);
    private void RokuButton_Click(object sender, RoutedEventArgs e) => RokuRequested?.Invoke(this, e);
}
