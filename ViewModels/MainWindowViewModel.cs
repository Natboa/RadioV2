using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Threading;

namespace RadioV2.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private DispatcherTimer? _clockTimer;

    [ObservableProperty] private bool _isClockEnabled;
    [ObservableProperty] private string _currentTime = string.Empty;

    partial void OnIsClockEnabledChanged(bool value)
    {
        if (value) StartClock();
        else StopClock();
    }

    private void StartClock()
    {
        UpdateTime();
        if (_clockTimer == null)
        {
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _clockTimer.Tick += (_, _) => UpdateTime();
        }
        _clockTimer.Start();
    }

    private void StopClock()
    {
        _clockTimer?.Stop();
    }

    private void UpdateTime()
    {
        CurrentTime = DateTime.Now.ToString("HH:mm");
    }
}
