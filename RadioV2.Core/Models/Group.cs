namespace RadioV2.Models;

public class Group
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    public ICollection<Station> Stations { get; set; } = new List<Station>();
}
