using System.Text;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ScenarioCommandClipboardService
{
    public ScenarioCommandClipboardItem CreateClipboardItem(
        string scenarioFileName,
        ScenarioStructureRow row,
        IReadOnlyList<ScenarioCommandParameterRow> parameters)
    {
        return new ScenarioCommandClipboardItem
        {
            ScenarioFileName = scenarioFileName,
            SourceSceneIndex = row.SceneIndex,
            SourceSectionIndex = row.SectionIndex,
            SourceCommandIndex = row.CommandIndex,
            SourceOffsetHex = row.OffsetHex,
            CommandIdHex = row.CommandIdHex,
            CommandName = row.CommandName,
            ParameterPreview = row.ParameterPreview,
            CommandTemplateHint = row.CommandTemplateHint,
            ReferenceHint = row.ReferenceHint,
            Annotation = row.Annotation,
            Parameters = parameters.ToList()
        };
    }

    public string BuildCommandCopyText(
        string scenarioFileName,
        ScenarioStructureRow row,
        IReadOnlyList<ScenarioCommandParameterRow> parameters)
    {
        var builder = new StringBuilder();
        builder.AppendLine("CCZModStudio 剧本命令复制候选");
        builder.AppendLine($"剧本：{scenarioFileName}");
        builder.AppendLine($"位置：Scene {row.SceneIndex} / Section {row.SectionIndex} / Command {row.CommandIndex} / {row.OffsetHex}");
        builder.AppendLine($"命令：{row.CommandIdHex} {row.CommandName}");
        builder.AppendLine($"参数预览：{ValueOrDash(row.ParameterPreview)}");
        builder.AppendLine($"模板提示：{ValueOrDash(row.CommandTemplateHint)}");
        builder.AppendLine($"引用候选：{ValueOrDash(row.ReferenceHint)}");
        builder.AppendLine($"中文注释：{ValueOrDash(row.Annotation)}");
        builder.AppendLine();
        builder.AppendLine("参数表：");
        if (parameters.Count == 0)
        {
            builder.AppendLine("- 暂无可拆分参数；请结合旧剧本编辑器和相邻命令核对。");
        }
        else
        {
            foreach (var parameter in parameters.OrderBy(x => x.Index))
            {
                builder.AppendLine(
                    $"- P{parameter.Index} {parameter.SlotName}｜{parameter.Kind}｜{parameter.RawHex}/{parameter.DecimalValue}｜{parameter.DecodedValue}｜{parameter.Meaning}｜风险：{parameter.Risk}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("安全边界：以上内容是复制候选和中文注释，用于旧工具对照、策划记录、创作者备注和实机验证；尚未确认完整命令长度时，不作为直接写回命令结构的依据。");
        return builder.ToString().TrimEnd();
    }

    public string BuildPastePreview(
        ScenarioCommandClipboardItem source,
        string targetScenarioFileName,
        ScenarioStructureRow target,
        IReadOnlyList<ScenarioCommandParameterRow> targetParameters)
    {
        var builder = new StringBuilder();
        builder.AppendLine("CCZModStudio 剧本命令粘贴预览（不写入）");
        builder.AppendLine($"来源：{source.ScenarioFileName} Scene {source.SourceSceneIndex} / Section {source.SourceSectionIndex} / Command {source.SourceCommandIndex} / {source.SourceOffsetHex}");
        builder.AppendLine($"目标：{targetScenarioFileName} Scene {target.SceneIndex} / Section {target.SectionIndex} / Command {target.CommandIndex} / {target.OffsetHex}");
        builder.AppendLine();
        builder.AppendLine($"来源命令：{source.CommandIdHex} {source.CommandName}");
        builder.AppendLine($"目标命令：{target.CommandIdHex} {target.CommandName}");
        builder.AppendLine($"命令号：{(Same(source.CommandIdHex, target.CommandIdHex) ? "一致，可作为同类命令候选继续核对" : "不一致，只能作为参考，不能直接覆盖")}");
        builder.AppendLine($"来源参数预览：{ValueOrDash(source.ParameterPreview)}");
        builder.AppendLine($"目标参数预览：{ValueOrDash(target.ParameterPreview)}");
        builder.AppendLine();

        var max = Math.Max(source.Parameters.Count, targetParameters.Count);
        builder.AppendLine("参数差异：");
        if (max == 0)
        {
            builder.AppendLine("- 两侧都没有可拆分参数；请回到旧编辑器或实机核对命令边界。");
        }
        else
        {
            for (var i = 0; i < max; i++)
            {
                var s = i < source.Parameters.Count ? source.Parameters[i] : null;
                var t = i < targetParameters.Count ? targetParameters[i] : null;
                if (s == null)
                {
                    builder.AppendLine($"- P{i + 1} 目标额外：{t!.SlotName} {t.RawHex}/{t.DecimalValue} {t.DecodedValue}");
                    continue;
                }
                if (t == null)
                {
                    builder.AppendLine($"- P{i + 1} 来源额外：{s.SlotName} {s.RawHex}/{s.DecimalValue} {s.DecodedValue}");
                    continue;
                }

                var sameValue = Same(s.RawHex, t.RawHex) && s.DecimalValue == t.DecimalValue;
                builder.AppendLine(
                    $"- P{i + 1} {s.SlotName} -> {t.SlotName}：{(sameValue ? "相同" : "不同")}" +
                    $"｜来源 {s.RawHex}/{s.DecimalValue} {ValueOrDash(s.DecodedValue)}" +
                    $"｜目标 {t.RawHex}/{t.DecimalValue} {ValueOrDash(t.DecodedValue)}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("预览结论：当前只生成差异说明，不修改 SV/E5S。只有在命令长度、参数槽和目标语义都确认后，才允许把对应字段纳入安全写回。");
        return builder.ToString().TrimEnd();
    }

    private static string ValueOrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "（无）" : value;

    private static bool Same(string? left, string? right) =>
        string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
}
