using System.ComponentModel;
using System.Data;
using System.Globalization;
using CCZModStudio.Models;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private sealed record GridViewportSnapshot(
        string? RowKey,
        int RowIndex,
        string? ColumnName,
        int FirstDisplayedRowIndex,
        int FirstDisplayedColumnIndex,
        IReadOnlyList<string> SelectedRowKeys,
        IReadOnlyList<GridCellKey> SelectedCellKeys,
        string? SortColumnName,
        ListSortDirection? SortDirection);

    private sealed record GridCellKey(string? RowKey, int RowIndex, string? ColumnName);

    private GridViewportSnapshot CaptureGridViewport(DataGridView grid, string? keyColumn = "ID")
    {
        var currentRow = grid.CurrentCell?.OwningRow;
        var currentColumn = grid.CurrentCell?.OwningColumn;
        var selectedRows = grid.SelectedRows
            .Cast<DataGridViewRow>()
            .Where(row => !row.IsNewRow)
            .Select(row => GetGridRowKey(row, keyColumn))
            .Where(key => key != null)
            .Select(key => key!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var selectedCells = grid.SelectedCells
            .Cast<DataGridViewCell>()
            .Where(cell => cell.RowIndex >= 0 && cell.ColumnIndex >= 0)
            .Select(cell => new GridCellKey(
                GetGridRowKey(grid.Rows[cell.RowIndex], keyColumn),
                cell.RowIndex,
                GetGridColumnName(grid.Columns[cell.ColumnIndex])))
            .Distinct()
            .ToList();

        return new GridViewportSnapshot(
            currentRow == null ? null : GetGridRowKey(currentRow, keyColumn),
            grid.CurrentCell?.RowIndex ?? -1,
            currentColumn == null ? null : GetGridColumnName(currentColumn),
            TryGetFirstDisplayedRowIndex(grid),
            TryGetFirstDisplayedColumnIndex(grid),
            selectedRows,
            selectedCells,
            grid.SortedColumn == null ? null : GetGridColumnName(grid.SortedColumn),
            grid.SortOrder switch
            {
                SortOrder.Ascending => ListSortDirection.Ascending,
                SortOrder.Descending => ListSortDirection.Descending,
                _ => null
            });
    }

    private void RestoreGridViewport(DataGridView grid, GridViewportSnapshot snapshot, string? keyColumn = "ID")
    {
        if (grid.Rows.Count == 0)
        {
            return;
        }

        RestoreGridSort(grid, snapshot);
        RestoreGridCurrentCell(grid, snapshot, keyColumn);
        RestoreGridSelection(grid, snapshot, keyColumn);
        RestoreGridScroll(grid, snapshot);
    }

    private void RunPreservingGridViewport(DataGridView grid, Action action, string? keyColumn = "ID")
    {
        var snapshot = CaptureGridViewport(grid, keyColumn);
        action();
        RestoreGridViewport(grid, snapshot, keyColumn);
    }

    private static void AcceptSavedDataTable(DataTable table, Action? refreshVisibleRows = null)
    {
        table.AcceptChanges();
        refreshVisibleRows?.Invoke();
    }

    private static void SyncDataTableRowsByKey(DataTable target, DataTable source, string keyColumn)
    {
        if (!target.Columns.Contains(keyColumn) || !source.Columns.Contains(keyColumn))
        {
            throw new InvalidOperationException($"Cannot sync data table rows: missing key column {keyColumn}.");
        }

        var sourceRows = source.Rows
            .Cast<DataRow>()
            .ToDictionary(row => Convert.ToString(row[keyColumn], CultureInfo.InvariantCulture) ?? string.Empty, StringComparer.Ordinal);

        foreach (DataRow targetRow in target.Rows)
        {
            var key = Convert.ToString(targetRow[keyColumn], CultureInfo.InvariantCulture) ?? string.Empty;
            if (!sourceRows.TryGetValue(key, out var sourceRow))
            {
                continue;
            }

            foreach (DataColumn column in target.Columns)
            {
                if (source.Columns.Contains(column.ColumnName) && !column.ReadOnly)
                {
                    targetRow[column.ColumnName] = sourceRow[column.ColumnName];
                }
            }
        }
    }

    private static IReadOnlyList<DataRow> GetChangedRows(DataTable table)
        => table.Rows
            .Cast<DataRow>()
            .Where(row => row.RowState is not DataRowState.Unchanged and not DataRowState.Detached)
            .ToList();

    private void VerifySavedTableMatchesCurrentData(
        HexTableDefinition table,
        DataTable expected,
        DataTable actual,
        IReadOnlyList<DataRow> changedRows)
        => VerifySavedDataTableChanges(expected, actual, changedRows, null, table.TableName);

    private static void VerifySavedDataTableChanges(
        DataTable expected,
        DataTable actual,
        IReadOnlyList<DataRow> changedRows,
        IReadOnlyList<string>? columns = null,
        string? label = null)
    {
        if (changedRows.Count == 0)
        {
            return;
        }

        var tableLabel = string.IsNullOrWhiteSpace(label) ? expected.TableName : label;
        foreach (var expectedRow in changedRows)
        {
            var actualRow = FindMatchingSavedRow(expected, actual, expectedRow);
            if (actualRow == null)
            {
                throw new InvalidOperationException($"Save verification failed for {tableLabel}: row {DescribeDataRow(expected, expectedRow)} was not found after reread.");
            }

            var columnsToCheck = ResolveColumnsToVerify(expectedRow, columns);
            foreach (var columnName in columnsToCheck)
            {
                if (!expected.Columns.Contains(columnName) || !actual.Columns.Contains(columnName))
                {
                    continue;
                }

                var expectedText = Convert.ToString(expectedRow[columnName, DataRowVersion.Current], CultureInfo.InvariantCulture) ?? string.Empty;
                var actualText = Convert.ToString(actualRow[columnName], CultureInfo.InvariantCulture) ?? string.Empty;
                if (!string.Equals(expectedText, actualText, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Save verification failed for {tableLabel}: row {DescribeDataRow(expected, expectedRow)} column {columnName} expected \"{expectedText}\", actual \"{actualText}\".");
                }
            }
        }
    }

    private TableSaveResult? SaveChangedTableAndVerify(TableReadResult read)
    {
        if (_project == null)
        {
            return null;
        }

        var changedRows = GetChangedRows(read.Data);
        if (changedRows.Count == 0)
        {
            return null;
        }

        var save = _tableWriter.Save(_project, read.Table, read.Data);
        var verifyRead = _tableReader.Read(_project, read.Table, _tables);
        if (!verifyRead.Validation.IsUsable)
        {
            throw new InvalidOperationException("Save verification reread failed; check diagnostics and backup.");
        }

        VerifySavedTableMatchesCurrentData(read.Table, read.Data, verifyRead.Data, changedRows);
        AcceptSavedDataTable(read.Data);
        return save;
    }

    private void RestoreGridSort(DataGridView grid, GridViewportSnapshot snapshot)
    {
        if (snapshot.SortColumnName == null || snapshot.SortDirection == null)
        {
            return;
        }

        var column = FindGridColumn(grid, snapshot.SortColumnName);
        if (column?.SortMode == DataGridViewColumnSortMode.NotSortable)
        {
            return;
        }

        try
        {
            grid.Sort(column!, snapshot.SortDirection.Value);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void RestoreGridCurrentCell(DataGridView grid, GridViewportSnapshot snapshot, string? keyColumn)
    {
        var rowIndex = FindGridRowIndex(grid, snapshot.RowKey, snapshot.RowIndex, keyColumn);
        var column = snapshot.ColumnName == null ? null : FindGridColumn(grid, snapshot.ColumnName);
        if (rowIndex < 0)
        {
            return;
        }

        var columnIndex = column?.Index ?? Math.Clamp(grid.CurrentCell?.ColumnIndex ?? 0, 0, grid.Columns.Count - 1);
        while (columnIndex < grid.Columns.Count && !grid.Columns[columnIndex].Visible)
        {
            columnIndex++;
        }

        if (columnIndex >= grid.Columns.Count)
        {
            columnIndex = grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(x => x.Visible)?.Index ?? -1;
        }

        if (columnIndex < 0)
        {
            return;
        }

        try
        {
            grid.CurrentCell = grid.Rows[rowIndex].Cells[columnIndex];
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void RestoreGridSelection(DataGridView grid, GridViewportSnapshot snapshot, string? keyColumn)
    {
        try
        {
            grid.ClearSelection();
            foreach (var key in snapshot.SelectedRowKeys)
            {
                var rowIndex = FindGridRowIndex(grid, key, -1, keyColumn);
                if (rowIndex >= 0)
                {
                    grid.Rows[rowIndex].Selected = true;
                }
            }

            foreach (var cellKey in snapshot.SelectedCellKeys)
            {
                var rowIndex = FindGridRowIndex(grid, cellKey.RowKey, cellKey.RowIndex, keyColumn);
                var column = cellKey.ColumnName == null ? null : FindGridColumn(grid, cellKey.ColumnName);
                if (rowIndex >= 0 && column != null && column.Visible)
                {
                    grid.Rows[rowIndex].Cells[column.Index].Selected = true;
                }
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void RestoreGridScroll(DataGridView grid, GridViewportSnapshot snapshot)
    {
        if (snapshot.FirstDisplayedColumnIndex >= 0)
        {
            try
            {
                grid.FirstDisplayedScrollingColumnIndex = Math.Clamp(snapshot.FirstDisplayedColumnIndex, 0, grid.Columns.Count - 1);
            }
            catch (InvalidOperationException)
            {
            }
        }

        if (snapshot.FirstDisplayedRowIndex >= 0 && grid.Rows.Count > 0)
        {
            var rowIndex = Math.Clamp(snapshot.FirstDisplayedRowIndex, 0, grid.Rows.Count - 1);
            while (rowIndex < grid.Rows.Count && !grid.Rows[rowIndex].Visible)
            {
                rowIndex++;
            }

            if (rowIndex < grid.Rows.Count)
            {
                try
                {
                    grid.FirstDisplayedScrollingRowIndex = rowIndex;
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
    }

    private static IReadOnlyList<string> ResolveColumnsToVerify(DataRow row, IReadOnlyList<string>? columns)
    {
        if (columns is { Count: > 0 })
        {
            return columns;
        }

        if (row.RowState == DataRowState.Added || !row.HasVersion(DataRowVersion.Original))
        {
            return row.Table.Columns.Cast<DataColumn>().Select(column => column.ColumnName).ToList();
        }

        return row.Table.Columns
            .Cast<DataColumn>()
            .Where(column =>
            {
                var original = Convert.ToString(row[column, DataRowVersion.Original], CultureInfo.InvariantCulture) ?? string.Empty;
                var current = Convert.ToString(row[column, DataRowVersion.Current], CultureInfo.InvariantCulture) ?? string.Empty;
                return !string.Equals(original, current, StringComparison.Ordinal);
            })
            .Select(column => column.ColumnName)
            .ToList();
    }

    private static DataRow? FindMatchingSavedRow(DataTable expected, DataTable actual, DataRow expectedRow)
    {
        if (expected.Columns.Contains("ID") && actual.Columns.Contains("ID"))
        {
            var id = Convert.ToString(expectedRow["ID", DataRowVersion.Current], CultureInfo.InvariantCulture);
            return actual.Rows
                .Cast<DataRow>()
                .FirstOrDefault(row => string.Equals(Convert.ToString(row["ID"], CultureInfo.InvariantCulture), id, StringComparison.Ordinal));
        }

        var index = expected.Rows.IndexOf(expectedRow);
        return index >= 0 && index < actual.Rows.Count ? actual.Rows[index] : null;
    }

    private static string DescribeDataRow(DataTable table, DataRow row)
    {
        if (table.Columns.Contains("ID"))
        {
            return "ID=" + Convert.ToString(row["ID", DataRowVersion.Current], CultureInfo.InvariantCulture);
        }

        return "#" + table.Rows.IndexOf(row).ToString(CultureInfo.InvariantCulture);
    }

    private static int TryGetFirstDisplayedRowIndex(DataGridView grid)
    {
        try
        {
            return grid.Rows.Count == 0 ? -1 : grid.FirstDisplayedScrollingRowIndex;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private static int TryGetFirstDisplayedColumnIndex(DataGridView grid)
    {
        try
        {
            return grid.Columns.Count == 0 ? -1 : grid.FirstDisplayedScrollingColumnIndex;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private static string? GetGridRowKey(DataGridViewRow row, string? keyColumn)
    {
        if (string.IsNullOrWhiteSpace(keyColumn))
        {
            return null;
        }

        if (row.DataBoundItem is DataRowView view && view.Row.Table.Columns.Contains(keyColumn))
        {
            return Convert.ToString(view.Row[keyColumn], CultureInfo.InvariantCulture);
        }

        if (row.DataBoundItem is DataRow dataRow && dataRow.Table.Columns.Contains(keyColumn))
        {
            return Convert.ToString(dataRow[keyColumn], CultureInfo.InvariantCulture);
        }

        var column = row.DataGridView == null ? null : FindGridColumn(row.DataGridView, keyColumn);
        return column == null ? null : Convert.ToString(row.Cells[column.Index].Value, CultureInfo.InvariantCulture);
    }

    private static int FindGridRowIndex(DataGridView grid, string? rowKey, int fallbackRowIndex, string? keyColumn)
    {
        if (!string.IsNullOrWhiteSpace(rowKey) && !string.IsNullOrWhiteSpace(keyColumn))
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow)
                {
                    continue;
                }

                if (string.Equals(GetGridRowKey(row, keyColumn), rowKey, StringComparison.Ordinal))
                {
                    return row.Index;
                }
            }
        }

        return fallbackRowIndex >= 0 && fallbackRowIndex < grid.Rows.Count ? fallbackRowIndex : -1;
    }

    private static DataGridViewColumn? FindGridColumn(DataGridView grid, string columnName)
        => grid.Columns
            .Cast<DataGridViewColumn>()
            .FirstOrDefault(column =>
                string.Equals(GetGridColumnName(column), columnName, StringComparison.Ordinal) ||
                string.Equals(column.Name, columnName, StringComparison.Ordinal));

    private static string GetGridColumnName(DataGridViewColumn column)
        => string.IsNullOrWhiteSpace(column.DataPropertyName) ? column.Name : column.DataPropertyName;
}
