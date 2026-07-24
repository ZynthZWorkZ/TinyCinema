using System.Windows;

namespace TinyCinema;

public static class ThemeManager
{
    public static AppTheme CurrentTheme { get; private set; } = AppTheme.Black;

    public static void ApplyTheme(AppTheme theme)
    {
        CurrentTheme = theme;

        var app = Application.Current;
        if (app == null)
            return;

        var merged = app.Resources.MergedDictionaries;
        ResourceDictionary? existing = null;

        for (var i = merged.Count - 1; i >= 0; i--)
        {
            var source = merged[i].Source?.OriginalString ?? string.Empty;
            if (source.Contains("ThemeColors.", StringComparison.OrdinalIgnoreCase))
            {
                existing = merged[i];
                merged.RemoveAt(i);
            }
        }

        var uri = new Uri(GetThemeUri(theme), UriKind.Relative);
        merged.Insert(0, new ResourceDictionary { Source = uri });
    }

    public static AppTheme ParseTheme(string? value) =>
        value?.Trim() switch
        {
            "Red" => AppTheme.Red,
            "MidnightBlue" or "Midnight Blue" => AppTheme.MidnightBlue,
            _ => AppTheme.Black
        };

    public static string ToSettingValue(AppTheme theme) => theme switch
    {
        AppTheme.Red => "Red",
        AppTheme.MidnightBlue => "MidnightBlue",
        _ => "Black"
    };

    public static string GetDisplayName(AppTheme theme) => theme switch
    {
        AppTheme.Red => "Red",
        AppTheme.MidnightBlue => "Midnight Blue",
        _ => "Black"
    };

    private static string GetThemeUri(AppTheme theme) => theme switch
    {
        AppTheme.Red => "Themes/ThemeColors.Red.xaml",
        AppTheme.MidnightBlue => "Themes/ThemeColors.MidnightBlue.xaml",
        _ => "Themes/ThemeColors.Black.xaml"
    };
}
