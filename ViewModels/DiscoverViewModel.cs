using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioV2.Helpers;
using RadioV2.Models;
using RadioV2.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace RadioV2.ViewModels;

public partial class DiscoverViewModel : ObservableObject
{
    private readonly IStationService _stationService;
    private readonly MiniPlayerViewModel _miniPlayer;
    private int _groupSkip;
    private int _generation;
    private int _loadingGeneration = -1;
    private bool _preparingGroup;
    private bool _searchPending;
    private CancellationTokenSource _visitCts = new();
    private string[] _artworkSnapshot = [];
    private CancellationTokenSource _searchCts = new();

    // Tracks how many items at the start of AllStationItems belong to the featured section
    // (0 when no featured; otherwise: 1 header + N stations + 1 separator = N+2)
    private int _featuredSectionSize;

    public DiscoverViewModel(IStationService stationService, MiniPlayerViewModel miniPlayer, NetworkMonitor networkMonitor)
    {
        _stationService = stationService;
        _miniPlayer = miniPlayer;
        FeaturedStations.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasFeaturedStations));
        networkMonitor.ConnectivityChanged += (_, isOnline) =>
        {
            if (!isOnline) return;
            foreach (var s in GroupStations) s.NotifyLogoChanged();
            foreach (var s in FeaturedStations) s.NotifyLogoChanged();
        };
    }

    // ── Carousel (category rows) ─────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<CarouselRowViewModel> _categoryRows = [];
    [ObservableProperty] private bool _categoriesLoaded;

    private List<GroupWithCount> _allGroups = [];

    // ── Discover search (genres / countries) ─────────────────────────────────
    [ObservableProperty] private string _discoverSearchQuery = string.Empty;
    [ObservableProperty] private ObservableCollection<GroupWithCount> _searchResultGroups = [];

    public bool IsSearching => DiscoverSearchQuery.Length > 0;
    public bool IsCarouselVisible => !IsGroupView && !IsSearching;
    public bool IsSearchResultsVisible => !IsGroupView && IsSearching;

    partial void OnDiscoverSearchQueryChanged(string value)
    {
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(IsCarouselVisible));
        OnPropertyChanged(nameof(IsSearchResultsVisible));
        SearchResultGroups.Clear();
        if (value.Length == 0) return;
        foreach (var g in _allGroups.Where(g => g.Name.Contains(value, StringComparison.OrdinalIgnoreCase)))
            SearchResultGroups.Add(g);
    }

    public async Task LoadCategoriesAsync(CancellationToken ct = default)
    {
        if (CategoriesLoaded) return;
        var cached = _stationService.CategoriesAreCached;
        if (!cached) IsLoading = true;
        try
        {
            var categories = await Task.Run(() => _stationService.GetCategoriesWithGroupsAsync(ct), ct);
            CategoryRows.Clear();
            foreach (var cat in categories)
            {
                CategoryRows.Add(new CarouselRowViewModel
                {
                    CategoryId = cat.Id,
                    CategoryName = cat.Name,
                    Groups = cat.Groups,
                    SelectGroupCommand = SelectGroupCommand
                });
            }
            _allGroups = categories.SelectMany(c => c.Groups).ToList();
            CategoriesLoaded = true;
        }
        catch (OperationCanceledException) { }
        finally { IsLoading = false; }
    }

    // ── Station drill-down ───────────────────────────────────────────────────
    [ObservableProperty] private GroupWithCount? _selectedGroup;

    // GroupStations is kept for playlist context (keyboard navigation / SetStation).
    [ObservableProperty] private ObservableCollection<Station> _groupStations = [];

    // FeaturedStations is kept for ToggleFeatured sync logic.
    [ObservableProperty] private ObservableCollection<Station> _featuredStations = [];

    // AllStationItems is the single source for the unified virtualized ListBox:
    //   [FeaturedHeaderItem] [StationGroupViewItem...] [SectionSeparatorItem] [StationGroupViewItem...]
    [ObservableProperty] private ObservableCollection<object> _allStationItems = [];

    public bool HasFeaturedStations => FeaturedStations.Count > 0;
    [ObservableProperty] private string _groupSearchQuery = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCarouselVisible))]
    [NotifyPropertyChangedFor(nameof(IsSearchResultsVisible))]
    private bool _isGroupView;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadingSpinner))]
    private bool _isLoading;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadingSpinner))]
    private bool _isAtBottom = true;
    [ObservableProperty] private bool _hasMoreItems = true;

    public bool ShowLoadingSpinner => IsLoading && IsAtBottom;

    partial void OnGroupSearchQueryChanged(string value)
    {
        if (!IsGroupView || SelectedGroup is null) return;
        _searchCts.Cancel();
        _searchCts.Dispose();
        _searchCts = CancellationTokenSource.CreateLinkedTokenSource(_visitCts.Token);
        _generation++;
        _preparingGroup = false;
        _searchPending = true;
        _ = DebounceGroupSearchAsync(_searchCts.Token);
    }

    private async Task DebounceGroupSearchAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(300, ct);
            ct.ThrowIfCancellationRequested();
            GroupStations.Clear();
            _groupSkip = 0;
            HasMoreItems = true;

            bool isSearching = GroupSearchQuery.Length >= 2;

            // Rebuild AllStationItems: hide featured section while searching
            AllStationItems.Clear();
            if (!isSearching && FeaturedStations.Count > 0)
            {
                AllStationItems.Add(new FeaturedHeaderItem());
                foreach (var s in FeaturedStations)
                    AllStationItems.Add(new StationGroupViewItem(s));
                AllStationItems.Add(new SectionSeparatorItem());
                _featuredSectionSize = FeaturedStations.Count + 2;
            }
            else
            {
                _featuredSectionSize = 0;
            }

            _searchPending = false;
            await LoadMoreGroupStationsAsync(ct);
        }
        catch (OperationCanceledException) { }
    }

    [RelayCommand]
    public async Task SelectGroupAsync(GroupWithCount group)
    {
        _visitCts.Cancel();
        _visitCts.Dispose();
        _visitCts = new CancellationTokenSource();
        _searchCts.Cancel();
        _searchCts.Dispose();
        _searchCts = new CancellationTokenSource();
        var ct = _visitCts.Token;
        var generation = ++_generation;
        _artworkSnapshot = StationArtworkCache.Shared.Snapshot();
        IsGroupView = false; // resetting the query must not schedule a search
        SelectedGroup = group;
        GroupStations.Clear();
        FeaturedStations.Clear();
        AllStationItems.Clear();
        _featuredSectionSize = 0;
        DiscoverSearchQuery = string.Empty;
        GroupSearchQuery = string.Empty;
        _groupSkip = 0;
        HasMoreItems = true;
        _searchPending = false;
        _preparingGroup = true;
        IsLoading = true;
        IsGroupView = true;

        try
        {
            // Load featured first (fast, small query) so the header is in place before stations arrive.
            var snapshot = _artworkSnapshot;
            var featured = await Task.Run(() => _stationService.GetFeaturedStationsByGroupAsync(group.Id, ct, snapshot), ct);
            if (ct.IsCancellationRequested || !IsGroupView) return;
            foreach (var station in featured) FeaturedStations.Add(station);
            if (featured.Count > 0 && !_searchPending && GroupSearchQuery.Length < 2)
            {
                AllStationItems.Insert(0, new FeaturedHeaderItem());
                int index = 1;
                foreach (var s in featured)
                {
                    AllStationItems.Insert(index++, new StationGroupViewItem(s));
                }
                AllStationItems.Insert(index, new SectionSeparatorItem());
                _featuredSectionSize = featured.Count + 2; // header + stations + separator
            }

            _preparingGroup = false;
            if (generation != _generation) return;
            await LoadMoreGroupStationsAsync(ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (generation == _generation) { _preparingGroup = false; IsLoading = false; }
        }
    }

    [RelayCommand]
    public async Task LoadMoreGroupStationsAsync(CancellationToken ct = default)
    {
        if (_preparingGroup || _searchPending || _loadingGeneration == _generation || !IsGroupView || !HasMoreItems || SelectedGroup is null) return;
        var generation = _generation;
        _loadingGeneration = generation;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _visitCts.Token, _searchCts.Token);
        ct = linked.Token;
        var groupId = SelectedGroup.Id;
        var snapshot = _artworkSnapshot;
        IsLoading = true;
        try
        {
            var query = GroupSearchQuery.Length >= 2 ? GroupSearchQuery : null;
            var isFirstBatch = _groupSkip == 0;
            var skip = _groupSkip;
            var batch = await Task.Run(() => _stationService.GetStationsByGroupAsync(groupId, skip, 30, query, ct, snapshot), ct);
            if (generation != _generation || ct.IsCancellationRequested) return;

            var targetCollection = isFirstBatch ? new ObservableCollection<Station>() : GroupStations;
            var dispatcher = Application.Current?.Dispatcher;
            if (isFirstBatch)
            {
                GroupStations = targetCollection;
                // Let the ItemsSource binding fire before we start adding items.
                if (dispatcher is not null) await dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
            }

            // Add in ~8 ms chunks, yielding at Loaded priority so render/animation frames
            // run between chunks (keeps the ProgressRing spinning, prevents UI freeze).
            var sw = Stopwatch.StartNew();
            foreach (var s in batch)
            {
                if (generation != _generation || ct.IsCancellationRequested) return;
                targetCollection.Add(s);
                AllStationItems.Add(new StationGroupViewItem(s));
                if (sw.ElapsedMilliseconds >= 8)
                {
                    if (dispatcher is not null) await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
                    sw.Restart();
                }
            }
            _groupSkip += batch.Count;
            HasMoreItems = batch.Count == 30;
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_loadingGeneration == generation) _loadingGeneration = -1;
            if (generation == _generation) IsLoading = false;
        }
    }

    [RelayCommand]
    private void BackToGroups()
    {
        _visitCts.Cancel();
        _searchCts.Cancel();
        _generation++;
        _searchPending = false;
        _preparingGroup = false;
        IsLoading = false;
        IsGroupView = false;
        SelectedGroup = null;
        GroupStations.Clear();
        FeaturedStations.Clear();
        AllStationItems.Clear();
        _featuredSectionSize = 0;
    }

    [RelayCommand]
    private void PlayStation(Station station) => _miniPlayer.SetStation(station, GroupStations);

    [RelayCommand]
    private async Task ToggleFavourite(Station station)
    {
        station.IsFavorite = !station.IsFavorite;
        await _stationService.ToggleFavouriteAsync(station.Id);
    }

    [RelayCommand]
    private async Task ToggleFeatured(Station station)
    {
        var newValue = !station.IsFeatured;
        await _stationService.SetStationFeaturedAsync(station.Id, newValue);
        station.IsFeatured = newValue;

        // Keep the GroupStations copy in sync
        var groupCopy = GroupStations.FirstOrDefault(s => s.Id == station.Id);
        if (groupCopy != null && !ReferenceEquals(groupCopy, station))
            groupCopy.IsFeatured = newValue;

        bool searchActive = GroupSearchQuery.Length >= 2;

        if (newValue)
        {
            FeaturedStations.Add(station);
            if (!searchActive)
            {
                if (_featuredSectionSize == 0)
                {
                    // First featured item — insert header, item, separator at the top
                    AllStationItems.Insert(0, new SectionSeparatorItem());
                    AllStationItems.Insert(0, new StationGroupViewItem(station));
                    AllStationItems.Insert(0, new FeaturedHeaderItem());
                    _featuredSectionSize = 3;
                }
                else
                {
                    // Insert before the separator (last item of featured section)
                    AllStationItems.Insert(_featuredSectionSize - 1, new StationGroupViewItem(station));
                    _featuredSectionSize++;
                }
            }
        }
        else
        {
            var toRemove = FeaturedStations.FirstOrDefault(s => s.Id == station.Id);
            if (toRemove != null) FeaturedStations.Remove(toRemove);

            if (!searchActive)
            {
                // Remove from the featured section in AllStationItems
                for (int i = 0; i < _featuredSectionSize; i++)
                {
                    if (AllStationItems[i] is StationGroupViewItem svi && svi.Station.Id == station.Id)
                    {
                        AllStationItems.RemoveAt(i);
                        _featuredSectionSize--;
                        break;
                    }
                }
                // If only header + separator remain (no featured stations), remove them too
                if (_featuredSectionSize == 2)
                {
                    AllStationItems.RemoveAt(1); // separator
                    AllStationItems.RemoveAt(0); // header
                    _featuredSectionSize = 0;
                }
            }
        }
    }
}
