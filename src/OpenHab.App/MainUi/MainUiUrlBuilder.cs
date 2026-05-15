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
            Fragment = string.Empty,
            UserName = string.Empty,
            Password = string.Empty
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

    public static Uri StripUserInfo(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (string.IsNullOrEmpty(uri.UserInfo))
        {
            return uri;
        }

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty
        };
        return builder.Uri;
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

        if (!normalized.StartsWith('/')
            && !normalized.StartsWith('?')
            && !normalized.StartsWith('#'))
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
