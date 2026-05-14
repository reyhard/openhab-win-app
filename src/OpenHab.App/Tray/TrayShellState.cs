namespace OpenHab.App.Tray;

public sealed record TrayShellState(
    TrayShellSurface VisibleSurface,
    bool IsRunningInBackground,
    bool PendingRefresh,
    long RefreshRequestVersion,
    bool ShouldExitProcess)
{
    public static TrayShellState Initial { get; } = new(
        TrayShellSurface.None,
        true,
        false,
        0,
        false);
}
