# Uncommitted Changes Summary
_Generated before reverting to last commit on 2026-03-11_

## Files Changed

### `RadioV2.Core/Models/Station.cs`
- Added `IsFeatured` bool property with `INotifyPropertyChanged` support

### `Services/DatabaseInitService.cs`
- Added migration step to `ALTER TABLE Stations ADD COLUMN IsFeatured INTEGER NOT NULL DEFAULT 0`
- Renumbered migration steps (3–6)

### `Services/IStationService.cs`
- Added two new interface methods:
  - `GetFeaturedStationsByGroupAsync(int groupId)`
  - `SetFeaturedAsync(int stationId, bool featured)`

### `Services/StationService.cs`
- Implemented `GetFeaturedStationsByGroupAsync` — queries stations by GroupId where IsFeatured is true
- Implemented `SetFeaturedAsync` — finds station and toggles IsFeatured, saves to DB

### `Services/CategorySeeder.cs`
- Reordered carousel rows: music genres (Rock, Pop, etc.) now come **before** regional categories (Europe, Americas, Asia)
- Removed `north_america`, `south_america` group keys from Americas category

### `ViewModels/DiscoverViewModel.cs`
- Added `FeaturedStations` ObservableCollection and `HasFeaturedStations` computed bool
- `SelectGroupAsync` now loads featured stations for the group on navigation
- Added `ToggleFeaturedCommand` — toggles `IsFeatured` on station, syncs both `FeaturedStations` and `GroupStations` lists

### `Views/DiscoverPage.xaml`
- Added a "Featured" section above the station list in the group drill-down view
- Section is only visible when `HasFeaturedStations` is true
- Grid rows renumbered to accommodate the new section (StationsListBox moved from Row 2 → Row 3, ProgressRing Row 3 → Row 4)

### `Controls/StationListItem.xaml`
- Added a star button (Toggle Featured) as a new column in the station list item
- Button only visible when `DebugHelper.IsAdminMode` is true
- Star icon turns accent color when `IsFeatured` is true

### `Controls/DiscoverCarouselRow.xaml.cs`
- Added `_isAnimating` flag to prevent arrow buttons from fading out mid-scroll animation
- Button state updates are deferred until the scroll animation completes
- Left/right arrow buttons now eagerly hide when the scroll target reaches the start/end

### `MainWindow.xaml.cs`
- Pane toggle button compact-mode left margin nudged right by +6px for better alignment with nav icons

### `Helpers/DebugHelper.cs` _(new untracked file)_
- Provides `IsAdminMode` static bool used to show/hide admin-only UI (e.g. the featured toggle button)

### `stream_checker.py` _(unrelated to app)_
- Significant rewrite of the stream health checker script (not part of the WPF app)

### `.gitignore`
- 2 lines removed (minor cleanup)
