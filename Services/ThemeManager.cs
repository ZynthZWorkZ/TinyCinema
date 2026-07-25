using System.Windows;

namespace TinyCinema;

public static class ThemeManager
{
    private sealed record ThemeDefinition(string SettingValue, string DisplayName, string ResourcePath);

    private static readonly ThemeDefinition[] ThemeDefinitions =
    [
        new("Black", "Black", "Themes/ThemeColors.Black.xaml"),
        new("Red", "Red", "Themes/ThemeColors.Red.xaml"),
        new("MidnightBlue", "Midnight Blue", "Themes/ThemeColors.MidnightBlue.xaml"),
        new("Emerald", "Emerald", "Themes/ThemeColors.Emerald.xaml"),
        new("Purple", "Purple", "Themes/ThemeColors.Purple.xaml"),
        new("Orange", "Orange", "Themes/ThemeColors.Orange.xaml"),
        new("Teal", "Teal", "Themes/ThemeColors.Teal.xaml"),
        new("Rose", "Rose", "Themes/ThemeColors.Rose.xaml"),
        new("Slate", "Slate", "Themes/ThemeColors.Slate.xaml"),
        new("Forest", "Forest", "Themes/ThemeColors.Forest.xaml"),
        new("Gold", "Gold", "Themes/ThemeColors.Gold.xaml"),
        new("Ocean", "Ocean", "Themes/ThemeColors.Ocean.xaml"),
        new("Wine", "Wine", "Themes/ThemeColors.Wine.xaml"),
        new("Graphite", "Graphite", "Themes/ThemeColors.Graphite.xaml"),
        new("Neon", "Neon", "Themes/ThemeColors.Neon.xaml")
    ];

    private static readonly Dictionary<AppTheme, ThemeDefinition> ThemesByEnum =
        ThemeDefinitions
            .Select((definition, index) => (Theme: (AppTheme)index, Definition: definition))
            .ToDictionary(pair => pair.Theme, pair => pair.Definition);

    private static readonly Dictionary<string, AppTheme> ThemesByKey =
        ThemeDefinitions
            .Select((definition, index) => (Theme: (AppTheme)index, Definition: definition))
            .SelectMany(pair => new[]
            {
                KeyValuePair.Create(pair.Definition.SettingValue, pair.Theme),
                KeyValuePair.Create(pair.Definition.DisplayName, pair.Theme)
            })
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);

    public static AppTheme CurrentTheme { get; private set; } = AppTheme.Black;

    public static IReadOnlyList<string> GetAvailableDisplayNames() =>
        ThemeDefinitions.Select(definition => definition.DisplayName).ToList();

    public static void ApplyTheme(AppTheme theme)
    {
        CurrentTheme = theme;

        var app = Application.Current;
        if (app == null)
            return;

        var merged = app.Resources.MergedDictionaries;

        for (var i = merged.Count - 1; i >= 0; i--)
        {
            var source = merged[i].Source?.OriginalString ?? string.Empty;
            if (source.Contains("ThemeColors.", StringComparison.OrdinalIgnoreCase))
                merged.RemoveAt(i);
        }

        var uri = new Uri(GetThemeUri(theme), UriKind.Relative);
        merged.Insert(0, new ResourceDictionary { Source = uri });
    }

    public static AppTheme ParseTheme(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return AppTheme.Black;

        var trimmed = value.Trim();
        return ThemesByKey.TryGetValue(trimmed, out var theme) ? theme : AppTheme.Black;
    }

    public static string ToSettingValue(AppTheme theme) =>
        ThemesByEnum.TryGetValue(theme, out var definition) ? definition.SettingValue : "Black";

    public static string GetDisplayName(AppTheme theme) =>
        ThemesByEnum.TryGetValue(theme, out var definition) ? definition.DisplayName : "Black";

    private static string GetThemeUri(AppTheme theme) =>
        ThemesByEnum.TryGetValue(theme, out var definition)
            ? definition.ResourcePath
            : "Themes/ThemeColors.Black.xaml";
}
