using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TinyCinema;

public partial class BuildSearchIndexDialog : Window
{
    private readonly string _catalogPath;
    private readonly StringBuilder _logBuffer = new();
    private readonly ObservableCollection<string> _recentActivity = new();
    private readonly Dispatcher _dispatcher;
    private SearchIndexBuildSession? _buildSession;
    private string? _logFilePath;
    private bool _pendingLogScroll;
    private bool _userInitiatedStop;
    private bool _buildSucceeded;
    private int _currentStep;

    public bool IndexBuilt { get; private set; }

    public BuildSearchIndexDialog(string catalogPath)
    {
        InitializeComponent();
        _catalogPath = catalogPath;
        _dispatcher = Dispatcher;
        CatalogPathText.Text = catalogPath;
        RecentActivityList.ItemsSource = _recentActivity;

        if (!SmartSearchCoordinator.IsModelAvailable)
        {
            AppendLog("ERROR: Embedding model files are missing from Assets/Models/e5-small-v2.");
            CurrentItemText.Text = "Model files not found.";
            StartButton.IsEnabled = false;
        }
        else if (!File.Exists(catalogPath))
        {
            AppendLog($"ERROR: Catalog file not found: {catalogPath}");
            CurrentItemText.Text = "Movie catalog file was not found.";
            StartButton.IsEnabled = false;
        }
        else
        {
            AppendLog(SmartSearchCoordinator.GetStatusText(catalogPath));
            AppendLog($"Model directory: {EmbeddingModelPaths.GetModelDirectory()}");
            AppendLog("Progress auto-saves every 50 movies. Use Stop & Save to pause and resume later.");
            AppendLog("Click Build Index to start. Step 1 loads the ~127 MB ONNX model and can take 1-3 minutes.");
            UpdateCheckpointUi();
        }
    }

    private void UpdateCheckpointUi()
    {
        var checkpoint = SmartSearchCoordinator.GetCheckpointStatus(_catalogPath);
        if (checkpoint == null)
        {
            PauseBanner.Visibility = Visibility.Collapsed;
            if (_buildSession == null)
                StartButton.Content = "Build Index";
            if (_buildSession == null)
                CurrentItemText.Text = "Ready when you are — click Build Index.";
            return;
        }

        PauseBanner.Visibility = Visibility.Visible;
        PauseBannerText.Text =
            $"Build paused at {checkpoint.ProcessedCount:N0}/{checkpoint.TotalCount:N0} movies " +
            $"({checkpoint.Percent:F0}% — saved {checkpoint.SavedAtUtc.ToLocalTime():g}). " +
            "Click Resume Build to continue where you left off.";
        if (_buildSession == null)
            StartButton.Content = "Resume Build";

        if (_buildSession == null && !_buildSucceeded)
        {
            CurrentItemText.Text = $"Ready to resume from movie {checkpoint.ProcessedCount + 1:N0} of {checkpoint.TotalCount:N0}.";
            BuildProgressBar.Value = checkpoint.Percent;
            StatsText.Text = $"{checkpoint.ProcessedCount:N0}/{checkpoint.TotalCount:N0} movies already embedded.";
        }
    }

