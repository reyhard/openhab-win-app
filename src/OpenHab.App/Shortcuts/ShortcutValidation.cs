using System.Collections.Immutable;

namespace OpenHab.App.Shortcuts;

public sealed record ShortcutValidationResult(bool IsValid, ImmutableArray<string> Errors)
{
    public static ShortcutValidationResult Valid { get; } = new(true, []);

    public static ShortcutValidationResult Invalid(IEnumerable<string> errors)
    {
        var immutableErrors = errors
            .Where(static error => !string.IsNullOrWhiteSpace(error))
            .ToImmutableArray();
        return new ShortcutValidationResult(false, immutableErrors);
    }
}

public sealed record ShortcutBindingOwner(string OwnerName, ShortcutBinding Binding);

public static class ShortcutValidation
{
    private static readonly ImmutableHashSet<string> ReservedBindings = ["Win + V"];

    public static ShortcutValidationResult ValidateBinding(
        ShortcutBinding? binding,
        string ownerName,
        IEnumerable<ShortcutBindingOwner> existingBindings,
        bool allowUnassigned = false)
    {
        if (binding is null)
        {
            return allowUnassigned
                ? ShortcutValidationResult.Valid
                : ShortcutValidationResult.Invalid(["Shortcut is required."]);
        }

        var errors = new List<string>();

        var hasModifier = binding.Modifiers.Any();
        if (!hasModifier)
        {
            errors.Add("Shortcut must include at least one modifier.");
        }

        var key = (binding.Key ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            errors.Add("Shortcut must include a key.");
        }

        if (!hasModifier && IsBlockedSingleKey(key))
        {
            errors.Add($"'{key}' by itself cannot be used as a shortcut.");
        }

        if (errors.Count > 0)
        {
            return ShortcutValidationResult.Invalid(errors);
        }

        if (!ShortcutBindingFormatter.TryNormalize(binding, out var normalized))
        {
            return ShortcutValidationResult.Invalid(["Shortcut key is invalid."]);
        }

        var normalizedBinding = ShortcutBindingFormatter.Format(normalized);
        if (ReservedBindings.Contains(normalizedBinding))
        {
            errors.Add($"'{normalizedBinding}' is reserved by Windows and cannot be used.");
        }

        foreach (var existing in existingBindings)
        {
            if (string.Equals(existing.OwnerName, ownerName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!ShortcutBindingFormatter.TryNormalize(existing.Binding, out var normalizedExisting))
            {
                continue;
            }

            if (ShortcutBindingFormatter.Format(normalizedExisting).Equals(normalizedBinding, StringComparison.Ordinal))
            {
                errors.Add($"'{normalizedBinding}' is already used by '{existing.OwnerName}'.");
                break;
            }
        }

        return errors.Count == 0
            ? ShortcutValidationResult.Valid
            : ShortcutValidationResult.Invalid(errors);
    }

    public static ShortcutValidationResult ValidateAction(ShortcutAction action)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(action.Id))
        {
            errors.Add("Action id is required.");
        }

        if (string.IsNullOrWhiteSpace(action.Name))
        {
            errors.Add("Action name is required.");
        }

        if (string.IsNullOrWhiteSpace(action.TargetItem))
        {
            errors.Add("Target item is required.");
        }

        if (!Enum.IsDefined(action.CommandType))
        {
            errors.Add("Command type is invalid.");
        }

        switch (action.CommandType)
        {
            case ShortcutCommandType.OnOff:
                if (!IsOneOf(action.CommandValue, "ON", "OFF"))
                {
                    errors.Add("OnOff action requires command value ON or OFF.");
                }

                break;
            case ShortcutCommandType.OpenClose:
                if (!IsOneOf(action.CommandValue, "OPEN", "CLOSE"))
                {
                    errors.Add("OpenClose action requires command value OPEN or CLOSE.");
                }

                break;
            case ShortcutCommandType.SendCommand:
                if (string.IsNullOrWhiteSpace(action.CommandValue))
                {
                    errors.Add("SendCommand action requires a command value.");
                }

                break;
        }

        if (action.GlobalShortcut is not null && !ShortcutBindingFormatter.TryNormalize(action.GlobalShortcut, out _))
        {
            errors.Add("Global shortcut is invalid.");
        }

        if (!action.ShowInCommandMenu && action.GlobalShortcut is null)
        {
            errors.Add("Action must be shown in command menu, have a global shortcut, or both.");
        }

        return errors.Count == 0
            ? ShortcutValidationResult.Valid
            : ShortcutValidationResult.Invalid(errors);
    }

    private static bool IsBlockedSingleKey(string key) =>
        key.Equals("Escape", StringComparison.OrdinalIgnoreCase)
        || key.Equals("Enter", StringComparison.OrdinalIgnoreCase)
        || key.Equals("Tab", StringComparison.OrdinalIgnoreCase);

    private static bool IsOneOf(string? value, string first, string second)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Equals(first, StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(second, StringComparison.OrdinalIgnoreCase);
    }
}
