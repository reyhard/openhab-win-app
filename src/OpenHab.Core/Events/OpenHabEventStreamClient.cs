using System.Net.Http.Headers;
using System.Text;

namespace OpenHab.Core.Events;

public sealed class OpenHabEventStreamClient : IOpenHabEventStreamClient
{
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _initialBackoff;
    private readonly TimeSpan _maxBackoff;
    private readonly double _backoffMultiplier;
    private readonly string? _apiToken;
    private readonly string? _basicUserName;
    private readonly string? _basicPassword;

    private int _isConnected;
    private CancellationTokenSource? _internalCts;
    private Uri? _sseUri;

    public event EventHandler<OpenHabEvent>? EventReceived;
    public event EventHandler<SitemapWidgetEvent>? WidgetEventReceived;
    public event EventHandler<string>? ConnectionStateChanged;

    public bool IsConnected => Interlocked.CompareExchange(ref _isConnected, 0, 0) == 1;

    public OpenHabEventStreamClient(
        HttpClient httpClient,
        TimeSpan? initialBackoff = null,
        TimeSpan? maxBackoff = null,
        double? backoffMultiplier = null,
        string? apiToken = null,
        string? basicUserName = null,
        string? basicPassword = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _initialBackoff = initialBackoff ?? TimeSpan.FromSeconds(1);
        _maxBackoff = maxBackoff ?? TimeSpan.FromSeconds(30);
        _backoffMultiplier = backoffMultiplier ?? 2.0;
        _apiToken = apiToken;
        _basicUserName = basicUserName;
        _basicPassword = basicPassword;

        if (!string.IsNullOrWhiteSpace(apiToken) && !string.IsNullOrWhiteSpace(basicUserName))
            throw new ArgumentException("Configure either bearer token auth or basic auth, not both.");
    }

    public async Task ConnectAsync(Uri sseUri, CancellationToken cancellationToken = default)
    {
        var uriChanged = _sseUri is null || _sseUri != sseUri;
        if (IsConnected && !uriChanged)
            return;

        _sseUri = sseUri;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previous = Interlocked.Exchange(ref _internalCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        Interlocked.Exchange(ref _isConnected, 0);

        var firstAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(() => ReadLoopAsync(sseUri, cts, firstAttempt), CancellationToken.None);

        await firstAttempt.Task.WaitAsync(cancellationToken);
    }

    private async Task ReadLoopAsync(Uri sseUri, CancellationTokenSource cts, TaskCompletionSource firstAttempt)
    {
        var ct = cts.Token;
        var currentBackoff = _initialBackoff;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                NotifyConnectionStateChanged("connecting");
                DiagnosticLogger.Info($"SSE event stream connecting to {sseUri}");

                using var request = new HttpRequestMessage(HttpMethod.Get, sseUri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                ApplyAuth(request);

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                Interlocked.Exchange(ref _isConnected, 1);
                firstAttempt.TrySetResult();
                NotifyConnectionStateChanged("connected");
                DiagnosticLogger.Info("SSE event stream connected");
                currentBackoff = _initialBackoff;

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream);

                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);

                    if (line is null)
                    {
                        DiagnosticLogger.Warn("SSE event stream ended (null line)");
                        break;
                    }

                    // Log every non-empty line for debugging (icon logging is suppressed globally)
                    if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith(':') && !line.StartsWith("event:"))
                        DiagnosticLogger.Info($"SSE raw: {line[..Math.Min(line.Length, 200)]}");

                    var parsed = SitemapEventParser.ParseLine(line);
                    if (parsed is SitemapWidgetEvent widgetEvent)
                    {
                        DiagnosticLogger.Info($"SSE widget event: id={widgetEvent.WidgetId} vis={widgetEvent.Visibility} item={widgetEvent.ItemName} state={widgetEvent.ItemState}");
                        WidgetEventReceived?.Invoke(this, widgetEvent);
                    }
                    else
                    {
                        // Try raw event parser as fallback
                        var evt = SseMessageParser.ParseLine(line);
                        if (evt is not null)
                        {
                            DiagnosticLogger.Info($"SSE raw event: {evt.GetType().Name} topic={evt.Topic}");
                            EventReceived?.Invoke(this, evt);
                        }
                        else if (!string.IsNullOrWhiteSpace(line) && line.StartsWith("data:"))
                        {
                            DiagnosticLogger.Warn($"SSE unparsed data line: {line[..Math.Min(line.Length, 200)]}");
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn($"SSE event stream error: {ex.GetType().Name}: {ex.Message}");
                if (firstAttempt.TrySetException(ex))
                {
                    cts.Cancel();
                    break;
                }
            }

            if (ct.IsCancellationRequested)
                break;

            Interlocked.Exchange(ref _isConnected, 0);
            NotifyConnectionStateChanged("disconnected");

            try
            {
                NotifyConnectionStateChanged("reconnecting");
                DiagnosticLogger.Info($"SSE event stream reconnecting in {currentBackoff.TotalSeconds:F1}s");
                await Task.Delay(currentBackoff, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            currentBackoff = TimeSpan.FromTicks(
                (long)(currentBackoff.Ticks * _backoffMultiplier));
            if (currentBackoff > _maxBackoff)
                currentBackoff = _maxBackoff;
        }

        if (ReferenceEquals(Interlocked.CompareExchange(ref _internalCts, cts, cts), cts))
        {
            Interlocked.Exchange(ref _isConnected, 0);
        }

        firstAttempt.TrySetCanceled(ct);
    }

    public void Dispose()
    {
        var cts = Interlocked.Exchange(ref _internalCts, null);
        if (cts is not null)
        {
            cts.Cancel();
            cts.Dispose();
        }

        Interlocked.Exchange(ref _isConnected, 0);
    }

    private void NotifyConnectionStateChanged(string state)
    {
        var handlers = ConnectionStateChanged;
        if (handlers is null)
            return;

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<string>)handler)(this, state);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn($"SSE connection state handler error for '{state}': {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_apiToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
            return;
        }

        if (!string.IsNullOrWhiteSpace(_basicUserName))
        {
            var raw = $"{_basicUserName}:{_basicPassword ?? string.Empty}";
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64);
        }
    }

    public async Task<string?> SubscribeToSitemapEventsAsync(Uri baseUri, CancellationToken cancellationToken = default)
    {
        var subscribeUri = new Uri(baseUri, "rest/sitemaps/events/subscribe");
        using var request = new HttpRequestMessage(HttpMethod.Post, subscribeUri);
        ApplyAuth(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return SitemapEventParser.ParseSubscriptionId(body);
    }
}
