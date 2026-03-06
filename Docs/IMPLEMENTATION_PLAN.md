# RadioV2 — Detailed Implementation Plan

This document breaks the PRD milestones into concrete, step-by-step implementation tasks. Each step specifies the files to create/modify, code patterns to use, and acceptance criteria.

---

## Milestone 1 — Foundation

> Goal: A running WPF app with Fluent shell, navigation between 4 empty pages, database connectivity, and the mini-player UI shell.

---

### Step 1.1 — Project Scaffold

**Action:** Create the .NET 8 WPF project and install all NuGet packages.

```bash
dotnet new wpf -n RadioV2 --framework net8.0-windows
cd RadioV2
dotnet add package WPF-UI
dotnet add package CommunityToolkit.Mvvm
dotnet add package Microsoft.Extensions.Hosting
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
dotnet add package LibVLCSharp
dotnet add package VideoLAN.LibVLC.Windows
dotnet add package Microsoft.Xaml.Behaviors.Wpf
dotnet add package Serilog
dotnet add package Serilog.Sinks.File
```

**Files created:**
- `RadioV2.csproj` — verify `<TargetFramework>net8.0-windows</TargetFramework>` and `<UseWPF>true</UseWPF>`.

**Post-step:** Copy `Data/radioapp_large_groups.db` into the project directory. Add to `.csproj`:
```xml
<ItemGroup>
  <None Update="Data\radioapp_large_groups.db">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

**Acceptance:** `dotnet build` succeeds with no errors.

---

### Step 1.2 — Folder Structure

**Action:** Create the project folder skeleton.

```
RadioV2/
├── Models/
├── Data/
├── Services/
├── ViewModels/
├── Views/
├── Controls/
├── Converters/
├── Assets/
├── Helpers/
```

No code yet — just empty directories for organization.

---

### Step 1.3 — EF Core Models & DbContext

**Action:** Create entity classes matching the existing database schema.

**Files to create:**

**`Models/Station.cs`**
```csharp
public class Station
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public int GroupId { get; set; }
    public bool IsFavorite { get; set; }

    public Group Group { get; set; } = null!;
}
```

**`Models/Group.cs`**
```csharp
public class Group
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public ICollection<Station> Stations { get; set; } = new List<Station>();
}
```

**`Models/Setting.cs`**
```csharp
public class Setting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
```

**`Data/RadioDbContext.cs`**
```csharp
public class RadioDbContext : DbContext
{
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Setting> Settings => Set<Setting>();

    public RadioDbContext(DbContextOptions<RadioDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Station>(e =>
        {
            e.HasIndex(s => s.StreamUrl).IsUnique();
            e.HasIndex(s => s.Name); // For fast LIKE search
            e.HasOne(s => s.Group).WithMany(g => g.Stations).HasForeignKey(s => s.GroupId);
        });

        modelBuilder.Entity<Setting>(e =>
        {
            e.HasKey(s => s.Key);
        });
    }
}
```

**Critical:** This maps to the **existing** database. Do NOT use EF migrations to create the DB — use `Database.EnsureCreated()` only as a fallback. The DB file is pre-seeded.

**Acceptance:** Project builds. DbContext can be instantiated in a test harness and query the existing `.db` file.

---

### Step 1.4 — Dependency Injection & App Host

**Action:** Set up `Microsoft.Extensions.Hosting` in `App.xaml.cs` to wire DI for the entire app.

**Files to modify:**

**`App.xaml`** — Change `StartupUri="MainWindow.xaml"` to remove it (we'll show the window manually from code).

**`App.xaml.cs`**
```csharp
public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Database
                var dbPath = Path.Combine(AppContext.BaseDirectory, "Data", "radioapp_large_groups.db");
                services.AddDbContext<RadioDbContext>(options =>
                    options.UseSqlite($"Data Source={dbPath}"));

                // Services (registered in later steps)
                // services.AddSingleton<IRadioPlayerService, RadioPlayerService>();
                // services.AddScoped<IStationService, StationService>();

                // ViewModels
                services.AddSingleton<MainWindowViewModel>();
                services.AddTransient<BrowseViewModel>();
                services.AddTransient<DiscoverViewModel>();
                services.AddTransient<FavouritesViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddSingleton<MiniPlayerViewModel>();

                // Main Window
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();

        // Apply theme
        ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica, true);

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }
}
```

**Key decisions:**
- `RadioDbContext` registered as scoped (one per operation, not singleton — avoids threading issues).
- `MiniPlayerViewModel` and `MainWindowViewModel` are singletons (shared state across pages).
- Page ViewModels are transient (fresh state when navigating).

**Acceptance:** App launches, DI container builds without errors.

---

### Step 1.5 — FluentWindow Shell & NavigationView

**Action:** Create the main window with WPF-UI's `FluentWindow`, integrated title bar, left-pane navigation, and the mini-player placeholder.

**Files to create/modify:**

**`Views/MainWindow.xaml`**
```xml
<ui:FluentWindow x:Class="RadioV2.Views.MainWindow"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    Title="RadioV2"
    Width="1100" Height="700"
    WindowBackdropType="Mica"
    ExtendsContentIntoTitleBar="True">

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />   <!-- Title bar -->
            <RowDefinition Height="*" />       <!-- Navigation + content -->
            <RowDefinition Height="Auto" />    <!-- Mini-player -->
        </Grid.RowDefinitions>

        <ui:TitleBar Grid.Row="0" Title="RadioV2" />

        <ui:NavigationView Grid.Row="1"
            x:Name="RootNavigation"
            PaneDisplayMode="Left"
            IsBackButtonVisible="Visible">
            <ui:NavigationView.MenuItems>
                <ui:NavigationViewItem Content="Browse" TargetPageType="{x:Type views:BrowsePage}" Icon="{ui:SymbolIcon Search24}" />
                <ui:NavigationViewItem Content="Discover" TargetPageType="{x:Type views:DiscoverPage}" Icon="{ui:SymbolIcon Compass24}" />
                <ui:NavigationViewItem Content="Favourites" TargetPageType="{x:Type views:FavouritesPage}" Icon="{ui:SymbolIcon Heart24}" />
            </ui:NavigationView.MenuItems>
            <ui:NavigationView.FooterMenuItems>
                <ui:NavigationViewItem Content="Settings" TargetPageType="{x:Type views:SettingsPage}" Icon="{ui:SymbolIcon Settings24}" />
            </ui:NavigationView.FooterMenuItems>
        </ui:NavigationView>

        <!-- Mini-Player placeholder (Step 1.6) -->
        <Border Grid.Row="2" Height="72"
            Background="{ui:ThemeResource ControlFillColorDefaultBrush}"
            CornerRadius="8,8,0,0"
            Padding="16,8">
            <TextBlock Text="Mini Player — coming soon"
                VerticalAlignment="Center"
                Opacity="0.5" />
        </Border>
    </Grid>
