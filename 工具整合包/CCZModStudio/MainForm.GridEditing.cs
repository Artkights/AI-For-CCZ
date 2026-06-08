using CCZModStudio.Core;
using System.Data;
using System.Globalization;
using System.Text;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private void AttachGridEditShortcuts(
        DataGridView grid,
        Action<int, int>? afterCellChanged = null,
        Action? afterGridChanged = null,
        Action? pasteSelection = null,
        Action? fillSelection = null,
        Action? undo = null,
        Action? redo = null)
    {
        grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
        grid.KeyDown += (_, e) =>
        {
            if (e.SuppressKeyPress) return;

            if (e.Control && e.KeyCode == Keys.C)
            {
                CopyGridSelection(grid);
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Control && e.KeyCode == Keys.V)
            {
                if (pasteSelection == null)
                {
                    PasteGridSelection(grid, afterCellChanged, afterGridChanged);
                }
                else
                {
                    pasteSelection();
                }
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Control && e.KeyCode == Keys.D)
            {
                if (fillSelection == null)
                {
                    FillSelectedGridColumnWithCurrentValue(grid, afterCellChanged, afterGridChanged);
                }
                else
                {
                    fillSelection();
                }
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Control && e.KeyCode == Keys.Z && undo != null)
            {
                undo();
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Control && e.KeyCode == Keys.Y && redo != null)
            {
                redo();
                e.SuppressKeyPress = true;
            }
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("复制选区", null, (_, _) => CopyGridSelection(grid));
        menu.Items.Add("粘贴到选区", null, (_, _) =>
        {
            if (pasteSelection == null) PasteGridSelection(grid, afterCellChanged, afterGridChanged);
            else pasteSelection();
        });
        menu.Items.Add("批量填充选区", null, (_, _) =>
        {
            if (fillSelection == null) FillSelectedGridColumnWithCurrentValue(grid, afterCellChanged, afterGridChanged);
            else fillSelection();
        });
        if (undo != null || redo != null)
        {
            menu.Items.Add(new ToolStripSeparator());
            if (undo != null) menu.Items.Add("后退", null, (_, _) => undo());
            if (redo != null) menu.Items.Add("前进", null, (_, _) => redo());
        }
        grid.ContextMenuStrip = menu;
    }

    private void CopyGridSelection(DataGridView grid)
    {
        if (grid.SelectedCells.Count == 0)
        {
            SetStatus("没有选中的单元格可复制。");
            return;
        }

        Clipboard.SetText(BuildGridSelectionText(grid));
        SetStatus($"已复制 {grid.SelectedCells.Count} 个单元格。");
    }

    private static string BuildGridSelectionText(DataGridView grid)
    {
        var cells = grid.SelectedCells.Cast<DataGridViewCell>()
            .Where(cell => cell.RowIndex >= 0 && cell.ColumnIndex >= 0)
            .ToList();
        if (cells.Count == 0) return string.Empty;

        var rowIndexes = cells.Select(cell => cell.RowIndex).Distinct().OrderBy(x => x).ToList();
        var columnIndexes = cells.Select(cell => cell.ColumnIndex).Distinct().OrderBy(x => x).ToList();
        var selected = cells.ToDictionary(cell => (cell.RowIndex, cell.ColumnIndex), cell => cell);
        var lines = new List<string>();
        foreach (var rowIndex in rowIndexes)
        {
            var values = new List<string>();
            foreach (var columnIndex in columnIndexes)
            {
                values.Add(selected.TryGetValue((rowIndex, columnIndex), out var cell)
                    ? Convert.ToString(cell.Value, CultureInfo.InvariantCulture) ?? string.Empty
                    : string.Empty);
            }

            lines.Add(string.Join('\t', values));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void PasteGridSelection(
        DataGridView grid,
        Action<int, int>? afterCellChanged = null,
        Action? afterGridChanged = null)
    {
        if (grid.ReadOnly)
        {
            SetStatus("当前表格不可编辑。");
            return;
        }

        if (!Clipboard.ContainsText())
        {
            SetStatus("剪贴板没有文本。");
            return;
        }

        var start = GetPasteStartCell(grid);
        if (start == null)
        {
            SetStatus("请先选中粘贴起点。");
            return;
        }

        grid.EndEdit();
        var matrix = ParseClipboardMatrix(Clipboard.GetText());
        var changed = 0;
        var lastCell = start.Value;
        for (var r = 0; r < matrix.Count; r++)
        {
            var rowIndex = start.Value.Row + r;
            if (rowIndex >= grid.Rows.Count) break;
            for (var c = 0; c < matrix[r].Count; c++)
            {
                var columnIndex = start.Value.Column + c;
                if (columnIndex >= grid.Columns.Count) break;
                if (!TrySetGridCellValue(grid, rowIndex, columnIndex, matrix[r][c], out _)) continue;
                changed++;
                lastCell = (rowIndex, columnIndex);
                afterCellChanged?.Invoke(rowIndex, columnIndex);
            }
        }

        if (changed > 0)
        {
            grid.CurrentCell = grid.Rows[lastCell.Row].Cells[lastCell.Column];
            afterGridChanged?.Invoke();
        }

        SetStatus($"粘贴完成：更新 {changed} 个单元格。");
    }

    private static (int Row, int Column)? GetPasteStartCell(DataGridView grid)
    {
        if (grid.CurrentCell != null)
        {
            return (grid.CurrentCell.RowIndex, grid.CurrentCell.ColumnIndex);
        }

        return grid.SelectedCells.Cast<DataGridViewCell>()
            .Where(cell => cell.RowIndex >= 0 && cell.ColumnIndex >= 0)
            .OrderBy(cell => cell.RowIndex)
            .ThenBy(cell => cell.ColumnIndex)
            .Select(cell => ((int Row, int Column)?)(cell.RowIndex, cell.ColumnIndex))
            .FirstOrDefault();
    }

    private static List<List<string>> ParseClipboardMatrix(string text)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n');
        if (text.Length == 0) return new List<List<string>> { new() { string.Empty } };
        return text.Split('\n').Select(line => line.Split('\t').ToList()).ToList();
    }

    private void FillSelectedGridColumnWithCurrentValue(
        DataGridView grid,
        Action<int, int>? afterCellChanged = null,
        Action? afterGridChanged = null)
    {
        if (grid.ReadOnly)
        {
            SetStatus("当前表格不可编辑。");
            return;
        }

        if (grid.CurrentCell == null)
        {
            SetStatus("请先选中用于批量填充的单元格。");
            return;
        }

        if (!grid.EndEdit())
        {
            SetStatus("当前单元格未能提交，无法批量填充。");
            return;
        }

        var columnIndex = grid.CurrentCell.ColumnIndex;
        var value = FormatGridValueForBatchInput(grid.CurrentCell.Value);
        var targetCells = GetGridFillTargetCells(grid, columnIndex);
        if (targetCells.Count <= 1)
        {
            SetStatus("请先选中要批量填充的多个单元格或多行。");
            return;
        }

        var changed = 0;
        foreach (var cell in targetCells)
        {
            if (!TrySetGridCellValue(grid, cell.RowIndex, cell.ColumnIndex, value, out _)) continue;
            changed++;
            afterCellChanged?.Invoke(cell.RowIndex, cell.ColumnIndex);
        }

        if (changed > 0) afterGridChanged?.Invoke();
        SetStatus($"批量填充完成：更新 {changed} 个单元格。");
    }

    private string FormatGridValueForBatchInput(object? value)
    {
        if (_currentPageHexButton.Checked &&
            IsIntegerObject(value) &&
            TryGetIntegerValue(value, out var parsed))
        {
            return (parsed.IsNegative ? "-0x" : "0x") + parsed.Magnitude.ToString("X", CultureInfo.InvariantCulture);
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static bool IsIntegerObject(object? value)
        => value is byte or sbyte or ushort or short or uint or int or ulong or long;

    private static List<DataGridViewCell> GetGridFillTargetCells(DataGridView grid, int fallbackColumnIndex)
    {
        var selectedCells = grid.SelectedCells.Cast<DataGridViewCell>()
            .Where(cell => cell.RowIndex >= 0 && cell.ColumnIndex >= 0)
            .OrderBy(cell => cell.RowIndex)
            .ThenBy(cell => cell.ColumnIndex)
            .ToList();
        if (selectedCells.Count > 1)
        {
            return selectedCells;
        }

        var selectedRows = grid.SelectedRows.Cast<DataGridViewRow>()
            .Where(row => row.Index >= 0)
            .OrderBy(row => row.Index)
            .Select(row => row.Cells[fallbackColumnIndex])
            .ToList();
        if (selectedRows.Count > 1)
        {
            return selectedRows;
        }

        return selectedCells;
    }

    private bool TrySetGridCellValue(DataGridView grid, int rowIndex, int columnIndex, string text, out string error)
    {
        error = string.Empty;
        if (rowIndex < 0 || rowIndex >= grid.Rows.Count || columnIndex < 0 || columnIndex >= grid.Columns.Count) return false;
        var column = grid.Columns[columnIndex];
        if (column.ReadOnly || !column.Visible) return false;
        var cell = grid.Rows[rowIndex].Cells[columnIndex];
        if (cell.ReadOnly) return false;

        try
        {
            var targetType = ResolveGridCellTargetType(grid, rowIndex, columnIndex);
            cell.Value = ConvertGridTextValue(text, targetType);
            cell.ErrorText = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            cell.ErrorText = error;
            return false;
        }
    }

    private static Type? ResolveGridCellTargetType(DataGridView grid, int rowIndex, int columnIndex)
    {
        var column = grid.Columns[columnIndex];
        if (TryGetDataColumn(grid, column, out var dataColumn))
        {
            if (dataColumn.DataType != typeof(object)) return dataColumn.DataType;
        }

        var value = rowIndex >= 0 && rowIndex < grid.Rows.Count
            ? grid.Rows[rowIndex].Cells[columnIndex].Value
            : null;
        return value == null || value == DBNull.Value ? column.ValueType : value.GetType();
    }

    private object ConvertGridTextValue(string text, Type? targetType)
    {
        targetType = NormalizeNullableType(targetType);
        if (targetType == null || targetType == typeof(object) || targetType == typeof(string))
        {
            return text;
        }

        if (IsSupportedIntegerType(targetType) &&
            TryParseIntegerInput(text, _currentPageHexButton.Checked, out var parsed) &&
            TryConvertParsedIntegerToType(parsed, targetType, out var converted))
        {
            return converted;
        }

        if (targetType == typeof(bool))
        {
            if (bool.TryParse(text, out var boolValue)) return boolValue;
            if (text == "1") return true;
            if (text == "0") return false;
        }

        return Convert.ChangeType(text, targetType, CultureInfo.InvariantCulture);
    }

    private void ExportDataTableCsv(DataTable? table, string defaultFileName)
    {
        if (table == null) return;
        using var dialog = new SaveFileDialog
        {
            Title = "导出 CSV",
            Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
            FileName = defaultFileName
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            CsvService.Export(table, dialog.FileName);
            System.Diagnostics.Debug.WriteLine("已导出 CSV：" + dialog.FileName);
            SetStatus("CSV 导出完成");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("CSV 导出失败：" + ex);
            MessageBox.Show(this, ex.Message, "CSV 导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportDataTableCsv(DataTable? table, string editorName, Action? afterImport = null)
    {
        if (table == null) return;
        using var dialog = new OpenFileDialog
        {
            Title = "导入 CSV",
            Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var count = CsvService.ImportInto(table, dialog.FileName, allowPartialColumns: true, matchByIdWhenPresent: true);
            afterImport?.Invoke();
            System.Diagnostics.Debug.WriteLine($"已导入 {editorName} CSV：{dialog.FileName}，更新行 {count}");
            SetStatus($"{editorName} CSV 导入完成：更新 {count} 行，请检查后保存。");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"{editorName} CSV 导入失败：" + ex);
            MessageBox.Show(this, ex.Message, $"{editorName} CSV 导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
