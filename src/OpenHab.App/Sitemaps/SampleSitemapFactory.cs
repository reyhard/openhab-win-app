using OpenHab.Sitemaps.Models;

namespace OpenHab.App.Sitemaps;

public static class SampleSitemapFactory
{
    public static NormalizedSitemapPage CreateHomePage()
    {
        return new NormalizedSitemapPage(
            "home",
            "Home",
            new[]
            {
                new NormalizedSitemapWidget(
                    "Living Room Light",
                    SitemapWidgetType.Switch,
                    "LivingRoom_Light",
                    "OFF",
                    new[] { new SitemapMapping("ON", "On"), new SitemapMapping("OFF", "Off") },
                    CanNavigate: false,
                    RequiresFallback: false,
                    SitemapFallbackKind.None,
                    Array.Empty<SitemapPage>()),
                new NormalizedSitemapWidget(
                    "Hallway Temperature",
                    SitemapWidgetType.Text,
                    "Hallway_Temperature",
                    "21.4 C",
                    Array.Empty<SitemapMapping>(),
                    CanNavigate: false,
                    RequiresFallback: false,
                    SitemapFallbackKind.None,
                    Array.Empty<SitemapPage>()),
                new NormalizedSitemapWidget(
                    "Kitchen Dimmer",
                    SitemapWidgetType.Slider,
                    "Kitchen_Dimmer",
                    "42",
                    Array.Empty<SitemapMapping>(),
                    CanNavigate: false,
                    RequiresFallback: false,
                    SitemapFallbackKind.None,
                    Array.Empty<SitemapPage>())
            });
    }
}
