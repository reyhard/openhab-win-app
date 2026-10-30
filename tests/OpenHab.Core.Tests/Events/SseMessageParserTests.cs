using System.Text.Json;
using OpenHab.Core.Events;

namespace OpenHab.Core.Tests.Events;

public sealed class SseMessageParserTests
{
    private const string StateTopic = "openhab/items/Light_GF/state";
    private const string StateEventType = "ItemStateEvent";
    private const string StatePayload = """{"type":"OnOff","value":"ON"}""";

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

    [Fact]
    public void ParseLine_EventFieldWithoutDataPrefix_ReturnsNull()
    {
        Assert.Null(SseMessageParser.ParseLine("event: update"));
    }

    [Fact]
    public void ParseLine_DataFieldWithoutRequiredSpace_ReturnsNull()
    {
        var line = "data:" + """{"topic":"openhab/items/Light_GF/state"}""";

        Assert.Null(SseMessageParser.ParseLine(line));
    }

    [Theory]
    [InlineData("""data: {"topic":""")]
    [InlineData("data: not-json")]
    public void ParseLine_MalformedOuterJson_ReturnsNull(string line)
    {
        Assert.Null(SseMessageParser.ParseLine(line));
    }

    [Fact]
    public void ParseLine_MalformedNestedPayloadJson_ReturnsNull()
    {
        var line = BuildEnvelope(StateTopic, StateEventType, """{"value":""");

        Assert.Null(SseMessageParser.ParseLine(line));
    }

    // Wrong-typed envelope values (for example a numeric topic) and non-object JSON roots are deliberately not
    // exercised here: JsonElement.GetString/TryGetProperty throw InvalidOperationException for those shapes and
    // ParseLine only catches JsonException, so asserting a result would encode unverified behavior.
    [Theory]
    [InlineData("topic")]
    [InlineData("type")]
    [InlineData("payload")]
    public void ParseLine_MissingEnvelopeField_ReturnsNull(string field)
    {
        var line = BuildEnvelopeWithout(field);

        Assert.Null(SseMessageParser.ParseLine(line));
    }

    [Theory]
    [InlineData("topic")]
    [InlineData("type")]
    [InlineData("payload")]
    public void ParseLine_NullEnvelopeField_ReturnsNull(string field)
    {
        var line = BuildEnvelopeWithNull(field);

        Assert.Null(SseMessageParser.ParseLine(line));
    }

    [Theory]
    [InlineData("")]
    [InlineData("openhab")]
    [InlineData("openhab/items")]
    public void ParseLine_TopicWithFewerThanThreeSegments_ReturnsNull(string topic)
    {
        var line = BuildEnvelope(topic, StateEventType, StatePayload);

        Assert.Null(SseMessageParser.ParseLine(line));
    }

    [Theory]
    [InlineData("other/items/Light_GF/state")]
    [InlineData("openhab/things/Light_GF/state")]
    public void ParseLine_TopicOutsideItemNamespace_ReturnsNull(string topic)
    {
        var line = BuildEnvelope(topic, StateEventType, StatePayload);

        Assert.Null(SseMessageParser.ParseLine(line));
    }

    [Fact]
    public void ParseLine_PayloadWithoutValueProperty_ReturnsNull()
    {
        var line = BuildEnvelope(StateTopic, StateEventType, """{"type":"OnOff","status":"OK"}""");

        Assert.Null(SseMessageParser.ParseLine(line));
    }

    [Theory]
    [InlineData("""{"value":null}""")]
    [InlineData("""{"value":{"nested":"ON"}}""")]
    [InlineData("""{"value":["ON","OFF"]}""")]
    public void ParseLine_PayloadValueOfNonScalarKind_ReturnsNull(string payload)
    {
        var line = BuildEnvelope(StateTopic, StateEventType, payload);

        Assert.Null(SseMessageParser.ParseLine(line));
    }

    [Fact]
    public void ParseLine_PayloadNumericValue_UsesExactRawText()
    {
        var line = BuildEnvelope("openhab/items/Light_GF/statechanged", "ItemStateChangedEvent", """{"type":"Number","value":12.50}""");

        var result = SseMessageParser.ParseLine(line);

        var stateChanged = Assert.IsType<ItemStateChangedEvent>(result);
        Assert.Equal("Light_GF", stateChanged.ItemName);
        Assert.Equal("12.50", stateChanged.State);
        Assert.Equal("ItemStateChangedEvent", stateChanged.Type);
        Assert.Equal("openhab/items/Light_GF/statechanged", stateChanged.Topic);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void ParseLine_PayloadBooleanValue_UsesLowercaseRawText(string rawValue)
    {
        var payload = $$"""{"type":"Switch","value":{{rawValue}}}""";
        var line = BuildEnvelope(StateTopic, StateEventType, payload);

        var result = SseMessageParser.ParseLine(line);

        var stateChanged = Assert.IsType<ItemStateChangedEvent>(result);
        Assert.Equal("Light_GF", stateChanged.ItemName);
        Assert.Equal(rawValue, stateChanged.State);
        Assert.Equal(StateEventType, stateChanged.Type);
        Assert.Equal(StateTopic, stateChanged.Topic);
    }

    [Fact]
    public void ParseLine_UnsupportedEventType_ReturnsNull()
    {
        var line = BuildEnvelope(StateTopic, "ItemAddedEvent", StatePayload);

        Assert.Null(SseMessageParser.ParseLine(line));
    }

    private static string BuildLine(string json) => "data: " + json;

    private static string BuildEnvelope(string topic, string type, string payload) =>
        BuildLine(JsonSerializer.Serialize(DefaultEnvelope(topic, type, payload)));

    private static string BuildEnvelopeWithout(string field)
    {
        var fields = DefaultEnvelope(StateTopic, StateEventType, StatePayload);
        fields.Remove(field);
        return BuildLine(JsonSerializer.Serialize(fields));
    }

    private static string BuildEnvelopeWithNull(string field)
    {
        var fields = DefaultEnvelope(StateTopic, StateEventType, StatePayload);
        fields[field] = null;
        return BuildLine(JsonSerializer.Serialize(fields));
    }

    private static Dictionary<string, string?> DefaultEnvelope(string topic, string type, string payload) =>
        new(StringComparer.Ordinal)
        {
            ["topic"] = topic,
            ["type"] = type,
            ["payload"] = payload
        };
}
