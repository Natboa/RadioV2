namespace RadioV2.Models;

public class Station
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public int GroupId { get; set; }
    public bool IsFavorite { get; set; }

    public Group Group { get; set; } = null!;
}
