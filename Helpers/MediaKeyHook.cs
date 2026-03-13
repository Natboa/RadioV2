using System.Runtime.InteropServices;
using System.Windows.Input;

namespace RadioV2.Helpers;

public class MediaKeyHook : IDisposable
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;

    private const int ID_PLAY_PAUSE = 9001;
    private const int ID_STOP       = 9002;
    private const int ID_NEXT       = 9003;
    private const int ID_PREV       = 9004;

    private const uint VK_MEDIA_PLAY_PAUSE  = 0xB3;
    private const uint VK_MEDIA_STOP        = 0xB2;
    private const uint VK_MEDIA_NEXT_TRACK  = 0xB0;
    private const uint VK_MEDIA_PREV_TRACK  = 0xB1;

    private const uint MOD_NOREPEAT = 0x4000;

    public ICommand? PlayPauseCommand    { get; set; }
    public ICommand? StopCommand         { get; set; }
    public ICommand? NextStationCommand  { get; set; }
    public ICommand? PreviousStationCommand { get; set; }

    private IntPtr _hwnd;
    private bool _registered;

    public void Register(IntPtr hwnd)
    {
        _hwnd = hwnd;
        RegisterHotKey(hwnd, ID_PLAY_PAUSE, MOD_NOREPEAT, VK_MEDIA_PLAY_PAUSE);
        RegisterHotKey(hwnd, ID_STOP,       MOD_NOREPEAT, VK_MEDIA_STOP);
        RegisterHotKey(hwnd, ID_NEXT,       MOD_NOREPEAT, VK_MEDIA_NEXT_TRACK);
        RegisterHotKey(hwnd, ID_PREV,       MOD_NOREPEAT, VK_MEDIA_PREV_TRACK);
        _registered = true;
    }

    public IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY) return IntPtr.Zero;

        switch (wParam.ToInt32())
        {
            case ID_PLAY_PAUSE:
                if (PlayPauseCommand?.CanExecute(null) == true) PlayPauseCommand.Execute(null);
                handled = true;
                break;
            case ID_STOP:
                if (StopCommand?.CanExecute(null) == true) StopCommand.Execute(null);
                handled = true;
                break;
            case ID_NEXT:
                if (NextStationCommand?.CanExecute(null) == true) NextStationCommand.Execute(null);
                handled = true;
                break;
            case ID_PREV:
                if (PreviousStationCommand?.CanExecute(null) == true) PreviousStationCommand.Execute(null);
                handled = true;
                break;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (!_registered || _hwnd == IntPtr.Zero) return;
        UnregisterHotKey(_hwnd, ID_PLAY_PAUSE);
        UnregisterHotKey(_hwnd, ID_STOP);
        UnregisterHotKey(_hwnd, ID_NEXT);
        UnregisterHotKey(_hwnd, ID_PREV);
    }
}
