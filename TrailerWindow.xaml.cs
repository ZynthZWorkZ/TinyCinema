using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace TinyCinema;

public partial class TrailerWindow : Window
{
    private const string TrailerHost = "tinycinema.local";
    private const string TrailerOrigin = $"https://{TrailerHost}";

    private readonly string _videoKey;

    public TrailerWindow(string movieTitle, string videoKey)
    {
        InitializeComponent();
        _videoKey = videoKey;
        Title = $"{movieTitle} - Trailer";
        TitleText.Text = $"{movieTitle} - Trailer";
        Loaded += async (_, _) => await InitializeWebViewAsync();
        Closed += (_, _) =>
        {
            try
            {
                if (TrailerWebView.CoreWebView2 != null)
                {
                    TrailerWebView.CoreWebView2.WebResourceRequested -= OnWebResourceRequested;
                }

                TrailerWebView.Dispose();
            }
            catch
            {
                // Ignore cleanup errors.
            }
        };
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            TrailerWebView.DefaultBackgroundColor = System.Drawing.Color.Black;
            await TrailerWebView.EnsureCoreWebView2Async();

            var core = TrailerWebView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;

            core.AddWebResourceRequestedFilter(
                "*://*.youtube.com/*",
                CoreWebView2WebResourceContext.All,
                CoreWebView2WebResourceRequestSourceKinds.All);
            core.AddWebResourceRequestedFilter(
                "*://*.youtube-nocookie.com/*",
                CoreWebView2WebResourceContext.All,
                CoreWebView2WebResourceRequestSourceKinds.All);
            core.AddWebResourceRequestedFilter(
                "*://*.googlevideo.com/*",
                CoreWebView2WebResourceContext.All,
                CoreWebView2WebResourceRequestSourceKinds.All);
            core.AddWebResourceRequestedFilter(
                "*://*.ytimg.com/*",
                CoreWebView2WebResourceContext.All,
                CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += OnWebResourceRequested;

            var trailerDir = Path.Combine(Path.GetTempPath(), "TinyCinema", "trailer");
            Directory.CreateDirectory(trailerDir);
            File.WriteAllText(Path.Combine(trailerDir, "player.html"), BuildTrailerHtml(_videoKey));
            core.SetVirtualHostNameToFolderMapping(
                TrailerHost,
                trailerDir,
                CoreWebView2HostResourceAccessKind.Allow);

            core.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                    LoadingOverlay.Visibility = Visibility.Collapsed;
            };

            core.Navigate($"{TrailerOrigin}/player.html");
        }
        catch (Exception ex)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            MessageBox.Show(
                $"Could not load trailer.\n\n{ex.Message}",
                "Trailer Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = e.Request.Uri;
        if (!uri.Contains("youtube", StringComparison.OrdinalIgnoreCase) &&
            !uri.Contains("googlevideo", StringComparison.OrdinalIgnoreCase) &&
            !uri.Contains("ytimg", StringComparison.OrdinalIgnoreCase))
            return;

        e.Request.Headers.SetHeader("Referer", $"{TrailerOrigin}/");
        e.Request.Headers.SetHeader("Referrer-Policy", "strict-origin-when-cross-origin");
    }

    private static string BuildTrailerHtml(string videoKey)
    {
        var origin = Uri.EscapeDataString($"{TrailerOrigin}/");
        var embedUrl =
            $"https://www.youtube-nocookie.com/embed/{videoKey}?autoplay=1&rel=0&modestbranding=1&playsinline=1&origin={origin}";

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="referrer" content="strict-origin-when-cross-origin">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <style>
                    html, body {
                        margin: 0;
                        width: 100%;
                        height: 100%;
                        background: #000;
                        overflow: hidden;
                    }
                    iframe {
                        border: 0;
                        width: 100%;
                        height: 100%;
                    }
                </style>
            </head>
            <body>
                <iframe src="{{embedUrl}}"
                        title="Trailer"
                        referrerpolicy="strict-origin-when-cross-origin"
                        allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share"
                        allowfullscreen></iframe>
            </body>
            </html>
            """;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
