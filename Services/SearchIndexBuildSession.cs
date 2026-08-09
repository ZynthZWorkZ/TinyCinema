namespace TinyCinema;

public sealed class SearchIndexBuildSession : IDisposable
{
    private int _stopMode;

    public CancellationTokenSource CancellationSource { get; } = new();

    public CancellationToken Token => CancellationSource.Token;

    public bool ResumeFromCheckpoint { get; init; }

    public bool StartFresh { get; init; }

    public SearchIndexBuildStopMode StopMode =>
        (SearchIndexBuildStopMode)Volatile.Read(ref _stopMode);

    public void RequestStopAndSave()
    {
        Volatile.Write(ref _stopMode, (int)SearchIndexBuildStopMode.StopAndSave);
        CancellationSource.Cancel();
    }

    public void RequestCancelDiscard()
    {
        Volatile.Write(ref _stopMode, (int)SearchIndexBuildStopMode.CancelDiscard);
        CancellationSource.Cancel();
    }

    public void Dispose() => CancellationSource.Dispose();
}
