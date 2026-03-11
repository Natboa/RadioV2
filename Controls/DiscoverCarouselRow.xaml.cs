using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using UserControl = System.Windows.Controls.UserControl;
using ScrollChangedEventArgs = System.Windows.Controls.ScrollChangedEventArgs;
using Wpf.Ui.Appearance;

namespace RadioV2.Controls;

public partial class DiscoverCarouselRow : UserControl
{
    private EventHandler? _renderHandler;
    private bool _rowHovered;
    private bool _canScrollLeft;
    private bool _canScrollRight;

    public static readonly DependencyProperty IsDarkModeProperty =
        DependencyProperty.Register(nameof(IsDarkMode), typeof(bool), typeof(DiscoverCarouselRow), new PropertyMetadata(false));

    public bool IsDarkMode
    {
        get => (bool)GetValue(IsDarkModeProperty);
        set => SetValue(IsDarkModeProperty, value);
    }

    public DiscoverCarouselRow()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            IsDarkMode = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
            ApplicationThemeManager.Changed += OnThemeChanged;
        };
        Unloaded += (_, _) => ApplicationThemeManager.Changed -= OnThemeChanged;

        MouseEnter += (_, _) => { _rowHovered = true;  UpdateButtonStates(); };
        MouseLeave += (_, _) => { _rowHovered = false; UpdateButtonStates(); };
    }

    private void OnThemeChanged(ApplicationTheme theme, System.Windows.Media.Color _) =>
        IsDarkMode = theme == ApplicationTheme.Dark;

    // ── Scroll arrow clicks ──────────────────────────────────────────────────

    private void RightArrow_Click(object sender, RoutedEventArgs e)
    {
        const double cardWidth = 172.0; // card 160 + margin 12
        const double cardBody = 160.0;

        double viewport = CarouselScroll.ViewportWidth;
        double rawTarget = CarouselScroll.HorizontalOffset + viewport;
        double rawRight = rawTarget + viewport;

        double snappedRight = Math.Floor((rawRight - cardBody) / cardWidth) * cardWidth + cardWidth;
        double target = snappedRight - viewport;

        target = Math.Min(target, CarouselScroll.ScrollableWidth);
        target = Math.Max(target, CarouselScroll.HorizontalOffset);
        AnimateScrollTo(target);
    }

    private void LeftArrow_Click(object sender, RoutedEventArgs e)
    {
        const double cardWidth = 172.0;
        const double cardMargin = 12.0;

        double viewport = CarouselScroll.ViewportWidth;
        double rawTarget = CarouselScroll.HorizontalOffset - viewport;

        double snappedLeft = Math.Floor(rawTarget / cardWidth) * cardWidth - cardMargin;
        double target = Math.Max(snappedLeft, 0);
        target = Math.Min(target, CarouselScroll.HorizontalOffset);
        AnimateScrollTo(target);
    }

    // ── Scroll state tracking ────────────────────────────────────────────────

    private void CarouselScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        _canScrollLeft  = CarouselScroll.HorizontalOffset > 0;
        _canScrollRight = CarouselScroll.HorizontalOffset < CarouselScroll.ScrollableWidth;
        UpdateButtonStates();
    }

    // ── Button fade logic ────────────────────────────────────────────────────

    private void UpdateButtonStates()
    {
        FadeButton(LeftArrowButton,  _rowHovered && _canScrollLeft);
        FadeButton(RightArrowButton, _rowHovered && _canScrollRight);
    }

    private static void FadeButton(System.Windows.Controls.Control button, bool show)
    {
        double target = show ? 1.0 : 0.0;
        if (Math.Abs(button.Opacity - target) < 0.01 && button.IsHitTestVisible == show) return;

        button.IsHitTestVisible = show;
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        button.BeginAnimation(OpacityProperty, anim);
    }

    // ── Smooth scroll animation ──────────────────────────────────────────────

    private void AnimateScrollTo(double target)
    {
        if (_renderHandler != null)
        {
            CompositionTarget.Rendering -= _renderHandler;
            _renderHandler = null;
        }

        double start = CarouselScroll.HorizontalOffset;
        double distance = target - start;
        if (Math.Abs(distance) < 0.5) return;

        const double duration = 850.0;
        TimeSpan startTime = TimeSpan.Zero;
        TimeSpan lastRenderTime = TimeSpan.MinValue;

        EventHandler? handler = null;
        handler = (s, e) =>
        {
            var renderTime = ((RenderingEventArgs)e).RenderingTime;
            if (renderTime == lastRenderTime) return;
            lastRenderTime = renderTime;

            if (startTime == TimeSpan.Zero) startTime = renderTime;

            double elapsed = (renderTime - startTime).TotalMilliseconds;
            double t = Math.Min(elapsed / duration, 1.0);
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
