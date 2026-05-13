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
        var modifiers = ModifierOrder
            .Where(modifier => binding.Modifiers.Contains(modifier))
            .ToImmutableArray();
        return new ShortcutBinding(modifiers, NormalizeKey(binding.Key));
    }

    public static string Format(ShortcutBinding? binding)
    {
        if (binding is null)
        {
            return "Unassigned";
        }

        var normalized = Normalize(binding);
        var parts = normalized.Modifiers.Select(static modifier => modifier.ToString()).Append(normalized.Key);
        return string.Join(" + ", parts);
    }

    private static string NormalizeKey(string key)
    {
        var trimmed = (key ?? string.Empty).Trim();
        return trimmed.Length == 1 ? trimmed.ToUpperInvariant() : trimmed;
    }
}
