using System.Collections.Immutable;

namespace OpenHab.App.Shortcuts;

public enum ShortcutModifier
{
    Win,
    Ctrl,
    Alt,
    Shift
}

public enum RadialActivationMode
{
    Toggle,
    Hold
}

public sealed record ShortcutBinding(
    ImmutableArray<ShortcutModifier> Modifiers,
    string Key);

public static class ShortcutBindingFormatter
{
    private static readonly ShortcutModifier[] ModifierOrder =
    [
        ShortcutModifier.Win,
        ShortcutModifier.Ctrl,
        ShortcutModifier.Alt,
        ShortcutModifier.Shift
    ];

    public static ShortcutBinding Normalize(ShortcutBinding binding)
    {
        if (!TryNormalize(binding, out var normalized))
        {
            throw new ArgumentException("Shortcut key must not be empty.", nameof(binding));
        }

        return normalized;
    }

    public static bool TryNormalize(ShortcutBinding? binding, out ShortcutBinding normalized)
    {
        if (binding is null)
        {
            normalized = default!;
            return false;
        }

        var modifiers = ModifierOrder
            .Where(modifier => binding.Modifiers.Contains(modifier))
            .ToImmutableArray();
        var key = NormalizeKey(binding.Key);
        if (string.IsNullOrWhiteSpace(key))
        {
            normalized = default!;
            return false;
        }

        normalized = new ShortcutBinding(modifiers, key);
        return true;
    }

    public static string Format(ShortcutBinding? binding)
    {
        if (!TryNormalize(binding, out var normalized))
        {
            return "Unassigned";
        }

        var parts = normalized.Modifiers.Select(static modifier => modifier.ToString()).Append(normalized.Key);
        return string.Join(" + ", parts);
    }

    private static string NormalizeKey(string key)
    {
        var trimmed = (key ?? string.Empty).Trim();
        return trimmed.Length == 1 ? trimmed.ToUpperInvariant() : trimmed;
    }
}
