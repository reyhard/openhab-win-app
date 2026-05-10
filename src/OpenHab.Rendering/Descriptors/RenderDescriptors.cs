using OpenHab.Sitemaps.Models;

namespace OpenHab.Rendering.Descriptors;

public sealed record SitemapMapOption(string Command, string Label, int? Row = null, int? Column = null, bool IsActive = false);

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
    string? LabelColor = null,
    string? ValueColor = null,
    string? IconColor = null,
    bool IsSectionHeader = false,
    double? MinValue = null,
    double? MaxValue = null,
    double? Step = null,
    string? RawItemState = null,
    bool IsVisible = true,
    int? GridRow = null,
    int? GridColumn = null,
    string? Command = null,
    string? ReleaseCommand = null,
    bool? Stateless = null,
    bool SliderUpdateOnMove = true,
    string? Url = null,
    string? Period = null,
    string? ItemName = null,
    SitemapInputHint InputHint = SitemapInputHint.Auto)
{
    public SitemapRowDescriptor(
        string Label,
        string? State,
        RenderControlKind Control,
        RenderActionKind Action,
        RenderDensity Density)
        : this(Label, State, Control, Action, Density, [], null, null, null, null, null, false, null, null, null, null, true, null, null, null, null, null, true, null, null, null, SitemapInputHint.Auto)
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
    Button,
    ButtonGrid,
    Image,
    Webview,
    Chart,
    Input,
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
