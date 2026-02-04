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
    private static readonly Dictionary<string, BitmapImage> _imageCache = new();
    private int _movieCount;
    private string _selectedGenre = string.Empty;
    private string _selectedCountry = string.Empty;
    private List<Movie> _filteredMovies = new List<Movie>();
    private bool _showFavoritesOnly = false;
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
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public MainWindow()
    {
        try
        {
            InitializeComponent();
            _movies = new ObservableCollection<Movie>();
            _allMovies = new List<Movie>();
            MoviesListView.ItemsSource = _movies;
            DataContext = this;
            
            LoadMoviesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error in constructor: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void LoadMoviesAsync()
    {
        try
        {
            var settingsWindow = new SettingsWindow();
            var movieLinksLocation = settingsWindow.MovieLinksLocation;

            if (!File.Exists(movieLinksLocation))
            {
                MessageBox.Show($"Movie links file not found at: {movieLinksLocation}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Read all lines but don't process them yet
            var lines = await File.ReadAllLinesAsync(movieLinksLocation);
            _currentIndex = 0;

            // Process all movies first
            foreach (var line in lines)
            {
                var parts = line.Split('|').Select(p => p.Trim()).ToArray();
                if (parts.Length >= 7) // Now we expect 7 parts: year, title, url, image url, genre, duration, country
                {
                    _allMovies.Add(new Movie
                    {
                        Year = parts[0],
                        Title = parts[1],
                        Url = parts[2],
                        ImageUrl = parts[3],
                        Genre = parts[4],
                        Duration = parts[5],
                        Country = parts[6]
                    });
                }
            }

            // Load favorites from file
            LoadFavorites();

            // Update movie count
            MovieCount = _allMovies.Count;

            // Get unique genres by splitting comma-separated values
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

            // Get unique countries by splitting comma-separated values
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

            // In LoadMoviesAsync, after loading all movies, set _filteredMovies = _allMovies before loading the initial batch
            _filteredMovies = _allMovies;

            // Load initial batch
            await LoadNextBatchAsync();

            // Find the ScrollViewer in the visual tree
            var scrollViewer = FindVisualChild<ScrollViewer>(MoviesListView);
            if (scrollViewer != null)
            {
                scrollViewer.ScrollChanged += async (s, e) =>
                {
                    if (_isLoading) return;

                    // If we're near the bottom, load more items
                    if (e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - 100)
                    {
                        await LoadNextBatchAsync();
                    }
                };
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading movies: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadNextBatchAsync()
    {
        if (_isLoading) return;

        _isLoading = true;
        await Task.Run(async () =>
        {
            var sourceList = string.IsNullOrWhiteSpace(_lastSearchText) && string.IsNullOrEmpty(_selectedGenre) && string.IsNullOrEmpty(_selectedCountry) && !_showFavoritesOnly
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
        var searchText = SearchBox.Text.ToLower().Trim();
        
        // Don't re-search if the text hasn't changed
        if (searchText == _lastSearchText) return;
        _lastSearchText = searchText;
        
        ApplyFilters();
    }

    private void GenreFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GenreFilter.SelectedItem != null)
        {
            _selectedGenre = GenreFilter.SelectedItem.ToString();
            if (_selectedGenre == "All Genres")
                _selectedGenre = string.Empty;
            
            ApplyFilters();
        }
    }

    private void CountryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CountryFilter.SelectedItem != null)
        {
            _selectedCountry = CountryFilter.SelectedItem.ToString();
            if (_selectedCountry == "All Countries")
                _selectedCountry = string.Empty;
            
            ApplyFilters();
        }
    }

    private void ApplyFilters()
    {
        _currentIndex = 0;
        _movies.Clear();

        // Apply filters
        _filteredMovies = _allMovies.Where(m =>
            (string.IsNullOrEmpty(_selectedGenre) || m.Genre.Split(',').Select(g => g.Trim()).Contains(_selectedGenre)) &&
            (string.IsNullOrEmpty(_selectedCountry) || m.Country.Split(',').Select(c => c.Trim()).Contains(_selectedCountry)) &&
            (string.IsNullOrEmpty(_lastSearchText) || IsMatch(m, _lastSearchText.Split(new[] { ' ', '-', '_', '.', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(term => term.Length >= 2)
                .ToArray())) &&
            (!_showFavoritesOnly || m.IsFavorite)
        ).ToList();

        // Load initial batch of filtered movies
        LoadNextBatchAsync();

        // Scroll to top
        var scrollViewer = FindVisualChild<ScrollViewer>(MoviesListView);
        if (scrollViewer != null)
        {
            scrollViewer.ScrollToTop();
        }
    }

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
        var scrollViewer = sender as ScrollViewer;
        if (scrollViewer != null)
        {
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
            CheckAndLoadMoreItems(scrollViewer);
            e.Handled = true;
        }
    }

    private void MoviesListView_KeyDown(object sender, KeyEventArgs e)
    {
        var scrollViewer = FindVisualChild<ScrollViewer>(MoviesListView);
        if (scrollViewer != null)
        {
            double scrollAmount = 50; // Adjust this value to control scroll speed
            switch (e.Key)
            {
                case Key.Down:
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + scrollAmount);
                    break;
                case Key.Up:
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - scrollAmount);
                    break;
                case Key.PageDown:
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + scrollViewer.ViewportHeight);
                    break;
                case Key.PageUp:
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - scrollViewer.ViewportHeight);
                    break;
                case Key.End:
                    scrollViewer.ScrollToBottom();
                    break;
                case Key.Home:
                    scrollViewer.ScrollToTop();
                    break;
            }
            CheckAndLoadMoreItems(scrollViewer);
        }
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

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow();
        settingsWindow.Owner = this;
        settingsWindow.ShowDialog();
    }

    private void MoviesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Update the visual state of all items
        foreach (var item in MoviesListView.Items)
        {
            var container = MoviesListView.ItemContainerGenerator.ContainerFromItem(item) as ListViewItem;
            if (container != null)
            {
                container.IsSelected = item == MoviesListView.SelectedItem;
            }
        }
    }

    private void UrlButton_Click(object sender, RoutedEventArgs e)
    {
        if (MoviesListView.SelectedItem is Movie selectedMovie)
        {
            var urlWindow = new UrlWindow(selectedMovie.Url);
            urlWindow.Owner = this;
            urlWindow.ShowDialog();
        }
    }

    private async void InfoButton_Click(object sender, RoutedEventArgs e)
    {
        if (MoviesListView.SelectedItem is Movie selectedMovie)
        {
            try
            {
                // Create cancellation token source
                using var cts = new CancellationTokenSource();

                // Show loading state
                var loadingWindow = new Window
                {
                    Title = "Loading Movie Info",
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
                    Text = "Loading Movie Info",
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
                    Text = "Loading movie information...",
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
                    // Get movie details with HTTP request (no browser needed)
                    var (description, genre) = await GetMovieDetails(selectedMovie.Url, cts.Token);

                    // Check if operation was cancelled
                    if (cts.Token.IsCancellationRequested)
                    {
                        return;
                    }

                    // Close loading window
                    loadingWindow.Close();

                    // Show movie details using the new DetailsWindow
                    var detailsWindow = new DetailsWindow(
                        selectedMovie.Title,
                        selectedMovie.Year,
                        description,
                        genre,
                        selectedMovie.ImageUrl
                    );
                    detailsWindow.Owner = this;
                    detailsWindow.ShowDialog();
                }
                catch (OperationCanceledException)
                {
                    // Operation was cancelled, just close the loading window
                    loadingWindow.Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error loading movie details: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (MoviesListView.SelectedItem is Movie selectedMovie)
        {
            try
            {
                // Clean up any existing temp files first
                CleanupTempFiles();

                // Close all Firefox processes - this is crucial for the app to work properly
                try
                {
                    var firefoxProcesses = System.Diagnostics.Process.GetProcessesByName("firefox");
                    if (firefoxProcesses.Length > 0)
                    {
                        foreach (var firefoxProcess in firefoxProcesses)
                        {
                            try
                            {
                                if (!firefoxProcess.HasExited)
                                {
                                    firefoxProcess.Kill(true); // Kill the process and its child processes
                                    firefoxProcess.WaitForExit(3000); // Wait up to 3 seconds for the process to exit
                                }
                            }
                            catch
                            {
                                // Ignore errors for individual processes, continue killing others
                            }
                        }
                        // Give a brief moment for all Firefox processes to fully terminate
                        await Task.Delay(500);
                    }
                }
                catch
                {
                    // If there's an error closing Firefox, show a warning but continue
                    MessageBox.Show(
                        "Warning: Could not close all Firefox processes. Please manually close Firefox before playing.",
                        "Firefox Warning",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                }

                // Check if TinyScrapev2 is already running
                var existingProcesses = System.Diagnostics.Process.GetProcessesByName("TinyScrapev2");
                if (existingProcesses.Length > 0)
                {
                    MessageBox.Show(
                        "TinyScrapev2 is already running. Please wait for it to finish or close it manually.",
                        "Warning",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    return;
                }

                // Show loading state
                var loadingWindow = new Window
                {
                    Title = "Processing Movie",
                    Width = 400,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    Background = new SolidColorBrush(Color.FromRgb(15, 15, 15)),
                    Foreground = Brushes.White,
                    AllowsTransparency = true
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
                loadingGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                loadingGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                loadingGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

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
                    Text = "Processing Movie",
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

                // Store the process reference
                System.Diagnostics.Process? tinyScrapev2Process = null;

                // Add event handler for window closing
                loadingWindow.Closing += (s, args) =>
                {
                    try
                    {
                        // Kill all TinyScrapev2 processes
                        var processes = System.Diagnostics.Process.GetProcessesByName("TinyScrapev2");
                        foreach (var process in processes)
                        {
                            try
                            {
                                if (!process.HasExited)
                                {
                                    process.Kill(true); // Kill the process and its child processes
                                    process.WaitForExit(1000); // Wait up to 1 second for the process to exit
                                }
                            }
                            catch
                            {
                                // Ignore errors for individual processes
                            }
                        }
                    }
                    catch
                    {
                        // Ignore any errors during process termination
                    }
                };

                loadingCloseButton.Click += (s, args) => loadingWindow.Close();
                Grid.SetColumn(loadingCloseButton, 1);

                loadingTitleBar.Children.Add(loadingTitle);
                loadingTitleBar.Children.Add(loadingCloseButton);
                Grid.SetRow(loadingTitleBar, 0);

                // Movie title
                var movieTitleText = new TextBlock
                {
                    Text = selectedMovie.Title,
                    Foreground = Brushes.White,
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(20, 20, 20, 10)
                };
                Grid.SetRow(movieTitleText, 1);

                // Progress bar
                var progressBar = new ProgressBar
                {
                    Height = 4,
                    Margin = new Thickness(20, 0, 20, 0),
                    Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
                    BorderThickness = new Thickness(0),
                    Value = 0
                };
                Grid.SetRow(progressBar, 2);

                // Progress bar animation
                var progressAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = 100,
                    Duration = TimeSpan.FromSeconds(30),
                    RepeatBehavior = RepeatBehavior.Forever
                };
                progressBar.BeginAnimation(ProgressBar.ValueProperty, progressAnimation);

                // Status text
                var loadingText = new TextBlock
                {
                    Text = "Initializing...",
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 20)
                };
                Grid.SetRow(loadingText, 3);

                // Time elapsed
                var timeElapsedText = new TextBlock
                {
                    Text = "Time elapsed: 00:00",
                    Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 20),
                    FontSize = 12
                };
                Grid.SetRow(timeElapsedText, 4);

                loadingGrid.Children.Add(loadingTitleBar);
                loadingGrid.Children.Add(movieTitleText);
                loadingGrid.Children.Add(progressBar);
                loadingGrid.Children.Add(loadingText);
                loadingGrid.Children.Add(timeElapsedText);
                loadingBorder.Child = loadingGrid;
                loadingWindow.Content = loadingBorder;

                // Add drag functionality
                loadingTitleBar.MouseLeftButtonDown += (s, args) =>
                {
                    loadingWindow.DragMove();
                };

                // Start loading window
                loadingWindow.Show();

                // Start TinyScrapev2.exe with the movie URL (without -ffplay, we'll launch player ourselves)
                var settingsWindow = new SettingsWindow();
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "TinyScrapev2.exe",
                    Arguments = $"\"{selectedMovie.Url}\"",
                    UseShellExecute = false,
                    CreateNoWindow = settingsWindow.HideTinyScraper,
                    WindowStyle = settingsWindow.HideTinyScraper ? 
                        System.Diagnostics.ProcessWindowStyle.Hidden : 
                        System.Diagnostics.ProcessWindowStyle.Normal
                };

                tinyScrapev2Process = System.Diagnostics.Process.Start(startInfo);
                if (tinyScrapev2Process == null)
                {
                    loadingWindow.Close();
                    MessageBox.Show(
                        "Failed to start TinyScrapev2 process.",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    return;
                }

                // Wait for hls-urls.txt to appear in the application directory - poll more frequently to catch it ASAP
                var hlsUrlsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hls-urls.txt");
                
                // Ensure any existing hls-urls.txt is removed before starting (clean slate)
                try
                {
                    if (File.Exists(hlsUrlsFile))
                    {
                        File.Delete(hlsUrlsFile);
                        await Task.Delay(100); // Brief delay to ensure file system has released it
                    }
                }
                catch { }
                
                var startTime = DateTime.Now;
                var checkInterval = TimeSpan.FromMilliseconds(50); // Check every 50ms for even faster detection
                string m3u8Url = null;
                const int maxWaitTimeSeconds = 60; // Maximum time to wait for the file (60 seconds)
                var maxWaitTime = TimeSpan.FromSeconds(maxWaitTimeSeconds);
                var lastFileSize = 0L;

                // Poll for the file and read it immediately when it appears
                while (m3u8Url == null)
                {
                    // Check if we've exceeded the maximum wait time
                    var elapsed = DateTime.Now - startTime;
                    if (elapsed > maxWaitTime)
                    {
                        // Clean up any leftover file before showing error
                        try
                        {
                            if (File.Exists(hlsUrlsFile))
                            {
                                File.Delete(hlsUrlsFile);
                            }
                        }
                        catch { }
                        
                        loadingWindow.Close();
                        MessageBox.Show(
                            "Timeout: hls-urls.txt was not found. Please try again later.",
                            "Timeout",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );
                        return;
                    }

                    // Check if process has exited without creating the file
                    if (tinyScrapev2Process.HasExited)
                    {
                        // Give it multiple chances to finish writing, with retries
                        for (int retry = 0; retry < 5; retry++)
                        {
                            await Task.Delay(300);
                            if (File.Exists(hlsUrlsFile))
                            {
                                try
                                {
                                    // Try to read with file size check to ensure it's fully written
                                    var fileInfo = new FileInfo(hlsUrlsFile);
                                    if (fileInfo.Length > 0)
                                    {
                                        // Try reading with retries in case file is still being written
                                        for (int readRetry = 0; readRetry < 3; readRetry++)
                                        {
                                            try
                                            {
                                                m3u8Url = await File.ReadAllTextAsync(hlsUrlsFile);
                                                m3u8Url = m3u8Url.Trim();
                                                
                                                if (!string.IsNullOrEmpty(m3u8Url))
                                                {
                                                    // Clean up the file immediately after reading
                                                    try
                                                    {
                                                        File.Delete(hlsUrlsFile);
                                                    }
                                                    catch { }
                                                    break;
                                                }
                                            }
                                            catch (IOException)
                                            {
                                                // File might still be locked, wait and retry
                                                if (readRetry < 2)
                                                {
                                                    await Task.Delay(100);
                                                }
                                            }
                                        }
                                        
                                        if (!string.IsNullOrEmpty(m3u8Url))
                                        {
                                            break;
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                        
                        // Clean up any leftover file
                        try
                        {
                            if (File.Exists(hlsUrlsFile))
                            {
                                File.Delete(hlsUrlsFile);
                            }
                        }
                        catch { }
                        
                        if (string.IsNullOrEmpty(m3u8Url))
                        {
                            loadingWindow.Close();
                            MessageBox.Show(
                                "No media was found. The hls-urls.txt file was not created or was empty. Please try again later.",
                                "No Media",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information
                            );
                            return;
                        }
                        else
                        {
                            break; // We got the URL, exit the loop
                        }
                    }

                    // Check if file exists and try to read it immediately
                    if (File.Exists(hlsUrlsFile))
                    {
                        try
                        {
                            // Check if file size has changed (indicates it's being written)
                            var fileInfo = new FileInfo(hlsUrlsFile);
                            if (fileInfo.Length > 0 && fileInfo.Length == lastFileSize)
                            {
                                // File size hasn't changed, likely fully written - try to read
                                // Try reading with retries in case of file locks
                                for (int readRetry = 0; readRetry < 5; readRetry++)
                                {
                                    try
                                    {
                                        // Read the file immediately before TinyScrapev2 removes it
                                        m3u8Url = await File.ReadAllTextAsync(hlsUrlsFile);
                                        m3u8Url = m3u8Url.Trim();
                                        
                                        // Validate it's a URL (contains http:// or https://)
                                        if (!string.IsNullOrEmpty(m3u8Url) && 
                                            (m3u8Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                                             m3u8Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                                        {
                                            // Clean up the file immediately after reading
                                            try
                                            {
                                                File.Delete(hlsUrlsFile);
                                            }
                                            catch { }
                                            break; // Exit the loop once we have the URL
                                        }
                                        else
                                        {
                                            // Invalid URL, continue waiting
                                            m3u8Url = null;
                                            break;
                                        }
                                    }
                                    catch (IOException)
                                    {
                                        // File might be locked, wait briefly and retry
                                        if (readRetry < 4)
                                        {
                                            await Task.Delay(50);
                                        }
                                    }
                                    catch (Exception)
                                    {
                                        // Other error, break and continue polling
                                        break;
                                    }
                                }
                                
                                if (!string.IsNullOrEmpty(m3u8Url))
                                {
                                    break; // Successfully read the URL
                                }
                            }
                            else if (fileInfo.Length > 0)
                            {
                                // File size changed, update and continue waiting for it to stabilize
                                lastFileSize = fileInfo.Length;
                            }
                        }
                        catch (Exception)
                        {
                            // Error checking file, continue polling
                        }
                    }

                    // Update loading text with elapsed time (reuse elapsed from timeout check above)
                    timeElapsedText.Text = $"Time elapsed: {elapsed:mm\\:ss}";
                    
                    // Update status text based on elapsed time
                    if (elapsed.TotalSeconds < 5)
                        loadingText.Text = "Initializing...";
                    else if (elapsed.TotalSeconds < 10)
                        loadingText.Text = "Analyzing video source...";
                    else if (elapsed.TotalSeconds < 15)
                        loadingText.Text = "Processing media streams...";
                    else if (elapsed.TotalSeconds < 20)
                        loadingText.Text = "Optimizing playback...";
                    else
                        loadingText.Text = "Almost there...";
                    
                    await Task.Delay(checkInterval);
                }

                // Check if we got a valid URL
                if (string.IsNullOrEmpty(m3u8Url))
                {
                    loadingWindow.Close();
                    MessageBox.Show(
                        "No media was found. Please try again later.",
                        "No Media",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                    return;
                }

                // Close the loading window
                loadingWindow.Close();

                // Get the selected player from settings (reuse existing settingsWindow)
                var selectedPlayer = settingsWindow.SelectedPlayer ?? "TinyPlayer";
                
                // Launch the selected player with the m3u8 URL
                System.Diagnostics.ProcessStartInfo playerStartInfo;
                string playerName;
                
                if (selectedPlayer == "FFPLAY")
                {
                    playerName = "ffplay";
                    playerStartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "ffplay",
                        Arguments = $"\"{m3u8Url}\"",
                        UseShellExecute = false,
                        CreateNoWindow = false,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal
                    };
                }
                else if (selectedPlayer == "VLC")
                {
                    playerName = "VLC";
                    playerStartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = SettingsWindow.GetVlcPath(),
                        Arguments = $"\"{m3u8Url}\"",
                        UseShellExecute = false,
                        CreateNoWindow = false,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal
                    };
                }
                else // Default to TinyPlayer
                {
                    playerName = "TinyPlayer";
                    // TinyPlayer.exe is in TinyPlayer subdirectory
                    var tinyPlayerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TinyPlayer", "TinyPlayer.exe");
                    if (!File.Exists(tinyPlayerPath))
                    {
                        // Fallback to current directory
                        tinyPlayerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TinyPlayer.exe");
                    }
                    
                    playerStartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = tinyPlayerPath,
                        Arguments = $"\"{m3u8Url}\"",
                        UseShellExecute = false,
                        CreateNoWindow = false,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal
                    };
                }

                try
                {
                    System.Diagnostics.Process.Start(playerStartInfo);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to launch {playerName}: {ex.Message}\n\nPlease make sure {playerName} is installed and available.",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error processing movie: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }
    }

    private async void TrailerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var button = (Button)sender;
            var movie = (Movie)button.DataContext;

            // Show loading state
            var loadingWindow = new Window
            {
                Title = "Loading Trailer",
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(15, 15, 15)),
                Foreground = Brushes.White,
                AllowsTransparency = true
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
            loadingGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            loadingGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            loadingGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

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
                Text = "Loading Trailer",
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

            // Store the process reference
            System.Diagnostics.Process? trailerSearchProcess = null;

            // Add event handler for window closing
            loadingWindow.Closing += (s, args) =>
            {
                try
                {
                    // Kill all TrailerSearch processes
                    var processes = System.Diagnostics.Process.GetProcessesByName("TrailerSearch");
                    foreach (var process in processes)
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                process.Kill(true); // Kill the process and its child processes
                                process.WaitForExit(1000); // Wait up to 1 second for the process to exit
                            }
                        }
                        catch
                        {
                            // Ignore errors for individual processes
                        }
                    }
                }
                catch
                {
                    // Ignore any errors during process termination
                }
            };

            loadingCloseButton.Click += (s, args) => loadingWindow.Close();
            Grid.SetColumn(loadingCloseButton, 1);

            loadingTitleBar.Children.Add(loadingTitle);
            loadingTitleBar.Children.Add(loadingCloseButton);
            Grid.SetRow(loadingTitleBar, 0);

            // Movie title
            var movieTitleText = new TextBlock
            {
                Text = movie.Title,
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20, 20, 20, 10)
            };
            Grid.SetRow(movieTitleText, 1);

            // Progress bar
            var progressBar = new ProgressBar
            {
                Height = 4,
                Margin = new Thickness(20, 0, 20, 0),
                Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
                BorderThickness = new Thickness(0),
                Value = 0
            };
            Grid.SetRow(progressBar, 2);

            // Progress bar animation
            var progressAnimation = new DoubleAnimation
            {
                From = 0,
                To = 100,
                Duration = TimeSpan.FromSeconds(30),
                RepeatBehavior = RepeatBehavior.Forever
            };
            progressBar.BeginAnimation(ProgressBar.ValueProperty, progressAnimation);

            // Status text
            var loadingText = new TextBlock
            {
                Text = "Searching for trailer...",
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 20)
            };
            Grid.SetRow(loadingText, 3);

            // Time elapsed
            var timeElapsedText = new TextBlock
            {
                Text = "Time elapsed: 00:00",
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20),
                FontSize = 12
            };
            Grid.SetRow(timeElapsedText, 4);

            loadingGrid.Children.Add(loadingTitleBar);
            loadingGrid.Children.Add(movieTitleText);
            loadingGrid.Children.Add(progressBar);
            loadingGrid.Children.Add(loadingText);
            loadingGrid.Children.Add(timeElapsedText);
            loadingBorder.Child = loadingGrid;
            loadingWindow.Content = loadingBorder;

            // Add drag functionality
            loadingTitleBar.MouseLeftButtonDown += (s, args) =>
            {
                loadingWindow.DragMove();
            };

            // Start loading window
            loadingWindow.Show();

            // Start TrailerSearch.exe
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "TrailerSearch.exe",
                Arguments = $"\"{movie.Title} {movie.Year}\" -year \"{movie.Year}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            trailerSearchProcess = System.Diagnostics.Process.Start(startInfo);
            if (trailerSearchProcess == null)
            {
                loadingWindow.Close();
                MessageBox.Show(
                    "Failed to start TrailerSearch process.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                return;
            }

            var startTime = DateTime.Now;
            var checkInterval = TimeSpan.FromSeconds(1);

            while (!trailerSearchProcess.HasExited)
            {
                // Update loading text with elapsed time
                var elapsed = DateTime.Now - startTime;
                timeElapsedText.Text = $"Time elapsed: {elapsed:mm\\:ss}";
                
                // Update status text based on elapsed time
                if (elapsed.TotalSeconds < 5)
                    loadingText.Text = "Searching for trailer...";
                else if (elapsed.TotalSeconds < 10)
                    loadingText.Text = "Processing video source...";
                else if (elapsed.TotalSeconds < 15)
                    loadingText.Text = "Preparing playback...";
                else
                    loadingText.Text = "Almost there...";
                
                await Task.Delay(checkInterval);
            }

            // Close the loading window
            loadingWindow.Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error loading trailer: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private async void RokuButton_Click(object sender, RoutedEventArgs e)
    {
        if (MoviesListView.SelectedItem is Movie selectedMovie)
        {
            // Clean up any existing temp files first
            CleanupTempFiles();

            var settingsWindow = new SettingsWindow();
            if (string.IsNullOrWhiteSpace(settingsWindow.RokuIpAddress))
            {
                MessageBox.Show(
                    "Please set your Roku IP address in Settings first.",
                    "Roku IP Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            try
            {
                // Check if TinyScraper is already running
                var existingProcesses = System.Diagnostics.Process.GetProcessesByName("TinyScraper");
                if (existingProcesses.Length > 0)
                {
                    MessageBox.Show(
                        "TinyScraper is already running. Please wait for it to finish or close it manually.",
                        "Warning",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    return;
                }

                // Show loading state
                var loadingWindow = new Window
                {
                    Title = "Sending to Roku",
                    Width = 400,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    Background = new SolidColorBrush(Color.FromRgb(15, 15, 15)),
                    Foreground = Brushes.White,
                    AllowsTransparency = true
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
                loadingGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                loadingGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                loadingGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

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
                    Text = "Sending to Roku",
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

                // Store the process reference
                System.Diagnostics.Process? tinyScraperProcess = null;

                // Add event handler for window closing
                loadingWindow.Closing += (s, args) =>
                {
                    try
                    {
                        // Kill all TinyScraper processes
                        var processes = System.Diagnostics.Process.GetProcessesByName("TinyScraper");
                        foreach (var process in processes)
                        {
                            try
                            {
                                if (!process.HasExited)
                                {
                                    process.Kill(true); // Kill the process and its child processes
                                    process.WaitForExit(1000); // Wait up to 1 second for the process to exit
                                }
                            }
                            catch
                            {
                                // Ignore errors for individual processes
                            }
                        }
                    }
                    catch
                    {
                        // Ignore any errors during process termination
                    }
                };

                loadingCloseButton.Click += (s, args) => loadingWindow.Close();
                Grid.SetColumn(loadingCloseButton, 1);

                loadingTitleBar.Children.Add(loadingTitle);
                loadingTitleBar.Children.Add(loadingCloseButton);
                Grid.SetRow(loadingTitleBar, 0);

                // Movie title with TV icon
                var titleStack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(20, 20, 20, 10)
                };

                var tvIcon = new FontAwesome.WPF.FontAwesome
                {
                    Icon = FontAwesome.WPF.FontAwesomeIcon.Tv,
                    Foreground = Brushes.White,
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(0, 0, 10, 0)
                };

                var movieTitleText = new TextBlock
                {
                    Text = selectedMovie.Title,
                    Foreground = Brushes.White,
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center
                };

                titleStack.Children.Add(tvIcon);
                titleStack.Children.Add(movieTitleText);
                Grid.SetRow(titleStack, 1);

                // Progress bar with Roku purple color
                var progressBar = new ProgressBar
                {
                    Height = 4,
                    Margin = new Thickness(20, 0, 20, 0),
                    Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
                    BorderThickness = new Thickness(0),
                    Value = 0
                };

                // Set Roku purple color for the progress bar
                var rokuPurple = new SolidColorBrush(Color.FromRgb(102, 45, 145)); // Roku's purple color
                progressBar.Foreground = rokuPurple;
                Grid.SetRow(progressBar, 2);

                // Progress bar animation
                var progressAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = 100,
                    Duration = TimeSpan.FromSeconds(30),
                    RepeatBehavior = RepeatBehavior.Forever
                };
                progressBar.BeginAnimation(ProgressBar.ValueProperty, progressAnimation);

                // Status text
                var loadingText = new TextBlock
                {
                    Text = "Initializing...",
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 20)
                };
                Grid.SetRow(loadingText, 3);

                // Time elapsed
                var timeElapsedText = new TextBlock
                {
                    Text = "Time elapsed: 00:00",
                    Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 20),
                    FontSize = 12
                };
                Grid.SetRow(timeElapsedText, 4);

                loadingGrid.Children.Add(loadingTitleBar);
                loadingGrid.Children.Add(titleStack);
                loadingGrid.Children.Add(progressBar);
                loadingGrid.Children.Add(loadingText);
                loadingGrid.Children.Add(timeElapsedText);
                loadingBorder.Child = loadingGrid;
                loadingWindow.Content = loadingBorder;

                // Add drag functionality
                loadingTitleBar.MouseLeftButtonDown += (s, args) =>
                {
                    loadingWindow.DragMove();
                };

                // Start loading window
                loadingWindow.Show();

                // Start TinyScraper.exe with the movie URL and Roku flags
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "TinyScraper.exe",
                    Arguments = $"-getm3 \"{selectedMovie.Url}\"" + 
                              (settingsWindow.IsFastModeEnabled ? " -fast" : "") + 
                              $" -rokusl {settingsWindow.RokuIpAddress}",
                    UseShellExecute = false,
                    CreateNoWindow = settingsWindow.HideTinyScraper,
                    WindowStyle = settingsWindow.HideTinyScraper ? 
                        System.Diagnostics.ProcessWindowStyle.Hidden : 
                        System.Diagnostics.ProcessWindowStyle.Normal
                };

                tinyScraperProcess = System.Diagnostics.Process.Start(startInfo);
                if (tinyScraperProcess == null)
                {
                    loadingWindow.Close();
                    MessageBox.Show(
                        "Failed to start TinyScraper process.",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    return;
                }

                // Wait for either ClickedMovieTemp.txt or nomedia.txt to appear
                var tempFile = "ClickedMovieTemp.txt";
                var noMediaFile = "nomedia.txt";
                var startTime = DateTime.Now;
                var checkInterval = TimeSpan.FromSeconds(1);

                while (!File.Exists(tempFile) && !File.Exists(noMediaFile))
                {
                    // Check if process has exited
                    if (tinyScraperProcess.HasExited)
                    {
                        loadingWindow.Close();
                        MessageBox.Show(
                            "No media was found. Please try again later.",
                            "No Media",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );
                        return;
                    }

                    // Update loading text with elapsed time
                    var elapsed = DateTime.Now - startTime;
                    timeElapsedText.Text = $"Time elapsed: {elapsed:mm\\:ss}";
                    
                    // Update status text based on elapsed time
                    if (elapsed.TotalSeconds < 5)
                        loadingText.Text = "Initializing Roku connection...";
                    else if (elapsed.TotalSeconds < 10)
                        loadingText.Text = "Analyzing video source...";
                    else if (elapsed.TotalSeconds < 15)
                        loadingText.Text = "Preparing for Roku...";
                    else if (elapsed.TotalSeconds < 20)
                        loadingText.Text = "Sending to Roku...";
                    else
                        loadingText.Text = "Almost there...";
                    
                    await Task.Delay(checkInterval);
                }

                // Close the loading window
                loadingWindow.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error launching TinyScraper: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }
    }

    private void ShuffleButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Create a random number generator
            var random = new Random();
            
            // Shuffle the entire list using Fisher-Yates algorithm
            for (int i = _allMovies.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (_allMovies[i], _allMovies[j]) = (_allMovies[j], _allMovies[i]);
            }
            
            // Clear current visible movies
            _movies.Clear();
            _currentIndex = 0;
            
            // Reload the initial batch of movies
            var initialBatch = _allMovies.Take(BatchSize).ToList();
            foreach (var movie in initialBatch)
            {
                _movies.Add(movie);
                // Start loading the image asynchronously
                _ = movie.LoadImageAsync();
            }
            _currentIndex = initialBatch.Count;
            
            // Scroll to top
            var scrollViewer = FindVisualChild<ScrollViewer>(MoviesListView);
            if (scrollViewer != null)
            {
                scrollViewer.ScrollToTop();
            }
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

    private void SortMovies(string sortBy, bool ascending)
    {
        try
        {
            // Sort the entire list
            switch (sortBy)
            {
                case "Year":
                    if (ascending)
                    {
                        _allMovies.Sort((a, b) => string.Compare(a.Year, b.Year, StringComparison.Ordinal));
                    }
                    else
                    {
                        _allMovies.Sort((a, b) => string.Compare(b.Year, a.Year, StringComparison.Ordinal));
                    }
                    break;
                case "Genre":
                    if (ascending)
                    {
                        _allMovies.Sort((a, b) => string.Compare(a.Genre, b.Genre, StringComparison.Ordinal));
                    }
                    else
                    {
                        _allMovies.Sort((a, b) => string.Compare(b.Genre, a.Genre, StringComparison.Ordinal));
                    }
                    break;
                case "Country":
                    if (ascending)
                    {
                        _allMovies.Sort((a, b) => string.Compare(a.Country, b.Country, StringComparison.Ordinal));
                    }
                    else
                    {
                        _allMovies.Sort((a, b) => string.Compare(b.Country, a.Country, StringComparison.Ordinal));
                    }
                    break;
            }

            // Clear current visible movies
            _movies.Clear();
            _currentIndex = 0;

            // Reload the initial batch of movies
            var initialBatch = _allMovies.Take(BatchSize).ToList();
            foreach (var movie in initialBatch)
            {
                _movies.Add(movie);
                // Start loading the image asynchronously
                _ = movie.LoadImageAsync();
            }
            _currentIndex = initialBatch.Count;

            // Scroll to top
            var scrollViewer = FindVisualChild<ScrollViewer>(MoviesListView);
            if (scrollViewer != null)
            {
                scrollViewer.ScrollToTop();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error sorting movies: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CleanupTempFiles()
    {
        try
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var tempFiles = new[] 
            { 
                Path.Combine(baseDirectory, "ClickedMovieTemp.txt"), 
                Path.Combine(baseDirectory, "nomedia.txt"), 
                Path.Combine(baseDirectory, "hls-urls.txt") 
            };
            foreach (var file in tempFiles)
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var movie = (Movie)button.DataContext;
        
        // Toggle favorite status
        movie.IsFavorite = !movie.IsFavorite;
        
        // Save favorites to file
        SaveFavorites();
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

    private void FavoritesButton_Click(object sender, RoutedEventArgs e)
    {
        _showFavoritesOnly = !_showFavoritesOnly;
        
        // Update button appearance to show active state
        if (_showFavoritesOnly)
        {
            FavoritesIcon.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38)); // Red when active
            FavoritesButton.ToolTip = "Show All Movies";
        }
        else
        {
            FavoritesIcon.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)); // White when inactive
            FavoritesButton.ToolTip = "Show Favorites";
        }
        
        ApplyFilters();
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
    private BitmapImage _cachedImage;
    private bool _isLoading;
    private bool _isFavorite;

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            _isFavorite = value;
            OnPropertyChanged(nameof(IsFavorite));
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
    private static SettingsWindow _settingsWindow;
    private static readonly object _lock = new object();

    private static SettingsWindow GetSettingsWindow()
    {
        if (_settingsWindow == null)
        {
            lock (_lock)
            {
                if (_settingsWindow == null)
                {
                    _settingsWindow = new SettingsWindow();
                }
            }
        }
        return _settingsWindow;
    }

    public static void Cleanup()
    {
        _imageCache.Clear();
        if (_settingsWindow != null)
        {
            _settingsWindow.Close();
            _settingsWindow = null;
        }
    }

    public static async Task<BitmapImage> GetCachedImageAsync(string imageUrl)
    {
        try
        {
            var settingsWindow = GetSettingsWindow();

            // If caching is disabled, load directly from URL
            if (!settingsWindow.IsCachingEnabled)
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

            string cacheFilePath = Path.Combine(settingsWindow.CacheLocation, cacheFileName + ".jpg");

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
        bool boolValue = (bool)value;
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