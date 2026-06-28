using CCZModStudio.Core;
using System.Data;
using System.Globalization;
using System.Text;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private sealed record GridCellEdit(GridCellKey Key, object? OldValue, object? NewValue);

    private sealed record GridBatchEditResult(
        int Changed,
        int Skipped,
        IReadOnlyList<GridCellKey> ChangedCells,
        IReadOnlyList<string> Errors);

    private sealed class GridEditSession
    {
        public Stack<IReadOnlyList<GridCellEdit>> UndoStack { get; } = new();
        public Stack<IReadOnlyList<GridCellEdit>> RedoStack { get; } = new();
        public IReadOnlyList<GridCellKey> RecentChangedCells { get; private set; } = Array.Empty<GridCellKey>();
        public GridViewportSnapshot? Viewport { get; set; }

        public bool CanUndo => UndoStack.Count > 0;
        public bool CanRedo => RedoStack.Count > 0;

        public void Push(IReadOnlyList<GridCellEdit> edits)
        {
            if (edits.Count == 0) return;
            UndoStack.Push(edits.ToList());
            RedoStack.Clear();
            RecentChangedCells = edits.Select(edit => edit.Key).Distinct().ToList();
        }

        public void MarkRecent(IReadOnlyList<GridCellKey> cells)
            => RecentChangedCells = cells.Distinct().ToList();

        public void Clear()
        {
            UndoStack.Clear();
            RedoStack.Clear();
            RecentChangedCells = Array.Empty<GridCellKey>();
            Viewport = null;
        }

        public GridEditSession Clone()
        {
            var clone = new GridEditSession
            {
                Viewport = Viewport
            };
            foreach (var edits in UndoStack.Reverse())
            {
                clone.UndoStack.Push(edits.ToList());
            }

            foreach (var edits in RedoStack.Reverse())
            {
                clone.RedoStack.Push(edits.ToList());
            }

            clone.RecentChangedCells = RecentChangedCells.ToList();
            return clone;
        }
    }

    private readonly Dictionary<DataGridView, GridEditSession> _gridEditSessions = new();

    private GridEditSession GetGridEditSession(DataGridView grid)
    {
        if (!_gridEditSessions.TryGetValue(grid, out var session))
        {
            session = new GridEditSession();
            _gridEditSessions[grid] = session;
        }

        return session;
    }

    private void ClearGridEditSession(DataGridView grid)
        => GetGridEditSession(grid).Clear();

    private bool CanUndoGridEdit(DataGridView grid)
        => _gridEditSessions.TryGetValue(grid, out var session) && session.CanUndo;

    private bool CanRedoGridEdit(DataGridView grid)
        => _gridEditSessions.TryGetValue(grid, out var session) && session.CanRedo;

    private void AttachGridEditShortcuts(
        DataGridView grid,
        Action<int, int>? afterCellChanged = null,
        Action? afterGridChanged = null,
        Action? pasteSelection = null,
        Action? fillSelection = null,
        Action? undo = null,
        Action? redo = null,
        Action<IReadOnlyList<GridCellKey>>? afterCellsChanged = null)
    {
        grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
        grid.KeyDown += (_, e) =>
        {
            if (e.SuppressKeyPress) return;

            if (e.Control && !e.Shift && e.KeyCode == Keys.C)
            {
                CopyGridSelection(grid);
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Control && !e.Shift && e.KeyCode == Keys.V)
            {
                if (pasteSelection == null) PasteGridSelection(grid, afterCellChanged, afterGridChanged, afterCellsChanged);
                else pasteSelection();
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Control && !e.Shift && e.KeyCode == Keys.D)
            {
                if (fillSelection == null) FillSelectedGridColumnWithCurrentValue(grid, afterCellChanged, afterGridChanged, afterCellsChanged);
                else fillSelection();
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Control && !e.Shift && e.KeyCode == Keys.H)
            {
                ShowGridBatchModifyDialog(grid, afterCellChanged, afterGridChanged, afterCellsChanged);
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Delete)
            {
                ClearSelectedGridCells(grid, afterCellChanged, afterGridChanged, afterCellsChanged);
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.F2 && grid.CurrentCell != null && !grid.CurrentCell.ReadOnly)
            {
                grid.BeginEdit(selectAll: true);
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Control && e.Shift && e.KeyCode == Keys.Z)
            {
                if (CanRedoGridEdit(grid)) RedoGridEdit(grid, afterCellChanged, afterGridChanged, afterCellsChanged);
                else redo?.Invoke();
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Control && !e.Shift && e.KeyCode == Keys.Z)
            {
                if (CanUndoGridEdit(grid)) UndoGridEdit(grid, afterCellChanged, afterGridChanged, afterCellsChanged);
                else undo?.Invoke();
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Control && !e.Shift && e.KeyCode == Keys.Y)
            {
                if (CanRedoGridEdit(grid)) RedoGridEdit(grid, afterCellChanged, afterGridChanged, afterCellsChanged);
                else redo?.Invoke();
                e.SuppressKeyPress = true;
            }
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Copy", null, (_, _) => CopyGridSelection(grid));
        menu.Items.Add("Paste", null, (_, _) =>
        {
            if (pasteSelection == null) PasteGridSelection(grid, afterCellChanged, afterGridChanged, afterCellsChanged);
            else pasteSelection();
        });
        menu.Items.Add("Fill from current cell", null, (_, _) =>
        {
            if (fillSelection == null) FillSelectedGridColumnWithCurrentValue(grid, afterCellChanged, afterGridChanged, afterCellsChanged);
            else fillSelection();
        });
        menu.Items.Add("Batch modify...", null, (_, _) => ShowGridBatchModifyDialog(grid, afterCellChanged, afterGridChanged, afterCellsChanged));
        menu.Items.Add("Clear selection", null, (_, _) => ClearSelectedGridCells(grid, afterCellChanged, afterGridChanged, afterCellsChanged));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Undo", null, (_, _) =>
        {
            if (CanUndoGridEdit(grid)) UndoGridEdit(grid, afterCellChanged, afterGridChanged, afterCellsChanged);
            else undo?.Invoke();
        });
        menu.Items.Add("Redo", null, (_, _) =>
        {
            if (CanRedoGridEdit(grid)) RedoGridEdit(grid, afterCellChanged, afterGridChanged, afterCellsChanged);
            else redo?.Invoke();
        });
        grid.ContextMenuStrip = menu;
    }

    private void CopyGridSelection(DataGridView grid)
    {
        if (grid.SelectedCells.Count == 0)
        {
            SetStatus("No selected cells to copy.");
            return;
        }

        Clipboard.SetText(BuildGridSelectionText(grid));
        SetStatus($"Copied {grid.SelectedCells.Count} cells.");
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
        Action? afterGridChanged = null,
        Action<IReadOnlyList<GridCellKey>>? afterCellsChanged = null)
    {
        if (grid.ReadOnly)
        {
            SetStatus("Current grid is read-only.");
            return;
        }

        if (!Clipboard.ContainsText())
        {
            SetStatus("Clipboard does not contain text.");
            return;
        }

        var start = GetPasteStartCell(grid);
        if (start == null)
        {
            SetStatus("Select a paste start cell first.");
            return;
        }

        if (!grid.EndEdit())
        {
            SetStatus("Current cell edit could not be committed.");
            return;
        }

        var matrix = ParseClipboardMatrix(Clipboard.GetText());
        var edits = new List<GridCellEdit>();
        var skipped = 0;
        var errors = new List<string>();
        for (var r = 0; r < matrix.Count; r++)
        {
            var rowIndex = start.Value.Row + r;
            if (rowIndex >= grid.Rows.Count) break;
            for (var c = 0; c < matrix[r].Count; c++)
            {
                var columnIndex = start.Value.Column + c;
                if (columnIndex >= grid.Columns.Count) break;
                if (TryBuildGridCellEdit(grid, rowIndex, columnIndex, matrix[r][c], out var edit, out var error))
                {
                    edits.Add(edit!);
                }
                else
                {
                    skipped++;
                    if (!string.IsNullOrWhiteSpace(error)) errors.Add(error);
                }
            }
        }

        var result = ApplyGridCellEdits(grid, edits, skipped, errors, recordHistory: true, afterCellChanged, afterGridChanged, afterCellsChanged);
        SetGridBatchStatus("Paste", result);
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
        Action? afterGridChanged = null,
        Action<IReadOnlyList<GridCellKey>>? afterCellsChanged = null)
    {
        if (grid.ReadOnly)
        {
            SetStatus("Current grid is read-only.");
            return;
        }

        if (grid.CurrentCell == null)
        {
            SetStatus("Select a source cell first.");
            return;
        }

        if (!grid.EndEdit())
        {
            SetStatus("Current cell edit could not be committed.");
            return;
        }

        var columnIndex = grid.CurrentCell.ColumnIndex;
        var value = FormatGridValueForBatchInput(grid.CurrentCell.Value);
        var targetCells = GetGridFillTargetCells(grid, columnIndex);
        if (targetCells.Count <= 1)
        {
            SetStatus("Select multiple cells or rows to fill.");
            return;
        }

        var edits = new List<GridCellEdit>();
        var skipped = 0;
        var errors = new List<string>();
        foreach (var cell in targetCells)
        {
            if (TryBuildGridCellEdit(grid, cell.RowIndex, cell.ColumnIndex, value, out var edit, out var error))
            {
                edits.Add(edit!);
            }
            else
            {
                skipped++;
                if (!string.IsNullOrWhiteSpace(error)) errors.Add(error);
            }
        }

        var result = ApplyGridCellEdits(grid, edits, skipped, errors, recordHistory: true, afterCellChanged, afterGridChanged, afterCellsChanged);
        SetGridBatchStatus("Fill", result);
    }

    private void ClearSelectedGridCells(
        DataGridView grid,
        Action<int, int>? afterCellChanged = null,
        Action? afterGridChanged = null,
        Action<IReadOnlyList<GridCellKey>>? afterCellsChanged = null)
    {
        if (grid.ReadOnly)
        {
            SetStatus("Current grid is read-only.");
            return;
        }

        if (!grid.EndEdit())
        {
            SetStatus("Current cell edit could not be committed.");
            return;
        }

        var targets = GetGridBatchTargetCells(grid);
        if (targets.Count == 0)
        {
            SetStatus("No editable cells selected.");
            return;
        }

        var edits = new List<GridCellEdit>();
        var skipped = 0;
        var errors = new List<string>();
        foreach (var cell in targets)
        {
            if (TryBuildGridCellEdit(grid, cell.RowIndex, cell.ColumnIndex, string.Empty, out var edit, out var error))
            {
                edits.Add(edit!);
            }
            else
            {
                skipped++;
                if (!string.IsNullOrWhiteSpace(error)) errors.Add(error);
            }
        }

        var result = ApplyGridCellEdits(grid, edits, skipped, errors, recordHistory: true, afterCellChanged, afterGridChanged, afterCellsChanged);
        SetGridBatchStatus("Clear", result);
    }

    private string FormatGridValueForBatchInput(object? value)
    {
        if (_currentPageHexButton.Checked &&
            IsIntegerObject(value) &&
            TryGetIntegerValue(value, out var parsed))
        {
            return FormatIntegerAsHex(parsed, 0);
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

    private static List<DataGridViewCell> GetGridBatchTargetCells(DataGridView grid)
    {
        var selectedCells = grid.SelectedCells.Cast<DataGridViewCell>()
            .Where(cell => cell.RowIndex >= 0 && cell.ColumnIndex >= 0)
            .OrderBy(cell => cell.RowIndex)
            .ThenBy(cell => cell.ColumnIndex)
            .ToList();
        if (selectedCells.Count > 0)
        {
            return selectedCells;
        }

        var selectedRows = grid.SelectedRows.Cast<DataGridViewRow>()
            .Where(row => row.Index >= 0 && !row.IsNewRow)
            .OrderBy(row => row.Index)
            .SelectMany(row => row.Cells.Cast<DataGridViewCell>())
            .Where(cell => cell.ColumnIndex >= 0)
            .ToList();
        if (selectedRows.Count > 0)
        {
            return selectedRows;
        }

        return grid.CurrentCell is { RowIndex: >= 0, ColumnIndex: >= 0 } current
            ? new List<DataGridViewCell> { current }
            : new List<DataGridViewCell>();
    }

    private void ShowGridBatchModifyDialog(
        DataGridView grid,
        Action<int, int>? afterCellChanged = null,
        Action? afterGridChanged = null,
        Action<IReadOnlyList<GridCellKey>>? afterCellsChanged = null)
    {
        if (grid.ReadOnly)
        {
            SetStatus("Current grid is read-only.");
            return;
        }

        var targets = GetGridBatchTargetCells(grid);
        if (targets.Count == 0)
        {
            SetStatus("Select cells before batch modify.");
            return;
        }

        using var dialog = new Form
        {
            Text = "Batch modify",
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            Width = 420,
            Height = 230,
            Font = Font
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 5,
            ColumnCount = 2,
            Padding = new Padding(12)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        dialog.Controls.Add(root);

        var modeBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        modeBox.Items.AddRange(new object[] { "Fill value", "Find/replace", "Numeric delta" });
        modeBox.SelectedIndex = 0;
        var findBox = new TextBox { Dock = DockStyle.Fill, Enabled = false };
        var valueBox = new TextBox { Dock = DockStyle.Fill };
        var hint = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Text = $"Targets: {targets.Count} cells. Numeric delta uses the current page number base.",
            Padding = new Padding(0, 4, 0, 6)
        };

        root.Controls.Add(new Label { Text = "Mode", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 0);
        root.Controls.Add(modeBox, 1, 0);
        root.Controls.Add(new Label { Text = "Find", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 1);
        root.Controls.Add(findBox, 1, 1);
        root.Controls.Add(new Label { Text = "Value", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 2);
        root.Controls.Add(valueBox, 1, 2);
        root.Controls.Add(hint, 0, 3);
        root.SetColumnSpan(hint, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var okButton = new Button { Text = "Apply", AutoSize = true, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange(new Control[] { cancelButton, okButton });
        root.Controls.Add(buttons, 0, 4);
        root.SetColumnSpan(buttons, 2);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        modeBox.SelectedIndexChanged += (_, _) =>
        {
            var mode = Convert.ToString(modeBox.SelectedItem, CultureInfo.InvariantCulture);
            findBox.Enabled = string.Equals(mode, "Find/replace", StringComparison.Ordinal);
            valueBox.PlaceholderText = mode switch
            {
                "Find/replace" => "Replacement text",
                "Numeric delta" => "Example: 1, -1, 0x10",
                _ => "New value"
            };
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var result = ApplyGridBatchModify(grid, targets, Convert.ToString(modeBox.SelectedItem, CultureInfo.InvariantCulture) ?? "Fill value", findBox.Text, valueBox.Text, afterCellChanged, afterGridChanged, afterCellsChanged);
        SetGridBatchStatus("Batch modify", result);
    }

    private GridBatchEditResult ApplyGridBatchModify(
        DataGridView grid,
        IReadOnlyList<DataGridViewCell> targets,
        string mode,
        string findText,
        string valueText,
        Action<int, int>? afterCellChanged,
        Action? afterGridChanged,
        Action<IReadOnlyList<GridCellKey>>? afterCellsChanged)
    {
        var edits = new List<GridCellEdit>();
        var skipped = 0;
        var errors = new List<string>();

        if (string.Equals(mode, "Find/replace", StringComparison.Ordinal) && string.IsNullOrEmpty(findText))
        {
            return new GridBatchEditResult(0, targets.Count, Array.Empty<GridCellKey>(), new[] { "Find text is empty." });
        }

        ParsedIntegerValue delta = default;
        if (string.Equals(mode, "Numeric delta", StringComparison.Ordinal) &&
            !TryParseIntegerInput(valueText, _currentPageHexButton.Checked, out delta))
        {
            return new GridBatchEditResult(0, targets.Count, Array.Empty<GridCellKey>(), new[] { "Numeric delta is invalid." });
        }

        foreach (var cell in targets)
        {
            if (string.Equals(mode, "Numeric delta", StringComparison.Ordinal))
            {
                if (TryBuildNumericDeltaGridCellEdit(grid, cell.RowIndex, cell.ColumnIndex, delta, out var edit, out var error))
                {
                    edits.Add(edit!);
                }
                else
                {
                    skipped++;
                    if (!string.IsNullOrWhiteSpace(error)) errors.Add(error);
                }
                continue;
            }

            var newText = valueText;
            if (string.Equals(mode, "Find/replace", StringComparison.Ordinal))
            {
                var currentText = Convert.ToString(cell.Value, CultureInfo.InvariantCulture) ?? string.Empty;
                if (!currentText.Contains(findText, StringComparison.Ordinal))
                {
                    skipped++;
                    continue;
                }

                newText = currentText.Replace(findText, valueText, StringComparison.Ordinal);
            }

            if (TryBuildGridCellEdit(grid, cell.RowIndex, cell.ColumnIndex, newText, out var textEdit, out var textError))
            {
                edits.Add(textEdit!);
            }
            else
            {
                skipped++;
                if (!string.IsNullOrWhiteSpace(textError)) errors.Add(textError);
            }
        }

        return ApplyGridCellEdits(grid, edits, skipped, errors, recordHistory: true, afterCellChanged, afterGridChanged, afterCellsChanged);
    }

    private bool TryBuildNumericDeltaGridCellEdit(
        DataGridView grid,
        int rowIndex,
        int columnIndex,
        ParsedIntegerValue delta,
        out GridCellEdit? edit,
        out string error)
    {
        edit = null;
        error = string.Empty;
        if (!TryGetEditableGridCell(grid, rowIndex, columnIndex, out var cell, out error))
        {
            return false;
        }

        if (!TryGetIntegerValue(cell.Value, out var current) ||
            !TryConvertParsedIntegerToType(current, typeof(long), out var currentObject) ||
            !TryConvertParsedIntegerToType(delta, typeof(long), out var deltaObject))
        {
            error = "Cell is not a supported integer value.";
            cell.ErrorText = error;
            return false;
        }

        try
        {
            var next = checked((long)currentObject + (long)deltaObject);
            var targetType = ResolveGridCellTargetType(grid, rowIndex, columnIndex);
            if (!TryConvertParsedIntegerToType(ToParsedIntegerValue(next), targetType, out var converted))
            {
                error = "Numeric delta is outside the target column range.";
                cell.ErrorText = error;
                return false;
            }

            return TryBuildGridCellEditFromValue(grid, rowIndex, columnIndex, converted, out edit, out error);
        }
        catch (OverflowException ex)
        {
            error = ex.Message;
            cell.ErrorText = error;
            return false;
        }
    }

    private static ParsedIntegerValue ToParsedIntegerValue(long value)
        => value < 0
            ? new ParsedIntegerValue(true, ToUnsignedMagnitude(value))
            : new ParsedIntegerValue(false, (ulong)value);

    private bool TrySetGridCellValue(DataGridView grid, int rowIndex, int columnIndex, string text, out string error)
    {
        error = string.Empty;
        if (!TryBuildGridCellEdit(grid, rowIndex, columnIndex, text, out var edit, out error))
        {
            return false;
        }

        return TryApplyGridCellValue(grid, edit!, useNewValue: true, out _, out _, out error);
    }

    private bool TryBuildGridCellEdit(
        DataGridView grid,
        int rowIndex,
        int columnIndex,
        string text,
        out GridCellEdit? edit,
        out string error)
    {
        edit = null;
        error = string.Empty;
        if (!TryGetEditableGridCell(grid, rowIndex, columnIndex, out var cell, out error))
        {
            return false;
        }

        try
        {
            var targetType = ResolveGridCellTargetType(grid, rowIndex, columnIndex);
            var converted = ConvertGridTextValue(text, targetType);
            return TryBuildGridCellEditFromValue(grid, rowIndex, columnIndex, converted, out edit, out error);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            cell.ErrorText = error;
            return false;
        }
    }

    private bool TryBuildGridCellEditFromValue(
        DataGridView grid,
        int rowIndex,
        int columnIndex,
        object? value,
        out GridCellEdit? edit,
        out string error)
    {
        edit = null;
        error = string.Empty;
        if (!TryGetEditableGridCell(grid, rowIndex, columnIndex, out var cell, out error))
        {
            return false;
        }

        var oldValue = cell.Value;
        if (GridValuesEqual(oldValue, value))
        {
            cell.ErrorText = string.Empty;
            return false;
        }

        edit = new GridCellEdit(BuildGridCellKey(grid, rowIndex, columnIndex), oldValue, value);
        return true;
    }

    private static bool TryGetEditableGridCell(
        DataGridView grid,
        int rowIndex,
        int columnIndex,
        out DataGridViewCell cell,
        out string error)
    {
        cell = null!;
        error = string.Empty;
        if (rowIndex < 0 || rowIndex >= grid.Rows.Count || columnIndex < 0 || columnIndex >= grid.Columns.Count)
        {
            error = "Cell is outside the grid.";
            return false;
        }

        var column = grid.Columns[columnIndex];
        cell = grid.Rows[rowIndex].Cells[columnIndex];
        if (grid.Rows[rowIndex].IsNewRow)
        {
            error = "New-row placeholder cannot be edited.";
            return false;
        }

        if (column.ReadOnly || !column.Visible || cell.ReadOnly)
        {
            return false;
        }

        return true;
    }

    private GridBatchEditResult ApplyGridCellEdits(
        DataGridView grid,
        IReadOnlyList<GridCellEdit> edits,
        int skipped,
        IReadOnlyList<string> errors,
        bool recordHistory,
        Action<int, int>? afterCellChanged,
        Action? afterGridChanged,
        Action<IReadOnlyList<GridCellKey>>? afterCellsChanged)
    {
        if (edits.Count == 0)
        {
            return new GridBatchEditResult(0, skipped, Array.Empty<GridCellKey>(), errors);
        }

        var applied = new List<GridCellEdit>();
        var changedCells = new List<GridCellKey>();
        var applyErrors = errors.ToList();
        SuspendGridRepaintPreservingViewport(grid, () =>
        {
            foreach (var edit in edits)
            {
                if (TryApplyGridCellValue(grid, edit, useNewValue: true, out var rowIndex, out var columnIndex, out var error))
                {
                    applied.Add(edit);
                    changedCells.Add(new GridCellKey(edit.Key.RowKey, rowIndex, edit.Key.ColumnName));
                    afterCellChanged?.Invoke(rowIndex, columnIndex);
                }
                else
                {
                    applyErrors.Add(error);
                }
            }
        });

        if (applied.Count > 0 && recordHistory)
        {
            var session = GetGridEditSession(grid);
            session.Viewport = CaptureGridViewport(grid);
            session.Push(applied);
        }
        else if (changedCells.Count > 0)
        {
            GetGridEditSession(grid).MarkRecent(changedCells);
        }

        if (changedCells.Count > 0)
        {
            if (afterCellsChanged != null)
            {
                afterCellsChanged(changedCells);
            }
            else if (afterGridChanged != null)
            {
                afterGridChanged();
            }
            else
            {
                RefreshChangedGridCells(grid, changedCells);
            }
        }

        return new GridBatchEditResult(applied.Count, skipped + (edits.Count - applied.Count), changedCells, applyErrors);
    }

    private bool TryApplyGridCellValue(
        DataGridView grid,
        GridCellEdit edit,
        bool useNewValue,
        out int rowIndex,
        out int columnIndex,
        out string error)
    {
        rowIndex = FindGridRowIndex(grid, edit.Key.RowKey, edit.Key.RowIndex, "ID");
        columnIndex = -1;
        error = string.Empty;
        if (rowIndex < 0 || rowIndex >= grid.Rows.Count)
        {
            error = "Changed row is no longer visible in the grid.";
            return false;
        }

        var column = edit.Key.ColumnName == null ? null : FindGridColumn(grid, edit.Key.ColumnName);
        if (column == null)
        {
            error = "Changed column is no longer visible in the grid.";
            return false;
        }

        columnIndex = column.Index;
        var cell = grid.Rows[rowIndex].Cells[columnIndex];
        try
        {
            cell.Value = useNewValue ? edit.NewValue : edit.OldValue;
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

    private void UndoGridEdit(
        DataGridView grid,
        Action<int, int>? afterCellChanged = null,
        Action? afterGridChanged = null,
        Action<IReadOnlyList<GridCellKey>>? afterCellsChanged = null)
    {
        var session = GetGridEditSession(grid);
        if (session.UndoStack.Count == 0)
        {
            SetStatus("Nothing to undo.");
            return;
        }

        var edits = session.UndoStack.Pop();
        var undoEdits = edits.Select(edit => new GridCellEdit(edit.Key, edit.NewValue, edit.OldValue)).ToList();
        var result = ApplyGridCellEdits(grid, undoEdits, 0, Array.Empty<string>(), recordHistory: false, afterCellChanged, afterGridChanged, afterCellsChanged);
        session.RedoStack.Push(edits);
        SetGridBatchStatus("Undo", result);
    }

    private void RedoGridEdit(
        DataGridView grid,
        Action<int, int>? afterCellChanged = null,
        Action? afterGridChanged = null,
        Action<IReadOnlyList<GridCellKey>>? afterCellsChanged = null)
    {
        var session = GetGridEditSession(grid);
        if (session.RedoStack.Count == 0)
        {
            SetStatus("Nothing to redo.");
            return;
        }

        var edits = session.RedoStack.Pop();
        var result = ApplyGridCellEdits(grid, edits, 0, Array.Empty<string>(), recordHistory: false, afterCellChanged, afterGridChanged, afterCellsChanged);
        session.UndoStack.Push(edits);
        SetGridBatchStatus("Redo", result);
    }

    private GridCellKey BuildGridCellKey(DataGridView grid, int rowIndex, int columnIndex)
        => new(
            rowIndex >= 0 && rowIndex < grid.Rows.Count ? GetGridRowKey(grid.Rows[rowIndex], "ID") : null,
            rowIndex,
            columnIndex >= 0 && columnIndex < grid.Columns.Count ? GetGridColumnName(grid.Columns[columnIndex]) : null);

    private static bool GridValuesEqual(object? left, object? right)
    {
        if (left == null || left == DBNull.Value)
        {
            return right == null || right == DBNull.Value;
        }

        if (right == null || right == DBNull.Value)
        {
            return false;
        }

        if (Equals(left, right))
        {
            return true;
        }

        return string.Equals(
            Convert.ToString(left, CultureInfo.InvariantCulture) ?? string.Empty,
            Convert.ToString(right, CultureInfo.InvariantCulture) ?? string.Empty,
            StringComparison.Ordinal);
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

    private void SetGridBatchStatus(string operation, GridBatchEditResult result)
    {
        var failed = result.Errors.Count;
        var suffix = failed == 0 ? string.Empty : $" Failed: {failed}. First: {result.Errors[0]}";
        SetStatus($"{operation}: changed {result.Changed} cells, skipped {result.Skipped}.{suffix}");
    }

    private void ExportDataTableCsv(DataTable? table, string defaultFileName)
        => ExportDataTableCsv(table, defaultFileName, null);

    private void ExportDataTableCsv(DataTable? table, string defaultFileName, IReadOnlyList<string>? columnNames)
    {
        if (table == null) return;
        using var dialog = new SaveFileDialog
        {
            Title = "Export CSV",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = defaultFileName
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            if (columnNames == null)
            {
                CsvService.Export(table, dialog.FileName);
            }
            else
            {
                CsvService.ExportColumns(table, dialog.FileName, columnNames);
            }
            System.Diagnostics.Debug.WriteLine("CSV exported: " + dialog.FileName);
            SetStatus("CSV export completed.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("CSV export failed: " + ex);
            MessageBox.Show(this, ex.Message, "CSV export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportDataTableCsv(
        DataTable? table,
        string editorName,
        Action? afterImport = null,
        DataGridView? grid = null,
        Action<IReadOnlyList<GridCellKey>>? afterCellsChanged = null)
    {
        if (table == null) return;
        using var dialog = new OpenFileDialog
        {
            Title = "Import CSV",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            CsvImportResult result = null!;
            var changedCells = Array.Empty<GridCellKey>() as IReadOnlyList<GridCellKey>;
            void Import()
            {
                result = CsvService.ImportIntoWithChanges(table, dialog.FileName, allowPartialColumns: true, matchByIdWhenPresent: true);
                changedCells = result.ChangedCells
                    .Select(cell => new GridCellKey(cell.RowKey, cell.RowIndex, cell.ColumnName))
                    .ToList();
            }

            if (grid == null)
            {
                Import();
                if (TryRefreshImportedDataTableCells(table, changedCells))
                {
                    afterImport = null;
                }

                afterImport?.Invoke();
            }
            else
            {
                SuspendGridRepaintPreservingViewport(grid, Import);
                if (afterCellsChanged != null) afterCellsChanged(changedCells);
                else if (changedCells.Count > 0) RefreshChangedGridRowsOnly(grid, changedCells);
                afterImport?.Invoke();
            }

            System.Diagnostics.Debug.WriteLine($"Imported {editorName} CSV: {dialog.FileName}; rows {result.ImportedRows}; cells {changedCells.Count}");
            SetStatus($"{editorName} CSV import completed: rows {result.ImportedRows}, changed cells {changedCells.Count}, skipped read-only {result.SkippedReadOnlyCells}.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"{editorName} CSV import failed: " + ex);
            MessageBox.Show(this, ex.Message, $"{editorName} CSV import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool TryRefreshImportedDataTableCells(DataTable table, IReadOnlyList<GridCellKey> changedCells)
    {
        if (ReferenceEquals(table, _currentRoleEditorData))
        {
            RefreshRoleEditorCellsAfterEdit(changedCells);
            return true;
        }

        if (ReferenceEquals(table, _currentShopEditorData))
        {
            RefreshShopEditorCellsAfterEdit(changedCells);
            return true;
        }

        return false;
    }
}
