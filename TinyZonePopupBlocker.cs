using Microsoft.Web.WebView2.Core;

namespace TinyCinema;

public static class TinyZonePopupBlocker
{
    public const string BlockPopupsScript = """
        (() => {
            if (window.__tinyCinemaPopupBlocker) return;
            window.__tinyCinemaPopupBlocker = true;

            window.open = () => null;

            document.addEventListener('click', (event) => {
                const anchor = event.target?.closest?.('a[target="_blank"], a[target="blank"]');
                if (!anchor) return;

                event.preventDefault();
                event.stopImmediatePropagation();
            }, true);
        })();
        """;

    public static void Attach(CoreWebView2 core)
    {
        core.NewWindowRequested += OnNewWindowRequested;
    }

    public static void Detach(CoreWebView2 core)
    {
        core.NewWindowRequested -= OnNewWindowRequested;
    }

    private static void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
    }
}
