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
            // Groups: NavigationView gives unconstrained height so GroupsScrollViewer.ScrollableHeight
            // is always 0. Find the outer NavigationView ScrollViewer — that is what actually scrolls.
            var groupsOuterSv = FindParentScrollViewer(GroupsScrollViewer);

            // Groups: intercept wheel before ui:Card children consume it, forward to outer SV.
            GroupsScrollViewer.PreviewMouseWheel += (_, e) =>
            {
                if (groupsOuterSv != null)
                    groupsOuterSv.ScrollToVerticalOffset(groupsOuterSv.VerticalOffset - e.Delta);
                e.Handled = true;
            };

            // Groups: infinite scroll on the outer SV — fires on actual user scroll.
            if (groupsOuterSv != null)
            {
                groupsOuterSv.ScrollChanged += async (_, _) =>
                {
                    if (viewModel.IsGroupView) return;
                    if (groupsOuterSv.VerticalOffset >= groupsOuterSv.ScrollableHeight - 300)
                        await viewModel.LoadMoreGroupsAsync();
                };
            }

            // Groups: auto-fill — GroupsScrollViewer.ScrollChanged fires when extent grows
            // as items are added (even with ScrollableHeight == 0), keeping viewport filled.
            GroupsScrollViewer.ScrollChanged += async (_, _) =>
            {
                if (!viewModel.IsGroupView &&
                    GroupsScrollViewer.VerticalOffset >= GroupsScrollViewer.ScrollableHeight - 300)
                    await viewModel.LoadMoreGroupsAsync();
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

            // Groups: auto-fill after each batch — use near-bottom threshold instead of == 0
            // so that batches that add a small amount of scroll (but not enough to fill) also trigger.
            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(viewModel.IsGroupView) && viewModel.IsGroupView)
                    stationsSv?.ScrollToTop();

                if (e.PropertyName != nameof(viewModel.IsLoading) || viewModel.IsLoading) return;
                Dispatcher.InvokeAsync(async () =>
                {
                    if (!viewModel.IsGroupView && viewModel.HasMoreGroups &&
                        GroupsScrollViewer.VerticalOffset >= GroupsScrollViewer.ScrollableHeight - 400)
                        await viewModel.LoadMoreGroupsAsync();
                }, DispatcherPriority.Background);
            };

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
