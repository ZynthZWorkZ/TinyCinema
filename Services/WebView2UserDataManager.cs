using System.IO;
using Microsoft.Web.WebView2.Core;

namespace TinyCinema;

public static class WebView2UserDataManager
{
    public static string PlayerUserDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TinyCinema",
        "WebView2",
        "Player");

    public static string? LegacyPlayerUserDataFolder
    {
        get
        {
            var processPath = Environment.ProcessPath;
            return string.IsNullOrWhiteSpace(processPath) ? null : processPath + ".WebView2";
        }
    }

    public static async Task<CoreWebView2Environment> CreatePlayerEnvironmentAsync()
    {
        Directory.CreateDirectory(PlayerUserDataFolder);
        return await CoreWebView2Environment.CreateAsync(null, PlayerUserDataFolder);
    }

    public static void TryClearPlayerUserData()
    {
        TryDeleteDirectory(PlayerUserDataFolder);

        var legacyFolder = LegacyPlayerUserDataFolder;
        if (!string.IsNullOrWhiteSpace(legacyFolder))
            TryDeleteDirectory(legacyFolder);
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(200);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(200);
            }
            catch
            {
                return;
            }
        }
    }
}
