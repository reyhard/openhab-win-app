using OpenHab.Core.Auth;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenHab.App.Settings;

public sealed class AppSettingsController
{
    public const int MinFlyoutWidth = 360;
    public const int MaxFlyoutWidth = 900;
    public const int MinNotificationPollIntervalSeconds = 10;
    public const int MaxNotificationPollIntervalSeconds = 600;

    private static readonly Regex SitemapNamePattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);
    private const string CredentialResource = "OpenHabAuth";
    private const string LocalTokenKey = "local-token";
    private const string CloudUserNameKey = "cloud-username";
    private const string CloudPasswordKey = "cloud-password";

    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenHab.WinApp",
        "settings.json");

    private readonly ICredentialStore? credentialStore;
    private readonly object syncRoot = new();

    private string SettingsDirectory => Path.GetDirectoryName(SettingsFilePath)!;

    public AppSettings Current { get; private set; } = AppSettings.Default;

    public AppSettingsController(ICredentialStore? credentialStore = null)
    {
        this.credentialStore = credentialStore;
        TryLoad();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (credentialStore is null) return;

        var hasLocal = await credentialStore.RetrieveAsync(CredentialResource, LocalTokenKey, cancellationToken) is not null;
        var cloudUserName = await credentialStore.RetrieveAsync(CredentialResource, CloudUserNameKey, cancellationToken);
        var cloudPassword = await credentialStore.RetrieveAsync(CredentialResource, CloudPasswordKey, cancellationToken);
        var hasCloudCredentials = !string.IsNullOrWhiteSpace(cloudUserName) && !string.IsNullOrWhiteSpace(cloudPassword);
        lock (syncRoot)
        {
            Current = Current with
            {
                HasLocalToken = hasLocal,
                HasCloudCredentials = hasCloudCredentials,
                CloudUserName = cloudUserName
            };
        }
    }

    public void SetSkin(SitemapSkinKind skin)
    {
        lock (syncRoot)
        {
            Current = Current with { Skin = skin };
        }
        _ = SaveAsync();
    }

    public void SetEndpointMode(EndpointMode endpointMode)
    {
        lock (syncRoot)
        {
            Current = Current with { EndpointMode = endpointMode };
        }
        _ = SaveAsync();
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
        _ = SaveAsync();
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
        _ = SaveAsync();
    }

    public void SetFollowSystemTheme(bool follow)
    {
        lock (syncRoot)
        {
            Current = Current with { FollowSystemTheme = follow };
        }
        _ = SaveAsync();
    }

    public void SetUseWindows11Icons(bool use)
    {
        lock (syncRoot)
        {
            Current = Current with { UseWindows11Icons = use };
        }
        _ = SaveAsync();
    }

    public int GetFlyoutAnimationDurationMs()
    {
        return Current.AnimationSpeed switch
        {
            FlyoutAnimationSpeed.Off => 0,
            FlyoutAnimationSpeed.Fast => 150,
            FlyoutAnimationSpeed.Default => 300,
            FlyoutAnimationSpeed.Slow => 450,
            _ => 300
        };
    }

    public void SetFlyoutWidth(int width)
    {
        if (width < MinFlyoutWidth || width > MaxFlyoutWidth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                $"Flyout width must be between {MinFlyoutWidth} and {MaxFlyoutWidth}.");
        }

        lock (syncRoot)
        {
            Current = Current with { FlyoutWidth = width };
        }
        _ = SaveAsync();
    }

    public void SetNotificationPollInterval(int seconds)
    {
        if (seconds < MinNotificationPollIntervalSeconds || seconds > MaxNotificationPollIntervalSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds),
                $"Notification poll interval must be between {MinNotificationPollIntervalSeconds} and {MaxNotificationPollIntervalSeconds} seconds.");
        }

        lock (syncRoot)
        {
            Current = Current with { NotificationPollIntervalSeconds = seconds };
        }
        _ = SaveAsync();
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
            TransportKind.Cloud => throw new ArgumentException("Cloud transport uses username and password credentials.", nameof(transportKind)),
            _ => throw new ArgumentOutOfRangeException(nameof(transportKind))
        };

        await credentialStore.StoreAsync(CredentialResource, key, token, cancellationToken);

        lock (syncRoot)
        {
            Current = transportKind switch
            {
                TransportKind.Local => Current with { HasLocalToken = true },
                _ => throw new ArgumentOutOfRangeException(nameof(transportKind))
            };
        }
        _ = SaveAsync();
    }

    public async Task ClearApiTokenAsync(TransportKind transportKind, CancellationToken cancellationToken = default)
    {
        if (credentialStore is null)
            throw new InvalidOperationException("No credential store is configured.");

        var key = transportKind switch
        {
            TransportKind.Local => LocalTokenKey,
            TransportKind.Cloud => throw new ArgumentException("Cloud transport uses username and password credentials.", nameof(transportKind)),
            _ => throw new ArgumentOutOfRangeException(nameof(transportKind))
        };

        await credentialStore.RemoveAsync(CredentialResource, key, cancellationToken);

        lock (syncRoot)
        {
            Current = transportKind switch
            {
                TransportKind.Local => Current with { HasLocalToken = false },
                _ => throw new ArgumentOutOfRangeException(nameof(transportKind))
            };
        }
        _ = SaveAsync();
    }

    public async Task<string?> GetApiTokenAsync(TransportKind transportKind, CancellationToken cancellationToken = default)
    {
        if (credentialStore is null)
            throw new InvalidOperationException("No credential store is configured.");

        var key = transportKind switch
        {
            TransportKind.Local => LocalTokenKey,
            TransportKind.Cloud => throw new ArgumentException("Cloud transport uses username and password credentials.", nameof(transportKind)),
            _ => throw new ArgumentOutOfRangeException(nameof(transportKind))
        };

        return await credentialStore.RetrieveAsync(CredentialResource, key, cancellationToken);
    }

    public async Task SetCloudCredentialsAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        if (credentialStore is null)
            throw new InvalidOperationException("No credential store is configured.");

        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("Cloud user name must not be blank.", nameof(userName));

        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Cloud password must not be blank.", nameof(password));

        var normalizedUserName = userName.Trim();
        await credentialStore.StoreAsync(CredentialResource, CloudUserNameKey, normalizedUserName, cancellationToken);
        await credentialStore.StoreAsync(CredentialResource, CloudPasswordKey, password, cancellationToken);

        lock (syncRoot)
        {
            Current = Current with
            {
                HasCloudCredentials = true,
                CloudUserName = normalizedUserName
            };
        }
        _ = SaveAsync();
    }

    public async Task ClearCloudCredentialsAsync(CancellationToken cancellationToken = default)
    {
        if (credentialStore is null)
            throw new InvalidOperationException("No credential store is configured.");

        await credentialStore.RemoveAsync(CredentialResource, CloudUserNameKey, cancellationToken);
        await credentialStore.RemoveAsync(CredentialResource, CloudPasswordKey, cancellationToken);

        lock (syncRoot)
        {
            Current = Current with
            {
                HasCloudCredentials = false,
                CloudUserName = null
            };
        }
        _ = SaveAsync();
    }

    public async Task<CloudCredentials?> GetCloudCredentialsAsync(CancellationToken cancellationToken = default)
    {
        if (credentialStore is null)
            throw new InvalidOperationException("No credential store is configured.");

        var userName = await credentialStore.RetrieveAsync(CredentialResource, CloudUserNameKey, cancellationToken);
        var password = await credentialStore.RetrieveAsync(CredentialResource, CloudPasswordKey, cancellationToken);

        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        return new CloudCredentials(userName, password);
    }

    private async Task SaveAsync()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(SettingsFilePath, json);
        }
        catch
        {
            // Best-effort persistence — swallow IO errors.
        }
    }

    private void TryLoad()
    {
        try
        {
            if (!File.Exists(SettingsFilePath)) return;
            var json = File.ReadAllText(SettingsFilePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json);
            if (loaded is not null)
            {
                var normalized = NormalizeLoadedSettings(loaded);
                // Preserve credential-backed auth flags; they are hydrated separately from the credential store.
                lock (syncRoot)
                {
                    Current = normalized with
                    {
                        HasLocalToken = Current.HasLocalToken,
                        HasCloudCredentials = Current.HasCloudCredentials,
                        CloudUserName = Current.CloudUserName
                    };
                }
            }
        }
        catch
        {
            // Corrupt or missing settings file — stick with defaults.
        }
    }

    private async Task TryLoadAsync()
    {
        try
        {
            if (!File.Exists(SettingsFilePath)) return;
            var json = await File.ReadAllTextAsync(SettingsFilePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json);
            if (loaded is not null)
            {
                var normalized = NormalizeLoadedSettings(loaded);
                // Preserve credential-backed auth flags; they are hydrated separately from the credential store.
                lock (syncRoot)
                {
                    Current = normalized with
                    {
                        HasLocalToken = Current.HasLocalToken,
                        HasCloudCredentials = Current.HasCloudCredentials,
                        CloudUserName = Current.CloudUserName
                    };
                }
            }
        }
        catch
        {
            // Corrupt or missing settings file — stick with defaults.
        }
    }

    private static bool IsHttpOrHttps(Uri endpoint)
    {
        return endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps;
    }

    private static AppSettings NormalizeLoadedSettings(AppSettings settings)
    {
        var width = settings.FlyoutWidth;
        if (width < MinFlyoutWidth || width > MaxFlyoutWidth)
        {
            width = AppSettings.Default.FlyoutWidth;
        }

        var interval = settings.NotificationPollIntervalSeconds;
        if (interval < MinNotificationPollIntervalSeconds || interval > MaxNotificationPollIntervalSeconds)
        {
            interval = AppSettings.Default.NotificationPollIntervalSeconds;
        }

        return settings with { FlyoutWidth = width, NotificationPollIntervalSeconds = interval };
    }
}
