using System.Net;
using System.Text;
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
        var sseLine = @"data: {""topic"":""openhab/items/TestItem/state"",""payload"":""{\""type\"":\""Number\"",\""value\"":\""42\""}"",""type"":""ItemStateEvent""}";
        var handler = new FakeHttpMessageHandler(sseLine);
        using var httpClient = new HttpClient(handler);

        using var client = new OpenHabEventStreamClient(httpClient, initialBackoff: TimeSpan.FromMilliseconds(50), maxBackoff: TimeSpan.FromMilliseconds(500));
        await client.ConnectAsync(new Uri("http://localhost:8080/"));

        Assert.True(client.IsConnected);

        client.Dispose();

        // Give the read loop time to exit
        await Task.Delay(300);

        Assert.False(client.IsConnected);
    }
}
