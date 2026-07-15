using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using OpenHab.App.Settings;
using OpenHab.Core;
using OpenHab.Core.Profiles;

namespace OpenHab.App.Notifications;

public sealed class NotificationMediaResolver
{
    private readonly HttpClient httpClient;
    private readonly Func<AppSettings> getSettings;
    private readonly Func<TransportKind, string?> getApiToken;
    private readonly Func<TransportKind, CloudCredentials?> getCloudCredentials;
    private readonly NotificationMediaCache cache;
    private readonly int maxBytes;

    public NotificationMediaResolver(
        HttpClient httpClient,
        Func<AppSettings> getSettings,
        Func<TransportKind, string?> getApiToken,
        Func<TransportKind, CloudCredentials?> getCloudCredentials,
        NotificationMediaCache? cache = null,
        string? cacheRootDirectory = null,
        int maxBytes = 2 * 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(getSettings);
        ArgumentNullException.ThrowIfNull(getApiToken);
        ArgumentNullException.ThrowIfNull(getCloudCredentials);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        this.httpClient = httpClient;
        this.getSettings = getSettings;
        this.getApiToken = getApiToken;
        this.getCloudCredentials = getCloudCredentials;
        if (cache is not null && cacheRootDirectory is not null)
        {
            throw new ArgumentException("Specify either a notification media cache or a cache root directory, not both.");
        }

        this.cache = cache ?? new NotificationMediaCache(cacheRootDirectory);
        this.maxBytes = maxBytes;
    }

