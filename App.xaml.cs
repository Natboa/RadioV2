using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioV2.Data;
using RadioV2.ViewModels;
using System.IO;
using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace RadioV2;

public partial class App : Application
{
    private readonly IHost _host;

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

                // Main Window
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();

        ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica, true);

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }
}
