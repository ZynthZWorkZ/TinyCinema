using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace TinyCinema;

public partial class IptvHomePanel : UserControl
{
    private readonly IptvPlaylistService _playlistService = new();
    private readonly ObservableCollection<IptvChannel> _allChannels = new();
    private readonly ICollectionView _channelsView;
    private readonly List<IptvCategory> _allCategories;
    private IptvCategory? _currentCategory;
    private bool _showFavoritesOnly;
    private string _searchText = string.Empty;
    private CancellationTokenSource? _loadCts;

    public event EventHandler<IptvChannel>? ChannelPlayRequested;

    public IptvHomePanel()
    {
        InitializeComponent();
        _channelsView = CollectionViewSource.GetDefaultView(_allChannels);
        _channelsView.Filter = ChannelFilter;
        ChannelsListView.ItemsSource = _channelsView;

        _allCategories = IptvCategoryCatalog.GetAll()
            .Where(category => category.ListedChannelCount > 0)
            .OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        CategoriesItemsControl.ItemsSource = _allCategories;
        RefreshFeaturedFavoritesCount();
    }

    public void ShowCategories()
    {
        _loadCts?.Cancel();
        CategoriesView.Visibility = Visibility.Visible;
        ChannelsView.Visibility = Visibility.Collapsed;
        _currentCategory = null;
        RefreshFeaturedFavoritesCount();
    }

    public void ApplySearch(string searchText)
    {
        _searchText = searchText.Trim();
        if (_currentCategory == null)
            FilterCategories();
        else
        {
            _channelsView.Refresh();
            UpdateChannelsEmptyState();
            UpdateChannelHeaderStats();
        }
    }

    private void RefreshFeaturedFavoritesCount()
    {
        var count = IptvFavoritesStore.GetFavoriteChannels().Count;
        FeaturedFavoritesCountText.Text = count switch
        {
            0 => "Save channels with the heart button",
            1 => "1 saved channel",
            _ => $"{count:N0} saved channels"
        };
    }

    private void FilterCategories()
    {
        if (string.IsNullOrWhiteSpace(_searchText))
        {
            CategoriesItemsControl.ItemsSource = _allCategories;
            return;
        }

        CategoriesItemsControl.ItemsSource = _allCategories
            .Where(category => category.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private async void FeaturedFavoritesButton_Click(object sender, RoutedEventArgs e) =>
        await OpenFavoritesAsync();

    private async void CategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: IptvCategory category })
            return;

