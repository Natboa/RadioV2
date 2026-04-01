using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;
using RadioV2.ViewModels;

namespace RadioV2.Controls;

public partial class MiniPlayer : System.Windows.Controls.UserControl
{
    public MiniPlayer()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged old)
            old.PropertyChanged -= OnVmPropertyChanged;
        if (e.NewValue is INotifyPropertyChanged vm)
            vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MiniPlayerViewModel.IsPlaying))
            Dispatcher.Invoke(() => ((Storyboard)Resources["PlayPulse"]).Begin(PlayPauseBtn, true));
    }

    private void FavouriteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MiniPlayerViewModel vm)
            vm.ToggleFavouriteCommand.Execute(null);
    }
}
