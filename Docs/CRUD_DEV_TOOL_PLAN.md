# RadioV2 — Developer CRUD Tool Plan

> **Purpose:** A standalone WPF executable for developers to manage the station/group database directly.
> Not shipped to end users. Not part of the installer or release package.

---

## 1. Project Structure

- **Location:** `RadioV2.DevTool/` folder inside the solution root (sibling to the main `RadioV2/` project)
- **Type:** Separate `.csproj` — WPF app targeting `net8.0-windows`
- **Solution file:** Add as a second project to the existing `.sln`
- **Shared code:** Extract `Station.cs`, `Group.cs`, `Setting.cs`, and `RadioDbContext.cs` into a shared class library `RadioV2.Core/`. Both `RadioV2` and `RadioV2.DevTool` reference it. One source of truth — model changes automatically apply to both projects.
- **DB file:** Hardcoded relative path from the DevTool executable to `../RadioV2/Data/radioapp_large_groups.db` (or equivalent). Can be updated manually if folder structure changes.
- **No DI host required** — simpler setup; use `RadioDbContext` directly via `new DbContextOptionsBuilder()` in a `DbService` class

---

## 2. Tech Stack (DevTool only)

| Concern | Choice | Reason |
|---|---|---|
| UI framework | WPF + WPF-UI (`Wpf.Ui`) | Consistent look, already a dependency |
| MVVM | CommunityToolkit.Mvvm | Already used in main app |
| Data access | EF Core 8 + SQLite | Same as main app, shared models |
| No Mica / FluentWindow | Plain `Window` or `FluentWindow` without Mica | Dev tool, keep it simple |

---

## 3. UI Layout

Single window, two-tab structure:

```
+--------------------------------------------------+
|  RadioV2 Dev Tool                           [x]  |
+--------------------------------------------------+
|  [ Stations ]  [ Groups ]                        |  <- TabControl
+--------------------------------------------------+
```

### 3a. Stations Tab

```
+------------------------+---------------------------+
|  [Search box]          |                           |
|  [Group filter v]      |   Edit / Create Form      |
|------------------------|                           |
|  Station list          |  Name: [_______________]  |
|  (scrollable)          |  Stream URL: [_________]  |
|                        |  Logo URL:  [_________]   |
|  > Station Name        |  Group: [dropdown v]      |
|    Group: Jazz         |  Favourite: [checkbox]    |
|                        |                           |
|  > Station Name        |  [Save]  [Delete]         |
|    Group: Rock         |                           |
|                        |  -- or --                 |
|  ...                   |                           |
|                        |  [+ New Station]          |
|  [+ New Station btn]   |  (clears form for input)  |
+------------------------+---------------------------+
```

- Selecting a station in the list populates the right-side form
- "New Station" button clears the form and puts it in Create mode
- Save in Create mode = INSERT; Save in Edit mode = UPDATE
- Delete button only visible when a station is selected (Edit mode)

### 3b. Groups Tab

```
+------------------------+---------------------------+
|  [Search box]          |                           |
|------------------------|   Group Form              |
|  Group list            |                           |
|  (with station count)  |  Name: [_______________]  |
|                        |                           |
|  > Jazz (1,204)        |  [Save]  [Delete Group]   |
|  > Rock (892)          |                           |
|  > Pop (3,011)         |  ------- Merge -------    |
|  ...                   |                           |
|                        |  Merge INTO: [dropdown v] |
|  [+ New Group]         |  [Merge Groups]           |
+------------------------+---------------------------+
```

- Group list shows name + station count in parentheses
- Selecting a group populates the form (name editable for rename)
- Merge section: current selected group = source, dropdown = target
- "New Group" clears form to Create mode

---

## 4. Operations Breakdown

### 4.1 Stations

| Operation | Trigger | Behavior |
|---|---|---|
| List | Tab open / search / filter | Load stations matching search text + group filter. Paginated 50 at a time, next batch loads as user scrolls near the bottom. |
| Search | Text box input | Filter by `Name LIKE %query%` |
| Filter by group | Group dropdown | Filter by `GroupId` |
| Select | Click list item | Populate right-side form in Edit mode |
| Create | "New Station" button → fill form → Save | INSERT with all fields. `StreamUrl` must be unique — show inline error if duplicate. |
| Edit | Select station → modify fields → Save | UPDATE the station record |
| Delete | Delete button (edit mode) | Confirmation dialog: "Delete '[Name]'? This cannot be undone." → DELETE record |

### 4.2 Groups

| Operation | Trigger | Behavior |
|---|---|---|
| List | Tab open / search | Load all groups with station count via `GROUP BY` query |
| Search | Text box input | Filter group names |
| Select | Click list item | Populate form in Edit mode |
| Create | "New Group" → fill name → Save | INSERT group |
| Rename | Select group → edit name → Save | UPDATE group name |
| Delete | "Delete Group" button | Confirmation dialog: "Delete '[Name]' and its [N] stations? This cannot be undone." → DELETE stations WHERE GroupId = X, then DELETE group |
| Merge | Select source group → pick target from dropdown → "Merge Groups" | See §4.3 |

### 4.3 Group Merge Flow

