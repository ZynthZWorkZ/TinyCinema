using System.Diagnostics;
using System.IO;
using System.Windows;

namespace TinyCinema;

public static class ExternalPlayerLauncher
{
    public static bool Launch(string playerName, string streamUrl)
    {
        try
        {
            var startInfo = BuildStartInfo(playerName, streamUrl);
            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to launch {playerName}: {ex.Message}",
                "Player Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private static ProcessStartInfo BuildStartInfo(string playerName, string streamUrl)
    {
        if (playerName == PlayerNames.FFPLAY)
        {
            return new ProcessStartInfo
            {
                FileName = "ffplay",
                Arguments = $"\"{streamUrl}\"",
                UseShellExecute = false,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            };
        }

        if (playerName == PlayerNames.VLC)
        {
            return new ProcessStartInfo
            {
                FileName = SettingsWindow.GetVlcPath(),
                Arguments = $"\"{streamUrl}\"",
                UseShellExecute = false,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            };
        }

        var tinyPlayerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TinyPlayer", "TinyPlayer.exe");
        if (!File.Exists(tinyPlayerPath))
            tinyPlayerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TinyPlayer.exe");

        return new ProcessStartInfo
        {
            FileName = tinyPlayerPath,
            Arguments = $"\"{streamUrl}\"",
            UseShellExecute = false,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Normal
        };
    }
}
