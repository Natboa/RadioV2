using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioV2.Helpers;
using RadioV2.Models;
using RadioV2.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace RadioV2.ViewModels;

public partial class BrowseViewModel : ObservableObject
{
    private static readonly string HistoryPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RadioV2", "search_history.json");

    private static readonly string RecentPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RadioV2", "recent_stations.json");

    private record RecentEntry(int Id, string Name, string StreamUrl, string? LogoUrl, int GroupId);

    private readonly IStationService _stationService;
    private readonly MiniPlayerViewModel _miniPlayer;
    private int _skip;
    private CancellationTokenSource _searchCts = new();

    public BrowseViewModel(IStationService stationService, MiniPlayerViewModel miniPlayer, NetworkMonitor networkMonitor)
    {
        _stationService = stationService;
        _miniPlayer = miniPlayer;
        _miniPlayer.StationStarted += OnStationStarted;
        LoadRecentStations();
        networkMonitor.ConnectivityChanged += OnConnectivityChanged;
    }

    private void OnConnectivityChanged(object? sender, bool isOnline)
    {
        if (!isOnline) return;
        foreach (var s in Stations) s.NotifyLogoChanged();
        foreach (var s in RecentStations) s.NotifyLogoChanged();
    }

    [ObservableProperty] private ObservableCollection<Station> _stations = [];
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadingSpinner))]
    private bool _isLoading;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadingSpinner))]
    private bool _isAtBottom = true;
    [ObservableProperty] private bool _hasMoreItems = true;

    public bool ShowLoadingSpinner => IsLoading && IsAtBottom;
    [ObservableProperty] private ObservableCollection<string> _historyItems = [];
    [ObservableProperty] private bool _isHistoryVisible;
    [ObservableProperty] private ObservableCollection<Station> _recentStations = [];
    [ObservableProperty] private bool _isRecentVisible;

    partial void OnSearchQueryChanged(string value)
    {
        IsRecentVisible = value.Length < 2 && RecentStations.Count > 0;
        _searchCts.Cancel();
        _searchCts = new CancellationTokenSource();
        _ = DebounceSearchAsync(_searchCts.Token);
    }

    private async Task DebounceSearchAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(300, ct);
            IsHistoryVisible = false;
            Stations.Clear();
            _skip = 0;
            HasMoreItems = true;
            await LoadMoreAsync(ct);
        }
        catch (OperationCanceledException) { }
    }

    public void ShowHistory()
    {
        LoadHistory();
        IsHistoryVisible = HistoryItems.Count > 0;
    }

    public void HideHistory() => IsHistoryVisible = false;

    public void SaveCurrentQueryToHistory()
    {
        if (SearchQuery.Length >= 2) SaveToHistory(SearchQuery);
    }

    [RelayCommand]
    public void SelectHistoryItem(string item)
    {
        IsHistoryVisible = false;
        SearchQuery = item;
    }

    private void LoadHistory()
    {
        if (!File.Exists(HistoryPath)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(HistoryPath)) ?? [];
            HistoryItems.Clear();
            foreach (var h in list) HistoryItems.Add(h);
        }
        catch { }
    }

    private void SaveToHistory(string query)
    {
        LoadHistory();
        var list = HistoryItems.ToList();
        list.Remove(query);
        list.Insert(0, query);
        if (list.Count > 7) list = [.. list.Take(7)];
        HistoryItems.Clear();
        foreach (var h in list) HistoryItems.Add(h);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
            File.WriteAllText(HistoryPath, JsonSerializer.Serialize(list));
        }
        catch { }
    }

    private void OnStationStarted(object? sender, Station station)
    {
        var existing = RecentStations.FirstOrDefault(s => s.Id == station.Id);
        if (existing is not null) RecentStations.Remove(existing);
        RecentStations.Insert(0, station);
        while (RecentStations.Count > 8) RecentStations.RemoveAt(RecentStations.Count - 1);
        IsRecentVisible = SearchQuery.Length < 2;
        SaveRecentStations();
    }

    private void LoadRecentStations()
    {
        if (!File.Exists(RecentPath)) return;
        try
        {
            var entries = JsonSerializer.Deserialize<List<RecentEntry>>(File.ReadAllText(RecentPath)) ?? [];
            foreach (var e in entries.Take(8))
                RecentStations.Add(new Station { Id = e.Id, Name = e.Name, StreamUrl = e.StreamUrl, LogoUrl = e.LogoUrl, GroupId = e.GroupId });
            IsRecentVisible = RecentStations.Count > 0 && SearchQuery.Length < 2;
        }
        catch { }
    }

    private void SaveRecentStations()
    {
        try
        {
            var entries = RecentStations.Select(s => new RecentEntry(s.Id, s.Name, s.StreamUrl, s.LogoUrl, s.GroupId)).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(RecentPath)!);
            File.WriteAllText(RecentPath, JsonSerializer.Serialize(entries));
        }
        catch { }
    }

    [RelayCommand]
    public async Task LoadMoreAsync(CancellationToken ct = default)
    {
        if (IsLoading || !HasMoreItems) return;
        IsLoading = true;
        try
        {
            var query = SearchQuery.Length >= 2 ? SearchQuery : null;
            var batch = await Task.Run(() => _stationService.GetStationsAsync(_skip, 50, query, ct), ct);
            foreach (var s in batch) Stations.Add(s);
            _skip += batch.Count;
            HasMoreItems = batch.Count == 50;
        }
        catch (OperationCanceledException) { }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void PlayStation(Station station) => _miniPlayer.SetStation(station, Stations);

    [RelayCommand]
    private async Task ToggleFavourite(Station station)
    {
        await _stationService.ToggleFavouriteAsync(station.Id);
        station.IsFavorite = !station.IsFavorite;
    }
}
