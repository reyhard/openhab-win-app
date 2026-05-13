using OpenHab.Core;
using OpenHab.Core.Auth;
using OpenHab.Core.Profiles;
using OpenHab.App.MainUi;
using OpenHab.Rendering.Descriptors;
using System.Collections.Immutable;
using System.Collections.Generic;
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

    private static readonly string DefaultSettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenHab.WinApp",
        "settings.json");

    private static readonly object saveSyncRoot = new();
    private static readonly Dictionary<string, SaveQueue> queuedSaveTasks = new(StringComparer.OrdinalIgnoreCase);

    private readonly ICredentialStore? credentialStore;
    private readonly string settingsFilePath;

    private readonly object syncRoot = new();

    private string SettingsDirectory => Path.GetDirectoryName(settingsFilePath)!;

    public AppSettings Current { get; private set; } = AppSettings.Default;

    public event EventHandler? SettingsChanged;

    public AppSettingsController(ICredentialStore? credentialStore = null, string? settingsFilePath = null)
    {
        this.credentialStore = credentialStore;
        this.settingsFilePath = Path.GetFullPath(settingsFilePath ?? DefaultSettingsFilePath);
        WaitForQueuedSave();
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

    public Task FlushAsync()
    {
        var saveQueue = GetSaveQueue(settingsFilePath);
        lock (saveQueue.SyncRoot)
        {
            return saveQueue.QueuedSaveTask;
        }
    }

    public void SetSkin(SitemapSkinKind skin)
    {
        UpdateSettings(settings => settings with { Skin = skin });
    }

    public void SetEndpointMode(EndpointMode endpointMode)
    {
        UpdateSettings(settings => settings with { EndpointMode = endpointMode });
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

        if (!string.IsNullOrEmpty(localEndpoint.UserInfo))
        {
            throw new ArgumentException("Local endpoint must not contain user information.", nameof(localEndpoint));
        }

        if (!cloudEndpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("Cloud endpoint must be an absolute URI.", nameof(cloudEndpoint));
        }

        if (!IsHttpOrHttps(cloudEndpoint))
        {
            throw new ArgumentException("Cloud endpoint must use HTTP or HTTPS.", nameof(cloudEndpoint));
        }

        if (!string.IsNullOrEmpty(cloudEndpoint.UserInfo))
        {
            throw new ArgumentException("Cloud endpoint must not contain user information.", nameof(cloudEndpoint));
        }

        UpdateSettings(settings =>
            settings with
            {
                LocalEndpoint = localEndpoint,
                CloudEndpoint = cloudEndpoint
            });
    }

    public void SetSitemapName(string sitemapName)
    {
        if (string.IsNullOrWhiteSpace(sitemapName))
        {
            UpdateSettings(settings => settings with { SitemapName = string.Empty });
            return;
        }
        if (!SitemapNamePattern.IsMatch(sitemapName))
        {
            throw new ArgumentException("Sitemap name can only contain letters, digits, underscores, and hyphens.", nameof(sitemapName));
        }

        UpdateSettings(settings => settings with { SitemapName = sitemapName });
    }

    public void SetAppColorTheme(AppColorTheme theme)
    {
        if (!Enum.IsDefined(theme))
        {
            throw new ArgumentOutOfRangeException(nameof(theme));
        }

        UpdateSettings(settings => settings with { AppColorTheme = theme });
    }

    public void SetUseWindows11Icons(bool use)
    {
        UpdateSettings(settings => settings with { UseWindows11Icons = use });
    }

    public void SetChartQuality(ChartQuality quality)
    {
        UpdateSettings(settings => settings with { ChartQuality = quality });
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

    public void SetAnimationSpeed(FlyoutAnimationSpeed speed)
    {
        UpdateSettings(settings => settings with { AnimationSpeed = speed });
    }

    public void SetFlyoutWidth(int width)
    {
        if (width < MinFlyoutWidth || width > MaxFlyoutWidth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                $"Flyout width must be between {MinFlyoutWidth} and {MaxFlyoutWidth}.");
        }

        UpdateSettings(settings => settings with { FlyoutWidth = width });
    }

    public void SetLaunchAtStartup(bool launch)
    {
        UpdateSettings(settings => settings with { LaunchAtStartup = launch });
    }

    public void SetNotificationPollInterval(int seconds)
    {
        if (seconds < MinNotificationPollIntervalSeconds || seconds > MaxNotificationPollIntervalSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds),
                $"Notification poll interval must be between {MinNotificationPollIntervalSeconds} and {MaxNotificationPollIntervalSeconds} seconds.");
        }

        UpdateSettings(settings => settings with { NotificationPollIntervalSeconds = seconds });
    }

    public void SetImportantNotificationTags(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        UpdateSettings(settings => settings with
        {
            ImportantNotificationTags = NormalizeImportantNotificationTags(tags)
        });
    }

    public void SetDeviceInfoSyncSettings(DeviceInfoSyncSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.SyncIntervalMinutes < DeviceInfoSyncSettings.MinSyncIntervalMinutes
            || settings.SyncIntervalMinutes > DeviceInfoSyncSettings.MaxSyncIntervalMinutes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                $"Device info sync interval must be between {DeviceInfoSyncSettings.MinSyncIntervalMinutes} and {DeviceInfoSyncSettings.MaxSyncIntervalMinutes} minutes.");
        }

        UpdateSettings(appSettings => appSettings with { DeviceInfoSync = settings.Normalized() });
    }

    public void SetMainUiPagesExpanded(bool expanded)
    {
        UpdateSettings(settings => settings with { MainUiPagesExpanded = expanded });
    }

    public void SetMainWindowSitemapPaneVisible(bool visible)
    {
        UpdateSettings(settings => settings with { MainWindowSitemapPaneVisible = visible });
    }

    public void SetMainWindowSidebarCollapsed(bool collapsed)
    {
        UpdateSettings(settings => settings with { MainWindowSidebarCollapsed = collapsed });
    }

    public void SetCachedMainUiPageLinks(IEnumerable<MainUiPageLink> links)
    {
        ArgumentNullException.ThrowIfNull(links);
        UpdateSettings(settings => settings with
        {
            CachedMainUiPageLinks = NormalizeMainUiPageLinks(links)
        });
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

        UpdateSettings(settings => transportKind switch
            {
                TransportKind.Local => settings with { HasLocalToken = true },
                _ => throw new ArgumentOutOfRangeException(nameof(transportKind))
            });
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

        UpdateSettings(settings => transportKind switch
            {
                TransportKind.Local => settings with { HasLocalToken = false },
                _ => throw new ArgumentOutOfRangeException(nameof(transportKind))
            });
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

        UpdateSettings(settings =>
            settings with
            {
                HasCloudCredentials = true,
                CloudUserName = normalizedUserName
            });
    }

    public async Task ClearCloudCredentialsAsync(CancellationToken cancellationToken = default)
    {
        if (credentialStore is null)
            throw new InvalidOperationException("No credential store is configured.");

        await credentialStore.RemoveAsync(CredentialResource, CloudUserNameKey, cancellationToken);
        await credentialStore.RemoveAsync(CredentialResource, CloudPasswordKey, cancellationToken);

        UpdateSettings(settings =>
            settings with
            {
                HasCloudCredentials = false,
                CloudUserName = null
            });
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

    private void UpdateSettings(Func<AppSettings, AppSettings> update)
    {
        var saveQueue = GetSaveQueue(settingsFilePath);
        lock (saveQueue.SyncRoot)
        {
            lock (syncRoot)
            {
                Current = update(Current);
                QueueSave(saveQueue, Current);
            }
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void QueueSave(SaveQueue saveQueue, AppSettings snapshot)
    {
        saveQueue.QueuedSaveTask = saveQueue.QueuedSaveTask
            .ContinueWith(
                _ => SaveAsync(snapshot),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default)
            .Unwrap();
    }

    private async Task SaveAsync(AppSettings snapshot)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(settingsFilePath, json);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn($"Settings save failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static SaveQueue GetSaveQueue(string settingsFilePath)
    {
        lock (saveSyncRoot)
        {
            if (!queuedSaveTasks.TryGetValue(settingsFilePath, out var saveQueue))
            {
                saveQueue = new SaveQueue();
                queuedSaveTasks[settingsFilePath] = saveQueue;
            }

            return saveQueue;
        }
    }

    private void WaitForQueuedSave()
    {
        var saveQueue = GetSaveQueue(settingsFilePath);
        Task queuedSaveTask;
        lock (saveQueue.SyncRoot)
        {
            queuedSaveTask = saveQueue.QueuedSaveTask;
        }

        queuedSaveTask.GetAwaiter().GetResult();
    }

    private void TryLoad()
    {
        try
        {
            if (!File.Exists(settingsFilePath)) return;
            var json = File.ReadAllText(settingsFilePath);
            var legacyFollowSystemTheme = ReadLegacyFollowSystemTheme(json);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json);
            if (loaded is not null)
            {
                if (legacyFollowSystemTheme is bool legacyTheme)
                {
                    loaded = loaded with { FollowSystemTheme = legacyTheme };
                }

                var normalized = NormalizeLoadedSettings(loaded, legacyFollowSystemTheme);
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
            if (!File.Exists(settingsFilePath)) return;
            var json = await File.ReadAllTextAsync(settingsFilePath);
            var legacyFollowSystemTheme = ReadLegacyFollowSystemTheme(json);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json);
            if (loaded is not null)
            {
                if (legacyFollowSystemTheme is bool legacyTheme)
                {
                    loaded = loaded with { FollowSystemTheme = legacyTheme };
                }

                var normalized = NormalizeLoadedSettings(loaded, legacyFollowSystemTheme);
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

    private static AppSettings NormalizeLoadedSettings(AppSettings settings, bool? legacyFollowSystemTheme = null)
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

        var appColorTheme = settings.AppColorTheme;
        var legacyTheme = settings.FollowSystemTheme ?? legacyFollowSystemTheme;
        if (legacyTheme is bool followSystemTheme)
        {
            appColorTheme = followSystemTheme
                ? AppColorTheme.FollowSystemSettings
                : AppColorTheme.Dark;
        }
        else if (!Enum.IsDefined(appColorTheme))
        {
            appColorTheme = AppSettings.Default.AppColorTheme;
        }

        return settings with
        {
            AppColorTheme = appColorTheme,
            FollowSystemTheme = null,
            LocalEndpoint = NormalizeLoadedEndpoint(settings.LocalEndpoint, AppSettings.Default.LocalEndpoint),
            CloudEndpoint = NormalizeLoadedEndpoint(settings.CloudEndpoint, AppSettings.Default.CloudEndpoint),
            FlyoutWidth = width,
            NotificationPollIntervalSeconds = interval,
            ImportantNotificationTags = NormalizeImportantNotificationTags(settings.ImportantNotificationTags),
            DeviceInfoSync = settings.DeviceInfoSync?.Normalized() ?? DeviceInfoSyncSettings.Default,
            CachedMainUiPageLinks = NormalizeMainUiPageLinks(settings.CachedMainUiPageLinks)
        };
    }

    private static bool? ReadLegacyFollowSystemTheme(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty("FollowSystemTheme", out var legacyProperty))
            {
                return null;
            }

            return legacyProperty.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Uri NormalizeLoadedEndpoint(Uri? endpoint, Uri defaultEndpoint)
    {
        if (endpoint is null || !endpoint.IsAbsoluteUri || !IsHttpOrHttps(endpoint))
        {
            return defaultEndpoint;
        }

        if (string.IsNullOrEmpty(endpoint.UserInfo))
        {
            return endpoint;
        }

        var builder = new UriBuilder(endpoint)
        {
            UserName = string.Empty,
            Password = string.Empty
        };
        return builder.Uri;
    }

    private static ImmutableArray<MainUiPageLink> NormalizeMainUiPageLinks(IEnumerable<MainUiPageLink>? links)
    {
        if (links is null)
        {
            return [];
        }

        if (links is ImmutableArray<MainUiPageLink> immutableLinks && immutableLinks.IsDefault)
        {
            return [];
        }

        return links
            .Where(static link => !string.IsNullOrWhiteSpace(link.Uid))
            .Select(static link =>
            {
                var uid = link.Uid.Trim();
                var label = string.IsNullOrWhiteSpace(link.Label) ? uid : link.Label.Trim();
                var route = string.IsNullOrWhiteSpace(link.Route) ? "/page/" + Uri.EscapeDataString(uid) : link.Route.Trim();
                if (!route.StartsWith("/", StringComparison.Ordinal))
                {
                    route = "/" + route;
                }

                return link with
                {
                    Uid = uid,
                    Label = label,
                    Route = route
                };
            })
            .DistinctBy(static link => link.Uid, StringComparer.Ordinal)
            .OrderBy(static link => link.Order ?? int.MaxValue)
            .ThenBy(static link => link.Label, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(static link => link.Uid, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static ImmutableArray<string> NormalizeImportantNotificationTags(IEnumerable<string>? tags)
    {
        if (tags is null)
        {
            return [];
        }

        if (tags is ImmutableArray<string> immutableTags && immutableTags.IsDefault)
        {
            return [];
        }

        var normalized = tags
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
        return normalized;
    }

    private sealed class SaveQueue
    {
        public object SyncRoot { get; } = new();

        public Task QueuedSaveTask { get; set; } = Task.CompletedTask;
    }
}

