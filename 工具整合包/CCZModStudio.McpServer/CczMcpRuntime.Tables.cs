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
            Tables = tables.Select(table =>
            {
                var validation = _tableReader.Validate(project, table);
                return new
                {
                    table.TableName,
                    table.Version,
                    table.FileName,
                    BeginId = table.BeginId,
                    table.RowCount,
                    table.RowSize,
                    DataPosHex = HexDisplayFormatter.FormatOffset(table.DataPos),
                    table.ReadOnly,
                    validation.TableStatus,
                    validation.WriteRisk,
                    table.EvidenceStatus,
                    table.SourceTableName,
                    table.IsGeneratedCompatibilityTable,
                    table.IsEvidenceReadOnlyTable,
                    Fields = table.Fields.Select(field => new
                    {
                        field.ColumnName,
                        Kind = field.Kind.ToString(),
                        field.Size,
                        field.ConsumesBytes,
                        field.VisibleByDefault
                    })
                };
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
            source = fallback ? Ccz66RevisedLayout.CrossVersionFallbackTableStatus : Ccz66RevisedLayout.ExactOrCompatibleTableStatus,
            generated66Tables = tables.Count(table => table.Version.Equals("6.6", StringComparison.OrdinalIgnoreCase) && table.IsGeneratedCompatibilityTable),
            readOnlyEvidenceTables = tables.Count(table => table.IsEvidenceReadOnlyTable),
            warning = fallback
                ? "6.6 engine is using a non-6.6 HexTable.xml. Writes are blocked by default; MCP table writes require write_mode=CrossVersionFallbackWrite and reports will be marked risky."
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
            Oracle = BuildImageAssignerOracleStatusPayload(project, table),
            Validation = new
            {
                result.Validation.IsUsable,
                result.Validation.CanWrite,
                result.Validation.FilePath,
                result.Validation.FileExists,
                result.Validation.FileLength,
                result.Validation.TableStatus,
                result.Validation.WriteRisk,
                result.Validation.IsNative66,
                result.Validation.IsCrossVersionFallback,
                result.Validation.IsReadOnlyEvidenceOnly,
                result.Validation.SemanticValidationStatus,
                result.Validation.HiddenTailPolicy,
                result.Validation.EffectResolutionSource,
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
        if (result.Validation.IsReadOnlyEvidenceOnly)
        {
            throw new InvalidOperationException("The selected table is read-only/evidence-only and cannot be written: " + result.Validation.TableStatus);
        }
        if (result.Validation.IsCrossVersionFallback && !IsCrossVersionFallbackWriteMode(writeMode))
        {
            throw new InvalidOperationException("The selected 6.6 table resolved through CrossVersionFallback. Re-run with write_mode=CrossVersionFallbackWrite to explicitly accept the non-native 6.6 layout risk.");
        }
        if (!result.Validation.CanWrite && !result.Validation.IsCrossVersionFallback)
        {
            throw new InvalidOperationException("The selected table cannot be written: " + result.Validation.TableStatus);
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
            save.TableStatus,
            save.WriteRisk,
            save.WriteMode,
            table.TableName,
            Oracle = BuildImageAssignerOracleStatusPayload(project, table)
        };
    }

    private static bool IsCrossVersionFallbackWriteMode(string? writeMode)
        => string.Equals(writeMode, "CrossVersionFallbackWrite", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(writeMode, "cross_version_fallback_write", StringComparison.OrdinalIgnoreCase);

}
