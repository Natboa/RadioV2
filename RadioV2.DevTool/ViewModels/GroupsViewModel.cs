using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioV2.DevTool.Services;
using RadioV2.Models;

namespace RadioV2.DevTool.ViewModels;

public partial class GroupsViewModel : ObservableObject
{
    private readonly DevDbService _db = new();

    [ObservableProperty] ObservableCollection<GroupWithCount> groups = new();
    [ObservableProperty] ObservableCollection<GroupWithCount> mergeTargets = new();
    [ObservableProperty] GroupWithCount? selectedGroup;
    [ObservableProperty] GroupWithCount? mergeTargetGroup;
    [ObservableProperty] string searchText = "";
    [ObservableProperty] string mergeSearchText = "";
    [ObservableProperty] string formName = "";
    [ObservableProperty] bool isEditMode;
    [ObservableProperty] string? errorMessage;
    [ObservableProperty] bool isBusy;

    public bool IsNotBusy => !IsBusy;
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    public GroupsViewModel()
    {
        _ = LoadGroupsAsync();
    }

    private async Task LoadGroupsAsync()
    {
        var list = await Task.Run(() => _db.GetGroupsAsync(SearchText.Trim()));
        Groups.Clear();
        foreach (var g in list) Groups.Add(g);
        RefreshMergeTargets();
    }

    partial void OnSearchTextChanged(string value)
        => _ = LoadGroupsAsync();

    partial void OnMergeSearchTextChanged(string value)
        => RefreshMergeTargets();

    partial void OnSelectedGroupChanged(GroupWithCount? value)
    {
        if (value == null) return;
        IsEditMode = true;
        FormName = value.Name;
        ErrorMessage = null;
        RefreshMergeTargets();
    }

    private void RefreshMergeTargets()
    {
        var filter = MergeSearchText.Trim();
        MergeTargets.Clear();
        foreach (var g in Groups)
        {
            if (g.Id == SelectedGroup?.Id) continue;
            if (!string.IsNullOrEmpty(filter) && !g.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            MergeTargets.Add(g);
        }
        MergeTargetGroup = MergeTargets.FirstOrDefault();
    }

    [RelayCommand]
    private void NewGroup()
    {
        SelectedGroup = null;
        IsEditMode = false;
        FormName = "";
        ErrorMessage = null;
        RefreshMergeTargets();
    }

    [RelayCommand]
    private async Task Save()
    {
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(FormName))
        {
            ErrorMessage = "Group name is required.";
            return;
        }

        try
        {
            if (IsEditMode && SelectedGroup != null)
                await _db.RenameGroupAsync(SelectedGroup.Id, FormName.Trim());
            else
                await _db.CreateGroupAsync(FormName.Trim());

            await LoadGroupsAsync();
            FormName = "";
            SelectedGroup = null;
            IsEditMode = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteGroup()
    {
        if (SelectedGroup == null) return;

        int count = await Task.Run(() => _db.GetStationCountForGroupAsync(SelectedGroup.Id));

        var confirmed = Dialogs.ConfirmDialog.Show(
            $"Delete group '{SelectedGroup.Name}' and its {count:N0} station(s)?\n\nThis cannot be undone.",
            "Confirm Delete");

        if (!confirmed) return;

        IsBusy = true;
        try
        {
            await Task.Run(() => _db.DeleteGroupWithStationsAsync(SelectedGroup.Id));
            NewGroup();
            await LoadGroupsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task MergeGroups()
    {
        if (SelectedGroup == null || MergeTargetGroup == null) return;
        if (SelectedGroup.Id == MergeTargetGroup.Id)
        {
            ErrorMessage = "Source and target must be different groups.";
            return;
        }

        int count = await _db.GetStationCountForGroupAsync(SelectedGroup.Id);

        var confirmed = Dialogs.ConfirmDialog.Show(
            $"Move {count:N0} station(s) from '{SelectedGroup.Name}' into '{MergeTargetGroup.Name}'?\n\n'{SelectedGroup.Name}' will be deleted. This cannot be undone.",
            "Confirm Merge");

        if (!confirmed) return;

        IsBusy = true;
        try
        {
            await Task.Run(() => _db.MergeGroupsAsync(SelectedGroup.Id, MergeTargetGroup.Id));
            NewGroup();
            await LoadGroupsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
