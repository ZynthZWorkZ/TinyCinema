using System.Diagnostics;
using System.IO;
using System.Text;

namespace TinyCinema;

public sealed class SearchIndexBuildReporter
{
    private readonly Action<SearchIndexBuildProgress>? _publish;
    private readonly object _fileLock = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private int _processed;
    private int _total;
    private string _phase = string.Empty;
    private string _status = string.Empty;
    private DateTime _lastRateSampleUtc = DateTime.UtcNow;
    private int _lastRateSampleProcessed;

    public SearchIndexBuildReporter(IProgress<SearchIndexBuildProgress>? progress = null)
    {
        if (progress != null)
            _publish = report => progress.Report(report);

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TinyCinema",
            "logs");

        Directory.CreateDirectory(logDirectory);
        LogFilePath = Path.Combine(logDirectory, $"search_index_build_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        File.WriteAllText(
            LogFilePath,
            $"""
             TinyCinema - Smart Search Index Build Log
             Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
             Machine: {Environment.MachineName}
             OS: {Environment.OSVersion}
             CPUs: {Environment.ProcessorCount}

             """,
            Encoding.UTF8);
    }

    public string LogFilePath { get; }

    public void SetPhase(string phase)
    {
        _phase = phase;
        Log(phase, includeInStatus: true);
    }

    public void SetTotal(int total)
    {
        _total = total;
        Publish();
    }

    public void Log(string message, bool includeInStatus = false)
    {
        if (includeInStatus)
            _status = message;

        var line = $"[{_stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}] {message}";
        WriteToFile(line);
        Publish(logLine: line);
    }

    public void ReportItemProcessed(int processed, string itemStatus)
    {
        _processed = processed;
        _status = itemStatus;

        double? rate = null;
        var now = DateTime.UtcNow;
        var elapsedSinceSample = (now - _lastRateSampleUtc).TotalSeconds;
        if (elapsedSinceSample >= 1.0)
        {
            rate = (_processed - _lastRateSampleProcessed) / elapsedSinceSample;
            _lastRateSampleUtc = now;
            _lastRateSampleProcessed = _processed;
        }

        var line =
            $"[{_stopwatch.Elapsed:hh\\:mm\\:ss}] [{processed:N0}/{_total:N0}] {itemStatus}" +
            (rate is > 0 ? $" — {rate.Value:F1} movies/sec" : string.Empty);

        WriteToFile(line);

        var publishLine = processed <= 5 || processed % 25 == 0 || processed == _total;
        Publish(logLine: publishLine ? line : string.Empty, itemsPerSecond: rate);
    }

    private void WriteToFile(string line)
    {
        try
        {
            lock (_fileLock)
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Keep building even if log file write fails.
        }
    }

    private void Publish(string? logLine = null, double? itemsPerSecond = null)
    {
        try
        {
            _publish?.Invoke(new SearchIndexBuildProgress
            {
                Processed = _processed,
                Total = _total,
                Status = _status,
                Phase = _phase,
                LogLine = logLine ?? string.Empty,
                LogFilePath = LogFilePath,
                ElapsedSeconds = _stopwatch.Elapsed.TotalSeconds,
                ItemsPerSecond = itemsPerSecond
            });
        }
        catch
        {
            // Never crash the build because UI progress reporting failed.
        }
    }
}
