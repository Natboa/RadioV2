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

    public DiscoverPage(DiscoverViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            // ── GENRES VIEW ──────────────────────────────────────────────────────────
            // GroupsScrollViewer is now the actual scroll container (outer NavigationView
            // DynamicScrollViewer is disabled via App.xaml template override).
            // ui:Card children still consume PreviewMouseWheel, so intercept it here
            // and forward directly to GroupsScrollViewer.
            GroupsScrollViewer.PreviewMouseWheel += (_, e) =>
            {
                GroupsScrollViewer.ScrollToVerticalOffset(
                    GroupsScrollViewer.VerticalOffset - e.Delta);
                e.Handled = true;
            };

            // Auto-fill: keep loading batches until the viewport has enough content
            // to scroll (avoids a screen that is only partially filled on first load).
            GroupsScrollViewer.ScrollChanged += async (_, _) =>
            {
                if (!viewModel.IsGroupView && viewModel.HasMoreGroups &&
                    GroupsScrollViewer.ScrollableHeight < 400)
                    await viewModel.LoadMoreGroupsAsync();
            };

            // ── STATIONS VIEW ─────────────────────────────────────────────────────────
            viewModel.PropertyChanged += (_, e) =>
            {
                // Switching into station view: scroll to top and ensure SV is wired.
                if (e.PropertyName == nameof(viewModel.IsGroupView) && viewModel.IsGroupView)
                {
                    TrySetupStationsScrollViewer(viewModel);
                    _stationsSv?.ScrollToTop();
                }

                // After each genre batch finishes loading, auto-fill if still not full.
                if (e.PropertyName == nameof(viewModel.IsLoading) && !viewModel.IsLoading)
                {
                    Dispatcher.InvokeAsync(async () =>
                    {
                        if (!viewModel.IsGroupView && viewModel.HasMoreGroups &&
                            GroupsScrollViewer.ScrollableHeight < 400)
                            await viewModel.LoadMoreGroupsAsync();
                    }, DispatcherPriority.Background);
                }
            };

            // Try wiring up stations SV immediately (ListBox template is usually applied
            // even when its parent Grid is Collapsed).
            TrySetupStationsScrollViewer(viewModel);

            await viewModel.LoadGroupsAsync();
        };
    }

    /// <summary>
    /// Finds the ListBox's internal ScrollViewer and hooks it for infinite station loading.
    /// Called on Page.Loaded and again when IsGroupView becomes true (stations view opens).
    /// </summary>
    private void TrySetupStationsScrollViewer(DiscoverViewModel viewModel)
    {
        if (_stationsSvSetup) return;
        _stationsSv = FindChildScrollViewer(StationsListBox);
        if (_stationsSv == null) return;
        _stationsSvSetup = true;

        _stationsSv.ScrollChanged += async (_, _) =>
        {
            if (!viewModel.IsGroupView) return;
            // Require VerticalOffset > 0 so loading doesn't trigger at the top.
            // Use 80% of ScrollableHeight so we only load when genuinely near the
            // bottom — fixed 200px threshold was too small when ScrollableHeight < 200.
            if (!viewModel.IsLoading &&
                _stationsSv.ScrollableHeight > 0 &&
                _stationsSv.VerticalOffset > 0 &&
                _stationsSv.VerticalOffset >= _stationsSv.ScrollableHeight * 0.8)
                await viewModel.LoadMoreGroupStationsAsync();
        };
    }

    /// <summary>Walks the visual tree downward to find the first ScrollViewer.</summary>
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
