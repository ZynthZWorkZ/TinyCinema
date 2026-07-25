using System.Diagnostics;
using System.IO;

namespace TinyCinema;

public static class PlayerLauncher
{
    private static readonly string VlcPath = @"C:\Program Files\VideoLAN\VLC\vlc.exe";

    public static void Launch(string streamUrl, string playerName)
    {
        var startInfo = playerName switch
        {
            PlayerNames.FFPLAY => new ProcessStartInfo
            {
                FileName = "ffplay",
                Arguments = $"\"{streamUrl}\"",
                UseShellExecute = false,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            },
            PlayerNames.VLC => new ProcessStartInfo
            {
                FileName = VlcPath,
                Arguments = $"\"{streamUrl}\"",
                UseShellExecute = false,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            },
            _ => new ProcessStartInfo
            {
                FileName = ResolveTinyPlayerPath(),
                Arguments = $"\"{streamUrl}\"",
                UseShellExecute = false,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            }
        };

        Process.Start(startInfo);
    }

    private static string ResolveTinyPlayerPath()
    {
        var tinyPlayerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TinyPlayer", "TinyPlayer.exe");
        if (File.Exists(tinyPlayerPath))
            return tinyPlayerPath;

        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TinyPlayer.exe");
    }
}
