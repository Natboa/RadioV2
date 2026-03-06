using RadioV2.ViewModels;
using System.Windows.Controls;

namespace RadioV2.Views;

public partial class DiscoverPage : Page
{
    public DiscoverPage(DiscoverViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await viewModel.LoadGroupsAsync();
    }
}
