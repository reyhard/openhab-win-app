using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OpenHab.Rendering;
using Windows.Storage.Streams;

namespace OpenHab.Windows.Tray.Rendering;

internal static class OpenHabIconImageSourceLoader
{
    private static readonly HttpClient IconHttpClient = new();
    private static readonly ConcurrentDictionary<string, IconPayload> IconPayloadCache = new(StringComparer.Ordinal);
    private static readonly Regex SvgOpenTagRegex = new(
        "<svg\\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private sealed record IconPayload(byte[] Bytes, string? MediaType);

    internal sealed record LoadResult(
        bool Success,
        string? Error,
        string DecodedAs,
        string? MediaType,
        int BytesLength,
        bool FromCache);

    internal static async Task<LoadResult> TryLoadAsync(
        Image image,
        Uri iconUri,
        string? iconColor,
        SitemapControlFactory.IconAuthContext? authContext)
    {
        var cacheKey = BuildPayloadCacheKey(iconUri, iconColor, authContext);
        if (IconPayloadCache.TryGetValue(cacheKey, out var cachedPayload))
        {
            var targetSize = ResolveImageDecodeSize(image);
            var cachedSource = await CreateImageSourceFromBytesAsync(
                cachedPayload.Bytes,
                cachedPayload.MediaType,
                iconColor,
                targetSize.Width,
                targetSize.Height);

            if (cachedSource is null)
            {
                return Failed($"decode-failed(media={cachedPayload.MediaType ?? "unknown"},source=cache)");
            }

            image.Source = cachedSource;
            return Succeeded(cachedSource, cachedPayload.MediaType, cachedPayload.Bytes.Length, fromCache: true);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, iconUri);
        if (authContext is { } context)
        {
            ApplyAuthHeaders(request, context);
        }

        using var response = await IconHttpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead);
        if (!response.IsSuccessStatusCode)
        {
            return Failed($"status={(int)response.StatusCode}", response.Content.Headers.ContentType?.MediaType);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync();
        if (bytes.Length == 0)
        {
            return Failed("empty");
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var decodeSize = ResolveImageDecodeSize(image);
        var source = await CreateImageSourceFromBytesAsync(
            bytes,
            mediaType,
            iconColor,
            decodeSize.Width,
            decodeSize.Height);

        if (source is null)
        {
            return Failed($"decode-failed(media={mediaType ?? "unknown"})", mediaType);
        }

        image.Source = source;
        IconPayloadCache.TryAdd(cacheKey, new IconPayload(bytes.ToArray(), mediaType));
        return Succeeded(source, mediaType, bytes.Length, fromCache: false);
    }

    internal static string BuildPayloadCacheKey(
        Uri iconUri,
        string? iconColor,
        SitemapControlFactory.IconAuthContext? authContext)
    {
        return SitemapUiLogic.BuildIconPayloadCacheKey(iconUri, iconColor, GetAuthMode(authContext));
    }

    internal static async Task<ImageSource?> CreateImageSourceFromBytesAsync(
        byte[] bytes,
        string? mediaType,
        string? iconColor = null,
        double rasterizePixelWidth = 0,
        double rasterizePixelHeight = 0)
    {
        if (LooksLikeSvg(mediaType, bytes))
        {
            var tintedBytes = TryApplySvgColorTint(bytes, iconColor) ?? bytes;
            var svg = await CreateSvgFromBytesAsync(tintedBytes, rasterizePixelWidth, rasterizePixelHeight);
            if (svg is not null)
            {
                return svg;
            }
        }

        try
        {
            return await CreateBitmapFromBytesAsync(bytes);
        }
        catch
        {
            return await CreateSvgFromBytesAsync(bytes, rasterizePixelWidth, rasterizePixelHeight);
        }
    }

    private static (double Width, double Height) ResolveImageDecodeSize(Image image)
    {
        var width = double.IsNaN(image.Width) || image.Width <= 0 ? 0 : image.Width;
        var height = double.IsNaN(image.Height) || image.Height <= 0 ? 0 : image.Height;
        return (width, height);
    }

    private static LoadResult Succeeded(ImageSource source, string? mediaType, int bytesLength, bool fromCache)
    {
        return new LoadResult(
            Success: true,
            Error: null,
            DecodedAs: source is SvgImageSource ? "svg" : "bitmap",
            MediaType: mediaType,
            BytesLength: bytesLength,
            FromCache: fromCache);
    }

    private static LoadResult Failed(string error, string? mediaType = null)
    {
        return new LoadResult(
            Success: false,
            Error: error,
            DecodedAs: string.Empty,
            MediaType: mediaType,
            BytesLength: 0,
            FromCache: false);
    }

    private static byte[]? TryApplySvgColorTint(byte[] svgBytes, string? iconColor)
    {
        if (string.IsNullOrWhiteSpace(iconColor))
        {
            return null;
        }

        if (!TryNormalizeColorToHex(iconColor, out var hexColor))
        {
            return null;
        }

        try
        {
            var svgText = Encoding.UTF8.GetString(svgBytes);
            if (string.IsNullOrWhiteSpace(svgText) || svgText.IndexOf("<svg", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return null;
            }

            var match = SvgOpenTagRegex.Match(svgText);
            if (!match.Success)
            {
                return null;
            }

            var replacement = $"<svg style=\"color:{hexColor};\"";
            var tinted = svgText[..match.Index] + replacement + svgText[(match.Index + match.Length)..];
            return Encoding.UTF8.GetBytes(tinted);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryNormalizeColorToHex(string color, out string hex)
    {
        hex = string.Empty;
        if (!TryParseColor(color, out var parsed))
        {
            return false;
        }

        hex = $"#{parsed.R:X2}{parsed.G:X2}{parsed.B:X2}";
        return true;
    }

    private static bool TryParseColor(string? color, out global::Windows.UI.Color parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(color))
        {
            return false;
        }

        var input = color.Trim();
        if (input.StartsWith('#'))
        {
            var hex = input[1..];
            if (hex.Length == 3)
            {
                hex = string.Concat(hex.Select(c => $"{c}{c}"));
            }

            if (hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            {
                parsed = CreateColor(
                    255,
                    (byte)((rgb >> 16) & 0xFF),
                    (byte)((rgb >> 8) & 0xFF),
                    (byte)(rgb & 0xFF));
                return true;
            }

            if (hex.Length == 8 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var argb))
            {
                parsed = CreateColor(
                    (byte)((argb >> 24) & 0xFF),
                    (byte)((argb >> 16) & 0xFF),
                    (byte)((argb >> 8) & 0xFF),
                    (byte)(argb & 0xFF));
                return true;
            }
        }

        var property = typeof(Microsoft.UI.Colors).GetProperty(input, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
        if (property?.PropertyType == typeof(global::Windows.UI.Color))
        {
            parsed = (global::Windows.UI.Color)property.GetValue(null)!;
            return true;
        }

        return false;
    }

    private static async Task<SvgImageSource?> CreateSvgFromBytesAsync(
        byte[] bytes,
        double rasterizePixelWidth = 0,
        double rasterizePixelHeight = 0)
    {
        var svg = new SvgImageSource();
        if (rasterizePixelWidth > 0)
        {
            svg.RasterizePixelWidth = rasterizePixelWidth;
        }

        if (rasterizePixelHeight > 0)
        {
            svg.RasterizePixelHeight = rasterizePixelHeight;
        }

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        var status = await svg.SetSourceAsync(stream);
        return status == SvgImageSourceLoadStatus.Success ? svg : null;
    }

    private static bool LooksLikeSvg(string? mediaType, byte[] bytes)
    {
        if (!string.IsNullOrWhiteSpace(mediaType) &&
            mediaType.Contains("svg", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var sampleLength = Math.Min(bytes.Length, 256);
        if (sampleLength == 0)
        {
            return false;
        }

        var sample = Encoding.UTF8.GetString(bytes, 0, sampleLength).TrimStart('\uFEFF', '\t', '\r', '\n', ' ');
        return sample.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) ||
               sample.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<BitmapImage> CreateBitmapFromBytesAsync(byte[] bytes)
    {
        var bitmap = new BitmapImage();
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    private static global::Windows.UI.Color CreateColor(byte a, byte r, byte g, byte b)
    {
        return global::Windows.UI.Color.FromArgb(a, r, g, b);
    }

    private static void ApplyAuthHeaders(HttpRequestMessage request, SitemapControlFactory.IconAuthContext authContext)
    {
        if (!string.IsNullOrWhiteSpace(authContext.ApiToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authContext.ApiToken);
            return;
        }

        if (!string.IsNullOrWhiteSpace(authContext.BasicUserName))
        {
            var raw = $"{authContext.BasicUserName}:{authContext.BasicPassword ?? string.Empty}";
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64);
        }
    }

    private static string GetAuthMode(SitemapControlFactory.IconAuthContext? authContext)
    {
        if (authContext is null) return "none";

        var context = authContext.Value;
        if (!string.IsNullOrWhiteSpace(context.ApiToken)) return "bearer";
        if (!string.IsNullOrWhiteSpace(context.BasicUserName)) return "basic";
        return "none";
    }
}
