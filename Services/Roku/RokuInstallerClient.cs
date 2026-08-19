using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace TinyCinema;

public static class RokuInstallerClient
{
    private const int MaxAttempts = 3;

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(90)
    };

    private static readonly string[] SubmitActions = ["Install", "Replace"];

    private static readonly Regex CompileErrorRegex = new(
        @"install\s+failure:\s+compilation\s+failed",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InstallFailureRegex = new(
        @"install\s+failure|installation\s+failed|install\s+failed|form\s+error",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InstallSuccessRegex = new(
        @"install\s+success",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MySubmitMissingRegex = new(
        @"mysubmit\s+field\s+not\s+found",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<RokuInstallerResult> InstallAsync(
        string rokuIp,
        string username,
        string password,
        byte[] zipBytes,
        CancellationToken cancellationToken = default)
    {
        var host = NormalizeHost(rokuIp);
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var probe = await ProbeInstallerAsync(host, cancellationToken);
                if (!probe.Reachable)
                {
                    return RokuInstallerResult.Fail(
                        probe.Message ?? "Could not reach the Roku developer installer.",
                        responseBody: probe.ResponseBody);
                }

                var result = await InstallOnceAsync(host, username, password, zipBytes, cancellationToken);
                if (result.Success || result.IsUnauthorized || result.IsCompileError)
                    return result;

                if (attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
                    continue;
                }

                return result;
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < MaxAttempts)
            {
                lastError = new TimeoutException("The Roku stopped responding during upload.");
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }

        return RokuInstallerResult.Fail(
            "Could not upload to your Roku after several attempts.",
            responseBody: lastError?.Message);
    }

    private static async Task<RokuInstallerResult> InstallOnceAsync(
        string host,
        string username,
        string password,
        byte[] zipBytes,
        CancellationToken cancellationToken)
    {
        RokuInstallerResult? lastResult = null;

        foreach (var submitAction in SubmitActions)
        {
            var result = await PostInstallAsync(host, username, password, zipBytes, submitAction, cancellationToken);
            if (result.Success || result.IsUnauthorized || result.IsCompileError)
                return result;

            lastResult = result;

            if (!IsMySubmitFieldError(result.ResponseBody))
                return result;
        }

        return lastResult ?? RokuInstallerResult.Fail("Roku install request failed.");
    }

    private static async Task<RokuInstallerResult> PostInstallAsync(
        string host,
        string username,
        string password,
        byte[] zipBytes,
        string submitAction,
        CancellationToken cancellationToken)
    {
        const string path = "/plugin_install";
        var url = $"http://{host}{path}";

        await RokuEcpClient.PrepareForInstallAsync(host, cancellationToken);

        using var initialContent = RokuMultipartBuilder.BuildInstallBody(zipBytes, submitAction);
        using var initialRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = initialContent
        };

        using var initialResponse = await Http.SendAsync(initialRequest, cancellationToken);
        var body = await initialResponse.Content.ReadAsStringAsync(cancellationToken);

        if (initialResponse.StatusCode != HttpStatusCode.Unauthorized)
        {
            return RokuInstallerResult.Fail(
                DescribeUnexpectedInitialResponse(initialResponse.StatusCode, body),
                httpStatusCode: initialResponse.StatusCode,
                responseBody: body);
        }

        var authHeader = initialResponse.Headers.WwwAuthenticate
            .FirstOrDefault(parameter => parameter.Scheme.Equals("Digest", StringComparison.OrdinalIgnoreCase));

        if (authHeader == null)
        {
            return RokuInstallerResult.Fail(
                "Roku requested authentication but did not provide a Digest challenge.",
                isUnauthorized: true,
                httpStatusCode: initialResponse.StatusCode,
                responseBody: body);
        }

        var challengeText = authHeader.Parameter ?? string.Empty;
        var challenge = RokuDigestAuth.ParseDigestChallenge(challengeText);
        var digestParams = RokuDigestAuth.GenerateDigestResponse(username, password, "POST", path, challenge);
        var authorization = RokuDigestAuth.FormatDigestHeader(digestParams);

        using var retryContent = RokuMultipartBuilder.BuildInstallBody(zipBytes, submitAction);
        using var retryRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = retryContent
        };
        retryRequest.Headers.TryAddWithoutValidation("Authorization", authorization);

        using var retryResponse = await Http.SendAsync(retryRequest, cancellationToken);
        body = await retryResponse.Content.ReadAsStringAsync(cancellationToken);

        return ParseResponse(retryResponse.StatusCode, body, authenticated: true);
    }

    private static async Task<RokuInstallerProbeResult> ProbeInstallerAsync(string host, CancellationToken cancellationToken)
    {
        var url = $"http://{host}/plugin_install";

        try
        {
            using var response = await Http.GetAsync(url, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.OK)
                return new RokuInstallerProbeResult(true, null, body);

            return new RokuInstallerProbeResult(
                false,
                DescribeInstallerFailure(response.StatusCode, body),
                body);
        }
        catch (HttpRequestException ex)
        {
            return new RokuInstallerProbeResult(
                false,
                $"Could not reach http://{host}/. Confirm Developer Mode is enabled and the IP in Settings is correct.",
                ex.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new RokuInstallerProbeResult(
                false,
                $"Timed out connecting to http://{host}/. The Roku may be offline or blocking device-to-device traffic.",
                null);
        }
    }

    private static bool IsMySubmitFieldError(string? body) =>
        !string.IsNullOrWhiteSpace(body) && MySubmitMissingRegex.IsMatch(body);

    private static RokuInstallerResult ParseResponse(HttpStatusCode statusCode, string body, bool authenticated)
    {
        if (!authenticated)
        {
            return RokuInstallerResult.Fail(
                "Upload response was not authenticated. The channel was not installed.",
                httpStatusCode: statusCode,
                responseBody: body);
        }

        if (statusCode == HttpStatusCode.Unauthorized)
        {
            return RokuInstallerResult.Fail(
                "Roku rejected the developer username or password.",
                isUnauthorized: true,
                httpStatusCode: statusCode,
                responseBody: body);
        }

        if (CompileErrorRegex.IsMatch(body) || InstallFailureRegex.IsMatch(body) || IsMySubmitFieldError(body))
        {
            return RokuInstallerResult.Fail(
                ExtractFailureMessage(body),
                isCompileError: CompileErrorRegex.IsMatch(body),
                httpStatusCode: statusCode,
                responseBody: body);
        }

        if ((int)statusCode is < 200 or >= 300)
        {
            return RokuInstallerResult.Fail(
                DescribeInstallerFailure(statusCode, body),
                httpStatusCode: statusCode,
                responseBody: body);
        }

        var identicalVersion = body.Contains("Identical to previous version", StringComparison.OrdinalIgnoreCase);
        if (InstallSuccessRegex.IsMatch(body) || identicalVersion)
            return RokuInstallerResult.Ok(identicalVersion, body);

        return RokuInstallerResult.Fail(
            "Roku did not confirm the install. The response did not contain Install Success.",
            httpStatusCode: statusCode,
            responseBody: body);
    }

    private static string DescribeUnexpectedInitialResponse(HttpStatusCode statusCode, string body)
    {
        if (statusCode == HttpStatusCode.OK &&
            body.Contains("Development Application Installer", StringComparison.OrdinalIgnoreCase))
        {
            return "Roku returned the installer page instead of accepting the upload. " +
                   "This usually means the upload was not authenticated or the request was blocked.";
        }

        return $"Unexpected Roku installer response before authentication (HTTP {(int)statusCode}). " +
               "Confirm Developer Mode is enabled and the IP address is correct.";
    }

    private static string ExtractFailureMessage(string body)
    {
        if (IsMySubmitFieldError(body))
        {
            return "Roku rejected the upload form (mysubmit field missing). " +
                   "TinyCinema will retry with alternate install modes automatically.";
        }

        if (CompileErrorRegex.IsMatch(body))
        {
            return "The channel failed to compile on your Roku. Check the developer telnet console on port 8080 for details.";
        }

        var snippet = ExtractResponseSnippet(body);
        return string.IsNullOrWhiteSpace(snippet)
            ? "Roku reported an install failure."
            : $"Roku reported an install failure: {snippet}";
    }

    private static string DescribeInstallerFailure(HttpStatusCode statusCode, string body)
    {
        if (statusCode == HttpStatusCode.NotFound)
        {
            return "Could not reach the Roku developer installer at http://{your-roku-ip}/. " +
                   "Confirm Developer Mode is enabled and the IP address in Settings is correct.";
        }

        if (statusCode == HttpStatusCode.ServiceUnavailable)
        {
            return "The Roku developer installer is unavailable. Reboot the Roku or re-enable Developer Mode.";
        }

        var snippet = ExtractResponseSnippet(body);
        return string.IsNullOrWhiteSpace(snippet)
            ? $"Roku installer returned HTTP {(int)statusCode}."
            : $"Roku installer returned HTTP {(int)statusCode}: {snippet}";
    }

    internal static string ExtractResponseSnippet(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        var jsonMessage = Regex.Match(body, @"""text""\s*:\s*""((?:\\.|[^""\\])*)""", RegexOptions.IgnoreCase);
        if (jsonMessage.Success)
        {
            var decoded = jsonMessage.Groups[1].Value
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\n", " ", StringComparison.Ordinal)
                .Trim();

            if (!string.IsNullOrWhiteSpace(decoded))
                return decoded.Length <= 220 ? decoded : decoded[..220] + "...";
        }

        var redFontMatch = Regex.Match(body, @"<font\s+color\s*=\s*""red""[^>]*>(.*?)</font>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (redFontMatch.Success)
        {
            var redText = Regex.Replace(redFontMatch.Groups[1].Value, "<[^>]+>", " ");
            redText = Regex.Replace(redText, @"\s+", " ").Trim();
            if (!string.IsNullOrWhiteSpace(redText))
                return redText.Length <= 220 ? redText : redText[..220] + "...";
        }

        var text = Regex.Replace(body, "<[^>]+>", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length <= 180 ? text : text[..180] + "...";
    }

    private static string NormalizeHost(string rokuIp) =>
        rokuIp
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim()
            .TrimEnd('/');
}

internal static class RokuMultipartBuilder
{
    public static ByteArrayContent BuildInstallBody(byte[] zipBytes, string submitAction)
    {
        var boundary = $"----TinyCinema{Guid.NewGuid():N}";
        using var stream = new MemoryStream();

        WriteText(stream, $"--{boundary}\r\n");
        WriteText(stream, "Content-Disposition: form-data; name=\"mysubmit\"\r\n");
        WriteText(stream, "\r\n");
        WriteText(stream, $"{submitAction}\r\n");

        WriteText(stream, $"--{boundary}\r\n");
        WriteText(stream, "Content-Disposition: form-data; name=\"archive\"; filename=\"dev.zip\"\r\n");
        WriteText(stream, "Content-Type: application/octet-stream\r\n");
        WriteText(stream, "\r\n");
        stream.Write(zipBytes, 0, zipBytes.Length);
        WriteText(stream, "\r\n");

        WriteText(stream, $"--{boundary}--\r\n");

        var content = new ByteArrayContent(stream.ToArray());
        content.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={boundary}");
        return content;
    }

    private static void WriteText(Stream stream, string value) =>
        stream.Write(Encoding.UTF8.GetBytes(value));
}

public readonly record struct RokuInstallerProbeResult(bool Reachable, string? Message, string? ResponseBody);

public readonly record struct RokuInstallerResult(
    bool Success,
    string Message,
    bool IdenticalVersion = false,
    bool IsUnauthorized = false,
    bool IsCompileError = false,
    HttpStatusCode? HttpStatusCode = null,
    string? ResponseBody = null)
{
    public static RokuInstallerResult Ok(bool identicalVersion, string? responseBody = null) =>
        new(true, "Channel installed on Roku.", identicalVersion, ResponseBody: responseBody);

    public static RokuInstallerResult Fail(
        string message,
        bool isUnauthorized = false,
        bool isCompileError = false,
        HttpStatusCode? httpStatusCode = null,
        string? responseBody = null) =>
        new(false, message, IsUnauthorized: isUnauthorized, IsCompileError: isCompileError,
            HttpStatusCode: httpStatusCode, ResponseBody: responseBody);
}
