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

namespace TinyCinema;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    // Win32 API for folder selection
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr ILCreateFromPath([MarshalAs(UnmanagedType.LPTStr)] string pszPath);

    [DllImport("shell32.dll", CharSet = CharSet.None, ExactSpelling = false)]
    private static extern void ILFree(IntPtr pidl);

    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int SHILCreateFromPath([MarshalAs(UnmanagedType.LPTStr)] string pszPath, out IntPtr ppIdl, ref uint rgfInOut);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern int SHCreateItemFromIDList(IntPtr pidl, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);

    private const int MAX_PATH = 260;
    private static readonly Guid IID_IShellItem = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    private const int BatchSize = 50;
    private readonly ObservableCollection<Movie> _movies;
    private readonly List<Movie> _allMovies;
    private bool _isLoading;
    private int _currentIndex;
    private Point _lastMousePosition;
    private bool _isDragging;
    private string _lastSearchText = string.Empty;
    private static readonly Dictionary<string, BitmapImage> _imageCache = new();
    private string _cacheLocation;
    private int _movieCount;
    private bool _isCachingEnabled;

    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TinyCinema",
        "settings.json"
    );

    public bool IsCachingEnabled
    {
        get => _isCachingEnabled;
        set
        {
            _isCachingEnabled = value;
            OnPropertyChanged(nameof(IsCachingEnabled));
            SaveSettings();
        }
    }

    public int MovieCount
    {
        get => _movieCount;
        private set
        {
            _movieCount = value;
            OnPropertyChanged(nameof(MovieCount));
        }
    }

    public string CacheLocation
    {
        get => _cacheLocation;
        private set
        {
            _cacheLocation = value;
            OnPropertyChanged(nameof(CacheLocation));
            SaveSettings();
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
            
            LoadSettings();
            InitializeCacheDirectory();
            
            LoadMoviesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error in constructor: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var settings = File.ReadAllText(SettingsFile);
                var lines = settings.Split('\n');
                foreach (var line in lines)
                {
                    if (line.StartsWith("CacheLocation="))
                    {
                        CacheLocation = line.Substring("CacheLocation=".Length).Trim();
                    }
                    else if (line.StartsWith("IsCachingEnabled="))
                    {
                        IsCachingEnabled = bool.Parse(line.Substring("IsCachingEnabled=".Length).Trim());
                    }
                }
            }
        }
        catch
        {
            // If settings can't be loaded, use defaults
        }

        if (string.IsNullOrEmpty(CacheLocation))
        {
            CacheLocation = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TinyCinema",
                "ImageCache"
            );
        }
    }

    private void SaveSettings()
    {
        try
        {
            var settingsDir = Path.GetDirectoryName(SettingsFile);
            if (!Directory.Exists(settingsDir))
            {
                Directory.CreateDirectory(settingsDir);
            }

            var settings = new StringBuilder();
            settings.AppendLine($"CacheLocation={CacheLocation}");
            settings.AppendLine($"IsCachingEnabled={IsCachingEnabled}");
            File.WriteAllText(SettingsFile, settings.ToString());
        }
        catch
        {
            // Silently handle settings save errors
        }
    }

    private void InitializeCacheDirectory()
    {
        try
        {
            if (!Directory.Exists(CacheLocation))
            {
                Directory.CreateDirectory(CacheLocation);
            }
        }
        catch
        {
            // If custom location fails, fall back to default
            CacheLocation = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TinyCinema",
                "ImageCache"
            );
            Directory.CreateDirectory(CacheLocation);
        }
    }

    private void SelectCacheLocation_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Select Cache Location",
            Filter = "All Files|*.*",
            FileName = "Select Folder",
            CheckFileExists = false,
            CheckPathExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            var selectedPath = Path.GetDirectoryName(dialog.FileName);
            if (!string.IsNullOrEmpty(selectedPath))
            {
                try
                {
                    // Test if we can write to the directory
                    var testFile = Path.Combine(selectedPath, "test.tmp");
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);

                    // If successful, update cache location
                    CacheLocation = selectedPath;
                    InitializeCacheDirectory();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Cannot use selected location: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    public static async Task<BitmapImage> GetCachedImageAsync(string imageUrl)
    {
        try
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow == null) return null;

            // If caching is disabled, load directly from URL
            if (!mainWindow.IsCachingEnabled)
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

            string cacheFilePath = Path.Combine(mainWindow.CacheLocation, cacheFileName + ".jpg");

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

    private async void LoadMoviesAsync()
    {
        try
        {
            var filePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "movie_links.txt");
            
            if (!File.Exists(filePath))
            {
                return;
            }

            // Read all lines but don't process them yet
            var lines = await File.ReadAllLinesAsync(filePath);
            _currentIndex = 0;

            // Process all movies first
            foreach (var line in lines)
            {
                var parts = line.Split('|').Select(p => p.Trim()).ToArray();
                if (parts.Length == 4)
                {
                    _allMovies.Add(new Movie
                    {
                        Year = parts[0],
                        Title = parts[1],
                        Url = parts[2],
                        ImageUrl = parts[3]
                    });
                }
            }

            // Update movie count
            MovieCount = _allMovies.Count;

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
            // Silently handle the error
        }
    }

    private async Task LoadNextBatchAsync()
    {
        if (_isLoading) return;

        _isLoading = true;
        await Task.Run(async () =>
        {
            if (!string.IsNullOrWhiteSpace(_lastSearchText))
            {
                var searchTerms = _lastSearchText.Split(new[] { ' ', '-', '_', '.', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                               .Where(term => term.Length >= 2)
                                               .ToArray();

                if (searchTerms.Length > 0)
                {
                    var filteredMovies = _allMovies
                        .Where(m => IsMatch(m, searchTerms))
                        .OrderByDescending(m => GetMatchScore(m, searchTerms))
                        .Skip(_currentIndex)
                        .Take(BatchSize)
                        .ToList();

                    await Dispatcher.Invoke(async () =>
                    {
                        foreach (var movie in filteredMovies)
                        {
                            _movies.Add(movie);
                            // Start loading the image asynchronously
                            _ = movie.LoadImageAsync();
                        }
                    });

                    _currentIndex += filteredMovies.Count;
                }
            }
            else
        {
            var endIndex = Math.Min(_currentIndex + BatchSize, _allMovies.Count);
            var batch = _allMovies.Skip(_currentIndex).Take(endIndex - _currentIndex).ToList();

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
            }
        });
        _isLoading = false;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var searchText = SearchBox.Text.ToLower().Trim();
        
        // Don't re-search if the text hasn't changed
        if (searchText == _lastSearchText) return;
        _lastSearchText = searchText;
        
        // Clear current items
        _movies.Clear();
        _currentIndex = 0;

        if (string.IsNullOrWhiteSpace(searchText))
        {
            // If search is empty, immediately load initial batch
            var initialBatch = _allMovies.Take(BatchSize).ToList();
            foreach (var movie in initialBatch)
            {
                _movies.Add(movie);
            }
            _currentIndex = initialBatch.Count;
            return;
        }

        // Split search terms and remove empty entries
        var searchTerms = searchText.Split(new[] { ' ', '-', '_', '.', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Where(term => term.Length >= 2)
                                  .ToArray();

        if (searchTerms.Length == 0)
        {
            // If no valid search terms, immediately load initial batch
            var initialBatch = _allMovies.Take(BatchSize).ToList();
            foreach (var movie in initialBatch)
        {
            _movies.Add(movie);
            }
            _currentIndex = initialBatch.Count;
            return;
        }

        // Load first batch of matching items
        LoadNextBatchAsync();
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

    private void MoviesListView_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isDragging = true;
        _lastMousePosition = e.GetPosition(MoviesListView);
        MoviesListView.CaptureMouse();
    }

    private void MoviesListView_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
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

    private void MoviesListView_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
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
        Close();
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
        SettingsPanel.Visibility = Visibility.Visible;
    }

    private void CloseSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsPanel.Visibility = Visibility.Collapsed;
    }
}

public class Movie : INotifyPropertyChanged
{
    public required string Year { get; set; }
    public required string Title { get; set; }
    public required string Url { get; set; }
    public required string ImageUrl { get; set; }
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
            var image = await MainWindow.GetCachedImageAsync(ImageUrl);
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

public class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return (bool)value ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return (Visibility)value == Visibility.Collapsed;
    }
}