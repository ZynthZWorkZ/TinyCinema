using System.Text;
using System.Windows;
using Serilog;

namespace TinyCinema;

public static class DownloadDebugHelper
{
    public static void ShowError(string stage, Exception ex, string? extraContext = null)
    {
        Log.Error(ex, "Download error at stage {Stage}. Context: {Context}", stage, extraContext ?? string.Empty);

        var details = new StringBuilder();
        details.AppendLine($"Stage: {stage}");
        details.AppendLine();
        details.AppendLine(ex.Message);

        if (!string.IsNullOrWhiteSpace(extraContext))
        {
            details.AppendLine();
            details.AppendLine("Context:");
            details.AppendLine(extraContext);
        }

        if (ex.InnerException != null)
        {
            details.AppendLine();
            details.AppendLine("Inner error:");
            details.AppendLine(ex.InnerException.Message);
        }

        details.AppendLine();
        details.AppendLine("Type:");
        details.AppendLine(ex.GetType().FullName);

        details.AppendLine();
        details.AppendLine("Stack trace:");
        details.AppendLine(ex.StackTrace);

        MessageBox.Show(
            details.ToString(),
            "Download Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
