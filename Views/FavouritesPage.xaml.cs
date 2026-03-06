using RadioV2.ViewModels;
using System.Windows.Controls;

namespace RadioV2.Views;

public partial class FavouritesPage : Page
{
    private readonly FavouritesViewModel _viewModel;

    public FavouritesPage(FavouritesViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await viewModel.LoadFavouritesAsync();
    }
}
