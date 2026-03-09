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

    public MainWindow(MainWindowViewModel viewModel, IServiceProvider serviceProvider, ISnackbarService snackbarService)
    {
        _snackbarService = snackbarService;

        DataContext = viewModel;
        InitializeComponent();

        // Wire snackbar presenter
        snackbarService.SetSnackbarPresenter(RootSnackbar);

        RootNavigation.SetServiceProvider(serviceProvider);

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
        RootNavigation.Navigate(typeof(Views.FavouritesPage));

        UpdatePaneToggleButton();
        DependencyPropertyDescriptor
            .FromProperty(Wpf.Ui.Controls.NavigationView.IsPaneOpenProperty, typeof(Wpf.Ui.Controls.NavigationView))
            ?.AddValueChanged(RootNavigation, (_, _) => UpdatePaneToggleButton());
    }

    private void UpdatePaneToggleButton()
    {
        bool isOpen = RootNavigation.IsPaneOpen;

        PaneToggleIcon.Symbol = isOpen
            ? SymbolRegular.FullScreenMinimize20
            : SymbolRegular.FullScreenMaximize20;

        double openLen = RootNavigation.OpenPaneLength;
        double compactLen = RootNavigation.CompactPaneLength;
        double btnW = PaneToggleBtn.Width;

        // When open: button sits at the right edge of the expanded pane.
        // When compact: button sits at the right edge of the compact strip.
        PaneToggleBtn.Margin = isOpen
            ? new Thickness(openLen - btnW - 4, 8, 0, 0)
            : new Thickness(compactLen - btnW - 4, 8, 0, 0);
    }

    private void OnPaneToggleClick(object sender, RoutedEventArgs e)
    {
        RootNavigation.IsPaneOpen = !RootNavigation.IsPaneOpen;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _trayIcon.Dispose();
        _networkMonitor.Dispose();
        base.OnClosing(e);
        Application.Current.Shutdown();
    }

    private void OnHideToTrayClick(object sender, RoutedEventArgs e)
    {
        Hide();
        ShowInTaskbar = false;
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Activate();
    }

    private void Quit()
    {
        Close();
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
