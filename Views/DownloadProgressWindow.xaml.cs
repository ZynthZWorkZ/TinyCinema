using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace TinyCinema;

public partial class DownloadProgressWindow : Window
{
    private readonly string _streamUrl;
    private readonly string _movieTitle;
    private readonly string? _referer;
    private readonly StreamDownloadRequest _request;
    private readonly FfmpegDownloader _downloader = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private string? _outputPath;
    private bool _isCompleted;
    private bool _isClosing;

    public DownloadProgressWindow(string streamUrl, string movieTitle, string? referer, StreamDownloadRequest request)
    {
        try
        {
            InitializeComponent();
            _streamUrl = streamUrl;
            _movieTitle = movieTitle;
            _referer = referer;
            _request = request;

            if (_request.Mode == StreamDownloadMode.Clip && _request.ClipStart.HasValue && _request.ClipEnd.HasValue)
            {
                Title = "Download Clip";
                TitleText.Text = $"{movieTitle} · clip";
                _outputPath = FfmpegDownloader.BuildOutputPath(movieTitle, _request.ClipStart, _request.ClipEnd);
            }
            else
            {
                TitleText.Text = movieTitle;
                _outputPath = FfmpegDownloader.BuildOutputPath(movieTitle);
            }

            OutputPathText.Text = _outputPath;
            Loaded += (_, _) => _ = StartDownloadSafeAsync();
            Closing += (_, _) =>
            {
                if (_isClosing)
                    return;

                if (!_isCompleted)
                    _cancellationTokenSource.Cancel();
            };
        }
        catch (Exception ex)
        {
            DownloadDebugHelper.ShowError(
                "Initializing download progress window",
                ex,
                $"Stream URL: {streamUrl}\nMode: {request.Mode}");
            throw;
        }
    }

    private async Task StartDownloadSafeAsync()
    {
        try
        {
            await StartDownloadAsync();
        }
        catch (Exception ex)
        {
            DownloadDebugHelper.ShowError("Unexpected download failure", ex, $"Output: {_outputPath}");
        }
    }

    private async Task StartDownloadAsync()
    {
        if (!FfmpegDownloader.TryResolveFfmpegPath(out _))
        {
            StatusText.Text = "ffmpeg was not found on this system.";
            PercentText.Text = "—";
            IndeterminateProgressBar.Visibility = Visibility.Collapsed;
            CancelButton.Content = "Close";
            MessageBox.Show(
                "ffmpeg was not found.\n\nInstall ffmpeg and add it to your PATH, or place ffmpeg.exe next to TinyCinema.",
                "ffmpeg Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var progress = new Progress<FfmpegDownloadProgress>(UpdateProgress);

        try
        {
            _outputPath = await _downloader.DownloadAsync(
                _streamUrl,
                _outputPath!,
                _referer,
                progress,
                _cancellationTokenSource.Token,
                _request.Mode == StreamDownloadMode.Clip ? _request.ClipStart : null,
                _request.Mode == StreamDownloadMode.Clip ? _request.ClipEnd : null);

            _isCompleted = true;
            IndeterminateProgressBar.Visibility = Visibility.Collapsed;
            DownloadProgressBar.Value = 100;
            PercentText.Text = "100%";
            StatusText.Text = _request.Mode == StreamDownloadMode.Clip ? "Clip saved." : "Download complete.";
            CancelButton.Content = "Close";
            OpenFolderButton.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Download cancelled.";
            PercentText.Text = "—";
            IndeterminateProgressBar.Visibility = Visibility.Collapsed;
            CancelButton.Content = "Close";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Download failed.";
            PercentText.Text = "—";
            IndeterminateProgressBar.Visibility = Visibility.Collapsed;
            CancelButton.Content = "Close";
            DownloadDebugHelper.ShowError(
                "Running ffmpeg download",
                ex,
                $"Stream URL: {_streamUrl}\nOutput: {_outputPath}\nMode: {_request.Mode}");
        }
    }

    private void UpdateProgress(FfmpegDownloadProgress progress)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateProgress(progress));
            return;
        }

        StatusText.Text = progress.Status;

        if (progress.Percent.HasValue)
        {
            IndeterminateProgressBar.Visibility = Visibility.Collapsed;
            DownloadProgressBar.Value = progress.Percent.Value;
            PercentText.Text = $"{progress.Percent.Value:0}%";
            return;
        }

        if (progress.SizeKb.HasValue)
            StatusText.Text = $"{progress.Status} · {progress.SizeKb.Value:N0} KB received";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCompleted || _cancellationTokenSource.IsCancellationRequested)
        {
            CloseWindow();
            return;
        }

        _cancellationTokenSource.Cancel();
        _downloader.Cancel();
        CancelButton.IsEnabled = false;
        StatusText.Text = "Cancelling...";
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_outputPath))
            return;

        var folder = Path.GetDirectoryName(_outputPath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true
        });
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseWindow();
    }

    private void CloseWindow()
    {
        _isClosing = true;
        if (!_isCompleted)
            _cancellationTokenSource.Cancel();

        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _downloader.Dispose();
        _cancellationTokenSource.Dispose();
        base.OnClosed(e);
    }
}
