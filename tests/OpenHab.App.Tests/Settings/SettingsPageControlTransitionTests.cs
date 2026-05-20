namespace OpenHab.App.Tests.Settings;

public sealed class SettingsPageControlTransitionTests
{
    [Fact]
    public void SettingsOptionNavigationKeepsHorizontalOverlapTransition()
    {
        var source = File.ReadAllText(Path.Combine(
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

        Assert.Contains("SitemapPageTransitionAnimator.AnimateOverlapAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsPageTransitionAnimator.AnimateEntranceAsync", source, StringComparison.Ordinal);
    }
}