</ui:FluentWindow>
```

**`Views/MainWindow.xaml.cs`** — Constructor receives `MainWindowViewModel` via DI, sets `DataContext`. Also sets up the `NavigationView` page service.

**Page stubs to create** (each is a minimal `ui:FluentPage`):
- `Views/BrowsePage.xaml` + `.cs`
- `Views/DiscoverPage.xaml` + `.cs`
- `Views/FavouritesPage.xaml` + `.cs`
- `Views/SettingsPage.xaml` + `.cs`

Each page stub contains a centered `TextBlock` with the page name (e.g., "Browse Page") and receives its ViewModel via DI.

**ViewModels to create** (minimal stubs):
- `ViewModels/MainWindowViewModel.cs` — inherits `ObservableObject`
- `ViewModels/BrowseViewModel.cs` — inherits `ObservableObject`
- `ViewModels/DiscoverViewModel.cs` — inherits `ObservableObject`
- `ViewModels/FavouritesViewModel.cs` — inherits `ObservableObject`
- `ViewModels/SettingsViewModel.cs` — inherits `ObservableObject`
- `ViewModels/MiniPlayerViewModel.cs` — inherits `ObservableObject`

**Acceptance:** App launches with Mica backdrop. NavigationView shows 4 items. Clicking each item navigates to the corresponding stub page. Mini-player placeholder bar is visible at the bottom.

---

### Step 1.6 — Mini-Player Control (UI Shell)

**Action:** Create the `MiniPlayer` user control with the final layout but no playback wiring yet.

**File to create: `Controls/MiniPlayer.xaml`**

Layout (horizontal `DockPanel` or `Grid`):
- Left: Station logo `Image` (40x40, rounded), station name `TextBlock`, track title `TextBlock` (secondary opacity).
- Center: Previous button (`ui:Button` Transparent, `SymbolIcon Previous24`), Play/Pause button (`ui:Button` Primary, `SymbolIcon Play24`), Stop button, Next button.
- Right: Volume `Slider` (width ~120), Mute toggle (`ui:Button` Transparent, `SymbolIcon Speaker224`), Favourite heart toggle.

**File to create: `Controls/MiniPlayer.xaml.cs`** — DataContext bound to `MiniPlayerViewModel` (injected or set from `MainWindow`).

**`MiniPlayerViewModel` properties** (all `[ObservableProperty]`):
- `string StationName`, `string? StationLogoUrl`
- `string? NowPlayingArtist` — parsed artist name from ICY metadata; null when unavailable.
- `string? NowPlayingTitle` — parsed song title from ICY metadata; null when unavailable.
- `string? NowPlayingDisplay` — computed, read-only: returns `"Artist — Title"` when both fields are set, `Title` alone when only title is available, or `null` when no metadata. Used for the mini-player's secondary text line.
- `bool IsPlaying`, `bool IsFavourite`, `bool IsBuffering`
- `int Volume` (0-100, default 50)
- `bool IsMuted`
- `bool HasStation` (computed — true if a station is loaded)
- `bool HasNowPlaying` (computed — true when `NowPlayingDisplay` is not null; controls visibility of the Now Playing line)

**`MiniPlayerViewModel` commands** (all `[RelayCommand]`, wired in M3):
- `PlayPause()`, `Stop()`, `NextStation()`, `PreviousStation()`, `ToggleFavourite()`, `ToggleMute()`

**Acceptance:** Mini-player renders at the bottom with all buttons and the volume slider. Buttons don't do anything yet. The layout responds correctly to window resizing.

---

### Step 1.7 — Theme Support (Light / Dark)

**Action:** Implement Light/Dark theme switching using WPF-UI's built-in Fluent UI colors.

**`Helpers/ThemeHelper.cs`**
```csharp
public static class ThemeHelper
{
    public static void ApplyTheme(string theme)
    {
        var appTheme = theme switch
        {
            "Light" => ApplicationTheme.Light,
            _       => ApplicationTheme.Dark   // Default to Dark
        };
        ApplicationThemeManager.Apply(appTheme, WindowBackdropType.Mica, true);
    }
}
```

**On startup** (`App.xaml.cs`, before showing the window): read `"Theme"` from the Settings table → call `ThemeHelper.ApplyTheme(value)`. Default: `"Dark"`.

All colors in the app come exclusively from WPF-UI's semantic theme brushes (e.g., `{ui:ThemeResource TextFillColorPrimaryBrush}`). No custom colors, no accent overrides, no hardcoded hex values anywhere in XAML or code.

**Acceptance:** App starts in Dark mode by default. The Light/Dark toggle in Settings switches the theme immediately. All controls, backgrounds, and text automatically use the correct Fluent UI colors for the active theme.

---

### Step 1.8 — Single-Instance Enforcement

**Action:** Use a named `Mutex` to prevent multiple instances.

In `App.xaml.cs` `OnStartup`:
```csharp
private Mutex? _mutex;

