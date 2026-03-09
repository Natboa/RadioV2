# Discover Page — Slow First Load Fix

## Problem

Clicking Discover for the first time caused a **~15 second freeze** before the carousel appeared.

## Root Causes

### 1. EF Core / SQLite cold start
On the first DB operation of a particular query shape, EF Core compiles the LINQ query into SQL. Combined with SQLite opening and reading the 80 MB database file, this adds several seconds of overhead before any data is returned.

### 2. SQLite has no true async I/O
`Microsoft.Data.Sqlite` does not implement real async I/O. Calls like `ToListAsync()` are synchronous under the hood — they run on whichever thread calls them, blocking it for the full duration of the query. This means calling `GetCategoriesWithGroupsAsync()` from the UI thread (even as a fire-and-forget `_ = Task`) still freezes the UI.

### 3. No caching — every Discover visit re-queries
`GetCategoriesWithGroupsAsync()` hit the DB every time, including when navigating away and back to Discover within the same session. Categories and groups never change at runtime, so this was pure wasted work.

---

## Fixes Applied

### Fix 1 — In-memory cache (`StationService.cs`)

Added a `volatile` cache field to `StationService`. On first call the result is fetched from DB and stored; all subsequent calls return the cached list instantly without touching the DB.

```csharp
private volatile List<CategoryWithGroups>? _categoriesCache;

public async Task<List<CategoryWithGroups>> GetCategoriesWithGroupsAsync(CancellationToken ct = default)
{
    if (_categoriesCache is not null)
        return _categoriesCache;

    using var db = _factory.CreateDbContext();
    var result = await db.Categories...ToListAsync(ct);

    _categoriesCache = result;
    return result;
}
```

`volatile` ensures reads are not stale across threads (background warm-up writes, UI thread reads).

Cache is in-memory only — it resets on every app launch (intentional; data could change between sessions).

---

### Fix 2 — Background warm-up at startup (`App.xaml.cs`)

After the main window is shown, kick off `GetCategoriesWithGroupsAsync()` on a **thread-pool thread** via `Task.Run`. This populates the cache in the background while the user is on the home screen, so by the time they click Discover the data is ready.

```csharp
// In OnStartup, after mainWindow.Show():
_ = Task.Run(() => _host.Services.GetRequiredService<IStationService>().GetCategoriesWithGroupsAsync());
```

**Why `Task.Run` is required:** Without it, the call starts on the UI thread. Even though it's fire-and-forget (`_ =`), SQLite's fake-async means the query runs synchronously on the UI thread — causing a ~10 second freeze at app startup instead of at Discover. `Task.Run` pushes the entire operation (including the synchronous SQLite work) to a thread-pool thread, keeping the UI free throughout.

---

## Net Result

| Scenario | Before | After |
|---|---|---|
| App startup | Fast | Fast (warm-up runs in background) |
| First Discover visit | ~15s freeze | Near-instant (cache already populated) |
| Return to Discover (same session) | ~15s freeze | Instant (cache hit) |
| Next app launch | ~15s freeze | Background warm-up again |
