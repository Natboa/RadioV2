using System.Collections.Concurrent;
using System.Diagnostics;
using LibVLCSharp.Shared;
using RadioV2.Helpers;
using Serilog;

namespace RadioV2.Services;

/// <summary>
/// Owns LibVLC and serialises every call into it onto one dedicated thread.
///
/// <para>
/// Two problems drove this design. First, <c>libvlc_media_player_stop</c> is
/// synchronous in VLC 3.x: it blocks until the input thread tears down, which on a
/// stalled stream is seconds. It used to be called straight from the UI thread by the
/// pause button, the network-loss handler and — worst — from inside <c>WndProc</c>
/// while Windows was trying to suspend. Second, media objects were released on a
/// thread-pool thread while VLC could still be raising events on them, a
/// use-after-free on the hot path of every station switch.
/// </para>
///
/// <para>
/// A queue rather than a task per call, because ordering matters: a stop racing a
/// play must not interleave. A real <see cref="Thread"/> rather than the thread pool,
/// because the work blocks and because keeping every native call on one thread is
/// what removes the race. Native initialisation is the thread's first job, so it
/// stays off the startup path too; commands queued before it finishes simply wait.
/// </para>
/// </summary>
public class RadioPlayerService : IRadioPlayerService, IDisposable
{
    private readonly BlockingCollection<PlayerCommand> _commands = new();
    private readonly Thread _worker;

    // Touched only on the player thread.
    private LibVLC? _libVLC;
    private MediaPlayer? _mediaPlayer;
    private Media? _currentMedia;
    private bool _initFailed;

    private volatile bool _isPlaying;
    private volatile int _volume = 50;

    public RadioPlayerService()
    {
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "RadioV2.Player",
        };
        _worker.Start();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public Task PlayAsync(string streamUrl) => Enqueue(new PlayCommand(streamUrl));

    public Task StopAsync() => Enqueue(new StopCommand());

