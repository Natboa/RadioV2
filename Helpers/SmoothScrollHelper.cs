using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RadioV2.Helpers;

/// <summary>
/// Attached behavior that replaces the default instant mouse-wheel jump on any
/// ScrollViewer with a smooth, expo-ease-out pixel animation (true 60 fps via
/// CompositionTarget.Rendering). Accumulates rapid ticks into a single running
/// animation — no stutter, no overlapping timers.
/// </summary>
public static class SmoothScrollHelper
{
    private sealed class ScrollState
    {
        public double TargetOffset;
        public double StartOffset;
        public TimeSpan AnimStart;
        public TimeSpan LastRenderTime = TimeSpan.MinValue;
        public EventHandler? RenderHandler;
    }

    private static readonly Dictionary<ScrollViewer, ScrollState> _states = [];

    // ── Attached property ─────────────────────────────────────────────────────

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(SmoothScrollHelper),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject obj, bool value) =>
        obj.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject obj) =>
        (bool)obj.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer sv) return;
        if ((bool)e.NewValue)
        {
            sv.PreviewMouseWheel += OnWheel;
            sv.Unloaded += OnUnloaded;
        }
        else
        {
            sv.PreviewMouseWheel -= OnWheel;
            sv.Unloaded -= OnUnloaded;
            StopAnimation(sv);
        }
    }

    // ── Wheel handler ─────────────────────────────────────────────────────────

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var sv = (ScrollViewer)sender;

        // Only intercept vertical scrollers that can actually scroll
        if (sv.ScrollableHeight <= 0) return;

        e.Handled = true;

        // ~3 lines × 16px — matches WPF default feel but in pixels
        double delta = e.Delta / 120.0 * 48.0;

        if (!_states.TryGetValue(sv, out var state))
        {
            state = new ScrollState { TargetOffset = sv.VerticalOffset };
            _states[sv] = state;
        }

        // Accumulate target; redirect from current visual position each tick
        state.TargetOffset = Math.Clamp(state.TargetOffset - delta, 0, sv.ScrollableHeight);
        state.StartOffset  = sv.VerticalOffset;
        state.AnimStart    = TimeSpan.Zero;   // reset: running handler picks this up next frame

        if (state.RenderHandler != null) return;   // already animating — just updated target above

        EventHandler? handler = null;
        handler = (_, args) =>
        {
            var rt = ((RenderingEventArgs)args).RenderingTime;

            // Deduplicate: WPF sometimes fires Rendering twice per vsync
            if (rt == state.LastRenderTime) return;
            state.LastRenderTime = rt;

            if (state.AnimStart == TimeSpan.Zero) state.AnimStart = rt;

            double t     = Math.Min((rt - state.AnimStart).TotalMilliseconds / 350.0, 1.0);
            double eased = t >= 1.0 ? 1.0 : 1.0 - Math.Pow(2.0, -10.0 * t);

            sv.ScrollToVerticalOffset(state.StartOffset + (state.TargetOffset - state.StartOffset) * eased);

            if (t >= 1.0)
            {
                CompositionTarget.Rendering -= handler;
                state.RenderHandler = null;
                _states.Remove(sv);
            }
        };

        state.RenderHandler = handler;
        CompositionTarget.Rendering += handler;
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    private static void OnUnloaded(object sender, RoutedEventArgs e) =>
        StopAnimation((ScrollViewer)sender);

    private static void StopAnimation(ScrollViewer sv)
    {
        if (!_states.TryGetValue(sv, out var state)) return;
        if (state.RenderHandler != null)
            CompositionTarget.Rendering -= state.RenderHandler;
        _states.Remove(sv);
    }
}
