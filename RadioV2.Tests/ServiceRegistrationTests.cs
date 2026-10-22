using Microsoft.Extensions.DependencyInjection;
using RadioV2.Helpers;
using RadioV2.Models;
using RadioV2.Services;
using RadioV2.ViewModels;
using RadioV2.Views;
using Xunit;

namespace RadioV2.Tests;

/// <summary>
/// These lifetimes are load-bearing, not stylistic. Browse and Favourites subscribe
/// to events on singletons and never unsubscribe, so a transient registration leaks
/// one view model and its whole station list per navigation. Their pages subscribe
/// to the view models, so transient pages would leak for the mirror-image reason.
/// </summary>
public class ServiceRegistrationTests
{
    private static ServiceCollection Registered()
    {
        var services = new ServiceCollection();
        services.AddRadioV2ViewModels();
        return services;
    }

    [Theory]
    [InlineData(typeof(BrowseViewModel))]
    [InlineData(typeof(FavouritesViewModel))]
    [InlineData(typeof(DiscoverViewModel))]
    [InlineData(typeof(MiniPlayerViewModel))]
    [InlineData(typeof(MainWindowViewModel))]
    [InlineData(typeof(BrowsePage))]
    [InlineData(typeof(FavouritesPage))]
    [InlineData(typeof(DiscoverPage))]
    public void TypesThatSubscribeToSingletons_AreSingletons(Type type)
    {
        var descriptor = Assert.Single(Registered(), d => d.ServiceType == type);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Theory]
    [InlineData(typeof(SettingsViewModel))]
    [InlineData(typeof(SettingsPage))]
    public void SettingsStaysTransient_ItSubscribesToNothingThatOutlivesIt(Type type)
    {
        var descriptor = Assert.Single(Registered(), d => d.ServiceType == type);
        Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
    }

    /// <summary>
    /// The descriptor test above says what was registered; this one says what the
    /// container actually hands out, which is what decides whether a second
    /// subscription to the singleton event ever exists.
    /// </summary>
    [Fact]
    public void ResolvingBrowseRepeatedly_YieldsOneInstance()
    {
        using var provider = BuildProvider();

        var instances = Enumerable.Range(0, 10)
            .Select(_ => provider.GetRequiredService<BrowseViewModel>())
            .Distinct()
            .ToList();

        Assert.Single(instances);
    }

    [Fact]
    public void ResolvingFavouritesRepeatedly_YieldsOneInstance()
    {
        using var provider = BuildProvider();

        var instances = Enumerable.Range(0, 10)
            .Select(_ => provider.GetRequiredService<FavouritesViewModel>())
            .Distinct()
            .ToList();

        Assert.Single(instances);
    }

    /// <summary>
    /// Demonstrates the symptom the lifetime change exists to prevent: two view
    /// models sharing one mini player both handle StationStarted, so every leaked
    /// instance went on appending to the recents list and rewriting its file.
    /// </summary>
    [Fact]
    public void TwoBrowseInstances_WouldBothHandleStationStarted()
    {
        using var provider = BuildProvider();
        var miniPlayer = provider.GetRequiredService<MiniPlayerViewModel>();
        var stationService = provider.GetRequiredService<IStationService>();
        var monitor = provider.GetRequiredService<NetworkMonitor>();

        var first = provider.GetRequiredService<BrowseViewModel>();
        var second = new BrowseViewModel(stationService, miniPlayer, monitor); // what transient used to do

        miniPlayer.SetStation(new Station { Id = 7, Name = "S", StreamUrl = "http://x/7", GroupId = 1 });

        Assert.Contains(first.RecentStations, s => s.Id == 7);
        Assert.Contains(second.RecentStations, s => s.Id == 7);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRadioPlayerService, FakePlayerService>();
        services.AddSingleton<IStationService, FakeStationService>();
        services.AddSingleton<IStationLogoCache, FakeLogoCache>();
        services.AddSingleton<NetworkMonitor>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MiniPlayerViewModel>();
        services.AddSingleton<DiscoverViewModel>();
        services.AddSingleton<BrowseViewModel>();
        services.AddSingleton<FavouritesViewModel>();
        return services.BuildServiceProvider();
    }

    private sealed class FakeLogoCache : IStationLogoCache
    {
        public string? GetCachedPath(int stationId) => null;
        public Task DownloadAsync(int stationId, string logoUrl, CancellationToken ct = default) => Task.CompletedTask;
        public void Delete(int stationId) { }
    }
}
