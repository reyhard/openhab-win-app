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
        var next = Current with
        {
            CenterPage = page,
            PendingMainUiRoute = page == MainWindowCenterPage.MainUi ? Current.PendingMainUiRoute : null
        };

        if (EqualityComparer<MainWindowShellState>.Default.Equals(Current, next))
        {
            return;
        }

        Current = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SelectPromotedMainUiPage(string route)
    {
        var normalizedRoute = NormalizeRoute(route);

        var next = Current with
        {
            CenterPage = MainWindowCenterPage.MainUi,
            PendingMainUiRoute = normalizedRoute
        };

        if (EqualityComparer<MainWindowShellState>.Default.Equals(Current, next))
        {
            return;
        }

        Current = next;
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

    private static string NormalizeRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return "/";
        }

        var normalized = route.Trim();
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
        {
            normalized = "/" + normalized;
        }

        return normalized;
    }
}
