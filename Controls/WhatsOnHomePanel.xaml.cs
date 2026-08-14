using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TinyCinema;

public partial class WhatsOnHomePanel : UserControl
{
    private const int NetflixBatchSize = 30;

    private readonly ObservableCollection<WhatsOnMovieEntry> _displayedMovies = new();
    private readonly List<WhatsOnStreamingService> _services;
    private List<WhatsOnNetflixItem> _allNetflixItems = [];
    private List<WhatsOnNetflixItem> _fullNetflixItems = [];
    private Dictionary<string, Movie> _matchIndex = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<Movie> _localCatalog = [];
    private WhatsOnNetflixCatalog? _netflixCatalog;
    private CancellationTokenSource? _loadCts;
    private string _searchText = string.Empty;
    private bool _showingNetflix;
    private int _netflixDisplayIndex;
    private bool _isLoadingNetflixBatch;
    private DispatcherTimer? _scrollDebounceTimer;

    public event EventHandler<WhatsOnMovieEntry>? MovieSelected;
    public event EventHandler<bool>? ViewChanged;

    public WhatsOnMovieEntry? SelectedEntry { get; private set; }

    public WhatsOnHomePanel()
    {
        InitializeComponent();

        _services =
        [
            new WhatsOnStreamingService
            {
                Id = "netflix",
                Name = "Netflix"
            }
        ];

        MoviesItemsControl.ItemsSource = _displayedMovies;
        ServicesItemsControl.ItemsSource = _services;
        MoviesScrollViewer.ScrollChanged += MoviesScrollViewer_ScrollChanged;

        _scrollDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _scrollDebounceTimer.Tick += ScrollDebounceTimer_Tick;
    }

    public void SetLocalCatalog(IReadOnlyList<Movie> catalog) => _localCatalog = catalog;

    public async Task EnsureLoadedAsync()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        ShowServicesView();

        try
        {
            _netflixCatalog = await WhatsOnNetflixStore.TryLoadAsync(token);
            if (_netflixCatalog != null && !WhatsOnNetflixStore.IsStale(_netflixCatalog))
                return;

            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingStatusText.Text = _netflixCatalog == null
                ? "Fetching Netflix movies for the first time..."
                : "Netflix listings are older than 24 hours. Refreshing...";

            var progress = new Progress<WhatsOnFetchProgress>(report =>
            {
                Dispatcher.Invoke(() => LoadingStatusText.Text = report.Status);
            });

            _netflixCatalog = await WhatsOnNetflixFetcher.FetchAllMoviesAsync(progress, token);
            await WhatsOnNetflixStore.SaveAsync(_netflixCatalog, token);
        }
        catch (OperationCanceledException)
        {
            // Navigated away.
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not refresh Netflix listings:\n{ex.Message}",
                "What's On",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    public void ApplySearch(string searchText)
    {
        _searchText = searchText.Trim();
        if (!_showingNetflix)
            return;

        if (string.IsNullOrWhiteSpace(_searchText))
        {
            _allNetflixItems = _fullNetflixItems;
            _ = ResetNetflixBatchViewAsync();
            return;
        }

        _allNetflixItems = _fullNetflixItems
            .Where(MatchesSearchItem)
            .ToList();

        _netflixDisplayIndex = 0;
        _displayedMovies.Clear();
        UpdateLoadMoreVisibility();
        _ = LoadNextNetflixBatchAsync(resetSelection: true);
    }

    private async Task ResetNetflixBatchViewAsync()
    {
        if (!_showingNetflix || _netflixCatalog == null)
            return;

        PrepareNetflixItemList(_netflixCatalog);
        _netflixDisplayIndex = 0;
        _displayedMovies.Clear();
        MoviesScrollViewer.ScrollToTop();
        UpdateLoadMoreVisibility();
        await LoadNextNetflixBatchAsync(resetSelection: true);
        UpdateMoviesEmptyState();
    }

    private bool MatchesSearchItem(WhatsOnNetflixItem item)
    {
        if (string.IsNullOrWhiteSpace(_searchText))
            return true;

        var haystack = $"{item.Title} {item.Year} {item.Genre}".ToLowerInvariant();
        return haystack.Contains(_searchText.ToLowerInvariant(), StringComparison.Ordinal);
    }

    public void ShowServicesView()
    {
        _showingNetflix = false;
        ServicesView.Visibility = Visibility.Visible;
        MoviesView.Visibility = Visibility.Collapsed;
        LoadMoreButton.Visibility = Visibility.Collapsed;
        ClearSelection();
        ViewChanged?.Invoke(this, false);
    }

    public void ClearSelection()
    {
        if (SelectedEntry != null)
        {
            SelectedEntry.IsSelected = false;
            SelectedEntry = null;
        }
    }

    public void SetSelectedEntry(WhatsOnMovieEntry? entry)
    {
        if (SelectedEntry != null)
            SelectedEntry.IsSelected = false;

        SelectedEntry = entry;

        if (SelectedEntry != null)
            SelectedEntry.IsSelected = true;
    }

    private void SelectEntry(WhatsOnMovieEntry entry)
    {
        SetSelectedEntry(entry);
        MovieSelected?.Invoke(this, entry);
    }

    private void SelectFirstDisplayedEntry()
    {
        if (_displayedMovies.Count == 0)
            return;

        SelectEntry(_displayedMovies[0]);
    }

    private void NetflixServiceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_netflixCatalog == null || _netflixCatalog.Items.Count == 0)
        {
            MessageBox.Show(
                "Netflix listings are not loaded yet. Try again in a moment.",
                "What's On",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            _ = EnsureLoadedAsync();
            return;
        }

        _ = ShowNetflixMoviesAsync();
    }

