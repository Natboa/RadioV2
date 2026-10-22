using Microsoft.Extensions.DependencyInjection;
using RadioV2.ViewModels;
using RadioV2.Views;

namespace RadioV2.Services;

/// <summary>
/// View model and page registrations, extracted from <c>App</c> so their lifetimes
/// can be asserted in tests.
/// </summary>
public static class ServiceRegistration
{
    /// <summary>
    /// Registers the view models and pages.
    ///
    /// <para>
    /// Browse and Favourites are singletons deliberately. As transients they
    /// subscribed to the singleton <c>NetworkMonitor.ConnectivityChanged</c> and
    /// <c>MiniPlayerViewModel.StationStarted</c> and never unsubscribed, so every
    /// navigation leaked a view model and its entire station list — and every leaked
    /// BrowseViewModel went on handling StationStarted and rewriting
    /// recent_stations.json.
    /// </para>
    ///
    /// <para>
    /// Their pages are singletons for the same reason in reverse: BrowsePage
    /// subscribes to its view model's PropertyChanged, so a transient page attached
    /// to a singleton view model would be the thing that leaked instead. One page
    /// instance, one subscription. Settings is transient because it subscribes to
    /// nothing that outlives it.
    /// </para>
    /// </summary>
    public static IServiceCollection AddRadioV2ViewModels(this IServiceCollection services)
    {
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MiniPlayerViewModel>();
        services.AddSingleton<DiscoverViewModel>();
        services.AddSingleton<BrowseViewModel>();
        services.AddSingleton<FavouritesViewModel>();
        services.AddTransient<SettingsViewModel>();

        services.AddSingleton<DiscoverPage>();
        services.AddSingleton<BrowsePage>();
        services.AddSingleton<FavouritesPage>();
        services.AddTransient<SettingsPage>();

        return services;
    }
}
