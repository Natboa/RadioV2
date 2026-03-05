# RadioV2 — Technology Stack

## 1. Runtime & Language

| Choice | Details |
|---|---|
| **.NET 8 (LTS)** | Long-term support until November 2026. Best performance and API surface for Windows desktop apps. |
| **C# 12** | Latest language features: primary constructors, collection expressions, and improved pattern matching. |
| **Target** | `net8.0-windows` — enables WPF and Windows-specific APIs. |

**Why .NET 8 over .NET 9:** .NET 8 is the current LTS release. .NET 9 is STS (support ends May 2025). For a desktop app that ships to users, LTS stability matters more than bleeding-edge features.

---

## 2. UI Framework

| Choice | Details |
|---|---|
| **WPF** | Mature, GPU-accelerated, deep data-binding and templating support. The gold standard for rich Windows desktop UI. |
| **[WPF-UI](https://github.com/lepoco/wpfui) (Wpf.Ui)** | Provides Fluent Design 2 controls, Mica/Acrylic backdrops, `FluentWindow`, `NavigationView`, and theme management — all matching our Natboa Fluent Guidelines. |

**NuGet Package:** `WPF-UI` (latest stable, currently 3.x)

### Why WPF-UI over alternatives

| Alternative | Why Not |
|---|---|
| **WinUI 3** | Promising but still has packaging friction (MSIX required for some features), fewer community resources, and less mature third-party ecosystem. |
| **Avalonia UI** | Cross-platform is unnecessary here; WPF-UI gives a more native Windows 11 feel with less effort. |
| **MAUI** | Designed for mobile-first cross-platform. Desktop support is secondary and the control library is limited compared to WPF. |
| **Raw WPF (no toolkit)** | Would require manually recreating every Fluent control, backdrop, and theme — months of extra work for no benefit. |

---

## 3. Architecture & MVVM

| Choice | Details |
|---|---|
| **CommunityToolkit.Mvvm** | Microsoft's official, lightweight MVVM toolkit. Source-generated `ObservableProperty`, `RelayCommand`, and `ObservableObject` — minimal boilerplate. |
| **Dependency Injection** | `Microsoft.Extensions.DependencyInjection` — wire up ViewModels, services, and the DbContext via the built-in .NET DI container. |

**NuGet Packages:**
- `CommunityToolkit.Mvvm`
- `Microsoft.Extensions.DependencyInjection`

### Project Structure (recommended)

```
RadioV2/
├── App.xaml                    # Application entry, DI setup, theme init
├── Models/                     # EF Core entities (Station, Group, Setting)
├── Data/
│   └── RadioDbContext.cs       # EF Core DbContext
├── Services/
│   ├── IRadioPlayerService.cs  # Playback abstraction
│   ├── RadioPlayerService.cs   # LibVLCSharp implementation
│   ├── IStationService.cs      # Data access abstraction
│   ├── StationService.cs       # EF Core queries
│   └── M3UParserService.cs     # M3U import
├── ViewModels/
│   ├── MainWindowViewModel.cs
│   ├── NowPlayingViewModel.cs
│   ├── DiscoverViewModel.cs
│   ├── SearchViewModel.cs
│   ├── FavoritesViewModel.cs
│   └── SettingsViewModel.cs
├── Views/
│   ├── MainWindow.xaml
│   ├── NowPlayingPage.xaml
│   ├── DiscoverPage.xaml
│   ├── SearchPage.xaml
│   ├── FavoritesPage.xaml
│   └── SettingsPage.xaml
├── Controls/                   # Reusable UI components
│   ├── StationListItem.xaml    # Shared station row template
│   └── MiniPlayer.xaml         # Bottom playback bar
├── Converters/                 # Value converters
├── Assets/                     # Icons, images, fonts
├── Data/
│   └── radioapp_large_groups.db
└── Docs/
```

---

## 4. Database & ORM

| Choice | Details |
|---|---|
| **SQLite 3** | Already in use. Zero-config, embedded, single-file database. Perfect for a local desktop app with a pre-seeded ~80 MB station catalog. |
| **Entity Framework Core 8 (SQLite provider)** | Type-safe queries, migrations, LINQ support. Consistent with the existing schema doc. |

**NuGet Packages:**
- `Microsoft.EntityFrameworkCore.Sqlite`
- `Microsoft.EntityFrameworkCore.Design` (dev only, for migrations)

### Performance considerations for large datasets
- Use `AsNoTracking()` for all read-only queries (search, browse) — avoids change-tracker overhead on tens of thousands of rows.
- Create a SQLite index on `Stations.Name` for fast `LIKE` search.
- Use `IQueryable` with server-side pagination rather than loading full result sets into memory.
- Enable SQLite WAL mode for concurrent read performance.

---

## 5. Audio Playback — LibVLCSharp (Recommended)

| Choice | Details |
|---|---|
| **LibVLCSharp** | .NET bindings for the VLC media engine. Battle-tested, supports every codec and streaming protocol RadioV2 will encounter. |
| **VideoLAN.LibVLC.Windows** | The native VLC libraries bundled as a NuGet package — no separate VLC install required. |

**NuGet Packages:**
- `LibVLCSharp`
- `VideoLAN.LibVLC.Windows`

### Why LibVLCSharp over NAudio

| Factor | LibVLCSharp | NAudio |
|---|---|---|
| **Codec support** | MP3, AAC, HLS, Ogg, FLAC, WMA — all built-in | MP3 native; AAC/HLS require extra plugins or custom code |
| **HTTP streaming** | First-class support, handles redirects, ICY protocol | Requires manual `HttpClient` + `StreamMediaFoundationReader` plumbing |
| **ICY metadata** | Built-in event for track title changes | Must manually parse ICY headers from the raw HTTP stream |
| **Buffering events** | Exposes `Buffering` event with percentage | No built-in buffering feedback |
| **Reliability** | Powers VLC — handles edge-case streams gracefully | May choke on non-standard streams |
| **Package size** | ~120 MB (includes native libs) | ~2 MB |
| **Learning curve** | Simple: `new MediaPlayer(new Media(libVLC, url))` then `.Play()` | More wiring needed for streaming scenarios |

**Trade-off:** LibVLCSharp adds ~120 MB to the app bundle. This is acceptable for a desktop app that ships as an installer — reliability and codec coverage are worth it for a radio player.

### Integration pattern

```csharp
public class RadioPlayerService : IRadioPlayerService
{
    private readonly LibVLC _libVLC;
    private readonly MediaPlayer _mediaPlayer;

    public RadioPlayerService()
    {
        Core.Initialize();
        _libVLC = new LibVLC();
        _mediaPlayer = new MediaPlayer(_libVLC);
    }

    public void Play(string streamUrl)
    {
        var media = new Media(_libVLC, streamUrl, FromType.FromLocation);
        _mediaPlayer.Play(media);
    }

    public void Pause() => _mediaPlayer.Pause();
    public void Stop() => _mediaPlayer.Stop();
    public int Volume { get => _mediaPlayer.Volume; set => _mediaPlayer.Volume = value; }
}
```

---

## 6. Image Loading

| Choice | Details |
|---|---|
| **Built-in WPF BitmapImage** | Handles HTTP image URLs natively via `BitmapImage(new Uri(logoUrl))`. |
| **Local caching** | Cache downloaded logos to `%AppData%/RadioV2/cache/` to avoid re-fetching. Use a simple `HttpClient` download-on-first-access pattern. |

No additional NuGet package needed. For the logo thumbnails in lists, set `DecodePixelWidth` to limit memory (e.g., 48px for list items, 200px for Now Playing).

---

## 7. Additional Libraries

| Package | Purpose |
|---|---|
| `Microsoft.Extensions.Hosting` | App host builder for DI, configuration, and lifecycle management. |
| `Microsoft.Xaml.Behaviors.Wpf` | Attach behaviors to XAML elements without code-behind (e.g., debounced search TextBox). |
| `Serilog` + `Serilog.Sinks.File` | Structured logging to a file for debugging stream errors and crashes. Optional but recommended. |

---

## 8. Development Tools

| Tool | Purpose |
|---|---|
| **Visual Studio 2022** | Primary IDE — best WPF designer and XAML tooling support. |
| **VS Code** | Lightweight editing, markdown docs, git operations. |
| **SQLite Browser (DB Browser for SQLite)** | Inspect and query the station database during development. |
| **Git** | Version control. |

---

## 9. Packaging & Distribution

| Option | Details |
|---|---|
| **Single-file publish (recommended)** | `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` — produces one `.exe` + the LibVLC native folder + the `.db` file. Simplest for users. |
| **MSIX** | Alternative if you want auto-updates via the Microsoft Store or sideloading. More setup overhead. |

### Publish command

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true
```

---

## 10. Complete NuGet Package List

```xml
<ItemGroup>
  <!-- UI -->
  <PackageReference Include="WPF-UI" Version="3.*" />

  <!-- MVVM -->
  <PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />

  <!-- DI & Hosting -->
  <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.*" />

  <!-- Database -->
  <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.*" />

  <!-- Audio -->
  <PackageReference Include="LibVLCSharp" Version="3.*" />
  <PackageReference Include="VideoLAN.LibVLC.Windows" Version="3.*" />

  <!-- XAML Behaviors -->
  <PackageReference Include="Microsoft.Xaml.Behaviors.Wpf" Version="1.*" />

  <!-- Logging (optional) -->
  <PackageReference Include="Serilog" Version="3.*" />
  <PackageReference Include="Serilog.Sinks.File" Version="5.*" />
</ItemGroup>
```

---

## 11. Decision Summary

| Concern | Decision | Confidence |
|---|---|---|
| UI framework | WPF + WPF-UI | High — aligns with Natboa guidelines, mature ecosystem |
| MVVM toolkit | CommunityToolkit.Mvvm | High — Microsoft-backed, source-generated, minimal boilerplate |
| Audio engine | LibVLCSharp | High — best codec/protocol coverage for radio streaming |
| Database | SQLite + EF Core 8 | High — already in use, proven at scale |
| Target runtime | .NET 8 LTS | High — stability and long-term support |
| Packaging | Single-file self-contained | Medium — simplest option; MSIX if Store distribution needed later |
