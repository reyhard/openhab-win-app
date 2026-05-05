using OpenHab.Sitemaps.Models;
using OpenHab.Sitemaps.Parsing;

namespace OpenHab.Sitemaps.Tests;

public sealed class OpenHabSitemapJsonParserTests
{
    [Fact]
    public void ParseHomepageParsesHomepageAndLinkedPages()
    {
        const string json = """
            {
              "homepage": {
                "id": "home",
                "title": "Main Home",
                "widgets": [
                  {
                    "type": "Switch",
                    "label": "Kitchen Light [ON]",
                    "item": {
                      "name": "KitchenLight",
                      "state": "OFF"
                    },
                    "visibility": true,
                    "linkedPage": {
                      "id": "kitchen",
                      "title": "Kitchen",
                      "widgets": [
                        {
                          "type": "Text",
                          "label": "Temperature [21.5 °C]",
                          "item": {
                            "name": "KitchenTemp",
                            "state": "21.7 \u00B0C"
                          }
                        }
                      ]
                    }
                  }
                ]
              }
            }
            """;

        var parsed = OpenHabSitemapJsonParser.ParseHomepage(json);

        Assert.Equal("home", parsed.Id);
        Assert.Equal("Main Home", parsed.Label);
        var rootWidget = Assert.Single(parsed.Widgets);
        Assert.Equal(SitemapWidgetType.Switch, rootWidget.Type);
        Assert.Equal("Kitchen Light", rootWidget.Label);
        Assert.Equal("KitchenLight", rootWidget.ItemName);
        Assert.Equal("OFF", rootWidget.State);
        Assert.True(rootWidget.IsVisible);

        var childPage = Assert.Single(rootWidget.Children);
        Assert.Equal("kitchen", childPage.Id);
        Assert.Equal("Kitchen", childPage.Label);
        var childWidget = Assert.Single(childPage.Widgets);
        Assert.Equal(SitemapWidgetType.Text, childWidget.Type);
        Assert.Equal("Temperature", childWidget.Label);
        Assert.Equal("21.7 \u00B0C", childWidget.State);
    }

    [Fact]
    public void ParseHomepageParsesWidgetTypeCaseInsensitively()
    {
        const string json = """
            {
              "homepage": {
                "id": "home",
                "widgets": [
                  {
                    "type": "sWiTcH",
                    "label": "Kitchen Light [ON]"
                  }
                ]
              }
            }
            """;

        var parsed = OpenHabSitemapJsonParser.ParseHomepage(json);

        var widget = Assert.Single(parsed.Widgets);
        Assert.Equal(SitemapWidgetType.Switch, widget.Type);
    }

    [Fact]
    public void ParseHomepageUsesStateFromLabelAndDefaultsMappingsChildrenAndVisibility()
    {
        const string json = """
            {
              "homepage": {
                "id": "home",
                "widgets": [
                  {
                    "type": "does-not-exist",
                    "label": "Mode [AUTO]",
                    "item": {
                      "name": "ModeItem"
                    }
                  }
                ]
              }
            }
            """;

        var parsed = OpenHabSitemapJsonParser.ParseHomepage(json);

        Assert.Equal("home", parsed.Id);
        Assert.Equal("home", parsed.Label);
        var widget = Assert.Single(parsed.Widgets);
        Assert.Equal(SitemapWidgetType.Default, widget.Type);
        Assert.Equal("Mode", widget.Label);
        Assert.Equal("ModeItem", widget.ItemName);
        Assert.Equal("AUTO", widget.State);
        Assert.True(widget.IsVisible);
        Assert.Empty(widget.Mappings);
        Assert.Empty(widget.Children);
    }

    [Fact]
    public void ParseHomepageUsesPageLabelWhenTitleIsMissing()
    {
        const string json = """
            {
              "homepage": {
                "id": "home",
                "label": "Fallback Label"
              }
            }
            """;

        var parsed = OpenHabSitemapJsonParser.ParseHomepage(json);

        Assert.Equal("Fallback Label", parsed.Label);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ParseHomepageThrowsForBlankInput(string json)
    {
        Assert.Throws<ArgumentException>(() => OpenHabSitemapJsonParser.ParseHomepage(json));
    }

    [Fact]
    public void ParseHomepageThrowsForNonObjectHomepage()
    {
        const string json = """
            {
              "homepage": null
            }
            """;

        Assert.Throws<FormatException>(() => OpenHabSitemapJsonParser.ParseHomepage(json));
    }

    [Fact]
    public void ParseHomepageThrowsForNonObjectWidgetEntry()
    {
        const string json = """
            {
              "homepage": {
                "id": "home",
                "widgets": [
                  "invalid-widget"
                ]
              }
            }
            """;

        var ex = Assert.Throws<FormatException>(() => OpenHabSitemapJsonParser.ParseHomepage(json));

        Assert.Contains("home", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("widget", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
