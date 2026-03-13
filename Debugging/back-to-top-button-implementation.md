# Back to Top Button — Implementation Log

## Feature Spec

- In the group drill-down view (station list), a **"Back to top"** button appears when the user scrolls **up**
- It disappears when the user scrolls **back down**
- It disappears immediately (no animation) when navigating to a new group
- Clicking it scrolls the list back to the top
- Fade in / fade out animation (180 ms)
- Scale animation on mouse hover
- Uses Fluent UI theme colours
- Positioned just below the search bar

---

## Attempt 1 — Button overlaid inside the ListBox's wrapper Grid

### What was done

Wrapped `StationsListBox` in a `<Grid>` and placed the button as a sibling inside that Grid with `Panel.ZIndex="10"`, `VerticalAlignment="Bottom"`, `HorizontalAlignment="Center"`.

```xml
<Grid Grid.Row="3">
    <ListBox x:Name="StationsListBox" ... />

    <ui:Button x:Name="BackToTopButton"
               VerticalAlignment="Bottom"
               HorizontalAlignment="Center"
               Margin="0,0,0,16"
               Appearance="Secondary"
               Opacity="0"
               Visibility="Collapsed"
               Panel.ZIndex="10"
               Click="BackToTopButton_Click">
        ...
    </ui:Button>
</Grid>
```

Scroll direction tracking in code-behind:

```csharp
double current = _stationsSv.VerticalOffset;
bool scrollingUp = current < _lastVerticalOffset;
_lastVerticalOffset = current;

if (current <= 0)       HideBackToTop();
else if (scrollingUp)   ShowBackToTop();
else                    HideBackToTop();
```

Fade in/out via `DoubleAnimation` on `OpacityProperty`. `Visibility` toggled to `Collapsed` after fade-out completes.

### Problems

1. **Position wrong** — button was at the bottom of the list, not below the search bar.
2. **Z-index not working** — WPF-UI's `ListBoxItem` hover highlight rendered on top of the button. `Panel.ZIndex` only sorts children within the same panel; the ListBox's internal hover highlight renders inside the ListBox's own composition surface which is separate.

---

## Attempt 2 — Button moved to outer group Grid, Row 2+3 span

### What was done

Removed the wrapper `<Grid>` around the ListBox. Moved the button to be a direct child of the outer group view `Grid`, spanning Row 2 (featured) and Row 3 (stations), `VerticalAlignment="Top"` so it sits just below the search bar.

Also added hover scale animation via XAML `EventTrigger`:

```xml
<ui:Button x:Name="BackToTopButton"
           Grid.Row="2" Grid.RowSpan="2"
           VerticalAlignment="Top"
           HorizontalAlignment="Center"
           Margin="0,8,0,0"
           Appearance="Secondary"
           Opacity="0"
           Visibility="Collapsed"
           Panel.ZIndex="10"
           RenderTransformOrigin="0.5,0.5"
           Click="BackToTopButton_Click">
    <ui:Button.RenderTransform>
        <ScaleTransform ScaleX="1" ScaleY="1" />
    </ui:Button.RenderTransform>
    <ui:Button.Triggers>
        <EventTrigger RoutedEvent="Mouse.MouseEnter">
            <BeginStoryboard>
                <Storyboard>
                    <DoubleAnimation Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleX)"
                                     To="1.08" Duration="0:0:0.12" />
                    <DoubleAnimation Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleY)"
                                     To="1.08" Duration="0:0:0.12" />
                </Storyboard>
            </BeginStoryboard>
        </EventTrigger>
        <EventTrigger RoutedEvent="Mouse.MouseLeave">
            ...reverse...
        </EventTrigger>
    </ui:Button.Triggers>
    <StackPanel Orientation="Horizontal">
        <ui:SymbolIcon Symbol="ArrowUp24" Margin="0,0,6,0" />
        <TextBlock Text="Back to top" />
    </StackPanel>
</ui:Button>
```

### Problems

