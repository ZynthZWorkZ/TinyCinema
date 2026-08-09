namespace TinyCinema;

public sealed class SearchIndexCheckpoint
{
    public required string CatalogPath { get; init; }

    public required DateTime CatalogLastWriteUtc { get; init; }

    public required string CatalogFingerprint { get; init; }

    public required string ModelName { get; init; }

    public required int TotalCount { get; init; }

    public required int ProcessedCount { get; init; }

    public required string[] Urls { get; init; }

    public required float[][] Vectors { get; init; }
}

public sealed class SearchIndexCheckpointStatus
{
    public int ProcessedCount { get; init; }

    public int TotalCount { get; init; }

    public DateTime SavedAtUtc { get; init; }

    public double Percent => TotalCount <= 0 ? 0 : (double)ProcessedCount / TotalCount * 100;
}

public enum SearchIndexBuildStopMode
{
    None = 0,
    StopAndSave = 1,
    CancelDiscard = 2
}

public sealed class SearchIndexBuildPausedException : OperationCanceledException
{
    public SearchIndexBuildPausedException(int processed, int total)
    {
        Processed = processed;
        Total = total;
    }

    public int Processed { get; }

    public int Total { get; }
}
