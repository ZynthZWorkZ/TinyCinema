namespace TinyCinema;

public enum StreamDownloadMode
{
    Full,
    Clip
}

public sealed class StreamDownloadRequest
{
    public StreamDownloadMode Mode { get; init; } = StreamDownloadMode.Full;
    public TimeSpan? ClipStart { get; init; }
    public TimeSpan? ClipEnd { get; init; }
}
