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

        var normalizedRoute = NormalizeInternalRoute(route);
        return new Uri(builder.Uri, normalizedRoute);
    }

    public static bool IsSameHost(Uri expectedBase, Uri candidate)
    {
        ArgumentNullException.ThrowIfNull(expectedBase);
        ArgumentNullException.ThrowIfNull(candidate);

        return string.Equals(expectedBase.Scheme, candidate.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(CanonicalizeHost(expectedBase.Host), CanonicalizeHost(candidate.Host), StringComparison.OrdinalIgnoreCase)
            && expectedBase.Port == candidate.Port;
    }

    private static string NormalizeInternalRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return "/";
        }

        var normalized = route.Trim();
        if (normalized.StartsWith("//", StringComparison.Ordinal))
        {
            return "/" + normalized.TrimStart('/');
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out _))
        {
            return "/" + normalized;
        }

        if (!normalized.StartsWith("/", StringComparison.Ordinal)
            && !normalized.StartsWith("?", StringComparison.Ordinal)
            && !normalized.StartsWith("#", StringComparison.Ordinal))
        {
            return "/" + normalized;
        }

        return normalized;
    }

    private static string CanonicalizeHost(string host)
    {
        return string.Equals(host, "myopenhab.org", StringComparison.OrdinalIgnoreCase)
            ? "home.myopenhab.org"
            : host;
    }
}
