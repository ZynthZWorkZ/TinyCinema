using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace TinyCinema;

public partial class WatchedHomePanel : UserControl
{
    private readonly ObservableCollection<WatchedCardViewModel> _cards = [];
    private Movie? _selectedMovie;
    private WatchedContentFilter _contentFilter = WatchedContentFilter.All;
    private string _searchText = string.Empty;
    private Dictionary<string, Movie> _catalogByUrl = new(StringComparer.Ordinal);

    public event EventHandler<Movie>? MovieSelected;
    public event EventHandler<Movie>? WatchedToggleRequested;

    public Movie? SelectedMovie => _selectedMovie;

    public WatchedHomePanel()
    {
        InitializeComponent();
        WatchedItemsControl.ItemsSource = _cards;
        Loaded += (_, _) => UpdateFilterVisuals();
    }

    public void Refresh(IEnumerable<Movie> catalog, string searchText)
    {
        _searchText = searchText.Trim();
        _catalogByUrl = catalog
            .GroupBy(movie => movie.Url, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        RefreshStats();
        RebuildCards();
    }

    private void RefreshStats()
    {
        var (total, movies, tvShows) = WatchedStore.GetStats();
        WatchedCountText.Text = total switch
        {
            0 => "No titles yet",
            1 => "1 title watched",
            _ => $"{total} titles watched"
        };
        MoviesStatText.Text = movies.ToString(CultureInfo.InvariantCulture);
        TvShowsStatText.Text = tvShows.ToString(CultureInfo.InvariantCulture);
    }

    private void RebuildCards()
    {
        _cards.Clear();

        foreach (var entry in WatchedStore.GetAllEntries())
        {
            if (!MatchesFilter(entry))
                continue;

            var movie = WatchedStore.ResolveMovie(entry, _catalogByUrl);
            if (!MatchesSearch(movie))
                continue;

            _cards.Add(new WatchedCardViewModel(movie));
            _ = movie.LoadImageAsync();
        }

        var hasItems = _cards.Count > 0;
        WatchedItemsControl.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        EmptyStatePanel.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;

        if (hasItems)
            SelectMovie(_cards[0].Movie, notify: _selectedMovie == null);
        else
            _selectedMovie = null;
    }

    private bool MatchesFilter(WatchedEntry entry) => _contentFilter switch
    {
        WatchedContentFilter.Movies => entry.ContentType == CatalogContentType.Movie,
        WatchedContentFilter.TvShows => entry.ContentType == CatalogContentType.TvShow,
        _ => true
    };

    private bool MatchesSearch(Movie movie) =>
        HybridSearchService.MatchesExtended(movie, _searchText);

    public void SelectMovie(Movie movie, bool notify = false)
    {
        _selectedMovie = movie;
        foreach (var card in _cards)
            card.IsSelected = ReferenceEquals(card.Movie, movie);

        if (notify)
            MovieSelected?.Invoke(this, movie);
    }

    public void NotifyWatchedChanged(Movie movie)
    {
        RefreshStats();

        if (!movie.IsWatched)
        {
            var removed = _cards.FirstOrDefault(card => ReferenceEquals(card.Movie, movie));
            if (removed != null)
                _cards.Remove(removed);

            if (_cards.Count == 0)
            {
                _selectedMovie = null;
                EmptyStatePanel.Visibility = Visibility.Visible;
                WatchedItemsControl.Visibility = Visibility.Collapsed;
                return;
            }

            if (ReferenceEquals(_selectedMovie, movie))
                SelectMovie(_cards[0].Movie, notify: true);

            return;
        }

        if (_cards.Any(card => ReferenceEquals(card.Movie, movie)))
            return;

        if (!MatchesFilter(WatchedEntry.FromMovie(movie, movie.WatchedAtUtc ?? DateTime.UtcNow)))
            return;

        if (!MatchesSearch(movie))
            return;

        var card = new WatchedCardViewModel(movie) { IsSelected = ReferenceEquals(_selectedMovie, movie) };
        _cards.Insert(0, card);
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        WatchedItemsControl.Visibility = Visibility.Visible;
        _ = movie.LoadImageAsync();
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string tag)
            return;

        if (!Enum.TryParse<WatchedContentFilter>(tag, out var filter))
            return;

        _contentFilter = filter;
        UpdateFilterVisuals();
        RebuildCards();
    }

    private void UpdateFilterVisuals()
    {
        var activeBrush = Application.Current.Resources["AccentBrush"] as SolidColorBrush
            ?? new SolidColorBrush(Color.FromRgb(220, 38, 38));
        var inactiveForeground = new SolidColorBrush(Color.FromRgb(179, 179, 179));
        var inactiveBackground = new SolidColorBrush(Color.FromRgb(42, 42, 42));
        var inactiveBorder = new SolidColorBrush(Color.FromRgb(58, 58, 58));
        var activeBackground = new SolidColorBrush(Color.FromArgb(51, 26, 39, 68));

        void StyleFilter(Button button, WatchedContentFilter filter)
        {
            var isActive = _contentFilter == filter;
            button.Background = isActive ? activeBackground : inactiveBackground;
            button.BorderBrush = isActive ? activeBrush : inactiveBorder;
            button.Foreground = isActive ? activeBrush : inactiveForeground;
        }

        StyleFilter(AllFilterButton, WatchedContentFilter.All);
        StyleFilter(MoviesFilterButton, WatchedContentFilter.Movies);
        StyleFilter(TvShowsFilterButton, WatchedContentFilter.TvShows);
    }

    private void CardButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WatchedCardViewModel card })
            return;

        SelectMovie(card.Movie, notify: true);
    }

    private void UnwatchButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: WatchedCardViewModel card })
            return;

        WatchedToggleRequested?.Invoke(this, card.Movie);
    }

    private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }
    }
}

public enum WatchedContentFilter
{
    All,
    Movies,
    TvShows
}

public sealed class WatchedCardViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public WatchedCardViewModel(Movie movie)
    {
        Movie = movie;
    }

    public Movie Movie { get; }

    public string WatchedDateLabel
    {
        get
        {
            if (Movie.WatchedAtUtc is not DateTime watchedAt)
                return "Watched";

            var local = watchedAt.ToLocalTime();
            return local.Date == DateTime.Today
                ? "Watched today"
                : $"Watched {local:MMM d, yyyy}";
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class WatchedIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isWatched = value is bool watched && watched;
        return isWatched
            ? FontAwesome.WPF.FontAwesomeIcon.Eye
            : FontAwesome.WPF.FontAwesomeIcon.EyeSlash;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class WatchedColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isWatched = value is bool watched && watched;
        return new SolidColorBrush(isWatched ? Color.FromRgb(16, 185, 129) : Colors.White);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class WatchedCardBorderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isSelected = value is bool selected && selected;
        return new SolidColorBrush(isSelected ? Color.FromRgb(220, 38, 38) : Color.FromRgb(51, 65, 85));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
