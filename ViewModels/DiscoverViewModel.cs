using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private bool _isLoadingStations;
    private CancellationTokenSource _searchCts = new();

    public DiscoverViewModel(IStationService stationService, MiniPlayerViewModel miniPlayer)
    {
        _stationService = stationService;
        _miniPlayer = miniPlayer;
        FeaturedStations.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasFeaturedStations));
    }

    // ── Carousel (category rows) ─────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<CarouselRowViewModel> _categoryRows = [];
    [ObservableProperty] private bool _categoriesLoaded;

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
            CategoriesLoaded = true;
        }
        catch (OperationCanceledException) { }
        finally { IsLoading = false; }
    }

    // ── Station drill-down ───────────────────────────────────────────────────
    [ObservableProperty] private GroupWithCount? _selectedGroup;
    [ObservableProperty] private ObservableCollection<Station> _groupStations = [];

    [ObservableProperty]
    private ObservableCollection<Station> _featuredStations = [];

    public bool HasFeaturedStations => FeaturedStations.Count > 0;
    [ObservableProperty] private string _groupSearchQuery = string.Empty;
    [ObservableProperty] private bool _isGroupView;
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
    public async Task SelectGroupAsync(GroupWithCount group)
    {
        SelectedGroup = group;
        GroupStations.Clear();
        FeaturedStations.Clear();
        GroupSearchQuery = string.Empty;
        _groupSkip = 0;
        HasMoreItems = true;
        IsGroupView = true;

        var featuredTask = Task.Run(() => _stationService.GetFeaturedStationsByGroupAsync(group.Id));
        var stationsTask = LoadMoreGroupStationsAsync();
        var featured = await featuredTask;
        foreach (var s in featured) FeaturedStations.Add(s);
        await stationsTask;
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
            var isFirstBatch = _groupSkip == 0;
            var batch = await Task.Run(() => _stationService.GetStationsByGroupAsync(SelectedGroup.Id, _groupSkip, 100, query, ct), ct);
            // First batch: populate in chunks, yielding at Background priority between each chunk.
            // This lets the compositor render animation frames (ProgressRing) between bursts.
            // DispatcherPriority.Background (4) is below Render (7) so render passes happen before resuming.
            var targetCollection = isFirstBatch ? new ObservableCollection<Station>() : GroupStations;
            var dispatcher = Application.Current.Dispatcher;
            if (isFirstBatch)
            {
                GroupStations = targetCollection;
                // The ItemsControl's ItemsSource binding update is queued at DataBind priority (8),
                // not applied immediately. If we add items before it fires, ItemsControl sees a
                // pre-filled collection and creates all containers in one burst.
                // Yielding at DataBind lets the binding fire so ItemsControl subscribes first —
                // after this, each Add triggers exactly one container creation (the expensive part),
                // allowing the Stopwatch to correctly time and chunk the work.
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
            }
            // Yield at Loaded (6) every ~8ms. Render (7) is higher priority, so a render/animation
            // frame runs before each resumed chunk → spinner stays animated. Input (5) is lower, so
            // scroll events are handled after chunks but the short burst keeps stutter minimal.
            var sw = Stopwatch.StartNew();
            foreach (var s in batch)
            {
                targetCollection.Add(s);
                if (sw.ElapsedMilliseconds >= 8)
                {
                    await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
                    sw.Restart();
                }
            }
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
        FeaturedStations.Clear();
    }

    [RelayCommand]
    private void PlayStation(Station station) => _miniPlayer.SetStation(station, GroupStations);

    [RelayCommand]
    private async Task ToggleFavourite(Station station)
    {
        await _stationService.ToggleFavouriteAsync(station.Id);
        station.IsFavorite = !station.IsFavorite;
    }

    [RelayCommand]
    private async Task ToggleFeatured(Station station)
    {
        var newValue = !station.IsFeatured;
        await _stationService.SetStationFeaturedAsync(station.Id, newValue);
        station.IsFeatured = newValue;

        // Keep the GroupStations copy in sync (it may be a different object from the featured query)
        var groupCopy = GroupStations.FirstOrDefault(s => s.Id == station.Id);
        if (groupCopy != null && !ReferenceEquals(groupCopy, station))
            groupCopy.IsFeatured = newValue;

        if (newValue)
        {
            FeaturedStations.Add(station);
        }
        else
        {
            var toRemove = FeaturedStations.FirstOrDefault(s => s.Id == station.Id);
            if (toRemove != null) FeaturedStations.Remove(toRemove);
        }
    }
}
