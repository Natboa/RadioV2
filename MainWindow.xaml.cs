using Microsoft.Extensions.DependencyInjection;
using RadioV2.Helpers;
using RadioV2.ViewModels;
using System.Windows.Interop;
using Wpf.Ui.Controls;

namespace RadioV2;

public partial class MainWindow : FluentWindow
{
    private readonly MediaKeyHook _mediaKeyHook = new();

    public MainWindow(MainWindowViewModel viewModel, IServiceProvider serviceProvider)
    {
        DataContext = viewModel;
        InitializeComponent();
        RootNavigation.SetServiceProvider(serviceProvider);
        RootNavigation.Navigate(typeof(Views.BrowsePage));

        var miniPlayer = serviceProvider.GetRequiredService<MiniPlayerViewModel>();
        MiniPlayerControl.DataContext = miniPlayer;

        _mediaKeyHook.PlayPauseCommand = miniPlayer.PlayPauseCommand;
        _mediaKeyHook.StopCommand = miniPlayer.StopCommand;
        _mediaKeyHook.NextStationCommand = miniPlayer.NextStationCommand;
        _mediaKeyHook.PreviousStationCommand = miniPlayer.PreviousStationCommand;

        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(_mediaKeyHook.WndProc);
    }
}
