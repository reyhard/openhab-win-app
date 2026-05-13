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
        var commandMenuBinding = CommandMenu.Binding is null
            ? Default.CommandMenu.Binding
            : ShortcutBindingFormatter.Normalize(CommandMenu.Binding);

        var commandMenuMode = Enum.IsDefined(CommandMenu.RadialActivationMode)
            ? CommandMenu.RadialActivationMode
            : RadialActivationMode.Toggle;

        var actions = Actions.IsDefault
            ? []
            : Actions
                .Where(static action => !string.IsNullOrWhiteSpace(action.Id))
                .Select(static action => action with
                {
                    Id = action.Id.Trim(),
                    Name = string.IsNullOrWhiteSpace(action.Name) ? "Unnamed action" : action.Name.Trim(),
                    IconId = string.IsNullOrWhiteSpace(action.IconId) ? "custom" : action.IconId.Trim(),
                    GlobalShortcut = action.GlobalShortcut is null ? null : ShortcutBindingFormatter.Normalize(action.GlobalShortcut),
                    TargetItem = action.TargetItem?.Trim() ?? string.Empty,
                    CommandValue = string.IsNullOrWhiteSpace(action.CommandValue) ? null : action.CommandValue.Trim()
                })
                .ToImmutableArray();

        return new ShortcutSettings(
            CommandMenu with
            {
                Binding = commandMenuBinding,
                RadialActivationMode = commandMenuMode
            },
            VoiceMode with
            {
                Enabled = false,
                Binding = null,
                RadialActivationMode = RadialActivationMode.Toggle
            },
            actions);
    }
}
