using RadioV2.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace RadioV2.Views;

public partial class DiscoverPage : Page
{
    private ScrollViewer? _stationsSv;
    private bool _stationsSvSetup;
    private bool _pageSetup;

    public DiscoverPage(DiscoverViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (!_pageSetup)
            {
                _pageSetup = true;

                // Wire station drill-down scroll viewer when IsGroupView changes
                viewModel.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(viewModel.IsGroupView) && viewModel.IsGroupView)
                    {
                        viewModel.IsAtBottom = true;
                        // Reset scroll synchronously (we're on the UI thread) before the
                        // DataBind/Normal-priority queue runs and starts populating the list.
                        // _stationsSv is null on the very first visit; the Loaded-deferred
                        // call below handles that case.
                        _stationsSv?.ScrollToTop();
                        Dispatcher.InvokeAsync(() =>
                        {
                            TrySetupStationsScrollViewer(viewModel);
                            _stationsSv?.ScrollToTop();
                        }, DispatcherPriority.Loaded);
                    }
                };

                TrySetupStationsScrollViewer(viewModel);
            }

            await viewModel.LoadCategoriesAsync();
        };
    }

    private void TrySetupStationsScrollViewer(DiscoverViewModel viewModel)
    {
        if (_stationsSvSetup) return;
        // StationsListBox now has Height="*" (fixed height) so its internal ScrollViewer
        // is constrained and virtualization is active. Find that internal ScrollViewer.
        _stationsSv = FindChildScrollViewer(StationsListBox);
        if (_stationsSv is null) return; // visual tree not ready yet; retry on next IsGroupView change

        _stationsSvSetup = true;

        _stationsSv.ScrollChanged += async (_, _) =>
        {
            if (!viewModel.IsGroupView) return;

            viewModel.IsAtBottom = _stationsSv.ScrollableHeight == 0 ||
                _stationsSv.VerticalOffset >= _stationsSv.ScrollableHeight * 0.8;
            if (!viewModel.IsLoading &&
                _stationsSv.ScrollableHeight > 0 &&
                _stationsSv.VerticalOffset > 0 &&
                _stationsSv.VerticalOffset >= _stationsSv.ScrollableHeight * 0.8)
                await viewModel.LoadMoreGroupStationsAsync();
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
}
