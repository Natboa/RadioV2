using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioV2.Data;
using RadioV2.Helpers;
using RadioV2.Models;
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

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception");

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                var appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RadioV2");
                Directory.CreateDirectory(appDataDir);

                var dataDir = Path.Combine(appDataDir, "Data");
                Directory.CreateDirectory(dataDir);

                // ── stations.db (installer-managed seed data) ──────────────────
                var stationsDbPath = Path.Combine(dataDir, "stations.db");
                var legacyDbPath   = Path.Combine(appDataDir, "radioapp_large_groups.db");

                if (!File.Exists(stationsDbPath))
                {
                    if (File.Exists(legacyDbPath))
                    {
                        // Migrate from old single-DB layout, then remove legacy file
                        // so future resets copy from the seed instead of stale legacy data
                        File.Copy(legacyDbPath, stationsDbPath);
                        File.Delete(legacyDbPath);
                    }
                    else
                    {
                        // Fresh install — copy seed from app directory
                        var seedPath = Path.Combine(AppContext.BaseDirectory, "Data", "stations.db");
                        if (File.Exists(seedPath))
                            File.Copy(seedPath, stationsDbPath);
                    }
                }

                services.AddDbContextFactory<StationsDbContext>(options =>
                    options.UseSqlite($"Data Source={stationsDbPath}"));

                // ── userdata.db (user-owned: favourites + settings) ────────────
                var userDbPath = Path.Combine(dataDir, "userdata.db");
                services.AddDbContextFactory<UserDbContext>(options =>
                    options.UseSqlite($"Data Source={userDbPath}"));

                // Helpers
                services.AddSingleton<NetworkMonitor>();

                // Services
                services.AddSingleton<IStationService, StationService>();
                services.AddSingleton<IRadioPlayerService, RadioPlayerService>();
                services.AddSingleton<IFavouritesIOService, FavouritesIOService>();
                services.AddSingleton<M3UParserService>();
                services.AddSingleton<ISnackbarService, SnackbarService>();
                services.AddSingleton<UpdateCheckerService>();

                // ViewModels
                services.AddSingleton<MainWindowViewModel>();
                services.AddTransient<BrowseViewModel>();
                services.AddSingleton<DiscoverViewModel>();
                services.AddTransient<FavouritesViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddSingleton<MiniPlayerViewModel>();

                // Pages
                services.AddTransient<BrowsePage>();
                services.AddSingleton<DiscoverPage>();
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

        // Initialise userdata.db (creates tables on first run)
        var userDbFactory = _host.Services.GetRequiredService<IDbContextFactory<UserDbContext>>();
        using (var userDb = userDbFactory.CreateDbContext())
        {
            await userDb.Database.EnsureCreatedAsync();
            await MigrateLegacyFavouritesAsync(userDb);
        }

        // Initialise stations.db schema (safe on every launch)
        var stationsDbFactory = _host.Services.GetRequiredService<IDbContextFactory<StationsDbContext>>();
        using (var stationsDb = stationsDbFactory.CreateDbContext())
            await DatabaseInitService.InitialiseAsync(stationsDb);

        await RestoreSessionAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        // Pre-warm DiscoverViewModel and image cache
        _ = _host.Services.GetRequiredService<DiscoverViewModel>().LoadCategoriesAsync();

        var svcForImages = _host.Services.GetRequiredService<IStationService>();
        _ = Task.Run(async () =>
        {
            var categories = await svcForImages.GetCategoriesWithGroupsAsync();
            foreach (var cat in categories)
                foreach (var g in cat.Groups)
                    GroupImageHelper.GetImage(g.Name);
        });

        // Pre-compile EF query shapes
        var svc = _host.Services.GetRequiredService<IStationService>();
        _ = Task.Run(() => svc.GetStationsByGroupAsync(0, 0, 1));
        _ = Task.Run(() => svc.GetFeaturedStationsByGroupAsync(0));

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

    /// <summary>
    /// One-time migration: if the legacy single-DB had IsFavorite=1 rows and userdata.db
    /// was just created empty, port those StationIds into UserDbContext.Favourites.
    /// Uses SQLite ATTACH to avoid opening RadioDbContext.
    /// </summary>
    private async Task MigrateLegacyFavouritesAsync(UserDbContext userDb)
    {
        if (await userDb.Favourites.AnyAsync()) return;

        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RadioV2");
        var legacyDbPath = Path.Combine(appDataDir, "radioapp_large_groups.db");
        if (!File.Exists(legacyDbPath)) return;

        try
        {
            var conn = userDb.Database.GetDbConnection();
            await conn.OpenAsync();

            using var attachCmd = conn.CreateCommand();
            attachCmd.CommandText = $"ATTACH DATABASE '{legacyDbPath.Replace("'", "''")}' AS legacy";
            await attachCmd.ExecuteNonQueryAsync();

            var favIds = new List<int>();
            using (var selectCmd = conn.CreateCommand())
            {
                selectCmd.CommandText = "SELECT Id FROM legacy.Stations WHERE IsFavorite = 1";
                using var reader = await selectCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    favIds.Add(reader.GetInt32(0));
            }

            if (favIds.Count > 0)
            {
                foreach (var id in favIds)
                    userDb.Favourites.Add(new Favourite { StationId = id });
                await userDb.SaveChangesAsync();
                Log.Information("Migrated {Count} favourites from legacy database", favIds.Count);
            }

            using var detachCmd = conn.CreateCommand();
            detachCmd.CommandText = "DETACH DATABASE legacy";
            await detachCmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not migrate favourites from legacy database — skipping");
        }
    }

    private async Task RestoreSessionAsync()
    {
        var stationService = _host.Services.GetRequiredService<IStationService>();
        var miniPlayer = _host.Services.GetRequiredService<MiniPlayerViewModel>();

        // Theme
        var theme = await stationService.GetSettingAsync("Theme") ?? "Dark";
        ThemeHelper.ApplyTheme(theme);

        // Clock
        var mainVm = _host.Services.GetRequiredService<MainWindowViewModel>();
        mainVm.IsClockEnabled = (await stationService.GetSettingAsync("ClockEnabled")) == "true";

        // Volume
        if (int.TryParse(await stationService.GetSettingAsync("Volume"), out var volume))
            miniPlayer.Volume = volume;

        // Last played station — only restore if it is a favourite (display only, no auto-play)
        var lastIdStr = await stationService.GetSettingAsync("LastPlayedStationId");
        if (int.TryParse(lastIdStr, out var lastId) && lastId > 0)
        {
            var userFactory = _host.Services.GetRequiredService<IDbContextFactory<UserDbContext>>();
            using var userDb = userFactory.CreateDbContext();

            if (await userDb.Favourites.AnyAsync(f => f.StationId == lastId))
            {
                var stationsFactory = _host.Services.GetRequiredService<IDbContextFactory<StationsDbContext>>();
                using var stationsDb = stationsFactory.CreateDbContext();
                var station = await stationsDb.Stations
                    .Include(s => s.Group)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == lastId);

                if (station != null)
                {
                    station.IsFavorite = true;
                    miniPlayer.RestoreStation(station);
                }
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
        e.Handled = true;
    }
}
