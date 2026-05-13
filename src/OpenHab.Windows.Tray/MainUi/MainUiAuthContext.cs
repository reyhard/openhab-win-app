namespace OpenHab.Windows.Tray.MainUi;

public sealed record MainUiAuthContext(string? ApiToken, string? BasicUserName, string? BasicPassword)
{
    public static MainUiAuthContext None { get; } = new(null, null, null);
}
