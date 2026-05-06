using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using OpenHab.Core;

namespace OpenHab.Windows.Notifications;

public sealed class NotificationPoller : IDisposable
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

    private int isStarted;
    private CancellationTokenSource? cts;
    private Task? pollingTask;

    public event EventHandler<CloudNotification>? NotificationReceived;

    public NotificationPoller(
        HttpClient httpClient,
        Uri cloudBaseUri,
        string? apiToken = null,
        string? basicUserName = null,
        string? basicPassword = null,
        TimeSpan? pollInterval = null,
        DispatcherQueue? dispatcher = null)
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
        this.pollInterval = pollInterval ?? TimeSpan.FromSeconds(60);
        this.dispatcher = dispatcher;
    }

    public bool IsRunning => pollingTask is not null && !pollingTask.IsCompleted;

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
        DiagnosticLogger.Info("Notification polling stopped");
        if (Interlocked.CompareExchange(ref isStarted, 0, 1) != 1) return;
        cts?.Cancel();
    }

    public void Dispose()
    {
        Stop();
        cts?.Dispose();
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

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(cloudBaseUri, "api/v1/notifications"));

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
            if (seenIds.Add(notification.Id))
            {
                newCount++;
                DiagnosticLogger.Info($"New notification: Id={notification.Id}, Severity={notification.Severity}");
                RaiseNotification(notification);
            }
        }

        DiagnosticLogger.Info($"Polled {newCount} new notifications out of {notifications.Count} total");

        if (seenIds.Count > MaxSeenIds)
        {
            seenIds.Clear();
        }
    }

    private void RaiseNotification(CloudNotification notification)
    {
        if (dispatcher is not null)
            dispatcher.TryEnqueue(() => NotificationReceived?.Invoke(this, notification));
        else
            NotificationReceived?.Invoke(this, notification);
    }
}
