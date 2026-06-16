using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 常见 R/S eex 命令参数模板表。
/// 这些模板用于把“后续 16 位词”解释成创作者可读的参数槽候选；当前仍是只读辅助，不作为完整命令写回依据。
/// </summary>
public sealed class ScenarioCommandParameterTemplateService
{
    private static readonly IReadOnlyDictionary<int, CommandTemplate> Templates = BuildTemplates();

    public string BuildTemplateDetail(
        ScenarioStructureRow row,
        CczProject? project = null,
        IReadOnlyList<HexTableDefinition>? tables = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine();

        if (row.NodeType != "Command候选")
        {
            builder.AppendLine("命令参数模板（只读候选）：该节点是 Scene/Section 层级节点，不按命令参数模板解释。");
            return builder.ToString().TrimEnd();
        }

        if (!TryParseCommandId(row.CommandIdHex, out var commandId))
        {
            builder.AppendLine($"命令参数模板（只读候选）：无法识别命令 ID“{row.CommandIdHex}”，仅保留泛化参数分组。");
            return builder.ToString().TrimEnd();
        }

        var words = ScenarioStructureParameterExtractor.ExtractLogicalWords(row).Take(16).ToList();
        var lookups = BuildNameLookups(project, tables);

        if (!Templates.TryGetValue(commandId, out var template))
        {
            builder.AppendLine($"命令参数模板（只读候选）：暂未为 {row.CommandIdHex} {row.CommandName} 建立专用模板。");
            builder.AppendLine("- 当前处理方式：请优先参考上方“参数分组解释”、同文件文本线索、原工具显示结果。");
            builder.AppendLine("- 后续计划：把更多高频命令逐步加入模板表；在真实参数长度未验证前，不开放完整命令树写回。");
            return builder.ToString().TrimEnd();
        }

        builder.AppendLine($"命令参数模板（只读候选）：{template.Name}（{row.CommandIdHex} {row.CommandName}）");
        builder.AppendLine($"- 模板用途：{template.Purpose}");
        builder.AppendLine($"- 风险边界：{template.Risk}");
        builder.AppendLine($"- 参数候选数量：{words.Count}；模板槽位：{template.Slots.Count}。如果槽位缺值，说明当前探针上下文不足或该命令实际参数更短。");

        if (words.Count == 0)
        {
            builder.AppendLine("- 当前行没有可解析的逻辑参数候选；请结合相邻命令、原始上下文词和旧剧本编辑器核对。");
            return builder.ToString().TrimEnd();
        }

        foreach (var slot in template.Slots)
        {
            builder.AppendLine(BuildSlotLine(slot, words, lookups, project));
        }

        if (words.Count > template.Slots.Count)
        {
            var remaining = words
                .Skip(template.Slots.Count)
                .Take(8)
                .Select((value, offset) => $"P{template.Slots.Count + offset + 1}={HexDisplayFormatter.FormatWord(value)}({value})")
                .ToList();
            builder.AppendLine("- 模板外剩余词：" + string.Join(" / ", remaining) + "；这些可能属于后续命令、变长参数或误扫上下文，暂不强行解释。");
        }

        builder.AppendLine("- 使用建议：先用模板判断“可能参数槽”，再到人物/物品/策略表、文本线索逐项核对；不要把模板结果直接当作可写回结构。");
        return builder.ToString().TrimEnd();
    }

    public string BuildShortHint(int commandId, string commandName)
    {
        if (!Templates.TryGetValue(commandId, out var template))
        {
            return $"暂无专用参数模板：{commandName}";
        }

        return $"{template.Name}：{string.Join("，", template.Slots.Take(5).Select(slot => slot.Name))}";
    }

    public bool HasTemplate(int commandId) => Templates.ContainsKey(commandId);

    public int TemplateCount => Templates.Count;

    public IReadOnlyList<ScenarioCommandParameterRow> BuildParameterRows(
        ScenarioStructureRow row,
        CczProject? project = null,
        IReadOnlyList<HexTableDefinition>? tables = null)
    {
        if (row.NodeType != "Command候选" || !TryParseCommandId(row.CommandIdHex, out var commandId))
        {
            return Array.Empty<ScenarioCommandParameterRow>();
        }

        var words = ScenarioStructureParameterExtractor.ExtractLogicalWords(row).Take(16).ToList();
        if (words.Count == 0)
        {
            return Array.Empty<ScenarioCommandParameterRow>();
        }

        var lookups = BuildNameLookups(project, tables);
        var result = new List<ScenarioCommandParameterRow>();
        if (Templates.TryGetValue(commandId, out var template))
        {
            foreach (var slot in template.Slots)
            {
                if (slot.Index >= words.Count)
                {
                    result.Add(new ScenarioCommandParameterRow
                    {
                        Index = slot.Index + 1,
                        SlotName = slot.Name,
                        Kind = BuildSlotKindName(slot.Kind),
                        RawHex = "<缺值>",
                        DecimalValue = -1,
                        DecodedValue = "当前参数预览缺少该槽位。",
                        Meaning = slot.Meaning,
                        Risk = slot.Risk,
                        FromTemplate = true,
                        Annotation = $"模板槽 P{slot.Index + 1}：{slot.Name}；当前预览缺值，不能据此写回。"
                    });
                    continue;
                }

                var value = words[slot.Index];
                var decoded = DecodeValue(slot, words, lookups, project);
                result.Add(new ScenarioCommandParameterRow
                {
                    Index = slot.Index + 1,
                    SlotName = slot.Name,
                    Kind = BuildSlotKindName(slot.Kind),
                    RawHex = HexDisplayFormatter.FormatWord(value),
                    DecimalValue = value,
                    DecodedValue = decoded,
                    Meaning = slot.Meaning,
                    Risk = slot.Risk,
                    FromTemplate = true,
                    Annotation = $"P{slot.Index + 1} {slot.Name}：{decoded}。{slot.Meaning} 风险：{slot.Risk}"
                });
            }

            foreach (var extra in words.Select((value, index) => new { value, index }).Where(x => template.Slots.All(slot => slot.Index != x.index)))
            {
                result.Add(BuildRawParameterRow(extra.index, extra.value, lookups, "模板外参数：可能属于后续命令、变长参数或误扫上下文，暂不写回。"));
            }

            return result.OrderBy(x => x.Index).ToList();
        }

        return words
            .Select((value, index) => BuildRawParameterRow(index, value, lookups, "暂无专用模板：仅作原始 16 位词候选显示。"))
            .ToList();
    }

