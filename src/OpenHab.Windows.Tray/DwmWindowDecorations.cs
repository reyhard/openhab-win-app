using System.Runtime.InteropServices;
using OpenHab.App.Settings;

namespace OpenHab.Windows.Tray;

internal static class DwmWindowDecorations
{
    internal const uint ColorNone = 0xFFFFFFFE;
    private const uint ColorBlack = 0x00000000;
    private const uint ColorWhite = 0x00FFFFFF;

    internal static FlyoutTheme ResolveFlyoutTheme(AppColorTheme appColorTheme, bool isSystemDark)
    {
        return appColorTheme switch
        {
            AppColorTheme.Dark => FlyoutTheme.Dark,
            AppColorTheme.Bright => FlyoutTheme.Light,
            AppColorTheme.FollowSystemSettings => isSystemDark ? FlyoutTheme.Dark : FlyoutTheme.Light,
            _ => FlyoutTheme.Dark
        };
    }

    internal static IReadOnlyList<DwmAttributeRequest> BuildRequests(bool isWindows11OrLater, FlyoutTheme theme = FlyoutTheme.Dark)
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

    internal static void TryApply(IntPtr hwnd, FlyoutTheme theme = FlyoutTheme.Dark)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            foreach (var request in BuildRequests(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000), theme))
            {
                if (request.UIntValue is uint uintValue)
                {
                    _ = DwmSetWindowAttribute(hwnd, request.Attribute, ref uintValue, sizeof(uint));
                    continue;
                }

                var intValue = request.IntValue.GetValueOrDefault();
                _ = DwmSetWindowAttribute(hwnd, request.Attribute, ref intValue, sizeof(int));
            }
        }
        catch (ArgumentException)
        {
            // Cosmetic DWM hints can fail during early composition or on unsupported hosts.
        }
        catch (InvalidOperationException)
        {
            // The HWND may be in a transient state while the WinUI window is activating.
        }
        catch (DllNotFoundException)
        {
            // DWM is not available in this environment.
        }
        catch (EntryPointNotFoundException)
        {
            // Older platform/runtime combinations may not expose the attribute API.
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        DwmWindowAttribute dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        DwmWindowAttribute dwAttribute,
        ref uint pvAttribute,
        int cbAttribute);
}

internal readonly record struct DwmAttributeRequest(
    DwmWindowAttribute Attribute,
    int? IntValue,
    uint? UIntValue)
{
    public static DwmAttributeRequest FromInt(DwmWindowAttribute attribute, int value) =>
        new(attribute, value, null);

    public static DwmAttributeRequest FromUInt(DwmWindowAttribute attribute, uint value) =>
        new(attribute, null, value);
}

internal enum DwmWindowAttribute
{
    UseImmersiveDarkMode = 20,
    WindowCornerPreference = 33,
    BorderColor = 34,
    SystemBackdropType = 38
}

internal enum DwmWindowCornerPreference
{
    Default = 0,
    DoNotRound = 1,
    Round = 2,
    RoundSmall = 3
}

internal enum DwmSystemBackdropType
{
    Auto = 0,
    None = 1,
    MainWindow = 2,
    TransientWindow = 3,
    TabbedWindow = 4
}

internal enum FlyoutTheme
{
    Light,
    Dark
}
