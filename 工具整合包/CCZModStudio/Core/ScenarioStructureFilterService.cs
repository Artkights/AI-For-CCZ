using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// SV 结构草图筛选服务：按模板、文本、坐标/地图、高风险和关键字筛选，同时保留命中命令的 Scene/Section 父节点。
/// </summary>
public sealed class ScenarioStructureFilterService
{
    public IReadOnlyList<ScenarioStructureRow> Filter(IReadOnlyList<ScenarioStructureRow> rows, ScenarioStructureFilterOptions options)
    {
        if (rows.Count == 0) return Array.Empty<ScenarioStructureRow>();
        if (options.IsEmpty) return rows.ToList();

        var keep = new HashSet<int>();
        ScenarioStructureRow? currentScene = null;
        ScenarioStructureRow? currentSection = null;

        foreach (var row in rows)
        {
            if (row.NodeType == "Scene候选")
            {
                currentScene = row;
                currentSection = null;
            }
            else if (row.NodeType == "Section候选")
            {
                currentSection = row;
            }

            if (!Matches(row, options)) continue;

            if (row.NodeType == "Command候选")
            {
                if (currentScene != null) keep.Add(currentScene.Index);
                if (currentSection != null) keep.Add(currentSection.Index);
            }
            else if (row.NodeType == "Section候选" && currentScene != null)
            {
                keep.Add(currentScene.Index);
            }

            keep.Add(row.Index);
        }

        return rows.Where(row => keep.Contains(row.Index)).ToList();
    }

