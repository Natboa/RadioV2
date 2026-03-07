using RadioV2.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace RadioV2.Views;

public partial class BrowsePage : Page
{
    private readonly BrowseViewModel _viewModel;

    public BrowsePage(BrowseViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await viewModel.LoadMoreAsync();
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        => _viewModel.ShowHistory();

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
            () => _viewModel.HideHistory());

    private void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
            _viewModel.SaveCurrentQueryToHistory();
    }
}
