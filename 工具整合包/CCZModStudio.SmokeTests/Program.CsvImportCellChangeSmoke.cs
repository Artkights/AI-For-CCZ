using CCZModStudio.Core;
using System.Data;
using System.Text;

internal partial class Program
{
    static void RunCsvImportCellChangeSmoke()
    {
        var smokeDir = Path.Combine(Path.GetTempPath(), "CCZModStudio_CsvImportCellChangeSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(smokeDir);

        var table = new DataTable("CsvImportCellChangeSmoke");
        table.Columns.Add("ID", typeof(int));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Power", typeof(byte));
        table.Columns.Add("Note", typeof(string));
        table.Columns.Add("ReadOnlyFlag", typeof(string));
        table.Columns["ReadOnlyFlag"]!.ReadOnly = true;

        table.Rows.Add(0, "Alpha", (byte)10, "same", "locked-0");
        table.Rows.Add(1, "Beta", (byte)20, "same", "locked-1");
        table.Rows.Add(2, "Gamma", (byte)30, "same", "locked-2");
        table.AcceptChanges();

        var csvPath = Path.Combine(smokeDir, "partial-by-id.csv");
        File.WriteAllText(
            csvPath,
            "ID,Name,Power,Note,ReadOnlyFlag\r\n" +
            "字段说明,名称,数值,说明,只读\r\n" +
            "0,Alpha,10,same,changed-readonly\r\n" +
            "1,Beta Prime,25,same,changed-readonly\r\n" +
            "2,Gamma,30,changed note,changed-readonly\r\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var result = CsvService.ImportIntoWithChanges(table, csvPath, allowPartialColumns: true, matchByIdWhenPresent: true);
        if (result.ImportedRows != 3)
        {
            throw new InvalidOperationException($"Expected 3 imported rows, got {result.ImportedRows}.");
        }

        if (result.SkippedReadOnlyCells != 6)
        {
            throw new InvalidOperationException($"Expected 6 skipped read-only cells for ID+ReadOnlyFlag, got {result.SkippedReadOnlyCells}.");
        }

        var changed = result.ChangedCells
            .Select(cell => $"{cell.RowKey}:{cell.RowIndex}:{cell.ColumnName}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
        var expected = new[]
        {
            "1:1:Name",
            "1:1:Power",
            "2:2:Note"
        };
        if (!changed.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Unexpected changed cells: " + string.Join(", ", changed));
        }

        if (Convert.ToString(table.Rows[0]["Name"]) != "Alpha" ||
            table.Rows[0]["Power"] is not byte row0Power || row0Power != 10 ||
            Convert.ToString(table.Rows[0]["ReadOnlyFlag"]) != "locked-0")
        {
            throw new InvalidOperationException("Unchanged/read-only cells were modified unexpectedly.");
        }

        if (Convert.ToString(table.Rows[1]["Name"]) != "Beta Prime" ||
            table.Rows[1]["Power"] is not byte row1Power || row1Power != 25 ||
            Convert.ToString(table.Rows[2]["Note"]) != "changed note")
        {
            throw new InvalidOperationException("Changed cells were not imported correctly.");
        }

        Console.WriteLine($"CSV_IMPORT_CELL_CHANGE_SMOKE_OK path={smokeDir}");
    }
}
