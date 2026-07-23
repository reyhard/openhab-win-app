namespace OpenHab.Core.Api;

public static class OpenHabEndpointUri
{
    public static Uri Combine(Uri baseUri, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentNullException.ThrowIfNull(relativePath);

        var baseBuilder = new UriBuilder(baseUri);
        if (!baseBuilder.Path.EndsWith('/'))
        {
            baseBuilder.Path += "/";
        }

        return new Uri(baseBuilder.Uri, relativePath.TrimStart('/'));
    }
}
