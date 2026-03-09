using System.Windows;
using System.Windows.Threading;
using UserControl = System.Windows.Controls.UserControl;
using ScrollChangedEventArgs = System.Windows.Controls.ScrollChangedEventArgs;

namespace RadioV2.Controls;

public partial class DiscoverCarouselRow : UserControl
{
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
        double start = CarouselScroll.HorizontalOffset;
        double duration = 300.0; // ms
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (s, e) =>
        {
            double t = Math.Min(sw.ElapsedMilliseconds / duration, 1.0);
            double eased = 1 - Math.Pow(1 - t, 3); // ease-out cubic
            CarouselScroll.ScrollToHorizontalOffset(start + (target - start) * eased);
            if (t >= 1.0) timer.Stop();
        };
        timer.Start();
    }
}
