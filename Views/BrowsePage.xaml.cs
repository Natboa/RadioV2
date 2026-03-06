using RadioV2.ViewModels;
using System.Windows.Controls;

namespace RadioV2.Views;

public partial class BrowsePage : Page
{
    public BrowsePage(BrowseViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
