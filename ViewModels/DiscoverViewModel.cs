using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioV2.Models;
using RadioV2.Services;
using System.Collections.ObjectModel;

namespace RadioV2.ViewModels;

public partial class DiscoverViewModel : ObservableObject
{
    private readonly IStationService _stationService;
    private int _groupSkip;
    private CancellationTokenSource _searchCts = new();

    public DiscoverViewModel(IStationService stationService)
    {
        _stationService = stationService;
    }

    [ObservableProperty] private ObservableCollection<GroupWithCount> _groups = [];
    [ObservableProperty] private GroupWithCount? _selectedGroup;
    [ObservableProperty] private ObservableCollection<Station> _groupStations = [];
    [ObservableProperty] private string _groupSearchQuery = string.Empty;
    [ObservableProperty] private bool _isGroupView;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasMoreItems = true;

    partial void OnGroupSearchQueryChanged(string value)
    {
        if (!IsGroupView || SelectedGroup is null) return;
        _searchCts.Cancel();
        _searchCts = new CancellationTokenSource();
        _ = DebounceGroupSearchAsync(_searchCts.Token);
    }

    private async Task DebounceGroupSearchAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(300, ct);
            GroupStations.Clear();
            _groupSkip = 0;
            HasMoreItems = true;
            await LoadMoreGroupStationsAsync(ct);
        }
        catch (OperationCanceledException) { }
    }

    [RelayCommand]
    public async Task LoadGroupsAsync(CancellationToken ct = default)
    {
        if (Groups.Count > 0) return;
        IsLoading = true;
        try
        {
            var groups = await _stationService.GetGroupsWithCountsAsync(ct);
            foreach (var g in groups) Groups.Add(g);
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task SelectGroupAsync(GroupWithCount group)
    {
        SelectedGroup = group;
        GroupStations.Clear();
        GroupSearchQuery = string.Empty;
        _groupSkip = 0;
        HasMoreItems = true;
        IsGroupView = true;
        await LoadMoreGroupStationsAsync();
    }

    [RelayCommand]
    public async Task LoadMoreGroupStationsAsync(CancellationToken ct = default)
    {
        if (IsLoading || !HasMoreItems || SelectedGroup is null) return;
        IsLoading = true;
        try
        {
            var query = GroupSearchQuery.Length >= 2 ? GroupSearchQuery : null;
            var batch = await _stationService.GetStationsByGroupAsync(SelectedGroup.Id, _groupSkip, 50, query, ct);
            foreach (var s in batch) GroupStations.Add(s);
            _groupSkip += batch.Count;
            HasMoreItems = batch.Count == 50;
        }
        catch (OperationCanceledException) { }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void BackToGroups()
    {
        IsGroupView = false;
        SelectedGroup = null;
        GroupStations.Clear();
    }

    [RelayCommand]
    private void PlayStation(Station station) { /* wired in M3 */ }

    [RelayCommand]
    private async Task ToggleFavourite(Station station)
    {
        await _stationService.ToggleFavouriteAsync(station.Id);
        station.IsFavorite = !station.IsFavorite;
        var idx = GroupStations.IndexOf(station);
        if (idx >= 0) { GroupStations[idx] = station; }
    }
}
