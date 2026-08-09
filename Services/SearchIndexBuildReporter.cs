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
    private int _stepIndex;
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
        _stepIndex = ParseStepIndex(phase);
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

    public void ReportItemProcessed(int processed, string itemStatus, SearchPassageDescription? passage = null)
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
        Publish(
            logLine: publishLine ? line : string.Empty,
            itemsPerSecond: rate,
            passageFields: passage?.Fields,
            passagePreview: passage?.Preview,
            searchKeywords: passage?.SearchKeywords,
            modelTokens: passage?.ModelTokens);
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

    private void Publish(
        string? logLine = null,
        double? itemsPerSecond = null,
        IReadOnlyList<SearchPassageField>? passageFields = null,
        string? passagePreview = null,
        IReadOnlyList<string>? searchKeywords = null,
        IReadOnlyList<string>? modelTokens = null)
    {
        try
        {
            _publish?.Invoke(new SearchIndexBuildProgress
            {
                Processed = _processed,
                Total = _total,
                Status = _status,
                Phase = _phase,
                StepIndex = _stepIndex,
                LogLine = logLine ?? string.Empty,
                LogFilePath = LogFilePath,
                ElapsedSeconds = _stopwatch.Elapsed.TotalSeconds,
                ItemsPerSecond = itemsPerSecond,
                PassageFields = passageFields ?? [],
                PassagePreview = passagePreview ?? string.Empty,
                SearchKeywords = searchKeywords ?? [],
                ModelTokens = modelTokens ?? []
            });
        }
        catch
        {
            // Never crash the build because UI progress reporting failed.
        }
    }

    private static int ParseStepIndex(string phase)
    {
        if (phase.Contains("Step 1", StringComparison.Ordinal))
            return 1;
        if (phase.Contains("Step 2", StringComparison.Ordinal))
            return 2;
        if (phase.Contains("Step 3", StringComparison.Ordinal))
            return 3;
        if (phase.Contains("Step 4", StringComparison.Ordinal) || phase.Contains("Save index", StringComparison.OrdinalIgnoreCase))
            return 4;
        if (phase.Contains("Finishing", StringComparison.OrdinalIgnoreCase))
            return 4;

        return 0;
    }
}
