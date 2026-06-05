using System.Globalization;
using System.Text;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 生成发布前综合报告：项目体检、测试副本差异、备份历史和结构化写入报告摘要。
/// 该报告只写入 CCZModStudio_Reports，不改变任何游戏文件。
/// </summary>
public sealed class ProjectDeliveryReportService
{
    private readonly ProjectAuditService _auditService = new();
    private readonly TestCopyDiffService _diffService = new();
    private readonly BackupHistoryService _backupHistoryService = new();
    private readonly WriteOperationReportFormatter _reportFormatter = new();
    private readonly ProjectEvidenceService _evidenceService = new();

    public string WriteReport(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        IReadOnlyList<ProjectAuditItem>? auditItems = null,
        IReadOnlyList<ProjectDiffItem>? diffItems = null,
        IReadOnlyList<BackupHistoryItem>? backupItems = null,
        IReadOnlyList<ResourceDiagnosticItem>? resourceDiagnostics = null,
        IReadOnlyList<ScenarioMapLinkInfo>? scenarioMapLinks = null,
        IReadOnlyList<CreatorNote>? creatorNotes = null)
    {
        var reportRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Reports");
        Directory.CreateDirectory(reportRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(reportRoot, $"{stamp}_发布前综合报告.md");
        var text = BuildReport(project, tables, auditItems, diffItems, backupItems, resourceDiagnostics, scenarioMapLinks, creatorNotes);
        File.WriteAllText(reportPath, text, Encoding.UTF8);
        return reportPath;
    }

    public string BuildReport(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        IReadOnlyList<ProjectAuditItem>? auditItems = null,
        IReadOnlyList<ProjectDiffItem>? diffItems = null,
        IReadOnlyList<BackupHistoryItem>? backupItems = null,
        IReadOnlyList<ResourceDiagnosticItem>? resourceDiagnostics = null,
        IReadOnlyList<ScenarioMapLinkInfo>? scenarioMapLinks = null,
        IReadOnlyList<CreatorNote>? creatorNotes = null)
    {
        var audit = auditItems is { Count: > 0 } ? auditItems : _auditService.Analyze(project, tables);
        var diffResult = TryGetDiff(project, diffItems);
        var backups = backupItems is { Count: > 0 } ? backupItems : SafeScanBackups(project);
        var evidence = SafeScanEvidence(project);

        var builder = new StringBuilder();
        builder.AppendLine("# CCZModStudio 发布前综合报告");
        builder.AppendLine();
        builder.AppendLine($"- 生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"- 项目目录：`{project.GameRoot}`");
        builder.AppendLine($"- 工作区：`{project.WorkspaceRoot}`");
        builder.AppendLine($"- HexTable：`{project.HexTableXmlPath}`");
        builder.AppendLine($"- 当前模式：{(project.IsTestCopy ? "测试副本（可写，可做差异对比）" : "当前项目（可写）")}");
        builder.AppendLine();

        AppendCoreFiles(builder, project);
        AppendAudit(builder, audit);
        AppendDiff(builder, project, diffResult);
        AppendBackups(builder, backups);
        AppendCreatorRiskSummary(builder, resourceDiagnostics, scenarioMapLinks, creatorNotes);
        AppendProjectEvidenceSummary(builder, evidence, creatorNotes);
        AppendChecklist(builder, project, audit, diffResult.Items, backups, diffResult.ErrorMessage, resourceDiagnostics, scenarioMapLinks);
        AppendBoundary(builder);
        return builder.ToString();
    }

    private void AppendCoreFiles(StringBuilder builder, CczProject project)
    {
        builder.AppendLine("## 1. 核心文件状态");
        builder.AppendLine();
        builder.AppendLine("| 名称 | 类型 | 存在 | 大小 | 路径 |");
        builder.AppendLine("| --- | --- | --- | ---: | --- |");
        foreach (var file in project.GetFileStatuses())
        {
            builder.AppendLine($"| {Escape(file.Name)} | {Escape(file.Kind)} | {(file.Exists ? "是" : "否")} | {file.SizeBytes?.ToString("N0", CultureInfo.InvariantCulture) ?? "-"} | `{Escape(file.Path)}` |");
        }
        builder.AppendLine();
    }

    private static void AppendAudit(StringBuilder builder, IReadOnlyList<ProjectAuditItem> audit)
    {
        var errors = audit.Count(item => item.Severity == "Error");
        var warnings = audit.Count(item => item.Severity == "Warn");
        var infos = audit.Count - errors - warnings;

        builder.AppendLine("## 2. 项目体检摘要");
        builder.AppendLine();
        builder.AppendLine($"- 体检项：{audit.Count}");
        builder.AppendLine($"- 错误：{errors}");
        builder.AppendLine($"- 警告：{warnings}");
        builder.AppendLine($"- 信息：{infos}");
        builder.AppendLine();
        builder.AppendLine("| 等级 | 分类 | 名称 | 状态 | 说明 |");
        builder.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var item in audit
                     .OrderBy(item => SeveritySort(item.Severity))
                     .ThenBy(item => item.Category, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                     .Take(20))
        {
            builder.AppendLine($"| {Escape(item.Severity)} | {Escape(item.Category)} | {Escape(item.Name)} | {Escape(item.Status)} | {Escape(Trim(item.Detail, 120))} |");
        }
        builder.AppendLine();
    }

    private static void AppendDiff(StringBuilder builder, CczProject project, DiffResult diffResult)
    {
        builder.AppendLine("## 3. 测试副本差异摘要");
        builder.AppendLine();
        if (!project.IsTestCopy)
        {
            builder.AppendLine("当前目录不是测试副本，未生成“测试副本 vs 原始项目”的差异摘要。当前项目仍可直接发布副本；如需文件级对比，请先创建测试副本。");
            builder.AppendLine();
            return;
        }

        if (!string.IsNullOrWhiteSpace(diffResult.ErrorMessage))
        {
            builder.AppendLine("差异分析失败：");
            builder.AppendLine();
            builder.AppendLine("> " + Escape(diffResult.ErrorMessage));
            builder.AppendLine();
            return;
        }

        var diff = diffResult.Items;
        var modified = diff.Count(item => item.Status == "已修改");
        var added = diff.Count(item => item.Status == "新增");
        var missing = diff.Count(item => item.Status == "缺失");
        builder.AppendLine($"- 差异项：{diff.Count}");
        builder.AppendLine($"- 已修改：{modified}");
        builder.AppendLine($"- 新增：{added}");
        builder.AppendLine($"- 缺失：{missing}");
        builder.AppendLine();
        builder.AppendLine("| 状态 | 相对路径 | 原始大小 | 测试大小 | 说明 |");
        builder.AppendLine("| --- | --- | ---: | ---: | --- |");
        foreach (var item in diff
                     .OrderBy(item => DiffSort(item.Status))
                     .ThenBy(item => item.RelativePath, StringComparer.CurrentCultureIgnoreCase)
                     .Take(40))
        {
            builder.AppendLine($"| {Escape(item.Status)} | `{Escape(item.RelativePath)}` | {FormatSize(item.SourceSize)} | {FormatSize(item.TestSize)} | {Escape(Trim(item.Detail, 120))} |");
        }
        builder.AppendLine();
    }

    private void AppendBackups(StringBuilder builder, IReadOnlyList<BackupHistoryItem> backups)
    {
        var restorable = backups.Count(item => item.Restorable);
        var withReport = backups.Count(item => !string.IsNullOrWhiteSpace(item.ReportPath));
        var structured = backups.Count(item => item.ReportPath.EndsWith("WriteOperationReport.json", StringComparison.OrdinalIgnoreCase));

        builder.AppendLine("## 4. 备份历史与结构化写入报告");
        builder.AppendLine();
        builder.AppendLine($"- 备份数量：{backups.Count}");
        builder.AppendLine($"- 可还原：{restorable}");
        builder.AppendLine($"- 关联报告：{withReport}");
        builder.AppendLine($"- 结构化 WriteOperationReport.json：{structured}");
        builder.AppendLine();

        if (backups.Count == 0)
        {
            builder.AppendLine("未扫描到备份历史。若当前是测试副本，建议在写入前确认自动备份目录是否存在。");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("| 时间 | 类型 | 目标 | 可还原 | 来源动作 | 报告 |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var item in backups.OrderByDescending(item => item.CreatedAt).Take(30))
        {
            var reportName = string.IsNullOrWhiteSpace(item.ReportPath) ? "-" : Path.GetFileName(item.ReportPath);
            builder.AppendLine($"| {Escape(item.CreatedAtText)} | {Escape(item.Kind)} | `{Escape(item.TargetRelativePath)}` | {(item.Restorable ? "是" : "否")} | {Escape(item.SourceAction)} | {Escape(reportName)} |");
        }
        builder.AppendLine();

        var latestStructured = backups
            .Where(item => item.ReportPath.EndsWith("WriteOperationReport.json", StringComparison.OrdinalIgnoreCase) && File.Exists(item.ReportPath))
            .OrderByDescending(item => item.CreatedAt)
            .Take(5)
            .ToList();
        if (latestStructured.Count == 0)
        {
            builder.AppendLine("最近结构化写入报告：未找到 WriteOperationReport.json。");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("### 最近结构化写入报告摘要");
        builder.AppendLine();
        foreach (var item in latestStructured)
        {
            builder.AppendLine($"#### {Escape(item.TargetRelativePath)} / {Escape(item.CreatedAtText)}");
            builder.AppendLine();
            builder.AppendLine("```text");
            builder.AppendLine(_reportFormatter.FormatForCreator(item.ReportPath, maxChanges: 3).TrimEnd());
            builder.AppendLine("```");
            builder.AppendLine();
        }
    }

    private static void AppendCreatorRiskSummary(
        StringBuilder builder,
        IReadOnlyList<ResourceDiagnosticItem>? resourceDiagnostics,
        IReadOnlyList<ScenarioMapLinkInfo>? scenarioMapLinks,
        IReadOnlyList<CreatorNote>? creatorNotes)
    {
        var diagnostics = resourceDiagnostics ?? Array.Empty<ResourceDiagnosticItem>();
        var links = scenarioMapLinks ?? Array.Empty<ScenarioMapLinkInfo>();
        var notes = creatorNotes ?? Array.Empty<CreatorNote>();

        builder.AppendLine("## 5. MOD创作风险摘要");
        builder.AppendLine();
        builder.AppendLine("本节汇总资源诊断、人物 R/S 引用、关卡地图联动和创作者备注，帮助发布前集中复核。若某项显示“未提供”，请先在对应页面运行诊断/生成联动后重新导出本报告。");
        builder.AppendLine();

        AppendResourceDiagnosticSummary(builder, diagnostics);
        AppendRsReferenceSummary(builder, diagnostics);
        AppendScenarioMapSummary(builder, links);
        AppendCreatorNoteSummary(builder, notes);
    }

    private static void AppendResourceDiagnosticSummary(StringBuilder builder, IReadOnlyList<ResourceDiagnosticItem> diagnostics)
    {
        builder.AppendLine("### 5.1 资源诊断总览");
        builder.AppendLine();
        if (diagnostics.Count == 0)
        {
            builder.AppendLine("- 未提供资源诊断结果：建议先运行“资源诊断”，再重新生成发布前综合报告。");
            builder.AppendLine();
            return;
        }

        var errors = diagnostics.Count(item => item.Severity == "Error");
        var warnings = diagnostics.Count(item => item.Severity == "Warn");
        var infos = diagnostics.Count(item => item.Severity == "Info");
        builder.AppendLine($"- 诊断项：{diagnostics.Count}");
        builder.AppendLine($"- Error：{errors}");
        builder.AppendLine($"- Warn：{warnings}");
        builder.AppendLine($"- Info：{infos}");
        builder.AppendLine();

        builder.AppendLine("| 等级 | 分类 | 规则 | 对象 | 状态 | 建议 |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var item in diagnostics
                     .OrderBy(item => SeveritySort(item.Severity))
                     .ThenBy(item => item.Category, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(item => item.Rule, StringComparer.CurrentCultureIgnoreCase)
                     .Take(15))
        {
            builder.AppendLine($"| {Escape(item.Severity)} | {Escape(item.Category)} | {Escape(item.Rule)} | {Escape(item.Name)} | {Escape(Trim(item.Status, 80))} | {Escape(Trim(item.Suggestion, 100))} |");
        }
        builder.AppendLine();
    }

    private static void AppendRsReferenceSummary(StringBuilder builder, IReadOnlyList<ResourceDiagnosticItem> diagnostics)
    {
        builder.AppendLine("### 5.2 人物 R/S 形象引用");
        builder.AppendLine();
        if (diagnostics.Count == 0)
        {
            builder.AppendLine("- 未提供人物 R/S 引用诊断。");
            builder.AppendLine();
            return;
        }

        var rsDiagnostics = diagnostics
            .Where(item => item.Category.StartsWith("表格引用/R形象", StringComparison.Ordinal) ||
                           item.Category.StartsWith("表格引用/S形象", StringComparison.Ordinal))
            .ToList();
        if (rsDiagnostics.Count == 0)
        {
            builder.AppendLine("- 未发现人物 R/S 引用诊断项；若尚未运行资源诊断，请先运行后重导。");
            builder.AppendLine();
            return;
        }

        var missingNamed = rsDiagnostics.Count(item => item.Rule == "人物形象缺失");
        var placeholderMissing = rsDiagnostics.Count(item => item.Rule == "空名占位缺失");
        var unused = rsDiagnostics.Count(item => item.Rule == "资源未被人物表引用");
        var shared = rsDiagnostics.Count(item => item.Rule == "多个已命名人物共享形象");
        builder.AppendLine($"- 已命名人物形象缺失：{missingNamed}");
        builder.AppendLine($"- 空名/占位行缺失提示：{placeholderMissing}");
        builder.AppendLine($"- 资源存在但未被人物表引用：{unused}");
        builder.AppendLine($"- 多个已命名人物共享形象提示：{shared}");
        builder.AppendLine("- 建议：先处理已命名人物形象缺失；共享形象和未使用资源应结合角色规划决定保留、改号或清理。");
        builder.AppendLine();

        builder.AppendLine("| 分类 | 规则 | 行/编号 | 对象 | 状态 | 处理建议 |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var item in rsDiagnostics
                     .OrderBy(item => SeveritySort(item.Severity))
                     .ThenBy(item => item.Category, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(item => item.Rule, StringComparer.CurrentCultureIgnoreCase)
                     .Take(20))
        {
            builder.AppendLine($"| {Escape(item.Category)} | {Escape(item.Rule)} | {Escape(item.Id)} | {Escape(item.Name)} | {Escape(Trim(item.Status, 80))} | {Escape(Trim(item.Suggestion, 100))} |");
        }
        builder.AppendLine();
    }

    private static void AppendScenarioMapSummary(StringBuilder builder, IReadOnlyList<ScenarioMapLinkInfo> links)
    {
        builder.AppendLine("### 5.3 关卡地图联动");
        builder.AppendLine();
        if (links.Count == 0)
        {
            builder.AppendLine("- 未提供关卡地图联动结果：建议先打开“关卡地图联动”页生成联动，再导出检查报告和本综合报告。");
            builder.AppendLine();
            return;
        }

        var normal = links.Count(link => link.Status != "非普通关卡");
        var complete = links.Count(link => link.Status == "完整候选");
        var incomplete = links.Count(link => link.Status.Contains("缺", StringComparison.Ordinal));
        var missingMap = links.Count(link => link.Status != "非普通关卡" && !link.MapImageExists);
        var missingHex = links.Count(link => link.Status != "非普通关卡" && !link.HexzmapBlockExists);
        builder.AppendLine($"- 普通关卡候选：{normal}");
        builder.AppendLine($"- 完整候选：{complete}");
        builder.AppendLine($"- 不完整联动：{incomplete}");
        builder.AppendLine($"- 缺 Map 图片：{missingMap}");
        builder.AppendLine($"- 缺 Hexzmap 地形块：{missingHex}");
        builder.AppendLine("- 建议：发布前重点复核不完整联动，必要时导出“关卡地图联动检查报告”和“预览PNG”，并进行实机地图验证。");
        builder.AppendLine();

        var examples = links
            .Where(link => link.Status.Contains("缺", StringComparison.Ordinal))
            .OrderBy(link => ScenarioSortKey(link.ScenarioId))
            .ThenBy(link => link.ScenarioFileName, StringComparer.CurrentCultureIgnoreCase)
            .Take(20)
            .ToList();
        if (examples.Count == 0)
        {
            builder.AppendLine("- 未发现不完整联动示例。");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("| SV | 地图 | 标题 | 状态 | 图片 | 地形块 | 建议 |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        foreach (var link in examples)
        {
            builder.AppendLine($"| {Escape(link.ScenarioFileName)} | {Escape(link.MapId)} | {Escape(link.ScenarioTitle)} | {Escape(link.Status)} | {(link.MapImageExists ? Escape(link.MapImageName) : "缺失")} | {(link.HexzmapBlockExists ? Escape(link.HexzmapOffsetHex) : "缺失")} | {Escape(Trim(link.Suggestion, 100))} |");
        }
        builder.AppendLine();
    }

    private static void AppendCreatorNoteSummary(StringBuilder builder, IReadOnlyList<CreatorNote> notes)
    {
        builder.AppendLine("### 5.4 创作者备注");
        builder.AppendLine();
        if (notes.Count == 0)
        {
            builder.AppendLine("- 未提供创作者备注；若已记录风险/待办，请先读取备注后重新导出报告。");
            builder.AppendLine();
            return;
        }

        var todo = notes.Count(note => ContainsAny(note.Content, "待办", "TODO", "todo", "未完成") || ContainsAny(note.Tags, "待办", "TODO", "todo"));
        var risk = notes.Count(note => ContainsAny(note.Content, "风险", "危险", "需验证", "实机验证") || ContainsAny(note.Tags, "风险", "验证"));
        builder.AppendLine($"- 备注总数：{notes.Count}");
        builder.AppendLine($"- 待办相关：{todo}");
        builder.AppendLine($"- 风险/验证相关：{risk}");
        builder.AppendLine();

        builder.AppendLine("| 范围 | 标题 | 标签 | 更新时间 | 摘要 |");
        builder.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var note in notes
                     .Where(note => ContainsAny(note.Content, "待办", "TODO", "todo", "风险", "需验证", "实机验证") ||
                                    ContainsAny(note.Tags, "待办", "TODO", "todo", "风险", "验证"))
                     .OrderByDescending(note => note.UpdatedAtText, StringComparer.Ordinal)
                     .Take(15))
        {
            builder.AppendLine($"| {Escape(note.Scope)} | {Escape(note.Title)} | {Escape(note.Tags)} | {Escape(note.UpdatedAtText)} | {Escape(Trim(note.Content, 120))} |");
        }
        builder.AppendLine();
    }

    private static void AppendProjectEvidenceSummary(
        StringBuilder builder,
        IReadOnlyList<ProjectEvidenceItem> evidence,
        IReadOnlyList<CreatorNote>? creatorNotes)
    {
        var notes = creatorNotes ?? Array.Empty<CreatorNote>();
        var rsCommandNotes = notes
            .Where(note =>
                note.Scope.Equals("R/S命令", StringComparison.Ordinal) ||
                note.Scope.Equals("SV命令", StringComparison.Ordinal))
            .ToList();
        var commandReferenceNotes = rsCommandNotes
            .Where(note => ContainsAny(note.Tags, "命令引用", "待核对", "实机验证") ||
                           ContainsAny(note.Content, "可跳转引用候选", "旧工具对照", "安全边界"))
            .ToList();
        var checklistCount = evidence.Count(item => item.Kind == "R/S命令引用核对清单" || item.Kind == "SV命令引用核对清单");
        var templateCatalogCount = evidence.Count(item => item.Kind == "R/S命令参数模板目录" || item.Kind == "SV命令参数模板目录");
        var mapReportCount = evidence.Count(item => item.Kind == "关卡地图联动报告");
        var previewCount = evidence.Count(item => item.Kind == "可视化预览PNG");
        var writeReportCount = evidence.Count(item => item.Kind == "结构化写入报告");

        builder.AppendLine("## 6. 最近报告/发布证据摘要");
        builder.AppendLine();
        builder.AppendLine("本节反向汇总 CCZModStudio 已生成的报告、导出表、预览 PNG、写入报告和 R/S eex 命令备注，用于发布前确认“证据是否齐全”。它只读取工具产物索引，不修改游戏文件。");
        builder.AppendLine();
        builder.AppendLine($"- 最近证据总数：{evidence.Count}");
        builder.AppendLine($"- R/S eex 命令引用核对清单：{checklistCount}");
        builder.AppendLine($"- R/S eex 命令参数模板目录：{templateCatalogCount}");
        builder.AppendLine($"- R/S eex 命令备注：{rsCommandNotes.Count}（其中命令引用/待核对模板：{commandReferenceNotes.Count}）");
        builder.AppendLine($"- 关卡地图联动报告：{mapReportCount}");
        builder.AppendLine($"- 可视化预览 PNG：{previewCount}");
        builder.AppendLine($"- 结构化写入报告：{writeReportCount}");
        builder.AppendLine();

        if (evidence.Count == 0)
        {
            builder.AppendLine("- 未发现最近报告/发布证据。建议先导出 R/S eex 命令引用清单、关卡地图联动报告、预览 PNG、测试副本差异和结构化写入报告，再重新生成综合报告。");
            builder.AppendLine();
        }
        else
        {
            builder.AppendLine("### 6.1 最近重点证据");
            builder.AppendLine();
            builder.AppendLine("| 类型 | 文件 | 来源 | 时间 | 大小 | 发布前用途 | 安全说明 |");
            builder.AppendLine("| --- | --- | --- | --- | ---: | --- | --- |");
            foreach (var item in evidence.Take(14))
            {
                builder.AppendLine($"| {Escape(item.Kind)} | `{Escape(item.FileName)}` | {Escape(item.SourceRoot)} | {Escape(item.LastWriteTimeText)} | {Escape(item.SizeText)} | {Escape(Trim(item.SuggestedUse, 90))} | {Escape(Trim(item.SafetyNote, 90))} |");
            }
            builder.AppendLine();

            var byKind = evidence
                .GroupBy(item => item.Kind)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
                .Take(12)
                .ToList();
            builder.AppendLine("### 6.2 证据类型分布");
            builder.AppendLine();
            builder.AppendLine("| 类型 | 数量 | 最新文件 | 创作者用途 |");
            builder.AppendLine("| --- | ---: | --- | --- |");
            foreach (var group in byKind)
            {
                var latest = group.OrderByDescending(item => item.LastWriteTime).First();
                builder.AppendLine($"| {Escape(group.Key)} | {group.Count()} | `{Escape(latest.FileName)}` | {Escape(Trim(latest.SuggestedUse, 100))} |");
            }
            builder.AppendLine();
        }

        builder.AppendLine("### 6.3 R/S eex 命令备注待核对摘录");
        builder.AppendLine();
        if (commandReferenceNotes.Count == 0)
        {
            builder.AppendLine("- 未发现带“命令引用/待核对/实机验证”的 R/S eex 命令备注。若近期修改剧情命令，建议在结构草图中选中命令并点击“为命令引用建备注”。");
            builder.AppendLine();
        }
        else
        {
            builder.AppendLine("| 命令目标 | 标题 | 标签 | 更新时间 | 摘要 |");
            builder.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var note in commandReferenceNotes
                         .OrderByDescending(note => note.UpdatedAtText, StringComparer.Ordinal)
                         .Take(10))
            {
                builder.AppendLine($"| `{Escape(note.TargetKey)}` | {Escape(note.Title)} | {Escape(note.Tags)} | {Escape(note.UpdatedAtText)} | {Escape(Trim(note.Content, 120))} |");
            }
            builder.AppendLine();
        }
    }

    private static void AppendChecklist(
        StringBuilder builder,
        CczProject project,
        IReadOnlyList<ProjectAuditItem> audit,
        IReadOnlyList<ProjectDiffItem> diff,
        IReadOnlyList<BackupHistoryItem> backups,
        string diffError,
        IReadOnlyList<ResourceDiagnosticItem>? resourceDiagnostics,
        IReadOnlyList<ScenarioMapLinkInfo>? scenarioMapLinks)
    {
        var errors = audit.Count(item => item.Severity == "Error");
        var missing = diff.Count(item => item.Status == "缺失");
        var structured = backups.Count(item => item.ReportPath.EndsWith("WriteOperationReport.json", StringComparison.OrdinalIgnoreCase));
        var resourceErrors = resourceDiagnostics?.Count(item => item.Severity == "Error") ?? -1;
        var namedRsMissing = resourceDiagnostics?.Count(item =>
            item.Category.StartsWith("表格引用/", StringComparison.Ordinal) &&
            item.Rule == "人物形象缺失") ?? -1;
        var incompleteScenarioMaps = scenarioMapLinks?.Count(item => item.Status.Contains("缺", StringComparison.Ordinal)) ?? -1;

        builder.AppendLine("## 7. 发布前检查清单");
        builder.AppendLine();
        builder.AppendLine($"- [{(project.IsTestCopy || string.IsNullOrWhiteSpace(diffError) ? "x" : " ")}] 当前目录可写；如需差异对比，已使用测试副本或明确跳过。");
        builder.AppendLine($"- [{(errors == 0 ? "x" : " ")}] 项目体检没有 Error 级阻断项。");
        builder.AppendLine($"- [{(resourceErrors == 0 ? "x" : " ")}] 资源诊断没有 Error 级阻断项。");
        builder.AppendLine($"- [{(namedRsMissing == 0 ? "x" : " ")}] 已命名人物没有缺失 R/S 形象资源。");
        builder.AppendLine($"- [{(incompleteScenarioMaps == 0 ? "x" : " ")}] 关卡地图联动没有缺 Map 图片或 Hexzmap 地形块。");
        builder.AppendLine($"- [{(project.IsTestCopy && string.IsNullOrWhiteSpace(diffError) ? "x" : " ")}] 已生成测试副本差异。");
        builder.AppendLine($"- [{(missing == 0 && string.IsNullOrWhiteSpace(diffError) ? "x" : " ")}] 差异中没有缺失文件，或缺失文件已确认是有意删除。");
        builder.AppendLine($"- [{(backups.Any(item => item.Restorable) ? "x" : " ")}] 写入操作存在可还原备份。");
        builder.AppendLine($"- [{(structured > 0 ? "x" : " ")}] 关键写入操作存在结构化 JSON 报告，可追溯具体改动。");
        builder.AppendLine("- [ ] 已用游戏实机或原工具验证 Data/Imsg/Star/Ekd5/RS eex/资源替换结果。");
        builder.AppendLine("- [ ] 已生成发布副本，并确认发布目录不包含 `_CCZModStudio_Backups`、`_CCZModStudio_Reports`、`_CCZModStudio_TestCopy.txt`。");
        builder.AppendLine();
    }

    private static void AppendBoundary(StringBuilder builder)
    {
        builder.AppendLine("## 8. 安全边界");
        builder.AppendLine();
        builder.AppendLine("- 本报告只是汇总和导出，不会修改任何游戏文件。");
        builder.AppendLine("- 已知表格、R/S eex 短文本、补丁、整文件资源替换必须继续遵守“自动备份、复读校验或结构化报告、可回滚”。");
        builder.AppendLine("- EEX/Ls/E5 内部重封包、完整 R/S eex 命令树写回仍属于未完成格式研究，报告中出现的候选解释不能作为直接写入依据。");
        builder.AppendLine();
    }

    private DiffResult TryGetDiff(CczProject project, IReadOnlyList<ProjectDiffItem>? supplied)
    {
        if (!project.IsTestCopy) return new DiffResult(Array.Empty<ProjectDiffItem>(), string.Empty);
        if (supplied is { Count: > 0 }) return new DiffResult(supplied, string.Empty);
        try
        {
            return new DiffResult(_diffService.Analyze(project), string.Empty);
        }
        catch (Exception ex)
        {
            return new DiffResult(Array.Empty<ProjectDiffItem>(), ex.Message);
        }
    }

    private IReadOnlyList<BackupHistoryItem> SafeScanBackups(CczProject project)
    {
        try
        {
            return _backupHistoryService.Scan(project);
        }
        catch
        {
            return Array.Empty<BackupHistoryItem>();
        }
    }

    private IReadOnlyList<ProjectEvidenceItem> SafeScanEvidence(CczProject project)
    {
        try
        {
            return _evidenceService.Scan(project, maxItems: 80);
        }
        catch
        {
            return Array.Empty<ProjectEvidenceItem>();
        }
    }

    private static int SeveritySort(string severity) => severity switch
    {
        "Error" => 0,
        "Warn" => 1,
        _ => 2
    };

    private static int DiffSort(string status) => status switch
    {
        "缺失" => 0,
        "已修改" => 1,
        "新增" => 2,
        _ => 3
    };

    private static int ScenarioSortKey(string id) =>
        int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : int.MaxValue;

    private static string FormatSize(long? value) => value.HasValue ? value.Value.ToString("N0", CultureInfo.InvariantCulture) : "-";

    private static string Trim(string value, int maxChars)
    {
        value = (value ?? string.Empty).Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');
        return value.Length <= maxChars ? value : value[..maxChars] + "…";
    }

    private static string Escape(string value)
        => (value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static bool ContainsAny(string value, params string[] keywords)
        => !string.IsNullOrWhiteSpace(value) &&
           keywords.Any(keyword => value.Contains(keyword, StringComparison.CurrentCultureIgnoreCase));

    private sealed record DiffResult(IReadOnlyList<ProjectDiffItem> Items, string ErrorMessage);
}
