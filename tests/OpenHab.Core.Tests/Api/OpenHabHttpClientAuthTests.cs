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

            var response = new HttpResponseMessage(StatusCode);
            if (!string.IsNullOrEmpty(ResponseBody))
            {
                response.Content = new StringContent(ResponseBody);
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
}
