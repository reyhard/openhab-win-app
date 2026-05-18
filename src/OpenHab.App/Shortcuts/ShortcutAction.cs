namespace OpenHab.App.Shortcuts;

public enum ShortcutCommandType
{
    Toggle,
    OnOff,
    OpenClose,
    OpenSlider,
    OpenColorPicker,
    SendCommand,
    Voice
}

public sealed record ShortcutAction(
    string Id,
    string Name,
    string IconId,
    bool ShowInCommandMenu,
    ShortcutBinding? GlobalShortcut,
    string TargetItem,
    ShortcutCommandType CommandType,
    string? CommandValue);
