using System.Globalization;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 6.X 读取 / 6.5 写入安全护栏。
/// 读取侧允许加载 HexTable.xml 中声明的 6.X 表；写入侧仍只开放已验证的 6.5 表和 6.5 基准核心尺寸，
/// 避免把 6.5 偏移写到 6.6x 或其他改版文件。
/// </summary>
public static class ProjectVersionGuardService
{
    public static readonly IReadOnlyDictionary<string, long> Expected65CoreSizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
    {
        ["Ekd5.exe"] = 1_196_032,
        ["Data.e5"] = 61_379,
        ["Imsg.e5"] = 450_000,
        ["Star.e5"] = 6_359,
        ["Hexzmap.e5"] = 45_254
    };

    public static IReadOnlyList<ProjectAuditItem> Analyze(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var enabled6x = tables.Where(t => t.Enabled && HexTableNameResolver.Is6XVersion(t.Version)).ToList();
        var enabled65Count = enabled6x.Count(t => t.Version == "6.5");
        var enabledOtherVersions = enabled6x
            .Where(t => !string.Equals(t.Version, "6.5", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Version)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var coreStates = Expected65CoreSizes
            .Select(pair =>
            {
                var path = project.ResolveGameFile(pair.Key);
                var exists = File.Exists(path);
                var actual = exists ? new FileInfo(path).Length : -1;
                var ok = exists && actual == pair.Value;
                return new CoreState(pair.Key, pair.Value, exists, actual, ok, path);
            })
            .ToList();

        var missing = coreStates.Where(x => !x.Exists).ToList();
        var mismatched = coreStates.Where(x => x.Exists && !x.SizeMatches65).ToList();
        var severity = missing.Count > 0 || enabled6x.Count == 0
            ? "Error"
            : mismatched.Count > 0 || enabledOtherVersions.Count > 0
                ? "Warn"
                : "Info";
        var status = severity switch
        {
            "Info" => "6.5 读写基准通过",
            "Warn" => "6.X 可读；非 6.5 写入受保护",
            _ => "核心文件不完整或缺少 6.X 表定义"
        };

        var detailParts = new List<string>
        {
            $"启用 6.X 表 {enabled6x.Count} 个，其中 6.5 表 {enabled65Count} 个",
            enabledOtherVersions.Count == 0
                ? "未启用非 6.5 表"
                : "启用非 6.5 表：" + string.Join("、", enabledOtherVersions),
            "核心尺寸：" + string.Join("；", coreStates.Select(x => x.Exists
                ? $"{x.FileName}={x.ActualSize.ToString(CultureInfo.InvariantCulture)}/{x.ExpectedSize.ToString(CultureInfo.InvariantCulture)}{(x.SizeMatches65 ? "(匹配)" : "(不匹配)")}"
                : $"{x.FileName}=缺失"))
        };

        var items = new List<ProjectAuditItem>
        {
            new()
            {
                Severity = severity,
                Category = "版本/偏移护栏",
                Name = "6.5/6.6x 防混用",
                Status = status,
                Detail = string.Join("；", detailParts),
                Path = project.GameRoot
            },
            new()
            {
                Severity = "Info",
                Category = "版本/偏移护栏",
                Name = "写入规则",
                Status = "6.X 读取；6.5 写入",
                Detail = "读取侧按当前 HexTable.xml 中的 6.X 表定义工作；写入侧只允许 Version=6.5 且 Ekd5.exe/Data.e5/Imsg.e5/Star.e5/Hexzmap.e5 尺寸匹配 6.5 基准的目标。尺寸不符或表版本不是 6.5 时拒绝写入。补丁、资源替换、SV 短文本等高风险写入仍按各自模块规则控制。",
                Path = project.GameRoot
            }
        };

        return items;
    }

    public static void EnsureTableCompatibleForWrite(CczProject project, HexTableDefinition table)
    {
        if (!string.Equals(table.Version, "6.5", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"6.5/6.6x 偏移防混用：当前只允许写入 Version=6.5 的表，表“{table.TableName}”标记为 {table.Version}。");
        }

        EnsureCoreFileCompatibleForWrite(project, table.FileName);
    }

    public static void EnsureCoreFileCompatibleForWrite(CczProject project, string fileName)
    {
        var coreName = Path.GetFileName(fileName);
        if (!Expected65CoreSizes.TryGetValue(coreName, out var expectedSize))
        {
            return;
        }

        var path = project.ResolveGameFile(coreName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"6.5/6.6x 偏移防混用：目标核心文件不存在，禁止写入 {coreName}。", path);
        }

        var actualSize = new FileInfo(path).Length;
        if (actualSize != expectedSize)
        {
            throw new InvalidOperationException(
                $"6.5/6.6x 偏移防混用：{coreName} 尺寸为 {actualSize} 字节，6.5 基准应为 {expectedSize} 字节。为避免按错误偏移写坏 6.6x/其他改版文件，已拒绝写入。");
        }
    }

    private sealed record CoreState(string FileName, long ExpectedSize, bool Exists, long ActualSize, bool SizeMatches65, string Path);
}
