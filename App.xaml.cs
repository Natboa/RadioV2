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
    private bool _ownsMutex;

    public App()
    {
        // Configure Serilog before anything else
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RadioV2", "logs", "log-.txt");

        // ThreadId is in the template deliberately: whether a call ran on the UI thread
        // or a worker is the single most useful thing to know when playback hangs.
        var logConfig = new LoggerConfiguration()
            .Enrich.WithThreadId()
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] [T{ThreadId,3}] {Message:lj}{NewLine}{Exception}");
#if DEBUG
        logConfig = logConfig.MinimumLevel.Debug();
#else
        logConfig = logConfig.MinimumLevel.Information();
#endif
        Log.Logger = logConfig.CreateLogger();

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
                services.AddSingleton<IStationLogoCache, StationLogoCache>();
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
            // This instance never acquired the mutex, so OnExit must not release it.
            MessageBox.Show("RadioV2 is already running.", "RadioV2", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _ownsMutex = true;

        // Phase timings are the acceptance measure for the startup work; cold start was
        // 27.7 s on v1.0.6 and these lines are how we find out where it went.
        var startupTimer = System.Diagnostics.Stopwatch.StartNew();
        void Phase(string name) =>
            Log.Information("startup: {Phase} at {Elapsed} ms", name, startupTimer.ElapsedMilliseconds);

        Log.Information("RadioV2 {Version} starting up",
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown");

        await _host.StartAsync();
        Phase("host started");

        // userdata.db must be ready before we read settings (fast — creates empty schema)
        var userDbFactory = _host.Services.GetRequiredService<IDbContextFactory<UserDbContext>>();
        using (var userDb = userDbFactory.CreateDbContext())
        {
            await userDb.Database.EnsureCreatedAsync();
            await MigrateLegacyFavouritesAsync(userDb);
        }
        Phase("userdata ready");

        // Apply theme before showing the window to avoid a flash
        var stationService = _host.Services.GetRequiredService<IStationService>();
        var theme = await stationService.GetSettingAsync("Theme") ?? "Dark";
        ThemeHelper.ApplyTheme(theme);
        Phase("theme applied");

        // Constructing MainWindow pulls in MiniPlayerViewModel -> RadioPlayerService
        // (LibVLC native init) and NetworkMonitor, so this phase is where the
        // remaining UI-thread startup cost hides.
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        Phase("main window constructed");
        mainWindow.Show();
        Phase("window shown");

        // Heavy stations.db init, session restore, and pre-warm run in the background
        // so the window appears immediately instead of waiting on the 80 MB database.
        _ = Task.Run(async () =>
        {
            try
            {
                // Initialise stations.db schema (safe on every launch)
                var stationsDbFactory = _host.Services.GetRequiredService<IDbContextFactory<StationsDbContext>>();
                using (var stationsDb = stationsDbFactory.CreateDbContext())
                    await DatabaseInitService.InitialiseAsync(stationsDb);
                Phase("schema init done");

                await RestoreSessionAsync(skipTheme: true);
                Phase("session restored");

                // Pre-warm DiscoverViewModel and image cache (must run after schema init)
                await Current.Dispatcher.InvokeAsync(() =>
                    _ = _host.Services.GetRequiredService<DiscoverViewModel>().LoadCategoriesAsync());

                var svcForImages = _host.Services.GetRequiredService<IStationService>();
                var categories = await svcForImages.GetCategoriesWithGroupsAsync();
                foreach (var cat in categories)
                    foreach (var g in cat.Groups)
                        GroupImageHelper.GetImage(g.Name);

                // Pre-compile EF query shapes
                var svc = _host.Services.GetRequiredService<IStationService>();
                await Task.WhenAll(
                    svc.GetStationsByGroupAsync(0, 0, 1),
                    svc.GetFeaturedStationsByGroupAsync(0));
                Phase("pre-warm done");
            }
            catch (Exception ex)
            {
                // Nothing here is fatal on its own, but a silent failure leaves the app
                // with no categories and no restored session and no way to find out why.
                Log.Error(ex, "Background startup initialisation failed");
            }
        });

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("RadioV2 shutting down");

        // Deliberately synchronous. As `async void` this returned at its first await
        // and WPF tore the dispatcher down before the continuation ran, so the session
        // save often never happened — which is why volume and last station only
        // sometimes came back.
        RunToCompletion(SaveSessionAsync, "save session");
        RunToCompletion(() => _host.StopAsync(), "stop host");

        _host.Dispose();

        if (_ownsMutex)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch (ApplicationException ex)
            {
                Log.Warning(ex, "Could not release the single-instance mutex");
            }
        }
        _mutex?.Dispose();

        base.OnExit(e);
        Log.CloseAndFlush();
    }

    /// <summary>
    /// Runs an async shutdown step to completion with a bounded wait.
    /// </summary>
    private static void RunToCompletion(Func<Task> work, string step)
    {
        try
        {
            // Task.Run so the awaits inside resume on the thread pool. Blocking the UI
            // thread on a continuation that itself needs the UI thread would deadlock
            // until the timeout — exactly the failure this method exists to remove.
            if (!Task.Run(work).Wait(TimeSpan.FromSeconds(5)))
                Log.Warning("shutdown: {Step} did not finish within 5 s", step);
            else
                Log.Debug("shutdown: {Step} done", step);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "shutdown: {Step} failed", step);
        }
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

    private async Task RestoreSessionAsync(bool skipTheme = false)
    {
        var stationService = _host.Services.GetRequiredService<IStationService>();
        var miniPlayer = _host.Services.GetRequiredService<MiniPlayerViewModel>();

        // Theme (already applied before Show() when skipTheme is true)
        if (!skipTheme)
        {
            var theme = await stationService.GetSettingAsync("Theme") ?? "Dark";
            ThemeHelper.ApplyTheme(theme);
        }

        // Clock
        var mainVm = _host.Services.GetRequiredService<MainWindowViewModel>();
        bool clockEnabled = (await stationService.GetSettingAsync("ClockEnabled")) == "true";

        // Volume
        int.TryParse(await stationService.GetSettingAsync("Volume"), out var volume);

        // Last played station — only restore if it is a favourite (display only, no auto-play)
        Models.Station? stationToRestore = null;
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
                    stationToRestore = station;
                }
            }
        }

        // Apply UI state on the dispatcher thread
        await Current.Dispatcher.InvokeAsync(() =>
        {
            mainVm.IsClockEnabled = clockEnabled;
            if (volume > 0) miniPlayer.Volume = volume;
            if (stationToRestore != null) miniPlayer.RestoreStation(stationToRestore);
        });
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
        TryShowErrorSnackbar(e.Exception);

#if DEBUG
        // Deliberately not handled in DEBUG: let the debugger break. Carrying on with
        // corrupted state is how a small bug becomes an unexplained crash later.
#else
        e.Handled = true;
#endif
    }

    /// <summary>
    /// Best-effort user-visible report. A swallowed exception the user never sees is
    /// how this app came to have no diagnostics at all.
    /// </summary>
    private void TryShowErrorSnackbar(Exception ex)
    {
        try
        {
            _host.Services.GetService<ISnackbarService>()?.Show(
                "Something went wrong",
                ex.Message,
                Wpf.Ui.Controls.ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(6));
        }
        catch
        {
            // The presenter may not be wired yet if this fired during startup.
            // Error reporting must never throw from inside the error handler.
        }
    }
}
