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
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length == 0) throw new InvalidOperationException("CSV 文件为空。 ");

        var headers = ParseLine(lines[0]);
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
        }

        var dataLines = lines.Length - 1;
        if (dataLines != table.Rows.Count)
        {
            throw new InvalidOperationException($"CSV 数据行数 {dataLines} 与当前表行数 {table.Rows.Count} 不一致。 ");
        }

        for (var r = 0; r < dataLines; r++)
        {
            var values = ParseLine(lines[r + 1]);
            if (values.Count != table.Columns.Count)
            {
                throw new InvalidOperationException($"CSV 第 {r + 2} 行列数不一致。 ");
            }

            for (var c = 1; c < table.Columns.Count; c++) // skip synthetic ID
            {
                table.Rows[r][c] = values[c];
            }
        }
    }

    private static string Escape(string value)
    {
        var mustQuote = value.Contains(',') || value.Contains('"') || value.Contains('\r') || value.Contains('\n');
        value = value.Replace("\"", "\"\"");
        return mustQuote ? $"\"{value}\"" : value;
    }

    private static List<string> ParseLine(string line)
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