- Position now correct (just below search bar). ✅
- Hover animation works. ✅
- **Z-index still broken** — WPF-UI ListBoxItem blue hover highlight still rendered visually on top of the button. Even though the button is a sibling in the outer Grid with `Panel.ZIndex="10"`, the ListBox's internal GPU composition layer is not subject to the outer Grid's z-ordering rules.

---

## Attempt 3 — Canvas overlay as last child of root Grid

### What was done

Moved the button into a `<Canvas x:Name="OverlayCanvas">` added as the **absolute last child** of the page's root `<Grid>`. In WPF, later children in a panel are painted last = on top. `OverlayCanvas` was in `Grid.Row="1"`, after the group view Grid and carousel ScrollViewer.

Button position computed in code-behind via `TranslatePoint`:

```csharp
private void PositionBackToTopButton()
{
    BackToTopButton.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
    var size = BackToTopButton.DesiredSize;
    var pt = GroupSearchBox.TranslatePoint(
        new System.Windows.Point(0, GroupSearchBox.ActualHeight + 8), OverlayCanvas);
    Canvas.SetLeft(BackToTopButton, pt.X + (GroupSearchBox.ActualWidth - size.Width) / 2);
    Canvas.SetTop(BackToTopButton, pt.Y);
}
```

Called `PositionBackToTopButton()` on show and on `SizeChanged`.

### Problems

- **Still not working** — the WPF-UI ListBoxItem hover highlight was still rendering visually on top of the button. Even being the last-painted child of the root Grid is not enough. WPF-UI appears to use GPU-accelerated composition layers for its hover/focus effects that are submitted to the compositor independently of the CPU-side visual tree paint order.

---

## Attempt 4 — Popup (current implementation)

### Why this works

A `Popup` with `AllowsTransparency="True"` creates its own **Win32 HWND** — a separate layered window that Windows composites **at the OS level**, above the main application window entirely. No WPF rendering trick, no z-index, and no GPU composition layer inside the app can appear on top of a layered Popup window.

### XAML

Placed inside the inner group view `Grid` (so `PlacementTarget` can resolve `GroupSearchBox` by element name). `Grid.Row` is irrelevant for Popups — they render outside the layout.

```xml
<Popup x:Name="BackToTopPopup"
       Grid.Row="1"
       AllowsTransparency="True"
       Focusable="False"
       StaysOpen="True"
       PlacementTarget="{Binding ElementName=GroupSearchBox}"
       Placement="Bottom"
       VerticalOffset="8">
    <ui:Button x:Name="BackToTopButton"
               Opacity="0"
               Appearance="Secondary"
               RenderTransformOrigin="0.5,0.5"
               Click="BackToTopButton_Click">
        <ui:Button.RenderTransform>
            <ScaleTransform ScaleX="1" ScaleY="1" />
        </ui:Button.RenderTransform>
        <ui:Button.Triggers>
            <EventTrigger RoutedEvent="Mouse.MouseEnter">
                <BeginStoryboard>
                    <Storyboard>
                        <DoubleAnimation Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleX)"
                                         To="1.08" Duration="0:0:0.12" />
                        <DoubleAnimation Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleY)"
                                         To="1.08" Duration="0:0:0.12" />
                    </Storyboard>
                </BeginStoryboard>
            </EventTrigger>
            <EventTrigger RoutedEvent="Mouse.MouseLeave">
                <BeginStoryboard>
                    <Storyboard>
                        <DoubleAnimation Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleX)"
                                         To="1.0" Duration="0:0:0.12" />
                        <DoubleAnimation Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleY)"
                                         To="1.0" Duration="0:0:0.12" />
                    </Storyboard>
                </BeginStoryboard>
            </EventTrigger>
        </ui:Button.Triggers>
        <StackPanel Orientation="Horizontal">
            <ui:SymbolIcon Symbol="ArrowUp24" Margin="0,0,6,0" />
            <TextBlock Text="Back to top" />
        </StackPanel>
    </ui:Button>
</Popup>
```

### Key Popup properties

