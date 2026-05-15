using OpenHab.App.Settings;

namespace OpenHab.App.Tray;

public static class WindowDecorationPolicy
{
    public const uint ColorNone = 0xFFFFFFFE;
    private const uint ColorBlack = 0x00000000;
    private const uint ColorWhite = 0x00FFFFFF;

    public static FlyoutTheme ResolveFlyoutTheme(AppColorTheme appColorTheme, bool isSystemDark)
    {
        return appColorTheme switch
        {
            AppColorTheme.Dark => FlyoutTheme.Dark,
            AppColorTheme.Bright => FlyoutTheme.Light,
            AppColorTheme.FollowSystemSettings => isSystemDark ? FlyoutTheme.Dark : FlyoutTheme.Light,
            _ => FlyoutTheme.Dark
        };
    }

    public static IReadOnlyList<DwmAttributeRequest> BuildRequests(bool isWindows11OrLater, FlyoutTheme theme = FlyoutTheme.Dark)
    {
        var requests = new List<DwmAttributeRequest>();

        if (isWindows11OrLater)
        {
            requests.Add(DwmAttributeRequest.FromInt(
                DwmWindowAttribute.WindowCornerPreference,
                (int)DwmWindowCornerPreference.Round));
            requests.Add(DwmAttributeRequest.FromUInt(
                DwmWindowAttribute.BorderColor,
                theme == FlyoutTheme.Dark ? ColorBlack : ColorWhite));
        }

        requests.Add(DwmAttributeRequest.FromInt(
            DwmWindowAttribute.UseImmersiveDarkMode,
            theme == FlyoutTheme.Dark ? 1 : 0));

        if (isWindows11OrLater)
        {
            requests.Add(DwmAttributeRequest.FromInt(
                DwmWindowAttribute.SystemBackdropType,
                (int)DwmSystemBackdropType.MainWindow));
        }

        return requests;
    }
}

public readonly record struct DwmAttributeRequest(
    DwmWindowAttribute Attribute,
    int? IntValue,
    uint? UIntValue)
{
    public static DwmAttributeRequest FromInt(DwmWindowAttribute attribute, int value) =>
        new(attribute, value, null);

    public static DwmAttributeRequest FromUInt(DwmWindowAttribute attribute, uint value) =>
        new(attribute, null, value);
}

public enum DwmWindowAttribute
{
    UseImmersiveDarkMode = 20,
    WindowCornerPreference = 33,
    BorderColor = 34,
    SystemBackdropType = 38
}

public enum DwmWindowCornerPreference
{
    Default = 0,
    DoNotRound = 1,
    Round = 2,
    RoundSmall = 3
}

public enum DwmSystemBackdropType
{
    Auto = 0,
    None = 1,
    MainWindow = 2,
    TransientWindow = 3,
    TabbedWindow = 4
}

public enum FlyoutTheme
{
    Light,
    Dark
}
