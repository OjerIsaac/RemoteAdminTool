using System.Windows.Forms;

namespace RemoteAdminTool.Host;

/// <summary>
/// Keeps a visible tray icon at all times. This is a deliberate design choice, not
/// boilerplate: a remote-access tool should never run invisibly, even on a machine
/// you own. The icon/text changes when a viewer is actively connected.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;

    public TrayApplicationContext()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Shield,
            Visible = true,
            Text = "Remote Admin Host: idle (no viewer connected)"
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        _notifyIcon.ContextMenuStrip = menu;
    }

    public void SetConnectedState(bool isConnected)
    {
        _notifyIcon.Text = isConnected
            ? "Remote Admin Host: VIEWER CONNECTED"
            : "Remote Admin Host: idle (no viewer connected)";
    }

    private void ExitApp()
    {
        _notifyIcon.Visible = false;
        System.Windows.Forms.Application.Exit();
    }
}
