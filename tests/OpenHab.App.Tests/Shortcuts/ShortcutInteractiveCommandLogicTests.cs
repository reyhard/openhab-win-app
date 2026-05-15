using OpenHab.App.Shortcuts;
using OpenHab.Rendering;

namespace OpenHab.App.Tests.Shortcuts;

public sealed class ShortcutInteractiveCommandLogicTests
{
    [Theory]
    [InlineData("42", 42)]
    [InlineData("57.8 %", 58)]
    [InlineData("NULL", 0)]
    [InlineData("UNDEF", 0)]
    public void CreateSliderStateUsesNumericItemStateOrDefault(string rawState, double expected)
    {
        var state = ShortcutInteractiveCommandLogic.CreateSliderState(rawState);

        Assert.Equal(0, state.Minimum);
        Assert.Equal(100, state.Maximum);
        Assert.Equal(expected, state.InitialValue);
    }

    [Theory]
    [InlineData(42.4, "42")]
    [InlineData(42.5, "42")]
    [InlineData(99.9, "100")]
    public void FormatSliderCommandRoundsToOpenHabPercentCommand(double value, string expected)
    {
        var command = ShortcutInteractiveCommandLogic.FormatSliderCommand(value);

        Assert.Equal(expected, command);
    }

    [Fact]
    public void CreateColorStateUsesExistingOpenHabColorState()
    {
        var state = ShortcutInteractiveCommandLogic.CreateColorState("120,100,50");

        Assert.Equal("#008000", state.Hex);
        Assert.Equal("120,100,50", state.Command);
    }

    [Fact]
    public void FormatColorCommandConvertsColorToOpenHabHsbCommand()
    {
        var command = ShortcutInteractiveCommandLogic.FormatColorCommand(new SitemapColor(255, 255, 0, 0));

        Assert.Equal("0,100,100", command);
    }
}
