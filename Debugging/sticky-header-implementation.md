# Sticky Header Implementation — Debug Log

## Goal
When scrolling down in any page, the page title (and search bar / back arrow if present)
should stay pinned at the top. Affects: Browse, Discover, Favourites, Settings.

---

## Root Cause

WPF-UI's `NavigationViewContentPresenter` (the Frame-like control that hosts pages) has an
`IsDynamicScrollViewerEnabled` property that defaults to `true`. When true, its ControlTemplate
wraps all page content in a `DynamicScrollViewer`:

```
NavigationView
└── NavigationViewContentPresenter
    └── DynamicScrollViewer   ← outer scroll — THIS was scrolling everything
        └── ContentPresenter (PART_FrameCP)
            └── Page content (titles, lists, etc.)
```

Because the outer `DynamicScrollViewer` was the scroll container, ALL page content (including
titles) scrolled together. The page-internal Grid row structure (`Height="Auto"` for titles,
`Height="*"` for ListBox) was irrelevant — the ListBox measured with infinite height, so
`Height="*"` expanded to content size and no internal scrolling occurred.

A secondary effect: all existing "infinite scroll" code was written to target this outer
`DynamicScrollViewer` (finding it via `FindParentScrollViewer`), not the pages' own containers.

---

## Attempt 1 — Wrong (previous session)

Changed `SettingsPage.xaml` to move the title TextBlock outside its `ScrollViewer`. Marked as
FIXED in debugging.md but the outer `DynamicScrollViewer` was still wrapping everything, so
the title still scrolled. Browse / Discover / Favourites were never actually fixed.

**Why it seemed fixed**: The worktree test may have only checked SettingsPage visually and not
noticed the outer scroll was still active on other pages.

---

## Attempt 2 — Wrong (`IsDynamicScrollViewerEnabled` style setter)

Added to `App.xaml`:
```xml
<Style TargetType="{x:Type ui:NavigationViewContentPresenter}">
    <Setter Property="IsDynamicScrollViewerEnabled" Value="False" />
</Style>
```
**Build error MC3080**: XAML compiler enforces the `protected set` on the CLR property even
through a style setter. Cannot set this property from outside the class in XAML.

---

## Attempt 3 — Wrong (ContentPresenter-only template)

Overrode the ControlTemplate to use only a `ContentPresenter`, removing the
`DynamicScrollViewer` entirely:
```xml
<ControlTemplate TargetType="{x:Type ui:NavigationViewContentPresenter}">
    <ContentPresenter x:Name="PART_FrameCP" Margin="{TemplateBinding Padding}" />
</ControlTemplate>
```
**Result**: Pages built and displayed. Sticky headers appeared to work — but only because
nothing scrolled at all. A plain `ContentPresenter` passes an **unconstrained (infinite)
height** to pages, so the page Grid's `Height="*"` rows expanded to fit all content and no
internal scrolling occurred. The outer scroll was gone, so headers appeared "sticky" but lists
were not actually scrollable.

**Discover mouse wheel broken**: DiscoverPage's existing `PreviewMouseWheel` handler forwarded
wheel events to the outer NavigationView ScrollViewer (now gone) and set `e.Handled = true`,
preventing GroupsScrollViewer from scrolling.

**Infinite scroll at top bug**: `DiscoverPage.xaml.cs` had `groupsOuterSv.ScrollChanged`
hooked — the outer SV (or disabled SV) always reported `VerticalOffset=0 >= ScrollableHeight-300`
so it triggered immediately on load.

---

## Attempt 4 — Wrong (`DynamicScrollViewer` with `VerticalScrollBarVisibility="Disabled"`)

Changed the template to keep `DynamicScrollViewer` but disable vertical scrolling:
```xml
<ControlTemplate TargetType="{x:Type ui:NavigationViewContentPresenter}">
    <ui:DynamicScrollViewer VerticalScrollBarVisibility="Disabled" ...>
        <ContentPresenter x:Name="PART_FrameCP" Margin="{TemplateBinding Padding}" />
    </ui:DynamicScrollViewer>
</ControlTemplate>
```
**Expected**: `DynamicScrollViewer` with `Disabled` would constrain page height and pass
mouse wheel events through to inner ScrollViewers/ListBoxes.

