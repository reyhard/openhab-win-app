using OpenHab.App.Shortcuts;
using Windows.System;

namespace OpenHab.Windows.Tray.Shortcuts;

internal static class ShortcutWindowsMapper
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    public static bool TryMap(ShortcutBinding? binding, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        if (!ShortcutBindingFormatter.TryNormalize(binding, out var normalized))
        {
            return false;
        }

        foreach (var modifier in normalized.Modifiers)
        {
            modifiers |= modifier switch
            {
                ShortcutModifier.Win => ModWin,
                ShortcutModifier.Ctrl => ModControl,
                ShortcutModifier.Alt => ModAlt,
                ShortcutModifier.Shift => ModShift,
                _ => 0
            };
        }

        if (modifiers == 0)
        {
            return false;
        }

        if (!TryMapVirtualKey(normalized.Key, out virtualKey))
        {
            return false;
        }

        return true;
    }

    public static bool TryMapVirtualKey(ShortcutBinding? binding, out VirtualKey virtualKey)
    {
        virtualKey = VirtualKey.None;
        if (!ShortcutBindingFormatter.TryNormalize(binding, out var normalized)
            || !TryMapVirtualKey(normalized.Key, out var mappedVirtualKey))
        {
            return false;
        }

        virtualKey = (VirtualKey)mappedVirtualKey;
        return true;
    }

    public static bool IsModifierKey(VirtualKey key)
    {
        return key is VirtualKey.LeftWindows
            or VirtualKey.RightWindows
            or VirtualKey.Control
            or VirtualKey.LeftControl
            or VirtualKey.RightControl
            or VirtualKey.Menu
            or VirtualKey.LeftMenu
            or VirtualKey.RightMenu
            or VirtualKey.Shift
            or VirtualKey.LeftShift
            or VirtualKey.RightShift;
    }

    public static string FormatKey(VirtualKey key)
    {
        if (key >= VirtualKey.A && key <= VirtualKey.Z)
        {
            return key.ToString();
        }

        if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9)
        {
            return ((int)key - (int)VirtualKey.Number0).ToString();
        }

        if (key >= VirtualKey.F1 && key <= VirtualKey.F24)
        {
            return key.ToString();
        }

        return key switch
        {
            VirtualKey.Space => "Space",
            VirtualKey.Back => "Backspace",
            VirtualKey.Delete => "Delete",
            VirtualKey.Insert => "Insert",
            VirtualKey.Up => "Up",
            VirtualKey.Down => "Down",
            VirtualKey.Left => "Left",
            VirtualKey.Right => "Right",
            VirtualKey.PageUp => "PageUp",
            VirtualKey.PageDown => "PageDown",
            VirtualKey.Home => "Home",
            VirtualKey.End => "End",
            VirtualKey.Enter => "Enter",
            VirtualKey.Tab => "Tab",
            _ => string.Empty
        };
    }

    private static bool TryMapVirtualKey(string key, out uint virtualKey)
    {
        virtualKey = 0;
        var normalizedKey = (key ?? string.Empty).Trim();
        if (normalizedKey.Length == 0)
        {
            return false;
        }

        if (normalizedKey.Length == 1)
        {
            var character = normalizedKey[0];
            if (character >= 'A' && character <= 'Z')
            {
                virtualKey = (uint)(VirtualKey.A + (character - 'A'));
                return true;
            }

            if (character >= '0' && character <= '9')
            {
                virtualKey = (uint)(VirtualKey.Number0 + (character - '0'));
                return true;
            }
        }

        if (Enum.TryParse<VirtualKey>(normalizedKey, ignoreCase: true, out var parsedVirtualKey))
        {
            if (parsedVirtualKey == VirtualKey.None)
            {
                return false;
            }

            virtualKey = (uint)parsedVirtualKey;
            return true;
        }

        if (normalizedKey.StartsWith("F", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(normalizedKey[1..], out var functionNumber)
            && functionNumber is >= 1 and <= 24)
        {
            virtualKey = (uint)(VirtualKey.F1 + (functionNumber - 1));
            return true;
        }

        var mapped = normalizedKey.ToUpperInvariant() switch
        {
            "SPACE" => VirtualKey.Space,
            "BACKSPACE" => VirtualKey.Back,
            "DELETE" => VirtualKey.Delete,
            "INSERT" => VirtualKey.Insert,
            "UP" => VirtualKey.Up,
            "DOWN" => VirtualKey.Down,
            "LEFT" => VirtualKey.Left,
            "RIGHT" => VirtualKey.Right,
            "PAGEUP" => VirtualKey.PageUp,
            "PAGEDOWN" => VirtualKey.PageDown,
            "HOME" => VirtualKey.Home,
            "END" => VirtualKey.End,
            "ENTER" => VirtualKey.Enter,
            "TAB" => VirtualKey.Tab,
            _ => (VirtualKey)0
        };

        if (mapped == 0)
        {
            return false;
        }

        virtualKey = (uint)mapped;
        return true;
    }
}
