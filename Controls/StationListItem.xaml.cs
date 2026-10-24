using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using RadioV2.Helpers;

namespace RadioV2.Controls;

public partial class StationListItem : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty HoverOnlyHeartProperty =
        DependencyProperty.Register(nameof(HoverOnlyHeart), typeof(bool), typeof(StationListItem), new PropertyMetadata(false));

    public bool HoverOnlyHeart
    {
        get => (bool)GetValue(HoverOnlyHeartProperty);
        set => SetValue(HoverOnlyHeartProperty, value);
    }

    public StationListItem()
    {
        InitializeComponent();
        MouseDoubleClick += OnMouseDoubleClick;
    }

    private void OnFavouriteFeedback(object sender, RoutedEventArgs e)
    {
        if (!MotionPreferences.Enabled || !IsVisible || !IsMouseOver
            || Keyboard.IsKeyDown(Key.Space) || Keyboard.IsKeyDown(Key.Enter)) return;
        var transform = (ScaleTransform)HeartIcon.RenderTransform;
        var animation = new DoubleAnimation(1, 1.08, TimeSpan.FromMilliseconds(70))
        { AutoReverse = true, FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Walk up to the parent Page to find PlayStationCommand on its DataContext
        DependencyObject current = this;
        while (current != null)
        {
            if (current is System.Windows.Controls.Page page)
            {
                var dc = page.DataContext;
                if (dc == null) return;

                var prop = dc.GetType().GetProperty("PlayStationCommand");
                if (prop?.GetValue(dc) is ICommand command && command.CanExecute(DataContext))
                {
                    command.Execute(DataContext);
                    e.Handled = true;
                }
                return;
            }
            current = VisualTreeHelper.GetParent(current);
        }
    }
}
