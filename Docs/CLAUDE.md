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
- **Bug fixes require user confirmation before pushing.** Do not commit and push a bug fix until the user explicitly confirms the fix works. You may commit locally, but hold the push until they say it's resolved.
- **Speed Optimization:** NEVER use `mcp__github__push_files` for code changes — it is too slow for our .NET project.
- **The Fast Way:** Use native git CLI for all commits and pushes:
  1. `git checkout <feature-branch>` — ensure you're on the right branch before committing
  2. `git add .`
  3. `git commit -m "feat: your descriptive message"`
  4. `git push`
- **When to Push:** Push automatically when a milestone step is complete or a new module is scaffolded.

### After every bug fix or feature addition:
- **Always run `dotnet build` immediately after making changes.**
- If the build fails, fix all errors before doing anything else (no commits, no further changes).
- Only proceed (commit, test, etc.) once the build is clean.

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

## Bug Debugging Protocol

Whenever you are actively trying to fix a bug:

1. **Create a debug file** in `Debugging/` named after the bug (e.g., `discover-stations-infinite-scroll.md`). Do this before making any code changes.
2. **Before each fix attempt**, document in the file:
   - What you tried and why you expected it to work
3. **Every code change made while debugging** must be recorded in the file immediately — what file was changed, what changed, and why. Do not batch changes; log each one as it happens.
4. **After each failed attempt**, document why it failed (root cause analysis).
5. **Only the user can declare a bug fixed.** Do not commit or push a fix until the user confirms it works. When they confirm, update the file: set status to `CONFIRMED FIXED`, fill in the `## Fix` section describing the working solution and the exact change that resolved it. Then commit and push.
6. If starting a new session mid-bug, read the existing debug file first to avoid repeating failed approaches.

Debug files live in: `Debugging/`

---

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

> **MANDATORY** — mem0 is the project's long-term brain. Skipping it means losing hard-won knowledge across sessions. Always use it.

### Scoping
- **Always** pass `agent_id: "radioV2"` on every `add_memory` and `search_memories` call.

### Session Start — do this FIRST, before any code or planning
1. Call `mcp__mem0__search_memories` with query `"radioV2 project status architecture"` and `filters: {"AND": [{"agent_id": "radioV2"}]}`.
2. Also search `"bugs and gotchas"` to recall known pitfalls before touching any code.
3. Only after reading the results, proceed with the task.

### While Working — search before acting on anything uncertain
- Before making any architectural decision, search mem0 first: `"radioV2 <topic>"`.
- Before adding a new memory, search for it first to avoid duplicates. Update existing memories instead of adding new contradictory ones.
- Useful mid-session queries: `"DI lifetimes"`, `"WPF-UI gotchas"`, `"ICY metadata"`, `"theming"`, `"database rules"`, `"git workflow"`.

### After Completing a Milestone or Step — always update mem0
- After every completed milestone step, call `mcp__mem0__add_memory` (or update an existing memory) with what changed.
- Specifically store: milestone progress update, any new architectural patterns, any new bugs/gotchas discovered.
- Also update `Docs/PROGRESS.md` — mem0 for knowledge, PROGRESS.md for step status. Both, every time.

### Store bugs and surprises immediately
- The moment you hit a surprising build error, runtime quirk, or library limitation: save it to mem0 before moving on.
- Examples of what to store: WPF-UI API differences from docs, EF Core edge cases, threading surprises, NuGet version conflicts.

### What belongs where
- **PROGRESS.md** — step completion status only (done / not started / in progress).
- **Mem0** — architecture, patterns, gotchas, decisions, DI wiring, known bugs.
