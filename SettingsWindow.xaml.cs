using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.ComponentModel;

namespace TinyCinema;

public partial class SettingsWindow : Window, INotifyPropertyChanged
{
    private string _cacheLocation;
    private bool _isCachingEnabled;
    private string _movieLinksLocation;
    private bool _isFastModeEnabled = true;

    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TinyCinema",
        "settings.json"
    );

    public string MovieLinksLocation
    {
        get => _movieLinksLocation;
        private set
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

    public bool IsFastModeEnabled
    {
        get => _isFastModeEnabled;
        set
        {
            _isFastModeEnabled = value;
            OnPropertyChanged(nameof(IsFastModeEnabled));
            SaveSettings();
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
        LoadSettings();
        InitializeCacheDirectory();
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
                    else if (line.StartsWith("IsFastModeEnabled="))
                    {
                        IsFastModeEnabled = bool.Parse(line.Substring("IsFastModeEnabled=".Length).Trim());
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
            var settingsDir = Path.GetDirectoryName(SettingsFile);
            if (!Directory.Exists(settingsDir))
            {
                Directory.CreateDirectory(settingsDir);
            }

            var settings = new[]
            {
                $"CacheLocation={CacheLocation}",
                $"IsCachingEnabled={IsCachingEnabled}",
                $"MovieLinksLocation={MovieLinksLocation}",
                $"IsFastModeEnabled={IsFastModeEnabled}"
            };

            File.WriteAllLines(SettingsFile, settings);
        }
        catch
        {
            // If settings can't be saved, continue without them
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
} 