using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioV2.Data;
using RadioV2.Helpers;
using RadioV2.Services;
using RadioV2.ViewModels;
using RadioV2.Views;
using Serilog;
using Wpf.Ui;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace RadioV2;

public partial class App : Application
{
    private readonly IHost _host;
    private Mutex? _mutex;

    public App()
    {
        // Configure Serilog before anything else
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RadioV2", "logs", "log-.txt");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();

        // Catch unhandled exceptions on any thread
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception");

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Database
                var dbPath = Path.Combine(AppContext.BaseDirectory, "Data", "radioapp_large_groups.db");
                services.AddDbContextFactory<RadioDbContext>(options =>
                    options.UseSqlite($"Data Source={dbPath}"));

                // Services
                services.AddSingleton<IStationService, StationService>();
                services.AddSingleton<IRadioPlayerService, RadioPlayerService>();
                services.AddScoped<IFavouritesIOService, FavouritesIOService>();
                services.AddScoped<M3UParserService>();
                services.AddSingleton<ISnackbarService, SnackbarService>();

                // ViewModels
                services.AddSingleton<MainWindowViewModel>();
                services.AddTransient<BrowseViewModel>();
                services.AddSingleton<DiscoverViewModel>();
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
        bool isNew;
        try
        {
            _mutex = new Mutex(true, "RadioV2_SingleInstance", out isNew);
        }
        catch (AbandonedMutexException)
        {
            // Previous instance was killed (e.g. by dotnet watch) — we now own the mutex
            isNew = true;
        }
        if (!isNew)
        {
            MessageBox.Show("RadioV2 is already running.", "RadioV2", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Log.Information("RadioV2 starting up");

        await _host.StartAsync();

        // Apply schema additions and seed category data (safe on every launch)
        var dbFactory = _host.Services.GetRequiredService<IDbContextFactory<RadioDbContext>>();
        using (var db = dbFactory.CreateDbContext())
            await DatabaseInitService.InitialiseAsync(db);

        await RestoreSessionAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        Log.Information("RadioV2 shutting down");
        await SaveSessionAsync();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private async Task RestoreSessionAsync()
    {
        var stationService = _host.Services.GetRequiredService<IStationService>();
        var miniPlayer = _host.Services.GetRequiredService<MiniPlayerViewModel>();

        // Theme
        var theme = await stationService.GetSettingAsync("Theme") ?? "Dark";
        ThemeHelper.ApplyTheme(theme);

        // Volume
        if (int.TryParse(await stationService.GetSettingAsync("Volume"), out var volume))
            miniPlayer.Volume = volume;

        // Last played station (restore display info — do NOT auto-play)
        var lastIdStr = await stationService.GetSettingAsync("LastPlayedStationId");
        if (int.TryParse(lastIdStr, out var lastId) && lastId > 0)
        {
            var dbFactory = _host.Services.GetRequiredService<IDbContextFactory<RadioDbContext>>();
            using var db = dbFactory.CreateDbContext();
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
        var stationService = _host.Services.GetRequiredService<IStationService>();
        var miniPlayer = _host.Services.GetRequiredService<MiniPlayerViewModel>();

        await stationService.SetSettingAsync("Volume", miniPlayer.Volume.ToString());
        if (miniPlayer.CurrentStation != null)
            await stationService.SetSettingAsync("LastPlayedStationId", miniPlayer.CurrentStation.Id.ToString());
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled dispatcher exception");
        e.Handled = true; // prevent crash; let the app keep running
    }
}
