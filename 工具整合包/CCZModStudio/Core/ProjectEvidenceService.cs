using System.Globalization;
using System.Text;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 汇总 CCZModStudio 生成的报告、导出表和预览 PNG，供制作向导展示“最近报告/发布证据”。
/// 该服务只扫描工具产物目录，不读取或修改游戏核心文件。
/// </summary>
public sealed class ProjectEvidenceService
{
    private static readonly string[] EvidenceExtensions = { ".md", ".txt", ".json", ".csv", ".png" };

    public IReadOnlyList<ProjectEvidenceItem> Scan(CczProject? project, int maxItems = 120)
    {
        if (project == null)
        {
            return Array.Empty<ProjectEvidenceItem>();
        }

        var roots = BuildEvidenceRoots(project);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ProjectEvidenceItem>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root.Path))
            {
                continue;
            }

            foreach (var file in EnumerateEvidenceFiles(root))
            {
                var fullPath = Path.GetFullPath(file);
                if (!seen.Add(fullPath))
                {
                    continue;
                }

                var info = new FileInfo(fullPath);
                if (!info.Exists)
                {
                    continue;
                }

                result.Add(BuildItem(info, root.Label));
            }
        }

        return result
            .OrderByDescending(item => EvidenceKindRank(item.Kind))
            .ThenByDescending(item => item.LastWriteTime)
            .ThenBy(item => item.FileName, StringComparer.CurrentCultureIgnoreCase)
            .Take(maxItems)
            .ToList();
    }

    public ProjectEvidenceItem? FindLatestImportant(CczProject? project)
    {
        return Scan(project)
            .OrderByDescending(item => EvidenceKindRank(item.Kind))
            .ThenByDescending(item => item.LastWriteTime)
            .FirstOrDefault();
    }

    public string BuildSummary(CczProject? project, IReadOnlyList<ProjectEvidenceItem> items)
    {
        if (project == null)
        {
            return "最近报告/发布证据：尚未加载项目。打开项目后会扫描 CCZModStudio_Reports、CCZModStudio_Exports、测试副本报告和备份写入报告。";
        }

        if (items.Count == 0)
        {
            return "最近报告/发布证据：当前未发现工具生成的报告、导出表或预览 PNG。建议先运行项目体检、资源诊断、关卡地图联动报告、R/S eex 命令引用清单或发布前综合报告。";
        }

        var builder = new StringBuilder();
        var latest = items.OrderByDescending(item => item.LastWriteTime).First();
        var grouped = items
            .GroupBy(item => item.Kind)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Take(8)
            .Select(group => $"{group.Key}:{group.Count()}");
        builder.AppendLine($"最近报告/发布证据：{items.Count} 项。最新：{latest.Kind} / {latest.FileName} / {latest.LastWriteTimeText}。");
        builder.AppendLine("类型分布：" + string.Join("，", grouped));
        builder.Append("安全边界：这些文件是 CCZModStudio 的报告、导出物或预览图，用于发布前复核、沟通和留证；它们不参与游戏运行，也不等同于已确认可写入的内部格式。");
        return builder.ToString();
    }

    private static IReadOnlyList<EvidenceRoot> BuildEvidenceRoots(CczProject project)
    {
        return new[]
        {
            new EvidenceRoot(Path.Combine(project.WorkspaceRoot, "CCZModStudio_Reports"), "工作区报告"),
            new EvidenceRoot(Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports"), "工作区导出"),
            new EvidenceRoot(Path.Combine(project.GameRoot, "_CCZModStudio_Reports"), "测试副本差异报告"),
            new EvidenceRoot(Path.Combine(project.GameRoot, "_CCZModStudio_Backups"), "测试副本备份报告")
        };
    }

    private static IEnumerable<string> EnumerateEvidenceFiles(EvidenceRoot root)
    {
        var option = root.Label.Contains("备份", StringComparison.Ordinal) ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var path in Directory.EnumerateFiles(root.Path, "*.*", option))
        {
            var extension = Path.GetExtension(path);
            if (!EvidenceExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (root.Label.Contains("备份", StringComparison.Ordinal) &&
                !Path.GetFileName(path).Contains("Report", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileName(path).Contains("WriteOperationReport", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return path;
        }
    }

    private static ProjectEvidenceItem BuildItem(FileInfo info, string sourceRoot)
    {
        var kind = ClassifyKind(info.Name);
        var category = ClassifyCategory(kind, info.Extension);
        return new ProjectEvidenceItem
        {
            Category = category,
            Kind = kind,
            FileName = info.Name,
            SourceRoot = sourceRoot,
            FullPath = info.FullName,
            LastWriteTime = info.LastWriteTime,
            LastWriteTimeText = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            SizeBytes = info.Length,
            SizeText = FormatSize(info.Length),
            Annotation = BuildAnnotation(kind, info.Name),
            SuggestedUse = BuildSuggestedUse(kind),
            SafetyNote = BuildSafetyNote(kind)
        };
    }

    private static string ClassifyKind(string fileName)
    {
        if (fileName.Contains("发布前综合报告", StringComparison.Ordinal)) return "发布前综合报告";
        if (fileName.Contains("关卡地图联动检查报告", StringComparison.Ordinal)) return "关卡地图联动报告";
        if (fileName.Contains("RS命令引用核对清单", StringComparison.Ordinal) ||
            fileName.Contains("SV命令引用核对清单", StringComparison.Ordinal)) return "R/S命令引用核对清单";
        if (fileName.Contains("RS命令参数模板目录", StringComparison.Ordinal) ||
            fileName.Contains("SV命令参数模板目录", StringComparison.Ordinal)) return "R/S命令参数模板目录";
        if (fileName.Contains("TestCopyDiff", StringComparison.OrdinalIgnoreCase)) return "测试副本差异报告";
        if (fileName.Contains("ProjectAudit", StringComparison.OrdinalIgnoreCase)) return "项目体检报告";
        if (fileName.Contains("WriteOperationReport", StringComparison.OrdinalIgnoreCase)) return "结构化写入报告";
        if (fileName.Contains("ResourceReplaceReport", StringComparison.OrdinalIgnoreCase)) return "资源替换文本报告";
        if (fileName.Contains("ScenarioTexts", StringComparison.OrdinalIgnoreCase)) return "R/S文本导出";
        if (fileName.Contains("ScenarioStructureProbe", StringComparison.OrdinalIgnoreCase)) return "R/S结构草图导出";
        if (fileName.Contains("关卡地图联动预览", StringComparison.Ordinal) || fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return "可视化预览PNG";
        if (fileName.Contains("MissingResources", StringComparison.OrdinalIgnoreCase)) return "缺失资源CSV";
        if (fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return "CSV导出";
        return "其他报告/导出";
    }

    private static string ClassifyCategory(string kind, string extension)
    {
        if (kind.Contains("发布", StringComparison.Ordinal)) return "发布证据";
        if (kind.Contains("地图", StringComparison.Ordinal) || kind.Contains("预览", StringComparison.Ordinal)) return "地图/可视化证据";
        if (kind.Contains("R/S", StringComparison.Ordinal) || kind.Contains("SV", StringComparison.Ordinal)) return "R/S剧本证据";
        if (kind.Contains("写入", StringComparison.Ordinal) || kind.Contains("替换", StringComparison.Ordinal)) return "写入/回滚证据";
        if (kind.Contains("体检", StringComparison.Ordinal) || kind.Contains("差异", StringComparison.Ordinal)) return "安全检查证据";
        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)) return "表格导出证据";
        return "其他证据";
    }

    private static int EvidenceKindRank(string kind) => kind switch
    {
        "发布前综合报告" => 100,
        "关卡地图联动报告" => 90,
        "R/S命令引用核对清单" => 88,
        "R/S命令参数模板目录" => 87,
        "测试副本差异报告" => 80,
        "项目体检报告" => 75,
        "结构化写入报告" => 70,
        "可视化预览PNG" => 65,
        "R/S结构草图导出" => 60,
        "R/S文本导出" => 55,
        "缺失资源CSV" => 50,
        "CSV导出" => 40,
        _ => 10
    };

    private static string BuildAnnotation(string kind, string fileName) => kind switch
    {
        "发布前综合报告" => "汇总核心文件、体检、差异、备份历史、资源风险、关卡地图联动和创作者备注，是发布前总检查入口。",
        "关卡地图联动报告" => "核对旧 E5S 兼容线索、Map 底图和 Hexzmap 地形块是否成套；当前不代表 R/S eex 主剧本引用规则。",
        "R/S命令引用核对清单" => "把 R/S eex 结构草图里的命令引用候选整理成逐条核对清单，适合剧情命令、人物、物品、文本和地图坐标复查。",
        "R/S命令参数模板目录" => "汇总已知 R/S eex 命令参数槽位、中文解释、风险提示和字典覆盖率，适合剧情命令研究与备注前查阅。",
        "测试副本差异报告" => "记录测试副本相对原始目录的文件级差异，便于确认发布内容和回滚范围。",
        "项目体检报告" => "记录核心文件、偏移护栏、只读/测试副本边界和资源目录状态。",
        "结构化写入报告" => "记录一次具体写入的目标、备份、哈希和字段/偏移级改动，是回滚和复盘的关键证据。",
        "可视化预览PNG" => "可视化截图或地形/地图叠加预览，适合制作沟通、发布前留证和实机对照。",
        "R/S文本导出" => "导出的 R/S eex 文本线索，包含 GBK 容量、写回状态和中文注释，便于离线校对对白。",
        "R/S结构草图导出" => "导出的 R/S eex 结构草图 XML，适合对照旧剧本编辑器和记录命令候选。",
        "缺失资源CSV" => "列出缺失或异常资源，适合批量补素材和分配制作任务。",
        "CSV导出" => "当前数据表、资源或诊断的表格导出，可作为策划表和离线核对材料。",
        _ => $"工具生成文件：{fileName}"
    };

    private static string BuildSuggestedUse(string kind) => kind switch
    {
        "发布前综合报告" => "发布前最后复读；若报告仍有风险或未检查项，不建议打包发布。",
        "关卡地图联动报告" => "地图、地形或旧 E5S 兼容文件变动后重新生成，并与预览 PNG 一起保留。",
        "R/S命令引用核对清单" => "逐条对照旧工具、备注和实机测试；不确定命令先记录备注，不直接写回完整结构。",
        "R/S命令参数模板目录" => "阅读模板覆盖范围，优先补充未确认命令的备注、旧工具截图和实机验证记录。",
        "测试副本差异报告" => "确认只包含预期改动，再生成发布副本。",
        "项目体检报告" => "作为版本和安全边界证据；发现 Error/Warn 先处理。",
        "结构化写入报告" => "需要回滚、解释改动或排查异常时优先查看。",
        "可视化预览PNG" => "用于对比地图底图、地形格、资源替换前后效果。",
        _ => "作为制作过程留证；必要时为该证据添加创作者备注。"
    };

    private static string BuildSafetyNote(string kind)
    {
        var text = "打开或定位该证据不会写入游戏文件。";
        if (kind.Contains("R/S命令", StringComparison.Ordinal) || kind.Contains("SV命令", StringComparison.Ordinal))
        {
            text += " R/S eex 命令候选来自 16 位词窗口扫描，不代表完整命令结构已确认。";
        }
        else if (kind.Contains("预览", StringComparison.Ordinal))
        {
            text += " PNG 只是可视化证据，不会参与游戏运行。";
        }
        else if (kind.Contains("写入", StringComparison.Ordinal))
        {
            text += " 若要还原，请回到“备份历史/回滚”页执行受保护回滚。";
        }

        return text;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024 * 1024)
        {
            return (bytes / 1024d / 1024d).ToString("F2", CultureInfo.InvariantCulture) + " MB";
        }

        if (bytes >= 1024)
        {
            return (bytes / 1024d).ToString("F1", CultureInfo.InvariantCulture) + " KB";
        }

        return bytes.ToString("N0", CultureInfo.InvariantCulture) + " B";
    }

    private sealed record EvidenceRoot(string Path, string Label);
}
