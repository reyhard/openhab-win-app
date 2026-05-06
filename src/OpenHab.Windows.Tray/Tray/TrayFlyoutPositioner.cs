using Microsoft.UI.Windowing;
using System.Windows.Forms;
using Windows.Graphics;

namespace OpenHab.Windows.Tray.Tray;

public static class TrayFlyoutPositioner
{
    private const int DefaultFlyoutWidth = 500;
    private const int DefaultFlyoutHeight = 560;
    private const int ScreenPadding = 8;

    public static void PlaceNearTrayArea(FlyoutWindow flyoutWindow)
    {
        ArgumentNullException.ThrowIfNull(flyoutWindow);

        var appWindow = flyoutWindow.AppWindow;
        var width = appWindow.Size.Width > 0 ? appWindow.Size.Width : DefaultFlyoutWidth;
        var height = appWindow.Size.Height > 0 ? appWindow.Size.Height : DefaultFlyoutHeight;
        var placement = CalculatePlacement(width, height);

        appWindow.MoveAndResize(new RectInt32(placement.X, placement.Y, placement.Width, placement.Height));
    }

    public static TrayFlyoutPlacement CalculatePlacement(int flyoutWidth, int flyoutHeight)
    {
        var workArea = Screen.PrimaryScreen!.WorkingArea;

        var maxWidth = Math.Max(1, workArea.Width - (ScreenPadding * 2));
        var maxHeight = Math.Max(1, workArea.Height - (ScreenPadding * 2));
        var width = Math.Clamp(flyoutWidth, 1, maxWidth);
        var height = Math.Clamp(flyoutHeight, 1, maxHeight);

        // Position flyout at bottom-right of working area, near the taskbar tray area
        var x = workArea.Right - width - ScreenPadding;
        var y = workArea.Bottom - height - ScreenPadding;

        return new TrayFlyoutPlacement(x, y, width, height);
    }
}
