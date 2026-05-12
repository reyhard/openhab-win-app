namespace OpenHab.App.Shell;

public sealed class MainWindowShellController
{
    public MainWindowShellController(bool initialSitemapVisible)
    {
        Current = MainWindowShellState.Initial(initialSitemapVisible);
    }

    public MainWindowShellState Current { get; private set; }

    public event EventHandler? Changed;

    public void SelectCenterPage(MainWindowCenterPage page)
    {
        Current = Current with
        {
            CenterPage = page,
            PendingMainUiRoute = page == MainWindowCenterPage.MainUi ? Current.PendingMainUiRoute : null
        };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SelectPromotedMainUiPage(string route)
    {
        var normalizedRoute = string.IsNullOrWhiteSpace(route) ? "/" : route.Trim();
        if (!normalizedRoute.StartsWith("/", StringComparison.Ordinal))
        {
            normalizedRoute = "/" + normalizedRoute;
        }

        Current = Current with
        {
            CenterPage = MainWindowCenterPage.MainUi,
            PendingMainUiRoute = normalizedRoute
        };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetSitemapVisible(bool visible)
    {
        if (Current.IsSitemapVisible == visible)
        {
            return;
        }

        Current = Current with { IsSitemapVisible = visible };
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
