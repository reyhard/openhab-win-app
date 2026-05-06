using Microsoft.UI.Windowing;
using System.Drawing;
using System.Windows.Forms;
using Windows.Graphics;

namespace OpenHab.Windows.Tray.Tray;

public static class TrayFlyoutPositioner
{
    private const int DefaultFlyoutWidth = 420;
    private const int DefaultFlyoutHeight = 560;
    private const int ScreenPadding = 8;
    private const int CursorOffset = 12;

    public static void PlaceNearTrayArea(FlyoutWindow flyoutWindow)
    {
        ArgumentNullException.ThrowIfNull(flyoutWindow);

        var appWindow = flyoutWindow.AppWindow;
        var width = appWindow.Size.Width > 0 ? appWindow.Size.Width : DefaultFlyoutWidth;
        var height = appWindow.Size.Height > 0 ? appWindow.Size.Height : DefaultFlyoutHeight;
        var placement = CalculatePlacement(Cursor.Position, width, height);

        appWindow.MoveAndResize(new RectInt32(placement.X, placement.Y, placement.Width, placement.Height));
    }

    public static TrayFlyoutPlacement CalculatePlacement(Point cursorPosition, int flyoutWidth, int flyoutHeight)
    {
        var screen = Screen.FromPoint(cursorPosition);
        var workArea = screen.WorkingArea;

        var availableLeft = workArea.Left + ScreenPadding;
        var availableTop = workArea.Top + ScreenPadding;
        var maxWidth = Math.Max(1, workArea.Width - (ScreenPadding * 2));
        var maxHeight = Math.Max(1, workArea.Height - (ScreenPadding * 2));
        var width = Math.Clamp(flyoutWidth, 1, maxWidth);
        var height = Math.Clamp(flyoutHeight, 1, maxHeight);
        var availableRight = availableLeft + (maxWidth - width);
        var availableBottom = availableTop + (maxHeight - height);

        var preferAbove = cursorPosition.Y > workArea.Top + (workArea.Height / 2);
        var x = cursorPosition.X - width + CursorOffset;
        var y = preferAbove
            ? cursorPosition.Y - height - CursorOffset
            : cursorPosition.Y + CursorOffset;

        if (availableRight < availableLeft)
        {
            x = availableLeft;
        }
        else
        {
            x = Math.Clamp(x, availableLeft, availableRight);
        }

        if (availableBottom < availableTop)
        {
            y = availableTop;
        }
        else
        {
            y = Math.Clamp(y, availableTop, availableBottom);
        }

        return new TrayFlyoutPlacement(x, y, width, height);
    }
}
