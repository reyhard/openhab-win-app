using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace OpenHab.Windows.Tray.Shortcuts;

[ExcludeFromCodeCoverage(Justification = "Win32 message-only window for global hotkey callbacks.")]
internal sealed partial class HotkeyMessageWindow : IDisposable
{
    private const string WindowClassName = "OpenHabHotkeyMessageWindow";
    private const int HwndMessage = -3;
    private const uint WmClose = 0x0010;

    private static readonly object ClassRegistrationLock = new();
    private static bool windowClassRegistered;
    private static WndProcDelegate? sharedWndProc;

    private IntPtr hwnd;
    private bool disposed;

    public HotkeyMessageWindow()
    {
        RegisterWindowClass();

        hwnd = CreateWindowEx(
            0,
            WindowClassName,
            "openHAB Hotkey Message Window",
            0,
            0,
            0,
            0,
            0,
            new IntPtr(HwndMessage),
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create hotkey message window.");
        }
    }

    public IntPtr Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return hwnd;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (hwnd != IntPtr.Zero)
        {
            _ = DestroyWindow(hwnd);
            hwnd = IntPtr.Zero;
        }
    }

    private static void RegisterWindowClass()
    {
        lock (ClassRegistrationLock)
        {
            if (windowClassRegistered)
            {
                return;
            }

            sharedWndProc = StaticWndProc;
            var wc = new WndClassEx
            {
                cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(sharedWndProc),
                hInstance = GetModuleHandle(null),
                lpszClassName = WindowClassName
            };

            if (RegisterClassEx(ref wc) == 0)
            {
                throw new InvalidOperationException("Failed to register hotkey message window class.");
            }

            windowClassRegistered = true;
        }
    }

    private static IntPtr StaticWndProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmClose)
        {
            _ = DestroyWindow(windowHandle);
            return IntPtr.Zero;
        }

        return DefWindowProc(windowHandle, message, wParam, lParam);
    }

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx wndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        [MarshalAs(UnmanagedType.LPWStr)] string lpClassName,
        [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