    public string BuildSummary(IReadOnlyList<ScenarioStructureRow> allRows, IReadOnlyList<ScenarioStructureRow> filteredRows, ScenarioStructureFilterOptions options)
    {
        var allCommands = allRows.Count(row => row.NodeType == "Command候选");
        var filteredCommands = filteredRows.Count(row => row.NodeType == "Command候选");
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.Keyword)) parts.Add($"关键字“{options.Keyword.Trim()}”");
        if (options.TemplatesOnly) parts.Add("仅有参数模板");
        if (options.TextRelatedOnly) parts.Add("仅文本/剧情命令");
        if (options.MapCoordinateOnly) parts.Add("仅地图/坐标命令");
        if (options.HighRiskOnly) parts.Add("仅高风险/需核对命令");
        var filterText = parts.Count == 0 ? "未启用筛选" : string.Join(" + ", parts);
        var highRiskNote = options.HighRiskOnly
            ? "\r\n高风险定义：仅收录低置信度、流程/变量/分支、奖励/资源/人物状态变更，或“缺少参数模板且可能影响流程/地图/资源”的命令；不会再因为通用“需实机验证”说明而把全部命令都列为高风险。"
            : string.Empty;
        return
            $"SV 结构草图筛选：{filterText}。\r\n" +
            $"显示行：{filteredRows.Count}/{allRows.Count}；显示命令：{filteredCommands}/{allCommands}。\r\n" +
            "说明：筛选命中 Command 时会保留其 Scene/Section 父节点，方便继续按事件树浏览；筛选仍只读，不改变 SV 文件。" +
            highRiskNote;
    }

    public bool Matches(ScenarioStructureRow row, ScenarioStructureFilterOptions options)
    {
        if (!MatchesKeyword(row, options.Keyword)) return false;
        if (options.TemplatesOnly && !row.HasCommandTemplate) return false;
        if (options.TextRelatedOnly && !IsTextRelated(row)) return false;
        if (options.MapCoordinateOnly && !IsMapCoordinateRelated(row)) return false;
        if (options.HighRiskOnly && !IsHighRisk(row)) return false;
        return true;
    }

    public bool IsTextRelated(ScenarioStructureRow row)
        => row.NodeType == "Command候选" &&
           (ContainsAny(row.CommandName, "对话", "信息", "旁白", "章名", "文字", "剧情", "场所", "胜利条件")
            || ContainsAny(row.ReferenceHint, "文本线索", "GBK", "对白", "文本")
            || ContainsAny(row.CommandTemplateHint, "文本编号", "信息编号", "旁白文本", "章名文本"));

    public bool IsMapCoordinateRelated(ScenarioStructureRow row)
        => row.NodeType == "Command候选" &&
           (ContainsAny(row.CommandName, "地图", "视点", "坐标", "地点", "区域", "移动", "出现", "转向", "高亮", "绘图", "背景")
            || ContainsAny(row.ParameterPreview, "坐标", "地图")
            || ContainsAny(row.ReferenceHint, "坐标候选", "地图文件候选", "地图编号")
            || ContainsAny(row.CommandTemplateHint, "X坐标", "Y坐标", "地图", "区域"));

    public bool IsHighRisk(ScenarioStructureRow row)
        => !string.IsNullOrWhiteSpace(BuildHighRiskReason(row));

    public string BuildHighRiskReason(ScenarioStructureRow row)
    {
        if (row.NodeType != "Command候选")
        {
            return string.Empty;
        }

        if (row.Confidence == "低")
        {
            return "低置信度命令候选：命令字典或上下文证据不足，修改前必须先和旧编辑器/实机对照。";
        }

        if (IsFlowOrBranchCommand(row))
        {
            return "流程/变量/分支命令：可能影响剧情分支、事件开关、跳转或胜败逻辑，修改前需要重点核对参数长度和触发条件。";
        }

        if (IsStateOrResourceCommand(row))
        {
            return "状态/奖励/资源命令：可能影响人物出场、物品奖励、形象或资源状态，修改前需要核对跨表编号并做实机测试。";
        }

        if (NoTemplateButImportant(row))
        {
            return "缺少参数模板的关键命令：当前只能看到 16 位词预览，尚不能确认每个参数槽位，建议只做定位和备注。";
        }

        if (ContainsAny(row.ReferenceHint, "超长", "不可写回"))
        {
            return "写回边界风险：关联说明中出现超长或不可写回提示，当前只建议读取和定位。";
        }

        return string.Empty;
    }

    private static bool IsFlowOrBranchCommand(ScenarioStructureRow row)
        => ContainsAny(row.CommandName, "变量", "跳转", "case", "else", "询问测试", "变量测试", "条件", "分支", "进入指定地点测试", "进区域测试", "胜利条件", "失败条件")
           || ContainsAny(row.ParameterPreview, "流程/变量/跳转参数候选")
           || ContainsAny(row.ReferenceHint, "流程参数候选", "需等待参数长度表确认");

    private static bool IsStateOrResourceCommand(ScenarioStructureRow row)
        => ContainsAny(
               row.CommandName,
               "获得物品",
               "战利品",
               "金钱",
               "奖励",
               "经验",
               "等级",
               "功勋",
               "形象改变",
               "出场限制",
               "出场",
               "撤退",
               "死亡",
               "加入",
               "离队",
               "兵种",
               "转职",
               "物品",
               "装备",
               "策略")
           || ContainsAny(row.ParameterPreview, "物品/金钱/奖励参数候选");

    private static bool NoTemplateButImportant(ScenarioStructureRow row)
        => row.CommandTemplateHint.StartsWith("暂无", StringComparison.Ordinal)
           && (ContainsAny(row.CommandName, "地图", "坐标", "移动", "区域", "视点", "绘图", "背景", "物体")
               || ContainsAny(row.ParameterPreview, "地图/坐标/图像参数候选"));

    private static bool MatchesKeyword(ScenarioStructureRow row, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        keyword = keyword.Trim();
        return new[]
            {
                row.NodeType,
                row.OffsetHex,
                row.CommandIdHex,
                row.CommandName,
                row.ParameterPreview,
                row.CommandTemplateHint,
                row.ReferenceHint,
                row.Confidence,
                row.Annotation
            }
            .Any(value => value.Contains(keyword, StringComparison.CurrentCultureIgnoreCase));
    }

    private static bool ContainsAny(string source, params string[] values)
    {
        if (string.IsNullOrWhiteSpace(source)) return false;
        return values.Any(value => source.Contains(value, StringComparison.CurrentCultureIgnoreCase));
    }
}
