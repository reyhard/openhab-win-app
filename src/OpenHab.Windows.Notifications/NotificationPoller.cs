using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.UI.Dispatching;

namespace OpenHab.Windows.Notifications;

public sealed class NotificationPoller : IDisposable
{
    private const int MaxSeenIds = 200;

    private readonly HttpClient httpClient;
    private readonly Uri cloudBaseUri;
    private readonly string? apiToken;
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
        TimeSpan? pollInterval = null,
        DispatcherQueue? dispatcher = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.cloudBaseUri = cloudBaseUri ?? throw new ArgumentNullException(nameof(cloudBaseUri));
        this.apiToken = apiToken;
        this.pollInterval = pollInterval ?? TimeSpan.FromSeconds(60);
        this.dispatcher = dispatcher;
    }

    public bool IsRunning => pollingTask is not null && !pollingTask.IsCompleted;

    public string? LastError { get; private set; }

    public void Start()
    {
        if (Interlocked.CompareExchange(ref isStarted, 1, 0) != 0) return;
        cts = new CancellationTokenSource();
        pollingTask = PollLoopAsync(cts.Token);
    }

    public void Stop()
    {
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
                LastError = ex.Message;
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
            new Uri(cloudBaseUri, "rest/notifications?limit=20"));

        if (apiToken is not null)
        {
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiToken);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var notifications = await response.Content
            .ReadFromJsonAsync<List<CloudNotification>>(options, cancellationToken);

        if (notifications is null) return;

        foreach (var notification in notifications.OrderBy(n => n.Created))
        {
            if (seenIds.Add(notification.Id))
            {
                RaiseNotification(notification);
            }
        }

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