1. User selects a group in the left list (this becomes the **source**)
2. User picks the **target** group from the "Merge INTO" dropdown (all groups except source)
3. User clicks "Merge Groups"
4. **Preview dialog appears:**
   > "Move [N] stations from '[Source]' into '[Target]'? '[Source]' group will be deleted. This cannot be undone."
   > `[Cancel]` `[Merge]`
5. On confirm:
   - `UPDATE Stations SET GroupId = targetId WHERE GroupId = sourceId`
   - `DELETE FROM Groups WHERE Id = sourceId`
6. Refresh group list — source is gone, target count updated

---

## 5. Confirmation Dialog Spec

All destructive actions use a consistent modal dialog:

- **Title:** "Confirm Delete" / "Confirm Merge"
- **Body:** Human-readable description with counts (e.g., "This will permanently delete 'Jazz' and its 1,204 stations.")
- **Buttons:** `[Cancel]` (left, secondary) and `[Confirm]` (right, danger/red appearance)
- No backup, no undo — confirmation is the only safety net

---

## 6. Data Access Pattern

- Single `DevDbService` class wraps `RadioDbContext`
- DB path: hardcoded relative path resolved at startup — e.g. `Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\RadioV2\Data\radioapp_large_groups.db"))`
- Use `AsNoTracking()` for all read queries
- Wrap writes in `try/catch` — display error message in the UI (e.g., duplicate `StreamUrl` on station create/edit)
- No migrations — `Database.EnsureCreated()` only

---

## 7. Folder Structure

```
RadioV2.sln
RadioV2/                          <- existing main app (unchanged)
RadioV2.Core/                     <- NEW shared class library
  RadioV2.Core.csproj             <- targets net8.0, no WPF dependency
  Models/
    Station.cs
    Group.cs
    Setting.cs
  Data/
    RadioDbContext.cs
RadioV2.DevTool/                  <- NEW dev tool WPF app
  RadioV2.DevTool.csproj          <- references RadioV2.Core
  App.xaml / App.xaml.cs
  MainWindow.xaml / MainWindow.xaml.cs
  Services/
    DevDbService.cs               <- EF Core wrapper, all CRUD methods
  ViewModels/
    StationsViewModel.cs
    GroupsViewModel.cs
  Views/
    StationsTab.xaml
    GroupsTab.xaml
  Dialogs/
    ConfirmDialog.xaml            <- reusable confirmation dialog
```

The main `RadioV2` project also adds a project reference to `RadioV2.Core` and removes the duplicate model files from its own `Models/` and `Data/` folders.


---

## 8. Out of Scope

- No auth / access control (dev-only tool)
- No undo/redo
- No DB backup on destructive operations
- No import/export (M3U import stays in the main app's Settings page)
- No station playback preview
- No Mica backdrop or system tray
- Not localized

---

## 9. Implementation Progress

> Update this section after every completed step.

| Step | Status | Notes |
|---|---|---|
| 1. Create `RadioV2.Core` class library project | Complete | `RadioV2.Core/RadioV2.Core.csproj` — net8.0, EF Core SQLite |
| 2. Move models + DbContext into `RadioV2.Core` | Complete | `Station.cs`, `Group.cs`, `Setting.cs`, `GroupWithCount.cs`, `RadioDbContext.cs` — same namespaces kept |
| 3. Add `RadioV2.Core` reference to `RadioV2` | Complete | ProjectReference added; Compile/Page/None excludes prevent double-compilation from subdirs |
| 4. Create `RadioV2.DevTool` WPF project | Complete | Added to solution; references `RadioV2.Core`; WPF-UI 4.2, EF Core, CommunityToolkit.Mvvm |
| 5. Scaffold DevTool — `App.xaml`, `MainWindow`, `TabControl` shell | Complete | Dark theme via `ThemesDictionary`; TabControl with Stations/Groups tabs |
| 6. Implement `DevDbService` | Complete | All CRUD + merge ops; hardcoded path 4 levels up from bin to solution root |
| 7. Stations tab — list + search + group filter + pagination | Complete | 50-at-a-time with Load More button; search + group filter each reset offset |
| 8. Stations tab — side panel edit form (Edit + Delete) | Complete | Select station → form populates; Save updates; Delete shows ConfirmDialog |
| 9. Stations tab — Create new station | Complete | New Station clears form; duplicate StreamUrl caught and shown inline |
| 10. Groups tab — list with station counts | Complete | Groups listed with `{N:N0}` stations count |
| 11. Groups tab — Create + Rename | Complete | New Group = create mode; select existing = rename mode |
| 12. Groups tab — Delete group (with count in confirmation) | Complete | ConfirmDialog shows exact station count before delete |
| 13. Groups tab — Merge groups (with preview dialog) | Complete | Source = selected; target = dropdown; ConfirmDialog shows count before merge |
| 14. Reusable `ConfirmDialog` | Complete | `Dialogs/ConfirmDialog.xaml` — static `Show()` returns bool; red Confirm button |

---

## 10. Decisions Log

| # | Decision | Choice |
|---|---|---|
| A | Shared class library vs. copy models? | Shared library `RadioV2.Core` — both `RadioV2` and `RadioV2.DevTool` reference it |
| B | Load all stations or paginate? | Paginate 50 at a time with scroll-to-load |
| C | DB path config | Hardcoded relative path for now |
