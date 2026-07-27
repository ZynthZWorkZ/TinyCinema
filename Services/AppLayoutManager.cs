using System.IO;
using System.Windows;
using System.Windows.Media;

namespace TinyCinema;

public enum AppWindowSize
{
    Compact,
    Standard,
    Large
}

public sealed class AppLayoutProfile
{
    public required AppWindowSize Size { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required double Scale { get; init; }

    public const double BaseWidth = 1280;
    public const double BaseHeight = 800;
    public const double BaseMinWidth = 960;
    public const double BaseMinHeight = 640;

    public double WindowWidth => Math.Round(BaseWidth * Scale);
    public double WindowHeight => Math.Round(BaseHeight * Scale);
    public double WindowMinWidth => Math.Round(BaseMinWidth * Scale);
    public double WindowMinHeight => Math.Round(BaseMinHeight * Scale);
}

public static class AppLayoutManager
{
    private static readonly AppLayoutProfile[] Profiles =
    [
        new AppLayoutProfile
        {
            Size = AppWindowSize.Compact,
            DisplayName = "Compact (Laptop)",
            Description = "Best for 1366×768 and smaller screens.",
            Scale = 0.82
        },
        new AppLayoutProfile
        {
            Size = AppWindowSize.Standard,
            DisplayName = "Standard",
            Description = "Balanced layout for most displays.",
            Scale = 1.0
        },
        new AppLayoutProfile
        {
            Size = AppWindowSize.Large,
            DisplayName = "Large (Desktop)",
            Description = "Roomier layout for 1440p and larger monitors.",
            Scale = 1.12
        }
    ];

    public static event Action? LayoutChanged;

    public static AppWindowSize CurrentSize { get; private set; } = AppWindowSize.Standard;

    public static IReadOnlyList<AppLayoutProfile> AvailableProfiles => Profiles;

    public static IReadOnlyList<string> AvailableDisplayNames =>
        Profiles.Select(profile => profile.DisplayName).ToArray();

    public static AppLayoutProfile GetProfile(AppWindowSize size) =>
        Profiles.First(profile => profile.Size == size);

    public static AppLayoutProfile CurrentProfile => GetProfile(CurrentSize);

    public static void LoadFromSettings()
    {
        CurrentSize = ReadFromSettings();
    }

    public static AppWindowSize ReadFromSettings()
    {
        try
        {
            var settingsFile = SettingsWindow.SettingsFilePath;
            if (!File.Exists(settingsFile))
                return AppWindowSize.Standard;

            foreach (var line in File.ReadAllLines(settingsFile))
            {
                if (line.StartsWith("WindowSize=", StringComparison.Ordinal))
                    return ParseSize(line.Substring("WindowSize=".Length).Trim());
            }
        }
        catch
        {
            // Ignore read errors.
        }

        return AppWindowSize.Standard;
    }

    public static AppWindowSize ParseSize(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "compact" or "compact (laptop)" or "laptop" => AppWindowSize.Compact,
            "large" or "large (desktop)" or "desktop" => AppWindowSize.Large,
            _ => AppWindowSize.Standard
        };

    public static string ToSettingValue(AppWindowSize size) =>
        size switch
        {
            AppWindowSize.Compact => "Compact",
            AppWindowSize.Large => "Large",
            _ => "Standard"
        };

    public static string GetDisplayName(AppWindowSize size) => GetProfile(size).DisplayName;

    public static AppWindowSize ParseDisplayName(string displayName)
    {
        var match = Profiles.FirstOrDefault(profile =>
            profile.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase));
        return match?.Size ?? AppWindowSize.Standard;
    }

    public static void SetSize(AppWindowSize size, bool persist = true)
    {
        if (CurrentSize == size)
            return;

        CurrentSize = size;
        if (persist)
            SettingsWindow.UpdateWindowSizeSetting(size);

        LayoutChanged?.Invoke();
    }

    public static void ApplyTo(Window window, ScaleTransform scaleTransform)
    {
        var profile = CurrentProfile;
        scaleTransform.ScaleX = profile.Scale;
        scaleTransform.ScaleY = profile.Scale;

        if (window.WindowState != WindowState.Maximized)
        {
            window.Width = profile.WindowWidth;
            window.Height = profile.WindowHeight;
        }

        window.MinWidth = profile.WindowMinWidth;
        window.MinHeight = profile.WindowMinHeight;
    }
}
