using System.Drawing;
using System.Windows.Forms;

namespace OpenHab.Windows.Tray.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon notifyIcon;

    public TrayIconService(Action showWindow, Action exitApplication)
    {
        ArgumentNullException.ThrowIfNull(showWindow);
        ArgumentNullException.ThrowIfNull(exitApplication);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", image: null, (_, _) => showWindow());
        menu.Items.Add("Exit", image: null, (_, _) => exitApplication());

        notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "openHAB",
            ContextMenuStrip = menu,
            Visible = true
        };

        notifyIcon.DoubleClick += (_, _) => showWindow();
    }

    public void Dispose()
    {
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
    }
}
