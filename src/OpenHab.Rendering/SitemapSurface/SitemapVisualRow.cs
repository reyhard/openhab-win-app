using OpenHab.Rendering.Descriptors;

namespace OpenHab.Rendering.SitemapSurface;

public sealed record SitemapVisualRow(
    int RowIndex,
    SitemapRowDescriptor Row,
    int NextDescriptorIndex);
