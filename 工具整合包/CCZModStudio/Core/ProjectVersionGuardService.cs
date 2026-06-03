using System.Globalization;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 6.5 偏移安全护栏。
/// 本工具当前所有可写表结构均来自 CczRSX 6.5 的 HexTable.xml，
/// 因此写入前必须确认目标核心文件仍符合 6.5 基准尺寸，避免把 6.6x 或其他改版文件按 6.5 偏移写坏。
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
        var enabled65Count = tables.Count(t => t.Enabled && t.Version == "6.5");
        var enabledOtherVersions = tables
            .Where(t => t.Enabled && !string.Equals(t.Version, "6.5", StringComparison.OrdinalIgnoreCase))
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
        var severity = missing.Count > 0 || mismatched.Count > 0 || enabled65Count == 0 ? "Error" : "Info";
        var status = severity == "Info"
            ? enabledOtherVersions.Count > 0
                ? "6.5 基准通过；非 6.5 表已禁止写入"
                : "6.5 偏移护栏通过"
            : "疑似非 6.5 或核心文件不完整";

        var detailParts = new List<string>
        {
            $"启用 6.5 表 {enabled65Count} 个",
            enabledOtherVersions.Count == 0
                ? "未启用其他版本表"
                : "启用其他版本表：" + string.Join("、", enabledOtherVersions),
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
                Status = "当前项目可写 + 自动备份 + 6.5 基准核心文件",
                Detail = "已确认的数据表和 R/S 联动允许直接保存当前 MOD 项目，写入前自动备份；对 Ekd5.exe/Data.e5/Imsg.e5/Star.e5/Hexzmap.e5 还会检查 6.5 基准尺寸，尺寸不符时拒绝写入。补丁、资源替换、SV 短文本等高风险写入仍按各自模块规则控制。",
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
