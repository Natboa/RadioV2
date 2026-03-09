# App Logo / Icon Setup

## What was done

### 1. Added the logo file
User provided `RaduiV2_Logo.png` in the root. Moved to `Assets/RadioV2_Logo.ico` (fixed typo in filename).

### 2. Set window icon in XAML
Added `Icon="Assets/RadioV2_Logo.png"` to `FluentWindow` in `MainWindow.xaml`.

### 3. Registered as WPF Resource + ApplicationIcon
In `RadioV2.csproj`:
- `<ApplicationIcon>Assets\RadioV2_Logo.ico</ApplicationIcon>` — sets the EXE file icon (Explorer, Start menu)
- `<Resource Include="Assets\RadioV2_Logo.png" />`
- `<Resource Include="Assets\RadioV2_Logo.ico" />`

### 4. Set tray icon in TrayIconManager
`TrayIconManager.cs` was using `SystemIcons.Application` (generic Windows icon).
Added `LoadAppIcon()` which loads the ICO from the WPF pack URI:
```csharp
var uri = new Uri("pack://application:,,,/Assets/RadioV2_Logo.ico", UriKind.Absolute);
var info = System.Windows.Application.GetResourceStream(uri);
return new Icon(info.Stream);
```
Falls back to `SystemIcons.Application` if loading fails.

### 5. Added logo to nav pane header
Placed a 40x40 `Image` in `NavigationView.PaneHeader` so the logo is visible inside the app above the nav items.

---

## Problem: icon still looked too small in taskbar / tray

**Attempt 1:** Generated ICO using `Icon.FromHandle(bitmap.GetHicon())` — produced a single-frame ICO at one size only. Windows scales it down badly.

**Attempt 2:** Built a proper multi-resolution ICO (6 frames: 16, 32, 48, 64, 128, 256px) using a manual ICO binary writer in PowerShell. Still looked small.

**Attempt 3:** Changed `MainWindow.xaml` `Icon` from `.png` to `.ico` so WPF uses the multi-resolution ICO at runtime (not just a single PNG scaled by WPF).

**Still small.** Root cause identified:

**The logo PNG is 1024x1024 but the actual radio graphic only occupies pixels ~(242,283) to (782,687).** The rest is whitespace. When the ICO was generated from the full canvas, the radio was a small fraction of each frame.

---

## Final fix

Auto-cropped the PNG to the bounding box of non-white/non-transparent pixels (with 8px padding) before generating the ICO. PowerShell script:

1. Scanned every pixel — skipped white (R>240, G>240, B>240) and near-transparent (A<=20)
2. Found tight bounding box: (242,283)→(782,687) from a 1024x1024 source
3. Cropped to that region
4. Generated multi-resolution ICO from the cropped bitmap (16/32/48/64/128/256px frames)
5. Saved to `Assets/RadioV2_Logo.ico`

The radio graphic now fills nearly the entire icon frame at every size, making it appear correctly sized in the taskbar, Alt-Tab switcher, and system tray.

---

## Files changed

| File | Change |
|---|---|
| `Assets/RadioV2_Logo.png` | Logo source (moved from root, filename typo fixed) |
| `Assets/RadioV2_Logo.ico` | Generated multi-resolution ICO (tight-cropped) |
| `RadioV2.csproj` | `ApplicationIcon`, `<Resource>` entries |
| `MainWindow.xaml` | `Icon="Assets/RadioV2_Logo.ico"`, logo in PaneHeader |
| `Helpers/TrayIconManager.cs` | `LoadAppIcon()` loads ICO from pack URI for `NotifyIcon` |
