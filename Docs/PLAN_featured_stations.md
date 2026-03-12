# Plan: Featured Stations per Group

## Overview

Add a "Featured" section at the top of each group's station list. Only developers can manage
the featured list (via debug-only UI). Release builds show the list read-only. Groups with no
featured stations display unchanged.

Featured stations appear in **both** the featured banner and the main station list below.
The featured banner is a highlight, not a replacement for the full list.

---

## Architecture Decisions

**Debug gate:** A `Helpers/DebugHelper.cs` static class with a compile-time `IsDebugBuild`
bool (`#if DEBUG`). XAML binds button visibility to
`{x:Static helpers:DebugHelper.IsDebugBuild}` — zero overhead in Release, no runtime checks.

**Toggle UX:** A star button on every `StationListItem`, styled like the existing heart button.
Empty star = add to featured; filled star = remove. Only visible in Debug builds. In the
featured list itself, all stars show filled (click to remove).

**DB column:** `IsFeatured INTEGER NOT NULL DEFAULT 0` added to the Stations table via startup
SQL guard in `DatabaseInitService.cs` (same pattern as the existing `CategoryId` guard).

**No exclusion:** `GetStationsByGroupAsync` is unchanged — featured stations appear in both
the banner and the main list below.

---

## Step 1 — Database Schema

**File:** `Services/DatabaseInitService.cs`

Add after the existing `CategoryId` column guard:

```csharp
// Add IsFeatured column to Stations if it doesn't exist yet
try
{
    await db.Database.ExecuteSqlRawAsync(
        "ALTER TABLE Stations ADD COLUMN IsFeatured INTEGER NOT NULL DEFAULT 0;");
}
catch { /* column already exists — safe to ignore */ }
```

---

## Step 2 — Model

**File:** `RadioV2.Core/Models/Station.cs`

Add `IsFeatured` with the same `INotifyPropertyChanged` pattern as `IsFavorite`:

```csharp
private bool _isFeatured;
public bool IsFeatured
{
    get => _isFeatured;
    set
    {
        if (_isFeatured == value) return;
        _isFeatured = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFeatured)));
    }
}
```

No `OnModelCreating` changes needed — EF Core maps the bool column by convention.

---

## Step 3 — Service Interface & Implementation

**File:** `Services/IStationService.cs`

Add two methods:

```csharp
Task<List<Station>> GetFeaturedStationsByGroupAsync(int groupId, CancellationToken ct = default);
Task SetStationFeaturedAsync(int stationId, bool isFeatured, CancellationToken ct = default);
```

**File:** `Services/StationService.cs`

Implement them:

```csharp
public async Task<List<Station>> GetFeaturedStationsByGroupAsync(int groupId, CancellationToken ct = default)
{
    using var scope = _scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
    return await db.Stations
        .AsNoTracking()
        .Include(s => s.Group)
        .Where(s => s.GroupId == groupId && s.IsFeatured)
        .OrderBy(s => s.Name)
        .ToListAsync(ct);
}

public async Task SetStationFeaturedAsync(int stationId, bool isFeatured, CancellationToken ct = default)
{
    using var scope = _scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
    await db.Database.ExecuteSqlRawAsync(
        $"UPDATE Stations SET IsFeatured = {(isFeatured ? 1 : 0)} WHERE Id = {stationId}", ct);
}
```

No changes to `GetStationsByGroupAsync` — it continues to return all stations in the group.

---

## Step 4 — ViewModel

**File:** `ViewModels/DiscoverViewModel.cs`

