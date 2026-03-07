# Search History Bug — Debug Log

## Feature Goal
Browse page: when the user clicks the search TextBox, show a dropdown of up to 7 previous searches.
A search is added to history only when the user presses Enter.
Clicking a history item writes it in the search box and triggers the search.

---

## Attempt 1 — Standalone Popup (REVERTED — full git checkout -- .)

**What was done:**
- Wrapped TextBox in a Grid with a standalone WPF `Popup` control (AllowsTransparency="True", StaysOpen="True")
- Popup contained a themed ListBox of history items
- BrowseViewModel: added HistoryItems, IsHistoryOpen, SelectHistoryItemCommand, JSON persistence

**Crash symptom:**
- Browse screen became completely unresponsive to mouse clicks
- Title bar buttons (minimize, close) turned black and stopped working
- Entire non-client area of the FluentWindow was broken

**Root cause identified:**
WPF `Popup` with `AllowsTransparency="True"` creates a separate layered transparent HWND.
This HWND sits on top of the WPF-UI FluentWindow's non-client area (title bar) and blocks
all hit-testing on the window chrome. WPF-UI's FluentWindow uses custom non-client rendering
that is incompatible with layered child HWNDs floating over it.

**Action:** Full `git checkout -- .` — all changes discarded, back to commit dc1b568.

---

## Attempt 2 — Zero-Height Grid Row Overlay (REVERTED — full git checkout -- .)

**What was done:**
- Replaced Popup with a `<RowDefinition Height="0"/>` trick
- A Border with `VerticalAlignment="Top"` and `Panel.ZIndex="10"` overflowed from the zero-height row
- Intended to act as an in-place overlay without a separate HWND

**Crash symptom:**
- Same as Attempt 1: browse screen unresponsive, title bar buttons black and non-functional

**Root cause (suspected):**
The Z-index overlay likely still interfered with hit-testing on the FluentWindow non-client area,
possibly because the Border's hit-test area extended beyond the Grid boundary into the chrome region,
or the Panel.ZIndex=10 element intercepted mouse events destined for the title bar.

**Action:** Full `git checkout -- .` — all changes discarded, back to commit dc1b568.

---

## Attempt 3 — In-Layout Border + ItemsControl + ui:Button (KEPT — app works, clicking broken)

**What was done:**
- Added a proper `Grid.Row="2"` (Height="Auto") to hold the history section
- History is a `Border` containing an `ItemsControl`
- Each item is a `ui:Button` with `Appearance="Transparent"`, bound via `SelectHistoryItemCommand`
- BrowseViewModel: HistoryItems, IsHistoryVisible, ShowHistory(), HideHistory(),
  SelectHistoryItemCommand, JSON persistence to %LocalAppData%/RadioV2/search_history.json
- SearchBox GotFocus → ShowHistory(); LostFocus → Dispatcher.BeginInvoke(Background, HideHistory)

**Result:** App does NOT crash. History dropdown appears correctly when focusing the search box.

**Remaining problem:** Clicking a history item does nothing.

**Root cause:**
Two possible causes, likely both contributing:
1. LostFocus fires when focus leaves TextBox on click, which queues HideHistory at Background
   priority. The Border collapses before or during the click, cancelling the button interaction.
2. The `ui:Button` command binding via `RelativeSource AncestorType=Page` may have been
   resolving but the Dispatcher.Background callback was collapsing the Border before MouseUp
   could fire the Click event on the button.

---

## Attempt 4 — Replace ItemsControl with ListBox + ControlTemplate (REVERTED — user rewound)

**What was done:**
- Changed Border+ItemsControl to a direct `ListBox` with a custom `ItemContainerStyle`
  containing a full `ControlTemplate` for `ListBoxItem`
- ControlTemplate included `IsMouseOver` trigger using `{ui:ThemeResource SubtleFillColorHoverBrush}`
- LostFocus changed to check `HistoryList.IsMouseOver` instead of using Dispatcher delay
- SelectionChanged handler in code-behind replaced command binding

**Crash symptom:**
- Same as Attempts 1 and 2: browse screen unresponsive, title bar buttons black and non-functional

**Root cause (suspected):**
Unknown — this approach uses no Popup and no Z-index overlay.
Possible causes under investigation:
- Custom `ControlTemplate` for `ListBoxItem` may remove built-in focus/input handling in a way
  that propagates events incorrectly to the FluentWindow non-client area
- `{ui:ThemeResource SubtleFillColorHoverBrush}` may not exist in WPF-UI 4.2.0, causing a
  resource lookup exception that corrupts WPF's input system state
- ListBox internally creates a ScrollViewer which may intercept input differently than ItemsControl

**Action:** User rewound only this change (not a full git revert). Base returned to Attempt 3 state.

---

## Attempt 5 — PreviewMouseDown on Border + Border+TextBlock items (CURRENT — freezes)

**What was done:**
- Kept the exact working Border+ItemsControl structure from Attempt 3
- Replaced `ui:Button` items with plain `Border+TextBlock` (DataTemplate with IsMouseOver trigger)
- Added `x:Name="HistoryBorder"` and `PreviewMouseDown="HistoryBorder_PreviewMouseDown"` to outer Border
- PreviewMouseDown handler walks visual tree from OriginalSource up to HistoryBorder,
  finds the string DataContext, marks e.Handled=true (to prevent LostFocus), calls SelectHistoryItem
- Fixed CS0122 compile error: SelectHistoryItem was private (from [RelayCommand] private),
  changed to public

**Crash symptom:**
- App freezes when clicking the search area (TextBox or history items)
- No title bar issue this time — different symptom from previous crashes