protected override void OnStartup(StartupEventArgs e)
{
    _mutex = new Mutex(true, "RadioV2_SingleInstance", out bool isNew);
    if (!isNew)
    {
        MessageBox.Show("RadioV2 is already running.");
        Shutdown();
        return;
    }
    // ... rest of startup
}
```

**Acceptance:** Launching a second instance shows a message and closes immediately.

---

## Milestone 2 — Browse & Discover

> Goal: Both data pages are fully functional with infinite scroll, search, and the shared station list item.

---

### Step 2.1 — Station Service (Data Access Layer)

**Action:** Create the service that handles all station/group database queries with pagination.

**Files to create:**

**`Services/IStationService.cs`**
```csharp
public interface IStationService
{
    Task<List<Station>> GetStationsAsync(int skip, int take, string? searchQuery = null, CancellationToken ct = default);
    Task<List<Group>> GetGroupsWithCountsAsync(CancellationToken ct = default);
    Task<List<Station>> GetStationsByGroupAsync(int groupId, int skip, int take, string? searchQuery = null, CancellationToken ct = default);
    Task<List<Station>> GetFavouritesAsync(CancellationToken ct = default);
    Task ToggleFavouriteAsync(int stationId, CancellationToken ct = default);
    Task<string?> GetSettingAsync(string key, CancellationToken ct = default);
    Task SetSettingAsync(string key, string value, CancellationToken ct = default);
}
```

**`Services/StationService.cs`**

Implementation notes:
- All read queries use `.AsNoTracking()`.
- `GetStationsAsync` applies `LIKE` on `Name` if `searchQuery` is provided (min 2 chars enforced at ViewModel level).
- All list queries use `.Skip(skip).Take(take)` for pagination.
- `GetGroupsWithCountsAsync` returns groups with a `StationCount` — use a projection: `Select(g => new { g.Id, g.Name, Count = g.Stations.Count })`. Return as a DTO or add a non-mapped property.
- `ToggleFavouriteAsync` loads the station tracked, flips `IsFavorite`, calls `SaveChangesAsync`.

**DTO to create: `Models/GroupWithCount.cs`**
```csharp
public class GroupWithCount
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int StationCount { get; set; }
}
```

**Register in DI:** `services.AddScoped<IStationService, StationService>();`

**Acceptance:** Service methods can be called from a ViewModel and return correct paginated data from the real `.db` file.

---

### Step 2.2 — Shared Station List Item Control

**Action:** Create a reusable `DataTemplate` or `UserControl` for displaying a single station row across Browse, Discover, and Favourites.

**File to create: `Controls/StationListItem.xaml`** (as a `UserControl`)

Layout (horizontal):
- `Image` — 40x40, rounded corners (4px), bound to `LogoUrl`. Use a fallback/default icon if null or load fails. Set `DecodePixelWidth="40"` to save memory.
- `StackPanel` (vertical) — Station `Name` (FontSize 14, SemiBold), Group `Name` (FontSize 12, Opacity 0.7).
- Right-aligned buttons:
  - Play button (`ui:Button` Transparent, `SymbolIcon Play24`) — `Command` bound to a play action.
  - Favourite toggle (`ui:ToggleButton` Transparent, heart icon filled/unfilled based on `IsFavorite`).

**Data binding:** The control's `DataContext` is a `Station` entity. Commands are routed to the parent page's ViewModel via `RelativeSource` binding or an event-based approach.

**Acceptance:** The control renders correctly with sample data and adapts to theme changes.

---

### Step 2.3 — Infinite Scroll Behaviour

**Action:** Create a reusable scroll behaviour that detects when the user is near the bottom of a `ScrollViewer` or `ListBox` and triggers a command to load more data.

**File to create: `Helpers/InfiniteScrollBehavior.cs`**

Using `Microsoft.Xaml.Behaviors.Wpf`:
```csharp
public class InfiniteScrollBehavior : Behavior<ScrollViewer>
{
    public static readonly DependencyProperty LoadMoreCommandProperty =
        DependencyProperty.Register(nameof(LoadMoreCommand), typeof(ICommand), typeof(InfiniteScrollBehavior));

    public ICommand LoadMoreCommand { get; set; }

