using System.Data;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.McpServer;

public sealed partial class CczMcpRuntime
{
    public object ListTables(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var engine = _engineProfileService.Detect(project);
        return new
        {
            project.GameRoot,
            project.HexTableXmlPath,
            Engine = engine,
            TableDiagnostics = BuildTableDiagnostics(engine, tables),
            Count = tables.Count,
            Tables = tables.Select(table => new
            {
                table.TableName,
                table.Version,
                table.FileName,
                BeginId = table.BeginId,
                table.RowCount,
                table.RowSize,
                DataPosHex = HexDisplayFormatter.FormatOffset(table.DataPos),
                table.ReadOnly,
                Fields = table.Fields.Select(field => new
                {
                    field.ColumnName,
                    Kind = field.Kind.ToString(),
                    field.Size,
                    field.ConsumesBytes
                })
            })
        };
    }

    private static object BuildTableDiagnostics(CczEngineProfile engine, IReadOnlyList<HexTableDefinition> tables)
    {
        var actualPrefixes = tables
            .Where(table => HexTableNameResolver.Is6XTable(table))
            .Select(table => table.Version)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(version => version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var fallback = Ccz66RevisedLayout.Is66(engine) &&
                       !actualPrefixes.Contains("6.6", StringComparer.OrdinalIgnoreCase);

        return new
        {
            requestedPrefix = engine.TableVersionPrefix,
            actualPrefixes,
            tableFallback = fallback,
            source = fallback ? "CrossVersionFallback" : "ExactOrCompatible",
            warning = fallback
                ? "6.6 engine is using a non-6.6 HexTable.xml. Writes are not blocked; verify offsets and row definitions before saving."
                : string.Empty
        };
    }

    public object ReadTable(string? gameRoot, string tableName, List<int>? rowIds, List<string>? columns, string? keyword, int limit)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var table = FindTable(project, tables, tableName);
        var result = _tableReader.Read(project, table, tables);
        var selectedColumns = ResolveColumns(result.Data, columns, includeId: true);
        var rowIdSet = rowIds is { Count: > 0 } ? rowIds.ToHashSet() : null;
        var effectiveLimit = NormalizeLimit(limit, 50, 500);

        var rows = result.Data.AsEnumerable()
            .Where(row => rowIdSet == null || rowIdSet.Contains(Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture)))
            .Where(row => MatchesKeyword(row, selectedColumns, keyword))
            .Take(effectiveLimit)
            .Select(row => RowToDictionary(row, selectedColumns))
            .ToList();

        return new
        {
            table.TableName,
            table.FileName,
            Validation = new
            {
                result.Validation.IsUsable,
                result.Validation.FilePath,
                result.Validation.FileExists,
                result.Validation.FileLength,
                result.Validation.Warnings
            },
            TotalRows = result.Data.Rows.Count,
            ReturnedRows = rows.Count,
            Columns = selectedColumns.Select(x => x.ColumnName),
            Rows = rows
        };
    }

    public object WriteTableRows(string? gameRoot, string tableName, List<TableRowUpdate> updates, string? writeMode)
    {
        if (updates.Count == 0) throw new InvalidOperationException("updates must contain at least one row update.");

        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var tables = LoadTables(project);
        var table = FindTable(project, tables, tableName);

        var result = _tableReader.Read(project, table, tables);
        if (!result.Validation.IsUsable)
        {
            throw new InvalidOperationException("The selected table is not usable for writing.");
        }

        foreach (var update in updates)
        {
            var row = result.Data.AsEnumerable()
                .FirstOrDefault(x => Convert.ToInt32(x["ID"], CultureInfo.InvariantCulture) == update.RowId)
                ?? throw new InvalidOperationException($"Row ID {update.RowId} was not found in table {table.TableName}.");

            foreach (var (columnName, value) in update.Values)
            {
                if (columnName.Equals("ID", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("ID is synthetic and cannot be written.");
                }

                var column = FindColumn(result.Data, columnName);
                var field = column.ExtendedProperties["FieldDefinition"] as HexFieldDefinition;
                if (field == null || !field.ConsumesBytes)
                {
                    throw new InvalidOperationException($"Column {columnName} is derived or non-writable.");
                }

                row[column] = ConvertJsonValue(value);
            }
        }

        _shopEditorService.EnsureShopDataTableValidForSave(
            project,
            tables,
            table,
            result.Data,
            changedItemSlotsOnly: true);

        var save = _tableWriter.Save(project, table, result.Data);
        return new
        {
            save.FilePath,
            save.BackupPath,
            save.ReportJsonPath,
            save.RowsWritten,
            save.ChangedBytes,
            table.TableName
        };
    }
}