**Suspected root cause:**
- `e.Handled = true` in PreviewMouseDown on the Border may be intercepting events that the
  `ui:TextBox` (in a sibling Grid row) needs in order to process focus/input correctly
- OR: `ShowHistory()` calls `LoadHistory()` synchronously on the UI thread (file read from
  %LocalAppData%). If the file read blocks, GotFocus would freeze the UI thread.
- OR: Setting `SearchQuery = item` inside a PreviewMouseDown handler (which runs synchronously
  on the UI thread) triggers `OnSearchQueryChanged` → `DebounceSearchAsync` → potential
  interaction with the synchronous execution context that causes a deadlock.

**Status: REVERTED — freeze, then "does nothing" — see Attempts 6 & 7**

---

## Attempt 6 — Remove e.Handled from PreviewMouseDown (KEPT as base — partial fix)

**What was done:**
- Removed `e.Handled = true` from `HistoryBorder_PreviewMouseDown`
- No other changes

**Result:**
- App no longer freezes when clicking the search area
- BUT: clicking history items does nothing (SelectHistoryItem may be called but has no visible effect, or the visual tree walk fails to find the string DataContext)

**Status: KEPT as stable non-freezing base, but clicking items still broken**

---

## Attempt 7 — MouseLeftButtonDown + IsMouseOver in LostFocus (REVERTED — froze)

**What was done:**
- Changed XAML: `PreviewMouseDown` → `MouseLeftButtonDown` on HistoryBorder
- Changed LostFocus: replaced `Dispatcher.BeginInvoke(Background, HideHistory)` with immediate `if (!HistoryBorder.IsMouseOver) _viewModel.HideHistory()`
- Renamed handler to `HistoryBorder_MouseLeftButtonDown`

**Crash symptom:**
- App froze every time the browse search bar was clicked

**Root cause (unknown):**
- `MouseLeftButtonDown` on a non-Preview event, combined with synchronous `IsMouseOver` check in LostFocus, caused a freeze
- Possibly: LostFocus fires during MouseDown processing (WPF-UI TextBox may behave differently from standard WPF), and the synchronous HideHistory call inside LostFocus disrupts event processing
- Possibly: `HistoryBorder.IsMouseOver` always true on click, preventing HideHistory from ever running, creating inconsistent UI state that stalls input

**Action:** Reverted — back to Attempt 6 state (PreviewMouseDown without e.Handled)

---

## Key Constraints Identified

1. WPF `Popup` (standalone, not in a control template) with `AllowsTransparency="True"` is
   incompatible with WPF-UI FluentWindow — always causes black title bar crash.

2. Z-index overlays (Panel.ZIndex on overflowing elements) also cause the same crash.

3. ListBox with custom ControlTemplate causes the same crash for unknown reasons.

4. The ONLY structure that does NOT crash is: plain `Border` in a real Grid row containing
   an `ItemsControl` — no ControlTemplate overrides, no overlays, no Z-index.

5. The core challenge: LostFocus on the TextBox fires before click events register on history
   items, collapsing the dropdown before the click can be handled.

6. `e.Handled = true` on any mouse event handler in this area causes a freeze.

7. Switching from `PreviewMouseDown` to `MouseLeftButtonDown` + synchronous `IsMouseOver` check
   in LostFocus also causes a freeze. Exact mechanism unknown.

8. Current safe base (Attempt 6): `PreviewMouseDown` on HistoryBorder WITHOUT `e.Handled`,
   `Dispatcher.BeginInvoke(Background, HideHistory)` in LostFocus. App does not freeze.
   Remaining problem: clicking items does nothing.

9. Attempt 8 root cause: adding `Focusable="True"` to inner Borders causes WPF to auto-focus
   the first item the moment HistoryBorder becomes Visible. This fires GotFocus → SelectHistoryItem
   on the first item immediately → IsHistoryVisible = false — all before any render frame,
   so the dropdown never visually appears. Lesson: never set Focusable=True on items inside
   a visibility-toggled container.

---

## SOLUTION — Attempt 9: ListBox + SelectionChanged ✅

**What was done:**
- Replaced `ItemsControl` with a plain `ListBox` (x:Name="HistoryList")
- No custom `ControlTemplate` on `ListBoxItem` — only minimal `ItemContainerStyle`
  (HorizontalContentAlignment=Stretch, Padding=0)
- Simplified `ItemTemplate` to just a `TextBlock` (no inner Border, no IsMouseOver trigger)
- Handled `SelectionChanged` on the ListBox in code-behind:
  clear `SelectedItem` first, then call `SelectHistoryItem(query)`
- LostFocus unchanged: `Dispatcher.BeginInvoke(Background, HideHistory)`

**Why it works:**
`SelectionChanged` fires during `MouseDown` processing — AFTER LostFocus queues `HideHistory`
at `Background` priority, but BEFORE that `Background` callback actually executes. This gives
a clean window where we can handle the selection without race conditions.

**Status: FIXED ✅ — dropdown appears, clicking items works, no freeze**

---

## Files Modified (final state)

- `ViewModels/BrowseViewModel.cs` — HistoryItems, IsHistoryVisible, ShowHistory, HideHistory,
  SaveCurrentQueryToHistory, SelectHistoryItem, LoadHistory, SaveToHistory, JSON persistence
- `Views/BrowsePage.xaml` — Grid.Row="2" history Border, ListBox with SelectionChanged,
  plain TextBlock ItemTemplate
- `Views/BrowsePage.xaml.cs` — GotFocus, LostFocus, KeyDown, HistoryList_SelectionChanged
