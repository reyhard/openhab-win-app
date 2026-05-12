namespace OpenHab.App.MainUi;

public static class MainUiUrlBuilder
{
    public static Uri Build(Uri endpoint, string? route)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var builder = new UriBuilder(endpoint)
        {
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty
        };

        if (string.Equals(builder.Host, "myopenhab.org", StringComparison.OrdinalIgnoreCase))
        {
            builder.Host = "home.myopenhab.org";
        }

        var normalizedRoute = string.IsNullOrWhiteSpace(route) ? "/" : route.Trim();
        if (!normalizedRoute.StartsWith("/", StringComparison.Ordinal))
        {
            normalizedRoute = "/" + normalizedRoute;
        }

        return new Uri(builder.Uri, normalizedRoute.TrimStart('/'));
    }

    public static bool IsSameHost(Uri expectedBase, Uri candidate)
    {
        ArgumentNullException.ThrowIfNull(expectedBase);
        ArgumentNullException.ThrowIfNull(candidate);

        return string.Equals(expectedBase.Scheme, candidate.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(expectedBase.Host, candidate.Host, StringComparison.OrdinalIgnoreCase)
            && expectedBase.Port == candidate.Port;
    }
}
