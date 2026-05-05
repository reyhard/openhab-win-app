using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.App.Runtime;

public sealed record SitemapRuntimeSnapshot(
    SitemapRenderDescriptor? Descriptor,
    TransportKind? ActiveTransport,
    ConnectionState ConnectionState,
    string StatusText,
    bool IsBusy,
    bool HasError)
{
    public static SitemapRuntimeSnapshot Initial { get; } = new(
        Descriptor: null,
        ActiveTransport: null,
        ConnectionState.Unknown,
        StatusText: "Not connected.",
        IsBusy: false,
        HasError: false);
}
