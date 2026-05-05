using OpenHab.App.Settings;
using OpenHab.Rendering.Descriptors;
using OpenHab.Rendering.Skins;

namespace OpenHab.App.Sitemaps;

public sealed class SitemapRenderController
{
    private readonly AppSettingsController settingsController;

    public SitemapRenderController(AppSettingsController settingsController)
    {
        this.settingsController = settingsController;
    }

    public SitemapRenderDescriptor BuildCurrentDescriptor()
    {
        var page = SampleSitemapFactory.CreateHomePage();
        ISitemapSkin skin = settingsController.Current.Skin switch
        {
            SitemapSkinKind.Basic => new BasicSitemapSkin(),
            SitemapSkinKind.Windows11 => new Windows11SitemapSkin(),
            _ => throw new InvalidOperationException($"Unsupported sitemap skin '{settingsController.Current.Skin}'.")
        };

        return skin.Render(page);
    }
}
