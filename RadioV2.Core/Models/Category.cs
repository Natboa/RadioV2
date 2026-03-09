namespace RadioV2.Models;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }

    public ICollection<Group> Groups { get; set; } = new List<Group>();
}
