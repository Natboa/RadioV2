using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RadioV2.Helpers;

/// <summary>
/// Attached behavior that replaces the default instant mouse-wheel jump on any
/// ScrollViewer with smooth deceleration at true 60 fps (CompositionTarget.Rendering).
/// Each frame moves a fixed fraction of remaining distance toward the target — this
/// gives natural exponential decay with no timing state that can stutter on reset.
/// Rapid wheel ticks simply accumulate into the target; no animation restart needed.
/// </summary>
public static class SmoothScrollHelper
{
    /// <summary>Frames without the offset moving before we conclude the target is
    /// unreachable and stop, rather than render forever trying to get there.</summary>
    private const int StallFrameLimit = 10;

    private sealed class ScrollState
    {
        public double TargetOffset;
        public TimeSpan LastRenderTime = TimeSpan.MinValue;
        public EventHandler? RenderHandler;
        public double LastOffset = double.NaN;
        public int StalledFrames;
    }

    // ConditionalWeakTable, not Dictionary: the old static dictionary held a strong
    // reference to every ScrollViewer it had ever animated, so an abandoned one could
    // not be collected.
    private static readonly ConditionalWeakTable<ScrollViewer, ScrollState> _states = new();

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

        if (!MotionPreferences.Enabled)
        {
            StopAnimation(sv);
            return; // leave native, immediate wheel handling in control
        }

        e.Handled = true;

        // ~3 lines × 16px per notch
        double delta = e.Delta / 120.0 * 48.0;

        if (!_states.TryGetValue(sv, out var state))
        {
            state = new ScrollState { TargetOffset = sv.VerticalOffset };
            _states.Add(sv, state);
        }

        // Accumulate — new ticks just shift the target, no animation restart
        state.TargetOffset = Math.Clamp(state.TargetOffset - delta, 0, sv.ScrollableHeight);
        state.StalledFrames = 0;

        if (state.RenderHandler != null) return;

        EventHandler? handler = null;
        handler = (_, args) =>
        {
            if (!sv.IsVisible || !MotionPreferences.Enabled || Window.GetWindow(sv) is { IsActive: false })
            {
                Detach(sv, state, handler!);
                return;
            }
            var rt = ((RenderingEventArgs)args).RenderingTime;

            // Deduplicate: WPF sometimes fires Rendering twice per vsync
            if (rt == state.LastRenderTime) return;
            state.LastRenderTime = rt;

            double current = sv.VerticalOffset;

            // Re-clamp every frame, not just on the wheel event. Infinite scroll
            // changes ScrollableHeight underneath us, and a target left beyond the
            // new maximum is never reached — which used to leave this handler
            // running at 60 fps for the rest of the process's life.
            state.TargetOffset = Math.Clamp(state.TargetOffset, 0, sv.ScrollableHeight);

            double remaining = state.TargetOffset - current;

            if (Math.Abs(remaining) < 0.5)
            {
                sv.ScrollToVerticalOffset(state.TargetOffset);
                Detach(sv, state, handler!);
                return;
            }

            // Backstop for anything the clamp cannot express: if the offset has not
            // actually moved for several frames, the target is unreachable whatever
            // the arithmetic says.
            if (Math.Abs(current - state.LastOffset) < 0.01)
            {
                if (++state.StalledFrames >= StallFrameLimit)
                {
                    Detach(sv, state, handler!);
                    return;
                }
            }
            else
            {
                state.StalledFrames = 0;
            }

            state.LastOffset = current;

            // Lerp: close 20% of remaining gap each frame → natural exponential deceleration
            sv.ScrollToVerticalOffset(current + remaining * 0.20);
        };

        state.RenderHandler = handler;
        CompositionTarget.Rendering += handler;
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    private static void Detach(ScrollViewer sv, ScrollState state, EventHandler handler)
    {
        CompositionTarget.Rendering -= handler;
        state.RenderHandler = null;
        _states.Remove(sv);
    }

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
