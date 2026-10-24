using System.Windows;

namespace RadioV2.Helpers;

public static class MotionPreferences
{
    public static bool Enabled => SystemParameters.ClientAreaAnimation;
}
