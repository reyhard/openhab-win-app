using OpenHab.App.Shortcuts;

namespace OpenHab.App.Tests.Shortcuts;

public sealed class ShortcutValidationTests
{
    [Fact]
    public void NullBindingIsAcceptedWhenAllowUnassignedIsTrue()
    {
        var result = ShortcutValidation.ValidateBinding(null, "Command Menu", [], allowUnassigned: true);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void NullBindingIsRejectedWhenAllowUnassignedIsFalse()
    {
        var result = ShortcutValidation.ValidateBinding(null, "Command Menu", [], allowUnassigned: false);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CommandMenuBindingRequiresModifierAndKey()
    {
        var result = ShortcutValidation.ValidateBinding(new ShortcutBinding([], "O"), "Command Menu", []);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("modifier", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DuplicateShortcutReportsOwner()
    {
        var owner = new ShortcutBindingOwner("Movie Night", new ShortcutBinding([ShortcutModifier.Win], "M"));

        var result = ShortcutValidation.ValidateBinding(
            new ShortcutBinding([ShortcutModifier.Win], "M"),
            "Kitchen",
            [owner]);

        Assert.False(result.IsValid);
        Assert.Contains("Movie Night", Assert.Single(result.Errors));
    }

    [Fact]
    public void VoiceTypingShortcutIsReserved()
    {
        var result = ShortcutValidation.ValidateBinding(
            new ShortcutBinding([ShortcutModifier.Win], "V"),
            "Voice Mode",
            []);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("reserved", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Escape")]
    [InlineData("Enter")]
    [InlineData("Tab")]
    public void SingleKeyBindingsForEscapeEnterAndTabAreExplicitlyRejected(string key)
    {
        var result = ShortcutValidation.ValidateBinding(
            new ShortcutBinding([], key),
            "Command Menu",
            []);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("cannot be used", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespaceKeyIsRejectedWithoutThrowing(string key)
    {
        var exception = Record.Exception(() =>
            ShortcutValidation.ValidateBinding(new ShortcutBinding([ShortcutModifier.Ctrl], key), "Command Menu", []));

        Assert.Null(exception);

        var result = ShortcutValidation.ValidateBinding(
            new ShortcutBinding([ShortcutModifier.Ctrl], key),
            "Command Menu",
            []);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("key", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NullExistingBindingsDoesNotThrow()
    {
        var exception = Record.Exception(() =>
            ShortcutValidation.ValidateBinding(new ShortcutBinding([ShortcutModifier.Win], "O"), "Command Menu", null));

        Assert.Null(exception);

        var result = ShortcutValidation.ValidateBinding(
            new ShortcutBinding([ShortcutModifier.Win], "O"),
            "Command Menu",
            null);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(ShortcutCommandType.OnOff, "ON")]
    [InlineData(ShortcutCommandType.OnOff, "OFF")]
    [InlineData(ShortcutCommandType.OpenClose, "OPEN")]
    [InlineData(ShortcutCommandType.OpenClose, "CLOSE")]
    [InlineData(ShortcutCommandType.SendCommand, "PLAY")]
    public void ValidActionsPass(ShortcutCommandType type, string value)
    {
        var action = new ShortcutAction("a1", "Media", "play", true, null, "LivingRoom_Speaker", type, value);

        var result = ShortcutValidation.ValidateAction(action);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void NullActionReturnsInvalid()
    {
        var result = ShortcutValidation.ValidateAction(null);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MissingRequiredActionFieldsAreInvalid()
    {
        var action = new ShortcutAction("", "", "play", true, null, "", ShortcutCommandType.Toggle, null);

        var result = ShortcutValidation.ValidateAction(action);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("ID", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("name", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("target item", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InvalidCommandEnumIsInvalid()
    {
        var action = new ShortcutAction(
            "a1",
            "Bad Enum",
            "custom",
            true,
            null,
            "LivingRoom_Speaker",
            (ShortcutCommandType)999,
            "PLAY");

        var result = ShortcutValidation.ValidateAction(action);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("command type", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ActionWithoutCommandMenuOrShortcutIsInvalid()
    {
        var action = new ShortcutAction(
            "a1",
            "No Route",
            "custom",
            false,
            null,
            "Kitchen_Light",
            ShortcutCommandType.Toggle,
            null);

        var result = ShortcutValidation.ValidateAction(action);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("command menu", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VoiceActionDoesNotRequireCommandValue()
    {
        var action = new ShortcutAction(
            "voice-1",
            "Voice",
            "microphone",
            true,
            null,
            "VoiceCommand",
            ShortcutCommandType.Voice,
            null);

        var result = ShortcutValidation.ValidateAction(action);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void VoiceActionMayBeHiddenAndUnassigned()
    {
        var action = new ShortcutAction(
            "voice-2",
            "Voice",
            "microphone",
            false,
            null,
            "VoiceCommand",
            ShortcutCommandType.Voice,
            null);

        var result = ShortcutValidation.ValidateAction(action);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void VoiceActionStillRequiresTargetItem()
    {
        var action = new ShortcutAction(
            "voice-3",
            "Voice",
            "microphone",
            true,
            null,
            "   ",
            ShortcutCommandType.Voice,
            null);

        var result = ShortcutValidation.ValidateAction(action);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("target item", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(ShortcutCommandType.OnOff, "MAYBE")]
    [InlineData(ShortcutCommandType.OpenClose, "HALF")]
    [InlineData(ShortcutCommandType.SendCommand, "")]
    [InlineData(ShortcutCommandType.SendCommand, "   ")]
    public void InvalidCommandValuesAreRejected(ShortcutCommandType type, string value)
    {
        var action = new ShortcutAction(
            "a1",
            "Invalid Value",
            "custom",
            true,
            null,
            "Kitchen_Light",
            type,
            value);

        var result = ShortcutValidation.ValidateAction(action);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void IconCatalogIncludesMediaIcons()
    {
        var ids = ShortcutIconCatalog.All.Select(icon => icon.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("play", ids);
        Assert.Contains("pause", ids);
        Assert.Contains("stop", ids);
        Assert.Contains("cast", ids);
    }

    [Fact]
    public void IconCatalogUsesStableGroupIds()
    {
        var byId = ShortcutIconCatalog.All.ToDictionary(icon => icon.Id, StringComparer.Ordinal);
        var groups = ShortcutIconCatalog.All.Select(icon => icon.Group).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("lighting", groups);
        Assert.Contains("openings", groups);
        Assert.Contains("climate", groups);
        Assert.Contains("media", groups);
        Assert.Contains("scenes-tools", groups);

        Assert.Equal("media", byId["play"].Group);
        Assert.Equal("scenes-tools", byId["movie"].Group);
    }
}
