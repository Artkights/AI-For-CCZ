using System.Data;
using System.Text;

namespace CCZModStudio.Core;

public static class CsvService
{
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
    {
        var records = ReadRecords(path);
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

        var dataRows = records.Skip(1).Where(record => record.Count > 1 || !string.IsNullOrWhiteSpace(record[0])).ToList();
        if (!allowPartialColumns && dataRows.Count != table.Rows.Count)
        {
            throw new InvalidOperationException($"CSV 数据行数 {dataRows.Count} 与当前表行数 {table.Rows.Count} 不一致。 ");
        }

        var idCsvIndex = headers.FindIndex(header => string.Equals(header, "ID", StringComparison.Ordinal));
        var useId = matchByIdWhenPresent && idCsvIndex >= 0 && table.Columns.Contains("ID");
        var idLookup = useId
            ? table.Rows.Cast<DataRow>().ToDictionary(row => Convert.ToString(row["ID"]) ?? string.Empty, row => row, StringComparer.Ordinal)
            : new Dictionary<string, DataRow>(StringComparer.Ordinal);

        var imported = 0;
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
                if (column.ColumnName == "ID" || column.ReadOnly) continue;
                row[column] = values[csvIndex];
            }

            imported++;
        }

        return imported;
    }

    private static string Escape(string value)
    {
        var mustQuote = value.Contains(',') || value.Contains('"') || value.Contains('\r') || value.Contains('\n');
        value = value.Replace("\"", "\"\"");
        return mustQuote ? $"\"{value}\"" : value;
    }

    private static List<List<string>> ReadRecords(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
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
