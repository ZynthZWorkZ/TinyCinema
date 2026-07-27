using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;

namespace TinyCinema;

public partial class SettingsWindow : Window, INotifyPropertyChanged
{
    private string _cacheLocation;
    private bool _isCachingEnabled;
    private bool _isTvShowCachingEnabled = true;
    private bool _isPopupBlockerEnabled = true;
    private bool _isClearPlayerBrowserDataOnClose;
    private bool _isMovieLairProbeEnabled;
    private bool _isStartCentered = true;
    private string _movieCatalogLocation;
    private string _tvShowLinksLocation;
    private string _movieLairShowsUrl = "https://movielair.cc/shows/10759/";
    private string _rokuIpAddress = "";
    private string _rokuUsername = "rokudev";
    private string _rokuPassword = "";
    private string _selectedPlayer = PlayerNames.InAppBrowser;
    private string _tinyZoneBaseUrl = "https://ww5.tinyzone.org";
    private string _tmdbApiKey = "";
    private string _selectedAppTheme = "Black";
    private AppWindowSize _selectedWindowSize = AppWindowSize.Standard;
    private List<string> _availablePlayers;
    private List<string> _availableThemes = ThemeManager.GetAvailableDisplayNames().ToList();
    private List<string> _availableWindowSizes = AppLayoutManager.AvailableDisplayNames.ToList();

