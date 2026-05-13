using System.Net;
using System.Net.Http.Headers;
using System.Text;
using OpenHab.App.Notifications;
using OpenHab.App.Settings;
using OpenHab.Core.Profiles;

namespace OpenHab.App.Tests.Notifications;

public sealed class NotificationMediaResolverTests : IDisposable
{
    private readonly string tempRoot;

    public NotificationMediaResolverTests()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "OpenHab.NotificationMediaResolverTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveAsync_Blank_ReturnsNull(string? value)
    {
        var resolver = CreateResolver(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)), EndpointMode.LocalOnly);

        var result = await resolver.ResolveAsync(value, CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("http://example.test/camera.jpg")]
    [InlineData("https://example.test/camera.jpg")]
    public async Task ResolveAsync_AbsoluteHttpHttps_ReturnsOriginalWithoutFetching(string absolute)
    {
        var called = false;
        var resolver = CreateResolver(
            new StubHandler(_ =>
            {
                called = true;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            EndpointMode.LocalOnly);

        var result = await resolver.ResolveAsync(absolute, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(absolute, result!.ToString());
        Assert.False(called);
    }

    [Fact]
    public async Task ResolveAsync_LocalRelativePath_FetchesAndCachesWithBearerAuth()
    {
        var handler = new CaptureHandler(req =>
        {
            Assert.Equal("http://openhab.local:8080/static/camera.jpg", req.RequestUri!.ToString());
            Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
            Assert.Equal("local-token", req.Headers.Authorization?.Parameter);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4, 5])
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return response;
        });

        var resolver = CreateResolver(handler, EndpointMode.LocalOnly, localToken: "local-token");

        var result = await resolver.ResolveAsync("/static/camera.jpg", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.IsFile);
        Assert.True(File.Exists(result.LocalPath));
        Assert.Equal(".jpg", Path.GetExtension(result.LocalPath));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, await File.ReadAllBytesAsync(result.LocalPath));
    }

    [Fact]
    public async Task ResolveAsync_ItemReference_UsesRestItemStatePath()
    {
        var handler = new CaptureHandler(req =>
        {
            Assert.Equal("http://openhab.local:8080/rest/items/Camera_Image/state", req.RequestUri!.ToString());
            return ImageResponse("image/png", [9, 8, 7]);
        });
        var resolver = CreateResolver(handler, EndpointMode.LocalOnly);

        var result = await resolver.ResolveAsync("item:Camera_Image", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.IsFile);
        Assert.Equal(".png", Path.GetExtension(result.LocalPath));
    }

    [Fact]
    public async Task ResolveAsync_ItemReferenceDataUri_DecodesImageBeforeCaching()
    {
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        var dataUri = $"data:image/png;base64,{Convert.ToBase64String(pngBytes)}";
        var handler = new CaptureHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(dataUri)
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            return response;
        });
        var resolver = CreateResolver(handler, EndpointMode.LocalOnly);

        var result = await resolver.ResolveAsync("item:Camera_Image", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.IsFile);
        Assert.Equal(".png", Path.GetExtension(result.LocalPath));
        Assert.Equal(pngBytes, await File.ReadAllBytesAsync(result.LocalPath));
    }

    [Fact]
    public async Task ResolveAsync_CloudTransport_UsesBasicAuthFromCloudCredentials()
    {
        var handler = new CaptureHandler(req =>
        {
            Assert.Equal("https://myopenhab.org/static/camera.jpg", req.RequestUri!.ToString());
            Assert.Equal("Basic", req.Headers.Authorization?.Scheme);
            var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("user@example.com:secret"));
            Assert.Equal(expected, req.Headers.Authorization?.Parameter);
            return ImageResponse("image/jpeg", [1, 2, 3]);
        });

        var resolver = CreateResolver(
            handler,
            EndpointMode.CloudOnly,
            cloudCredentials: new CloudCredentials("user@example.com", "secret"));

        var result = await resolver.ResolveAsync("/static/camera.jpg", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.IsFile);
    }

    [Fact]
    public async Task ResolveAsync_HonorsBoundedSize_ReturnsNullWhenTooLarge()
    {
        var bytes = new byte[(2 * 1024 * 1024) + 1];
        var handler = new CaptureHandler(_ => ImageResponse("image/jpeg", bytes));
        var resolver = CreateResolver(handler, EndpointMode.LocalOnly, maxBytes: 2 * 1024 * 1024);

        var result = await resolver.ResolveAsync("/static/camera.jpg", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_UnknownContentType_UsesBinExtension()
    {
        var handler = new CaptureHandler(_ => ImageResponse("application/octet-stream", [1, 2, 3]));
        var resolver = CreateResolver(handler, EndpointMode.LocalOnly);

        var result = await resolver.ResolveAsync("/static/camera", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(".bin", Path.GetExtension(result!.LocalPath));
    }

    [Fact]
    public async Task ResolveAsync_AutomaticMode_FallsBackToCloudWhenLocalFails()
    {
        var calls = new List<(string Uri, string? Scheme)>();
        var handler = new CaptureHandler(req =>
        {
            calls.Add((req.RequestUri!.ToString(), req.Headers.Authorization?.Scheme));
            if (req.RequestUri!.Host.Equals("openhab.local", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            Assert.Equal("myopenhab.org", req.RequestUri.Host);
            Assert.Equal("Basic", req.Headers.Authorization?.Scheme);
            return ImageResponse("image/jpeg", [3, 2, 1]);
        });

        var resolver = CreateResolver(
            handler,
            EndpointMode.Automatic,
            localToken: "local-token",
            cloudCredentials: new CloudCredentials("user@example.com", "secret"));

        var result = await resolver.ResolveAsync("/static/camera.jpg", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.IsFile);
        Assert.Equal(new[] { "openhab.local", "myopenhab.org" }, calls.Select(c => new Uri(c.Uri).Host).ToArray());
        Assert.Equal("Bearer", calls[0].Scheme);
        Assert.Equal("Basic", calls[1].Scheme);
    }

    [Fact]
    public async Task ResolveAsync_RelativePath_PreservesEndpointBasePathPrefix()
    {
        var handler = new CaptureHandler(req =>
        {
            Assert.Equal("https://example.test/openhab/static/camera.jpg", req.RequestUri!.ToString());
            return ImageResponse("image/jpeg", [1, 2, 3]);
        });
        var resolver = CreateResolver(
            handler,
            EndpointMode.LocalOnly,
            localEndpoint: new Uri("https://example.test/openhab"));

        var result = await resolver.ResolveAsync("/static/camera.jpg", CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task ResolveAsync_ItemReference_PreservesEndpointBasePathPrefix()
    {
        var handler = new CaptureHandler(req =>
        {
            Assert.Equal("https://example.test/openhab/rest/items/Camera_Image/state", req.RequestUri!.ToString());
            return ImageResponse("image/png", [9, 8, 7]);
        });
        var resolver = CreateResolver(
            handler,
            EndpointMode.LocalOnly,
            localEndpoint: new Uri("https://example.test/openhab"));

        var result = await resolver.ResolveAsync("item:Camera_Image", CancellationToken.None);

        Assert.NotNull(result);
    }

    private NotificationMediaResolver CreateResolver(
        HttpMessageHandler handler,
        EndpointMode endpointMode,
        string? localToken = null,
        CloudCredentials? cloudCredentials = null,
        Uri? localEndpoint = null,
        Uri? cloudEndpoint = null,
        int maxBytes = 2 * 1024 * 1024)
    {
        var client = new HttpClient(handler);
        return new NotificationMediaResolver(
            client,
            getSettings: () => AppSettings.Default with
            {
                EndpointMode = endpointMode,
                LocalEndpoint = localEndpoint ?? new Uri("http://openhab.local:8080/"),
                CloudEndpoint = cloudEndpoint ?? new Uri("https://myopenhab.org/")
            },
            getApiToken: kind => kind == TransportKind.Local ? localToken : null,
            getCloudCredentials: kind => kind == TransportKind.Cloud ? cloudCredentials : null,
            cacheRootDirectory: tempRoot,
            maxBytes: maxBytes);
    }

    private static HttpResponseMessage ImageResponse(string mediaType, byte[] bytes)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return response;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors in tests.
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }

    private sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }
}
