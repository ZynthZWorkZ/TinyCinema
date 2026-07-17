using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace TinyCinema;

public partial class TinyZonePlayerWindow : Window
{
    private const double UrlsPanelWidth = 340;
    private readonly string _selectedPlayer;
    private readonly string _movieTitle;
    private readonly string _pageUrl;
    private readonly HashSet<string> _capturedUrls = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenUrls = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<LiveUrlEntry> _liveUrls = new();
    private readonly ICollectionView _urlsView;
    private bool _isInitialized;
    private bool _urlsPanelOpen;
    private bool _hlsFilterOnly;
    private bool _urlCaptureEnabled = true;
    private bool _cinemaModeEnabled = true;

    public TinyZonePlayerWindow(Movie movie, string selectedPlayer)
    {
        InitializeComponent();
        _selectedPlayer = selectedPlayer;
        _movieTitle = movie.Title;
        _pageUrl = movie.Url;
        TitleText.Text = movie.Title;
        Title = movie.Title;
        StatusText.Text = "Loading page and listening for stream URLs...";
        _urlsView = CollectionViewSource.GetDefaultView(_liveUrls);
        _urlsView.Filter = UrlFilter;
        UrlsItemsControl.ItemsSource = _urlsView;
        HidePlayerSurface();
        Loaded += (_, _) => ToggleUrlsPanel(open: true);
        Loaded += async (_, _) => await InitializeAndNavigateAsync(movie.Url);
    }

    private void HidePlayerSurface()
    {
        PlayerWebViewHost.Visibility = Visibility.Collapsed;
        LoadingOverlay.Visibility = Visibility.Visible;
    }

