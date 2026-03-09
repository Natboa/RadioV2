using System.Windows;
using System.Windows.Media;
using UserControl = System.Windows.Controls.UserControl;
using ScrollChangedEventArgs = System.Windows.Controls.ScrollChangedEventArgs;

namespace RadioV2.Controls;

public partial class DiscoverCarouselRow : UserControl
{
    private EventHandler? _renderHandler;

    public DiscoverCarouselRow()
    {
        InitializeComponent();
    }

    private void RightArrow_Click(object sender, RoutedEventArgs e)
    {
        double target = Math.Min(
            CarouselScroll.HorizontalOffset + CarouselScroll.ViewportWidth,
            CarouselScroll.ScrollableWidth);
        AnimateScrollTo(target);
    }

    private void LeftArrow_Click(object sender, RoutedEventArgs e)
    {
        double target = Math.Max(
            CarouselScroll.HorizontalOffset - CarouselScroll.ViewportWidth, 0);
        AnimateScrollTo(target);
    }

    private void CarouselScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        LeftArrowButton.Visibility = CarouselScroll.HorizontalOffset > 0
            ? Visibility.Visible : Visibility.Collapsed;

        RightArrowButton.Visibility = CarouselScroll.HorizontalOffset < CarouselScroll.ScrollableWidth
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AnimateScrollTo(double target)
    {
        // Cancel any in-progress animation immediately
        if (_renderHandler != null)
        {
            CompositionTarget.Rendering -= _renderHandler;
            _renderHandler = null;
        }

        double start = CarouselScroll.HorizontalOffset;
        double distance = target - start;
        if (Math.Abs(distance) < 0.5) return;

        const double duration = 850.0; // ms
        TimeSpan startTime = TimeSpan.Zero;
        TimeSpan lastRenderTime = TimeSpan.MinValue;

        EventHandler? handler = null;
        handler = (s, e) =>
        {
            // WPF fires CompositionTarget.Rendering multiple times per vsync frame —
            // guard against duplicate calls using RenderingTime to get true 60fps
            var renderTime = ((RenderingEventArgs)e).RenderingTime;
            if (renderTime == lastRenderTime) return;
            lastRenderTime = renderTime;

            if (startTime == TimeSpan.Zero) startTime = renderTime;

            double elapsed = (renderTime - startTime).TotalMilliseconds;
            double t = Math.Min(elapsed / duration, 1.0);

            // Expo ease-out: launches at max velocity, brakes sharply at the end ("slot into place")
            double eased = t >= 1.0 ? 1.0 : 1.0 - Math.Pow(2.0, -10.0 * t);

            CarouselScroll.ScrollToHorizontalOffset(start + distance * eased);

            if (t >= 1.0)
            {
                CompositionTarget.Rendering -= handler;
                _renderHandler = null;
            }
        };

        _renderHandler = handler;
        CompositionTarget.Rendering += handler;
    }
}
