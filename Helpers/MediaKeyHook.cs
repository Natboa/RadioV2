using System.Windows.Input;
using System.Windows.Interop;

namespace RadioV2.Helpers;

public class MediaKeyHook
{
    private const int WM_APPCOMMAND = 0x0319;
    private const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14;
    private const int APPCOMMAND_MEDIA_STOP = 13;
    private const int APPCOMMAND_MEDIA_NEXTTRACK = 11;
    private const int APPCOMMAND_MEDIA_PREVIOUSTRACK = 12;

    public ICommand? PlayPauseCommand { get; set; }
    public ICommand? StopCommand { get; set; }
    public ICommand? NextStationCommand { get; set; }
    public ICommand? PreviousStationCommand { get; set; }

    public IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_APPCOMMAND)
        {
            var command = (int)(lParam.ToInt64() >> 16) & 0xFFF;
            switch (command)
            {
                case APPCOMMAND_MEDIA_PLAY_PAUSE:
                    if (PlayPauseCommand?.CanExecute(null) == true) PlayPauseCommand.Execute(null);
                    handled = true;
                    break;
                case APPCOMMAND_MEDIA_STOP:
                    if (StopCommand?.CanExecute(null) == true) StopCommand.Execute(null);
                    handled = true;
                    break;
                case APPCOMMAND_MEDIA_NEXTTRACK:
                    if (NextStationCommand?.CanExecute(null) == true) NextStationCommand.Execute(null);
                    handled = true;
                    break;
                case APPCOMMAND_MEDIA_PREVIOUSTRACK:
                    if (PreviousStationCommand?.CanExecute(null) == true) PreviousStationCommand.Execute(null);
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }
}