    private void ShowPlayerSurface()
    {
        PlayerWebViewHost.Visibility = Visibility.Visible;
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private async Task InitializeAndNavigateAsync(string pageUrl)
    {
        try
        {
            PlayerWebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 7, 7, 7);
            await PlayerWebView.EnsureCoreWebView2Async();
            _isInitialized = true;

            var core = PlayerWebView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(TinyZonePopupBlocker.BlockPopupsScript);
            await core.AddScriptToExecuteOnDocumentCreatedAsync(TinyZoneCinemaMode.EarlyHideScript);
            await core.AddScriptToExecuteOnDocumentCreatedAsync(TinyZoneCinemaMode.BootstrapScript);
            TinyZonePopupBlocker.Attach(core);
            core.FrameCreated += OnFrameCreated;
            core.WebResourceResponseReceived += OnWebResourceResponseReceived;
            core.NavigationStarting += (_, _) =>
            {
                if (_cinemaModeEnabled)
                    HidePlayerSurface();
            };
            core.NavigationCompleted += async (_, args) =>
            {
                if (!args.IsSuccess)
                {
                    ShowPlayerSurface();
                    StatusText.Text = $"Failed to load page (HTTP {args.HttpStatusCode}).";
                    return;
                }

                try
                {
                    if (_cinemaModeEnabled)
                        await PrepareCinemaViewAsync();
                    else
                        ShowPlayerSurface();

                    StatusText.Text = _cinemaModeEnabled
                        ? "Cinema mode active. Playback should start automatically."
                        : "Page loaded. Start playback on the page to capture the stream.";
                }
                catch
                {
                    ShowPlayerSurface();
                    StatusText.Text = "Page loaded with limited cinema styling.";
                }
            };

            core.Navigate(pageUrl);
        }
        catch (Exception ex)
        {
            ShowPlayerSurface();
            StatusText.Text = "WebView2 failed to start.";
            MessageBox.Show(
                $"Could not start the TinyZone browser.\n\n{ex.Message}\n\nMake sure Microsoft Edge WebView2 Runtime is installed.",
                "Player Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }

    private async void OnFrameCreated(object? sender, CoreWebView2FrameCreatedEventArgs e)
    {
        if (!_cinemaModeEnabled)
            return;

        try
        {
            e.Frame.NavigationCompleted += async (_, _) =>
            {
                try
                {
                    await e.Frame.ExecuteScriptAsync(TinyZoneCinemaMode.BuildInjectIframeCssScript());
                }
                catch
                {
                    // Some embed frames block injection; main-page cinema mode still applies.
                }
            };
        }
        catch
        {
            // Ignore frame hook errors.
        }
    }

    private async Task PrepareCinemaViewAsync()
    {
        if (!_isInitialized)
            return;

        HidePlayerSurface();
        LoadingHintText.Text = "Loading page in background...";

        await WaitForPageReadyAsync();

        LoadingHintText.Text = "Applying cinema view...";
        await ApplyCinemaHiddenAsync();

        LoadingHintText.Text = "Selecting Server 1...";
        await PlayerWebView.CoreWebView2.ExecuteScriptAsync(TinyZoneCinemaMode.SelectServer1Script);
        await Task.Delay(200);

        LoadingHintText.Text = "Starting playback...";
        await TryAutoStartPlaybackAsync();

        await PlayerWebView.CoreWebView2.ExecuteScriptAsync(TinyZoneCinemaMode.SelectServer1Script);
        await ApplyCinemaHiddenAsync();

        if (!await VerifyCinemaAppliedAsync())
            await ApplyCinemaHiddenAsync();

        await PlayerWebView.CoreWebView2.ExecuteScriptAsync(
            TinyZoneCinemaMode.BuildRevealScript(_movieTitle));

        ShowPlayerSurface();
        StatusText.Text = "Cinema mode active. Listening for stream URLs...";
    }

    private async Task ApplyCinemaHiddenAsync()
    {
        await PlayerWebView.CoreWebView2.ExecuteScriptAsync(
            TinyZoneCinemaMode.BuildSetModeScript(true, _movieTitle, reveal: false));
        await Task.Delay(50);
    }

    private async Task<bool> VerifyCinemaAppliedAsync()
    {
        var result = await PlayerWebView.CoreWebView2.ExecuteScriptAsync(TinyZoneCinemaMode.BuildVerifyScript());
        return result.Contains("ok", StringComparison.OrdinalIgnoreCase);
    }

    private async Task WaitForPageReadyAsync()
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var result = await PlayerWebView.CoreWebView2.ExecuteScriptAsync(
                """
                (() => {
                    if (document.getElementById('srv-1')) return 'ready';
                    if (document.getElementById('play-now')) return 'ready-play-cover';
                    if (document.getElementById('watch')) return 'ready-watch';
                    if (document.getElementById('header')) return 'ready-header';
                    return 'waiting';
                })()
                """);

            if (result.Contains("ready", StringComparison.OrdinalIgnoreCase))
                return;

            await Task.Delay(100);
        }
    }

    private async Task ApplyCinemaModeAsync(bool reveal = false)
    {
        if (!_isInitialized)
            return;

        try
        {
            await PlayerWebView.CoreWebView2.ExecuteScriptAsync(
                TinyZoneCinemaMode.BuildSetModeScript(_cinemaModeEnabled, _movieTitle, reveal));
        }
        catch
        {
            // Page may still be initializing.
        }
    }

    private void OnWebResourceResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        if (!_urlCaptureEnabled)
            return;

        var url = e.Request.Uri;
        if (string.IsNullOrWhiteSpace(url) || !_seenUrls.Add(url))
            return;

        var isStream = M3U8Capture.IsStreamUrl(url);
        Dispatcher.Invoke(() =>
        {
            AddLiveUrl(url, isStream);

            if (isStream && _capturedUrls.Add(url))
                HandleCapturedStream(url);
        });
    }

    private void AddLiveUrl(string url, bool isStream)
    {
        _liveUrls.Insert(0, new LiveUrlEntry
        {
            Url = url,
            IsStream = isStream,
            Time = DateTime.Now.ToString("HH:mm:ss")
        });

        _urlsView.Refresh();
        UpdateUrlCountText();
    }

