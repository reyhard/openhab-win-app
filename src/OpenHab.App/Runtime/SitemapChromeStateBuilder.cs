namespace OpenHab.App.Runtime;

public sealed record SitemapChromeState(
    string Title,
    string StatusText,
    IReadOnlyList<string> Breadcrumbs,
    bool ShowBreadcrumbs,
    bool ShowSearch,
    string SearchText);

public static class SitemapChromeStateBuilder
{
    public static SitemapChromeState Build(
        SitemapRuntimeSnapshot snapshot,
        string configuredSitemapName,
        bool isSearchChromeOpen)
    {
        var title = ResolveRootTitle(snapshot, configuredSitemapName);
        var breadcrumbs = snapshot.Breadcrumbs.Count > 0
            ? snapshot.Breadcrumbs
            : [title];
        var showSearch = isSearchChromeOpen || snapshot.IsSearchActive;

        return new SitemapChromeState(
            title,
            snapshot.StatusText,
            breadcrumbs,
            ShowBreadcrumbs: !showSearch && breadcrumbs.Count > 1,
            ShowSearch: showSearch,
            SearchText: snapshot.SearchQuery);
    }

    private static string ResolveRootTitle(SitemapRuntimeSnapshot snapshot, string configuredSitemapName)
    {
        if (snapshot.Breadcrumbs.Count > 0)
        {
            return snapshot.Breadcrumbs[0];
        }

        if (!string.IsNullOrWhiteSpace(configuredSitemapName))
        {
            return configuredSitemapName;
        }

        return "openHAB";
    }
}
