using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Input;
using System.Text.RegularExpressions;
using System.Windows.Media.Animation;
using System.Net;
using System.Net.Http;
using System.IO.Compression;
using System.Windows.Media.Imaging;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using Serilog;
using System.Threading;
using HtmlAgilityPack;

namespace TinyCinema;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const int BatchSize = 50;
    private readonly ObservableCollection<Movie> _movies;
    private readonly List<Movie> _allMovies;
    private bool _isLoading;
    private int _currentIndex;
    private Point _lastMousePosition;
    private bool _isDragging;
    private string _lastSearchText = string.Empty;
    private CancellationTokenSource? _searchDebounceCts;
    private static readonly Dictionary<string, BitmapImage> _imageCache = new();
    private int _movieCount;
    private int _tvShowCount;
    private string _selectedGenre = string.Empty;
    private string _selectedCountry = string.Empty;
    private string _selectedContentType = string.Empty;
    private List<Movie> _filteredMovies = new List<Movie>();
    private bool _showFavoritesOnly = false;
    private MainNavSection _currentNav = MainNavSection.Explore;
    private CancellationTokenSource? _heroLoadCts;
    private CancellationTokenSource? _exploreRefreshCts;
    private Movie? _heroSubscribedMovie;
    private static readonly string FavoritesFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TinyCinema",
        "favorites.txt"
    );

    public int MovieCount
    {
        get => _movieCount;
        private set
        {
            _movieCount = value;
            OnPropertyChanged(nameof(MovieCount));
            OnPropertyChanged(nameof(CatalogCountText));
        }
    }

    public int TvShowCount
    {
        get => _tvShowCount;
        private set
        {
            _tvShowCount = value;
            OnPropertyChanged(nameof(TvShowCount));
            OnPropertyChanged(nameof(CatalogCountText));
        }
    }

    public string CatalogCountText =>
        TvShowCount > 0
            ? $"({MovieCount} movies · {TvShowCount} TV shows)"
            : $"({MovieCount} movies)";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool _scrollViewerHooked;
    private bool _startupCenterPending;

    public MainWindow()
    {
        try
        {
            InitializeComponent();
            _startupCenterPending = SettingsWindow.GetStartCentered();
            WindowStartupLocation = WindowStartupLocation.Manual;
            _movies = new ObservableCollection<Movie>();
            _allMovies = new List<Movie>();
            MoviesListView.ItemsSource = _movies;
            DataContext = this;

            HeroPanel.PlayRequested += PlayButton_Click;
            HeroPanel.ContinueRequested += ContinueButton_Click;
            HeroPanel.FavoriteRequested += FavoriteButton_Click;
            HeroPanel.WatchedRequested += WatchedButton_Click;
            HeroPanel.TrailerRequested += TrailerButton_Click;
            HeroPanel.OpeningCreditsRequested += OpeningCreditsButton_Click;
            HeroPanel.InfoRequested += InfoButton_Click;
            HeroPanel.UrlRequested += UrlButton_Click;
            HeroPanel.RokuRequested += RokuButton_Click;

            ExploreHomePanel.MovieSelected += ExploreHomePanel_MovieSelected;
            WatchedHomePanel.MovieSelected += WatchedHomePanel_MovieSelected;
            WatchedHomePanel.WatchedToggleRequested += WatchedHomePanel_WatchedToggleRequested;
            IptvHomePanel.ChannelPlayRequested += IptvHomePanel_ChannelPlayRequested;
            SettingsPanel.HostWindow = this;
            RandomPickOverlay.PreviewRequested += RandomPickOverlay_PreviewRequested;

            PosterScrollViewer.ScrollChanged += async (_, e) =>
            {
                if (_isLoading)
                    return;

                if (e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - 100)
                    await LoadNextBatchAsync();
            };
            _scrollViewerHooked = true;

            UpdateSidebarVisuals();
            SmartSearchCoordinator.IndexReady += SmartSearchCoordinator_IndexReady;
            AppLayoutManager.LayoutChanged += AppLayoutManager_LayoutChanged;
            Loaded += MainWindow_Loaded;
            ContentRendered += MainWindow_ContentRendered;
            LoadMoviesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error in constructor: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyWindowLayout();
        ApplyStartupCenterIfEnabled();
    }

    private void MainWindow_ContentRendered(object sender, EventArgs e)
    {
        if (!_startupCenterPending)
            return;

        _startupCenterPending = false;
        ApplyStartupCenterIfEnabled();
    }

    private void ApplyStartupCenterIfEnabled()
    {
        if (!SettingsWindow.GetStartCentered())
            return;

        WindowPlacementHelper.CenterOnWorkingArea(this);
    }

    private void AppLayoutManager_LayoutChanged()
    {
        Dispatcher.Invoke(ApplyWindowLayout);
    }

    private void ApplyWindowLayout()
    {
        AppLayoutManager.LoadFromSettings();
        AppLayoutManager.ApplyTo(this, LayoutScaleTransform);

        if (SettingsWindow.GetStartCentered() && WindowState != WindowState.Maximized)
            WindowPlacementHelper.CenterOnWorkingArea(this);
    }

    private async void LoadMoviesAsync()
    {
        await ReloadMoviesAsync();
    }

    public async Task ReloadMoviesAsync()
    {
        try
        {
            _movies.Clear();
            _allMovies.Clear();
            _filteredMovies.Clear();
            _currentIndex = 0;
            _lastSearchText = string.Empty;
            _selectedGenre = string.Empty;
            _selectedCountry = string.Empty;
            _selectedContentType = string.Empty;
            _showFavoritesOnly = false;
            _currentNav = MainNavSection.Explore;

            var movieCatalogLocation = SettingsWindow.GetMovieCatalogLocation();
            var tvShowCatalogLocation = SettingsWindow.GetTvShowCatalogLocation();

            if (!File.Exists(movieCatalogLocation) && !File.Exists(tvShowCatalogLocation))
            {
                MessageBox.Show(
                    $"No catalog files found.\n\nMovies: {movieCatalogLocation}\nTV shows: {tvShowCatalogLocation}\n\nUse Settings to fetch movies or TV shows.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            if (File.Exists(movieCatalogLocation))
            {
                var movies = await MovieCatalogStore.LoadMoviesAsync(movieCatalogLocation);
                foreach (var movie in movies)
                    _allMovies.Add(movie);
            }

            if (File.Exists(tvShowCatalogLocation))
            {
                var tvShows = await TvShowCatalogStore.LoadShowsAsync(tvShowCatalogLocation);
                foreach (var show in tvShows)
                    _allMovies.Add(show);
            }

            LoadFavorites();
            LoadWatched();
            MovieCount = _allMovies.Count(m => m.ContentType == CatalogContentType.Movie);
            TvShowCount = _allMovies.Count(m => m.ContentType == CatalogContentType.TvShow);

            var genres = _allMovies
                .SelectMany(m => m.Genre.Split(',')
                    .Select(g => g.Trim())
                    .Where(g => !string.IsNullOrWhiteSpace(g)))
                .Distinct()
                .OrderBy(g => g)
                .ToList();
            genres.Insert(0, "All Genres");
            GenreFilter.ItemsSource = genres;
            GenreFilter.SelectedIndex = 0;

            var countries = _allMovies
                .SelectMany(m => m.Country.Split(',')
                    .Select(c => c.Trim())
                    .Where(c => !string.IsNullOrWhiteSpace(c)))
                .Distinct()
                .OrderBy(c => c)
                .ToList();
            countries.Insert(0, "All Countries");
            CountryFilter.ItemsSource = countries;
            CountryFilter.SelectedIndex = 0;

            UpdateSidebarVisuals();
            _filteredMovies = _allMovies;
            if (_currentNav == MainNavSection.Explore)
                await RefreshExploreAsync();
            else
            {
                await LoadNextBatchAsync();
                SelectFirstVisibleItem();
            }

            SmartSearchCoordinator.QueueRebuildIfStale(movieCatalogLocation);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading movies: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SmartSearchCoordinator_IndexReady()
    {
        Dispatcher.InvokeAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(_lastSearchText))
                return;

            await ApplyFiltersCoreAsync();
        });
    }

    private async Task LoadNextBatchAsync()
    {
        if (_isLoading) return;

        _isLoading = true;
        await Task.Run(async () =>
        {
            var sourceList = string.IsNullOrWhiteSpace(_lastSearchText) && string.IsNullOrEmpty(_selectedGenre) && string.IsNullOrEmpty(_selectedCountry) && string.IsNullOrEmpty(_selectedContentType) && !_showFavoritesOnly
                ? _allMovies
                : _filteredMovies;

            var endIndex = Math.Min(_currentIndex + BatchSize, sourceList.Count);
            var batch = sourceList.Skip(_currentIndex).Take(endIndex - _currentIndex).ToList();

            await Dispatcher.Invoke(async () =>
            {
                foreach (var movie in batch)
                {
                    _movies.Add(movie);
                    // Start loading the image asynchronously
                    _ = movie.LoadImageAsync();
                }
            });

            _currentIndex = endIndex;
        });
        _isLoading = false;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        var searchText = SearchBox.Text.ToLower().Trim();

        if (searchText == _lastSearchText)
            return;

        _lastSearchText = searchText;

        _searchDebounceCts?.Cancel();
        _searchDebounceCts = new CancellationTokenSource();
        var token = _searchDebounceCts.Token;
        _ = DebouncedApplyFiltersAsync(token);
    }

    private async Task DebouncedApplyFiltersAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(250, token);
            await ApplyFiltersCoreAsync();
        }
        catch (OperationCanceledException)
        {
            // Ignore debounce cancellation.
        }
    }

    private async void ApplyFiltersAsync()
    {
        await ApplyFiltersCoreAsync();
    }

    private async Task ApplyFiltersCoreAsync()
    {
        if (_currentNav == MainNavSection.Explore)
        {
            UpdateContentVisibility();
            await RefreshExploreAsync();
            return;
        }

        if (_currentNav == MainNavSection.Iptv)
        {
            UpdateContentVisibility();
            IptvHomePanel.ApplySearch(_lastSearchText);
            EmptyGridText.Visibility = Visibility.Collapsed;
            return;
        }

        if (_currentNav == MainNavSection.Watched)
        {
            UpdateContentVisibility();
            WatchedHomePanel.Refresh(_allMovies, _lastSearchText);
            EmptyGridText.Visibility = Visibility.Collapsed;
            if (WatchedHomePanel.SelectedMovie == null)
                HeroPanel.ShowEmptyState("Nothing watched yet. Mark titles with the eye icon to build your history.");
            return;
        }

        if (_currentNav == MainNavSection.Settings)
        {
            UpdateContentVisibility();
            return;
        }

        UpdateContentVisibility();
        _currentIndex = 0;
        _movies.Clear();

        _filteredMovies = HybridSearchService.FilterAndRank(
            _allMovies,
            _lastSearchText,
            SmartSearchCoordinator.GetIndex(),
            SmartSearchCoordinator.GetEmbeddingModel(),
            _selectedContentType,
            _selectedGenre,
            _selectedCountry,
            _showFavoritesOnly);

        EmptyGridText.Visibility = _filteredMovies.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PosterScrollViewer.Visibility = _filteredMovies.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        await LoadNextBatchAsync();

        PosterScrollViewer.ScrollToTop();
        SelectFirstVisibleItem();
    }

    private void GenreFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GenreFilter.SelectedItem != null)
        {
            _selectedGenre = GenreFilter.SelectedItem.ToString();
            if (_selectedGenre == "All Genres")
                _selectedGenre = string.Empty;

            ApplyFiltersAsync();
        }
    }

    private void CountryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CountryFilter.SelectedItem != null)
        {
            _selectedCountry = CountryFilter.SelectedItem.ToString();
            if (_selectedCountry == "All Countries")
                _selectedCountry = string.Empty;

            ApplyFiltersAsync();
        }
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string tag)
            return;

        if (!Enum.TryParse<MainNavSection>(tag, out var section))
            return;

        ApplyNavSection(section);
    }

    private void ApplyNavSection(MainNavSection section)
    {
        _currentNav = section;
        _showFavoritesOnly = section == MainNavSection.Favorites;
        _selectedContentType = section switch
        {
            MainNavSection.Movies => "Movies",
            MainNavSection.TvShows => "TV Shows",
            _ => string.Empty
        };

        UpdateSidebarVisuals();
        ApplyFiltersAsync();
    }

    private void UpdateSidebarVisuals()
    {
        var activeBrush = Application.Current.Resources["NavActiveBackgroundBrush"] as SolidColorBrush
            ?? new SolidColorBrush(Color.FromRgb(26, 26, 26));
        var accentForeground = Application.Current.Resources["AccentBrush"] as SolidColorBrush
            ?? Brushes.White;
        var normalForeground = Application.Current.Resources["NavInactiveForegroundBrush"] as SolidColorBrush
            ?? new SolidColorBrush(Color.FromRgb(163, 163, 163));
        var transparent = Brushes.Transparent;

        void StyleNav(Button button, bool isActive)
        {
            button.Background = isActive ? activeBrush : transparent;
            button.Foreground = isActive ? accentForeground : normalForeground;
        }

        StyleNav(NavMoviesButton, _currentNav == MainNavSection.Movies);
        StyleNav(NavTvShowsButton, _currentNav == MainNavSection.TvShows);
        StyleNav(NavExploreButton, _currentNav == MainNavSection.Explore);
        StyleNav(NavFavoritesButton, _currentNav == MainNavSection.Favorites);
        StyleNav(NavWatchedButton, _currentNav == MainNavSection.Watched);
        StyleNav(NavIptvButton, _currentNav == MainNavSection.Iptv);
        StyleNav(NavSettingsButton, _currentNav == MainNavSection.Settings);

        GridSectionTitle.Text = _currentNav switch
        {
            MainNavSection.Movies => "Movies",
            MainNavSection.TvShows => "TV Shows",
            MainNavSection.Favorites => "Favorites",
            MainNavSection.Watched => "Watched",
            MainNavSection.Iptv => "Live TV",
            MainNavSection.Settings => "Settings",
            _ => "For You"
        };
    }

    private void UpdateContentVisibility()
    {
        var showExplore = _currentNav == MainNavSection.Explore;
        var showIptv = _currentNav == MainNavSection.Iptv;
        var showWatched = _currentNav == MainNavSection.Watched;
        var showSettings = _currentNav == MainNavSection.Settings;

        ExploreHomePanel.Visibility = showExplore ? Visibility.Visible : Visibility.Collapsed;
        IptvHomePanel.Visibility = showIptv ? Visibility.Visible : Visibility.Collapsed;
        WatchedHomePanel.Visibility = showWatched ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
        PosterScrollViewer.Visibility = showExplore || showIptv || showWatched || showSettings
            ? Visibility.Collapsed
            : Visibility.Visible;
        HeroPanel.Visibility = showIptv || showSettings ? Visibility.Collapsed : Visibility.Visible;
        SearchFiltersBorder.Visibility = showSettings ? Visibility.Collapsed : Visibility.Visible;
        ShuffleButton.Visibility = showSettings || showIptv ? Visibility.Collapsed : Visibility.Visible;
        DiceButton.Visibility = showSettings || showIptv ? Visibility.Collapsed : Visibility.Visible;
        SortButton.Visibility = showSettings ? Visibility.Collapsed : Visibility.Visible;
        GenreFilter.IsEnabled = !showIptv && !showWatched && !showSettings;
        CountryFilter.IsEnabled = !showIptv && !showWatched && !showSettings;
        EmptyGridText.Visibility = Visibility.Collapsed;
        GridSectionTitle.Visibility = showExplore || showIptv || showWatched || showSettings
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (showExplore)
            MoviesListView.SelectedItem = null;

        if (!showIptv)
            IptvHomePanel.ShowCategories();
    }

    private void IptvHomePanel_ChannelPlayRequested(object? sender, IptvChannel channel)
    {
        IptvPlaybackLauncher.Play(channel);
    }

    private async Task RefreshExploreAsync()
    {
        _exploreRefreshCts?.Cancel();
        _exploreRefreshCts?.Dispose();
        _exploreRefreshCts = new CancellationTokenSource();
        var token = _exploreRefreshCts.Token;

        ExploreHomePanel.SetLoading(true);
        EmptyGridText.Visibility = Visibility.Collapsed;

        try
        {
            var interactions = UserInteractionTracker.GetRecent();
            var taste = CatalogTasteProfile.Build(interactions, _allMovies);
            var local = CatalogRecommendationEngine.BuildLocal(_allMovies, taste);

            var usedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in local.Rows)
            {
                foreach (var item in row.Items)
                    usedUrls.Add(item.Url);
            }

            var tmdbRows = await CatalogRecommendationEngine.BuildTmdbRowsAsync(
                _allMovies,
                interactions,
                usedUrls,
                token);

            if (token.IsCancellationRequested)
                return;

            var allRows = local.Rows.ToList();
            var insertIndex = allRows.FindIndex(row => row.Title == "For You");
            if (insertIndex < 0)
                insertIndex = Math.Max(0, allRows.Count - 1);

            allRows.InsertRange(insertIndex + 1, tmdbRows);

            var recommendations = new ExploreRecommendations
            {
                Rows = allRows,
                HintText = local.HintText
            };

            await Dispatcher.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested)
                    return;

                ExploreHomePanel.SetRecommendations(recommendations);

                if (!allRows.Any(row => row.Items.Count > 0))
                {
                    HeroPanel.ShowEmptyState("No recommendations yet. Play and favorite titles to personalize Explore.");
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Navigated away or refreshed again.
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error refreshing explore recommendations");
            await Dispatcher.InvokeAsync(() =>
            {
                ExploreHomePanel.SetLoading(false);
                EmptyGridText.Text = "Could not load recommendations.";
                EmptyGridText.Visibility = Visibility.Visible;
            });
        }
    }

    private void ExploreHomePanel_MovieSelected(object? sender, Movie movie)
    {
        SelectHeroMovie(movie);
    }

    private void WatchedHomePanel_MovieSelected(object? sender, Movie movie)
    {
        SelectHeroMovie(movie);
    }

    private void WatchedHomePanel_WatchedToggleRequested(object? sender, Movie movie)
    {
        ToggleWatched(movie);
    }

    private void SelectHeroMovie(Movie movie)
    {
        if (_heroSubscribedMovie != null)
        {
            _heroSubscribedMovie.PropertyChanged -= HeroMovie_PropertyChanged;
            _heroSubscribedMovie = null;
        }

        _heroSubscribedMovie = movie;
        movie.PropertyChanged += HeroMovie_PropertyChanged;
        HeroPanel.SetMovie(movie);
        UserInteractionTracker.Record(movie, InteractionEventType.View);
        _ = LoadHeroDetailsAsync(movie, movie);
    }

    public void RefreshTheme()
    {
        UpdateSidebarVisuals();
    }

    private void SelectFirstVisibleItem()
    {
        if (_movies.Count > 0)
        {
            MoviesListView.SelectedIndex = 0;
            return;
        }

        MoviesListView.SelectedItem = null;
        HeroPanel.ShowEmptyState(GetEmptyHeroMessage());
    }

    private string GetEmptyHeroMessage() => _currentNav switch
    {
        MainNavSection.Favorites => "No favorites yet. Heart a title to add it here.",
        MainNavSection.Watched => "Nothing watched yet. Mark titles with the eye icon to build your history.",
        _ => "No titles match your filters."
    };

    private Movie? GetSelectedMovie() =>
        _currentNav == MainNavSection.Explore
            ? ExploreHomePanel.SelectedMovie ?? HeroPanel.CurrentMovie
            : _currentNav == MainNavSection.Watched
                ? WatchedHomePanel.SelectedMovie ?? HeroPanel.CurrentMovie
                : MoviesListView.SelectedItem as Movie ?? HeroPanel.CurrentMovie;


    private bool IsMatch(Movie movie, string[] searchTerms)
    {
        var title = movie.Title.ToLower();
        var year = movie.Year.ToLower();

        // Check if all search terms match
        return searchTerms.All(term =>
        {
            // Check for exact matches first
            if (title.Contains(term) || year.Contains(term))
                return true;

            // Check for fuzzy matches with higher threshold for longer terms
            if (IsFuzzyMatch(title, term) || IsFuzzyMatch(year, term))
                return true;

            // Check for partial word matches
            if (IsPartialWordMatch(title, term) || IsPartialWordMatch(year, term))
                return true;

            return false;
        });
    }

    private int GetMatchScore(Movie movie, string[] searchTerms)
    {
        var title = movie.Title.ToLower();
        var year = movie.Year.ToLower();
        int score = 0;

        foreach (var term in searchTerms)
        {
            // Exact matches get highest score
            if (title.Contains(term))
                score += 100;
            if (year.Contains(term))
                score += 50;

            // Word boundary matches get good score
            if (IsPartialWordMatch(title, term))
                score += 30;
            if (IsPartialWordMatch(year, term))
                score += 15;

            // Fuzzy matches get lower score
            if (IsFuzzyMatch(title, term))
                score += 10;
            if (IsFuzzyMatch(year, term))
                score += 5;
        }

        return score;
    }

    private bool IsFuzzyMatch(string text, string searchTerm)
    {
        // Calculate Levenshtein distance
        int distance = LevenshteinDistance(text, searchTerm);
        
        // Adjust threshold based on search term length
        int maxDistance = Math.Max(1, searchTerm.Length / 3);
        
        return distance <= maxDistance;
    }

    private bool IsPartialWordMatch(string text, string searchTerm)
    {
        // Split text into words
        var words = text.Split(new[] { ' ', '-', '_', '.', ',' }, StringSplitOptions.RemoveEmptyEntries);
        
        // Check if any word starts with the search term
        return words.Any(word => word.StartsWith(searchTerm) || word.EndsWith(searchTerm));
    }

    private int LevenshteinDistance(string s, string t)
    {
        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];

        if (n == 0) return m;
        if (m == 0) return n;

        for (int i = 0; i <= n; i++)
            d[i, 0] = i;

        for (int j = 0; j <= m; j++)
            d[0, j] = j;

        for (int j = 1; j <= m; j++)
        {
            for (int i = 1; i <= n; i++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result)
                return result;

            var descendant = FindVisualChild<T>(child);
            if (descendant != null)
                return descendant;
        }
        return null;
    }

    private void MoviesListView_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _lastMousePosition = e.GetPosition(MoviesListView);
        MoviesListView.CaptureMouse();
    }

    private void MoviesListView_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            Point currentPosition = e.GetPosition(MoviesListView);
            double deltaY = _lastMousePosition.Y - currentPosition.Y;
            
            var scrollViewer = FindVisualChild<ScrollViewer>(MoviesListView);
            if (scrollViewer != null)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + deltaY);
                CheckAndLoadMoreItems(scrollViewer);
            }
            
            _lastMousePosition = currentPosition;
        }
    }

    private void MoviesListView_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        MoviesListView.ReleaseMouseCapture();
    }

    private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (PosterScrollViewer != null)
        {
            PosterScrollViewer.ScrollToVerticalOffset(PosterScrollViewer.VerticalOffset - e.Delta);
            CheckAndLoadMoreItems(PosterScrollViewer);
            e.Handled = true;
        }
    }

    private void MoviesListView_KeyDown(object sender, KeyEventArgs e)
    {
        double scrollAmount = 50;
        switch (e.Key)
        {
            case Key.Down:
                PosterScrollViewer.ScrollToVerticalOffset(PosterScrollViewer.VerticalOffset + scrollAmount);
                break;
            case Key.Up:
                PosterScrollViewer.ScrollToVerticalOffset(PosterScrollViewer.VerticalOffset - scrollAmount);
                break;
            case Key.PageDown:
                PosterScrollViewer.ScrollToVerticalOffset(PosterScrollViewer.VerticalOffset + PosterScrollViewer.ViewportHeight);
                break;
            case Key.PageUp:
                PosterScrollViewer.ScrollToVerticalOffset(PosterScrollViewer.VerticalOffset - PosterScrollViewer.ViewportHeight);
                break;
            case Key.End:
                PosterScrollViewer.ScrollToBottom();
                break;
            case Key.Home:
                PosterScrollViewer.ScrollToTop();
                break;
        }
        CheckAndLoadMoreItems(PosterScrollViewer);
    }

    private void CheckAndLoadMoreItems(ScrollViewer scrollViewer)
    {
        if (scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 200)
        {
            LoadNextBatchAsync();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        ImageCache.Cleanup();
        Application.Current.Shutdown();
    }

    protected override void OnClosed(EventArgs e)
    {
        _heroLoadCts?.Cancel();
        _heroLoadCts?.Dispose();
        base.OnClosed(e);
        ImageCache.Cleanup();
        Application.Current.Shutdown();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            // Start fade out animation
            var fadeOut = (Storyboard)FindResource("FadeOut");
            fadeOut.Begin(this);
            
            // Start dragging
            DragMove();
            
            // Start fade in animation
            var fadeIn = (Storyboard)FindResource("FadeIn");
            fadeIn.Begin(this);
        }
    }

    private void MoviesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_heroSubscribedMovie != null)
        {
            _heroSubscribedMovie.PropertyChanged -= HeroMovie_PropertyChanged;
            _heroSubscribedMovie = null;
        }

        if (MoviesListView.SelectedItem is Movie selectedMovie)
        {
            _heroSubscribedMovie = selectedMovie;
            selectedMovie.PropertyChanged += HeroMovie_PropertyChanged;
            HeroPanel.SetMovie(selectedMovie);
            UserInteractionTracker.Record(selectedMovie, InteractionEventType.View);
            _ = LoadHeroDetailsAsync(selectedMovie);
            return;
        }

        if (_movies.Count == 0)
        {
            HeroPanel.ShowEmptyState(GetEmptyHeroMessage());
        }
        else
        {
            HeroPanel.ShowEmptyState("Select a title to see details");
        }
    }

    private void HeroMovie_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Movie.CachedImage) && sender is Movie movie &&
            IsHeroMovieSelected(movie))
        {
            HeroPanel.SetMovie(movie);
        }

        if (e.PropertyName == nameof(Movie.IsFavorite) && sender is Movie favMovie &&
            IsHeroMovieSelected(favMovie))
        {
            HeroPanel.UpdateFavoriteVisual(favMovie.IsFavorite);
        }

        if (e.PropertyName == nameof(Movie.IsWatched) && sender is Movie watchedMovie &&
            IsHeroMovieSelected(watchedMovie))
        {
            HeroPanel.UpdateWatchedVisual(watchedMovie.IsWatched);
        }
    }

    private bool IsHeroMovieSelected(Movie movie) =>
        _currentNav == MainNavSection.Explore
            ? ExploreHomePanel.SelectedMovie == movie
            : _currentNav == MainNavSection.Watched
                ? WatchedHomePanel.SelectedMovie == movie
                : MoviesListView.SelectedItem == movie;

    private async Task LoadHeroDetailsAsync(Movie movie, Movie? selectionCheck = null)
    {
        _heroLoadCts?.Cancel();
        _heroLoadCts?.Dispose();
        _heroLoadCts = new CancellationTokenSource();
        var token = _heroLoadCts.Token;

        if (!movie.IsTvShow && !string.IsNullOrWhiteSpace(movie.Description))
        {
            if (!token.IsCancellationRequested && IsHeroMovieSelected(selectionCheck ?? movie))
                HeroPanel.SetDescription(movie.Description);
            return;
        }

        HeroPanel.SetDescriptionLoading(true);

        try
        {
            string description;
            if (movie.IsTvShow)
            {
                description = await GetTvShowDescriptionAsync(movie, token);
            }
            else if (!string.IsNullOrWhiteSpace(movie.Description))
            {
                description = movie.Description;
            }
            else
            {
                (description, _) = await GetMovieDetails(movie.Url, token);
            }

            if (token.IsCancellationRequested || !IsHeroMovieSelected(selectionCheck ?? movie))
                return;

            HeroPanel.SetDescription(description);
        }
        catch (OperationCanceledException)
        {
            // Selection changed.
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading hero details for {Title}", movie.Title);
            if (!token.IsCancellationRequested && IsHeroMovieSelected(selectionCheck ?? movie))
                HeroPanel.SetDescription("Could not load description.");
        }
    }

    private void UrlButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedMovie() is not Movie selectedMovie)
            return;

        var urlWindow = new UrlWindow(selectedMovie.Url);
        urlWindow.Owner = this;
        urlWindow.ShowDialog();
    }

    private async void InfoButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedMovie() is not Movie selectedMovie)
            return;

        var isTvShow = selectedMovie.ContentType == CatalogContentType.TvShow;

        if (!isTvShow && HasLocalMovieDetails(selectedMovie))
        {
            OpenMovieDetailsWindow(selectedMovie);
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource();

            var loadingWindow = new Window
            {
                Title = isTvShow ? "Loading TV Show Info" : "Loading Movie Info",
                Width = 300,
                Height = 100,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(15, 15, 15)),
                Foreground = Brushes.White,
                AllowsTransparency = true
            };

                // Add event handler for window closing
                loadingWindow.Closing += (s, args) =>
                {
                    cts.Cancel(); // Cancel the operation when window is closed
                };

                var loadingBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(15, 15, 15)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8)
                };

                var loadingGrid = new Grid();
                loadingGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                loadingGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                loadingGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                // Title bar
                var loadingTitleBar = new Grid
                {
                    Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                    Height = 32
                };
                loadingTitleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                loadingTitleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var loadingTitle = new TextBlock
                {
                    Text = isTvShow ? "Loading TV Show Info" : "Loading Movie Info",
                    Foreground = Brushes.White,
                    Margin = new Thickness(12, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(loadingTitle, 0);

                var loadingCloseButton = new Button
                {
                    Style = (Style)FindResource("CloseButtonStyle"),
                    Width = 46,
                    Height = 32
                };
                loadingCloseButton.Content = new FontAwesome.WPF.FontAwesome
                {
                    Icon = FontAwesome.WPF.FontAwesomeIcon.Close,
                    Foreground = Brushes.White,
                    Width = 12,
                    Height = 12
                };
                loadingCloseButton.Click += (s, args) => loadingWindow.Close();
                Grid.SetColumn(loadingCloseButton, 1);

                loadingTitleBar.Children.Add(loadingTitle);
                loadingTitleBar.Children.Add(loadingCloseButton);
                Grid.SetRow(loadingTitleBar, 0);

                var spinner = new FontAwesome.WPF.FontAwesome
                {
                    Icon = FontAwesome.WPF.FontAwesomeIcon.Spinner,
                    Width = 32,
                    Height = 32,
                    Foreground = Brushes.White
                };
                Grid.SetRow(spinner, 1);

                // Add rotation animation to spinner
                var rotateTransform = new RotateTransform();
                spinner.RenderTransform = rotateTransform;
                var animation = new DoubleAnimation
                {
                    From = 0,
                    To = 360,
                    Duration = TimeSpan.FromSeconds(1),
                    RepeatBehavior = RepeatBehavior.Forever
                };
                rotateTransform.BeginAnimation(RotateTransform.AngleProperty, animation);

                var loadingText = new TextBlock
                {
                    Text = isTvShow ? "Loading TV show information..." : "Loading movie information...",
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetRow(loadingText, 2);

                loadingGrid.Children.Add(loadingTitleBar);
                loadingGrid.Children.Add(spinner);
                loadingGrid.Children.Add(loadingText);
                loadingBorder.Child = loadingGrid;
                loadingWindow.Content = loadingBorder;

                // Add drag functionality
                loadingTitleBar.MouseLeftButtonDown += (s, args) =>
                {
                    loadingWindow.DragMove();
                };

                // Start loading window
                loadingWindow.Show();

                try
                {
                    string description;
                    string genre;

                    if (isTvShow)
                    {
                        description = await GetTvShowDescriptionAsync(selectedMovie, cts.Token);
                        genre = BuildTvShowGenreInfo(selectedMovie);
                    }
                    else if (!string.IsNullOrWhiteSpace(selectedMovie.Description))
                    {
                        description = selectedMovie.Description;
                        genre = BuildMovieDetailsInfo(selectedMovie);
                    }
                    else
                    {
                        (description, genre) = await GetMovieDetails(selectedMovie.Url, cts.Token);
                    }

                    if (cts.Token.IsCancellationRequested)
                        return;

                    loadingWindow.Close();

                    var scrapedCast = !isTvShow ? ExtractCastNamesFromDetailsText(genre) : [];
                    var detailsWindow = new DetailsWindow(
                        selectedMovie.Title,
                        selectedMovie.Year,
                        description,
                        genre,
                        scrapedCast,
                        selectedMovie.ImageUrl,
                        isTvShow);
                    detailsWindow.Owner = this;
                    detailsWindow.ShowDialog();
                }
                catch (OperationCanceledException)
                {
                    loadingWindow.Close();
                }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error loading {(isTvShow ? "TV show" : "movie")} details: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }


    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedMovie() is not Movie selectedMovie)
            return;

        UserInteractionTracker.Record(selectedMovie, InteractionEventType.Play);
        OpenPlayer(selectedMovie);

        if (_currentNav == MainNavSection.Explore)
            _ = RefreshExploreAsync();
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedMovie() is not Movie selectedMovie || !selectedMovie.IsTvShow)
            return;

        var history = HeroPanel.ContinueHistory ?? TvShowWatchHistory.TryGet(selectedMovie.Url);
        if (history == null)
            return;

        UserInteractionTracker.Record(selectedMovie, InteractionEventType.Continue);
        OpenPlayer(selectedMovie, history.Season, history.Episode);

        if (_currentNav == MainNavSection.Explore)
            _ = RefreshExploreAsync();
    }

    private void OpenPlayer(Movie movie, int? resumeSeason = null, int? resumeEpisode = null)
    {
        try
        {
            var playerWindow = new TinyZonePlayerWindow(movie, SettingsWindow.GetSelectedPlayer(), resumeSeason, resumeEpisode)
            {
                Owner = this
            };
            playerWindow.Closed += (_, _) =>
            {
                if (GetSelectedMovie() is Movie selected && selected.IsTvShow)
                    HeroPanel.SetContinueState(TvShowWatchHistory.TryGet(selected.Url));
            };
            playerWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error starting playback: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void TrailerButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedMovie() is not Movie movie)
            return;

        await PlayTrailerForMovieAsync(movie);
    }

    private async Task PlayTrailerForMovieAsync(Movie movie)
    {
        var apiKey = SettingsWindow.GetTmdbApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show(
                "Add your TMDB API key in Settings to watch trailers.\n\nGet a free key at themoviedb.org/settings/api.",
                "TMDB API Key Required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var previousCursor = Cursor;
        Cursor = Cursors.Wait;

        try
        {
            var videoKey = await TmdbClient.GetTrailerVideoKeyAsync(movie.Title, movie.Year, apiKey);
            if (string.IsNullOrWhiteSpace(videoKey))
            {
                MessageBox.Show(
                    $"No YouTube trailer was found on TMDB for \"{movie.Title}\" ({movie.Year}).",
                    "Trailer Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var trailerWindow = new TrailerWindow(movie.Title, videoKey)
            {
                Owner = this
            };
            trailerWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error loading trailer: {ex.Message}",
                "Trailer Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Cursor = previousCursor;
        }
    }

    private async void OpeningCreditsButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedMovie() is not Movie movie)
            return;

        await PlayOpeningCreditsForMovieAsync(movie);
    }

    private async Task PlayOpeningCreditsForMovieAsync(Movie movie)
    {
        var apiKey = SettingsWindow.GetTmdbApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show(
                "Add your TMDB API key in Settings to play opening credits.\n\nGet a free key at themoviedb.org/settings/api.",
                "TMDB API Key Required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var previousCursor = Cursor;
        Cursor = Cursors.Wait;

        try
        {
            var tmdbId = MovieLairTvDetailsParser.ExtractShowId(movie.Url);
            var videoKey = await TmdbClient.GetTvOpeningCreditsVideoKeyAsync(tmdbId, movie.Title, movie.Year, apiKey);
            if (string.IsNullOrWhiteSpace(videoKey))
            {
                MessageBox.Show(
                    $"No opening credits video was found on TMDB for \"{movie.Title}\" ({movie.Year}).\n\nNot all shows have opening credits listed.",
                    "Opening Credits Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var playerWindow = new TrailerWindow(movie.Title, videoKey, "Opening Credits")
            {
                Owner = this
            };
            playerWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error loading opening credits: {ex.Message}",
                "Opening Credits Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Cursor = previousCursor;
        }
    }

    private async void RandomPickOverlay_PreviewRequested(Movie movie)
    {
        if (movie.IsTvShow)
            await PlayOpeningCreditsForMovieAsync(movie);
        else
            await PlayTrailerForMovieAsync(movie);
    }

    private void RokuButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Roku sideload is available from the player.\n\n1. Play a movie\n2. Wait for an HLS stream in the Live URLs panel\n3. Click the TV icon on that stream",
            "Roku Sideload",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void DiceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNav is MainNavSection.Settings or MainNavSection.Iptv)
            return;

        var candidates = RandomCatalogPicker.GetCandidates(_currentNav, _allMovies);
        if (candidates.Count == 0)
        {
            MessageBox.Show(
                RandomCatalogPicker.GetEmptyMessage(_currentNav),
                "Roll the Dice",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        DiceButton.IsEnabled = false;
        ShuffleButton.IsEnabled = false;

        try
        {
            var result = await RandomPickOverlay.RollAndPickAsync(
                candidates,
                RandomCatalogPicker.GetRollingLabel(_currentNav));

            if (result.Action != RandomPickAction.Watch || result.Movie == null)
                return;

            SelectHeroMovie(result.Movie);
            UserInteractionTracker.Record(result.Movie, InteractionEventType.Play);
            OpenPlayer(result.Movie);

            if (_currentNav == MainNavSection.Explore)
                _ = RefreshExploreAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not roll the dice:\n{ex.Message}",
                "Roll the Dice",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            DiceButton.IsEnabled = true;
            ShuffleButton.IsEnabled = true;
        }
    }

    private async void ShuffleButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var random = new Random();

            for (int i = _allMovies.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (_allMovies[i], _allMovies[j]) = (_allMovies[j], _allMovies[i]);
            }

            await ApplyFiltersCoreAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error shuffling movies: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SortButton_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button == null) return;

        var contextMenu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0)
        };

        // Year sorting options
        var yearHeader = new MenuItem
        {
            Header = "Year",
            Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
            Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            FontWeight = FontWeights.SemiBold,
            IsEnabled = false
        };
        contextMenu.Items.Add(yearHeader);

        var sortByYearAsc = new MenuItem
        {
            Header = "Oldest First",
            Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
            Foreground = Brushes.White,
            Padding = new Thickness(16, 8, 16, 8)
        };
        sortByYearAsc.Click += (s, args) => SortMovies("Year", true);

        var sortByYearDesc = new MenuItem
        {
            Header = "Newest First",
            Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
            Foreground = Brushes.White,
            Padding = new Thickness(16, 8, 16, 8)
        };
        sortByYearDesc.Click += (s, args) => SortMovies("Year", false);

        contextMenu.Items.Add(sortByYearAsc);
        contextMenu.Items.Add(sortByYearDesc);

        // Add separator
        contextMenu.Items.Add(new Separator { Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)) });

        // Genre sorting options
        var genreHeader = new MenuItem
        {
            Header = "Genre",
            Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
            Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            FontWeight = FontWeights.SemiBold,
            IsEnabled = false
        };
        contextMenu.Items.Add(genreHeader);

        var sortByGenreAsc = new MenuItem
        {
            Header = "A to Z",
            Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
            Foreground = Brushes.White,
            Padding = new Thickness(16, 8, 16, 8)
        };
        sortByGenreAsc.Click += (s, args) => SortMovies("Genre", true);

        var sortByGenreDesc = new MenuItem
        {
            Header = "Z to A",
            Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
            Foreground = Brushes.White,
            Padding = new Thickness(16, 8, 16, 8)
        };
        sortByGenreDesc.Click += (s, args) => SortMovies("Genre", false);

        contextMenu.Items.Add(sortByGenreAsc);
        contextMenu.Items.Add(sortByGenreDesc);

        // Add separator
        contextMenu.Items.Add(new Separator { Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)) });

        // Country sorting options
        var countryHeader = new MenuItem
        {
            Header = "Country",
            Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
            Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            FontWeight = FontWeights.SemiBold,
            IsEnabled = false
        };
        contextMenu.Items.Add(countryHeader);

        var sortByCountryAsc = new MenuItem
        {
            Header = "A to Z",
            Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
            Foreground = Brushes.White,
            Padding = new Thickness(16, 8, 16, 8)
        };
        sortByCountryAsc.Click += (s, args) => SortMovies("Country", true);

        var sortByCountryDesc = new MenuItem
        {
            Header = "Z to A",
            Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
            Foreground = Brushes.White,
            Padding = new Thickness(16, 8, 16, 8)
        };
        sortByCountryDesc.Click += (s, args) => SortMovies("Country", false);

        contextMenu.Items.Add(sortByCountryAsc);
        contextMenu.Items.Add(sortByCountryDesc);

        // Style for menu items
        var menuItemStyle = new Style(typeof(MenuItem));
        menuItemStyle.Setters.Add(new Setter(MenuItem.BackgroundProperty, new SolidColorBrush(Color.FromRgb(26, 26, 26))));
        menuItemStyle.Setters.Add(new Setter(MenuItem.ForegroundProperty, Brushes.White));
        menuItemStyle.Setters.Add(new Setter(MenuItem.PaddingProperty, new Thickness(16, 8, 16, 8)));
        
        var trigger = new Trigger { Property = MenuItem.IsMouseOverProperty, Value = true };
        trigger.Setters.Add(new Setter(MenuItem.BackgroundProperty, new SolidColorBrush(Color.FromRgb(42, 42, 42))));
        menuItemStyle.Triggers.Add(trigger);

        contextMenu.Resources.Add(typeof(MenuItem), menuItemStyle);

        // Position the context menu below the button
        contextMenu.PlacementTarget = button;
        contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        contextMenu.IsOpen = true;
    }

    private async void SortMovies(string sortBy, bool ascending)
    {
        try
        {
            switch (sortBy)
            {
                case "Year":
                    _allMovies.Sort((a, b) => ascending
                        ? string.Compare(a.Year, b.Year, StringComparison.Ordinal)
                        : string.Compare(b.Year, a.Year, StringComparison.Ordinal));
                    break;
                case "Genre":
                    _allMovies.Sort((a, b) => ascending
                        ? string.Compare(a.Genre, b.Genre, StringComparison.Ordinal)
                        : string.Compare(b.Genre, a.Genre, StringComparison.Ordinal));
                    break;
                case "Country":
                    _allMovies.Sort((a, b) => ascending
                        ? string.Compare(a.Country, b.Country, StringComparison.Ordinal)
                        : string.Compare(b.Country, a.Country, StringComparison.Ordinal));
                    break;
            }

            await ApplyFiltersCoreAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error sorting movies: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedMovie() is not Movie movie)
            return;

        movie.IsFavorite = !movie.IsFavorite;
        HeroPanel.UpdateFavoriteVisual(movie.IsFavorite);
        SaveFavorites();

        if (movie.IsFavorite)
            UserInteractionTracker.Record(movie, InteractionEventType.Favorite);

        if (_showFavoritesOnly && !movie.IsFavorite)
            ApplyFiltersAsync();
        else if (_currentNav == MainNavSection.Explore)
            _ = RefreshExploreAsync();
    }

    private void WatchedButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedMovie() is not Movie movie)
            return;

        ToggleWatched(movie);
    }

    private void ToggleWatched(Movie movie)
    {
        var isWatched = !movie.IsWatched;
        WatchedStore.SetWatched(movie, isWatched);
        HeroPanel.UpdateWatchedVisual(movie.IsWatched);

        if (_currentNav == MainNavSection.Watched)
            WatchedHomePanel.NotifyWatchedChanged(movie);
    }

    private void LoadWatched()
    {
        try
        {
            WatchedStore.ApplyToMovies(_allMovies);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading watched titles");
        }
    }

    private void LoadFavorites()
    {
        try
        {
            if (File.Exists(FavoritesFile))
            {
                var favoriteUrls = File.ReadAllLines(FavoritesFile).ToHashSet();
                foreach (var movie in _allMovies)
                {
                    movie.IsFavorite = favoriteUrls.Contains(movie.Url);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading favorites");
        }
    }

    private void SaveFavorites()
    {
        try
        {
            var directory = Path.GetDirectoryName(FavoritesFile);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var favoriteUrls = _allMovies.Where(m => m.IsFavorite).Select(m => m.Url).ToList();
            File.WriteAllLines(FavoritesFile, favoriteUrls);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error saving favorites");
        }
    }

    private static string BuildMovieDetailsInfo(Movie movie)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(movie.Year) && !movie.Year.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            parts.Add($"Released: {movie.Year}");

        if (!string.IsNullOrWhiteSpace(movie.Genre) && !movie.Genre.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            parts.Add($"Genre: {movie.Genre}");

        if (!string.IsNullOrWhiteSpace(movie.Duration) && !movie.Duration.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            parts.Add($"Duration: {movie.Duration}");

        if (!string.IsNullOrWhiteSpace(movie.Country) && !movie.Country.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            parts.Add($"Country: {movie.Country}");

        if (!string.IsNullOrWhiteSpace(movie.Director))
            parts.Add($"Director: {movie.Director}");

        return string.Join('\n', parts);
    }

    private static bool HasLocalMovieDetails(Movie movie) =>
        !string.IsNullOrWhiteSpace(movie.Description) ||
        !string.IsNullOrWhiteSpace(movie.Director) ||
        movie.Cast.Count > 0 ||
        !string.IsNullOrWhiteSpace(movie.Genre) ||
        !string.IsNullOrWhiteSpace(movie.Duration) ||
        !string.IsNullOrWhiteSpace(movie.Country);

    private void OpenMovieDetailsWindow(Movie movie)
    {
        var detailsWindow = new DetailsWindow(
            movie.Title,
            movie.Year,
            movie.Description,
            BuildMovieDetailsInfo(movie),
            movie.Cast,
            movie.ImageUrl,
            isTvShow: false);
        detailsWindow.Owner = this;
        detailsWindow.ShowDialog();
    }

    private static List<string> ExtractCastNamesFromDetailsText(string detailsText)
    {
        if (string.IsNullOrWhiteSpace(detailsText))
            return [];

        var castMatch = Regex.Match(detailsText, @"Casts?:\s*([^\n]+)", RegexOptions.IgnoreCase);
        if (!castMatch.Success)
            return [];

        return castMatch.Groups[1].Value
            .Split([',', '،'], StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
    }

    private static string BuildTvShowGenreInfo(Movie show)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(show.Year))
            parts.Add($"Released: {show.Year}");

        if (!string.IsNullOrWhiteSpace(show.Genre) && !show.Genre.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            parts.Add($"Genre: {show.Genre}");

        if (!string.IsNullOrWhiteSpace(show.Duration) && !show.Duration.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            parts.Add($"Seasons: {show.Duration}");

        if (!string.IsNullOrWhiteSpace(show.Country) && !show.Country.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            parts.Add($"Country: {show.Country}");

        return string.Join("\n\n", parts);
    }

    private static async Task<string> GetTvShowDescriptionAsync(Movie show, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var apiKey = SettingsWindow.GetTmdbApiKey();
        var tmdbId = MovieLairTvDetailsParser.ExtractShowId(show.Url);
        if (!string.IsNullOrWhiteSpace(apiKey) && tmdbId is > 0)
        {
            try
            {
                var details = await MovieLairTmdbClient.GetTvDetailsAsync(tmdbId.Value, apiKey, cancellationToken);
                if (details != null)
                {
                    var parts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(details.Tagline))
                        parts.Add(details.Tagline.Trim());
                    if (!string.IsNullOrWhiteSpace(details.Overview))
                        parts.Add(details.Overview.Trim());

                    if (parts.Count > 0)
                        return string.Join("\n\n", parts);
                }
            }
            catch (Exception ex)
            {
                Log.Information(ex, "TMDB TV details lookup failed for {Title}", show.Title);
            }
        }

        try
        {
            using var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer()
            };
            var uri = new Uri(show.Url);
            handler.CookieContainer.Add(uri, new Cookie("srv", "2", "/", uri.Host));

            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:147.0) Gecko/20100101 Firefox/147.0");
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");

            var html = await client.GetStringAsync(show.Url, cancellationToken);
            var (description, _) = MovieLairTvDetailsParser.ParseDescription(html);
            if (!string.IsNullOrWhiteSpace(description))
                return description;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting TV show description for {Url}", show.Url);
        }

        return string.Empty;
    }

    private static async Task<(string description, string genre)> GetMovieDetails(string url, CancellationToken cancellationToken)
    {
        try
        {
            // Check for cancellation before making request
            cancellationToken.ThrowIfCancellationRequested();

            using (var handler = new HttpClientHandler())
            {
                // Add cookie container for session cookies
                handler.CookieContainer = new System.Net.CookieContainer();
                var uri = new Uri(url);
                handler.CookieContainer.Add(uri, new System.Net.Cookie("srv", "2", "/", uri.Host));

                using (var client = new HttpClient(handler))
                {
                    client.Timeout = TimeSpan.FromSeconds(30);

                    // Set headers to mimic a real browser request
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:147.0) Gecko/20100101 Firefox/147.0");
                    client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                    client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
                    client.DefaultRequestHeaders.Add("Accept-Encoding", "identity");
                    client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
                    client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
                    client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
                    client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
                    client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");

                    // Get the HTML content
                    var response = await client.GetStringAsync(url);

                    // Check for cancellation after receiving response
                    cancellationToken.ThrowIfCancellationRequested();

                    // Parse HTML using HtmlAgilityPack
                    var htmlDoc = new HtmlDocument();
                    htmlDoc.LoadHtml(response);

                    // Get description
                    string description = "";
                    try
                    {
                        var descNode = htmlDoc.DocumentNode.SelectSingleNode("//div[@class='description']");
                        if (descNode != null)
                        {
                            description = descNode.InnerText.Trim();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Information($"Could not find movie description: {ex.Message}");
                    }

                    // Get genre
                    string genre = "";
                    try
                    {
                        var genreNode = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(@class, 'col-xl-7') and contains(@class, 'col-lg-7') and contains(@class, 'col-md-8') and contains(@class, 'col-sm-12')]");
                        if (genreNode != null)
                        {
                            genre = genreNode.InnerText.Trim();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Information($"Could not find movie genre: {ex.Message}");
                    }

                    return (description, genre);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw; // Re-throw cancellation exception
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting movie details");
            return ("", "");
        }
    }
}

public class Movie : INotifyPropertyChanged
{
    public required string Year { get; set; }
    public required string Title { get; set; }
    public required string Url { get; set; }
    public required string ImageUrl { get; set; }
    public required string Genre { get; set; }
    public required string Duration { get; set; }
    public required string Country { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Director { get; set; } = string.Empty;
    public List<string> Cast { get; set; } = [];
    public CatalogContentType ContentType { get; set; } = CatalogContentType.Movie;
    public string ContentTypeLabel => ContentType == CatalogContentType.TvShow ? "TV Show" : "Movie";
    public bool IsTvShow => ContentType == CatalogContentType.TvShow;
    public string DetailsToolTip => ContentType == CatalogContentType.TvShow ? "View TV show details" : "View movie details";
    private BitmapImage _cachedImage;
    private bool _isLoading;
    private bool _isFavorite;
    private bool _isWatched;
    private DateTime? _watchedAtUtc;

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            _isFavorite = value;
            OnPropertyChanged(nameof(IsFavorite));
        }
    }

    public bool IsWatched
    {
        get => _isWatched;
        set
        {
            _isWatched = value;
            OnPropertyChanged(nameof(IsWatched));
        }
    }

    public DateTime? WatchedAtUtc
    {
        get => _watchedAtUtc;
        set
        {
            _watchedAtUtc = value;
            OnPropertyChanged(nameof(WatchedAtUtc));
        }
    }

    public BitmapImage CachedImage
    {
        get => _cachedImage;
        private set
        {
            _cachedImage = value;
            OnPropertyChanged(nameof(CachedImage));
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            _isLoading = value;
            OnPropertyChanged(nameof(IsLoading));
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public async Task LoadImageAsync()
    {
        if (_cachedImage != null || IsLoading) return;

        try
        {
            IsLoading = true;
            var image = await ImageCache.GetCachedImageAsync(ImageUrl);
            if (image != null)
            {
                CachedImage = image;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }
}

public static class ImageCache
{
    private static readonly Dictionary<string, BitmapImage> _imageCache = new();

    public static void Cleanup()
    {
        _imageCache.Clear();
    }

    public static string? TryGetCachedFilePath(string imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return null;

        try
        {
            if (!SettingsWindow.GetIsCachingEnabled())
                return null;

            var cacheFileName = Convert.ToBase64String(
                    System.Security.Cryptography.SHA256.Create()
                        .ComputeHash(System.Text.Encoding.UTF8.GetBytes(imageUrl)))
                .Replace("/", "_")
                .Replace("+", "-")
                .Replace("=", "");

            return Path.Combine(SettingsWindow.GetCacheLocation(), cacheFileName + ".jpg");
        }
        catch
        {
            return null;
        }
    }

    public static async Task<BitmapImage> GetCachedImageAsync(string imageUrl)
    {
        try
        {
            // If caching is disabled, load directly from URL
            if (!SettingsWindow.GetIsCachingEnabled())
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.None;
                image.UriSource = new Uri(imageUrl);
                image.EndInit();
                return image;
            }

            // Check memory cache first
            if (_imageCache.TryGetValue(imageUrl, out var cachedImage))
            {
                return cachedImage;
            }

            // Generate cache file path
            string cacheFileName = Convert.ToBase64String(
                System.Security.Cryptography.SHA256.Create()
                    .ComputeHash(System.Text.Encoding.UTF8.GetBytes(imageUrl)))
                .Replace("/", "_")
                .Replace("+", "-")
                .Replace("=", "");

            string cacheFilePath = Path.Combine(SettingsWindow.GetCacheLocation(), cacheFileName + ".jpg");

            // Check disk cache
            if (File.Exists(cacheFilePath))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(cacheFilePath);
                image.EndInit();
                _imageCache[imageUrl] = image;
                return image;
            }

            // Download and cache the image
            using (var client = new HttpClient())
            {
                var imageBytes = await client.GetByteArrayAsync(imageUrl);
                await File.WriteAllBytesAsync(cacheFilePath, imageBytes);

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(cacheFilePath);
                image.EndInit();
                _imageCache[imageUrl] = image;
                return image;
            }
        }
        catch (Exception)
        {
            // Return a default image or null if download fails
            return null;
        }
    }
}

public class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        var boolValue = value is bool b && b;
        bool inverse = parameter?.ToString() == "Inverse";
        
        if (inverse)
        {
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        bool inverse = parameter?.ToString() == "Inverse";
        Visibility visibility = (Visibility)value;
        
        if (inverse)
        {
            return visibility == Visibility.Visible;
        }
        else
        {
            return visibility == Visibility.Collapsed;
        }
    }
}

public class SelectedMovieVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (values.Length < 2)
            return Visibility.Collapsed;

        var isSelected = values[0] is bool selected && selected;
        var isTvShow = values[1] is bool tvShow && tvShow;
        return isSelected && !isTvShow ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

public class SelectedTvShowVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (values.Length < 2)
            return Visibility.Collapsed;

        var isSelected = values[0] is bool selected && selected;
        var isTvShow = values[1] is bool tvShow && tvShow;
        return isSelected && isTvShow ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

public class FavoriteIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        bool isFavorite = value is bool b && b;
        return isFavorite ? FontAwesome.WPF.FontAwesomeIcon.Heart : FontAwesome.WPF.FontAwesomeIcon.HeartOutline;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class FavoriteColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        bool isFavorite = value is bool b && b;
        return new SolidColorBrush(isFavorite ? Color.FromRgb(220, 38, 38) : Color.FromRgb(255, 255, 255));
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
