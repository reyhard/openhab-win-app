namespace OpenHab.Core.Events;

public sealed record SitemapWidgetEvent(
    string WidgetId,
    string? Label,
    string? Icon,
    bool Visibility,
    string? ItemName,
    string? ItemState,
    string SitemapName,
    string PageId,
    bool DescriptionChanged);
