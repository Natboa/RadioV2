# Minimize / Tray Button Fix

## Symptoms
- Native minimize button (`_`) was minimizing to taskbar instead of hiding to tray.
- The custom "minimize to taskbar" button (overlaid left of native buttons) was not clickable.

## Root Causes

### 1. `MinimizeActionOverride` not intercepting the native button
`MinimizeActionOverride` in WPF-UI v4 is typed as `System.Action` (no parameters), but the code
was assigning `(_, _) => { ... }` (two-parameter lambda). This type mismatch means the override
was never actually set, so the native button continued its default minimize-to-taskbar behavior.

### 2. Floating overlay button not clickable
The custom button was placed in `Grid.Row="0"` with `Panel.ZIndex="2"` and
`WindowChrome.IsHitTestVisibleInChrome="True"`, overlapping the TitleBar control.
Even with those settings, WPF-UI's TitleBar has an internal transparent drag surface that
absorbs pointer events in the caption area before they reach sibling elements in the same Grid
row, making the custom button unresponsive.

## Fix (v1 — wrong behavior, see v2 below)
Button moved into `TitleBar.TrailingContent` so hits work. `StateChanged` + `_minimizingToTaskbar`
flag used to route: native `_` → tray, custom → taskbar. This compiled correctly but the user
observed swapped behavior — likely because the `StateChanged` interceptor fires for BOTH paths
and the flag-based routing was unreliable in practice.

## Fix (v2.1 — startup crash fix)
`TabDesktopArrowLeft24` is not a valid `SymbolRegular` enum member in WPF-UI v4.2.0. XAML parse
exception in `InitializeComponent()` prevented the window from ever showing. Reverted icon to
`ArrowMinimize24` (confirmed valid). Tooltip "Minimize to tray" distinguishes it from the native
button visually.

## Fix (v2 — behavior swap)
### Button behavior swapped
- Custom button (TrailingContent) → calls `Hide() + ShowInTaskbar = false` directly (hide to tray).
- Native `_` button → no interception, default WPF minimize behavior (minimizes to taskbar).
No `StateChanged` interception needed. Logic is now direct and unambiguous.

### Alignment
Added `Padding="0"` and `VerticalAlignment="Stretch"` to the TrailingContent button so it sits
flush with the native window control buttons.

## Files Changed
- `MainWindow.xaml` — button in `TitleBar.TrailingContent`, handler renamed to `OnHideToTrayClick`, alignment fixed
- `MainWindow.xaml.cs` — removed `StateChanged` handler and `_minimizingToTaskbar` flag; `OnHideToTrayClick` calls `Hide()` + `ShowInTaskbar = false`
