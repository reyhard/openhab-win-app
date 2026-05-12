using System.Net;
using System.Collections.ObjectModel;
using OpenHab.Core.Api;
using System.Text.Json;

namespace OpenHab.Core.Tests.Api;

public sealed class OpenHabHttpClientMainUiPageTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public string ResponseBody { get; set; } = "[]";
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? AuthHeaderValue { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                LastRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            AuthHeaderValue = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody)
            });
        }
    }

    [Fact]
    public async Task GetMainUiPageComponentsAsync_UsesUiPageEndpointAndAuth()
    {
        var handler = new CapturingHandler
        {
            ResponseBody = """
            [
              {
                "uid": "energy",
                "component": "oh-layout-page",
                "config": { "label": "Energy", "sidebar": true, "order": "20", "icon": "f7:bolt" }
              }
            ]
            """
        };
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"), apiToken: "token");

        var pages = await client.GetMainUiPageComponentsAsync(CancellationToken.None);

        Assert.Equal(new Uri("http://openhab:8080/rest/ui/components/ui:page"), handler.LastRequest!.RequestUri);
        Assert.Equal("Bearer token", handler.AuthHeaderValue);
        var page = Assert.Single(pages);
        Assert.Equal("energy", page.Uid);
        Assert.Equal("oh-layout-page", page.Component);
        Assert.Equal("Energy", page.GetConfigString("label"));
        Assert.True(page.GetConfigBoolean("sidebar"));
        Assert.Equal(20, page.GetConfigInt32("order"));
        Assert.Equal("f7:bolt", page.GetConfigString("icon"));
    }

    [Fact]
    public async Task GetMainUiPageComponentsAsync_ReturnsEmptyForBlankArray()
    {
        var handler = new CapturingHandler { ResponseBody = "[]" };
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        var pages = await client.GetMainUiPageComponentsAsync(CancellationToken.None);

        Assert.Empty(pages);
    }

    [Fact]
    public async Task GetMainUiPageComponentsAsync_FiltersNonObjectEntriesAndBlankUid()
    {
        var handler = new CapturingHandler
        {
            ResponseBody = """
            [
              "skip",
              42,
              { "uid": "", "component": "oh-layout-page", "config": {} },
              { "uid": "energy", "component": "oh-layout-page", "config": {} }
            ]
            """
        };
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        var pages = await client.GetMainUiPageComponentsAsync(CancellationToken.None);

        var page = Assert.Single(pages);
        Assert.Equal("energy", page.Uid);
        Assert.Equal("oh-layout-page", page.Component);
    }

    [Fact]
    public async Task GetMainUiPageComponentsAsync_DefaultsMissingOrInvalidComponentToEmptyString()
    {
        var handler = new CapturingHandler
        {
            ResponseBody = """
            [
              { "uid": "first", "config": {} },
              { "uid": "second", "component": 123, "config": {} }
            ]
            """
        };
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        var pages = await client.GetMainUiPageComponentsAsync(CancellationToken.None);

        Assert.Collection(
            pages,
            first =>
            {
                Assert.Equal("first", first.Uid);
                Assert.Equal(string.Empty, first.Component);
            },
            second =>
            {
                Assert.Equal("second", second.Uid);
                Assert.Equal(string.Empty, second.Component);
            });
    }

    [Fact]
    public async Task GetMainUiPageComponentsAsync_ReturnsReadOnlyClonedConfigValues()
    {
        var handler = new CapturingHandler
        {
            ResponseBody = """
            [
              {
                "uid": "energy",
                "component": "oh-layout-page",
                "config": { "label": "Energy", "sidebar": true, "order": 20 }
              }
            ]
            """
        };
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        var pages = await client.GetMainUiPageComponentsAsync(CancellationToken.None);

        var page = Assert.Single(pages);
        Assert.IsType<ReadOnlyDictionary<string, JsonElement>>(page.Config);
        Assert.Equal("Energy", page.Config["label"].GetString());
        Assert.True(page.Config["sidebar"].GetBoolean());
        Assert.Equal(20, page.Config["order"].GetInt32());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, JsonElement>)page.Config).Add("extra", default));
    }

    [Fact]
    public async Task GetMainUiPageComponentsAsync_ThrowsRedactedRequestExceptionOnFailure()
    {
        var handler = new CapturingHandler
        {
            StatusCode = HttpStatusCode.InternalServerError,
            ResponseBody = "token=secret"
        };
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"), apiToken: "secret");

        var exception = await Assert.ThrowsAsync<OpenHabRequestException>(
            () => client.GetMainUiPageComponentsAsync(CancellationToken.None));

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.Ordinal);
    }
}
