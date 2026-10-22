namespace RadioV2.Services;

public interface IRadioPlayerService
{
    /// <summary>Queues a stream to play. Returns once the command has been executed
    /// on the player thread, not once audio is flowing — listen for
    /// <see cref="PlaybackStarted"/> for that.</summary>
    Task PlayAsync(string streamUrl);

    /// <summary>Queues a stop. Never blocks the caller, however long VLC's own
    /// teardown takes.</summary>
    Task StopAsync();

    int Volume { get; set; }
    bool IsPlaying { get; }

    event EventHandler<string>? MetadataChanged;    // ICY StreamTitle
    event EventHandler<float>? BufferingChanged;     // 0-100%
    event EventHandler? PlaybackStarted;
    event EventHandler? PlaybackStopped;
    event EventHandler<string>? PlaybackError;       // error message
}
