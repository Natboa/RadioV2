using Microsoft.Extensions.DependencyInjection;
using RadioV2.Helpers;
using RadioV2.Services;
using RadioV2.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace RadioV2;

public partial class MainWindow : FluentWindow
{
    private readonly MediaKeyHook _mediaKeyHook = new();
    private readonly TrayIconManager _trayIcon;
    private readonly NetworkMonitor _networkMonitor;
    private readonly ISnackbarService _snackbarService;
    private bool _isQuitting;

    public MainWindow(MainWindowViewModel viewModel, IServiceProvider serviceProvider, ISnackbarService snackbarService)
    {
        _snackbarService = snackbarService;

        DataContext = viewModel;
        InitializeComponent();

        // Wire snackbar presenter
        snackbarService.SetSnackbarPresenter(RootSnackbar);

        RootNavigation.SetServiceProvider(serviceProvider);
        RootNavigation.Navigate(typeof(Views.BrowsePage));

        var miniPlayer = serviceProvider.GetRequiredService<MiniPlayerViewModel>();
        MiniPlayerControl.DataContext = miniPlayer;

        // Media keys
        _mediaKeyHook.PlayPauseCommand = miniPlayer.PlayPauseCommand;
        _mediaKeyHook.StopCommand = miniPlayer.StopCommand;
        _mediaKeyHook.NextStationCommand = miniPlayer.NextStationCommand;
        _mediaKeyHook.PreviousStationCommand = miniPlayer.PreviousStationCommand;

        // System tray
        _trayIcon = new TrayIconManager(
            miniPlayer,
            showWindowAction: ShowWindow,
            quitAction: Quit);

        // Network monitor
        _networkMonitor = new NetworkMonitor();
        _networkMonitor.ConnectivityChanged += OnConnectivityChanged;

        // Playback error → snackbar
        var playerService = serviceProvider.GetRequiredService<IRadioPlayerService>();
        playerService.PlaybackError += (s, msg) =>
        {
            var stationName = miniPlayer.StationName;
            Dispatcher.Invoke(() =>
            {
                var message = string.IsNullOrEmpty(stationName)
                    ? "Stream error. The station may be offline."
                    : $"Could not play {stationName}. The stream may be offline.";
                _snackbarService.Show("Playback Error", message, ControlAppearance.Danger, null, TimeSpan.FromSeconds(5));
            });
        };

        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(_mediaKeyHook.WndProc);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isQuitting)
        {
            e.Cancel = true;
            HideWindow();
            return;
        }
        _trayIcon.Dispose();
        _networkMonitor.Dispose();
        base.OnClosing(e);
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Activate();
    }

    private void HideWindow()
    {
        Hide();
        ShowInTaskbar = false;
    }

    private void Quit()
    {
        _isQuitting = true;
        Close();
        Application.Current.Shutdown();
    }

    private void OnConnectivityChanged(object? sender, bool isOnline)
    {
        if (!isOnline)
            _snackbarService.Show("No Connection", "No internet connection — playback unavailable.",
                ControlAppearance.Caution, null, TimeSpan.Zero);
        else
            _snackbarService.Show("Connection Restored", "Internet connection is back.",
                ControlAppearance.Success, null, TimeSpan.FromSeconds(3));
    }
}
