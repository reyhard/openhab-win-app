using OpenHab.Rendering.Descriptors;
using OpenHab.Sitemaps.Models;

namespace OpenHab.Rendering.Skins;

public sealed class Windows11SitemapSkin : ISitemapSkin
{
    public SitemapRenderDescriptor Render(NormalizedSitemapPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var rows = page.Widgets.Select(widget => SitemapRowMapper.ToRow(widget, RenderDensity.Comfortable)).ToArray();
        return new SitemapRenderDescriptor(SitemapSkinKind.Windows11, page.Id, page.Label, rows);
    }
}
