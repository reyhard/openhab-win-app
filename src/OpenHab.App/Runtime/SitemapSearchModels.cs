using OpenHab.Rendering.Descriptors;
using OpenHab.Sitemaps.Models;

namespace OpenHab.App.Runtime;

public enum SitemapSearchMatchKind
{
    Row,
    Frame,
    ChildRow
}

public sealed record SitemapSearchSource(
    string ResultKey,
    SitemapSearchMatchKind MatchKind,
    bool IsContextual,
    string SourcePageId,
    IReadOnlyList<string> SourcePagePath,
    string SourceWidgetId,
    string SourceWidgetPath,
    string SourceWidgetLabel,
    string? SourceItemName,
    SitemapWidgetType SourceWidgetType,
    int? CurrentPageRowIndex);

public sealed record SitemapSearchBuildResult(
    SitemapRenderDescriptor Descriptor,
    bool IsSearchActive,
    string Query,
    int ResultCount,
    IReadOnlyDictionary<string, SitemapSearchSource> SourcesByResultKey);
