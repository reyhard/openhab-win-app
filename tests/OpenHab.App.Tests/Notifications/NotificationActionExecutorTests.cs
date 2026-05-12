using System.Net;
using System.Net.Http.Headers;
using System.Text;
using OpenHab.App.Settings;
using OpenHab.Core.Profiles;
using OpenHab.Windows.Notifications;
using OpenHab.Windows.Tray.Notifications;

namespace OpenHab.App.Tests.Notifications;

public sealed class NotificationActionExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_CommandInLocalMode_PostsCommandWithBearerAuth()
    {
        var handler = new CaptureHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("http://openhab.local:8080/rest/items/KitchenLight", req.RequestUri!.ToString());
            Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
            Assert.Equal("local-token", req.Headers.Authorization?.Parameter);
            Assert.Equal("ON", req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal("text/plain", req.Content.Headers.ContentType?.MediaType);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var opened = new List<Uri>();
        var executor = CreateExecutor(
            handler,
            getSettings: () => AppSettings.Default with { EndpointMode = EndpointMode.LocalOnly, LocalEndpoint = new Uri("http://openhab.local:8080/") },
            getApiToken: kind => kind == TransportKind.Local ? "local-token" : null,
            getCloudCredentials: _ => null,
            opened);

        await executor.ExecuteAsync(new NotificationAction("command", "KitchenLight:ON"), CancellationToken.None);

        Assert.Empty(opened);
    }

    [Fact]
    public async Task ExecuteAsync_CommandInCloudMode_PostsCommandWithBasicAuth()
    {
        var handler = new CaptureHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://myopenhab.org/rest/items/KitchenLight", req.RequestUri!.ToString());
            Assert.Equal("Basic", req.Headers.Authorization?.Scheme);
            var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("user@example.com:secret"));
            Assert.Equal(expected, req.Headers.Authorization?.Parameter);
            Assert.Equal("OFF", req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var opened = new List<Uri>();
        var executor = CreateExecutor(
            handler,
            getSettings: () => AppSettings.Default with { EndpointMode = EndpointMode.CloudOnly, CloudEndpoint = new Uri("https://myopenhab.org/") },
            getApiToken: _ => null,
            getCloudCredentials: kind => kind == TransportKind.Cloud ? new CloudCredentials("user@example.com", "secret") : null,
            opened);

        await executor.ExecuteAsync(new NotificationAction("command", "KitchenLight:OFF"), CancellationToken.None);

        Assert.Empty(opened);
    }

    [Fact]
    public async Task ExecuteAsync_CommandInAutomaticMode_UsesLocalEndpointFirst()
    {
        var handler = new CaptureHandler(req =>
        {
            Assert.Equal("http://openhab.local:8080/rest/items/KitchenLight", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var opened = new List<Uri>();
        var executor = CreateExecutor(
            handler,
            getSettings: () => AppSettings.Default with
            {
                EndpointMode = EndpointMode.Automatic,
                LocalEndpoint = new Uri("http://openhab.local:8080/"),
                CloudEndpoint = new Uri("https://myopenhab.org/")
            },
            getApiToken: _ => null,
            getCloudCredentials: _ => null,
            opened);

        await executor.ExecuteAsync(new NotificationAction("command", "KitchenLight:ON"), CancellationToken.None);

        Assert.Empty(opened);
    }

    [Theory]
    [InlineData("http", "http://example.com")]
    [InlineData("https", "https://openhab.org")]
    public async Task ExecuteAsync_HttpAndHttpsActions_OpenExternalUri(string type, string url)
    {
        var opened = new List<Uri>();
        var executor = CreateExecutor(new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)), opened: opened);

        await executor.ExecuteAsync(new NotificationAction(type, url), CancellationToken.None);

        Assert.Single(opened);
        Assert.Equal(new Uri(url), opened[0]);
    }

    [Fact]
    public async Task ExecuteAsync_UiBasicUiRelativeAction_ResolvesAgainstSelectedEndpoint()
    {
        var opened = new List<Uri>();
        var executor = CreateExecutor(
            new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
            getSettings: () => AppSettings.Default with
            {
                EndpointMode = EndpointMode.LocalOnly,
                LocalEndpoint = new Uri("https://example.test/openhab/")
            },
            opened: opened);

        await executor.ExecuteAsync(new NotificationAction("ui", "/basicui/app?w=0000&sitemap=main"), CancellationToken.None);

        Assert.Single(opened);
        Assert.Equal("https://example.test/openhab/basicui/app?w=0000&sitemap=main", opened[0].ToString());
    }

    [Fact]
    public async Task ExecuteAsync_UiAbsolutePath_ResolvesAgainstSelectedEndpoint()
    {
        var opened = new List<Uri>();
        var executor = CreateExecutor(
            new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
            getSettings: () => AppSettings.Default with
            {
                EndpointMode = EndpointMode.LocalOnly,
                LocalEndpoint = new Uri("https://example.test/openhab/")
            },
            opened: opened);

        await executor.ExecuteAsync(new NotificationAction("ui", "/some/absolute/path"), CancellationToken.None);

        Assert.Single(opened);
        Assert.Equal("https://example.test/openhab/some/absolute/path", opened[0].ToString());
    }

    [Fact]
    public async Task ExecuteAsync_UiNavigateAction_ResolvesToMainUiPageRoute()
    {
        var opened = new List<Uri>();
        var executor = CreateExecutor(
            new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
            getSettings: () => AppSettings.Default with
            {
                EndpointMode = EndpointMode.LocalOnly,
                LocalEndpoint = new Uri("https://example.test/openhab/")
            },
            opened: opened);

        await executor.ExecuteAsync(new NotificationAction("ui", "navigate:/page/my_floorplan_page"), CancellationToken.None);

        Assert.Single(opened);
        Assert.Equal("https://example.test/openhab/page/my_floorplan_page", opened[0].ToString());
    }

    [Fact]
    public async Task ExecuteAsync_UiPopupAction_EncodesPopupIntentIntoMainUiUrl()
    {
        var opened = new List<Uri>();
        var executor = CreateExecutor(
            new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
            getSettings: () => AppSettings.Default with
            {
                EndpointMode = EndpointMode.LocalOnly,
                LocalEndpoint = new Uri("https://example.test/openhab/")
            },
            opened: opened);

        await executor.ExecuteAsync(new NotificationAction("ui", "popup:oh-clock-card"), CancellationToken.None);

        Assert.Single(opened);
        Assert.Equal("https://example.test/openhab/?notificationPopup=oh-clock-card", opened[0].ToString());
    }

    [Theory]
    [InlineData("rule", "my-rule")]
    [InlineData("app", "android=com.openhab")]
    [InlineData("unknown", "xyz")]
    public async Task ExecuteAsync_UnsupportedActions_NoOp(string type, string payload)
    {
        var called = false;
        var handler = new CaptureHandler(_ =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var opened = new List<Uri>();
        var executor = CreateExecutor(handler, opened: opened);

        await executor.ExecuteAsync(new NotificationAction(type, payload), CancellationToken.None);

        Assert.False(called);
        Assert.Empty(opened);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidCommandPayload_NoOp()
    {
        var called = false;
        var handler = new CaptureHandler(_ =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var opened = new List<Uri>();
        var executor = CreateExecutor(handler, opened: opened);

        await executor.ExecuteAsync(new NotificationAction("command", "MissingDelimiter"), CancellationToken.None);

        Assert.False(called);
        Assert.Empty(opened);
    }

    private static NotificationActionExecutor CreateExecutor(
        HttpMessageHandler handler,
        Func<AppSettings>? getSettings = null,
        Func<TransportKind, string?>? getApiToken = null,
        Func<TransportKind, CloudCredentials?>? getCloudCredentials = null,
        List<Uri>? opened = null)
    {
        var httpClient = new HttpClient(handler);
        opened ??= new List<Uri>();

        return new NotificationActionExecutor(
            httpClient,
            getSettings ?? (() => AppSettings.Default),
            getApiToken ?? (_ => null),
            getCloudCredentials ?? (_ => null),
            openExternal: uri =>
            {
                opened.Add(uri);
                return Task.CompletedTask;
            });
    }

    private sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }
}