    private bool UrlFilter(object item)
    {
        if (item is not LiveUrlEntry entry)
            return false;

        return !_hlsFilterOnly || entry.IsStream;
    }

    private void UpdateUrlCountText()
    {
        if (!_urlCaptureEnabled)
        {
            UrlCountText.Text = _liveUrls.Count > 0
                ? $"{_liveUrls.Count} captured · capture paused"
                : "Capture paused";
            return;
        }

        var streamCount = _liveUrls.Count(u => u.IsStream);
        var visibleCount = _urlsView.Cast<object>().Count();

        if (_hlsFilterOnly)
        {
            UrlCountText.Text = visibleCount > 0
                ? $"{visibleCount} HLS stream{(visibleCount == 1 ? "" : "s")} · {_liveUrls.Count} total"
                : $"0 HLS streams · {_liveUrls.Count} total";
            return;
        }

        UrlCountText.Text = streamCount > 0
            ? $"{_liveUrls.Count} captured · {streamCount} stream{(streamCount == 1 ? "" : "s")}"
            : $"{_liveUrls.Count} captured";
    }

    private void HandleCapturedStream(string streamUrl)
    {
        StatusText.Text = $"HLS stream captured ({_capturedUrls.Count} found). Use the play button on a stream in the URLs panel.";
    }

    private async Task TryAutoStartPlaybackAsync()
    {
        if (!_isInitialized)
            return;

        try
        {
            await PlayerWebView.CoreWebView2.ExecuteScriptAsync(TinyZoneCinemaMode.SelectServer1Script);

            var result = await PlayerWebView.CoreWebView2.ExecuteScriptAsync(
                """
                (() => {
                    if (typeof window.tinyCinemaSelectServer1 === 'function') {
                        window.tinyCinemaSelectServer1();
                    }

                    const playNow = document.getElementById('play-now');
                    if (playNow) {
                        playNow.click();
                        return 'clicked-play-now';
                    }

                    const playButton = document.querySelector('.dp-w-c-play');
                    if (playButton) {
                        playButton.click();
                        return 'clicked-cover-play';
                    }

                    return 'no-play-control';
                })()
                """);

            if (result.Contains("clicked", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(600);
                await PlayerWebView.CoreWebView2.ExecuteScriptAsync(TinyZoneCinemaMode.SelectServer1Script);
                await ApplyCinemaHiddenAsync();
            }
        }
        catch
        {
            // User can start playback manually.
        }
    }

    private async void CinemaModeToggle_Click(object sender, RoutedEventArgs e)
    {
        _cinemaModeEnabled = CinemaModeToggle.IsChecked == true;

        if (_cinemaModeEnabled)
        {
            HidePlayerSurface();
            await PrepareCinemaViewAsync();
            StatusText.Text = "Cinema mode enabled.";
            return;
        }

        await ApplyCinemaModeAsync();
        if (_isInitialized)
            await PlayerWebView.CoreWebView2.ExecuteScriptAsync("window.tinyCinemaReveal && window.tinyCinemaReveal();");
        ShowPlayerSurface();
        StatusText.Text = "Showing full TinyZone page.";
    }

    private void OpenHlsInPlayerButton_Click(object sender, RoutedEventArgs e)
    {
        var url = ResolveUrlFromSender(sender);
        if (string.IsNullOrWhiteSpace(url))
            return;

        var player = _selectedPlayer == PlayerNames.InAppBrowser ? PlayerNames.TinyPlayer : _selectedPlayer;

        try
        {
            PlayerLauncher.Launch(url, player);
            StatusText.Text = $"Opened stream in {player}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to launch {player}:\n{ex.Message}", "Player Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DownloadHlsButton_Click(object sender, RoutedEventArgs e)
    {
        var url = ResolveUrlFromSender(sender);
        if (string.IsNullOrWhiteSpace(url))
            return;

        if (!FfmpegDownloader.TryResolveFfmpegPath(out _))
        {
            MessageBox.Show(
                "ffmpeg was not found.\n\nInstall ffmpeg and add it to your PATH, or place ffmpeg.exe next to TinyCinema.",
                "ffmpeg Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var downloadWindow = new DownloadProgressWindow(url, _movieTitle, _pageUrl)
        {
            Owner = this
        };
        downloadWindow.Show();
        StatusText.Text = "Download started in background window.";
    }

    private void UrlsTabButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleUrlsPanel(open: !_urlsPanelOpen);
    }

    private void HideUrlsPanel_Click(object sender, RoutedEventArgs e)
    {
        ToggleUrlsPanel(open: false);
    }

    private void ToggleUrlsPanel(bool open)
    {
        _urlsPanelOpen = open;
        UrlsPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        UrlsPanelColumn.Width = new GridLength(open ? UrlsPanelWidth : 0);
        UrlsTabButton.Background = open
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 37, 37))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 26, 26));
        UrlsTabButton.ToolTip = open ? "Hide URLs panel" : "Show URLs panel";
        UrlsTabIcon.Icon = open
            ? FontAwesome.WPF.FontAwesomeIcon.ChevronLeft
            : FontAwesome.WPF.FontAwesomeIcon.Link;
    }

