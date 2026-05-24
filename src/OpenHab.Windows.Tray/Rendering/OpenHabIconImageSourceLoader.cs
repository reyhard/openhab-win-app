using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OpenHab.Rendering;
using Windows.Storage.Streams;

namespace OpenHab.Windows.Tray.Rendering;

internal static class OpenHabIconImageSourceLoader
{
    private const int MaxIconPayloadCacheEntries = 256;
    private static readonly HttpClient IconHttpClient = new();
    private static readonly SitemapMediaPayloadCache IconPayloadCache = new(MaxIconPayloadCacheEntries);

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
        if (IconPayloadCache.TryGet(cacheKey, out var cachedPayload))
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
            IconAuthHeaderHelper.ApplyAuthHeaders(request, context);
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
        IconPayloadCache.AddOrUpdate(cacheKey, bytes, mediaType);
        return Succeeded(source, mediaType, bytes.Length, fromCache: false);
    }

    internal static int CachedPayloadCount => IconPayloadCache.Count;

    internal static void ClearPayloadCache()
    {
        IconPayloadCache.Clear();
    }

    internal static string BuildPayloadCacheKey(
        Uri iconUri,
        string? iconColor,
        SitemapControlFactory.IconAuthContext? authContext)
    {
        return SitemapUiLogic.BuildIconPayloadCacheKey(iconUri, iconColor, IconAuthHeaderHelper.GetAuthMode(authContext));
    }

    internal static async Task<ImageSource?> CreateImageSourceFromBytesAsync(
        byte[] bytes,
        string? mediaType,
        string? iconColor = null,
        double rasterizePixelWidth = 0,
        double rasterizePixelHeight = 0)
    {
        if (OpenHabIconSvgPolicy.LooksLikeSvg(mediaType, bytes))
        {
            var tintedBytes = OpenHabIconSvgPolicy.TryApplySvgColorTint(bytes, iconColor) ?? bytes;
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

}
