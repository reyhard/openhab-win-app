using System.Net.Http.Json;
using System.Text.Json;

namespace OpenHab.Windows.Notifications;

public sealed class NotificationPoller : IDisposable
{
    private readonly HttpClient httpClient;
    private readonly Uri cloudBaseUri;
    private readonly string? apiToken;
    private readonly TimeSpan pollInterval;
    private readonly HashSet<string> seenIds = new();

    private CancellationTokenSource? cts;
    private Task? pollingTask;

    public event EventHandler<CloudNotification>? NotificationReceived;

    public NotificationPoller(
        HttpClient httpClient,
        Uri cloudBaseUri,
        string? apiToken = null,
        TimeSpan? pollInterval = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.cloudBaseUri = cloudBaseUri ?? throw new ArgumentNullException(nameof(cloudBaseUri));
        this.apiToken = apiToken;
        this.pollInterval = pollInterval ?? TimeSpan.FromSeconds(60);
    }

    public bool IsRunning => pollingTask is not null && !pollingTask.IsCompleted;

    public void Start()
    {
        if (IsRunning) return;
        cts = new CancellationTokenSource();
        pollingTask = PollLoopAsync(cts.Token);
    }

    public void Stop()
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
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
            catch
            {
                // Silently ignore polling errors — next cycle will retry.
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
                NotificationReceived?.Invoke(this, notification);
            }
        }
    }
}