| Property | Value | Reason |
|---|---|---|
| `AllowsTransparency` | `True` | Required for the fade animation to render correctly (layered HWND needs transparency) |
| `Focusable` | `False` | Prevents the popup from stealing keyboard focus from the main window |
| `StaysOpen` | `True` | Prevents auto-close on focus loss |
| `PlacementTarget` | `GroupSearchBox` | Anchors the popup below the search box |
| `Placement` | `Bottom` | Appears directly below the PlacementTarget |
| `VerticalOffset` | `8` | 8 px gap between search box bottom and button top |

### Code-behind

```csharp
private void ShowBackToTop()
{
    if (_backToTopVisible) return;
    _backToTopVisible = true;
    BackToTopPopup.IsOpen = true;
    // Measure after open so DesiredSize is available, then centre over the search box
    BackToTopButton.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
    BackToTopPopup.HorizontalOffset = (GroupSearchBox.ActualWidth - BackToTopButton.DesiredSize.Width) / 2;
    var anim = new DoubleAnimation(1, TimeSpan.FromMilliseconds(180));
    BackToTopButton.BeginAnimation(OpacityProperty, anim);
}

private void HideBackToTop()
{
    if (!_backToTopVisible) return;
    _backToTopVisible = false;
    var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
    anim.Completed += (_, _) =>
    {
        if (!_backToTopVisible)
            BackToTopPopup.IsOpen = false;
    };
    BackToTopButton.BeginAnimation(OpacityProperty, anim);
}

private void HideBackToTopImmediate()
{
    _backToTopVisible = false;
    BackToTopButton.BeginAnimation(OpacityProperty, null);
    BackToTopButton.Opacity = 0;
    BackToTopPopup.IsOpen = false;
}
```

`HideBackToTopImmediate()` is called when entering a new group — cancels any in-flight animation and closes the popup synchronously before the scroll position resets.

### Scroll direction detection

```csharp
// Inside _stationsSv.ScrollChanged handler:
double current = _stationsSv.VerticalOffset;
bool scrollingUp = current < _lastVerticalOffset;
_lastVerticalOffset = current;

if (current <= 0)       HideBackToTop();
else if (scrollingUp)   ShowBackToTop();
else                    HideBackToTop();
```

---

## Attempt 5 — Border wrapper inside Popup with solid background

### Problem (Attempt 4 residual issue)

Even with the Popup rendering in its own HWND, the blue ListBoxItem hover highlight was still visually bleeding through the button. Root cause: `ui:Button` with `Appearance="Secondary"` has a semi-transparent background in WPF-UI's template. Because the Popup uses `AllowsTransparency="True"` (layered HWND), WPF composites the popup content's per-pixel alpha against whatever is behind the popup window — including the main window's ListBoxItem highlight. Transparent/semi-transparent pixels in the button let the main window show through.

### Fix

Wrapped the `ui:Button` in a `Border` with an opaque background and moved the fade animation to the Border:

```xml
<Popup ... AllowsTransparency="True" ...>
    <Border x:Name="BackToTopBorder"
            Background="{ui:ThemeResource ApplicationBackgroundBrush}"
            CornerRadius="6"
            Padding="0"
            Opacity="0">
        <ui:Button x:Name="BackToTopButton" .../>
    </Border>
</Popup>
```

Code-behind now animates `BackToTopBorder.Opacity` instead of `BackToTopButton.Opacity`. The Border's opaque background blocks the main window content from bleeding through any semi-transparent areas of the button fill.

---

## Key lesson

**`Panel.ZIndex` and visual tree child order are both insufficient to render above WPF-UI's ListBoxItem highlight.** WPF-UI submits hover/focus highlight draws to the GPU compositor independently of the CPU-side visual tree, so no amount of z-ordering within the WPF visual tree can win. The only reliable solution for a truly-on-top overlay in WPF is a `Popup` (own HWND) or an `Adorner` in the `AdornerLayer`.

**Additionally:** even inside a Popup (own HWND) with `AllowsTransparency="True"`, if the popup content has semi-transparent pixels, the main window content bleeds through those pixels at the DWM compositor level. Always wrap Popup content in a `Border` with a solid opaque background when you need a fully opaque visual.
