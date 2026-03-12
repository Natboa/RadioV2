using RadioV2.ViewModels;
using System.Windows;
using System.Windows.Controls;
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
        _stationsSv = GroupScrollViewer;
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
}
