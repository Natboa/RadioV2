# Bug: Discover Page — Stations Not Loading More When Scrolling

**Status:** CONFIRMED FIXED (Attempt 6) ✓
**Page:** Discover → inside a group (station list)
**Symptom:** First 100 stations load correctly. Scrolling to the bottom does nothing — no additional stations load.

---

## Root Cause Analysis

The WPF-UI `NavigationView` wraps page content in a `ScrollViewer`, giving pages **unconstrained height**. This means:
- The stations `ListBox`'s internal `ScrollViewer.ScrollableHeight` is always **0** (all items render without needing to scroll within the ListBox itself)
- The actual scrolling happens in the **outer NavigationView ScrollViewer**, not the ListBox
- Any approach checking the ListBox's inner `ScrollableHeight > 0` will never trigger

---

## Attempts (Chronological)

### Attempt 1 — InfiniteScrollBehavior via XAML binding
**What:** Used `InfiniteScrollBehavior` on the `StationsListBox` with a `RelativeSource AncestorType=Page` binding in XAML.
**Why it failed:** The `RelativeSource` binding silently fails when used inside a `Behavior<T>` attached to a nested element — the behavior has no logical tree parent to traverse. `LoadMoreCommand` is null, so `CanExecute` is false and nothing fires.

### Attempt 2 — PropertyChanged → check inner ScrollableHeight
**What:** In `DiscoverPage.xaml.cs`, watched `viewModel.IsLoading` via `PropertyChanged`. When `IsLoading` became false, called `FindScrollViewer(StationsListBox)` and checked `sv.ScrollableHeight == 0` to decide whether to load more.
**Why it failed:** Two issues:
1. Items are added while `IsLoading = true`. WPF layout fires `ScrollChanged` at that moment (blocked). When `IsLoading = false`, no new `ScrollChanged` fires — the chain breaks.
2. `ScrollableHeight` is stale (0) because the check ran before the layout pass settled. This caused an infinite load loop (all stations loaded at once).

### Attempt 3 — PropertyChanged + DispatcherPriority.Background deferral
**What:** Same as Attempt 2 but wrapped in `Dispatcher.InvokeAsync(..., DispatcherPriority.Background)` to let layout settle before reading `ScrollableHeight`.
**Why it failed:** `Background` priority runs after `Render` (layout), so `ScrollableHeight` is accurate. But for stations, it is **still 0** — because the ListBox container is unconstrained (outer NavigationView ScrollViewer). The auto-fill loop loaded all stations before stopping.

### Attempt 4 — StationsListBox.Loaded → FindScrollViewer (inner)
**What:** On `StationsListBox.Loaded`, called `FindScrollViewer(StationsListBox)` (depth-first visual tree traversal downward) to get the ListBox's internal ScrollViewer. Hooked `ScrollChanged` on it.
**Why it failed:** Inner `ScrollableHeight == 0` always (unconstrained container). The condition `sv.ScrollableHeight > 0` never becomes true. Handler never fires.

### Attempt 5 — StationsListBox.Loaded → FindParentScrollViewer (outer) ← CURRENT
**What:** On `StationsListBox.Loaded`, traversed **up** the visual tree with `VisualTreeHelper.GetParent` to find the nearest ancestor `ScrollViewer` (the NavigationView's outer one). Hook `ScrollChanged` on it with `!viewModel.IsGroupView` guard.
**Status:** Fixed — see Attempt 6.

**Code (DiscoverPage.xaml.cs):**
```csharp
StationsListBox.Loaded += (_, _) =>
{
    var sv = FindParentScrollViewer(StationsListBox);
    if (sv == null) return;
    sv.ScrollChanged += async (_, _) =>
    {
        if (!viewModel.IsGroupView) return;
        if (sv.ScrollableHeight > 0 && sv.VerticalOffset >= sv.ScrollableHeight - 200)
            await viewModel.LoadMoreGroupStationsAsync();
    };
};

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
```

### Attempt 6 — Remove nested `StationsListBox.Loaded` wrapper ← CURRENT FIX
**What:** Attempt 5's `ScrollChanged` hook was wrapped in `StationsListBox.Loaded += ...` inside the `Page.Loaded` handler. By the time `Page.Loaded` fires, all children (including `StationsListBox`) have already fired their own `Loaded` events. Subscribing to `Loaded` at that point is too late — the handler never executes, `FindParentScrollViewer` is never called, and no `ScrollChanged` hook is ever wired up.

**Fix:** Removed the `StationsListBox.Loaded` wrapper. Call `FindParentScrollViewer(StationsListBox)` directly inside `Page.Loaded` — the ListBox is already in the visual tree at that point, so the traversal works immediately.

---

## Other Changes Made During This Bug Hunt

- `IStationService` / `StationService`: added `skip`/`take` to `GetGroupsWithCountsAsync` (groups pagination)
- `DiscoverViewModel`: added `_groupsSkip`, `HasMoreGroups`, `LoadMoreGroupsAsync` command
- Batch size for stations increased from 50 → 100 (guarantee viewport overflow on first load)
- `DiscoverPage.xaml.cs`: groups auto-fill via `PropertyChanged` + `DispatcherPriority.Background` (working)
- `DiscoverPage.xaml.cs`: groups manual scroll via `GroupsScrollViewer.ScrollChanged` (working)
- Removed `InfiniteScrollBehavior` from stations `ListBox` in XAML (replaced with code-behind)

---

## Fix

**Root cause:** `StationsListBox.Loaded` was subscribed inside `Page.Loaded`. WPF fires child `Loaded` events before the parent's, so by the time the `Page.Loaded` handler runs, `StationsListBox.Loaded` has already fired. The late subscription is never invoked → `FindParentScrollViewer` never runs → no `ScrollChanged` hook → infinite scroll silently does nothing.

**Fix (Attempt 6):** Removed the `StationsListBox.Loaded += ...` wrapper. Since `Page.Loaded` fires after all children are already loaded and in the visual tree, calling `FindParentScrollViewer(StationsListBox)` directly inside `Page.Loaded` works correctly. The outer NavigationView `ScrollViewer` is found, and the `ScrollChanged` handler is wired up successfully.
