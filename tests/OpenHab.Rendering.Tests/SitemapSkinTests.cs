using OpenHab.Rendering.Descriptors;
using OpenHab.Rendering.Skins;
using OpenHab.Sitemaps.Models;

namespace OpenHab.Rendering.Tests;

public sealed class SitemapSkinTests
{
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

    private static NormalizedSitemapPage Page()
    {
        return new NormalizedSitemapPage("root", "Home", [
            new NormalizedSitemapWidget("Light", SitemapWidgetType.Switch, "Light", "ON", [], false, false, SitemapFallbackKind.None, [])
        ]);
    }
}
