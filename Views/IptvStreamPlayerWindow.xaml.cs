using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace TinyCinema;

public partial class IptvStreamPlayerWindow : Window
{
    private const string PlayerHost = "iptv.tinycinema.local";
    private const string PlayerOrigin = $"https://{PlayerHost}";

    private readonly IptvChannel _channel;

    public IptvStreamPlayerWindow(IptvChannel channel)
    {
        _channel = channel;
        InitializeComponent();
        Title = channel.Name;
        TitleText.Text = channel.Name;
        StatusText.Text = channel.StreamUrl;
        VlcButton.Visibility = SettingsWindow.IsVlcInstalled() ? Visibility.Visible : Visibility.Collapsed;
        Loaded += async (_, _) => await InitializePlayerAsync();
        Closed += (_, _) =>
        {
            try
            {
                PlayerWebView.Dispose();
            }
            catch
            {
                // Ignore cleanup errors.
            }
        };
    }

    private async Task InitializePlayerAsync()
    {
        try
        {
            PlayerWebView.DefaultBackgroundColor = System.Drawing.Color.Black;
            var environment = await WebView2UserDataManager.CreatePlayerEnvironmentAsync();
            await PlayerWebView.EnsureCoreWebView2Async(environment);

            var core = PlayerWebView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;

            var playerDir = Path.Combine(Path.GetTempPath(), "TinyCinema", "iptv-player");
            Directory.CreateDirectory(playerDir);
            File.WriteAllText(Path.Combine(playerDir, "player.html"), BuildPlayerHtml(_channel.StreamUrl));
            core.SetVirtualHostNameToFolderMapping(
                PlayerHost,
                playerDir,
                CoreWebView2HostResourceAccessKind.Allow);

            core.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                else
                    StatusText.Text = $"Failed to load stream (HTTP {args.HttpStatusCode}). Try FFPLAY or VLC.";
            };

            core.Navigate($"{PlayerOrigin}/player.html");
        }
        catch (Exception ex)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            MessageBox.Show(
                $"Could not start the IPTV player.\n\n{ex.Message}",
                "IPTV Player",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }

    private static string BuildPlayerHtml(string streamUrl)
    {
        var encodedUrl = System.Net.WebUtility.HtmlEncode(streamUrl);
        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8"/>
                <meta name="viewport" content="width=device-width, initial-scale=1"/>
                <title>Live TV</title>
                <script src="https://cdn.jsdelivr.net/npm/hls.js@1.5.15/dist/hls.min.js"></script>
                <style>
                    html, body {
                        margin: 0;
                        width: 100%;
                        height: 100%;
                        background: #000;
                        overflow: hidden;
                    }
                    video {
                        width: 100%;
                        height: 100%;
                        background: #000;
                        object-fit: contain;
                    }
                    .error {
                        color: #f5f5f5;
                        font-family: "Segoe UI", sans-serif;
                        padding: 24px;
                    }
                </style>
            </head>
            <body>
                <video id="video" controls autoplay playsinline></video>
                <script>
                    const src = "{{encodedUrl}}";
                    const video = document.getElementById('video');
                    if (video.canPlayType('application/vnd.apple.mpegurl')) {
                        video.src = src;
                    } else if (window.Hls && Hls.isSupported()) {
                        const hls = new Hls();
                        hls.loadSource(src);
                        hls.attachMedia(video);
                        hls.on(Hls.Events.MANIFEST_PARSED, () => video.play().catch(() => {}));
                    } else {
                        document.body.innerHTML = '<div class="error">HLS playback is not supported in this browser.</div>';
                    }
                </script>
            </body>
            </html>
            """;
    }

    private void OpenFfplayButton_Click(object sender, RoutedEventArgs e) =>
        ExternalPlayerLauncher.Launch(PlayerNames.FFPLAY, _channel.StreamUrl);

    private void OpenVlcButton_Click(object sender, RoutedEventArgs e) =>
        ExternalPlayerLauncher.Launch(PlayerNames.VLC, _channel.StreamUrl);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
