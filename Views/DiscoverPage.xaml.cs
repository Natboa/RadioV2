using RadioV2.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace RadioV2.Views;

public partial class DiscoverPage : Page
{
    public DiscoverPage(DiscoverViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            // Groups: mouse-wheel passthrough (ui:Card children consume wheel events)
            GroupsScrollViewer.PreviewMouseWheel += (_, e) =>
            {
                GroupsScrollViewer.ScrollToVerticalOffset(GroupsScrollViewer.VerticalOffset - e.Delta);
                e.Handled = true;
            };

            // Groups: scroll near bottom — removed ScrollableHeight > 0 guard because
            // NavigationView gives the page unconstrained height, so GroupsScrollViewer.ScrollableHeight
            // is always 0 and the guard was preventing all scroll-triggered loads.
            GroupsScrollViewer.ScrollChanged += async (_, _) =>
            {
                if (!viewModel.IsGroupView &&
                    GroupsScrollViewer.VerticalOffset >= GroupsScrollViewer.ScrollableHeight - 300)
                    await viewModel.LoadMoreGroupsAsync();
            };

            // Groups: auto-fill after each batch — use near-bottom threshold instead of == 0
            // so that batches that add a small amount of scroll (but not enough to fill) also trigger.
            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(viewModel.IsLoading) || viewModel.IsLoading) return;
                Dispatcher.InvokeAsync(async () =>
                {
                    if (!viewModel.IsGroupView && viewModel.HasMoreGroups &&
                        GroupsScrollViewer.VerticalOffset >= GroupsScrollViewer.ScrollableHeight - 400)
                        await viewModel.LoadMoreGroupsAsync();
                }, DispatcherPriority.Background);
            };

            // Stations: the NavigationView wraps pages in a ScrollViewer (unconstrained height),
            // so the ListBox's own ScrollableHeight is always 0. Find the outer scroll viewer instead.
            // NOTE: StationsListBox.Loaded has already fired by the time Page.Loaded runs,
            // so we skip the nested Loaded subscription and query the visual tree directly.
            var stationsSv = FindParentScrollViewer(StationsListBox);
            if (stationsSv != null)
            {
                stationsSv.ScrollChanged += async (_, _) =>
                {
                    if (!viewModel.IsGroupView) return;
                    if (stationsSv.ScrollableHeight > 0 && stationsSv.VerticalOffset >= stationsSv.ScrollableHeight - 200)
                        await viewModel.LoadMoreGroupStationsAsync();
                };
            }

            await viewModel.LoadGroupsAsync();
        };
    }

    private static ScrollViewer? FindParentScrollViewer(DependencyObject child)
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is ScrollViewer sv) return sv;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }
}
