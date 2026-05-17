using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using OpenHab.App.Settings;
using OpenHab.App.Tray;

namespace OpenHab.Windows.Tray;

[ExcludeFromCodeCoverage(Justification = "Win32/DWM composition wrapper.")]
internal static class DwmWindowDecorations
{
    internal static FlyoutTheme ResolveFlyoutTheme(AppColorTheme appColorTheme, bool isSystemDark)
    {
        return WindowDecorationPolicy.ResolveFlyoutTheme(appColorTheme, isSystemDark);
    }

    internal static IReadOnlyList<DwmAttributeRequest> BuildRequests(bool isWindows11OrLater, FlyoutTheme theme = FlyoutTheme.Dark)
    {
        return WindowDecorationPolicy.BuildRequests(isWindows11OrLater, theme);
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
