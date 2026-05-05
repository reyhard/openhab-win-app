using System.Drawing;
using System.Windows.Forms;

namespace OpenHab.Windows.Tray.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon notifyIcon;
    private readonly ContextMenuStrip contextMenu;
    private bool isDisposed;

    public TrayIconService(Action showWindow, Action exitApplication)
    {
        ArgumentNullException.ThrowIfNull(showWindow);
        ArgumentNullException.ThrowIfNull(exitApplication);

        contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Open", image: null, (_, _) => showWindow());
        contextMenu.Items.Add("Exit", image: null, (_, _) => exitApplication());

        notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "openHAB",
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        notifyIcon.DoubleClick += (_, _) => showWindow();
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        notifyIcon.Visible = false;
        notifyIcon.ContextMenuStrip = null;
        notifyIcon.Dispose();
        contextMenu.Dispose();
    }
}
