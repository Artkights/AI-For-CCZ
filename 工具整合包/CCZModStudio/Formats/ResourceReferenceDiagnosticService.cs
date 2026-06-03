using System.Data;
using System.Globalization;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class ResourceReferenceDiagnosticService
{
    public IReadOnlyList<ResourceDiagnosticItem> AnalyzeImageAssignments(CczProject project, DataTable assignments, IEnumerable<ResourceIndexItem> resources)
    {
        var resourceList = resources.ToList();
        var diagnostics = new List<ResourceDiagnosticItem>();
        AnalyzePrefix(project, assignments, resourceList, diagnostics, "R", "R形象", "R形象编号", "R资源状态", "6.5-0-4 R形象", "人物 R 形象");
        AnalyzePrefix(project, assignments, resourceList, diagnostics, "S", "S形象", "S形象编号", "S资源状态", "6.5-0-5 S形象", "人物 S 形象");
        return diagnostics
            .OrderByDescending(x => SeverityRank(x.Severity))
            .ThenBy(x => x.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Rule, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => NaturalIdSortKey(x.Id))
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<ResourceDiagnosticItem> AnalyzeScenarioMapLinks(CczProject project, IEnumerable<ScenarioMapLinkInfo> links)
    {
        var linkList = links.ToList();
        var diagnostics = new List<ResourceDiagnosticItem>();
        if (linkList.Count == 0)
        {
            return diagnostics;
        }

        var normalLinks = linkList.Where(x => x.Status != "非普通关卡").ToList();
        var complete = normalLinks.Count(x => x.Status == "完整候选");
        var missingMap = normalLinks.Where(x => !x.MapImageExists).ToList();
        var missingHexzmap = normalLinks.Where(x => !x.HexzmapBlockExists).ToList();
        var incomplete = normalLinks.Where(x => x.Status.Contains("缺", StringComparison.Ordinal)).ToList();

        diagnostics.Add(new ResourceDiagnosticItem
        {
            Severity = incomplete.Count > 0 ? "Warn" : "Info",
            Category = "关卡地图联动",
            Rule = "联动概览",
            Id = string.Empty,
            Name = "SV/Map/Hexzmap",
            Status = $"普通关卡 {normalLinks.Count}，完整候选 {complete}，缺地图图 {missingMap.Count}，缺地形块 {missingHexzmap.Count}",
            Detail = "按同编号候选规则检查 SVxxx.E5S、Map\\Mxxx.jpg 和 Hexzmap 候选块 Mxxx 是否成套。该规则用于发布前资源排查，不代表已完全确认引擎真实引用字段。",
            Suggestion = "优先处理缺地图图或缺地形块的关卡；若该关卡实际使用特殊地图，请在策划记录中说明，避免发布前误删或漏放资源。",
            Path = project.GameRoot
        });

        foreach (var link in incomplete.OrderBy(x => ScenarioSortKey(x.ScenarioId)).ThenBy(x => x.ScenarioFileName, StringComparer.CurrentCultureIgnoreCase))
        {
            var missingParts = new List<string>();
            if (!link.MapImageExists) missingParts.Add("Map图片");
            if (!link.HexzmapBlockExists) missingParts.Add("Hexzmap地形块");
            diagnostics.Add(new ResourceDiagnosticItem
            {
                Severity = "Warn",
                Category = "关卡地图联动",
                Rule = "关卡地图资源不完整",
                Id = string.IsNullOrWhiteSpace(link.MapId) ? link.ScenarioId : link.MapId,
                Name = $"{link.ScenarioFileName} {link.ScenarioTitle}".Trim(),
                Status = "缺失：" + string.Join("、", missingParts),
                Detail = link.Annotation,
                Suggestion = link.Suggestion,
                Path = !link.MapImageExists && !string.IsNullOrWhiteSpace(link.MapImagePath)
                    ? link.MapImagePath
                    : link.ScenarioPath
            });
        }

        foreach (var link in normalLinks
                     .Where(x => x.Status == "完整候选")
                     .OrderBy(x => ScenarioSortKey(x.ScenarioId))
                     .ThenBy(x => x.ScenarioFileName, StringComparer.CurrentCultureIgnoreCase)
                     .Take(20))
        {
            diagnostics.Add(new ResourceDiagnosticItem
            {
                Severity = "Info",
                Category = "关卡地图联动",
                Rule = "完整候选示例",
                Id = link.MapId,
                Name = $"{link.ScenarioFileName} {link.ScenarioTitle}".Trim(),
                Status = "SV/Map/Hexzmap 三者齐全",
                Detail = $"地图图片 {link.MapImageName}；Hexzmap 偏移 {link.HexzmapOffsetHex}；主地形 {link.DominantTerrain}；高频地形 {link.TopTerrainNames}",
                Suggestion = "完整候选通常无需处理；如实机地图不匹配，再回到关卡地图联动页核对预览和脚本证据。",
                Path = link.MapImagePath
            });
        }

        return diagnostics;
    }

    private static void AnalyzePrefix(
        CczProject project,
        DataTable assignments,
        IReadOnlyList<ResourceIndexItem> resources,
        List<ResourceDiagnosticItem> diagnostics,
        string prefix,
        string resourceCategory,
        string idColumn,
        string statusColumn,
        string tableName,
        string displayName)
    {
        if (!assignments.Columns.Contains(idColumn) || !assignments.Columns.Contains(statusColumn)) return;

        var category = $"表格引用/{resourceCategory}";
        var rows = assignments.Rows.Cast<DataRow>()
            .Select(row => new AssignmentRef(
                Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture),
                Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToInt32(row[idColumn], CultureInfo.InvariantCulture),
                Convert.ToString(row[statusColumn], CultureInfo.InvariantCulture) ?? string.Empty))
            .ToList();
        var namedRows = rows.Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToList();
        var missingNamed = namedRows.Where(x => CharacterImageResourceService.IsMissingStatus(x.Status)).ToList();
        var missingUnnamed = rows.Where(x => string.IsNullOrWhiteSpace(x.Name) && CharacterImageResourceService.IsMissingStatus(x.Status)).ToList();
        diagnostics.Add(new ResourceDiagnosticItem
        {
            Severity = missingNamed.Count > 0 ? "Warn" : "Info",
            Category = category,
            Rule = "引用概览",
            Id = string.Empty,
            Name = displayName,
            Status = $"已命名引用 {namedRows.Count}，已命名定位异常 {missingNamed.Count}，空名占位定位异常 {missingUnnamed.Count}",
            Detail = prefix == "R"
                ? $"{tableName} 的 {idColumn} 表示 R 形象编号：对应 Pmapobj.e5 的正/反两张图（2n+1/2n+2，1-based 图号）。"
                : $"{tableName} 的 {idColumn} 表示 S 形象编号：普通 0-139；特殊通常为 140-156（按教程分段）。资源候选在 Unit_atk/Unit_mov/Unit_spc.e5。",
            Suggestion = "先处理已命名人物中明显越界/异常的编号，再结合旧工具与实机确认；该报告不再要求补齐 RS\\*.eex。",
            Path = project.GameRoot
        });

        foreach (var row in missingNamed)
        {
            var resolver = new CharacterImageResourceService();
            var status = prefix == "S" ? resolver.BuildSStatus(project, row.ResourceId) : resolver.BuildRStatus(project, row.ResourceId);
            diagnostics.Add(new ResourceDiagnosticItem
            {
                Severity = "Warn",
                Category = category,
                Rule = "人物形象缺失",
                Id = row.RowId.ToString(CultureInfo.InvariantCulture),
                Name = row.Name,
                Status = status.Status + "：" + status.ResourceName,
                Detail = $"{tableName} 第 {row.RowId} 行“{row.Name}”引用 {idColumn}={row.ResourceId}。{status.Detail}",
                Suggestion = "请先确认 Pmapobj.e5 / Unit_*.e5 是否在项目目录；再对照 6.5 形象指定器与实机确认该编号是否应调整。",
                Path = status.Path
            });
        }

        if (missingUnnamed.Count > 0)
        {
            diagnostics.Add(new ResourceDiagnosticItem
            {
                Severity = "Info",
                Category = category,
                Rule = "空名占位缺失",
                Id = string.Empty,
                Name = displayName,
                Status = $"{missingUnnamed.Count} 行空名人物引用缺失资源",
                Detail = "空名/占位人物行也存在形象编号缺失，示例行 ID：" + string.Join("，", missingUnnamed.Take(30).Select(x => x.RowId.ToString(CultureInfo.InvariantCulture))),
                Suggestion = "若这些行不会在 MOD 中出场，可暂不处理；若计划启用，请补齐名称与 R/S 形象资源。",
                Path = project.GameRoot
            });
        }

        foreach (var shared in namedRows.GroupBy(x => x.ResourceId).Where(x => x.Count() > 1).OrderByDescending(x => x.Count()).ThenBy(x => x.Key).Take(40))
        {
            var resolver = new CharacterImageResourceService();
            var status = prefix == "S" ? resolver.BuildSStatus(project, shared.Key) : resolver.BuildRStatus(project, shared.Key);
            var expectedFile = ImageAssignmentService.GetImageResourceFileName(prefix, shared.Key);
            var exists = !CharacterImageResourceService.IsMissingStatus(status.Status);
            diagnostics.Add(new ResourceDiagnosticItem
            {
                Severity = "Info",
                Category = category,
                Rule = "多个已命名人物共享形象",
                Id = shared.Key.ToString("00", CultureInfo.InvariantCulture),
                Name = expectedFile,
                Status = $"{shared.Count()} 个已命名人物共享",
                Detail = string.Join("；", shared.Take(12).Select(x => $"{x.RowId}:{x.Name}")) + (shared.Count() > 12 ? " ..." : string.Empty),
                Suggestion = exists
                    ? "共享形象通常合法；若发现人物串形象，请检查这些人物是否误用了同一编号。"
                    : "共享编号对应资源定位异常，应先检查 Pmapobj.e5 / Unit_*.e5 是否齐全，再决定是否统一改到确认存在的编号。",
                Path = status.Path
            });
        }
    }

    private static bool TryParseId(string id, out int number) =>
        int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);

    private static int NaturalIdSortKey(string id) =>
        TryParseId(id, out var number) ? number : int.MaxValue;

    private static int ScenarioSortKey(string id) =>
        int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : int.MaxValue;

    private static int SeverityRank(string severity) => severity switch
    {
        "Error" => 3,
        "Warn" => 2,
        "Info" => 1,
        _ => 0
    };

    private sealed record AssignmentRef(int RowId, string Name, int ResourceId, string Status);
}
