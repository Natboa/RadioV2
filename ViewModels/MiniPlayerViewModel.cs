using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioV2.Helpers;
using RadioV2.Models;
using RadioV2.Services;
using System.Windows;
using System.Windows.Threading;

namespace RadioV2.ViewModels;

public partial class MiniPlayerViewModel : ObservableObject
{
    private readonly IRadioPlayerService _playerService;
    private readonly IStationService _stationService;
    private readonly NetworkMonitor _networkMonitor;
    private int _previousVolume = 50;
    private List<Station> _currentPlaylist = [];
    private bool _shouldReconnect;

    public MiniPlayerViewModel(IRadioPlayerService playerService, IStationService stationService, NetworkMonitor networkMonitor)
    {
        _playerService = playerService;
        _stationService = stationService;
        _networkMonitor = networkMonitor;

        networkMonitor.ConnectivityChanged += (_, isOnline) =>
        {
            if (isOnline)
            {
                // Refresh station logo
                if (StationLogoUrl is not null)
                {
                    var url = StationLogoUrl;
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        StationLogoUrl = null;
                        StationLogoUrl = url;
                    }, DispatcherPriority.Background);
                }

                // Auto-reconnect if playback was interrupted by the network drop
                if (_shouldReconnect && CurrentStation is not null && !IsPlaying)
                    Application.Current.Dispatcher.BeginInvoke(() =>
                        _playerService.Play(CurrentStation.StreamUrl));
            }
            else
            {
                // Stop cleanly on internet loss; _shouldReconnect stays true so we resume when back
                if (_playerService.IsPlaying || _playerService.IsPaused)
                    Application.Current.Dispatcher.BeginInvoke(() => _playerService.Stop());
            }
        };

        _playerService.PlaybackStarted += (s, e) =>
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                IsPlaying = true;
                if (CurrentStation is not null) CurrentStation.IsNowPlaying = true;
            });

        _playerService.PlaybackStopped += (s, e) =>
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                IsPlaying = false;
                NowPlayingArtist = null;
                NowPlayingTitle = null;
                if (CurrentStation is not null) CurrentStation.IsNowPlaying = false;
            });

        _playerService.BufferingChanged += (s, e) =>
            Application.Current.Dispatcher.BeginInvoke(() => IsBuffering = e < 100f);

        _playerService.PlaybackError += (s, e) =>
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                IsPlaying = false;
                NowPlayingArtist = null;
                NowPlayingTitle = null;
                if (CurrentStation is not null) CurrentStation.IsNowPlaying = false;
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

    public event EventHandler<Station>? StationStarted;

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
        if (CurrentStation is not null)
        {
            CurrentStation.IsNowPlaying = false;
            CurrentStation.PropertyChanged -= OnCurrentStationPropertyChanged;
        }
        CurrentStation = station;
        CurrentStation.PropertyChanged += OnCurrentStationPropertyChanged;
        StationName = station.Name;
        StationLogoUrl = station.LogoUrl;
        IsFavourite = station.IsFavorite;
        NowPlayingArtist = null;
        NowPlayingTitle = null;
        _shouldReconnect = true;
        _playerService.Volume = IsMuted ? 0 : Volume;
        _playerService.Play(station.StreamUrl);
        StationStarted?.Invoke(this, station);
    }

    /// <summary>Restores a station's display on startup without starting playback.</summary>
    public void RestoreStation(Station station)
    {
        if (CurrentStation is not null)
            CurrentStation.PropertyChanged -= OnCurrentStationPropertyChanged;
        CurrentStation    = station;
        CurrentStation.PropertyChanged += OnCurrentStationPropertyChanged;
        StationName       = station.Name;
        StationLogoUrl    = station.LogoUrl;
        IsFavourite       = station.IsFavorite;
        NowPlayingArtist  = null;
        NowPlayingTitle   = null;
    }

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    private void PlayPause()
    {
        if (_playerService.IsPlaying || _playerService.IsPaused)
        {
            _shouldReconnect = false; // user deliberately pausing/stopping
            _playerService.TogglePlayPause();
        }
        else if (CurrentStation != null)
        {
            _shouldReconnect = true; // user manually resuming
            _playerService.Volume = IsMuted ? 0 : Volume;
            _playerService.Play(CurrentStation.StreamUrl);
        }
    }

    [RelayCommand]
    private void Stop()
    {
        _shouldReconnect = false;
        _playerService.Stop();
    }

    [RelayCommand]
    private async Task ToggleFavourite()
    {
        if (CurrentStation is null) return;
        // Update UI immediately before the DB round-trip
        CurrentStation.IsFavorite = !CurrentStation.IsFavorite;
        IsFavourite = CurrentStation.IsFavorite;
        await _stationService.ToggleFavouriteAsync(CurrentStation.Id);
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

    // ── Sleep / wake ──────────────────────────────────────────────────────

    /// <summary>Called when the system is about to sleep. Stops playback cleanly but
    /// preserves <c>_shouldReconnect</c> so we can resume on wake.</summary>
    public void OnSleeping()
    {
        if (_playerService.IsPlaying || _playerService.IsPaused)
            _playerService.Stop();
        // _shouldReconnect intentionally NOT cleared — OnWaking will use it
    }

    /// <summary>Called when the system resumes from sleep. Reconnects if we were
    /// playing before sleep; waits for network if it isn't up yet.</summary>
    public async void OnWaking()
    {
        if (!_shouldReconnect || CurrentStation is null) return;

        // Give Windows a moment to re-establish the network after wake
        await Task.Delay(3000);

        if (_networkMonitor.IsOnline && _shouldReconnect && CurrentStation is not null && !IsPlaying)
            _ = Application.Current.Dispatcher.BeginInvoke(() =>
                _playerService.Play(CurrentStation.StreamUrl));
        // If still offline, the ConnectivityChanged handler will reconnect when network is back
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
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            NowPlayingArtist = artist;
            NowPlayingTitle = title;
        });
    }

    private void OnCurrentStationPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Station.IsFavorite))
            Application.Current.Dispatcher.BeginInvoke(() => IsFavourite = CurrentStation!.IsFavorite);
    }
}
