using Microsoft.Extensions.DependencyInjection;
using RadioV2.ViewModels;
using Wpf.Ui.Controls;

namespace RadioV2;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainWindowViewModel viewModel, IServiceProvider serviceProvider)
    {
        DataContext = viewModel;
        InitializeComponent();
        RootNavigation.SetServiceProvider(serviceProvider);
        RootNavigation.Navigate(typeof(Views.BrowsePage));
        MiniPlayerControl.DataContext = serviceProvider.GetRequiredService<MiniPlayerViewModel>();
    }
}
