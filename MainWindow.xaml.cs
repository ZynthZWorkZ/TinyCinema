﻿using System;
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
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using OpenQA.Selenium;
using Serilog;
using System.Threading;

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
            var sourceList = string.IsNullOrWhiteSpace(_lastSearchText) && string.IsNullOrEmpty(_selectedGenre) && string.IsNullOrEmpty(_selectedCountry)
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
                .ToArray()))
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
                    // Get movie details in background with cancellation support
                    var (description, genre) = await Task.Run(() => GetMovieDetails(selectedMovie.Url, cts.Token));

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

                // Start TinyScraper.exe with the movie URL
                var settingsWindow = new SettingsWindow();
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "TinyScraper.exe",
                    Arguments = $"-getm3 \"{selectedMovie.Url}\"" + (settingsWindow.IsFastModeEnabled ? " -fast" : ""),
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

                // Check which file appeared
                if (File.Exists(noMediaFile))
                {
                    // Clean up the no media file
                    try
                    {
                        File.Delete(noMediaFile);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }

                    loadingWindow.Close();
                    MessageBox.Show(
                        "No media was found. Please try again later.",
                        "No Media",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                    return;
                }

                // Wait a bit more to ensure the file is completely written
                await Task.Delay(2000);

                // Read the m3u8 URL from the file
                var m3u8Url = await File.ReadAllTextAsync(tempFile);
                m3u8Url = m3u8Url.Trim();

                // Clean up the temp file
                try
                {
                    File.Delete(tempFile);
                }
                catch
                {
                    // Ignore cleanup errors
                }

                // Close the loading window
                loadingWindow.Close();

                // Launch ffplay with the m3u8 URL
                var ffplayStartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffplay",
                    Arguments = $"\"{m3u8Url}\"",
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal
                };

                try
                {
                    System.Diagnostics.Process.Start(ffplayStartInfo);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to launch ffplay: {ex.Message}\n\nPlease make sure ffplay is installed and available in your system PATH.",
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
            var tempFiles = new[] { "ClickedMovieTemp.txt", "nomedia.txt" };
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

    private static (string description, string genre) GetMovieDetails(string url, CancellationToken cancellationToken)
    {
        try
        {
            var options = new ChromeOptions();
            options.AddArgument("--headless=new"); // Use new headless mode
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--window-size=1920,1080");
            options.AddArgument("--hide-scrollbars");
            options.AddArgument("--disable-extensions");
            options.AddArgument("--disable-notifications");
            options.AddArgument("--disable-infobars");
            options.AddArgument("--disable-logging");
            options.AddArgument("--log-level=3"); // Only show fatal errors
            options.AddArgument("--silent");
            options.AddExcludedArgument("enable-automation"); // Remove automation flag
            options.AddAdditionalOption("useAutomationExtension", false);

            var service = ChromeDriverService.CreateDefaultService();
            service.HideCommandPromptWindow = true; // Hide the console window

            using (var driver = new ChromeDriver(service, options))
            {
                // Check for cancellation before navigating
                cancellationToken.ThrowIfCancellationRequested();
                driver.Navigate().GoToUrl(url);

                // Wait for page to load
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

                // Get description
                string description = "";
                try
                {
                    // Check for cancellation before getting description
                    cancellationToken.ThrowIfCancellationRequested();
                    var descElement = wait.Until(d => d.FindElement(By.CssSelector("div.description")));
                    description = descElement.Text.Trim();
                }
                catch
                {
                    Log.Information("Could not find movie description");
                }

                // Get genre
                string genre = "";
                try
                {
                    // Check for cancellation before getting genre
                    cancellationToken.ThrowIfCancellationRequested();
                    var genreElement = wait.Until(d => d.FindElement(By.CssSelector(".col-xl-7.col-lg-7.col-md-8.col-sm-12")));
                    genre = genreElement.Text.Trim();
                }
                catch
                {
                    Log.Information("Could not find movie genre");
                }

                return (description, genre);
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