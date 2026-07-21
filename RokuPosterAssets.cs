using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace TinyCinema;

internal static class RokuPosterAssets
{
    private static readonly (string FileName, int Width, int Height)[] Targets =
    [
        ("rsgde_mm_focus_hd.jpg", 336, 210),
        ("rde_mm_focus_sd.jpg", 248, 140),
        ("rde_splash_sd.jpg", 720, 480),
        ("rsgde_splash_hd.jpg", 1280, 720),
        ("rde_splash_fhd.jpg", 1920, 1080)
    ];

    public static async Task<bool> ApplyMoviePosterAsync(
        string appDirectory,
        string? posterImageUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(posterImageUrl))
            return false;

        if (!FfmpegDownloader.TryResolveFfmpegPath(out var ffmpegPath))
            return false;

        var imagesDirectory = Path.Combine(appDirectory, "images");
        if (!Directory.Exists(imagesDirectory))
            return false;

        var workDirectory = Path.Combine(appDirectory, ".poster-work");
        Directory.CreateDirectory(workDirectory);

        try
        {
            var sourcePath = await DownloadPosterAsync(posterImageUrl, workDirectory, cancellationToken);
            if (sourcePath == null)
                return false;

            foreach (var (fileName, width, height) in Targets)
            {
                var outputPath = Path.Combine(imagesDirectory, fileName);
                if (!await ResizeWithFfmpegAsync(ffmpegPath, sourcePath, outputPath, width, height, cancellationToken))
                    return false;
            }

            return true;
        }
        finally
        {
            try
            {
                if (Directory.Exists(workDirectory))
                    Directory.Delete(workDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors.
            }
        }
    }

    private static async Task<string?> DownloadPosterAsync(
        string posterImageUrl,
        string workDirectory,
        CancellationToken cancellationToken)
    {
        var cachedPath = ImageCache.TryGetCachedFilePath(posterImageUrl);
        if (!string.IsNullOrWhiteSpace(cachedPath) && File.Exists(cachedPath))
            return cachedPath;

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TinyCinema/1.0");
            var bytes = await client.GetByteArrayAsync(posterImageUrl, cancellationToken);
            if (bytes.Length == 0)
                return null;

            var extension = GuessExtension(posterImageUrl, bytes);
            var targetPath = Path.Combine(workDirectory, $"poster-source{extension}");
            await File.WriteAllBytesAsync(targetPath, bytes, cancellationToken);
            return targetPath;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> ResizeWithFfmpegAsync(
        string ffmpegPath,
        string sourcePath,
        string outputPath,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        var filter = $"scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height}";

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-y -hide_banner -loglevel error -i \"{sourcePath}\" -vf \"{filter}\" -q:v 2 \"{outputPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            }
        };

        if (!process.Start())
            return false;

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0 && File.Exists(outputPath);
    }

    private static string GuessExtension(string url, byte[] bytes)
    {
        if (url.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50))
        {
            return ".png";
        }

        if (url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
            (bytes.Length >= 12 && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P'))
        {
            return ".webp";
        }

        return ".jpg";
    }
}
