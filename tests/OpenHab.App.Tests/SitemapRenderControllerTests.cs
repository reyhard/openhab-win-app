using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Rendering.Descriptors;
using OpenHab.Sitemaps.Models;
using System.IO;

namespace OpenHab.App.Tests;

public sealed class SitemapRenderControllerTests
{
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenHab.WinApp",
        "settings.json");

    public SitemapRenderControllerTests()
    {
        // Retry deletion — fire-and-forget SaveAsync from a previous test may still be writing.
        for (int i = 0; i < 5; i++)
        {
            try { File.Delete(SettingsFilePath); } catch { }
            if (!File.Exists(SettingsFilePath)) break;
            Thread.Sleep(10);
        }
    }

    [Fact]
    public void BuildsWindows11DescriptorByDefault()
    {
        var settings = new AppSettingsController();
        var controller = new SitemapRenderController(settings);
        var page = CreateTestPage();

        var descriptor = controller.BuildCurrentDescriptor(page);

        Assert.Equal(SitemapSkinKind.Windows11, descriptor.Skin);
        Assert.Equal("custom-page", descriptor.PageId);
        Assert.Equal("Custom Home", descriptor.Title);
        Assert.Equal(3, descriptor.Rows.Count);

        var row0 = descriptor.Rows[0];
        Assert.Equal("Porch Light", row0.Label);
        Assert.Equal("ON", row0.State);
        Assert.Equal(RenderControlKind.Toggle, row0.Control);
        Assert.Equal(RenderActionKind.SendCommand, row0.Action);
        Assert.Equal(RenderDensity.Comfortable, row0.Density);

        var row1 = descriptor.Rows[1];
        Assert.Equal("Boiler Temperature", row1.Label);
        Assert.Equal("57 C", row1.State);
        Assert.Equal(RenderControlKind.Text, row1.Control);
        Assert.Equal(RenderActionKind.None, row1.Action);
        Assert.Equal(RenderDensity.Comfortable, row1.Density);

        var row2 = descriptor.Rows[2];
        Assert.Equal("Office Brightness", row2.Label);
        Assert.Equal("33", row2.State);
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
        var page = CreateTestPage();

        var descriptor = controller.BuildCurrentDescriptor(page);

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

    [Fact]
    public void BuildCurrentDescriptorThrowsForNullPage()
    {
        var settings = new AppSettingsController();
        var controller = new SitemapRenderController(settings);

        var exception = Assert.Throws<ArgumentNullException>(() => controller.BuildCurrentDescriptor(null!));

        Assert.Equal("page", exception.ParamName);
    }

    private static NormalizedSitemapPage CreateTestPage()
    {
        return new NormalizedSitemapPage(
            "custom-page",
            "Custom Home",
            new[]
            {
                new NormalizedSitemapWidget(
                    "Porch Light",
                    SitemapWidgetType.Switch,
                    "Porch_Light",
                    "ON",
                    new[] { new SitemapMapping("ON", "On"), new SitemapMapping("OFF", "Off") },
                    CanNavigate: false,
                    RequiresFallback: false,
                    SitemapFallbackKind.None,
                    Array.Empty<SitemapPage>()),
                new NormalizedSitemapWidget(
                    "Boiler Temperature",
                    SitemapWidgetType.Text,
                    "Boiler_Temperature",
                    "57 C",
                    Array.Empty<SitemapMapping>(),
                    CanNavigate: false,
                    RequiresFallback: false,
                    SitemapFallbackKind.None,
                    Array.Empty<SitemapPage>()),
                new NormalizedSitemapWidget(
                    "Office Brightness",
                    SitemapWidgetType.Slider,
                    "Office_Dimmer",
                    "33",
                    Array.Empty<SitemapMapping>(),
                    CanNavigate: false,
                    RequiresFallback: false,
                    SitemapFallbackKind.None,
                    Array.Empty<SitemapPage>())
            });
    }
}
