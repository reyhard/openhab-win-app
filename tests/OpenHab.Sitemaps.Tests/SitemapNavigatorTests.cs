using OpenHab.Sitemaps.Models;
using OpenHab.Sitemaps.Runtime;

namespace OpenHab.Sitemaps.Tests;

public sealed class SitemapNavigatorTests
{
    [Fact]
    public void NavigateToChildPushesChildPage()
    {
        var child = new SitemapPage("living", "Living Room", []);
        var root = new SitemapPage("root", "Home", [
            new SitemapWidget("Living Room", SitemapWidgetType.Text, null, null, [], true, [child])
        ]);
        var navigator = new SitemapNavigator(root);

        var intent = navigator.ActivateWidget(0);

        Assert.Equal(new NavigateIntent("living"), intent);
        Assert.Equal("Living Room", navigator.CurrentPage.Label);
    }

    [Fact]
    public void BackReturnsToPreviousPage()
    {
        var child = new SitemapPage("living", "Living Room", []);
        var root = new SitemapPage("root", "Home", [
            new SitemapWidget("Living Room", SitemapWidgetType.Text, null, null, [], true, [child])
        ]);
        var navigator = new SitemapNavigator(root);

        navigator.ActivateWidget(0);
        var moved = navigator.Back();

        Assert.True(moved);
        Assert.Equal("Home", navigator.CurrentPage.Label);
    }

    [Fact]
    public void SwitchWidgetCreatesSendCommandIntent()
    {
        var root = new SitemapPage("root", "Home", [
            new SitemapWidget("Light", SitemapWidgetType.Switch, "Light", "OFF", [], true, [])
        ]);
        var navigator = new SitemapNavigator(root);

        var intent = navigator.ActivateWidget(0);

        Assert.Equal(new SendCommandIntent("Light", "ON"), intent);
    }
}
