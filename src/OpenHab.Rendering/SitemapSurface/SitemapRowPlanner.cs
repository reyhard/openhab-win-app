using OpenHab.Rendering.Descriptors;

namespace OpenHab.Rendering.SitemapSurface;

public static class SitemapRowPlanner
{
    public static IReadOnlyList<SitemapVisualRow> BuildVisualRows(IReadOnlyList<SitemapRowDescriptor> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var visualRows = new List<SitemapVisualRow>();
        var index = 0;
        while (index < rows.Count)
        {
            var row = rows[index];
            if (row.Control == RenderControlKind.Button)
            {
                index++;
                continue;
            }

            if (row.Control == RenderControlKind.ButtonGrid)
            {
                var mergedRow = BuildMergedButtonGridRow(index, rows, out var nextIndex);
                visualRows.Add(new SitemapVisualRow(index, mergedRow, nextIndex));
                index = nextIndex;
                continue;
            }

            visualRows.Add(new SitemapVisualRow(index, row, index + 1));
            index++;
        }

        return visualRows;
    }

    public static IReadOnlyList<int> ExpandChangedIndices(IReadOnlyList<int> changedIndices, IReadOnlyList<SitemapRowDescriptor> rows)
    {
        ArgumentNullException.ThrowIfNull(changedIndices);
        ArgumentNullException.ThrowIfNull(rows);

        var set = new SortedSet<int>();
        foreach (var index in changedIndices)
        {
            if (index < 0 || index >= rows.Count)
            {
                continue;
            }

            var effectiveIndex = index;
            if (rows[index].Control == RenderControlKind.Button)
            {
                effectiveIndex = FindOwningButtonGridIndex(index, rows);
                if (effectiveIndex < 0)
                {
                    continue;
                }
            }

            if (rows[effectiveIndex].Control == RenderControlKind.Button)
            {
                continue;
            }

            set.Add(effectiveIndex);
        }

        return set.ToArray();
    }

    public static bool TryResolveRowIndex(IReadOnlyList<SitemapRowDescriptor>? rows, string rowKey, out int rowIndex)
    {
        ArgumentNullException.ThrowIfNull(rowKey);

        if (rows is null)
        {
            rowIndex = -1;
            return false;
        }

        for (var index = 0; index < rows.Count; index++)
        {
            if (string.Equals(SitemapRowVisualPolicy.BuildRowIdentityKey(rows[index]), rowKey, StringComparison.Ordinal))
            {
                rowIndex = index;
                return true;
            }
        }

        rowIndex = -1;
        return false;
    }

    public static int CountVisualRows(IReadOnlyList<SitemapRowDescriptor> rows) => BuildVisualRows(rows).Count;

    public static SitemapRowDescriptor BuildMergedButtonGridRow(int gridIndex, IReadOnlyList<SitemapRowDescriptor> rows) =>
        BuildMergedButtonGridRow(gridIndex, rows, out _);

    internal static SitemapRowDescriptor BuildMergedButtonGridRow(int gridIndex, IReadOnlyList<SitemapRowDescriptor> rows, out int nextIndex)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (gridIndex < 0 || gridIndex >= rows.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(gridIndex));
        }

        var row = rows[gridIndex];
        if (row.Control != RenderControlKind.ButtonGrid)
        {
            nextIndex = gridIndex + 1;
            return row;
        }

        var childOptions = BuildMergedButtonGridOptions(gridIndex, rows, out nextIndex);
        return childOptions.Count > 0 ? row with { SelectionOptions = childOptions } : row;
    }

    private static List<SitemapMapOption> BuildMergedButtonGridOptions(int gridIndex, IReadOnlyList<SitemapRowDescriptor> rows, out int nextIndex)
    {
        var childOptions = new List<SitemapMapOption>();
        var scan = gridIndex + 1;
        while (scan < rows.Count && rows[scan].Control == RenderControlKind.Button)
        {
            var child = rows[scan];
            var command = child.Command ?? child.RawItemState ?? child.RawState ?? child.State ?? string.Empty;
            var isActive = string.Equals(child.RawItemState ?? child.RawState ?? child.State, "ON", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(child.Command, "ON", StringComparison.OrdinalIgnoreCase);

            childOptions.Add(new SitemapMapOption(
                command,
                child.Label,
                child.GridRow,
                child.GridColumn,
                isActive,
                child.Command,
                child.ReleaseCommand,
                child.Stateless,
                scan));
            scan++;
        }

        nextIndex = scan;

        var visibleChildOptions = childOptions.Where(option => option.SourceRowIndex.HasValue && rows[option.SourceRowIndex.Value].IsVisible).ToList();
        if (visibleChildOptions.Count > 0)
        {
            childOptions = visibleChildOptions;
        }

        return childOptions;
    }

    private static int FindOwningButtonGridIndex(int childIndex, IReadOnlyList<SitemapRowDescriptor> rows)
    {
        for (var scan = childIndex - 1; scan >= 0; scan--)
        {
            if (rows[scan].Control == RenderControlKind.ButtonGrid)
            {
                return scan;
            }

            if (rows[scan].Control != RenderControlKind.Button)
            {
                break;
            }
        }

        return -1;
    }
}
