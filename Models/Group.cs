namespace RadioV2.Models;

public class Group
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public ICollection<Station> Stations { get; set; } = new List<Station>();
}