    public static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TinyCinema",
        "settings.json"
    );

    private static readonly string SettingsFile = SettingsFilePath;
    
    private static readonly string VlcPath = @"C:\Program Files\VideoLAN\VLC\vlc.exe";

    public bool IsPopupBlockerEnabled
    {
        get => _isPopupBlockerEnabled;
        set
        {
            _isPopupBlockerEnabled = value;
            OnPropertyChanged(nameof(IsPopupBlockerEnabled));
            SaveSettings();
        }
    }

    public bool IsClearPlayerBrowserDataOnClose
    {
        get => _isClearPlayerBrowserDataOnClose;
        set
        {
            _isClearPlayerBrowserDataOnClose = value;
            OnPropertyChanged(nameof(IsClearPlayerBrowserDataOnClose));
            SaveSettings();
        }
    }

    public bool IsMovieLairProbeEnabled
    {
        get => _isMovieLairProbeEnabled;
        set
        {
            _isMovieLairProbeEnabled = value;
            OnPropertyChanged(nameof(IsMovieLairProbeEnabled));
            SaveSettings();
        }
    }

    public bool IsStartCentered
    {
        get => _isStartCentered;
        set
        {
            _isStartCentered = value;
            OnPropertyChanged(nameof(IsStartCentered));
            SaveSettings();
        }
    }

    public string MovieCatalogLocation
    {
        get => _movieCatalogLocation;
        set
        {
            _movieCatalogLocation = value;
            OnPropertyChanged(nameof(MovieCatalogLocation));
            SaveSettings();
        }
    }

    public string TvShowLinksLocation
    {
        get => _tvShowLinksLocation;
        set
        {
            _tvShowLinksLocation = value;
            OnPropertyChanged(nameof(TvShowLinksLocation));
            SaveSettings();
        }
    }

    public string MovieLairShowsUrl
    {
        get => _movieLairShowsUrl;
        set
        {
            _movieLairShowsUrl = value;
            OnPropertyChanged(nameof(MovieLairShowsUrl));
            SaveSettings();
        }
    }

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

    public bool IsTvShowCachingEnabled
    {
        get => _isTvShowCachingEnabled;
        set
        {
            _isTvShowCachingEnabled = value;
            OnPropertyChanged(nameof(IsTvShowCachingEnabled));
            SaveSettings();
        }
    }

    public string TvShowEpisodeCacheLocation { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TinyCinema",
        "TvShowEpisodeCache");

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

    public string RokuIpAddress
    {
        get => _rokuIpAddress;
        set
        {
            _rokuIpAddress = value;
            OnPropertyChanged(nameof(RokuIpAddress));
            SaveSettings();
        }
    }

    public string RokuUsername
    {
        get => _rokuUsername;
        set
        {
            _rokuUsername = value;
            OnPropertyChanged(nameof(RokuUsername));
            SaveSettings();
        }
    }

    public string RokuPassword
    {
        get => _rokuPassword;
        set
        {
            _rokuPassword = value;
            OnPropertyChanged(nameof(RokuPassword));
            SaveSettings();
        }
    }

    public string SelectedPlayer
    {
        get => _selectedPlayer;
        set
        {
            _selectedPlayer = value;
            OnPropertyChanged(nameof(SelectedPlayer));
            SaveSettings();
        }
    }

    public string TinyZoneBaseUrl
    {
        get => _tinyZoneBaseUrl;
        set
        {
            _tinyZoneBaseUrl = value;
            OnPropertyChanged(nameof(TinyZoneBaseUrl));
            SaveSettings();
        }
    }

    public string TmdbApiKey
    {
        get => _tmdbApiKey;
        set
        {
            _tmdbApiKey = value;
            OnPropertyChanged(nameof(TmdbApiKey));
            SaveSettings();
        }
    }

    public List<string> AvailableThemes
    {
        get => _availableThemes;
        private set
        {
            _availableThemes = value;
            OnPropertyChanged(nameof(AvailableThemes));
        }
    }

    public string SelectedAppTheme
    {
        get => _selectedAppTheme;
        set
        {
            var normalized = NormalizeThemeDisplayName(value);
            if (_selectedAppTheme == normalized)
                return;

            _selectedAppTheme = normalized;
            ThemeManager.ApplyTheme(ThemeManager.ParseTheme(normalized));
            OnPropertyChanged(nameof(SelectedAppTheme));
            SaveSettings();

            if (Owner is MainWindow mainWindow)
                mainWindow.RefreshTheme();
        }
    }

    private static string NormalizeThemeDisplayName(string? value) =>
        ThemeManager.GetDisplayName(ThemeManager.ParseTheme(value));

    public List<string> AvailableWindowSizes
    {
        get => _availableWindowSizes;
        private set
        {
            _availableWindowSizes = value;
            OnPropertyChanged(nameof(AvailableWindowSizes));
        }
    }

    public string SelectedWindowSize
    {
        get => AppLayoutManager.GetDisplayName(_selectedWindowSize);
        set
        {
            var parsed = AppLayoutManager.ParseDisplayName(value);
            if (_selectedWindowSize == parsed)
                return;

            _selectedWindowSize = parsed;
            OnPropertyChanged(nameof(SelectedWindowSize));
            OnPropertyChanged(nameof(WindowSizeDescription));
            SaveSettings();
            AppLayoutManager.SetSize(parsed, persist: false);
        }
    }

    public string WindowSizeDescription =>
        AppLayoutManager.GetProfile(_selectedWindowSize).Description +
        $" ({Math.Round(AppLayoutManager.GetProfile(_selectedWindowSize).WindowWidth)}×{Math.Round(AppLayoutManager.GetProfile(_selectedWindowSize).WindowHeight)})";

    public List<string> AvailablePlayers
    {
        get => _availablePlayers;
        private set
        {
            _availablePlayers = value;
            OnPropertyChanged(nameof(AvailablePlayers));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = this;
        DetectAvailablePlayers();
        LoadSettings();
        InitializeCacheDirectory();
        
        // Initialize player ComboBox
        if (PlayerComboBox != null)
        {
            PlayerComboBox.ItemsSource = AvailablePlayers;
            // Ensure selected player is still available, otherwise use first available
            if (!AvailablePlayers.Contains(SelectedPlayer) && AvailablePlayers.Count > 0)
            {
                SelectedPlayer = AvailablePlayers[0];
            }
            PlayerComboBox.SelectedItem = SelectedPlayer;
        }

        if (ThemeComboBox != null)
        {
            ThemeComboBox.ItemsSource = AvailableThemes;
            ThemeComboBox.SelectedItem = SelectedAppTheme;
        }

        if (WindowSizeComboBox != null)
        {
            WindowSizeComboBox.ItemsSource = AvailableWindowSizes;
            WindowSizeComboBox.SelectedItem = SelectedWindowSize;
        }

        UpdateSmartSearchStatus();
    }

    private void UpdateSmartSearchStatus()
    {
        if (SmartSearchStatusText == null)
            return;

        SmartSearchStatusText.Text = SmartSearchCoordinator.GetStatusText(MovieCatalogLocation);
    }
    
    private void DetectAvailablePlayers()
    {
        var players = new List<string>
        {
            PlayerNames.InAppBrowser,
            PlayerNames.TinyPlayer,
            PlayerNames.FFPLAY
        };
        
        if (File.Exists(VlcPath))
            players.Add(PlayerNames.VLC);
        
        AvailablePlayers = players;
    }
    
    public static bool IsVlcInstalled()
    {
        return File.Exists(VlcPath);
    }
    
    public static string GetVlcPath()
    {
        return VlcPath;
    }
    
    private void PlayerComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (PlayerComboBox?.SelectedItem is string selectedPlayer)
        {
            SelectedPlayer = selectedPlayer;
        }
    }

    private void ThemeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ThemeComboBox?.SelectedItem is string selectedTheme)
        {
            SelectedAppTheme = selectedTheme;
        }
    }

    private void WindowSizeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (WindowSizeComboBox?.SelectedItem is string selectedSize)
        {
            SelectedWindowSize = selectedSize;
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
                    else if (line.StartsWith("IsTvShowCachingEnabled="))
                    {
                        IsTvShowCachingEnabled = bool.Parse(line.Substring("IsTvShowCachingEnabled=".Length).Trim());
                    }
                    else if (line.StartsWith("IsPopupBlockerEnabled="))
                    {
                        IsPopupBlockerEnabled = bool.Parse(line.Substring("IsPopupBlockerEnabled=".Length).Trim());
                    }
                    else if (line.StartsWith("IsClearPlayerBrowserDataOnClose="))
                    {
                        IsClearPlayerBrowserDataOnClose = bool.Parse(line.Substring("IsClearPlayerBrowserDataOnClose=".Length).Trim());
                    }
                    else if (line.StartsWith("IsMovieLairProbeEnabled="))
                    {
                        IsMovieLairProbeEnabled = bool.Parse(line.Substring("IsMovieLairProbeEnabled=".Length).Trim());
                    }
                    else if (line.StartsWith("MovieCatalogLocation="))
                    {
                        MovieCatalogLocation = line.Substring("MovieCatalogLocation=".Length).Trim();
                    }
                    else if (line.StartsWith("MovieLinksLocation="))
                    {
                        MovieCatalogLocation = line.Substring("MovieLinksLocation=".Length).Trim();
                    }
                    else if (line.StartsWith("TvShowLinksLocation="))
                    {
                        TvShowLinksLocation = line.Substring("TvShowLinksLocation=".Length).Trim();
                    }
                    else if (line.StartsWith("MovieLairShowsUrl="))
                    {
                        MovieLairShowsUrl = line.Substring("MovieLairShowsUrl=".Length).Trim();
                    }
                    else if (line.StartsWith("RokuIpAddress="))
                    {
                        RokuIpAddress = line.Substring("RokuIpAddress=".Length).Trim();
                    }
                    else if (line.StartsWith("RokuUsername="))
                    {
                        RokuUsername = line.Substring("RokuUsername=".Length).Trim();
                    }
                    else if (line.StartsWith("RokuPassword="))
                    {
                        RokuPassword = line.Substring("RokuPassword=".Length).Trim();
                    }
                    else if (line.StartsWith("SelectedPlayer="))
                    {
                        var player = line.Substring("SelectedPlayer=".Length).Trim();
                        SelectedPlayer = player switch
                        {
                            "Built-in Browser" or "TinyZone Browser" => PlayerNames.InAppBrowser,
                            _ => player
                        };
                    }
                    else if (line.StartsWith("TinyZoneBaseUrl="))
                    {
                        TinyZoneBaseUrl = line.Substring("TinyZoneBaseUrl=".Length).Trim();
                    }
                    else if (line.StartsWith("TmdbApiKey="))
                    {
                        TmdbApiKey = line.Substring("TmdbApiKey=".Length).Trim();
                    }
                    else if (line.StartsWith("AppTheme="))
                    {
                        _selectedAppTheme = NormalizeThemeDisplayName(line.Substring("AppTheme=".Length).Trim());
                    }
                    else if (line.StartsWith("WindowSize="))
                    {
                        _selectedWindowSize = AppLayoutManager.ParseSize(line.Substring("WindowSize=".Length).Trim());
                    }
                    else if (line.StartsWith("StartCentered="))
                    {
                        _isStartCentered = bool.Parse(line.Substring("StartCentered=".Length).Trim());
                    }
                }

                ThemeManager.ApplyTheme(ThemeManager.ParseTheme(_selectedAppTheme));
                OnPropertyChanged(nameof(SelectedAppTheme));
                OnPropertyChanged(nameof(SelectedWindowSize));
                OnPropertyChanged(nameof(WindowSizeDescription));
                OnPropertyChanged(nameof(IsStartCentered));
                AppLayoutManager.LoadFromSettings();
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

        MovieCatalogLocation = NormalizeMovieCatalogLocation(MovieCatalogLocation);

        if (string.IsNullOrEmpty(TvShowLinksLocation))
        {
            TvShowLinksLocation = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "tv_show_links.txt"
            );
        }
    }

    private void SaveSettings()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFile);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var settings = new[]
            {
                $"CacheLocation={CacheLocation}",
                $"IsCachingEnabled={IsCachingEnabled}",
                $"IsTvShowCachingEnabled={IsTvShowCachingEnabled}",
                $"IsPopupBlockerEnabled={IsPopupBlockerEnabled}",
                $"IsClearPlayerBrowserDataOnClose={IsClearPlayerBrowserDataOnClose}",
                $"IsMovieLairProbeEnabled={IsMovieLairProbeEnabled}",
                $"MovieCatalogLocation={MovieCatalogLocation}",
                $"TvShowLinksLocation={TvShowLinksLocation}",
                $"MovieLairShowsUrl={MovieLairShowsUrl}",
                $"RokuIpAddress={RokuIpAddress}",
                $"RokuUsername={RokuUsername}",
                $"RokuPassword={RokuPassword}",
                $"SelectedPlayer={SelectedPlayer}",
                $"TinyZoneBaseUrl={TinyZoneBaseUrl}",
                $"TmdbApiKey={TmdbApiKey}",
                $"AppTheme={ThemeManager.ToSettingValue(ThemeManager.ParseTheme(SelectedAppTheme))}",
                $"WindowSize={AppLayoutManager.ToSettingValue(_selectedWindowSize)}",
                $"StartCentered={IsStartCentered}"
            };

            File.WriteAllLines(SettingsFile, settings);
        }
        catch
        {
            // If settings can't be saved, ignore
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

    public static bool GetIsPopupBlockerEnabled()
    {
        try
        {
            if (!File.Exists(SettingsFile))
                return true;

            foreach (var line in File.ReadAllLines(SettingsFile))
            {
                if (line.StartsWith("IsPopupBlockerEnabled=", StringComparison.Ordinal) &&
                    bool.TryParse(line.Substring("IsPopupBlockerEnabled=".Length).Trim(), out var enabled))
                {
                    return enabled;
                }
            }
        }
        catch
        {
            // Ignore read errors.
        }

        return true;
    }

    public static bool GetIsClearPlayerBrowserDataOnClose()
    {
        try
        {
            if (!File.Exists(SettingsFile))
                return false;

            foreach (var line in File.ReadAllLines(SettingsFile))
            {
                if (line.StartsWith("IsClearPlayerBrowserDataOnClose=", StringComparison.Ordinal) &&
                    bool.TryParse(line.Substring("IsClearPlayerBrowserDataOnClose=".Length).Trim(), out var enabled))
                {
                    return enabled;
                }
            }
        }
        catch
        {
            // Ignore read errors.
        }

        return false;
    }

    public static bool GetIsMovieLairProbeEnabled()
    {
        try
        {
            if (!File.Exists(SettingsFile))
                return false;

            foreach (var line in File.ReadAllLines(SettingsFile))
            {
                if (line.StartsWith("IsMovieLairProbeEnabled=", StringComparison.Ordinal) &&
                    bool.TryParse(line.Substring("IsMovieLairProbeEnabled=".Length).Trim(), out var enabled))
                {
                    return enabled;
                }
            }
        }
        catch
        {
            // Ignore read errors.
        }

        return false;
    }

    public static string GetMovieCatalogLocation()
    {
        try
        {
            if (!File.Exists(SettingsFile))
                return NormalizeMovieCatalogLocation(null);

            foreach (var line in File.ReadAllLines(SettingsFile))
            {
                if (line.StartsWith("MovieCatalogLocation=", StringComparison.Ordinal))
                    return NormalizeMovieCatalogLocation(line.Substring("MovieCatalogLocation=".Length).Trim());

                if (line.StartsWith("MovieLinksLocation=", StringComparison.Ordinal))
                    return NormalizeMovieCatalogLocation(line.Substring("MovieLinksLocation=".Length).Trim());
            }
        }
        catch
        {
            // Ignore read errors.
        }

        return NormalizeMovieCatalogLocation(null);
    }

    public static AppWindowSize GetWindowSize() => AppLayoutManager.ReadFromSettings();

    public static bool GetStartCentered()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
                return true;

            foreach (var line in File.ReadAllLines(SettingsFilePath))
            {
                if (line.StartsWith("StartCentered=", StringComparison.Ordinal) &&
                    bool.TryParse(line.Substring("StartCentered=".Length).Trim(), out var centered))
                {
                    return centered;
                }
            }
        }
        catch
        {
            // Ignore read errors.
        }

        return true;
    }

    public static void UpdateWindowSizeSetting(AppWindowSize size)
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var lines = File.Exists(SettingsFilePath)
                ? File.ReadAllLines(SettingsFilePath).ToList()
                : [];

            var settingLine = $"WindowSize={AppLayoutManager.ToSettingValue(size)}";
            var index = lines.FindIndex(line => line.StartsWith("WindowSize=", StringComparison.Ordinal));
            if (index >= 0)
                lines[index] = settingLine;
            else
                lines.Add(settingLine);

            File.WriteAllLines(SettingsFilePath, lines);
        }
        catch
        {
            // Ignore write errors.
        }
    }

    public static AppTheme GetAppTheme()
    {
        try
        {
            if (!File.Exists(SettingsFile))
                return AppTheme.Black;

            foreach (var line in File.ReadAllLines(SettingsFile))
            {
                if (line.StartsWith("AppTheme=", StringComparison.Ordinal))
                    return ThemeManager.ParseTheme(line.Substring("AppTheme=".Length).Trim());
            }
        }
        catch
        {
            // Ignore read errors.
        }

        return AppTheme.Black;
    }

    public static string GetTmdbApiKey()
    {
        try
        {
            if (!File.Exists(SettingsFile))
                return "";

            foreach (var line in File.ReadAllLines(SettingsFile))
            {
                if (line.StartsWith("TmdbApiKey=", StringComparison.Ordinal))
                    return line.Substring("TmdbApiKey=".Length).Trim();
            }
        }
        catch
        {
            // Ignore read errors.
        }

        return "";
    }

    public static bool GetIsTvShowCachingEnabled()
    {
        try
        {
            if (!File.Exists(SettingsFile))
                return true;

            foreach (var line in File.ReadAllLines(SettingsFile))
            {
                if (line.StartsWith("IsTvShowCachingEnabled=", StringComparison.Ordinal))
                    return bool.Parse(line.Substring("IsTvShowCachingEnabled=".Length).Trim());
            }
        }
        catch
        {
            // Ignore read errors.
        }

        return true;
    }

    public static (string Ip, string Username, string Password) GetRokuCredentials()
    {
        var ip = "";
        var username = "rokudev";
        var password = "";

        try
        {
            if (!File.Exists(SettingsFile))
                return (ip, username, password);

            foreach (var line in File.ReadAllLines(SettingsFile))
            {
                if (line.StartsWith("RokuIpAddress=", StringComparison.Ordinal))
                    ip = line.Substring("RokuIpAddress=".Length).Trim();
                else if (line.StartsWith("RokuUsername=", StringComparison.Ordinal))
                    username = line.Substring("RokuUsername=".Length).Trim();
                else if (line.StartsWith("RokuPassword=", StringComparison.Ordinal))
                    password = line.Substring("RokuPassword=".Length).Trim();
            }
        }
        catch
        {
            // Ignore read errors.
        }

        if (string.IsNullOrWhiteSpace(username))
            username = "rokudev";

        return (ip, username, password);
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

    private static string NormalizeMovieCatalogLocation(string? configuredPath)
    {
        var defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Movies.json");

        if (string.IsNullOrWhiteSpace(configuredPath))
            return defaultPath;

        if (configuredPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return configuredPath;

        if (configuredPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            var jsonSibling = Path.Combine(
                Path.GetDirectoryName(configuredPath) ?? AppDomain.CurrentDomain.BaseDirectory,
                "Movies.json");

            if (File.Exists(jsonSibling))
                return jsonSibling;
        }

        return configuredPath;
    }

    private void SelectMovieCatalogLocation_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Movie Catalog File",
            Filter = "JSON Files|*.json|All Files|*.*",
            CheckFileExists = true,
            CheckPathExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                _ = MovieCatalogStore.LoadAsync(dialog.FileName).GetAwaiter().GetResult();
                MovieCatalogLocation = dialog.FileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Cannot use selected file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void FetchMovies_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new FetchMoviesDialog(TinyZoneBaseUrl, MovieCatalogLocation)
        {
            Owner = Owner ?? this
        };

        if (dialog.ShowDialog() == true)
        {
            MovieCatalogLocation = dialog.OutputPath;
            TinyZoneBaseUrl = dialog.SelectedBaseUrl;
            SmartSearchCoordinator.QueueRebuildIfStale(MovieCatalogLocation);
            UpdateSmartSearchStatus();

            if (Owner is MainWindow mainWindow)
                _ = mainWindow.ReloadMoviesAsync();
        }
    }

    private void AddMovieByUrl_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddCatalogItemDialog(CatalogContentType.Movie, MovieCatalogLocation)
        {
            Owner = Owner ?? this
        };

        if (dialog.ShowDialog() == true && dialog.CatalogUpdated)
        {
            SmartSearchCoordinator.QueueRebuildIfStale(MovieCatalogLocation);
            UpdateSmartSearchStatus();

            if (Owner is MainWindow mainWindow)
                _ = mainWindow.ReloadMoviesAsync();
        }
    }

    private void BuildSearchIndex_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new BuildSearchIndexDialog(MovieCatalogLocation)
        {
            Owner = Owner ?? this
        };

        if (dialog.ShowDialog() == true)
        {
            UpdateSmartSearchStatus();

            if (Owner is MainWindow mainWindow)
                _ = mainWindow.ReloadMoviesAsync();
        }
    }

    private void SelectTvShowLinksLocation_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select TV Show Links File",
            Filter = "Text Files|*.txt|All Files|*.*",
            CheckFileExists = true,
            CheckPathExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                File.ReadAllLines(dialog.FileName);
                TvShowLinksLocation = dialog.FileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Cannot use selected file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void FetchTvShows_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new FetchTvShowsDialog(MovieLairShowsUrl, TvShowLinksLocation)
        {
            Owner = Owner ?? this
        };

        if (dialog.ShowDialog() == true)
        {
            TvShowLinksLocation = dialog.OutputPath;
            MovieLairShowsUrl = dialog.SelectedCategoryUrl;

            if (Owner is MainWindow mainWindow)
                _ = mainWindow.ReloadMoviesAsync();
        }
    }

    private void AddTvShowByUrl_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddCatalogItemDialog(CatalogContentType.TvShow, TvShowLinksLocation, TmdbApiKey)
        {
            Owner = Owner ?? this
        };

        if (dialog.ShowDialog() == true && dialog.CatalogUpdated && Owner is MainWindow mainWindow)
            _ = mainWindow.ReloadMoviesAsync();
    }
} 