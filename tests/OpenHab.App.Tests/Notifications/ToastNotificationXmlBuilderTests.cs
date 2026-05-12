using OpenHab.Windows.Notifications;

namespace OpenHab.App.Tests.Notifications;

public sealed class ToastNotificationXmlBuilderTests
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
        Assert.Contains("launch=\"action=ui%3Anavigate%3A%2Fpage%2Fsecurity\"", xml, StringComparison.Ordinal);
        Assert.Contains("<header", xml, StringComparison.Ordinal);
        Assert.Contains("title=\"Security\"", xml, StringComparison.Ordinal);
        Assert.Contains("content=\"Open\"", xml, StringComparison.Ordinal);
        Assert.Contains("content=\"Light On\"", xml, StringComparison.Ordinal);
        Assert.Contains("arguments=\"action=ui%3Anavigate%3A%2Fpage%2Fsecurity\"", xml, StringComparison.Ordinal);
        Assert.Contains("arguments=\"action=command%3AGarageLight%3AON\"", xml, StringComparison.Ordinal);
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
    public void BuildTagAndGroup_PrefersReferenceIdAndFallsBackToTag()
    {
        var fromReference = ToastNotificationXmlBuilder.BuildTagAndGroup(
            new ToastNotificationRequest("t", "b", ReferenceId: "ref-42", Tag: "ignored"));

        Assert.Equal("ref-42", fromReference.Tag);
        Assert.Equal("openhab-ref", fromReference.Group);

        var fromTag = ToastNotificationXmlBuilder.BuildTagAndGroup(
            new ToastNotificationRequest("t", "b", Tag: "priority"));

        Assert.Equal("priority", fromTag.Tag);
        Assert.Equal("openhab-tag", fromTag.Group);

        var fallback = ToastNotificationXmlBuilder.BuildTagAndGroup(
            new ToastNotificationRequest("t", "b"));

        Assert.Null(fallback.Tag);
        Assert.Null(fallback.Group);
    }
}
