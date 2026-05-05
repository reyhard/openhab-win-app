using OpenHab.Core.Auth;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;
using System.Text.RegularExpressions;

namespace OpenHab.App.Settings;

public sealed class AppSettingsController
{
    private static readonly Regex SitemapNamePattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);
    private const string CredentialResource = "OpenHabAuth";
    private const string LocalTokenKey = "local-token";
    private const string CloudTokenKey = "cloud-token";

    private readonly ICredentialStore? credentialStore;

    public AppSettings Current { get; private set; } = AppSettings.Default;

    public AppSettingsController(ICredentialStore? credentialStore = null)
    {
        this.credentialStore = credentialStore;
    }

    public void SetSkin(SitemapSkinKind skin)
    {
        Current = Current with { Skin = skin };
    }

    public void SetEndpointMode(EndpointMode endpointMode)
    {
        Current = Current with { EndpointMode = endpointMode };
    }

    public void SetEndpoints(Uri localEndpoint, Uri cloudEndpoint)
    {
        ArgumentNullException.ThrowIfNull(localEndpoint);
        ArgumentNullException.ThrowIfNull(cloudEndpoint);

        if (!localEndpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("Local endpoint must be an absolute URI.", nameof(localEndpoint));
        }

        if (!IsHttpOrHttps(localEndpoint))
        {
            throw new ArgumentException("Local endpoint must use HTTP or HTTPS.", nameof(localEndpoint));
        }

        if (!cloudEndpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("Cloud endpoint must be an absolute URI.", nameof(cloudEndpoint));
        }

        if (!IsHttpOrHttps(cloudEndpoint))
        {
            throw new ArgumentException("Cloud endpoint must use HTTP or HTTPS.", nameof(cloudEndpoint));
        }

        Current = Current with
        {
            LocalEndpoint = localEndpoint,
            CloudEndpoint = cloudEndpoint
        };
    }

    public void SetSitemapName(string sitemapName)
    {
        if (string.IsNullOrWhiteSpace(sitemapName))
        {
            throw new ArgumentException("Sitemap name cannot be blank.", nameof(sitemapName));
        }
        if (!SitemapNamePattern.IsMatch(sitemapName))
        {
            throw new ArgumentException("Sitemap name can only contain letters, digits, underscores, and hyphens.", nameof(sitemapName));
        }

        Current = Current with { SitemapName = sitemapName };
    }

    public async Task SetApiTokenAsync(TransportKind transportKind, string token, CancellationToken cancellationToken = default)
    {
        if (credentialStore is null)
            throw new InvalidOperationException("No credential store is configured.");

        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Token must not be blank.", nameof(token));

        var key = transportKind == TransportKind.Local ? LocalTokenKey : CloudTokenKey;
        await credentialStore.StoreAsync(CredentialResource, key, token, cancellationToken);

        Current = transportKind == TransportKind.Local
            ? Current with { HasLocalToken = true }
            : Current with { HasCloudToken = true };
    }

    public async Task ClearApiTokenAsync(TransportKind transportKind, CancellationToken cancellationToken = default)
    {
        if (credentialStore is null)
            throw new InvalidOperationException("No credential store is configured.");

        var key = transportKind == TransportKind.Local ? LocalTokenKey : CloudTokenKey;
        await credentialStore.RemoveAsync(CredentialResource, key, cancellationToken);

        Current = transportKind == TransportKind.Local
            ? Current with { HasLocalToken = false }
            : Current with { HasCloudToken = false };
    }

    public async Task<string?> GetApiTokenAsync(TransportKind transportKind, CancellationToken cancellationToken = default)
    {
        if (credentialStore is null)
            throw new InvalidOperationException("No credential store is configured.");

        var key = transportKind == TransportKind.Local ? LocalTokenKey : CloudTokenKey;
        return await credentialStore.RetrieveAsync(CredentialResource, key, cancellationToken);
    }

    private static bool IsHttpOrHttps(Uri endpoint)
    {
        return endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps;
    }
}
