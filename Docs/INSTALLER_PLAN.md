# RadioV2 — Installer & Distribution Plan

## Overview

Package RadioV2 as a distributable Windows installer EXE, hosted as a GitHub Release asset. Users download one file, run it, and the app is installed with no manual steps. The app checks for updates on startup and notifies the user when a new version is available.

---

## Decisions Log

| Topic | Decision |
|---|---|
| Installer tool | Inno Setup |
| .NET runtime | Framework-dependent build; installer checks and downloads .NET if missing |
| Default install path | `%LocalAppData%\RadioV2\` |
| Custom path | User can choose; shows a warning if they deviate from default |
| DB location | Same folder as the app (installer copies it there) |
| Uninstaller | Included; removes everything (app files, DB, shortcuts, registry entries) |
| Auto-update | Notify only — dismissible banner on startup if a new GitHub Release exists |
| Distribution | GitHub Releases on `https://github.com/Natboa/radioV2` |
| Code signing | None (SmartScreen "Unknown publisher" warning is expected) |
| License | MIT |
| Minimum Windows version | Windows 10 (1809+) |
| DB structure | Split into `stations.db` (overwritable) and `userdata.db` (never touched by installer) |

---

## Installed File Layout

After installation, everything lives in one folder (wherever the user chose):

```
%LocalAppData%\RadioV2\          ← default
├── RadioV2.exe
├── RadioV2.dll
├── [all other .NET assemblies and WPF-UI assets]
├── Assets/
│   └── [icons, images used by the app]
└── Data/
    ├── stations.db          ← station/group data, overwritten by installer on update
    └── userdata.db          ← favourites, history, settings — never touched by installer
```

The uninstaller removes this entire folder plus:
- Desktop shortcut (if created)
- Start Menu folder entry
- Registry uninstall entry

---

## Phase 0 — Split DB into Two Files

**This must be done before any installer work.**

**Problem:** The current single `radioapp_large_groups.db` mixes station data (safe to overwrite on update) with user data (favourites, history, settings — must never be lost). If the installer overwrites the DB on update, the user loses everything.

**Solution:** Split into two SQLite files with two separate DbContexts (or one context per connection string):

| File | Tables | Owned by |
|---|---|---|
| `Data/stations.db` | `Stations`, `Groups` | Installer — overwritten on every update |
| `Data/userdata.db` | `Favourites`, `History`, `Settings` | User — created on first run, never touched by installer |

**What changes:**
- Create `Data/StationsDbContext.cs` — reads from `stations.db` (read-only queries, `AsNoTracking()`)
- Create `Data/UserDbContext.cs` — reads/writes `userdata.db` (favourites, history, settings)
- Update DI registration in `Program.cs` to register both contexts
- Update all services to inject the correct context:
  - `IStationService` → `StationsDbContext`
  - `IFavouritesService`, `IHistoryService` → `UserDbContext`
  - Settings read/write → `UserDbContext`
- Migrate the `IsFavorite` column off the `Stations` table into a `Favourites` table in `userdata.db` (stores `StationId` FK)
- `userdata.db` is created automatically by EF Core on first launch if it doesn't exist (using `EnsureCreated()`)
- Both DB paths resolved at runtime: `Path.Combine(AppContext.BaseDirectory, "Data", "stations.db")` etc.

**Import/export favourites after the split:**

The M3U import and export feature touches both databases and must be explicitly wired to work post-split:

| Operation | Databases touched | Notes |
|---|---|---|
| **Export (M3U/JSON)** | `userdata.db` (read `Favourites`) + `stations.db` (read `Stations` to get Name, StreamUrl, LogoUrl) | `IFavouritesService` must inject both `UserDbContext` and `StationsDbContext` to JOIN/resolve station details at export time |
| **Import (M3U parse)** | `stations.db` (lookup by `StreamUrl`) + `userdata.db` (write `Favourites`) | `M3UParserService` / `IFavouritesService` first queries `StationsDbContext` for a matching station by URL; if found, inserts that `StationId` into `Favourites`; if not found, inserts a minimal station row into `stations.db` then references it |

**Verification checklist for import/export after Phase 0:**
- [ ] Export: all favourite stations appear in the exported M3U/JSON with correct Name and StreamUrl (confirm station detail JOIN works across both contexts)
- [ ] Import: importing a previously exported M3U re-populates favourites correctly (no FK violations, no duplicate stations)
- [ ] Import: importing an M3U with unknown stream URLs creates new station rows in `stations.db` and adds them to `Favourites` in `userdata.db`
- [ ] No service accidentally holds a cross-context EF navigation (e.g. a `Station` nav prop on a `Favourite` entity) — resolve cross-DB joins in-memory or with explicit two-step queries

