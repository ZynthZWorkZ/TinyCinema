using System.ComponentModel;
using System.Windows.Media;

namespace TinyCinema;

public sealed class IptvChannel : INotifyPropertyChanged
{
    private bool _isFavorite;
    private ImageSource? _logoImage;
    private bool _isLoadingLogo = true;

    public required string Name { get; init; }

    public required string StreamUrl { get; init; }

    public string? LogoUrl { get; init; }

    public string? GroupTitle { get; init; }

    public string? TvgId { get; init; }

    public required string CategoryName { get; init; }

    public required string CategorySlug { get; init; }

    public bool IsGeoBlocked { get; init; }

    public bool IsNot247 { get; init; }

    public string UniqueId => !string.IsNullOrWhiteSpace(TvgId)
        ? $"{CategorySlug}:{TvgId}"
        : $"{CategorySlug}:{StreamUrl}";

    public string BadgesText
    {
        get
        {
            var badges = new List<string>();
            if (IsGeoBlocked)
                badges.Add("Geo-blocked");
            if (IsNot247)
                badges.Add("Not 24/7");
            return badges.Count == 0 ? string.Empty : string.Join(" · ", badges);
        }
    }

    public bool HasBadges => IsGeoBlocked || IsNot247;

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value)
                return;

            _isFavorite = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFavorite)));
        }
    }

    public ImageSource? LogoImage
    {
        get => _logoImage;
        private set
        {
            _logoImage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LogoImage)));
        }
    }

    public bool IsLoadingLogo
    {
        get => _isLoadingLogo;
        private set
        {
            _isLoadingLogo = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoadingLogo)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task LoadLogoAsync()
    {
        if (string.IsNullOrWhiteSpace(LogoUrl))
        {
            IsLoadingLogo = false;
            return;
        }

        try
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(LogoUrl, UriKind.Absolute);
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            await bitmap.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            LogoImage = bitmap;
        }
        catch
        {
            LogoImage = null;
        }
        finally
        {
            IsLoadingLogo = false;
        }
    }
}
