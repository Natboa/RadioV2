using Microsoft.Extensions.DependencyInjection;
using RadioV2.Helpers;
using RadioV2.Services;
using RadioV2.ViewModels;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace RadioV2;

public partial class MainWindow : FluentWindow
{
    private readonly MediaKeyHook _mediaKeyHook = new();
    private readonly TrayIconManager _trayIcon;
    private readonly NetworkMonitor _networkMonitor;
    private readonly ISnackbarService _snackbarService;
    private readonly UpdateCheckerService _updateChecker;
    private string? _latestVersion;

    public MainWindow(MainWindowViewModel viewModel, IServiceProvider serviceProvider, ISnackbarService snackbarService, UpdateCheckerService updateChecker)
    {
        _snackbarService = snackbarService;
        _updateChecker = updateChecker;

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
        var hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(_mediaKeyHook.WndProc);
        _mediaKeyHook.Register(hwnd);
        RootNavigation.Navigate(typeof(Views.FavouritesPage));

        UpdatePaneToggleButton();
        DependencyPropertyDescriptor
            .FromProperty(Wpf.Ui.Controls.NavigationView.IsPaneOpenProperty, typeof(Wpf.Ui.Controls.NavigationView))
            ?.AddValueChanged(RootNavigation, (_, _) => UpdatePaneToggleButton());

        // React to IsClockEnabled changes from settings page
        var vm = (MainWindowViewModel)DataContext;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsClockEnabled))
                UpdateClockPanel();
        };

        // Check for updates in the background — silently skipped if no internet
        _ = CheckForUpdateAsync();
    }

    private async Task CheckForUpdateAsync()
    {
        _latestVersion = await _updateChecker.CheckForUpdateAsync();
        if (_latestVersion is null) return;

        Dispatcher.Invoke(() =>
        {
            UpdateBannerText.Text = $"RadioV2 v{_latestVersion} is available.";
            UpdateBanner.Visibility = Visibility.Visible;
        });
    }

    private void OnDownloadUpdateClick(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://github.com/Natboa/radioV2/releases/latest") { UseShellExecute = true });
    }

    private void OnDismissUpdateBannerClick(object sender, RoutedEventArgs e)
    {
        UpdateBanner.Visibility = Visibility.Collapsed;
    }

    private void UpdatePaneToggleButton()
    {
        bool isOpen = RootNavigation.IsPaneOpen;

        PaneToggleIcon.Symbol = isOpen
            ? SymbolRegular.FullScreenMinimize20
            : SymbolRegular.FullScreenMaximize20;

        PaneToggleBtn.ToolTip = isOpen ? "Minimize" : "Expand";

        double openLen = RootNavigation.OpenPaneLength;
        double compactLen = RootNavigation.CompactPaneLength;
        double btnW = PaneToggleBtn.Width;

        // When open: float at the right edge of the expanded pane.
        // When compact: sit at the right edge of the compact icon column.
        // Guard against CompactPaneLength returning 0 (WPF-UI Left mode quirk).
        double compactRight = compactLen > btnW ? compactLen - btnW + 2 : 10;
        double leftMargin = isOpen
            ? openLen - btnW
            : compactRight;
        PaneToggleBtn.Margin = new Thickness(leftMargin, 8, 0, 0);

        // Clock panel fade on pane toggle
        var vm = (MainWindowViewModel)DataContext;
        if (!vm.IsClockEnabled) return;

        if (!isOpen && ClockPanel.Visibility == Visibility.Visible)
        {
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(100));
            fadeOut.Completed += (_, _) =>
            {
                if (!RootNavigation.IsPaneOpen)
                    ClockPanel.Visibility = Visibility.Collapsed;
            };
            ClockPanel.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
        else if (isOpen)
        {
            ClockPanel.BeginAnimation(UIElement.OpacityProperty, null);
            ClockPanel.Opacity = 1;
            ClockPanel.Visibility = Visibility.Visible;
        }
    }

    private void UpdateClockPanel()
    {
        var vm = (MainWindowViewModel)DataContext;
        if (!vm.IsClockEnabled)
        {
            ClockPanel.Visibility = Visibility.Collapsed;
            return;
        }
        // Show only when pane is open
        if (RootNavigation.IsPaneOpen)
        {
            ClockPanel.Opacity = 1;
            ClockPanel.Visibility = Visibility.Visible;
        }
    }

    private void OnPaneToggleClick(object sender, RoutedEventArgs e)
    {
        PaneToggleBtn.Visibility = Visibility.Collapsed;
        RootNavigation.IsPaneOpen = !RootNavigation.IsPaneOpen;
        Task.Delay(160).ContinueWith(_ =>
            Dispatcher.Invoke(() => PaneToggleBtn.Visibility = Visibility.Visible));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _mediaKeyHook.Dispose();
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
