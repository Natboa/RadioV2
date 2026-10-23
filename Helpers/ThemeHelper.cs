using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace RadioV2.Helpers;

public static class ThemeHelper
{
    /// <summary>
    /// The app's accent, seeded into WPF-UI's Fluent accent ramp.
    /// <para>
    /// Everything else in RadioV2 paints from Fluent semantic brushes, which is
    /// what keeps Mica and light/dark correct. The accent is the one deliberate
    /// exception: left on the Windows default it was whatever blue the user
    /// happened to have set, which matched nothing — least of all the genre
    /// artwork, which is the most distinctive thing on screen. This magenta is
    /// taken from that artwork, so the chrome and the art finally share a
    /// palette instead of fighting. The favourite heart is white rather than
    /// red for the same reason: against a magenta accent a red heart reads as
    /// a second, competing signal.
    /// </para>
    /// <para>
    /// To go back to following the Windows accent, delete the
    /// <see cref="ApplicationAccentColorManager"/> call below and pass
    /// <c>updateAccent: true</c>.
    /// </para>
    /// </summary>
    private static readonly System.Windows.Media.Color ArtworkMagenta =
        System.Windows.Media.Color.FromRgb(0xE8, 0x1C, 0x9B);

    public static void ApplyTheme(string theme)
    {
        var appTheme = theme switch
        {
            "Light" => ApplicationTheme.Light,
            _       => ApplicationTheme.Dark
        };

        // updateAccent: false — the next call owns the accent. Leaving this true
        // would pull the system accent back in and overwrite the ramp.
        ApplicationThemeManager.Apply(appTheme, WindowBackdropType.Mica, updateAccent: false);

        // Derives the primary/secondary/tertiary accent shades for this theme,
        // so the magenta stays legible in light mode instead of being used raw.
        ApplicationAccentColorManager.Apply(ArtworkMagenta, appTheme, systemGlassColor: false);
    }
}
