# Scroll Freeze + Featured Pinning — Dual Bug Fix

## Bug Description

Two bugs that kept recurring together, each fix breaking the other:

1. **Scroll freeze**: When scrolling down in a group's station list and more stations loaded
   (infinite scroll), the UI froze for ~200–500 ms per batch.

2. **Featured section pinned**: After fixing the scroll freeze, the "Featured" section stayed
   stuck to the top of the viewport while the station list scrolled beneath it — instead of
   scrolling with the list as regular content.

---

## Root Cause

The two requirements are fundamentally in conflict in standard WPF:

| Requirement | What it needs |
|---|---|
| WPF virtualization (no freeze) | ListBox must own its ScrollViewer — `VirtualizingStackPanel` must be a direct child of the scroll host |
| Featured scrolls with stations | Both featured and stations must be inside the **same** scroll host |

Every previous fix solved one problem but broke the other:

- **Commit `1d791c3`** — Put `StationsListBox` in a `Height="*"` grid row with its own `ScrollViewer` + VSP virtualization. Fixed freeze ✓, but featured was in a separate `Auto` grid row above it, so it pinned to the top ✗.

- **Commit `ca343c1`** — Merged featured + stations into a single outer `ScrollViewer → StackPanel`. Fixed pinning ✓, but this disabled WPF virtualization (VSP requires the ListBox's own SV as scroll host), restoring the freeze ✗.

### Why non-virtualized → freeze

With internal scroll disabled (`ScrollViewer.VerticalScrollBarVisibility="Disabled"`), WPF's
`VirtualizingStackPanel` falls back to non-virtualizing mode — it materializes ALL item
containers at once. Every `ObservableCollection.Add()` immediately creates a `ListBoxItem`
+ `StationListItem` UserControl on the UI thread. Loading 30 items in one batch = 30
synchronous container creations = UI thread blocked for ~300–500 ms.

---

## Fix

### Approach: Single virtualized ListBox for all items

Merge the featured header, featured stations, separator, and regular stations into a
**single `ListBox`** (`AllStationItems: ObservableCollection<object>`). The ListBox owns
its own `ScrollViewer` (virtualization works), and everything scrolls together naturally.

Mixed item types are handled by:
- **`DataTemplate DataType`** in `ListBox.Resources` — picks the right template per type
- **`GroupViewItemStyleSelector`** — gives header/separator markers a chrome-free
  `ListBoxItem` (no hover highlight, no focus ring) while station items get normal style

---

## New Types

### `Models/GroupViewItem.cs` (new file)

```
GroupViewItem (abstract base)
├── FeaturedHeaderItem      — renders "Featured" label
├── SectionSeparatorItem    — renders <Separator>
└── StationGroupViewItem    — wraps Station for display
    └── Station Station { get; }
```

`AllStationItems` layout when a group has featured stations:
```
[0]  FeaturedHeaderItem
[1]  StationGroupViewItem  (featured)
[2]  StationGroupViewItem  (featured)
[3]  SectionSeparatorItem
[4]  StationGroupViewItem  (regular)
[5]  StationGroupViewItem  (regular)
...
```

`_featuredSectionSize` field tracks the count of featured-section items (0 when none;
otherwise `featured.Count + 2`) so the regular-stations portion can be cleared or appended
without touching the featured section.

### `Helpers/GroupViewItemStyleSelector.cs` (new file)

Simple `StyleSelector` with two `Style` properties:
- `StationStyle` — for `StationGroupViewItem` (normal hover, padding, background)
- `MarkerStyle` — for header/separator (bare `ContentPresenter` template, `IsHitTestVisible=False`, `Focusable=False`)

---

## Code Changes

### `ViewModels/DiscoverViewModel.cs`

| Change | Detail |
|---|---|
| Added `_featuredSectionSize` field | Tracks where the featured section ends in `AllStationItems` |
| Added `AllStationItems: ObservableCollection<object>` | Unified display collection for the ListBox |
| `SelectGroupAsync` | Awaits featured query first (fast), builds header+items+separator in `AllStationItems`, then calls `LoadMoreGroupStationsAsync` |
| `LoadMoreGroupStationsAsync` | Adds `StationGroupViewItem(s)` to `AllStationItems` alongside each `GroupStations.Add(s)` |
| `DebounceGroupSearchAsync` | Trims `AllStationItems` back to `_featuredSectionSize` (keeps featured, clears stations) before re-loading |
| `BackToGroups` | Clears `AllStationItems`, resets `_featuredSectionSize = 0` |
| `ToggleFeatured` | Targeted insert/remove in `AllStationItems`; adds header+separator when first featured is added, removes them when last featured is removed |

Note: `GroupStations` is kept as-is for playlist context (`SetStation` for keyboard navigation).

### `Views/DiscoverPage.xaml`

Replaced:
```
ScrollViewer (StationsScrollViewer)
└── StackPanel
      ├── Border (featured: FeaturedHeaderItem + ListBox)
      └── ListBox (StationsListBox, ScrollViewer disabled → no virtualization)
```

With a single virtualized ListBox:
```xml
<ListBox x:Name="StationsListBox"
         ItemsSource="{Binding AllStationItems}"
         VirtualizingStackPanel.IsVirtualizing="True"
         VirtualizingStackPanel.VirtualizationMode="Standard"
         VirtualizingPanel.ScrollUnit="Pixel">
    <ListBox.ItemContainerStyleSelector>
        <helpers:GroupViewItemStyleSelector>
            <StationStyle>  <!-- normal hover -->
            <MarkerStyle>   <!-- no chrome -->
        </helpers:GroupViewItemStyleSelector>
    </ListBox.ItemContainerStyleSelector>
    <ListBox.Resources>
        <DataTemplate DataType="{x:Type models:FeaturedHeaderItem}">   <!-- "Featured" label -->
        <DataTemplate DataType="{x:Type models:SectionSeparatorItem}"> <!-- <Separator> -->
        <DataTemplate DataType="{x:Type models:StationGroupViewItem}"> <!-- StationListItem, DataContext={Binding Station} -->
    </ListBox.Resources>
</ListBox>
```

Key: `DataContext="{Binding Station}"` on `StationListItem` in the DataTemplate means all
`{Binding Name}`, `{Binding IsFavorite}` etc. resolve against `Station`, while
`RelativeSource AncestorType=Page` command bindings still walk up to `DiscoverPage.DataContext`
(DiscoverViewModel) correctly.

### `Views/DiscoverPage.xaml.cs`

- Removed direct `StationsScrollViewer` reference (element no longer exists)
- Added `_stationsSv` field + `FindChildScrollViewer` helper to locate the ListBox's internal `ScrollViewer` after WPF realizes it
- `IsGroupView` change handler wraps setup in `Dispatcher.InvokeAsync(..., DispatcherPriority.Loaded)` so layout completes before `FindChildScrollViewer` is called
- `StationsSv_ScrollChanged` handles infinite scroll trigger and `IsAtBottom` tracking

---

## Files Changed

| File | Change |
|---|---|
| `Models/GroupViewItem.cs` | **New** — `GroupViewItem`, `FeaturedHeaderItem`, `SectionSeparatorItem`, `StationGroupViewItem` |
| `Helpers/GroupViewItemStyleSelector.cs` | **New** — `StyleSelector` for mixed ListBox item types |
| `ViewModels/DiscoverViewModel.cs` | `AllStationItems` + `_featuredSectionSize`; updated `SelectGroupAsync`, `LoadMoreGroupStationsAsync`, `DebounceGroupSearchAsync`, `BackToGroups`, `ToggleFeatured` |
| `Views/DiscoverPage.xaml` | Single virtualized `ListBox` replaces `ScrollViewer → StackPanel → two ListBoxes` |
| `Views/DiscoverPage.xaml.cs` | `FindChildScrollViewer`, `_stationsSv`, `StationsSv_ScrollChanged`; removed `StationsScrollViewer` reference |
