using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace TinyCinema;

public partial class TinyZonePlayerWindow : Window
{
    private enum SidePanelKind
    {
        None,
        Episodes,
        Urls
    }

    private enum TvPlayerPhase
    {
        ScrapingCatalog,
        ResolvingEpisode,
        ShowingEmbed
    }

    private const double UrlsPanelWidth = 372;
    private readonly string _selectedPlayer;
    private readonly string _movieTitle;
    private readonly string _pageUrl;
    private readonly string _posterImageUrl;
    private readonly HashSet<string> _capturedUrls = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenUrls = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<LiveUrlEntry> _liveUrls = new();
    private readonly ICollectionView _urlsView;
    private bool _isInitialized;
    private bool _hlsFilterOnly;
    private string _urlSearchText = string.Empty;
    private bool _urlCaptureEnabled = true;
    private bool _cinemaModeEnabled = true;
    private bool _popupBlockerEnabled = true;
    private readonly bool _isTvShow;
    private readonly ObservableCollection<TvEpisodeEntry> _allTvEpisodes = new();
    private readonly ObservableCollection<TvEpisodeEntry> _filteredTvEpisodes = new();
    private SidePanelKind _openSidePanel = SidePanelKind.None;
    private TvPlayerPhase _tvPhase;
    private TvEpisodeEntry? _currentTvEpisode;
    private CancellationTokenSource? _episodeResolveCts;
    private int _episodeResolveGeneration;
    private TaskCompletionSource<string>? _embedResolveTcs;
    private bool _suppressEpisodeSelection;
    private bool _tvNavigationScrapeActive;
    private int _selectedSeasonFilter;

    public TinyZonePlayerWindow(Movie movie, string selectedPlayer)
    {
        InitializeComponent();
        _selectedPlayer = selectedPlayer;
        _movieTitle = movie.Title;
        _pageUrl = movie.Url;
        _posterImageUrl = movie.ImageUrl;
        _isTvShow = movie.ContentType == CatalogContentType.TvShow;
        _cinemaModeEnabled = !_isTvShow;
        TitleText.Text = movie.Title;
        Title = movie.Title;

        if (_isTvShow)
        {
            EpisodesTabButton.Visibility = Visibility.Visible;
            CinemaModeToggle.Visibility = Visibility.Collapsed;
            EpisodesListBox.ItemsSource = _filteredTvEpisodes;
            LoadingHintText.Text = "Finding episodes...";
            StatusText.Text = "Loading TV show catalog...";
        }
        else
        {
            CinemaModeToggle.IsChecked = true;
            StatusText.Text = "Loading page and listening for stream URLs...";
        }

        _urlsView = CollectionViewSource.GetDefaultView(_liveUrls);
        _urlsView.Filter = UrlFilter;
        UrlsItemsControl.ItemsSource = _urlsView;
        HidePlayerSurface();
        Loaded += (_, _) =>
        {
            if (_isTvShow)
                ToggleSidePanel(SidePanelKind.Episodes, open: true);
            else
                ToggleSidePanel(SidePanelKind.Urls, open: true);
        };
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

            _popupBlockerEnabled = SettingsWindow.GetIsPopupBlockerEnabled();
            if (_popupBlockerEnabled)
            {
                await core.AddScriptToExecuteOnDocumentCreatedAsync(TinyZonePopupBlocker.BlockPopupsScript);
                TinyZonePopupBlocker.Attach(core);
            }

            if (!_isTvShow)
            {
                await core.AddScriptToExecuteOnDocumentCreatedAsync(TinyZoneCinemaMode.EarlyHideScript);
                await core.AddScriptToExecuteOnDocumentCreatedAsync(TinyZoneCinemaMode.BootstrapScript);
                core.FrameCreated += OnFrameCreated;
            }

            core.WebResourceResponseReceived += OnWebResourceResponseReceived;

            if (_isTvShow)
            {
                core.NavigationStarting += OnTvNavigationStarting;
                core.NavigationCompleted += OnTvNavigationCompleted;
                _tvPhase = TvPlayerPhase.ScrapingCatalog;
                core.Navigate(pageUrl);
                return;
            }

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

    private void OnTvNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (_tvPhase == TvPlayerPhase.ShowingEmbed &&
            TvEmbedResolver.IsEmbedPageUrl(e.Uri))
            return;

        HidePlayerSurface();
    }

