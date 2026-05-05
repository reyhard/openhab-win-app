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
    [property: JsonIgnore] bool HasLocalToken = false,
    [property: JsonIgnore] bool HasCloudToken = false)
{
    public static AppSettings Default { get; } = new(
        SitemapSkinKind.Windows11,
        EndpointMode.Automatic,
        new Uri("http://openhab.local:8080"),
        new Uri("https://myopenhab.org"),
        "default");
}
