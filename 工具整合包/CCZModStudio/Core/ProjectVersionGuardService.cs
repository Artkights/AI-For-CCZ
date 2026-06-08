using System.Globalization;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 6.X 读取 / 写入兼容报告。
/// 历史版本曾在这里按 6.5 表版本和核心文件尺寸阻止写入；当前写入保护已取消，
/// 这里仅保留体检报告和尺寸参考，不再拦截保存。
/// </summary>
public static class ProjectVersionGuardService
{
    public const long CurrentObservedHexzmapSampleSize = 44_840;
    public const long Expected65HexzmapGuardSize = 45_254;

    public static readonly IReadOnlyDictionary<string, long> Expected65CoreSizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
    {
        ["Ekd5.exe"] = 1_196_032,
        ["Data.e5"] = 61_379,
        ["Imsg.e5"] = 450_000,
        ["Star.e5"] = 6_359,
        ["Hexzmap.e5"] = Expected65HexzmapGuardSize
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
        var severity = enabled6x.Count == 0
            ? "Error"
            : missing.Count > 0 || mismatched.Count > 0 || enabledOtherVersions.Count > 0
                ? "Warn"
                : "Info";
        var status = severity switch
        {
            "Info" => "6.5 尺寸参考匹配",
            "Warn" => "尺寸/版本仅提示；写入不再拦截",
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
                Category = "版本/偏移提示",
                Name = "6.X 表与核心尺寸参考",
                Status = status,
                Detail = string.Join("；", detailParts),
                Path = project.GameRoot
            },
            new()
            {
                Severity = "Info",
                Category = "版本/偏移提示",
                Name = "写入规则",
                Status = "写入保护已取消",
                Detail = "读取侧按当前 HexTable.xml 中的 6.X 表定义工作；写入侧不再因为 Version 不是 6.5、核心文件尺寸不匹配或表 ReadOnly 标记而拒绝保存。仍会保留文件存在、偏移范围、字段类型、字段长度、路径归属和自动备份等基础校验。",
                Path = project.GameRoot
            }
        };

        return items;
    }

    public static void EnsureTableCompatibleForWrite(CczProject project, HexTableDefinition table)
    {
        _ = project;
        _ = table;
    }

    public static void EnsureCoreFileCompatibleForWrite(CczProject project, string fileName)
    {
        _ = project;
        _ = fileName;
    }

    private sealed record CoreState(string FileName, long ExpectedSize, bool Exists, long ActualSize, bool SizeMatches65, string Path);
}
