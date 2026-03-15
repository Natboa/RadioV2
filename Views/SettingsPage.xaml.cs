using RadioV2.ViewModels;
using System.Diagnostics;
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

    private void DonateButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://ko-fi.com/natboa") { UseShellExecute = true });
    }
}
