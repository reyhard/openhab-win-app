using System.Security.Cryptography;
using System.Text;
using OpenHab.App.Settings;
using OpenHab.Core.Profiles;

namespace OpenHab.Windows.Tray.Rendering;

internal readonly record struct SitemapMediaCacheProfile(
    EndpointMode EndpointMode,
    Uri LocalEndpoint,
    Uri CloudEndpoint,
    bool HasLocalToken,
    bool HasCloudCredentials,
    string? CloudUserName,
    string? LocalCredentialFingerprint,
    string? CloudCredentialFingerprint)
{
    internal static SitemapMediaCacheProfile FromSettings(AppSettings settings) =>
        FromSettings(settings, localApiToken: null, cloudCredentials: null);

    internal static SitemapMediaCacheProfile FromSettings(
        AppSettings settings,
        string? localApiToken,
        CloudCredentials? cloudCredentials)
    {
        return new SitemapMediaCacheProfile(
            settings.EndpointMode,
            settings.LocalEndpoint,
            settings.CloudEndpoint,
            settings.HasLocalToken,
            settings.HasCloudCredentials,
            settings.CloudUserName,
            BuildCredentialFingerprint(localApiToken),
            BuildCredentialFingerprint(cloudCredentials?.UserName, cloudCredentials?.Password));
    }

    private static string? BuildCredentialFingerprint(params string?[] values)
    {
        if (values.All(string.IsNullOrEmpty))
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var value in values)
        {
            builder.Append(value?.Length ?? -1);
            builder.Append(':');
            builder.Append(value);
            builder.Append('|');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}

internal static class SitemapMediaCacheInvalidationPolicy
{
    internal static bool ShouldClear(SitemapMediaCacheProfile previous, SitemapMediaCacheProfile current) =>
        previous != current;
}
