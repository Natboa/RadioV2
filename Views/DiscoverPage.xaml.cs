using RadioV2.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace RadioV2.Views;

public partial class DiscoverPage : Page
{
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
                        StationsScrollViewer.ScrollToTop();
                    }
                };

                StationsScrollViewer.ScrollChanged += async (_, _) =>
                {
                    if (!viewModel.IsGroupView) return;

                    viewModel.IsAtBottom = StationsScrollViewer.ScrollableHeight == 0 ||
                        StationsScrollViewer.VerticalOffset >= StationsScrollViewer.ScrollableHeight * 0.8;
                    if (!viewModel.IsLoading &&
                        StationsScrollViewer.ScrollableHeight > 0 &&
                        StationsScrollViewer.VerticalOffset > 0 &&
                        StationsScrollViewer.VerticalOffset >= StationsScrollViewer.ScrollableHeight * 0.8)
                        await viewModel.LoadMoreGroupStationsAsync();
                };
            }

            await viewModel.LoadCategoriesAsync();
        };
    }
}
