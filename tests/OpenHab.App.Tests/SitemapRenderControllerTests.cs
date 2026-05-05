using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.App.Tests;

public sealed class SitemapRenderControllerTests
{
    [Fact]
    public void BuildsWindows11DescriptorByDefault()
    {
        var settings = new AppSettingsController();
        var controller = new SitemapRenderController(settings);

        var descriptor = controller.BuildCurrentDescriptor();

        Assert.Equal(SitemapSkinKind.Windows11, descriptor.Skin);
        Assert.Equal("home", descriptor.PageId);
        Assert.Equal("Home", descriptor.Title);
        Assert.Equal(3, descriptor.Rows.Count);

        var row0 = descriptor.Rows[0];
        Assert.Equal("Living Room Light", row0.Label);
        Assert.Equal("OFF", row0.State);
        Assert.Equal(RenderControlKind.Toggle, row0.Control);
        Assert.Equal(RenderActionKind.SendCommand, row0.Action);
        Assert.Equal(RenderDensity.Comfortable, row0.Density);

        var row1 = descriptor.Rows[1];
        Assert.Equal("Hallway Temperature", row1.Label);
        Assert.Equal("21.4 C", row1.State);
        Assert.Equal(RenderControlKind.Text, row1.Control);
        Assert.Equal(RenderActionKind.None, row1.Action);
        Assert.Equal(RenderDensity.Comfortable, row1.Density);

        var row2 = descriptor.Rows[2];
        Assert.Equal("Kitchen Dimmer", row2.Label);
        Assert.Equal("42", row2.State);
        Assert.Equal(RenderControlKind.Slider, row2.Control);
        Assert.Equal(RenderActionKind.SendCommand, row2.Action);
        Assert.Equal(RenderDensity.Comfortable, row2.Density);
    }

    [Fact]
    public void UsesBasicSkinWhenSelected()
    {
        var settings = new AppSettingsController();
        settings.SetSkin(SitemapSkinKind.Basic);
        var controller = new SitemapRenderController(settings);

        var descriptor = controller.BuildCurrentDescriptor();

        Assert.Equal(SitemapSkinKind.Basic, descriptor.Skin);
        Assert.Equal(3, descriptor.Rows.Count);
        Assert.All(descriptor.Rows, row => Assert.Equal(RenderDensity.Compact, row.Density));
    }

    [Fact]
    public void ConstructorThrowsForNullSettingsController()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new SitemapRenderController(null!));

        Assert.Equal("settingsController", exception.ParamName);
    }
}
