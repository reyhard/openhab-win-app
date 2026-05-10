namespace OpenHab.Sitemaps.Models;

public sealed record SitemapPage(string Id, string Label, IReadOnlyList<SitemapWidget> Widgets);

public sealed record SitemapWidget(
    string Label,
    SitemapWidgetType Type,
    string? ItemName,
    string? State,
    IReadOnlyList<SitemapMapping> Mappings,
    bool IsVisible,
    IReadOnlyList<SitemapPage> Children,
    string? Icon = null,
    string? LabelColor = null,
    string? ValueColor = null,
    string? IconColor = null,
    double? MinValue = null,
    double? MaxValue = null,
    double? Step = null,
    string WidgetId = "",
    string? RawItemState = null,
    int? Row = null,
    int? Column = null,
    string? Command = null,
    string? ReleaseCommand = null,
    bool? Stateless = null,
    string? Url = null,
    string? Period = null,
    string? Service = null,
    SitemapInputHint InputHint = SitemapInputHint.Auto);

public sealed record NormalizedSitemapPage(string Id, string Label, IReadOnlyList<NormalizedSitemapWidget> Widgets);

public sealed record NormalizedSitemapWidget(
    string Label,
    SitemapWidgetType Type,
    string? ItemName,
    string? State,
    IReadOnlyList<SitemapMapping> Mappings,
    bool CanNavigate,
    bool RequiresFallback,
    SitemapFallbackKind FallbackKind,
    IReadOnlyList<SitemapPage> Children,
    string? Icon = null,
    string? LabelColor = null,
    string? ValueColor = null,
    string? IconColor = null,
    double? MinValue = null,
    double? MaxValue = null,
    double? Step = null,
    string WidgetId = "",
    string? RawItemState = null,
    bool IsVisible = true,
    int? Row = null,
    int? Column = null,
    string? Command = null,
    string? ReleaseCommand = null,
    bool? Stateless = null,
    string? Url = null,
    string? Period = null,
    string? Service = null,
    SitemapInputHint InputHint = SitemapInputHint.Auto);

public sealed record SitemapMapping(string Command, string Label);

public enum SitemapWidgetType
{
    Default,
    Text,
    Group,
    Switch,
    Selection,
    Setpoint,
    Slider,
    Colorpicker,
    Colortemperaturepicker,
    Input,
    Buttongrid,
    Button,
    Webview,
    Mapview,
    Image,
    Frame,
    Video,
    Chart
}

public enum SitemapFallbackKind
{
    None,
    MainUiOrBrowser
}

public enum SitemapInputHint
{
    Auto,
    Text,
    Number,
    Date,
    Time,
    DateTime
}
