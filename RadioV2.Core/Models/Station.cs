using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace RadioV2.Models;

public class Station : INotifyPropertyChanged
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public int GroupId { get; set; }

    private bool _isFavorite;
    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value) return;
            _isFavorite = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFavorite)));
        }
    }

    private bool _isFeatured;
    public bool IsFeatured
    {
        get => _isFeatured;
        set
        {
            if (_isFeatured == value) return;
            _isFeatured = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFeatured)));
        }
    }

    private bool _isNowPlaying;
    [NotMapped]
    public bool IsNowPlaying
    {
        get => _isNowPlaying;
        set
        {
            if (_isNowPlaying == value) return;
            _isNowPlaying = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNowPlaying)));
        }
    }

    [NotMapped]
    private string? _cachedLogoPath;

    [NotMapped]
    public string? CachedLogoPath
    {
        get => _cachedLogoPath;
        set
        {
            if (_cachedLogoPath == value) return;
            _cachedLogoPath = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CachedLogoPath)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayLogoSource)));
        }
    }

    /// <summary>Local cache path when available, otherwise the remote URL.</summary>
    [NotMapped]
    public string? DisplayLogoSource =>
        _cachedLogoPath != null ? new Uri(_cachedLogoPath).AbsoluteUri : LogoUrl;

    public Group Group { get; set; } = null!;

    public void NotifyLogoChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LogoUrl)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayLogoSource)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
