using System.Windows;

namespace TinyCinema;

public partial class ImportNetflixGapsDialog : Window
{
    private readonly string _catalogPath;
    private CancellationTokenSource? _cancellationTokenSource;
    public bool CatalogUpdated { get; private set; }

    public ImportNetflixGapsDialog(string catalogPath)
    {
        InitializeComponent();
        _catalogPath = catalogPath;
        SummaryText.Text = $"Catalog: {catalogPath}";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellationTokenSource != null)
            _cancellationTokenSource.Cancel();
        else
            DialogResult = false;
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = SettingsWindow.GetTmdbApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show(
                "Add your TMDB API key in Settings before importing Netflix titles.",
                "TMDB API Key Required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        StartButton.IsEnabled = false;
        CancelButton.Content = "Stop";
        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;

        try
        {
            var netflixCatalog = await WhatsOnNetflixStore.TryLoadAsync(token);
            if (netflixCatalog == null || netflixCatalog.Items.Count == 0)
            {
                netflixCatalog = await WhatsOnNetflixFetcher.FetchAllMoviesAsync(
                    new Progress<WhatsOnFetchProgress>(report => StatusText.Text = report.Status),
                    token);
                await WhatsOnNetflixStore.SaveAsync(netflixCatalog, token);
            }

            var localMovies = await MovieCatalogStore.LoadMoviesAsync(_catalogPath, token);
            var progress = new Progress<WhatsOnImportProgress>(report =>
            {
                Dispatcher.Invoke(() =>
                {
                    ImportProgressBar.Value = report.Total > 0
                        ? report.Processed * 100.0 / report.Total
                        : 0;
                    StatusText.Text =
                        $"{report.Status}\nAdded: {report.Added} · Skipped: {report.Skipped} · Failed: {report.Failed} · {report.Processed}/{report.Total}";
                });
            });

            var result = await WhatsOnCatalogImporter.ImportGapsAsync(
                _catalogPath,
                localMovies,
                netflixCatalog.Items,
                apiKey,
                MoviePlayerSource.VidSrc,
                progress,
                token);

            CatalogUpdated = result.Added > 0;
            MessageBox.Show(
                $"Import finished.\n\nAdded: {result.Added}\nSkipped: {result.Skipped}\nFailed: {result.Failed}\nTotal gaps processed: {result.Total}",
                "Netflix Import",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            DialogResult = CatalogUpdated;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Import failed:\n{ex.Message}",
                "Netflix Import",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            StartButton.IsEnabled = true;
            CancelButton.Content = "Close";
        }
    }
}
