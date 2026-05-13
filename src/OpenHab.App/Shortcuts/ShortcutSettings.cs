using System.Collections.Immutable;

namespace OpenHab.App.Shortcuts;

public sealed record BuiltInShortcutSettings(
    bool Enabled,
    ShortcutBinding? Binding,
    RadialActivationMode RadialActivationMode = RadialActivationMode.Toggle);

public sealed record ShortcutSettings(
    BuiltInShortcutSettings CommandMenu,
    BuiltInShortcutSettings VoiceMode,
    ImmutableArray<ShortcutAction> Actions)
{
    public static ShortcutSettings Default { get; } = new(
        new BuiltInShortcutSettings(
            Enabled: true,
            Binding: new ShortcutBinding([ShortcutModifier.Win], "O"),
            RadialActivationMode: RadialActivationMode.Toggle),
        new BuiltInShortcutSettings(
            Enabled: false,
            Binding: null,
            RadialActivationMode: RadialActivationMode.Toggle),
        []);

    public ShortcutSettings Normalized()
    {
        var commandMenu = CommandMenu ?? Default.CommandMenu;
        var voiceMode = VoiceMode ?? Default.VoiceMode;

        var commandMenuBinding = ShortcutBindingFormatter.TryNormalize(commandMenu.Binding, out var normalizedCommandMenuBinding)
            ? normalizedCommandMenuBinding
            : Default.CommandMenu.Binding;

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
                    CommandValue = string.IsNullOrWhiteSpace(action.CommandValue) ? null : action.CommandValue.Trim()
                })
                .ToImmutableArray();

        return new ShortcutSettings(
            commandMenu with
            {
                Binding = commandMenuBinding,
                RadialActivationMode = commandMenuMode
            },
            // Voice Mode is intentionally locked for this release and must not register shortcuts yet.
            voiceMode with
            {
                Enabled = false,
                Binding = null,
                RadialActivationMode = RadialActivationMode.Toggle
            },
            actions);
    }
}
