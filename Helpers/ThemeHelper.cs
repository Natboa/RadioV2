using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace RadioV2.Helpers;

public static class ThemeHelper
{
    public static void ApplyTheme(string theme)
    {
        var appTheme = theme switch
        {
            "Light" => ApplicationTheme.Light,
            _       => ApplicationTheme.Dark
        };
        ApplicationThemeManager.Apply(appTheme, WindowBackdropType.Mica, true);
    }
}
