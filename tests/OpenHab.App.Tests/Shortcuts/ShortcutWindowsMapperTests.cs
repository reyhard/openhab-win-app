using System.Collections.Immutable;
using OpenHab.App.Shortcuts;
using OpenHab.Windows.Tray.Shortcuts;
using Windows.System;

namespace OpenHab.App.Tests.Shortcuts;

public sealed class ShortcutWindowsMapperTests
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    [Fact]
    public void TryMap_ReturnsNativeModifierFlagsAndVirtualKey()
    {
        var binding = new ShortcutBinding(
            [ShortcutModifier.Win, ShortcutModifier.Ctrl, ShortcutModifier.Shift],
            "o");

        var mapped = ShortcutWindowsMapper.TryMap(binding, out var modifiers, out var virtualKey);

        Assert.True(mapped);
        Assert.Equal(ModWin | ModControl | ModShift, modifiers);
        Assert.Equal((uint)VirtualKey.O, virtualKey);
    }

    [Fact]
    public void TryMap_MapsAltModifierAlone()
    {
        var binding = new ShortcutBinding([ShortcutModifier.Alt], "X");

        var mapped = ShortcutWindowsMapper.TryMap(binding, out var modifiers, out var virtualKey);

        Assert.True(mapped);
        Assert.Equal(ModAlt, modifiers);
        Assert.Equal((uint)VirtualKey.X, virtualKey);
    }

    [Fact]
    public void TryMap_MapsCompleteModifierCombinationAfterNormalization()
    {
        var binding = new ShortcutBinding(
            [
                ShortcutModifier.Shift,
                ShortcutModifier.Alt,
                ShortcutModifier.Ctrl,
                ShortcutModifier.Win,
                ShortcutModifier.Alt,
                ShortcutModifier.Ctrl
            ],
            "K");

        var mapped = ShortcutWindowsMapper.TryMap(binding, out var modifiers, out var virtualKey);

        Assert.True(mapped);
        Assert.Equal(ModWin | ModControl | ModAlt | ModShift, modifiers);
        Assert.Equal((uint)VirtualKey.K, virtualKey);
    }

    [Fact]
    public void TryMap_RejectsNullBindingAndLeavesOutputsEmpty()
    {
        var mapped = ShortcutWindowsMapper.TryMap(null, out var modifiers, out var virtualKey);

        Assert.False(mapped);
        Assert.Equal(0u, modifiers);
        Assert.Equal(0u, virtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryMap_RejectsBlankKeyAndLeavesOutputsEmpty(string key)
    {
        var binding = new ShortcutBinding([ShortcutModifier.Ctrl], key);

        var mapped = ShortcutWindowsMapper.TryMap(binding, out var modifiers, out var virtualKey);

        Assert.False(mapped);
        Assert.Equal(0u, modifiers);
        Assert.Equal(0u, virtualKey);
    }

    [Fact]
    public void TryMap_RejectsBindingWithoutModifiersAndLeavesOutputsEmpty()
    {
        var binding = new ShortcutBinding(ImmutableArray<ShortcutModifier>.Empty, "O");

        var mapped = ShortcutWindowsMapper.TryMap(binding, out var modifiers, out var virtualKey);

        Assert.False(mapped);
        Assert.Equal(0u, modifiers);
        Assert.Equal(0u, virtualKey);
    }

    [Fact]
    public void TryMap_RejectsUnsupportedKeyWithoutKeyOutput()
    {
        // Modifier/key outputs are only meaningful when TryMap succeeds, so only the
        // key output is asserted to stay unset on this rejection path.
        var binding = new ShortcutBinding([ShortcutModifier.Ctrl], "NotAKey");

        var mapped = ShortcutWindowsMapper.TryMap(binding, out _, out var virtualKey);

        Assert.False(mapped);
        Assert.Equal(0u, virtualKey);
    }

    [Fact]
    public void TryMapVirtualKey_RejectsNullBinding()
    {
        var mapped = ShortcutWindowsMapper.TryMapVirtualKey(null, out var virtualKey);

        Assert.False(mapped);
        Assert.Equal(VirtualKey.None, virtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("None")]
    [InlineData("NotAKey")]
    [InlineData("-")]
    [InlineData("F0")]
    [InlineData("F25")]
    public void TryMapVirtualKey_RejectsInvalidKeys(string key)
    {
        var mapped = ShortcutWindowsMapper.TryMapVirtualKey(
            new ShortcutBinding([ShortcutModifier.Win], key),
            out var virtualKey);

        Assert.False(mapped);
        Assert.Equal(VirtualKey.None, virtualKey);
    }

    [Theory]
    [InlineData("A", VirtualKey.A)]
    [InlineData("7", VirtualKey.Number7)]
    [InlineData("F12", VirtualKey.F12)]
    [InlineData("Space", VirtualKey.Space)]
    [InlineData("Backspace", VirtualKey.Back)]
    [InlineData("PageDown", VirtualKey.PageDown)]
    [InlineData(" enter ", VirtualKey.Enter)]
    [InlineData("pageup", VirtualKey.PageUp)]
    [InlineData("left", VirtualKey.Left)]
    [InlineData("z", VirtualKey.Z)]
    [InlineData("0", VirtualKey.Number0)]
    [InlineData("9", VirtualKey.Number9)]
    [InlineData("F1", VirtualKey.F1)]
    [InlineData("f1", VirtualKey.F1)]
    [InlineData("F24", VirtualKey.F24)]
    public void TryMapVirtualKey_MapsSupportedKeys(string key, VirtualKey expected)
    {
        var mapped = ShortcutWindowsMapper.TryMapVirtualKey(
            new ShortcutBinding([ShortcutModifier.Win], key),
            out var virtualKey);

        Assert.True(mapped);
        Assert.Equal(expected, virtualKey);
    }

    [Theory]
    [InlineData(VirtualKey.A, "A")]
    [InlineData(VirtualKey.Number7, "7")]
    [InlineData(VirtualKey.F24, "F24")]
    [InlineData(VirtualKey.Space, "Space")]
    [InlineData(VirtualKey.Back, "Backspace")]
    [InlineData(VirtualKey.PageUp, "PageUp")]
    [InlineData(VirtualKey.PageDown, "PageDown")]
    [InlineData(VirtualKey.None, "")]
    public void FormatKey_ReturnsDisplayText(VirtualKey key, string expected)
    {
        Assert.Equal(expected, ShortcutWindowsMapper.FormatKey(key));
    }

    [Theory]
    [InlineData(VirtualKey.Delete, "Delete")]
    [InlineData(VirtualKey.Insert, "Insert")]
    [InlineData(VirtualKey.Up, "Up")]
    [InlineData(VirtualKey.Down, "Down")]
    [InlineData(VirtualKey.Left, "Left")]
    [InlineData(VirtualKey.Right, "Right")]
    [InlineData(VirtualKey.Home, "Home")]
    [InlineData(VirtualKey.End, "End")]
    [InlineData(VirtualKey.Enter, "Enter")]
    [InlineData(VirtualKey.Tab, "Tab")]
    [InlineData(VirtualKey.Escape, "")]
    public void FormatKey_ReturnsDisplayTextForNavigationAndEditingKeys(VirtualKey key, string expected)
    {
        Assert.Equal(expected, ShortcutWindowsMapper.FormatKey(key));
    }

    [Theory]
    [InlineData(VirtualKey.LeftWindows, true)]
    [InlineData(VirtualKey.RightWindows, true)]
    [InlineData(VirtualKey.Control, true)]
    [InlineData(VirtualKey.LeftControl, true)]
    [InlineData(VirtualKey.RightControl, true)]
    [InlineData(VirtualKey.Menu, true)]
    [InlineData(VirtualKey.LeftMenu, true)]
    [InlineData(VirtualKey.RightMenu, true)]
    [InlineData(VirtualKey.Shift, true)]
    [InlineData(VirtualKey.LeftShift, true)]
    [InlineData(VirtualKey.RightShift, true)]
    [InlineData(VirtualKey.O, false)]
    [InlineData(VirtualKey.F1, false)]
    public void IsModifierKey_IdentifiesModifierKeys(VirtualKey key, bool expected)
    {
        Assert.Equal(expected, ShortcutWindowsMapper.IsModifierKey(key));
    }
}
