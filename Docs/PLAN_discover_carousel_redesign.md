# Plan: Discover Page — Carousel Redesign

## Goal

Redesign the Discover page into a Netflix/Spotify-style vertical list of horizontal carousels.
Each row is a **Category**. Cards inside each row are the **Groups** (genres) belonging to that
category. Clicking a group card navigates to the existing group station list.

---

## Why "Carousel"?

A **carousel** is a UI pattern where a row of items extends beyond the visible width of the
screen. Only 4–5 items are visible at a time. Left and right arrow buttons let the user
advance through the rest — the whole row slides sideways (like a fairground carousel rotating).

This is the pattern Netflix uses for genre rows ("New Releases", "Trending Now") and Spotify
uses for category rows. It is distinct from a scrollable list (which scrolls vertically) or a
grid (which shows everything at once). The key characteristic: **the user never sees a
horizontal scrollbar** — the arrows are the only navigation affordance.

---

## Confirmed Decisions

| Topic | Decision |
|---|---|
| Font | Segoe UI Variable (system default — no custom font files needed) |
| Header effect | Semi-transparent `LayerFillColorDefaultBrush` border (Mica provides ambient blur underneath) |
| Play button colour | `ui:Button Appearance="Primary"` — system accent colour, no hardcoded green |
| Card layout | Fixed-width cards, arrow button scrolls to reveal the next batch |
| DB structure | New normalised `Category` table + `CategoryId` FK on `Group` |

---

## Data Model Changes

### New table: `Categories`

```csharp
// Models/Category.cs
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public ICollection<Group> Groups { get; set; } = new List<Group>();
}
```

### Updated `Group` model

Add a nullable FK so existing groups without a category still work:

```csharp
public int? CategoryId { get; set; }
public Category? Category { get; set; }
```

### `RadioDbContext` additions

```csharp
public DbSet<Category> Categories => Set<Category>();
```

```csharp
modelBuilder.Entity<Group>(e =>
{
    e.HasOne(g => g.Category)
     .WithMany(c => c.Groups)
     .HasForeignKey(g => g.CategoryId)
     .IsRequired(false);
});
```

### DB migration strategy

> The pre-seeded DB must never be wiped. EF migrations cannot be used against it normally.

Use a **raw SQL startup guard** in the existing `EnsureDatabaseMigratedAsync` (or equivalent
startup service):

```csharp
// Run once at startup — safe to call repeatedly (IF NOT EXISTS guards)
await db.Database.ExecuteSqlRawAsync(@"
    CREATE TABLE IF NOT EXISTS Categories (
        Id   INTEGER PRIMARY KEY AUTOINCREMENT,
        Name TEXT    NOT NULL
    );
");
await db.Database.ExecuteSqlRawAsync(@"
    ALTER TABLE Groups ADD COLUMN CategoryId INTEGER
        REFERENCES Categories(Id);
"); // Wrap in try/catch — SQLite throws if column already exists
```

This keeps the pre-seeded data intact and adds the new schema non-destructively.

---

## High-Level Layout

```
┌─────────────────────────────────────────────────────────┐
│  [Sticky semi-transparent header: "Discover"]           │
├─────────────────────────────────────────────────────────┤
│  Jazz Essentials                           [<]  [>]     │  ← Category row
│  ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐         │
│  │ img  │ │ img  │ │ img  │ │ img  │ │ img  │         │  ← Group cards
│  │      │ │      │ │      │ │      │ │      │         │
│  │ Name │ │ Name │ │ Name │ │ Name │ │ Name │         │
│  └──────┘ └──────┘ └──────┘ └──────┘ └──────┘         │
├─────────────────────────────────────────────────────────┤
│  Top Hits                                  [<]  [>]     │
│  ┌──────┐  ...                                          │
└─────────────────────────────────────────────────────────┘
```

The outer page scrolls **vertically** (one `ScrollViewer` wrapping all rows).
Each row scrolls **horizontally** via left/right arrow buttons — no visible scrollbar.

---

## Typography

All using Segoe UI Variable (WPF default — no changes needed):

| Element | Size | Weight | Brush |
|---|---|---|---|
| "Discover" sticky header | 28 | SemiBold | `TextFillColorPrimaryBrush` |
| Category row title | 20 | SemiBold | `TextFillColorPrimaryBrush` |
| Group card name | 13 | SemiBold | `TextFillColorPrimaryBrush` |
| Group card sub-text (e.g. station count) | 11 | Regular | `TextFillColorSecondaryBrush` |

---

## Sticky Header

