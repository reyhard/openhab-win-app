using System.Net;
using System.Collections.Concurrent;
using System.Text;
using OpenHab.Core;
using OpenHab.Core.Events;

namespace OpenHab.Core.Tests.Events;

public sealed class OpenHabEventStreamClientTests
{
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseContent;
        private readonly HttpStatusCode _statusCode;
        public bool WasCalled { get; private set; }

        public FakeHttpMessageHandler(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseContent = responseContent;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            WasCalled = true;
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseContent, Encoding.UTF8, "text/event-stream")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Lock _sync = new();
        private readonly Queue<HttpResponseMessage> _responses;
        private int _callCount;

        public int CallCount => Interlocked.CompareExchange(ref _callCount, 0, 0);

        public QueueHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref _callCount);
            lock (_sync)
            {
                return Task.FromResult(_responses.Dequeue());
            }
        }
    }

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, int, Task<HttpResponseMessage>> _sendAsync;
        private int _callCount;

        public int CallCount => Interlocked.CompareExchange(ref _callCount, 0, 0);

        public DelegateHandler(Func<HttpRequestMessage, CancellationToken, int, Task<HttpResponseMessage>> sendAsync)
        {
            _sendAsync = sendAsync;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var callCount = Interlocked.Increment(ref _callCount);
            return _sendAsync(request, ct, callCount);
        }
    }

    private sealed class BlockingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task ConnectAsyncSurfacesFirstConnectionFailure()
    {
        var handler = new QueueHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("unauthorized")
        });
        using var client = new OpenHabEventStreamClient(new HttpClient(handler));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.ConnectAsync(new Uri("http://localhost:8080/rest/events")));
    }

    [Fact]
    public async Task ConnectAsyncFirstConnectionFailureDoesNotRetryInBackground()
    {
        var handler = new QueueHandler(
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("unauthorized")
            },
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("unauthorized")
            });
        using var client = new OpenHabEventStreamClient(
            new HttpClient(handler),
            initialBackoff: TimeSpan.FromMilliseconds(10),
            maxBackoff: TimeSpan.FromMilliseconds(10));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.ConnectAsync(new Uri("http://localhost:8080/rest/events")));

        await Task.Delay(100);

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task IsConnectedBecomesFalseWhenStreamDropsBeforeReconnect()
    {
        var handler = new QueueHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "text/event-stream")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "text/event-stream")
            });
        using var client = new OpenHabEventStreamClient(
            new HttpClient(handler),
            initialBackoff: TimeSpan.FromSeconds(5),
            maxBackoff: TimeSpan.FromSeconds(5));
        var disconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ConnectionStateChanged += (_, state) =>
        {
            if (state == "disconnected")
                disconnected.TrySetResult(client.IsConnected);
        };

        await client.ConnectAsync(new Uri("http://localhost:8080/rest/events"));

        var wasConnectedWhenDisconnectedEmitted = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(wasConnectedWhenDisconnectedEmitted);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task ConnectAsyncClearsIsConnectedWhenReplacingConnectedStream()
    {
        var replacementRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHandler((_, ct, callCount) =>
        {
            if (callCount == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new BlockingReadStream())
                });
            }

            replacementRequestStarted.TrySetResult();
            return replacementResponse.Task.WaitAsync(ct);
        });
        using var client = new OpenHabEventStreamClient(new HttpClient(handler));

        await client.ConnectAsync(new Uri("http://localhost:8080/rest/events/old"));
        Assert.True(client.IsConnected);

        var replacementTask = client.ConnectAsync(new Uri("http://localhost:8080/rest/events/new"));
        await replacementRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(client.IsConnected);

        replacementResponse.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new BlockingReadStream())
        });
        await replacementTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ReconnectAfterInitialSuccessContinuesInBackground()
    {
        var handler = new QueueHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "text/event-stream")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "text/event-stream")
            });
        using var client = new OpenHabEventStreamClient(
            new HttpClient(handler),
            initialBackoff: TimeSpan.FromMilliseconds(10),
            maxBackoff: TimeSpan.FromMilliseconds(10));

        await client.ConnectAsync(new Uri("http://localhost:8080/rest/events"));

        await Task.Delay(100);

        Assert.True(handler.CallCount >= 2);
    }

    [Fact]
    public async Task ConnectionStateChangedExceptionDoesNotStopReconnect()
    {
        var secondRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHandler((_, _, callCount) =>
        {
            if (callCount >= 2)
                secondRequestStarted.TrySetResult();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = callCount == 1
                    ? new StringContent(string.Empty, Encoding.UTF8, "text/event-stream")
                    : new StreamContent(new BlockingReadStream())
            });
        });
        using var client = new OpenHabEventStreamClient(
            new HttpClient(handler),
            initialBackoff: TimeSpan.FromMilliseconds(10),
            maxBackoff: TimeSpan.FromMilliseconds(10));
        client.ConnectionStateChanged += (_, state) =>
        {
            if (state is "disconnected" or "reconnecting")
                throw new InvalidOperationException("Subscriber failure");
        };

        await client.ConnectAsync(new Uri("http://localhost:8080/rest/events"));

        await secondRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(handler.CallCount >= 2);
    }

    [Fact]
    public async Task EventReceived_FiresOnValidSseLine()
    {
        var sseLine = @"data: {""topic"":""openhab/items/Light_GF/state"",""payload"":""{\""type\"":\""OnOff\"",\""value\"":\""ON\""}"",""type"":""ItemStateEvent""}";
        var handler = new FakeHttpMessageHandler(sseLine);
        using var httpClient = new HttpClient(handler);

        var tcs = new TaskCompletionSource<ItemStateChangedEvent>();
        using var client = new OpenHabEventStreamClient(httpClient, initialBackoff: TimeSpan.FromMilliseconds(50), maxBackoff: TimeSpan.FromMilliseconds(500));
        client.EventReceived += (_, evt) =>
        {
            if (evt is ItemStateChangedEvent stateChanged)
            {
                tcs.TrySetResult(stateChanged);
            }
        };

        await client.ConnectAsync(new Uri("http://localhost:8080/"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stateEvent = await tcs.Task.WaitAsync(cts.Token);

        Assert.NotNull(stateEvent);
        Assert.Equal("Light_GF", stateEvent.ItemName);
        Assert.Equal("ON", stateEvent.State);
        Assert.Equal("ItemStateEvent", stateEvent.Type);

        client.Dispose();
    }

    [Fact]
    public async Task EventReceived_DoesNotLogRawSseDetailsWhenVerboseDiagnosticsDisabled()
    {
        var capturedLines = new Queue<string>();
        using var capture = DiagnosticLogger.BeginLogCapture(false, line =>
        {
            lock (capturedLines)
            {
                capturedLines.Enqueue(line);
            }
        });

        DiagnosticLogger.Info("capture-sentinel");

        var token = Guid.NewGuid().ToString("N");
        var sseLine = @"data: {""topic"":""openhab/items/Light_GF/state"",""payload"":""{\""type\"":\""OnOff\"",\""value\"":\""ON\""}"",""type"":""ItemStateEvent""}";
        sseLine = sseLine.Replace("Light_GF", token);
        var handler = new FakeHttpMessageHandler(sseLine);
        using var httpClient = new HttpClient(handler);

        var tcs = new TaskCompletionSource<ItemStateChangedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new OpenHabEventStreamClient(httpClient, initialBackoff: TimeSpan.FromMilliseconds(50), maxBackoff: TimeSpan.FromMilliseconds(500));
        client.EventReceived += (_, evt) =>
        {
            if (evt is ItemStateChangedEvent stateChanged)
            {
                tcs.TrySetResult(stateChanged);
            }
        };

        await client.ConnectAsync(new Uri("http://localhost:8080/"));
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        string[] lines;
        lock (capturedLines)
        {
            lines = capturedLines.ToArray();
        }

        Assert.Contains(lines, line => line.Contains("capture-sentinel", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains(token, StringComparison.Ordinal));

        client.Dispose();
    }

    [Fact]
    public async Task WidgetEventDoesNotLogPayloadWhenVerboseDiagnosticsEnabled()
    {
        var capturedLines = new ConcurrentQueue<string>();
        using var capture = DiagnosticLogger.BeginLogCapture(true, capturedLines.Enqueue);
        var privateValue = Guid.NewGuid().ToString("N");
        var sseLine = $"data: {{\"widgetId\":\"{privateValue}\",\"label\":\"{privateValue}\",\"item\":{{\"name\":\"{privateValue}\",\"state\":\"{privateValue}\"}}}}";
        var handler = new FakeHttpMessageHandler(sseLine);
        using var client = new OpenHabEventStreamClient(
            new HttpClient(handler),
            initialBackoff: TimeSpan.FromSeconds(5),
            maxBackoff: TimeSpan.FromSeconds(5));
        var eventReceived = new TaskCompletionSource<SitemapWidgetEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.WidgetEventReceived += (_, widgetEvent) => eventReceived.TrySetResult(widgetEvent);

        await client.ConnectAsync(new Uri("https://openhab.test/rest/sitemaps/events/abc123"));
        await eventReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.DoesNotContain(capturedLines, line => line.Contains(privateValue, StringComparison.Ordinal));
    }

    [Fact]
    public async Task BeginLogCapture_DisposedInnerScopeDoesNotLeakIntoChildAsyncFlow()
    {
        var outerLines = new ConcurrentQueue<string>();
        var flowStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFlow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var outerCapture = DiagnosticLogger.BeginLogCapture(false, line => outerLines.Enqueue(line));
        Task childFlow;

        using (DiagnosticLogger.BeginLogCapture(true, _ => { }))
        {
            childFlow = Task.Run(async () =>
            {
                flowStarted.SetResult();
                await releaseFlow.Task.WaitAsync(TimeSpan.FromSeconds(2));

                DiagnosticLogger.Info("child-info");
                DiagnosticLogger.Verbose("child-verbose");
            });

            await flowStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }

        releaseFlow.SetResult();
        await childFlow.WaitAsync(TimeSpan.FromSeconds(2));

        var lines = outerLines.ToArray();
        Assert.Contains(lines, line => line.Contains("child-info", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("child-verbose", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BeginLogCapture_IsolatedAcrossParallelAsyncFlows()
    {
        var previousVerboseEventLogging = DiagnosticLogger.VerboseEventLogging;
        DiagnosticLogger.VerboseEventLogging = true;

        try
        {
            var flow1Lines = new ConcurrentQueue<string>();
            var flow2Lines = new ConcurrentQueue<string>();
            var flow1Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var flow2Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseLogs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var flow1 = Task.Run(async () =>
            {
                using var capture = DiagnosticLogger.BeginLogCapture(false, line => flow1Lines.Enqueue(line));
                flow1Started.SetResult();
                await releaseLogs.Task.WaitAsync(TimeSpan.FromSeconds(2));

                DiagnosticLogger.Info("flow1-info");
                DiagnosticLogger.Verbose("flow1-verbose");
            });

            await flow1Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var flow2 = Task.Run(async () =>
            {
                using var capture = DiagnosticLogger.BeginLogCapture(true, line => flow2Lines.Enqueue(line));
                flow2Started.SetResult();
                await releaseLogs.Task.WaitAsync(TimeSpan.FromSeconds(2));

                DiagnosticLogger.Info("flow2-info");
                DiagnosticLogger.Verbose("flow2-verbose");
            });

            await flow2Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            releaseLogs.SetResult();

            await Task.WhenAll(flow1, flow2).WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Collection(flow1Lines,
                line => Assert.Contains("flow1-info", line, StringComparison.Ordinal));

            Assert.Collection(flow2Lines,
                line => Assert.Contains("flow2-info", line, StringComparison.Ordinal),
                line => Assert.Contains("flow2-verbose", line, StringComparison.Ordinal));
        }
        finally
        {
            DiagnosticLogger.VerboseEventLogging = previousVerboseEventLogging;
        }
    }

    [Fact]
    public async Task ConnectionStateChanged_FiresOnConnect()
    {
        var sseLine = @"data: {""topic"":""openhab/items/TestItem/state"",""payload"":""{\""type\"":\""Number\"",\""value\"":\""42\""}"",""type"":""ItemStateEvent""}";
        var handler = new FakeHttpMessageHandler(sseLine);
        using var httpClient = new HttpClient(handler);

        var states = new List<string>();
        var sync = new Lock();
        using var client = new OpenHabEventStreamClient(httpClient, initialBackoff: TimeSpan.FromMilliseconds(50), maxBackoff: TimeSpan.FromMilliseconds(500));
        client.ConnectionStateChanged += (_, state) =>
        {
            lock (sync)
            {
                states.Add(state);
            }
        };

        await client.ConnectAsync(new Uri("http://localhost:8080/"));

        // Wait for the read loop to connect
        await Task.Delay(300);

        lock (sync)
        {
            Assert.Contains("connecting", states);
            Assert.Contains("connected", states);
        }

        client.Dispose();
    }

    [Fact]
    public async Task Dispose_StopsReadLoop()
    {
        var handler = new DelegateHandler((_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new BlockingReadStream())
            }));
        using var httpClient = new HttpClient(handler);

        using var client = new OpenHabEventStreamClient(httpClient, initialBackoff: TimeSpan.FromMilliseconds(50), maxBackoff: TimeSpan.FromMilliseconds(500));
        await client.ConnectAsync(new Uri("http://localhost:8080/"));

        for (var i = 0; i < 20 && !client.IsConnected; i++)
        {
            await Task.Delay(25);
        }
        Assert.True(client.IsConnected);

        client.Dispose();

        for (var i = 0; i < 20 && client.IsConnected; i++)
        {
            await Task.Delay(25);
        }
        Assert.False(client.IsConnected);
    }

    [Theory]
    [InlineData("/rest/sitemaps/events/abc123")]
    [InlineData("https://openhab.test/rest/sitemaps/events/abc123")]
    [InlineData("https://openhab.test/rest/sitemaps/events/abc123?x=1#f")]
    public async Task SubscribeUsesStandardLocationHeaderBeforeLegacyResponseBody(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("{\"context\":{\"headers\":{\"Location\":[\"/rest/sitemaps/events/legacy\"]}}}")
        };
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        var handler = new QueueHandler(response);
        using var client = new OpenHabEventStreamClient(new HttpClient(handler));

        var subscriptionId = await client.SubscribeToSitemapEventsAsync(new Uri("https://openhab.test/"));

        Assert.Equal("abc123", subscriptionId);
    }

    [Fact]
    public async Task SubscribeReadsLegacyResponseBody()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "CompatibilityFixtures",
            "openhab-5.1.4",
            "events",
            "subscription-response.json");
        var handler = new QueueHandler(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(File.ReadAllText(fixturePath))
        });
        using var client = new OpenHabEventStreamClient(new HttpClient(handler));

        var subscriptionId = await client.SubscribeToSitemapEventsAsync(new Uri("https://openhab.test/"));

        Assert.Equal("5937207b-c13a-441c-8baf-bf248685316b", subscriptionId);
    }

    [Theory]
    [InlineData("{\"context\":{\"headers\":{\"Location\":[\"\"]}}}")]
    [InlineData("{\"context\":{\"headers\":{\"Location\":[\"/\"]}}}")]
    [InlineData("{\"context\":{\"headers\":{\"Location\":[\"https://openhab.test/\"]}}}")]
    [InlineData("{\"context\":{\"headers\":{\"Location\":[\"not-a-location\"]}}}")]
    public void ParseSubscriptionIdReturnsNullForUnusableLegacyLocations(string responseBody)
    {
        Assert.Null(SitemapEventParser.ParseSubscriptionId(responseBody));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("https://openhab.test/")]
    [InlineData("not-a-location")]
    public void ResolveSubscriptionIdDoesNotUseLegacyBodyWhenLocationHeaderIsUnusable(string location)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);

        var subscriptionId = OpenHabEventStreamClient.ResolveSubscriptionId(
            response,
            "{\"context\":{\"headers\":{\"Location\":[\"/rest/sitemaps/events/legacy\"]}}}");

        Assert.Null(subscriptionId);
    }

    [Fact]
    public void ResolveSubscriptionIdDoesNotUseLegacyBodyWhenLocationHeaderIsBlank()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created);
        response.Headers.TryAddWithoutValidation("Location", " ");

        var subscriptionId = OpenHabEventStreamClient.ResolveSubscriptionId(
            response,
            "{\"context\":{\"headers\":{\"Location\":[\"/rest/sitemaps/events/legacy\"]}}}");

        Assert.Null(subscriptionId);
    }

    [Fact]
    public async Task SubscribeAppliesBearerTokenToSubscriptionAndStream()
    {
        var authorizations = new List<string?>();
        var sync = new Lock();
        var handler = new DelegateHandler((request, _, callCount) =>
        {
            lock (sync)
            {
                authorizations.Add(request.Headers.Authorization?.ToString());
            }

            if (callCount == 1)
            {
                var subscribeResponse = new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{}")
                };
                subscribeResponse.Headers.Location = new Uri("/rest/sitemaps/events/abc123", UriKind.Relative);
                return Task.FromResult(subscribeResponse);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new BlockingReadStream())
            });
        });
        using var client = new OpenHabEventStreamClient(new HttpClient(handler), apiToken: "test-token");

        var subscriptionId = await client.SubscribeToSitemapEventsAsync(new Uri("https://openhab.test/"));
        await client.ConnectAsync(new Uri($"https://openhab.test/rest/sitemaps/events/{subscriptionId}"));

        lock (sync)
        {
            Assert.Equal(["Bearer test-token", "Bearer test-token"], authorizations);
        }
    }

    [Fact]
    public async Task SubscribeAppliesBasicAuthenticationToSubscriptionAndStream()
    {
        var authorizations = new List<string?>();
        var sync = new Lock();
        var handler = new DelegateHandler((request, _, callCount) =>
        {
            lock (sync)
            {
                authorizations.Add(request.Headers.Authorization?.ToString());
            }

            if (callCount == 1)
            {
                var subscribeResponse = new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{}")
                };
                subscribeResponse.Headers.Location = new Uri("/rest/sitemaps/events/abc123", UriKind.Relative);
                return Task.FromResult(subscribeResponse);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new BlockingReadStream())
            });
        });
        using var client = new OpenHabEventStreamClient(new HttpClient(handler), basicUserName: "user", basicPassword: "password");

        var subscriptionId = await client.SubscribeToSitemapEventsAsync(new Uri("https://openhab.test/"));
        await client.ConnectAsync(new Uri($"https://openhab.test/rest/sitemaps/events/{subscriptionId}"));

        lock (sync)
        {
            Assert.Equal(["Basic dXNlcjpwYXNzd29yZA==", "Basic dXNlcjpwYXNzd29yZA=="], authorizations);
        }
    }

    [Fact]
    public async Task ConnectAsyncForbiddenFirstConnectionFailureDoesNotRetryInBackground()
    {
        var handler = new QueueHandler(
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("forbidden")
            },
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("forbidden")
            });
        using var client = new OpenHabEventStreamClient(
            new HttpClient(handler),
            initialBackoff: TimeSpan.FromMilliseconds(10),
            maxBackoff: TimeSpan.FromMilliseconds(10));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.ConnectAsync(new Uri("http://localhost:8080/rest/events")));

        await Task.Delay(100);

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task MalformedSitemapDataDoesNotLogRawPayloadWhenVerboseDiagnosticsDisabled()
    {
        var capturedLines = new ConcurrentQueue<string>();
        using var capture = DiagnosticLogger.BeginLogCapture(false, capturedLines.Enqueue);
        var privateValue = Guid.NewGuid().ToString("N");
        var handler = new FakeHttpMessageHandler($"data: {{\"widgetId\":\"{privateValue}");
        using var client = new OpenHabEventStreamClient(
            new HttpClient(handler),
            initialBackoff: TimeSpan.FromSeconds(5),
            maxBackoff: TimeSpan.FromSeconds(5));

        await client.ConnectAsync(new Uri("https://openhab.test/rest/sitemaps/events/abc123"));
        await Task.Delay(50);

        Assert.DoesNotContain(capturedLines, line => line.Contains(privateValue, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConnectAsyncCancellationStopsPendingInitialRequestWithoutReconnect()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHandler((_, cancellationToken, _) =>
        {
            requestStarted.TrySetResult();
            return pendingResponse.Task.WaitAsync(cancellationToken);
        });
        using var client = new OpenHabEventStreamClient(new HttpClient(handler));
        using var cancellation = new CancellationTokenSource();

        var connectTask = client.ConnectAsync(new Uri("https://openhab.test/rest/sitemaps/events/abc123"), cancellation.Token);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connectTask);

        await Task.Delay(50);
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData("1:0")]
    [InlineData("floor/main")]
    [InlineData("page with spaces")]
    public async Task ContextlessWidgetEventUsesDecodedSitemapRequestContext(string pageId)
    {
        var handler = new FakeHttpMessageHandler("data: {\"widgetId\":\"2:001100\",\"item\":{\"name\":\"Mode\",\"state\":\"ON\"}}");
        using var client = new OpenHabEventStreamClient(
            new HttpClient(handler),
            initialBackoff: TimeSpan.FromSeconds(5),
            maxBackoff: TimeSpan.FromSeconds(5));
        var received = new TaskCompletionSource<SitemapWidgetEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.WidgetEventReceived += (_, widgetEvent) => received.TrySetResult(widgetEvent);

        var requestUri = new Uri(
            $"https://openhab.test/rest/sitemaps/events/abc123?sitemap=default&pageid={Uri.EscapeDataString(pageId)}");
        await client.ConnectAsync(requestUri);

        var widgetEvent = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("default", widgetEvent.SitemapName);
        Assert.Equal(pageId, widgetEvent.PageId);
    }
}
