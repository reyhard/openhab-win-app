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
    private readonly object syncRoot = new();

    public AppSettings Current { get; private set; } = AppSettings.Default;

    public AppSettingsController(ICredentialStore? credentialStore = null)
    {
        this.credentialStore = credentialStore;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (credentialStore is null) return;

        var hasLocal = await credentialStore.RetrieveAsync(CredentialResource, LocalTokenKey, cancellationToken) is not null;
        var hasCloud = await credentialStore.RetrieveAsync(CredentialResource, CloudTokenKey, cancellationToken) is not null;
        lock (syncRoot)
        {
            Current = Current with { HasLocalToken = hasLocal, HasCloudToken = hasCloud };
        }
    }

    public void SetSkin(SitemapSkinKind skin)
    {
        lock (syncRoot)
        {
            Current = Current with { Skin = skin };
        }
    }

    public void SetEndpointMode(EndpointMode endpointMode)
    {
        lock (syncRoot)
        {
            Current = Current with { EndpointMode = endpointMode };
        }
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

        lock (syncRoot)
        {
            Current = Current with
            {
                LocalEndpoint = localEndpoint,
                CloudEndpoint = cloudEndpoint
            };
        }
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

        lock (syncRoot)
        {
            Current = Current with { SitemapName = sitemapName };
        }
    }

    public async Task SetApiTokenAsync(TransportKind transportKind, string token, CancellationToken cancellationToken = default)
    {
        if (credentialStore is null)
            throw new InvalidOperationException("No credential store is configured.");

        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Token must not be blank.", nameof(token));

        var key = transportKind switch
        {
            TransportKind.Local => LocalTokenKey,
            TransportKind.Cloud => CloudTokenKey,
            _ => throw new ArgumentOutOfRangeException(nameof(transportKind))
        };

        await credentialStore.StoreAsync(CredentialResource, key, token, cancellationToken);

        lock (syncRoot)
        {
            Current = transportKind switch
            {
                TransportKind.Local => Current with { HasLocalToken = true },
                TransportKind.Cloud => Current with { HasCloudToken = true },
                _ => throw new ArgumentOutOfRangeException(nameof(transportKind))
            };
        }
    }

    public async Task ClearApiTokenAsync(TransportKind transportKind, CancellationToken cancellationToken = default)
    {
        if (credentialStore is null)
            throw new InvalidOperationException("No credential store is configured.");

        var key = transportKind switch
        {
            TransportKind.Local => LocalTokenKey,
            TransportKind.Cloud => CloudTokenKey,
            _ => throw new ArgumentOutOfRangeException(nameof(transportKind))
        };

        await credentialStore.RemoveAsync(CredentialResource, key, cancellationToken);

        lock (syncRoot)
        {
            Current = transportKind switch
            {
                TransportKind.Local => Current with { HasLocalToken = false },
                TransportKind.Cloud => Current with { HasCloudToken = false },
                _ => throw new ArgumentOutOfRangeException(nameof(transportKind))
            };
        }
    }

    public async Task<string?> GetApiTokenAsync(TransportKind transportKind, CancellationToken cancellationToken = default)
    {
        if (credentialStore is null)
            throw new InvalidOperationException("No credential store is configured.");

        var key = transportKind switch
        {
            TransportKind.Local => LocalTokenKey,
            TransportKind.Cloud => CloudTokenKey,
            _ => throw new ArgumentOutOfRangeException(nameof(transportKind))
        };

        return await credentialStore.RetrieveAsync(CredentialResource, key, cancellationToken);
    }

    private static bool IsHttpOrHttps(Uri endpoint)
    {
        return endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps;
    }
}
