using System.Data;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class HexTableReader
{
    private readonly Dictionary<string, DataTable> _indexCache = new(StringComparer.Ordinal);

    public HexTableValidationResult Validate(CczProject project, HexTableDefinition table)
    {
        var warnings = new List<string>();
        var filePath = project.ResolveGameFile(table.FileName);
        var info = new FileInfo(filePath);
        var columnsMatchBytes = table.Columns.Count == table.ByteSizes.Count;
        if (!columnsMatchBytes)
        {
            warnings.Add($"列数量 {table.Columns.Count} 与 Bytes 数量 {table.ByteSizes.Count} 不一致。");
        }

        var padding = table.RowSize - table.PositiveBytesSum;
        if (padding < 0)
        {
            warnings.Add($"字段字节合计 {table.PositiveBytesSum} 大于行长 {table.RowSize}。");
        }
        else if (padding > 0)
        {
            warnings.Add($"每行存在 {padding} 字节未命名/保留区。保存已拆分字段时会保留这些原始字节。 ");
        }

        var fits = info.Exists && info.Length >= table.EndOffsetExclusive;
        if (info.Exists && !fits)
        {
            warnings.Add($"文件长度 {info.Length} 小于表结束偏移 {table.EndOffsetExclusive}。");
        }

        return new HexTableValidationResult
        {
            Table = table,
            FilePath = filePath,
            FileExists = info.Exists,
            FileLength = info.Exists ? info.Length : 0,
            ColumnsMatchBytes = columnsMatchBytes,
            FitsInFile = fits,
            PaddingBytes = Math.Max(0, padding),
            Warnings = warnings
        };
    }

    public TableReadResult Read(CczProject project, HexTableDefinition table, IReadOnlyList<HexTableDefinition> allTables)
    {
        var validation = Validate(project, table);
        var data = CreateDataTable(table);

        if (!validation.IsUsable)
        {
            return new TableReadResult { Table = table, Data = data, Validation = validation };
        }

        if (ItemEffectNameReader.IsItemEffectNameTable(table))
        {
            ReadItemEffectNameTable(project, table, data);
            return new TableReadResult { Table = table, Data = data, Validation = validation };
        }

        if (JobEffectNameReader.IsJobEffectNameTable(table))
        {
            data = new JobEffectNameReader().ReadTable(project, table);
            return new TableReadResult { Table = table, Data = data, Validation = validation };
        }

        var filePath = project.ResolveGameFile(table.FileName);
        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream);

        DataTable? indexTable = null;
        if (table.Fields.Any(f => f.Kind == HexFieldKind.Derived) && !string.IsNullOrWhiteSpace(table.IndexTable))
        {
            indexTable = TryLoadIndexTable(project, table.IndexTable, allTables);
        }

        for (var rowIndex = 0; rowIndex < table.RowCount; rowIndex++)
        {
            var row = data.NewRow();
            var id = table.BeginId + rowIndex;
            row["ID"] = id;

            stream.Position = table.DataPos + ((long)rowIndex * table.RowSize);
            foreach (var field in table.Fields)
            {
                if (field.Kind == HexFieldKind.Derived)
                {
                    row[field.ColumnName] = ResolveDerivedName(indexTable, rowIndex, id);
                    continue;
                }

                var bytes = reader.ReadBytes(field.Size);
                row[field.ColumnName] = DecodeField(field, bytes);
            }

            data.Rows.Add(row);
        }

        data.AcceptChanges();

        return new TableReadResult { Table = table, Data = data, Validation = validation };
    }

    private static void ReadItemEffectNameTable(CczProject project, HexTableDefinition table, DataTable data)
    {
        var names = new ItemEffectNameReader().ReadNames(project, table);
        var row = data.NewRow();
        row["ID"] = table.BeginId;
        foreach (DataColumn column in data.Columns)
        {
            if (column.ColumnName == "ID") continue;
            if (TryParseLeadingHexByte(column.ColumnName, out var id) &&
                names.TryGetValue(id, out var name))
            {
                row[column.ColumnName] = name;
            }
            else
            {
                row[column.ColumnName] = string.Empty;
            }
        }

        data.Rows.Add(row);
        data.AcceptChanges();
    }

    private DataTable? TryLoadIndexTable(CczProject project, string indexTableName, IReadOnlyList<HexTableDefinition> allTables)
    {
        var cacheKey = project.GameRoot + "\0" + indexTableName;
        if (_indexCache.TryGetValue(cacheKey, out var cached)) return cached;

        if (!HexTableNameResolver.TryResolveForProject(project, allTables, indexTableName, out var indexDefinition)) return null;

        var validation = Validate(project, indexDefinition);
        if (!validation.IsUsable) return null;

        if (JobEffectNameReader.IsJobEffectNameTable(indexDefinition))
        {
            var jobEffectNames = new JobEffectNameReader().ReadTable(project, indexDefinition);
            _indexCache[cacheKey] = jobEffectNames;
            return jobEffectNames;
        }

        var result = Read(project, indexDefinition, allTables);
        _indexCache[cacheKey] = result.Data;
        return result.Data;
    }

    private static object ResolveDerivedName(DataTable? indexTable, int rowIndex, int id)
    {
        if (indexTable == null || rowIndex < 0 || rowIndex >= indexTable.Rows.Count) return $"#{id}";
        if (indexTable.Columns.Contains("名称")) return indexTable.Rows[rowIndex]["名称"];
        if (indexTable.Columns.Count > 1) return indexTable.Rows[rowIndex][1];
        return $"#{id}";
    }

    private static bool TryParseLeadingHexByte(string columnName, out int id)
    {
        var token = new string(columnName.TakeWhile(Uri.IsHexDigit).ToArray());
        return int.TryParse(token, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out id);
    }

    private static DataTable CreateDataTable(HexTableDefinition table)
    {
        var data = new DataTable(table.TableName);
        data.Columns.Add("ID", typeof(int));
        foreach (var field in table.Fields)
        {
            var columnName = field.ColumnName;
            if (data.Columns.Contains(columnName))
            {
                columnName = $"{field.ColumnName}_{data.Columns.Count}";
                data.Columns.Add(columnName, typeof(string));
            }
            else
            {
                data.Columns.Add(columnName, typeof(object));
            }

            data.Columns[columnName]!.ExtendedProperties["FieldDefinition"] = field;
        }
        return data;
    }

    private static object DecodeField(HexFieldDefinition field, byte[] bytes)
    {
        return field.Kind switch
        {
            HexFieldKind.UInt8 => bytes.Length >= 1 ? bytes[0] : 0,
            HexFieldKind.UInt16 => bytes.Length >= 2 ? BitConverter.ToUInt16(bytes, 0) : 0,
            HexFieldKind.UInt32 => bytes.Length >= 4 ? BitConverter.ToUInt32(bytes, 0) : 0,
            HexFieldKind.FixedString => EncodingService.DecodeFixedString(bytes),
            HexFieldKind.RawBytes => BitConverter.ToString(bytes).Replace("-", " "),
            _ => string.Empty
        };
    }
}
