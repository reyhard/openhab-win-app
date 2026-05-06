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

    [Theory]
    [InlineData(typeof(BasicSitemapSkin))]
    [InlineData(typeof(Windows11SitemapSkin))]
    public void SwitchRow_PreservesIconNameInDescriptor(Type skinType)
    {
        var page = new NormalizedSitemapPage("root", "Home", [
            new NormalizedSitemapWidget("Light", SitemapWidgetType.Switch, "Light", "ON", [], false, false, SitemapFallbackKind.None, [], "light")
        ]);
        var skin = (ISitemapSkin)Activator.CreateInstance(skinType)!;

        var descriptor = skin.Render(page);
        var row = Assert.Single(descriptor.Rows);

        Assert.Equal("light", row.IconName);
    }

    [Theory]
    [InlineData(typeof(BasicSitemapSkin))]
    [InlineData(typeof(Windows11SitemapSkin))]
    public void SwitchRow_PreservesRawStateInDescriptor(Type skinType)
    {
        var page = new NormalizedSitemapPage("root", "Home", [
            new NormalizedSitemapWidget("Light", SitemapWidgetType.Switch, "Light", "ON",
                [new SitemapMapping("ON", "An"), new SitemapMapping("OFF", "Aus")],
                false, false, SitemapFallbackKind.None, [])
        ]);
        var skin = (ISitemapSkin)Activator.CreateInstance(skinType)!;

        var descriptor = skin.Render(page);
        var row = Assert.Single(descriptor.Rows);

        Assert.Equal("An", row.State);
        Assert.Equal("ON", row.RawState);
    }

    [Theory]
    [InlineData(typeof(BasicSitemapSkin))]
    [InlineData(typeof(Windows11SitemapSkin))]
    public void TextRow_PreservesIconNameInDescriptor(Type skinType)
    {
        var page = new NormalizedSitemapPage("root", "Home", [
            new NormalizedSitemapWidget("Temp", SitemapWidgetType.Text, "Temp", "21", [], false, false, SitemapFallbackKind.None, [], "temperature")
        ]);
        var skin = (ISitemapSkin)Activator.CreateInstance(skinType)!;

        var descriptor = skin.Render(page);
        var row = Assert.Single(descriptor.Rows);

        Assert.Equal("temperature", row.IconName);
    }

    [Theory]
    [InlineData(typeof(BasicSitemapSkin))]
    [InlineData(typeof(Windows11SitemapSkin))]
    public void SliderRow_PreservesIconNameInDescriptor(Type skinType)
    {
        var page = new NormalizedSitemapPage("root", "Home", [
            new NormalizedSitemapWidget("Dimmer", SitemapWidgetType.Slider, "Dimmer", "50", [], false, false, SitemapFallbackKind.None, [], "dimmer")
        ]);
        var skin = (ISitemapSkin)Activator.CreateInstance(skinType)!;

        var descriptor = skin.Render(page);
        var row = Assert.Single(descriptor.Rows);

        Assert.Equal("dimmer", row.IconName);
    }

    [Theory]
    [InlineData(typeof(BasicSitemapSkin))]
    [InlineData(typeof(Windows11SitemapSkin))]
    public void SelectionRow_PreservesIconNameInDescriptor(Type skinType)
    {
        var page = new NormalizedSitemapPage("root", "Home", [
            new NormalizedSitemapWidget("Mode", SitemapWidgetType.Selection, "Mode", "Home", [], false, false, SitemapFallbackKind.None, [], "settings")
        ]);
        var skin = (ISitemapSkin)Activator.CreateInstance(skinType)!;

        var descriptor = skin.Render(page);
        var row = Assert.Single(descriptor.Rows);

        Assert.Equal("settings", row.IconName);
    }

    [Theory]
    [InlineData(typeof(BasicSitemapSkin))]
    [InlineData(typeof(Windows11SitemapSkin))]
    public void WidgetWithoutIcon_HasNullIconName(Type skinType)
    {
        var page = new NormalizedSitemapPage("root", "Home", [
            new NormalizedSitemapWidget("Text", SitemapWidgetType.Text, "Text", "hi", [], false, false, SitemapFallbackKind.None, [])
        ]);
        var skin = (ISitemapSkin)Activator.CreateInstance(skinType)!;

        var descriptor = skin.Render(page);
        var row = Assert.Single(descriptor.Rows);

        Assert.Null(row.IconName);
    }

    private static NormalizedSitemapPage Page()
    {
        return new NormalizedSitemapPage("root", "Home", [
            new NormalizedSitemapWidget("Light", SitemapWidgetType.Switch, "Light", "ON", [], false, false, SitemapFallbackKind.None, [])
        ]);
    }
}
