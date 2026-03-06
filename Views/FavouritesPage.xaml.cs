using RadioV2.ViewModels;
using System.Windows.Controls;

namespace RadioV2.Views;

public partial class FavouritesPage : Page
{
    public FavouritesPage(FavouritesViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