    // Threshold: trigger when within 200px of bottom
    // On ScrollChanged, check: if (scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 200) invoke command
}
```

Alternative approach: attach to a `ListBox`'s internal `ScrollViewer` and detect the same condition. Either approach is valid.

**Acceptance:** Attaching this behaviour to any scrollable list triggers the bound command when scrolling near the bottom.

---

### Step 2.4 — Browse Page (Full Implementation)

**Action:** Implement the Browse page with search and infinite scroll.

**`ViewModels/BrowseViewModel.cs`**

Properties:
- `ObservableCollection<Station> Stations` — the visible list, appended to on each batch load.
- `string SearchQuery` — bound to the search TextBox. On change (debounced), clear `Stations`, reset skip counter, and load first batch.
- `bool IsLoading` — shows a loading indicator.
- `bool HasMoreItems` — false when a batch returns fewer than 50 items.

Commands:
- `LoadMoreCommand` — async, calls `IStationService.GetStationsAsync(skip, 50, searchQuery)`, appends to `Stations`, increments skip by 50.
- `PlayStationCommand(Station)` — sets the station on `MiniPlayerViewModel` (wired in M3).
- `ToggleFavouriteCommand(Station)` — calls `IStationService.ToggleFavouriteAsync`, updates the station's `IsFavorite` in the collection.

Debounce logic:
- Use a `CancellationTokenSource` that resets on each keystroke. After 300ms without a new keystroke, execute the search.

**`Views/BrowsePage.xaml`**

Layout:
- Page title: "Browse" (FontSize 28, SemiBold).
- `ui:TextBox` with placeholder "Search stations..." and a search icon.
- `ListBox` with `ItemTemplate` using `StationListItem`, `VirtualizingStackPanel` as items panel.
- Attach `InfiniteScrollBehavior` to the ListBox's ScrollViewer.
- Loading spinner (`ui:ProgressRing`) at the bottom, visible when `IsLoading`.

**Acceptance:** Page loads first 50 stations on navigate. Scrolling to the bottom loads the next 50. Typing in search filters results after 300ms debounce. Favourite toggle updates the heart icon immediately.

---

### Step 2.5 — Discover Page: Genre Grid

**Action:** Implement the genre selection grid.

**`ViewModels/DiscoverViewModel.cs`**

Properties:
- `ObservableCollection<GroupWithCount> Groups` — loaded once on page navigation.
- `GroupWithCount? SelectedGroup` — when set, triggers loading the station sub-view.
- `ObservableCollection<Station> GroupStations` — stations for the selected group.
- `string GroupSearchQuery` — filter within the selected genre.
- `bool IsGroupView` — true when a genre is selected (controls which sub-view is visible).
- `bool IsLoading`, `bool HasMoreItems`.

Commands:
- `LoadGroupsCommand` — async, calls `IStationService.GetGroupsWithCountsAsync()`.
- `SelectGroupCommand(GroupWithCount)` — sets `SelectedGroup`, clears `GroupStations`, loads first batch.
- `LoadMoreGroupStationsCommand` — async, calls `IStationService.GetStationsByGroupAsync(groupId, skip, 50, searchQuery)`.
- `BackToGroupsCommand` — clears `SelectedGroup`, sets `IsGroupView = false`.
- `PlayStationCommand(Station)`, `ToggleFavouriteCommand(Station)` — same as Browse.

**`Views/DiscoverPage.xaml`**

Two visual states (use `Visibility` binding on `IsGroupView`):

**State 1 — Genre grid** (`IsGroupView == false`):
- Page title: "Discover" (FontSize 28, SemiBold).
- `ItemsControl` with `WrapPanel` as items panel.
- Each genre is a `ui:Card` (clickable) showing:
  - Group name (FontSize 16, SemiBold).
  - Station count (FontSize 12, Opacity 0.7, e.g., "342 stations").
- Cards have consistent width (~180px) and spacing (8px gap).

**State 2 — Station list** (`IsGroupView == true`):
- Back button + group name as header.
- `ui:TextBox` with placeholder "Filter within [GroupName]...".
- `ListBox` with `StationListItem` template, infinite scroll, loading spinner.

**Acceptance:** Discover page shows genre cards with correct station counts. Clicking a genre shows stations with infinite scroll. Search within genre filters correctly. Back button returns to genre grid.

---

## Milestone 3 — Playback & Media Keys

> Goal: Fully functional audio playback via the mini-player, with media key support.

---

### Step 3.1 — RadioPlayerService (LibVLCSharp)

**Action:** Implement the audio playback service.

**Files to create:**

**`Services/IRadioPlayerService.cs`**
```csharp
public interface IRadioPlayerService
{
    void Play(string streamUrl);
    void Pause();
    void Stop();
    void TogglePlayPause();
    int Volume { get; set; }
    bool IsPlaying { get; }
    bool IsPaused { get; }

    event EventHandler<string>? MetadataChanged;   // ICY track title
    event EventHandler<float>? BufferingChanged;    // 0-100%
    event EventHandler? PlaybackStarted;
    event EventHandler? PlaybackStopped;
    event EventHandler<string>? PlaybackError;      // error message
}
```

**`Services/RadioPlayerService.cs`**

Implementation:
```csharp
public class RadioPlayerService : IRadioPlayerService, IDisposable
{
    private readonly LibVLC _libVLC;
    private readonly MediaPlayer _mediaPlayer;

    public RadioPlayerService()
    {
        Core.Initialize();
        _libVLC = new LibVLC("--no-video");  // Audio only
        _mediaPlayer = new MediaPlayer(_libVLC);

        _mediaPlayer.Playing += (s, e) => PlaybackStarted?.Invoke(this, EventArgs.Empty);
        _mediaPlayer.Stopped += (s, e) => PlaybackStopped?.Invoke(this, EventArgs.Empty);
        _mediaPlayer.Buffering += (s, e) => BufferingChanged?.Invoke(this, e.Cache);
        _mediaPlayer.EncounteredError += (s, e) => PlaybackError?.Invoke(this, "Stream error");

        // ICY metadata — fires whenever the stream updates the NowPlaying tag.
        // MediaChanged fires when a new Media object is set (i.e., a new station begins loading).
        // MetaChanged fires repeatedly during playback as the station updates track info.
        _mediaPlayer.MediaChanged += (s, e) =>
        {
            if (_mediaPlayer.Media != null)
            {
                _mediaPlayer.Media.MetaChanged += (ms, me) =>
                {
                    // MetadataType.NowPlaying maps to the ICY StreamTitle field.
                    // Typical format: "Artist - Song Title" (dash-separated).
                    // Some stations omit it entirely; Media.Meta() returns null in that case.
                    var rawNowPlaying = _mediaPlayer.Media?.Meta(MetadataType.NowPlaying);
                    if (!string.IsNullOrWhiteSpace(rawNowPlaying))
                        MetadataChanged?.Invoke(this, rawNowPlaying);
                };
            }
        };
    }

    public void Play(string streamUrl)
    {
        var media = new Media(_libVLC, streamUrl, FromType.FromLocation);
        _mediaPlayer.Play(media);
    }

