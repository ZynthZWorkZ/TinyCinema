using System.Windows;
using System.Windows.Input;

namespace TinyCinema;

public partial class AddCatalogItemDialog : Window
{
    private readonly CatalogContentType _contentType;
    private readonly string _outputPath;
    private readonly string? _tmdbApiKey;
    private CancellationTokenSource? _cancellationTokenSource;

    public bool CatalogUpdated { get; private set; }

    public AddCatalogItemDialog(CatalogContentType contentType, string outputPath, string? tmdbApiKey = null)
    {
        InitializeComponent();
        _contentType = contentType;
        _outputPath = outputPath;
        _tmdbApiKey = tmdbApiKey;

        if (contentType == CatalogContentType.TvShow)
        {
            Title = "Add TV Show";
            TitleText.Text = "Add TV Show by URL";
            TitleIcon.Icon = FontAwesome.WPF.FontAwesomeIcon.Television;
            HintText.Text = "Paste a MovieLair watch URL, for example:\nhttps://movielair.cc/watch-tv/94997/";
        }
        else
        {
            Title = "Add Movie";
            TitleText.Text = "Add Movie by URL";
            TitleIcon.Icon = FontAwesome.WPF.FontAwesomeIcon.Film;
            HintText.Text = "Paste a TinyZone movie page URL, for example:\nhttps://ww5.tinyzone.org/movie/example-movie/";
        }
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show("Enter a URL.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AddButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        UrlTextBox.IsEnabled = false;
        StatusText.Text = "Fetching details...";

        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            ManualCatalogAddResult result;
            if (_contentType == CatalogContentType.TvShow)
            {
                result = await ManualCatalogAdder.AddTvShowAsync(url, _outputPath, _tmdbApiKey, _cancellationTokenSource.Token);
            }
            else
            {
                result = await ManualCatalogAdder.AddMovieAsync(url, _outputPath, _cancellationTokenSource.Token);
            }

            if (result.AlreadyExists)
            {
                MessageBox.Show(
                    "That item is already in your catalog file.",
                    "Already Added",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            CatalogUpdated = result.Added;
            MessageBox.Show(
                $"Added \"{result.Title}\" to your catalog.",
                "Added",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Could Not Add Item",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            StatusText.Text = "Failed.";
        }
        finally
        {
            AddButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
            UrlTextBox.IsEnabled = true;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cancellationTokenSource?.Cancel();
        DialogResult = false;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _cancellationTokenSource?.Cancel();
        DialogResult = false;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }
}
