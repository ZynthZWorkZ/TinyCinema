using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace TinyCinema;

public partial class ExploreCarouselRow : UserControl
{
    private bool _isUpdatingPosterSize;
    private DispatcherTimer? _posterSizeTimer;

    public static readonly DependencyProperty RowViewModelProperty =
        DependencyProperty.Register(
            nameof(RowViewModel),
            typeof(ExploreRowViewModel),
            typeof(ExploreCarouselRow),
            new PropertyMetadata(null, OnRowViewModelChanged));

    public ExploreRowViewModel? RowViewModel
    {
        get => (ExploreRowViewModel?)GetValue(RowViewModelProperty);
        set => SetValue(RowViewModelProperty, value);
    }

    public event EventHandler<Movie>? MovieClicked;

    public ExploreCarouselRow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => SchedulePosterSizeUpdate();
        Loaded += (_, _) =>
        {
            SchedulePosterSizeUpdate();
            UpdateScrollButtons();
        };
        Unloaded += (_, _) => _posterSizeTimer?.Stop();
    }

    public void SetSelectedMovie(Movie? movie)
    {
        if (RowViewModel == null)
            return;

        foreach (var item in RowViewModel.Items)
            item.IsSelected = item.Movie == movie;
    }

    private static void OnRowViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ExploreCarouselRow row)
        {
            row.DataContext = e.NewValue;
            row.SchedulePosterSizeUpdate();
        }
    }

    private void SchedulePosterSizeUpdate()
    {
        _posterSizeTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };

        _posterSizeTimer.Stop();
        _posterSizeTimer.Tick -= PosterSizeTimer_Tick;
        _posterSizeTimer.Tick += PosterSizeTimer_Tick;
        _posterSizeTimer.Start();
    }

    private void PosterSizeTimer_Tick(object? sender, EventArgs e)
    {
        _posterSizeTimer?.Stop();
        UpdatePosterSize();
    }

    private void UpdatePosterSize()
    {
        if (_isUpdatingPosterSize || RowViewModel == null || ActualWidth <= 0)
            return;

        try
        {
            _isUpdatingPosterSize = true;
            var available = Math.Max(360, ActualWidth - 24);
            RowViewModel.PosterWidth = Math.Round(available / 6.4, 0);
        }
        finally
        {
            _isUpdatingPosterSize = false;
        }
    }

    private void UpdateScrollButtons()
    {
        var canScroll = CarouselScroller.ScrollableWidth > 4;
        ScrollLeftButton.Visibility = canScroll ? Visibility.Visible : Visibility.Collapsed;
        ScrollRightButton.Visibility = canScroll ? Visibility.Visible : Visibility.Collapsed;

        if (!canScroll)
            return;

        ScrollLeftButton.IsEnabled = CarouselScroller.HorizontalOffset > 4;
        ScrollRightButton.IsEnabled = CarouselScroller.HorizontalOffset < CarouselScroller.ScrollableWidth - 4;
    }

    private void ScrollByPages(int direction)
    {
        if (RowViewModel == null)
            return;

        var delta = (RowViewModel.PosterWidth + 12) * 3 * direction;
        CarouselScroller.ScrollToHorizontalOffset(
            Math.Clamp(CarouselScroller.HorizontalOffset + delta, 0, CarouselScroller.ScrollableWidth));
    }

    private void ScrollLeftButton_Click(object sender, RoutedEventArgs e) => ScrollByPages(-1);

    private void ScrollRightButton_Click(object sender, RoutedEventArgs e) => ScrollByPages(1);

    private void CarouselScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.HorizontalChange != 0 || e.ExtentWidthChange != 0)
            UpdateScrollButtons();
    }

    private void CarouselScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && CarouselScroller.ScrollableWidth > 0)
        {
            CarouselScroller.ScrollToHorizontalOffset(
                Math.Clamp(CarouselScroller.HorizontalOffset - e.Delta, 0, CarouselScroller.ScrollableWidth));
            e.Handled = true;
        }
    }

    private void PosterCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not ExplorePosterItem item)
            return;

        MovieClicked?.Invoke(this, item.Movie);
        e.Handled = true;
    }
}
