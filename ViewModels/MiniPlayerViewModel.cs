using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioV2.Helpers;
using RadioV2.Models;
using RadioV2.Services;
using System.Windows;

namespace RadioV2.ViewModels;

public partial class MiniPlayerViewModel : ObservableObject
{
    private readonly IRadioPlayerService _playerService;
    private readonly IStationService _stationService;
    private int _previousVolume = 50;
    private List<Station> _favouritesList = [];
    private int _currentFavouriteIndex = -1;

    public MiniPlayerViewModel(IRadioPlayerService playerService, IStationService stationService)
    {
        _playerService = playerService;
        _stationService = stationService;

        _playerService.PlaybackStarted += (s, e) =>
            Application.Current.Dispatcher.Invoke(() => IsPlaying = true);

        _playerService.PlaybackStopped += (s, e) =>
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsPlaying = false;
                NowPlayingArtist = null;
                NowPlayingTitle = null;
            });

        _playerService.BufferingChanged += (s, e) =>
            Application.Current.Dispatcher.Invoke(() => IsBuffering = e < 100f);

        _playerService.PlaybackError += (s, e) =>
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsPlaying = false;
                NowPlayingArtist = null;
                NowPlayingTitle = null;
            });

        _playerService.MetadataChanged += OnMetadataChanged;
    }

    // ── Observable properties ─────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStation))]
    private string _stationName = string.Empty;

    [ObservableProperty]
    private string? _stationLogoUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NowPlayingDisplay), nameof(HasNowPlaying))]
    private string? _nowPlayingArtist;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NowPlayingDisplay), nameof(HasNowPlaying))]
    private string? _nowPlayingTitle;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isFavourite;

    [ObservableProperty]
    private bool _isBuffering;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private int _volume = 50;

    partial void OnVolumeChanged(int value)
    {
        if (!IsMuted)
            _playerService.Volume = value;
    }

    public Station? CurrentStation { get; private set; }

    public string? NowPlayingDisplay => (NowPlayingArtist, NowPlayingTitle) switch
    {
        ({ } artist, { } title) => $"{artist} \u2014 {title}",
        (null, { } title) => title,
        _ => null
    };

    public bool HasStation => !string.IsNullOrEmpty(StationName);
    public bool HasNowPlaying => NowPlayingDisplay is not null;

    // ── Public API ────────────────────────────────────────────────────────

    public void SetStation(Station station)
    {
        CurrentStation = station;
        StationName = station.Name;
        StationLogoUrl = station.LogoUrl;
        IsFavourite = station.IsFavorite;
        NowPlayingArtist = null;
        NowPlayingTitle = null;
        _playerService.Volume = IsMuted ? 0 : Volume;
        _playerService.Play(station.StreamUrl);
    }

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    private void PlayPause() => _playerService.TogglePlayPause();

    [RelayCommand]
    private void Stop() => _playerService.Stop();

    [RelayCommand]
    private async Task ToggleFavourite()
    {
        if (CurrentStation is null) return;
        await _stationService.ToggleFavouriteAsync(CurrentStation.Id);
        CurrentStation.IsFavorite = !CurrentStation.IsFavorite;
        IsFavourite = CurrentStation.IsFavorite;
    }

    [RelayCommand]
    private void ToggleMute()
    {
        if (IsMuted)
        {
            IsMuted = false;
            _playerService.Volume = _previousVolume;
        }
        else
        {
            _previousVolume = Volume;
            IsMuted = true;
            _playerService.Volume = 0;
        }
    }

    [RelayCommand]
    private async Task NextStation()
    {
        await RefreshFavouritesListAsync();
        if (_favouritesList.Count == 0) return;

        _currentFavouriteIndex = _favouritesList.FindIndex(s => s.Id == CurrentStation?.Id);
        _currentFavouriteIndex = (_currentFavouriteIndex + 1) % _favouritesList.Count;
        SetStation(_favouritesList[_currentFavouriteIndex]);
    }

    [RelayCommand]
    private async Task PreviousStation()
    {
        await RefreshFavouritesListAsync();
        if (_favouritesList.Count == 0) return;

        _currentFavouriteIndex = _favouritesList.FindIndex(s => s.Id == CurrentStation?.Id);
        _currentFavouriteIndex = _currentFavouriteIndex <= 0
            ? _favouritesList.Count - 1
            : _currentFavouriteIndex - 1;
        SetStation(_favouritesList[_currentFavouriteIndex]);
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private void OnMetadataChanged(object? sender, string rawNowPlaying)
    {
        var (artist, title) = NowPlayingParser.Parse(rawNowPlaying);
        Application.Current.Dispatcher.Invoke(() =>
        {
            NowPlayingArtist = artist;
            NowPlayingTitle = title;
        });
    }

    private async Task RefreshFavouritesListAsync()
    {
        _favouritesList = await _stationService.GetFavouritesAsync();
    }
}