Sits **above** the outer `ScrollViewer` in its own `Grid` row so it never scrolls away.

```xml
<Border Background="{ui:ThemeResource LayerFillColorDefaultBrush}"
        Opacity="0.92"
        BorderBrush="{ui:ThemeResource ControlStrokeColorDefaultBrush}"
        BorderThickness="0,0,0,1"
        Padding="24,14">
    <TextBlock Text="Discover"
               FontSize="28"
               FontWeight="SemiBold"
               Foreground="{ui:ThemeResource TextFillColorPrimaryBrush}" />
</Border>
```

---

## Group Card Component (`Controls/GroupCarouselCard.xaml`)

A new `UserControl`. Represents one group (genre) inside a carousel row.

### Visual structure

```
┌───────────────────────┐
│  ┌─────────────────┐  │  ← ui:Card, Width=160, CornerRadius=8, Padding=0
│  │                 │  │
│  │   [Group Logo / │  │  ← Image 160×160, CornerRadius top=8
│  │    placeholder] │  │    Clipped so zoom stays inside rounded corners
│  │                 │  │
│  │         [▶ Play]│  │  ← Accent circle button, bottom-right of image
│  └─────────────────┘  │    Hidden by default; shown on card hover
│  Group Name           │  ← FontSize=13, SemiBold, CharacterEllipsis
│  1,240 stations       │  ← FontSize=11, SecondaryBrush
└───────────────────────┘
```

### Hover state (via `ControlTemplate` triggers)
- Card background → `ControlFillColorSecondaryBrush`
- Play button → `Visibility=Visible`
- Image → `ScaleTransform` animates 1.0 → 1.06 (200 ms ease-out)
  The image is wrapped in a `Border` with matching `CornerRadius` + `ClipToBounds="True"`
  so the zoom doesn't overflow the rounded corners.

### Play button
```xml
<ui:Button Appearance="Primary"
           Width="36" Height="36"
           CornerRadius="18"
           Visibility="Collapsed"
           Command="{Binding DataContext.PlayGroupCommand,
                             RelativeSource={RelativeSource AncestorType=Page}}"
           CommandParameter="{Binding}">
    <ui:SymbolIcon Symbol="Play24" />
</ui:Button>
```

