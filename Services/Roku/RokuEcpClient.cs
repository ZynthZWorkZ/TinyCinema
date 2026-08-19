using System.Net;
using System.Net.Http;
using System.Xml.Linq;

namespace TinyCinema;

public static class RokuEcpClient
{
    private const int EcpPort = 8060;

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public static async Task PrepareForInstallAsync(string host, CancellationToken cancellationToken = default)
    {
        _ = await TryPostAsync(host, "/keypress/home", cancellationToken);
        await Task.Delay(500, cancellationToken);
        _ = await TryPostAsync(host, "/keypress/home", cancellationToken);
    }

    public static Task<RokuEcpResult> TryLaunchDevChannelAsync(string host, CancellationToken cancellationToken = default) =>
        TryPostAsync(host, "/launch/dev", cancellationToken);

    public static async Task<bool> IsAvailableAsync(string host, CancellationToken cancellationToken = default)
    {
        var result = await TryGetAsync(host, "/query/device-info", cancellationToken);
        return result.Success;
    }

    public static async Task<bool> HasDevChannelAsync(string host, CancellationToken cancellationToken = default)
    {
        var result = await TryGetAsync(host, "/query/apps", cancellationToken);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Body))
            return false;

        try
        {
            var document = XDocument.Parse(result.Body);
            return document.Descendants("app")
                .Any(app => string.Equals(app.Attribute("id")?.Value, "dev", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return result.Body.Contains("id=\"dev\"", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static async Task<RokuEcpResult> TryPostAsync(string host, string path, CancellationToken cancellationToken)
    {
        var url = $"http://{host}:{EcpPort}{path}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            using var response = await Http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            return response.IsSuccessStatusCode
                ? RokuEcpResult.Ok(response.StatusCode, body)
                : RokuEcpResult.Fail(path, response.StatusCode, body);
        }
        catch (HttpRequestException ex)
        {
            return RokuEcpResult.Fail(path, message: BuildConnectionMessage(host, ex.Message));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RokuEcpResult.Fail(path, message: $"Timed out waiting for Roku ECP at {host}:{EcpPort}.");
        }
    }

    private static async Task<RokuEcpResult> TryGetAsync(string host, string path, CancellationToken cancellationToken)
    {
        var url = $"http://{host}:{EcpPort}{path}";

        try
        {
            using var response = await Http.GetAsync(url, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            return response.IsSuccessStatusCode
                ? RokuEcpResult.Ok(response.StatusCode, body)
                : RokuEcpResult.Fail(path, response.StatusCode, body);
        }
        catch (HttpRequestException ex)
        {
            return RokuEcpResult.Fail(path, message: BuildConnectionMessage(host, ex.Message));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RokuEcpResult.Fail(path, message: $"Timed out waiting for Roku ECP at {host}:{EcpPort}.");
        }
    }

    private static string BuildConnectionMessage(string host, string details) =>
        $"Could not reach Roku ECP at {host}:{EcpPort}. " +
        "Make sure the Roku is on the same network and External Control is enabled in Settings → System → Advanced system settings → Control by mobile apps → Network access.";
}

public readonly record struct RokuEcpResult(
    bool Success,
    string Path,
    HttpStatusCode? StatusCode = null,
    string? Body = null,
    string? Message = null)
{
    public string UserMessage
    {
        get
        {
            if (Success)
                return "ECP request succeeded.";

            if (!string.IsNullOrWhiteSpace(Message))
                return Message;

            if (StatusCode == HttpStatusCode.NotFound && Path.Contains("/launch/", StringComparison.OrdinalIgnoreCase))
            {
                return "Roku ECP could not launch the developer channel (404 on /launch/dev). " +
                       "The channel may still have installed successfully — check your Roku home screen for the sideloaded app.";
            }

            if (StatusCode == HttpStatusCode.NotFound)
            {
                return $"Roku ECP endpoint not found (404) for {Path}. " +
                       "Your Roku may not expose this control API, or External Control may be disabled.";
            }

            return StatusCode.HasValue
                ? $"Roku ECP request failed ({(int)StatusCode.Value} {StatusCode.Value}) for {Path}."
                : $"Roku ECP request failed for {Path}.";
        }
    }

    public static RokuEcpResult Ok(HttpStatusCode statusCode, string? body = null) =>
        new(true, string.Empty, statusCode, body);

    public static RokuEcpResult Fail(string path, HttpStatusCode? statusCode = null, string? body = null, string? message = null) =>
        new(false, path, statusCode, body, message);
}
