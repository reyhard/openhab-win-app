using System.Collections.Immutable;
using OpenHab.App.Shortcuts;
using OpenHab.Windows.Tray.Shortcuts;
using Windows.System;

namespace OpenHab.App.Tests.Shortcuts;

public sealed class ShortcutWindowsMapperTests
{
    [Fact]
    public void TryMap_ReturnsNativeModifierFlagsAndVirtualKey()
    {
        var binding = new ShortcutBinding(
            [ShortcutModifier.Win, ShortcutModifier.Ctrl, ShortcutModifier.Shift],
            "o");

        var mapped = ShortcutWindowsMapper.TryMap(binding, out var modifiers, out var virtualKey);

        Assert.True(mapped);
        Assert.Equal(0x0008u | 0x0002u | 0x0004u, modifiers);
        Assert.Equal((uint)VirtualKey.O, virtualKey);
    }

    [Theory]
    [InlineData("A", VirtualKey.A)]
    [InlineData("7", VirtualKey.Number7)]
    [InlineData("F12", VirtualKey.F12)]
    [InlineData("Space", VirtualKey.Space)]
    [InlineData("Backspace", VirtualKey.Back)]
    [InlineData("PageDown", VirtualKey.PageDown)]
    public void TryMapVirtualKey_MapsSupportedKeys(string key, VirtualKey expected)
    {
        var mapped = ShortcutWindowsMapper.TryMapVirtualKey(
            new ShortcutBinding([ShortcutModifier.Win], key),
            out var virtualKey);

        Assert.True(mapped);
        Assert.Equal(expected, virtualKey);
    }

    [Fact]
    public void TryMap_RejectsBindingsWithoutModifiers()
    {
        var mapped = ShortcutWindowsMapper.TryMap(
            new ShortcutBinding(ImmutableArray<ShortcutModifier>.Empty, "O"),
            out _,
            out _);

        Assert.False(mapped);
    }

    [Theory]
    [InlineData(VirtualKey.A, "A")]
    [InlineData(VirtualKey.Number7, "7")]
    [InlineData(VirtualKey.F24, "F24")]
    [InlineData(VirtualKey.Space, "Space")]
    [InlineData(VirtualKey.Back, "Backspace")]
    [InlineData(VirtualKey.PageUp, "PageUp")]
    [InlineData(VirtualKey.None, "")]
    public void FormatKey_ReturnsDisplayText(VirtualKey key, string expected)
    {
        Assert.Equal(expected, ShortcutWindowsMapper.FormatKey(key));
    }

    [Theory]
    [InlineData(VirtualKey.LeftWindows, true)]
    [InlineData(VirtualKey.RightControl, true)]
    [InlineData(VirtualKey.LeftMenu, true)]
    [InlineData(VirtualKey.Shift, true)]
    [InlineData(VirtualKey.O, false)]
    public void IsModifierKey_IdentifiesModifierKeys(VirtualKey key, bool expected)
    {
        Assert.Equal(expected, ShortcutWindowsMapper.IsModifierKey(key));
    }
}
