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

---

## Bug 2: Stations loaded too early (after tiny scroll)

**Symptom:** Opening a group and scrolling just a little immediately triggered the next batch — didn't need to scroll to the bottom.

**Root cause:** The threshold was `VerticalOffset >= ScrollableHeight - 200` (fixed 200px offset from bottom). When `ScrollableHeight < 200` (which happens with a short first batch), `ScrollableHeight - 200` goes negative. Any non-zero `VerticalOffset` satisfies `>= negative_number`, so even a 1px scroll triggered a load.

**Fix:** Changed to a percentage-based threshold — `VerticalOffset >= ScrollableHeight * 0.8`. Load only fires when 80% scrolled through the content, regardless of how much content is loaded.

**File:** `Views/DiscoverPage.xaml.cs` — `_stationsSv.ScrollChanged` handler.

---

## Bug 3: Two batches loaded at once when reaching the bottom

**Symptom:** Scrolling to the bottom of a group loaded two batches of stations back-to-back instead of one.

**Root cause:** `ScrollChanged` fires multiple times as WPF layout settles after items are added (each `GroupStations.Add(s)` call can trigger a layout pass → `ScrollChanged`). The VM's `_isLoadingStations` flag is set to `false` in `finally` before all layout passes complete. A `ScrollChanged` event queued on the UI thread checks `_isLoadingStations` after it's been cleared and fires a second load.

**Fix:** Added `!viewModel.IsLoading` check to the scroll handler in addition to the VM's internal flag. `IsLoading` is set synchronously to `true` before any `await` inside `LoadMoreGroupStationsAsync`, so by the time control returns to the UI thread and any queued `ScrollChanged` handlers run, the flag is already true and blocks the second call.

**File:** `Views/DiscoverPage.xaml.cs` — `_stationsSv.ScrollChanged` handler.

---

## Bug 4: First group opened — scrolling to the bottom never loads more stations; second group works fine

**Symptom:** Open the app, navigate to Discover, enter any group, scroll to the bottom — nothing loads. Go back, re-enter the same (or any other) group — infinite scroll works correctly from that point on.

**Root cause:** `StationsListBox` is inside a `Grid` whose `Visibility` is bound to `IsGroupView` via `BoolToVisibilityConverter`. While `IsGroupView = false`, that Grid is `Collapsed`. In WPF, **Collapsed elements are never measured or arranged**, which means the ListBox's ControlTemplate is never applied and its internal `ScrollViewer` does not exist in the visual tree.

`TrySetupStationsScrollViewer` was called in two places:
1. At the end of `Page.Loaded` — the Grid is still `Collapsed` here → `FindChildScrollViewer` returns `null` → `_stationsSvSetup` stays `false`.
2. In the `PropertyChanged` handler when `IsGroupView` becomes `true` — but this fires **synchronously**, before WPF has scheduled a layout pass to realize the newly visible Grid. The ListBox template still doesn't exist in the visual tree at that instant → `FindChildScrollViewer` returns `null` again → `ScrollChanged` is never subscribed.

On the **second** visit, WPF preserves the realized visual tree even after the Grid is collapsed again. So the next call to `TrySetupStationsScrollViewer` finds the ScrollViewer immediately and wires up the listener.

**Fix:** In the `PropertyChanged` handler, wrapped `TrySetupStationsScrollViewer` + `ScrollToTop` in `Dispatcher.InvokeAsync(..., DispatcherPriority.Loaded)`. `DispatcherPriority.Loaded` runs after the measure/arrange pass, so the ListBox ControlTemplate is fully applied by then and `FindChildScrollViewer` succeeds on the very first group visit.

```csharp
if (e.PropertyName == nameof(viewModel.IsGroupView) && viewModel.IsGroupView)
{
    viewModel.IsAtBottom = true;
    Dispatcher.InvokeAsync(() =>
    {
        TrySetupStationsScrollViewer(viewModel);
        _stationsSv?.ScrollToTop();
    }, DispatcherPriority.Loaded);
}
```

**File:** `Views/DiscoverPage.xaml.cs` — `PropertyChanged` handler inside `Page.Loaded`.
