using System.Net;
using System.Text;
using OpenHab.Windows.Notifications;

namespace OpenHab.App.Tests.Notifications;

public sealed class NotificationPollerTests
{
    [Fact]
    public async Task PollOnce_Push_RaisesAndStoresNormalizedNotification()
    {
        using var poller = CreatePoller(
            """
            [
              {
                "_id": "push-1",
                "message": "Fallback",
                "created": "2026-05-12T10:00:00Z",
                "payload": {
                  "message": "Motion detected",
                  "title": "Motion",
                  "type": "push"
                }
              }
            ]
            """,
            out var raised,
            out var stored,
            out var hidden);

        await poller.PollOnceForTestingAsync(CancellationToken.None);

        Assert.Single(raised);
        Assert.Single(stored);
        Assert.Empty(hidden);
        Assert.Equal("push-1", raised[0].Id);
        Assert.Equal("Motion detected", raised[0].Message);
        Assert.Equal("Motion", raised[0].Title);
        Assert.Equal(CloudNotificationKind.Push, raised[0].Kind);
        Assert.Equal("push-1", stored[0].Id);
    }

    [Fact]
    public async Task PollOnce_LogOnly_StoresButDoesNotRaiseToastEvent()
    {
        using var poller = CreatePoller(
            """
            [
              {
                "_id": "log-1",
                "message": "Saved only",
                "created": "2026-05-12T10:00:00Z",
                "payload": {
                  "message": "Saved only",
                  "type": "logOnly"
                }
              }
            ]
            """,
            out var raised,
            out var stored,
            out var hidden);

        await poller.PollOnceForTestingAsync(CancellationToken.None);

        Assert.Empty(raised);
        Assert.Single(stored);
        Assert.Empty(hidden);
        Assert.Equal("log-1", stored[0].Id);
        Assert.Equal(CloudNotificationKind.LogOnly, stored[0].Kind);
    }

    [Fact]
    public async Task PollOnce_Hide_InvokesHideCallbackOncePerTargetWithoutStoreOrToast()
    {
        using var poller = CreatePoller(
            """
            [
              {
                "_id": "hide-1",
                "message": "",
                "created": "2026-05-12T10:00:00Z",
                "payload": {
                  "type": "hideNotification",
                  "reference-id": "motion-123",
                  "tag": "Motion"
                }
              }
            ]
            """,
            out var raised,
            out var stored,
            out var hidden);

        await poller.PollOnceForTestingAsync(CancellationToken.None);

        Assert.Empty(raised);
        Assert.Empty(stored);
        Assert.Equal(2, hidden.Count);
        Assert.Contains(hidden, t => t.Kind == NotificationHideTargetKind.ReferenceId && t.Value == "motion-123");
        Assert.Contains(hidden, t => t.Kind == NotificationHideTargetKind.Tag && t.Value == "Motion");
    }

    [Fact]
    public async Task PollOnce_DoesNotRepeatPushOrLogForAlreadySeenNormalizedIds()
    {
        using var poller = CreatePoller(
            """
            [
              {
                "_id": "push-seen",
                "message": "Body",
                "created": "2026-05-12T10:00:00Z"
              },
              {
                "_id": "log-seen",
                "message": "Saved only",
                "created": "2026-05-12T10:01:00Z",
                "payload": { "type": "logOnly" }
              }
            ]
            """,
            out var raised,
            out var stored,
            out _,
            preSeenIds: new HashSet<string> { "push-seen", "log-seen" });

        await poller.PollOnceForTestingAsync(CancellationToken.None);

        Assert.Empty(raised);
        Assert.Empty(stored);
    }

    [Fact]
    public async Task PollOnce_SkipsDismissedPushAndLogIds()
    {
        using var poller = CreatePoller(
            """
            [
              {
                "_id": "push-dismissed",
                "message": "Body",
                "created": "2026-05-12T10:00:00Z"
              },
              {
                "_id": "log-dismissed",
                "message": "Saved only",
                "created": "2026-05-12T10:01:00Z",
                "payload": { "type": "logOnly" }
              }
            ]
            """,
            out var raised,
            out var stored,
            out _,
            isDismissed: id => id is "push-dismissed" or "log-dismissed");

        await poller.PollOnceForTestingAsync(CancellationToken.None);

        Assert.Empty(raised);
        Assert.Empty(stored);
    }

    private static NotificationPoller CreatePoller(
        string json,
        out List<NormalizedCloudNotification> raised,
        out List<NormalizedCloudNotification> stored,
        out List<NotificationHideTarget> hidden,
        IReadOnlySet<string>? preSeenIds = null,
        Func<string, bool>? isDismissed = null)
    {
        var localRaised = new List<NormalizedCloudNotification>();
        var localStored = new List<NormalizedCloudNotification>();
        var localHidden = new List<NotificationHideTarget>();

        raised = localRaised;
        stored = localStored;
        hidden = localHidden;

        var httpClient = new HttpClient(new FixedJsonHandler(json));
        var poller = new NotificationPoller(
            httpClient,
            new Uri("https://example.test/"),
            preSeenIds: preSeenIds,
            isDismissedFunc: isDismissed,
            onNewNotification: n => localStored.Add(n),
            onHideNotification: t => localHidden.Add(t));
        poller.NotificationReceived += (_, notification) => localRaised.Add(notification);
        return poller;
    }

    private sealed class FixedJsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
