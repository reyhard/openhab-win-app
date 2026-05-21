namespace OpenHab.App.Tests.Tray;

public sealed class MainWindowCenterContentTransitionTests
{
    [Fact]
    public void CenterContentEntranceDefersStoryboardUntilAfterLayoutQueue()
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
            "MainWindow.xaml.cs"));

        Assert.Contains("PrepareCenterContentEntrance", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueue.TryEnqueue(() => StartCenterContentEntranceStoryboard", source, StringComparison.Ordinal);
    }
}
