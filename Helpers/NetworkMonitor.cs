using System.Net.NetworkInformation;
using System.Windows;

namespace RadioV2.Helpers;

public class NetworkMonitor : IDisposable
{
    private bool _isOnline;
    private int _checking; // Interlocked flag to prevent concurrent checks

    public bool IsOnline => _isOnline;
    public event EventHandler<bool>? ConnectivityChanged;

    public NetworkMonitor()
    {
        _isOnline = HasRealConnectivity();
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        // Avoid stacking checks if multiple events fire at once
        if (Interlocked.CompareExchange(ref _checking, 1, 0) != 0) return;

        Task.Run(async () =>
        {
            try
            {
                // Brief settle time — address changes can fire mid-transition
                await Task.Delay(1000);
                var isNowOnline = HasRealConnectivity();
                if (isNowOnline != _isOnline)
                {
                    _isOnline = isNowOnline;
                    Application.Current?.Dispatcher.BeginInvoke(
                        () => ConnectivityChanged?.Invoke(this, _isOnline));
                }
            }
            finally
            {
                Interlocked.Exchange(ref _checking, 0);
            }
        });
    }

    private static bool HasRealConnectivity()
    {
        if (!NetworkInterface.GetIsNetworkAvailable()) return false;
        try
        {
            using var ping = new Ping();
            var reply = ping.Send("8.8.8.8", 1500);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
    }
}
