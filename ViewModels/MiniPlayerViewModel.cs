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
    private List<Station> _currentPlaylist = [];

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

    /// <summary>Play a station with a playlist context for keyboard Next/Prev navigation.</summary>
    public void SetStation(Station station, IList<Station> playlist)
    {
        _currentPlaylist = [.. playlist];
        SetStationCore(station);
    }

    public void SetStation(Station station)
    {
        _currentPlaylist = [];
        SetStationCore(station);
    }

    private void SetStationCore(Station station)
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
            Volume = _previousVolume; // restores slider and player volume via OnVolumeChanged
        }
        else
        {
            _previousVolume = Volume;
            IsMuted = true;
            _playerService.Volume = 0;
            Volume = 0; // moves slider to left; OnVolumeChanged skips player while muted
        }
    }

    [RelayCommand]
    private async Task NextStation()
    {
        var playlist = await GetEffectivePlaylistAsync();
        if (playlist.Count == 0) return;

        var idx = playlist.FindIndex(s => s.Id == CurrentStation?.Id);
        idx = (idx + 1) % playlist.Count;
        SetStationCore(playlist[idx]);
    }

    [RelayCommand]
    private async Task PreviousStation()
    {
        var playlist = await GetEffectivePlaylistAsync();
        if (playlist.Count == 0) return;

        var idx = playlist.FindIndex(s => s.Id == CurrentStation?.Id);
        idx = idx <= 0 ? playlist.Count - 1 : idx - 1;
        SetStationCore(playlist[idx]);
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private async Task<List<Station>> GetEffectivePlaylistAsync()
    {
        if (_currentPlaylist.Count > 0) return _currentPlaylist;
        // Fallback: iterate favourites when no context playlist is set
        var favs = await _stationService.GetFavouritesAsync();
        _currentPlaylist = favs;
        return _currentPlaylist;
    }

    private void OnMetadataChanged(object? sender, string rawNowPlaying)
    {
        var (artist, title) = NowPlayingParser.Parse(rawNowPlaying);
        Application.Current.Dispatcher.Invoke(() =>
        {
            NowPlayingArtist = artist;
            NowPlayingTitle = title;
        });
    }
}
