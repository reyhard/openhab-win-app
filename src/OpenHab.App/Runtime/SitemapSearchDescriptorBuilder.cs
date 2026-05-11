using System.Globalization;
using OpenHab.App.Sitemaps;
using OpenHab.Rendering.Descriptors;
using OpenHab.Sitemaps.Models;
using OpenHab.Sitemaps.Runtime;

namespace OpenHab.App.Runtime;

public static class SitemapSearchDescriptorBuilder
{
    private const string SearchPageId = "__search__";
    private const string SearchTitle = "Search results";
    private const string EmptyLabel = "No matching sitemap elements";

    public static SitemapSearchBuildResult Build(
        NormalizedSitemapPage currentPage,
        SitemapRenderDescriptor normalDescriptor,
        string? query,
        SitemapRenderController renderController)
    {
        ArgumentNullException.ThrowIfNull(currentPage);
        ArgumentNullException.ThrowIfNull(normalDescriptor);
        ArgumentNullException.ThrowIfNull(renderController);

        var trimmedQuery = (query ?? string.Empty).Trim();
        if (trimmedQuery.Length == 0)
        {
            return new SitemapSearchBuildResult(
                normalDescriptor,
                IsSearchActive: false,
                Query: string.Empty,
                ResultCount: 0,
                SourcesByResultKey: new Dictionary<string, SitemapSearchSource>());
        }

        var rows = new List<SitemapRowDescriptor>();
        var sources = new Dictionary<string, SitemapSearchSource>(StringComparer.Ordinal);
        var resultCount = 0;

        rows.Add(CreateHeaderRow(SearchTitle, "Searching current section and child pages"));

        for (var index = 0; index < currentPage.Widgets.Count; index++)
        {
            AddWidgetMatches(
                currentPage,
                normalDescriptor,
                currentPage,
                normalDescriptor,
                currentPage.Widgets[index],
                index,
                trimmedQuery,
                renderController,
                rows,
                sources,
                ref resultCount,
                pagePath: [currentPage.Id],
                widgetPathPrefix: currentPage.Id,
                parentNavigationLabel: null,
                currentPageRowIndex: index,
                forcedChildInclusion: false,
                inheritedFrameMatch: false);
        }

        if (resultCount == 0)
        {
            rows[0] = CreateHeaderRow(SearchTitle, "0 results in current section and child pages");
            rows.Add(new SitemapRowDescriptor(
                EmptyLabel,
                null,
                RenderControlKind.Text,
                RenderActionKind.None,
                RenderDensity.Comfortable,
                [],
                IsVisible: true));
        }
        else
        {
            rows[0] = CreateHeaderRow(
                SearchTitle,
                string.Create(CultureInfo.InvariantCulture, $"{resultCount} results in current section and child pages"));
        }

        var descriptor = new SitemapRenderDescriptor(normalDescriptor.Skin, SearchPageId, SearchTitle, rows);
        return new SitemapSearchBuildResult(descriptor, true, trimmedQuery, resultCount, sources);
    }

