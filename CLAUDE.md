# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

RadioV2 is a Windows desktop radio streaming application built with WPF. It allows users to discover stations by genre, search by name, save/import/export favourites, and stream audio with media key support. The app ships with a pre-seeded SQLite database (~80 MB) containing tens of thousands of radio stations.

## Tech Stack

- **.NET 8** (LTS), C# 12, target `net8.0-windows`
- **WPF + WPF-UI** (`Wpf.Ui`) for Fluent Design 2 controls, Mica backdrop, `FluentWindow`
- **CommunityToolkit.Mvvm** for MVVM (source-generated `ObservableProperty`, `RelayCommand`)
- **Entity Framework Core 8** with SQLite provider
- **LibVLCSharp** + `VideoLAN.LibVLC.Windows` for audio streaming
- **Microsoft.Extensions.Hosting** for DI and app lifecycle

## Build & Run

```bash
dotnet build
dotnet run
```

Publish as single-file:
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
```

## Architecture

**MVVM pattern** with dependency injection. Services are injected into ViewModels; Views bind to ViewModels via DataContext.

### Key layers:
- **Models/** — EF Core entities: `Station`, `Group`, `Setting`
- **Data/RadioDbContext.cs** — DbContext with `DbSet<Station>`, `DbSet<Group>`, `DbSet<Setting>`
- **Services/** — `IRadioPlayerService` (LibVLCSharp playback), `IStationService` (data queries), `IFavouritesService` (import/export), `M3UParserService` (import)
- **ViewModels/** — one per page (`BrowseViewModel`, `DiscoverViewModel`, `FavouritesViewModel`, `SettingsViewModel`) plus `MainWindowViewModel` and `MiniPlayerViewModel`
- **Views/** — XAML pages: Browse, Discover, Favourites, Settings
- **Controls/** — reusable components: `StationListItem`, `MiniPlayer` (persistent bottom playback bar)

### Navigation & Pages
`FluentWindow` with left-pane `NavigationView`. Four pages:
- **Browse** (Home) — search all stations by name, infinite-scroll list
- **Discover** — genre cards → drill into genre → infinite-scroll stations, with search-within-genre filter
- **Favourites** — saved stations list with import/export buttons
- **Settings** — theme, audio device, about, bulk M3U import

**No separate Now Playing page** — all playback controls live in the persistent mini-player bar at the bottom.

### Key Behaviours
- **Infinite scroll everywhere** — station lists load in batches of 50, next batch loads automatically near the bottom. Never load all stations at once.
- **Media keys** — global hotkeys for Play/Pause, Next, Previous, Stop. Next/Previous cycle through favourites.
- **System tray** — closing the window minimizes to tray. Right-click menu: Play/Pause, Next, Previous, Show, Quit. Double-click restores.
- **Import/export favourites** — M3U/M3U8 and JSON formats via file dialogs on the Favourites page.

## Database

**File:** `Data/radioapp_large_groups.db` (SQLite 3)

Three tables:
- **Stations** — Id, Name, StreamUrl (unique), LogoUrl, GroupId (FK), IsFavorite
- **Groups** — Id, Name (genre/category)
- **Settings** — Key (PK), Value (app preferences like Volume, LastPlayedStationId)

### Performance rules for the large dataset:
- Use `AsNoTracking()` on all read-only queries
- Infinite-scroll pagination: `Skip(offset).Take(50)` via `IQueryable`, never load full tables
- Debounce search input (~300ms)
- Create SQLite index on `Stations.Name` for fast LIKE queries

## UI Guidelines

Follow `Docs/NatboaFluentGuidelines_Relaxed.md`:
- `FluentWindow` with `WindowBackdropType="Mica"` and `ExtendsContentIntoTitleBar="True"`
- Use `ui:Card` for grouping, `ui:Button` with appropriate `Appearance` (Primary/Transparent)
- Semantic theme brushes only — no hardcoded colors
- Spacing in multiples of 4/8px, rounded corners (4px controls, 8px containers)
- Support Light/Dark/System themes via `ApplicationThemeManager`

## Git & GitHub Workflow

This project uses Git for version control with GitHub as the remote. Follow these conventions:

### Branching
- **`main`** — stable, always-buildable branch. Never commit directly to main.
- **Feature branches** — create a branch per milestone step (e.g., `feat/m1-foundation`, `feat/m2-browse-page`). Use prefixes: `feat/`, `fix/`, `refactor/`, `docs/`.

### Commits
- Commit after each meaningful unit of work (a completed step, a working feature, a bug fix).
- Write concise commit messages: imperative mood, focused on "why" not "what".
- Do not bundle unrelated changes in a single commit.

### Pull Requests
- Create a PR for each milestone or logical group of steps.
- PR title: short, under 70 chars. Body: summary bullets + test plan.
- Merge to `main` after review/approval.

### Workflow per implementation step:
1. Create/switch to feature branch.
2. Implement the step.
3. `dotnet build` — verify no errors.
4. Commit with a descriptive message.
5. Push to remote.
6. When a milestone is complete, open a PR to `main`.

## Key Design Documents

- `Docs/PRD.md` — full product requirements, page specs, and milestone plan
- `Docs/TECH_STACK.md` — technology choices with rationale and NuGet package list
- `Docs/DATABASE_SCHEMA.md` — table schemas and M3U import format
- `Docs/NatboaFluentGuidelines_Relaxed.md` — UI/UX design system
