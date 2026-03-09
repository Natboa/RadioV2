# Favourite Heart Animation & Scrollbar Fix

## Changes Made

### 1. Heart pop animation on click — `Controls/StationListItem.xaml`

Added a scale "pop" animation to the heart `SymbolIcon` triggered on `Button.Click`.

- Added `x:Name="HeartIcon"`, `RenderTransformOrigin="0.5,0.5"`, and `<ScaleTransform />` to the icon.
- Added `<EventTrigger RoutedEvent="Button.Click">` with a `Storyboard` that animates `ScaleX`/`ScaleY`:
  - 0ms → 100ms: scale 1 → 1.4 (`QuadraticEase EaseOut`)
  - 100ms → 220ms: scale 1.4 → 1 (`QuadraticEase EaseIn`)
- No code-behind required — pure XAML.

### 2. No button chrome when favourited and not hovering — `Controls/StationListItem.xaml`

**Problem:** When a station is favourited and the mouse is not over it, the heart `ui:Button` showed a visible square/border around the icon.

**Fix:** Overlaid two elements in the heart `Grid`:
- A plain `ui:SymbolIcon` (no button chrome) — visible only when `IsFavorite=True` AND `IsMouseOver=False` on the UserControl.
- The `ui:Button` (with animation) — visible only when `IsMouseOver=True`.
- Both use `Visibility="Hidden"` (not `Collapsed`) so the Grid column width stays stable and never shifts layout.

### 3. Buttons fixed position, heart left of play — `Controls/StationListItem.xaml`

**Problem:** The heart button appearing/disappearing shifted the play button position. Also the buttons were in wrong order (play left, heart right).

**Fix:**
- Replaced the actions `StackPanel` with a two-column `Grid` (`Auto`/`Auto`).
- Heart in column 0 (left), Play in column 1 (right).
- Both inner heart children use `Hidden` instead of `Collapsed` — the column width is always determined by the larger element (the button), so it never resizes.

### 4. Scrollbar overlap with station items — `Views/BrowsePage.xaml`, `DiscoverPage.xaml`, `FavouritesPage.xaml`

**Problem:** WPF-UI uses an overlay-style scrollbar (floats on top of content). Station items (including their hover/selection background) extended into the scrollbar area.

**Attempted (did not fully work):** `Padding="0,0,16,0"` on the `ListBox` — WPF-UI's custom `ListBox` template does not reliably use `Padding` to inset the ScrollViewer content area.

**Fix:** `Margin="0,0,16,0"` on each `ListBoxItem` via `ItemContainerStyle`. The item itself (background, content, buttons) is physically 16px narrower on the right, which is where the overlay scrollbar sits.

## Architecture Notes

- `NavigationViewContentPresenter` outer `DynamicScrollViewer` has vertical scrolling **disabled** (set in `App.xaml`) — each page manages its own scroll via internal `ListBox` scrollbars.
- WPF-UI scrollbars are overlay-style — they float on top of content, so content must be manually inset.
