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
    private readonly string cacheRootDirectory;
    private readonly int maxBytes;

    public NotificationMediaResolver(
        HttpClient httpClient,
        Func<AppSettings> getSettings,
        Func<TransportKind, string?> getApiToken,
        Func<TransportKind, CloudCredentials?> getCloudCredentials,
        string? cacheRootDirectory = null,
        int maxBytes = 2 * 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(getSettings);
        ArgumentNullException.ThrowIfNull(getApiToken);
        ArgumentNullException.ThrowIfNull(getCloudCredentials);

        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        this.httpClient = httpClient;
        this.getSettings = getSettings;
        this.getApiToken = getApiToken;
        this.getCloudCredentials = getCloudCredentials;
        this.cacheRootDirectory = cacheRootDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenHab.WinApp",
                "NotificationMedia");
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

            var extension = ResolveFileExtension(bytes.Value.mediaType);
            var cachePath = BuildCachePath(fetchUri, extension);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            await File.WriteAllBytesAsync(cachePath, bytes.Value.data, cancellationToken);
            return new Uri(cachePath);
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
        if (!basePath.EndsWith("/", StringComparison.Ordinal))
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

    private string BuildCachePath(Uri fetchUri, string extension)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fetchUri.AbsoluteUri)));
        return Path.Combine(cacheRootDirectory, $"{hash}{extension}");
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