    private async Task ShowNetflixMoviesAsync()
    {
        _showingNetflix = true;
        ServicesView.Visibility = Visibility.Collapsed;
        MoviesView.Visibility = Visibility.Visible;
        LoadingOverlay.Visibility = Visibility.Visible;
        LoadingStatusText.Text = "Preparing Netflix catalog...";

        try
        {
            var catalog = _netflixCatalog!;
            await Task.Run(() =>
            {
                _matchIndex = WhatsOnCatalogMatcher.BuildMatchIndex(_localCatalog);
                PrepareNetflixItemList(catalog);
            });

            _netflixDisplayIndex = 0;
            _displayedMovies.Clear();

            var inCatalogCount = WhatsOnCatalogMatcher.CountInCatalog(_allNetflixItems, _matchIndex);
            var fetchedLocal = catalog.FetchedAt.ToLocalTime();
            NetflixHeaderText.Text = "Netflix Movies";
            NetflixSubheaderText.Text =
                $"{_allNetflixItems.Count:N0} titles · {inCatalogCount:N0} in your catalog · updated {fetchedLocal:g}";

            MoviesScrollViewer.ScrollToTop();
            await LoadNextNetflixBatchAsync();
            UpdateMoviesEmptyState();
            ViewChanged?.Invoke(this, true);
            SelectFirstDisplayedEntry();
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void PrepareNetflixItemList(WhatsOnNetflixCatalog catalog)
    {
        _fullNetflixItems = catalog.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .OrderByDescending(item => WhatsOnCatalogMatcher.IsInCatalog(item, _matchIndex))
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _allNetflixItems = _fullNetflixItems;
    }

    private void MoviesScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!_showingNetflix || _isLoadingNetflixBatch)
            return;

        _scrollDebounceTimer?.Stop();
        _scrollDebounceTimer?.Start();
    }

    private async void ScrollDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _scrollDebounceTimer?.Stop();
        await TryLoadMoreFromScrollAsync();
    }

    private async Task TryLoadMoreFromScrollAsync()
    {
        if (!_showingNetflix || _isLoadingNetflixBatch || !HasMoreNetflixItems())
            return;

        var scrollable = MoviesScrollViewer.ExtentHeight - MoviesScrollViewer.ViewportHeight;
        if (scrollable <= 0)
        {
            UpdateLoadMoreVisibility();
            return;
        }

        if (MoviesScrollViewer.VerticalOffset >= scrollable - 280)
            await LoadNextNetflixBatchAsync();
    }

    private async void LoadMoreButton_Click(object sender, RoutedEventArgs e) =>
        await LoadNextNetflixBatchAsync();

    private async Task LoadNextNetflixBatchAsync(bool resetSelection = false)
    {
        if (_isLoadingNetflixBatch || !HasMoreNetflixItems())
        {
            UpdateLoadMoreVisibility();
            return;
        }

        _isLoadingNetflixBatch = true;
        LoadMoreButton.IsEnabled = false;

        try
        {
            var batchItems = _allNetflixItems
                .Skip(_netflixDisplayIndex)
                .Take(NetflixBatchSize)
                .ToList();

            if (batchItems.Count == 0)
                return;

            _netflixDisplayIndex += batchItems.Count;

            foreach (var item in batchItems)
            {
                var entry = WhatsOnCatalogMatcher.BuildEntry(item, _matchIndex);
                _displayedMovies.Add(entry);
                _ = entry.LoadPosterAsync();
            }

            UpdateMoviesEmptyState();
            UpdateLoadMoreVisibility();

            if (resetSelection && _displayedMovies.Count > 0)
                SelectFirstDisplayedEntry();
        }
        finally
        {
            _isLoadingNetflixBatch = false;
            LoadMoreButton.IsEnabled = true;
        }
    }

    private bool HasMoreNetflixItems() => _netflixDisplayIndex < _allNetflixItems.Count;

    private void UpdateLoadMoreVisibility()
    {
        LoadMoreButton.Visibility = _showingNetflix && HasMoreNetflixItems()
            ? Visibility.Visible
            : Visibility.Collapsed;

        var remaining = Math.Max(0, _allNetflixItems.Count - _netflixDisplayIndex);
        LoadMoreButton.Content = remaining > 0
            ? $"Load more ({remaining:N0} remaining)"
            : "Load more";
    }

    private void BackToServicesButton_Click(object sender, RoutedEventArgs e) => ShowServicesView();

    private void RefreshNetflixButton_Click(object sender, RoutedEventArgs e) => _ = RefreshNetflixAsync();

    private async Task RefreshNetflixAsync()
    {
        _netflixCatalog = null;
        await EnsureLoadedAsync();
        if (_netflixCatalog != null && _netflixCatalog.Items.Count > 0 && _showingNetflix)
            await ShowNetflixMoviesAsync();
    }

    private void MovieCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WhatsOnMovieEntry entry })
            return;

        SelectEntry(entry);
    }

    private void UpdateMoviesEmptyState()
    {
        EmptyStatePanel.Visibility = _displayedMovies.Count > 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}
