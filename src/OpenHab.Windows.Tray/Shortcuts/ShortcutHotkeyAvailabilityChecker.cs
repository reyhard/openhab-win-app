using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using OpenHab.App.Shortcuts;

namespace OpenHab.Windows.Tray.Shortcuts;

internal enum ShortcutHotkeyAvailabilityStatus
{
    Available,
    Invalid,
    Unmappable,
    Unavailable
}

internal static partial class ShortcutHotkeyAvailabilityChecker
{
    private const int ProbeHotkeyId = 0x5F00;

    [ExcludeFromCodeCoverage(Justification = "Win32 hotkey availability probe uses live OS registration.")]
    public static ShortcutHotkeyAvailabilityResult Check(ShortcutBinding? binding)
    {
        return Check(binding, TryRegisterProbeHotkey);
    }

    internal static ShortcutHotkeyAvailabilityResult Check(
        ShortcutBinding? binding,
        Func<uint, uint, bool> tryRegister)
    {
        ArgumentNullException.ThrowIfNull(tryRegister);

        if (!ShortcutBindingFormatter.TryNormalize(binding, out var normalized))
        {
            return ShortcutHotkeyAvailabilityResult.Invalid;
        }

        if (!ShortcutWindowsMapper.TryMap(normalized, out var modifiers, out var virtualKey))
        {
            return ShortcutHotkeyAvailabilityResult.Unmappable;
        }

        return tryRegister(modifiers, virtualKey)
            ? ShortcutHotkeyAvailabilityResult.Available
            : ShortcutHotkeyAvailabilityResult.Unavailable;
    }

    [ExcludeFromCodeCoverage(Justification = "Win32 hotkey availability probe uses live OS registration.")]
    private static bool TryRegisterProbeHotkey(uint modifiers, uint virtualKey)
    {
        if (!RegisterHotKey(IntPtr.Zero, ProbeHotkeyId, modifiers, virtualKey))
        {
            return false;
        }

        _ = UnregisterHotKey(IntPtr.Zero, ProbeHotkeyId);
        return true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

internal sealed record ShortcutHotkeyAvailabilityResult(ShortcutHotkeyAvailabilityStatus Status)
{
    public bool IsAvailable => Status == ShortcutHotkeyAvailabilityStatus.Available;

    public static ShortcutHotkeyAvailabilityResult Available { get; } = new(ShortcutHotkeyAvailabilityStatus.Available);
    public static ShortcutHotkeyAvailabilityResult Invalid { get; } = new(ShortcutHotkeyAvailabilityStatus.Invalid);
    public static ShortcutHotkeyAvailabilityResult Unmappable { get; } = new(ShortcutHotkeyAvailabilityStatus.Unmappable);
    public static ShortcutHotkeyAvailabilityResult Unavailable { get; } = new(ShortcutHotkeyAvailabilityStatus.Unavailable);
}
