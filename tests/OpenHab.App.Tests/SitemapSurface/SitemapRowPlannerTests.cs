using OpenHab.Rendering;
using OpenHab.Rendering.Descriptors;
using OpenHab.Rendering.SitemapSurface;

namespace OpenHab.App.Tests.SitemapSurface;

public sealed class SitemapRowPlannerTests
{
    [Fact]
    public void VisualRowsSkipButtonChildrenAndMergeVisibleButtonGridOptions()
    {
        var rows = new[]
        {
            Grid("Mode"),
            Button("Auto", "AUTO", visible: true),
            Button("Manual", "MANUAL", visible: false),
            Text("Temperature")
        };

        var visualRows = SitemapRowPlanner.BuildVisualRows(rows);

        Assert.Equal([0, 3], visualRows.Select(row => row.RowIndex).ToArray());
        Assert.Single(visualRows[0].Row.SelectionOptions);
        Assert.Equal("Auto", visualRows[0].Row.SelectionOptions[0].Label);
        Assert.Equal(1, visualRows[0].Row.SelectionOptions[0].SourceRowIndex);
    }

    [Fact]
    public void VisualRowsPreserveHiddenButtonGridChildrenWhenNoVisibleChildExists()
    {
        var rows = new[]
        {
            Grid("Mode"),
            Button("Auto", "AUTO", visible: false),
            Button("Manual", "MANUAL", visible: false)
        };

        var visualRows = SitemapRowPlanner.BuildVisualRows(rows);

        Assert.Single(visualRows);
        Assert.Equal(0, visualRows[0].RowIndex);
        Assert.Equal(2, visualRows[0].Row.SelectionOptions.Count);
        Assert.Equal(["Auto", "Manual"], visualRows[0].Row.SelectionOptions.Select(option => option.Label).ToArray());
        Assert.All(visualRows[0].Row.SelectionOptions, option =>
        {
            var sourceRowIndex = option.SourceRowIndex;
            Assert.True(sourceRowIndex.HasValue);
            Assert.False(rows[sourceRowIndex.Value].IsVisible);
        });
    }

    [Fact]
    public void ExpandChangedIndices_IgnoresOutOfRangeValues_AndReturnsSortedUniqueMappedIndices()
    {
        var rows = new[]
        {
            Grid("Mode"),
            Button("Auto", "AUTO", true),
            Button("Manual", "MANUAL", true),
            Text("Temperature"),
            Text("Humidity")
        };

        var expanded = SitemapRowPlanner.ExpandChangedIndices([4, -1, 2, 99, 2, 1, 0], rows);

        Assert.Equal([0, 4], expanded);
    }

    [Fact]
    public void ExpandChangedIndices_DoesNotMapOrphanButtonAcrossSeparator()
    {
        var rows = new[]
        {
            Grid("Mode"),
            Button("Auto", "AUTO", true),
            Text("Temperature"),
            Button("Orphan", "ORPHAN", true)
        };

        var expanded = SitemapRowPlanner.ExpandChangedIndices([3], rows);

        Assert.Empty(expanded);
    }

    [Fact]
    public void BuildVisualRows_ExposesNextDescriptorIndex_ForMergedButtonGrid_AndCountMatches()
    {
        var rows = new[]
        {
            Grid("Mode"),
            Button("Auto", "AUTO", true),
            Button("Manual", "MANUAL", true),
            Text("Temperature"),
            Button("Orphan", "ORPHAN", true)
        };

        var visualRows = SitemapRowPlanner.BuildVisualRows(rows);

        Assert.Equal(2, SitemapRowPlanner.CountVisualRows(rows));
        Assert.Equal(2, visualRows.Count);
        Assert.Equal(0, visualRows[0].RowIndex);
        Assert.Equal(3, visualRows[0].NextDescriptorIndex);
        Assert.Equal(3, visualRows[1].RowIndex);
        Assert.Equal(4, visualRows[1].NextDescriptorIndex);
    }

    [Fact]
    public void BuildMergedButtonGridRow_PreservesOptionMetadata()
    {
        var rows = new[]
        {
            Grid("Mode"),
            Button("Auto", "AUTO", true, gridRow: 1, gridColumn: 2, releaseCommand: "AUTO_OFF", stateless: true),
            Button("Manual", "MANUAL", true, gridRow: 3, gridColumn: 4, releaseCommand: "MANUAL_OFF", stateless: false)
        };

        var merged = SitemapRowPlanner.BuildMergedButtonGridRow(0, rows);

        Assert.Collection(
            merged.SelectionOptions,
            option =>
            {
                Assert.Equal(1, option.Row);
                Assert.Equal(2, option.Column);
                Assert.Equal("AUTO", option.ClickCommand);
                Assert.Equal("AUTO_OFF", option.ReleaseCommand);
                Assert.True(option.Stateless);
                Assert.Equal(1, option.SourceRowIndex);
            },
            option =>
            {
                Assert.Equal(3, option.Row);
                Assert.Equal(4, option.Column);
                Assert.Equal("MANUAL", option.ClickCommand);
                Assert.Equal("MANUAL_OFF", option.ReleaseCommand);
                Assert.False(option.Stateless);
                Assert.Equal(2, option.SourceRowIndex);
            });
    }

    [Fact]
    public void TryResolveRowIndexFindsCurrentRowByStableKey()
    {
        var rows = new[] { Text("Kitchen", widgetId: "w-kitchen"), Text("Hall", widgetId: "w-hall") };
        var key = SitemapUiLogic.BuildRowIdentityKey(rows[1]);

        var found = SitemapRowPlanner.TryResolveRowIndex(rows, key, out var rowIndex);

        Assert.True(found);
        Assert.Equal(1, rowIndex);
    }

    [Fact]
    public void TryResolveRowIndexDistinguishesRowsWithSameItemAndDifferentLabels()
    {
        var rows = new[]
        {
            Toggle("Ceiling", "Kitchen_Light"),
            Toggle("Cabinet", "Kitchen_Light")
        };
        var key = SitemapUiLogic.BuildRowIdentityKey(rows[1]);

        var found = SitemapRowPlanner.TryResolveRowIndex(rows, key, out var rowIndex);

        Assert.True(found);
        Assert.Equal(1, rowIndex);
    }

    [Fact]
    public void TryResolveRowIndexReturnsFalseForNullRows()
    {
        var found = SitemapRowPlanner.TryResolveRowIndex(null, "row-key", out var rowIndex);

        Assert.False(found);
        Assert.Equal(-1, rowIndex);
    }

    private static SitemapRowDescriptor Text(string label, string? widgetId = null) =>
        new(label, null, RenderControlKind.Text, RenderActionKind.None, RenderDensity.Compact, [], WidgetId: widgetId);

    private static SitemapRowDescriptor Toggle(string label, string itemName) =>
        new(label, "OFF", RenderControlKind.Toggle, RenderActionKind.SendCommand, RenderDensity.Compact, [], ItemName: itemName);

    private static SitemapRowDescriptor Grid(string label) =>
        new(label, null, RenderControlKind.ButtonGrid, RenderActionKind.SendCommand, RenderDensity.Compact, []);

    private static SitemapRowDescriptor Button(
        string label,
        string command,
        bool visible,
        int? gridRow = null,
        int? gridColumn = null,
        string? releaseCommand = null,
        bool? stateless = null) =>
        new(label, command, RenderControlKind.Button, RenderActionKind.SendCommand, RenderDensity.Compact, [],
            Command: command,
            IsVisible: visible,
            GridRow: gridRow,
            GridColumn: gridColumn,
            ReleaseCommand: releaseCommand,
            Stateless: stateless);
}
