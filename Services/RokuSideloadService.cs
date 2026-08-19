using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace TinyCinema;

public static class RokuSideloadService
{
    public static string TemplateDirectory =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RokuSideload", "VideoPlay");

    public static async Task<RokuSideloadResult> SideloadAsync(
        string movieTitle,
        string streamUrl,
        string rokuIp,
        string? posterImageUrl = null,
        string? rokuUsername = null,
        string? rokuPassword = null,
        IProgress<RokuSideloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(movieTitle))
            return RokuSideloadResult.Fail("Movie title is required.");

        if (string.IsNullOrWhiteSpace(streamUrl))
            return RokuSideloadResult.Fail("Stream URL is required.");

        if (string.IsNullOrWhiteSpace(rokuIp))
        {
            return RokuSideloadResult.Fail(
                "Roku IP address is required.",
                "Open Settings and enter the IP shown on your Roku Developer Mode screen.");
        }

        if (!Directory.Exists(TemplateDirectory))
        {
            return RokuSideloadResult.Fail(
                "Roku channel template is missing.",
                TemplateDirectory);
        }

        Report(progress, RokuSideloadStage.Building, "Building Roku channel package...");

        BuildChannelPackageResult package;
        try
        {
            package = await BuildChannelPackageAsync(movieTitle, streamUrl, posterImageUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            Report(progress, RokuSideloadStage.Failed, "Could not build the Roku channel package.");
            return RokuSideloadResult.Fail(
                "Could not build the Roku channel package.",
                ex.Message);
        }

        var username = NormalizeCredential(rokuUsername, "rokudev");
        var password = NormalizeCredential(rokuPassword, "rokudev");
        var host = NormalizeHost(rokuIp);

        try
        {
            Report(progress, RokuSideloadStage.Uploading, $"Uploading channel to Roku at {host}...");
            var zipBytes = await File.ReadAllBytesAsync(package.ZipPath, cancellationToken);

            Report(progress, RokuSideloadStage.Installing, "Installing channel on Roku...");
            var installResult = await RokuInstallerClient.InstallAsync(
                host,
                username,
                password,
                zipBytes,
                cancellationToken);

            if (!installResult.Success)
            {
                Report(progress, RokuSideloadStage.Failed, installResult.Message);
                return await FallbackToManualAsync(
                    package,
                    host,
                    installResult.Message,
                    BuildInstallerFailureDetails(installResult),
                    RokuSideloadFailureStage.Install,
                    progress,
                    cancellationToken);
            }

            if (await RokuEcpClient.IsAvailableAsync(host, cancellationToken))
            {
                await Task.Delay(1500, cancellationToken);
                if (!await RokuEcpClient.HasDevChannelAsync(host, cancellationToken))
                {
                    Report(progress, RokuSideloadStage.Failed, "Install could not be verified on Roku.");
                    return await FallbackToManualAsync(
                        package,
                        host,
                        "Roku did not confirm the developer channel after upload.",
                        "The installer response looked successful, but the dev channel was not detected via ECP. " +
                        "Try uploading the zip manually from the Roku installer page.",
                        RokuSideloadFailureStage.Install,
                        progress,
                        cancellationToken);
                }
            }

            var launchMessage = string.Empty;
            if (installResult.IdenticalVersion)
            {
                Report(progress, RokuSideloadStage.Launching, "Relaunching channel on Roku...");
                var launchResult = await RokuEcpClient.TryLaunchDevChannelAsync(host, cancellationToken);
                if (!launchResult.Success)
                {
                    launchMessage = launchResult.UserMessage;
                }
            }
            else
            {
                Report(progress, RokuSideloadStage.Launching, "Install complete. Roku should auto-start the channel.");
            }

            if (!string.IsNullOrWhiteSpace(launchMessage))
            {
                Report(progress, RokuSideloadStage.Completed, "Channel installed on Roku.");
                return RokuSideloadResult.Ok(
                    "Channel installed on Roku, but automatic launch failed.",
                    BuildPartialLaunchDetails(package, host, launchMessage),
                    installedOnRoku: true);
            }

            var details = BuildSuccessDetails(package, host, installResult.IdenticalVersion);
            Report(progress, RokuSideloadStage.Completed, installResult.IdenticalVersion
                ? "Channel reinstalled and relaunched on Roku."
                : "Channel installed on Roku.");

            return RokuSideloadResult.Ok(
                installResult.IdenticalVersion
                    ? "Channel reinstalled and relaunched on Roku."
                    : "Channel installed on Roku. It should start automatically on your TV.",
                details,
                installedOnRoku: true,
                launchedOnRoku: installResult.IdenticalVersion);
        }
        catch (HttpRequestException ex)
        {
            Report(progress, RokuSideloadStage.Failed, "Could not reach your Roku.");
            return await FallbackToManualAsync(
                package,
                host,
                "Could not reach your Roku over the network.",
                BuildNetworkFailureDetails(host, ex),
                RokuSideloadFailureStage.Network,
                progress,
                cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            Report(progress, RokuSideloadStage.Failed, "Roku did not respond in time.");
            return await FallbackToManualAsync(
                package,
                host,
                "Roku did not respond in time.",
                ex.Message,
                RokuSideloadFailureStage.Network,
                progress,
                cancellationToken);
        }
        catch (Exception ex)
        {
            Report(progress, RokuSideloadStage.Failed, "Automatic Roku sideload failed.");
            return await FallbackToManualAsync(
                package,
                host,
                "Automatic Roku sideload failed.",
                ex.Message,
                RokuSideloadFailureStage.Unknown,
                progress,
                cancellationToken);
        }
    }

    public static async Task<BuildChannelPackageResult> BuildChannelPackageAsync(
        string movieTitle,
        string streamUrl,
        string? posterImageUrl = null,
        CancellationToken cancellationToken = default)
    {
        var sanitizedTitle = SanitizeDirectoryName(movieTitle);
        var workRoot = Path.Combine(Path.GetTempPath(), "TinyCinema", "RokuSideload");
        var appDir = Path.Combine(workRoot, sanitizedTitle);
        var zipPath = Path.Combine(workRoot, $"{sanitizedTitle}.zip");

        if (Directory.Exists(appDir))
            Directory.Delete(appDir, recursive: true);

        if (File.Exists(zipPath))
            File.Delete(zipPath);

        CopyDirectory(TemplateDirectory, appDir);

        var videoscenePath = Path.Combine(appDir, "components", "videoscene.xml");
        if (!File.Exists(videoscenePath))
            throw new FileNotFoundException("Roku template is missing components/videoscene.xml.", videoscenePath);

        var escapedTitle = EscapeXml(movieTitle);
        var escapedUrl = EscapeXml(streamUrl.Trim());
        var content = await File.ReadAllTextAsync(videoscenePath, cancellationToken);
        content = content.Replace("videocontent.title = \"\"", $"videocontent.title = \"{escapedTitle}\"");
        content = content.Replace("videocontent.url = \"\"", $"videocontent.url = \"{escapedUrl}\"");
        await File.WriteAllTextAsync(videoscenePath, content, cancellationToken);

        UpdateManifest(appDir, movieTitle);

        var usedMoviePoster = await RokuPosterAssets.ApplyMoviePosterAsync(appDir, posterImageUrl, cancellationToken);

        RokuChannelPackager.CreateZip(appDir, zipPath);
        return new BuildChannelPackageResult(zipPath, usedMoviePoster);
    }

    private static async Task<RokuSideloadResult> FallbackToManualAsync(
        BuildChannelPackageResult package,
        string host,
        string message,
        string? errorDetails,
        RokuSideloadFailureStage failureStage,
        IProgress<RokuSideloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        OpenZipLocation(package.ZipPath);
        OpenInstallerPage(host);

        var details = new List<string>
        {
            $"Zip: {package.ZipPath}",
            string.Empty,
            failureStage switch
            {
                RokuSideloadFailureStage.Install =>
                    "Automatic upload/install failed, so TinyCinema opened the zip and Roku installer page for manual upload.",
                RokuSideloadFailureStage.Network =>
                    "TinyCinema could not reach your Roku. Verify the IP address, Developer Mode, and that both devices are on the same network.",
                _ =>
                    "Automatic sideload failed, so TinyCinema opened the zip and Roku installer page for manual upload."
            }
        };

        if (!string.IsNullOrWhiteSpace(errorDetails))
            details.Add($"Details: {errorDetails.Trim()}");

        details.Add("Manual fallback: upload the zip at the Roku installer page that just opened, then launch the channel from your Roku home screen.");

        if (package.UsedMoviePoster)
            details.Add("Channel icons and splash screens use this movie's poster.");
        else
            details.Add("Default Roku artwork was kept (poster/ffmpeg unavailable).");

        Report(progress, RokuSideloadStage.Failed, "Opened manual sideload fallback.");

        return RokuSideloadResult.Ok(
            message,
            string.Join("\n", details),
            usedManualFallback: true);
    }

    private static string BuildInstallerFailureDetails(RokuInstallerResult installResult)
    {
        var details = new List<string> { installResult.Message };

        if (installResult.IsUnauthorized)
        {
            details.Add("Check the Roku developer username and password in Settings (default is rokudev / rokudev).");
        }

        if (installResult.IsCompileError)
        {
            details.Add("Open the Roku developer telnet console on port 8080 for compile errors.");
        }

        if (!string.IsNullOrWhiteSpace(installResult.ResponseBody))
        {
            var snippet = ExtractResponseSnippet(installResult.ResponseBody);
            if (!string.IsNullOrWhiteSpace(snippet))
                details.Add($"Roku response: {snippet}");
        }

        return string.Join("\n", details);
    }

    private static string BuildNetworkFailureDetails(string host, Exception ex)
    {
        var details = new List<string>
        {
            $"Target: {host}",
            DescribeNetworkException(ex),
            string.Empty,
            "Tips:",
            "- Confirm the IP matches the address shown on your Roku Developer Mode screen",
            "- Developer Mode must be enabled",
            "- PC and Roku must be on the same network",
            "- Some routers block device-to-device traffic (AP/client isolation)",
            "- Wait a few seconds and try again if the Roku was busy from a previous attempt"
        };

        return string.Join("\n", details);
    }

    private static string DescribeNetworkException(Exception ex)
    {
        if (ex is HttpRequestException { InnerException: IOException ioEx })
            return $"Connection error: {ioEx.Message}";

        var message = ex.Message;
        if (ex.InnerException != null)
            message += $" ({ex.InnerException.Message})";

        return message;
    }

    private static string BuildPartialLaunchDetails(
        BuildChannelPackageResult package,
        string host,
        string launchMessage)
    {
        var details = new List<string>
        {
            $"Roku: {host}",
            "Upload/install succeeded.",
            launchMessage,
            "Open the sideloaded channel manually from your Roku home screen if it did not start on its own.",
            $"Package: {package.ZipPath}"
        };

        if (package.UsedMoviePoster)
            details.Add("Channel icons and splash screens use this movie's poster.");

        return string.Join("\n", details);
    }

    private static string ExtractResponseSnippet(string body)
    {
        var text = Regex.Replace(body, "<[^>]+>", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length <= 220 ? text : text[..220] + "...";
    }

    private static string BuildSuccessDetails(
        BuildChannelPackageResult package,
        string host,
        bool identicalVersion)
    {
        var details = new List<string>
        {
            $"Roku: {host}",
            identicalVersion
                ? "Roku reported the package was identical to the previous version and relaunched it."
                : "The channel was uploaded and installed. Roku normally auto-starts a freshly sideloaded channel."
        };

        if (package.UsedMoviePoster)
            details.Add("Channel icons and splash screens use this movie's poster.");

        details.Add($"Package: {package.ZipPath}");
        return string.Join("\n", details);
    }

    private static void Report(IProgress<RokuSideloadProgress>? progress, RokuSideloadStage stage, string message) =>
        progress?.Report(new RokuSideloadProgress(stage, message));

    private static string NormalizeCredential(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static void UpdateManifest(string appDirectory, string movieTitle)
    {
        var manifestPath = Path.Combine(appDirectory, "manifest");
        if (!File.Exists(manifestPath))
            return;

        var manifest = File.ReadAllText(manifestPath);
        manifest = Regex.Replace(manifest, "^title=.*$", $"title={SanitizeManifestValue(movieTitle)}", RegexOptions.Multiline);
        manifest = manifest.Replace(
            "mm_icon_focus_sd=pkg:/images/rde_mm_focus_sd.png",
            "mm_icon_focus_sd=pkg:/images/rde_mm_focus_sd.jpg",
            StringComparison.Ordinal);
        File.WriteAllText(manifestPath, manifest);
    }

    private static string SanitizeManifestValue(string value)
    {
        var sanitized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "TinyCinema" : sanitized;
    }

    private static void OpenZipLocation(string zipPath)
    {
        var directory = Path.GetDirectoryName(zipPath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = File.Exists(zipPath) ? $"/select,\"{zipPath}\"" : $"\"{directory}\"",
            UseShellExecute = true
        });
    }

    private static void OpenInstallerPage(string host)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = $"http://{host}/",
            UseShellExecute = true
        });
    }

    private static string NormalizeHost(string rokuIp) =>
        rokuIp
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim()
            .TrimEnd('/');

    private static string SanitizeDirectoryName(string title)
    {
        var sanitized = Regex.Replace(title, @"[<>:""/\\|?*']", "_").Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "TinyCinema_Channel" : sanitized;
    }

    private static string EscapeXml(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);

        foreach (var dir in Directory.GetDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }
}

public enum RokuSideloadFailureStage
{
    Install,
    Network,
    Unknown
}

public readonly record struct BuildChannelPackageResult(string ZipPath, bool UsedMoviePoster);

public readonly record struct RokuSideloadResult(
    bool Success,
    string Message,
    string? Details = null,
    bool UsedManualFallback = false,
    bool InstalledOnRoku = false,
    bool LaunchedOnRoku = false)
{
    public string DisplayText =>
        string.IsNullOrWhiteSpace(Details) ? Message : $"{Message}\n\n{Details}";

    public static RokuSideloadResult Ok(
        string message,
        string? details = null,
        bool usedManualFallback = false,
        bool installedOnRoku = false,
        bool launchedOnRoku = false) =>
        new(true, message, details, usedManualFallback, installedOnRoku, launchedOnRoku);

    public static RokuSideloadResult Fail(string message, string? details = null) =>
        new(false, message, details);
}
