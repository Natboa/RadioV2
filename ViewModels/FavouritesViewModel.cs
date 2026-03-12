using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioV2.Models;
using RadioV2.Services;
using System.Collections.ObjectModel;

namespace RadioV2.ViewModels;

public partial class FavouritesViewModel : ObservableObject
{
    private readonly IStationService _stationService;
    private readonly MiniPlayerViewModel _miniPlayer;

    public FavouritesViewModel(IStationService stationService, MiniPlayerViewModel miniPlayer)
    {
        _stationService = stationService;
        _miniPlayer = miniPlayer;
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
        foreach (var s in list) Favourites.Add(s);
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
