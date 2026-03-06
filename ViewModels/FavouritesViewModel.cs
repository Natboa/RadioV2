using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RadioV2.Models;
using RadioV2.Services;
using System.Collections.ObjectModel;
using System.IO;

namespace RadioV2.ViewModels;

public partial class FavouritesViewModel : ObservableObject
{
    private readonly IStationService _stationService;
    private readonly IFavouritesIOService _ioService;
    private readonly MiniPlayerViewModel _miniPlayer;

    public FavouritesViewModel(IStationService stationService, IFavouritesIOService ioService, MiniPlayerViewModel miniPlayer)
    {
        _stationService = stationService;
        _ioService = ioService;
        _miniPlayer = miniPlayer;
    }

    [ObservableProperty]
    private ObservableCollection<Station> _favourites = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private int _favouriteCount;

    public bool IsEmpty => FavouriteCount == 0;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasStatusMessage;

    [RelayCommand]
    public async Task LoadFavouritesAsync()
    {
        var list = await Task.Run(() => _stationService.GetFavouritesAsync());
        Favourites.Clear();
        foreach (var s in list) Favourites.Add(s);
        FavouriteCount = Favourites.Count;
    }

    [RelayCommand]
    private async Task RemoveFavourite(Station station)
    {
        await _stationService.ToggleFavouriteAsync(station.Id);
        Favourites.Remove(station);
        FavouriteCount = Favourites.Count;
    }

    [RelayCommand]
    private void PlayStation(Station station) => _miniPlayer.SetStation(station);

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

        var count = await _ioService.ImportAsync(dialog.FileName, format);
        await LoadFavouritesAsync();
        ShowStatus($"{count} station{(count == 1 ? "" : "s")} added to favourites.");
    }

    [RelayCommand]
    private async Task ExportFavourites()
    {
        if (Favourites.Count == 0)
        {
            ShowStatus("Nothing to export — add some favourites first.");
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

        await _ioService.ExportAsync(dialog.FileName, format, [.. Favourites]);
        ShowStatus("Favourites exported successfully.");
    }

    private void ShowStatus(string message)
    {
        StatusMessage = message;
        HasStatusMessage = true;
    }
}
