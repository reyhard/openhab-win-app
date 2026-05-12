using OpenHab.Windows.Notifications;

namespace OpenHab.App.Tests.Notifications;

public sealed class NotificationActionTests
{
    // ───────────────────────────── TryParse ─────────────────────────────

    [Fact]
    public void Parse_CommandAction_ReturnsCorrectTypeAndPayload()
    {
        var result = NotificationActionParser.TryParse("command:KitchenLights:ON");

        Assert.NotNull(result);
        Assert.Equal("command", result.Type);
        Assert.Equal("KitchenLights:ON", result.Payload);
    }

    [Fact]
    public void Parse_UiAction_ReturnsCorrectTypeAndPayload()
    {
        var result = NotificationActionParser.TryParse("ui:/basicui/app?w=0000&sitemap=main");

        Assert.NotNull(result);
        Assert.Equal("ui", result.Type);
        Assert.Equal("/basicui/app?w=0000&sitemap=main", result.Payload);
    }

    [Fact]
    public void Parse_HttpUrl_ReturnsHttpType()
    {
        var httpsResult = NotificationActionParser.TryParse("https://openhab.org");
        Assert.NotNull(httpsResult);
        Assert.Equal("https", httpsResult.Type);
        Assert.Equal("https://openhab.org", httpsResult.Payload);

        var httpResult = NotificationActionParser.TryParse("http://example.com");
        Assert.NotNull(httpResult);
        Assert.Equal("http", httpResult.Type);
        Assert.Equal("http://example.com", httpResult.Payload);
    }

    [Fact]
    public void Parse_HttpUrl_TrimmedInput_PreservesTrimmedAbsoluteUrlPayload()
    {
        var httpsResult = NotificationActionParser.TryParse("  https://openhab.org/docs  ");
        Assert.NotNull(httpsResult);
        Assert.Equal("https", httpsResult.Type);
        Assert.Equal("https://openhab.org/docs", httpsResult.Payload);

        var httpResult = NotificationActionParser.TryParse("  http://example.com/path?q=1  ");
        Assert.NotNull(httpResult);
        Assert.Equal("http", httpResult.Type);
        Assert.Equal("http://example.com/path?q=1", httpResult.Payload);
    }

    [Fact]
    public void Parse_TrimsTypeAndPayloadParts()
    {
        var result = NotificationActionParser.TryParse("  command  :  KitchenLights:ON  ");

        Assert.NotNull(result);
        Assert.Equal("command", result.Type);
        Assert.Equal("KitchenLights:ON", result.Payload);
    }

    [Fact]
    public void Parse_RuleAction_ReturnsCorrectTypeAndPayload()
    {
        var result = NotificationActionParser.TryParse("rule:02ffc3a297:prop1=foo");

        Assert.NotNull(result);
        Assert.Equal("rule", result.Type);
        Assert.Equal("02ffc3a297:prop1=foo", result.Payload);
    }

    [Fact]
    public void Parse_AppAction_ReturnsCorrectTypeAndPayload()
    {
        var result = NotificationActionParser.TryParse("app:android=com.sonos,ios=sonos://");

        Assert.NotNull(result);
        Assert.Equal("app", result.Type);
        Assert.Equal("android=com.sonos,ios=sonos://", result.Payload);
    }

    [Fact]
    public void Parse_UiNavigateAction_ReturnsCorrectTypeAndPayload()
    {
        var result = NotificationActionParser.TryParse("ui:navigate:/page/my_page");

        Assert.NotNull(result);
        Assert.Equal("ui", result.Type);
        Assert.Equal("navigate:/page/my_page", result.Payload);
    }

    [Fact]
    public void Parse_NullInput_ReturnsNull()
    {
        var result = NotificationActionParser.TryParse(null);

        Assert.Null(result);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsNull()
    {
        var result = NotificationActionParser.TryParse("");

        Assert.Null(result);
    }

    [Fact]
    public void Parse_WhitespaceInput_ReturnsNull()
    {
        var result = NotificationActionParser.TryParse("   ");

        Assert.Null(result);
    }

    [Fact]
    public void Parse_SimpleKeyValue_ReturnsCorrectTypeAndPayload()
    {
        // Just "type:value" — no extra colons
        var result = NotificationActionParser.TryParse("telephone:123-456-7890");

        Assert.NotNull(result);
        Assert.Equal("telephone", result.Type);
        Assert.Equal("123-456-7890", result.Payload);
    }

    // ─────────────────────────── TryParseButton ─────────────────────────

    [Fact]
    public void Parse_ButtonWithTitle_ExtractsTitleAndAction()
    {
        var result = NotificationActionParser.TryParseButton("Turn on light=command:KitchenLight:ON");

        Assert.NotNull(result);
        Assert.Equal("Turn on light", result.Title);
        Assert.Equal("command", result.Type);
        Assert.Equal("KitchenLight:ON", result.Payload);
    }

    [Fact]
    public void Parse_ButtonWithoutEquals_ReturnsNull()
    {
        var result = NotificationActionParser.TryParseButton("command:KitchenLight:ON");

        Assert.Null(result);
    }

    [Fact]
    public void Parse_ButtonTitleContainsEquals_ExtractsCorrectSplit()
    {
        // Title only goes until the first "=" - remainder is the action part
        var result = NotificationActionParser.TryParseButton("Volume=50=command:Speakers:50");

        Assert.NotNull(result);
        Assert.Equal("Volume", result.Title);
        Assert.Equal("50=command", result.Type);
        Assert.Equal("Speakers:50", result.Payload);
    }

    [Fact]
    public void Parse_ButtonWithHttpUrl_ExtractsCorrectly()
    {
        var result = NotificationActionParser.TryParseButton("Open Dashboard=https://dashboard.example.com");

        Assert.NotNull(result);
        Assert.Equal("Open Dashboard", result.Title);
        Assert.Equal("https", result.Type);
        Assert.Equal("https://dashboard.example.com", result.Payload);
    }

    [Fact]
    public void Parse_ButtonNullInput_ReturnsNull()
    {
        Assert.Null(NotificationActionParser.TryParseButton(null));
    }

    [Fact]
    public void Parse_ButtonEmptyString_ReturnsNull()
    {
        Assert.Null(NotificationActionParser.TryParseButton(""));
    }

    [Fact]
    public void Parse_ButtonRejectsEmptyTitle()
    {
        Assert.Null(NotificationActionParser.TryParseButton("   =command:KitchenLight:ON"));
    }

    [Fact]
    public void Parse_ButtonRejectsEmptyAction()
    {
        Assert.Null(NotificationActionParser.TryParseButton("Turn on light=   "));
    }

    [Fact]
    public void Parse_ButtonTrimsTitleAndAction()
    {
        var result = NotificationActionParser.TryParseButton("  Turn on light  =  command:KitchenLight:ON  ");

        Assert.NotNull(result);
        Assert.Equal("Turn on light", result.Title);
        Assert.Equal("command", result.Type);
        Assert.Equal("KitchenLight:ON", result.Payload);
    }

    [Fact]
    public void Button_ToRawButton_RoundTrips()
    {
        var button = new NotificationActionButton("Open", "ui", "navigate:/page/security");

        var raw = button.ToRawButton();
        var parsed = NotificationActionParser.TryParseButton(raw);

        Assert.Equal("Open=ui:navigate:/page/security", raw);
        Assert.NotNull(parsed);
        Assert.Equal(button, parsed);
    }
}
