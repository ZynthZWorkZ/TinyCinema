using System.IO;
using Serilog;

namespace TinyCinema;

public static class SmartSearchCoordinator
{
    private static readonly object Gate = new();
    private static readonly SemaphoreSlim BuildLock = new(1, 1);
    private static SearchIndexData? _index;
    private static EmbeddingModelService? _embeddingModel;
    private static Task? _backgroundLoadTask;
    private static bool _modelUnavailable;

    public static bool IsIndexReady
    {
        get
        {
            lock (Gate)
                return _index != null;
        }
    }

    public static bool IsModelAvailable => EmbeddingModelPaths.IsModelAvailable();

    public static event Action? IndexReady;

    public static SearchIndexData? GetIndex()
    {
        lock (Gate)
            return _index;
    }

    public static EmbeddingModelService? GetEmbeddingModel()
    {
        lock (Gate)
            return _embeddingModel;
    }

    public static string GetIndexPath(string catalogPath) => SearchIndexStore.GetIndexPath(catalogPath);

    public static bool IsIndexStale(string catalogPath) => SearchIndexStore.IsStale(catalogPath);

    public static string GetStatusText(string catalogPath)
    {
        if (!IsModelAvailable)
            return "Smart search model files are missing from Assets/Models/e5-small-v2.";

        if (!File.Exists(catalogPath))
            return "Movie catalog not found.";

        var indexPath = GetIndexPath(catalogPath);
        if (!File.Exists(indexPath))
            return "Search index not built yet.";

        if (IsIndexStale(catalogPath))
            return "Search index is out of date — rebuild recommended.";

        lock (Gate)
        {
            if (_index != null)
                return $"Smart search ready ({_index.Urls.Length:N0} movies indexed).";
        }

        return "Search index found on disk.";
    }

    public static void EnsureIndexLoaded(string catalogPath)
    {
        if (!File.Exists(catalogPath) || !IsModelAvailable)
            return;

        var indexPath = GetIndexPath(catalogPath);
        if (!File.Exists(indexPath))
            return;

        lock (Gate)
        {
            if (_backgroundLoadTask is { IsCompleted: false })
                return;

            _backgroundLoadTask = Task.Run(async () =>
            {
                try
                {
                    await LoadIndexInternalAsync(catalogPath, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to load smart search index.");
                }
            });
        }
    }

    public static void QueueRebuildIfStale(string catalogPath)
    {
        // Never auto-rebuild in the background — only load an existing index if present.
        EnsureIndexLoaded(catalogPath);
    }

    public static async Task<SearchIndexBuildReporter> RebuildIndexAsync(
        string catalogPath,
        IProgress<SearchIndexBuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        await BuildLock.WaitAsync(cancellationToken);
        try
        {
            await WaitForBackgroundWorkAsync(cancellationToken);

            var reporter = new SearchIndexBuildReporter(progress);
            reporter.Log("Build queued on background thread (UI will stay responsive).");
            reporter.Log($"Detailed log file: {reporter.LogFilePath}");

            await Task.Run(
                async () => await RebuildIndexInternalAsync(catalogPath, reporter, cancellationToken),
                cancellationToken);

            return reporter;
        }
        finally
        {
            BuildLock.Release();
        }
    }

    private static async Task WaitForBackgroundWorkAsync(CancellationToken cancellationToken)
    {
        Task? backgroundTask;
        lock (Gate)
            backgroundTask = _backgroundLoadTask;

        if (backgroundTask is { IsCompleted: false })
            await backgroundTask.WaitAsync(cancellationToken);
    }

    private static async Task LoadIndexInternalAsync(string catalogPath, CancellationToken cancellationToken)
    {
        var indexPath = GetIndexPath(catalogPath);
        var loaded = await SearchIndexStore.TryLoadAsync(indexPath, cancellationToken);
        if (loaded == null)
            return;

        EnsureEmbeddingModelSilent();
        lock (Gate)
            _index = loaded;

        IndexReady?.Invoke();
    }

    private static async Task RebuildIndexInternalAsync(
        string catalogPath,
        SearchIndexBuildReporter? reporter,
        CancellationToken cancellationToken)
    {
        reporter?.SetPhase("Step 1/4 - Load embedding model");
        reporter?.Log($"App directory: {AppDomain.CurrentDomain.BaseDirectory}");

        var model = await Task.Run(
            () => CreateOrReplaceEmbeddingModel(reporter),
            cancellationToken);

        await SearchIndexBuilder.BuildAndSaveAsync(catalogPath, model, reporter, cancellationToken);

        reporter?.SetPhase("Finishing");
        reporter?.Log("Loading index into memory...");
        await LoadIndexInternalAsync(catalogPath, cancellationToken);
        reporter?.Log("Smart search index build finished successfully.");
    }

    private static EmbeddingModelService CreateOrReplaceEmbeddingModel(SearchIndexBuildReporter? reporter)
    {
        if (_modelUnavailable)
            throw new InvalidOperationException("Embedding model is unavailable.");

        try
        {
            var model = EmbeddingModelService.Create(reporter);

            lock (Gate)
            {
                _embeddingModel?.Dispose();
                _embeddingModel = model;
            }

            return model;
        }
        catch (Exception ex)
        {
            _modelUnavailable = true;
            reporter?.Log($"ERROR: Failed to load embedding model — {ex.Message}");
            Log.Error(ex, "Unable to initialize embedding model.");
            throw;
        }
    }

    private static void EnsureEmbeddingModelSilent()
    {
        lock (Gate)
        {
            if (_embeddingModel != null || _modelUnavailable)
                return;
        }

        if (!IsModelAvailable)
        {
            _modelUnavailable = true;
            return;
        }

        try
        {
            var model = EmbeddingModelService.Create();
            lock (Gate)
                _embeddingModel = model;
        }
        catch (Exception ex)
        {
            _modelUnavailable = true;
            Log.Error(ex, "Unable to initialize embedding model.");
        }
    }
}
