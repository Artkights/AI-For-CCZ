using System.Globalization;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ItemEffectNameReader
{
    public IReadOnlyDictionary<int, string> ReadBaseNames(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var result = new Dictionary<int, string>();
        var profile = new CczEngineProfileService().Detect(project);
        foreach (var tableName in new[] { profile.TableHints.ItemEffectNameLowTable, profile.TableHints.ItemEffectNameHighTable })
        {
            if (!HexTableNameResolver.TryResolveForProject(project, tables, tableName, out var table)) continue;

            foreach (var pair in ReadNames(project, table))
            {
                result[pair.Key] = pair.Value;
            }
        }

        return result;
    }

    public static bool IsItemEffectNameTable(HexTableDefinition table)
    {
        var key = HexTableNameResolver.BuildRangeAgnosticSemanticKey(table.TableName).TrimStart('-', ' ');
        return key is "1-2 装备特效名称" or "1-3 装备特效名称";
    }

    public IReadOnlyDictionary<int, string> ReadNames(CczProject project, HexTableDefinition table)
    {
        var ids = table.Columns
            .Select(TryParseLeadingHexByte)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();
        if (ids.Count == 0) return new Dictionary<int, string>();

        var path = project.ResolveGameFile(table.FileName);
        var bytes = File.ReadAllBytes(path);
        if (table.DataPos < 0 || table.DataPos >= bytes.Length) return new Dictionary<int, string>();

        var length = checked((int)Math.Min(table.RowSize, bytes.Length - table.DataPos));
        var names = SplitNonEmptyNullSeparatedGbk(bytes, checked((int)table.DataPos), length);
        var count = Math.Min(ids.Count, names.Count);
        var result = new Dictionary<int, string>();
        for (var i = 0; i < count; i++)
        {
            var name = Sanitize(names[i]);
            if (!string.IsNullOrWhiteSpace(name))
            {
                result[ids[i]] = name;
            }
        }

        return result;
    }

    private static List<string> SplitNonEmptyNullSeparatedGbk(byte[] bytes, int offset, int length)
    {
        var result = new List<string>();
        var start = offset;
        var end = offset + length;
        for (var i = offset; i <= end; i++)
        {
            if (i != end && bytes[i] != 0x00) continue;
            if (i > start)
            {
                result.Add(EncodingService.Gbk.GetString(bytes, start, i - start));
            }

            start = i + 1;
        }

        return result;
    }

    private static int? TryParseLeadingHexByte(string value)
    {
        var token = new string(value.TakeWhile(Uri.IsHexDigit).ToArray());
        return token.Length == 0
            ? null
            : int.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id)
                ? id
                : null;
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return new string(value.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
    }
}
