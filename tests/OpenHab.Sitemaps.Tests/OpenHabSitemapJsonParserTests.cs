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
    public void ParseWidgetParsesVideoEncoding()
    {
        const string json = """
            {
              "homepage": {
                "id": "home",
                "widgets": [
                  {
                    "type": "Video",
                    "label": "Camera",
                    "url": "https://demo.openhab.org/Hue.m4v",
                    "encoding": "mjpeg"
                  }
                ]
              }
            }
            """;

        var parsed = OpenHabSitemapJsonParser.ParseHomepage(json);
        var widget = Assert.Single(parsed.Widgets);

        Assert.Equal(SitemapWidgetType.Video, widget.Type);
        Assert.Equal("https://demo.openhab.org/Hue.m4v", widget.Url);
        Assert.Equal("mjpeg", widget.Encoding);
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

    [Fact]
    public void ParseHomepagePreservesSingleWidthOpenHab52WidgetIds()
    {
        const string json = """
            {
              "homepage": {
                "id": "compatibility",
                "widgets": [
                  { "type": "Text", "widgetId": "1:0" },
                  { "type": "Text", "widgetId": "1:00" },
                  { "type": "Text", "widgetId": "1:010" }
                ]
              }
            }
            """;

        var page = OpenHabSitemapJsonParser.ParseHomepage(json);

        Assert.Equal(["1:0", "1:00", "1:010"], page.Widgets.Select(widget => widget.WidgetId));
    }

    [Fact]
    public void ParseHomepagePreservesMultiWidthOpenHab52WidgetIds()
    {
        const string json = """
            {
              "homepage": {
                "id": "compatibility",
                "widgets": [
                  { "type": "Text", "widgetId": "2:0011" },
                  { "type": "Text", "widgetId": "2:001100" },
                  { "type": "Text", "widgetId": "3:000001000" }
                ]
              }
            }
            """;

        var page = OpenHabSitemapJsonParser.ParseHomepage(json);

        Assert.Equal(["2:0011", "2:001100", "3:000001000"], page.Widgets.Select(widget => widget.WidgetId));
    }

    [Fact]
    public void ParseHomepageParsesOpenHab52NestedButtonGridButtons()
    {
        const string json = """
            {
              "homepage": {
                "id": "compatibility",
                "widgets": [
                  {
                    "type": "Buttongrid",
                    "widgetId": "2:0011",
                    "widgets": [
                      {
                        "type": "Button",
                        "widgetId": "2:001100",
                        "row": 0,
                        "column": 0,
                        "command": "ON",
                        "releaseCommand": "OFF",
                        "label": "Light",
                        "icon": "light",
                        "item": {
                          "name": "Compatibility_Switch",
                          "state": "OFF"
                        }
                      }
                    ]
                  }
                ]
              }
            }
            """;

        var page = OpenHabSitemapJsonParser.ParseHomepage(json);

        Assert.Equal(SitemapWidgetType.Buttongrid, page.Widgets[0].Type);
        Assert.Equal("2:0011", page.Widgets[0].WidgetId);
        var button = Assert.Single(page.Widgets.Skip(1));
        Assert.Equal(SitemapWidgetType.Button, button.Type);
        Assert.Equal("2:001100", button.WidgetId);
        Assert.Equal(0, button.Row);
        Assert.Equal(0, button.Column);
        Assert.Equal("ON", button.Command);
        Assert.Equal("OFF", button.ReleaseCommand);
        Assert.Equal("Light", button.Label);
        Assert.Equal("light", button.Icon);
        Assert.Equal("Compatibility_Switch", button.ItemName);
        Assert.Equal("OFF", button.RawItemState);

        var fixturePage = OpenHabSitemapJsonParser.ParseHomepage(
            CompatibilityFixture.ReadText("openhab-5.2.0", "sitemaps", "home.json"));
        var fixtureGridIndex = fixturePage.Widgets
            .Select((widget, index) => (widget, index))
            .Single(entry => entry.widget.WidgetId == "1_06")
            .index;
        var fixtureButtons = fixturePage.Widgets
            .Skip(fixtureGridIndex + 1)
            .TakeWhile(widget => widget.Type == SitemapWidgetType.Button)
            .ToArray();

        Assert.Equal(12, fixtureButtons.Length);
        Assert.Equal(12, fixtureButtons.Select(widget => widget.WidgetId).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(fixtureButtons, widget => widget.WidgetId == "2_000610");
        Assert.Contains(fixtureButtons, widget => widget.WidgetId == "2_000611");
    }

    [Fact]
    public void ParseHomepageStillParsesLegacyButtonGridButtons()
    {
        var legacyPage = OpenHabSitemapJsonParser.ParseHomepage(
            CompatibilityFixture.ReadText("openhab-5.1.4", "sitemaps", "home.json"));
        var currentPage = OpenHabSitemapJsonParser.ParseHomepage(
            CompatibilityFixture.ReadText("openhab-5.2.0", "sitemaps", "home.json"));

        var legacyGrid = Assert.Single(legacyPage.Widgets, widget => widget.WidgetId == "0006");
        var currentGrid = Assert.Single(currentPage.Widgets, widget => widget.WidgetId == "1_06");

        Assert.Equal(SitemapWidgetType.Buttongrid, legacyGrid.Type);
        Assert.Equal("Compatibility_Mode", legacyGrid.ItemName);
        Assert.Equal("NULL", legacyGrid.RawItemState);
        Assert.DoesNotContain(legacyPage.Widgets, widget => widget.Type == SitemapWidgetType.Button);
        Assert.Equal(
            ButtonGridControls(legacyGrid, legacyPage.Widgets),
            ButtonGridControls(currentGrid, currentPage.Widgets));
    }

    [Fact]
    public void ParseHomepageAcceptsEmptyWidgetsAndMappingsArrays()
    {
        const string json = """
            {
              "homepage": {
                "id": "compatibility",
                "widgets": [
                  { "type": "Frame", "widgets": [], "mappings": [] },
                  { "type": "Buttongrid", "widgets": [], "mappings": [] }
                ],
                "mappings": []
              }
            }
            """;

        const string emptyPageJson = """
            {
              "homepage": {
                "id": "empty",
                "widgets": []
              }
            }
            """;

        var emptyPage = OpenHabSitemapJsonParser.ParseHomepage(emptyPageJson);
        var page = OpenHabSitemapJsonParser.ParseHomepage(json);

        Assert.Empty(emptyPage.Widgets);
        Assert.Equal(2, page.Widgets.Count);
        Assert.All(page.Widgets, widget => Assert.Empty(widget.Mappings));
    }

    [Fact]
    public void OpenHab514And520CompatibilityFixturesProduceEquivalentControls()
    {
        var legacy = OpenHabSitemapJsonParser.ParseHomepage(
            CompatibilityFixture.ReadText("openhab-5.1.4", "sitemaps", "home.json"));
        var current = OpenHabSitemapJsonParser.ParseHomepage(
            CompatibilityFixture.ReadText("openhab-5.2.0", "sitemaps", "home.json"));

        var legacyControls = ComparableControls(legacy);
        var currentControls = ComparableControls(current);
        var sharedCurrentControls = currentControls.Where(legacyControls.Contains).ToArray();
        var currentOnlyControls = currentControls.Except(legacyControls).ToArray();

        Assert.Equal(legacyControls, sharedCurrentControls);
        var currentOnly = Assert.Single(currentOnlyControls);
        Assert.Equal(SitemapWidgetType.Text, currentOnly.Type);
        Assert.Equal("Visible text", currentOnly.Label);
        Assert.Equal("Compatibility_Text", currentOnly.ItemName);
    }

    private static IReadOnlyList<ComparableWidget> ComparableControls(SitemapPage page)
    {
        var controls = new List<ComparableWidget>();
        for (var index = 0; index < page.Widgets.Count; index++)
        {
            var widget = page.Widgets[index];
            if (widget.Type == SitemapWidgetType.Button)
            {
                continue;
            }

            if (widget.Type == SitemapWidgetType.Buttongrid)
            {
                controls.Add(new ComparableWidget(
                    widget.Type,
                    widget.Label,
                    null,
                    MappingSignature(ButtonGridControls(widget, page.Widgets)),
                    widget.Row,
                    widget.Column,
                    widget.Command,
                    widget.ReleaseCommand));
                continue;
            }

            controls.Add(new ComparableWidget(
                widget.Type,
                widget.Label,
                widget.ItemName,
                MappingSignature(widget.Mappings.Select(mapping => (mapping.Command, mapping.Label))),
                widget.Row,
                widget.Column,
                widget.Command,
                widget.ReleaseCommand));
        }

        return controls;
    }

    private static IReadOnlyList<ComparableButtonGridControl> ButtonGridControls(
        SitemapWidget buttonGrid,
        IReadOnlyList<SitemapWidget> widgets)
    {
        if (buttonGrid.Mappings.Count > 0)
        {
            return buttonGrid.Mappings
                .Select(mapping => new ComparableButtonGridControl(buttonGrid.ItemName, mapping.Command, mapping.Label))
                .ToArray();
        }

        var gridIndex = -1;
        for (var index = 0; index < widgets.Count; index++)
        {
            if (ReferenceEquals(widgets[index], buttonGrid))
            {
                gridIndex = index;
                break;
            }
        }

        if (gridIndex < 0)
        {
            throw new InvalidOperationException("ButtonGrid was not found in its containing widget sequence.");
        }

        return widgets
            .Skip(gridIndex + 1)
            .TakeWhile(widget => widget.Type == SitemapWidgetType.Button)
            .Select(button => new ComparableButtonGridControl(
                button.ItemName ?? buttonGrid.ItemName,
                button.Command ?? string.Empty,
                button.Label))
            .ToArray();
    }

    private static string MappingSignature(IEnumerable<ComparableButtonGridControl> mappings) =>
        string.Join("\u001e", mappings.Select(mapping => $"{mapping.ItemName}\u001f{mapping.Command}\u001f{mapping.Label}"));

    private static string MappingSignature(IEnumerable<(string Command, string Label)> mappings) =>
        string.Join("\u001e", mappings.Select(mapping => $"{mapping.Command}\u001f{mapping.Label}"));

    private sealed record ComparableWidget(
        SitemapWidgetType Type,
        string Label,
        string? ItemName,
        string MappingSignature,
        int? Row,
        int? Column,
        string? Command,
        string? ReleaseCommand);

    private sealed record ComparableButtonGridControl(string? ItemName, string Command, string Label);
}
