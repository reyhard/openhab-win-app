using OpenHab.Rendering.Descriptors;

namespace OpenHab.Windows.Tray.Rendering.SitemapSurface;

public sealed record SitemapVisualRow(
    int RowIndex,
    SitemapRowDescriptor Row,
    int NextDescriptorIndex);
