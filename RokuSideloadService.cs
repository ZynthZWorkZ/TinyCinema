using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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

        BuildChannelPackageResult package;
        try
        {
            package = await BuildChannelPackageAsync(movieTitle, streamUrl, posterImageUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            return RokuSideloadResult.Fail(
                "Could not build the Roku channel package.",
                ex.Message);
        }

        try
        {
            OpenZipLocation(package.ZipPath);

            var host = NormalizeHost(rokuIp);
            Process.Start(new ProcessStartInfo
            {
                FileName = $"http://{host}/",
                UseShellExecute = true
            });

            var details = $"Zip: {package.ZipPath}\n\nUpload the zip in your browser, then open the channel on your Roku.";
            if (package.UsedMoviePoster)
                details += "\n\nChannel icons and splash screens use this movie's poster.";
            else if (!string.IsNullOrWhiteSpace(posterImageUrl))
                details += "\n\nDefault Roku artwork was kept (poster/ffmpeg unavailable).";

            return RokuSideloadResult.Ok("Channel zip created.", details);
        }
        catch (Exception ex)
        {
            return RokuSideloadResult.Fail(
                "Could not open the zip folder or Roku page.",
                ex.Message);
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
        var content = File.ReadAllText(videoscenePath);
        content = content.Replace("videocontent.title = \"\"", $"videocontent.title = \"{escapedTitle}\"");
        content = content.Replace("videocontent.url = \"\"", $"videocontent.url = \"{escapedUrl}\"");
        File.WriteAllText(videoscenePath, content);

        UpdateManifest(appDir, movieTitle);

        var usedMoviePoster = await RokuPosterAssets.ApplyMoviePosterAsync(appDir, posterImageUrl, cancellationToken);

        ZipFile.CreateFromDirectory(appDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return new BuildChannelPackageResult(zipPath, usedMoviePoster);
    }

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

    private static string NormalizeHost(string rokuIp) =>
        rokuIp
            .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
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

public readonly record struct BuildChannelPackageResult(string ZipPath, bool UsedMoviePoster);

public readonly record struct RokuSideloadResult(bool Success, string Message, string? Details = null)
{
    public string DisplayText =>
        string.IsNullOrWhiteSpace(Details) ? Message : $"{Message}\n\n{Details}";

    public static RokuSideloadResult Ok(string message, string? details = null) => new(true, message, details);
    public static RokuSideloadResult Fail(string message, string? details = null) => new(false, message, details);
}
