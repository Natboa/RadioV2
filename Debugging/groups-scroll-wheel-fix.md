# Bug: Scrolling doesn't work when mouse is over group cards (only works on the sides)

## From debugging.md

> scrolling up and down is not working when mouse is on the groups in the discovery page,
> when it is on the sides the scrolling works
> FIXED: ui:Card children were consuming mouse wheel events. Added PreviewMouseWheel handler on
> GroupsScrollViewer that manually scrolls and marks the event handled, so scroll works
> regardless of where the mouse is. (DiscoverPage.xaml.cs)

## Why the previous "fix" didn't work

The previous fix added:
```csharp
GroupsScrollViewer.PreviewMouseWheel += (_, e) =>
{
    GroupsScrollViewer.ScrollToVerticalOffset(GroupsScrollViewer.VerticalOffset - e.Delta);
    e.Handled = true;
};
```

This correctly intercepts the mouse wheel event before `ui:Card` can consume it.
**But `GroupsScrollViewer.ScrollToVerticalOffset` does nothing** because:

- NavigationView wraps every page in an outer `ScrollViewer` with unconstrained height
- `GroupsScrollViewer.ScrollableHeight` is always **0** (extent == viewport, no overflow)
- Calling `ScrollToVerticalOffset` on a scrollviewer with no scrollable area is a no-op
- The actual page scrolling is done by the **outer NavigationView ScrollViewer**

When the mouse is over the **sides** (outside the `GroupsScrollViewer` hit area), the wheel
event bubbles up to the outer ScrollViewer directly, which is why scrolling works there.

## Root Cause

`GroupsScrollViewer.ScrollToVerticalOffset` targets the wrong scroll viewer.
The outer NavigationView `ScrollViewer` needs to be scrolled instead.

## Fix (applied to `Views/DiscoverPage.xaml.cs`)

1. Find the outer ScrollViewer at Loaded time using `FindParentScrollViewer(GroupsScrollViewer)`
2. Change `PreviewMouseWheel` to call `ScrollToVerticalOffset` on the **outer** scroll viewer
3. Also hook the outer scroll viewer's `ScrollChanged` for groups infinite scroll detection
   (so `LoadMoreGroupsAsync` is triggered when the user actually scrolls near the bottom)

```csharp
var groupsOuterSv = FindParentScrollViewer(GroupsScrollViewer);

GroupsScrollViewer.PreviewMouseWheel += (_, e) =>
{
    if (groupsOuterSv != null)
        groupsOuterSv.ScrollToVerticalOffset(groupsOuterSv.VerticalOffset - e.Delta);
    e.Handled = true;
};

if (groupsOuterSv != null)
{
    groupsOuterSv.ScrollChanged += async (_, _) =>
    {
        if (viewModel.IsGroupView) return;
        if (groupsOuterSv.VerticalOffset >= groupsOuterSv.ScrollableHeight - 300)
            await viewModel.LoadMoreGroupsAsync();
    };
}
```

## Files Changed

- `Views/DiscoverPage.xaml.cs`
