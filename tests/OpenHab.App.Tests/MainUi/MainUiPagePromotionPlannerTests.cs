using System.Text.Json;
using OpenHab.App.MainUi;
using OpenHab.Core.Ui;

namespace OpenHab.App.Tests.MainUi;

public sealed class MainUiPagePromotionPlannerTests
{
    [Fact]
    public void PlanPromotedLinks_FiltersNonSidebarAndBlankUidPages()
    {
        var pages = new[]
        {
            Page("energy", "Energy", sidebar: true, order: "10", icon: "f7:bolt"),
            Page("hidden", "Hidden", sidebar: false, order: "20", icon: "f7:eye-slash"),
            Page("   ", "Blank", sidebar: true, order: "30", icon: "f7:question")
        };

        var links = MainUiPagePromotionPlanner.PlanPromotedLinks(pages);

        var link = Assert.Single(links);
        Assert.Equal("energy", link.Uid);
    }

    [Fact]
    public void PlanPromotedLinks_UsesTrimmedUidWhenLabelMissing()
    {
        var pages = new[] { Page("  energy  ", null, sidebar: true, order: null, icon: null) };

        var link = Assert.Single(MainUiPagePromotionPlanner.PlanPromotedLinks(pages));

        Assert.Equal("energy", link.Uid);
        Assert.Equal("energy", link.Label);
        Assert.Equal("/page/energy", link.Route);
    }

    [Fact]
    public void PlanPromotedLinks_EscapesUidInRoute()
    {
        var pages = new[] { Page("Floor Plan", "Floor Plan", sidebar: true, order: null, icon: null) };

        var link = Assert.Single(MainUiPagePromotionPlanner.PlanPromotedLinks(pages));

        Assert.Equal("/page/Floor%20Plan", link.Route);
    }

    [Fact]
    public void PlanPromotedLinks_PreservesRawIconTypeAndOrder()
    {
        var pages = new[] { Page("energy", "Energy", sidebar: true, order: "10", icon: "f7:bolt") };

        var link = Assert.Single(MainUiPagePromotionPlanner.PlanPromotedLinks(pages));

        Assert.Equal("f7:bolt", link.Icon);
        Assert.Equal("oh-layout-page", link.Type);
        Assert.Equal(10, link.Order);
    }

    [Fact]
    public void PlanPromotedLinks_SortsByOrderThenLabelThenUid()
    {
        var pages = new[]
        {
            Page("zeta", "Zeta", sidebar: true, order: "20", icon: null),
            Page("beta", "Beta", sidebar: true, order: "10", icon: null),
            Page("alpha", "alpha", sidebar: true, order: "10", icon: null),
            Page("omega", "Omega", sidebar: true, order: null, icon: null)
        };

        var links = MainUiPagePromotionPlanner.PlanPromotedLinks(pages);

        Assert.Collection(
            links,
            first => Assert.Equal("alpha", first.Uid),
            second => Assert.Equal("beta", second.Uid),
            third => Assert.Equal("zeta", third.Uid),
            fourth => Assert.Equal("omega", fourth.Uid));
    }

    [Fact]
    public void BuildPromotedLinks_DelegatesToPromotionPlanner()
    {
        var pages = new[]
        {
            Page("energy", "Energy", sidebar: true, order: "10", icon: "f7:bolt"),
            Page("hidden", "Hidden", sidebar: false, order: "20", icon: null)
        };

        var fromService = MainUiPageDiscoveryService.BuildPromotedLinks(pages);
        var fromPlanner = MainUiPagePromotionPlanner.PlanPromotedLinks(pages);

        Assert.Equal(fromPlanner, fromService);
    }

    private static MainUiPageComponent Page(string uid, string? label, bool sidebar, string? order, string? icon)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (label is not null)
        {
            values["label"] = JsonDocument.Parse(JsonSerializer.Serialize(label)).RootElement.Clone();
        }

        values["sidebar"] = JsonDocument.Parse(sidebar ? "true" : "false").RootElement.Clone();
        if (order is not null)
        {
            values["order"] = JsonDocument.Parse(JsonSerializer.Serialize(order)).RootElement.Clone();
        }

        if (icon is not null)
        {
            values["icon"] = JsonDocument.Parse(JsonSerializer.Serialize(icon)).RootElement.Clone();
        }

        return new MainUiPageComponent(uid, "oh-layout-page", values);
    }
}
