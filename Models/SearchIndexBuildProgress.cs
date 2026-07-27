namespace TinyCinema;

public sealed class SearchIndexBuildProgress
{
    public int Processed { get; init; }

    public int Total { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Phase { get; init; } = string.Empty;

    public string LogLine { get; init; } = string.Empty;

    public string? LogFilePath { get; init; }

    public double ElapsedSeconds { get; init; }

    public double? ItemsPerSecond { get; init; }

    public double Percent => Total <= 0 ? 0 : (double)Processed / Total * 100;
}
