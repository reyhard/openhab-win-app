using System.Runtime.InteropServices;
using OpenHab.Windows.Tray.Localization;

namespace OpenHab.App.Tests.Localization;

public sealed class WinUiTextLocalizerTests
{
    [Fact]
    public void GetFallsBackToDefaultEnglishWhenWinUiResourceLookupThrows()
    {
        var localizer = new WinUiTextLocalizer(_ => throw new COMException("Resource loader unavailable."));

        var text = localizer.Get("Runtime.Error");

        Assert.Equal("Error.", text);
    }

    [Fact]
    public void FormatFallsBackToDefaultEnglishWhenWinUiResourceLookupThrows()
    {
        var localizer = new WinUiTextLocalizer(_ => throw new COMException("Resource loader unavailable."));

        var text = localizer.Format("Runtime.Connection.State", "Offline");

        Assert.Equal("Connection: Offline", text);
    }

    [Fact]
    public void GetUsesWinUiSlashResourceNameForDottedKeys()
    {
        var localizer = new WinUiTextLocalizer(key =>
            string.Equals(key, "Settings/Appearance/Title", StringComparison.Ordinal)
                ? "Wygląd"
                : null);

        var text = localizer.Get("Settings.Appearance.Title");

        Assert.Equal("Wygląd", text);
    }

    [Fact]
    public void GetTriesSlashResourceNameWhenDottedLookupThrows()
    {
        var localizer = new WinUiTextLocalizer(key =>
        {
            if (string.Equals(key, "Settings.Appearance.Title", StringComparison.Ordinal))
            {
                throw new COMException("Dotted resource name unavailable.");
            }

            return string.Equals(key, "Settings/Appearance/Title", StringComparison.Ordinal)
                ? "Wygląd"
                : null;
        });

        var text = localizer.Get("Settings.Appearance.Title");

        Assert.Equal("Wygląd", text);
    }
}