Add properties (XAML already binds to these — they just don't exist yet):

```csharp
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(HasFeaturedStations))]
private ObservableCollection<Station> _featuredStations = [];

public bool HasFeaturedStations => FeaturedStations.Count > 0;
```

Update `SelectGroupAsync` to load featured stations concurrently with the first page:

```csharp
[RelayCommand]
public async Task SelectGroupAsync(GroupWithCount group)
{
    SelectedGroup = group;
    GroupStations.Clear();
    FeaturedStations.Clear();
    GroupSearchQuery = string.Empty;
    _groupSkip = 0;
    HasMoreItems = true;
    IsGroupView = true;

    var featuredTask = Task.Run(() => _stationService.GetFeaturedStationsByGroupAsync(group.Id));
    var stationsTask = LoadMoreGroupStationsAsync();
    var featured = await featuredTask;
    foreach (var s in featured) FeaturedStations.Add(s);
    await stationsTask;
}
```

Add `ToggleFeaturedCommand`:

```csharp
[RelayCommand]
private async Task ToggleFeatured(Station station)
{
    var newValue = !station.IsFeatured;
    await _stationService.SetStationFeaturedAsync(station.Id, newValue);
    station.IsFeatured = newValue;

    if (newValue)
        FeaturedStations.Add(station);
    else
        FeaturedStations.Remove(station);
    // GroupStations is left untouched — featured stations remain in the main list
}
```

Update `BackToGroups` to also clear the featured list:

```csharp
[RelayCommand]
private void BackToGroups()
{
    IsGroupView = false;
    SelectedGroup = null;
    GroupStations.Clear();
    FeaturedStations.Clear();
}
```

---

## Step 5 — DebugHelper

**File:** `Helpers/DebugHelper.cs` *(new file)*

```csharp
namespace RadioV2.Helpers;

public static class DebugHelper
{
#if DEBUG
    public static bool IsDebugBuild => true;
#else
    public static bool IsDebugBuild => false;
#endif
}
```

---

## Step 6 — Converters

**File:** `Converters/BoolToStarIconConverter.cs` *(new file)*

Maps `IsFeatured` bool to the correct WPF-UI symbol:

```csharp
using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Common;

namespace RadioV2.Converters;

public class BoolToStarIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? SymbolRegular.Star24 : SymbolRegular.StarAdd24;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
```

**File:** `Converters/BoolToFeaturedTooltipConverter.cs` *(new file)*

```csharp
using System.Globalization;
using System.Windows.Data;

namespace RadioV2.Converters;

public class BoolToFeaturedTooltipConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "Remove from Featured" : "Add to Featured";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
```

Register both in `App.xaml` resources.

---

## Step 7 — StationListItem: Star Button

**File:** `Controls/StationListItem.xaml`

Add `xmlns:helpers="clr-namespace:RadioV2.Helpers"` to the `<UserControl>` element.

Add a new column to the actions grid between the heart and the play button:

```xaml
<!-- Add/Remove Featured (DEBUG builds only) -->
<ui:Button Grid.Column="1"
           Appearance="Transparent"
           Command="{Binding DataContext.ToggleFeaturedCommand,
               RelativeSource={RelativeSource AncestorType=Page}}"
           CommandParameter="{Binding}"
           ToolTip="{Binding IsFeatured,
               Converter={StaticResource BoolToFeaturedTooltipConverter}}"
           Visibility="{Binding Source={x:Static helpers:DebugHelper.IsDebugBuild},
               Converter={StaticResource BoolToVisibilityConverter}}">
    <ui:SymbolIcon Symbol="{Binding IsFeatured,
        Converter={StaticResource BoolToStarIconConverter}}" />
</ui:Button>
```

Shift the play button from column 1 to column 2.

---

## Step 8 — DiscoverPage.xaml

No structural changes needed. The featured section at lines 90–119 already exists with the
correct `HasFeaturedStations` and `FeaturedStations` bindings. It will become active as soon
as the ViewModel properties are added in Step 4.

---

## File Change Summary

| File | Change |
|---|---|
| `Services/DatabaseInitService.cs` | Add `IsFeatured` column SQL guard |
| `RadioV2.Core/Models/Station.cs` | Add `IsFeatured` property with INPC |
| `Services/IStationService.cs` | Add 2 method signatures |
| `Services/StationService.cs` | Implement 2 new methods |
| `ViewModels/DiscoverViewModel.cs` | Add `FeaturedStations`, `HasFeaturedStations`, `ToggleFeaturedCommand`; update `SelectGroupAsync` and `BackToGroups` |
| `Helpers/DebugHelper.cs` | New — static compile-time `IsDebugBuild` |
| `Controls/StationListItem.xaml` | Add debug-only star button (new action column) |
| `Converters/BoolToStarIconConverter.cs` | New — maps `IsFeatured` bool to star symbol |
| `Converters/BoolToFeaturedTooltipConverter.cs` | New — maps `IsFeatured` bool to tooltip text |

---

## Key Behaviors

- **No featured stations** → `HasFeaturedStations = false` → featured `Border` stays
  `Collapsed`. Group view is completely unchanged.
- **Has featured** → featured banner appears at top, separated by a `Separator`. Featured
  stations also remain in the main list below (the banner is a highlight, not a filter).
- **Adding a station** (debug): star button click → station appears in featured banner
  immediately. It stays in the main list too.
- **Removing a station** (debug): star button click → station removed from the featured banner.
  It remains in the main list.
- **Debug mode**: star button renders on every station row (hover to reveal, like the heart).
- **Release mode**: star button never rendered (`Collapsed` set at compile time via `x:Static`).
- **No migrations**: uses the same try/catch `ALTER TABLE` pattern already in
  `DatabaseInitService.cs`.
