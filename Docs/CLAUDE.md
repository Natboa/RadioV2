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
- **Helpers/** — `NowPlayingParser` (ICY `StreamTitle` → Artist + Title), `ThemeHelper`, `MediaKeyHook`, `InfiniteScrollBehavior`, `TrayIconManager`

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
- **ICY metadata (Now Playing)** — LibVLC fires `Media.MetaChanged` with `MetadataType.NowPlaying` when the stream updates. Raw format is typically `"Artist - Song Title"`. Parse with `Helpers/NowPlayingParser.cs` (split on first `" - "`). Expose `NowPlayingArtist` and `NowPlayingTitle` as separate `[ObservableProperty]` fields on `MiniPlayerViewModel`; compute `NowPlayingDisplay` from them. Always dispatch metadata updates to the UI thread. Clear both fields when playback stops or a new station loads.

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

Follow `NatboaFluentGuidelines_Relaxed.md` (same `Docs/` folder):
- `FluentWindow` with `WindowBackdropType="Mica"` and `ExtendsContentIntoTitleBar="True"`
- Use `ui:Card` for grouping, `ui:Button` with appropriate `Appearance` (Primary/Transparent)
- **No hardcoded colors anywhere** — use WPF-UI semantic theme brushes exclusively (`{ui:ThemeResource ...}`). No custom palette, no accent overrides, no hex values in XAML or code.
- Spacing in multiples of 4/8px, rounded corners (4px controls, 8px containers)
- **Theme (Light/Dark):** `ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, true)` — two options only: `ApplicationTheme.Light` or `ApplicationTheme.Dark`. Read from `"Theme"` key in Settings table on startup, applied before the window is shown. Default: Dark. Settings page shows a Light/Dark toggle.

## Git & GitHub Workflow

**Repository:** `Natboa/radioV2` on GitHub
**GitHub MCP is installed** — use `mcp__github__*` tools for branch creation, PRs, and merges. Use native git CLI for all commits and pushes.

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

### 🚀 Autonomous Git Workflow (Turbo Mode)
- **Autonomy:** You are encouraged to commit and push when you reach a stable milestone (e.g., a feature is working or a bug is fixed).
- **Speed Optimization:** NEVER use `mcp__github__push_files` for code changes — it is too slow for our .NET project.
- **The Fast Way:** Use native git CLI for all commits and pushes:
  1. `git checkout <feature-branch>` — ensure you're on the right branch before committing
  2. `git add .`
  3. `git commit -m "feat: your descriptive message"`
  4. `git push`
- **When to Push:** Push automatically when a milestone step is complete or a new module is scaffolded.

### Workflow per implementation step:
1. `mcp__github__create_branch` — create the feature branch off `main`.
2. `git checkout <branch>` — switch to it locally.
3. Implement the step locally.
4. `dotnet build` — verify no errors.
5. `git add .` / `git commit` / `git push` — commit and push via CLI.
6. When a milestone is complete: `mcp__github__create_pull_request` → `mcp__github__merge_pull_request`.

### Key MCP tools to use:
- `mcp__github__create_branch` — create feature branches
- `mcp__github__create_pull_request` — open PRs
- `mcp__github__merge_pull_request` — merge PRs to main
- `mcp__github__create_issue` — track bugs or tasks
- `mcp__github__list_commits` / `mcp__github__get_pull_request` — inspect history

## Key Design Documents

All docs live in the `Docs/` folder (same directory as this file):

- `CLAUDE.md` ← this file — project guidance for Claude Code
- `PRD.md` — full product requirements, page specs, and milestone plan
- `TECH_STACK.md` — technology choices with rationale and NuGet package list
- `DATABASE_SCHEMA.md` — table schemas and M3U import format
- `NatboaFluentGuidelines_Relaxed.md` — UI/UX design system
- `IMPLEMENTATION_PLAN.md` — detailed step-by-step build plan for all milestones
- `PROGRESS.md` — live implementation progress tracker; update this after every completed step

## Persistent Memory (Mem0)

### Scoping
- Always pass `agent_id: "radioV2"` on every `add_memory` and `search_memories` call to keep memories scoped to this project and avoid cross-project noise.

### Session Start
- Run `search_memories` (agent_id: "radioV2") with the query `"current project status and architecture"` before doing anything else.
- Additional useful queries: `"current milestone step"`, `"ICY metadata parsing"`, `"theming decisions"`, `"database performance rules"`, `"bugs and gotchas"`.

### Task Completion
- After finishing a feature or making a major architectural decision, use `add_memory` to update the project's long-term state.
- Before adding, run `search_memories` for the topic first — update or replace an existing memory rather than creating a duplicate or contradictory one.

### What belongs where
- **PROGRESS.md** — step-by-step completion status (what's done, what's next). Update after every completed step.
- **Mem0** — architectural decisions, established patterns, coding gotchas, and bugs discovered during implementation.

### Store bugs and surprises
- Any tricky runtime behavior discovered during implementation should be saved immediately (e.g. threading surprises, library quirks, EF Core edge cases). These are the facts most likely to be re-learned the hard way in future sessions.

### Context Management
- If the conversation becomes long, use `search_memories` to recall earlier decisions instead of re-reading large files.
