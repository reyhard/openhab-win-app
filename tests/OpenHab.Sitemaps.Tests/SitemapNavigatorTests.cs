using OpenHab.Sitemaps.Models;
using OpenHab.Sitemaps.Runtime;

namespace OpenHab.Sitemaps.Tests;

public sealed class SitemapNavigatorTests
{
    [Fact]
    public void ConstructorThrowsWhenRootPageIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new SitemapNavigator(null!));
    }

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
    public void BackAtRootReturnsFalseAndKeepsCurrentPage()
    {
        var root = new SitemapPage("root", "Home", []);
        var navigator = new SitemapNavigator(root);

        var moved = navigator.Back();

        Assert.False(moved);
        Assert.Equal(root, navigator.CurrentPage);
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
    public void ActivateWidgetThrowsForNegativeIndex()
    {
        var root = new SitemapPage("root", "Home", []);
        var navigator = new SitemapNavigator(root);

        Assert.Throws<ArgumentOutOfRangeException>(() => navigator.ActivateWidget(-1));
    }

    [Fact]
    public void ActivateWidgetThrowsForTooLargeIndex()
    {
        var root = new SitemapPage("root", "Home", [
            new SitemapWidget("Light", SitemapWidgetType.Switch, "Light", "OFF", [], true, [])
        ]);
        var navigator = new SitemapNavigator(root);

        Assert.Throws<ArgumentOutOfRangeException>(() => navigator.ActivateWidget(1));
    }

    [Fact]
    public void ActivateWidgetThrowsWhenCurrentPageWidgetsIsNull()
    {
        var root = new SitemapPage("root", "Home", null!);
        var navigator = new SitemapNavigator(root);

        Assert.Throws<InvalidOperationException>(() => navigator.ActivateWidget(0));
    }

    [Theory]
    [InlineData(SitemapWidgetType.Mapview)]
    [InlineData(SitemapWidgetType.Video)]
    public void NativeMediaWidgetCreatesNoOpIntent(SitemapWidgetType type)
    {
        var root = new SitemapPage("root", "Home", [
            new SitemapWidget("Media", type, null, null, [], true, [])
        ]);
        var navigator = new SitemapNavigator(root);

        var intent = navigator.ActivateWidget(0);

        Assert.Equal(new NoOpIntent(), intent);
    }

    [Fact]
    public void SupportedNativeWidgetCreatesNoOpIntent()
    {
        var root = new SitemapPage("root", "Home", [
            new SitemapWidget("Chart", SitemapWidgetType.Chart, null, null, [], true, [])
        ]);
        var navigator = new SitemapNavigator(root);

        var intent = navigator.ActivateWidget(0);

        Assert.Equal(new NoOpIntent(), intent);
    }

    [Fact]
    public void NonSwitchNonFallbackNonNavigableWidgetCreatesNoOpIntent()
    {
        var root = new SitemapPage("root", "Home", [
            new SitemapWidget("Image", SitemapWidgetType.Image, null, null, [], true, [])
        ]);
        var navigator = new SitemapNavigator(root);

        var intent = navigator.ActivateWidget(0);

        Assert.Equal(new NoOpIntent(), intent);
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

    [Fact]
    public void SwitchWidgetWithOnStateCreatesOffCommandIntent()
    {
        var root = new SitemapPage("root", "Home", [
            new SitemapWidget("Light", SitemapWidgetType.Switch, "Light", "ON", [], true, [])
        ]);
        var navigator = new SitemapNavigator(root);

        var intent = navigator.ActivateWidget(0);

        Assert.Equal(new SendCommandIntent("Light", "OFF"), intent);
    }

    [Fact]
    public void SwitchWidgetWithFormattedStateUsesRawItemStateForCommandIntent()
    {
        var root = new SitemapPage("root", "Home", [
            new SitemapWidget(
                "Lock",
                SitemapWidgetType.Switch,
                "FrontDoor_Lock",
                "UNLOCKED",
                [],
                true,
                [],
                RawItemState: "OFF")
        ]);
        var navigator = new SitemapNavigator(root);

        var intent = navigator.ActivateWidget(0);

        Assert.Equal(new SendCommandIntent("FrontDoor_Lock", "ON"), intent);
    }

    [Fact]
    public void SwitchWidgetWithNullItemNameCreatesNoOpIntent()
    {
        var root = new SitemapPage("root", "Home", [
            new SitemapWidget("Light", SitemapWidgetType.Switch, null, "OFF", [], true, [])
        ]);
        var navigator = new SitemapNavigator(root);

        var intent = navigator.ActivateWidget(0);

        Assert.Equal(new NoOpIntent(), intent);
    }
}