    public string WriteTemplateCatalog(CczProject project, SceneStringDocument? dictionary = null)
    {
        var reportRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Reports");
        Directory.CreateDirectory(reportRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(reportRoot, $"{stamp}_RS命令参数模板目录.md");
        File.WriteAllText(reportPath, BuildTemplateCatalog(dictionary), Encoding.UTF8);
        return reportPath;
    }

    public string BuildTemplateCatalog(SceneStringDocument? dictionary = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# CCZModStudio R/S eex 命令参数模板目录");
        builder.AppendLine();
        builder.AppendLine($"- 生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"- 已内置模板：{Templates.Count} 条");
        if (dictionary != null)
        {
            var dictionaryCommandCount = dictionary.Commands.Count;
            var covered = dictionary.Commands.Count(command => Templates.ContainsKey(command.Id));
            var missing = dictionaryCommandCount - covered;
            builder.AppendLine($"- 命令字典：{dictionaryCommandCount} 条；已覆盖：{covered}；未覆盖：{missing}");
            builder.AppendLine($"- 字典来源：`{Escape(dictionary.SourcePath)}`");
        }
        else
        {
            builder.AppendLine("- 命令字典：未提供；本目录仅列出内置模板。");
        }
        builder.AppendLine();
        builder.AppendLine("> 安全边界：本目录解释的是“后续 16 位词”的常见参数槽候选，用于界面中文注释、核对记录、核对清单和旧工具对照；它不证明完整命令长度，也不作为 R/S eex 完整命令树写回依据。");
        builder.AppendLine();

        if (dictionary != null && dictionary.Commands.Count > 0)
        {
            builder.AppendLine("## 1. 命令字典覆盖表");
            builder.AppendLine();
            builder.AppendLine("| 命令 | 字典名称 | 模板状态 | 核对提示 |");
            builder.AppendLine("| --- | --- | --- | --- |");
            foreach (var command in dictionary.Commands.OrderBy(command => command.Id))
            {
                if (Templates.TryGetValue(command.Id, out var template))
                {
                    builder.AppendLine($"| {HexDisplayFormatter.Format(command.Id, 2)} | {Escape(command.Name)} | 已覆盖 | {Escape(template.Name)}：{Escape(string.Join("，", template.Slots.Take(4).Select(slot => slot.Name)))} |");
                }
                else
                {
                    builder.AppendLine($"| {HexDisplayFormatter.Format(command.Id, 2)} | {Escape(command.Name)} | 待补充 | 暂用通用参数分组和引用候选；需要结合旧工具与实机验证。 |");
                }
            }
            builder.AppendLine();
        }

        builder.AppendLine("## 2. 已知模板明细");
        builder.AppendLine();
        foreach (var template in Templates.Values.OrderBy(template => template.Id))
        {
            builder.AppendLine($"### {HexDisplayFormatter.Format(template.Id, 2)} {Escape(template.Name)}");
            builder.AppendLine();
            builder.AppendLine($"- 用途：{Escape(template.Purpose)}");
            builder.AppendLine($"- 风险边界：{Escape(template.Risk)}");
            builder.AppendLine();
            builder.AppendLine("| 槽位 | 名称 | 候选类型 | 创作者解释 | 风险提示 |");
            builder.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var slot in template.Slots)
            {
                builder.AppendLine($"| P{slot.Index + 1} | {Escape(slot.Name)} | {Escape(BuildSlotKindName(slot.Kind))} | {Escape(slot.Meaning)} | {Escape(slot.Risk)} |");
            }
            builder.AppendLine();
        }

        builder.AppendLine("## 3. 推荐使用方式");
        builder.AppendLine();
        builder.AppendLine("1. 在“R/S eex高级探针”中生成结构草图，优先筛选“仅有模板”和“高风险/需核对”。");
        builder.AppendLine("2. 选中命令后阅读节点详情中的“命令参数模板”，再用“命令引用”下拉框跳到人物、物品、策略、文本或地图预览核对。");
        builder.AppendLine("3. 对重要剧情命令保留外部制作记录，记录旧工具截图、修改意图、实机验证结果和备份文件。");
        builder.AppendLine("4. 模板缺失或模板外剩余词较多时，不要直接写回命令结构；先作为只读研究事项记录。");
        return builder.ToString();
    }

    public IReadOnlyList<ScenarioCommandTemplateCatalogItem> BuildCatalogItems(SceneStringDocument? dictionary = null)
    {
        var items = new List<ScenarioCommandTemplateCatalogItem>();
        if (dictionary != null && dictionary.Commands.Count > 0)
        {
            foreach (var command in dictionary.Commands.OrderBy(command => command.Id))
            {
                items.Add(Templates.TryGetValue(command.Id, out var template)
                    ? BuildCatalogItem(command.Id, command.Name, template)
                    : BuildMissingCatalogItem(command.Id, command.Name));
            }

            foreach (var template in Templates.Values
                         .Where(template => dictionary.Commands.All(command => command.Id != template.Id))
                         .OrderBy(template => template.Id))
            {
                items.Add(BuildCatalogItem(template.Id, string.Empty, template));
            }
        }
        else
        {
            items.AddRange(Templates.Values.OrderBy(template => template.Id).Select(template => BuildCatalogItem(template.Id, string.Empty, template)));
        }

        return items;
    }

    public string BuildCatalogItemDetail(ScenarioCommandTemplateCatalogItem item)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"R/S eex 命令模板：{item.IdHex} {ValueOrDash(item.DictionaryName)}");
        builder.AppendLine($"模板状态：{item.Status}    分类：{item.Category}    槽位数：{item.SlotCount}");
        builder.AppendLine($"模板名称：{ValueOrDash(item.TemplateName)}");
        builder.AppendLine();
        builder.AppendLine("用途说明：");
        builder.AppendLine(item.Purpose);
        builder.AppendLine();
        builder.AppendLine("槽位摘要：");
        builder.AppendLine(string.IsNullOrWhiteSpace(item.SlotSummary) ? "暂无专用槽位；当前只能按通用参数分组和引用候选核对。" : item.SlotSummary);
        builder.AppendLine();
        builder.AppendLine("槽位明细：");
        builder.AppendLine(string.IsNullOrWhiteSpace(item.SlotDetails) ? "暂无专用槽位明细。" : item.SlotDetails);
        builder.AppendLine();
        builder.AppendLine("核对提示：");
        builder.AppendLine(item.CreatorTip);
        builder.AppendLine();
        builder.AppendLine("风险边界：");
        builder.AppendLine(item.Risk);
        builder.AppendLine();
        builder.AppendLine("安全说明：");
        builder.AppendLine(item.SafetyNote);
        return builder.ToString();
    }

    private static ScenarioCommandTemplateCatalogItem BuildCatalogItem(int id, string dictionaryName, CommandTemplate template)
    {
        var slotSummary = string.Join("，", template.Slots.Select(slot => $"P{slot.Index + 1}{slot.Name}"));
        var slotDetails = string.Join(Environment.NewLine, template.Slots.Select(slot =>
            $"- P{slot.Index + 1} {slot.Name}｜{BuildSlotKindName(slot.Kind)}：{slot.Meaning} 风险：{slot.Risk}"));
        return new ScenarioCommandTemplateCatalogItem
        {
            Id = id,
            IdHex = HexDisplayFormatter.Format(id, 2),
            DictionaryName = dictionaryName,
            TemplateName = template.Name,
            Status = "已覆盖",
            Category = CategorizeCommand(template.Name + " " + template.Purpose),
            SlotCount = template.Slots.Count,
            SlotSummary = slotSummary,
            Purpose = template.Purpose,
            Risk = template.Risk,
            SlotDetails = slotDetails,
            CreatorTip = BuildCreatorTip(template),
            SafetyNote = "只读模板：用于中文注释、跳转核对、核对记录和报告，不证明完整命令长度，不作为 R/S eex 写回依据。"
        };
    }

    private static ScenarioCommandTemplateCatalogItem BuildMissingCatalogItem(int id, string dictionaryName)
        => new()
        {
            Id = id,
            IdHex = HexDisplayFormatter.Format(id, 2),
            DictionaryName = dictionaryName,
            TemplateName = "暂无专用模板",
            Status = "待补充",
            Category = CategorizeCommand(dictionaryName),
            SlotCount = 0,
            SlotSummary = string.Empty,
            Purpose = "当前命令已存在于 CczString.ini 字典，但尚未建立专用参数槽位模板。",
            Risk = "只能使用通用参数分组、命令引用候选、旧工具和实机表现进行核对；不要据此写回完整命令结构。",
            SlotDetails = string.Empty,
            CreatorTip = "建议在结构草图中筛选该命令，导出命令引用核对清单，记录旧工具截图、上下文和实机验证结果。",
            SafetyNote = "待补模板：当前只读研究，不写入游戏文件。"
        };

    private static string CategorizeCommand(string text)
    {
        if (ContainsAny(text, "对话", "信息", "旁白", "文本", "章名", "场所", "胜利条件", "地图文字")) return "剧情/文本";
        if (ContainsAny(text, "变量", "case", "else", "跳转", "测试", "概率", "流程")) return "流程/变量";
        if (ContainsAny(text, "武将", "人物", "我军", "敌军", "友军", "出场", "移动", "状态", "撤退", "复活", "兵种")) return "人物/战场单位";
        if (ContainsAny(text, "物品", "装备", "战利品", "奖励", "钱")) return "物品/奖励";
        if (ContainsAny(text, "地图", "坐标", "区域", "天气", "障碍", "视点", "高亮")) return "地图/坐标/战场";
        if (ContainsAny(text, "单挑")) return "单挑";
        if (ContainsAny(text, "法术", "特效", "动画", "音效", "CD", "音乐")) return "演出/音画";
        if (ContainsAny(text, "存档", "Game Over", "结局", "剧本")) return "系统/路线";
        return "其他/待研究";
    }

    private static string BuildCreatorTip(CommandTemplate template)
    {
        var category = CategorizeCommand(template.Name + " " + template.Purpose);
        return category switch
        {
            "剧情/文本" => "优先跳到“文本线索”核对 GBK 容量；如要改对白，尽量只做原容量内短写回并保留实机截图。",
            "流程/变量" => "先记录上下文、前后 case/else/跳转，再实机触发；不要只按单行参数判断流程。",
            "人物/战场单位" => "跳到人物表、实机战场核对人物 ID、出场状态、坐标、朝向和 AI。",
            "物品/奖励" => "跳到物品表核对 ID、名称、特效和奖励设计；发布前实机确认获得/丢弃/战利品结果。",
            "地图/坐标/战场" => "配合地图预览 PNG 和 Hexzmap 地形格核对坐标范围。",
            "单挑" => "按单挑开始、出场、对话、动作、胜负显示的顺序核对，并记录实机录像或截图。",
            "演出/音画" => "结合资源索引、EEX/Ls 探针、音效/BGM 和实机画面核对，不直接替换资源内部结构。",
            "系统/路线" => "涉及结局、剧本跳转、Game Over 或存档开关时，必须记录路线设计并做完整流程测试。",
            _ => "作为只读研究项处理；为不确定参数补充外部制作记录、旧工具截图和实机验证。"
        };
    }

    private static bool ContainsAny(string text, params string[] keywords)
        => keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static string BuildSlotLine(
        TemplateSlot slot,
        IReadOnlyList<int> words,
        NameLookups lookups,
        CczProject? project)
    {
        var label = $"- P{slot.Index + 1} {slot.Name}";
        if (slot.Index >= words.Count)
        {
            return $"{label}：<当前预览缺值>；{slot.Meaning} 风险：{slot.Risk}";
        }

        var value = words[slot.Index];
        var valueText = $"{HexDisplayFormatter.FormatWord(value)}({value})";
        var decoded = DecodeValue(slot, words, lookups, project);
        var riskText = string.IsNullOrWhiteSpace(slot.Risk) ? string.Empty : $"；风险：{slot.Risk}";
        return $"{label}：{valueText} => {decoded}；{slot.Meaning}{riskText}";
    }

    private static ScenarioCommandParameterRow BuildRawParameterRow(int index, int value, NameLookups lookups, string risk)
        => new()
        {
            Index = index + 1,
            SlotName = $"P{index + 1}",
            Kind = "原始 16 位词",
            RawHex = HexDisplayFormatter.FormatWord(value),
            DecimalValue = value,
            DecodedValue = DecodeGeneralValue(value, lookups),
            Meaning = "未命中专用槽位；用于人工核对命令上下文。",
            Risk = risk,
            FromTemplate = false,
            Annotation = $"P{index + 1} 原始值 {value}；{risk}"
        };

    private static string DecodeValue(
        TemplateSlot slot,
        IReadOnlyList<int> words,
        NameLookups lookups,
        CczProject? project)
    {
        var value = words[slot.Index];
        return slot.Kind switch
        {
            SlotKind.EventId => $"子事件/流程块编号候选 #{value}",
            SlotKind.VariableId => $"变量/开关编号候选 #{value}",
            SlotKind.CompareOperator => DecodeCompareOperator(value),
            SlotKind.Value => DecodeGeneralValue(value, lookups),
            SlotKind.Person => DecodeNamed(value, lookups.PersonNames, "人物/部队"),
            SlotKind.Item => DecodeNamed(value, lookups.ItemNames, "物品/装备"),
            SlotKind.Strategy => DecodeNamed(value, lookups.StrategyNames, "策略"),
            SlotKind.CoordinateX => DecodeCoordinate(words, slot.Index, isX: true),
            SlotKind.CoordinateY => DecodeCoordinate(words, slot.Index, isX: false),
            SlotKind.MapId => DecodeMapId(value, project),
            SlotKind.TextIndex => $"文本/消息/内部字符串编号候选 #{value}；请在“文本线索”页按标题、对白、说明核对",
            SlotKind.BranchTarget => $"跳转目标/条件成立后流程块候选 #{value}",
            SlotKind.BooleanFlag => value switch
            {
                0 => "关闭/否/不启用 候选",
                1 => "开启/是/启用 候选",
                _ => $"枚举标志候选 {value}"
            },
            SlotKind.Direction => DecodeDirection(value),
            SlotKind.Count => $"数量/上限/序号候选 {value}",
            SlotKind.AbilityKind => DecodeAbilityKind(value),
            SlotKind.UnitStatus => DecodeUnitStatus(value),
            SlotKind.AiPolicy => DecodeAiPolicy(value),
            SlotKind.Weather => DecodeWeather(value),
            SlotKind.SoundEffect => $"音效编号候选 #{value}；请结合实机声音、旧工具音效列表或资源文件核对",
            SlotKind.MusicTrack => $"音乐/CD轨道候选 #{value}；请结合战场 BGM、旧工具 CD 音轨列表或资源文件核对",
            SlotKind.ForceSide => value switch
            {
                0 => "阵营候选：我军/己方",
                1 => "阵营候选：敌军",
                2 => "阵营候选：友军/中立",
                _ => $"阵营/队列枚举候选 {value}"
            },
            SlotKind.BattleResult => value switch
            {
                0 => "胜负/显示结果候选：未定或平局",
                1 => "胜负/显示结果候选：胜利/胜者",
                2 => "胜负/显示结果候选：失败/败者",
                _ => $"胜负/显示枚举候选 {value}"
            },
            SlotKind.Operation => DecodeOperation(value),
            _ => $"原始数值候选 {value}"
        };
    }

    private static string BuildSlotKindName(SlotKind kind) => kind switch
    {
        SlotKind.Raw => "原始值",
        SlotKind.EventId => "子事件/流程块",
        SlotKind.VariableId => "变量/开关",
        SlotKind.CompareOperator => "比较方式",
        SlotKind.Value => "通用数值",
        SlotKind.Person => "人物/部队",
        SlotKind.Item => "物品/装备",
        SlotKind.Strategy => "策略/法术",
        SlotKind.CoordinateX => "地图 X 坐标",
        SlotKind.CoordinateY => "地图 Y 坐标",
        SlotKind.MapId => "地图编号",
        SlotKind.TextIndex => "文本/消息编号",
        SlotKind.BranchTarget => "流程跳转目标",
        SlotKind.BooleanFlag => "布尔/标志",
        SlotKind.Direction => "方向/朝向",
        SlotKind.Count => "数量/序号",
        SlotKind.ForceSide => "阵营/行动方",
        SlotKind.BattleResult => "胜负/结果",
        SlotKind.Operation => "运算/设置方式",
        SlotKind.AbilityKind => "能力/属性类别",
        SlotKind.UnitStatus => "状态/异常类别",
        SlotKind.AiPolicy => "AI 方针",
        SlotKind.Weather => "天气/环境",
        SlotKind.SoundEffect => "音效编号",
        SlotKind.MusicTrack => "音乐/CD轨道",
        _ => kind.ToString()
    };

    private static string DecodeCompareOperator(int value) => value switch
    {
        0 => "比较方式候选：等于",
        1 => "比较方式候选：不等于",
        2 => "比较方式候选：大于",
        3 => "比较方式候选：小于",
        4 => "比较方式候选：大于等于",
        5 => "比较方式候选：小于等于",
        _ => $"比较/条件枚举候选 {value}"
    };

    private static string DecodeOperation(int value) => value switch
    {
        0 => "操作候选：直接赋值/设置",
        1 => "操作候选：增加",
        2 => "操作候选：减少",
        3 => "操作候选：乘/叠加枚举",
        4 => "操作候选：除/清除枚举",
        _ => $"运算/设置方式枚举候选 {value}"
    };

    private static string DecodeDirection(int value) => value switch
    {
        0 => "方向候选：上/北",
        1 => "方向候选：右/东",
        2 => "方向候选：下/南",
        3 => "方向候选：左/西",
        _ => $"方向/朝向枚举候选 {value}"
    };

    private static string DecodeAbilityKind(int value) => value switch
    {
        0 => "能力候选：攻击",
        1 => "能力候选：防御",
        2 => "能力候选：精神",
        3 => "能力候选：爆发",
        4 => "能力候选：士气",
        5 => "能力候选：HP",
        6 => "能力候选：MP",
        7 => "能力候选：当前 HP",
        8 => "能力候选：当前 MP",
        9 => "能力候选：等级",
        10 => "能力候选：武力",
        11 => "能力候选：统率",
        12 => "能力候选：智力",
        13 => "能力候选：敏捷",
        14 => "能力候选：运气",
        _ => $"能力/属性枚举候选 {value}"
    };

    private static string DecodeUnitStatus(int value) => value switch
    {
        0 => "状态候选：麻痹",
        1 => "状态候选：封咒",
        2 => "状态候选：混乱",
        3 => "状态候选：中毒",
        4 => "状态候选：怯力",
        5 => "状态候选：迷失",
        _ => $"战场状态/异常枚举候选 {value}"
    };

    private static string DecodeAiPolicy(int value) => value switch
    {
        0 => "AI 方针候选：被动出击",
        1 => "AI 方针候选：主动出击",
        2 => "AI 方针候选：坚守原地",
        3 => "AI 方针候选：攻击武将",
        4 => "AI 方针候选：到指定点",
        5 => "AI 方针候选：跟随武将",
        6 => "AI 方针候选：逃到指定点",
        _ => $"AI 方针枚举候选 {value}"
    };

    private static string DecodeWeather(int value) => value switch
    {
        0 => "天气/环境候选：晴或默认",
        1 => "天气/环境候选：阴/云",
        2 => "天气/环境候选：雨",
        3 => "天气/环境候选：雪",
        4 => "天气/环境候选：风/特殊",
        _ => $"天气/环境枚举候选 {value}"
    };

    private static string DecodeGeneralValue(int value, NameLookups lookups)
    {
        var parts = new List<string> { $"数值 {value}" };
        if (lookups.PersonNames.TryGetValue(value, out var person)) parts.Add($"人物:{person}");
        if (lookups.ItemNames.TryGetValue(value, out var item)) parts.Add($"物品:{item}");
        if (lookups.StrategyNames.TryGetValue(value, out var strategy)) parts.Add($"策略:{strategy}");
        if (value <= 0x00FF) parts.Add("小整数/开关/枚举候选");
        return string.Join("；", parts);
    }

    private static string DecodeNamed(int value, IReadOnlyDictionary<int, string> names, string label)
    {
        return names.TryGetValue(value, out var name)
            ? $"{label}{value}:{name}"
            : $"{label}编号候选 {value}（未在当前表中命中名称）";
    }

    private static string DecodeCoordinate(IReadOnlyList<int> words, int index, bool isX)
    {
        var value = words[index];
        var otherIndex = isX ? index + 1 : index - 1;
        var pairText = otherIndex >= 0 && otherIndex < words.Count
            ? isX
                ? $"坐标对候选 ({value},{words[otherIndex]})"
                : $"坐标对候选 ({words[otherIndex]},{value})"
            : isX ? $"X 坐标候选 {value}" : $"Y 坐标候选 {value}";
        var boundsText = value <= 60 ? "；数值落在常见 30x30/60x60 地图坐标观察范围" : "；数值偏大，可能不是坐标";
        return pairText + boundsText;
    }

    private static string DecodeMapId(int value, CczProject? project)
    {
        var mapId = "M" + value.ToString("000", CultureInfo.InvariantCulture);
        var same = false ? string.Empty : string.Empty;
        var exists = string.Empty;
        if (project != null)
        {
            var mapRoot = project.ResolveGameFile("Map");
            var found = new[] { ".jpg", ".JPG", ".jpeg", ".JPEG", ".png", ".PNG" }
                .Select(ext => Path.Combine(mapRoot, mapId + ext))
                .FirstOrDefault(File.Exists);
            exists = found == null ? "；地图图片未命中" : $"；地图图片存在:{Path.GetFileName(found)}";
        }
        return $"{mapId} 地图编号候选{same}{exists}";
    }

    private static bool TryParseCommandId(string commandIdHex, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(commandIdHex)) return false;
        var text = commandIdHex.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static IReadOnlyDictionary<int, CommandTemplate> BuildTemplates()
    {
        var templates = new List<CommandTemplate>
        {
            T(0x01, "子事件设定", "用于开启、关闭或切换子事件/流程块，常见于关卡触发条件初始化。",
                "不同版本子事件编号和标志位可能有差异；必须结合事件树上下文确认。",
                S(0, "子事件编号", SlotKind.EventId, "指向某个子事件/流程块的候选编号。"),
                S(1, "启用标志", SlotKind.BooleanFlag, "常见为启用/禁用或触发状态。"),
                S(2, "触发阶段/状态", SlotKind.Value, "可能是阶段、状态或局部枚举。"),
                S(3, "后续流程", SlotKind.BranchTarget, "可能指向条件满足后的事件块。")),

            T(0x02, "内部信息", "用于内部消息、剧情提示或调试/系统信息类显示，是查找文本线索的重要入口。",
                "编号不一定等于文本偏移；请以同文件 GBK 文本线索和实机显示为准。",
                S(0, "信息编号", SlotKind.TextIndex, "可能指向内部消息或文本表候选。"),
                S(1, "显示方式", SlotKind.BooleanFlag, "可能控制是否等待、是否弹窗或是否显示。"),
                S(2, "关联人物/头像", SlotKind.Person, "若命令带头像或说话人，该槽可能是人物候选。"),
                S(3, "流程/标志", SlotKind.Value, "可能是消息后的流程标志或局部变量。")),

            T(0x03, "else", "用于条件分支的 else 部分，辅助理解 if/test/case 结构。",
                "else 往往是结构标记，后续词可能属于下一条命令，模板只作定位提示。",
                S(0, "跳转/块编号", SlotKind.BranchTarget, "可能指向 else 分支或结束位置。"),
                S(1, "分支层级/标志", SlotKind.Value, "可能是条件层级或结构标志。")),

            T(0x05, "变量测试", "用于测试变量、开关或流程值是否满足条件，是剧情分支的核心命令。",
                "比较方式和跳转目标需结合前后 case/else/结束命令确认。",
                S(0, "变量编号", SlotKind.VariableId, "被测试的变量/开关候选。"),
                S(1, "比较方式", SlotKind.CompareOperator, "等于/不等于/大小比较等候选。"),
                S(2, "比较值", SlotKind.Value, "与变量比较的目标值。"),
                S(3, "成立后跳转", SlotKind.BranchTarget, "条件成立后的流程块或偏移候选。")),

            T(0x06, "我军出场限制", "用于限制或设置我军可出场状态，常见于战前选择和剧情强制出场。",
                "人物编号、出场位和限制标志在不同命令族中容易混淆；请结合人物表和战前界面验证。",
                S(0, "我军人物/出场位", SlotKind.Person, "我军人物、部队或出场位候选。"),
                S(1, "允许/禁止标志", SlotKind.BooleanFlag, "可能表示能否出场或是否强制。"),
                S(2, "数量/位置", SlotKind.Count, "可能是出场数量、位置或队列序号。")),

            T(0x07, "出战测试", "用于判断某人物或出场位是否参与当前战斗。",
                "出战槽可能是人物 ID、出场序号或队列位置，需结合战前选择界面验证。",
                S(0, "人物/出战位", SlotKind.Person, "被检测的人物或出战槽候选。"),
                S(1, "比较方式", SlotKind.CompareOperator, "出战状态比较方式候选。"),
                S(2, "成立后跳转", SlotKind.BranchTarget, "满足条件后的流程块候选。")),

            T(0x09, "延时", "用于剧情演出中的等待、停顿或节奏控制。",
                "延时时间单位未完全确认，过大或过小都可能影响演出节奏。",
                S(0, "等待时间", SlotKind.Count, "等待帧数、时间片或速度候选。"),
                S(1, "等待方式", SlotKind.BooleanFlag, "是否阻塞、是否允许动画继续的候选标志。")),

            T(0x0A, "初始化局部变量", "用于初始化局部变量、临时开关或事件内状态。",
                "局部变量作用域尚未完全确认，修改前应记录旧工具结果和实机验证。",
                S(0, "局部变量编号", SlotKind.VariableId, "局部变量/开关候选。"),
                S(1, "初始值", SlotKind.Value, "初始化数值候选。"),
                S(2, "初始化方式", SlotKind.Operation, "清零、赋值或批量初始化方式候选。")),

            T(0x0B, "变量赋值", "用于给变量/开关赋值或运算，是剧情状态变化的关键命令。",
                "运算方式枚举尚未完全验证，修改前必须用旧工具和实机回放确认。",
                S(0, "变量编号", SlotKind.VariableId, "被写入的变量/开关候选。"),
                S(1, "赋值/运算方式", SlotKind.Operation, "直接赋值、增加、减少等候选。"),
                S(2, "新值/来源值", SlotKind.Value, "写入值或参与运算的值。"),
                S(3, "作用域/后续标志", SlotKind.Value, "可能是作用域、局部/全局标志或后续控制。")),

            T(0x0E, "战斗失败", "用于触发失败分支、失败演出或失败结算。",
                "失败命令通常会中断流程或跳出战斗，必须结合胜败条件和实机验证。",
                S(0, "失败原因/分支", SlotKind.Value, "失败原因或失败分支枚举候选。"),
                S(1, "失败文本/演出", SlotKind.TextIndex, "失败提示或演出文本候选。"),
                S(2, "后续跳转", SlotKind.BranchTarget, "失败后的流程或结局跳转候选。")),

            T(0x0F, "结局设定", "用于设置结局、章节结果或后续路线。",
                "结局/路线编号一旦错用会影响后续关卡与剧情，发布前必须完整通关验证。",
                S(0, "结局/路线编号", SlotKind.Value, "结局、路线或分支编号候选。"),
                S(1, "角色/阵营", SlotKind.Person, "可能关联结局展示角色或阵营对象。"),
                S(2, "保存标志", SlotKind.BooleanFlag, "是否写入结局状态或存档标志候选。")),

            T(0x11, "剧本跳转", "用于切换到其他 SV 剧本、关卡或剧情段。",
                "跳转编号需与 R/S eex 文件、关卡列表和路线设计共同核对。",
                S(0, "目标剧本/关卡", SlotKind.MapId, "目标关卡或剧本编号候选，常与 SVxxx/Mxxx 同号线索相关。"),
                S(1, "进入方式", SlotKind.Value, "直接进入、战斗前后或剧情模式候选。"),
                S(2, "保留状态", SlotKind.BooleanFlag, "是否保留队伍/变量/剧情状态候选。")),

            T(0x12, "选择框", "用于弹出选项或选择菜单，常与 case/else 分支配合。",
                "选项文本通常另存于同文件文本区，参数只作为编号/流程候选。",
                S(0, "选择框编号", SlotKind.TextIndex, "选项组或提示文本候选。"),
                S(1, "选项数量", SlotKind.Count, "可选项数量候选。"),
                S(2, "默认项/标志", SlotKind.Value, "默认项或显示方式候选。")),

            T(0x13, "case", "用于选择框或变量测试后的 case 分支。",
                "case 常作为结构标记，后续词可能已进入下一命令。",
                S(0, "case 值", SlotKind.Value, "当前分支匹配的值。"),
                S(1, "跳转/块编号", SlotKind.BranchTarget, "分支目标或块编号候选。")),

            T(0x14, "对话", "用于常规对白显示，常关联说话人、头像、文本编号。",
                "文本编号未确认时，应优先按“文本线索”页修改原容量内短文本。",
                S(0, "说话人/头像", SlotKind.Person, "人物或头像候选。"),
                S(1, "文本编号", SlotKind.TextIndex, "对白文本候选。"),
                S(2, "表情/朝向", SlotKind.Direction, "表情、方向或头像状态枚举候选。"),
                S(3, "显示标志", SlotKind.BooleanFlag, "等待/自动/窗口样式标志候选。")),

            T(0x15, "对话2", "用于另一种对白窗口或扩展对白显示。",
                "对话2 与普通对话的槽位可能存在差异，需以旧工具和实机窗口样式验证。",
                S(0, "说话人/头像", SlotKind.Person, "人物、头像或地图头像候选。"),
                S(1, "文本编号", SlotKind.TextIndex, "对白文本候选。"),
                S(2, "表情/窗口样式", SlotKind.Value, "头像表情、窗口位置或样式候选。"),
                S(3, "显示标志", SlotKind.BooleanFlag, "等待/自动/清屏候选标志。")),

            T(0x16, "信息", "用于无头像信息框、提示或战场说明。",
                "参数不等同于 GBK 文本偏移；请结合文本线索核对。",
                S(0, "信息文本编号", SlotKind.TextIndex, "信息框文本候选。"),
                S(1, "显示方式", SlotKind.BooleanFlag, "窗口/等待/清屏标志候选。")),

            T(0x17, "场所名", "用于显示地点、战场名或场景标题。",
                "场所名通常在同文件文本线索中出现，改名优先走文本线索短写回。",
                S(0, "场所文本编号", SlotKind.TextIndex, "场所名/地点名文本候选。"),
                S(1, "显示方式", SlotKind.BooleanFlag, "显示/等待/淡入候选标志。")),

            T(0x18, "事件名称设定", "用于设置当前事件、章节或小节标题。",
                "事件名称可能影响存档提示或 UI 标题，需对照文本线索与实机界面。",
                S(0, "事件名文本编号", SlotKind.TextIndex, "事件标题文本候选。"),
                S(1, "保存/显示标志", SlotKind.BooleanFlag, "是否显示或保存标题的候选标志。")),

            T(0x19, "胜利条件", "用于设置或切换胜利条件文本/规则。",
                "胜利条件的显示文本与实际判定可能分离，必须同时验证条件命令和实机胜利触发。",
                S(0, "胜利条件文本", SlotKind.TextIndex, "胜利条件文本候选。"),
                S(1, "条件类型", SlotKind.Value, "歼灭、到达、回合、保护等类型候选。"),
                S(2, "关联人物/地点", SlotKind.Value, "可能关联人物 ID、地点或回合数。")),

            T(0x1A, "显示胜利条件", "用于弹出或刷新胜利/失败条件显示。",
                "该命令可能只控制显示，不等同于修改判定逻辑。",
                S(0, "显示标志", SlotKind.BooleanFlag, "显示/隐藏/刷新候选标志。"),
                S(1, "文本编号", SlotKind.TextIndex, "胜败条件文本候选。")),

            T(0x1B, "撤退信息是否显示设定", "用于控制撤退提示、死亡提示或战场信息显示。",
                "关闭提示可能隐藏重要演出反馈，应在实机中验证。",
                S(0, "显示标志", SlotKind.BooleanFlag, "显示或隐藏撤退信息的候选标志。"),
                S(1, "作用对象/阵营", SlotKind.ForceSide, "作用于我军、敌军或全体的候选枚举。")),

            T(0x1F, "地图视点切换", "用于把镜头移动到地图坐标或目标单位。",
                "坐标和人物目标可能共用槽位，需结合地图画面验证。",
                S(0, "X坐标/目标", SlotKind.CoordinateX, "地图视点 X 或目标编号候选。"),
                S(1, "Y坐标", SlotKind.CoordinateY, "地图视点 Y 坐标候选。"),
                S(2, "移动速度/等待", SlotKind.Count, "镜头移动速度或等待标志候选。")),

            T(0x20, "武将头像状态设置", "用于设置对话头像、人物头像状态或表情。",
                "头像状态枚举需结合头像资源和实机对白窗口验证。",
                S(0, "人物/头像", SlotKind.Person, "人物或头像编号候选。"),
                S(1, "头像状态", SlotKind.Value, "普通、惊讶、愤怒、欣喜等状态候选。"),
                S(2, "显示/保留标志", SlotKind.BooleanFlag, "是否立即显示或保留状态候选。")),

            T(0x21, "战场物体添加", "用于在地图上添加障碍、宝箱、门、剧情物体或装饰对象。",
                "物体编号、地形阻挡和图像资源关系未完全确认，修改后必须导出地图预览并实机验证。",
                S(0, "物体编号/类型", SlotKind.Value, "战场物体类型或对象编号候选。"),
                S(1, "X坐标", SlotKind.CoordinateX, "物体放置 X 坐标候选。"),
                S(2, "Y坐标", SlotKind.CoordinateY, "物体放置 Y 坐标候选。"),
                S(3, "状态/朝向", SlotKind.Value, "开启、关闭、方向或显示状态候选。")),

            T(0x22, "动画", "用于播放战场动画、剧情动画或效果演出。",
                "动画编号与效果资源关系尚需旧工具和实机对照，不直接替代资源编辑。",
                S(0, "动画编号", SlotKind.Value, "动画/演出编号候选。"),
                S(1, "X坐标/目标", SlotKind.CoordinateX, "动画位置 X 或目标编号候选。"),
                S(2, "Y坐标", SlotKind.CoordinateY, "动画位置 Y 坐标候选。"),
                S(3, "等待标志", SlotKind.BooleanFlag, "是否等待动画结束候选。")),

            T(0x23, "音效", "用于播放音效或战场提示音。",
                "音效编号需要结合旧工具列表、音频资源和实机听感核对。",
                S(0, "音效编号", SlotKind.SoundEffect, "播放的音效编号候选。"),
                S(1, "播放方式", SlotKind.Value, "音量、循环、通道或等待方式候选。")),

            T(0x24, "CD音轨", "用于切换背景音乐或播放指定音轨。",
                "音乐编号需结合游戏资源和实机听感核对，避免误改战场 BGM。",
                S(0, "音乐/CD轨道", SlotKind.MusicTrack, "目标音乐或 CD 音轨候选。"),
                S(1, "播放方式", SlotKind.Value, "循环、淡入淡出、停止或等待方式候选。")),

            T(0x25, "武将进入指定地点测试", "用于判断某个武将是否到达指定坐标点，常见于触发剧情。",
                "人物编号与坐标槽若错位会导致误判；应对照地图和事件触发时机。",
                S(0, "武将/部队编号", SlotKind.Person, "被检测的武将或部队候选。"),
                S(1, "目标X坐标", SlotKind.CoordinateX, "指定地点 X 坐标候选。"),
                S(2, "目标Y坐标", SlotKind.CoordinateY, "指定地点 Y 坐标候选。"),
                S(3, "成立后跳转", SlotKind.BranchTarget, "进入地点后触发的流程块候选。")),

            T(0x26, "武将进入指定区域测试", "用于判断武将是否进入矩形/区域范围。",
                "区域通常至少需要两组坐标；当前预览只显示候选槽，不能确认完整长度。",
                S(0, "武将/部队编号", SlotKind.Person, "被检测的武将或部队候选。"),
                S(1, "区域X1", SlotKind.CoordinateX, "区域起点 X 候选。"),
                S(2, "区域Y1", SlotKind.CoordinateY, "区域起点 Y 候选。"),
                S(3, "区域X2", SlotKind.CoordinateX, "区域终点 X 候选。"),
                S(4, "区域Y2", SlotKind.CoordinateY, "区域终点 Y 候选。")),

            T(0x27, "背景显示", "用于显示剧情背景、场景底图或过场画面。",
                "背景编号与图片/资源文件关系未完全确认，建议结合资源索引和实机截图核对。",
                S(0, "背景编号", SlotKind.Value, "背景图或过场资源编号候选。"),
                S(1, "显示方式", SlotKind.Value, "淡入、居中、拉伸或叠加方式候选。"),
                S(2, "等待标志", SlotKind.BooleanFlag, "是否等待显示完成候选。")),

            T(0x29, "地图头像显示", "用于在地图或剧情画面显示头像/小头像。",
                "地图头像与对话头像、人物编号可能不同，需结合画面验证。",
                S(0, "人物/头像", SlotKind.Person, "显示的人物或头像候选。"),
                S(1, "X坐标", SlotKind.CoordinateX, "头像显示 X 坐标候选。"),
                S(2, "Y坐标", SlotKind.CoordinateY, "头像显示 Y 坐标候选。"),
                S(3, "状态/表情", SlotKind.Value, "头像状态或显示层级候选。")),

            T(0x2A, "地图头像移动", "用于移动地图头像或剧情头像。",
                "移动速度和坐标单位需实机验证，避免头像移出画面。",
                S(0, "人物/头像", SlotKind.Person, "被移动的头像候选。"),
                S(1, "目标X坐标", SlotKind.CoordinateX, "目标 X 坐标候选。"),
                S(2, "目标Y坐标", SlotKind.CoordinateY, "目标 Y 坐标候选。"),
                S(3, "速度/等待", SlotKind.Count, "移动速度或等待标志候选。")),

            T(0x2B, "地图头像消失", "用于隐藏或移除地图头像。",
                "若与后续显示命令配套，需按演出顺序核对。",
                S(0, "人物/头像", SlotKind.Person, "需要消失的头像候选。"),
                S(1, "消失方式", SlotKind.Value, "淡出、立即隐藏或保留状态候选。")),

            T(0x2C, "地图文字显示", "用于在地图/战场画面显示文字提示。",
                "文字内容仍需通过文本线索核对，坐标槽需要实机画面对照。",
                S(0, "文字编号", SlotKind.TextIndex, "地图文字文本候选。"),
                S(1, "X坐标", SlotKind.CoordinateX, "文字显示 X 坐标候选。"),
                S(2, "Y坐标", SlotKind.CoordinateY, "文字显示 Y 坐标候选。"),
                S(3, "显示方式", SlotKind.Value, "颜色、停留时间或层级候选。")),

            T(0x2D, "武将点击测试", "用于判断玩家是否点击指定武将或对象。",
                "点击对象可能是人物、地图头像或战场单位，需结合触发场景验证。",
                S(0, "人物/对象", SlotKind.Person, "被点击的人物或对象候选。"),
                S(1, "比较/状态", SlotKind.CompareOperator, "点击状态判断候选。"),
                S(2, "成立后跳转", SlotKind.BranchTarget, "点击成立后的流程块候选。")),

            T(0x2E, "武将相邻测试", "用于判断两个武将或单位是否相邻。",
                "相邻判定可能与战场格、阵营和存活状态有关，需实机验证。",
                S(0, "武将A", SlotKind.Person, "第一个武将/单位候选。"),
                S(1, "武将B", SlotKind.Person, "第二个武将/单位候选。"),
                S(2, "成立后跳转", SlotKind.BranchTarget, "相邻成立后的流程块候选。")),

            T(0x2F, "清除人物", "用于移除、清空或隐藏指定人物/单位。",
                "清除人物可能影响战场人数、胜败条件和后续出场，必须保存备份并实机验证。",
                S(0, "人物/部队编号", SlotKind.Person, "被清除的人物或部队候选。"),
                S(1, "清除方式", SlotKind.Value, "离场、隐藏、删除或复位方式候选。")),

            T(0x30, "武将出现", "用于让战场武将/部队出现在指定坐标。",
                "出现、复活、隐藏武将出现等命令参数相似，需结合命令名区分。",
                S(0, "武将/部队编号", SlotKind.Person, "出现的武将或部队候选。"),
                S(1, "X坐标", SlotKind.CoordinateX, "出现位置 X 候选。"),
                S(2, "Y坐标", SlotKind.CoordinateY, "出现位置 Y 候选。"),
                S(3, "朝向", SlotKind.Direction, "出现后的朝向候选。")),

            T(0x31, "武将消失", "用于让指定武将从战场或剧情画面消失。",
                "消失不一定等同撤退或阵亡，可能只影响显示；需结合战场人数和胜败条件验证。",
                S(0, "武将/部队编号", SlotKind.Person, "消失的武将或部队候选。"),
                S(1, "消失方式", SlotKind.Value, "隐藏、退场、保留状态等方式候选。")),

            T(0x32, "武将移动", "用于移动指定武将到坐标或沿路线移动。",
                "移动命令可能有路线、速度、等待等变长参数，当前仅解释前几个常见槽。",
                S(0, "武将/部队编号", SlotKind.Person, "被移动的武将或部队候选。"),
                S(1, "目标X坐标", SlotKind.CoordinateX, "目标 X 坐标候选。"),
                S(2, "目标Y坐标", SlotKind.CoordinateY, "目标 Y 坐标候选。"),
                S(3, "速度/等待", SlotKind.Count, "移动速度、等待或路线标志候选。")),

            T(0x33, "武将转向", "用于设置武将朝向。",
                "朝向枚举需以实机画面验证。",
                S(0, "武将/部队编号", SlotKind.Person, "目标武将或部队候选。"),
                S(1, "朝向", SlotKind.Direction, "朝向候选。")),

            T(0x34, "武将动作", "用于播放指定武将动作、攻击、受击或剧情动作。",
                "动作编号与 R/S 动作资源有关，需结合 EEX/R/S 资源和实机动画验证。",
                S(0, "武将/部队编号", SlotKind.Person, "执行动作的武将或部队候选。"),
                S(1, "动作编号", SlotKind.Value, "攻击、防御、特殊动作或剧情动作编号候选。"),
                S(2, "等待标志", SlotKind.BooleanFlag, "是否等待动作结束候选。")),

            T(0x35, "武将形象改变", "用于切换战场形象或特殊形象。",
                "形象编号应与 R/S 形象指定器和资源索引共同核对。",
                S(0, "武将/部队编号", SlotKind.Person, "目标武将或部队候选。"),
                S(1, "R/S形象编号", SlotKind.Value, "新形象编号候选。"),
                S(2, "刷新/保留标志", SlotKind.BooleanFlag, "是否立即刷新或保留状态候选。")),

            T(0x36, "武将状态测试", "用于测试武将是否处于某状态、异常或战场标志。",
                "状态枚举和比较方式需结合异常状态图标、战场变量和实机结果验证。",
                S(0, "武将/部队编号", SlotKind.Person, "被检测的武将或部队候选。"),
                S(1, "状态类别", SlotKind.UnitStatus, "麻痹、封咒、混乱等状态候选。"),
                S(2, "比较方式", SlotKind.CompareOperator, "是否拥有该状态的比较方式候选。"),
                S(3, "成立后跳转", SlotKind.BranchTarget, "条件成立后的流程块候选。")),

            T(0x37, "钱、章节编号、忠奸测试", "用于测试金钱、章节编号、忠奸路线等全局值。",
                "该命令含义依赖测试类型槽，修改前需对照旧工具字段名。",
                S(0, "测试类型", SlotKind.Value, "金钱、章节、忠奸或路线值类型候选。"),
                S(1, "比较方式", SlotKind.CompareOperator, "比较方式候选。"),
                S(2, "比较值", SlotKind.Value, "目标金钱/章节/路线值候选。"),
                S(3, "成立后跳转", SlotKind.BranchTarget, "条件成立后的流程块候选。")),

            T(0x38, "武将能力设定", "用于设置人物能力、HP/MP、等级或战场属性。",
                "能力类别和目标值会直接影响平衡，必须保留备份并复读验证。",
                S(0, "武将/人物编号", SlotKind.Person, "目标人物候选。"),
                S(1, "能力类别", SlotKind.AbilityKind, "攻击、防御、精神、HP、等级等能力类别候选。"),
                S(2, "操作方式", SlotKind.Operation, "赋值、增加、减少等方式候选。"),
                S(3, "目标数值", SlotKind.Value, "能力新值或增减值候选。")),

            T(0x39, "武将等级提升", "用于提升等级、经验或触发升级演出。",
                "等级提升可能触发成长和策略学习，需实机确认最终能力。",
                S(0, "武将/人物编号", SlotKind.Person, "目标人物候选。"),
                S(1, "提升等级/经验", SlotKind.Count, "提升等级数或经验值候选。"),
                S(2, "提示标志", SlotKind.BooleanFlag, "是否显示升级提示候选。")),

            T(0x3A, "钱、章节编号、忠奸设置", "用于设置金钱、章节编号、忠奸路线或全局剧情值。",
                "全局剧情值会影响后续路线和商店/结局，必须记录修改意图。",
                S(0, "设置类型", SlotKind.Value, "金钱、章节、忠奸或路线值类型候选。"),
                S(1, "操作方式", SlotKind.Operation, "赋值、增加、减少等方式候选。"),
                S(2, "目标数值", SlotKind.Value, "写入值或增减值候选。")),

            T(0x3B, "武将加入", "用于让人物加入我军或队伍。",
                "加入方式可能影响人物列传、装备、等级和后续出场，需检查人物表与实机名单。",
                S(0, "武将/人物编号", SlotKind.Person, "加入的人物候选。"),
                S(1, "加入方式", SlotKind.Value, "data加入、内存加入、离开等方式候选。"),
                S(2, "提示标志", SlotKind.BooleanFlag, "是否显示加入提示候选。")),

            T(0x3C, "武将加入测试", "用于判断人物是否已经加入、离队或可用。",
                "人物状态可能有剧情临时状态，不能只按人物表判断。",
                S(0, "武将/人物编号", SlotKind.Person, "被检测的人物候选。"),
                S(1, "状态比较", SlotKind.CompareOperator, "加入/离队状态比较方式候选。"),
                S(2, "成立后跳转", SlotKind.BranchTarget, "条件成立后的流程块候选。")),

            T(0x3D, "获得物品", "用于获得物品、装备或奖励。",
                "物品 ID 应按 Data.e5 物品表核对；数量/装备状态可能因命令族不同而变。",
                S(0, "物品/装备编号", SlotKind.Item, "获得的物品或装备候选。"),
                S(1, "数量/等级", SlotKind.Count, "数量、装备等级或附加值候选。"),
                S(2, "提示/标志", SlotKind.BooleanFlag, "是否显示获得提示或加入背包标志候选。")),

            T(0x3E, "加入装备设定", "用于设置人物加入时的武器、防具、辅助或装备状态。",
                "装备槽位和人物加入流程相关，需同时核对人物表、物品表和实机装备栏。",
                S(0, "武将/人物编号", SlotKind.Person, "目标人物候选。"),
                S(1, "装备槽位", SlotKind.Value, "武器、防具、辅助等槽位候选。"),
                S(2, "物品/装备编号", SlotKind.Item, "设置的装备候选。"),
                S(3, "等级/经验", SlotKind.Count, "装备等级或经验候选。")),

            T(0x3F, "回合测试", "用于判断当前回合数或回合阶段。",
                "回合测试常影响增援、胜败条件和隐藏事件，应结合回合上限验证。",
                S(0, "回合变量", SlotKind.Value, "当前回合或阶段编号候选。"),
                S(1, "比较方式", SlotKind.CompareOperator, "回合比较方式候选。"),
                S(2, "比较值", SlotKind.Count, "目标回合数候选。"),
                S(3, "成立后跳转", SlotKind.BranchTarget, "条件成立后的流程块候选。")),

            T(0x40, "行动方测试", "用于判断当前行动方、阵营或回合所属。",
                "行动方判断会影响 AI 与玩家回合事件，需实机按回合切换测试。",
                S(0, "行动方/阵营", SlotKind.ForceSide, "我军、敌军、友军等阵营候选。"),
                S(1, "比较方式", SlotKind.CompareOperator, "行动方比较方式候选。"),
                S(2, "成立后跳转", SlotKind.BranchTarget, "条件成立后的流程块候选。")),

            T(0x41, "战场人数测试", "用于判断某阵营存活、出场或剩余人数。",
                "人数统计范围可能受隐藏、撤退、死亡影响，需实机验证。",
                S(0, "阵营/范围", SlotKind.ForceSide, "统计我军、敌军、友军或全体的候选。"),
                S(1, "比较方式", SlotKind.CompareOperator, "人数比较方式候选。"),
                S(2, "人数值", SlotKind.Count, "目标人数候选。"),
                S(3, "成立后跳转", SlotKind.BranchTarget, "条件成立后的流程块候选。")),

            T(0x42, "战斗胜利测试", "用于判断战斗胜利状态或胜利条件是否达成。",
                "显示胜利条件和实际胜利判定可能分离，需完整打到触发点验证。",
                S(0, "胜利类型", SlotKind.BattleResult, "胜利结果或胜利条件类型候选。"),
                S(1, "比较方式", SlotKind.CompareOperator, "是否达成的比较方式候选。"),
                S(2, "成立后跳转", SlotKind.BranchTarget, "胜利后流程块候选。")),

            T(0x43, "战斗失败测试", "用于判断失败条件是否达成。",
                "失败判定可能涉及回合数、主将死亡、特殊对象撤退等多条件。",
                S(0, "失败类型", SlotKind.BattleResult, "失败结果或失败条件类型候选。"),
                S(1, "比较方式", SlotKind.CompareOperator, "是否达成的比较方式候选。"),
                S(2, "成立后跳转", SlotKind.BranchTarget, "失败后流程块候选。")),

            T(0x44, "战斗初始化", "用于战场初始化、单位布置或战斗开始状态设置。",
                "初始化命令通常影响整场战斗，参数多且风险高；当前只解释前几个槽。",
                S(0, "初始化段/编号", SlotKind.Value, "战斗初始化段或配置编号候选。"),
                S(1, "地图/关卡编号", SlotKind.MapId, "相关地图或关卡编号候选。"),
                S(2, "天气/环境", SlotKind.Weather, "初始天气或环境候选。"),
                S(3, "保留状态", SlotKind.BooleanFlag, "是否保留队伍/变量状态候选。")),

            T(0x45, "战场全局变量", "用于读写战场级全局变量。",
                "变量作用域和运算方式需结合战斗初始化/战斗结束逻辑确认。",
                S(0, "变量编号", SlotKind.VariableId, "战场全局变量候选。"),
                S(1, "操作方式", SlotKind.Operation, "设置/增加/减少等候选。"),
                S(2, "数值", SlotKind.Value, "变量目标值候选。")),

            T(0x46, "友军出场设定", "用于设置友军单位是否出场、位置或状态。",
                "友军出场与 AI、胜败条件和剧情保护对象相关，需结合地图预览和实机战场验证。",
                S(0, "友军人物/部队", SlotKind.Person, "友军人物或部队候选。"),
                S(1, "出场标志", SlotKind.BooleanFlag, "是否出场或启用候选。"),
                S(2, "X坐标", SlotKind.CoordinateX, "出场 X 坐标候选。"),
                S(3, "Y坐标", SlotKind.CoordinateY, "出场 Y 坐标候选。")),

            T(0x47, "敌军出场设定", "用于设置敌军单位是否出场、位置或状态。",
                "敌军出场会影响难度和胜败条件，改动后应完整战斗测试。",
                S(0, "敌军人物/部队", SlotKind.Person, "敌军人物或部队候选。"),
                S(1, "出场标志", SlotKind.BooleanFlag, "是否出场或启用候选。"),
                S(2, "X坐标", SlotKind.CoordinateX, "出场 X 坐标候选。"),
                S(3, "Y坐标", SlotKind.CoordinateY, "出场 Y 坐标候选。")),

            T(0x48, "战场装备设定", "用于设置 46/47 部署段中友军或敌军单位的武器、防具和辅助装备。",
                "装备字段不是原始物品 ID：0=默认，1=卸去，2+=按武器/防具/辅助分段的相对编号+2；分段以 B形象指定器 System.ini 的 DefID/AssID 为准。",
                S(0, "人物/部队", SlotKind.Person, "目标友军或敌军人物候选。"),
                S(1, "武器编码", SlotKind.Value, "武器分段内相对装备编码：0=默认，1=卸去，2+=武器物品序号+2。"),
                S(2, "武器等级", SlotKind.Count, "武器等级候选；战场状态弹窗按 0=默认、1..16=等级处理。"),
                S(3, "防具编码", SlotKind.Value, "防具分段内相对装备编码：0=默认，1=卸去，2+=防具物品序号+2。"),
                S(4, "防具等级", SlotKind.Count, "防具等级候选；战场状态弹窗按 0=默认、1..16=等级处理。"),
                S(5, "辅助编码", SlotKind.Value, "辅助/道具分段内相对装备编码：0=默认，1=卸去，2+=辅助物品序号+2。")),

            T(0x49, "战斗结束", "用于触发战斗结束、结算或返回剧情。",
                "战斗结束命令会中断战场流程，需结合胜败条件和发布前综合报告复核。",
                S(0, "结束类型", SlotKind.BattleResult, "胜利、失败、撤退或剧情结束类型候选。"),
                S(1, "后续剧本/分支", SlotKind.BranchTarget, "后续剧情、结算或跳转候选。"),
                S(2, "奖励/结算标志", SlotKind.BooleanFlag, "是否结算经验、战利品或提示候选。")),

            T(0x4A, "我军出场强制设定", "用于强制指定我军人物出场或禁止离队。",
                "强制出场会影响战前选择和剧情队伍，需测试战前界面。",
                S(0, "我军人物", SlotKind.Person, "目标我军人物候选。"),
                S(1, "强制标志", SlotKind.BooleanFlag, "强制出场/取消强制候选。"),
                S(2, "出场位置", SlotKind.Count, "出场槽位或位置候选。")),

            T(0x4B, "我军出场设定", "用于设置我军人物出场、隐藏或可选状态。",
                "该命令与出场限制、强制出场类似，需按战前选择实机验证。",
                S(0, "我军人物", SlotKind.Person, "目标我军人物候选。"),
                S(1, "出场标志", SlotKind.BooleanFlag, "出场、隐藏、可选等候选标志。"),
                S(2, "出场位置", SlotKind.Count, "出场槽位或位置候选。")),

            T(0x4C, "隐藏武将出现", "用于让隐藏单位、伏兵或剧情单位出现。",
                "隐藏出现常关联增援与地图坐标，需导出地图预览并实机触发。",
                S(0, "武将/部队编号", SlotKind.Person, "出现的隐藏武将或部队候选。"),
                S(1, "X坐标", SlotKind.CoordinateX, "出现 X 坐标候选。"),
                S(2, "Y坐标", SlotKind.CoordinateY, "出现 Y 坐标候选。"),
                S(3, "朝向/阵营", SlotKind.Value, "朝向、阵营或出现方式候选。")),

            T(0x4D, "武将状态变更", "用于改变武将状态、异常、行动或可控标志。",
                "状态变更可能影响行动权、AI 和胜败条件，需战场实机验证。",
                S(0, "武将/部队编号", SlotKind.Person, "目标武将候选。"),
                S(1, "状态类别", SlotKind.UnitStatus, "状态/异常类别候选。"),
                S(2, "操作方式", SlotKind.Operation, "赋予、解除、切换等候选。")),

            T(0x4E, "武将方针变更", "用于改变 AI 方针或单位行动目标。",
                "AI 方针影响敌军/友军行动，建议结合回合测试和实机录像验证。",
                S(0, "武将/部队编号", SlotKind.Person, "目标武将或部队候选。"),
                S(1, "AI 方针", SlotKind.AiPolicy, "被动、主动、坚守、跟随等 AI 方针候选。"),
                S(2, "目标人物/地点", SlotKind.Value, "攻击目标、跟随对象或指定点候选。")),

            T(0x4F, "战场转向设置", "用于设置战场单位朝向或队列方向。",
                "转向可能只是演出效果，也可能影响后续动作表现。",
                S(0, "武将/部队编号", SlotKind.Person, "目标武将或部队候选。"),
                S(1, "朝向", SlotKind.Direction, "目标朝向候选。")),

            T(0x50, "战场动作设定", "用于设置战场动作、攻击姿态或演出状态。",
                "动作编号需对照 R/S/EEX 资源和实机动画。",
                S(0, "武将/部队编号", SlotKind.Person, "目标武将或部队候选。"),
                S(1, "动作编号", SlotKind.Value, "动作或姿态编号候选。"),
                S(2, "等待/循环标志", SlotKind.BooleanFlag, "是否等待或循环候选。")),

            T(0x51, "战场恢复行动权", "用于恢复单位行动权、清除已行动标志或重置回合行动。",
                "行动权修改会显著影响平衡和 AI，必须实机测试。",
                S(0, "武将/部队编号", SlotKind.Person, "恢复行动权的武将候选。"),
                S(1, "恢复方式", SlotKind.Value, "恢复、清除、重置或全体方式候选。")),

            T(0x52, "兵种改变", "用于改变人物兵种或战场单位职业。",
                "兵种编号应跳到详细兵种表核对，改动会影响移动、攻击范围和成长。",
                S(0, "武将/人物编号", SlotKind.Person, "目标人物候选。"),
                S(1, "兵种/职业编号", SlotKind.Value, "目标兵种编号候选；请跳到兵种表核对名称。")),

            T(0x53, "战场撤退", "用于让指定单位撤退或从战场离开。",
                "撤退与阵亡、隐藏、消失不同，可能影响胜败和后续剧情。",
                S(0, "武将/部队编号", SlotKind.Person, "撤退的武将候选。"),
                S(1, "撤退方式", SlotKind.Value, "普通撤退、剧情撤退或强制离场候选。"),
                S(2, "提示标志", SlotKind.BooleanFlag, "是否显示撤退信息候选。")),

            T(0x54, "战场撤退确认", "用于确认或检测某单位是否撤退。",
                "撤退确认通常参与胜败条件和保护目标判断。",
                S(0, "武将/部队编号", SlotKind.Person, "被检测的武将候选。"),
                S(1, "比较方式", SlotKind.CompareOperator, "撤退状态比较方式候选。"),
                S(2, "成立后跳转", SlotKind.BranchTarget, "撤退成立后的流程块候选。")),

            T(0x55, "战场复活", "用于复活、重新出场或恢复指定单位。",
                "复活与隐藏出现、移动命令参数相似，需结合坐标和战场状态验证。",
                S(0, "武将/部队编号", SlotKind.Person, "复活的武将候选。"),
                S(1, "X坐标", SlotKind.CoordinateX, "复活位置 X 坐标候选。"),
                S(2, "Y坐标", SlotKind.CoordinateY, "复活位置 Y 坐标候选。"),
                S(3, "HP/状态", SlotKind.Value, "复活后的 HP、状态或方式候选。")),

            T(0x56, "天气类别设定", "用于设置战场天气类型或可变化天气范围。",
                "天气会影响策略、移动和画面效果，需实机验证。",
                S(0, "天气类别", SlotKind.Weather, "天气类型候选。"),
                S(1, "变化/锁定标志", SlotKind.BooleanFlag, "是否锁定或允许变化候选。")),

            T(0x57, "当前天气设定", "用于直接设置当前战场天气。",
                "当前天气与天气类别可能不同，修改后需测试战场效果。",
                S(0, "当前天气", SlotKind.Weather, "当前天气枚举候选。"),
                S(1, "显示/刷新标志", SlotKind.BooleanFlag, "是否立即刷新天气画面候选。")),

            T(0x58, "战场障碍设定", "用于设置障碍、门、机关、地形阻挡或战场对象状态。",
                "障碍会影响路径和 AI，需要和地图/地形预览一起核对。",
                S(0, "障碍/对象编号", SlotKind.Value, "障碍或战场对象编号候选。"),
                S(1, "X坐标", SlotKind.CoordinateX, "障碍位置 X 坐标候选。"),
                S(2, "Y坐标", SlotKind.CoordinateY, "障碍位置 Y 坐标候选。"),
                S(3, "状态", SlotKind.BooleanFlag, "开启、关闭、破坏或恢复状态候选。")),

            T(0x59, "战利品", "用于设定战斗奖励或战利品。",
                "奖励槽可能同时包含物品、金钱、功勋或提示标志，需要与战后实机结果核对。",
                S(0, "物品/奖励编号", SlotKind.Item, "战利品物品候选。"),
                S(1, "数量/金钱", SlotKind.Value, "数量、金钱或奖励值候选。"),
                S(2, "显示标志", SlotKind.BooleanFlag, "是否显示战利品提示候选。")),

            T(0x5A, "战场操作开始", "用于开启战场操作、解除剧情锁定或进入可控阶段。",
                "该命令会影响剧情演出和玩家操作切换，需按事件顺序验证。",
                S(0, "操作阶段", SlotKind.Value, "开始操作、锁定解除或阶段编号候选。"),
                S(1, "控制阵营", SlotKind.ForceSide, "进入操作的阵营候选。")),

            T(0x5B, "战场高亮区域", "用于高亮地图区域，引导玩家关注坐标范围。",
                "区域参数通常为坐标范围，需对照地图确认。",
                S(0, "区域X1", SlotKind.CoordinateX, "高亮区域起点 X 候选。"),
                S(1, "区域Y1", SlotKind.CoordinateY, "高亮区域起点 Y 候选。"),
                S(2, "区域X2", SlotKind.CoordinateX, "高亮区域终点 X 候选。"),
                S(3, "区域Y2", SlotKind.CoordinateY, "高亮区域终点 Y 候选。")),

            T(0x5C, "战场高亮人物", "用于高亮指定人物或单位，提示玩家注意目标。",
                "高亮对象需与当前出场单位一致，避免指向未出场人物。",
                S(0, "武将/部队编号", SlotKind.Person, "被高亮的人物或部队候选。"),
                S(1, "显示方式", SlotKind.Value, "闪烁、箭头、持续时间或层级候选。")),

            T(0x5D, "回合上限设定", "用于设置本关回合上限或失败倒计时。",
                "回合上限会直接影响失败条件，需要在胜败条件文本和实机中同步验证。",
                S(0, "回合上限", SlotKind.Count, "允许回合数候选。"),
                S(1, "提示/失败标志", SlotKind.BooleanFlag, "是否显示并参与失败判定候选。")),

            T(0x5E, "武将不同测试", "用于判断两个人物/单位是否不同或是否为同一对象。",
                "常用于动态对象或变量对象判断，需结合上下文确认槽位来源。",
                S(0, "武将A", SlotKind.Person, "第一个人物/单位候选。"),
                S(1, "武将B", SlotKind.Person, "第二个人物/单位候选。"),
                S(2, "成立后跳转", SlotKind.BranchTarget, "判断成立后的流程块候选。")),

            T(0x5F, "单挑结束", "用于结束单挑场景并返回战场或剧情。",
                "单挑结束通常与胜负显示、阵亡、战场状态恢复配套。",
                S(0, "结束方式", SlotKind.BattleResult, "胜负、返回、失败等结束方式候选。"),
                S(1, "后续跳转", SlotKind.BranchTarget, "结束后的流程块候选。")),

            T(0x60, "单挑武将出场", "用于在单挑场景中设置参战武将。",
                "单挑人物槽必须与后续单挑对话/动作/胜负显示一致。",
                S(0, "武将A", SlotKind.Person, "单挑一方人物候选。"),
                S(1, "武将B", SlotKind.Person, "单挑另一方人物候选。"),
                S(2, "出场位置/方向", SlotKind.Direction, "单挑入场方向或位置候选。")),

            T(0x61, "单挑胜负显示", "用于单挑结算演出或胜负显示。",
                "单挑命令族通常与独立单挑场景配套，人物槽和结果槽必须结合单挑上下文确认。",
                S(0, "胜者/武将A", SlotKind.Person, "单挑武将或胜者候选。"),
                S(1, "败者/武将B", SlotKind.Person, "单挑对手或败者候选。"),
                S(2, "胜负结果", SlotKind.BattleResult, "胜负显示或结算枚举候选。"),
                S(3, "文本/演出编号", SlotKind.TextIndex, "单挑台词或演出段落候选。")),

            T(0x62, "单挑阵亡", "用于处理单挑后阵亡、撤退或状态变化。",
                "阵亡处理会影响后续战场单位状态和剧情分支。",
                S(0, "阵亡/撤退武将", SlotKind.Person, "被处理的单挑武将候选。"),
                S(1, "处理方式", SlotKind.BattleResult, "阵亡、撤退、保留等候选。")),

            T(0x63, "单挑对话", "用于单挑场景中的对白。",
                "文本仍需到文本线索中核对容量，人物槽需与单挑双方一致。",
                S(0, "说话武将", SlotKind.Person, "单挑对白说话人候选。"),
                S(1, "文本编号", SlotKind.TextIndex, "单挑对白文本候选。"),
                S(2, "表情/方向", SlotKind.Direction, "头像或方向状态候选。")),

            T(0x64, "单挑动作", "用于播放单挑中的动作或演出。",
                "动作编号与单挑动画资源相关，需要实机确认。",
                S(0, "动作武将", SlotKind.Person, "执行动作的武将候选。"),
                S(1, "动作编号", SlotKind.Value, "单挑动作编号候选。"),
                S(2, "等待标志", SlotKind.BooleanFlag, "是否等待动作完成候选。")),

            T(0x65, "单挑攻击1", "用于单挑攻击演出或攻击段。",
                "攻击命令族参数可能包含伤害、动作和文本，当前仅解释常见槽。",
                S(0, "攻击方", SlotKind.Person, "攻击武将候选。"),
                S(1, "防御方", SlotKind.Person, "受击武将候选。"),
                S(2, "攻击动作/结果", SlotKind.Value, "攻击动作、命中或伤害候选。")),

            T(0x66, "单挑攻击2", "用于另一类单挑攻击演出或连击段。",
                "需结合单挑攻击1、动作和胜负显示共同验证。",
                S(0, "攻击方", SlotKind.Person, "攻击武将候选。"),
                S(1, "防御方", SlotKind.Person, "受击武将候选。"),
                S(2, "攻击动作/结果", SlotKind.Value, "攻击动作、命中或伤害候选。")),

            T(0x67, "章名", "用于显示章节名或章节切换文字。",
                "章节名文本通常在同文件文本线索中出现，编号不等于可直接改写地址。",
                S(0, "章名文本编号", SlotKind.TextIndex, "章节名文本候选。"),
                S(1, "显示方式", SlotKind.BooleanFlag, "显示/等待/淡入标志候选。")),

            T(0x68, "单挑开始", "用于进入单挑场景或初始化单挑演出。",
                "单挑开始会切换演出上下文，需与后续单挑出场/对话/动作配套。",
                S(0, "单挑编号/场景", SlotKind.Value, "单挑场景或配置编号候选。"),
                S(1, "武将A", SlotKind.Person, "单挑一方候选。"),
                S(2, "武将B", SlotKind.Person, "单挑另一方候选。")),

            T(0x69, "旁白", "用于无头像旁白文本显示。",
                "旁白文本仍应通过文本线索短写回，不直接改命令参数。",
                S(0, "旁白文本编号", SlotKind.TextIndex, "旁白文本候选。"),
                S(1, "显示方式", SlotKind.BooleanFlag, "等待/窗口/清屏标志候选。")),

            T(0x6A, "Game Over指令", "用于触发 Game Over 或失败结束。",
                "该命令通常为强终止流程，高风险；需备份并验证关联条件。",
                S(0, "失败/结束类型", SlotKind.BattleResult, "Game Over 类型或失败原因候选。"),
                S(1, "显示文本/演出", SlotKind.TextIndex, "失败提示或演出文本候选。")),

            T(0x6B, "法术", "用于触发策略/法术效果、教学演出或脚本施法。",
                "策略编号需跳到策略表核对；目标可能是人物、坐标或范围。",
                S(0, "策略/法术编号", SlotKind.Strategy, "策略或法术候选。"),
                S(1, "施法者", SlotKind.Person, "施法人物候选。"),
                S(2, "目标/坐标X", SlotKind.CoordinateX, "目标人物、X 坐标或范围候选。"),
                S(3, "坐标Y/效果值", SlotKind.CoordinateY, "Y 坐标、效果值或范围候选。")),

            T(0x6C, "武将能力复制", "用于把一名人物的能力复制给另一名人物或单位。",
                "能力复制会影响平衡和后续成长，需明确来源与目标。",
                S(0, "来源武将", SlotKind.Person, "能力来源人物候选。"),
                S(1, "目标武将", SlotKind.Person, "能力目标人物候选。"),
                S(2, "能力类别", SlotKind.AbilityKind, "复制的能力类别候选。")),

            T(0x6D, "相对复活或移动", "用于按相对坐标复活、移动或调整单位位置。",
                "相对坐标与绝对坐标容易混淆，需在地图预览和实机中验证。",
                S(0, "武将/部队编号", SlotKind.Person, "目标武将或部队候选。"),
                S(1, "相对X", SlotKind.CoordinateX, "相对或目标 X 候选。"),
                S(2, "相对Y", SlotKind.CoordinateY, "相对或目标 Y 候选。"),
                S(3, "方式/标志", SlotKind.Value, "复活、移动或保留状态方式候选。")),

            T(0x6E, "概率测试", "用于按概率触发剧情分支或随机事件。",
                "概率单位可能是百分比、千分比或枚举，需多次实机测试。",
                S(0, "概率值", SlotKind.Count, "触发概率候选。"),
                S(1, "比较/随机方式", SlotKind.Value, "随机算法或比较方式候选。"),
                S(2, "成立后跳转", SlotKind.BranchTarget, "概率成功后的流程块候选。")),

            T(0x6F, "丢弃物品", "用于从队伍、人物或背包中移除物品。",
                "丢弃对象和数量需核对物品表，避免误删关键道具。",
                S(0, "物品/装备编号", SlotKind.Item, "被移除的物品候选。"),
                S(1, "数量/槽位", SlotKind.Count, "数量或背包槽位候选。"),
                S(2, "提示标志", SlotKind.BooleanFlag, "是否显示提示候选。")),

            T(0x70, "能力选择复制", "用于选择某类能力或属性进行复制/转移。",
                "能力类别和来源目标必须核对，避免复制错属性。",
                S(0, "来源武将", SlotKind.Person, "来源人物候选。"),
                S(1, "目标武将", SlotKind.Person, "目标人物候选。"),
                S(2, "能力类别", SlotKind.AbilityKind, "攻击、防御、精神等类别候选。"),
                S(3, "复制方式", SlotKind.Operation, "覆盖、增加或临时复制候选。")),

            T(0x71, "特效请求", "用于请求战斗特效、屏幕特效或资源演出。",
                "特效编号需结合资源索引、动画和实机画面核对。",
                S(0, "特效编号", SlotKind.Value, "特效或动画编号候选。"),
                S(1, "X坐标/目标", SlotKind.CoordinateX, "特效位置 X 或目标候选。"),
                S(2, "Y坐标", SlotKind.CoordinateY, "特效位置 Y 坐标候选。"),
                S(3, "等待标志", SlotKind.BooleanFlag, "是否等待特效结束候选。")),

            T(0x72, "信息传送", "用于传送信息、变量或跨事件数据。",
                "信息传送的源/目标含义高度依赖上下文，当前只作只读提示。",
                S(0, "源编号/变量", SlotKind.VariableId, "源变量、消息或对象编号候选。"),
                S(1, "目标编号/变量", SlotKind.VariableId, "目标变量、消息或对象编号候选。"),
                S(2, "传送方式", SlotKind.Operation, "赋值、复制、追加或清除候选。")),

            T(0x73, "人物五围和测试", "用于测试或处理人物五围属性。",
                "五围属性和比较值需结合人物表、等级和装备影响验证。",
                S(0, "武将/人物编号", SlotKind.Person, "目标人物候选。"),
                S(1, "能力类别", SlotKind.AbilityKind, "武力、统率、智力、敏捷、运气等候选。"),
                S(2, "比较方式", SlotKind.CompareOperator, "属性比较方式候选。"),
                S(3, "比较值", SlotKind.Value, "目标属性值候选。")),

            T(0x74, "开/禁存档", "用于允许或禁止存档、打开菜单功能。",
                "禁存档影响玩家体验，需在剧情段落结束后确认是否恢复。",
                S(0, "存档开关", SlotKind.BooleanFlag, "允许/禁止存档候选。"),
                S(1, "作用范围", SlotKind.Value, "当前剧情、战场或全局范围候选。")),

            T(0x75, "S特殊形象指定", "用于指定 S 形象、特殊战场形象或插图形象。",
                "形象编号需与 S 形象资源、人物表和形象指定器核对。",
                S(0, "武将/人物编号", SlotKind.Person, "目标人物候选。"),
                S(1, "S形象/特殊形象编号", SlotKind.Value, "目标 S 形象编号候选。"),
                S(2, "刷新/保留标志", SlotKind.BooleanFlag, "是否立即刷新或保留候选。")),

            T(0x76, "无条件跳转", "用于直接跳转到某流程块或命令位置。",
                "跳转目标的单位尚未完全确认，不能据此自动重排命令。",
                S(0, "跳转目标", SlotKind.BranchTarget, "目标事件块/偏移/序号候选。")),

            T(0x77, "变量运算", "用于变量加减、比较或复合运算。",
                "运算枚举和变量作用域需结合旧工具与实机验证。",
                S(0, "变量编号", SlotKind.VariableId, "参与运算的变量候选。"),
                S(1, "运算方式", SlotKind.Operation, "加减赋值等候选。"),
                S(2, "运算值", SlotKind.Value, "参与运算的值或变量候选。")),

            T(0x78, "整型变量赋值", "用于给整型变量、指针变量或扩展变量赋值。",
                "变量类型和指针含义需结合旧工具变量类型说明核对。",
                S(0, "变量编号", SlotKind.VariableId, "整型变量或指针变量候选。"),
                S(1, "变量类型", SlotKind.Value, "常数、指针变量、整型变量等类型候选。"),
                S(2, "赋值方式", SlotKind.Operation, "赋值、加减、乘除或取模方式候选。"),
                S(3, "赋值内容", SlotKind.Value, "写入的常数、变量或地址候选。")),

            T(0x79, "变量测试", "用于扩展变量、整型变量或指针变量测试。",
                "与 0x05 变量测试类似，但变量类型可能不同，需结合旧工具字段名。",
                S(0, "变量编号", SlotKind.VariableId, "被测试变量候选。"),
                S(1, "变量类型", SlotKind.Value, "常数、指针变量、整型变量等类型候选。"),
                S(2, "比较方式", SlotKind.CompareOperator, "比较方式候选。"),
                S(3, "比较值", SlotKind.Value, "目标值或变量候选。"),
                S(4, "成立后跳转", SlotKind.BranchTarget, "条件成立后的流程块候选。")),

            T(0x7A, "对话3", "用于扩展对白、特殊窗口或新版剧情对话。",
                "对话3 可能含扩展头像/窗口槽，文本仍建议走文本线索短写回。",
                S(0, "说话人/头像", SlotKind.Person, "人物、头像或特殊头像候选。"),
                S(1, "文本编号", SlotKind.TextIndex, "对白文本候选。"),
                S(2, "窗口/表情", SlotKind.Value, "窗口样式、表情或显示位置候选。"),
                S(3, "显示标志", SlotKind.BooleanFlag, "等待、清屏或自动推进候选。")),

            T(0x7B, "信息传送测试", "用于检测信息传送、变量传递或跨事件数据是否满足条件。",
                "源/目标变量含义依赖上下文，需结合 0x72 信息传送和实机触发验证。",
                S(0, "源编号/变量", SlotKind.VariableId, "源变量、消息或对象编号候选。"),
                S(1, "目标编号/变量", SlotKind.VariableId, "目标变量、消息或对象编号候选。"),
                S(2, "比较方式", SlotKind.CompareOperator, "传送结果比较方式候选。"),
                S(3, "成立后跳转", SlotKind.BranchTarget, "条件成立后的流程块候选。"))
        };

        return templates.ToDictionary(template => template.Id);
    }

    private static CommandTemplate T(int id, string name, string purpose, string risk, params TemplateSlot[] slots)
        => new(id, name, purpose, risk, slots);

    private static TemplateSlot S(int index, string name, SlotKind kind, string meaning, string risk = "")
        => new(index, name, kind, meaning, string.IsNullOrWhiteSpace(risk) ? "候选解释，真实含义需结合原工具和实机验证。" : risk);

    private static NameLookups BuildNameLookups(CczProject? project, IReadOnlyList<HexTableDefinition>? tables)
    {
        if (project == null || tables == null || tables.Count == 0)
        {
            return NameLookups.Empty;
        }

        var reader = new HexTableReader();
        var itemNames = new Dictionary<int, string>();
        foreach (var table in HexTableNameResolver.ResolveItemTables(project, tables))
        {
            foreach (var pair in LoadNameMap(project, tables, reader, table.TableName))
            {
                itemNames[pair.Key] = pair.Value;
            }
        }

        return new NameLookups(
            LoadNameMap(project, tables, reader, "6.5-0 人物"),
            itemNames,
            LoadNameMap(project, tables, reader, "6.5-5 策略"));
    }

    private static IReadOnlyDictionary<int, string> LoadNameMap(CczProject project, IReadOnlyList<HexTableDefinition> tables, HexTableReader reader, string tableName)
    {
        try
        {
            if (!HexTableNameResolver.TryResolveForProject(project, tables, tableName, out var table)) return new Dictionary<int, string>();
            var read = reader.Read(project, table, tables);
            if (!read.Validation.IsUsable || !read.Data.Columns.Contains("ID")) return new Dictionary<int, string>();
            var nameColumn = read.Data.Columns.Contains("名称")
                ? "名称"
                : read.Data.Columns.Cast<DataColumn>().FirstOrDefault(column => column.ColumnName.Contains("名", StringComparison.Ordinal))?.ColumnName;
            if (string.IsNullOrWhiteSpace(nameColumn)) return new Dictionary<int, string>();

            var result = new Dictionary<int, string>();
            foreach (DataRow row in read.Data.Rows)
            {
                var id = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
                var name = Convert.ToString(row[nameColumn], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    result[id] = name;
                }
            }
            return result;
        }
        catch
        {
            return new Dictionary<int, string>();
        }
    }

    private static string Escape(string? value)
    {
        value ??= string.Empty;
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r\n", "<br>", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", "<br>", StringComparison.Ordinal);
    }

    private static string ValueOrDash(string value) => string.IsNullOrWhiteSpace(value) ? "-" : value;

    private sealed record CommandTemplate(int Id, string Name, string Purpose, string Risk, IReadOnlyList<TemplateSlot> Slots);

    private sealed record TemplateSlot(int Index, string Name, SlotKind Kind, string Meaning, string Risk);

    private enum SlotKind
    {
        Raw,
        EventId,
        VariableId,
        CompareOperator,
        Value,
        Person,
        Item,
        Strategy,
        CoordinateX,
        CoordinateY,
        MapId,
        TextIndex,
        BranchTarget,
        BooleanFlag,
        Direction,
        Count,
        ForceSide,
        BattleResult,
        Operation,
        AbilityKind,
        UnitStatus,
        AiPolicy,
        Weather,
        SoundEffect,
        MusicTrack
    }

    private sealed record NameLookups(
        IReadOnlyDictionary<int, string> PersonNames,
        IReadOnlyDictionary<int, string> ItemNames,
        IReadOnlyDictionary<int, string> StrategyNames)
    {
        public static NameLookups Empty { get; } = new(
            new Dictionary<int, string>(),
            new Dictionary<int, string>(),
            new Dictionary<int, string>());
    }
}

