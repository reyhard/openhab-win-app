using System.Net;
using OpenHab.Core.Api;
using TestSupport;

namespace OpenHab.Core.Tests;

public sealed class OpenHabHttpClientTests
{
    [Fact]
    public async Task SendCommandPostsPlainTextToItemEndpoint()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK);
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        await client.SendCommandAsync("LivingRoom_Light", "ON", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://openhab:8080/rest/items/LivingRoom_Light", request.RequestUri!.ToString());
        Assert.Equal("ON", await request.Content!.ReadAsStringAsync());
    }

    [Fact]
    public async Task SetItemStatePutsPlainTextToStateEndpoint()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK);
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("https://myopenhab.org"));

        await client.SetItemStateAsync("PcLockedState", "ON", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("https://myopenhab.org/rest/items/PcLockedState/state", request.RequestUri!.ToString());
        Assert.Equal("ON", await request.Content!.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetSitemapJsonUsesSitemapEndpoint()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"name":"home"}""");
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        var json = await client.GetSitemapJsonAsync("home", CancellationToken.None);

        Assert.Equal("""{"name":"home"}""", json);
        Assert.Equal("http://openhab:8080/rest/sitemaps/home", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task FailedCommandThrowsRedactedOpenHabRequestException()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, "bad token");
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("https://token:secret@myopenhab.org"));

        var error = await Assert.ThrowsAsync<OpenHabRequestException>(() => client.SendCommandAsync("Light", "OFF", CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.DoesNotContain("secret", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendCommandPreservesConfiguredBasePath()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK);
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("https://myopenhab.org/openhab"));

        await client.SendCommandAsync("Light", "ON", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://myopenhab.org/openhab/rest/items/Light", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task SendCommandHonorsCanceledToken()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK);
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("https://myopenhab.org"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendCommandAsync("Light", "ON", cts.Token));
        Assert.Empty(handler.Requests);
    }
}
