using System.Diagnostics;
using System.IO;

namespace TinyCinema;

public static class SearchIndexBuilder
{
    public static async Task<SearchIndexData> BuildAsync(
        string catalogPath,
        EmbeddingModelService embeddingModel,
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
        reporter?.SetTotal(records.Count);

        reporter?.SetPhase("Step 3/4 - Embed movies");
        var urls = new string[records.Count];
        var vectors = new float[records.Count][];

        for (var i = 0; i < records.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var record = records[i];
            urls[i] = record.Url;

            if (i == 0)
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

            reporter?.ReportItemProcessed(i + 1, record.Title);

            if ((i + 1) % 10 == 0)
                await Task.Yield();
        }

        var catalogWriteUtc = File.Exists(catalogPath)
            ? File.GetLastWriteTimeUtc(catalogPath)
            : DateTime.UtcNow;

        return new SearchIndexData
        {
            Urls = urls,
            Vectors = vectors,
            CatalogLastWriteUtc = catalogWriteUtc,
            ModelName = EmbeddingModelService.ModelName
        };
    }

    public static async Task BuildAndSaveAsync(
        string catalogPath,
        EmbeddingModelService embeddingModel,
        SearchIndexBuildReporter? reporter = null,
        CancellationToken cancellationToken = default)
    {
        var index = await BuildAsync(catalogPath, embeddingModel, reporter, cancellationToken);

        reporter?.SetPhase("Step 4/4 - Save index");
        var indexPath = SearchIndexStore.GetIndexPath(catalogPath);
        reporter?.Log($"Writing index to: {indexPath}");
        reporter?.Log($"Saving {index.Urls.Length:N0} vectors ({SearchIndexData.VectorDimension} dimensions each)...");

        var saveStopwatch = Stopwatch.StartNew();
        await SearchIndexStore.SaveAsync(indexPath, index, reporter, cancellationToken);
        reporter?.Log($"Index saved in {saveStopwatch.Elapsed.TotalSeconds:F1}s.");
        reporter?.Log($"Index file size: {FormatFileSize(indexPath)}");
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
