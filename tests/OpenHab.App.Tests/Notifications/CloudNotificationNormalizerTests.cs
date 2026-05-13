using System.Text.Json;
using OpenHab.Windows.Notifications;

namespace OpenHab.App.Tests.Notifications;

public sealed class CloudNotificationNormalizerTests
{
    private static CloudNotification Deserialize(string json)
    {
        return JsonSerializer.Deserialize<CloudNotification>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    [Fact]
    public void Normalize_PrefersNestedPayloadAdvancedFields()
    {
        var raw = Deserialize("""
        {
          "_id": "cloud-1",
          "message": "Fallback message",
          "created": "2026-05-12T10:00:00Z",
          "title": "Flat Title",
          "tag": "flat-tag",
          "payload": {
            "message": "Motion detected in the apartment!",
            "title": "Motion Detected",
            "icon": "motion",
            "tag": "Motion Tag",
            "reference-id": "motion-id-1234",
            "media-attachment-url": "item:VaccumingRobot_01_CleaningMap",
            "on-click-action": "ui:navigate:/page/security",
            "actionButton1": "Turn on the light=command:BulbDesk_01_Switch:ON",
            "actionButton2": "Dismiss=command:BulbKitchen_01_Switch:ON"
          }
        }
        """);

        var normalized = CloudNotificationNormalizer.Normalize(raw);

        Assert.Equal("cloud-1", normalized.Id);
        Assert.Equal("Motion detected in the apartment!", normalized.Message);
        Assert.Equal("Motion Detected", normalized.Title);
        Assert.Equal("motion", normalized.Icon);
        Assert.Equal("Motion Tag", normalized.Tag);
        Assert.Equal("motion-id-1234", normalized.ReferenceId);
        Assert.Equal("item:VaccumingRobot_01_CleaningMap", normalized.MediaAttachmentUrl);
        Assert.Equal("ui:navigate:/page/security", normalized.OnClickAction);
        Assert.Equal(CloudNotificationKind.Push, normalized.Kind);
        Assert.Collection(
            normalized.ActionButtons,
            first =>
            {
                Assert.Equal("Turn on the light", first.Title);
                Assert.Equal("command", first.Type);
                Assert.Equal("BulbDesk_01_Switch:ON", first.Payload);
            },
            second =>
            {
                Assert.Equal("Dismiss", second.Title);
                Assert.Equal("command", second.Type);
                Assert.Equal("BulbKitchen_01_Switch:ON", second.Payload);
            });
    }

    [Fact]
    public void Normalize_FallsBackToFlatLegacyFields()
    {
        var raw = Deserialize("""
        {
          "_id": "legacy-1",
          "message": "Legacy body",
          "created": "2026-05-12T10:00:00Z",
          "title": "Legacy title",
          "icon": "motion",
          "severity": "Warning",
          "referenceId": "legacy-reference",
          "onClickAction": "https://openhab.org",
          "mediaAttachmentUrl": "https://example.test/camera.jpg",
          "actionButton1": "Open=https://example.test"
        }
        """);

        var normalized = CloudNotificationNormalizer.Normalize(raw);

        Assert.Equal("Legacy body", normalized.Message);
        Assert.Equal("Legacy title", normalized.Title);
        Assert.Equal("motion", normalized.Icon);
        Assert.Equal("Warning", normalized.Tag);
        Assert.Equal("legacy-reference", normalized.ReferenceId);
        Assert.Equal("https://openhab.org", normalized.OnClickAction);
        Assert.Equal("https://example.test/camera.jpg", normalized.MediaAttachmentUrl);
        Assert.Single(normalized.ActionButtons);
    }

    [Fact]
    public void Normalize_ClassifiesLogOnlyPayload()
    {
        var raw = Deserialize("""
        {
          "_id": "log-1",
          "message": "Saved only",
          "created": "2026-05-12T10:00:00Z",
          "payload": {
            "message": "Saved only",
            "type": "log"
          }
        }
        """);

        var normalized = CloudNotificationNormalizer.Normalize(raw);

        Assert.Equal(CloudNotificationKind.LogOnly, normalized.Kind);
        Assert.Empty(normalized.HideTargets);
    }

    [Fact]
    public void Normalize_ClassifiesLogOnlyTypePayload()
    {
        var raw = Deserialize("""
        {
          "_id": "log-only-1",
          "message": "Saved only",
          "created": "2026-05-12T10:00:00Z",
          "payload": {
            "message": "Saved only",
            "type": "logOnly"
          }
        }
        """);

        var normalized = CloudNotificationNormalizer.Normalize(raw);

        Assert.Equal(CloudNotificationKind.LogOnly, normalized.Kind);
        Assert.Empty(normalized.HideTargets);
    }

    [Fact]
    public void Normalize_ClassifiesHideByReferenceIdAndTag()
    {
        var raw = Deserialize("""
        {
          "_id": "hide-1",
          "message": "",
          "created": "2026-05-12T10:00:00Z",
          "payload": {
            "type": "hideNotification",
            "reference-id": "motion-id-1234",
            "tag": "Motion Tag"
          }
        }
        """);

        var normalized = CloudNotificationNormalizer.Normalize(raw);

        Assert.Equal(CloudNotificationKind.Hide, normalized.Kind);
        Assert.Contains(normalized.HideTargets, t => t.Kind == NotificationHideTargetKind.ReferenceId && t.Value == "motion-id-1234");
        Assert.Contains(normalized.HideTargets, t => t.Kind == NotificationHideTargetKind.Tag && t.Value == "Motion Tag");
    }

    [Fact]
    public void Normalize_ClassifiesFlatLogOnlyTypeWhenPayloadTypeMissing()
    {
        var raw = Deserialize("""
        {
          "_id": "flat-log-only-1",
          "message": "Saved only",
          "created": "2026-05-12T10:00:00Z",
          "type": "logOnly",
          "payload": {
            "message": "Saved only"
          }
        }
        """);

        var normalized = CloudNotificationNormalizer.Normalize(raw);

        Assert.Equal(CloudNotificationKind.LogOnly, normalized.Kind);
    }

    [Fact]
    public void Normalize_ClassifiesFlatHideNotificationTypeWhenPayloadTypeMissing()
    {
        var raw = Deserialize("""
        {
          "_id": "flat-hide-1",
          "message": "",
          "created": "2026-05-12T10:00:00Z",
          "type": "hideNotification",
          "referenceId": "motion-id-1234",
          "tag": "Motion Tag",
          "payload": {}
        }
        """);

        var normalized = CloudNotificationNormalizer.Normalize(raw);

        Assert.Equal(CloudNotificationKind.Hide, normalized.Kind);
        Assert.Contains(normalized.HideTargets, t => t.Kind == NotificationHideTargetKind.ReferenceId && t.Value == "motion-id-1234");
        Assert.Contains(normalized.HideTargets, t => t.Kind == NotificationHideTargetKind.Tag && t.Value == "Motion Tag");
    }

    [Fact]
    public void Normalize_ParsesPayloadActionsArray()
    {
        var raw = Deserialize("""
        {
          "_id": "actions-1",
          "message": "Body",
          "created": "2026-05-12T10:00:00Z",
          "payload": {
            "actions": [
              "Light=command:Light:ON",
              { "title": "Main UI", "action": "ui:navigate:/page/overview" }
            ]
          }
        }
        """);

        var normalized = CloudNotificationNormalizer.Normalize(raw);

        Assert.Collection(
            normalized.ActionButtons,
            first => Assert.Equal("Light", first.Title),
            second => Assert.Equal("Main UI", second.Title));
    }

    [Fact]
    public void Normalize_TruncatesActionsToThreeButtons()
    {
        var raw = Deserialize("""
        {
          "_id": "actions-2",
          "message": "Body",
          "created": "2026-05-12T10:00:00Z",
          "payload": {
            "actionButton1": "One=command:Light:ON",
            "actionButton2": "Two=command:Fan:ON",
            "actionButton3": "Three=command:Door:OPEN",
            "actions": [
              "Four=command:Alarm:ON",
              { "title": "Five", "action": "ui:navigate:/page/overview" }
            ]
          }
        }
        """);

        var normalized = CloudNotificationNormalizer.Normalize(raw);

        Assert.Equal(3, normalized.ActionButtons.Count);
        Assert.Collection(
            normalized.ActionButtons,
            first => Assert.Equal("One", first.Title),
            second => Assert.Equal("Two", second.Title),
            third => Assert.Equal("Three", third.Title));
    }
}
