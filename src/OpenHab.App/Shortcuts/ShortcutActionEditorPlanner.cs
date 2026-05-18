using System.Collections.Immutable;

namespace OpenHab.App.Shortcuts;

public sealed record ShortcutActionEditorDraft(
    string? Id,
    string Name,
    string IconId,
    bool ShowInCommandMenu,
    ShortcutBinding? GlobalShortcut,
    string TargetItem,
    ShortcutCommandType CommandType,
    string? CommandValue);

public sealed class ShortcutActionEditorPlanner
{
    private readonly Func<string> idFactory;

    public ShortcutActionEditorPlanner(Func<string>? idFactory = null)
    {
        this.idFactory = idFactory ?? (() => Guid.NewGuid().ToString("N"));
    }

    public static ShortcutActionEditorDraft CreateDraft(ShortcutAction? existing)
    {
        if (existing is null)
        {
            return new ShortcutActionEditorDraft(
                Id: null,
                Name: string.Empty,
                IconId: "custom",
                ShowInCommandMenu: true,
                GlobalShortcut: null,
                TargetItem: string.Empty,
                CommandType: ShortcutCommandType.Toggle,
                CommandValue: null);
        }

        return new ShortcutActionEditorDraft(
            existing.Id,
            existing.Name,
            existing.IconId,
            existing.ShowInCommandMenu,
            existing.GlobalShortcut,
            existing.TargetItem,
            existing.CommandType,
            existing.CommandValue);
    }

    public ShortcutAction BuildAction(ShortcutActionEditorDraft draft)
    {
        var rawId = string.IsNullOrWhiteSpace(draft.Id) ? idFactory() : draft.Id;
        var actionId = (rawId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(actionId))
        {
            actionId = Guid.NewGuid().ToString("N");
        }

        return new ShortcutAction(
            actionId,
            (draft.Name ?? string.Empty).Trim(),
            string.IsNullOrWhiteSpace(draft.IconId) ? "custom" : draft.IconId.Trim(),
            draft.ShowInCommandMenu,
            draft.GlobalShortcut,
            (draft.TargetItem ?? string.Empty).Trim(),
            draft.CommandType,
            string.IsNullOrWhiteSpace(draft.CommandValue) ? null : draft.CommandValue.Trim());
    }

    public static ImmutableArray<ShortcutAction> UpsertAction(IEnumerable<ShortcutAction> actions, ShortcutAction updated)
    {
        var list = actions.ToList();
        var index = list.FindIndex(action => string.Equals(action.Id, updated.Id, StringComparison.Ordinal));
        if (index >= 0)
        {
            list[index] = updated;
            return list.ToImmutableArray();
        }

        list.Add(updated);
        return list.ToImmutableArray();
    }

    public static ImmutableArray<ShortcutAction> RemoveAction(IEnumerable<ShortcutAction> actions, string actionId)
    {
        if (VoiceShortcutPolicy.IsProtectedDefaultVoiceAction(actionId))
        {
            return actions.ToImmutableArray();
        }

        return actions
            .Where(action => !string.Equals(action.Id, actionId, StringComparison.Ordinal))
            .ToImmutableArray();
    }

    public static ImmutableArray<ShortcutAction> MoveAction(IReadOnlyList<ShortcutAction> actions, string actionId, int offset)
    {
        if (actions.Count <= 1 || offset == 0)
        {
            return actions.ToImmutableArray();
        }

        var list = actions.ToList();
        var index = list.FindIndex(action => string.Equals(action.Id, actionId, StringComparison.Ordinal));
        if (index < 0)
        {
            return list.ToImmutableArray();
        }

        var targetIndex = Math.Clamp(index + offset, 0, list.Count - 1);
        if (targetIndex == index)
        {
            return list.ToImmutableArray();
        }

        var moving = list[index];
        list.RemoveAt(index);
        list.Insert(targetIndex, moving);
        return list.ToImmutableArray();
    }

    public static bool DraftEqualsAction(ShortcutActionEditorDraft current, ShortcutAction saved)
    {
        return string.Equals((current.Name ?? string.Empty).Trim(), saved.Name, StringComparison.Ordinal)
            && string.Equals((current.IconId ?? string.Empty).Trim(), saved.IconId, StringComparison.Ordinal)
            && current.ShowInCommandMenu == saved.ShowInCommandMenu
            && ShortcutBindingFormatter.Format(current.GlobalShortcut).Equals(ShortcutBindingFormatter.Format(saved.GlobalShortcut), StringComparison.Ordinal)
            && string.Equals((current.TargetItem ?? string.Empty).Trim(), saved.TargetItem, StringComparison.Ordinal)
            && current.CommandType == saved.CommandType
            && string.Equals((current.CommandValue ?? string.Empty).Trim(), saved.CommandValue ?? string.Empty, StringComparison.Ordinal);
    }
}
