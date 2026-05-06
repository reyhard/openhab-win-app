namespace OpenHab.Windows.Tray.Tray;

public readonly record struct TrayFlyoutPlacement(int X, int Y, int Width, int Height)
{
    public static TrayFlyoutPlacement Empty { get; } = new(0, 0, 0, 0);
}
