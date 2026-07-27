using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace TinyCinema;

public partial class BuildSearchIndexDialog : Window
{
    private readonly string _catalogPath;
    private readonly StringBuilder _logBuffer = new();
    private readonly Dispatcher _dispatcher;
    private CancellationTokenSource? _cancellationTokenSource;
    private string? _logFilePath;
    private bool _pendingLogScroll;

    public bool IndexBuilt { get; private set; }

    public BuildSearchIndexDialog(string catalogPath)
    {
        InitializeComponent();
        _catalogPath = catalogPath;
        _dispatcher = Dispatcher;
        CatalogPathText.Text = catalogPath;

        if (!SmartSearchCoordinator.IsModelAvailable)
        {
            AppendLog("ERROR: Embedding model files are missing from Assets/Models/e5-small-v2.");
            StatusText.Text = "Model files not found.";
            StartButton.IsEnabled = false;
        }
        else if (!File.Exists(catalogPath))
        {
            AppendLog($"ERROR: Catalog file not found: {catalogPath}");
            StatusText.Text = "Movie catalog file was not found.";
            StartButton.IsEnabled = false;
        }
        else
        {
            AppendLog(SmartSearchCoordinator.GetStatusText(catalogPath));
            AppendLog($"Model directory: {EmbeddingModelPaths.GetModelDirectory()}");
            AppendLog("Click Build Index to start. Step 1 (loading the 127 MB ONNX model) can take 1-3 minutes before movie progress appears.");
            StatusText.Text = "Ready to build.";
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellationTokenSource != null)
            _cancellationTokenSource.Cancel();
        else
            Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellationTokenSource != null)
            _cancellationTokenSource.Cancel();
        else
            Close();
    }

    private void OpenLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_logFilePath) || !File.Exists(_logFilePath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _logFilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open log file:\n{ex.Message}", "Log File",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_catalogPath))
        {
            MessageBox.Show("Movie catalog file was not found.", "Smart Search",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!SmartSearchCoordinator.IsModelAvailable)
        {
            MessageBox.Show(
                "Embedding model files were not found in Assets/Models/e5-small-v2.",
                "Smart Search",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SetBuildingState(true);
        BuildProgressBar.Value = 0;
        PhaseText.Text = "Starting...";
        StatusText.Text = "Build started — see log below for live details.";
        StatsText.Text = string.Empty;
        _logBuffer.Clear();
        LogTextBlock.Text = string.Empty;

        AppendLog("=== Build started ===");

        _cancellationTokenSource = new CancellationTokenSource();
        var progress = new Progress<SearchIndexBuildProgress>(report =>
            _dispatcher.BeginInvoke(DispatcherPriority.Background, () => ApplyProgress(report)));

        try
        {
            var reporter = await SmartSearchCoordinator.RebuildIndexAsync(
                _catalogPath,
                progress,
                _cancellationTokenSource.Token);

            _logFilePath = reporter.LogFilePath;
            LogFilePathText.Text = _logFilePath;
            OpenLogButton.IsEnabled = true;

            IndexBuilt = true;
            PhaseText.Text = "Complete";
            StatusText.Text = SmartSearchCoordinator.GetStatusText(_catalogPath);
            AppendLog("=== Build complete ===");

            MessageBox.Show(
                "Smart search index built successfully.\n\nYou can now search by plot, vibe, cast, and director.",
                "Smart Search",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            PhaseText.Text = "Cancelled";
            StatusText.Text = "Index build cancelled.";
            AppendLog("=== Build cancelled by user ===");
            MessageBox.Show("Smart search index build was cancelled.", "Cancelled",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            PhaseText.Text = "Failed";
            StatusText.Text = "Index build failed.";
            AppendLog($"ERROR: {ex.Message}");
            if (ex.InnerException != null)
                AppendLog($"Inner error: {ex.InnerException.Message}");
            if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                AppendLog(ex.StackTrace);

            MessageBox.Show(
                $"Failed to build search index:\n{ex.Message}\n\nSee the build log for full details.",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            SetBuildingState(false);
        }
    }

    private void ApplyProgress(SearchIndexBuildProgress report)
    {
        if (!string.IsNullOrWhiteSpace(report.LogFilePath))
        {
            _logFilePath = report.LogFilePath;
            LogFilePathText.Text = report.LogFilePath;
            OpenLogButton.IsEnabled = true;
        }

        if (!string.IsNullOrWhiteSpace(report.Phase))
            PhaseText.Text = report.Phase;

        if (!string.IsNullOrWhiteSpace(report.Status))
            StatusText.Text = report.Status;

        if (report.Total > 0)
        {
            BuildProgressBar.Value = Math.Clamp(report.Percent, 0, 100);
            var elapsed = TimeSpan.FromSeconds(report.ElapsedSeconds);
            var rateText = report.ItemsPerSecond is > 0
                ? $" · {report.ItemsPerSecond.Value:F1} movies/sec"
                : string.Empty;
            var remaining = report.ItemsPerSecond is > 0 && report.Processed > 0
                ? TimeSpan.FromSeconds((report.Total - report.Processed) / report.ItemsPerSecond.Value)
                : (TimeSpan?)null;

            StatsText.Text = remaining.HasValue
                ? $"Elapsed {elapsed:hh\\:mm\\:ss}{rateText} · ETA {remaining.Value:hh\\:mm\\:ss} · {report.Processed:N0}/{report.Total:N0}"
                : $"Elapsed {elapsed:hh\\:mm\\:ss}{rateText} · {report.Processed:N0}/{report.Total:N0}";
        }
        else if (report.ElapsedSeconds > 0)
        {
            StatsText.Text = $"Elapsed {TimeSpan.FromSeconds(report.ElapsedSeconds):hh\\:mm\\:ss}";
        }

        if (!string.IsNullOrWhiteSpace(report.LogLine))
            AppendLog(report.LogLine);
    }

    private void AppendLog(string line)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Background, () => AppendLog(line));
            return;
        }

        _logBuffer.AppendLine(line);
        LogTextBlock.Text = _logBuffer.ToString();

        if (_pendingLogScroll)
            return;

        _pendingLogScroll = true;
        _dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _pendingLogScroll = false;
            LogScrollViewer.ScrollToEnd();
        });
    }

    private void SetBuildingState(bool isBuilding)
    {
        StartButton.IsEnabled = !isBuilding;
        CancelButton.Content = isBuilding ? "Cancel" : "Close";
    }
}
