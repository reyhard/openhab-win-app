using OpenHab.App.Runtime;
using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Sitemaps.Models;
using OpenHab.Sitemaps.Runtime;
using System.IO;

namespace OpenHab.App.Tests.Runtime;

public sealed class SitemapSearchDescriptorBuilderTests
{
    private readonly SitemapRenderController renderController;

    public SitemapSearchDescriptorBuilderTests()
    {
        var settings = new AppSettingsController(settingsFilePath: Path.Combine(
            Path.GetTempPath(),
            "OpenHab.App.Tests",
            Guid.NewGuid().ToString("N"),
            "settings.json"));
        renderController = new SitemapRenderController(settings);
    }

    [Fact]
    public void EmptyOrWhitespaceQueryReturnsNormalDescriptorAndInactiveSearch()
    {
        var page = CreateSearchPage();
        var normal = renderController.BuildCurrentDescriptor(page);

        var result = SitemapSearchDescriptorBuilder.Build(page, normal, "   ", renderController);

        Assert.False(result.IsSearchActive);
        Assert.Equal(string.Empty, result.Query);
        Assert.Equal(0, result.ResultCount);
        Assert.Equal(normal, result.Descriptor);
        Assert.Empty(result.SourcesByResultKey);
    }

    [Fact]
    public void QueryMatchesVisibleLabelsOnly()
    {
        var page = CreateSearchPage();
        var normal = renderController.BuildCurrentDescriptor(page);

        var result = SitemapSearchDescriptorBuilder.Build(page, normal, "lampka", renderController);

        Assert.True(result.IsSearchActive);
        Assert.Equal(2, result.ResultCount);
        Assert.Contains(result.Descriptor.Rows, row => row.Label == "Lampka nocna");
        Assert.Contains(result.Descriptor.Rows, row => row.Label == "Lampka mobilna");
        Assert.DoesNotContain(result.Descriptor.Rows, row => row.Label == "Hidden Lampka");
    }

    [Fact]
    public void QueryIgnoresItemNamesAndStateValues()
    {
        var page = CreateSearchPage();
        var normal = renderController.BuildCurrentDescriptor(page);

        var byItem = SitemapSearchDescriptorBuilder.Build(page, normal, "Kitchen_Light", renderController);
        var byState = SitemapSearchDescriptorBuilder.Build(page, normal, "22.5", renderController);

        Assert.True(byItem.IsSearchActive);
        Assert.True(byState.IsSearchActive);
        Assert.Equal(0, byItem.ResultCount);
        Assert.Equal(0, byState.ResultCount);
        Assert.Contains(byItem.Descriptor.Rows, r => r.Label == "No matching sitemap elements");
        Assert.Contains(byState.Descriptor.Rows, r => r.Label == "No matching sitemap elements");
    }

    [Fact]
    public void HiddenWidgetsAreExcluded()
    {
        var page = CreateSearchPage();
        var normal = renderController.BuildCurrentDescriptor(page);

        var result = SitemapSearchDescriptorBuilder.Build(page, normal, "Hidden", renderController);

        Assert.Equal(0, result.ResultCount);
        Assert.DoesNotContain(result.Descriptor.Rows, row => row.Label == "Hidden Lampka");
        Assert.DoesNotContain(result.Descriptor.Rows, row => row.Label == "Hidden Child");
    }

    [Fact]
    public void FrameLabelMatchIncludesFrameAndAllVisibleChildren()
    {
        var page = CreateSearchPage();
        var normal = renderController.BuildCurrentDescriptor(page);

        var result = SitemapSearchDescriptorBuilder.Build(page, normal, "Automatyka", renderController);

        Assert.True(result.IsSearchActive);
        Assert.Contains(result.Descriptor.Rows, row => row.Label == "Automatyka swiatel");
        Assert.Contains(result.Descriptor.Rows, row => row.Label == "Tryb lampki");
        Assert.Contains(result.Descriptor.Rows, row => row.Label == "Timer");
        Assert.DoesNotContain(result.Descriptor.Rows, row => row.Label == "Hidden Child");
        Assert.Equal(3, result.ResultCount);
    }

    [Fact]
    public void ChildPageOnlyMatchIncludesGroupingContextAndSourceMetadata()
    {
        var page = CreateSearchPage();
        var normal = renderController.BuildCurrentDescriptor(page);

        var result = SitemapSearchDescriptorBuilder.Build(page, normal, "Timer", renderController);

        Assert.True(result.IsSearchActive);
        Assert.Equal(1, result.ResultCount);

        var timerRow = Assert.Single(result.Descriptor.Rows, row => row.Label == "Timer");
        Assert.NotNull(timerRow.SearchResultKey);
        Assert.Equal("automation", timerRow.SourcePageId);
        Assert.Equal("timer-widget", timerRow.SourceWidgetId);
        Assert.True(result.SourcesByResultKey.ContainsKey(timerRow.SearchResultKey!));

        Assert.Contains(result.Descriptor.Rows, row => row.IsSectionHeader && row.Label == "Automatyka swiatel");
    }