**Installer behaviour after this change:**
- On install/update: always copy `stations.db` to `{app}\Data\`
- Never touch `userdata.db` — the app creates it on first run

---

## Phase 1 — Dynamic DB Path

**Problem:** The DB path is currently likely hardcoded or relative to the project directory. After installation, the app runs from wherever the user installed it.

**Solution:** Resolve the DB path at runtime relative to the EXE location using `AppContext.BaseDirectory`. Covered as part of Phase 0 above — both DB paths use this pattern:
  ```csharp
  Path.Combine(AppContext.BaseDirectory, "Data", "stations.db")
  Path.Combine(AppContext.BaseDirectory, "Data", "userdata.db")
  ```

---

## Phase 2 — Version Number

**Problem:** The app needs a version number to compare against GitHub Releases.

**What changes:**
- Set `<Version>` in `RadioV2.csproj` (e.g. `1.0.0`) — this becomes the assembly version
- The update checker reads this at runtime via:
  ```csharp
  System.Reflection.Assembly.GetExecutingAssembly().GetName().Version
  ```
- GitHub Release tags must match the format `v1.0.0` (standard convention)

---

## Phase 3 — Update Checker Service

**How it works:**
1. On app startup (after the window loads), a background task calls the GitHub Releases API:
   ```
   GET https://api.github.com/repos/Natboa/radioV2/releases/latest
   ```
2. Parse the `tag_name` field from the JSON response (e.g. `"v1.2.0"`)
3. Compare to the running app's version
4. If the latest release is newer → show a dismissible notification banner in the UI
5. If the user closes/ignores the banner → it stays dismissed for the rest of the session (does NOT persist across restarts — it will check again next launch)

**New files:**
- `Services/UpdateCheckerService.cs` — async method that fetches and compares versions, returns a `string? latestVersion` (null if up to date or check failed silently)

**UI:**
- A slim dismissible info bar (WPF-UI `InfoBar` component) at the top of the main window
- Text: `"RadioV2 v{latestVersion} is available. → Download"` — the Download link opens the GitHub releases page in the browser
- An X button closes it for the session
- Only shown if the version check returns a newer version

**Error handling:**
- If the API call fails (no internet, GitHub down) → silently do nothing, no error shown to user

---

## Phase 4 — .NET Publish Configuration

**Build type:** Framework-dependent (not self-contained)
The installer handles .NET installation if missing, so we don't need to bundle the runtime.

**Publish profile settings** (`Properties/PublishProfiles/release.pubxml`):
```xml
<PropertyGroup>
  <PublishSingleFile>true</PublishSingleFile>
  <SelfContained>false</SelfContained>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <Configuration>Release</Configuration>
  <IncludeNativeLibrariesForSelfExtract>false</IncludeNativeLibrariesForSelfExtract>
</PropertyGroup>
```

**Publish command:**
```bash
dotnet publish -c Release -r win-x64 --self-contained false -o ./publish
```

Output will be in `./publish/` — this folder is what Inno Setup packages.

---

## Phase 5 — Inno Setup Script

**File:** `Installer/RadioV2Setup.iss`

**Key behaviors:**
- Default install directory: `{localappdata}\RadioV2`
- User can change the directory during install
- If the user selects a path that does not start with `{localappdata}` → show a warning: _"You chose a custom install location. Make sure you have write permissions to this folder, as RadioV2 stores its database there."_
- Optional desktop shortcut checkbox (checked by default)
- Start Menu entry under `RadioV2`
- .NET 8 prerequisite check: if not found → download and run the official Microsoft .NET 8 Desktop Runtime installer before proceeding
- Packages: all files from `./publish/`, and `Data/stations.db` only
- **Never installs `userdata.db`** — the app creates it on first run
- On update/reinstall: `stations.db` is overwritten; `userdata.db` is untouched (Inno Setup only touches files it explicitly lists)
- Registers an uninstaller in Add/Remove Programs
- Uninstall removes the entire install directory (including `userdata.db`)

**Rough script structure:**
```iss
[Setup]
AppName=RadioV2
AppVersion=1.0.0
DefaultDirName={localappdata}\RadioV2
DefaultGroupName=RadioV2
OutputBaseFilename=RadioV2Setup
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest   ; no admin needed when installing to AppData
MinVersion=10.0.17763       ; Windows 10 1809 minimum

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs
Source: "..\Data\stations.db"; DestDir: "{app}\Data"
; userdata.db is NOT listed here — app creates it on first run

[Icons]
Name: "{group}\RadioV2"; Filename: "{app}\RadioV2.exe"
Name: "{commondesktop}\RadioV2"; Filename: "{app}\RadioV2.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: checked

[Code]
// .NET 8 check + download logic goes here
// Custom path warning logic goes here

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
```

---

## Phase 6 — GitHub Release Process

Each new release follows this process:

1. Bump `<Version>` in `RadioV2.csproj`
2. Run `dotnet publish` to build the release output
3. Run Inno Setup compiler to produce `RadioV2Setup.exe`
4. Create a new GitHub Release with tag `v{version}` (e.g. `v1.1.0`)
5. Attach `RadioV2Setup.exe` as a release asset
6. Users who have the app installed will see the update banner on next launch and can click through to the releases page to download

---

## Open Questions Before Implementation

All questions resolved:

- [x] **License** — MIT
- [x] **Minimum Windows version** — Windows 10 (1809+), `MinVersion=10.0.17763` in Inno Setup
- [x] **DB update strategy** — split into `stations.db` (installer overwrites) and `userdata.db` (user-owned, never touched by installer)

---

## Implementation Order

1. **Phase 0** — Split DB into `stations.db` + `userdata.db` (prerequisite for everything)
2. **Phase 1** — Dynamic DB paths using `AppContext.BaseDirectory` (done as part of Phase 0)
3. **Phase 2** — Version number in `RadioV2.csproj`
4. **Phase 3** — Update checker service + dismissible UI banner
5. **Phase 4** — Publish profile (`release.pubxml`)
6. **Phase 5** — Inno Setup script (`Installer/RadioV2Setup.iss`)
7. **Phase 6** — First GitHub Release with MIT license file