    private void HlsFilterToggle_Click(object sender, RoutedEventArgs e)
    {
        _hlsFilterOnly = HlsFilterToggle.IsChecked == true;
        _urlsView.Refresh();
        UpdateUrlCountText();
    }

    private void UrlCaptureToggle_Click(object sender, RoutedEventArgs e)
    {
        _urlCaptureEnabled = UrlCaptureToggle.IsChecked == true;
        UpdateUrlCountText();
        StatusText.Text = _urlCaptureEnabled
            ? "URL capture enabled. Listening for stream URLs..."
            : "URL capture paused. Existing URLs are kept.";
    }

    private void CopyUrlButton_Click(object sender, RoutedEventArgs e)
    {
        var url = ResolveUrlFromSender(sender);
        if (string.IsNullOrWhiteSpace(url))
            return;

        CopyUrlToClipboard(url);
    }

    private static string? ResolveUrlFromSender(object sender)
    {
        if (sender is not Button button)
            return null;

        if (button.CommandParameter is string commandUrl && !string.IsNullOrWhiteSpace(commandUrl))
            return commandUrl;

        if (button.DataContext is LiveUrlEntry entry && !string.IsNullOrWhiteSpace(entry.Url))
            return entry.Url;

        return button.Tag as string;
    }

    private void CopyUrlToClipboard(string url)
    {
        try
        {
            Clipboard.SetDataObject(url, true);
            StatusText.Text = "URL copied to clipboard.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not copy URL:\n{ex.Message}", "Copy Failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ClearUrlsButton_Click(object sender, RoutedEventArgs e)
    {
        _liveUrls.Clear();
        _urlsView.Refresh();
        UpdateUrlCountText();
        StatusText.Text = "URL list cleared. New requests will appear here.";
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _capturedUrls.Clear();
        _seenUrls.Clear();
        _liveUrls.Clear();
        UrlCaptureToggle.IsChecked = true;
        _urlCaptureEnabled = true;
        StatusText.Text = "Refreshing page...";
        UpdateUrlCountText();
        HidePlayerSurface();
        LoadingHintText.Text = "Preparing player view...";

        if (_isInitialized)
            PlayerWebView.CoreWebView2.Reload();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_isInitialized)
        {
            try
            {
                TinyZonePopupBlocker.Detach(PlayerWebView.CoreWebView2);
                PlayerWebView.CoreWebView2.FrameCreated -= OnFrameCreated;
                PlayerWebView.CoreWebView2.WebResourceResponseReceived -= OnWebResourceResponseReceived;
                PlayerWebView.Dispose();
            }
            catch
            {
                // Ignore cleanup errors.
            }
        }

        base.OnClosed(e);
    }
}
