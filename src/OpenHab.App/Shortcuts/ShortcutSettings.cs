using System.Collections.Immutable;

namespace OpenHab.App.Shortcuts;

public sealed record BuiltInShortcutSettings(
    bool Enabled,
    ShortcutBinding? Binding,
    RadialActivationMode RadialActivationMode = RadialActivationMode.Toggle);

public sealed record VoiceModeShortcutSettings(
    bool Enabled,
    bool RequireConfirmationBeforeSending = false);

public sealed record ShortcutSettings(
    BuiltInShortcutSettings CommandMenu,
    VoiceModeShortcutSettings VoiceMode,
    ImmutableArray<ShortcutAction> Actions)
{
    private static readonly ShortcutBinding LegacyWinOCommandMenuBinding = new([ShortcutModifier.Win], "O");

    public static ShortcutSettings Default { get; } = new(
        new BuiltInShortcutSettings(
            Enabled: true,
            Binding: new ShortcutBinding([ShortcutModifier.Win, ShortcutModifier.Shift], "I"),
            RadialActivationMode: RadialActivationMode.Toggle),
        new VoiceModeShortcutSettings(
            Enabled: false,
            RequireConfirmationBeforeSending: false),
        []);

    public ShortcutSettings Normalized()
    {
        var commandMenu = CommandMenu ?? Default.CommandMenu;
        var voiceMode = VoiceMode ?? Default.VoiceMode;

        var commandMenuBinding = ShortcutBindingFormatter.TryNormalize(commandMenu.Binding, out var normalizedCommandMenuBinding)
            ? normalizedCommandMenuBinding
            : Default.CommandMenu.Binding;
        if (IsSameBinding(commandMenuBinding, LegacyWinOCommandMenuBinding))
        {
            commandMenuBinding = Default.CommandMenu.Binding;
        }

        var commandMenuMode = Enum.IsDefined(commandMenu.RadialActivationMode)
            ? commandMenu.RadialActivationMode
            : RadialActivationMode.Toggle;

        var actions = Actions.IsDefault
            ? []
            : Actions
                .Where(static action => action is not null && !string.IsNullOrWhiteSpace(action.Id))
                .Select(static action => action with
                {
                    Id = action!.Id.Trim(),
                    Name = string.IsNullOrWhiteSpace(action.Name) ? "Unnamed action" : action.Name.Trim(),
                    IconId = string.IsNullOrWhiteSpace(action.IconId) ? "custom" : action.IconId.Trim(),
                    GlobalShortcut = ShortcutBindingFormatter.TryNormalize(action.GlobalShortcut, out var normalizedShortcut) ? normalizedShortcut : null,
                    TargetItem = action.TargetItem?.Trim() ?? string.Empty,
                    CommandType = Enum.IsDefined(action.CommandType) ? action.CommandType : ShortcutCommandType.SendCommand,
                    CommandValue = action.CommandType == ShortcutCommandType.Voice
                        ? null
                        : string.IsNullOrWhiteSpace(action.CommandValue) ? null : action.CommandValue.Trim()
                })
                .ToImmutableArray();

        if (voiceMode.Enabled)
        {
            actions = VoiceShortcutPolicy
                .EnsureProtectedDefaultVoiceAction(actions)
                .ToImmutableArray();
        }

        return new ShortcutSettings(
            commandMenu with
            {
                Binding = commandMenuBinding,
                RadialActivationMode = commandMenuMode
            },
            voiceMode,
            actions);
    }

    private static bool IsSameBinding(ShortcutBinding? first, ShortcutBinding? second)
    {
        return ShortcutBindingFormatter.TryNormalize(first, out var normalizedFirst)
            && ShortcutBindingFormatter.TryNormalize(second, out var normalizedSecond)
            && string.Equals(
                ShortcutBindingFormatter.Format(normalizedFirst),
                ShortcutBindingFormatter.Format(normalizedSecond),
                StringComparison.Ordinal);
    }
}
