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
        Assert.Equal("21.5 \u00B0C", childWidget.State);
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

    [Fact]
    public void ParseWidgetExtractsIcon()
    {
        const string json = """
            {
              "homepage": {
                "id": "home",
                "widgets": [
                  {
                    "type": "Switch",
                    "label": "Kitchen Light [ON]",
                    "icon": "light",
                    "item": {
                      "name": "KitchenLight",
                      "state": "OFF"
                    }
                  }
                ]
              }
            }
            """;

        var parsed = OpenHabSitemapJsonParser.ParseHomepage(json);

        var widget = Assert.Single(parsed.Widgets);
        Assert.Equal("light", widget.Icon);
    }

    [Fact]
    public void ParseWidgetDefaultsIconToNullWhenAbsent()
    {
        const string json = """
            {
              "homepage": {
                "id": "home",
                "widgets": [
                  {
                    "type": "Text",
                    "label": "Temperature [21.5 \u00B0C]",
                    "item": {
                      "name": "KitchenTemp",
                      "state": "21.7 \u00B0C"
                    }
                  }
                ]
              }
            }
            """;

        var parsed = OpenHabSitemapJsonParser.ParseHomepage(json);

        var widget = Assert.Single(parsed.Widgets);
        Assert.Null(widget.Icon);
    }

    [Fact]
    public void ParseHomepageFlattensFrameChildren()
    {
        const string json = """
        {
          "homepage": {
            "id": "home",
            "title": "Home",
            "widgets": [
              {
                "type": "Frame",
                "label": "Living Room",
                "widgets": [
                  {
                    "type": "Switch",
                    "label": "Light [ON]",
                    "item": { "name": "LivingRoom_Light", "state": "ON" }
                  },
                  {
                    "type": "Text",
                    "label": "Temperature [22.5]",
                    "item": { "name": "LivingRoom_Temp", "state": "22.5" }
                  }
                ]
              }
            ]
          }
        }
        """;

        var page = OpenHabSitemapJsonParser.ParseHomepage(json);

        // Frame + 2 children = 3 widgets total
        Assert.Equal(3, page.Widgets.Count);
        Assert.Equal(SitemapWidgetType.Frame, page.Widgets[0].Type);
        Assert.Equal("Living Room", page.Widgets[0].Label);
        Assert.Equal(SitemapWidgetType.Switch, page.Widgets[1].Type);
        Assert.Equal("Light", page.Widgets[1].Label);
        Assert.Equal("ON", page.Widgets[1].State);
        Assert.Equal(SitemapWidgetType.Text, page.Widgets[2].Type);
        Assert.Equal("Temperature", page.Widgets[2].Label);
        Assert.Equal("22.5", page.Widgets[2].State);
    }

    [Fact]
    public void ParseWidgetParsesInputHint()
    {
        const string json = """
            {
              "homepage": {
                "id": "home",
                "widgets": [
                  {
                    "type": "Input",
                    "label": "PIN []",
                    "inputHint": "number",
                    "item": {
                      "name": "SmartLock_01_PIN",
                      "state": ""
                    }
                  }
                ]
              }
            }
            """;

        var parsed = OpenHabSitemapJsonParser.ParseHomepage(json);
        var widget = Assert.Single(parsed.Widgets);

        Assert.Equal(SitemapWidgetType.Input, widget.Type);
        Assert.Equal(SitemapInputHint.Number, widget.InputHint);
    }

    [Fact]
    public void ParseWidgetParsesColorInputHint()
    {
        const string json = """
            {
              "homepage": {
                "id": "home",
                "widgets": [
                  {
                    "type": "Input",
                    "label": "Color []",
                    "inputHint": "color",
                    "item": {
                      "name": "Light_01_Color",
                      "state": "#ff0000"
                    }
                  }
                ]
              }
            }
            """;

        var parsed = OpenHabSitemapJsonParser.ParseHomepage(json);
        var widget = Assert.Single(parsed.Widgets);

        Assert.Equal(SitemapWidgetType.Input, widget.Type);
        Assert.Equal(SitemapInputHint.Color, widget.InputHint);
    }

    [Fact]
    public void ParseWidgetParsesColorTemperatureInputHint()
    {
        const string json = """
            {
              "homepage": {
                "id": "home",
                "widgets": [
                  {
                    "type": "Input",
                    "label": "Color Temp []",
                    "inputHint": "colortemperature",
                    "item": {
                      "name": "Light_01_ColorTemp",
                      "state": "55"
                    }
                  }
                ]
              }
            }
            """;

        var parsed = OpenHabSitemapJsonParser.ParseHomepage(json);
        var widget = Assert.Single(parsed.Widgets);

        Assert.Equal(SitemapWidgetType.Input, widget.Type);
        Assert.Equal(SitemapInputHint.ColorTemperature, widget.InputHint);
    }

    [Fact]
    public void ParseWidgetParsesHeight()
    {
        const string json = """
            {
              "homepage": {
                "id": "home",
                "widgets": [
                  {
                    "type": "Webview",
                    "label": "Events",
                    "url": "http://openhab:9001",
                    "height": 130
                  }
                ]
              }
            }
            """;

        var parsed = OpenHabSitemapJsonParser.ParseHomepage(json);
        var widget = Assert.Single(parsed.Widgets);

        Assert.Equal(SitemapWidgetType.Webview, widget.Type);
        Assert.Equal(130, widget.HeightRows);
    }

    [Fact]
    public void ParseWidgetParsesResolvedColors()
    {
        const string json = """
            {
              "homepage": {
                "id": "home",
                "widgets": [
                  {
                    "type": "Text",
                    "label": "Gas [3.5 m3]",
                    "labelcolor": ["orange"],
                    "valuecolor": "green",
                    "iconcolor": [{ "color": "blue" }]
                  }
                ]
              }
            }
            """;

        var parsed = OpenHabSitemapJsonParser.ParseHomepage(json);
        var widget = Assert.Single(parsed.Widgets);

        Assert.Equal("orange", widget.LabelColor);
        Assert.Equal("green", widget.ValueColor);
        Assert.Equal("blue", widget.IconColor);
    }
}