    public void Pause() => _mediaPlayer.Pause();
    public void Stop() => _mediaPlayer.Stop();
    public void TogglePlayPause() { if (IsPlaying) Pause(); else if (IsPaused) _mediaPlayer.Play(); }

    public int Volume
    {
        get => _mediaPlayer.Volume;
        set => _mediaPlayer.Volume = Math.Clamp(value, 0, 100);
    }

    public bool IsPlaying => _mediaPlayer.IsPlaying;
    public bool IsPaused => _mediaPlayer.State == VLCState.Paused;

    // Events
    public event EventHandler<string>? MetadataChanged;
    public event EventHandler<float>? BufferingChanged;
    public event EventHandler? PlaybackStarted;
    public event EventHandler? PlaybackStopped;
    public event EventHandler<string>? PlaybackError;

    public void Dispose()
    {
        _mediaPlayer.Dispose();
        _libVLC.Dispose();
    }
}
```

**Register in DI:** `services.AddSingleton<IRadioPlayerService, RadioPlayerService>();`

**Acceptance:** Calling `Play(url)` with a valid radio stream URL plays audio through speakers. Events fire correctly for buffering, play/stop, and metadata changes.

---

### Step 3.2 — Wire Mini-Player to Playback

**Action:** Connect `MiniPlayerViewModel` to `IRadioPlayerService` and `IStationService`.

**`MiniPlayerViewModel` updates:**

- Inject `IRadioPlayerService` and `IStationService` via constructor.
- Add `Station? CurrentStation` property — holds the full station object.
- `SetStation(Station station)` method — called by page ViewModels when user clicks play. Sets `CurrentStation`, updates display properties, calls `_playerService.Play(station.StreamUrl)`.
- Wire commands:
  - `PlayPause()` → `_playerService.TogglePlayPause()`
  - `Stop()` → `_playerService.Stop()`
  - `ToggleFavourite()` → `_stationService.ToggleFavouriteAsync(CurrentStation.Id)`, update `IsFavourite`.
  - `NextStation()` → see Step 3.3
  - `PreviousStation()` → see Step 3.3
  - `ToggleMute()` → store previous volume, set to 0, or restore.
- Subscribe to `IRadioPlayerService` events:
  - `MetadataChanged` → pass raw string to `NowPlayingParser.Parse()`, update `NowPlayingArtist` and `NowPlayingTitle` on the UI thread via `App.Current.Dispatcher.Invoke`. Both must be set atomically to avoid a flash of partial state.
  - `BufferingChanged` → update `IsBuffering` (true if < 100%)
  - `PlaybackStarted` → set `IsPlaying = true`
  - `PlaybackStopped` → set `IsPlaying = false`, clear `NowPlayingArtist` and `NowPlayingTitle`
  - `PlaybackError` → set `IsPlaying = false`, clear Now Playing, show error via a `Snackbar` or `InfoBar`.
- Volume slider two-way binds to `Volume` property which delegates to `_playerService.Volume`.

**Wire page ViewModels:** `BrowseViewModel` and `DiscoverViewModel` receive `MiniPlayerViewModel` (singleton) via DI. Their `PlayStationCommand` calls `_miniPlayer.SetStation(station)`.

**Acceptance:** Clicking play on any station in Browse or Discover starts playback. Mini-player shows station info, track title updates from ICY metadata. Play/Pause/Stop buttons work. Volume slider controls audio level. Favourite toggle works.

---

### Step 3.3 — Next/Previous Station (Favourites Navigation)

**Action:** Implement cycling through the favourites list.

**In `MiniPlayerViewModel`:**

```csharp
private List<Station> _favouritesList = new();
private int _currentFavouriteIndex = -1;

private async Task RefreshFavouritesListAsync()
{
    _favouritesList = await _stationService.GetFavouritesAsync();
}

[RelayCommand]
private async Task NextStation()
{
    await RefreshFavouritesListAsync();
    if (_favouritesList.Count == 0) return;

    // Find current station in favourites, or start from beginning
    _currentFavouriteIndex = _favouritesList.FindIndex(s => s.Id == CurrentStation?.Id);
    _currentFavouriteIndex = (_currentFavouriteIndex + 1) % _favouritesList.Count;

    SetStation(_favouritesList[_currentFavouriteIndex]);
}

[RelayCommand]
private async Task PreviousStation()
{
    await RefreshFavouritesListAsync();
    if (_favouritesList.Count == 0) return;

    _currentFavouriteIndex = _favouritesList.FindIndex(s => s.Id == CurrentStation?.Id);
    _currentFavouriteIndex = _currentFavouriteIndex <= 0 ? _favouritesList.Count - 1 : _currentFavouriteIndex - 1;

    SetStation(_favouritesList[_currentFavouriteIndex]);
}
```

**Acceptance:** Next/Previous buttons cycle through favourites. Wraps around at the end/beginning. If no favourites exist, buttons do nothing.

---

### Step 3.4 — Global Media Key Support

**Action:** Register global hotkeys for media keys using Win32 interop.

**File to create: `Helpers/MediaKeyHook.cs`**

Use `System.Windows.Interop.HwndSource` to register a WndProc hook on the main window. Listen for `WM_APPCOMMAND` messages:
- `APPCOMMAND_MEDIA_PLAY_PAUSE` → `MiniPlayerViewModel.PlayPauseCommand`
- `APPCOMMAND_MEDIA_NEXTTRACK` → `MiniPlayerViewModel.NextStationCommand`
- `APPCOMMAND_MEDIA_PREVIOUSTRACK` → `MiniPlayerViewModel.PreviousStationCommand`
- `APPCOMMAND_MEDIA_STOP` → `MiniPlayerViewModel.StopCommand`

Alternative: Use `RegisterHotKey` Win32 API for `VK_MEDIA_PLAY_PAUSE`, `VK_MEDIA_NEXT_TRACK`, `VK_MEDIA_PREV_TRACK`, `VK_MEDIA_STOP`.

**Register in `MainWindow.xaml.cs`** after the window loads:
```csharp
var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
source.AddHook(_mediaKeyHook.WndProc);
```

**Acceptance:** Pressing media keys on the keyboard controls playback even when the app is not focused. Works when minimized to tray (after M5).

---

### Step 3.5 — ICY Metadata Parsing (Now Playing Artist & Title)

**Action:** Implement a dedicated parser that converts a raw ICY `StreamTitle` string into discrete Artist and Title fields.

**File to create: `Helpers/NowPlayingParser.cs`**

```csharp
public static class NowPlayingParser
{
    // Common separators used by ICY streams: " - ", " – " (en/em dash variants)
    private static readonly string[] Separators = [" - ", " – ", " — "];

