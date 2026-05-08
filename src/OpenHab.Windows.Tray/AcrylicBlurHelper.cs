using System.Runtime.InteropServices;

namespace OpenHab.Windows.Tray;

internal static class AcrylicBlurHelper
{
    private enum AccentState
    {
        Disabled = 0,
        EnableAcrylicBlurBehind = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public uint AccentFlags;
        public uint GradientColor;
        public uint AnimationId;
    }

    private enum WindowCompositionAttribute
    {
        AccentPolicy = 19
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(
        IntPtr hwnd,
        ref WindowCompositionAttributeData data);

    public static void Apply(IntPtr hwnd, FlyoutTheme theme)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var opacity = theme == FlyoutTheme.Dark ? 0xB0u : 0xA8u;
        var bgrBackground = theme == FlyoutTheme.Dark ? 0x000000u : 0xFFFFFFu;
        var gradientColor = (opacity << 24) | (bgrBackground & 0xFFFFFFu);
        TrySetAccent(hwnd, AccentState.EnableAcrylicBlurBehind, gradientColor);
    }

    public static void Remove(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        TrySetAccent(hwnd, AccentState.Disabled, 0);
    }

    private static void TrySetAccent(IntPtr hwnd, AccentState accentState, uint gradientColor)
    {
        try
        {
            var accent = new AccentPolicy
            {
                AccentState = accentState,
                AccentFlags = 0,
                GradientColor = gradientColor,
                AnimationId = 0
            };

            var accentPtr = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>());
            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.AccentPolicy,
                    Data = accentPtr,
                    SizeOfData = Marshal.SizeOf<AccentPolicy>()
                };

                _ = SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }
        catch (DllNotFoundException)
        {
            // Best-effort visual enhancement.
        }
        catch (EntryPointNotFoundException)
        {
            // Best-effort visual enhancement.
        }
    }
}
