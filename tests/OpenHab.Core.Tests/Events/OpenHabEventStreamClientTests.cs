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
        using var client = new OpenHabEventStreamClient(httpClient, initialBackoff: TimeSpan.FromMilliseconds(50), maxBackoff: TimeSpan.FromMilliseconds(500));
        client.ConnectionStateChanged += (_, state) => states.Add(state);

        await client.ConnectAsync(new Uri("http://localhost:8080/"));

        // Wait for the read loop to connect
        await Task.Delay(300);

        Assert.Contains("connecting", states);
        Assert.Contains("connected", states);

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
