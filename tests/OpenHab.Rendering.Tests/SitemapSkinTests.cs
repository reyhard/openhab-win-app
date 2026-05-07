using OpenHab.Rendering.Descriptors;
using OpenHab.Rendering.Skins;
using OpenHab.Sitemaps.Models;

namespace OpenHab.Rendering.Tests;

public sealed class SitemapSkinTests
{
    [Theory]
    [InlineData(typeof(BasicSitemapSkin))]
    [InlineData(typeof(Windows11SitemapSkin))]
    public void SetpointMapsToSliderWithSendCommand(Type skinType)
    {
        var page = new NormalizedSitemapPage("root", "Home", [
            new NormalizedSitemapWidget("Target Temp", SitemapWidgetType.Setpoint, "Target Temp", "21", [], false, false, SitemapFallbackKind.None, [])
        ]);
        var skin = (ISitemapSkin)Activator.CreateInstance(skinType)!;

        var descriptor = skin.Render(page);
        var row = Assert.Single(descriptor.Rows);

        Assert.Equal(RenderControlKind.Slider, row.Control);
        Assert.Equal(RenderActionKind.SendCommand, row.Action);
    }

    [Fact]
    public void BasicAndWindows11UseSameNonDensityMappings()
    {
        var page = new NormalizedSitemapPage("root", "Home", [
            new NormalizedSitemapWidget("Light", SitemapWidgetType.Switch, "Light", "ON", [], false, false, SitemapFallbackKind.None, []),
            new NormalizedSitemapWidget("Dimmer", SitemapWidgetType.Slider, "Dimmer", "12", [], false, false, SitemapFallbackKind.None, []),
            new NormalizedSitemapWidget("Setpoint", SitemapWidgetType.Setpoint, "Setpoint", "20", [], false, false, SitemapFallbackKind.None, []),
            new NormalizedSitemapWidget("Mode", SitemapWidgetType.Selection, "Mode", "Home", [], false, false, SitemapFallbackKind.None, []),
            new NormalizedSitemapWidget("Chart", SitemapWidgetType.Chart, "Chart", "", [], false, false, SitemapFallbackKind.None, []),
            new NormalizedSitemapWidget("Camera", SitemapWidgetType.Video, "Camera", "", [], false, true, SitemapFallbackKind.MainUiOrBrowser, [])
        ]);

        var basic = new BasicSitemapSkin().Render(page);
        var w11 = new Windows11SitemapSkin().Render(page);

        Assert.Equal(basic.Rows.Count, w11.Rows.Count);

        for (var i = 0; i < basic.Rows.Count; i++)
        {
            Assert.Equal(basic.Rows[i].Label, w11.Rows[i].Label);
            Assert.Equal(basic.Rows[i].State, w11.Rows[i].State);
            Assert.Equal(basic.Rows[i].Control, w11.Rows[i].Control);
            Assert.Equal(basic.Rows[i].Action, w11.Rows[i].Action);
        }
    }

    [Theory]
    [InlineData(typeof(BasicSitemapSkin))]
    [InlineData(typeof(Windows11SitemapSkin))]
    public void NavigateActionTakesPrecedenceOverSendCommand(Type skinType)
    {
        var page = new NormalizedSitemapPage("root", "Home", [
            new NormalizedSitemapWidget("Target Temp", SitemapWidgetType.Setpoint, "Target Temp", "21", [], true, false, SitemapFallbackKind.None, [])
        ]);
        var skin = (ISitemapSkin)Activator.CreateInstance(skinType)!;

        var descriptor = skin.Render(page);

        Assert.Equal(RenderActionKind.Navigate, descriptor.Rows[0].Action);
    }

    [Theory]
    [InlineData(typeof(BasicSitemapSkin))]
    [InlineData(typeof(Windows11SitemapSkin))]
    public void RenderThrowsArgumentNullExceptionWhenPageIsNull(Type skinType)
    {
        var skin = (ISitemapSkin)Activator.CreateInstance(skinType)!;

        var ex = Assert.Throws<ArgumentNullException>(() => skin.Render(null!));

        Assert.Equal("page", ex.ParamName);
    }

    [Fact]
    public void BasicSkinKeepsRowsCompact()
    {
        var page = Page();
        var skin = new BasicSitemapSkin();

        var descriptor = skin.Render(page);

        Assert.Equal(SitemapSkinKind.Basic, descriptor.Skin);
        Assert.All(descriptor.Rows, row => Assert.Equal(RenderDensity.Compact, row.Density));
    }

    [Fact]
    public void Windows11SkinUsesComfortableRows()
    {
        var page = Page();
        var skin = new Windows11SitemapSkin();

        var descriptor = skin.Render(page);

        Assert.Equal(SitemapSkinKind.Windows11, descriptor.Skin);
        Assert.All(descriptor.Rows, row => Assert.Equal(RenderDensity.Comfortable, row.Density));
    }

    [Fact]
    public void SkinDescriptorsExposeFallbackAction()
    {
        var page = new NormalizedSitemapPage("root", "Home", [
            new NormalizedSitemapWidget("Camera", SitemapWidgetType.Video, "Camera", "", [], false, true, SitemapFallbackKind.MainUiOrBrowser, [])
        ]);

        var descriptor = new Windows11SitemapSkin().Render(page);

        Assert.Equal(RenderActionKind.OpenFallback, descriptor.Rows[0].Action);
    }

    [Fact]
    public void ToRow_PreservesIconAndRawState_ForSwitchWidgets()
    {
        var page = new NormalizedSitemapPage("root", "Home", [
            new NormalizedSitemapWidget(
                "Kitchen",
                SitemapWidgetType.Switch,
                "Kitchen_Light",
                "OFF",
                [],
                false,
                false,
                SitemapFallbackKind.None,
                [],
                "light")
        ]);

        var descriptor = new Windows11SitemapSkin().Render(page);
        var row = Assert.Single(descriptor.Rows);

        Assert.Equal("light", row.IconName);
        Assert.Equal("OFF", row.RawState);
        Assert.Equal(RenderControlKind.Toggle, row.Control);
        Assert.Equal(RenderActionKind.SendCommand, row.Action);
    }

    [Fact]
    public void ToRow_PreservesStateAndTransformedDisplay_ForMappedSwitch()
    {
        var page = new NormalizedSitemapPage("root", "Home", [
            new NormalizedSitemapWidget(
                "Door",
                SitemapWidgetType.Switch,
                "DoorSensor",
                "OPEN",
                [new SitemapMapping("OPEN", "Unlocked"), new SitemapMapping("CLOSED", "Locked")],
                false,
                false,
                SitemapFallbackKind.None,
                [],
                "door")
        ]);

        var descriptor = new Windows11SitemapSkin().Render(page);
        var row = Assert.Single(descriptor.Rows);

        Assert.Equal("door", row.IconName);
        Assert.Equal("OPEN", row.RawState);
        Assert.Equal("Unlocked", row.State);
    }

    private static NormalizedSitemapPage Page()
    {
        return new NormalizedSitemapPage("root", "Home", [
            new NormalizedSitemapWidget("Light", SitemapWidgetType.Switch, "Light", "ON", [], false, false, SitemapFallbackKind.None, [])
        ]);
    }
}
