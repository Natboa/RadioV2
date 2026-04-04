using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RadioV2.Helpers;
using RadioV2.Services;
using System.IO;
using System.Reflection;

namespace RadioV2.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IStationService _stationService;
    private readonly IFavouritesIOService _favouritesIOService;
    private readonly MainWindowViewModel _mainWindowVm;

    public SettingsViewModel(MainWindowViewModel mainWindowVm, IStationService stationService, IFavouritesIOService favouritesIOService)
    {
        _mainWindowVm = mainWindowVm;
        _stationService = stationService;
        _favouritesIOService = favouritesIOService;
        AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
    }

    [ObservableProperty] private string _selectedTheme = "Dark";
    [ObservableProperty] private string _appVersion = string.Empty;
    [ObservableProperty] private string _favouritesStatusMessage = string.Empty;
    [ObservableProperty] private bool _hasFavouritesStatus;
    [ObservableProperty] private bool _isClockEnabled;

    private bool _suppressThemeChange;
    private bool _suppressClockChanges;

    partial void OnSelectedThemeChanged(string value)
    {
        if (_suppressThemeChange) return;
        ThemeHelper.ApplyTheme(value);
        _ = _stationService.SetSettingAsync("Theme", value);
    }

    partial void OnIsClockEnabledChanged(bool value)
    {
        if (_suppressClockChanges) return;
        _mainWindowVm.IsClockEnabled = value;
        _ = _stationService.SetSettingAsync("ClockEnabled", value ? "true" : "false");
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        var theme = await _stationService.GetSettingAsync("Theme") ?? "Dark";
        _suppressThemeChange = true;
        SelectedTheme = theme;
        _suppressThemeChange = false;

        // Sync clock enabled state from MainWindowViewModel (already loaded from DB at startup)
        _suppressClockChanges = true;
        IsClockEnabled = _mainWindowVm.IsClockEnabled;
        _suppressClockChanges = false;
    }

    [RelayCommand]
    private async Task ImportFavourites()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Favourites",
            Filter = "Playlist files|*.m3u;*.m3u8|All files|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        var count = await _favouritesIOService.ImportAsync(dialog.FileName);
        FavouritesStatusMessage = $"{count} station{(count == 1 ? "" : "s")} added to favourites.";
        HasFavouritesStatus = true;
    }

    [RelayCommand]
    private async Task ExportFavourites()
    {
        var favourites = await Task.Run(() => _stationService.GetFavouritesAsync());
        if (favourites.Count == 0)
        {
            FavouritesStatusMessage = "Nothing to export — add some favourites first.";
            HasFavouritesStatus = true;
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Favourites",
            Filter = "M3U Playlist|*.m3u",
            FileName = "RadioV2_Favourites"
        };
        if (dialog.ShowDialog() != true) return;

        await _favouritesIOService.ExportAsync(dialog.FileName, [.. favourites]);
        FavouritesStatusMessage = "Favourites exported successfully.";
        HasFavouritesStatus = true;
    }

}
