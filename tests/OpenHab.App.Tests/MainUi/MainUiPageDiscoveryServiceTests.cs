using System.Text.Json;
using OpenHab.App.MainUi;
using OpenHab.Core.Ui;

namespace OpenHab.App.Tests.MainUi;

public sealed class MainUiPageDiscoveryServiceTests
{
    [Fact]
    public void BuildPromotedLinks_ReturnsOnlySidebarPagesSortedByOrder()
    {
        var pages = new[]
        {
            Page("security", "Security", sidebar: true, order: "30", icon: "f7:shield"),
            Page("hidden", "Hidden", sidebar: false, order: "10", icon: null),
            Page("energy", "Energy", sidebar: true, order: "10", icon: "f7:bolt")
        };

        var links = MainUiPageDiscoveryService.BuildPromotedLinks(pages);

        Assert.Collection(
            links,
            first =>
            {
                Assert.Equal("energy", first.Uid);
                Assert.Equal("Energy", first.Label);
                Assert.Equal("/page/energy", first.Route);
                Assert.Equal("f7:bolt", first.Icon);
                Assert.Equal(10, first.Order);
            },
            second =>
            {
                Assert.Equal("security", second.Uid);
                Assert.Equal("Security", second.Label);
                Assert.Equal("/page/security", second.Route);
                Assert.Equal("f7:shield", second.Icon);
                Assert.Equal(30, second.Order);
            });
    }

    [Fact]
    public void BuildPromotedLinks_UsesUidWhenLabelIsMissing()
    {
        var pages = new[] { Page("page_without_label", null, sidebar: true, order: null, icon: null) };

        var link = Assert.Single(MainUiPageDiscoveryService.BuildPromotedLinks(pages));

        Assert.Equal("page_without_label", link.Label);
        Assert.Equal("/page/page_without_label", link.Route);
    }

    [Fact]
    public void BuildPromotedLinks_EscapesUidInRoute()
    {
        var pages = new[] { Page("Floor Plan", "Floor Plan", sidebar: true, order: null, icon: null) };

        var link = Assert.Single(MainUiPageDiscoveryService.BuildPromotedLinks(pages));

        Assert.Equal("/page/Floor%20Plan", link.Route);
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
