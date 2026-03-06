# Product Requirements Document: RadioV2

## 1. Overview

**Product Name:** RadioV2
**Platform:** Windows Desktop (Windows 10/11)
**Technology:** WPF with WPF-UI (Fluent Design), .NET 8, SQLite via Entity Framework Core
**Purpose:** A desktop application for discovering, searching, streaming, and saving internet radio stations with a modern Windows 11 Fluent Design interface.

---

## 2. Goals

- Provide a clean, native-feeling Windows app for streaming internet radio.
- Let users discover stations by genre/group, search by name, and save favorites.
- Deliver a responsive, low-friction listening experience with persistent user preferences.

---

## 3. Target User

Windows desktop users who want a lightweight, always-ready radio player without browser tabs or web clutter.

---

## 4. Tech Stack

| Layer | Technology |
|---|---|
| UI Framework | WPF with [WPF-UI](https://github.com/lepoco/wpfui) (Fluent 2) |
| Architecture | MVVM via `CommunityToolkit.Mvvm` |
| Database | SQLite 3 (`radioapp_large_groups.db`, ~80 MB pre-seeded) |
| ORM | Entity Framework Core (SQLite provider) |
| Audio Playback | LibVLCSharp + VideoLAN.LibVLC.Windows |
| Target Framework | .NET 8 (Windows) |

---

## 5. Data Model (Existing)

The SQLite database is already populated and contains three tables:

### Stations
| Column | Type | Notes |
|---|---|---|
| Id | Integer (PK) | Auto-increment |
| Name | String | Display name |
| StreamUrl | String (Unique) | Direct audio stream URL |
| LogoUrl | String (Nullable) | Station logo image URL |
| GroupId | Integer (FK) | References `Groups.Id` |
| IsFavorite | Boolean | User favorite flag |

### Groups
| Column | Type | Notes |
|---|---|---|
| Id | Integer (PK) | Auto-increment |
| Name | String | Genre/category name (e.g., "Rock", "Jazz", "News") |

### Settings
| Column | Type | Notes |
|---|---|---|
| Key | String (PK) | Setting name (e.g., "Volume", "LastPlayedStationId") |
| Value | String | Setting value |

---

## 6. Application Structure

### 6.1 Shell / Main Window

- Uses `FluentWindow` with Mica backdrop and integrated title bar.
- Left-pane `NavigationView` with the following pages:
  - **Browse** (Home / default page)
  - **Discover**
  - **Favourites**
  - **Settings**
- **Persistent mini-player bar** docked at the bottom of the window, visible on all pages.
- **System tray integration** — the app minimizes to the system tray instead of closing. Right-click tray icon menu: Play/Pause, Next Station, Quit.

### 6.2 Pages

#### 6.2.1 Browse (Home)

Search and browse all stations by name. This is the default landing page.

**Elements:**
- Search text box at the top with real-time filtering (debounced, ~300ms).
- Results displayed as an infinite-scroll list — loads stations in batches (e.g., 50 at a time) as the user scrolls down.
- Each result shows: logo thumbnail, station name, group name, play button, favorite toggle (heart icon).

**Behavior:**
- On initial load, shows a batch of stations (no search filter). More load as the user scrolls.
- Queries the `Stations` table with a `LIKE` filter on `Name` when the user types.
- Minimum 2 characters before searching.
- Infinite scroll: when the user reaches near the bottom, the next batch is fetched automatically.

#### 6.2.2 Discover

Browse stations organized by genre/group.

**Elements:**
- Grid or list of genre cards, each showing the group name and station count.
- Clicking a genre card navigates to a sub-view listing stations in that group.
- Station list items show: logo thumbnail, station name, and a play button.
- Each station item has a favorite toggle (heart icon).
- Filter/search box within the genre station list to narrow results within the selected genre.

**Behavior:**
- Groups are loaded from the `Groups` table.
- **Infinite scroll on station lists:** When a genre is selected, stations load in batches (e.g., 50 at a time). As the user scrolls down, the next batch loads automatically. Stations are NOT all loaded at once.
- The search box within a genre filters the already-selected genre's stations server-side.
- Clicking play on a station begins streaming via the mini-player.

#### 6.2.3 Favourites

View and manage saved stations.

**Elements:**
- List of all stations where `IsFavorite = true`.
- Each item shows: logo, name, group, play button, remove-from-favorites button.
- Empty state message when no favourites exist.
- **Import button** — import a favourites list from a file (M3U/M3U8 or JSON).
- **Export button** — export the current favourites list to a file (M3U/M3U8 or JSON).

**Behavior:**
- Toggling favorite off removes the station from this list immediately.
- **Import:** Opens a file dialog. Supported formats: M3U/M3U8 (standard playlist) and JSON (RadioV2 native format). Imported stations are matched by `StreamUrl`; matching stations are marked as favourites. Stations not in the database are skipped (or optionally added).
- **Export:** Opens a save dialog. Exports all current favourites with Name, StreamUrl, LogoUrl, and Group info. Formats: M3U/M3U8 or JSON.

#### 6.2.4 Settings

Application preferences.

**Elements:**
- **Theme selector** — two options: Light / Dark.
- Audio output device selector (if supported by audio library).
- About section: app version, credits.
- Import stations from M3U/M3U8 file (bulk station import, separate from favourites import).

**Behavior:**
- Theme changes apply immediately via `ApplicationThemeManager` and persist to the `Settings` table under key `"Theme"`.
- On startup, the `"Theme"` setting is read from the `Settings` table and applied before the main window is shown. Default: `"Dark"`.

---

## 7. Playback Engine

### Requirements
- Stream HTTP/HTTPS audio URLs (MP3, AAC, HLS common formats).
- Handle buffering gracefully — show a loading indicator in the mini-player when buffering.
- Handle stream errors (network loss, invalid URL) — show a non-blocking error notification and stop playback.
- Extract ICY metadata from the stream when available and display Artist and Song Title in the mini-player.

### ICY Metadata — Now Playing
Most internet radio streams embed real-time track information in ICY (SHOUTcast) metadata. When present, the `StreamTitle` field typically contains `"Artist - Song Title"` (dash-separated). The app must:

1. **Subscribe** to LibVLC's `Media.MetaChanged` event and read `MetadataType.NowPlaying`.
2. **Parse** the raw `StreamTitle` string:
   - Split on the first `" - "` (or `" – "` em-dash variant).
   - If a separator is found → `Artist` = left part, `Title` = right part.
   - If no separator → `Artist` = empty, `Title` = full raw string.
3. **Display** in the mini-player as `"Artist — Title"` when both are present, or just the title alone.
4. **Clear** the Now Playing info when playback stops or a new station loads (before new metadata arrives).
5. **Graceful absence** — many stations do not send ICY metadata. The mini-player must display cleanly with only the station name when no metadata is available.

### Library: LibVLCSharp
- Mature, supports all common codecs and streaming protocols out of the box.
- Built-in ICY metadata events (`Media.MetaChanged` + `MetadataType.NowPlaying`), buffering progress, and error handling.
- Adds ~120 MB to the app bundle — acceptable trade-off for reliability.

---

## 8. Persistent Mini-Player

The mini-player is the **only** playback UI. There is no separate Now Playing page.

A compact playback bar docked at the bottom of the window, visible on all pages at all times.

**Elements:**
- Station logo (small thumbnail).
- Station name (truncated if long).
- **Now Playing line** from ICY metadata (if available):
  - When Artist and Title are both parsed: `"Artist — Song Title"` (single truncated line, secondary opacity).
  - When only a raw title string is present: display it as-is.
  - When no metadata is available: the line is hidden (no empty placeholder).
- Play/Pause button.
- Stop button.
- Next Station button (skip to next favourite).
- Previous Station button (go to previous favourite).
- Volume slider (compact) with mute toggle.
- Favorite toggle (heart icon).

**Behavior:**
- Always visible when a station is loaded (playing or paused).
- Hidden or shows empty state when no station has been selected.
- **Next/Previous** cycle through the favourites list in order. If the current station is not a favourite, Next jumps to the first favourite.
- Stays in sync across all pages — toggling favourite on any page updates the mini-player heart icon.

---

## 9. Media Key & Keyboard Support

The app responds to hardware media keys and keyboard shortcuts globally (even when minimized to tray):

| Key | Action |
|---|---|
| **Media Play/Pause** | Toggle play/pause on current station |
| **Media Next Track** | Skip to next station in the favourites list |
| **Media Previous Track** | Go to previous station in the favourites list |
| **Media Stop** | Stop playback |

**Behaviour:**
- Next/Previous cycle through favourites in list order, wrapping around at the end.
- If no station is playing and Next is pressed, playback starts from the first favourite.
- Media keys work even when the app is minimized to the system tray.

---

## 10. System Tray Integration

When the user closes the window (clicks X), the app minimizes to the system tray instead of quitting.

**Tray icon context menu:**
- Station name (disabled/label, shows currently playing station or "Not Playing").
- Play / Pause.
- Next Station.
- Previous Station.
- Show Window (restore from tray).
- Quit (fully exit the application).

**Behaviour:**
- Double-clicking the tray icon restores the window.
- The tray icon shows a tooltip with the current station name.
- "Quit" in the tray menu is the only way to fully close the app (apart from Task Manager).

---

## 11. UI / UX Guidelines

Follow `NatboaFluentGuidelines_Relaxed.md` throughout:

- **Window:** `FluentWindow` + Mica backdrop + integrated title bar.
- **Navigation:** `NavigationView` (left pane) with 4 items: Browse, Discover, Favourites, Settings.
- **Cards:** Use `ui:Card` for grouping content sections and genre cards.
- **Buttons:** `Primary` for play/main actions, `Transparent` for icon-only toolbar buttons.
- **Typography:** Page titles at 24-28px SemiBold, section headers at 16px SemiBold, captions at reduced opacity.
- **Spacing:** Multiples of 4/8px, 16-24px page padding, rounded corners (4px controls, 8px containers).
- **Theming:** Light and Dark modes only, via `ApplicationThemeManager`. All colors come from WPF-UI's built-in Fluent UI semantic brushes — no custom or hardcoded colors anywhere. Theme switches apply instantly without restarting.
- **Data lists:** Infinite-scroll with batch loading. Clean rows, alternating backgrounds, accent-colored selection.

---

## 12. Performance Requirements

- **Startup:** App window visible within 2 seconds; database loaded in background.
- **Search:** Results appear within 500ms of user stopping typing.
- **Playback start:** Audio begins within 3 seconds of clicking play (network dependent).
- **Memory:** Under 200 MB typical usage.
- **Infinite scroll:** Each batch loads 50 stations. Next batch fetches when the user scrolls to within 10 items of the bottom. No perceptible lag between batches.

---

## 13. Non-Functional Requirements

- Single-instance application (prevent multiple windows).
- System tray residence — closing the window minimizes to tray, not quit.
- Graceful offline behavior — show cached favorites and last-played info; disable streaming with a clear message.
- No telemetry or network calls beyond audio streaming and logo fetching.
- Installer via MSIX or single-exe publish (self-contained .NET 8).

---

## 14. Out of Scope (v1)

- User accounts or cloud sync.
- Recording/saving streams.
- Equalizer or audio effects.
- Station ratings or community features.
- Streaming protocols beyond HTTP/HTTPS (no DAB, FM, etc.).
- Chromecast or external device casting.
- Custom playlists (beyond favourites).
- Favourite reordering.

---

## 15. Milestone Plan

### M1 — Foundation
- Project scaffold (WPF + WPF-UI + EF Core + SQLite).
- FluentWindow shell with NavigationView and page stubs (Browse, Discover, Favourites, Settings).
- Database context and model classes wired to existing `radioapp_large_groups.db`.
- Theme support (Light/Dark/System).
- Persistent mini-player bar (UI shell, no playback yet).

### M2 — Browse & Discover
- Browse page: search box + infinite-scroll station list.
- Discover page: genre grid → station list drill-down with infinite scroll.
- Search-within-genre filter on Discover.
- Shared station list item component (used across all pages).

### M3 — Playback & Media Keys
- LibVLCSharp integration.
- Mini-player: play/pause/stop, volume, ICY metadata display.
- Next/Previous station navigation through favourites.
- Media key support (Play/Pause, Next, Previous, Stop).

### M4 — Favourites & Persistence
- Favourite toggling from any station list or mini-player.
- Favourites page with full list.
- Import/export favourites (M3U and JSON formats).
- Session persistence (last station, volume) via Settings table.

### M5 — System Tray & Polish
- System tray integration (minimize to tray, context menu, tray icon).
- Settings page (theme, audio device, about, bulk M3U import).
- Error handling and offline states.
- Performance profiling and optimization.
- Installer packaging.
