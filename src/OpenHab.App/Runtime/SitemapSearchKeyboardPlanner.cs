namespace OpenHab.App.Runtime;

public enum SitemapKeyboardInput
{
    Escape,
    GoBack,
    Other
}

public enum SitemapSearchKeyboardAction
{
    None,
    CloseSearch
}

public readonly record struct SitemapSearchKeyboardState(
    bool HasVisibleSearchChrome,
    bool IsRefreshing);

public static class SitemapSearchKeyboardPlanner
{
    public static SitemapSearchKeyboardAction Plan(
        SitemapKeyboardInput input,
        SitemapSearchKeyboardState state)
    {
        if (input == SitemapKeyboardInput.Escape && state.HasVisibleSearchChrome)
        {
            return SitemapSearchKeyboardAction.CloseSearch;
        }

        if (input == SitemapKeyboardInput.GoBack && state.HasVisibleSearchChrome && !state.IsRefreshing)
        {
            return SitemapSearchKeyboardAction.CloseSearch;
        }

        return SitemapSearchKeyboardAction.None;
    }
}
