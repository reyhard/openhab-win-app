using System.Text.RegularExpressions;
using OpenHab.Windows.Notifications;

namespace OpenHab.App.Tests.Notifications;

public sealed partial class ToastNotificationXmlBuilderTests
{
    [Fact]
    public void Build_IncludesTitleBodyHeroLogoHeaderLaunchActionsAndUrgentScenario()
    {
        var request = new ToastNotificationRequest(
            Title: "Motion Detected",
            Body: "Garage camera saw motion",
            Actions:
            [
                new NotificationActionButton("Open", "ui", "navigate:/page/security"),
                new NotificationActionButton("Light On", "command", "GarageLight:ON")
            ],
            LaunchAction: "ui:navigate:/page/security",
            Important: true,
            Header: "Security",
            Tag: "motion-tag",
            ReferenceId: "event-123",
            AppLogoOverrideUri: new Uri("ms-appdata:///local/icons/motion.png"),
            HeroImageUri: new Uri("ms-appdata:///local/hero/motion.jpg"));

        var xml = ToastNotificationXmlBuilder.Build(request).GetXml();

        Assert.Contains("<text>Motion Detected</text>", xml, StringComparison.Ordinal);
        Assert.Contains("<text>Garage camera saw motion</text>", xml, StringComparison.Ordinal);
        Assert.Contains("placement=\"hero\"", xml, StringComparison.Ordinal);
        Assert.Contains("src=\"ms-appdata:///local/hero/motion.jpg\"", xml, StringComparison.Ordinal);
        Assert.Contains("placement=\"appLogoOverride\"", xml, StringComparison.Ordinal);
        Assert.Contains("src=\"ms-appdata:///local/icons/motion.png\"", xml, StringComparison.Ordinal);
        Assert.Contains("scenario=\"urgent\"", xml, StringComparison.Ordinal);
        Assert.Contains("launch=\"ui:navigate:/page/security\"", xml, StringComparison.Ordinal);
        Assert.Contains("<header", xml, StringComparison.Ordinal);
        Assert.Contains("title=\"Security\"", xml, StringComparison.Ordinal);
        Assert.Contains("content=\"Open\"", xml, StringComparison.Ordinal);
        Assert.Contains("content=\"Light On\"", xml, StringComparison.Ordinal);
        Assert.Contains("arguments=\"ui:navigate:/page/security\"", xml, StringComparison.Ordinal);
        Assert.Contains("arguments=\"command:GarageLight:ON\"", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_EmitsParserCompatibleActivationArguments()
    {
        var request = new ToastNotificationRequest(
            Title: "Title",
            Body: "Body",
            Actions:
            [
                new NotificationActionButton("Open", "ui", "navigate:/page/security"),
                new NotificationActionButton("Light On", "command", "GarageLight:ON")
            ],
            LaunchAction: "ui:navigate:/page/security");

        var xml = ToastNotificationXmlBuilder.Build(request).GetXml();

        var launch = ExtractAttribute(xml, "launch");
        var action1 = ExtractActionArgument(xml, 0);
        var action2 = ExtractActionArgument(xml, 1);

        var parsedLaunch = NotificationActionParser.TryParse(launch);
        var parsedAction1 = NotificationActionParser.TryParse(action1);
        var parsedAction2 = NotificationActionParser.TryParse(action2);

        Assert.NotNull(parsedLaunch);
        Assert.Equal("ui", parsedLaunch!.Type);
        Assert.Equal("navigate:/page/security", parsedLaunch.Payload);

        Assert.NotNull(parsedAction1);
        Assert.Equal("ui", parsedAction1!.Type);
        Assert.Equal("navigate:/page/security", parsedAction1.Payload);

        Assert.NotNull(parsedAction2);
        Assert.Equal("command", parsedAction2!.Type);
        Assert.Equal("GarageLight:ON", parsedAction2.Payload);
    }

    [Fact]
    public void Build_LimitsButtonsToThree()
    {
        var request = new ToastNotificationRequest(
            Title: "Title",
            Body: "Body",
            Actions:
            [
                new NotificationActionButton("1", "command", "One:ON"),
                new NotificationActionButton("2", "command", "Two:ON"),
                new NotificationActionButton("3", "command", "Three:ON"),
                new NotificationActionButton("4", "command", "Four:ON")
            ]);

        var xml = ToastNotificationXmlBuilder.Build(request).GetXml();

        Assert.Contains("content=\"1\"", xml, StringComparison.Ordinal);
        Assert.Contains("content=\"2\"", xml, StringComparison.Ordinal);
        Assert.Contains("content=\"3\"", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("content=\"4\"", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTagAndGroup_PrefersReferenceIdAndFallsBackToTag_WithStableBoundedValues()
    {
        var fromReference = ToastNotificationXmlBuilder.BuildTagAndGroup(
            new ToastNotificationRequest("t", "b", ReferenceId: "ref-42", Tag: "ignored"));
        var fromReferenceAgain = ToastNotificationXmlBuilder.BuildTagAndGroup(
            new ToastNotificationRequest("t", "b", ReferenceId: "ref-42", Tag: "different"));

        Assert.Equal("openhab-ref", fromReference.Group);
        Assert.StartsWith("ref-", fromReference.Tag, StringComparison.Ordinal);
        Assert.Equal(20, fromReference.Tag!.Length);
        Assert.Equal(fromReference.Tag, fromReferenceAgain.Tag);

        var fromTag = ToastNotificationXmlBuilder.BuildTagAndGroup(
            new ToastNotificationRequest("t", "b", Tag: "priority"));
        var fromTagAgain = ToastNotificationXmlBuilder.BuildTagAndGroup(
            new ToastNotificationRequest("t", "b", Tag: "priority"));

        Assert.Equal("openhab-tag", fromTag.Group);
        Assert.StartsWith("tag-", fromTag.Tag, StringComparison.Ordinal);
        Assert.Equal(20, fromTag.Tag!.Length);
        Assert.Equal(fromTag.Tag, fromTagAgain.Tag);

        var fallback = ToastNotificationXmlBuilder.BuildTagAndGroup(
            new ToastNotificationRequest("t", "b"));

        Assert.Null(fallback.Tag);
        Assert.Null(fallback.Group);
    }

    [Fact]
    public void Build_UsesBoundedStableHeaderId()
    {
        var request = new ToastNotificationRequest("t", "b", Header: "h", ReferenceId: "ref-42");
        var fromReference = ToastNotificationXmlBuilder.Build(request).GetXml();
        var fromReferenceAgain = ToastNotificationXmlBuilder.Build(request).GetXml();

        var id1 = ExtractHeaderId(fromReference);
        var id2 = ExtractHeaderId(fromReferenceAgain);

        Assert.StartsWith("hdr-ref-", id1, StringComparison.Ordinal);
        Assert.Equal(24, id1.Length);
        Assert.Equal(id1, id2);

        var fromTag = ToastNotificationXmlBuilder.Build(
            new ToastNotificationRequest("t", "b", Header: "h", Tag: "priority")).GetXml();
        var tagId = ExtractHeaderId(fromTag);

        Assert.StartsWith("hdr-tag-", tagId, StringComparison.Ordinal);
        Assert.Equal(24, tagId.Length);
    }

    private static string ExtractAttribute(string xml, string attribute)
    {
        var match = Regex.Match(xml, $"\\b{attribute}=\\\"([^\\\"]+)\\\"", RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"Missing '{attribute}' attribute in XML: {xml}");
        return match.Groups[1].Value;
    }

    private static string ExtractActionArgument(string xml, int index)
    {
        var matches = Regex.Matches(xml, "<action[^>]*\\barguments=\\\"([^\\\"]+)\\\"", RegexOptions.CultureInvariant);
        Assert.True(matches.Count > index, $"Missing action argument at index {index} in XML: {xml}");
        return matches[index].Groups[1].Value;
    }

    private static string ExtractHeaderId(string xml)
    {
        var match = Regex.Match(xml, "<header[^>]*\\bid=\\\"([^\\\"]+)\\\"", RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"Missing header id in XML: {xml}");
        return match.Groups[1].Value;
    }
}
