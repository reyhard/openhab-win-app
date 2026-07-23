using System.Net;
using OpenHab.Core.Api;
using TestSupport;

namespace OpenHab.Core.Tests;

public sealed class OpenHabHttpClientTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task SendCommandAcceptsEveryValidOpenHabSuccessStatus(HttpStatusCode statusCode)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(statusCode);
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        await client.SendCommandAsync("Compatibility_Switch", "ON", CancellationToken.None);
    }

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

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task SetItemStateAcceptsEveryValidOpenHabSuccessStatus(HttpStatusCode statusCode)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(statusCode);
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        await client.SetItemStateAsync("Compatibility_Switch", "OFF", CancellationToken.None);
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

    [Theory]
    [InlineData("openhab-5.1.4")]
    [InlineData("openhab-5.2.0")]
    public async Task GetSitemapJsonReturnsCapturedCompatibilityPayload(string version)
    {
        var fixture = ReadCompatibilityFixture(version, "sitemaps", "home.json");
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, fixture);
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        var result = await client.GetSitemapJsonAsync("compatibility", CancellationToken.None);

        Assert.Equal(fixture, result);
    }

    [Fact]
    public async Task GetItemsParsesItemSummaries()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        [
          { "name": "Light_LivingRoom", "label": "Living Room Light", "type": "Switch", "state": "ON" },
          { "name": "Speaker_Playback", "type": "Player", "state": "PLAY" }
        ]
        """);
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        var items = await client.GetItemsAsync(CancellationToken.None);

        Assert.Equal("http://openhab:8080/rest/items", handler.Requests[0].RequestUri!.ToString());
        Assert.Collection(items,
            item =>
            {
                Assert.Equal("Light_LivingRoom", item.Name);
                Assert.Equal("Living Room Light", item.Label);
                Assert.Equal("Switch", item.Type);
                Assert.Equal("ON", item.State);
            },
            item =>
            {
                Assert.Equal("Speaker_Playback", item.Name);
                Assert.Equal("Speaker_Playback", item.Label);
                Assert.Equal("Player", item.Type);
                Assert.Equal("PLAY", item.State);
            });
    }

    [Fact]
    public async Task GetItemsSkipsNonObjectEntriesAndEntriesWithoutValidName()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        [
          "invalid",
          { "label": "No Name", "type": "Switch", "state": "ON" },
          { "name": " ", "label": "Blank Name", "type": "Switch", "state": "ON" },
          { "name": "Valid_Item", "type": "Number", "state": "42" }
        ]
        """);
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        var items = await client.GetItemsAsync(CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Equal("Valid_Item", item.Name);
        Assert.Equal("Valid_Item", item.Label);
        Assert.Equal("Number", item.Type);
        Assert.Equal("42", item.State);
    }

    [Fact]
    public async Task GetItemsIgnoresUnknownOpenHab52Properties()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, ReadCompatibilityFixture("openhab-5.2.0", "items", "list.json"));
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        var items = await client.GetItemsAsync(CancellationToken.None);

        Assert.Collection(
            items,
            item => AssertItem(item, "Compatibility_Text", "Compatibility Text", "String", "NULL"),
            item => AssertItem(item, "Compatibility_Switch", "Compatibility Switch", "Switch", "ON"),
            item => AssertItem(item, "Compatibility_Number", "Compatibility Number", "Number", "NULL"),
            item => AssertItem(item, "Compatibility_Mode", "Compatibility Mode", "String", "NULL"),
            item => AssertItem(item, "Compatibility_Dimmer", "Compatibility Dimmer", "Dimmer", "NULL"));
    }

    [Theory]
    [InlineData("openhab-5.1.4", "NULL")]
    [InlineData("openhab-5.2.0", "ON")]
    public async Task GetItemsCompatibilityFixturesPreserveAppFacingModels(string version, string expectedSwitchState)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, ReadCompatibilityFixture(version, "items", "list.json"));
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        var items = await client.GetItemsAsync(CancellationToken.None);

        Assert.Equal(5, items.Count);
        AssertItem(items.Single(item => item.Name == "Compatibility_Switch"), "Compatibility_Switch", "Compatibility Switch", "Switch", expectedSwitchState);
    }

    [Fact]
    public async Task GetItemsThrowsFormatExceptionWhenRootIsNotArray()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{ "name": "Light" }""");
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        var error = await Assert.ThrowsAsync<FormatException>(() => client.GetItemsAsync(CancellationToken.None));

        Assert.Equal("Items response must be a JSON array.", error.Message);
    }

    [Fact]
    public async Task GetSitemapsIgnoresUnknownOpenHab52Properties()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        [
          {
            "name": "compatibility",
            "label": "",
            "homepage": { "widgets": [] },
            "openHab52Metadata": { "managed": false }
          }
        ]
        """);
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        var sitemap = Assert.Single(await client.GetSitemapsAsync(CancellationToken.None));

        Assert.Equal("compatibility", sitemap.Name);
        Assert.Equal("compatibility", sitemap.Label);
    }

    [Theory]
    [InlineData("openhab-5.1.4")]
    [InlineData("openhab-5.2.0")]
    public async Task GetSitemapsCompatibilityFixturesPreserveAppFacingModels(string version)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, ReadCompatibilityFixture(version, "sitemaps", "list.json"));
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        var sitemap = Assert.Single(await client.GetSitemapsAsync(CancellationToken.None));

        Assert.Equal("compatibility", sitemap.Name);
        Assert.Equal("Compatibility", sitemap.Label);
    }

    [Fact]
    public async Task GetItemStateReturnsStateProperty()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{ "name": "Light", "state": "OFF" }""");
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        var state = await client.GetItemStateAsync("Light", CancellationToken.None);

        Assert.Equal("OFF", state);
        Assert.Equal("http://openhab:8080/rest/items/Light", handler.Requests[0].RequestUri!.ToString());
    }

    [Theory]
    [InlineData("openhab-5.1.4", "NULL")]
    [InlineData("openhab-5.2.0", "ON")]
    public async Task GetItemStateCompatibilityFixturesPreserveState(string version, string expectedState)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, ReadCompatibilityFixture(version, "items", "test-item.json"));
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        var state = await client.GetItemStateAsync("Compatibility_Switch", CancellationToken.None);

        Assert.Equal(expectedState, state);
    }

    [Fact]
    public async Task GetItemStateEscapesItemName()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{ "name": "Light/Desk", "state": "ON" }""");
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        _ = await client.GetItemStateAsync("Light/Desk", CancellationToken.None);

        Assert.Equal("http://openhab:8080/rest/items/Light%2FDesk", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetItemStateThrowsFormatExceptionWhenRootIsNotObject()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """["OFF"]""");
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        var error = await Assert.ThrowsAsync<FormatException>(() => client.GetItemStateAsync("Light", CancellationToken.None));

        Assert.Equal("Item state response must be a JSON object.", error.Message);
    }

    [Fact]
    public async Task GetItemStateReturnsNullWhenStateIsMissing()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{ "name": "Light" }""");
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        var state = await client.GetItemStateAsync("Light", CancellationToken.None);

        Assert.Null(state);
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

    [Theory]
    [InlineData("Authorization: Bearer oh.secret.token", "oh.secret.token")]
    [InlineData("{\"password\":\"p@ssw0rd\",\"error\":\"bad\"}", "p@ssw0rd")]
    [InlineData("{\"token\":\"abc123\",\"message\":\"bad token\"}", "abc123")]
    [InlineData("{\"authorization\":\"Bearer abc.def\"}", "abc.def")]
    [InlineData("{\"token\":\"abc 123\"}", "abc 123")]
    [InlineData("https://user:pass@example.org/rest/items", "user:pass@example.org")]
    [InlineData("Basic dXNlcjpwYXNz", "dXNlcjpwYXNz")]
    public async Task FailedRequestRedactsSensitiveResponseBodies(string responseBody, string sensitiveText)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            ReasonPhrase = "Bad Request",
            Content = new StringContent(responseBody)
        });
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        var error = await Assert.ThrowsAsync<OpenHabRequestException>(
            () => client.GetSitemapJsonAsync("default", CancellationToken.None));

        Assert.DoesNotContain(sensitiveText, error.Message, StringComparison.Ordinal);
        Assert.Contains("[redacted]", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://proxy.test/openhab", "https://proxy.test/openhab/rest/items")]
    [InlineData("https://proxy.test/openhab/", "https://proxy.test/openhab/rest/items")]
    public async Task BaseUriPathPrefixIsPreserved(string baseUri, string expected)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "[]");
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri(baseUri));

        await client.GetItemsAsync(CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(expected, request.RequestUri!.ToString());
    }

    [Fact]
    public async Task FailedRequestRedactsCredentialsFromUrlAndResponseWhilePreservingStatus()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Forbidden, "token=oh.secret&password=cloud.secret");
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("https://cloud.user:cloud.password@openhab.test"));

        var error = await Assert.ThrowsAsync<OpenHabRequestException>(() => client.GetSitemapJsonAsync("compatibility", CancellationToken.None));

        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Contains("403", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("cloud.user", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("cloud.password", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("oh.secret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("cloud.secret", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedRequestRedactsSensitiveReasonPhrase()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            ReasonPhrase = "upstream token oh.secret.token"
        });
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("https://openhab.test"));

        var error = await Assert.ThrowsAsync<OpenHabRequestException>(() => client.GetSitemapJsonAsync("compatibility", CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
        Assert.Contains("502", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("oh.secret.token", error.Message, StringComparison.Ordinal);
        Assert.Contains("[redacted]", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FailedRequestRedactsStandaloneBearerReasonPhrase()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            ReasonPhrase = "upstream Bearer oh.secret.token"
        });
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("https://openhab.test"));

        var error = await Assert.ThrowsAsync<OpenHabRequestException>(() => client.GetSitemapJsonAsync("compatibility", CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
        Assert.Contains("502", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("oh.secret.token", error.Message, StringComparison.Ordinal);
        Assert.Contains("Bearer [redacted]", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FailedRequestPreservesHarmlessReasonPhraseText()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            ReasonPhrase = "token endpoint unavailable"
        });
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("https://openhab.test"));

        var error = await Assert.ThrowsAsync<OpenHabRequestException>(() => client.GetSitemapJsonAsync("compatibility", CancellationToken.None));

        Assert.Contains("token endpoint unavailable", error.Message, StringComparison.OrdinalIgnoreCase);
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

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }

    private static string ReadCompatibilityFixture(string version, params string[] path)
    {
        return File.ReadAllText(Path.Combine([AppContext.BaseDirectory, "CompatibilityFixtures", version, .. path]));
    }

    private static void AssertItem(OpenHabItemSummary item, string name, string label, string type, string? state)
    {
        Assert.Equal(name, item.Name);
        Assert.Equal(label, item.Label);
        Assert.Equal(type, item.Type);
        Assert.Equal(state, item.State);
    }
}
