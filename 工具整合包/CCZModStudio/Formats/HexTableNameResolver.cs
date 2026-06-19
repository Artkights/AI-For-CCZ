using System.Text.RegularExpressions;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public static partial class HexTableNameResolver
{
    public static string DetectVersion(string tableName)
    {
        var match = VersionPrefixRegex().Match(tableName.Trim());
        return match.Success ? match.Groups["version"].Value : "未知";
    }

    public static bool Is6XVersion(string version)
        => version.StartsWith("6.", StringComparison.OrdinalIgnoreCase);

    public static bool Is6XTable(HexTableDefinition table)
        => Is6XVersion(table.Version);

    public static HexTableDefinition Resolve(IReadOnlyList<HexTableDefinition> tables, string tableName)
        => TryResolve(tables, tableName, out var table)
            ? table
            : throw new InvalidOperationException($"未找到数据表：{tableName}。若目标是 6.6/其他 6.X，请优先提供对应版本的 HexTable.xml；当前缺表时可临时使用内置 6.5 表只读兜底，但该兜底不保证覆盖新版新增表，也不能作为写入依据。");

    public static HexTableDefinition ResolveForProject(CczProject project, IReadOnlyList<HexTableDefinition> tables, string tableName)
        => TryResolveForProject(project, tables, tableName, out var table)
            ? table
            : throw new InvalidOperationException($"未找到数据表：{tableName}。当前引擎识别后也没有匹配到等价 6.X 表，请检查 HexTable.xml 是否属于该项目。");

    public static bool TryResolveForProject(CczProject project, IReadOnlyList<HexTableDefinition> tables, string tableName, out HexTableDefinition table)
    {
        var profile = new CczEngineProfileService().Detect(project);
        var preferredName = BuildVersionedTableName(profile.TableVersionPrefix, tableName);

        table = tables.FirstOrDefault(item => item.TableName.Equals(preferredName, StringComparison.OrdinalIgnoreCase))!;
        if (table != null) return true;

        var semanticKey = BuildSemanticKey(tableName);
        table = tables
            .Where(item => Is6XTable(item))
            .Where(item => item.Version.Equals(profile.TableVersionPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(item => BuildSemanticKey(item.TableName).Equals(semanticKey, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Enabled)
            .ThenBy(item => item.Id)
            .FirstOrDefault()!;
        if (table != null) return true;

        var rangeAgnosticKey = BuildRangeAgnosticSemanticKey(tableName);
        table = tables
            .Where(item => Is6XTable(item))
            .Where(item => item.Version.Equals(profile.TableVersionPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(item => BuildRangeAgnosticSemanticKey(item.TableName).Equals(rangeAgnosticKey, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Enabled)
            .ThenBy(item => item.Id)
            .FirstOrDefault()!;
        if (table != null) return true;

        if (profile.TableVersionPrefix.Equals("6.6", StringComparison.OrdinalIgnoreCase) &&
            TryResolveCrossVersion(tables, tableName, out table))
        {
            return true;
        }

        return TryResolve(tables, tableName, out table);
    }

    public static bool TryResolve(IReadOnlyList<HexTableDefinition> tables, string tableName, out HexTableDefinition table)
    {
        table = tables.FirstOrDefault(item => item.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase))!;
        if (table != null) return true;

        var semanticKey = BuildSemanticKey(tableName);
        table = tables
            .Where(item => Is6XTable(item))
            .Where(item => BuildSemanticKey(item.TableName).Equals(semanticKey, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Enabled)
            .ThenByDescending(item => ParseVersionRank(item.Version))
            .ThenBy(item => item.Id)
            .FirstOrDefault()!;
        if (table != null) return true;

        var rangeAgnosticKey = BuildRangeAgnosticSemanticKey(tableName);
        table = tables
            .Where(item => Is6XTable(item))
            .Where(item => BuildRangeAgnosticSemanticKey(item.TableName).Equals(rangeAgnosticKey, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Enabled)
            .ThenByDescending(item => ParseVersionRank(item.Version))
            .ThenBy(item => item.Id)
            .FirstOrDefault()!;
        if (table != null) return true;

        table = tables
            .Where(item => item.TableName.Contains(tableName, StringComparison.OrdinalIgnoreCase) ||
                           tableName.Contains(item.TableName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Enabled)
            .ThenByDescending(item => Is6XTable(item))
            .ThenByDescending(item => ParseVersionRank(item.Version))
            .ThenBy(item => item.Id)
            .FirstOrDefault()!;
        return table != null;
    }

    private static bool TryResolveCrossVersion(IReadOnlyList<HexTableDefinition> tables, string tableName, out HexTableDefinition table)
    {
        var semanticKey = BuildSemanticKey(tableName);
        table = tables
            .Where(Is6XTable)
            .Where(item => !item.Version.Equals("6.6", StringComparison.OrdinalIgnoreCase))
            .Where(item => BuildSemanticKey(item.TableName).Equals(semanticKey, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Enabled)
            .ThenByDescending(item => ParseVersionRank(item.Version))
            .ThenBy(item => item.Id)
            .FirstOrDefault()!;
        if (table != null) return true;

        var rangeAgnosticKey = BuildRangeAgnosticSemanticKey(tableName);
        table = tables
            .Where(Is6XTable)
            .Where(item => !item.Version.Equals("6.6", StringComparison.OrdinalIgnoreCase))
            .Where(item => BuildRangeAgnosticSemanticKey(item.TableName).Equals(rangeAgnosticKey, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Enabled)
            .ThenByDescending(item => ParseVersionRank(item.Version))
            .ThenBy(item => item.Id)
            .FirstOrDefault()!;
        return table != null;
    }

    public static IReadOnlyList<HexTableDefinition> ResolveItemTables(IReadOnlyList<HexTableDefinition> tables)
    {
        var itemTables = tables
            .Where(Is6XTable)
            .Where(table =>
            {
                var name = StripVersionPrefix(table.TableName);
                return name.Contains("物品", StringComparison.Ordinal) &&
                       !name.Contains("说明", StringComparison.Ordinal) &&
                       !name.Contains("获取", StringComparison.Ordinal) &&
                       !name.Contains("特效", StringComparison.Ordinal);
            })
            .OrderBy(table => table.BeginId)
            .ThenBy(table => table.Id)
            .ToList();

        if (itemTables.Count > 0) return itemTables;

        var result = new List<HexTableDefinition>();
        foreach (var name in new[] { "6.5-1 物品（0-103）", "6.5-2 物品（104-255）" })
        {
            if (TryResolve(tables, name, out var table) && !result.Contains(table))
            {
                result.Add(table);
            }
        }

        return result;
    }

    public static IReadOnlyList<HexTableDefinition> ResolveItemTables(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var profile = new CczEngineProfileService().Detect(project);
        var result = new List<HexTableDefinition>();
        foreach (var tableName in new[] { profile.TableHints.ItemLowTable, profile.TableHints.ItemHighTable })
        {
            if (TryResolveForProject(project, tables, tableName, out var table) && !result.Contains(table))
            {
                result.Add(table);
            }
        }

        if (result.Count > 0) return result;

        result.AddRange(tables
            .Where(Is6XTable)
            .Where(table => table.Version.Equals(profile.TableVersionPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(table =>
            {
                var name = StripVersionPrefix(table.TableName);
                return name.Contains("物品", StringComparison.Ordinal) &&
                       !name.Contains("说明", StringComparison.Ordinal) &&
                       !name.Contains("获取", StringComparison.Ordinal) &&
                       !name.Contains("特效", StringComparison.Ordinal);
            })
            .OrderBy(table => table.BeginId)
            .ThenBy(table => table.Id));

        return result.Count > 0 ? result : ResolveItemTables(tables);
    }

    public static string StripVersionPrefix(string tableName)
    {
        var match = VersionPrefixRegex().Match(tableName.Trim());
        return match.Success ? match.Groups["suffix"].Value.Trim() : tableName.Trim();
    }

    public static string BuildVersionedTableName(string versionPrefix, string tableName)
    {
        var suffix = StripVersionPrefix(tableName).TrimStart('-', ' ');
        return string.IsNullOrWhiteSpace(suffix) ? versionPrefix : $"{versionPrefix}-{suffix}";
    }

    public static string BuildSemanticKey(string tableName)
        => NormalizeWhitespace(StripVersionPrefix(tableName));

    public static string BuildRangeAgnosticSemanticKey(string tableName)
    {
        var key = FullWidthParenthesizedNumberRangeRegex().Replace(BuildSemanticKey(tableName), string.Empty);
        key = AsciiParenthesizedNumberRangeRegex().Replace(key, string.Empty);
        return NormalizeWhitespace(key);
    }

    private static int ParseVersionRank(string version)
    {
        var text = version.StartsWith("6.", StringComparison.OrdinalIgnoreCase) ? version[2..] : version;
        var digits = new string(text.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var rank) ? rank : 0;
    }

    private static string NormalizeWhitespace(string value)
        => WhitespaceRegex().Replace(value.Trim(), " ");

    [GeneratedRegex(@"^(?<version>6\.[0-9A-Za-z]+)(?<suffix>(?:[-\s].*)?)$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPrefixRegex();

    [GeneratedRegex(@"（\s*[0-9A-Fa-f]+\s*[-~－—]\s*[0-9A-Fa-f]+\s*）", RegexOptions.CultureInvariant)]
    private static partial Regex FullWidthParenthesizedNumberRangeRegex();

    [GeneratedRegex(@"\(\s*[0-9A-Fa-f]+\s*[-~]\s*[0-9A-Fa-f]+\s*\)", RegexOptions.CultureInvariant)]
    private static partial Regex AsciiParenthesizedNumberRangeRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