    private void StartFreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_buildSession != null)
            return;

        var checkpoint = SmartSearchCoordinator.GetCheckpointStatus(_catalogPath);
        if (checkpoint == null)
            return;

        var confirm = MessageBox.Show(
            $"Discard saved progress ({checkpoint.ProcessedCount:N0}/{checkpoint.TotalCount:N0} movies) and start a fresh build?",
            "Start Fresh",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        SmartSearchCoordinator.ClearCheckpoint(_catalogPath);
        BuildProgressBar.Value = 0;
        StatsText.Text = string.Empty;
        AppendLog("Saved checkpoint discarded — next build starts from scratch.");
        UpdateCheckpointUi();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MinimizeBackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        MinimizeToBackground();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_buildSession != null)
            MinimizeToBackground();
        else
            Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        RequestCancelBuild(discardCheckpoint: true);
    }

    private void CancelBuildButton_Click(object sender, RoutedEventArgs e)
    {
        RequestCancelBuild(discardCheckpoint: true);
    }

    private void StopAndSaveButton_Click(object sender, RoutedEventArgs e)
    {
        RequestStopAndSave();
    }

    private void RequestStopAndSave()
    {
        if (_buildSession == null)
            return;

        _userInitiatedStop = true;
        StopAndSaveButton.IsEnabled = false;
        CancelBuildButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        CurrentItemText.Text = "Stopping build and saving progress...";
        _buildSession.RequestStopAndSave();
    }

    private void RequestCancelBuild(bool discardCheckpoint)
    {
        if (_buildSession != null)
        {
            _userInitiatedStop = true;
            StopAndSaveButton.IsEnabled = false;
            CancelBuildButton.IsEnabled = false;
            CancelButton.IsEnabled = false;
            CurrentItemText.Text = discardCheckpoint
                ? "Cancelling build..."
                : "Stopping build...";
            _buildSession.RequestCancelDiscard();
            return;
        }

        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_buildSession != null && !_userInitiatedStop)
        {
            e.Cancel = true;
            MinimizeToBackground();
        }
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (_buildSucceeded && WindowState != WindowState.Minimized)
            UpdateTitleForBuildState();
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
        if (_buildSucceeded)
        {
            Close();
            return;
        }

        if (SmartSearchCoordinator.IsBuildInProgress)
        {
            MessageBox.Show(
                "Another smart search index build is already running.",
                "Smart Search",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

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

        var checkpoint = SmartSearchCoordinator.GetCheckpointStatus(_catalogPath);
        var resume = checkpoint != null;
        if (resume)
        {
            AppendLog("=== Resuming build from checkpoint ===");
            AddRecentActivity("Resuming from checkpoint");
        }
        else
        {
            AppendLog("=== Build started ===");
            AddRecentActivity("Build started");
        }

        SetBuildingState(true);
        _buildSucceeded = false;
        _userInitiatedStop = false;
        SuccessBanner.Visibility = Visibility.Collapsed;
        PauseBanner.Visibility = Visibility.Collapsed;
        if (!resume)
        {
            BuildProgressBar.Value = 0;
            PhaseText.Text = "Starting...";
            CurrentItemText.Text = "Preparing build...";
            StatsText.Text = string.Empty;
            _recentActivity.Clear();
            _currentStep = 0;
            UpdateStepIndicators(0);
            ClearPassageDetails();
            _logBuffer.Clear();
            LogTextBlock.Text = string.Empty;
        }

        _buildSession = new SearchIndexBuildSession
        {
            ResumeFromCheckpoint = resume
        };

        var progress = new Progress<SearchIndexBuildProgress>(report =>
            _dispatcher.BeginInvoke(DispatcherPriority.Background, () => ApplyProgress(report)));

        try
        {
            var reporter = await SmartSearchCoordinator.RebuildIndexAsync(
                _catalogPath,
                progress,
                _buildSession);

            _logFilePath = reporter.LogFilePath;
            LogFilePathText.Text = _logFilePath;
            OpenLogButton.IsEnabled = true;

            IndexBuilt = true;
            _buildSucceeded = true;
            PhaseText.Text = "Complete";
            CurrentItemText.Text = SmartSearchCoordinator.GetStatusText(_catalogPath);
            UpdateStepIndicators(5);
            SuccessBanner.Visibility = Visibility.Visible;
            AppendLog("=== Build complete ===");
            AddRecentActivity("Build complete — smart search is ready");

            NotifyBuildComplete();
            SetBuildingState(false, buildSucceeded: true);
            UpdateCheckpointUi();
        }
        catch (SearchIndexBuildPausedException paused)
        {
            PhaseText.Text = "Paused";
            CurrentItemText.Text =
                $"Progress saved at {paused.Processed:N0}/{paused.Total:N0} movies. Click Resume Build to continue.";
            BuildProgressBar.Value = paused.Total > 0
                ? (double)paused.Processed / paused.Total * 100
                : 0;
            StatsText.Text = $"{paused.Processed:N0}/{paused.Total:N0} movies embedded";
            AppendLog($"=== Build paused at {paused.Processed:N0}/{paused.Total:N0} ===");
            AddRecentActivity($"Paused at {paused.Processed:N0}/{paused.Total:N0}");
            UpdateCheckpointUi();
        }
        catch (OperationCanceledException)
        {
            PhaseText.Text = "Cancelled";
            CurrentItemText.Text = "Index build cancelled.";
            AppendLog("=== Build cancelled by user ===");
            AddRecentActivity("Build cancelled");
            UpdateCheckpointUi();
            MessageBox.Show("Smart search index build was cancelled.", "Cancelled",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            PhaseText.Text = "Failed";
            CurrentItemText.Text = "Index build failed.";
            AppendLog($"ERROR: {ex.Message}");
            if (ex.InnerException != null)
                AppendLog($"Inner error: {ex.InnerException.Message}");
            if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                AppendLog(ex.StackTrace);

            AddRecentActivity($"Failed: {ex.Message}");
            MessageBox.Show(
                $"Failed to build search index:\n{ex.Message}\n\nSee the build log for full details.",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _buildSession?.Dispose();
            _buildSession = null;
            if (!_buildSucceeded)
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

        if (report.StepIndex > 0)
            UpdateStepIndicators(report.StepIndex);

        UpdateCurrentItemText(report);

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
                ? $"Elapsed {elapsed:hh\\:mm\\:ss}{rateText} · ETA {remaining.Value:hh\\:mm\\:ss} · {report.Processed:N0}/{report.Total:N0} movies"
                : $"Elapsed {elapsed:hh\\:mm\\:ss}{rateText} · {report.Processed:N0}/{report.Total:N0} movies";
        }
        else if (report.ElapsedSeconds > 0)
        {
            StatsText.Text = $"Elapsed {TimeSpan.FromSeconds(report.ElapsedSeconds):hh\\:mm\\:ss}";
        }

        if (report.Total > 0 && report.Processed > 0 && !string.IsNullOrWhiteSpace(report.Status))
            AddRecentActivity($"[{report.Processed:N0}/{report.Total:N0}] {report.Status}");

        UpdatePassageDetails(report);

        if (!string.IsNullOrWhiteSpace(report.LogLine))
            AppendLog(report.LogLine);

        UpdateTitleForBuildState();
    }

    private void UpdatePassageDetails(SearchIndexBuildProgress report)
    {
        if (report.PassageFields.Count == 0)
        {
            PassageDetailsPanel.Visibility = Visibility.Collapsed;
            PassageFieldsList.ItemsSource = null;
            SearchKeywordsText.Text = string.Empty;
            ModelTokensText.Text = string.Empty;
            PassagePreviewText.Text = string.Empty;
            return;
        }

        PassageDetailsPanel.Visibility = Visibility.Visible;
        PassageFieldsList.ItemsSource = report.PassageFields;
        SearchKeywordsText.Text = FormatKeywordList(report.SearchKeywords);
        ModelTokensText.Text = FormatKeywordList(report.ModelTokens);
        PassagePreviewText.Text = string.IsNullOrWhiteSpace(report.PassagePreview)
            ? string.Empty
            : report.PassagePreview;
    }

    private static string FormatKeywordList(IReadOnlyList<string> keywords) =>
        keywords.Count == 0
            ? "(none)"
            : string.Join(" · ", keywords);

    private void ClearPassageDetails()
    {
        PassageDetailsPanel.Visibility = Visibility.Collapsed;
        PassageFieldsList.ItemsSource = null;
        SearchKeywordsText.Text = string.Empty;
        ModelTokensText.Text = string.Empty;
        PassagePreviewText.Text = string.Empty;
    }

    private void UpdateCurrentItemText(SearchIndexBuildProgress report)
    {
        if (report.Total > 0 && report.Processed > 0 && !string.IsNullOrWhiteSpace(report.Status))
        {
            CurrentItemText.Text = $"Now embedding: {report.Status}";
            return;
        }

        CurrentItemText.Text = report.StepIndex switch
        {
            1 => "Loading the e5-small-v2 ONNX model (~127 MB). This step often takes 1-3 minutes.",
            2 => string.IsNullOrWhiteSpace(report.Status)
                ? "Reading your movie catalog JSON..."
                : report.Status,
            3 when report.Total > 0 => report.Processed > 0
                ? $"Now embedding: {report.Status}"
                : $"Preparing to embed {report.Total:N0} movies...",
            4 => "Writing vectors to disk and loading the index into memory...",
            _ => string.IsNullOrWhiteSpace(report.Status)
                ? CurrentItemText.Text
                : report.Status
        };
    }

    private void UpdateStepIndicators(int activeStep)
    {
        if (activeStep == _currentStep)
            return;

        _currentStep = activeStep;
        StyleStep(Step1Border, activeStep, 1);
        StyleStep(Step2Border, activeStep, 2);
        StyleStep(Step3Border, activeStep, 3);
        StyleStep(Step4Border, activeStep, 4);
    }

    private static void StyleStep(Border border, int activeStep, int stepNumber)
    {
        var isComplete = activeStep > stepNumber || activeStep >= 5;
        var isActive = activeStep == stepNumber;

        border.Background = isComplete
            ? new SolidColorBrush(Color.FromRgb(0x1A, 0x3D, 0x24))
            : isActive
                ? new SolidColorBrush(Color.FromRgb(0x1A, 0x2A, 0x44))
                : new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));

        border.BorderBrush = isComplete
            ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x46))
            : isActive
                ? new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC))
                : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
    }

    private void AddRecentActivity(string line)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Background, () => AddRecentActivity(line));
            return;
        }

        if (_recentActivity.Count > 0 && _recentActivity[0] == line)
            return;

        _recentActivity.Insert(0, line);
        while (_recentActivity.Count > 8)
            _recentActivity.RemoveAt(_recentActivity.Count - 1);
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

    private void SetBuildingState(bool isBuilding, bool buildSucceeded = false)
    {
        StartButton.IsEnabled = !isBuilding || buildSucceeded;
        StartButton.Content = buildSucceeded
            ? "Close"
            : SmartSearchCoordinator.GetCheckpointStatus(_catalogPath) != null && !isBuilding
                ? "Resume Build"
                : "Build Index";
        CancelButton.Content = isBuilding ? "Cancel Build" : "Close";
        CancelButton.Style = isBuilding
            ? (Style)FindResource("CancelBuildButtonStyle")
            : (Style)FindResource("ModernButtonStyle");
        CancelButton.IsEnabled = true;
        StopAndSaveButton.Visibility = isBuilding ? Visibility.Visible : Visibility.Collapsed;
        StopAndSaveButton.IsEnabled = isBuilding;
        CancelBuildButton.Visibility = isBuilding ? Visibility.Visible : Visibility.Collapsed;
        CancelBuildButton.IsEnabled = isBuilding;
        MinimizeBackgroundButton.Visibility = isBuilding ? Visibility.Visible : Visibility.Collapsed;
        StartFreshButton.IsEnabled = !isBuilding;
        UpdateTitleForBuildState();
    }

    private void MinimizeToBackground()
    {
        ShowInTaskbar = true;
        WindowState = WindowState.Minimized;
        UpdateTitleForBuildState();
    }

    private void NotifyBuildComplete()
    {
        UpdateTitleForBuildState();

        if (WindowState == WindowState.Minimized)
            FlashTaskbar();
    }

    private void UpdateTitleForBuildState()
    {
        if (_buildSucceeded)
        {
            Title = "Smart Search Index — Complete";
            TitleBarText.Text = "Smart Search Index — Complete";
            return;
        }

        if (_buildSession != null)
        {
            var minimizedHint = WindowState == WindowState.Minimized ? " (minimized)" : string.Empty;
            Title = $"Building Smart Search Index{minimizedHint}";
            TitleBarText.Text = Title;
            return;
        }

        Title = "Build Smart Search Index";
        TitleBarText.Text = Title;
    }

    private void FlashTaskbar()
    {
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;

            FlashWindow(handle, true);
        }
        catch
        {
            // Notification is optional.
        }
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindow(IntPtr hwnd, bool bInvert);
}
