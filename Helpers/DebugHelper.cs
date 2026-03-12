namespace RadioV2.Helpers;

public static class DebugHelper
{
#if DEBUG
    public static bool IsDebugBuild => true;
#else
    public static bool IsDebugBuild => false;
#endif
}
