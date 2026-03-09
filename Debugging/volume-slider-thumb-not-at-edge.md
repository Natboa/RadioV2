# Bug: Volume Slider Circle Not Reaching Edges at Min/Max

**File affected:** `Controls/MiniPlayer.xaml`
**Status:** Fixed

## Symptom

When the volume slider was at 0 (mute) or 100 (max), the thumb circle appeared to be at roughly 3% or 97% — it visually stopped short of the track endpoints instead of sitting at the edge.

## Root Cause

WPF-UI's Slider ControlTemplate (`UiHorizontalSlider`) uses a 20×20px thumb with a 12×12px inner ellipse. The inner ellipse is centered inside the thumb, leaving a 4px transparent gap on each side ((20-12)/2 = 4px).

WPF's `Track` control positions the thumb so its **outer edge** (the full 20px bounds) aligns with the track endpoints at min and max. This means the **visible circle** stops 4px short of each end of the track background line, which spans the full slider width. On a 120px slider this looks like the circle is at ~3% / ~97%.

## Attempts That Did Not Work

**Attempt 1:** Set `Padding="0"` directly on the `<Slider>` element.
- Had no effect — WPF-UI's Style (applied via `BasedOn="{StaticResource {x:Type Slider}}"`) sets the Template via a trigger, which takes priority over a direct property value.

**Attempt 2:** Add `<Setter Property="Padding" Value="0" />` inside the inline `<Style>`.
- Still no effect — the Template itself hard-codes `Margin="0"` on the `TrackBackground` border and does not use `{TemplateBinding Padding}` anywhere.

## Fix

Override the `ControlTemplate` for `Orientation=Horizontal` inside the slider's inline Style. The only change from WPF-UI's original template is setting `Margin="10,0"` (half of thumb width) on the `TrackBackground` border instead of `Margin="0"`.

This insets the visible track line by 10px on each side so its endpoints align with the thumb's center (and thus the inner circle) at min and max — matching Windows 11 Fluent slider behavior.

```
Track line visual:      |----------|        (inset 10px each side)
Thumb circle travel:  O            O        (center travels from 10px to width-10px)
```

**Key change in `MiniPlayer.xaml`:**

```xml
<!-- Before (inside WPF-UI's default template) -->
<Border x:Name="TrackBackground" ... Margin="0" ... />

<!-- After (custom ControlTemplate override) -->
<Border x:Name="TrackBackground" ... Margin="10,0" ... />
```

The override is placed inside a `<Trigger Property="Orientation" Value="Horizontal">` setter so it wins over the base style's trigger, which also targets the same property.
