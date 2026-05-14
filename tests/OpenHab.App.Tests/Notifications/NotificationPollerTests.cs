using System.Net;
using System.Text;
using OpenHab.App.Settings;
using OpenHab.Core.Profiles;
using OpenHab.Windows.Notifications;
using TrayApp = global::OpenHab.Windows.Tray.App;

namespace OpenHab.App.Tests.Notifications;

public sealed class NotificationPollerTests
{
    [Fact]
    public void Constructor_UsesProvidedPollInterval()
    {
        using var httpClient = new HttpClient(new FixedJsonHandler("[]"));
        using var poller = new NotificationPoller(
            httpClient,
            new Uri("https://example.test/"),
            pollInterval: TimeSpan.FromSeconds(90));

        Assert.Equal(TimeSpan.FromSeconds(90), poller.PollInterval);
    }

    [Fact]
    public void BuildNotificationPollingConfig_ChangesWhenCloudCredentialsChange()
    {
        var settings = AppSettings.Default with
        {
            EndpointMode = EndpointMode.CloudOnly
        };

        var firstPassword = TrayApp.BuildNotificationPollingConfig(
            settings,
            new CloudCredentials("user@example.com", "password-one"));
        var secondPassword = TrayApp.BuildNotificationPollingConfig(
            settings,
            new CloudCredentials("user@example.com", "password-two"));

        Assert.NotEqual(firstPassword, secondPassword);

        var firstUser = TrayApp.BuildNotificationPollingConfig(
            settings,
            new CloudCredentials("first-user@example.com", "shared-password"));
        var secondUser = TrayApp.BuildNotificationPollingConfig(
            settings,
            new CloudCredentials("second-user@example.com", "shared-password"));

        Assert.NotEqual(firstUser, secondUser);
    }

    [Fact]
    public void ShouldReconfigureNotificationPolling_ReturnsFalseForMatchingConfig()
    {
        var settings = AppSettings.Default with
        {
            EndpointMode = EndpointMode.CloudOnly
        };
        var config = TrayApp.BuildNotificationPollingConfig(
            settings,
            new CloudCredentials("user@example.com", "password"));

        var shouldReconfigure = TrayApp.ShouldReconfigureNotificationPolling(config, config);

        Assert.False(shouldReconfigure);
    }

    [Fact]
    public void ShouldReconfigureNotificationPolling_ReturnsTrueWhenActiveConfigMissing()
    {
        var settings = AppSettings.Default with
        {
            EndpointMode = EndpointMode.CloudOnly
        };
        var nextConfig = TrayApp.BuildNotificationPollingConfig(
            settings,
            new CloudCredentials("user@example.com", "password"));

        var shouldReconfigure = TrayApp.ShouldReconfigureNotificationPolling(null, nextConfig);

        Assert.True(shouldReconfigure);
    }

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
    public async Task PollOnce_RequestsAtMost25Notifications()
    {
        var handler = new FixedJsonHandler("[]");
        using var httpClient = new HttpClient(handler);
        using var poller = new NotificationPoller(httpClient, new Uri("https://example.test/"));

        await poller.PollOnceForTestingAsync(CancellationToken.None);

        Assert.Equal("https://example.test/api/v1/notifications?limit=25", handler.LastRequestUri?.ToString());
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
    public async Task PollOnce_Hide_DeduplicatesSameHideIdAcrossPolls()
    {
        using var poller = CreatePoller(
            """
            [
              {
                "_id": "hide-repeat-1",
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
        await poller.PollOnceForTestingAsync(CancellationToken.None);

        Assert.Empty(raised);
        Assert.Empty(stored);
        Assert.Equal(2, hidden.Count);
        Assert.Equal(1, hidden.Count(t => t.Kind == NotificationHideTargetKind.ReferenceId && t.Value == "motion-123"));
        Assert.Equal(1, hidden.Count(t => t.Kind == NotificationHideTargetKind.Tag && t.Value == "Motion"));
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

    [Fact]
    public async Task StopAsync_WaitsForInFlightPollToObserveCancellationBeforeReturning()
    {
        var pollStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeCancellationPath = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var httpClient = new HttpClient(new BlockingHandler(
            onSend: async cancellationToken =>
            {
                pollStarted.TrySetResult();
                using var _ = cancellationToken.Register(() => cancellationObserved.TrySetResult());
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("[]", Encoding.UTF8, "application/json")
                    };
                }
                catch (OperationCanceledException)
                {
                    await completeCancellationPath.Task;
                    throw;
                }
            }));
        await using var poller = new NotificationPoller(
            httpClient,
            new Uri("https://example.test/"),
            pollInterval: TimeSpan.FromSeconds(10));

        poller.Start();
        await pollStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var stopTask = poller.StopAsync();
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(stopTask.IsCompleted);

        completeCancellationPath.TrySetResult();
        await stopTask;
    }

    [Fact]
    public async Task StopThenStopAsync_StillWaitsForInFlightCleanup()
    {
        var pollStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeCancellationPath = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var httpClient = new HttpClient(new BlockingHandler(
            onSend: async cancellationToken =>
            {
                pollStarted.TrySetResult();
                using var _ = cancellationToken.Register(() => cancellationObserved.TrySetResult());
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("[]", Encoding.UTF8, "application/json")
                    };
                }
                catch (OperationCanceledException)
                {
                    await completeCancellationPath.Task;
                    throw;
                }
            }));

        await using var poller = new NotificationPoller(
            httpClient,
            new Uri("https://example.test/"),
            pollInterval: TimeSpan.FromSeconds(10));

        poller.Start();
        await pollStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        poller.Stop();
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var cleanupTask = poller.StopAsync();
        Assert.False(cleanupTask.IsCompleted);

        completeCancellationPath.TrySetResult();
        await cleanupTask;
    }

    [Fact]
    public async Task Dispose_CancelsWithoutWaitingForInFlightCleanup()
    {
        var pollStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeCancellationPath = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var httpClient = new HttpClient(new BlockingHandler(
            onSend: async cancellationToken =>
            {
                pollStarted.TrySetResult();
                using var _ = cancellationToken.Register(() => cancellationObserved.TrySetResult());
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("[]", Encoding.UTF8, "application/json")
                    };
                }
                catch (OperationCanceledException)
                {
                    await completeCancellationPath.Task;
                    throw;
                }
            }));

        var poller = new NotificationPoller(
            httpClient,
            new Uri("https://example.test/"),
            pollInterval: TimeSpan.FromSeconds(10));

        poller.Start();
        await pollStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var disposeTask = Task.Run(poller.Dispose);
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            var completed = await Task.WhenAny(disposeTask, Task.Delay(250));

            Assert.Same(disposeTask, completed);
        }
        finally
        {
            completeCancellationPath.TrySetResult();
        }

        await disposeTask;
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
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class BlockingHandler(Func<CancellationToken, Task<HttpResponseMessage>> onSend) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return onSend(cancellationToken);
        }
    }
}
