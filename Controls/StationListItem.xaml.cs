using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

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
