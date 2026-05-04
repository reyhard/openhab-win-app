using OpenHab.Rendering.Descriptors;
using OpenHab.Sitemaps.Models;

namespace OpenHab.Rendering.Skins;

public sealed class BasicSitemapSkin : ISitemapSkin
{
    public SitemapRenderDescriptor Render(NormalizedSitemapPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new SitemapRenderDescriptor(
            SitemapSkinKind.Basic,
            page.Id,
            page.Label,
            page.Widgets.Select(widget => SitemapRowMapper.ToRow(widget, RenderDensity.Compact)).ToArray());
    }
}
