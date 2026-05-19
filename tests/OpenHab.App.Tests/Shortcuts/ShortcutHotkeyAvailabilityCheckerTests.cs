using OpenHab.App.Shortcuts;
using OpenHab.Windows.Tray.Shortcuts;

namespace OpenHab.App.Tests.Shortcuts;

public sealed class ShortcutHotkeyAvailabilityCheckerTests
{
    [Fact]
    public void Check_ReturnsAvailableWhenProbeAcceptsMappedBinding()
    {
        uint capturedModifiers = 0;
        uint capturedKey = 0;

        var result = ShortcutHotkeyAvailabilityChecker.Check(
            new ShortcutBinding([ShortcutModifier.Win, ShortcutModifier.Shift], "I"),
            (modifiers, virtualKey) =>
            {
                capturedModifiers = modifiers;
                capturedKey = virtualKey;
                return true;
            });

        Assert.True(result.IsAvailable);
        Assert.Equal(ShortcutHotkeyAvailabilityStatus.Available, result.Status);
        Assert.Equal(0x0008u | 0x0004u, capturedModifiers);
        Assert.Equal((uint)global::Windows.System.VirtualKey.I, capturedKey);
    }

    [Fact]
    public void Check_ReturnsUnavailableWhenProbeRejectsMappedBinding()
    {
        var result = ShortcutHotkeyAvailabilityChecker.Check(
            new ShortcutBinding([ShortcutModifier.Win], "O"),
            (_, _) => false);

        Assert.False(result.IsAvailable);
        Assert.Equal(ShortcutHotkeyAvailabilityStatus.Unavailable, result.Status);
    }

    [Fact]
    public void Check_ReturnsInvalidForBlankBinding()
    {
        var result = ShortcutHotkeyAvailabilityChecker.Check(null, (_, _) => true);

        Assert.False(result.IsAvailable);
        Assert.Equal(ShortcutHotkeyAvailabilityStatus.Invalid, result.Status);
    }
}
