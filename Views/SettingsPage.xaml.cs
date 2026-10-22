using RadioV2.ViewModels;
using Serilog;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;

namespace RadioV2.Views;

public partial class SettingsPage : Page
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await viewModel.LoadAsync();
    }

    private void CopyrightButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://github.com/Natboa/RadioV2/blob/main/Legal/DMCA.txt") { UseShellExecute = true });
    }

    /// <summary>
    /// The logs are worth reading now — startup phase timings, playback transitions
    /// and VLC's own output — but nothing in the app pointed at them.
    /// </summary>
    private void OpenLogFolderButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RadioV2", "logs");

        try
        {
            Directory.CreateDirectory(logDir); // nothing has been logged yet on a first run
            Process.Start(new ProcessStartInfo(logDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "settings: could not open the log folder {Path}", logDir);
        }
    }
}
