using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace TinyCinema;

public partial class DownloadOptionsDialog : Window
{
    private static readonly Regex TimeSpanRegex = new(
        @"^(?:(?<hours>\d+):)?(?<minutes>\d{1,2}):(?<seconds>\d{1,2}(?:\.\d+)?)$",
        RegexOptions.Compiled);

    public StreamDownloadRequest? Request { get; private set; }

    public DownloadOptionsDialog()
    {
        try
        {
            InitializeComponent();
            FullDownloadRadio.Checked += DownloadMode_Changed;
            ClipDownloadRadio.Checked += DownloadMode_Changed;
            FullDownloadRadio.IsChecked = true;
            ApplyClipPanelState();
        }
        catch (Exception ex)
        {
            DownloadDebugHelper.ShowError("Opening download options dialog", ex);
            throw;
        }
    }

    private void DownloadMode_Changed(object sender, RoutedEventArgs e)
    {
        ApplyClipPanelState();
    }

    private void ApplyClipPanelState()
    {
        if (ClipTimingPanel == null || ClipDownloadRadio == null)
            return;

        var isClip = ClipDownloadRadio.IsChecked == true;
        ClipTimingPanel.IsEnabled = isClip;
        ClipTimingPanel.Opacity = isClip ? 1.0 : 0.5;
    }

    private void StartDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (FullDownloadRadio.IsChecked == true)
            {
                Request = new StreamDownloadRequest { Mode = StreamDownloadMode.Full };
                DialogResult = true;
                Close();
                return;
            }

            if (!TryParseTimeInput(StartTimeTextBox.Text, out var start))
            {
                MessageBox.Show(
                    "Enter a valid start time.\n\nExamples: 00:01:30, 1:30, or 90",
                    "Invalid Start Time",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (!TryParseTimeInput(EndTimeTextBox.Text, out var end))
            {
                MessageBox.Show(
                    "Enter a valid end time.\n\nExamples: 00:02:15, 2:15, or 135",
                    "Invalid End Time",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (end <= start)
            {
                MessageBox.Show(
                    "End time must be after start time.",
                    "Invalid Clip Range",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            Request = new StreamDownloadRequest
            {
                Mode = StreamDownloadMode.Clip,
                ClipStart = start,
                ClipEnd = end
            };

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            DownloadDebugHelper.ShowError("Validating download options", ex);
        }
    }

    internal static bool TryParseTimeInput(string input, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var trimmed = input.Trim();

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
            seconds >= 0)
        {
            result = TimeSpan.FromSeconds(seconds);
            return true;
        }

        var match = TimeSpanRegex.Match(trimmed);
        if (!match.Success)
            return false;

        var hours = match.Groups["hours"].Success && int.TryParse(match.Groups["hours"].Value, out var h) ? h : 0;
        if (!int.TryParse(match.Groups["minutes"].Value, out var minutes))
            return false;
        if (!double.TryParse(match.Groups["seconds"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var secs))
            return false;

        result = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(secs);
        return result >= TimeSpan.Zero;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }
}
