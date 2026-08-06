using System.Windows;
using System.Windows.Threading;

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

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    $"Fatal error: {ex.Message}",
                    "TinyCinema",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            Current?.Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                MessageBox.Show(
                    $"Background task error: {args.Exception.GetBaseException().Message}",
                    "TinyCinema",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            });
        };

        ThemeManager.ApplyTheme(SettingsWindow.GetAppTheme());
        AppLayoutManager.LoadFromSettings();

        base.OnStartup(e);
    }
}