    [Fact]
    public void RecomputedResultsFollowLatestSitemapOrder()
    {
        var first = CreateSearchPage(lampsReversed: false);
        var firstNormal = renderController.BuildCurrentDescriptor(first);
        var firstResult = SitemapSearchDescriptorBuilder.Build(first, firstNormal, "Lampka", renderController);
        var firstLabels = firstResult.Descriptor.Rows
            .Where(row => row.Label.StartsWith("Lampka", StringComparison.Ordinal))
            .Select(row => row.Label)
            .ToArray();

        var second = CreateSearchPage(lampsReversed: true);
        var secondNormal = renderController.BuildCurrentDescriptor(second);
        var secondResult = SitemapSearchDescriptorBuilder.Build(second, secondNormal, "Lampka", renderController);
        var secondLabels = secondResult.Descriptor.Rows
            .Where(row => row.Label.StartsWith("Lampka", StringComparison.Ordinal))
            .Select(row => row.Label)
            .ToArray();

        Assert.Equal(["Lampka nocna", "Lampka mobilna"], firstLabels);
        Assert.Equal(["Lampka mobilna", "Lampka nocna"], secondLabels);
    }

    [Fact]
    public void DuplicateWidgetIdsInDifferentChildPagesKeepDistinctResultSources()
    {
        var page = CreateDuplicateWidgetIdPage();
        var normal = renderController.BuildCurrentDescriptor(page);

        var result = SitemapSearchDescriptorBuilder.Build(page, normal, "Timer", renderController);
        var timerRows = result.Descriptor.Rows
            .Where(row => row.Label == "Timer")
            .ToArray();

        Assert.Equal(2, result.ResultCount);
        Assert.Equal(2, timerRows.Length);
        Assert.Equal(2, timerRows.Select(row => row.SearchResultKey).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(result.ResultCount, result.SourcesByResultKey.Count);
        Assert.All(timerRows, row => Assert.True(result.SourcesByResultKey.ContainsKey(row.SearchResultKey!)));
    }

    private static NormalizedSitemapPage CreateSearchPage(bool lampsReversed = false)
    {
        var lampNight = new SitemapWidget(
            "Lampka nocna",
            SitemapWidgetType.Switch,
            "Bedroom_Lamp",
            "OFF",
            [],
            true,
            [],
            WidgetId: "lamp-night");

        var lampMobile = new SitemapWidget(
            "Lampka mobilna",
            SitemapWidgetType.Switch,
            "Mobile_Lamp",
            "OFF",
            [],
            true,
            [],
            WidgetId: "lamp-mobile");

        var lamps = lampsReversed
            ? new[] { lampMobile, lampNight }
            : [lampNight, lampMobile];

        var rootWidgets = new List<SitemapWidget>(lamps)
        {
            new(
                "Hidden Lampka",
                SitemapWidgetType.Text,
                "Hidden_Lamp",
                "ON",
                [],
                false,
                []),
            new(
                "Desk",
                SitemapWidgetType.Text,
                "Kitchen_Light",
                "22.5",
                [],
                true,
                []),
            new(
                "Automatyka swiatel",
                SitemapWidgetType.Frame,
                null,
                null,
                [],
                true,
                [
                    new SitemapPage(
                        "automation",
                        "Automatyka swiatel",
                        [
                            new SitemapWidget(
                                "Tryb lampki",
                                SitemapWidgetType.Text,
                                "Lamp_Mode",
                                "AUTO",
                                [],
                                true,
                                [],
                                WidgetId: "mode-widget"),
                            new SitemapWidget(
                                "Timer",
                                SitemapWidgetType.Text,
                                "Lamp_Timer",
                                "10",
                                [],
                                true,
                                [],
                                WidgetId: "timer-widget"),
                            new SitemapWidget(
                                "Hidden Child",
                                SitemapWidgetType.Text,
                                "Hidden_Child",
                                "ON",
                                [],
                                false,
                                [])
                        ])
                ],
                WidgetId: "automation-frame")
        };

        return SitemapNormalizer.Normalize(new SitemapPage("home", "Home", rootWidgets));
    }

    private static NormalizedSitemapPage CreateDuplicateWidgetIdPage()
    {
        return SitemapNormalizer.Normalize(new SitemapPage(
            "home",
            "Home",
            [
                new SitemapWidget(
                    "First group",
                    SitemapWidgetType.Group,
                    null,
                    null,
                    [],
                    true,
                    [
                        new SitemapPage("first", "First", [
                            new SitemapWidget(
                                "Timer",
                                SitemapWidgetType.Text,
                                "First_Timer",
                                "10",
                                [],
                                true,
                                [],
                                WidgetId: "timer-widget")
                        ])
                    ],
                    WidgetId: "first-group"),
                new SitemapWidget(
                    "Second group",
                    SitemapWidgetType.Group,
                    null,
                    null,
                    [],
                    true,
                    [
                        new SitemapPage("second", "Second", [
                            new SitemapWidget(
                                "Timer",
                                SitemapWidgetType.Text,
                                "Second_Timer",
                                "20",
                                [],
                                true,
                                [],
                                WidgetId: "timer-widget")
                        ])
                    ],
                    WidgetId: "second-group")
            ]));
    }
}
