using CommunityToolkit.Mvvm.Input;
using RadioV2.Models;

namespace RadioV2.ViewModels;

public class CarouselRowViewModel
{
    public int CategoryId { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public List<GroupWithCount> Groups { get; init; } = [];

    /// <summary>
    /// Injected from DiscoverViewModel so cards can trigger navigation
    /// without needing a reference to the page.
    /// </summary>
    public IRelayCommand<GroupWithCount> SelectGroupCommand { get; init; } = null!;
}
