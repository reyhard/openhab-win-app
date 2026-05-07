using OpenHab.Core.Events;

namespace OpenHab.Core.Tests.Events;

public sealed class SseMessageParserTests
{
    [Fact]
    public void ParseLine_ItemStateEvent_ReturnsTypedEvent()
    {
        var line = @"data: {""topic"":""openhab/items/Light_GF/state"",""payload"":""{\""type\"":\""OnOff\"",\""value\"":\""ON\""}"",""type"":""ItemStateEvent""}";

        var result = SseMessageParser.ParseLine(line);

        Assert.NotNull(result);
        var stateChanged = Assert.IsType<ItemStateChangedEvent>(result);
        Assert.Equal("Light_GF", stateChanged.ItemName);
        Assert.Equal("ON", stateChanged.State);
        Assert.Equal("ItemStateEvent", stateChanged.Type);
        Assert.Equal("openhab/items/Light_GF/state", stateChanged.Topic);
    }

    [Fact]
    public void ParseLine_ItemStateChangedEvent_AlsoAccepted()
    {
        var line = @"data: {""topic"":""openhab/items/Light_GF/statechanged"",""payload"":""{\""type\"":\""OnOff\"",\""value\"":\""ON\""}"",""type"":""ItemStateChangedEvent""}";

        var result = SseMessageParser.ParseLine(line);

        Assert.NotNull(result);
        var stateChanged = Assert.IsType<ItemStateChangedEvent>(result);
        Assert.Equal("Light_GF", stateChanged.ItemName);
        Assert.Equal("ON", stateChanged.State);
    }

    [Fact]
    public void ParseLine_ItemCommandEvent_ReturnsTypedEvent()
    {
        var line = @"data: {""topic"":""openhab/items/Light_GF/command"",""payload"":""{\""type\"":\""OnOff\"",\""value\"":\""OFF\""}"",""type"":""ItemCommandEvent""}";

        var result = SseMessageParser.ParseLine(line);

        Assert.NotNull(result);
        var commandEvent = Assert.IsType<ItemCommandEvent>(result);
        Assert.Equal("Light_GF", commandEvent.ItemName);
        Assert.Equal("OFF", commandEvent.Command);
        Assert.Equal("ItemCommandEvent", commandEvent.Type);
        Assert.Equal("openhab/items/Light_GF/command", commandEvent.Topic);
    }

    [Fact]
    public void ParseLine_NonItemTopic_ReturnsNull()
    {
        var line = @"data: {""topic"":""openhab/things/zwave/status"",""payload"":""{\""status\"":\""online\""}"",""type"":""ThingStatusInfoChangedEvent""}";

        var result = SseMessageParser.ParseLine(line);

        Assert.Null(result);
    }

    [Fact]
    public void ParseLine_EmptyLine_ReturnsNull()
    {
        Assert.Null(SseMessageParser.ParseLine(""));
        Assert.Null(SseMessageParser.ParseLine("   "));
        Assert.Null(SseMessageParser.ParseLine("\t"));
    }

    [Fact]
    public void ParseLine_EventStreamComment_ReturnsNull()
    {
        var line = ": this is a comment";

        var result = SseMessageParser.ParseLine(line);

        Assert.Null(result);
    }
}
