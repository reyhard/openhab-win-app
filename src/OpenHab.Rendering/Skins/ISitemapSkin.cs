using OpenHab.Rendering.Descriptors;
using OpenHab.Sitemaps.Models;

namespace OpenHab.Rendering.Skins;

public interface ISitemapSkin
{
    SitemapRenderDescriptor Render(NormalizedSitemapPage page);
}
