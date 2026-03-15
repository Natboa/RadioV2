using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioV2.Helpers;
using RadioV2.Models;
using RadioV2.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace RadioV2.ViewModels;

public partial class FavouritesViewModel : ObservableObject
{
    private readonly IStationService _stationService;
    private readonly IStationLogoCache _logoCache;
    private readonly MiniPlayerViewModel _miniPlayer;

    public FavouritesViewModel(IStationService stationService, IStationLogoCache logoCache, MiniPlayerViewModel miniPlayer, NetworkMonitor networkMonitor)
    {
        _stationService = stationService;
        _logoCache = logoCache;
        _miniPlayer = miniPlayer;
        networkMonitor.ConnectivityChanged += (_, isOnline) =>
        {
            if (isOnline)
                foreach (var s in Favourites) s.NotifyLogoChanged();
        };
    }

    [ObservableProperty]
    private ObservableCollection<Station> _favourites = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private int _favouriteCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading = true;

    public bool IsEmpty => !IsLoading && FavouriteCount == 0;

    private bool _hasLoaded;

    [RelayCommand]
    public async Task LoadFavouritesAsync(bool force = false)
    {
        if (_hasLoaded && !force) return;
        _hasLoaded = true;
        IsLoading = true;
        var list = await Task.Run(() => _stationService.GetFavouritesAsync());
        Favourites.Clear();
        foreach (var s in list)
        {
            s.CachedLogoPath = _logoCache.GetCachedPath(s.Id);
            Favourites.Add(s);

            // Backfill cache for stations favourited before this feature existed
            if (s.CachedLogoPath is null && !string.IsNullOrEmpty(s.LogoUrl))
            {
                var station = s;
                _ = Task.Run(async () =>
                {
                    await _logoCache.DownloadAsync(station.Id, station.LogoUrl!);
                    var path = _logoCache.GetCachedPath(station.Id);
                    if (path is not null)
                        await Application.Current.Dispatcher.InvokeAsync(() => station.CachedLogoPath = path);
                });
            }
        }
        FavouriteCount = Favourites.Count;
        IsLoading = false;
    }

    [RelayCommand]
    private async Task ToggleFavourite(Station station)
    {
        await _stationService.ToggleFavouriteAsync(station.Id);
        Favourites.Remove(station);
        FavouriteCount = Favourites.Count;
    }

    [RelayCommand]
    private void PlayStation(Station station) => _miniPlayer.SetStation(station, Favourites);
}
