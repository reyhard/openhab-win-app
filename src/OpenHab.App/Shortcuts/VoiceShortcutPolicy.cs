namespace OpenHab.App.Shortcuts;

public static class VoiceShortcutPolicy
{
    public const string ProtectedDefaultActionId = "built-in.voice.default";
    public const string DefaultActionName = "Voice command";
    public const string DefaultActionIconId = "microphone";
    public const string DefaultTargetItem = "VoiceCommand";

    public static ShortcutBinding DefaultVoiceShortcut { get; } =
        new([ShortcutModifier.Ctrl, ShortcutModifier.Alt], "I");

    public static ShortcutAction CreateDefaultVoiceAction(bool requireConfirmationBeforeSending = false)
    {
        _ = requireConfirmationBeforeSending;
        return new ShortcutAction(
            ProtectedDefaultActionId,
            DefaultActionName,
            DefaultActionIconId,
            ShowInCommandMenu: true,
            GlobalShortcut: DefaultVoiceShortcut,
            TargetItem: DefaultTargetItem,
            CommandType: ShortcutCommandType.Voice,
            CommandValue: null);
    }

    public static bool IsProtectedDefaultVoiceAction(ShortcutAction? action)
    {
        return action is not null && IsProtectedDefaultVoiceAction(action.Id);
    }

    public static bool IsProtectedDefaultVoiceAction(string? actionId)
    {
        return string.Equals(actionId, ProtectedDefaultActionId, StringComparison.Ordinal);
    }

    public static IReadOnlyList<ShortcutAction> EnsureProtectedDefaultVoiceAction(IEnumerable<ShortcutAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        var normalized = actions
            .Where(static action => action is not null)
            .ToList();

        if (normalized.Any(IsProtectedDefaultVoiceAction))
        {
            return normalized;
        }

        normalized.Insert(0, CreateDefaultVoiceAction());
        return normalized;
    }
}
