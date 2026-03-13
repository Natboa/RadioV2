using RadioV2.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace RadioV2.Views;

public partial class BrowsePage : Page
{
    private readonly BrowseViewModel _viewModel;
    private ScrollViewer? _stationSv;
    private bool _stationSvSetup;
    private bool _pageSetup;

    public BrowsePage(BrowseViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (!_pageSetup)
            {
                _pageSetup = true;
                viewModel.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(viewModel.IsRecentVisible) && !viewModel.IsRecentVisible)
                    {
                        Dispatcher.InvokeAsync(() => TrySetupStationsScrollViewer(viewModel), DispatcherPriority.Loaded);
                    }
                };
                TrySetupStationsScrollViewer(viewModel);
            }

            if (!viewModel.IsRecentVisible)
                await viewModel.LoadMoreAsync();
        };
    }

    private void TrySetupStationsScrollViewer(BrowseViewModel viewModel)
    {
        if (_stationSvSetup) return;
        _stationSv = FindChildScrollViewer(StationListBox);
        if (_stationSv is null) return;

        _stationSvSetup = true;

        _stationSv.ScrollChanged += async (_, _) =>
        {
            if (viewModel.IsRecentVisible) return;
            viewModel.IsAtBottom = _stationSv.ScrollableHeight == 0 ||
                _stationSv.VerticalOffset >= _stationSv.ScrollableHeight * 0.8;
            if (!viewModel.IsLoading && viewModel.HasMoreItems &&
                _stationSv.ScrollableHeight > 0 &&
                _stationSv.VerticalOffset > 0 &&
                _stationSv.VerticalOffset >= _stationSv.ScrollableHeight * 0.8)
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
        => Dispatcher.BeginInvoke(DispatcherPriority.Background,
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
