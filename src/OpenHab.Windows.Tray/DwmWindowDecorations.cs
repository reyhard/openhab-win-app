using System.Runtime.InteropServices;

namespace OpenHab.Windows.Tray;

internal static class DwmWindowDecorations
{
    internal const uint ColorNone = 0xFFFFFFFE;

    internal static FlyoutTheme ResolveFlyoutTheme(bool followSystemTheme, bool isSystemDark)
    {
        if (!followSystemTheme)
        {
            return FlyoutTheme.Dark;
        }

        return isSystemDark ? FlyoutTheme.Dark : FlyoutTheme.Light;
    }

    internal static IReadOnlyList<DwmAttributeRequest> BuildRequests(bool isWindows11OrLater)
    {
        var requests = new List<DwmAttributeRequest>();

        if (isWindows11OrLater)
        {
            requests.Add(DwmAttributeRequest.FromInt(
                DwmWindowAttribute.WindowCornerPreference,
                (int)DwmWindowCornerPreference.RoundSmall));
            requests.Add(DwmAttributeRequest.FromUInt(
                DwmWindowAttribute.BorderColor,
                ColorNone));
        }

        requests.Add(DwmAttributeRequest.FromInt(
            DwmWindowAttribute.UseImmersiveDarkMode,
            1));

        if (isWindows11OrLater)
        {
            requests.Add(DwmAttributeRequest.FromInt(
                DwmWindowAttribute.SystemBackdropType,
                (int)DwmSystemBackdropType.TransientWindow));
        }

        return requests;
    }

    internal static void TryApply(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            foreach (var request in BuildRequests(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)))
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
