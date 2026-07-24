using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TinyCinema;

public partial class ExploreHomePanel : UserControl
{
    private Movie? _selectedMovie;
    private IReadOnlyList<ExploreRowViewModel> _rows = [];

    public event EventHandler<Movie>? MovieSelected;

    public Movie? SelectedMovie => _selectedMovie;

    public ExploreHomePanel()
    {
        InitializeComponent();
    }

    public void SetLoading(bool isLoading)
    {
        LoadingOverlay.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        MainScrollViewer.IsEnabled = !isLoading;
        EmptyStatePanel.Visibility = Visibility.Collapsed;
    }

    public void SetRecommendations(ExploreRecommendations recommendations)
    {
        SetLoading(false);

        var hasHint = !string.IsNullOrWhiteSpace(recommendations.HintText);
        HintTextBlock.Text = recommendations.HintText ?? string.Empty;
        HintBanner.Visibility = hasHint ? Visibility.Visible : Visibility.Collapsed;

        _rows = recommendations.Rows
            .Where(row => row.Items.Count > 0)
            .Select(row => new ExploreRowViewModel(row.Title, row.Items))
            .ToList();

        RowsItemsControl.ItemsSource = _rows;

        var hasRows = _rows.Count > 0;
        MainScrollViewer.Visibility = hasRows ? Visibility.Visible : Visibility.Collapsed;
        EmptyStatePanel.Visibility = hasRows ? Visibility.Collapsed : Visibility.Visible;

        foreach (var row in _rows)
        {
            foreach (var item in row.Items)
                _ = item.Movie.LoadImageAsync();
        }

        if (hasRows)
        {
            _selectedMovie = _rows[0].Items[0].Movie;
            Dispatcher.BeginInvoke(() =>
            {
                UpdateSelectionVisuals();
                if (_selectedMovie != null)
                    MovieSelected?.Invoke(this, _selectedMovie);
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
        else
        {
            _selectedMovie = null;
            UpdateSelectionVisuals();
        }
    }

    public void SelectMovie(Movie movie, bool notify = false)
    {
        _selectedMovie = movie;
        UpdateSelectionVisuals();

        if (notify)
            MovieSelected?.Invoke(this, movie);
    }

    public void ClearSelection()
    {
        _selectedMovie = null;
        UpdateSelectionVisuals();
    }

    private void ExploreCarouselRow_MovieClicked(object sender, Movie movie)
    {
        _selectedMovie = movie;
        UpdateSelectionVisuals();
        MovieSelected?.Invoke(this, movie);
    }

    private void UpdateSelectionVisuals()
    {
        foreach (var rowHost in FindVisualChildren<ExploreCarouselRow>(this))
            rowHost.SetSelectedMovie(_selectedMovie);
    }

    private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                yield return match;

            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }
}
