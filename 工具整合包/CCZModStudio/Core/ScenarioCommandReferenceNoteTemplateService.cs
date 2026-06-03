using System.Globalization;
using System.Text;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// R/S eex 命令引用候选的创作者备注模板服务。
/// 只生成项目侧 CreatorNote 草稿，供创作者记录旧工具对照、实机验证、修改意图、风险和回滚点；
/// 不读取/写入 E5S 存档信息，不把 16 位词窗口候选当作完整命令结构。
/// </summary>
public sealed class ScenarioCommandReferenceNoteTemplateService
{
    public CreatorNote BuildDraft(
        ScenarioStructureProbeResult structure,
        ScenarioStructureRow row,
        IReadOnlyList<ScenarioCommandReferenceTarget>? targets)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(row);
        targets ??= Array.Empty<ScenarioCommandReferenceTarget>();

        return new CreatorNote
        {
            Scope = "R/S命令",
            TargetKey = BuildTargetKey(structure.FileName, row),
            Title = $"R/S命令核对：{structure.FileName} #{row.CommandIndex} {row.CommandName}",
            Tags = "R/S命令,命令引用,待核对,实机验证",
            SourceHint = "由 R/S eex 结构草图“为命令引用建备注”生成；候选来自只读扫描，需结合旧工具和实机核对。",
            Content = BuildContent(structure, row, targets)
        };
    }

    public static string BuildTargetKey(string fileName, ScenarioStructureRow row)
        => $"{fileName}#Scene={row.SceneIndex}#Section={row.SectionIndex}#Command={row.CommandIndex}#Offset={row.OffsetHex}";

    private static string BuildContent(
        ScenarioStructureProbeResult structure,
        ScenarioStructureRow row,
        IReadOnlyList<ScenarioCommandReferenceTarget> targets)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# R/S eex 命令引用核对备注模板");
        builder.AppendLine();
        builder.AppendLine("## 1. 命令定位");
        builder.AppendLine();
        builder.AppendLine($"- 剧本文件：{structure.FileName}");
        builder.AppendLine($"- Scene / Section / Command：{row.SceneIndex} / {row.SectionIndex} / {row.CommandIndex}");
        builder.AppendLine($"- 偏移：{row.OffsetHex}");
        builder.AppendLine($"- 命令 ID / 名称：{row.CommandIdHex} / {row.CommandName}");
        builder.AppendLine($"- 参数预览：{SafeInline(row.ParameterPreview)}");
        builder.AppendLine($"- 参数模板：{SafeInline(string.IsNullOrWhiteSpace(row.CommandTemplateHint) ? "暂无专用参数模板" : row.CommandTemplateHint)}");
        builder.AppendLine($"- 引用提示：{SafeInline(string.IsNullOrWhiteSpace(row.ReferenceHint) ? "暂无自动引用提示" : row.ReferenceHint)}");
        builder.AppendLine($"- 自动注释：{SafeInline(string.IsNullOrWhiteSpace(row.Annotation) ? "暂无自动注释" : row.Annotation)}");
        builder.AppendLine();

        AppendTargets(builder, targets);
        AppendChecklist(builder);
        return builder.ToString();
    }

    private static void AppendTargets(StringBuilder builder, IReadOnlyList<ScenarioCommandReferenceTarget> targets)
    {
        builder.AppendLine("## 2. 可跳转引用候选");
        builder.AppendLine();
        builder.AppendLine("说明：以下候选用于跳转核对，不代表参数含义已经完全确认。请逐项检查人物、物品、策略、文本、地图和坐标是否符合剧情设计。");
        builder.AppendLine();

        if (targets.Count == 0)
        {
            builder.AppendLine("- 当前命令未命中可跳转引用候选。建议仍对照旧工具命令解释、上下文和实机表现记录判断。");
            builder.AppendLine();
        }

        AppendTargetGroup(builder, "### 2.1 数据表候选（人物/物品/策略）", targets.Where(target => target.CanJumpDataTable).ToList());
        AppendTargetGroup(builder, "### 2.2 文本候选", targets.Where(target => target.CanJumpScenarioText).ToList());
        AppendTargetGroup(builder, "### 2.3 地图/坐标候选", targets.Where(target => target.CanJumpScenarioMap).ToList());
        AppendTargetGroup(
            builder,
            "### 2.4 其他线索",
            targets.Where(target => !target.CanJumpDataTable && !target.CanJumpScenarioText && !target.CanJumpScenarioMap).ToList());
    }

    private static void AppendTargetGroup(
        StringBuilder builder,
        string title,
        IReadOnlyList<ScenarioCommandReferenceTarget> targets)
    {
        builder.AppendLine(title);
        builder.AppendLine();
        if (targets.Count == 0)
        {
            builder.AppendLine("- 暂无。");
            builder.AppendLine();
            return;
        }

        foreach (var target in targets.Take(40))
        {
            builder.AppendLine($"- [{target.Kind}] {target.DisplayText}");
            if (!string.IsNullOrWhiteSpace(target.Evidence))
            {
                builder.AppendLine($"  - 依据：{target.Evidence}");
            }

            var jump = BuildJumpText(target);
            if (!string.IsNullOrWhiteSpace(jump))
            {
                builder.AppendLine($"  - 可跳转位置：{jump}");
            }

            var raw = BuildRawValueText(target);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                builder.AppendLine($"  - 原始候选值：{raw}");
            }

            if (!string.IsNullOrWhiteSpace(target.SafetyNote))
            {
                builder.AppendLine($"  - 安全提示：{target.SafetyNote}");
            }
        }

        if (targets.Count > 40)
        {
            builder.AppendLine($"- ……另有 {targets.Count - 40} 项候选，请回到“命令引用”下拉框逐项查看。");
        }

        builder.AppendLine();
    }

    private static void AppendChecklist(StringBuilder builder)
    {
        builder.AppendLine("## 3. 旧工具对照");
        builder.AppendLine();
        builder.AppendLine("- 旧工具名称/版本：");
        builder.AppendLine("- 旧工具中该命令的解释：");
        builder.AppendLine("- 旧工具截图或导出路径：");
        builder.AppendLine("- 与本工具候选不一致之处：");
        builder.AppendLine();

        builder.AppendLine("## 4. 实机验证");
        builder.AppendLine();
        builder.AppendLine("- 测试副本路径：");
        builder.AppendLine("- 进入关卡/触发条件：");
        builder.AppendLine("- 预期表现：");
        builder.AppendLine("- 实际表现：");
        builder.AppendLine("- 是否需要二次核对：");
        builder.AppendLine();

        builder.AppendLine("## 5. 修改意图");
        builder.AppendLine();
        builder.AppendLine("- 我想修改的剧情/事件效果：");
        builder.AppendLine("- 涉及人物/物品/策略/文本/地图：");
        builder.AppendLine("- 预计改动文件：");
        builder.AppendLine();

        builder.AppendLine("## 6. 风险判断");
        builder.AppendLine();
        builder.AppendLine("- 参数含义是否已确认：未确认 / 部分确认 / 已确认");
        builder.AppendLine("- 是否影响流程分支、胜败条件、出场、AI 或奖励：");
        builder.AppendLine("- 是否需要先做最小化测试：");
        builder.AppendLine();

        builder.AppendLine("## 7. 回滚点/备份");
        builder.AppendLine();
        builder.AppendLine("- 写入前备份位置：");
        builder.AppendLine("- 写入报告路径：");
        builder.AppendLine("- 复读校验结果：");
        builder.AppendLine("- 回滚步骤：");
        builder.AppendLine();

        builder.AppendLine("## 8. 安全边界");
        builder.AppendLine();
        builder.AppendLine("- 本备注只保存在 `CCZModStudio_Notes`，不写入游戏文件，不参与发布封包。");
        builder.AppendLine("- 命令引用候选来自 16 位词窗口扫描和现有数据表/文本/地图联动线索，只能作为核对入口。");
        builder.AppendLine("- 它不证明完整命令长度、参数布局或控制流已经确认，不能直接作为 R/S eex 完整写回依据。");
        builder.AppendLine("- 真正写回前必须使用测试副本、自动备份、写后复读校验和实机验证。");
    }

    private static string BuildJumpText(ScenarioCommandReferenceTarget target)
    {
        var parts = new List<string>();
        if (target.CanJumpDataTable)
        {
            parts.Add($"数据表：{target.TableName} / ID={target.RowId} / 字段={target.FieldName}");
        }

        if (target.CanJumpScenarioText)
        {
            var textPart = $"文本线索：{target.ScenarioFileName}";
            if (target.TextIndex.HasValue) textPart += $" / 文本#{target.TextIndex.Value}";
            if (!string.IsNullOrWhiteSpace(target.TextOffsetHex)) textPart += $" / 偏移={target.TextOffsetHex}";
            parts.Add(textPart);
        }

        if (target.CanJumpScenarioMap)
        {
            var mapPart = "地图联动";
            if (!string.IsNullOrWhiteSpace(target.MapId)) mapPart += $"：{target.MapId}";
            if (target.CoordinateX.HasValue && target.CoordinateY.HasValue) mapPart += $" / 坐标=({target.CoordinateX.Value},{target.CoordinateY.Value})";
            parts.Add(mapPart);
        }

        return string.Join("；", parts);
    }

    private static string BuildRawValueText(ScenarioCommandReferenceTarget target)
    {
        if (!target.RawValue.HasValue)
        {
            return string.Empty;
        }

        return $"{target.RawValue.Value} / 0x{target.RawValue.Value:X4}";
    }

    private static string SafeInline(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "（空）";
        }

        value = value.Replace("\r\n", " / ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ');
        return value.Length <= 240 ? value : value[..240] + "……";
    }
}
