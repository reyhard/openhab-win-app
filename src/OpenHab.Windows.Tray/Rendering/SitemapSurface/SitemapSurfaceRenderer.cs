using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenHab.App.Runtime;
using OpenHab.App.Settings;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.Windows.Tray.Rendering.SitemapSurface;

public sealed class SitemapSurfaceRenderer(
    AppSettingsController settingsController,
    SitemapIconAuthResolver iconAuthResolver,
    Func<string, Task> activateByRowKey,
    Func<string, Task> navigateByRowKey,
    Func<string, string, Task> sendCommandByRowKey,
    Func<int, string, Task> sendCommandByRowIndex)
{
    private sealed record RenderedRowTag(int RowIndex, string RowKey, string VisualStateKey);
    private sealed record RenderContext(
        Uri? IconBaseUri,
        bool UseWindowsIcons,
        SitemapControlFactory.IconAuthContext IconAuth,
        int ChartDpi);

    public void Refresh(StackPanel rowsPanel, SitemapRuntimeSnapshot snapshot)
    {
        var rows = snapshot.Descriptor?.Rows;
        if (rows is null)
        {
            rowsPanel.Children.Clear();
            return;
        }

        if (snapshot.ChangedRowIndices is { Count: > 0 })
        {
            RefreshChangedRows(rowsPanel, rows, snapshot);
            return;
        }

        var visualRows = SitemapRowPlanner.BuildVisualRows(rows);
        var context = CreateRenderContext(snapshot);
        if (rowsPanel.Children.Count == visualRows.Count)
        {
            RefreshExistingRows(rowsPanel, visualRows, snapshot, context);
            return;
        }

        rowsPanel.Children.Clear();
        foreach (var visualRow in visualRows)
        {
            var element = CreateRowElement(visualRow.RowIndex, visualRow.Row, snapshot, context);
            rowsPanel.Children.Add(element);
            SitemapControlFactory.SetVisibility(element, visualRow.Row.IsVisible);
        }
    }

    private void RefreshExistingRows(
        StackPanel rowsPanel,
        IReadOnlyList<SitemapVisualRow> visualRows,
        SitemapRuntimeSnapshot snapshot,
        RenderContext context)
    {
        for (var childIndex = 0; childIndex < visualRows.Count; childIndex++)
        {
            var visualRow = visualRows[childIndex];
            if (rowsPanel.Children[childIndex] is not FrameworkElement existing
                || existing.Tag is not RenderedRowTag tag
                || tag.RowIndex != visualRow.RowIndex
                || !string.Equals(tag.RowKey, SitemapControlFactory.BuildRowIdentityKey(visualRow.Row), StringComparison.Ordinal)
                || visualRow.Row.Control == RenderControlKind.ButtonGrid
                || ShouldRebuild(existing, visualRow.Row, visualRow.RowIndex))
            {
                var replacement = CreateRowElement(visualRow.RowIndex, visualRow.Row, snapshot, context);
                SitemapControlFactory.SetVisibility(replacement, visualRow.Row.IsVisible);
                rowsPanel.Children.RemoveAt(childIndex);
                rowsPanel.Children.Insert(childIndex, replacement);
                continue;
            }

            SitemapControlFactory.UpdateState(existing, visualRow.Row);
            SitemapControlFactory.SetVisibility(existing, visualRow.Row.IsVisible);
            SetRenderedRowTag(existing, visualRow.RowIndex, visualRow.Row);
        }
    }

    private void RefreshChangedRows(
        StackPanel rowsPanel,
        IReadOnlyList<SitemapRowDescriptor> rows,
        SitemapRuntimeSnapshot snapshot)
    {
        var context = CreateRenderContext(snapshot);
        foreach (var index in SitemapRowPlanner.ExpandChangedIndices(snapshot.ChangedRowIndices, rows))
        {
            if (!TryFindRenderedRow(rowsPanel, index, out var existing, out var childIndex))
            {
                continue;
            }

            var row = rows[index].Control == RenderControlKind.ButtonGrid
                ? SitemapRowPlanner.BuildMergedButtonGridRow(index, rows)
                : rows[index];

            if (rows[index].Control == RenderControlKind.ButtonGrid || ShouldRebuild(existing, row, index))
            {
                var replacement = CreateRowElement(index, row, snapshot, context);
                SitemapControlFactory.SetVisibility(replacement, row.IsVisible);
                rowsPanel.Children.RemoveAt(childIndex);
                rowsPanel.Children.Insert(childIndex, replacement);
                continue;
            }

            SitemapControlFactory.UpdateState(existing, row);
            SitemapControlFactory.SetVisibility(existing, row.IsVisible);
            SetRenderedRowTag(existing, index, row);
        }
    }

    private RenderContext CreateRenderContext(SitemapRuntimeSnapshot snapshot)
    {
        var iconTransport = snapshot.ActiveTransport ?? TransportKind.Local;
        var iconBaseUri = iconTransport == TransportKind.Local
            ? settingsController.Current.LocalEndpoint
            : settingsController.Current.CloudEndpoint;
        var iconAuth = iconAuthResolver.Resolve(iconTransport);

        return new RenderContext(
            iconBaseUri,
            settingsController.Current.UseWindows11Icons,
            iconAuth,
            (int)settingsController.Current.ChartQuality);
    }

    private FrameworkElement CreateRowElement(int index, SitemapRowDescriptor row, SitemapRuntimeSnapshot snapshot, RenderContext context)
    {
        if (row.Control == RenderControlKind.ButtonGrid)
        {
            Func<SitemapMapOption, bool, Task> sendGridCommand = (option, isRelease) =>
            {
                var expectedCommand = isRelease ? option.ReleaseCommand : option.ClickCommand ?? option.Command;
                if (string.IsNullOrWhiteSpace(expectedCommand)
                    || string.Equals(expectedCommand, "NULL", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.CompletedTask;
                }

                return option.SourceRowIndex.HasValue
                    ? sendCommandByRowIndex(option.SourceRowIndex.Value, expectedCommand)
                    : sendCommandByRowIndex(index, expectedCommand);
            };

            var element = SitemapControlFactory.Create(
                row,
                activateRow: null,
                sendCommand: null,
                context.IconBaseUri,
                context.UseWindowsIcons,
                context.IconAuth,
                chartDpi: context.ChartDpi,
                sendButtonGridCommand: sendGridCommand);
            SetRenderedRowTag(element, index, row);
            return element;
        }

        var rowKey = SitemapControlFactory.BuildRowIdentityKey(row);
        Func<Task>? activateRow = row.Action switch
        {
            RenderActionKind.Navigate => () => navigateByRowKey(rowKey),
            RenderActionKind.SendCommand when row.Control == RenderControlKind.Toggle => () => activateByRowKey(rowKey),
            _ => null
        };
        Func<string, Task>? sendCommand = row.Action == RenderActionKind.SendCommand
            ? command => sendCommandByRowKey(rowKey, command)
            : null;

        var created = SitemapControlFactory.Create(
            row,
            activateRow,
            sendCommand,
            context.IconBaseUri,
            context.UseWindowsIcons,
            context.IconAuth,
            chartDpi: context.ChartDpi);
        SetRenderedRowTag(created, index, row);
        return created;
    }

    private static bool TryFindRenderedRow(
        StackPanel rowsPanel,
        int rowIndex,
        out FrameworkElement element,
        out int childIndex)
    {
        for (var i = 0; i < rowsPanel.Children.Count; i++)
        {
            if (rowsPanel.Children[i] is FrameworkElement candidate
                && candidate.Tag is RenderedRowTag tag
                && tag.RowIndex == rowIndex)
            {
                element = candidate;
                childIndex = i;
                return true;
            }
        }

        element = null!;
        childIndex = -1;
        return false;
    }

    private static void SetRenderedRowTag(FrameworkElement element, int rowIndex, SitemapRowDescriptor row)
    {
        element.Tag = new RenderedRowTag(
            rowIndex,
            SitemapControlFactory.BuildRowIdentityKey(row),
            SitemapControlFactory.BuildRowVisualStateKey(row, rowIndex));
    }

    private static bool ShouldRebuild(FrameworkElement element, SitemapRowDescriptor row, int rowIndex)
    {
        return element.Tag is RenderedRowTag tag
               && !string.Equals(
                   tag.VisualStateKey,
                   SitemapControlFactory.BuildRowVisualStateKey(row, rowIndex),
                   StringComparison.Ordinal);
    }
}