    public int Volume
    {
        get => _volume;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            _volume = clamped;
            _ = Enqueue(new VolumeCommand(clamped));
        }
    }

    /// <summary>Tracked from VLC's own events rather than queried, so callers never
    /// make a native call from the UI thread just to ask.</summary>
    public bool IsPlaying => _isPlaying;

    public event EventHandler<string>? MetadataChanged;
    public event EventHandler<float>? BufferingChanged;
    public event EventHandler? PlaybackStarted;
    public event EventHandler? PlaybackStopped;
    public event EventHandler<string>? PlaybackError;

    // ── Queue ─────────────────────────────────────────────────────────────────

    private Task Enqueue(PlayerCommand command)
    {
        try
        {
            _commands.Add(command);
        }
        catch (InvalidOperationException)
        {
            command.Completion.TrySetResult(); // shutting down
        }

        return command.Completion.Task;
    }

    private void WorkerLoop()
    {
        Initialise();

        foreach (var command in _commands.GetConsumingEnumerable())
        {
            try
            {
                switch (command)
                {
                    case PlayCommand play: ExecutePlay(play.Url); break;
                    case StopCommand: ExecuteStop(); break;
                    case VolumeCommand volume: ExecuteVolume(volume.Level); break;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "player: {Command} failed", command.GetType().Name);
            }
            finally
            {
                // Always completes, never faults. Callers fire and forget, so a
                // faulted task would surface as an unobserved exception rather than
                // anything useful; failures reach the app via PlaybackError.
                command.Completion.TrySetResult();
            }
        }

        Cleanup();
    }

    // ── Player thread ─────────────────────────────────────────────────────────

    private void Initialise()
    {
        try
        {
            var sw = Stopwatch.StartNew();

            Core.Initialize();
            _libVLC = new LibVLC("--no-video");
            _libVLC.Log += OnLibVlcLog;

            _mediaPlayer = new MediaPlayer(_libVLC) { Volume = _volume };
            _mediaPlayer.Playing += (_, _) =>
            {
                _isPlaying = true;
                Log.Information("player: playing");
                PlaybackStarted?.Invoke(this, EventArgs.Empty);
            };
            _mediaPlayer.Stopped += (_, _) =>
            {
                _isPlaying = false;
                Log.Information("player: stopped");
                PlaybackStopped?.Invoke(this, EventArgs.Empty);
            };
            _mediaPlayer.EncounteredError += (_, _) =>
            {
                _isPlaying = false;
                Log.Warning("player: encountered error");
                PlaybackError?.Invoke(this, "Stream error");
            };
            _mediaPlayer.Buffering += (_, e) => BufferingChanged?.Invoke(this, e.Cache);

            Log.Information("player: LibVLC ready in {Elapsed} ms", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _initFailed = true;
            Log.Fatal(ex, "player: LibVLC initialisation failed — playback unavailable");
        }
    }

    private bool EnsureReady()
    {
        if (!_initFailed && _mediaPlayer is not null) return true;

        PlaybackError?.Invoke(this, "Audio engine unavailable");
        return false;
    }

    private void ExecutePlay(string url)
    {
        if (!EnsureReady()) return;

        var previous = _currentMedia;
        if (previous is not null)
            previous.MetaChanged -= OnMediaMetaChanged;

        var media = new Media(_libVLC!, url, FromType.FromLocation);
        media.MetaChanged += OnMediaMetaChanged;
        _currentMedia = media;

        Log.Information("player: play {Url}", LogRedaction.Url(url));
        _mediaPlayer!.Play(media);

        // Released only after the new media is playing, and on this thread. The old
        // code released it from a pool thread while VLC could still be raising
        // MetaChanged on it.
        previous?.Dispose();
    }

    private void ExecuteStop()
    {
        // Synchronous in VLC 3.x — blocks until the input thread is gone. That is
        // precisely why it runs here and not on the dispatcher.
        _mediaPlayer?.Stop();
    }

    private void ExecuteVolume(int level)
    {
        if (_mediaPlayer is not null)
            _mediaPlayer.Volume = level;
    }

    private void Cleanup()
    {
        try
        {
            if (_currentMedia is not null) _currentMedia.MetaChanged -= OnMediaMetaChanged;
            if (_libVLC is not null) _libVLC.Log -= OnLibVlcLog;

            _mediaPlayer?.Dispose();
            _currentMedia?.Dispose();
            _libVLC?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "player: cleanup failed");
        }
    }

    // ── VLC callbacks ─────────────────────────────────────────────────────────

    private void OnMediaMetaChanged(object? sender, MediaMetaChangedEventArgs e)
    {
        if (e.MetadataType != MetadataType.NowPlaying) return;
        if (sender is not Media media) return;

        // Ignore metadata arriving from a station the user has already left; the old
        // handler would happily overwrite the new station's track with the old one's.
        if (!ReferenceEquals(media, _currentMedia)) return;

        var raw = media.Meta(MetadataType.NowPlaying);
        if (!string.IsNullOrWhiteSpace(raw))
            MetadataChanged?.Invoke(this, raw);
    }

    /// <summary>
    /// Forwards LibVLC's own log into Serilog. VLC's Debug and Notice levels are
    /// extremely chatty while streaming — hundreds of lines a second — so both map to
    /// Serilog Debug, which release builds drop. Release logs therefore carry only
    /// VLC's warnings and errors, which is exactly what a dead stream produces.
    /// </summary>
    private static void OnLibVlcLog(object? sender, LogEventArgs e)
    {
        switch (e.Level)
        {
            case LogLevel.Error:
                Log.Error("vlc[{Module}] {Message}", e.Module, LogRedaction.Scrub(e.Message));
                break;
            case LogLevel.Warning:
                Log.Warning("vlc[{Module}] {Message}", e.Module, LogRedaction.Scrub(e.Message));
                break;
            default:
                Log.Debug("vlc[{Module}] {Message}", e.Module, LogRedaction.Scrub(e.Message));
                break;
        }
    }

    // ── Shutdown ──────────────────────────────────────────────────────────────

    private int _disposed;

    public void Dispose()
    {
        // Idempotent: a stress run that disposed explicitly and then again via `using`
        // threw ObjectDisposedException out of CompleteAdding. Dispose is allowed to
        // be called twice, and during shutdown that must not throw.
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        _commands.CompleteAdding();

        // Bounded: a hung VLC teardown must not hold up process exit.
        if (!_worker.Join(TimeSpan.FromSeconds(5)))
            Log.Warning("player: worker thread did not finish within 5 s");

        _commands.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    private abstract class PlayerCommand
    {
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class PlayCommand(string url) : PlayerCommand
    {
        public string Url { get; } = url;
    }

    private sealed class StopCommand : PlayerCommand;

    private sealed class VolumeCommand(int level) : PlayerCommand
    {
        public int Level { get; } = level;
    }
}
