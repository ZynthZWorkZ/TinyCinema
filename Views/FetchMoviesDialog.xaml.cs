using System.IO;
using System.Windows;
using System.Windows.Input;

namespace TinyCinema;

public partial class FetchMoviesDialog : Window
{
    private readonly string _defaultOutputPath;
    private string _outputPath;
    private CancellationTokenSource? _cancellationTokenSource;
    public bool CatalogUpdated { get; private set; }
    public string OutputPath => _outputPath;
    public string SelectedBaseUrl { get; private set; } = "https://ww5.tinyzone.org";

    public FetchMoviesDialog(string baseUrl, string outputPath)
    {
        InitializeComponent();
        _defaultOutputPath = outputPath;
        _outputPath = outputPath;
        OutputPathText.Text = outputPath;

        DomainComboBox.ItemsSource = new[]
        {
            "https://ww5.tinyzone.org",
            "https://ww4.tinyzone.org",
            "https://ww3.tinyzone.org"
        };

        var selected = DomainComboBox.Items
            .Cast<string>()
            .FirstOrDefault(item => item.Equals(baseUrl, StringComparison.OrdinalIgnoreCase))
            ?? "https://ww5.tinyzone.org";

        DomainComboBox.SelectedItem = selected;

        SaveModeComboBox.ItemsSource = new[]
        {
            "Merge with existing (new movies on top)",
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
            Title = "Save Movie Catalog",
            Filter = "JSON Files|*.json|All Files|*.*",
            FileName = Path.GetFileName(_outputPath),
            DefaultExt = ".json",
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
        if (!int.TryParse(PageCountTextBox.Text.Trim(), out var pageCount) || pageCount < 1 || pageCount > 747)
        {
            MessageBox.Show("Enter a page count between 1 and 747.", "Invalid Input",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (DomainComboBox.SelectedItem is not string baseUrl)
        {
            MessageBox.Show("Select a TinyZone domain.", "Invalid Input",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedBaseUrl = baseUrl;

        var saveMode = SaveModeComboBox.SelectedIndex == 1
            ? MovieCatalogSaveMode.Overwrite
            : MovieCatalogSaveMode.MergeWithExisting;

        if (saveMode == MovieCatalogSaveMode.Overwrite &&
            File.Exists(_outputPath) &&
            new FileInfo(_outputPath).Length > 0)
        {
            var confirm = MessageBox.Show(
                $"This will replace all movies in:\n{_outputPath}\n\nContinue?",
                "Overwrite File",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;
        }

        if (string.IsNullOrWhiteSpace(_outputPath))
        {
            MessageBox.Show("Choose where to save the movie catalog JSON file.", "Invalid Input",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetFetchingState(true);
        FetchProgressBar.IsIndeterminate = true;
        StatusText.Text = "Starting fetch...";

        _cancellationTokenSource = new CancellationTokenSource();
        var progress = new Progress<MovieCatalogFetchProgress>(report =>
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
            var fetcher = new MovieCatalogFetcher();
            var result = await fetcher.FetchAsync(
                baseUrl,
                pageCount,
                _outputPath,
                saveMode,
                progress,
                _cancellationTokenSource.Token);

            CatalogUpdated = result.MoviesAdded > 0 || result.MoviesDiscovered > 0;

            var saveModeText = result.SaveMode == MovieCatalogSaveMode.MergeWithExisting
                ? "Merged (new movies added to top)"
                : "Overwritten";

            MessageBox.Show(
                $"Fetch complete.\n\n" +
                $"Save mode: {saveModeText}\n" +
                $"Domain used: {result.BaseUrlUsed}\n" +
                $"Pages fetched: {result.PagesFetched}\n" +
                $"Movies found: {result.MoviesDiscovered}\n" +
                $"New movies added: {result.MoviesAdded}\n" +
                $"Skipped duplicates: {result.MoviesSkipped}\n" +
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
            MessageBox.Show("Movie fetch was cancelled.", "Cancelled",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Fetch failed.";
            MessageBox.Show($"Failed to fetch movies:\n{ex.Message}", "Error",
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
        DomainComboBox.IsEnabled = !isFetching;
        SaveModeComboBox.IsEnabled = !isFetching;
        CancelButton.Content = isFetching ? "Cancel" : "Close";

        if (!isFetching)
            FetchProgressBar.Value = 0;
    }
}
