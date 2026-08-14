using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media.Imaging;

namespace TinyCinema;

public sealed class WhatsOnNetflixCatalog
{
    [JsonPropertyName("fetched_at")]
    public DateTime FetchedAt { get; set; }

    public string Source { get; set; } = string.Empty;

    public int Total { get; set; }

    public int Count { get; set; }

    public List<WhatsOnNetflixItem> Items { get; set; } = [];
}

public sealed class WhatsOnNetflixItem
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Genre { get; set; } = string.Empty;

    public string Runtime { get; set; } = string.Empty;

    public string Year { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Image { get; set; } = string.Empty;

    [JsonPropertyName("netflixid")]
    public string NetflixId { get; set; } = string.Empty;

    [JsonPropertyName("netflixlink")]
    public string NetflixLink { get; set; } = string.Empty;
}

public sealed class WhatsOnStreamingService
{
    public required string Id { get; init; }

    public required string Name { get; init; }
}

public sealed class WhatsOnMovieEntry : INotifyPropertyChanged
{
    private static readonly SemaphoreSlim PosterLoadGate = new(6, 6);

    private BitmapImage? _posterImage;
    private bool _isLoadingPoster;
    private bool _posterLoadAttempted;
    private bool _isSelected;

    public WhatsOnNetflixItem Item { get; init; } = null!;

    public bool IsInCatalog { get; init; }

    public Movie? CatalogMovie { get; init; }

    public string PlayHint => IsInCatalog ? "Play from your catalog" : "Play via VidSrc";

    public BitmapImage? PosterImage
    {
        get => _posterImage;
        private set
        {
            _posterImage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PosterImage)));
        }
    }

    public bool IsLoadingPoster
    {
        get => _isLoadingPoster;
        private set
        {
            _isLoadingPoster = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoadingPoster)));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task LoadPosterAsync()
    {
        if (_posterLoadAttempted || PosterImage != null || string.IsNullOrWhiteSpace(Item.Image))
            return;

        _posterLoadAttempted = true;
        SetIsLoadingPoster(true);

        await PosterLoadGate.WaitAsync();
        try
        {
            var image = await ImageCache.GetCachedImageAsync(Item.Image);
            if (image != null)
            {
                await Application.Current.Dispatcher.InvokeAsync(() => PosterImage = image);
            }
        }
        catch
        {
            // Poster is optional — keep the title card usable.
        }
        finally
        {
            PosterLoadGate.Release();
            SetIsLoadingPoster(false);
        }
    }

    private void SetIsLoadingPoster(bool value)
    {
        if (_isLoadingPoster == value)
            return;

        if (Application.Current.Dispatcher.CheckAccess())
        {
            IsLoadingPoster = value;
            return;
        }

        Application.Current.Dispatcher.Invoke(() => IsLoadingPoster = value);
    }
}
