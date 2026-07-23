using OpenHab.CompatibilityProbe;

namespace OpenHab.Core.Tests.Compatibility;

public sealed class ProductionPayloadValidatorTests
{
    [Fact]
    public void ValidateSitemap_UsesProductionParserAndCountsServerWidgetIds()
    {
        var result = ProductionPayloadValidator.ValidateSitemap("""
            { "homepage": { "id": "root", "widgets": [
              { "type": "Text", "label": "Synthetic", "widgetId": "2_000611" }
            ] } }
            """);

        Assert.Equal(1, result.WidgetCount);
        Assert.Equal(1, result.WidgetIdsObserved);
    }

    [Fact]
    public void ValidateMainUiPages_UsesProductionClientParser()
    {
        var result = ProductionPayloadValidator.ValidateMainUiPages("""
            [{ "uid": "read-only", "component": "oh-layout-page", "managed": false }]
            """);

        Assert.Equal(1, result.PageCount);
    }
}
