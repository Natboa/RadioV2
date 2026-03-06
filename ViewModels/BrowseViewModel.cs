using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioV2.Models;
using RadioV2.Services;
using System.Collections.ObjectModel;

namespace RadioV2.ViewModels;

public partial class BrowseViewModel : ObservableObject
{
    private readonly IStationService _stationService;
    private readonly MiniPlayerViewModel _miniPlayer;
    private int _skip;
    private CancellationTokenSource _searchCts = new();

    public BrowseViewModel(IStationService stationService, MiniPlayerViewModel miniPlayer)
    {
        _stationService = stationService;
        _miniPlayer = miniPlayer;
    }

    [ObservableProperty] private ObservableCollection<Station> _stations = [];
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasMoreItems = true;

    partial void OnSearchQueryChanged(string value)
    {
        _searchCts.Cancel();
        _searchCts = new CancellationTokenSource();
        _ = DebounceSearchAsync(_searchCts.Token);
    }

    private async Task DebounceSearchAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(300, ct);
            Stations.Clear();
            _skip = 0;
            HasMoreItems = true;
            await LoadMoreAsync(ct);
        }
        catch (OperationCanceledException) { }
    }

    [RelayCommand]
    public async Task LoadMoreAsync(CancellationToken ct = default)
    {
        if (IsLoading || !HasMoreItems) return;
        IsLoading = true;
        try
        {
            var query = SearchQuery.Length >= 2 ? SearchQuery : null;
            var batch = await _stationService.GetStationsAsync(_skip, 50, query, ct);
            foreach (var s in batch) Stations.Add(s);
            _skip += batch.Count;
            HasMoreItems = batch.Count == 50;
        }
        catch (OperationCanceledException) { }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void PlayStation(Station station) => _miniPlayer.SetStation(station);

    [RelayCommand]
    private async Task ToggleFavourite(Station station)
    {
        await _stationService.ToggleFavouriteAsync(station.Id);
        station.IsFavorite = !station.IsFavorite;
        // Refresh the item in the list to update UI
        var idx = Stations.IndexOf(station);
        if (idx >= 0) { Stations[idx] = station; }
    }
}
