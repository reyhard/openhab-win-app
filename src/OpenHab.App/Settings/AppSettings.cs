using System.Text.Json.Serialization;
using OpenHab.App.MainUi;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;
using System.Collections.Immutable;

namespace OpenHab.App.Settings;

public enum FlyoutAnimationSpeed
{
    Off = 0,     // Instant (no animation)
    Fast = 1,    // 150ms
    Default = 2, // 300ms
    Slow = 3,    // 450ms
}

public enum ChartQuality
{
    Normal = 96,
    High = 192
}

public sealed record AppSettings(
    SitemapSkinKind Skin,
    EndpointMode EndpointMode,
    Uri LocalEndpoint,
    Uri CloudEndpoint,
    string SitemapName,
    bool FollowSystemTheme = true,
    bool UseWindows11Icons = false,
    int FlyoutWidth = 460,
    FlyoutAnimationSpeed AnimationSpeed = FlyoutAnimationSpeed.Default,
    int NotificationPollIntervalSeconds = 30,
    ImmutableArray<string> ImportantNotificationTags = default,
    bool LaunchAtStartup = true,
    ChartQuality ChartQuality = ChartQuality.High,
    DeviceInfoSyncSettings? DeviceInfoSync = null,
    bool MainUiPagesExpanded = false,
    bool MainWindowSitemapPaneVisible = false,
    bool MainWindowSidebarCollapsed = false,
    ImmutableArray<MainUiPageLink> CachedMainUiPageLinks = default,
    [property: JsonIgnore] bool HasLocalToken = false,
    [property: JsonIgnore] bool HasCloudCredentials = false,
    [property: JsonIgnore] string? CloudUserName = null)
{
    public static AppSettings Default { get; } = new(
        SitemapSkinKind.Windows11,
        EndpointMode.Automatic,
        new Uri("http://openhab:8080"),
        new Uri("https://myopenhab.org"),
        string.Empty,
        AnimationSpeed: FlyoutAnimationSpeed.Default,
        NotificationPollIntervalSeconds: 30,
        ImportantNotificationTags: [],
        DeviceInfoSync: DeviceInfoSyncSettings.Default,
        CachedMainUiPageLinks: []);
}

