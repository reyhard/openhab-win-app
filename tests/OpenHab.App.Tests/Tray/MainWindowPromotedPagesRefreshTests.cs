using System.IO;

namespace OpenHab.App.Tests.Tray;

public sealed class MainWindowPromotedPagesRefreshTests
{
    [Fact]
    public void CreateMainWindowSchedulesPromotedPageRefresh()
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
            "App.xaml.cs"));

        Assert.Contains("SchedulePromotedMainUiPageRefresh(window);", source, StringComparison.Ordinal);
        Assert.Contains("private void SchedulePromotedMainUiPageRefresh(MainWindow window)", source, StringComparison.Ordinal);
    }
}
