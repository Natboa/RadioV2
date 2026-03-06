using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RadioV2.ViewModels;

public partial class MiniPlayerViewModel : ObservableObject
{
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
    private int _volume = 50;

    [ObservableProperty]
    private bool _isMuted;

    public string? NowPlayingDisplay => (NowPlayingArtist, NowPlayingTitle) switch
    {
        ({ } artist, { } title) => $"{artist} \u2014 {title}",
        (null, { } title) => title,
        _ => null
    };

    public bool HasStation => !string.IsNullOrEmpty(StationName);

    public bool HasNowPlaying => NowPlayingDisplay is not null;

    [RelayCommand]
    private void PlayPause() { }

    [RelayCommand]
    private void Stop() { }

    [RelayCommand]
    private void NextStation() { }

    [RelayCommand]
    private void PreviousStation() { }

    [RelayCommand]
    private void ToggleFavourite() { }

    [RelayCommand]
    private void ToggleMute() { }
}
