# Featured Section Sticky Header Bug

## Bug Description
The "Featured" station list in the group drill-down view pins to the top when scrolling down,
instead of scrolling with the stations list content. Featured stations should just be the
first rows in the scrollable content, not a fixed header.

---

## Root Cause

`DiscoverPage.xaml` inner grid for station list view had this structure:
```
Row 0 (Auto): back + title
Row 1 (Auto): search box
Row 2 (Auto): Featured Border  ← OUTSIDE the scroll container — always pinned
Row 3 (*):    StationsListBox  ← sole scroll container
Row 4 (48):   Spinner
```
The featured section sat in its own `Auto` grid row **above** the `StationsListBox`.
Scrolling inside `StationsListBox` didn't move the featured section because it lived
in a completely separate layout row — WPF positioned it statically at the grid level.

---

## Fix

### Layout restructure — `Views/DiscoverPage.xaml`

Merged rows 2 + 3 into a single `*` row containing a named
`ScrollViewer x:Name="StationsScrollViewer"`. Both the featured section and the
stations `ListBox` are now inside that single `ScrollViewer`, so they scroll together.

**New inner grid rows:**
```
Row 0 (Auto): back + title
Row 1 (Auto): search box
Row 2 (*):    ScrollViewer (StationsScrollViewer)
                └── StackPanel
                      ├── Featured Border (collapses when HasFeaturedStations=false)
                      │     └── ListBox (FeaturedStations) — internal scroll disabled
                      └── ListBox (StationsListBox, GroupStations) — internal scroll disabled
Row 3 (48):   Spinner
```

Key changes to each control inside the `ScrollViewer`:
- **Featured `ListBox`**: changed from `ItemsControl` back to `ListBox` so `ListBoxItem`
  containers provide the same Fluent hover highlight as the main stations list.
  `ScrollViewer.VerticalScrollBarVisibility="Disabled"` prevents nested scrolling.
- **`StationsListBox`**: `ScrollViewer.VerticalScrollBarVisibility="Disabled"` — the outer
  `StationsScrollViewer` owns all scrolling.
- Removed `VirtualizingStackPanel` settings from `StationsListBox` (WPF's VSP virtualization
  requires the ListBox to own its scroll; with external SV it would misrender items).

### Code-behind simplification — `Views/DiscoverPage.xaml.cs`

Replaced `FindChildScrollViewer(StationsListBox)` + deferred setup with a direct reference
to the named `StationsScrollViewer`. The scroll handler and `IsAtBottom` tracking now attach
once in `_pageSetup`, not on every `IsGroupView` change. Removed `_stationsSvSetup` flag and
`FindChildScrollViewer` helper entirely. Removed unused `System.Windows.Media` and
`System.Windows.Threading` imports.

### Why hover color was missing in the featured list

The intermediate version used `ItemsControl` for featured stations. `ItemsControl` wraps items
in `ContentPresenter`, which has no hover style. `ListBox` wraps items in `ListBoxItem`, which
inherits the WPF-UI Fluent hover background. Switching back to `ListBox` (with internal scroll
disabled) restores identical hover behaviour between featured and main station rows.

---

## Files Changed

| File | Change |
|---|---|
| `Views/DiscoverPage.xaml` | Merged featured row + station list row into single `*` ScrollViewer row; featured uses `ListBox` (disabled scroll) for hover parity |
| `Views/DiscoverPage.xaml.cs` | Use named `StationsScrollViewer` directly; removed `FindChildScrollViewer`, `_stationsSvSetup`, unused imports |

## Status
IN PROGRESS — awaiting user confirmation
