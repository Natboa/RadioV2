[README](README.md) | [Code of Conduct](CODE_OF_CONDUCT.md) | [Contributing](CONTRIBUTING.md) | [License](LICENSE)

---

<p align="center">
  <img src="Assets/radiov2_Logo_full.png" alt="RadioV2 Logo" width="320"/>
</p>

<h3 align="center">A modern Windows desktop internet radio player built with Fluent Design.</h3>

---

<p align="center">
  <img src="https://img.shields.io/github/actions/workflow/status/Natboa/radioV2/build.yml?style=flat-square&label=build" alt="Build Status"/>
  <img src="https://img.shields.io/badge/version-1.0.0-blue?style=flat-square" alt="Version"/>
  <img src="https://img.shields.io/github/license/Natboa/radioV2?style=flat-square" alt="License"/>
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 8"/>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4?style=flat-square&logo=windows" alt="Platform"/>
</p>

---

## Overview

RadioV2 is a lightweight  Desktop application for discovering, browsing, and streaming internet radio stations. It ships with a pre-seeded database of tens of thousands of stations organized by genre, and delivers a native Windows 11 experience through the WPF Fluent Design system.

## Features

- Browse and search stations across dozens of genre groups
- Discover stations by category with infinite-scroll lists
- Save and manage favourite stations
- Import and export favourites in M3U/M3U8 and JSON formats
- Persistent mini-player bar with live now-playing metadata
- Global media key support (Play/Pause, Next, Previous, Stop)
- System tray integration — minimize to tray, restore on double-click
- Light and Dark theme support with Mica backdrop

## Tech Stack

| Layer | Technology |
|---|---|
| UI Framework | WPF + [WPF-UI](https://github.com/lepoco/wpfui) (Fluent Design 2) |
| Architecture | MVVM via `CommunityToolkit.Mvvm` |
| Audio Playback | LibVLCSharp + VideoLAN.LibVLC.Windows |
| Database | SQLite 3 via Entity Framework Core 8 |
| Target Framework | .NET 8 (Windows) |

## Getting Started

**Prerequisites:** .NET 8 SDK, Windows 10 or 11.

```bash
git clone https://github.com/Natboa/radioV2.git
cd radioV2
dotnet build
dotnet run
```

**Publish as a self-contained single-file executable:**

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true
```

## Project Structure

```
RadioV2/
├── Assets/             # Icons, logos, genre images
├── Data/               # SQLite database
├── Docs/               # PRD, tech stack, implementation plan
├── Helpers/            # NowPlayingParser, ThemeHelper, MediaKeyHook
├── Models/             # EF Core entities: Station, Group, Setting
├── Services/           # Playback, data access, favourites, M3U import
├── ViewModels/         # One ViewModel per page + MiniPlayer + MainWindow
├── Views/              # Browse, Discover, Favourites, Settings pages
└── Controls/           # Reusable controls: StationListItem, MiniPlayer
```

## License

Distributed under the MIT License. See [LICENSE](LICENSE) for details.
