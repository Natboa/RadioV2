# Panel Clock

A digital clock displayed in the left navigation panel, between the Favourites and Settings buttons. Visible only when the panel is expanded; fades out fast on collapse.

---

## How it works

### State & timer — `ViewModels/MainWindowViewModel.cs`

| Property | Type | Purpose |
|---|---|---|
| `IsClockEnabled` | `bool` | Master on/off. Starts/stops the timer. |
| `CurrentTime` | `string` | Bound to the clock TextBlock. Updated every 30 s. |

When `IsClockEnabled` flips to `true`, `StartClock()` fires immediately (so the time shows at once) then a `DispatcherTimer` ticks every 30 seconds.

**Time format:** `HH:mm` (24-hour, no seconds). Change it in `UpdateTime()`:
```csharp
CurrentTime = DateTime.Now.ToString("HH:mm");   // → 14:35
// CurrentTime = DateTime.Now.ToString("hh:mm tt"); // → 02:35 PM
// CurrentTime = DateTime.Now.ToString("HH:mm:ss"); // → 14:35:07
```

**Tick interval** (default 30 s — accurate enough since seconds aren't shown):
```csharp
_clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
```

---

### XAML — `MainWindow.xaml`

The clock lives as the first item in `NavigationView.FooterMenuItems`, directly above Settings:

```xml
<ui:NavigationView.FooterMenuItems>
    <Border x:Name="ClockPanel" ...>
        <TextBlock Text="{Binding CurrentTime}" FontSize="80" FontWeight="Light" ... />
    </Border>
    <ui:NavigationViewItem Content="Settings" ... />
</ui:NavigationView.FooterMenuItems>
```

**To change the font size:** edit `FontSize="80"` on the TextBlock.
**To change the font weight:** edit `FontWeight="Light"` (try `Thin`, `Regular`, `SemiBold`).
**To add padding around the clock:** edit `Padding="0,2,0,2"` on the Border.
**To change the text colour:** replace `{ui:ThemeResource TextFillColorPrimaryBrush}` with another semantic brush — never use hex values.

---

### Visibility & fade — `MainWindow.xaml.cs`

Two methods control the clock panel at runtime:

#### `UpdatePaneToggleButton()`
Called every time `IsPaneOpen` changes. If the clock is enabled:
- **Pane closing:** animates `ClockPanel.Opacity` from 1 → 0 over **100 ms**, then sets `Visibility = Collapsed`.
- **Pane opening:** clears the animation, sets `Opacity = 1` and `Visibility = Visible`.

**To change fade duration:**
```csharp
var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(100)); // ← change this
```

#### `UpdateClockPanel()`
Called when `IsClockEnabled` changes (e.g. user toggles it in Settings). Shows or hides the panel immediately with no animation, respecting the current pane state.

Both methods are subscribed in `OnWindowLoaded`.

---

### Settings toggle — `Views/SettingsPage.xaml`

The toggle lives inside the **Appearance** card:

```xml
<DockPanel LastChildFill="False">
    <TextBlock Text="Clock" VerticalAlignment="Center" ... />
    <ui:ToggleSwitch DockPanel.Dock="Right" IsChecked="{Binding IsClockEnabled}" />
</DockPanel>
```

The binding hits `SettingsViewModel.IsClockEnabled`, which:
1. Sets `MainWindowViewModel.IsClockEnabled` (live update in the panel).
2. Persists the value to the database via `SetSettingAsync("ClockEnabled", ...)`.

On app startup (`App.xaml.cs → RestoreSessionAsync`), the saved value is read back and applied to `MainWindowViewModel` before the window is shown.

---

### SettingsViewModel — `ViewModels/SettingsViewModel.cs`

```csharp
partial void OnIsClockEnabledChanged(bool value)
{
    if (_suppressClockChanges) return;
    _mainWindowVm.IsClockEnabled = value;
    _ = _stationService.SetSettingAsync("ClockEnabled", value ? "true" : "false");
}
```

`_suppressClockChanges` prevents the DB write from firing during `LoadAsync()` (page open), when the property is synced silently from `MainWindowViewModel`.

---

## Quick-change reference

| What to change | Where | What to edit |
|---|---|---|
| Font size | `MainWindow.xaml` | `FontSize="80"` |
| Font weight | `MainWindow.xaml` | `FontWeight="Light"` |
| Time format | `MainWindowViewModel.cs` | `DateTime.Now.ToString("HH:mm")` |
| Update frequency | `MainWindowViewModel.cs` | `TimeSpan.FromSeconds(30)` |
| Fade speed | `MainWindow.xaml.cs` | `TimeSpan.FromMilliseconds(100)` |
| Clock vertical padding | `MainWindow.xaml` | `Padding="0,2,0,2"` on the Border |
| Label in Settings | `SettingsPage.xaml` | `Text="Clock"` |
