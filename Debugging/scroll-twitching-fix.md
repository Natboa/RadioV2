# Bug: Scroll twitching/glitching when scrolling up through stations or groups

## Symptoms

- Scrolling down through stations (Browse) or groups (Discover) and then scrolling back up caused visible jitter/twitching.
- The loading spinner (ProgressRing) would flash or jump.
- The list content would shift position during the scroll-up direction.

## Root Causes

### 1. ProgressRing layout reflow (main cause)

The ProgressRing rows were `Height="Auto"` in all pages. When `IsAtBottom` changed as the user scrolled up:

- `ShowLoadingSpinner = IsLoading && IsAtBottom` changed
- ProgressRing collapsed → its `Height="Auto"` row shrank to 0px
- The ListBox/ScrollViewer above expanded to fill reclaimed space
- `ScrollChanged` fired again with the new viewport size
- `IsAtBottom` re-evaluated → potential flip → spinner re-appeared
- Feedback loop → visible twitching

**Fix:** Changed ProgressRing `RowDefinition` from `Height="Auto"` to `Height="48"` in BrowsePage.xaml and DiscoverPage.xaml (both genre grid and station grid). The row always reserves 48px regardless of ProgressRing visibility, so showing/hiding it never causes layout reflow.

### 2. Item-based scrolling jumps

`VirtualizingStackPanel` defaults to item-based scrolling — it snaps to whole item heights when scrolling. With items ~48–64px tall this produces a jerky/twitchy feel, especially when scrolling up.

**Fix:** Added `VirtualizingStackPanel.ScrollUnit="Pixel"` to all station ListBoxes in BrowsePage.xaml and DiscoverPage.xaml. Pixel-based scrolling is smooth and continuous.

### 3. Missing `IsLoading` guard in Discover auto-fill

`GroupsScrollViewer.ScrollChanged` called `LoadMoreGroupsAsync()` without checking `!viewModel.IsLoading`. On fast scroll events this could queue concurrent load calls.

**Fix:** Added `!viewModel.IsLoading` guard in `DiscoverPage.xaml.cs` ScrollChanged handler (line 41).

## Files Changed

- `Views/BrowsePage.xaml` — ProgressRing row `Height="Auto"` → `Height="48"`; `ScrollUnit="Pixel"` on both ListBoxes
- `Views/DiscoverPage.xaml` — ProgressRing rows `Height="Auto"` → `Height="48"` (genre grid row 2, station grid row 3); `ScrollUnit="Pixel"` on StationsListBox
- `Views/DiscoverPage.xaml.cs` — Added `!viewModel.IsLoading` guard to ScrollChanged auto-fill condition
