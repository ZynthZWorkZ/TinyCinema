using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace TinyCinema;

public sealed class FfmpegDownloadProgress
{
    public double? Percent { get; init; }
    public required string Status { get; init; }
    public long? SizeKb { get; init; }
}

public sealed class FfmpegDownloader : IDisposable
{
    private static readonly Regex DurationRegex = new(@"Duration:\s*(\d{2}):(\d{2}):(\d{2}\.\d{2})", RegexOptions.Compiled);
    private static readonly Regex TimeRegex = new(@"time=(\d{2}):(\d{2}):(\d{2}\.\d{2})", RegexOptions.Compiled);
    private static readonly Regex SizeRegex = new(@"size=\s*(\d+)kB", RegexOptions.Compiled);

    private Process? _process;

    public static bool TryResolveFfmpegPath(out string ffmpegPath)
    {
        var candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe"),
            @"C:\ffmpeg\bin\ffmpeg.exe"
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                ffmpegPath = candidate;
                return true;
            }
        }

        ffmpegPath = "ffmpeg";
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process == null)
                return false;

            process.WaitForExit(3000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> DownloadAsync(
        string streamUrl,
        string outputPath,
        string? referer,
        IProgress<FfmpegDownloadProgress> progress,
        CancellationToken cancellationToken,
        TimeSpan? clipStart = null,
        TimeSpan? clipEnd = null)
    {
        if (!TryResolveFfmpegPath(out var ffmpegPath))
            throw new InvalidOperationException("ffmpeg was not found. Install ffmpeg and add it to your PATH.");

        if (clipStart.HasValue ^ clipEnd.HasValue)
            throw new ArgumentException("Clip downloads require both a start and end time.");

        if (clipStart.HasValue && clipEnd.HasValue && clipEnd.Value <= clipStart.Value)
            throw new ArgumentException("Clip end time must be after start time.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var args = new List<string>
        {
            "-y",
            "-hide_banner",
            "-loglevel", "info"
        };

        if (!string.IsNullOrWhiteSpace(referer))
        {
            args.Add("-headers");
            args.Add($"Referer: {referer}\r\nUser-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36\r\n");
        }

        if (clipStart.HasValue)
            args.AddRange(["-ss", FormatFfmpegTimestamp(clipStart.Value)]);

        args.Add("-i");
        args.Add(streamUrl);

        if (clipEnd.HasValue)
            args.AddRange(["-to", FormatFfmpegTimestamp(clipEnd.Value)]);

        args.AddRange(["-c", "copy", "-bsf:a", "aac_adtstoasc", "-movflags", "+faststart", outputPath]);

        var expectedDuration = clipStart.HasValue && clipEnd.HasValue
            ? clipEnd.Value - clipStart.Value
            : (TimeSpan?)null;

        return await RunFfmpegAsync(ffmpegPath, args, outputPath, progress, cancellationToken, expectedDuration);
    }

    private async Task<string> RunFfmpegAsync(
        string ffmpegPath,
        List<string> args,
        string outputPath,
        IProgress<FfmpegDownloadProgress> progress,
        CancellationToken cancellationToken,
        TimeSpan? expectedDuration)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start ffmpeg.");

        TimeSpan? totalDuration = expectedDuration;
        var errorOutput = new List<string>();

        try
        {
            await foreach (var line in ReadLinesAsync(_process.StandardError, cancellationToken))
            {
                errorOutput.Add(line);

                var durationMatch = DurationRegex.Match(line);
                if (durationMatch.Success && !expectedDuration.HasValue)
                {
                    if (TryParseTime(durationMatch, out var parsedDuration))
                        totalDuration = parsedDuration;

                    progress.Report(new FfmpegDownloadProgress
                    {
                        Percent = 0,
                        Status = "Download started...",
                        SizeKb = null
                    });
                    continue;
                }

                if (durationMatch.Success && expectedDuration.HasValue)
                {
                    progress.Report(new FfmpegDownloadProgress
                    {
                        Percent = 0,
                        Status = "Clip download started...",
                        SizeKb = null
                    });
                    continue;
                }

                var timeMatch = TimeRegex.Match(line);
                var sizeMatch = SizeRegex.Match(line);
                long? sizeKb = sizeMatch.Success && long.TryParse(sizeMatch.Groups[1].Value, out var kb) ? kb : null;

                if (!timeMatch.Success)
                {
                    if (sizeKb.HasValue)
                    {
                        progress.Report(new FfmpegDownloadProgress
                        {
                            Percent = totalDuration.HasValue ? null : null,
                            Status = BuildStatus(null, sizeKb, totalDuration),
                            SizeKb = sizeKb
                        });
                    }

                    continue;
                }

                if (!TryParseTime(timeMatch, out var currentTime))
                    continue;

                double? percent = null;
                if (totalDuration is { TotalSeconds: > 0 })
                {
                    percent = Math.Min(99, currentTime.TotalSeconds / totalDuration.Value.TotalSeconds * 100);
                }

                progress.Report(new FfmpegDownloadProgress
                {
                    Percent = percent,
                    Status = BuildStatus(currentTime, sizeKb, totalDuration),
                    SizeKb = sizeKb
                });
            }

            await _process.WaitForExitAsync(cancellationToken);

            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException();

            if (_process.ExitCode != 0 || !File.Exists(outputPath))
            {
                var details = string.Join(Environment.NewLine, errorOutput.TakeLast(8));
                throw new InvalidOperationException($"ffmpeg failed (exit {_process.ExitCode}).{Environment.NewLine}{details}");
            }

            progress.Report(new FfmpegDownloadProgress
            {
                Percent = 100,
                Status = expectedDuration.HasValue ? "Clip saved." : "Download complete.",
                SizeKb = new FileInfo(outputPath).Length / 1024
            });

            return outputPath;
        }
        finally
        {
            if (_process is { HasExited: false })
            {
                try { _process.Kill(true); } catch { /* ignore */ }
            }
        }
    }

    public void Cancel()
    {
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(true);
        }
        catch
        {
            // Ignore cancellation errors.
        }
    }

    public void Dispose()
    {
        Cancel();
        _process?.Dispose();
    }

    private static async IAsyncEnumerable<string> ReadLinesAsync(
        StreamReader reader,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line != null)
                yield return line;
        }
    }

    private static bool TryParseTime(Match match, out TimeSpan result)
    {
        var text = $"{match.Groups[1].Value}:{match.Groups[2].Value}:{match.Groups[3].Value}";
        return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out result);
    }

    private static string BuildStatus(TimeSpan? current, long? sizeKb, TimeSpan? total)
    {
        var parts = new List<string>();

        if (current.HasValue)
            parts.Add($"Time {current.Value:hh\\:mm\\:ss}");

        if (total.HasValue)
            parts.Add($"of {total.Value:hh\\:mm\\:ss}");

        if (sizeKb.HasValue)
            parts.Add($"{sizeKb.Value:N0} KB");

        return parts.Count > 0 ? string.Join(" · ", parts) : "Downloading stream...";
    }

    public static string BuildOutputPath(string movieTitle, TimeSpan? clipStart = null, TimeSpan? clipEnd = null)
    {
        var downloadsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "TinyCinema");

        var safeName = SanitizeFileName(movieTitle);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "stream";

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        if (clipStart.HasValue && clipEnd.HasValue)
        {
            return Path.Combine(
                downloadsRoot,
                $"{safeName}_clip_{FormatClipLabel(clipStart.Value)}-{FormatClipLabel(clipEnd.Value)}_{timestamp}.mp4");
        }

        return Path.Combine(downloadsRoot, $"{safeName}_{timestamp}.mp4");
    }

    private static string FormatFfmpegTimestamp(TimeSpan value) =>
        value.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

    private static string FormatClipLabel(TimeSpan value) =>
        $"{(int)value.TotalHours:D2}{value.Minutes:D2}{value.Seconds:D2}";

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return cleaned.Trim().TrimEnd('.');
    }
}