    private static bool AddWidgetMatches(
        NormalizedSitemapPage rootPage,
        SitemapRenderDescriptor rootDescriptor,
        NormalizedSitemapPage page,
        SitemapRenderDescriptor pageDescriptor,
        NormalizedSitemapWidget widget,
        int widgetIndex,
        string query,
        SitemapRenderController renderController,
        List<SitemapRowDescriptor> rows,
        Dictionary<string, SitemapSearchSource> sources,
        ref int resultCount,
        IReadOnlyList<string> pagePath,
        string widgetPathPrefix,
        string? parentNavigationLabel,
        int? currentPageRowIndex,
        bool forcedChildInclusion,
        bool inheritedFrameMatch)
    {
        if (!widget.IsVisible)
        {
            return false;
        }

        var labelMatches = LabelMatches(widget.Label, query);
        var frameMatch = widget.Type == SitemapWidgetType.Frame && labelMatches;
        var includeSelf = forcedChildInclusion || labelMatches || inheritedFrameMatch;
        var hasAnyDescendantIncluded = false;
        var insertedGroupHeader = false;

        if (includeSelf)
        {
            var baseRow = pageDescriptor.Rows[widgetIndex];
            var resultKey = BuildResultKey(page, widget, widgetIndex);
            var source = BuildSource(
                resultKey,
                widget,
                page,
                pagePath,
                widgetPathPrefix,
                widgetIndex,
                currentPageRowIndex,
                frameMatch ? SitemapSearchMatchKind.Frame : inheritedFrameMatch ? SitemapSearchMatchKind.ChildRow : SitemapSearchMatchKind.Row);
            var searchRow = baseRow with
            {
                SearchResultKey = resultKey,
                SourcePageId = page.Id,
                SourceWidgetId = widget.WidgetId
            };

            rows.Add(searchRow);
            sources[resultKey] = source;
            resultCount++;
        }

        for (var childPageIndex = 0; childPageIndex < widget.Children.Count; childPageIndex++)
        {
            var normalizedChildPage = SitemapNormalizer.Normalize(widget.Children[childPageIndex]);
            var childDescriptor = renderController.BuildCurrentDescriptor(normalizedChildPage);
            var childPagePath = Append(pagePath, normalizedChildPage.Id);
            var childWidgetPathPrefix = widgetPathPrefix + "/child:" + childPageIndex;

            var childRowsStart = rows.Count;
            var anyIncludedFromChildPage = false;
            for (var childWidgetIndex = 0; childWidgetIndex < normalizedChildPage.Widgets.Count; childWidgetIndex++)
            {
                var childIncluded = AddWidgetMatches(
                    rootPage,
                    rootDescriptor,
                    normalizedChildPage,
                    childDescriptor,
                    normalizedChildPage.Widgets[childWidgetIndex],
                    childWidgetIndex,
                    query,
                    renderController,
                    rows,
                    sources,
                    ref resultCount,
                    childPagePath,
                    childWidgetPathPrefix,
                    parentNavigationLabel: widget.Label,
                    currentPageRowIndex: null,
                    forcedChildInclusion: frameMatch || inheritedFrameMatch,
                    inheritedFrameMatch: frameMatch || inheritedFrameMatch);

                anyIncludedFromChildPage |= childIncluded;
            }

            if (!anyIncludedFromChildPage)
            {
                continue;
            }

            hasAnyDescendantIncluded = true;

            if (!frameMatch && !inheritedFrameMatch && !insertedGroupHeader)
            {
                rows.Insert(childRowsStart, CreateHeaderRow(parentNavigationLabel ?? widget.Label, null));
                insertedGroupHeader = true;
            }
        }

        return includeSelf || hasAnyDescendantIncluded;
    }

    private static SitemapSearchSource BuildSource(
        string resultKey,
        NormalizedSitemapWidget widget,
        NormalizedSitemapPage page,
        IReadOnlyList<string> pagePath,
        string widgetPathPrefix,
        int widgetIndex,
        int? currentPageRowIndex,
        SitemapSearchMatchKind matchKind)
    {
        return new SitemapSearchSource(
            resultKey,
            matchKind,
            page.Id,
            pagePath,
            widget.WidgetId,
            BuildWidgetPath(widgetPathPrefix, widget, widgetIndex),
            widget.Label,
            widget.Type,
            currentPageRowIndex);
    }

    private static string BuildResultKey(NormalizedSitemapPage page, NormalizedSitemapWidget widget, int index)
    {
        if (!string.IsNullOrWhiteSpace(widget.WidgetId))
        {
            return "search:widget:" + widget.WidgetId;
        }

        var fallback = string.Join(
            "|",
            "page=" + page.Id,
            "type=" + widget.Type,
            "label=" + (widget.Label ?? string.Empty),
            "item=" + (widget.ItemName ?? string.Empty),
            "sig=" + BuildActionSignature(widget),
            "index=" + index.ToString(CultureInfo.InvariantCulture));

        return "search:path:" + fallback;
    }

    private static string BuildWidgetPath(string prefix, NormalizedSitemapWidget widget, int index)
    {
        return string.Join(
            "/",
            prefix,
            "idx:" + index.ToString(CultureInfo.InvariantCulture),
            "type:" + widget.Type,
            "label:" + (widget.Label ?? string.Empty));
    }

    private static string BuildActionSignature(NormalizedSitemapWidget widget)
    {
        return string.Join(
            ";",
            widget.Command ?? string.Empty,
            widget.ReleaseCommand ?? string.Empty,
            widget.Url ?? string.Empty,
            widget.Period ?? string.Empty,
            widget.Service ?? string.Empty,
            widget.InputHint.ToString());
    }

    private static SitemapRowDescriptor CreateHeaderRow(string label, string? state)
    {
        return new SitemapRowDescriptor(
            label,
            state,
            RenderControlKind.Text,
            RenderActionKind.None,
            RenderDensity.Comfortable,
            [],
            IsSectionHeader: true,
            IsVisible: true);
    }

    private static bool LabelMatches(string label, string query)
    {
        return label.Contains(query, StringComparison.InvariantCultureIgnoreCase);
    }

    private static string[] Append(IReadOnlyList<string> path, string segment)
    {
        var next = new string[path.Count + 1];
        for (var i = 0; i < path.Count; i++)
        {
            next[i] = path[i];
        }

        next[path.Count] = segment;
        return next;
    }
}
