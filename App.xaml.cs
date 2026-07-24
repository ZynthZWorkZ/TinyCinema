using System.Configuration;
using System.Data;
using System.Windows;

namespace TinyCinema;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"Unexpected error: {args.Exception.Message}",
                "TinyCinema",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        ThemeManager.ApplyTheme(SettingsWindow.GetAppTheme());
        base.OnStartup(e);
    }
}

