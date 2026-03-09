using RadioV2.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RadioV2.Views;

public partial class BrowsePage : Page
{
    private readonly BrowseViewModel _viewModel;

    public BrowsePage(BrowseViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            var sv = FindChildScrollViewer(StationListBox);
            if (sv != null)
            {
                sv.ScrollChanged += (_, _) =>
                {
                    viewModel.IsAtBottom = sv.ScrollableHeight == 0 ||
                        sv.VerticalOffset >= sv.ScrollableHeight - 200;
                };
            }
            await viewModel.LoadMoreAsync();
        };
    }

    private static ScrollViewer? FindChildScrollViewer(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv) return sv;
            var result = FindChildScrollViewer(child);
            if (result != null) return result;
        }
        return null;
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

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox lb && lb.SelectedItem is string query)
        {
            lb.SelectedItem = null;
            _viewModel.SelectHistoryItem(query);
        }
    }
}
