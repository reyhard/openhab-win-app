using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenHab.App.Runtime;
using OpenHab.App.Settings;
using OpenHab.Core.Diagnostics;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.Windows.Tray.Rendering.SitemapSurface;

public sealed class SitemapSurfaceRenderer(
    AppSettingsController settingsController,
    SitemapIconAuthResolver iconAuthResolver,
    Func<string, Task> activateByRowKey,
    Func<string, Task> navigateByRowKey,
    Func<string, string, Task> sendCommandByRowKey)
{
    private sealed record RenderedRowTag(int RowIndex, string RowKey, string VisualStateKey);
    private sealed record ExistingRenderedRow(FrameworkElement Element, int ChildIndex);
    private sealed record PendingRowUpdate(FrameworkElement Element, int RowIndex, SitemapRowDescriptor Row);
    private sealed record RenderContext(
        Uri? IconBaseUri,
        bool UseWindowsIcons,
        SitemapControlFactory.IconAuthContext IconAuth,
        int ChartDpi);
    private bool forceFullRebuild;

    public void ForceFullRebuild()
    {
        forceFullRebuild = true;
    }

    public void Refresh(StackPanel rowsPanel, SitemapRuntimeSnapshot snapshot)
    {
        using var scope = OpenHabProfiling.StartScope("SitemapSurfaceRenderer.Refresh");
        var rows = snapshot.Descriptor?.Rows;
        scope?.SetTag("panel.child_count", rowsPanel.Children.Count);
        scope?.SetTag("rows.total_count", rows?.Count ?? 0);
        scope?.SetTag("rows.changed_count", snapshot.ChangedRowIndices?.Count ?? 0);
        if (rows is null)
        {
            rowsPanel.Children.Clear();
            return;
        }

        if (forceFullRebuild)
        {
            forceFullRebuild = false;
            rowsPanel.Children.Clear();
        }

        if (snapshot.ChangedRowIndices is { Count: > 0 } && rowsPanel.Children.Count > 0)
        {
            RefreshChangedRows(rowsPanel, rows, snapshot);
            return;
        }

        var context = CreateRenderContext(snapshot);
        var visualRows = SitemapRowPlanner.BuildVisualRows(rows);
        if (rowsPanel.Children.Count == visualRows.Count)
        {
            RefreshExistingRows(rowsPanel, visualRows, snapshot, context);
            return;
        }

        if (rowsPanel.Children.Count > 0)
        {
            ReconcileStructuralRows(rowsPanel, visualRows, snapshot, context);
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

            ApplyPartialRowUpdate(existing, visualRow.Row, visualRow.RowIndex);
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

            ApplyPartialRowUpdate(existing, row, index);
        }
    }

    private void ReconcileStructuralRows(
        StackPanel rowsPanel,
        IReadOnlyList<SitemapVisualRow> visualRows,
        SitemapRuntimeSnapshot snapshot,
        RenderContext context)
    {
        using var scope = OpenHabProfiling.StartScope("SitemapSurfaceRenderer.ReconcileStructuralRows");
        scope?.SetTag("panel.child_count_before", rowsPanel.Children.Count);
        scope?.SetTag("visual_rows.count", visualRows.Count);
        var existingByKey = new Dictionary<string, Queue<ExistingRenderedRow>>(StringComparer.Ordinal);
        for (var childIndex = 0; childIndex < rowsPanel.Children.Count; childIndex++)
        {
            if (rowsPanel.Children[childIndex] is not FrameworkElement child
                || child.Tag is not RenderedRowTag tag)
            {
                continue;
            }

            if (!existingByKey.TryGetValue(tag.RowKey, out var bucket))
            {
                bucket = new Queue<ExistingRenderedRow>();
                existingByKey[tag.RowKey] = bucket;
            }

            bucket.Enqueue(new ExistingRenderedRow(child, childIndex));
        }

        var orderedRows = new List<FrameworkElement>();
        var pendingUpdates = new List<PendingRowUpdate>();
        rowsPanel.Children.Clear();

        foreach (var visualRow in visualRows)
        {
            var row = visualRow.Row;
            var rowKey = SitemapControlFactory.BuildRowIdentityKey(row);
            if (existingByKey.TryGetValue(rowKey, out var bucket) && bucket.Count > 0)
            {
                var existing = bucket.Dequeue().Element;
                if (row.Control == RenderControlKind.ButtonGrid || ShouldRebuild(existing, row, visualRow.RowIndex))
                {
                    existing = CreateRowElement(visualRow.RowIndex, row, snapshot, context);
                    SitemapControlFactory.SetVisibility(existing, row.IsVisible);
                }
                else
                {
                    pendingUpdates.Add(new PendingRowUpdate(existing, visualRow.RowIndex, row));
                }

                orderedRows.Add(existing);
                continue;
            }

            var inserted = CreateRowElement(visualRow.RowIndex, row, snapshot, context);
            SitemapControlFactory.SetVisibility(inserted, visible: false);
            if (row.IsVisible)
            {
                pendingUpdates.Add(new PendingRowUpdate(inserted, visualRow.RowIndex, row));
            }

            orderedRows.Add(inserted);
        }

        var disappearing = existingByKey.Values
            .SelectMany(bucket => bucket)
            .OrderBy(item => item.ChildIndex)
            .ToList();

        foreach (var item in disappearing)
        {
            var insertIndex = Math.Min(item.ChildIndex, orderedRows.Count);
            orderedRows.Insert(insertIndex, item.Element);
        }

        foreach (var element in orderedRows)
        {
            rowsPanel.Children.Add(element);
        }

        foreach (var update in pendingUpdates)
        {
            ApplyPartialRowUpdate(update.Element, update.Row, update.RowIndex);
        }

        foreach (var item in disappearing)
        {
            SitemapControlFactory.CollapseAndRemove(rowsPanel, item.Element);
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

                var sourceRowIndex = option.SourceRowIndex ?? index;
                var sourceRows = snapshot.Descriptor?.Rows;
                if (sourceRows is null || sourceRowIndex < 0 || sourceRowIndex >= sourceRows.Count)
                {
                    return Task.CompletedTask;
                }

                var sourceRowKey = SitemapControlFactory.BuildRowIdentityKey(sourceRows[sourceRowIndex]);
                return sendCommandByRowKey(sourceRowKey, expectedCommand);
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

    private static void ApplyPartialRowUpdate(FrameworkElement element, SitemapRowDescriptor row, int rowIndex)
    {
        SitemapControlFactory.UpdateState(element, row);
        SetRenderedRowTag(element, rowIndex, row);
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
