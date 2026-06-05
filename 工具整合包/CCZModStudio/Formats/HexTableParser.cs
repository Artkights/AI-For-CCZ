using System.Globalization;
using System.Xml.Linq;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class HexTableParser
{
    public IReadOnlyList<HexTableDefinition> Load(string xmlPath)
    {
        if (!File.Exists(xmlPath))
        {
            throw new FileNotFoundException("找不到 HexTable.xml。", xmlPath);
        }

        var document = XDocument.Load(xmlPath);
        var tables = document.Root?.Elements("HexTable") ?? Enumerable.Empty<XElement>();
        return tables.Select(ParseTable).OrderBy(t => t.Id).ToList();
    }

    private static HexTableDefinition ParseTable(XElement element)
    {
        var tableName = Get(element, "TableName");
        var columns = SplitCsv(Get(element, "Columns"));
        var rowSize = GetInt(element, "RowSize");
        var bytes = SplitCsv(Get(element, "Bytes")).Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToList();
        bytes = TryInferSingleRowStringTableBytes(columns, bytes, rowSize);

        var fields = new List<HexFieldDefinition>();
        var max = Math.Max(columns.Count, bytes.Count);
        for (var i = 0; i < max; i++)
        {
            var column = i < columns.Count ? columns[i] : $"字段{i + 1}";
            var size = i < bytes.Count ? bytes[i] : 0;
            fields.Add(new HexFieldDefinition
            {
                ColumnName = column,
                Size = size,
                Kind = DetermineKind(tableName, column, size)
            });
        }

        return new HexTableDefinition
        {
            Id = GetInt(element, "ID"),
            Enabled = GetBool(element, "Enable"),
            TableName = tableName,
            FileName = Get(element, "FileName"),
            DataPos = GetLong(element, "DataPos"),
            RowCount = GetInt(element, "RowCount"),
            RowSize = rowSize,
            Columns = columns,
            ByteSizes = bytes,
            IndexTable = Get(element, "IndexTable"),
            BeginId = GetInt(element, "BeginID"),
            OnMem = GetBool(element, "OnMem"),
            ReadOnly = GetBool(element, "ReadOnly"),
            Version = DetectVersion(tableName),
            Fields = fields
        };
    }

    private static HexFieldKind DetermineKind(string tableName, string columnName, int size)
    {
        if (size < 0) return HexFieldKind.Derived;
        if (size == 1) return HexFieldKind.UInt8;
        if (size == 2) return HexFieldKind.UInt16;
        if (size == 4) return HexFieldKind.UInt32;

        if (IsStringLike(tableName, columnName, size))
        {
            return HexFieldKind.FixedString;
        }

        return HexFieldKind.RawBytes;
    }

    private static bool IsStringLike(string tableName, string columnName, int size)
    {
        if (size <= 0) return false;
        if (columnName.Contains("名称") || columnName.Contains("介绍") || columnName.Contains("台词")) return true;
        if (tableName.Contains("名称") || tableName.Contains("说明") || tableName.Contains("列传") || tableName.Contains("台词")) return true;
        if (size >= 5) return true;
        return false;
    }

    private static string DetectVersion(string tableName)
        => HexTableNameResolver.DetectVersion(tableName);

    private static List<int> TryInferSingleRowStringTableBytes(List<string> columns, List<int> bytes, int rowSize)
    {
        if (bytes.Count != 1 || columns.Count <= 1) return bytes;

        var defaultSize = bytes[0];
        var inferred = new List<int>(columns.Count);
        foreach (var column in columns)
        {
            inferred.Add(TryParseAnnotatedStringSize(column, defaultSize));
        }

        var inferredSize = inferred.Sum();
        return inferredSize <= rowSize ? inferred : bytes;
    }

    private static int TryParseAnnotatedStringSize(string column, int defaultSize)
    {
        var start = column.IndexOf('（');
        var end = column.IndexOf('）');
        if (start < 0 || end <= start) return defaultSize;

        var numberText = column.Substring(start + 1, end - start - 1);
        if (!double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var logicalLength))
        {
            return defaultSize;
        }

        // CczRSX 部分“名称整段表”在列名里写的是可显示中文长度，
        // 二进制中通常是 GBK 字节长度 + 1 个结束/填充字节。
        return checked((int)(logicalLength * 2 + 1));
    }

    private static List<string> SplitCsv(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? new List<string>()
            : value.Split(',').Select(x => x.Trim()).ToList();

    private static string Get(XElement element, string name) => element.Element(name)?.Value.Trim() ?? string.Empty;
    private static int GetInt(XElement element, string name) => int.Parse(Get(element, name), CultureInfo.InvariantCulture);
    private static long GetLong(XElement element, string name) => long.Parse(Get(element, name), CultureInfo.InvariantCulture);
    private static bool GetBool(XElement element, string name) => bool.TryParse(Get(element, name), out var value) && value;
}
