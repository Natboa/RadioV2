using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RadioV2.Controls;

public partial class SoundbarAnimation : System.Windows.Controls.UserControl
{
    private Storyboard? _storyboard;

    public static readonly DependencyProperty IsAnimatingProperty =
        DependencyProperty.Register(
            nameof(IsAnimating),
            typeof(bool),
            typeof(SoundbarAnimation),
            new PropertyMetadata(false, OnIsAnimatingChanged));

    public bool IsAnimating
    {
        get => (bool)GetValue(IsAnimatingProperty);
        set => SetValue(IsAnimatingProperty, value);
    }

    public SoundbarAnimation()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Set bar color at runtime to avoid ThemeResource in XAML
        var brush = SystemParameters.WindowGlassBrush as System.Windows.Media.Brush
            ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 215));

        Bar1.Fill = brush;
        Bar2.Fill = brush;
        Bar3.Fill = brush;

        // Guard: binding may have resolved before the control was loaded
        if (IsAnimating)
            StartAnimation();
        else
            Visibility = Visibility.Collapsed;
    }

    private static void OnIsAnimatingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (SoundbarAnimation)d;
        if ((bool)e.NewValue)
            ctrl.StartAnimation();
        else
            ctrl.StopAnimation();
    }

    private void StartAnimation()
    {
        Visibility = Visibility.Visible;
        _storyboard ??= BuildStoryboard();
        _storyboard.Begin(this, true);
    }

    private void StopAnimation()
    {
        _storyboard?.Stop(this);
        Visibility = Visibility.Collapsed;

        // Reset bars to collapsed state
        if (Bar1.RenderTransform is ScaleTransform st1) st1.ScaleY = 0.2;
        if (Bar2.RenderTransform is ScaleTransform st2) st2.ScaleY = 0.2;
        if (Bar3.RenderTransform is ScaleTransform st3) st3.ScaleY = 0.2;
    }

    private Storyboard BuildStoryboard()
    {
        var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

        AddBarAnimation(sb, Bar1, TimeSpan.Zero);
        AddBarAnimation(sb, Bar2, TimeSpan.FromMilliseconds(130));
        AddBarAnimation(sb, Bar3, TimeSpan.FromMilliseconds(70));

        return sb;
    }

    private static void AddBarAnimation(Storyboard sb, System.Windows.Shapes.Rectangle bar, TimeSpan beginTime)
    {
        var anim = new DoubleAnimation
        {
            From = 0.2,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(500)),
            AutoReverse = true,
            BeginTime = beginTime,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        Storyboard.SetTarget(anim, bar);
        Storyboard.SetTargetProperty(anim, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
        sb.Children.Add(anim);
    }
}
