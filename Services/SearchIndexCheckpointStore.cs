using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TinyCinema;

public static class SearchIndexCheckpointStore
{
    private const string Magic = "TCKPT";
    private const int FormatVersion = 1;
    private const string CheckpointFileName = "search_index.checkpoint.bin";

    public static string GetCheckpointPath(string catalogPath)
    {
        var directory = Path.GetDirectoryName(catalogPath);
        if (string.IsNullOrWhiteSpace(directory))
            directory = AppDomain.CurrentDomain.BaseDirectory;

        return Path.Combine(directory, CheckpointFileName);
    }

    public static bool Exists(string catalogPath) => File.Exists(GetCheckpointPath(catalogPath));

    public static void Delete(string catalogPath)
    {
        var path = GetCheckpointPath(catalogPath);
        if (File.Exists(path))
            File.Delete(path);
    }

    public static string ComputeCatalogFingerprint(IReadOnlyList<MovieCatalogRecord> records)
    {
        var builder = new StringBuilder(records.Count * 64);
        foreach (var record in records)
            builder.AppendLine(record.Url);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    public static SearchIndexCheckpointStatus? TryGetStatus(string catalogPath)
    {
        var checkpoint = TryLoad(catalogPath);
        if (checkpoint == null || checkpoint.ProcessedCount <= 0)
            return null;

        var path = GetCheckpointPath(catalogPath);
        return new SearchIndexCheckpointStatus
        {
            ProcessedCount = checkpoint.ProcessedCount,
            TotalCount = checkpoint.TotalCount,
            SavedAtUtc = File.GetLastWriteTimeUtc(path)
        };
    }

    public static SearchIndexCheckpoint? TryLoadForBuild(
        string catalogPath,
        IReadOnlyList<MovieCatalogRecord> records)
    {
        var checkpoint = TryLoad(catalogPath);
        if (checkpoint == null)
            return null;

        if (!ValidateAgainstCatalog(checkpoint, catalogPath, records))
            return null;

        return checkpoint;
    }

    public static bool ValidateAgainstCatalog(
        SearchIndexCheckpoint checkpoint,
        string catalogPath,
        IReadOnlyList<MovieCatalogRecord> records)
    {
        if (!string.Equals(checkpoint.CatalogPath, catalogPath, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!File.Exists(catalogPath))
            return false;

        var catalogWriteUtc = File.GetLastWriteTimeUtc(catalogPath);
        if (catalogWriteUtc != checkpoint.CatalogLastWriteUtc)
            return false;

        if (!string.Equals(checkpoint.ModelName, EmbeddingModelService.ModelName, StringComparison.Ordinal))
            return false;

        if (records.Count != checkpoint.TotalCount)
            return false;

        if (!string.Equals(
                ComputeCatalogFingerprint(records),
                checkpoint.CatalogFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (checkpoint.ProcessedCount <= 0 || checkpoint.ProcessedCount > checkpoint.TotalCount)
            return false;

        for (var i = 0; i < checkpoint.ProcessedCount; i++)
        {
            if (!string.Equals(records[i].Url, checkpoint.Urls[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    public static async Task SaveAsync(
        string catalogPath,
        string catalogFingerprint,
        DateTime catalogLastWriteUtc,
        int totalCount,
        int processedCount,
        string[] urls,
        float[][] vectors,
        CancellationToken cancellationToken = default)
    {
        if (processedCount <= 0)
            return;

        var checkpointPath = GetCheckpointPath(catalogPath);
        var directory = Path.GetDirectoryName(checkpointPath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var tempPath = checkpointPath + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic.ToCharArray());
            writer.Write(FormatVersion);
            WriteString(writer, catalogPath);
            writer.Write(catalogLastWriteUtc.ToBinary());
            WriteString(writer, catalogFingerprint);
            WriteString(writer, EmbeddingModelService.ModelName);
            writer.Write(totalCount);
            writer.Write(processedCount);

            for (var i = 0; i < processedCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteString(writer, urls[i]);

                var vector = vectors[i];
                for (var dim = 0; dim < SearchIndexData.VectorDimension; dim++)
                    writer.Write(vector[dim]);
            }
        }

        try
        {
            if (File.Exists(checkpointPath))
                File.Replace(tempPath, checkpointPath, destinationBackupFileName: null);
            else
                File.Move(tempPath, checkpointPath);
        }
        catch (IOException)
        {
            File.Copy(tempPath, checkpointPath, overwrite: true);
            File.Delete(tempPath);
        }
    }

    private static SearchIndexCheckpoint? TryLoad(string catalogPath)
    {
        var checkpointPath = GetCheckpointPath(catalogPath);
        if (!File.Exists(checkpointPath))
            return null;

        try
        {
            using var stream = new FileStream(checkpointPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            var magic = new string(reader.ReadChars(Magic.Length));
            if (!magic.Equals(Magic, StringComparison.Ordinal))
                return null;

            var version = reader.ReadInt32();
            if (version != FormatVersion)
                return null;

            var storedCatalogPath = ReadString(reader);
            var catalogLastWriteUtc = DateTime.FromBinary(reader.ReadInt64());
            var catalogFingerprint = ReadString(reader);
            var modelName = ReadString(reader);
            var totalCount = reader.ReadInt32();
            var processedCount = reader.ReadInt32();

            if (totalCount <= 0 || processedCount <= 0 || processedCount > totalCount)
                return null;

            var urls = new string[processedCount];
            var vectors = new float[processedCount][];

            for (var i = 0; i < processedCount; i++)
            {
                urls[i] = ReadString(reader);
                var vector = new float[SearchIndexData.VectorDimension];
                for (var dim = 0; dim < SearchIndexData.VectorDimension; dim++)
                    vector[dim] = reader.ReadSingle();
                vectors[i] = vector;
            }

            return new SearchIndexCheckpoint
            {
                CatalogPath = storedCatalogPath,
                CatalogLastWriteUtc = catalogLastWriteUtc,
                CatalogFingerprint = catalogFingerprint,
                ModelName = modelName,
                TotalCount = totalCount,
                ProcessedCount = processedCount,
                Urls = urls,
                Vectors = vectors
            };
        }
        catch
        {
            return null;
        }
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length <= 0)
            return string.Empty;

        var bytes = reader.ReadBytes(length);
        return Encoding.UTF8.GetString(bytes);
    }
}
