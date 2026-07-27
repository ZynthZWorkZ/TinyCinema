using System.Windows;

namespace TinyCinema;

public static class WindowPlacementHelper
{
    public static void CenterOnWorkingArea(Window window)
    {
        if (window.WindowState == WindowState.Maximized)
            return;

        window.WindowStartupLocation = WindowStartupLocation.Manual;

        var workArea = SystemParameters.WorkArea;
        var width = GetEffectiveWidth(window);
        var height = GetEffectiveHeight(window);

        window.Left = workArea.Left + Math.Max(0, (workArea.Width - width) / 2);
        window.Top = workArea.Top + Math.Max(0, (workArea.Height - height) / 2);
    }

    private static double GetEffectiveWidth(Window window)
    {
        if (!double.IsNaN(window.Width) && window.Width > 0)
            return window.Width;

        if (window.ActualWidth > 0)
            return window.ActualWidth;

        return AppLayoutManager.CurrentProfile.WindowWidth;
    }

    private static double GetEffectiveHeight(Window window)
    {
        if (!double.IsNaN(window.Height) && window.Height > 0)
            return window.Height;

        if (window.ActualHeight > 0)
            return window.ActualHeight;

        return AppLayoutManager.CurrentProfile.WindowHeight;
    }
}
