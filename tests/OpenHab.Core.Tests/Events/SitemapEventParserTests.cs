using OpenHab.Core.Events;

namespace OpenHab.Core.Tests.Events;

public sealed class SitemapEventParserTests
{
    [Theory]
    [InlineData("data: {\"widgetId\":\"1:00\"}")]
    [InlineData("data:{\"widgetId\":\"1:00\"}")]
    public void ParseLineAcceptsValidDataFieldSpacing(string line)
    {
        var result = SitemapEventParser.ParseLine(line);

        var widgetEvent = Assert.IsType<SitemapWidgetEvent>(result);
        Assert.Equal("1:00", widgetEvent.WidgetId);
    }

    [Fact]
    public void ParseLinePreservesVariableWidthWidgetId()
    {
        var result = SitemapEventParser.ParseLine("data: {\"widgetId\":\"2:001100\"}");

        var widgetEvent = Assert.IsType<SitemapWidgetEvent>(result);
        Assert.Equal("2:001100", widgetEvent.WidgetId);
    }

    [Theory]
    [InlineData(": keepalive")]
    [InlineData("data: {\"TYPE\":\"ALIVE\"}")]
    [InlineData("event: message")]
    [InlineData("")]
    public void ParseLineIgnoresNonWidgetFrames(string line)
    {
        Assert.Null(SitemapEventParser.ParseLine(line));
    }

    [Fact]
    public void ParseLineReturnsNullForMalformedJson()
    {
        Assert.Null(SitemapEventParser.ParseLine("data: {\"widgetId\": \"private"));
    }

    [Fact]
    public void CapturedOpenHab520WidgetEventParses()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "CompatibilityFixtures",
            "openhab-5.2.0",
            "events",
            "widget-update.sse");
        var line = File.ReadLines(fixturePath).First(static value => value.StartsWith("data:", StringComparison.Ordinal));

        var result = SitemapEventParser.ParseLine(line);

        var widgetEvent = Assert.IsType<SitemapWidgetEvent>(result);
        Assert.Equal("1_02", widgetEvent.WidgetId);
        Assert.Equal("compatibility", widgetEvent.SitemapName);
        Assert.Equal("compatibility", widgetEvent.PageId);
        Assert.Equal("Compatibility_Switch", widgetEvent.ItemName);
        Assert.Equal("ON", widgetEvent.ItemState);
        Assert.True(widgetEvent.Visibility);
        Assert.False(widgetEvent.DescriptionChanged);
    }
}
