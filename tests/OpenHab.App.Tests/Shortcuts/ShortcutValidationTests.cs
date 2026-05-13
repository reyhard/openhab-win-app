using OpenHab.App.Shortcuts;

namespace OpenHab.App.Tests.Shortcuts;

public sealed class ShortcutValidationTests
{
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
