using System.Windows;
using System.Windows.Media.Animation;
using RadioV2.ViewModels;
using RadioV2.Helpers;

namespace RadioV2.Controls;

public partial class MiniPlayer : System.Windows.Controls.UserControl
{
    public MiniPlayer()
    {
        InitializeComponent();
        PlayPauseBtn.PreviewMouseLeftButtonDown += (_, _) => AnimatePress();
        IsVisibleChanged += (_, _) => { if (!IsVisible) ((Storyboard)Resources["PlayPulse"]).Remove(PlayPauseBtn); };
    }

    private void AnimatePress()
    {
        if (IsLoaded && IsVisible
            && MotionPreferences.Enabled && Window.GetWindow(this) is { IsActive: true })
            Dispatcher.Invoke(() => ((Storyboard)Resources["PlayPulse"]).Begin(PlayPauseBtn,
                HandoffBehavior.SnapshotAndReplace, true));
    }

    private void FavouriteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MiniPlayerViewModel vm)
            vm.ToggleFavouriteCommand.Execute(null);
    }
}
