namespace OpenHab.App.Tests.Tray;

public sealed class FlyoutWindowTransitionTests
{
    [Fact]
    public void InactiveSlotPageTransitionsSuppressRowTransitionsUntilSlideCompletes()
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
            "FlyoutWindow.xaml.cs"));

        Assert.DoesNotContain("SuppressRowsTransitionsDuring", source, StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(source, "using (SuppressRowsTransitions())"));
        Assert.Equal(3, CountOccurrences(source, "RefreshRuntimeBindings(InactiveRows, animateStructuralInsertions: false);"));
        Assert.Equal(3, CountOccurrences(source, "await AnimatePageTransitionOverlapAsync(ToWinUiNavigationDirection(transitionPlan.Direction));"));
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
