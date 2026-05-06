using System.Text.Json.Serialization;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.App.Settings;

public sealed record AppSettings(
    SitemapSkinKind Skin,
    EndpointMode EndpointMode,
    Uri LocalEndpoint,
    Uri CloudEndpoint,
    string SitemapName,
    bool FollowSystemTheme = true,
    bool UseWindows11Icons = false,
    [property: JsonIgnore] bool HasLocalToken = false,
    [property: JsonIgnore] bool HasCloudCredentials = false,
    [property: JsonIgnore] string? CloudUserName = null)
{
    public static AppSettings Default { get; } = new(
        SitemapSkinKind.Windows11,
        EndpointMode.Automatic,
        new Uri("http://openhab:8080"),
        new Uri("https://myopenhab.org"),
        "default");
}
