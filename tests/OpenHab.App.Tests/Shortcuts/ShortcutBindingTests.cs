using OpenHab.App.Shortcuts;

namespace OpenHab.App.Tests.Shortcuts;

public sealed class ShortcutBindingTests
{
    [Fact]
    public void FormatOrdersModifiersConsistently()
    {
        var binding = new ShortcutBinding(
            [ShortcutModifier.Shift, ShortcutModifier.Win, ShortcutModifier.Alt],
            "o");

        Assert.Equal("Win + Alt + Shift + O", ShortcutBindingFormatter.Format(binding));
    }

    [Fact]
    public void NormalizeUppercasesKeyAndOrdersModifiers()
    {
        var binding = new ShortcutBinding(
            [ShortcutModifier.Shift, ShortcutModifier.Win, ShortcutModifier.Win],
            "o");

        var normalized = ShortcutBindingFormatter.Normalize(binding);

        Assert.Equal([ShortcutModifier.Win, ShortcutModifier.Shift], normalized.Modifiers.ToArray());
        Assert.Equal("O", normalized.Key);
    }

    [Fact]
    public void FormatNullReturnsUnassigned()
    {
        Assert.Equal("Unassigned", ShortcutBindingFormatter.Format(null));
    }

    [Fact]
    public void FormatEmptyKeyReturnsUnassigned()
    {
        var binding = new ShortcutBinding([ShortcutModifier.Ctrl], "   ");

        Assert.Equal("Unassigned", ShortcutBindingFormatter.Format(binding));
    }

    [Fact]
    public void TryNormalizeReturnsFalseForEmptyKey()
    {
        var isValid = ShortcutBindingFormatter.TryNormalize(
            new ShortcutBinding([ShortcutModifier.Ctrl], string.Empty),
            out _);

        Assert.False(isValid);
    }
}
