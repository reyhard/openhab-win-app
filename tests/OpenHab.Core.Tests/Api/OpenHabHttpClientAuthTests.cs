using System.Net;
using System.Text;
using OpenHab.Core.Api;

namespace OpenHab.Core.Tests.Api;

public sealed class OpenHabHttpClientAuthTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public string ResponseBody { get; set; } = "";

        public string? AuthHeaderValue { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public int RequestCount { get; private set; }
        public Func<HttpRequestMessage, string>? ResponseBodyFactory { get; set; }
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RequestCount++;
            AuthHeaderValue = request.Headers.Authorization?.ToString();

            // Clone request so callers can inspect it after the response is disposed
            LastRequest = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                LastRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            Requests.Add(LastRequest);

            var response = new HttpResponseMessage(StatusCode);
            var responseBody = ResponseBodyFactory?.Invoke(request) ?? ResponseBody;
            if (!string.IsNullOrEmpty(responseBody))
            {
                response.Content = new StringContent(responseBody);
            }

            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task InjectsBearerTokenIntoRequestHeaders()
    {
        var handler = new CapturingHandler { ResponseBody = """{"name":"home"}""" };
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"), apiToken: "oh.test.token123");

        await client.GetSitemapJsonAsync("home", CancellationToken.None);

        Assert.NotNull(handler.AuthHeaderValue);
        Assert.Equal("Bearer oh.test.token123", handler.AuthHeaderValue);
    }

    [Fact]
    public async Task InjectsBasicAuthIntoRequestHeaders()
    {
        var handler = new CapturingHandler { ResponseBody = """{"name":"home"}""" };
        var client = new OpenHabHttpClient(
            new HttpClient(handler),
            new Uri("http://openhab:8080"),
            basicUserName: "cloud.user",
            basicPassword: "cloud.secret");

        await client.GetSitemapJsonAsync("home", CancellationToken.None);

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("cloud.user:cloud.secret"));
        Assert.NotNull(handler.AuthHeaderValue);
        Assert.Equal($"Basic {expected}", handler.AuthHeaderValue);
    }

    [Fact]
    public async Task BearerAuthUsesExactSchemeAndTokenForEveryConsumedEndpoint()
    {
        var handler = CreateEndpointResponseHandler();
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"), apiToken: "oh.test.token123");

        await InvokeEveryConsumedEndpointAsync(client);

        AssertEndpointMatrix(handler.Requests);
        Assert.All(handler.Requests, request => Assert.Equal("Bearer oh.test.token123", request.Headers.Authorization?.ToString()));
    }

    [Fact]
    public async Task BasicAuthUsesSyntheticCredentialsForEveryConsumedEndpoint()
    {
        var handler = CreateEndpointResponseHandler();
        var client = new OpenHabHttpClient(
            new HttpClient(handler),
            new Uri("http://openhab:8080"),
            basicUserName: "cloud.user",
            basicPassword: "cloud.secret");

        await InvokeEveryConsumedEndpointAsync(client);

        AssertEndpointMatrix(handler.Requests);
        Assert.All(handler.Requests, request =>
        {
            var authorization = Assert.IsType<System.Net.Http.Headers.AuthenticationHeaderValue>(request.Headers.Authorization);
            Assert.Equal("Basic", authorization.Scheme);
            Assert.Equal("cloud.user:cloud.secret", Encoding.UTF8.GetString(Convert.FromBase64String(authorization.Parameter!)));
        });
    }

    [Fact]
    public async Task OmitsAuthorizationHeaderWhenTokenIsNull()
    {
        var handler = new CapturingHandler { ResponseBody = """{"name":"home"}""" };
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        await client.GetSitemapJsonAsync("home", CancellationToken.None);

        Assert.Null(handler.AuthHeaderValue);
    }

    [Fact]
    public void RejectsBearerAndBasicAuthTogether()
    {
        var handler = new CapturingHandler();

        var exception = Assert.Throws<ArgumentException>(() =>
            new OpenHabHttpClient(
                new HttpClient(handler),
                new Uri("http://openhab:8080"),
                apiToken: "oh.test.token123",
                basicUserName: "cloud.user",
                basicPassword: "cloud.secret"));

        Assert.Contains("either bearer token auth or basic auth, not both", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DoesNotIncludeTokenInExceptionMessage()
    {
        var handler = new CapturingHandler { StatusCode = HttpStatusCode.Unauthorized, ResponseBody = "unauthorized request" };
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"), apiToken: "oh.secret.token999");

        var error = await Assert.ThrowsAsync<OpenHabRequestException>(
            () => client.GetSitemapJsonAsync("home", CancellationToken.None));

        Assert.DoesNotContain("oh.secret.token999", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoesNotLeakTokenInUrlDiagnostics()
    {
        var handler = new CapturingHandler { StatusCode = HttpStatusCode.NotFound, ResponseBody = "not found" };
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"), apiToken: "oh.token.abc");

        var error = await Assert.ThrowsAsync<OpenHabRequestException>(
            () => client.GetSitemapJsonAsync("home", CancellationToken.None));

        Assert.DoesNotContain("oh.token.abc", error.Message, StringComparison.Ordinal);
    }

    private static CapturingHandler CreateEndpointResponseHandler()
    {
        return new CapturingHandler
        {
            ResponseBodyFactory = request => request.RequestUri!.AbsolutePath switch
            {
                "/rest/items" => "[]",
                "/rest/items/Compatibility_Switch" => """{"state":"ON"}""",
                "/rest/sitemaps" => "[]",
                "/rest/sitemaps/compatibility" => "{}",
                "/rest/ui/components/ui:page" => "[]",
                _ => string.Empty
            }
        };
    }

    private static async Task InvokeEveryConsumedEndpointAsync(OpenHabHttpClient client)
    {
        await client.SendCommandAsync("Compatibility_Switch", "ON", CancellationToken.None);
        await client.SetItemStateAsync("Compatibility_Switch", "OFF", CancellationToken.None);
        _ = await client.GetItemsAsync(CancellationToken.None);
        _ = await client.GetItemStateAsync("Compatibility_Switch", CancellationToken.None);
        _ = await client.GetSitemapsAsync(CancellationToken.None);
        _ = await client.GetSitemapJsonAsync("compatibility", CancellationToken.None);
        _ = await client.GetMainUiPageComponentsAsync(CancellationToken.None);
    }

    private static void AssertEndpointMatrix(IReadOnlyList<HttpRequestMessage> requests)
    {
        Assert.Collection(
            requests,
            request => AssertRequest(request, HttpMethod.Post, "/rest/items/Compatibility_Switch"),
            request => AssertRequest(request, HttpMethod.Put, "/rest/items/Compatibility_Switch/state"),
            request => AssertRequest(request, HttpMethod.Get, "/rest/items"),
            request => AssertRequest(request, HttpMethod.Get, "/rest/items/Compatibility_Switch"),
            request => AssertRequest(request, HttpMethod.Get, "/rest/sitemaps"),
            request => AssertRequest(request, HttpMethod.Get, "/rest/sitemaps/compatibility"),
            request => AssertRequest(request, HttpMethod.Get, "/rest/ui/components/ui:page"));
    }

    private static void AssertRequest(HttpRequestMessage request, HttpMethod method, string path)
    {
        Assert.Equal(method, request.Method);
        Assert.Equal(path, request.RequestUri!.AbsolutePath);
    }
}
