using RadioV2.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace RadioV2.Views;

public partial class DiscoverPage : Page
{
    private bool _pageSetup;
    private ScrollViewer? _stationsSv;

    public DiscoverPage(DiscoverViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (!_pageSetup)
            {
                _pageSetup = true;

                // When entering a group, wait for the ListBox's internal ScrollViewer to be
                // realized (it may not exist yet if this is the first visit), then attach.
                viewModel.PropertyChanged += async (_, e) =>
                {
                    if (e.PropertyName == nameof(viewModel.IsGroupView) && viewModel.IsGroupView)
                    {
                        viewModel.IsAtBottom = true;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            var sv = FindChildScrollViewer(StationsListBox);
                            if (sv != null && !ReferenceEquals(_stationsSv, sv))
                            {
                                _stationsSv = sv;
                                _stationsSv.ScrollChanged += StationsSv_ScrollChanged;
                            }
                            _stationsSv?.ScrollToTop();
                        }, DispatcherPriority.Loaded);
                    }
                };
            }

            await viewModel.LoadCategoriesAsync();
        };
    }

    private async void StationsSv_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (DataContext is not DiscoverViewModel vm || !vm.IsGroupView) return;
        var sv = (ScrollViewer)sender;
        vm.IsAtBottom = sv.ScrollableHeight == 0 ||
            sv.VerticalOffset >= sv.ScrollableHeight * 0.8;
        if (!vm.IsLoading &&
            sv.ScrollableHeight > 0 &&
            sv.VerticalOffset > 0 &&
            sv.VerticalOffset >= sv.ScrollableHeight * 0.8)
            await vm.LoadMoreGroupStationsAsync();
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
}
