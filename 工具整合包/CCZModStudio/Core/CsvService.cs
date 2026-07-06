using System.Data;
using System.Globalization;
using System.Text;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed record CsvChangedCell(string? RowKey, int RowIndex, string ColumnName);

public sealed record CsvImportResult(
    int ImportedRows,
    IReadOnlyList<CsvChangedCell> ChangedCells,
    int SkippedReadOnlyCells);

public static class CsvService
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static void Export(DataTable table, string path)
        => ExportColumns(table, path, table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList());

    public static void ExportColumns(DataTable table, string path, IReadOnlyList<string> columnNames)
        => ExportColumnsCore(table, path, columnNames, annotationRows: null, rows: null);

    public static void ExportColumnsWithAnnotationRow(DataTable table, string path, IReadOnlyList<string> columnNames, IReadOnlyDictionary<string, string> annotations)
        => ExportColumnsCore(table, path, columnNames, annotations, rows: null);

    public static void ExportColumnsRows(DataTable table, string path, IReadOnlyList<string> columnNames, IReadOnlyList<DataRow> rows)
        => ExportColumnsCore(table, path, columnNames, annotationRows: null, rows);

    public static void ExportColumnsRowsWithAnnotationRow(DataTable table, string path, IReadOnlyList<string> columnNames, IReadOnlyDictionary<string, string> annotations, IReadOnlyList<DataRow> rows)
        => ExportColumnsCore(table, path, columnNames, annotations, rows);

    private static void ExportColumnsCore(DataTable table, string path, IReadOnlyList<string> columnNames, IReadOnlyDictionary<string, string>? annotationRows, IReadOnlyList<DataRow>? rows)
    {
        if (columnNames.Count == 0)
        {
            throw new InvalidOperationException("没有可导出的列。");
        }

        foreach (var columnName in columnNames)
        {
            if (!table.Columns.Contains(columnName))
            {
                throw new InvalidOperationException($"当前表不存在列：{columnName}");
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", columnNames.Select(Escape)));
        if (annotationRows != null)
        {
            builder.AppendLine(string.Join(",", columnNames.Select(c =>
                Escape(annotationRows.TryGetValue(c, out var annotation) ? annotation : string.Empty))));
        }
        foreach (DataRow row in rows ?? table.Rows.Cast<DataRow>())
        {
            var values = columnNames.Select(c => Escape(Convert.ToString(row[c]) ?? string.Empty));
            builder.AppendLine(string.Join(",", values));
        }
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static void ImportInto(DataTable table, string path)
    {
        ImportInto(table, path, allowPartialColumns: false, matchByIdWhenPresent: false);
    }

    public static int ImportInto(DataTable table, string path, bool allowPartialColumns, bool matchByIdWhenPresent)
        => ImportIntoWithChanges(table, path, allowPartialColumns, matchByIdWhenPresent).ImportedRows;

    public static CsvImportResult ImportIntoWithChanges(DataTable table, string path, bool allowPartialColumns, bool matchByIdWhenPresent)
        => ImportIntoWithChanges(table, path, allowPartialColumns, matchByIdWhenPresent, canImportColumn: null);

    public static CsvImportResult ImportIntoWithChanges(
        DataTable table,
        string path,
        bool allowPartialColumns,
        bool matchByIdWhenPresent,
        Func<string, bool>? canImportColumn)
    {
        var records = ReadRecords(path, table);
        if (records.Count == 0) throw new InvalidOperationException("CSV 文件为空。 ");

        var headers = records[0];
        if (headers.Count == 0) throw new InvalidOperationException("CSV 表头为空。 ");

        var columnMap = new List<(int CsvIndex, DataColumn Column)>();
        if (!allowPartialColumns)
        {
            if (headers.Count != table.Columns.Count)
            {
                throw new InvalidOperationException($"CSV 列数 {headers.Count} 与当前表列数 {table.Columns.Count} 不一致。 ");
            }

            for (var i = 0; i < headers.Count; i++)
            {
                if (!string.Equals(headers[i], table.Columns[i].ColumnName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"第 {i + 1} 列不匹配：CSV={headers[i]}，当前表={table.Columns[i].ColumnName}。 ");
                }

                columnMap.Add((i, table.Columns[i]));
            }
        }
        else
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < headers.Count; i++)
            {
                var header = headers[i];
                if (!table.Columns.Contains(header))
                {
                    throw new InvalidOperationException($"CSV 第 {i + 1} 列不存在于当前表：{header}。 ");
                }

                if (!seen.Add(header))
                {
                    throw new InvalidOperationException($"CSV 存在重复列：{header}。 ");
                }

                columnMap.Add((i, table.Columns[header]!));
            }
        }

        var idCsvIndex = headers.FindIndex(header => string.Equals(header, "ID", StringComparison.Ordinal));
        var useId = matchByIdWhenPresent && idCsvIndex >= 0 && table.Columns.Contains("ID");
        var idLookup = useId
            ? table.Rows.Cast<DataRow>().ToDictionaryLastByKey(row => Convert.ToString(row["ID"]) ?? string.Empty, row => row, StringComparer.Ordinal)
            : new Dictionary<string, DataRow>(StringComparer.Ordinal);

        var dataRows = records.Skip(1).Where(HasAnyCsvValue).ToList();
        if (dataRows.Count > 0 && IsLikelyAnnotationRow(dataRows[0], headers, table, idCsvIndex, useId, idLookup))
        {
            dataRows.RemoveAt(0);
        }

        if (!allowPartialColumns && dataRows.Count != table.Rows.Count)
        {
            throw new InvalidOperationException($"CSV 数据行数 {dataRows.Count} 与当前表行数 {table.Rows.Count} 不一致。 ");
        }

        var seenIds = useId ? new HashSet<string>(StringComparer.Ordinal) : null;
        var imported = 0;
        var changedCells = new List<CsvChangedCell>();
        var skippedReadOnlyCells = 0;
        for (var r = 0; r < dataRows.Count; r++)
        {
            var values = dataRows[r];
            if (values.Count != headers.Count)
            {
                throw new InvalidOperationException($"CSV 第 {r + 2} 行列数不一致。 ");
            }

            DataRow row;
            if (useId)
            {
                var id = values[idCsvIndex];
                if (!seenIds!.Add(id))
                {
                    throw new InvalidOperationException($"CSV 第 {r + 2} 行 ID={id} 重复。 ");
                }

                if (!idLookup.TryGetValue(id, out row!))
                {
                    throw new InvalidOperationException($"CSV 第 {r + 2} 行 ID={id} 在当前表中不存在。 ");
                }
            }
            else
            {
                if (r >= table.Rows.Count)
                {
                    throw new InvalidOperationException($"CSV 第 {r + 2} 行超出当前表行数。 ");
                }

                row = table.Rows[r];
            }

            foreach (var (csvIndex, column) in columnMap)
            {
                if (column.ColumnName == "ID" || column.ReadOnly || canImportColumn?.Invoke(column.ColumnName) == false)
                {
                    skippedReadOnlyCells++;
                    continue;
                }

                try
                {
                    var converted = ConvertCsvValue(values[csvIndex], column, row);
                    if (CsvValuesEqual(row[column], converted))
                    {
                        continue;
                    }

                    row[column] = converted;
                    changedCells.Add(new CsvChangedCell(
                        useId ? Convert.ToString(row["ID"], CultureInfo.InvariantCulture) : null,
                        table.Rows.IndexOf(row),
                        column.ColumnName));
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"CSV 第 {r + 2} 行列 {column.ColumnName} 的值无法转换为当前表类型：{values[csvIndex]}。{ex.Message}", ex);
                }
            }

            imported++;
        }

        return new CsvImportResult(imported, changedCells, skippedReadOnlyCells);
    }

    private static bool CsvValuesEqual(object? left, object? right)
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

    private static bool HasAnyCsvValue(IReadOnlyList<string> record)
        => record.Any(value => !string.IsNullOrWhiteSpace(value));

    private static bool IsLikelyAnnotationRow(
        IReadOnlyList<string> record,
        IReadOnlyList<string> headers,
        DataTable table,
        int idCsvIndex,
        bool useId,
        IReadOnlyDictionary<string, DataRow> idLookup)
    {
        if (record.Count != headers.Count || !LooksLikeAnnotationText(record))
        {
            return false;
        }

        if (useId && idCsvIndex >= 0 && idCsvIndex < record.Count)
        {
            var idValue = record[idCsvIndex];
            if (idLookup.ContainsKey(idValue))
            {
                return false;
            }

            var idColumn = table.Columns["ID"]!;
            var sampleRow = table.Rows.Count > 0 ? table.Rows[0] : table.NewRow();
            return !CanConvertCsvValue(idValue, idColumn, sampleRow);
        }

        for (var i = 0; i < record.Count; i++)
        {
            var header = headers[i];
            if (!table.Columns.Contains(header)) continue;
            var column = table.Columns[header]!;
            if (column.ColumnName == "ID" || column.ReadOnly) continue;
            var sampleRow = table.Rows.Count > 0 ? table.Rows[0] : table.NewRow();
            if (!CanConvertCsvValue(record[i], column, sampleRow)) return true;
        }

        return false;
    }

    private static bool LooksLikeAnnotationText(IReadOnlyList<string> record)
    {
        var text = string.Join('\n', record);
        return text.Contains("字段", StringComparison.Ordinal) ||
               text.Contains("说明", StringComparison.Ordinal) ||
               text.Contains("语义", StringComparison.Ordinal) ||
               text.Contains("风险", StringComparison.Ordinal) ||
               text.Contains("编号", StringComparison.Ordinal) ||
               text.Contains("用于", StringComparison.Ordinal) ||
               text.Contains("来自", StringComparison.Ordinal) ||
               text.Contains("引用", StringComparison.Ordinal) ||
               text.Contains("资源", StringComparison.Ordinal) ||
               text.Contains("annotation", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("note", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanConvertCsvValue(string value, DataColumn column, DataRow row)
    {
        try
        {
            _ = ConvertCsvValue(value, column, row);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static object ConvertCsvValue(string value, DataColumn column, DataRow row)
    {
        var targetType = ResolveCsvValueTargetType(column, row);
        targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (targetType == typeof(object) || targetType == typeof(string))
        {
            return value;
        }

        if (string.IsNullOrEmpty(value) && column.AllowDBNull)
        {
            return DBNull.Value;
        }

        if (targetType == typeof(bool))
        {
            if (bool.TryParse(value, out var boolValue)) return boolValue;
            if (value == "1") return true;
            if (value == "0") return false;
        }

        if (targetType.IsEnum)
        {
            return Enum.Parse(targetType, value, ignoreCase: true);
        }

        if (TryConvertHexCsvValue(value, targetType, out var hexValue))
        {
            return hexValue;
        }

        return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    private static bool TryConvertHexCsvValue(string value, Type targetType, out object converted)
    {
        converted = null!;
        var text = value.Trim();
        if (!text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return false;
        var hex = text[2..].Replace("_", string.Empty, StringComparison.Ordinal);
        if (hex.Length == 0 || !ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        try
        {
            converted = targetType switch
            {
                Type t when t == typeof(byte) && parsed <= byte.MaxValue => (byte)parsed,
                Type t when t == typeof(ushort) && parsed <= ushort.MaxValue => (ushort)parsed,
                Type t when t == typeof(uint) && parsed <= uint.MaxValue => (uint)parsed,
                Type t when t == typeof(ulong) => parsed,
                Type t when t == typeof(sbyte) && parsed <= (ulong)sbyte.MaxValue => (sbyte)parsed,
                Type t when t == typeof(short) && parsed <= (ulong)short.MaxValue => (short)parsed,
                Type t when t == typeof(int) && parsed <= int.MaxValue => (int)parsed,
                Type t when t == typeof(long) && parsed <= long.MaxValue => (long)parsed,
                _ => null!
            };
            return converted != null;
        }
        catch (OverflowException)
        {
            converted = null!;
            return false;
        }
    }

    private static Type ResolveCsvValueTargetType(DataColumn column, DataRow row)
    {
        if (column.ExtendedProperties["FieldDefinition"] is HexFieldDefinition field)
        {
            return field.Kind switch
            {
                HexFieldKind.UInt8 => typeof(byte),
                HexFieldKind.UInt16 => typeof(ushort),
                HexFieldKind.UInt32 => typeof(uint),
                HexFieldKind.FixedString or HexFieldKind.RawBytes or HexFieldKind.Derived => typeof(string),
                _ => typeof(string)
            };
        }

        var targetType = column.DataType;
        if (targetType == typeof(object))
        {
            var current = row[column];
            if (current != null && current != DBNull.Value)
            {
                targetType = current.GetType();
            }
        }

        return targetType;
    }

    private static string Escape(string value)
    {
        var mustQuote = value.Contains(',') || value.Contains('"') || value.Contains('\r') || value.Contains('\n');
        value = value.Replace("\"", "\"\"");
        return mustQuote ? $"\"{value}\"" : value;
    }

    private static List<List<string>> ReadRecords(string path, DataTable? table = null)
    {
        var text = ReadTextWithEncodingFallback(path, table);
        return ParseRecords(text);
    }

    private static string ReadTextWithEncodingFallback(string path, DataTable? table)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }

            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }
        }

        try
        {
            var utf8Text = StrictUtf8.GetString(bytes);
            if (table == null)
            {
                return utf8Text;
            }

            var gbkText = EncodingService.Gbk.GetString(bytes);
            return HeaderMatchScore(ParseRecords(utf8Text), table) >= HeaderMatchScore(ParseRecords(gbkText), table)
                ? utf8Text
                : gbkText;
        }
        catch (DecoderFallbackException)
        {
            return EncodingService.Gbk.GetString(bytes);
        }
    }

    private static int HeaderMatchScore(IReadOnlyList<List<string>> records, DataTable table)
    {
        if (records.Count == 0) return -1;
        return records[0].Count(header => table.Columns.Contains(header));
    }

    private static List<List<string>> ParseRecords(string text)
    {
        var records = new List<List<string>>();
        var current = new StringBuilder();
        var record = new List<string>();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(ch);
                }
                continue;
            }

            if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == ',')
            {
                record.Add(current.ToString());
                current.Clear();
            }
            else if (ch == '\r' || ch == '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                record.Add(current.ToString());
                current.Clear();
                records.Add(record);
                record = new List<string>();
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0 || record.Count > 0)
        {
            record.Add(current.ToString());
            records.Add(record);
        }

        return records;
    }

    public static List<string> ParseLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(ch);
                }
            }
            else
            {
                if (ch == ',')
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else if (ch == '"')
                {
                    inQuotes = true;
                }
                else
                {
                    current.Append(ch);
                }
            }
        }

        result.Add(current.ToString());
        return result;
    }
}
