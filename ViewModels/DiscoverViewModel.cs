using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioV2.Models;
using RadioV2.Services;
using System.Collections.ObjectModel;

namespace RadioV2.ViewModels;

public partial class DiscoverViewModel : ObservableObject
{
    private readonly IStationService _stationService;
    private readonly MiniPlayerViewModel _miniPlayer;
    private int _groupsSkip;
    private int _groupSkip;
    private bool _isLoadingStations;
    private CancellationTokenSource _searchCts = new();

    public DiscoverViewModel(IStationService stationService, MiniPlayerViewModel miniPlayer)
    {
        _stationService = stationService;
        _miniPlayer = miniPlayer;
    }

    [ObservableProperty] private ObservableCollection<GroupWithCount> _groups = [];
    [ObservableProperty] private GroupWithCount? _selectedGroup;
    [ObservableProperty] private ObservableCollection<Station> _groupStations = [];
    [ObservableProperty] private string _groupSearchQuery = string.Empty;
    [ObservableProperty] private bool _isGroupView;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadingSpinner))]
    private bool _isLoading;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadingSpinner))]
    private bool _isAtBottom = true;
    [ObservableProperty] private bool _hasMoreGroups = true;

    public bool ShowLoadingSpinner => IsLoading && IsAtBottom;
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
    public Task LoadGroupsAsync(CancellationToken ct = default)
    {
        if (Groups.Count > 0) return Task.CompletedTask;
        return LoadMoreGroupsAsync(ct);
    }

    [RelayCommand]
    public async Task LoadMoreGroupsAsync(CancellationToken ct = default)
    {
        if (IsLoading || !HasMoreGroups) return;
        IsLoading = true;
        try
        {
            var batch = await Task.Run(() => _stationService.GetGroupsWithCountsAsync(_groupsSkip, 30, ct), ct);
            foreach (var g in batch) Groups.Add(g);
            _groupsSkip += batch.Count;
            HasMoreGroups = batch.Count == 30;
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
        if (_isLoadingStations || !HasMoreItems || SelectedGroup is null) return;
        _isLoadingStations = true;
        IsLoading = true;
        try
        {
            var query = GroupSearchQuery.Length >= 2 ? GroupSearchQuery : null;
            var batch = await Task.Run(() => _stationService.GetStationsByGroupAsync(SelectedGroup.Id, _groupSkip, 100, query, ct), ct);
            foreach (var s in batch) GroupStations.Add(s);
            _groupSkip += batch.Count;
            HasMoreItems = batch.Count == 100;
        }
        catch (OperationCanceledException) { }
        finally
        {
            _isLoadingStations = false;
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void BackToGroups()
    {
        IsGroupView = false;
        SelectedGroup = null;
        GroupStations.Clear();
    }

    [RelayCommand]
    private void PlayStation(Station station) => _miniPlayer.SetStation(station, GroupStations);

    [RelayCommand]
    private async Task ToggleFavourite(Station station)
    {
        await _stationService.ToggleFavouriteAsync(station.Id);
        station.IsFavorite = !station.IsFavorite;
    }
}
