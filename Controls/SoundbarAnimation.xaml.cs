using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using RadioV2.Helpers;

namespace RadioV2.Controls;

public partial class SoundbarAnimation : System.Windows.Controls.UserControl
{
    private Storyboard? _storyboard;
    private Window? _window;

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
        Unloaded += (_, _) =>
        {
            if (_window is not null)
            {
                _window.Activated -= OnWindowChanged;
                _window.Deactivated -= OnWindowChanged;
                _window.StateChanged -= OnWindowChanged;
            }
            _window = null;
            SystemParameters.StaticPropertyChanged -= OnMotionPreferenceChanged;
            _storyboard?.Remove(this);
        };
        IsVisibleChanged += (_, _) => UpdateAnimation();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // The now-playing station name beside these bars is painted with
        // AccentTextFillColorPrimaryBrush, so the bars have to use the same
        // brush or the row shows two different "accent" colours side by side.
        // WindowGlassBrush, which this used before, is the Windows glass
        // colour and matches neither.
        var brush = Application.Current?.TryFindResource("AccentTextFillColorPrimaryBrush")
            as System.Windows.Media.Brush ?? SystemParameters.WindowGlassBrush;

        Bar1.Fill = brush;
        Bar2.Fill = brush;
        Bar3.Fill = brush;
        Bar4.Fill = brush;
        Bar5.Fill = brush;

        _window = Window.GetWindow(this);
        SystemParameters.StaticPropertyChanged += OnMotionPreferenceChanged;
        if (_window is not null)
        {
            _window.Activated += OnWindowChanged;
            _window.Deactivated += OnWindowChanged;
            _window.StateChanged += OnWindowChanged;
        }
        UpdateAnimation();
    }

    private static void OnIsAnimatingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (SoundbarAnimation)d;
        ctrl.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        ctrl.UpdateAnimation();
    }

    private void OnWindowChanged(object? sender, EventArgs e) => UpdateAnimation();
    private void OnMotionPreferenceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) Dispatcher.Invoke(UpdateAnimation);
    }

    private void UpdateAnimation()
    {
        _storyboard?.Remove(this);
        if (!IsAnimating || !IsLoaded || !IsVisible) return;
        if (MotionPreferences.Enabled && _window is { IsActive: true, WindowState: not WindowState.Minimized }) StartAnimation();
        else // retain the playing indicator without perpetual motion
            foreach (var bar in new[] { Bar1, Bar2, Bar3, Bar4, Bar5 })
                ((ScaleTransform)bar.RenderTransform).ScaleY = .5;
    }

    private void StartAnimation()
    {
        Visibility = Visibility.Visible;
        _storyboard ??= BuildStoryboard();
        _storyboard.Begin(this, true);
    }

    private Storyboard BuildStoryboard()
    {
        var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

        AddBarAnimation(sb, Bar1, TimeSpan.Zero);
        AddBarAnimation(sb, Bar2, TimeSpan.FromMilliseconds(130));
        AddBarAnimation(sb, Bar3, TimeSpan.FromMilliseconds(70));
        AddBarAnimation(sb, Bar4, TimeSpan.FromMilliseconds(200));
        AddBarAnimation(sb, Bar5, TimeSpan.FromMilliseconds(100));

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
