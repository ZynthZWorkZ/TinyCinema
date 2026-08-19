using System.IO;
using System.IO.Compression;

namespace TinyCinema;

public static class RokuChannelPackager
{
    public static void CreateZip(string sourceDirectory, string zipPath)
    {
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var entryName = Path.GetRelativePath(sourceDirectory, filePath)
                .Replace('\\', '/');

            archive.CreateEntryFromFile(filePath, entryName, CompressionLevel.Optimal);
        }
    }
}