        await OpenCategoryAsync(category, forceRefresh: false);
    }

    private async Task OpenFavoritesAsync()
    {
        _currentCategory = new IptvCategory
        {
            Name = "Favorite Channels",
            Slug = "__favorites__",
            ListedChannelCount = 0,
            PlaylistUrl = string.Empty
        };

        CategoriesView.Visibility = Visibility.Collapsed;
        ChannelsView.Visibility = Visibility.Visible;
        ChannelsTitleText.Text = "Favorite Channels";
        ChannelsHintText.Text = "Your saved channels play instantly in FFPLAY.";
        FavoritesOnlyToggle.IsChecked = true;
        _showFavoritesOnly = true;

        _allChannels.Clear();
        foreach (var channel in IptvFavoritesStore.GetFavoriteChannels())
            _allChannels.Add(channel);

        _channelsView.Refresh();
        UpdateChannelsEmptyState();
        UpdateChannelHeaderStats();
        _ = LoadVisibleLogosAsync(_allChannels);
    }

    private async Task OpenCategoryAsync(IptvCategory category, bool forceRefresh)
    {
        _currentCategory = category;
        CategoriesView.Visibility = Visibility.Collapsed;
        ChannelsView.Visibility = Visibility.Visible;
        ChannelsTitleText.Text = category.Name;
        ChannelsHintText.Text = "Double-click a channel or press the play button to start FFPLAY.";
        FavoritesOnlyToggle.IsChecked = false;
        _showFavoritesOnly = false;
        ChannelsLoadingPanel.Visibility = Visibility.Visible;
        ChannelsEmptyPanel.Visibility = Visibility.Collapsed;
        ChannelsListView.IsEnabled = false;
        ChannelsLoadingText.Text = forceRefresh
            ? $"Refreshing {category.Name}..."
            : $"Loading {category.Name}...";
        UpdateChannelHeaderStats();

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        try
        {
            var channels = await _playlistService.LoadChannelsAsync(category, forceRefresh, token);
            if (token.IsCancellationRequested)
                return;

            _allChannels.Clear();
            IptvFavoritesStore.ApplyFavorites(channels);
            foreach (var channel in channels.OrderBy(ch => ch.Name, StringComparer.OrdinalIgnoreCase))
                _allChannels.Add(channel);

            _channelsView.Refresh();
            UpdateChannelsEmptyState();
            UpdateChannelHeaderStats();
            _ = LoadVisibleLogosAsync(_allChannels);
        }
        catch (OperationCanceledException)
        {
            // Ignore.
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to load IPTV channels for {category.Name}.\n\n{ex.Message}",
                "IPTV",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            ShowCategories();
        }
        finally
        {
            ChannelsLoadingPanel.Visibility = Visibility.Collapsed;
            ChannelsListView.IsEnabled = true;
        }
    }

    private async Task LoadVisibleLogosAsync(IEnumerable<IptvChannel> channels)
    {
        foreach (var channel in channels.Take(120))
            await channel.LoadLogoAsync();
    }

    private bool ChannelFilter(object item)
    {
        if (item is not IptvChannel channel)
            return false;

        if (_showFavoritesOnly && !channel.IsFavorite)
            return false;

        if (string.IsNullOrWhiteSpace(_searchText))
            return true;

        return channel.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
               (channel.GroupTitle?.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void UpdateChannelsEmptyState()
    {
        var hasItems = _channelsView.Cast<object>().Any();
        ChannelsEmptyPanel.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        ChannelsListView.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;

        ChannelsEmptyText.Text = _showFavoritesOnly
            ? "No favorite channels yet."
            : string.IsNullOrWhiteSpace(_searchText)
                ? "No channels found in this playlist."
                : "No channels match your search.";
    }

    private void UpdateChannelHeaderStats()
    {
        var visibleCount = _channelsView.Cast<object>().Count();
        var totalCount = _allChannels.Count;

        ChannelsSubtitleText.Text = totalCount switch
        {
            0 => "No channels loaded",
            1 when visibleCount == 1 => "1 channel available",
            _ when visibleCount == totalCount => $"{totalCount:N0} channels available",
            _ => $"{visibleCount:N0} shown · {totalCount:N0} total"
        };
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => ShowCategories();

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCategory == null)
            return;

        if (_currentCategory.IsFavorites)
        {
            await OpenFavoritesAsync();
            return;
        }

        await OpenCategoryAsync(_currentCategory, forceRefresh: true);
    }

    private void FavoritesOnlyToggle_Click(object sender, RoutedEventArgs e)
    {
        _showFavoritesOnly = FavoritesOnlyToggle.IsChecked == true;
        _channelsView.Refresh();
        UpdateChannelsEmptyState();
        UpdateChannelHeaderStats();
    }

    private void PlayChannelButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: IptvChannel channel })
            ChannelPlayRequested?.Invoke(this, channel);
    }

    private void ChannelsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ChannelsListView.SelectedItem is IptvChannel channel)
            ChannelPlayRequested?.Invoke(this, channel);
    }

    private void FavoriteChannelButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: IptvChannel channel })
            return;

        channel.IsFavorite = !channel.IsFavorite;
        IptvFavoritesStore.SetFavorite(channel, channel.IsFavorite);
        RefreshFeaturedFavoritesCount();
        _channelsView.Refresh();
        UpdateChannelsEmptyState();
        UpdateChannelHeaderStats();
    }
}
