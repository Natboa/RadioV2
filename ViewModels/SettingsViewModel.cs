using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RadioV2.Helpers;
using RadioV2.Models;
using RadioV2.Services;
using System.IO;
using System.Reflection;

namespace RadioV2.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IStationService _stationService;
    private readonly M3UParserService _m3uParser;
    private readonly IFavouritesIOService _favouritesIOService;

    public SettingsViewModel(IStationService stationService, M3UParserService m3uParser, IFavouritesIOService favouritesIOService)
    {
        _stationService = stationService;
        _m3uParser = m3uParser;
        _favouritesIOService = favouritesIOService;
        AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
    }

    [ObservableProperty] private string _selectedTheme = "Dark";
    [ObservableProperty] private string _appVersion = string.Empty;
    [ObservableProperty] private string _importStatusMessage = string.Empty;
    [ObservableProperty] private bool _hasImportStatus;
    [ObservableProperty] private bool _isImporting;
    [ObservableProperty] private string _favouritesStatusMessage = string.Empty;
    [ObservableProperty] private bool _hasFavouritesStatus;

    private bool _suppressThemeChange;

    partial void OnSelectedThemeChanged(string value)
    {
        if (_suppressThemeChange) return;
        ThemeHelper.ApplyTheme(value);
        _ = _stationService.SetSettingAsync("Theme", value);
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        var theme = await _stationService.GetSettingAsync("Theme") ?? "Dark";
        _suppressThemeChange = true;
        SelectedTheme = theme;
        _suppressThemeChange = false;
    }

    [RelayCommand]
    private async Task ImportFavourites()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Favourites",
            Filter = "Playlist files|*.m3u;*.m3u8;*.json|All files|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        var format = ext == ".json" ? "json" : "m3u";

        var count = await _favouritesIOService.ImportAsync(dialog.FileName, format);
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
            Filter = "M3U Playlist|*.m3u|JSON|*.json",
            FileName = "RadioV2_Favourites"
        };
        if (dialog.ShowDialog() != true) return;

        var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        var format = ext == ".json" ? "json" : "m3u";

        await _favouritesIOService.ExportAsync(dialog.FileName, format, [.. favourites]);
        FavouritesStatusMessage = "Favourites exported successfully.";
        HasFavouritesStatus = true;
    }

    [RelayCommand]
    private async Task ImportStations()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Stations from M3U",
            Filter = "M3U Playlist|*.m3u;*.m3u8|All files|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        IsImporting = true;
        HasImportStatus = false;
        try
        {
            var parsed = _m3uParser.Parse(dialog.FileName);
            var count = await _stationService.BulkImportStationsAsync(parsed);
            ImportStatusMessage = $"{count} new station{(count == 1 ? "" : "s")} added to the database.";
            HasImportStatus = true;
        }
        finally
        {
            IsImporting = false;
        }
    }
}
