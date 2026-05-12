namespace OpenHab.App.MainUi;

public sealed record MainUiPageLink(
    string Uid,
    string Label,
    string Route,
    string? Icon,
    string? Type,
    int? Order);
