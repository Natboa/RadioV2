using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioV2.Data;
using RadioV2.Helpers;
using RadioV2.Services;
using RadioV2.ViewModels;
using RadioV2.Views;
using System.IO;
using System.Threading;
using System.Windows;

namespace RadioV2;

public partial class App : Application
{
    private readonly IHost _host;
    private Mutex? _mutex;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Database
                var dbPath = Path.Combine(AppContext.BaseDirectory, "Data", "radioapp_large_groups.db");
                services.AddDbContext<RadioDbContext>(options =>
                    options.UseSqlite($"Data Source={dbPath}"));

                // Services
                services.AddScoped<IStationService, StationService>();
                services.AddSingleton<IRadioPlayerService, RadioPlayerService>();
                services.AddScoped<IFavouritesIOService, FavouritesIOService>();
                services.AddScoped<M3UParserService>();

                // ViewModels
                services.AddSingleton<MainWindowViewModel>();
                services.AddTransient<BrowseViewModel>();
                services.AddTransient<DiscoverViewModel>();
                services.AddTransient<FavouritesViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddSingleton<MiniPlayerViewModel>();

                // Pages
                services.AddTransient<BrowsePage>();
                services.AddTransient<DiscoverPage>();
                services.AddTransient<FavouritesPage>();
                services.AddTransient<SettingsPage>();

                // Main Window
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Single-instance enforcement
        _mutex = new Mutex(true, "RadioV2_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("RadioV2 is already running.", "RadioV2", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        await _host.StartAsync();

        // Restore session state (theme, volume, last station)
        await RestoreSessionAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await SaveSessionAsync();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }

    private async Task RestoreSessionAsync()
    {
        using var scope = _host.Services.CreateScope();
        var stationService = scope.ServiceProvider.GetRequiredService<IStationService>();
        var miniPlayer = _host.Services.GetRequiredService<MiniPlayerViewModel>();

        // Theme
        var theme = await stationService.GetSettingAsync("Theme") ?? "Dark";
        ThemeHelper.ApplyTheme(theme);

        // Volume
        if (int.TryParse(await stationService.GetSettingAsync("Volume"), out var volume))
            miniPlayer.Volume = volume;

        // Last played station (restore info but do NOT auto-play)
        var lastIdStr = await stationService.GetSettingAsync("LastPlayedStationId");
        if (int.TryParse(lastIdStr, out var lastId) && lastId > 0)
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
            var station = await db.Stations
                .Include(s => s.Group)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == lastId);

            if (station != null)
            {
                miniPlayer.StationName = station.Name;
                miniPlayer.StationLogoUrl = station.LogoUrl;
                miniPlayer.IsFavourite = station.IsFavorite;
            }
        }
    }

    private async Task SaveSessionAsync()
    {
        using var scope = _host.Services.CreateScope();
        var stationService = scope.ServiceProvider.GetRequiredService<IStationService>();
        var miniPlayer = _host.Services.GetRequiredService<MiniPlayerViewModel>();

        await stationService.SetSettingAsync("Volume", miniPlayer.Volume.ToString());

        if (miniPlayer.CurrentStation != null)
            await stationService.SetSettingAsync("LastPlayedStationId", miniPlayer.CurrentStation.Id.ToString());
    }
}
