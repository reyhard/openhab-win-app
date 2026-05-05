using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace OpenHab.Windows.Tray.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon notifyIcon;
    private readonly ContextMenuStrip contextMenu;
    private readonly EventHandler doubleClickHandler;
    private int isDisposed;

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

        doubleClickHandler = (_, _) => showWindow();
        notifyIcon.DoubleClick += doubleClickHandler;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        notifyIcon.DoubleClick -= doubleClickHandler;
        notifyIcon.Visible = false;
        notifyIcon.ContextMenuStrip = null;
        notifyIcon.Dispose();
        contextMenu.Dispose();
    }
}
