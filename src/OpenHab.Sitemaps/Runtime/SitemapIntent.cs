namespace OpenHab.Sitemaps.Runtime;

public abstract record SitemapIntent;

public sealed record SendCommandIntent(string ItemName, string Command) : SitemapIntent;

public sealed record NavigateIntent(string PageId) : SitemapIntent;

public sealed record OpenFallbackIntent(string PageOrWidgetLabel) : SitemapIntent;

public sealed record NoOpIntent : SitemapIntent;
