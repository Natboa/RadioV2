using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioV2.DevTool.Services;
using RadioV2.Models;

namespace RadioV2.DevTool.ViewModels;

public partial class StationsViewModel : ObservableObject
{
    private readonly DevDbService _db = new();
    private const int PageSize = 50;
    private int _offset;
    private string? _lastSearch;
    private int? _lastGroupId;

    [ObservableProperty] ObservableCollection<Station> stations = new();
    [ObservableProperty] ObservableCollection<GroupWithCount> groups = new();

    // "All Groups" sentinel + actual groups for the filter dropdown
    [ObservableProperty] ObservableCollection<GroupWithCount> groupFilter = new();

    [ObservableProperty] GroupWithCount? selectedGroupFilter;
    [ObservableProperty] string searchText = "";
    [ObservableProperty] Station? selectedStation;
    [ObservableProperty] bool isEditMode;
    [ObservableProperty] bool hasMore;
    [ObservableProperty] string? errorMessage;

    // Form fields (right panel)
    [ObservableProperty] string formName = "";
    [ObservableProperty] string formStreamUrl = "";
    [ObservableProperty] string formLogoUrl = "";
    [ObservableProperty] int formGroupId;
    [ObservableProperty] bool formIsFavorite;

    public StationsViewModel()
    {
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await LoadGroupsAsync();
        await ReloadStationsAsync();
    }

    private async Task LoadGroupsAsync()
    {
        var list = await _db.GetGroupsAsync();
        Groups.Clear();
        foreach (var g in list) Groups.Add(g);

        GroupFilter.Clear();
        GroupFilter.Add(new GroupWithCount { Id = 0, Name = "(All Groups)" });
        foreach (var g in list) GroupFilter.Add(g);

        if (SelectedGroupFilter == null)
            SelectedGroupFilter = GroupFilter.First();
    }

    partial void OnSelectedGroupFilterChanged(GroupWithCount? value)
        => _ = ReloadStationsAsync();

    partial void OnSearchTextChanged(string value)
        => _ = ReloadStationsAsync();

    private async Task ReloadStationsAsync()
    {
        _offset = 0;
        var search = SearchText.Trim();
        var groupId = (SelectedGroupFilter?.Id ?? 0) == 0 ? (int?)null : SelectedGroupFilter!.Id;
        _lastSearch = search;
        _lastGroupId = groupId;

        var result = await _db.GetStationsAsync(search, groupId, 0, PageSize);
        Stations.Clear();
        foreach (var s in result) Stations.Add(s);
        _offset = result.Count;

        var total = await _db.GetStationCountAsync(search, groupId);
        HasMore = _offset < total;
    }

    [RelayCommand]
    private async Task LoadMore()
    {
        var result = await _db.GetStationsAsync(_lastSearch, _lastGroupId, _offset, PageSize);
        foreach (var s in result) Stations.Add(s);
        _offset += result.Count;

        var total = await _db.GetStationCountAsync(_lastSearch, _lastGroupId);
        HasMore = _offset < total;
    }

    partial void OnSelectedStationChanged(Station? value)
    {
        if (value == null) return;
        IsEditMode = true;
        FormName = value.Name;
        FormStreamUrl = value.StreamUrl;
        FormLogoUrl = value.LogoUrl ?? "";
        FormGroupId = value.GroupId;
        FormIsFavorite = value.IsFavorite;
        ErrorMessage = null;
    }

    [RelayCommand]
    private void NewStation()
    {
        SelectedStation = null;
        IsEditMode = false;
        FormName = "";
        FormStreamUrl = "";
        FormLogoUrl = "";
        FormGroupId = Groups.FirstOrDefault()?.Id ?? 0;
        FormIsFavorite = false;
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task Save()
    {
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(FormName) || string.IsNullOrWhiteSpace(FormStreamUrl))
        {
            ErrorMessage = "Name and Stream URL are required.";
            return;
        }

        try
        {
            if (IsEditMode && SelectedStation != null)
            {
                SelectedStation.Name = FormName.Trim();
                SelectedStation.StreamUrl = FormStreamUrl.Trim();
                SelectedStation.LogoUrl = string.IsNullOrWhiteSpace(FormLogoUrl) ? null : FormLogoUrl.Trim();
                SelectedStation.GroupId = FormGroupId;
                SelectedStation.IsFavorite = FormIsFavorite;
                await _db.UpdateStationAsync(SelectedStation);
            }
            else
            {
                var station = new Station
                {
                    Name = FormName.Trim(),
                    StreamUrl = FormStreamUrl.Trim(),
                    LogoUrl = string.IsNullOrWhiteSpace(FormLogoUrl) ? null : FormLogoUrl.Trim(),
                    GroupId = FormGroupId,
                    IsFavorite = FormIsFavorite
                };
                await _db.CreateStationAsync(station);
            }

            await ReloadStationsAsync();
        }
        catch (Exception ex) when (ex.Message.Contains("UNIQUE") || ex.Message.Contains("unique"))
        {
            ErrorMessage = "Stream URL already exists — must be unique.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteStation()
    {
        if (SelectedStation == null) return;

        var result = Dialogs.ConfirmDialog.Show(
            $"Delete '{SelectedStation.Name}'?\n\nThis cannot be undone.",
            "Confirm Delete");

        if (!result) return;

        await _db.DeleteStationAsync(SelectedStation.Id);
        NewStation();
        await ReloadStationsAsync();
    }
}
