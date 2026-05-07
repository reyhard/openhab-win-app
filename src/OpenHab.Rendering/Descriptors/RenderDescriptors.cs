namespace OpenHab.Rendering.Descriptors;

public sealed record SitemapMapOption(string Command, string Label);

public sealed record SitemapRenderDescriptor(
    SitemapSkinKind Skin,
    string PageId,
    string Title,
    IReadOnlyList<SitemapRowDescriptor> Rows);

public sealed record SitemapRowDescriptor(
    string Label,
    string? State,
    RenderControlKind Control,
    RenderActionKind Action,
    RenderDensity Density,
    IReadOnlyList<SitemapMapOption> SelectionOptions,
    string? RawState = null,
    string? IconName = null,
    bool IsSectionHeader = false,
    double? MinValue = null,
    double? MaxValue = null,
    double? Step = null,
    string? RawItemState = null)
{
    public SitemapRowDescriptor(
        string Label,
        string? State,
        RenderControlKind Control,
        RenderActionKind Action,
        RenderDensity Density)
        : this(Label, State, Control, Action, Density, [], null, null, false, null, null, null, null)
    {
    }
}

public enum SitemapSkinKind
{
    Basic,
    Windows11
}

public enum RenderControlKind
{
    Text,
    Toggle,
    Slider,
    Selection,
    Fallback
}

public enum RenderActionKind
{
    None,
    SendCommand,
    Navigate,
    OpenFallback
}

public enum RenderDensity
{
    Compact,
    Comfortable
}
