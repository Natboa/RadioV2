using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioV2.Data;
using RadioV2.Helpers;
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

                // Services (registered in later steps)
                // services.AddSingleton<IRadioPlayerService, RadioPlayerService>();
                // services.AddScoped<IStationService, StationService>();

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

        // Read theme from DB, default to Dark
        var db = _host.Services.GetRequiredService<RadioDbContext>();
        var themeSetting = await db.Settings.FindAsync("Theme");
        ThemeHelper.ApplyTheme(themeSetting?.Value ?? "Dark");

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }
}
