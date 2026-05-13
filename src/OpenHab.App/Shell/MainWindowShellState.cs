namespace OpenHab.App.Shell;

public enum MainWindowCenterPage
{
    MainUi,
    Notifications,
    Settings,
    Diagnostics
}

public sealed record MainWindowShellState(
    MainWindowCenterPage CenterPage,
    bool IsSitemapVisible,
    string? PendingMainUiRoute)
{
    public static MainWindowShellState Initial(bool sitemapVisible) =>
        new(MainWindowCenterPage.MainUi, sitemapVisible, "/");
}
