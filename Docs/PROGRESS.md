# RadioV2 — Implementation Progress

> Update this file after every completed step. Use it at the start of each session to know exactly where to resume.

---

## Current Status

**Phase:** M1 — Foundation (in progress)
**Next action:** M1.7 — Theme Support (Light/Dark).

---

## Documentation

| File | Status | Notes |
|---|---|---|
| `PRD.md` | Complete | Includes ICY metadata spec, Light/Dark theming, mini-player layout |
| `TECH_STACK.md` | Complete | Full NuGet package list and rationale |
| `DATABASE_SCHEMA.md` | Complete | Pre-existing, matches live DB |
| `NatboaFluentGuidelines_Relaxed.md` | Complete | Light/Dark only, Fluent UI semantic brushes |
| `IMPLEMENTATION_PLAN.md` | Complete | M1–M5 with Step 3.5 (ICY parsing) added |
| `CLAUDE.md` | Complete | Moved to `Docs/`, paths updated |

---

## Milestone 1 — Foundation

| Step | Status | Notes |
|---|---|---|
| 1.1 Project Scaffold | Complete | dotnet new wpf, all NuGet packages installed, DB file wired, build passes |
| 1.2 Folder Structure | Complete | Models, Services, ViewModels, Views, Controls, Converters, Assets, Helpers |
| 1.3 EF Core Models & DbContext | Complete | Station, Group, Setting models + RadioDbContext with indexes, build passes |
| 1.4 DI & App Host | Complete | IHost wired, all ViewModels registered, theme applied on startup, build passes |
| 1.5 FluentWindow Shell & NavigationView | Complete | FluentWindow, Mica, NavigationView, 4 page stubs, mini-player placeholder, build passes |
| 1.6 Mini-Player Control (UI Shell) | Complete | MiniPlayer UserControl, full MiniPlayerViewModel with computed props and stub commands, BoolToPlayIconConverter |
| 1.7 Theme Support (Light/Dark) | Not started | |
| 1.8 Single-Instance Enforcement | Not started | |

---

## Milestone 2 — Browse & Discover

| Step | Status | Notes |
|---|---|---|
| 2.1 Station Service (Data Layer) | Not started | |
| 2.2 Shared Station List Item Control | Not started | |
| 2.3 Infinite Scroll Behaviour | Not started | |
| 2.4 Browse Page | Not started | |
| 2.5 Discover Page: Genre Grid | Not started | |

---

## Milestone 3 — Playback & Media Keys

| Step | Status | Notes |
|---|---|---|
| 3.1 RadioPlayerService (LibVLCSharp) | Not started | |
| 3.2 Wire Mini-Player to Playback | Not started | |
| 3.3 Next/Previous Station | Not started | |
| 3.4 Global Media Key Support | Not started | |
| 3.5 ICY Metadata Parsing (NowPlayingParser) | Not started | |

---

## Milestone 4 — Favourites & Persistence

| Step | Status | Notes |
|---|---|---|
| 4.1 Favourites Page | Not started | |
| 4.2 Import/Export Favourites | Not started | |
| 4.3 M3U Parser Service | Not started | |
| 4.4 Session Persistence | Not started | |

---

## Milestone 5 — System Tray & Polish

| Step | Status | Notes |
|---|---|---|
| 5.1 System Tray Integration | Not started | |
| 5.2 Settings Page | Not started | |
| 5.3 Error Handling & Offline States | Not started | |
| 5.4 Serilog Logging Setup | Not started | |
| 5.5 Performance Optimization | Not started | |
| 5.6 Installer Packaging | Not started | |

---

## Decisions & Notes

- Light/Dark theme only — no accent color picker, no custom colors. All colors from WPF-UI Fluent UI semantic brushes.
- ICY metadata: parse `"Artist - Song Title"` into separate `NowPlayingArtist` / `NowPlayingTitle` properties on `MiniPlayerViewModel`.
- DB is pre-seeded — never run EF migrations against it. Use `Database.EnsureCreated()` as fallback only.
- `MiniPlayerViewModel` and `MainWindowViewModel` are singletons; page ViewModels are transient.
