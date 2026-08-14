using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace TinyCinema;

public partial class SettingsPanel : UserControl, INotifyPropertyChanged
{
    private string _cacheLocation = "";
    private bool _isCachingEnabled;
    private bool _isTvShowCachingEnabled = true;
    private bool _isPopupBlockerEnabled = true;
    private bool _isClearPlayerBrowserDataOnClose;
    private bool _isMovieLairProbeEnabled;
    private bool _isStartCentered = true;
    private string _movieCatalogLocation = "";
    private string _tvShowLinksLocation = "";
    private string _movieLairShowsUrl = "https://movielair.cc/shows/10759/";
    private string _rokuIpAddress = "";
    private string _rokuUsername = "rokudev";
    private string _rokuPassword = "";
    private string _selectedPlayer = PlayerNames.InAppBrowser;
    private string _tinyZoneBaseUrl = "https://ww5.tinyzone.org";
    private string _tmdbApiKey = "";
    private string _playerEmbedHosts = "";
    private string _playerRequestBlocklist = "";
    private string _selectedAppTheme = "Black";
    private AppWindowSize _selectedWindowSize = AppWindowSize.Standard;
    private List<string> _availablePlayers = [];
    private List<string> _availableThemes = ThemeManager.GetAvailableDisplayNames().ToList();
    private List<string> _availableWindowSizes = AppLayoutManager.AvailableDisplayNames.ToList();
    private BuildSearchIndexDialog? _activeBuildDialog;

    public MainWindow? HostWindow { get; set; }

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

    public string PlayerEmbedHosts
    {
        get => _playerEmbedHosts;
        set
        {
            _playerEmbedHosts = value;
            OnPropertyChanged(nameof(PlayerEmbedHosts));
            SaveSettings();
        }
    }

    public string PlayerRequestBlocklist
    {
        get => _playerRequestBlocklist;
        set
        {
            _playerRequestBlocklist = value;
            OnPropertyChanged(nameof(PlayerRequestBlocklist));
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
            HostWindow?.RefreshTheme();
        }
    }

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

