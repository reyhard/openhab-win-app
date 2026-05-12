using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OpenHab.Core;
using OpenHab.Core.Ui;

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
    private readonly string? _apiToken;
    private readonly string? _basicUserName;
    private readonly string? _basicPassword;

    public OpenHabHttpClient(
        HttpClient httpClient,
        Uri baseUri,
        string? apiToken = null,
        string? basicUserName = null,
        string? basicPassword = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(baseUri);

        if (!string.IsNullOrWhiteSpace(apiToken) && !string.IsNullOrWhiteSpace(basicUserName))
        {
            throw new ArgumentException("Configure either bearer token auth or basic auth, not both.");
        }

        _httpClient = httpClient;
        _baseUri = baseUri;
        _apiToken = apiToken;
        _basicUserName = basicUserName;
        _basicPassword = basicPassword;
    }

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(_apiToken) || !string.IsNullOrWhiteSpace(_basicUserName);

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
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri($"rest/sitemaps/{Uri.EscapeDataString(sitemapName)}"));
        ApplyAuth(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await ThrowIfFailedAsync(response, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SitemapInfo>> GetSitemapsAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri("rest/sitemaps"));
        ApplyAuth(request);

        using var response = await _httpClient.SendAsync(request, ct);
        await ThrowIfFailedAsync(response, ct);

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var results = new List<SitemapInfo>();
        foreach (var element in json.RootElement.EnumerateArray())
        {
            var name = element.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                ? nameProp.GetString() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var label = element.TryGetProperty("label", out var labelProp) && labelProp.ValueKind == JsonValueKind.String
                ? labelProp.GetString() ?? name
                : name;
            results.Add(new SitemapInfo(name, label));
        }

        return results;
    }

    public async Task<IReadOnlyList<MainUiPageComponent>> GetMainUiPageComponentsAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri("rest/ui/components/ui:page"));
        ApplyAuth(request);

        using var response = await _httpClient.SendAsync(request, ct);
        await ThrowIfFailedAsync(response, ct);

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (json.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Main UI page component response must be a JSON array.");
        }

        var pages = new List<MainUiPageComponent>();
        foreach (var element in json.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var uid = ReadString(element, "uid");
            if (string.IsNullOrWhiteSpace(uid))
            {
                continue;
            }

            var component = ReadString(element, "component") ?? string.Empty;
            var config = ReadConfig(element);
            pages.Add(new MainUiPageComponent(uid, component, config));
        }

        return pages;
    }

    private async Task SendPlainTextAsync(HttpMethod method, string relativePath, string body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildUri(relativePath))
        {
            Content = new StringContent(body)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        ApplyAuth(request);

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

    private void ApplyAuth(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_apiToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
            return;
        }

        if (!string.IsNullOrWhiteSpace(_basicUserName))
        {
            var raw = $"{_basicUserName}:{_basicPassword ?? string.Empty}";
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64);
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static IReadOnlyDictionary<string, JsonElement> ReadConfig(JsonElement element)
    {
        if (!element.TryGetProperty("config", out var configElement) || configElement.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }

        var config = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in configElement.EnumerateObject())
        {
            config[property.Name] = property.Value.Clone();
        }

        return config;
    }

    private static async Task ThrowIfFailedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var safeBody = SensitiveTextRedactor.Redact(body);
        throw new OpenHabRequestException(response.StatusCode, $"openHAB request failed with {(int)response.StatusCode} {response.ReasonPhrase}: {safeBody}");
    }
}