    private async void OnTvNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!_isInitialized || !_isTvShow)
            return;

        if (!e.IsSuccess)
        {
            if (_tvPhase == TvPlayerPhase.ShowingEmbed)
            {
                ShowPlayerSurface();
                StatusText.Text = $"Failed to load player (HTTP {e.HttpStatusCode}).";
            }
            else if (_tvPhase == TvPlayerPhase.ResolvingEpisode)
            {
                _embedResolveTcs?.TrySetException(
                    new InvalidOperationException($"Failed to load episode page (HTTP {e.HttpStatusCode})."));
            }
            else
            {
                StatusText.Text = $"Failed to load show page (HTTP {e.HttpStatusCode}).";
            }

            return;
        }

        try
        {
            if (_tvPhase == TvPlayerPhase.ScrapingCatalog)
            {
                if (_tvNavigationScrapeActive)
                    return;

                await BootstrapTvShowAsync();
            }
            else if (_tvPhase == TvPlayerPhase.ResolvingEpisode)
            {
                await TryCompleteEmbedResolveFromDomAsync();

                var source = PlayerWebView.CoreWebView2.Source;
                if (_embedResolveTcs != null &&
                    !_embedResolveTcs.Task.IsCompleted &&
                    TvEmbedResolver.IsEmbedPageUrl(source))
                {
                    _embedResolveTcs.TrySetResult(NormalizeEmbedUrl(source));
                }
            }
            else if (_tvPhase == TvPlayerPhase.ShowingEmbed)
            {
                ShowPlayerSurface();
                StatusText.Text = _currentTvEpisode == null
                    ? "Player loaded."
                    : $"Now playing {_currentTvEpisode.DisplayLabel}";
            }
        }
        catch (Exception ex)
        {
            if (_tvPhase == TvPlayerPhase.ResolvingEpisode)
                _embedResolveTcs?.TrySetException(ex);
            else
            {
                ShowPlayerSurface();
                StatusText.Text = "TV show player failed to start.";
                MessageBox.Show(
                    $"Failed to prepare TV show playback:\n{ex.Message}",
                    "Player Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private async Task BootstrapTvShowAsync()
    {
        var showId = MovieLairTvDetailsParser.ExtractShowId(_pageUrl);
        if (showId is > 0)
        {
            var cachedEpisodes = TvShowEpisodeCache.TryLoad(showId.Value);
            if (cachedEpisodes is { Count: > 0 })
            {
                LoadingHintText.Text = "Loaded cached episodes";
                StatusText.Text = $"Loaded {cachedEpisodes.Count} episodes from cache";

                await PopulateEpisodesAndPlayAsync(cachedEpisodes);
                return;
            }
        }

        LoadingHintText.Text = "Scanning all seasons...";
        StatusText.Text = "Scanning episode list from every season...";

        var episodes = await ScrapeEpisodesAsync();

        var seasonsRaw = await PlayerWebView.CoreWebView2.ExecuteScriptAsync(MovieLairEpisodeScraper.ReadSeasonsScript);
        var expectedSeasons = MovieLairEpisodeScraper.ParseSeasonList(seasonsRaw);
        var scrapedSeasons = episodes.Select(episode => episode.Season).Distinct().Count();

        if (episodes.Count == 0 ||
            (expectedSeasons.Count > 1 && scrapedSeasons < expectedSeasons.Count))
        {
            var navigatedEpisodes = await ScrapeEpisodesByNavigationAsync();
            if (navigatedEpisodes.Count > episodes.Count)
                episodes = navigatedEpisodes;
        }

        var usedFallback = false;
        if (episodes.Count == 0)
        {
            usedFallback = true;
            episodes.Add(new TvEpisodeEntry
            {
                Season = 1,
                Episode = 1,
                Title = _movieTitle,
                MovieLairUrl = _pageUrl
            });
        }
        else if (showId is > 0 && !usedFallback)
        {
            TvShowEpisodeCache.Save(showId.Value, _pageUrl, episodes);
        }

        await PopulateEpisodesAndPlayAsync(episodes);
    }

    private async Task PopulateEpisodesAndPlayAsync(IReadOnlyList<TvEpisodeEntry> episodes)
    {
        _allTvEpisodes.Clear();
        foreach (var episode in episodes)
            _allTvEpisodes.Add(episode);

        PopulateSeasonFilter();
        ApplySeasonFilter();
        UpdateEpisodeCountText();

        var firstEpisode = _allTvEpisodes[0];
        await PlayTvEpisodeAsync(firstEpisode);
    }

    private async Task<List<TvEpisodeEntry>> ScrapeEpisodesAsync()
    {
        var core = PlayerWebView.CoreWebView2;

        List<int> seasons = [];
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var seasonsRaw = await core.ExecuteScriptAsync(MovieLairEpisodeScraper.ReadSeasonsScript);
            seasons = MovieLairEpisodeScraper.ParseSeasonList(seasonsRaw);
            if (seasons.Count > 0)
                break;

            await Task.Delay(500);
        }

        var merged = new Dictionary<string, TvEpisodeEntry>(StringComparer.OrdinalIgnoreCase);

        if (seasons.Count == 0)
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var raw = await core.ExecuteScriptAsync(MovieLairEpisodeScraper.ScrapeVisibleEpisodesScript);
                var batch = MovieLairEpisodeScraper.ParseScrapeResult(raw);
                if (batch.Count > 0)
                    return batch;

                await Task.Delay(500);
            }

            return [];
        }

        foreach (var season in seasons)
        {
            LoadingHintText.Text = $"Scanning season {season}...";
            StatusText.Text = $"Scanning season {season} of {seasons.Count}...";

            await core.ExecuteScriptAsync(MovieLairEpisodeScraper.BuildSelectSeasonScript(season));

            for (var attempt = 0; attempt < 12; attempt++)
            {
                await Task.Delay(400);

                var raw = await core.ExecuteScriptAsync(MovieLairEpisodeScraper.ScrapeVisibleEpisodesScript);
                var batch = MovieLairEpisodeScraper.ParseScrapeResult(raw)
                    .Where(episode => episode.Season == season)
                    .ToList();

                if (batch.Count > 0)
                {
                    foreach (var episode in batch)
                        merged[$"{episode.Season}-{episode.Episode}"] = episode;
                    break;
                }
            }
        }

        return merged.Values
            .OrderBy(episode => episode.Season)
            .ThenBy(episode => episode.Episode)
            .ToList();
    }

    private async Task<List<TvEpisodeEntry>> ScrapeEpisodesByNavigationAsync()
    {
        var core = PlayerWebView.CoreWebView2;
        var seasonsRaw = await core.ExecuteScriptAsync(MovieLairEpisodeScraper.ReadSeasonsScript);
        var seasons = MovieLairEpisodeScraper.ParseSeasonList(seasonsRaw);
        if (seasons.Count == 0)
            return [];

        var showIdMatch = System.Text.RegularExpressions.Regex.Match(_pageUrl, @"/watch-tv/(\d+)");
        if (!showIdMatch.Success)
            return [];

        var showId = showIdMatch.Groups[1].Value;
        var baseUrl = $"https://movielair.cc/watch-tv/{showId}";
        var merged = new Dictionary<string, TvEpisodeEntry>(StringComparer.OrdinalIgnoreCase);
        var scrapeCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnScrapeNavCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (_tvPhase != TvPlayerPhase.ScrapingCatalog || !args.IsSuccess)
                return;

            scrapeCompletion.TrySetResult(true);
        }

        core.NavigationCompleted += OnScrapeNavCompleted;
        _tvNavigationScrapeActive = true;

        try
        {
            foreach (var season in seasons)
            {
                scrapeCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                LoadingHintText.Text = $"Loading season {season}...";

                core.Navigate($"{baseUrl}?season={season}&episode=1");

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await using var reg = timeout.Token.Register(() => scrapeCompletion.TrySetResult(false));
                await scrapeCompletion.Task;

                await Task.Delay(600);

                var raw = await core.ExecuteScriptAsync(MovieLairEpisodeScraper.ScrapeVisibleEpisodesScript);
                foreach (var episode in MovieLairEpisodeScraper.ParseScrapeResult(raw))
                    merged[$"{episode.Season}-{episode.Episode}"] = episode;
            }
        }
        finally
        {
            _tvNavigationScrapeActive = false;
            core.NavigationCompleted -= OnScrapeNavCompleted;
        }

        return merged.Values
            .OrderBy(episode => episode.Season)
            .ThenBy(episode => episode.Episode)
            .ToList();
    }

    private void PopulateSeasonFilter()
    {
        var seasons = _allTvEpisodes
            .Select(episode => episode.Season)
            .Distinct()
            .OrderBy(season => season)
            .ToList();

        SeasonFilterComboBox.ItemsSource = seasons;
        if (seasons.Count == 0)
            return;

        _selectedSeasonFilter = seasons[0];
        SeasonFilterComboBox.SelectedItem = seasons[0];
    }

    private void ApplySeasonFilter()
    {
        _filteredTvEpisodes.Clear();

        foreach (var episode in _allTvEpisodes.Where(episode => episode.Season == _selectedSeasonFilter))
            _filteredTvEpisodes.Add(episode);

        UpdateEpisodeCountText();
    }

    private void UpdateEpisodeCountText()
    {
        EpisodeCountText.Text = _allTvEpisodes.Count == 1
            ? "1 episode"
            : $"{_allTvEpisodes.Count} episodes · Season {_selectedSeasonFilter}";
    }

    private async Task PlayTvEpisodeAsync(TvEpisodeEntry episode)
    {
        if (!_isInitialized)
            return;

        var generation = Interlocked.Increment(ref _episodeResolveGeneration);
        _episodeResolveCts?.Cancel();
        _episodeResolveCts?.Dispose();
        _episodeResolveCts = new CancellationTokenSource();

        SetCurrentEpisode(episode);
        HidePlayerSurface();
        LoadingHintText.Text = $"Loading {episode.DisplayLabel}...";
        StatusText.Text = $"Preparing {episode.DisplayLabel}...";
        _tvPhase = TvPlayerPhase.ResolvingEpisode;

        try
        {
            var embedUrl = await ResolveEmbedUrlAsync(episode.MovieLairUrl, _episodeResolveCts.Token);
            if (generation != _episodeResolveGeneration || _episodeResolveCts.Token.IsCancellationRequested)
                return;

            _tvPhase = TvPlayerPhase.ShowingEmbed;
            HidePlayerSurface();
            LoadingHintText.Text = $"Starting {episode.DisplayLabel}...";
            PlayerWebView.CoreWebView2.Navigate(embedUrl);
        }
        catch (OperationCanceledException)
        {
            // A newer episode selection replaced this resolve.
        }
        catch (Exception ex)
        {
            ShowPlayerSurface();
            StatusText.Text = $"Could not load {episode.DisplayLabel}.";
            MessageBox.Show(
                $"Failed to load episode embed:\n{ex.Message}",
                "Episode Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task<string> ResolveEmbedUrlAsync(string movieLairUrl, CancellationToken cancellationToken)
    {
        _embedResolveTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(25));
        await using var registration = timeoutCts.Token.Register(() =>
            _embedResolveTcs.TrySetException(new TimeoutException("Timed out waiting for the episode player embed.")));

        PlayerWebView.CoreWebView2.Navigate(movieLairUrl);
        return await _embedResolveTcs.Task;
    }

    private async Task TryCompleteEmbedResolveFromDomAsync()
    {
        if (_embedResolveTcs == null || _embedResolveTcs.Task.IsCompleted)
            return;

        var raw = await PlayerWebView.CoreWebView2.ExecuteScriptAsync(TvEmbedResolver.FindEmbedIframeScript);
        var iframeUrl = JsonSerializer.Deserialize<string>(raw)?.Trim();
        if (!string.IsNullOrWhiteSpace(iframeUrl) && TvEmbedResolver.IsEmbedPageUrl(iframeUrl))
            _embedResolveTcs.TrySetResult(NormalizeEmbedUrl(iframeUrl));
    }

    private static string NormalizeEmbedUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
            return absolute.ToString();

        return url;
    }

    private void SetCurrentEpisode(TvEpisodeEntry episode)
    {
        if (_currentTvEpisode != null)
            _currentTvEpisode.IsCurrent = false;

        _currentTvEpisode = episode;
        episode.IsCurrent = true;

        _suppressEpisodeSelection = true;
        EpisodesListBox.SelectedItem = episode;
        EpisodesListBox.ScrollIntoView(episode);
        _suppressEpisodeSelection = false;
    }

    private void TryResolveEmbedFromNetworkUrl(string url)
    {
        if (_tvPhase != TvPlayerPhase.ResolvingEpisode ||
            _embedResolveTcs == null ||
            _embedResolveTcs.Task.IsCompleted ||
            !TvEmbedResolver.IsEmbedPageUrl(url))
            return;

        _embedResolveTcs.TrySetResult(NormalizeEmbedUrl(url));
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
        if (string.IsNullOrWhiteSpace(url))
            return;

        TryResolveEmbedFromNetworkUrl(url);

        if (!_seenUrls.Add(url))
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

        if (_hlsFilterOnly && !entry.IsStream)
            return false;

        if (!string.IsNullOrWhiteSpace(_urlSearchText) &&
            !entry.Url.Contains(_urlSearchText, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private void UpdateUrlCountText()
    {
        var visibleCount = _urlsView.Cast<object>().Count();
        var hasSearch = !string.IsNullOrWhiteSpace(_urlSearchText);

        if (!_urlCaptureEnabled)
        {
            if (hasSearch)
            {
                UrlCountText.Text = visibleCount > 0
                    ? $"{visibleCount} matching · {_liveUrls.Count} total · capture paused"
                    : $"0 matching · {_liveUrls.Count} total · capture paused";
                return;
            }

            UrlCountText.Text = _liveUrls.Count > 0
                ? $"{_liveUrls.Count} captured · capture paused"
                : "Capture paused";
            return;
        }

        var streamCount = _liveUrls.Count(u => u.IsStream);

        if (hasSearch)
        {
            UrlCountText.Text = visibleCount > 0
                ? $"{visibleCount} matching · {_liveUrls.Count} total"
                : $"0 matching · {_liveUrls.Count} total";
            return;
        }

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

    private void UrlSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _urlSearchText = UrlSearchTextBox.Text.Trim();
        _urlsView.Refresh();
        UpdateUrlCountText();
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
        try
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

            DownloadOptionsDialog optionsDialog;
            try
            {
                optionsDialog = new DownloadOptionsDialog
                {
                    Owner = this
                };
            }
            catch (Exception ex)
            {
                DownloadDebugHelper.ShowError("Creating download options dialog", ex, $"Stream URL: {url}");
                return;
            }

            if (optionsDialog.ShowDialog() != true || optionsDialog.Request == null)
                return;

            DownloadProgressWindow downloadWindow;
            try
            {
                downloadWindow = new DownloadProgressWindow(url, _movieTitle, _pageUrl, optionsDialog.Request)
                {
                    Owner = this
                };
            }
            catch (Exception ex)
            {
                DownloadDebugHelper.ShowError(
                    "Creating download progress window",
                    ex,
                    $"Stream URL: {url}\nMode: {optionsDialog.Request.Mode}");
                return;
            }

            downloadWindow.Show();

            StatusText.Text = optionsDialog.Request.Mode == StreamDownloadMode.Clip
                ? "Clip download started in background window."
                : "Download started in background window.";
        }
        catch (Exception ex)
        {
            DownloadDebugHelper.ShowError("Starting HLS download", ex);
        }
    }

    private async void SendToRokuButton_Click(object sender, RoutedEventArgs e)
    {
        var url = ResolveUrlFromSender(sender);
        if (string.IsNullOrWhiteSpace(url))
            return;

        var (rokuIp, _, _) = SettingsWindow.GetRokuCredentials();
        if (string.IsNullOrWhiteSpace(rokuIp))
        {
            MessageBox.Show(
                "Set your Roku IP address in Settings before sideloading.\n\nDeveloper Mode must be enabled on the Roku.",
                "Roku Settings Required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (sender is Button button)
            button.IsEnabled = false;

        StatusText.Text = "Building Roku channel zip and poster images...";

        try
        {
            var result = await RokuSideloadService.SideloadAsync(_movieTitle, url, rokuIp, _posterImageUrl);

            if (result.Success)
            {
                StatusText.Text = "Zip created. Upload it in your browser.";
            }
            else
            {
                StatusText.Text = result.Message;
                MessageBox.Show(
                    result.DisplayText,
                    "Roku Sideload",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Roku sideload failed.";
            MessageBox.Show(
                $"Unexpected error while preparing Roku sideload.\n\n{ex.Message}",
                "Roku Sideload",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            if (sender is Button rokuButton)
                rokuButton.IsEnabled = true;
        }
    }

    private void UrlsTabButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleSidePanel(SidePanelKind.Urls, open: _openSidePanel != SidePanelKind.Urls);
    }

    private void EpisodesTabButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleSidePanel(SidePanelKind.Episodes, open: _openSidePanel != SidePanelKind.Episodes);
    }

    private void HideUrlsPanel_Click(object sender, RoutedEventArgs e)
    {
        ToggleSidePanel(SidePanelKind.Urls, open: false);
    }

    private void HideEpisodesPanel_Click(object sender, RoutedEventArgs e)
    {
        ToggleSidePanel(SidePanelKind.Episodes, open: false);
    }

    private void ToggleSidePanel(SidePanelKind kind, bool open)
    {
        if (!open)
        {
            _openSidePanel = SidePanelKind.None;
            UrlsPanel.Visibility = Visibility.Collapsed;
            EpisodesPanel.Visibility = Visibility.Collapsed;
            UrlsPanelColumn.Width = new GridLength(0);
            ResetSidePanelTabStyles();
            return;
        }

        _openSidePanel = kind;
        UrlsPanelColumn.Width = new GridLength(UrlsPanelWidth);
        UrlsPanel.Visibility = kind == SidePanelKind.Urls ? Visibility.Visible : Visibility.Collapsed;
        EpisodesPanel.Visibility = kind == SidePanelKind.Episodes ? Visibility.Visible : Visibility.Collapsed;
        UpdateSidePanelTabStyles();
    }

    private void ResetSidePanelTabStyles()
    {
        var inactive = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 26, 26));
        UrlsTabButton.Background = inactive;
        EpisodesTabButton.Background = inactive;
        UrlsTabButton.ToolTip = "Show URLs panel";
        EpisodesTabButton.ToolTip = "Show Episodes panel";
        UrlsTabIcon.Icon = FontAwesome.WPF.FontAwesomeIcon.Link;
        EpisodesTabIcon.Icon = FontAwesome.WPF.FontAwesomeIcon.List;
    }

    private void UpdateSidePanelTabStyles()
    {
        var active = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 37, 37));
        var inactive = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 26, 26));

        UrlsTabButton.Background = _openSidePanel == SidePanelKind.Urls ? active : inactive;
        EpisodesTabButton.Background = _openSidePanel == SidePanelKind.Episodes ? active : inactive;
        UrlsTabButton.ToolTip = _openSidePanel == SidePanelKind.Urls ? "Hide URLs panel" : "Show URLs panel";
        EpisodesTabButton.ToolTip = _openSidePanel == SidePanelKind.Episodes ? "Hide Episodes panel" : "Show Episodes panel";
        UrlsTabIcon.Icon = _openSidePanel == SidePanelKind.Urls
            ? FontAwesome.WPF.FontAwesomeIcon.ChevronLeft
            : FontAwesome.WPF.FontAwesomeIcon.Link;
        EpisodesTabIcon.Icon = _openSidePanel == SidePanelKind.Episodes
            ? FontAwesome.WPF.FontAwesomeIcon.ChevronLeft
            : FontAwesome.WPF.FontAwesomeIcon.List;
    }

    private void SeasonFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SeasonFilterComboBox.SelectedItem is not int season)
            return;

        _selectedSeasonFilter = season;
        ApplySeasonFilter();
    }

    private async void EpisodesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEpisodeSelection || EpisodesListBox.SelectedItem is not TvEpisodeEntry episode)
            return;

        if (_currentTvEpisode != null &&
            _currentTvEpisode.Season == episode.Season &&
            _currentTvEpisode.Episode == episode.Episode &&
            _tvPhase == TvPlayerPhase.ShowingEmbed)
            return;

        await PlayTvEpisodeAsync(episode);
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
        UrlSearchTextBox.Text = string.Empty;
        _urlSearchText = string.Empty;
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
        UpdateUrlCountText();
        HidePlayerSurface();

        if (_isTvShow && _currentTvEpisode != null)
        {
            LoadingHintText.Text = $"Reloading {_currentTvEpisode.DisplayLabel}...";
            StatusText.Text = $"Reloading {_currentTvEpisode.DisplayLabel}...";
            await PlayTvEpisodeAsync(_currentTvEpisode);
            return;
        }

        StatusText.Text = "Refreshing page...";
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
        _episodeResolveCts?.Cancel();
        _episodeResolveCts?.Dispose();

        if (_isInitialized)
        {
            try
            {
                if (_popupBlockerEnabled)
                    TinyZonePopupBlocker.Detach(PlayerWebView.CoreWebView2);

                if (_isTvShow)
                {
                    PlayerWebView.CoreWebView2.NavigationStarting -= OnTvNavigationStarting;
                    PlayerWebView.CoreWebView2.NavigationCompleted -= OnTvNavigationCompleted;
                }
                else
                {
                    PlayerWebView.CoreWebView2.FrameCreated -= OnFrameCreated;
                }

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
