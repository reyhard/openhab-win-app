using System.Collections.Immutable;
using OpenHab.App.Localization;

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
        IEnumerable<ShortcutBindingOwner>? existingBindings,
        bool allowUnassigned = false,
        ITextLocalizer? text = null)
    {
        text ??= DefaultEnglishTextLocalizer.Instance;
        if (binding is null)
        {
            return allowUnassigned
                ? ShortcutValidationResult.Valid
                : ShortcutValidationResult.Invalid([text.Get("Shortcuts.Validation.ShortcutRequired")]);
        }

        var errors = new List<string>();

        var hasModifier = binding.Modifiers.Any();
        if (!hasModifier)
        {
            errors.Add(text.Get("Shortcuts.Validation.ModifierRequired"));
        }

        var key = (binding.Key ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            errors.Add(text.Get("Shortcuts.Validation.KeyRequired"));
        }

        if (!hasModifier && IsBlockedSingleKey(key))
        {
            errors.Add(text.Format("Shortcuts.Validation.SingleKeyBlocked", key));
        }

        if (errors.Count > 0)
        {
            return ShortcutValidationResult.Invalid(errors);
        }

        if (!ShortcutBindingFormatter.TryNormalize(binding, out var normalized))
        {
            return ShortcutValidationResult.Invalid([text.Get("Shortcuts.Validation.KeyInvalid")]);
        }

        var normalizedBinding = ShortcutBindingFormatter.Format(normalized);
        if (ReservedBindings.Contains(normalizedBinding))
        {
            errors.Add(text.Format("Shortcuts.Validation.ReservedByWindows", normalizedBinding));
        }

        foreach (var existing in existingBindings ?? [])
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
                errors.Add(text.Format("Shortcuts.Validation.BindingAlreadyUsed", normalizedBinding, existing.OwnerName));
                break;
            }
        }

        return errors.Count == 0
            ? ShortcutValidationResult.Valid
            : ShortcutValidationResult.Invalid(errors);
    }

    public static ShortcutValidationResult ValidateAction(ShortcutAction? action, ITextLocalizer? text = null)
    {
        text ??= DefaultEnglishTextLocalizer.Instance;
        if (action is null)
        {
            return ShortcutValidationResult.Invalid([text.Get("Shortcuts.Validation.ActionRequired")]);
        }

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(action.Id))
        {
            errors.Add(text.Get("Shortcuts.Validation.ActionIdRequired"));
        }

        if (string.IsNullOrWhiteSpace(action.Name))
        {
            errors.Add(text.Get("Shortcuts.Validation.ActionNameRequired"));
        }

        if (string.IsNullOrWhiteSpace(action.TargetItem))
        {
            errors.Add(text.Get("Shortcuts.Validation.TargetItemRequired"));
        }

        if (!Enum.IsDefined(action.CommandType))
        {
            errors.Add(text.Get("Shortcuts.Validation.CommandTypeInvalid"));
        }

        switch (action.CommandType)
        {
            case ShortcutCommandType.OnOff:
                if (!IsOneOf(action.CommandValue, "ON", "OFF"))
                {
                    errors.Add(text.Get("Shortcuts.Validation.OnOffValueRequired"));
                }

                break;
            case ShortcutCommandType.OpenClose:
                if (!IsOneOf(action.CommandValue, "OPEN", "CLOSE"))
                {
                    errors.Add(text.Get("Shortcuts.Validation.OpenCloseValueRequired"));
                }

                break;
            case ShortcutCommandType.SendCommand:
                if (string.IsNullOrWhiteSpace(action.CommandValue))
                {
                    errors.Add(text.Get("Shortcuts.Validation.SendCommandValueRequired"));
                }

                break;
            case ShortcutCommandType.Voice:
                break;
        }

        if (action.GlobalShortcut is not null && !ShortcutBindingFormatter.TryNormalize(action.GlobalShortcut, out _))
        {
            errors.Add(text.Get("Shortcuts.Validation.GlobalShortcutInvalid"));
        }

        if (action.CommandType != ShortcutCommandType.Voice
            && !action.ShowInCommandMenu
            && action.GlobalShortcut is null)
        {
            errors.Add(text.Get("Shortcuts.Validation.ActionAvailabilityRequired"));
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
