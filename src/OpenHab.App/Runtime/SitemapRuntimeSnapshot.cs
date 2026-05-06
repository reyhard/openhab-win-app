using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.App.Runtime;

public sealed record SitemapRuntimeSnapshot(
    SitemapRenderDescriptor? Descriptor,
    TransportKind? ActiveTransport,
    ConnectionState ConnectionState,
    IReadOnlyList<string> Breadcrumbs,
    string StatusText,
    bool IsBusy,
    bool HasError)
{
    public static SitemapRuntimeSnapshot Initial { get; } = new(
        Descriptor: null,
        ActiveTransport: null,
        ConnectionState.Unknown,
        Breadcrumbs: [],
        StatusText: "Not connected.",
        IsBusy: false,
        HasError: false);
}