    public static (string? Artist, string? Title) Parse(string? rawStreamTitle)
    {
        if (string.IsNullOrWhiteSpace(rawStreamTitle))
            return (null, null);

        var trimmed = rawStreamTitle.Trim();

        foreach (var sep in Separators)
        {
            var idx = trimmed.IndexOf(sep, StringComparison.Ordinal);
            if (idx > 0)
            {
                var artist = trimmed[..idx].Trim();
                var title  = trimmed[(idx + sep.Length)..].Trim();
                if (!string.IsNullOrEmpty(artist) && !string.IsNullOrEmpty(title))
                    return (artist, title);
            }
        }

        // No separator found — treat the whole string as the title only
        return (null, trimmed);
    }
}
```

**Wire-up in `MiniPlayerViewModel`:**

```csharp
// Called on MetadataChanged event (always dispatch to UI thread)
private void OnMetadataChanged(object? sender, string rawNowPlaying)
{
    App.Current.Dispatcher.Invoke(() =>
    {
        var (artist, title) = NowPlayingParser.Parse(rawNowPlaying);
        NowPlayingArtist = artist;
        NowPlayingTitle  = title;
        // NowPlayingDisplay and HasNowPlaying are computed from these two
    });
}

// Called when loading a new station or stopping playback
private void ClearNowPlaying()
{
    NowPlayingArtist = null;
    NowPlayingTitle  = null;
}
```

**`NowPlayingDisplay` computed property** (not `[ObservableProperty]`, manually raises `PropertyChanged` when either field changes):

```csharp
public string? NowPlayingDisplay =>
    (NowPlayingArtist, NowPlayingTitle) switch
    {
        ({ } a, { } t) => $"{a} — {t}",
        (null, { } t)  => t,
        _              => null
    };
```

**Mini-player XAML binding:**
- `TextBlock` for Now Playing bound to `NowPlayingDisplay`, `Visibility` bound to `HasNowPlaying` (via `BooleanToVisibilityConverter`).
- `FontSize 12`, `Opacity 0.7`, `TextTrimming="CharacterEllipsis"`.

**Edge cases to handle:**
- Stations that repeat the same metadata — no-op update (check value equality before setting).
- Stations that send an empty string after stopping a track — clear the display.
- Raw strings with multiple separators (e.g., `"DJ Mix - Daft Punk - Get Lucky"`) — split only on the **first** occurrence so Artist = `"DJ Mix"`, Title = `"Daft Punk - Get Lucky"`.

**Acceptance:** Playing a station with ICY metadata shows the artist and song title separately formatted as `"Artist — Title"` in the mini-player's secondary line. Playing a station without ICY metadata shows nothing on the secondary line (not an empty row). Switching stations immediately clears the previous Now Playing info.

---

## Milestone 4 — Favourites & Persistence

> Goal: Full favourites management with import/export, and session persistence.

---

### Step 4.1 — Favourites Page

**Action:** Implement the Favourites page.

**`ViewModels/FavouritesViewModel.cs`**

Properties:
- `ObservableCollection<Station> Favourites`
- `bool IsEmpty` (computed, true when `Favourites.Count == 0`)

Commands:
- `LoadFavouritesCommand` — async, calls `IStationService.GetFavouritesAsync()`.
- `RemoveFavouriteCommand(Station)` — calls `ToggleFavouriteAsync`, removes from `Favourites` collection.
- `PlayStationCommand(Station)` — delegates to `MiniPlayerViewModel.SetStation()`.
- `ImportFavouritesCommand` — see Step 4.2.
- `ExportFavouritesCommand` — see Step 4.2.

**`Views/FavouritesPage.xaml`**

Layout:
- Page title: "Favourites" (FontSize 28, SemiBold).
- Toolbar area (`Border` with CornerRadius 8):
  - Import button (`ui:Button` with `SymbolIcon ArrowDownload24`, Content "Import").
  - Export button (`ui:Button` with `SymbolIcon ArrowUpload24`, Content "Export").
- `ListBox` with `StationListItem` template (same shared component, but with a remove button instead of/in addition to the heart toggle).
- Empty state: Centered icon + message "No favourites yet. Browse or Discover stations to add some!" — visible when `IsEmpty`.

**Acceptance:** Page shows all favourited stations. Remove button works. Empty state shows when no favourites. List refreshes when navigating back to the page.

---

### Step 4.2 — Import/Export Favourites

**Action:** Implement favourite list import and export in M3U and JSON formats.

**Files to create:**

**`Services/IFavouritesIOService.cs`**
```csharp
public interface IFavouritesIOService
{
    Task ExportAsync(string filePath, string format, List<Station> favourites);
    Task<int> ImportAsync(string filePath, string format); // returns count of stations matched & marked favourite
}
```

**`Services/FavouritesIOService.cs`**

**Export — JSON format:**
```json
{
  "version": 1,
  "exported": "2026-03-05T12:00:00Z",
  "favourites": [
    {
      "name": "Classic Rock Radio",
      "streamUrl": "http://stream.example.com/rock.mp3",
      "logoUrl": "https://example.com/logo.png",
      "group": "Rock"
    }
  ]
}
```

**Export — M3U format:** Standard Extended M3U with `tvg-logo` and `group-title` attributes (matching the existing M3U schema from `DATABASE_SCHEMA.md`).

**Import — JSON:** Deserialize, match each entry by `StreamUrl` against the database, set `IsFavorite = true` on matches.

**Import — M3U:** Reuse `M3UParserService` parsing logic, then match by `StreamUrl`.

**Wire to FavouritesViewModel:**
- `ImportFavouritesCommand` opens `OpenFileDialog` (filter: `*.m3u;*.m3u8;*.json`), calls `IFavouritesIOService.ImportAsync`, shows a `Snackbar` with "X stations added to favourites", refreshes list.
- `ExportFavouritesCommand` opens `SaveFileDialog` (filter: `*.m3u;*.json`), calls `IFavouritesIOService.ExportAsync`, shows success `Snackbar`.

**Register in DI:** `services.AddScoped<IFavouritesIOService, FavouritesIOService>();`

**Acceptance:** Export produces a valid M3U or JSON file. Import reads the file and marks matching stations as favourites. Non-matching URLs are silently skipped. A count toast appears after import.

---

### Step 4.3 — M3U Parser Service

**Action:** Create the M3U parser following the spec in `DATABASE_SCHEMA.md`.

**File to create: `Services/M3UParserService.cs`**

```csharp
public class M3UParserService
{
    public List<ParsedStation> Parse(string filePath)
    {
        // Read file line by line
        // For each #EXTINF line:
        //   Extract tvg-logo="..." → LogoUrl
        //   Extract group-title="..." → GroupName (default "Uncategorized")
        //   Extract station name after last comma → Name
        //   Next non-empty line → StreamUrl
        // Return list of ParsedStation DTOs
    }
}

