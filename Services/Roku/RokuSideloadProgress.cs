namespace TinyCinema;

public enum RokuSideloadStage
{
    Building,
    Uploading,
    Installing,
    Launching,
    Completed,
    Failed
}

public sealed class RokuSideloadProgress(RokuSideloadStage stage, string message)
{
    public RokuSideloadStage Stage { get; } = stage;
    public string Message { get; } = message;
}
