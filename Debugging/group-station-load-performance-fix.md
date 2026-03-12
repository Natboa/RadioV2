# Group Station Load — Performance & Spinner Fix

## Problems Reported

1. Opening a group for the first time took ~1 second before stations appeared.
2. The loading spinner (ProgressRing) was frozen/not animating while loading.
3. Scrolling to the bottom to load the next 100 stations froze the app for ~0.5 seconds.
4. After an attempted fix, stations loaded for ~1 second then appeared all at once instead of progressively.

---

## Root Causes (in order of discovery)

### 1. EF Core first-time query compilation (same as Discover page)

The first call to `GetStationsByGroupAsync` and `GetFeaturedStationsByGroupAsync` per session
triggered EF Core's LINQ→SQL compilation for those query shapes. Combined with SQLite's
fake-async (no true async I/O, runs synchronously on whichever thread calls it), this added
~300ms on the very first group open.

**Fix:** Added two warm-up calls in `App.OnStartup` after `mainWindow.Show()`:

```csharp
var svc = _host.Services.GetRequiredService<IStationService>();
_ = Task.Run(() => svc.GetStationsByGroupAsync(0, 0, 1));
_ = Task.Run(() => svc.GetFeaturedStationsByGroupAsync(0));
```

`groupId=0` returns no rows but forces EF to compile both query shapes on a thread-pool
thread in the background while the user is still on the home screen.

**File:** `App.xaml.cs`

---

### 2. 100 individual `ObservableCollection.Add` calls on the UI thread

The original code did:
```csharp
foreach (var s in batch) GroupStations.Add(s);
```

Each `Add` fires `CollectionChanged`. For a non-virtualizing `ItemsControl`, the
`ItemContainerGenerator` creates the `StationListItem` UserControl **synchronously** in the
`CollectionChanged` handler — before the next line of code runs. With 100 complex items
(multiple WPF-UI buttons, converters, `RelativeSource` bindings, storyboard animations), this
blocked the UI thread for ~500ms.

This also caused the spinner to freeze: `IsLoading = true` made it visible, but the UI thread
was immediately occupied by 100 container creations, so the ProgressRing storyboard never got
a render tick.

**First attempted fix (later superseded):** Replaced `foreach add` with a single collection
assignment:
```csharp
GroupStations = new ObservableCollection<Station>(batch);
```
One `CollectionChanged` reset instead of 100 `Add` events. This helped slightly but the
ItemsControl still processed all 100 containers in one burst during the next layout pass.

---

### 3. Dispatcher priority race — binding fires after all Adds

When `GroupStations = new ObservableCollection<Station>()` is executed, it fires
`PropertyChanged`. The `ItemsControl`'s `ItemsSource` binding update is **queued at
DataBind priority (8)**, not applied immediately. Our code runs at **Normal priority (9)**,
which is higher, so we raced ahead and added all 100 items to the collection **before** the
binding fired.

When the DataBind pass finally ran, `ItemsControl` saw a collection already containing 100
items and created all containers in one burst — identical to the original problem. This is
why intermediate fixes produced "loads for 1 second then shows all at once."

The `Stopwatch.ElapsedMilliseconds >= 8` check was also ineffective: since the `Add` calls
were fast (no container creation yet, ItemsControl not yet subscribed), the 8ms threshold was
never reached and no yielding occurred.

**Fix:** After setting `GroupStations = targetCollection`, yield at `DataBind` priority before
adding any items. This lets the binding engine fire first, subscribing the ItemsControl to the
new collection. After this yield, each `Add` creates exactly one container synchronously, and
the time-based chunking works correctly.

```csharp
GroupStations = targetCollection;
await dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
```

**File:** `ViewModels/DiscoverViewModel.cs`

---

### 4. `DispatcherPriority.Background` (4) is too low — continuous deferral

When yielding at `Background` priority (4), our continuation resumes only after all
higher-priority items are drained: Normal (9), DataBind (8), Render (7), Loaded (6), Input
(5). WPF continuously generates work at these levels (data binding, input, rendering), so our
continuations were deferred indefinitely — the infinite-scroll stutter fix made loading
*slower*, not faster.

**Fix:** Changed inter-chunk yield to `DispatcherPriority.Loaded` (6):

```csharp
await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
```

Priority ordering (higher number = higher priority = runs first):
```
Send(10) > Normal(9) > DataBind(8) > Render(7) > Loaded(6) > Input(5) > Background(4)
```

With `Loaded` (6): Render (7) is higher priority, so a render/animation frame runs before
each resumed chunk — the spinner stays animated. Input (5) is lower, so scroll events are
processed after chunks, but each burst is short enough (~8ms) to keep stutter minimal.

---

## Final Implementation (`ViewModels/DiscoverViewModel.cs`)

```csharp
public async Task LoadMoreGroupStationsAsync(CancellationToken ct = default)
{
    if (_isLoadingStations || !HasMoreItems || SelectedGroup is null) return;
    _isLoadingStations = true;
    IsLoading = true;
    try
    {
        var query = GroupSearchQuery.Length >= 2 ? GroupSearchQuery : null;
        var isFirstBatch = _groupSkip == 0;
        var batch = await Task.Run(() => _stationService.GetStationsByGroupAsync(
            SelectedGroup.Id, _groupSkip, 100, query, ct), ct);

        var targetCollection = isFirstBatch ? new ObservableCollection<Station>() : GroupStations;
        var dispatcher = Application.Current.Dispatcher;

        if (isFirstBatch)
        {
            GroupStations = targetCollection;
            // Yield at DataBind so ItemsControl subscribes before we add items.
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
        }

        // Yield at Loaded (6) every ~8ms so Render (7) frames run between bursts.
        var sw = Stopwatch.StartNew();
        foreach (var s in batch)
        {
            targetCollection.Add(s);
            if (sw.ElapsedMilliseconds >= 8)
            {
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
                sw.Restart();
            }
        }

        _groupSkip += batch.Count;
        HasMoreItems = batch.Count == 100;
    }
    catch (OperationCanceledException) { }
    finally
    {
        _isLoadingStations = false;
        IsLoading = false;
    }
}
```

---

## Net Result

| Scenario | Before | After |
|---|---|---|
| First group open (cold) | ~1s freeze, spinner frozen | ~300ms progressive, spinner spins |
| First group open (warm) | ~500ms freeze, spinner frozen | ~200ms progressive, spinner spins |
| Scroll to load next 100 | ~500ms app freeze | Progressive, no freeze |
| Return to same group | Instant (data already loaded) | Instant |

---

## Related Fix: Discover Page Categories (earlier session)

The same EF Core cold-start / SQLite fake-async problem affected the Discover page categories.
Full details in `discover-page-slow-load-fix.md`. Summary:

- **Problem:** First Discover visit caused ~15s freeze.
- **Fix 1:** In-memory `volatile` cache in `StationService.GetCategoriesWithGroupsAsync()`.
- **Fix 2:** Background warm-up call at startup via `Task.Run`.
- **Why `Task.Run` is mandatory:** SQLite has no true async I/O. Even `await ToListAsync()` runs
  synchronously on the calling thread. Without `Task.Run`, fire-and-forget calls from the UI
  thread still block the UI.
