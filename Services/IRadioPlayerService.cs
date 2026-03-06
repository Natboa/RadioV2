namespace RadioV2.Services;

public interface IRadioPlayerService
{
    void Play(string streamUrl);
    void Pause();
    void Stop();
    void TogglePlayPause();

    int Volume { get; set; }
    bool IsPlaying { get; }
    bool IsPaused { get; }

    event EventHandler<string>? MetadataChanged;    // ICY StreamTitle
    event EventHandler<float>? BufferingChanged;     // 0-100%
    event EventHandler? PlaybackStarted;
    event EventHandler? PlaybackStopped;
    event EventHandler<string>? PlaybackError;       // error message
}
