using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioV2.Helpers;
using RadioV2.Models;
using RadioV2.Services;
using Serilog;
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

    /// <summary>
    /// Guards automatic reconnects only. A wake and a network-restore can land within
    /// seconds of each other and both want to reconnect; without this, both call Play
    /// and two streams end up running. User-initiated plays deliberately bypass it —
    /// clicking a different station while one is still connecting must always win.
    /// </summary>
    private int _autoReconnecting;

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
                    OnUi(() =>
                    {
                        StationLogoUrl = null;
                        StationLogoUrl = url;
                    }, DispatcherPriority.Background);
                }

                TryAutoReconnect();
            }
            else if (_playerService.IsPlaying)
            {
                // Stop cleanly on internet loss; _shouldReconnect stays true so we
                // resume when it returns. No dispatcher hop — StopAsync queues and
                // returns, so this never blocks whoever raised the event.
                _ = _playerService.StopAsync();
            }
        };

        _playerService.PlaybackStarted += (s, e) =>
            OnUi(() =>
            {
                Interlocked.Exchange(ref _autoReconnecting, 0);
                IsPlaying = true;
                ErrorMessage = null;

                // Deliberately NOT clearing NowPlayingArtist/Title here. VLC delivers
                // ICY metadata during the HTTP buffering phase, which is before the
                // Playing event — clearing on Playing wipes the track that has
                // already arrived, and on a station that holds a track for several
                // minutes nothing is shown until the next song change.
                // They are reset by SetStationCore, RestoreStation, PlaybackStopped
                // and PlaybackError, so stale metadata cannot survive a switch.

                if (CurrentStation is not null) CurrentStation.IsNowPlaying = true;
            });

        _playerService.PlaybackStopped += (s, e) =>
            OnUi(() =>
            {
                Interlocked.Exchange(ref _autoReconnecting, 0);
                IsPlaying = false;
                NowPlayingArtist = null;
                NowPlayingTitle = null;
                if (CurrentStation is not null) CurrentStation.IsNowPlaying = false;
            });

        _playerService.BufferingChanged += (s, e) =>
            OnUi(() => IsBuffering = e < 100f);

        _playerService.PlaybackError += (s, e) =>
            OnUi(() =>
            {
                Interlocked.Exchange(ref _autoReconnecting, 0);
                IsPlaying = false;
                NowPlayingArtist = null;
                NowPlayingTitle = null;
                if (CurrentStation is not null) CurrentStation.IsNowPlaying = false;
                ErrorMessage = e == "Audio engine unavailable" ? "Audio engine unavailable" : "Station unavailable right now";
            });

        _playerService.MetadataChanged += OnMetadataChanged;
    }

    /// <summary>
    /// Marshals to the UI thread when there is one. Application.Current is null in
    /// tests and during shutdown, and the old unconditional BeginInvoke threw there.
    /// </summary>
    private static void OnUi(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            action();
        else
            dispatcher.BeginInvoke(action, priority);
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
    [NotifyPropertyChangedFor(nameof(PlayPauseLabel))]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isFavourite;

    [ObservableProperty]
    private bool _isBuffering;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage), nameof(PlayPauseLabel))]
    private string? _errorMessage;

    public bool HasErrorMessage => ErrorMessage is not null;
    public string PlayPauseLabel => HasErrorMessage ? "Retry station" : IsPlaying ? "Pause" : "Play";

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
        ({ } artist, { } title) => $"{artist} — {title}",
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
        Log.Information("station: selected {Id} {Name}", station.Id, station.Name);

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
        ErrorMessage = null;
        NowPlayingArtist = null;
        NowPlayingTitle = null;

        _ = StartPlayback(station);
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
    private async Task PlayPause()
    {
        if (_playerService.IsPlaying)
        {
            _shouldReconnect = false; // user deliberately pausing/stopping
            await _playerService.StopAsync();
        }
        else if (CurrentStation is not null)
        {
            await StartPlayback(CurrentStation);
        }
    }

    [RelayCommand]
    private async Task Stop()
    {
        _shouldReconnect = false;
        await _playerService.StopAsync();
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

    // ── Playback entry points ─────────────────────────────────────────────

    /// <summary>
    /// User-initiated start. Always proceeds, and supersedes any pending automatic
    /// reconnect — picking a station must never be ignored because a reconnect is
    /// in flight.
    /// </summary>
    private Task StartPlayback(Station station)
    {
        ErrorMessage = null;
        Interlocked.Exchange(ref _autoReconnecting, 0);
        _shouldReconnect = true;
        _playerService.Volume = IsMuted ? 0 : Volume;
        return _playerService.PlayAsync(station.StreamUrl);
    }

    /// <summary>
    /// Automatic reconnect after a network drop or a wake from sleep. At most one at
    /// a time, and never after the user has deliberately stopped.
    /// </summary>
    internal void TryAutoReconnect()
    {
        if (!_shouldReconnect || CurrentStation is null || _playerService.IsPlaying) return;
        if (Interlocked.CompareExchange(ref _autoReconnecting, 1, 0) != 0) return;

        var station = CurrentStation;
        Log.Information("player: auto-reconnecting to {Station}", station.Name);
        _playerService.Volume = IsMuted ? 0 : Volume;
        _ = _playerService.PlayAsync(station.StreamUrl);

        // The flag is normally cleared by the playback callbacks. This is the
        // backstop for a stream that never reports anything at all, so a single dead
        // reconnect cannot wedge the feature for the rest of the session.
        _ = Task.Delay(TimeSpan.FromSeconds(30))
                .ContinueWith(_ => Interlocked.Exchange(ref _autoReconnecting, 0), TaskScheduler.Default);
    }

    // ── Sleep / wake ──────────────────────────────────────────────────────

    /// <summary>Called when the system is about to sleep. Stops playback but
    /// preserves <c>_shouldReconnect</c> so we can resume on wake.</summary>
    public async Task OnSleepingAsync()
    {
        if (!_playerService.IsPlaying) return;
        Log.Information("power: sleeping, stopping playback");
        await _playerService.StopAsync();
    }

    /// <summary>Called when the system resumes from sleep. Reconnects if we were
    /// playing before sleep; waits for the network if it isn't up yet.</summary>
    public async Task OnWakingAsync()
    {
        if (!_shouldReconnect || CurrentStation is null) return;

        Log.Information("power: waking");

        // Give Windows a moment to re-establish the network after wake
        await Task.Delay(3000);

        if (_networkMonitor.IsOnline)
            TryAutoReconnect();
        // If still offline, ConnectivityChanged will reconnect when the network is back.
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
        OnUi(() =>
        {
            NowPlayingArtist = artist;
            NowPlayingTitle = title;
        });
    }

    private void OnCurrentStationPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Station.IsFavorite))
            OnUi(() => IsFavourite = CurrentStation!.IsFavorite);
    }
}
