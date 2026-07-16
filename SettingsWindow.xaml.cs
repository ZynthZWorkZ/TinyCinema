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
    private string _movieLinksLocation;
    private string _rokuIpAddress = "";
    private string _rokuUsername = "rokudev";
    private string _rokuPassword = "";
    private string _selectedPlayer = PlayerNames.InAppBrowser;
    private string _tinyZoneBaseUrl = "https://ww5.tinyzone.org";
    private List<string> _availablePlayers;

    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TinyCinema",
        "settings.json"
    );
    
    private static readonly string VlcPath = @"C:\Program Files\VideoLAN\VLC\vlc.exe";

    public string MovieLinksLocation
    {
        get => _movieLinksLocation;
        set
        {
            _movieLinksLocation = value;
            OnPropertyChanged(nameof(MovieLinksLocation));
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
                    else if (line.StartsWith("MovieLinksLocation="))
                    {
                        MovieLinksLocation = line.Substring("MovieLinksLocation=".Length).Trim();
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

        if (string.IsNullOrEmpty(MovieLinksLocation))
        {
            MovieLinksLocation = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "movie_links.txt"
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
                $"MovieLinksLocation={MovieLinksLocation}",
                $"RokuIpAddress={RokuIpAddress}",
                $"RokuUsername={RokuUsername}",
                $"RokuPassword={RokuPassword}",
                $"SelectedPlayer={SelectedPlayer}",
                $"TinyZoneBaseUrl={TinyZoneBaseUrl}"
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

    private void SelectMovieLinksLocation_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Movie Links File",
            Filter = "Text Files|*.txt|All Files|*.*",
            CheckFileExists = true,
            CheckPathExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                // Test if we can read the file
                File.ReadAllLines(dialog.FileName);
                
                // If successful, update movie links location
                MovieLinksLocation = dialog.FileName;
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
        var dialog = new FetchMoviesDialog(TinyZoneBaseUrl, MovieLinksLocation)
        {
            Owner = Owner ?? this
        };

        if (dialog.ShowDialog() == true)
        {
            MovieLinksLocation = dialog.OutputPath;
            TinyZoneBaseUrl = dialog.SelectedBaseUrl;

            if (Owner is MainWindow mainWindow)
                _ = mainWindow.ReloadMoviesAsync();
        }
    }
} 