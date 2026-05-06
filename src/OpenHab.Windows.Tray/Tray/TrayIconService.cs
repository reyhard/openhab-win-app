using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace OpenHab.Windows.Tray.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon notifyIcon;
    private readonly ContextMenuStrip contextMenu;
    private readonly MouseEventHandler mouseClickHandler;
    private int isDisposed;

    public TrayIconService(Action toggleFlyout, Action openMainWindow, Action exitApplication)
    {
        ArgumentNullException.ThrowIfNull(toggleFlyout);
        ArgumentNullException.ThrowIfNull(openMainWindow);
        ArgumentNullException.ThrowIfNull(exitApplication);

        contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Open flyout", image: null, (_, _) => toggleFlyout());
        contextMenu.Items.Add("Open main window", image: null, (_, _) => openMainWindow());
        contextMenu.Items.Add("Exit", image: null, (_, _) => exitApplication());

        notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "openHAB",
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        notifyIcon.BalloonTipClicked += (_, _) => openMainWindow();

        mouseClickHandler = (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                toggleFlyout();
            }
        };
        notifyIcon.MouseClick += mouseClickHandler;
    }

    /// <summary>
    /// Shows a balloon tip notification next to the tray icon.
    /// Works on all Windows configurations — no MSIX or COM registration required.
    /// </summary>
    public void ShowBalloon(string title, string text)
    {
        if (isDisposed != 0) return;
        notifyIcon.ShowBalloonTip(5000, title, text, ToolTipIcon.None);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        notifyIcon.MouseClick -= mouseClickHandler;
        notifyIcon.Visible = false;
        notifyIcon.ContextMenuStrip = null;
        notifyIcon.Dispose();
        contextMenu.Dispose();
    }
}
