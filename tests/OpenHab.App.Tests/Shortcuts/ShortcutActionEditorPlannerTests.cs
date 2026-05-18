using System.Collections.Immutable;
using OpenHab.App.Shortcuts;

namespace OpenHab.App.Tests.Shortcuts;

public sealed class ShortcutActionEditorPlannerTests
{
    [Fact]
    public void CreateDraftForNewActionUsesSafeDefaults()
    {
        var draft = ShortcutActionEditorPlanner.CreateDraft(null);

        Assert.Null(draft.Id);
        Assert.Equal(string.Empty, draft.Name);
        Assert.Equal("custom", draft.IconId);
        Assert.True(draft.ShowInCommandMenu);
        Assert.Null(draft.GlobalShortcut);
        Assert.Equal(string.Empty, draft.TargetItem);
        Assert.Equal(ShortcutCommandType.Toggle, draft.CommandType);
        Assert.Null(draft.CommandValue);
    }

    [Fact]
    public void CreateDraftForExistingActionCopiesValues()
    {
        var existing = new ShortcutAction(
            "id-1",
            "Kitchen",
            "lightbulb",
            ShowInCommandMenu: false,
            GlobalShortcut: new ShortcutBinding([ShortcutModifier.Ctrl], "K"),
            TargetItem: "Kitchen_Light",
            CommandType: ShortcutCommandType.SendCommand,
            CommandValue: "ON");

        var draft = ShortcutActionEditorPlanner.CreateDraft(existing);

        Assert.Equal(existing.Id, draft.Id);
        Assert.Equal(existing.Name, draft.Name);
        Assert.Equal(existing.IconId, draft.IconId);
        Assert.Equal(existing.ShowInCommandMenu, draft.ShowInCommandMenu);
        Assert.Equal(existing.GlobalShortcut, draft.GlobalShortcut);
        Assert.Equal(existing.TargetItem, draft.TargetItem);
        Assert.Equal(existing.CommandType, draft.CommandType);
        Assert.Equal(existing.CommandValue, draft.CommandValue);
    }

    [Fact]
    public void BuildActionTrimsTextAndGeneratesIdForNewAction()
    {
        var planner = new ShortcutActionEditorPlanner(() => " generated-id ");
        var draft = new ShortcutActionEditorDraft(
            Id: null,
            Name: "  Lamp  ",
            IconId: "  lightbulb  ",
            ShowInCommandMenu: true,
            GlobalShortcut: null,
            TargetItem: "  Living_Light  ",
            CommandType: ShortcutCommandType.SendCommand,
            CommandValue: "  ON  ");

        var action = planner.BuildAction(draft);

        Assert.Equal("generated-id", action.Id);
        Assert.Equal("Lamp", action.Name);
        Assert.Equal("lightbulb", action.IconId);
        Assert.Equal("Living_Light", action.TargetItem);
        Assert.Equal("ON", action.CommandValue);
    }

    [Fact]
    public void UpsertActionReplacesExistingById()
    {
        var original = Action("a1", "Old");
        var updated = Action("a1", "New");

        var result = ShortcutActionEditorPlanner.UpsertAction([original, Action("a2", "Second")], updated);

        Assert.Collection(
            result,
            first => Assert.Equal("New", first.Name),
            second => Assert.Equal("a2", second.Id));
    }

    [Fact]
    public void UpsertActionAppendsWhenIdNotFound()
    {
        var appended = Action("a3", "Third");

        var result = ShortcutActionEditorPlanner.UpsertAction([Action("a1", "First"), Action("a2", "Second")], appended);

        Assert.Equal(3, result.Length);
        Assert.Equal("a3", result[2].Id);
    }

    [Fact]
    public void RemoveActionRemovesMatchingId()
    {
        var result = ShortcutActionEditorPlanner.RemoveAction([Action("a1", "First"), Action("a2", "Second")], "a1");

        Assert.Single(result);
        Assert.Equal("a2", result[0].Id);
    }

    [Fact]
    public void RemoveActionPreservesProtectedDefaultVoiceAction()
    {
        var protectedAction = VoiceShortcutPolicy.CreateDefaultVoiceAction();
        var otherAction = Action("a2", "Second");

        var result = ShortcutActionEditorPlanner.RemoveAction(
            [protectedAction, otherAction],
            VoiceShortcutPolicy.ProtectedDefaultActionId);

        Assert.Collection(
            result,
            first => Assert.Equal(protectedAction, first),
            second => Assert.Equal(otherAction, second));
    }

    [Fact]
    public void MoveActionClampsDestinationInsideList()
    {
        var actions = ImmutableArray.Create(Action("a1", "First"), Action("a2", "Second"), Action("a3", "Third"));

        var movedToStart = ShortcutActionEditorPlanner.MoveAction(actions, "a2", -10);
        Assert.Collection(
            movedToStart,
            first => Assert.Equal("a2", first.Id),
            second => Assert.Equal("a1", second.Id),
            third => Assert.Equal("a3", third.Id));

        var movedToEnd = ShortcutActionEditorPlanner.MoveAction(actions, "a2", 10);
        Assert.Collection(
            movedToEnd,
            first => Assert.Equal("a1", first.Id),
            second => Assert.Equal("a3", second.Id),
            third => Assert.Equal("a2", third.Id));
    }

    private static ShortcutAction Action(string id, string name)
    {
        return new ShortcutAction(id, name, "custom", true, null, "Item", ShortcutCommandType.Toggle, null);
    }
}
