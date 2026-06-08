using System.Data;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class JobEffectNameReader
{
    public static bool IsJobEffectNameTable(HexTableDefinition table)
    {
        var key = HexTableNameResolver.BuildSemanticKey(table.TableName).TrimStart('-', ' ');
        return key == "7 兵种特效" &&
               table.Fields.Count == 1 &&
               table.Fields[0].ColumnName == "名称" &&
               table.Fields[0].Size == 0;
    }

    public IReadOnlyDictionary<int, string> ReadNames(CczProject project, HexTableDefinition table)
    {
        var path = project.ResolveGameFile(table.FileName);
        if (!File.Exists(path)) return new Dictionary<int, string>();

        var bytes = File.ReadAllBytes(path);
        var names = new Dictionary<int, string>();
        for (var rowIndex = 0; rowIndex < table.RowCount; rowIndex++)
        {
            var offset = checked((int)(table.DataPos + ((long)rowIndex * table.RowSize)));
            if (offset < 0 || offset >= bytes.Length) break;

            var length = Math.Min(table.RowSize, bytes.Length - offset);
            var textLength = 0;
            while (textLength < length && bytes[offset + textLength] != 0x00)
            {
                textLength++;
            }

            var id = table.BeginId + rowIndex;
            var text = textLength > 0
                ? Sanitize(EncodingService.Gbk.GetString(bytes, offset, textLength))
                : string.Empty;
            names[id] = string.IsNullOrWhiteSpace(text) ? $"#{id}" : text;
        }

        return names;
    }

    public DataTable ReadTable(CczProject project, HexTableDefinition table)
    {
        var data = new DataTable(table.TableName);
        data.Columns.Add("ID", typeof(int));
        data.Columns.Add("名称", typeof(string));

        var names = ReadNames(project, table);
        for (var rowIndex = 0; rowIndex < table.RowCount; rowIndex++)
        {
            var id = table.BeginId + rowIndex;
            var row = data.NewRow();
            row["ID"] = id;
            row["名称"] = names.TryGetValue(id, out var name) ? name : $"#{id}";
            data.Rows.Add(row);
        }

        data.AcceptChanges();
        return data;
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return new string(value.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
    }
}