**Still broken**: WPF-UI's `DynamicScrollViewer` apparently overrides `OnPreviewMouseWheel`
and marks the event handled even when `VerticalScrollBarVisibility=Disabled`. The code-behind
was also still forwarding to the outer (now disabled) SV. Sticky headers still appeared to
work for the wrong reason (pages still got unconstrained height from the disabled SV or some
other measurement path).

**Infinite scroll at top**: Same bug — code-behind found the disabled outer SV via
`FindParentScrollViewer`, which always reported `VerticalOffset=0` satisfying the load
condition immediately.

---

## Final Fix (Current)

### 1. `App.xaml` — template override with standard WPF `ScrollViewer` (Disabled)

Keep the `DynamicScrollViewer` → standard `ScrollViewer` with `VerticalScrollBarVisibility="Disabled"`:

```xml
<Style TargetType="{x:Type ui:NavigationViewContentPresenter}">
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="{x:Type ui:NavigationViewContentPresenter}">
                <ui:DynamicScrollViewer
                    VerticalScrollBarVisibility="Disabled"
                    HorizontalScrollBarVisibility="Disabled"
                    HorizontalAlignment="Stretch"
                    VerticalAlignment="Stretch"
                    Focusable="False">
                    <ContentPresenter x:Name="PART_FrameCP"
                                      Margin="{TemplateBinding Padding}" />
                </ui:DynamicScrollViewer>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

`VerticalScrollBarVisibility="Disabled"` on a WPF ScrollViewer makes it measure content with
the **constrained available height** (not infinity). Pages now get a real finite height from
the NavigationView layout, so `Height="*"` Grid rows work correctly and ListBoxes/ScrollViewers
scroll internally.

### 2. `Views/DiscoverPage.xaml.cs` — complete rewrite

The old code-behind used `FindParentScrollViewer` to find the outer NavigationView SV and:
- Forwarded mouse wheel to it
- Used its `ScrollChanged` for infinite scroll (genres and stations)

The new code-behind uses internal containers directly:
- **Mouse wheel**: `GroupsScrollViewer.PreviewMouseWheel` scrolls `GroupsScrollViewer` directly
- **Genre infinite scroll (auto-fill)**: `GroupsScrollViewer.ScrollChanged` fires when content
  grows; triggers more loading while `ScrollableHeight < 400`
- **Genre infinite scroll (user scroll)**: handled by the existing `ScrollViewerInfiniteScrollBehavior`
  attached in XAML (after condition fix below)
- **Station infinite scroll**: finds the ListBox's **internal** ScrollViewer via
  `FindChildScrollViewer` (walks visual tree downward instead of upward); hooks `ScrollChanged`
- **Station scroll-to-top**: `_stationsSv.ScrollToTop()` on `IsGroupView → true` transition

### 3. `Helpers/InfiniteScrollBehavior.cs` — fix trigger conditions

**`InfiniteScrollBehavior` (for ListBox, used on Browse):**
Old condition: `ScrollableHeight > 0 && VerticalOffset >= ScrollableHeight - 200`
— This fires at the top when `ScrollableHeight` is small (e.g. 50px): `0 >= 50-200 = -150` ✓

New logic:
```csharp
// Auto-fill: content doesn't fill viewport yet
if (ScrollableHeight == 0) { LoadMore(); return; }

// User-triggered: must have actually scrolled before firing
if (VerticalOffset > 0 && VerticalOffset >= ScrollableHeight - 200)
    LoadMore();
```

**`ScrollViewerInfiniteScrollBehavior` (for GroupsScrollViewer on Discover):**
Added `VerticalOffset > 0` guard to prevent firing at the top.

---

## Page-by-page status after fix

| Page      | Title sticky | Search/Back sticky | Scroll method          |
|-----------|-------------|-------------------|------------------------|
| Browse    | ✓           | ✓ (search bar)    | ListBox internal SV    |
| Discover  | ✓           | ✓ (back + title)  | GroupsScrollViewer / ListBox internal SV |
| Favourites| ✓           | n/a               | ListBox internal SV    |
| Settings  | ✓           | n/a               | ScrollViewer in `*` row|

---

## Files changed

- `App.xaml` — NavigationViewContentPresenter template override
- `Views/DiscoverPage.xaml.cs` — complete rewrite (no longer uses outer SV)
- `Helpers/InfiniteScrollBehavior.cs` — fixed scroll trigger conditions
- `Views/SettingsPage.xaml` — title moved outside ScrollViewer (previous session)
- `Debugging/sticky-header-implementation.md` — this file