### Image fallback
If `Group` has no logo URL (Groups don't currently have logos — see note below):
show a `Border` filled with `ControlFillColorSecondaryBrush` containing a centered
`ui:SymbolIcon Symbol="MusicNote24"`.

> **Note:** The current `Group` model has no `LogoUrl`. Two options:
> - **Option A:** Add `LogoUrl` to `Group` (and the DB column via the startup guard).
> - **Option B:** Display a coloured placeholder tile per group using a deterministic
>   colour derived from the group name (still using ThemeResource brushes — rotate through
>   a small set of semantic accent brushes).
>
> **Recommendation: Option A** (cleaner, matches the spec's image requirement). Needs one
> more `ALTER TABLE Groups ADD COLUMN LogoUrl TEXT;` in the startup guard.

---

## Carousel Row Component (`Controls/DiscoverCarouselRow.xaml`)

A `UserControl` that renders one full category row.

### XAML sketch

```xml
<Grid Margin="0,0,0,28">
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />   <!-- category title + arrow buttons -->
        <RowDefinition Height="Auto" />   <!-- card strip -->
    </Grid.RowDefinitions>

    <!-- Row header -->
    <Grid Grid.Row="0" Margin="0,0,0,10">
        <TextBlock Text="{Binding CategoryName}"
                   FontSize="20" FontWeight="SemiBold"
                   Foreground="{ui:ThemeResource TextFillColorPrimaryBrush}"
                   VerticalAlignment="Center" />
        <StackPanel HorizontalAlignment="Right" Orientation="Horizontal">
            <ui:Button Appearance="Transparent" x:Name="LeftArrowButton"
                       Click="LeftArrow_Click" Visibility="Collapsed">
                <ui:SymbolIcon Symbol="ChevronLeft24" />
            </ui:Button>
            <ui:Button Appearance="Transparent" x:Name="RightArrowButton"
                       Click="RightArrow_Click">
                <ui:SymbolIcon Symbol="ChevronRight24" />
            </ui:Button>
        </StackPanel>
    </Grid>

    <!-- Horizontally scrollable strip (no visible scrollbar) -->
    <ScrollViewer x:Name="CarouselScroll"
                  Grid.Row="1"
                  HorizontalScrollBarVisibility="Hidden"
                  VerticalScrollBarVisibility="Disabled"
                  ScrollChanged="CarouselScroll_ScrollChanged">
        <ItemsControl ItemsSource="{Binding Groups}">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                    <StackPanel Orientation="Horizontal" />
                </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <controls:GroupCarouselCard Margin="0,0,12,0" />
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </ScrollViewer>
</Grid>
```

### Arrow scroll logic (code-behind)

```csharp
private void RightArrow_Click(object sender, RoutedEventArgs e)
{
    double target = Math.Min(
        CarouselScroll.HorizontalOffset + CarouselScroll.ViewportWidth,
        CarouselScroll.ScrollableWidth);
    AnimateScrollTo(target);
}

private void LeftArrow_Click(object sender, RoutedEventArgs e)
{
    double target = Math.Max(
        CarouselScroll.HorizontalOffset - CarouselScroll.ViewportWidth, 0);
    AnimateScrollTo(target);
}

private void CarouselScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
{
    LeftArrowButton.Visibility  = CarouselScroll.HorizontalOffset > 0
        ? Visibility.Visible : Visibility.Collapsed;
    RightArrowButton.Visibility = CarouselScroll.HorizontalOffset < CarouselScroll.ScrollableWidth
        ? Visibility.Visible : Visibility.Collapsed;
}
```

- **AnimateScrollTo**: Uses a `DispatcherTimer` (16 ms tick, ~60 fps) or `BeginAnimation` on
  a helper attached property to interpolate `HorizontalOffset` over ~300 ms.
  (`ScrollViewer.HorizontalOffset` is not directly animatable in WPF — helper needed.)

---

## ViewModel Layer

### New: `CarouselRowViewModel.cs`

```csharp
public partial class CarouselRowViewModel : ObservableObject
{
    public int CategoryId   { get; init; }
    public string CategoryName { get; init; } = "";
    public ObservableCollection<Group> Groups { get; } = new();
}
```

### Changes to `DiscoverViewModel.cs`

```csharp
// New collection — replaces the flat Groups collection on the Discover top-level view
[ObservableProperty]
private ObservableCollection<CarouselRowViewModel> _categoryRows = new();

// Load categories + their groups (lazy: first 10 categories on load, more on scroll-down)
public async Task LoadCategoriesAsync() { ... }
```

The existing `Groups` collection, `IsGroupView`, `SelectedGroup`, `GroupStations`,
`BackToGroupsCommand`, and station-list infinite scroll all stay **unchanged** — the drill-down
view is reused as-is.

---

## Service Layer

### `IStationService` — new method

```csharp
// Returns categories with their groups pre-loaded, paged
Task<List<Category>> GetCategoriesWithGroupsAsync(int skip, int take);
```

```csharp
// Implementation
return await _db.Categories
    .AsNoTracking()
    .Include(c => c.Groups)
    .OrderBy(c => c.Name)
    .Skip(skip).Take(take)
    .ToListAsync();
```

---

## Files to Create

| File | Purpose |
|---|---|
| `RadioV2.Core/Models/Category.cs` | New EF entity |
| `Controls/GroupCarouselCard.xaml/.cs` | Square group card with hover + zoom + play |
| `Controls/DiscoverCarouselRow.xaml/.cs` | One horizontal carousel row |
| `Helpers/ScrollAnimationHelper.cs` | Smooth ScrollViewer offset animation |
| `ViewModels/CarouselRowViewModel.cs` | Per-row ViewModel |

## Files to Modify

| File | Change |
|---|---|
| `RadioV2.Core/Models/Group.cs` | Add `CategoryId?`, `Category?`, `LogoUrl?` |
| `RadioV2.Core/Data/RadioDbContext.cs` | Add `DbSet<Category>`, configure FK, startup SQL guard |
| `Services/IStationService.cs` + impl | Add `GetCategoriesWithGroupsAsync` |
| `ViewModels/DiscoverViewModel.cs` | Add `CategoryRows`, `LoadCategoriesAsync`, lazy load |
| `Views/DiscoverPage.xaml` | Replace wrap-panel grid with sticky header + category row list |

---

## Category & Group Seed Data

This is the authoritative list of categories and which groups belong to each one.
Group keys are the **sanitized names** (lowercase + underscores) — they match both the
image filenames in `Assets/Groups/` and the sanitized form of the group's `Name` in the DB.

### 1. Countries & Regions
**Europe:**
austria, belgium, croatia, czech_republic, europe, france, germany, greece, hungary,
ireland, italy, netherlands, norway, poland, portugal, romania, russia, serbia, slovakia,
spain, sweden, switzerland, turkey, uk, ukraine

**The Americas:**
argentina, brazil, canada, chile, colombia, ecuador, guatemala, mexico, north_america,
paraguay, peru, puerto_rico, south_america, uruguay, usa, venezuela

**Asia, Oceania & Africa:**
australia, india, indonesia, israel, japan, new_zealand, philippines, saudi_arabia,
singapore, south_africa, thailand, uae, uganda

### 2. Rock & Metal
alternative, alternative_rock, classic, classic_rock, gothic, hard_rock, heavy_metal,
metal, progressive_rock, punk, rock, rock_n_roll, soft_rock

### 3. Electronic & Dance
ambient, club, dance, deep_house, drum_and_bass, electronic, house, techno, trance,
trap, underground

### 4. Pop, Charts & Decades
**Decades:** 00s, 10s, 50s, 60s, 70s, 80s, 90s, oldies

**Pop Styles:** adult_contemporary, charts, hits, pop, pop_rock, top40

### 5. Urban & Latin
**Urban:** disco, disco_fox, funk, hiphop, rap, rnb, soul, urban

**Latin/Tropical:** espanol, latin, reggaeton, salsa, sertaneja, tropical

### 6. Jazz, Chill & Instrumental
ballads, blues, chillout, classical, easy_listening, instrumental, jazz, jazz_funk,
smooth_jazz, swing, trip_hop, vocal

### 7. News, Talk & Sports
comedy, entertainment, news, news_talk, sports, talk

### 8. Specialty & Mood
christmas, eclectic, holidays, kids, party, religious, romantic, soundtrack

### 9. Global & Cultural
english, folk, schlager, traditional, world

---

## Seeding Strategy

### What seeding means

"Seeding" means inserting the initial, known data into the database the first time the app
runs. In our case:
1. Creating the 9 `Category` rows in the new `Categories` table.
2. Updating each `Group` row to set its `CategoryId` so it belongs to the right category.

This only needs to happen **once** — on first launch after the schema change. On every
subsequent launch the seeder checks whether categories already exist and exits immediately
without doing anything. So calling it on every startup is safe and cheap.

### Why a seeder and not a SQL script

We could write a `.sql` file and run it manually, but then it's disconnected from the code
and easy to forget. Keeping the seed data in `CategorySeeder.cs` means:
- It runs automatically for every developer and for the shipped app.
- It's version-controlled alongside the code that uses it.
- If categories are ever reset (e.g. dev wipes the DB), re-running the app re-seeds
  everything correctly.

### How matching works

The seed data uses sanitized group name keys (e.g. `classic_rock`, `hip_hop`). The actual
group names stored in the DB may use different casing or spacing (e.g. "Classic Rock",
"HipHop", "Hip-Hop"). The seeder bridges this gap by running the same `Sanitize()` function
on every group name fetched from the DB, then building a lookup dictionary keyed by the
sanitized form. The seed keys are matched against that dictionary — so the seeder never
hardcodes DB IDs or relies on exact capitalisation.

**Example:**
```
DB stores:  "Classic Rock"
Sanitize → "classic_rock"
Seed key:   "classic_rock"  ✓ match → assign CategoryId
```

Any group whose sanitized name does not appear in the seed data is simply left with
`CategoryId = null` and will not appear on the Discover carousel. It remains accessible
via the old flat group view if that is kept, or can be assigned to a category later.

### `Services/CategorySeeder.cs` sketch

```csharp
public static class CategorySeeder
{
    // Ordered list so carousel rows appear in this exact sequence
    private static readonly (string CategoryName, string[] GroupKeys)[] SeedData =
    [
        ("Countries & Regions", new[] {
            "austria","belgium","croatia","czech_republic","europe","france","germany",
            "greece","hungary","ireland","italy","netherlands","norway","poland",
            "portugal","romania","russia","serbia","slovakia","spain","sweden",
            "switzerland","turkey","uk","ukraine",
            "argentina","brazil","canada","chile","colombia","ecuador","guatemala",
            "mexico","north_america","paraguay","peru","puerto_rico","south_america",
            "uruguay","usa","venezuela",
            "australia","india","indonesia","israel","japan","new_zealand","philippines",
            "saudi_arabia","singapore","south_africa","thailand","uae","uganda"
        }),
        ("Rock & Metal", new[] {
            "alternative","alternative_rock","classic","classic_rock","gothic",
            "hard_rock","heavy_metal","metal","progressive_rock","punk","rock",
            "rock_n_roll","soft_rock"
        }),
        ("Electronic & Dance", new[] {
            "ambient","club","dance","deep_house","drum_and_bass","electronic",
            "house","techno","trance","trap","underground"
        }),
        ("Pop, Charts & Decades", new[] {
            "00s","10s","50s","60s","70s","80s","90s","oldies",
            "adult_contemporary","charts","hits","pop","pop_rock","top40"
        }),
        ("Urban & Latin", new[] {
            "disco","disco_fox","funk","hiphop","rap","rnb","soul","urban",
            "espanol","latin","reggaeton","salsa","sertaneja","tropical"
        }),
        ("Jazz, Chill & Instrumental", new[] {
            "ballads","blues","chillout","classical","easy_listening","instrumental",
            "jazz","jazz_funk","smooth_jazz","swing","trip_hop","vocal"
        }),
        ("News, Talk & Sports", new[] {
            "comedy","entertainment","news","news_talk","sports","talk"
        }),
        ("Specialty & Mood", new[] {
            "christmas","eclectic","holidays","kids","party","religious","romantic","soundtrack"
        }),
        ("Global & Cultural", new[] {
            "english","folk","schlager","traditional","world"
        }),
    ];

    public static async Task SeedAsync(RadioDbContext db)
    {
        // Skip if categories already exist
        if (await db.Categories.AnyAsync()) return;

        // Load all groups once
        var allGroups = await db.Groups.ToListAsync();
        var groupsByKey = allGroups.ToDictionary(
            g => GroupImageHelper.Sanitize(g.Name),
            g => g);

        int displayOrder = 0;
        foreach (var (categoryName, keys) in SeedData)
        {
            var category = new Category { Name = categoryName, DisplayOrder = displayOrder++ };
            db.Categories.Add(category);
            await db.SaveChangesAsync(); // get the generated Id

            foreach (var key in keys)
            {
                if (groupsByKey.TryGetValue(key, out var group))
                {
                    group.CategoryId = category.Id;
                }
            }
        }

        await db.SaveChangesAsync();
    }
}
```

### `Category` model — add `DisplayOrder`

```csharp
public class Category
{
    public int Id           { get; set; }
    public string Name      { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }   // controls row order on Discover page

    public ICollection<Group> Groups { get; set; } = new List<Group>();
}
```

The `DisplayOrder` column ensures carousel rows always appear in the sequence defined above,
regardless of DB insertion order. Add it to the startup SQL guard:

```sql
CREATE TABLE IF NOT EXISTS Categories (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    Name         TEXT    NOT NULL,
    DisplayOrder INTEGER NOT NULL DEFAULT 0
);
```

---

## Pre-Implementation: Verify DB Group Names

> **Do this before writing any code.**

The seed keys are based on expected group names. Before implementing the seeder, run a
quick query against the actual DB to see what group names are stored and how they sanitize:

```bash
sqlite3 "Data/radioapp_large_groups.db" \
  "SELECT Name FROM Groups ORDER BY Name;" | head -80
```

Or via the DevTool's Groups tab if it lists them. Go through the output and check:
- Does "Hip Hop" exist or is it "HipHop"? (sanitizes to `hip_hop` vs `hiphop`)
- Does "R&B" exist or "RnB"? (`r_b` vs `rnb`)
- Does "Drum and Bass" exist or "Drum & Bass"? (`drum_and_bass` vs `drum_bass`)

Any mismatches must be corrected in the seed keys in `CategorySeeder.cs` before the seeder
runs. The seed keys in this document are the **intended** keys — treat the actual DB names
as the source of truth and adjust accordingly.

---

## Implementation Order

1. **Verify DB group names** (see above) — adjust seed keys if needed
2. `Category.cs` model + `Group.cs` updates (add `CategoryId?`, `DisplayOrder`)
3. `RadioDbContext` — register `DbSet<Category>`, configure FK, startup SQL guard
4. `Helpers/GroupImageHelper.cs` — name sanitiser + pack URI resolver (needed by seeder)
5. `Services/CategorySeeder.cs` — seed the 9 categories and assign groups
6. Wire seeder call into app startup (after SQL guard)
7. `IStationService` — add `GetCategoriesWithGroupsAsync`
8. `CarouselRowViewModel.cs`
9. `Helpers/ScrollAnimationHelper.cs`
10. `Controls/GroupCarouselCard.xaml/.cs` — card UI + hover/zoom + image logic
11. `Controls/DiscoverCarouselRow.xaml/.cs` — row UI + arrow logic
12. `ViewModels/DiscoverViewModel.cs` — wire up `CategoryRows`
13. `Views/DiscoverPage.xaml` — new layout with sticky header + row list
