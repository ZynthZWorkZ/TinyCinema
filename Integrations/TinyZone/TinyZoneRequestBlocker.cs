using System.IO;
using Microsoft.Web.WebView2.Core;

namespace TinyCinema;

public static class TinyZoneRequestBlocker
{
    private static readonly HashSet<CoreWebView2> AttachedCores = [];

    public static void Attach(CoreWebView2 core)
    {
        if (!AttachedCores.Add(core))
            return;

        PlayerRequestBlocklistSettings.RefreshCache();

        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;
    }

    public static void Detach(CoreWebView2 core)
    {
        if (!AttachedCores.Remove(core))
            return;

        core.WebResourceRequested -= OnWebResourceRequested;

        try
        {
            core.RemoveWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        }
        catch
        {
            // Ignore cleanup errors when the core is already disposed.
        }
    }

    private static void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (sender is not CoreWebView2 core)
            return;

        if (!PlayerRequestBlocklistSettings.ShouldBlock(e.Request.Uri))
            return;

        e.Response = core.Environment.CreateWebResourceResponse(
            Stream.Null,
            403,
            "Blocked",
            "Content-Type: text/plain");
    }
}
