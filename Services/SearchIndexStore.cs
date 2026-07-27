using System.IO;
using System.Text;

namespace TinyCinema;

public static class SearchIndexStore
{
    private const string Magic = "TCIDX";
    private const int FormatVersion = 1;

    public static string GetIndexPath(string catalogPath)
    {
        var directory = Path.GetDirectoryName(catalogPath);
        if (string.IsNullOrWhiteSpace(directory))
            directory = AppDomain.CurrentDomain.BaseDirectory;

        return Path.Combine(directory, "search_index.bin");
    }

    public static bool Exists(string catalogPath) => File.Exists(GetIndexPath(catalogPath));

    public static bool IsStale(string catalogPath)
    {
        var indexPath = GetIndexPath(catalogPath);
        if (!File.Exists(catalogPath) || !File.Exists(indexPath))
            return true;

        var catalogWrite = File.GetLastWriteTimeUtc(catalogPath);
        var indexWrite = File.GetLastWriteTimeUtc(indexPath);
        return catalogWrite > indexWrite;
    }

    public static async Task SaveAsync(
        string indexPath,
        SearchIndexData data,
        SearchIndexBuildReporter? reporter = null,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(indexPath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var tempPath = indexPath + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic.ToCharArray());
            writer.Write(FormatVersion);
            writer.Write(SearchIndexData.VectorDimension);
            writer.Write(data.Urls.Length);
            writer.Write(data.CatalogLastWriteUtc.ToBinary());
            WriteString(writer, data.ModelName);

            for (var i = 0; i < data.Urls.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteString(writer, data.Urls[i]);

                var vector = data.Vectors[i];
                for (var dim = 0; dim < SearchIndexData.VectorDimension; dim++)
                    writer.Write(vector[dim]);

                if (reporter != null && ((i + 1) % 2500 == 0 || i == data.Urls.Length - 1))
                    reporter.Log($"Written {i + 1:N0} / {data.Urls.Length:N0} vectors to disk...");
            }
        }

        reporter?.Log("Finalizing index file (atomic replace)...");

        try
        {
            if (File.Exists(indexPath))
                File.Replace(tempPath, indexPath, destinationBackupFileName: null);
            else
                File.Move(tempPath, indexPath);
        }
        catch (IOException)
        {
            File.Copy(tempPath, indexPath, overwrite: true);
            File.Delete(tempPath);
        }
    }

    public static async Task<SearchIndexData?> TryLoadAsync(string indexPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(indexPath))
            return null;

        await using var stream = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var magic = new string(reader.ReadChars(Magic.Length));
        if (!magic.Equals(Magic, StringComparison.Ordinal))
            return null;

        var version = reader.ReadInt32();
        if (version != FormatVersion)
            return null;

        var dimension = reader.ReadInt32();
        if (dimension != SearchIndexData.VectorDimension)
            return null;

        var count = reader.ReadInt32();
        var catalogLastWriteUtc = DateTime.FromBinary(reader.ReadInt64());
        var modelName = ReadString(reader);

        var urls = new string[count];
        var vectors = new float[count][];

        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            urls[i] = ReadString(reader);
            var vector = new float[dimension];
            for (var dim = 0; dim < dimension; dim++)
                vector[dim] = reader.ReadSingle();
            vectors[i] = vector;
        }

        return new SearchIndexData
        {
            Urls = urls,
            Vectors = vectors,
            CatalogLastWriteUtc = catalogLastWriteUtc,
            ModelName = modelName
        };
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
