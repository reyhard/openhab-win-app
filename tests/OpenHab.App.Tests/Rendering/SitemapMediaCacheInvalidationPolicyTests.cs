using OpenHab.App.Settings;
using OpenHab.Core.Profiles;
using OpenHab.Windows.Tray.Rendering;

namespace OpenHab.App.Tests.Rendering;

public sealed class SitemapMediaCacheInvalidationPolicyTests
{
    [Fact]
    public void ShouldClear_ReturnsFalseForUnchangedProfile()
    {
        var settings = AppSettings.Default with
        {
            EndpointMode = EndpointMode.Automatic,
            LocalEndpoint = new Uri("http://openhab.local:8080/"),
            CloudEndpoint = new Uri("https://myopenhab.org/"),
            HasLocalToken = true,
            HasCloudCredentials = true,
            CloudUserName = "user"
        };
        var previous = SitemapMediaCacheProfile.FromSettings(settings);
        var current = SitemapMediaCacheProfile.FromSettings(settings);

        Assert.False(SitemapMediaCacheInvalidationPolicy.ShouldClear(previous, current));
    }

    [Fact]
    public void ShouldClear_ReturnsTrueWhenEndpointModeChanges()
    {
        var previous = SitemapMediaCacheProfile.FromSettings(AppSettings.Default with
        {
            EndpointMode = EndpointMode.Automatic
        });
        var current = SitemapMediaCacheProfile.FromSettings(AppSettings.Default with
        {
            EndpointMode = EndpointMode.CloudOnly
        });

        Assert.True(SitemapMediaCacheInvalidationPolicy.ShouldClear(previous, current));
    }

    [Fact]
    public void ShouldClear_ReturnsTrueWhenEndpointChanges()
    {
        var previous = SitemapMediaCacheProfile.FromSettings(AppSettings.Default with
        {
            LocalEndpoint = new Uri("http://openhab.local:8080/")
        });
        var current = SitemapMediaCacheProfile.FromSettings(AppSettings.Default with
        {
            LocalEndpoint = new Uri("http://openhab-new.local:8080/")
        });

        Assert.True(SitemapMediaCacheInvalidationPolicy.ShouldClear(previous, current));
    }

    [Fact]
    public void ShouldClear_ReturnsTrueWhenCredentialStateChanges()
    {
        var previous = SitemapMediaCacheProfile.FromSettings(AppSettings.Default with
        {
            HasLocalToken = false,
            HasCloudCredentials = false,
            CloudUserName = null
        });
        var current = SitemapMediaCacheProfile.FromSettings(AppSettings.Default with
        {
            HasLocalToken = true,
            HasCloudCredentials = true,
            CloudUserName = "user"
        });

        Assert.True(SitemapMediaCacheInvalidationPolicy.ShouldClear(previous, current));
    }

    [Fact]
    public void ShouldClear_ReturnsTrueWhenCredentialMaterialChanges()
    {
        var settings = AppSettings.Default with
        {
            HasLocalToken = true,
            HasCloudCredentials = true,
            CloudUserName = "user"
        };
        var previous = SitemapMediaCacheProfile.FromSettings(
            settings,
            localApiToken: "old-token",
            cloudCredentials: new CloudCredentials("user", "old-password"));
        var current = SitemapMediaCacheProfile.FromSettings(
            settings,
            localApiToken: "new-token",
            cloudCredentials: new CloudCredentials("user", "new-password"));

        Assert.True(SitemapMediaCacheInvalidationPolicy.ShouldClear(previous, current));
    }

    [Fact]
    public void ShouldClear_ReturnsFalseWhenUnrelatedSettingChanges()
    {
        var previous = SitemapMediaCacheProfile.FromSettings(AppSettings.Default with
        {
            UseWindows11Icons = false
        });
        var current = SitemapMediaCacheProfile.FromSettings(AppSettings.Default with
        {
            UseWindows11Icons = true
        });

        Assert.False(SitemapMediaCacheInvalidationPolicy.ShouldClear(previous, current));
    }
}
