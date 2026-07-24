using System.ComponentModel;

namespace TinyCinema;

public sealed class ExplorePosterItem : INotifyPropertyChanged
{
    public ExplorePosterItem(Movie movie) => Movie = movie;

    public Movie Movie { get; }

    private bool _isSelected;

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
}

public sealed class ExploreRowViewModel : INotifyPropertyChanged
{
    public ExploreRowViewModel(string title, IReadOnlyList<Movie> movies)
    {
        Title = title;
        Items = new System.Collections.ObjectModel.ObservableCollection<ExplorePosterItem>(
            movies.Select(movie => new ExplorePosterItem(movie)));
    }

    public string Title { get; }
    public System.Collections.ObjectModel.ObservableCollection<ExplorePosterItem> Items { get; }

    private double _posterWidth = 140;

    public double PosterWidth
    {
        get => _posterWidth;
        set
        {
            var clamped = Math.Clamp(value, 118, 168);
            if (Math.Abs(_posterWidth - clamped) < 0.5)
                return;

            _posterWidth = clamped;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PosterWidth)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PosterHeight)));
        }
    }

    public double PosterHeight => PosterWidth * 1.5;

    public event PropertyChangedEventHandler? PropertyChanged;
}
