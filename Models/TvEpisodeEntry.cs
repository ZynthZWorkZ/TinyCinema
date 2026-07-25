using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TinyCinema;

public class TvEpisodeEntry : INotifyPropertyChanged
{
    private bool _isCurrent;

    public int Season { get; init; }
    public int Episode { get; init; }
    public string Title { get; init; } = string.Empty;
    public required string MovieLairUrl { get; init; }

    public string DisplayLabel => string.IsNullOrWhiteSpace(Title)
        ? $"S{Season} E{Episode}"
        : $"S{Season} E{Episode} · {Title}";

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
