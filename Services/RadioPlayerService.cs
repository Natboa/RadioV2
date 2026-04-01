using LibVLCSharp.Shared;

namespace RadioV2.Services;

public class RadioPlayerService : IRadioPlayerService, IDisposable
{
    private readonly LibVLC _libVLC;
    private readonly MediaPlayer _mediaPlayer;
    private Media? _currentMedia;

    public RadioPlayerService()
    {
        Core.Initialize();
        _libVLC = new LibVLC("--no-video");
        _mediaPlayer = new MediaPlayer(_libVLC);

        _mediaPlayer.Playing += (s, e) => PlaybackStarted?.Invoke(this, EventArgs.Empty);
        _mediaPlayer.Paused  += (s, e) => PlaybackStopped?.Invoke(this, EventArgs.Empty);
        _mediaPlayer.Stopped += (s, e) => PlaybackStopped?.Invoke(this, EventArgs.Empty);
        _mediaPlayer.Buffering += (s, e) => BufferingChanged?.Invoke(this, e.Cache);
        _mediaPlayer.EncounteredError += (s, e) => PlaybackError?.Invoke(this, "Stream error");

        // ICY metadata: MediaChanged fires when a new station is loaded;
        // MetaChanged fires repeatedly as the station updates track info.
        // Capture the media instance in the closure so MetaChanged always reads
        // from the correct station, even if the user switches mid-stream.
        _mediaPlayer.MediaChanged += (s, e) =>
        {
            var media = _mediaPlayer.Media;
            if (media != null)
            {
                media.MetaChanged += (ms, me) =>
                {
                    if (me.MetadataType == MetadataType.NowPlaying)
                    {
                        var raw = media.Meta(MetadataType.NowPlaying);
                        if (!string.IsNullOrWhiteSpace(raw))
                            MetadataChanged?.Invoke(this, raw);
                    }
                };
            }
        };
    }

    public void Play(string streamUrl)
    {
        var old = _currentMedia;
        _currentMedia = new Media(_libVLC, streamUrl, FromType.FromLocation);
        _mediaPlayer.Play(_currentMedia);
        Task.Run(() => old?.Dispose());
    }

    public void Pause() => _mediaPlayer.Pause();
    public void Stop() => _mediaPlayer.Stop();

    public void TogglePlayPause()
    {
        if (IsPlaying) Pause();
        else if (IsPaused) _mediaPlayer.Play();
    }

    public int Volume
    {
        get => _mediaPlayer.Volume;
        set => _mediaPlayer.Volume = Math.Clamp(value, 0, 100);
    }

    public bool IsPlaying => _mediaPlayer.IsPlaying;
    public bool IsPaused => _mediaPlayer.State == VLCState.Paused;

    public event EventHandler<string>? MetadataChanged;
    public event EventHandler<float>? BufferingChanged;
    public event EventHandler? PlaybackStarted;
    public event EventHandler? PlaybackStopped;
    public event EventHandler<string>? PlaybackError;

    public void Dispose()
    {
        _mediaPlayer.Dispose();
        _currentMedia?.Dispose();
        _libVLC.Dispose();
    }
}
