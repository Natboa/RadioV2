namespace RadioV2.Models;

public class CategoryWithGroups
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public List<GroupWithCount> Groups { get; set; } = new();
}