    public async Task<Uri?> ResolveAsync(string? mediaAttachmentUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mediaAttachmentUrl))
        {
            return null;
        }

        var trimmed = mediaAttachmentUrl.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri)
            && (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
        {
            return absoluteUri;
        }

        var settings = getSettings();
        foreach (var transport in GetTransportOrder(settings.EndpointMode))
        {
            var baseUri = transport == TransportKind.Cloud ? settings.CloudEndpoint : settings.LocalEndpoint;
            var fetchUri = BuildFetchUri(baseUri, trimmed);
            if (fetchUri is null)
            {
                continue;
            }

            var bytes = await TryFetchBytesAsync(fetchUri, transport, cancellationToken);
            if (bytes is null)
            {
                continue;
            }

            var media = DecodeDataUriIfNeeded(bytes.Value.data, bytes.Value.mediaType);
            if (media is null)
            {
                DiagnosticLogger.Warn($"Notification media skipped due to invalid data URI: endpoint='{fetchUri.Host}'");
                continue;
            }

            var extension = ResolveFileExtension(media.Value.mediaType);
            var cacheKey = BuildCacheKey(fetchUri);
            var cacheUri = await cache.StoreAsync(cacheKey, extension, media.Value.data, cancellationToken);
            if (cacheUri is null)
            {
                DiagnosticLogger.Warn(
                    $"Notification media cache unavailable: source='{ResolveSourceKind(trimmed)}', endpoint='{fetchUri.Host}', media='{media.Value.mediaType ?? "unknown"}', bytes={media.Value.data.Length}");
                return null;
            }

            DiagnosticLogger.Info(
                $"Notification media cached: source='{ResolveSourceKind(trimmed)}', endpoint='{fetchUri.Host}', media='{media.Value.mediaType ?? "unknown"}', bytes={media.Value.data.Length}, extension='{extension}'");
            return cacheUri;
        }

        return null;
    }

    private static IEnumerable<TransportKind> GetTransportOrder(EndpointMode endpointMode)
    {
        if (endpointMode == EndpointMode.CloudOnly)
        {
            yield return TransportKind.Cloud;
            yield break;
        }

        if (endpointMode == EndpointMode.LocalOnly)
        {
            yield return TransportKind.Local;
            yield break;
        }

        yield return TransportKind.Local;
        yield return TransportKind.Cloud;
    }

    private static Uri? BuildFetchUri(Uri baseUri, string mediaAttachmentUrl)
    {
        if (mediaAttachmentUrl.StartsWith("item:", StringComparison.OrdinalIgnoreCase))
        {
            var itemName = mediaAttachmentUrl["item:".Length..].Trim();
            if (string.IsNullOrWhiteSpace(itemName))
            {
                return null;
            }

            return BuildEndpointUri(baseUri, $"rest/items/{Uri.EscapeDataString(itemName)}/state");
        }

        if (mediaAttachmentUrl.StartsWith('/'))
        {
            return BuildEndpointUri(baseUri, mediaAttachmentUrl.TrimStart('/'));
        }

        return null;
    }

    private static Uri BuildEndpointUri(Uri endpointBaseUri, string relativePath)
    {
        var baseBuilder = new UriBuilder(endpointBaseUri);
        var basePath = baseBuilder.Path ?? string.Empty;
        if (!basePath.EndsWith('/'))
        {
            basePath += "/";
        }

        baseBuilder.Path = basePath;
        return new Uri(baseBuilder.Uri, relativePath.TrimStart('/'));
    }

    private async Task<(byte[] data, string? mediaType)?> TryFetchBytesAsync(
        Uri fetchUri,
        TransportKind transport,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, fetchUri);
        ApplyAuth(request, transport);

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            DiagnosticLogger.Warn($"Notification media request failed: endpoint='{fetchUri.Host}', status={(int)response.StatusCode}");
            return null;
        }

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > maxBytes)
        {
            DiagnosticLogger.Warn($"Notification media skipped due to content-length limit: endpoint='{fetchUri.Host}', bytes={contentLength.Value}");
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var data = await ReadBoundedAsync(stream, maxBytes, cancellationToken);
        if (data is null || data.Length == 0)
        {
            return null;
        }

        return (data, response.Content.Headers.ContentType?.MediaType);
    }

    private (byte[] data, string? mediaType)? DecodeDataUriIfNeeded(byte[] data, string? mediaType)
    {
        if (!LooksLikeDataUri(data))
        {
            return (data, mediaType);
        }

        var text = Encoding.UTF8.GetString(data);
        var commaIndex = text.IndexOf(',');
        if (commaIndex <= "data:".Length)
        {
            return null;
        }

        var metadata = text["data:".Length..commaIndex];
        if (!metadata.Contains(";base64", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var detectedMediaType = metadata.Split(';', 2)[0].Trim();
        if (detectedMediaType.Length == 0)
        {
            detectedMediaType = mediaType ?? "application/octet-stream";
        }

        try
        {
            var decoded = Convert.FromBase64String(text[(commaIndex + 1)..].Trim());
            if (decoded.Length == 0 || decoded.Length > maxBytes)
            {
                return null;
            }

            return (decoded, detectedMediaType);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool LooksLikeDataUri(byte[] data)
    {
        return data.Length >= 5
            && data[0] is (byte)'d' or (byte)'D'
            && data[1] is (byte)'a' or (byte)'A'
            && data[2] is (byte)'t' or (byte)'T'
            && data[3] is (byte)'a' or (byte)'A'
            && data[4] == (byte)':';
    }

    private void ApplyAuth(HttpRequestMessage request, TransportKind transport)
    {
        if (transport == TransportKind.Local)
        {
            var token = getApiToken(TransportKind.Local);
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            return;
        }

        var cloudCredentials = getCloudCredentials(TransportKind.Cloud);
        if (string.IsNullOrWhiteSpace(cloudCredentials?.UserName))
        {
            return;
        }

        var raw = $"{cloudCredentials.UserName}:{cloudCredentials.Password ?? string.Empty}";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64);
    }

    private static string BuildCacheKey(Uri fetchUri)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fetchUri.AbsoluteUri)));
    }

    private static string ResolveFileExtension(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return ".bin";
        }

        return mediaType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            "image/tiff" => ".tiff",
            "image/svg+xml" => ".svg",
            _ => ".bin"
        };
    }

    private static string ResolveSourceKind(string mediaAttachmentUrl)
    {
        if (mediaAttachmentUrl.StartsWith("item:", StringComparison.OrdinalIgnoreCase))
        {
            return "item";
        }

        if (mediaAttachmentUrl.StartsWith('/'))
        {
            return "relative";
        }

        return "unknown";
    }

    private static async Task<byte[]?> ReadBoundedAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (memory.Length + read > maxBytes)
            {
                return null;
            }

            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return memory.ToArray();
    }
}
