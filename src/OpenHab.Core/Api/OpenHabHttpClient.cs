using System.Net;
using System.Net.Http.Headers;

namespace OpenHab.Core.Api;

public sealed class OpenHabRequestException : Exception
{
    public OpenHabRequestException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public sealed class OpenHabHttpClient : IOpenHabClient
{
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;

    public OpenHabHttpClient(HttpClient httpClient, Uri baseUri)
    {
        _httpClient = httpClient;
        _baseUri = baseUri;
    }

    public Task SendCommandAsync(string itemName, string command, CancellationToken cancellationToken)
    {
        return SendPlainTextAsync(HttpMethod.Post, $"rest/items/{Uri.EscapeDataString(itemName)}", command, cancellationToken);
    }

    public Task SetItemStateAsync(string itemName, string state, CancellationToken cancellationToken)
    {
        return SendPlainTextAsync(HttpMethod.Put, $"rest/items/{Uri.EscapeDataString(itemName)}/state", state, cancellationToken);
    }

    public async Task<string> GetSitemapJsonAsync(string sitemapName, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(BuildUri($"rest/sitemaps/{Uri.EscapeDataString(sitemapName)}"), cancellationToken);
        await ThrowIfFailedAsync(response, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task SendPlainTextAsync(HttpMethod method, string relativePath, string body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildUri(relativePath))
        {
            Content = new StringContent(body)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await ThrowIfFailedAsync(response, cancellationToken);
    }

    private Uri BuildUri(string relativePath)
    {
        var baseBuilder = new UriBuilder(_baseUri);
        if (!baseBuilder.Path.EndsWith("/", StringComparison.Ordinal))
        {
            baseBuilder.Path += "/";
        }

        return new Uri(baseBuilder.Uri, relativePath.TrimStart('/'));
    }

    private static async Task ThrowIfFailedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var safeBody = body.Length > 120 ? body[..120] : body;
        throw new OpenHabRequestException(response.StatusCode, $"openHAB request failed with {(int)response.StatusCode} {response.ReasonPhrase}: {safeBody}");
    }
}
