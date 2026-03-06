using RadioV2.ViewModels;
using System.Drawing;
using System.Windows.Forms;

namespace RadioV2.Helpers;

public class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly MiniPlayerViewModel _miniPlayer;
    private readonly ToolStripMenuItem _stationItem;
    private readonly ToolStripMenuItem _playPauseItem;

    public TrayIconManager(MiniPlayerViewModel miniPlayer, Action showWindowAction, Action quitAction)
    {
        _miniPlayer = miniPlayer;

        _stationItem = new ToolStripMenuItem("RadioV2") { Enabled = false };
        _playPauseItem = new ToolStripMenuItem("Play / Pause", null,
            (s, e) => miniPlayer.PlayPauseCommand.Execute(null));

        var menu = new ContextMenuStrip();
        menu.Items.Add(_stationItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_playPauseItem);
        menu.Items.Add(new ToolStripMenuItem("Next Station", null,
            (s, e) => miniPlayer.NextStationCommand.Execute(null)));
        menu.Items.Add(new ToolStripMenuItem("Previous Station", null,
            (s, e) => miniPlayer.PreviousStationCommand.Execute(null)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Show Window", null,
            (s, e) => showWindowAction()));
        menu.Items.Add(new ToolStripMenuItem("Quit", null,
            (s, e) => quitAction()));

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "RadioV2",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (s, e) => showWindowAction();

        // Keep tray label in sync with now-playing state
        miniPlayer.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(MiniPlayerViewModel.StationName) or nameof(MiniPlayerViewModel.NowPlayingDisplay))
                UpdateLabel();
        };
    }

    private void UpdateLabel()
    {
        var name = _miniPlayer.StationName;
        var nowPlaying = _miniPlayer.NowPlayingDisplay;

        var label = string.IsNullOrEmpty(nowPlaying) ? name : $"{name} — {nowPlaying}";
        // NotifyIcon.Text max length is 63 chars
        _notifyIcon.Text = label.Length > 63 ? label[..63] : label;
        _stationItem.Text = string.IsNullOrEmpty(name) ? "RadioV2" : name;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
