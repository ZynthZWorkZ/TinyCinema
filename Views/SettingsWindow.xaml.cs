using System.IO;

namespace TinyCinema;

public static class SettingsWindow
{
    public static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TinyCinema",
        "settings.json");

    internal static readonly string VlcPath = @"C:\Program Files\VideoLAN\VLC\vlc.exe";

    public static bool IsVlcInstalled() => File.Exists(VlcPath);

    public static string GetVlcPath() => VlcPath;

    public static bool GetIsPopupBlockerEnabled() => ReadBoolSetting("IsPopupBlockerEnabled=", defaultValue: true);

    public static bool GetIsClearPlayerBrowserDataOnClose() =>
        ReadBoolSetting("IsClearPlayerBrowserDataOnClose=", defaultValue: false);

    public static bool GetIsMovieLairProbeEnabled() =>
        ReadBoolSetting("IsMovieLairProbeEnabled=", defaultValue: false);

    public static bool GetIsCachingEnabled() => ReadBoolSetting("IsCachingEnabled=", defaultValue: false);

    public static bool GetIsTvShowCachingEnabled() => ReadBoolSetting("IsTvShowCachingEnabled=", defaultValue: true);

    public static string GetCacheLocation()
    {
        var location = ReadStringSetting("CacheLocation=");
        if (!string.IsNullOrEmpty(location))
            return location;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TinyCinema",
            "ImageCache");
    }

    public static string GetSelectedPlayer()
    {
        var player = ReadStringSetting("SelectedPlayer=");
        return player switch
        {
            "" => PlayerNames.InAppBrowser,
            "Built-in Browser" or "TinyZone Browser" => PlayerNames.InAppBrowser,
            _ => player
        };
    }

    public static string GetMovieCatalogLocation()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
                return NormalizeMovieCatalogLocation(null);

            foreach (var line in File.ReadAllLines(SettingsFilePath))
            {
                if (line.StartsWith("MovieCatalogLocation=", StringComparison.Ordinal))
                    return NormalizeMovieCatalogLocation(line.Substring("MovieCatalogLocation=".Length).Trim());

                if (line.StartsWith("MovieLinksLocation=", StringComparison.Ordinal))
                    return NormalizeMovieCatalogLocation(line.Substring("MovieLinksLocation=".Length).Trim());
            }
        }
        catch
        {
            // Ignore read errors.
        }

        return NormalizeMovieCatalogLocation(null);
    }

    public static string GetTvShowCatalogLocation()
    {
        var location = ReadStringSetting("TvShowCatalogLocation=");
        if (string.IsNullOrEmpty(location))
            location = ReadStringSetting("TvShowLinksLocation=");

        return NormalizeTvShowCatalogLocation(location);
    }

    public static string GetTvShowLinksLocation() => GetTvShowCatalogLocation();

    public static AppWindowSize GetWindowSize() => AppLayoutManager.ReadFromSettings();

    public static bool GetStartCentered() => ReadBoolSetting("StartCentered=", defaultValue: true);

    public static void UpdateWindowSizeSetting(AppWindowSize size)
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var lines = File.Exists(SettingsFilePath)
                ? File.ReadAllLines(SettingsFilePath).ToList()
                : [];

            var settingLine = $"WindowSize={AppLayoutManager.ToSettingValue(size)}";
            var index = lines.FindIndex(line => line.StartsWith("WindowSize=", StringComparison.Ordinal));
            if (index >= 0)
                lines[index] = settingLine;
            else
                lines.Add(settingLine);

            File.WriteAllLines(SettingsFilePath, lines);
        }
        catch
        {
            // Ignore write errors.
        }
    }

    public static AppTheme GetAppTheme()
    {
        var value = ReadStringSetting("AppTheme=");
        return string.IsNullOrEmpty(value) ? AppTheme.Black : ThemeManager.ParseTheme(value);
    }

    public static string GetTmdbApiKey() => ReadStringSetting("TmdbApiKey=");

    public static string GetPlayerEmbedHostsRaw() => ReadStringSetting(PlayerEmbedHostSettings.SettingPrefix);

    public static string GetPlayerRequestBlocklistRaw() => ReadStringSetting(PlayerRequestBlocklistSettings.SettingPrefix);

    public static (string Ip, string Username, string Password) GetRokuCredentials()
    {
        var ip = "";
        var username = "rokudev";
        var password = "";

        try
        {
            if (!File.Exists(SettingsFilePath))
                return (ip, username, password);

            foreach (var line in File.ReadAllLines(SettingsFilePath))
            {
                if (line.StartsWith("RokuIpAddress=", StringComparison.Ordinal))
                    ip = line.Substring("RokuIpAddress=".Length).Trim();
                else if (line.StartsWith("RokuUsername=", StringComparison.Ordinal))
                    username = line.Substring("RokuUsername=".Length).Trim();
                else if (line.StartsWith("RokuPassword=", StringComparison.Ordinal))
                    password = line.Substring("RokuPassword=".Length).Trim();
            }
        }
        catch
        {
            // Ignore read errors.
        }

        if (string.IsNullOrWhiteSpace(username))
            username = "rokudev";

        if (string.IsNullOrWhiteSpace(password))
            password = "rokudev";

        return (ip, username, password);
    }

    internal static string NormalizeMovieCatalogLocation(string? configuredPath)
    {
        var defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Movies.json");

        if (string.IsNullOrWhiteSpace(configuredPath))
            return defaultPath;

        if (configuredPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return configuredPath;

        if (configuredPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            var jsonSibling = Path.Combine(
                Path.GetDirectoryName(configuredPath) ?? AppDomain.CurrentDomain.BaseDirectory,
                "Movies.json");

            if (File.Exists(jsonSibling))
                return jsonSibling;
        }

        return configuredPath;
    }

    internal static string NormalizeTvShowCatalogLocation(string? configuredPath)
    {
        var defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TvShows.json");

        if (string.IsNullOrWhiteSpace(configuredPath))
            return defaultPath;

        if (configuredPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return configuredPath;

        if (configuredPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            var jsonSibling = Path.Combine(
                Path.GetDirectoryName(configuredPath) ?? AppDomain.CurrentDomain.BaseDirectory,
                "TvShows.json");

            if (File.Exists(jsonSibling))
                return jsonSibling;
        }

        return configuredPath;
    }

    private static bool ReadBoolSetting(string prefix, bool defaultValue)
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
                return defaultValue;

            foreach (var line in File.ReadAllLines(SettingsFilePath))
            {
                if (line.StartsWith(prefix, StringComparison.Ordinal) &&
                    bool.TryParse(line.Substring(prefix.Length).Trim(), out var enabled))
                {
                    return enabled;
                }
            }
        }
        catch
        {
            // Ignore read errors.
        }

        return defaultValue;
    }

    private static string ReadStringSetting(string prefix)
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
                return "";

            foreach (var line in File.ReadAllLines(SettingsFilePath))
            {
                if (line.StartsWith(prefix, StringComparison.Ordinal))
                    return line.Substring(prefix.Length).Trim();
            }
        }
        catch
        {
            // Ignore read errors.
        }

        return "";
    }
}
