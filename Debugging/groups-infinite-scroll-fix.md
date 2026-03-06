# Bug: Groups panel only shows first 30 groups — no infinite scroll

## Root Cause (revised — second investigation)

The `DiscoverPage.xaml.cs` code-behind **already had** `GroupsScrollViewer.ScrollChanged` and
`PropertyChanged` handlers. The XAML behavior added in the first fix was redundant.

The real bug: **NavigationView wraps all pages in an outer `ScrollViewer` with unconstrained
height.** This means `GroupsScrollViewer.ScrollableHeight` is always **0** — the outer scroll
viewer does all the actual scrolling.

The existing `ScrollChanged` handler had this guard:

```csharp
if (!viewModel.IsGroupView &&
    GroupsScrollViewer.ScrollableHeight > 0 &&   // ← always FALSE
    GroupsScrollViewer.VerticalOffset >= GroupsScrollViewer.ScrollableHeight - 300)
```

Because `ScrollableHeight > 0` is always false, the scroll handler **never fired**.

The `PropertyChanged` auto-fill handler did check `ScrollableHeight == 0` and would have
auto-loaded more groups — but it only covered the "no scroll at all" case. If a small amount
of scroll existed (e.g. 100px), the `== 0` check failed and auto-fill stopped.

## Fix (applied to `Views/DiscoverPage.xaml.cs`)

1. **Removed `ScrollableHeight > 0` guard** from `ScrollChanged` handler — let it always
   evaluate the near-bottom condition (`VerticalOffset >= ScrollableHeight - 300`).
   When `ScrollableHeight = 0`, any `ScrollChanged` event satisfies `0 >= -300` → loads more.

2. **Broadened auto-fill condition** in `PropertyChanged` handler from `ScrollableHeight == 0`
   to `VerticalOffset >= ScrollableHeight - 400` — covers both zero-scroll and small-scroll cases.

## First Fix (still present, harmless)

`ScrollViewerInfiniteScrollBehavior` was added to `InfiniteScrollBehavior.cs` and attached in
`DiscoverPage.xaml`. This calls `LoadMoreGroupsCommand` on scroll — redundant with the code-behind
but not harmful (`IsLoading` guard prevents double loads).

## Files Changed

- `Helpers/InfiniteScrollBehavior.cs` — `ScrollViewerInfiniteScrollBehavior` added
- `Views/DiscoverPage.xaml` — behavior attached (redundant but harmless)
- `Views/DiscoverPage.xaml.cs` — **main fix**: removed `ScrollableHeight > 0` guard, broadened auto-fill threshold
