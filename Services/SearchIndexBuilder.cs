using System.Diagnostics;
using System.IO;

namespace TinyCinema;

public static class SearchIndexBuilder
{
    private const int CheckpointInterval = 50;

    public static async Task BuildAndSaveAsync(
        string catalogPath,
        EmbeddingModelService embeddingModel,
        SearchIndexBuildSession session,
        SearchIndexBuildReporter? reporter = null,
        CancellationToken cancellationToken = default)
    {
        if (session.StartFresh)
            SearchIndexCheckpointStore.Delete(catalogPath);

        var index = await BuildAsync(
            catalogPath,
            embeddingModel,
            session,
            reporter,
            cancellationToken);

        SearchIndexCheckpointStore.Delete(catalogPath);

        reporter?.SetPhase("Step 4/4 - Save index");
        var indexPath = SearchIndexStore.GetIndexPath(catalogPath);
        reporter?.Log($"Writing index to: {indexPath}");
        reporter?.Log($"Saving {index.Urls.Length:N0} vectors ({SearchIndexData.VectorDimension} dimensions each)...");

        var saveStopwatch = Stopwatch.StartNew();
        await SearchIndexStore.SaveAsync(indexPath, index, reporter, cancellationToken);
        reporter?.Log($"Index saved in {saveStopwatch.Elapsed.TotalSeconds:F1}s.");
        reporter?.Log($"Index file size: {FormatFileSize(indexPath)}");
    }

    public static async Task<SearchIndexData> BuildAsync(
        string catalogPath,
        EmbeddingModelService embeddingModel,
        SearchIndexBuildSession session,
        SearchIndexBuildReporter? reporter = null,
        CancellationToken cancellationToken = default)
    {
        reporter?.SetPhase("Step 2/4 - Load catalog");
        reporter?.Log($"Reading catalog JSON: {catalogPath}");
        reporter?.Log($"Catalog file size: {FormatFileSize(catalogPath)}");

        var loadStopwatch = Stopwatch.StartNew();
        var catalog = await MovieCatalogStore.LoadAsync(catalogPath, cancellationToken);
        reporter?.Log($"JSON parsed in {loadStopwatch.Elapsed.TotalSeconds:F1}s.");

        var records = catalog.Movies
            .Where(record => !string.IsNullOrWhiteSpace(record.Url))
            .ToList();

        reporter?.Log($"Found {records.Count:N0} movies with URLs.");

        var catalogLastWriteUtc = File.Exists(catalogPath)
            ? File.GetLastWriteTimeUtc(catalogPath)
            : DateTime.UtcNow;
        var catalogFingerprint = SearchIndexCheckpointStore.ComputeCatalogFingerprint(records);

        SearchIndexCheckpoint? checkpoint = null;
        if (session.ResumeFromCheckpoint)
        {
            checkpoint = SearchIndexCheckpointStore.TryLoadForBuild(catalogPath, records);
            if (checkpoint == null)
            {
                reporter?.Log("WARNING: Saved checkpoint could not be resumed (catalog changed). Starting fresh.");
                SearchIndexCheckpointStore.Delete(catalogPath);
            }
            else
            {
                reporter?.Log(
                    $"Resuming from checkpoint: {checkpoint.ProcessedCount:N0}/{checkpoint.TotalCount:N0} movies already embedded.");
            }
        }

        var startIndex = checkpoint?.ProcessedCount ?? 0;
        reporter?.SetTotal(records.Count);
        reporter?.SetPhase("Step 3/4 - Embed movies");

        var urls = new string[records.Count];
        var vectors = new float[records.Count][];

        if (checkpoint != null)
        {
            for (var i = 0; i < checkpoint.ProcessedCount; i++)
            {
                urls[i] = checkpoint.Urls[i];
                vectors[i] = checkpoint.Vectors[i];
            }

            reporter?.ReportItemProcessed(
                checkpoint.ProcessedCount,
                records[checkpoint.ProcessedCount - 1].Title);
        }

        try
        {
            for (var i = startIndex; i < records.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var record = records[i];
                urls[i] = record.Url;

                if (i == startIndex && startIndex == 0)
                {
                    reporter?.Log($"Embedding first movie: \"{record.Title}\"...");
                    var firstStopwatch = Stopwatch.StartNew();
                    vectors[i] = embeddingModel.EmbedPassage(record);
                    reporter?.Log($"First movie embedded in {firstStopwatch.ElapsedMilliseconds}ms.");
                }
                else
                {
                    vectors[i] = embeddingModel.EmbedPassage(record);
                }

                var passage = SearchPassageBuilder.BuildPassage(record);
                var passageDetails = SearchPassageBuilder.WithModelTokens(
                    SearchPassageBuilder.Describe(record),
                    embeddingModel.GetModelTokens(passage));

                reporter?.ReportItemProcessed(i + 1, record.Title, passageDetails);

                if ((i + 1) % CheckpointInterval == 0)
                {
                    await SaveCheckpointAsync(
                        catalogPath,
                        catalogFingerprint,
                        catalogLastWriteUtc,
                        records.Count,
                        i + 1,
                        urls,
                        vectors,
                        reporter,
                        cancellationToken);
                }

                if ((i + 1) % 10 == 0)
                    await Task.Yield();
            }
        }
        catch (OperationCanceledException)
        {
            if (session.StopMode == SearchIndexBuildStopMode.StopAndSave)
            {
                var processed = CountProcessedVectors(vectors, records.Count);
                if (processed > 0)
                {
                    await SaveCheckpointAsync(
                        catalogPath,
                        catalogFingerprint,
                        catalogLastWriteUtc,
                        records.Count,
                        processed,
                        urls,
                        vectors,
                        reporter,
                        CancellationToken.None);

                    reporter?.Log($"Checkpoint saved at {processed:N0}/{records.Count:N0} movies.");
                    throw new SearchIndexBuildPausedException(processed, records.Count);
                }
            }

            if (session.StopMode == SearchIndexBuildStopMode.CancelDiscard)
                SearchIndexCheckpointStore.Delete(catalogPath);

            throw;
        }

        return new SearchIndexData
        {
            Urls = urls,
            Vectors = vectors,
            CatalogLastWriteUtc = catalogLastWriteUtc,
            ModelName = EmbeddingModelService.ModelName
        };
    }

    private static int CountProcessedVectors(float[][] vectors, int total)
    {
        var processed = 0;
        for (var i = 0; i < total; i++)
        {
            if (vectors[i] == null)
                break;

            processed = i + 1;
        }

        return processed;
    }

    private static async Task SaveCheckpointAsync(
        string catalogPath,
        string catalogFingerprint,
        DateTime catalogLastWriteUtc,
        int totalCount,
        int processedCount,
        string[] urls,
        float[][] vectors,
        SearchIndexBuildReporter? reporter,
        CancellationToken cancellationToken)
    {
        await SearchIndexCheckpointStore.SaveAsync(
            catalogPath,
            catalogFingerprint,
            catalogLastWriteUtc,
            totalCount,
            processedCount,
            urls,
            vectors,
            cancellationToken);

        reporter?.Log($"Auto-saved checkpoint at {processedCount:N0}/{totalCount:N0} movies.");
    }

    private static string FormatFileSize(string path)
    {
        if (!File.Exists(path))
            return "missing";

        var bytes = new FileInfo(path).Length;
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
            >= 1024 => $"{bytes / 1024.0:F1} KB",
            _ => $"{bytes} bytes"
        };
    }
}
