using Microsoft.Extensions.DependencyInjection;
using RadioV2.Helpers;
using RadioV2.Services;
using RadioV2.ViewModels;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Automation.Peers;
using RadioV2.Models;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace RadioV2;

public partial class MainWindow : FluentWindow
{
    private static readonly string _placementFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RadioV2", "window-placement.json");

    private record WindowPlacement(double Left, double Top, double Width, double Height, WindowState State);

    private const int WM_POWERBROADCAST = 0x0218;
    private const int PBT_APMSUSPEND = 0x0004;
    private const int PBT_APMRESUMEAUTOMATIC = 0x0012;
    private const int PBT_APMRESUMESUSPEND = 0x0007;

    private readonly MediaKeyHook _mediaKeyHook = new();
    private readonly TrayIconManager _trayIcon;
    private readonly NetworkMonitor _networkMonitor;
    private readonly ISnackbarService _snackbarService;
    private readonly UpdateCheckerService _updateChecker;
    private readonly MiniPlayerViewModel _miniPlayer;
    private string? _latestVersion;
    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private Station? _noticeStation;
    private readonly TransientMessageGate _noticeGate = new();

    public MainWindow(MainWindowViewModel viewModel, IServiceProvider serviceProvider, ISnackbarService snackbarService, UpdateCheckerService updateChecker, NetworkMonitor networkMonitor)
    {
        _snackbarService = snackbarService;
        _updateChecker = updateChecker;

        DataContext = viewModel;
        InitializeComponent();

        // Wire snackbar presenter
        snackbarService.SetSnackbarPresenter(RootSnackbar);

        RootNavigation.SetServiceProvider(serviceProvider);

        _miniPlayer = serviceProvider.GetRequiredService<MiniPlayerViewModel>();
        MiniPlayerControl.DataContext = _miniPlayer;
        _noticeTimer.Tick += (_, _) =>
        {
            if (!PlaybackNotice.IsMouseOver && !PlaybackNotice.IsKeyboardFocusWithin) HidePlaybackNotice();
        };
        _miniPlayer.StationStarted += (_, _) => { HidePlaybackNotice(); _noticeGate.Reset(); };

        // Media keys
        _mediaKeyHook.PlayPauseCommand = _miniPlayer.PlayPauseCommand;
        _mediaKeyHook.StopCommand = _miniPlayer.StopCommand;
        _mediaKeyHook.NextStationCommand = _miniPlayer.NextStationCommand;
        _mediaKeyHook.PreviousStationCommand = _miniPlayer.PreviousStationCommand;

        // System tray
        _trayIcon = new TrayIconManager(
            _miniPlayer,
            showWindowAction: ShowWindow,
            quitAction: Quit);

        // Network monitor
        _networkMonitor = networkMonitor;
        _networkMonitor.ConnectivityChanged += OnConnectivityChanged;

        // Playback error → snackbar
        var playerService = serviceProvider.GetRequiredService<IRadioPlayerService>();
        playerService.PlaybackError += (s, msg) =>
        {
            var station = _miniPlayer.CurrentStation;
            Dispatcher.BeginInvoke(() =>
            {
                if (msg == "Audio engine unavailable")
                {
                    HidePlaybackNotice();
                    _snackbarService.Show("Audio engine unavailable", "Restart RadioV2 to try loading the audio engine again.",
                        ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(8));
                    return;
                }
                if (station is null || !ReferenceEquals(station, _miniPlayer.CurrentStation)
                    || !_noticeGate.ShouldShow(station.Id)) return;
                _noticeStation = station;
                NoticeMessage.Text = $"{station.Name} is unavailable right now. Try again or choose another station.";
                PlaybackNotice.Visibility = Visibility.Visible;
                if (MotionPreferences.Enabled && IsActive)
                    PlaybackNotice.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)) { FillBehavior = FillBehavior.Stop });
                _noticeTimer.Stop();
                _noticeTimer.Start();
                UIElementAutomationPeer.CreatePeerForElement(NoticeMessage)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            });
        };
        playerService.PlaybackStarted += (_, _) => Dispatcher.BeginInvoke(HidePlaybackNotice);

        SourceInitialized += (_, _) => RestoreWindowPlacement();
        Loaded += OnWindowLoaded;
    }

    private void HidePlaybackNotice()
    {
        _noticeTimer.Stop();
        PlaybackNotice.BeginAnimation(OpacityProperty, null);
        PlaybackNotice.Visibility = Visibility.Collapsed;
        _noticeStation = null;
    }

    private void OnRetryStation(object sender, RoutedEventArgs e)
    {
        var station = _noticeStation;
        HidePlaybackNotice();
        _noticeGate.Reset();
        if (station is not null && ReferenceEquals(station, _miniPlayer.CurrentStation))
            _miniPlayer.PlayPauseCommand.Execute(null);
    }

    private void OnDismissNotice(object sender, RoutedEventArgs e) => HidePlaybackNotice();
    private void OnNoticeKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape) { HidePlaybackNotice(); e.Handled = true; }
    }

    private void RestoreWindowPlacement()
    {
        try
        {
            if (File.Exists(_placementFile))
            {
                var json = File.ReadAllText(_placementFile);
                var p = JsonSerializer.Deserialize<WindowPlacement>(json);
                if (p is not null && IsPlacementOnScreen(p))
                {
                    Left = p.Left;
                    Top = p.Top;
                    Width = p.Width;
                    Height = p.Height;
                    WindowState = p.State;
                    return;
                }
            }
        }
        catch { /* fall through to center */ }

        CenterOnScreen();
    }

    private void SaveWindowPlacement()
    {
        try
        {
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            var p = new WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height, WindowState);
            Directory.CreateDirectory(Path.GetDirectoryName(_placementFile)!);
            File.WriteAllText(_placementFile, JsonSerializer.Serialize(p));
        }
        catch { }
    }

    private void CenterOnScreen()
    {
        var screen = SystemParameters.WorkArea;
        Left = screen.Left + (screen.Width - Width) / 2;
        Top = screen.Top + (screen.Height - Height) / 2;
    }

    private static bool IsPlacementOnScreen(WindowPlacement p)
    {
        var vLeft = SystemParameters.VirtualScreenLeft;
        var vTop = SystemParameters.VirtualScreenTop;
        var vRight = vLeft + SystemParameters.VirtualScreenWidth;
        var vBottom = vTop + SystemParameters.VirtualScreenHeight;
        // Ensure at least the title bar area is reachable
        return p.Left + p.Width > vLeft + 50
            && p.Left < vRight - 50
            && p.Top >= vTop
            && p.Top < vBottom - 50;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(_mediaKeyHook.WndProc);
        source?.AddHook(PowerWndProc);
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
        Process.Start(new ProcessStartInfo("https://github.com/Natboa/RadioV2/releases/latest") { UseShellExecute = true });
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
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(MotionPreferences.Enabled ? 100 : 0));
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
        _noticeTimer.Stop();
        SaveWindowPlacement();
        _mediaKeyHook.Dispose();
        _trayIcon.Dispose();
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

    private IntPtr PowerWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_POWERBROADCAST)
        {
            // Fire and forget, deliberately. A window procedure must not block, least
            // of all while Windows is trying to suspend the machine — the old code
            // called straight into VLC's synchronous Stop() from right here.
            int evt = wParam.ToInt32();
            if (evt == PBT_APMSUSPEND)
                _ = _miniPlayer.OnSleepingAsync();
            else if (evt == PBT_APMRESUMEAUTOMATIC || evt == PBT_APMRESUMESUSPEND)
                _ = _miniPlayer.OnWakingAsync();
        }
        return IntPtr.Zero;
    }

    private void OnConnectivityChanged(object? sender, bool isOnline)
    {
        if (!isOnline)
            _snackbarService.Show("No Connection", "No internet connection — playback unavailable.",
                ControlAppearance.Secondary, null, TimeSpan.FromSeconds(8));
        else
            _snackbarService.Show("Connection Restored", "Internet connection is back.",
                ControlAppearance.Secondary, null, TimeSpan.FromSeconds(3));
    }
}
