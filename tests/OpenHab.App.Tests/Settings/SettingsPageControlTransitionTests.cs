namespace OpenHab.App.Tests.Settings;

public sealed class SettingsPageControlTransitionTests
{
    private static string ReadSettingsPageControlSource()
    {
        return File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "OpenHab.Windows.Tray",
            "Settings",
            "SettingsPageControl.xaml.cs"));
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var startIndex = 0;
        while ((startIndex = source.IndexOf(value, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += value.Length;
        }

        return count;
    }

    [Fact]
    public void SettingsOptionNavigationKeepsHorizontalOverlapTransition()
    {
        var source = ReadSettingsPageControlSource();

        Assert.Contains("SitemapPageTransitionAnimator.AnimateOverlapAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsPageTransitionAnimator.AnimateEntranceAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandMenuSettingsExpanderStartsExpandedWithoutInitialExpansionAnimation()
    {
        var source = ReadSettingsPageControlSource();

        Assert.Contains("suppressInitialExpansionAnimation: true", source, StringComparison.Ordinal);
        Assert.Contains("isExpanded: true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("isExpanded: false));", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceInfoSyncSettingsExpandersStartExpandedWithoutInitialExpansionAnimation()
    {
        var source = ReadSettingsPageControlSource();

        Assert.True(
            CountOccurrences(source, "suppressInitialExpansionAnimation: true") >= 4,
            "Expected Command Menu plus all three Device Info Sync settings expanders to suppress initial expansion animation.");
    }
}
