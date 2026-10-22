using System.Net.Http;
using System.Net.NetworkInformation;
using System.Windows;
using Serilog;

namespace RadioV2.Helpers;

public class NetworkMonitor : IDisposable
{
    // Short timeouts throughout: this runs during startup and on every network
    // change, and a slow answer is worse than a provisional one.
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };

    private volatile bool _isOnline = true;
    private int _checking; // Interlocked flag to prevent concurrent checks

    public bool IsOnline => _isOnline;
    public event EventHandler<bool>? ConnectivityChanged;

    public NetworkMonitor()
    {
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;

        // Starts optimistic and corrects itself. The previous version ran a blocking
        // 1500 ms ping here, and this type is constructed inside MainWindow's
        // constructor, so that cost landed squarely on the startup path.
        _ = Task.Run(async () =>
        {
            // Let DI finish building the graph so subscribers are attached before the
            // first result can raise ConnectivityChanged.
            await Task.Delay(500);
            await CheckAsync();
        });
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => _ = CheckAsync(settleDelayMs: 1000);

    private async Task CheckAsync(int settleDelayMs = 0)
    {
        // Avoid stacking checks if multiple events fire at once
        if (Interlocked.CompareExchange(ref _checking, 1, 0) != 0) return;

        try
        {
            if (settleDelayMs > 0)
                await Task.Delay(settleDelayMs); // address changes can fire mid-transition

            var isNowOnline = await HasRealConnectivityAsync();
            if (isNowOnline == _isOnline) return;

            _isOnline = isNowOnline;
            Log.Information("network: {State}", isNowOnline ? "online" : "offline");
            Application.Current?.Dispatcher.BeginInvoke(
                () => ConnectivityChanged?.Invoke(this, isNowOnline));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "network: connectivity check failed");
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    private static async Task<bool> HasRealConnectivityAsync()
    {
        if (!NetworkInterface.GetIsNetworkAvailable()) return false;

        if (await TryPingAsync()) return true;

        // A blocked ping is not proof of no internet. Plenty of corporate, hotel and
        // mobile networks filter ICMP, and treating that as offline used to disable
        // auto-reconnect for the whole session.
        return await TryHttpAsync();
    }

    private static async Task<bool> TryPingAsync()
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync("8.8.8.8", 1500);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> TryHttpAsync()
    {
        try
        {
            // The standard captive-portal probe: 204 with an empty body. A portal
            // intercepting the request answers 200 with a login page, which is
            // correctly treated as "no real internet".
            using var response = await _http.GetAsync(
                "http://www.gstatic.com/generate_204", HttpCompletionOption.ResponseHeadersRead);
            return (int)response.StatusCode == 204;
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
