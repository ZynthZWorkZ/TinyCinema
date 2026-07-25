using System.IO;
using System.Windows;
using System.Windows.Input;

namespace TinyCinema;

public partial class FetchTvShowsDialog : Window
{
    private readonly string _defaultOutputPath;
    private string _outputPath;
    private CancellationTokenSource? _cancellationTokenSource;
    public bool CatalogUpdated { get; private set; }
    public string OutputPath => _outputPath;
    public string SelectedCategoryUrl { get; private set; } = "https://movielair.cc/shows/10759/";

    public FetchTvShowsDialog(string categoryUrl, string outputPath)
    {
        InitializeComponent();
        _defaultOutputPath = outputPath;
        _outputPath = outputPath;
        OutputPathText.Text = outputPath;
        CategoryUrlTextBox.Text = categoryUrl;

        SaveModeComboBox.ItemsSource = new[]
        {
            "Merge with existing (new shows on top)",
            "Overwrite file (replace all)"
        };
        SaveModeComboBox.SelectedIndex = 0;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellationTokenSource != null)
            _cancellationTokenSource.Cancel();
        else
            Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellationTokenSource != null)
            _cancellationTokenSource.Cancel();
        else
            Close();
    }

    private void BrowseOutputPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save TV Show Links File",
            Filter = "Text Files|*.txt|All Files|*.*",
            FileName = Path.GetFileName(_outputPath),
            InitialDirectory = Path.GetDirectoryName(_outputPath) ?? AppDomain.CurrentDomain.BaseDirectory,
            OverwritePrompt = false
        };

        if (dialog.ShowDialog() == true)
        {
            _outputPath = dialog.FileName;
            OutputPathText.Text = _outputPath;
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PageCountTextBox.Text.Trim(), out var pageCount) || pageCount < 1 || pageCount > 500)
        {
            MessageBox.Show("Enter a page count between 1 and 500.", "Invalid Input",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var categoryUrl = CategoryUrlTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(categoryUrl) ||
            !Uri.TryCreate(categoryUrl, UriKind.Absolute, out _))
        {
            MessageBox.Show("Enter a valid MovieLair category URL.", "Invalid Input",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedCategoryUrl = categoryUrl;

        var saveMode = SaveModeComboBox.SelectedIndex == 1
            ? MovieCatalogSaveMode.Overwrite
            : MovieCatalogSaveMode.MergeWithExisting;

        if (saveMode == MovieCatalogSaveMode.Overwrite &&
            File.Exists(_outputPath) &&
            new FileInfo(_outputPath).Length > 0)
        {
            var confirm = MessageBox.Show(
                $"This will replace all TV shows in:\n{_outputPath}\n\nContinue?",
                "Overwrite File",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;
        }

        if (string.IsNullOrWhiteSpace(_outputPath))
        {
            MessageBox.Show("Choose where to save tv_show_links.txt.", "Invalid Input",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var tmdbApiKey = SettingsWindow.GetTmdbApiKey();
        if (string.IsNullOrWhiteSpace(tmdbApiKey))
        {
            MessageBox.Show(
                "Add your TMDB API key in Settings before fetching TV shows.\n\nGet a free key at themoviedb.org/settings/api.",
                "TMDB API Key Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SetFetchingState(true);
        FetchProgressBar.IsIndeterminate = true;
        StatusText.Text = "Starting fetch...";

        _cancellationTokenSource = new CancellationTokenSource();
        var progress = new Progress<TvShowCatalogFetchProgress>(report =>
        {
            if (report.TotalPages > 0 && report.CurrentPage <= report.TotalPages)
            {
                var pageProgress = (double)report.CurrentPage / report.TotalPages * 100;
                FetchProgressBar.IsIndeterminate = false;
                FetchProgressBar.Value = pageProgress;
            }

            StatusText.Text = report.Status;
        });

        try
        {
            var fetcher = new TvShowCatalogFetcher();
            var result = await fetcher.FetchAsync(
                categoryUrl,
                pageCount,
                _outputPath,
                tmdbApiKey,
                saveMode,
                progress,
                _cancellationTokenSource.Token);

            CatalogUpdated = result.ShowsAdded > 0 || result.ShowsDiscovered > 0;

            var saveModeText = result.SaveMode == MovieCatalogSaveMode.MergeWithExisting
                ? "Merged (new shows added to top)"
                : "Overwritten";

            MessageBox.Show(
                $"Fetch complete.\n\n" +
                $"Save mode: {saveModeText}\n" +
                $"Category used: {result.CategoryUrlUsed}\n" +
                $"Pages fetched: {result.PagesFetched}\n" +
                $"TV shows found: {result.ShowsDiscovered}\n" +
                $"New shows added: {result.ShowsAdded}\n" +
                $"Skipped duplicates: {result.ShowsSkipped}\n" +
                $"Detail failures: {result.DetailFailures}\n\n" +
                $"Saved to:\n{result.OutputPath}",
                "Fetch Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Fetch cancelled.";
            MessageBox.Show("TV show fetch was cancelled.", "Cancelled",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Fetch failed.";
            MessageBox.Show($"Failed to fetch TV shows:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            SetFetchingState(false);
            FetchProgressBar.IsIndeterminate = false;
        }
    }

    private void SetFetchingState(bool isFetching)
    {
        StartButton.IsEnabled = !isFetching;
        PageCountTextBox.IsEnabled = !isFetching;
        CategoryUrlTextBox.IsEnabled = !isFetching;
        SaveModeComboBox.IsEnabled = !isFetching;
        CancelButton.Content = isFetching ? "Cancel" : "Close";

        if (!isFetching)
            FetchProgressBar.Value = 0;
    }
}
