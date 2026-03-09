using System.Windows;
using Wpf.Ui.Appearance;

namespace RadioV2.DevTool;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        var window = new MainWindow();
        window.Show();
    }
}