    public SettingsPanel()
    {
        InitializeComponent();
        DataContext = this;
        DetectAvailablePlayers();
        LoadSettings();
        InitializeCacheDirectory();

        if (PlayerComboBox != null)
        {
            PlayerComboBox.ItemsSource = AvailablePlayers;
            if (!AvailablePlayers.Contains(SelectedPlayer) && AvailablePlayers.Count > 0)
                SelectedPlayer = AvailablePlayers[0];
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

    private Window? DialogOwner => HostWindow;

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

        if (File.Exists(SettingsWindow.VlcPath))
            players.Add(PlayerNames.VLC);

        AvailablePlayers = players;
    }

    private static string NormalizeThemeDisplayName(string? value) =>
        ThemeManager.GetDisplayName(ThemeManager.ParseTheme(value));

    private void PlayerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlayerComboBox?.SelectedItem is string selectedPlayer)
            SelectedPlayer = selectedPlayer;
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox?.SelectedItem is string selectedTheme)
            SelectedAppTheme = selectedTheme;
    }

    private void WindowSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WindowSizeComboBox?.SelectedItem is string selectedSize)
            SelectedWindowSize = selectedSize;
    }

    private void LoadSettings()
    {
        var settingsFile = SettingsWindow.SettingsFilePath;

        try
        {
            if (File.Exists(settingsFile))
            {
                var settings = File.ReadAllText(settingsFile);
                var lines = settings.Split('\n');
                foreach (var line in lines)
                {
                    if (line.StartsWith("CacheLocation="))
                        CacheLocation = line.Substring("CacheLocation=".Length).Trim();
                    else if (line.StartsWith("IsCachingEnabled="))
                        IsCachingEnabled = bool.Parse(line.Substring("IsCachingEnabled=".Length).Trim());
                    else if (line.StartsWith("IsTvShowCachingEnabled="))
                        IsTvShowCachingEnabled = bool.Parse(line.Substring("IsTvShowCachingEnabled=".Length).Trim());
                    else if (line.StartsWith("IsPopupBlockerEnabled="))
                        IsPopupBlockerEnabled = bool.Parse(line.Substring("IsPopupBlockerEnabled=".Length).Trim());
                    else if (line.StartsWith("IsClearPlayerBrowserDataOnClose="))
                        IsClearPlayerBrowserDataOnClose = bool.Parse(line.Substring("IsClearPlayerBrowserDataOnClose=".Length).Trim());
                    else if (line.StartsWith("IsMovieLairProbeEnabled="))
                        IsMovieLairProbeEnabled = bool.Parse(line.Substring("IsMovieLairProbeEnabled=".Length).Trim());
                    else if (line.StartsWith("MovieCatalogLocation="))
                        MovieCatalogLocation = line.Substring("MovieCatalogLocation=".Length).Trim();
                    else if (line.StartsWith("MovieLinksLocation="))
                        MovieCatalogLocation = line.Substring("MovieLinksLocation=".Length).Trim();
                    else if (line.StartsWith("TvShowCatalogLocation="))
                        TvShowLinksLocation = line.Substring("TvShowCatalogLocation=".Length).Trim();
                    else if (line.StartsWith("TvShowLinksLocation="))
                        TvShowLinksLocation = line.Substring("TvShowLinksLocation=".Length).Trim();
                    else if (line.StartsWith("MovieLairShowsUrl="))
                        MovieLairShowsUrl = line.Substring("MovieLairShowsUrl=".Length).Trim();
                    else if (line.StartsWith("RokuIpAddress="))
                        RokuIpAddress = line.Substring("RokuIpAddress=".Length).Trim();
                    else if (line.StartsWith("RokuUsername="))
                        RokuUsername = line.Substring("RokuUsername=".Length).Trim();
                    else if (line.StartsWith("RokuPassword="))
                        RokuPassword = line.Substring("RokuPassword=".Length).Trim();
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
                        TinyZoneBaseUrl = line.Substring("TinyZoneBaseUrl=".Length).Trim();
                    else if (line.StartsWith("TmdbApiKey="))
                        TmdbApiKey = line.Substring("TmdbApiKey=".Length).Trim();
                    else if (line.StartsWith(PlayerEmbedHostSettings.SettingPrefix))
                        _playerEmbedHosts = PlayerEmbedHostSettings.FormatForDisplay(
                            line.Substring(PlayerEmbedHostSettings.SettingPrefix.Length).Trim());
                    else if (line.StartsWith(PlayerRequestBlocklistSettings.SettingPrefix))
                        _playerRequestBlocklist = PlayerRequestBlocklistSettings.FormatForDisplay(
                            line.Substring(PlayerRequestBlocklistSettings.SettingPrefix.Length).Trim());
                    else if (line.StartsWith("AppTheme="))
                        _selectedAppTheme = NormalizeThemeDisplayName(line.Substring("AppTheme=".Length).Trim());
                    else if (line.StartsWith("WindowSize="))
                        _selectedWindowSize = AppLayoutManager.ParseSize(line.Substring("WindowSize=".Length).Trim());
                    else if (line.StartsWith("StartCentered="))
                        _isStartCentered = bool.Parse(line.Substring("StartCentered=".Length).Trim());
                }

                ThemeManager.ApplyTheme(ThemeManager.ParseTheme(_selectedAppTheme));
                OnPropertyChanged(nameof(SelectedAppTheme));
                OnPropertyChanged(nameof(SelectedWindowSize));
                OnPropertyChanged(nameof(WindowSizeDescription));
                OnPropertyChanged(nameof(IsStartCentered));
                OnPropertyChanged(nameof(PlayerEmbedHosts));
                OnPropertyChanged(nameof(PlayerRequestBlocklist));
                AppLayoutManager.LoadFromSettings();
            }
        }
        catch
        {
            // If settings can't be loaded, use defaults.
        }

        if (string.IsNullOrEmpty(CacheLocation))
        {
            CacheLocation = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TinyCinema",
                "ImageCache");
        }

        MovieCatalogLocation = SettingsWindow.NormalizeMovieCatalogLocation(MovieCatalogLocation);

        TvShowLinksLocation = SettingsWindow.NormalizeTvShowCatalogLocation(TvShowLinksLocation);

        if (string.IsNullOrEmpty(TvShowLinksLocation))
        {
            TvShowLinksLocation = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "TvShows.json");
        }
    }

    private void SaveSettings()
    {
        try
        {
            var settingsFile = SettingsWindow.SettingsFilePath;
            var directory = Path.GetDirectoryName(settingsFile);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory!);

            var settings = new[]
            {
                $"CacheLocation={CacheLocation}",
                $"IsCachingEnabled={IsCachingEnabled}",
                $"IsTvShowCachingEnabled={IsTvShowCachingEnabled}",
                $"IsPopupBlockerEnabled={IsPopupBlockerEnabled}",
                $"IsClearPlayerBrowserDataOnClose={IsClearPlayerBrowserDataOnClose}",
                $"IsMovieLairProbeEnabled={IsMovieLairProbeEnabled}",
                $"MovieCatalogLocation={MovieCatalogLocation}",
                $"TvShowCatalogLocation={TvShowLinksLocation}",
                $"TvShowLinksLocation={TvShowLinksLocation}",
                $"MovieLairShowsUrl={MovieLairShowsUrl}",
                $"RokuIpAddress={RokuIpAddress}",
                $"RokuUsername={RokuUsername}",
                $"RokuPassword={RokuPassword}",
                $"SelectedPlayer={SelectedPlayer}",
                $"TinyZoneBaseUrl={TinyZoneBaseUrl}",
                $"TmdbApiKey={TmdbApiKey}",
                $"{PlayerEmbedHostSettings.SettingPrefix}{PlayerEmbedHostSettings.FormatForStorage(PlayerEmbedHosts)}",
                $"{PlayerRequestBlocklistSettings.SettingPrefix}{PlayerRequestBlocklistSettings.FormatForStorage(PlayerRequestBlocklist)}",
                $"AppTheme={ThemeManager.ToSettingValue(ThemeManager.ParseTheme(SelectedAppTheme))}",
                $"WindowSize={AppLayoutManager.ToSettingValue(_selectedWindowSize)}",
                $"StartCentered={IsStartCentered}"
            };

            File.WriteAllLines(settingsFile, settings);
        }
        catch
        {
            // If settings can't be saved, ignore.
        }
    }

    private void InitializeCacheDirectory()
    {
        try
        {
            if (!Directory.Exists(CacheLocation))
                Directory.CreateDirectory(CacheLocation);
        }
        catch
        {
            CacheLocation = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TinyCinema",
                "ImageCache");
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
                    var testFile = Path.Combine(selectedPath, "test.tmp");
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
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

    private void ImportNetflixGaps_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ImportNetflixGapsDialog(MovieCatalogLocation)
        {
            Owner = DialogOwner
        };

        if (dialog.ShowDialog() == true && dialog.CatalogUpdated)
        {
            SmartSearchCoordinator.QueueRebuildIfStale(MovieCatalogLocation);
            UpdateSmartSearchStatus();
            if (HostWindow != null)
                _ = HostWindow.ReloadMoviesAsync();
        }
    }

    private void FetchMovies_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new FetchMoviesDialog(TinyZoneBaseUrl, MovieCatalogLocation)
        {
            Owner = DialogOwner
        };

        if (dialog.ShowDialog() == true)
        {
            MovieCatalogLocation = dialog.OutputPath;
            TinyZoneBaseUrl = dialog.SelectedBaseUrl;
            SmartSearchCoordinator.QueueRebuildIfStale(MovieCatalogLocation);
            UpdateSmartSearchStatus();
            if (HostWindow != null)
                _ = HostWindow.ReloadMoviesAsync();
        }
    }

    private void AddMovieByUrl_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddCatalogItemDialog(CatalogContentType.Movie, MovieCatalogLocation)
        {
            Owner = DialogOwner
        };

        if (dialog.ShowDialog() == true && dialog.CatalogUpdated)
        {
            SmartSearchCoordinator.QueueRebuildIfStale(MovieCatalogLocation);
            UpdateSmartSearchStatus();
            if (HostWindow != null)
                _ = HostWindow.ReloadMoviesAsync();
        }
    }

    private void BuildSearchIndex_Click(object sender, RoutedEventArgs e)
    {
        if (_activeBuildDialog != null)
        {
            _activeBuildDialog.Show();
            if (_activeBuildDialog.WindowState == WindowState.Minimized)
                _activeBuildDialog.WindowState = WindowState.Normal;
            _activeBuildDialog.Activate();
            return;
        }

        if (SmartSearchCoordinator.IsBuildInProgress)
        {
            MessageBox.Show(
                "A smart search index build is already running. Check the taskbar for the progress window.",
                "Smart Search",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _activeBuildDialog = new BuildSearchIndexDialog(MovieCatalogLocation)
        {
            Owner = DialogOwner
        };
        _activeBuildDialog.Closed += (_, _) =>
        {
            if (_activeBuildDialog?.IndexBuilt == true)
            {
                UpdateSmartSearchStatus();
                if (HostWindow != null)
                    _ = HostWindow.ReloadMoviesAsync();
            }

            _activeBuildDialog = null;
        };
        _activeBuildDialog.Show();
    }

    private void SelectTvShowLinksLocation_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select TV Show Catalog File",
            Filter = "JSON Files|*.json|All Files|*.*",
            CheckFileExists = true,
            CheckPathExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                _ = File.ReadAllText(dialog.FileName);
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
            Owner = DialogOwner
        };

        if (dialog.ShowDialog() == true)
        {
            TvShowLinksLocation = dialog.OutputPath;
            MovieLairShowsUrl = dialog.SelectedCategoryUrl;
            if (HostWindow != null)
                _ = HostWindow.ReloadMoviesAsync();
        }
    }

    private void AddTvShowByUrl_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddCatalogItemDialog(CatalogContentType.TvShow, TvShowLinksLocation, TmdbApiKey)
        {
            Owner = DialogOwner
        };

        if (dialog.ShowDialog() == true && dialog.CatalogUpdated && HostWindow != null)
            _ = HostWindow.ReloadMoviesAsync();
    }
}
