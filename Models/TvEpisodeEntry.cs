using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace TinyCinema;

public class TvEpisodeEntry : INotifyPropertyChanged
{
    private bool _isCurrent;
    private ImageSource? _cachedThumbnail;

    public int Season { get; init; }
    public int Episode { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ThumbnailUrl { get; init; } = string.Empty;
    public required string MovieLairUrl { get; init; }

    public string DisplayLabel => string.IsNullOrWhiteSpace(Title)
        ? $"S{Season} E{Episode}"
        : $"S{Season} E{Episode} · {Title}";

    public string EpisodeNumberLabel => $"Episode {Episode}";

    public bool HasThumbnail => CachedThumbnail != null;

    public ImageSource? CachedThumbnail
    {
        get => _cachedThumbnail;
        private set
        {
            if (_cachedThumbnail == value)
                return;

            _cachedThumbnail = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasThumbnail));
        }
    }

    public async Task LoadThumbnailAsync()
    {
        if (CachedThumbnail != null || string.IsNullOrWhiteSpace(ThumbnailUrl))
            return;

        var image = await ImageCache.GetCachedImageAsync(ThumbnailUrl);
        if (image != null)
            CachedThumbnail = image;
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent == value)
                return;

            _isCurrent = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
