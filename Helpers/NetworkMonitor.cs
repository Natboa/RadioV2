using System.Net.NetworkInformation;
using System.Windows;

namespace RadioV2.Helpers;

public class NetworkMonitor : IDisposable
{
    public bool IsOnline { get; private set; }
    public event EventHandler<bool>? ConnectivityChanged;

    public NetworkMonitor()
    {
        IsOnline = NetworkInterface.GetIsNetworkAvailable();
        NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
    }

    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        IsOnline = e.IsAvailable;
        System.Windows.Application.Current?.Dispatcher.Invoke(() => ConnectivityChanged?.Invoke(this, IsOnline));
    }

    public void Dispose() => NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
}
