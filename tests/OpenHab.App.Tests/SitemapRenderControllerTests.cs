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
        Assert.Contains(descriptor.Rows, row => row.Control == RenderControlKind.Toggle && row.Action == RenderActionKind.SendCommand);
        Assert.Contains(descriptor.Rows, row => row.Control == RenderControlKind.Slider && row.Action == RenderActionKind.SendCommand);
    }

    [Fact]
    public void UsesBasicSkinWhenSelected()
    {
        var settings = new AppSettingsController();
        settings.SetSkin(SitemapSkinKind.Basic);
        var controller = new SitemapRenderController(settings);

        var descriptor = controller.BuildCurrentDescriptor();

        Assert.Equal(SitemapSkinKind.Basic, descriptor.Skin);
        Assert.All(descriptor.Rows, row => Assert.Equal(RenderDensity.Compact, row.Density));
    }
}
