using System.ComponentModel;

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

    public Group Group { get; set; } = null!;

    public event PropertyChangedEventHandler? PropertyChanged;
}
