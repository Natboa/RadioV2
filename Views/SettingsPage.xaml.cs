using RadioV2.ViewModels;
using System.Windows.Controls;

namespace RadioV2.Views;

public partial class SettingsPage : Page
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
