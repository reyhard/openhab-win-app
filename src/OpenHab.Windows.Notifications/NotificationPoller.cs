using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using OpenHab.Core;

namespace OpenHab.Windows.Notifications;

public sealed class NotificationPoller : IDisposable, IAsyncDisposable
{
    private const int MaxSeenIds = 200;

    private readonly HttpClient httpClient;
    private readonly Uri cloudBaseUri;
    private readonly string? apiToken;
    private readonly string? basicUserName;
    private readonly string? basicPassword;
    private readonly TimeSpan pollInterval;
    private readonly HashSet<string> seenIds = new();
    private readonly DispatcherQueue? dispatcher;
    private readonly Func<string, bool>? isDismissedFunc;
    private readonly Action<NormalizedCloudNotification>? onNewNotification;
    private readonly Action<NotificationHideTarget>? onHideNotification;

    private int isStarted;
    private CancellationTokenSource? cts;
    private Task? pollingTask;

    public event EventHandler<NormalizedCloudNotification>? NotificationReceived;

    public NotificationPoller(
        HttpClient httpClient,
        Uri cloudBaseUri,
        string? apiToken = null,
        string? basicUserName = null,
        string? basicPassword = null,
        TimeSpan? pollInterval = null,
        DispatcherQueue? dispatcher = null,
        IReadOnlySet<string>? preSeenIds = null,
        Func<string, bool>? isDismissedFunc = null,
        Action<NormalizedCloudNotification>? onNewNotification = null,
        Action<NotificationHideTarget>? onHideNotification = null)
    {
        if (!string.IsNullOrWhiteSpace(apiToken) && !string.IsNullOrWhiteSpace(basicUserName))
        {
            throw new ArgumentException("Configure either bearer token auth or basic auth, not both.");
        }

        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.cloudBaseUri = cloudBaseUri ?? throw new ArgumentNullException(nameof(cloudBaseUri));
        this.apiToken = apiToken;
        this.basicUserName = basicUserName;
        this.basicPassword = basicPassword;
        this.pollInterval = pollInterval ?? TimeSpan.FromSeconds(30);
        this.dispatcher = dispatcher;
        this.isDismissedFunc = isDismissedFunc;
        this.onNewNotification = onNewNotification;
        this.onHideNotification = onHideNotification;

        if (preSeenIds is not null)
        {
            foreach (var id in preSeenIds)
                seenIds.Add(id);
        }
    }

    public bool IsRunning => pollingTask is not null && !pollingTask.IsCompleted;

    public TimeSpan PollInterval => pollInterval;

    public string? LastError { get; private set; }

    public void Start()
    {
        DiagnosticLogger.Info($"Notification polling started - endpoint: {cloudBaseUri.Host}");
        if (Interlocked.CompareExchange(ref isStarted, 1, 0) != 0) return;
        cts = new CancellationTokenSource();
        pollingTask = PollLoopAsync(cts.Token);
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref isStarted, 0) == 1)
        {
            DiagnosticLogger.Info("Notification polling stopped");
        }

        cts?.Cancel();
    }

    public async Task StopAsync()
    {
        Task? taskToAwait;
        CancellationTokenSource? ctsToDispose;
        var wasStarted = Interlocked.Exchange(ref isStarted, 0) == 1;
        taskToAwait = pollingTask;
        ctsToDispose = cts;
        if (!wasStarted && taskToAwait is null && ctsToDispose is null)
        {
            return;
        }

        if (wasStarted)
        {
            DiagnosticLogger.Info("Notification polling stopped");
        }

        ctsToDispose?.Cancel();

        if (taskToAwait is not null)
        {
            try
            {
                await taskToAwait.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (ReferenceEquals(pollingTask, taskToAwait))
                {
                    pollingTask = null;
                }
            }
        }

        if (ReferenceEquals(cts, ctsToDispose))
        {
            cts = null;
        }

        ctsToDispose?.Dispose();
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        DiagnosticLogger.Info("Notification poll loop started");
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DiagnosticLogger.Error("Poll error", ex);
                LastError = ex.Message;
                DiagnosticLogger.Info("Polling will retry after error");
            }

            try
            {
                await Task.Delay(pollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal Task PollOnceForTestingAsync(CancellationToken cancellationToken)
    {
        return PollOnceAsync(cancellationToken);
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(cloudBaseUri, "api/v1/notifications?limit=25"));

        if (apiToken is not null)
        {
            DiagnosticLogger.Info("Using Bearer auth");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        }
        else if (!string.IsNullOrWhiteSpace(basicUserName))
        {
            DiagnosticLogger.Info("Using Basic auth");
            var raw = $"{basicUserName}:{basicPassword ?? string.Empty}";
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64);
        }
        else
        {
            DiagnosticLogger.Warn("No auth - credentials missing");
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            DiagnosticLogger.Warn($"Cloud notifications returned HTTP {(int)response.StatusCode}");
            return;
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var notifications = await response.Content
            .ReadFromJsonAsync<List<CloudNotification>>(options, cancellationToken);

        if (notifications is null)
        {
            DiagnosticLogger.Warn("Notifications response was null");
            return;
        }

        var newCount = 0;
        foreach (var notification in notifications.OrderBy(n => n.Created))
        {
            var normalized = CloudNotificationNormalizer.Normalize(notification);
            if (!seenIds.Add(normalized.Id))
            {
                continue;
            }

            if (normalized.Kind == CloudNotificationKind.Hide)
            {
                if (normalized.HideTargets.Count == 0)
                {
                    DiagnosticLogger.Warn($"Hide notification has no supported targets: Id={normalized.Id}");
                }

                foreach (var hideTarget in normalized.HideTargets)
                {
                    DiagnosticLogger.Info(
                        $"Hide notification received: Id={normalized.Id}, Target={hideTarget.Kind}, Value={hideTarget.Value}");
                    onHideNotification?.Invoke(hideTarget);
                }

                continue;
            }

            if (isDismissedFunc is not null && isDismissedFunc(normalized.Id))
            {
                DiagnosticLogger.Info($"Skipping dismissed notification: Id={normalized.Id}");
                continue;
            }

            newCount++;
            DiagnosticLogger.Info($"New notification: Id={normalized.Id}, Kind={normalized.Kind}");
            onNewNotification?.Invoke(normalized);
            if (normalized.Kind == CloudNotificationKind.Push)
            {
                RaiseNotification(normalized);
            }
        }

        DiagnosticLogger.Info($"Polled {newCount} new notifications out of {notifications.Count} total");

        if (seenIds.Count > MaxSeenIds)
        {
            seenIds.Clear();
        }
    }

    private void RaiseNotification(NormalizedCloudNotification notification)
    {
        if (dispatcher is not null)
            dispatcher.TryEnqueue(() => NotificationReceived?.Invoke(this, notification));
        else
            NotificationReceived?.Invoke(this, notification);
    }
}
