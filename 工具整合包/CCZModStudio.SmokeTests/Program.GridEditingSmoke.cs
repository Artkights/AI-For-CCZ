using CCZModStudio;
using System.Collections;
using System.Data;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;

internal partial class Program
{
    static void RunGridEditingSmoke()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                using var form = new MainForm();
                form.Show();

                var grid = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    AllowUserToAddRows = false,
                    AutoGenerateColumns = true
                };
                form.Controls.Add(grid);
                grid.BringToFront();

                var table = new DataTable("GridEditingSmoke");
                table.Columns.Add("ID", typeof(int));
                table.Columns.Add("Name", typeof(string));
                table.Columns.Add("Power", typeof(byte));
                table.Columns.Add("Locked", typeof(string));
                table.Columns.Add("Hidden", typeof(string));
                table.Rows.Add(1, "Alpha", (byte)10, "locked-1", "hidden-1");
                table.Rows.Add(2, "Beta", (byte)20, "locked-2", "hidden-2");
                table.Rows.Add(3, "Gamma", (byte)30, "locked-3", "hidden-3");
                table.AcceptChanges();

                grid.DataSource = table;
                Application.DoEvents();
                grid.Columns["ID"]!.ReadOnly = true;
                grid.Columns["Locked"]!.ReadOnly = true;
                grid.Columns["Hidden"]!.Visible = false;
                grid.CurrentCell = grid.Rows[0].Cells["Name"];

                var fillTargets = new List<DataGridViewCell>
                {
                    grid.Rows[0].Cells["Name"],
                    grid.Rows[1].Cells["Name"],
                    grid.Rows[0].Cells["ID"],
                    grid.Rows[0].Cells["Locked"],
                    grid.Rows[0].Cells["Hidden"]
                };
                var fill = ApplyGridBatchModifyForSmoke(form, grid, fillTargets, "Fill value", string.Empty, "Delta");
                AssertGridBatchForSmoke(fill, changed: 2, skipped: 3, "fill");
                AssertCellForSmoke(table, 0, "Name", "Delta");
                AssertCellForSmoke(table, 1, "Name", "Delta");
                AssertCellForSmoke(table, 0, "Locked", "locked-1");
                AssertCellForSmoke(table, 0, "Hidden", "hidden-1");

                InvokeGridEditHistoryForSmoke(form, "UndoGridEdit", grid);
                AssertCellForSmoke(table, 0, "Name", "Alpha");
                AssertCellForSmoke(table, 1, "Name", "Beta");

                InvokeGridEditHistoryForSmoke(form, "RedoGridEdit", grid);
                AssertCellForSmoke(table, 0, "Name", "Delta");
                AssertCellForSmoke(table, 1, "Name", "Delta");

                var nameTargets = new List<DataGridViewCell>
                {
                    grid.Rows[0].Cells["Name"],
                    grid.Rows[1].Cells["Name"],
                    grid.Rows[2].Cells["Name"]
                };
                var replace = ApplyGridBatchModifyForSmoke(form, grid, nameTargets, "Find/replace", "ta", "TA");
                AssertGridBatchForSmoke(replace, changed: 2, skipped: 1, "find/replace");
                AssertCellForSmoke(table, 0, "Name", "DelTA");
                AssertCellForSmoke(table, 1, "Name", "DelTA");
                AssertCellForSmoke(table, 2, "Name", "Gamma");

                GetPrivateFieldForGridSmoke<RadioButton>(form, "_currentPageHexButton").Checked = true;
                var numericTargets = new List<DataGridViewCell>
                {
                    grid.Rows[0].Cells["Power"],
                    grid.Rows[1].Cells["Power"],
                    grid.Rows[0].Cells["Name"],
                    grid.Rows[0].Cells["ID"]
                };
                var numeric = ApplyGridBatchModifyForSmoke(form, grid, numericTargets, "Numeric delta", string.Empty, "0x05");
                AssertGridBatchForSmoke(numeric, changed: 2, skipped: 2, "numeric delta");
                AssertCellForSmoke(table, 0, "Power", (byte)15);
                AssertCellForSmoke(table, 1, "Power", (byte)25);

                InvokeGridEditHistoryForSmoke(form, "UndoGridEdit", grid);
                AssertCellForSmoke(table, 0, "Power", (byte)10);
                AssertCellForSmoke(table, 1, "Power", (byte)20);

                InvokeGridEditHistoryForSmoke(form, "RedoGridEdit", grid);
                AssertCellForSmoke(table, 0, "Power", (byte)15);
                AssertCellForSmoke(table, 1, "Power", (byte)25);

                form.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
        {
            throw new InvalidOperationException("Grid editing smoke failed.", failure);
        }

        Console.WriteLine("GRID_EDITING_SMOKE_OK");
    }

    private static object ApplyGridBatchModifyForSmoke(
        MainForm form,
        DataGridView grid,
        IReadOnlyList<DataGridViewCell> targets,
        string mode,
        string findText,
        string valueText)
    {
        var method = typeof(MainForm).GetMethod("ApplyGridBatchModify", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing MainForm.ApplyGridBatchModify.");
        try
        {
            return method.Invoke(form, new object?[] { grid, targets, mode, findText, valueText, null, null, null })
                   ?? throw new InvalidOperationException("ApplyGridBatchModify returned null.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw ex.InnerException;
        }
    }

    private static void InvokeGridEditHistoryForSmoke(MainForm form, string methodName, DataGridView grid)
    {
        var method = typeof(MainForm).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing MainForm.{methodName}.");
        try
        {
            method.Invoke(form, new object?[] { grid, null, null, null });
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw ex.InnerException;
        }
    }

    private static void AssertGridBatchForSmoke(object result, int changed, int skipped, string label)
    {
        var type = result.GetType();
        var actualChanged = (int)(type.GetProperty("Changed")?.GetValue(result)
            ?? throw new InvalidOperationException("GridBatchEditResult.Changed not found."));
        var actualSkipped = (int)(type.GetProperty("Skipped")?.GetValue(result)
            ?? throw new InvalidOperationException("GridBatchEditResult.Skipped not found."));
        var changedCells = (IEnumerable)(type.GetProperty("ChangedCells")?.GetValue(result)
            ?? throw new InvalidOperationException("GridBatchEditResult.ChangedCells not found."));
        var changedCellCount = changedCells.Cast<object>().Count();

        if (actualChanged != changed || actualSkipped != skipped || changedCellCount != changed)
        {
            throw new InvalidOperationException(
                $"{label} batch mismatch: changed={actualChanged}, skipped={actualSkipped}, cells={changedCellCount}; expected changed={changed}, skipped={skipped}.");
        }
    }

    private static T GetPrivateFieldForGridSmoke<T>(MainForm form, string fieldName)
    {
        var field = typeof(MainForm).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing MainForm.{fieldName}.");
        return (T)field.GetValue(form)!;
    }

    private static void AssertCellForSmoke(DataTable table, int rowIndex, string columnName, object expected)
    {
        var actual = table.Rows[rowIndex][columnName];
        if (!Equals(actual, expected))
        {
            throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Unexpected {0}[{1}].{2}: actual={3} expected={4}",
                    table.TableName,
                    rowIndex,
                    columnName,
                    actual,
                    expected));
        }
    }
}