public class ParsedStation
{
    public string Name { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string GroupName { get; set; } = "Uncategorized";
}
```

Used by both: favourites import (match by URL) and bulk station import in Settings (insert/update).

**Acceptance:** Parser correctly extracts all fields from sample M3U files including edge cases (missing logo, missing group-title, URLs with special characters).

---

### Step 4.4 — Session Persistence

**Action:** Save and restore playback state across app sessions.

**On app shutdown** (in `App.xaml.cs` `OnExit` or `MainWindow.Closing`):
- Save `LastPlayedStationId` → `CurrentStation.Id`
- Save `Volume` → current volume value
- Save `Theme` → current theme selection

**On app startup** (after DI container is built):
- Read `LastPlayedStationId` → load the `Station` from DB → set on `MiniPlayerViewModel` as `CurrentStation` (but do NOT auto-play).
- Read `Volume` → set on `MiniPlayerViewModel`.
- Read `Theme` → apply via `ThemeHelper`.

Use `IStationService.GetSettingAsync` / `SetSettingAsync` for all reads/writes.

**Acceptance:** Close the app while playing a station. Reopen — the mini-player shows the last station's info (name, logo, favourite status) and the volume slider is at the saved position. No audio plays until the user clicks play.

---

## Milestone 5 — System Tray & Polish

> Goal: System tray integration, Settings page, error handling, and release packaging.

---

### Step 5.1 — System Tray Integration

**Action:** Add system tray (notification area) icon with context menu.

**Approach:** Use WPF-UI's `NotifyIcon` if available, or use `System.Windows.Forms.NotifyIcon` via interop (add `<UseWindowsForms>true</UseWindowsForms>` to `.csproj`).

**File to create: `Helpers/TrayIconManager.cs`**

```csharp
public class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly MiniPlayerViewModel _miniPlayer;

    public TrayIconManager(MiniPlayerViewModel miniPlayer, Action showWindowAction, Action quitAction)
    {
        _miniPlayer = miniPlayer;
        _notifyIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "RadioV2",
            Visible = true,
            ContextMenuStrip = BuildContextMenu(showWindowAction, quitAction)
        };
        _notifyIcon.DoubleClick += (s, e) => showWindowAction();
    }

    private ContextMenuStrip BuildContextMenu(Action showWindow, Action quit)
    {
        var menu = new ContextMenuStrip();
        // Station name (disabled label) — update dynamically
        // Play/Pause
        // Next Station
        // Previous Station
        // Separator
        // Show Window
        // Quit
        return menu;
    }

    public void UpdateTooltip(string stationName) => _notifyIcon.Text = $"RadioV2 — {stationName}";
}
```

**Modify `MainWindow.xaml.cs`:**
- Override `OnClosing`: Cancel the close, call `Hide()` instead. Set `ShowInTaskbar = false`.
- "Show Window" action: `Show()`, `WindowState = Normal`, `ShowInTaskbar = true`, `Activate()`.
- "Quit" action: set a flag `_isQuitting = true`, call `Close()`. Only in `OnClosing`, if `_isQuitting`, allow the close to proceed.

**Acceptance:** Clicking the window X hides the window but keeps the tray icon. Right-click tray icon shows the context menu. Play/Pause/Next/Previous in the menu work. Double-click restores. "Quit" fully exits the app.

---

### Step 5.2 — Settings Page

**Action:** Implement the Settings page.

**`ViewModels/SettingsViewModel.cs`**

Properties:
- `string SelectedTheme` (`"Light"` / `"Dark"`) — on change, call `ThemeHelper.ApplyTheme()`, save to DB under `"Theme"`.
- `string AppVersion` — read from assembly info.

Commands:
- `ImportStationsCommand` — opens `OpenFileDialog` for M3U/M3U8, parses with `M3UParserService`, bulk inserts/updates stations in the DB. Shows a `Snackbar` with count of stations added/updated.

**`Views/SettingsPage.xaml`**

Layout:
- Page title: "Settings" (FontSize 28, SemiBold).

**Appearance card** (`ui:Card`):
- "Theme" row: label + two `RadioButton`-style toggle buttons: **Light** and **Dark**. Bound to `SelectedTheme`.

**Import card** (`ui:Card`):
- "Import Stations" header.
- Description: "Add new stations to the database from an M3U/M3U8 playlist file."
- Import button (`ui:Button` Primary).

**About card** (`ui:Card`):
- App name, version, brief credits.

**Acceptance:** Clicking Light/Dark switches the theme immediately (Mica backdrop and all controls update). The selection persists — reopening the app restores it. M3U import adds new stations (or updates existing by StreamUrl). About section shows correct version.

---

### Step 5.3 — Error Handling & Offline States

**Action:** Add graceful error handling throughout the app.

**Playback errors:**
- `RadioPlayerService.PlaybackError` event triggers a `Snackbar` notification in the mini-player area (WPF-UI provides `Snackbar` control). Message: "Could not play [StationName]. The stream may be offline."
- Mini-player returns to stopped state.

**Network offline detection:**
- Use `NetworkInterface.GetIsNetworkAvailable()` or `NetworkChange.NetworkAvailabilityChanged` event.
- When offline: disable play buttons, show a subtle banner "No internet connection — playback unavailable." Favourites and browsing still work (data is local).
- When back online: remove the banner, re-enable play buttons.

**Image loading errors:**
- In `StationListItem`, bind `Image.Source` with a fallback converter. If the logo URL fails to load, show a default radio icon from `Assets/`.

**Database errors:**
- Wrap DB operations in try/catch. Show a `ContentDialog` for critical errors (DB file missing/corrupt). Log via Serilog.

**Acceptance:** Disconnecting Wi-Fi shows an offline banner. Attempting to play a dead stream shows a snackbar error. Missing logos show a fallback icon. No unhandled exceptions.

---

### Step 5.4 — Serilog Logging Setup

**Action:** Configure structured logging to file.

**In `App.xaml.cs`** (during host setup):
```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RadioV2", "logs", "log-.txt"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7)
    .CreateLogger();
```

Log key events:
- App startup/shutdown.
- Playback start/stop/error (include station name and URL).
- Import operations (file name, station count).
- Unhandled exceptions (via `AppDomain.CurrentDomain.UnhandledException` and `DispatcherUnhandledException`).

**Acceptance:** Log files appear in `%LocalAppData%/RadioV2/logs/`. Playback errors and import operations are logged with context.

---

### Step 5.5 — Performance Optimization

**Action:** Profile and optimize for the performance targets.

**Checklist:**
- [ ] Verify all `IQueryable` chains call `AsNoTracking()`.
- [ ] Confirm SQLite index exists on `Stations.Name` — add via EF Core `OnModelCreating` or raw SQL on first run.
- [ ] Enable WAL mode: execute `PRAGMA journal_mode=WAL;` on context creation.
- [ ] Verify `VirtualizingStackPanel.IsVirtualizing="True"` and `VirtualizationMode="Recycling"` on all `ListBox` controls.
- [ ] Set `DecodePixelWidth` on all `Image` controls in list items (40px for thumbnails).
- [ ] Test with slow network: confirm buffering indicator shows and playback doesn't freeze the UI.
- [ ] Test scroll performance with 10,000+ stations — should maintain 60fps.
- [ ] Measure startup time — target under 2 seconds to window visible.
- [ ] Measure memory under typical use — target under 200 MB.

---

### Step 5.6 — Installer Packaging

**Action:** Configure single-file self-contained publish.

**Add to `RadioV2.csproj`:**
```xml
<PropertyGroup>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <SelfContained>true</SelfContained>
  <PublishSingleFile>true</PublishSingleFile>
  <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
  <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
</PropertyGroup>
```

**Note:** LibVLC native DLLs (~120 MB) may not bundle into the single-file exe. They typically go into a `libvlc/` subdirectory alongside the exe. Test the publish output to confirm the folder structure.

**Publish command:**
```bash
dotnet publish -c Release
```

**Output structure:**
```
publish/
├── RadioV2.exe
├── Data/
│   └── radioapp_large_groups.db
└── libvlc/
    └── win-x64/
        └── (native VLC DLLs)
```

**Acceptance:** The published output runs on a clean Windows machine without .NET or VLC installed. The DB file is included and accessible.

---

## Dependency Graph

```
M1.1 Project Scaffold
  └─► M1.2 Folder Structure
       └─► M1.3 Models & DbContext
            └─► M1.4 DI & App Host
                 └─► M1.5 FluentWindow Shell
                      ├─► M1.6 Mini-Player UI Shell
                      ├─► M1.7 Theme Support
                      └─► M1.8 Single Instance
                           │
                 ┌─────────┘
                 ▼
            M2.1 Station Service
                 ├─► M2.2 Station List Item Control
                 ├─► M2.3 Infinite Scroll Behaviour
                 │    │
                 │    ├─► M2.4 Browse Page
                 │    └─► M2.5 Discover Page
                 │
                 └──────────────┐
                                ▼
                           M3.1 RadioPlayerService
                                ├─► M3.2 Wire Mini-Player
                                │    ├─► M3.3 Next/Previous
                                │    └─► M3.5 ICY Metadata Parsing (NowPlayingParser)
                                └─► M3.4 Media Keys
                                     │
                           ┌─────────┘
                           ▼
                      M4.1 Favourites Page
                           ├─► M4.2 Import/Export
                           │    └─► M4.3 M3U Parser
                           └─► M4.4 Session Persistence
                                │
                      ┌─────────┘
                      ▼
                 M5.1 System Tray
                 M5.2 Settings Page
                 M5.3 Error Handling
                 M5.4 Logging
                 M5.5 Performance
                 M5.6 Packaging
```
