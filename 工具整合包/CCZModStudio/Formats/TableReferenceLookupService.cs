using System.Data;
using System.Globalization;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class TableReferenceLookupService
{
    private readonly Dictionary<string, DataTable> _tableCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ItemEffectCatalogService _itemEffectCatalogService = new();

    public string BuildCellReferenceEvidence(CczProject project, IReadOnlyList<HexTableDefinition> tables, HexTableDefinition currentTable, HexFieldDefinition field, object? value, int? rowId = null)
    {
        if (value == null || value == DBNull.Value || !field.ConsumesBytes)
        {
            return string.Empty;
        }

        var textRowAlignedEvidence = BuildTextRowAlignedEvidence(project, tables, currentTable, field, rowId);
        if (!string.IsNullOrWhiteSpace(textRowAlignedEvidence))
        {
            return textRowAlignedEvidence;
        }

        if (!TryParseInt(value, out var id))
        {
            return string.Empty;
        }

        var rowAlignedEvidence = BuildRowAlignedEvidence(project, tables, currentTable, field, id, rowId);
        if (!string.IsNullOrWhiteSpace(rowAlignedEvidence))
        {
            return rowAlignedEvidence;
        }

        var rule = GetRule(currentTable, field);
        if (rule?.Kind == ReferenceRuleKind.EquipmentEffect)
        {
            return BuildEquipmentEffectReference(project, tables, field, id);
        }

        if (id == 255 && IsLikelyOptionalReference(currentTable, field))
        {
            return "\r\n\r\n跨表引用解释：当前值 255 通常可作为“无/空槽/不指定”的候选值处理。\r\n建议：若这是商店槽位、专属装备槽或可选引用，255 多半表示留空；若游戏内显示异常，请再结合原工具和实机验证。";
        }

        if (rule == null)
        {
            return BuildResourceHint(project, currentTable, field, id);
        }

        if (rule.Kind == ReferenceRuleKind.Item)
        {
            return BuildItemReference(project, tables, currentTable, field, id);
        }

        return BuildSingleTableReference(project, tables, currentTable, field, id, rule);
    }

    public string BuildFieldReferenceHint(HexTableDefinition currentTable, HexFieldDefinition field)
    {
        if (!field.ConsumesBytes)
        {
            return "辅助显示列：通常来自索引表或当前行号，不直接写入二进制。";
        }

        var textTarget = GetTextRowAlignedTargetTable(currentTable.TableName);
        if (!string.IsNullOrWhiteSpace(textTarget) && field.ColumnName is "介绍" or "名称")
        {
            return $"行对齐文本：本表通常按 ID 对齐“{textTarget}”，用于编辑同一编号对象的说明/台词文本。";
        }

        if (field.ColumnName == "内容")
        {
            var companion = currentTable.TableName switch
            {
                "6.5-5-2 策略动画1" => "策略主表同 ID 的第一段动画/主动画编号。",
                "6.5-5-3 策略动画2" => "策略主表同 ID 的第二段动画/附加动画编号。",
                "6.5-5-4 策略伤害类型" => "策略主表同 ID 的伤害类型/公式类别候选。",
                "6.5-5-5 策略伤害比例" => "策略主表同 ID 的伤害比例/倍率参数候选。",
                "6.5-5-6 策略命中率" => "策略主表同 ID 的命中率参数候选。",
                "6.5-5-7 学会策略" => "策略主表同 ID 的学习/可用条件候选。",
                "6.5-5-8 战场AI策略限制" => "策略主表同 ID 的战场 AI 使用限制候选。",
                "6.5-5-9 练武场AI策略限制" => "策略主表同 ID 的练武场 AI 使用限制候选。",
                _ => string.Empty
            };
            if (!string.IsNullOrWhiteSpace(companion))
            {
                return "策略附表行对齐：" + companion;
            }
        }

        var rule = GetRule(currentTable, field);
        if (rule?.Kind == ReferenceRuleKind.EquipmentEffect)
        {
            return "跨表引用：宝物页优先读取项目侧 `CCZModStudio_Notes/*_ItemEffectCatalog.json` 的 UTF-8 宝物特效目录，未命中时再回退到 `6.5-1-2/6.5-1-3 装备特效名称`；效果值是同一行参数。";
        }

        if (rule?.Kind == ReferenceRuleKind.Item)
        {
            return "跨表引用：通常指向“6.5-1 物品（0-103）/6.5-2 物品（104-255）”；255 在商店/槽位中常作为空槽候选。";
        }

        if (rule != null)
        {
            return $"跨表引用：通常指向“{rule.TargetTableName}”。依据：{rule.Reason}";
        }

        var name = field.ColumnName;
        if (name == "头像") return "资源引用：人物 Data 头像号不是 Face.e5 直接序号；0 对应 Face.e5 1-8，n>=1 对应 Face.e5 n+8；Tou.dll 真彩资源号=Face图号+300，语言 2052。当前仅做只读提示，不直接重封包。";
        if (name.Contains("图标", StringComparison.Ordinal)) return "资源引用：图标编号通常对应界面/策略/物品图标封包索引；需结合 E5/Ls 探针和实机验证。";
        if (name.Contains("范围", StringComparison.Ordinal)) return "参数引用：范围/模板编号，可能控制攻击、施法或穿透范围；建议对比同类条目后实测。";
        if (name.Contains("消耗", StringComparison.Ordinal)) return "参数引用：消耗值，可能是 MP/SP 或其他战斗资源消耗。";
        if (name is "策略类型" or "施展对象") return "枚举参数：通常影响策略分类、目标阵营或可选对象；当前按内部枚举提示处理。";
        if (name.Contains("效果值", StringComparison.Ordinal)) return "联动参数：通常与同一行的特效号/类型配套，不宜脱离上下文单独修改。";
        if (name.Contains("R形象", StringComparison.Ordinal)) return "资源引用：R 形象编号 n 对应 Pmapobj.e5 第 2n+1 张正面、第 2n+2 张反面（1-based 图号）；不是 RS\\R_XX.eex 人物图像。";
        if (name.Contains("S形象", StringComparison.Ordinal)) return "资源引用：S 形象编号通常结合 Unit_atk.e5 / Unit_mov.e5 / Unit_spc.e5；0-139 为普通范围，140-156 为教程记录的特殊形象区间；不是 RS\\S_XX.eex 人物图像。";

        return string.Empty;
    }

    public TableReferenceNavigationTarget ResolveCellReferenceTarget(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        HexTableDefinition currentTable,
        HexFieldDefinition field,
        object? value,
        int? rowId = null)
    {
        var sourceValue = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        var sourceRowId = rowId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        if (value == null || value == DBNull.Value)
        {
            return BuildUnrecognizedNavigationTarget(currentTable, field, sourceRowId, sourceValue, "当前单元格为空，没有可跳转的引用目标。");
        }

        if (!field.ConsumesBytes)
        {
            return BuildUnrecognizedNavigationTarget(currentTable, field, sourceRowId, sourceValue, "该列是辅助显示列，不直接写入二进制，也没有独立引用目标。");
        }

        var textRowAlignedTarget = TryBuildTextRowAlignedNavigationTarget(project, tables, currentTable, field, sourceValue, sourceRowId, rowId);
        if (textRowAlignedTarget != null)
        {
            return textRowAlignedTarget;
        }

        if (!TryParseInt(value, out var id))
        {
            return BuildUnrecognizedNavigationTarget(currentTable, field, sourceRowId, sourceValue, "当前值不是可解析的编号；无法生成跨表跳转。");
        }

        var strategyCompanionTarget = TryBuildStrategyCompanionNavigationTarget(project, tables, currentTable, field, id, sourceValue, sourceRowId, rowId);
        if (strategyCompanionTarget != null)
        {
            return strategyCompanionTarget;
        }

        if (id == 255 && IsLikelyOptionalReference(currentTable, field))
        {
            return new TableReferenceNavigationTarget
            {
                IsRecognized = true,
                IsOptionalEmpty = true,
                TargetRowExists = false,
                SourceTableName = currentTable.TableName,
                SourceRowId = sourceRowId,
                SourceFieldName = field.ColumnName,
                SourceValue = sourceValue,
                ReferenceKind = "空槽/不指定",
                Summary = $"{currentTable.TableName} / ID={sourceRowId} / {field.ColumnName} = 255：通常可作为“无/空槽/不指定”候选值，不跳转到目标表。",
                SafetyNote = "若游戏内显示异常，请结合原工具和实机验证；确认特殊用途后建议添加创作者备注。"
            };
        }

        var rule = GetRule(currentTable, field);
        if (rule == null)
        {
            return BuildUnrecognizedNavigationTarget(currentTable, field, sourceRowId, sourceValue, "该字段尚未建立确认的跨表导航规则；请先查看字段中文注释和资源提示。");
        }

        return rule.Kind switch
        {
            ReferenceRuleKind.SingleTable => BuildKnownTableNavigationTarget(project, tables, currentTable, field, sourceValue, sourceRowId, rule.DisplayName, rule.TargetTableName, id, rule.Reason),
            ReferenceRuleKind.Item => BuildItemNavigationTarget(project, tables, currentTable, field, id, sourceValue, sourceRowId, rule.Reason),
            ReferenceRuleKind.EquipmentEffect => BuildEquipmentEffectNavigationTarget(project, tables, currentTable, field, id, sourceValue, sourceRowId, rule.Reason),
            _ => BuildUnrecognizedNavigationTarget(currentTable, field, sourceRowId, sourceValue, "该字段的引用类型暂不支持导航。")
        };
    }

    private string BuildTextRowAlignedEvidence(CczProject project, IReadOnlyList<HexTableDefinition> tables, HexTableDefinition currentTable, HexFieldDefinition field, int? rowId)
    {
        if (rowId == null || field.ColumnName is not ("介绍" or "名称"))
        {
            return string.Empty;
        }

        var targetTableName = GetTextRowAlignedTargetTable(currentTable.TableName);
        if (string.IsNullOrWhiteSpace(targetTableName))
        {
            return string.Empty;
        }

        var targetName = LookupName(project, tables, targetTableName, rowId.Value);
        return
            $"\r\n\r\n行对齐文本解释：{currentTable.TableName}\r\n" +
            $"当前行 ID={rowId.Value} 通常与“{targetTableName}”同 ID 对齐；目标名称：{targetName}\r\n" +
            $"字段“{field.ColumnName}”用于同一编号对象的说明、列传、台词或显示文本。\r\n" +
            "建议：修改文本时重点检查 GBK 字节容量；若本行目标名称显示未找到，可能是扩展/空位/特殊编号，请结合主表确认。";
    }

    private TableReferenceNavigationTarget? TryBuildTextRowAlignedNavigationTarget(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        HexTableDefinition currentTable,
        HexFieldDefinition field,
        string sourceValue,
        string sourceRowId,
        int? rowId)
    {
        if (rowId == null || field.ColumnName is not ("介绍" or "名称"))
        {
            return null;
        }

        var targetTableName = GetTextRowAlignedTargetTable(currentTable.TableName);
        if (string.IsNullOrWhiteSpace(targetTableName))
        {
            return null;
        }

        return BuildKnownTableNavigationTarget(
            project,
            tables,
            currentTable,
            field,
            sourceValue,
            sourceRowId,
            "行对齐文本",
            targetTableName,
            rowId.Value,
            $"“{currentTable.TableName}”通常与“{targetTableName}”按同一 ID 对齐。");
    }

    private static string GetTextRowAlignedTargetTable(string tableName) => tableName switch
    {
        "6.5-0-1 人物列传" => "6.5-0 人物",
        "6.5-0-2 暴击台词" => "6.5-0 人物",
        "6.5-0-3 撤退台词" => "6.5-0 人物",
        "6.5-1-1 物品说明（0-103）" => "6.5-1 物品（0-103）",
        "6.5-2-1 物品说明（104-255）" => "6.5-2 物品（104-255）",
        "6.5-4-1 兵种说明" => "6.5-4 详细兵种",
        "6.5-5-1 策略说明" => "6.5-5 策略",
        "6.5-6-1 必杀说明" => "6.5-6 必杀",
        "6.5-7-1 兵种特效说明" => "6.5-7 兵种特效",
        "6.5-1-2 物品获取说明（0-103）" => "6.5-1 物品（0-103）",
        "6.5-2-2 物品获取说明（104-199）" => "6.5-2 物品（104-255）",
        _ => string.Empty
    };

    private string BuildRowAlignedEvidence(CczProject project, IReadOnlyList<HexTableDefinition> tables, HexTableDefinition currentTable, HexFieldDefinition field, int value, int? rowId)
    {
        if (rowId == null || field.ColumnName != "内容")
        {
            return string.Empty;
        }

        var meaning = currentTable.TableName switch
        {
            "6.5-5-2 策略动画1" => "策略第一段动画/主动画编号",
            "6.5-5-3 策略动画2" => "策略第二段动画/附加动画编号",
            "6.5-5-4 策略伤害类型" => "策略伤害类型/公式类别候选",
            "6.5-5-5 策略伤害比例" => "策略伤害比例/倍率参数候选",
            "6.5-5-6 策略命中率" => "策略命中率参数候选",
            "6.5-5-7 学会策略" => "策略学习/可用条件候选",
            "6.5-5-8 战场AI策略限制" => "战场 AI 使用限制候选",
            "6.5-5-9 练武场AI策略限制" => "练武场 AI 使用限制候选",
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(meaning))
        {
            return string.Empty;
        }

        var strategyName = LookupName(project, tables, "6.5-5 策略", rowId.Value);
        return
            $"\r\n\r\n策略附表解释：{currentTable.TableName}\r\n" +
            $"当前行 ID={rowId.Value} 与“6.5-5 策略”同 ID 对齐；策略名称：{strategyName}\r\n" +
            $"字段“内容”的当前值：{value}；解释候选：{meaning}。\r\n" +
            "建议：此类表通常是策略主表的分栏扩展，改动后请同时检查策略主表的消耗、范围、图标，以及实际战场释放动画/伤害/AI 行为。";
    }

    private TableReferenceNavigationTarget? TryBuildStrategyCompanionNavigationTarget(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        HexTableDefinition currentTable,
        HexFieldDefinition field,
        int value,
        string sourceValue,
        string sourceRowId,
        int? rowId)
    {
        if (rowId == null || field.ColumnName != "内容")
        {
            return null;
        }

        var meaning = currentTable.TableName switch
        {
            "6.5-5-2 策略动画1" => "策略第一段动画/主动画编号",
            "6.5-5-3 策略动画2" => "策略第二段动画/附加动画编号",
            "6.5-5-4 策略伤害类型" => "策略伤害类型/公式类别候选",
            "6.5-5-5 策略伤害比例" => "策略伤害比例/倍率参数候选",
            "6.5-5-6 策略命中率" => "策略命中率参数候选",
            "6.5-5-7 学会策略" => "策略学习/可用条件候选",
            "6.5-5-8 战场AI策略限制" => "战场 AI 使用限制候选",
            "6.5-5-9 练武场AI策略限制" => "练武场 AI 使用限制候选",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(meaning))
        {
            return null;
        }

        return BuildKnownTableNavigationTarget(
            project,
            tables,
            currentTable,
            field,
            sourceValue,
            sourceRowId,
            "策略附表行对齐",
            "6.5-5 策略",
            rowId.Value,
            $"当前值 {value} 是“{meaning}”，该附表通常与策略主表按同一 ID 对齐。");
    }

    private string BuildSingleTableReference(CczProject project, IReadOnlyList<HexTableDefinition> tables, HexTableDefinition currentTable, HexFieldDefinition field, int id, ReferenceRule rule)
    {
        var table = tables.FirstOrDefault(x => x.TableName == rule.TargetTableName);
        if (table == null)
        {
            return $"\r\n\r\n跨表引用解释：字段“{field.ColumnName}”可能引用“{rule.TargetTableName}”，但当前 HexTable.xml 中未找到该表。\r\n建议：保留原值或结合原工具验证后再改。";
        }

        var data = ReadTable(project, table, tables);
        var row = FindRowById(data, id);
        if (row == null)
        {
            return
                $"\r\n\r\n跨表引用解释：字段“{field.ColumnName}”的值 {id} 按规则应引用“{rule.TargetTableName}”，但目标表没有该 ID。\r\n" +
                $"建议：这可能是未使用值、特殊值或越界引用；发布前建议改为目标表已有编号。";
        }

        var name = GetDisplayName(row);
        return
            $"\r\n\r\n跨表引用解释：{rule.DisplayName}\r\n" +
            $"当前值：{id} -> {rule.TargetTableName} / ID={id} / 名称={name}\r\n" +
            $"依据：{rule.Reason}\r\n" +
            $"建议：改动该编号时，请同时检查目标表“{rule.TargetTableName}”中对应名称和说明，避免编号正确但含义不符合设计。";
    }

    private string BuildItemReference(CczProject project, IReadOnlyList<HexTableDefinition> tables, HexTableDefinition currentTable, HexFieldDefinition field, int id)
    {
        var itemTables = new[] { "6.5-1 物品（0-103）", "6.5-2 物品（104-255）" };
        foreach (var tableName in itemTables)
        {
            var table = tables.FirstOrDefault(x => x.TableName == tableName);
            if (table == null) continue;
            var data = ReadTable(project, table, tables);
            var row = FindRowById(data, id);
            if (row == null) continue;

            var name = GetDisplayName(row);
            return
                $"\r\n\r\n跨表引用解释：物品/装备编号\r\n" +
                $"当前值：{id} -> {tableName} / ID={id} / 名称={name}\r\n" +
                $"依据：字段“{field.ColumnName}”通常存放装备、道具、商店槽位或专属物品编号。\r\n" +
                "建议：改动后请同步检查物品说明、装备特效号、图标和获得/商店配置。";
        }

        return
            $"\r\n\r\n跨表引用解释：字段“{field.ColumnName}”看起来是物品/装备编号，但未在 6.5 物品表中找到 ID={id}。\r\n" +
            "建议：若该值不是“空槽/特殊值”，请改为物品表中存在的编号。";
    }

    private string BuildEquipmentEffectReference(CczProject project, IReadOnlyList<HexTableDefinition> tables, HexFieldDefinition field, int id)
    {
        if (id == 0)
        {
            return
                $"\r\n\r\n跨表引用解释：装备特效号\r\n" +
                "当前值：0 -> 通常可视为“无装备特效/不启用特效”的候选值。\r\n" +
                "依据：宝物页对 0 保持旧版“无”语义，不从项目侧宝物特效目录或基础特效名称表取名。\r\n" +
                "建议：若物品不需要特殊效果可保持 0；若需要特效，请改为已定义的特效号，并同步检查特效值参数。";
        }

        if (id == 255)
        {
            return
                $"\r\n\r\n跨表引用解释：装备特效号\r\n" +
                "当前值：255 -> 当前宝物页按旧版逻辑视为“普通装备/无扩展特效”。\r\n" +
                "依据：宝物页对 255 保持普通装备语义，不从项目侧宝物特效目录或基础特效名称表取名。\r\n" +
                "建议：若希望该物品使用自定义特效，请改为具体特效号，并同步检查“装备特效号-效果值”。";
        }

        var projectEntries = LoadProjectEquipmentEffectEntries(project);
        var projectMatches = projectEntries
            .Where(entry => entry.EffectId == id)
            .ToList();
        if (projectMatches.Count > 0)
        {
            var effectName = BuildProjectEquipmentEffectDisplayName(projectMatches);
            var effectDescription = BuildProjectEquipmentEffectDescription(projectMatches);
            var storeFile = Path.GetFileName(_itemEffectCatalogService.GetStorePath(project));
            return
                $"\r\n\r\n跨表引用解释：装备特效号\r\n" +
                $"当前值：{id} (0x{id:X2}) -> 项目侧宝物特效目录 / 名称={effectName}\r\n" +
                (string.IsNullOrWhiteSpace(effectDescription) ? string.Empty : $"说明：{effectDescription}\r\n") +
                $"依据：字段“{field.ColumnName}”当前优先读取 `CCZModStudio_Notes\\{storeFile}` 中的 UTF-8 宝物特效目录；同一特效号允许重复，宝物页会按“名称1 / 名称2”合并显示。\r\n" +
                "建议：若需统一中文名或补充长中文说明，请在“宝物设定 -> 宝物特效”维护；同步检查本行“装备特效号-效果值”，改动后务必进战场验证。";
        }

        var baseLookup = BuildBaseEquipmentEffectNameLookup(project, tables);
        if (baseLookup.TryGetValue(id, out var baseName))
        {
            return
                $"\r\n\r\n跨表引用解释：装备特效号\r\n" +
                $"当前值：{id} (0x{id:X2}) -> 基础装备特效名称表 / 名称={baseName}\r\n" +
                $"依据：字段“{field.ColumnName}”未在项目侧宝物特效目录中命中，当前回退到 `6.5-1-2/6.5-1-3 装备特效名称` 的基础中文名。\r\n" +
                "建议：若要使用可重复编号、变长中文名或补充中文说明，请在“宝物设定 -> 宝物特效”中新增项目侧目录记录；同步检查本行“装备特效号-效果值”。";
        }

        return
            $"\r\n\r\n跨表引用解释：装备特效号\r\n" +
            $"当前值：{id} (0x{id:X2}) 未在项目侧宝物特效目录和基础装备特效名称表中找到。\r\n" +
            "建议：这可能是原版特效、未命名扩展值或越界值；若需要稳定的中文显示，请在“宝物设定 -> 宝物特效”中补充该编号的项目侧 UTF-8 记录。";
    }

    private static string BuildResourceHint(CczProject project, HexTableDefinition currentTable, HexFieldDefinition field, int id)
    {
        var name = field.ColumnName;
        if (name == "头像")
        {
            var facePath = Path.Combine(project.GameRoot, "E5", "Face.e5");
            var faceNumbers = id <= 0
                ? "Face.e5 第 1-8 张（曹操多表情遗留规则）"
                : $"Face.e5 第 {id + 8} 张；Tou.dll 真彩资源号 {id + 8 + 300}，语言 2052";
            var status = File.Exists(facePath)
                ? $"已找到候选封包：{facePath}"
                : $"未找到候选封包：{facePath}";
            return
                $"\r\n\r\n资源编号解释：头像编号 {id}\r\n" +
                $"依据：人物表“头像”不是 Face.e5 直接图片序号；当前编号映射为 {faceNumbers}。{status}\r\n" +
                "建议：当前工具可用 Ls/E5 探针确认 Face.e5 是头像资源候选；完整头像切片/替换仍需后续封包格式验证，暂不贸然写入。";
        }

        if (name.Contains("图标", StringComparison.Ordinal))
        {
            return
                $"\r\n\r\n资源编号解释：图标编号 {id}\r\n" +
                $"依据：字段“{name}”通常用于界面图标或策略/物品图标索引，具体图块多在封包资源中。\r\n" +
                "建议：目前只提供编号解释和风险提示；若要替换图标，请先用素材库、Ls/E5 探针和原工具交叉确认资源位置。";
        }

        if (name.Contains("范围", StringComparison.Ordinal))
        {
            return
                $"\r\n\r\n参数解释：范围/模板编号 = {id}\r\n" +
                $"依据：字段“{name}”通常控制攻击、施法或穿透范围，可能引用引擎内置范围模板或枚举。\r\n" +
                "建议：先对比同表中已知策略/必杀的范围值，再小范围修改并进战场验证目标选择、可选格和穿透显示。";
        }

        if (name.Contains("消耗", StringComparison.Ordinal))
        {
            return
                $"\r\n\r\n参数解释：消耗值 = {id}\r\n" +
                $"依据：字段“{name}”通常控制 MP、SP 或其他资源消耗。\r\n" +
                "建议：改动后请检查人物资源上限、AI 使用频率、战场平衡和界面显示。";
        }

        if (name is "策略类型" or "施展对象")
        {
            return
                $"\r\n\r\n参数解释：{name} = {id}\r\n" +
                $"依据：字段“{name}”通常是策略内部枚举，影响策略分类、目标阵营或可选对象。\r\n" +
                "建议：当前只提供枚举性质提示；请参考同表已有策略的相同取值，并通过实机释放确认目标选择是否符合设计。";
        }

        if (name.Contains("效果值", StringComparison.Ordinal))
        {
            return
                $"\r\n\r\n参数解释：效果值/参数 = {id}\r\n" +
                $"依据：字段“{name}”通常不是独立资源编号，而是与同一行的特效号、策略或专属规则配套使用的参数。\r\n" +
                "建议：先查看同一行的特效号/类型字段，再判断此数值含义；不同特效可能把它解释为百分比、固定值、回合数、范围或开关。";
        }

        return string.Empty;
    }

    private TableReferenceNavigationTarget BuildKnownTableNavigationTarget(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        HexTableDefinition sourceTable,
        HexFieldDefinition sourceField,
        string sourceValue,
        string sourceRowId,
        string referenceKind,
        string targetTableName,
        int targetId,
        string reason)
    {
        var targetTable = tables.FirstOrDefault(x => x.TableName == targetTableName);
        if (targetTable == null)
        {
            return new TableReferenceNavigationTarget
            {
                IsRecognized = true,
                TargetRowExists = false,
                SourceTableName = sourceTable.TableName,
                SourceRowId = sourceRowId,
                SourceFieldName = sourceField.ColumnName,
                SourceValue = sourceValue,
                ReferenceKind = referenceKind,
                TargetTableName = targetTableName,
                TargetRowId = targetId.ToString(CultureInfo.InvariantCulture),
                Summary = $"{sourceTable.TableName} / ID={sourceRowId} / {sourceField.ColumnName} = {sourceValue}：可识别为“{referenceKind}”，但 HexTable.xml 中未找到目标表“{targetTableName}”。",
                SafetyNote = reason
            };
        }

        var data = ReadTable(project, targetTable, tables);
        var row = FindRowById(data, targetId);
        var targetFieldName = GetPreferredNavigationField(targetTable, data);
        var targetName = row == null ? "(未找到目标行)" : GetDisplayName(row);
        return new TableReferenceNavigationTarget
        {
            IsRecognized = true,
            TargetRowExists = row != null && data.Columns.Contains(targetFieldName),
            SourceTableName = sourceTable.TableName,
            SourceRowId = sourceRowId,
            SourceFieldName = sourceField.ColumnName,
            SourceValue = sourceValue,
            ReferenceKind = referenceKind,
            TargetTableName = targetTable.TableName,
            TargetRowId = targetId.ToString(CultureInfo.InvariantCulture),
            TargetFieldName = targetFieldName,
            TargetName = targetName,
            Summary = $"{sourceTable.TableName} / ID={sourceRowId} / {sourceField.ColumnName} = {sourceValue} -> {targetTable.TableName} / ID={targetId} / {targetFieldName} / {targetName}",
            SafetyNote = reason
        };
    }

    private TableReferenceNavigationTarget BuildItemNavigationTarget(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        HexTableDefinition sourceTable,
        HexFieldDefinition sourceField,
        int itemId,
        string sourceValue,
        string sourceRowId,
        string reason)
    {
        foreach (var targetTableName in new[] { "6.5-1 物品（0-103）", "6.5-2 物品（104-255）" })
        {
            var targetTable = tables.FirstOrDefault(x => x.TableName == targetTableName);
            if (targetTable == null) continue;
            var data = ReadTable(project, targetTable, tables);
            var row = FindRowById(data, itemId);
            if (row == null) continue;

            var targetFieldName = GetPreferredNavigationField(targetTable, data);
            var targetName = GetDisplayName(row);
            return new TableReferenceNavigationTarget
            {
                IsRecognized = true,
                TargetRowExists = data.Columns.Contains(targetFieldName),
                SourceTableName = sourceTable.TableName,
                SourceRowId = sourceRowId,
                SourceFieldName = sourceField.ColumnName,
                SourceValue = sourceValue,
                ReferenceKind = "物品/装备编号",
                TargetTableName = targetTable.TableName,
                TargetRowId = itemId.ToString(CultureInfo.InvariantCulture),
                TargetFieldName = targetFieldName,
                TargetName = targetName,
                Summary = $"{sourceTable.TableName} / ID={sourceRowId} / {sourceField.ColumnName} = {sourceValue} -> {targetTable.TableName} / ID={itemId} / {targetFieldName} / {targetName}",
                SafetyNote = reason
            };
        }

        var fallbackTableName = itemId <= 103 ? "6.5-1 物品（0-103）" : "6.5-2 物品（104-255）";
        return BuildKnownTableNavigationTarget(project, tables, sourceTable, sourceField, sourceValue, sourceRowId, "物品/装备编号", fallbackTableName, itemId, reason);
    }

    private TableReferenceNavigationTarget BuildEquipmentEffectNavigationTarget(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        HexTableDefinition sourceTable,
        HexFieldDefinition sourceField,
        int effectId,
        string sourceValue,
        string sourceRowId,
        string reason)
    {
        if (effectId == 0)
        {
            return new TableReferenceNavigationTarget
            {
                IsRecognized = true,
                IsOptionalEmpty = true,
                TargetRowExists = false,
                SourceTableName = sourceTable.TableName,
                SourceRowId = sourceRowId,
                SourceFieldName = sourceField.ColumnName,
                SourceValue = sourceValue,
                ReferenceKind = "装备特效号",
                Summary = $"{sourceTable.TableName} / ID={sourceRowId} / {sourceField.ColumnName} = 0：通常表示无装备特效，不跳转到特效名称表或项目侧目录。",
                SafetyNote = reason
            };
        }

        if (effectId == 255)
        {
            return new TableReferenceNavigationTarget
            {
                IsRecognized = true,
                IsOptionalEmpty = true,
                TargetRowExists = false,
                SourceTableName = sourceTable.TableName,
                SourceRowId = sourceRowId,
                SourceFieldName = sourceField.ColumnName,
                SourceValue = sourceValue,
                ReferenceKind = "装备特效号",
                Summary = $"{sourceTable.TableName} / ID={sourceRowId} / {sourceField.ColumnName} = 255：当前宝物页按旧版逻辑视为普通装备/无扩展特效，不跳转到特效名称表或项目侧目录。",
                SafetyNote = "若需要使用自定义特效，请改为具体特效号，并在“宝物设定 -> 宝物特效”维护项目侧中文名。"
            };
        }

        var projectEntries = LoadProjectEquipmentEffectEntries(project);
        var projectMatches = projectEntries
            .Where(entry => entry.EffectId == effectId)
            .ToList();
        if (projectMatches.Count > 0)
        {
            var effectName = BuildProjectEquipmentEffectDisplayName(projectMatches);
            var storeFile = Path.GetFileName(_itemEffectCatalogService.GetStorePath(project));
            return new TableReferenceNavigationTarget
            {
                IsRecognized = true,
                TargetRowExists = false,
                SourceTableName = sourceTable.TableName,
                SourceRowId = sourceRowId,
                SourceFieldName = sourceField.ColumnName,
                SourceValue = sourceValue,
                ReferenceKind = "装备特效号",
                Summary = $"{sourceTable.TableName} / ID={sourceRowId} / {sourceField.ColumnName} = 0x{effectId:X2}：项目侧宝物特效目录已定义，当前名称={effectName}。",
                SafetyNote = $"当前“跳到数据表”只支持 HexTable 表格导航；项目侧 UTF-8 目录请在“宝物设定 -> 宝物特效”中维护。目录文件：CCZModStudio_Notes\\{storeFile}"
            };
        }

        var targetTableName = effectId switch
        {
            >= 0x1A and <= 0x57 => "6.5-1-2 装备特效名称（1A-57）",
            >= 0x58 and <= 0x7F => "6.5-1-3 装备特效名称（58-7F）",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(targetTableName))
        {
            return new TableReferenceNavigationTarget
            {
                IsRecognized = true,
                TargetRowExists = false,
                SourceTableName = sourceTable.TableName,
                SourceRowId = sourceRowId,
                SourceFieldName = sourceField.ColumnName,
                SourceValue = sourceValue,
                ReferenceKind = "装备特效号",
                Summary = $"{sourceTable.TableName} / ID={sourceRowId} / {sourceField.ColumnName} = 0x{effectId:X2}：既不在项目侧宝物特效目录中，也不在当前已确认基础特效名称表范围内，暂不跳转。",
                SafetyNote = "可能是未使用值、特殊值或需要继续研究的扩展值；建议添加创作者备注，或在“宝物设定 -> 宝物特效”中补充项目侧目录后再实机验证。"
            };
        }

        var targetTable = tables.FirstOrDefault(x => x.TableName == targetTableName);
        if (targetTable == null)
        {
            return new TableReferenceNavigationTarget
            {
                IsRecognized = true,
                TargetRowExists = false,
                SourceTableName = sourceTable.TableName,
                SourceRowId = sourceRowId,
                SourceFieldName = sourceField.ColumnName,
                SourceValue = sourceValue,
                ReferenceKind = "装备特效号",
                TargetTableName = targetTableName,
                Summary = $"{sourceTable.TableName} / ID={sourceRowId} / {sourceField.ColumnName} = 0x{effectId:X2}：目标表“{targetTableName}”未找到。",
                SafetyNote = reason
            };
        }

        var data = ReadTable(project, targetTable, tables);
        var targetFieldName = data.Columns
            .Cast<DataColumn>()
            .Where(column => column.ColumnName != "ID")
            .Select(column => column.ColumnName)
            .FirstOrDefault(columnName => TryParseLeadingHexId(columnName, out var parsed) && parsed == effectId) ?? string.Empty;
        var row = data.Rows.Cast<DataRow>().FirstOrDefault();
        var targetRowId = row != null && data.Columns.Contains("ID")
            ? Convert.ToString(row["ID"], CultureInfo.InvariantCulture) ?? "0"
            : "0";
        var targetName = row != null && !string.IsNullOrWhiteSpace(targetFieldName)
            ? Convert.ToString(row[targetFieldName], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty
            : string.Empty;

        return new TableReferenceNavigationTarget
        {
            IsRecognized = true,
            TargetRowExists = row != null && !string.IsNullOrWhiteSpace(targetFieldName),
            SourceTableName = sourceTable.TableName,
            SourceRowId = sourceRowId,
            SourceFieldName = sourceField.ColumnName,
            SourceValue = sourceValue,
            ReferenceKind = "装备特效号",
            TargetTableName = targetTable.TableName,
            TargetRowId = targetRowId,
            TargetFieldName = targetFieldName,
            TargetName = targetName,
            Summary = $"{sourceTable.TableName} / ID={sourceRowId} / {sourceField.ColumnName} = 0x{effectId:X2} -> {targetTable.TableName} / ID={targetRowId} / {targetFieldName} / {targetName}",
            SafetyNote = "当前未命中项目侧宝物特效目录，故跳转到基础装备特效名称表；若宝物页显示名需按项目定制，请改用“宝物设定 -> 宝物特效”维护。"
        };
    }

    private IReadOnlyList<ItemEffectCatalogEntry> LoadProjectEquipmentEffectEntries(CczProject project)
    {
        var storePath = _itemEffectCatalogService.GetStorePath(project);
        if (!File.Exists(storePath))
        {
            return Array.Empty<ItemEffectCatalogEntry>();
        }

        return _itemEffectCatalogService.Load(project);
    }

    private IReadOnlyDictionary<int, string> BuildBaseEquipmentEffectNameLookup(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var result = new Dictionary<int, string>();
        foreach (var tableName in new[] { "6.5-1-2 装备特效名称（1A-57）", "6.5-1-3 装备特效名称（58-7F）" })
        {
            var table = tables.FirstOrDefault(x => x.TableName == tableName);
            if (table == null) continue;

            var data = ReadTable(project, table, tables);
            if (data.Rows.Count == 0) continue;
            var row = data.Rows[0];

            foreach (DataColumn column in data.Columns)
            {
                if (column.ColumnName == "ID") continue;
                if (!TryParseLeadingHexId(column.ColumnName, out var effectId)) continue;

                var effectName = SanitizeEquipmentEffectText(Convert.ToString(row[column], CultureInfo.InvariantCulture) ?? string.Empty);
                if (string.IsNullOrWhiteSpace(effectName)) continue;
                result[effectId] = effectName;
            }
        }

        return result;
    }

    private static string BuildProjectEquipmentEffectDisplayName(IEnumerable<ItemEffectCatalogEntry> entries)
    {
        var names = entries
            .Select(entry => entry.Name?.Trim() ?? string.Empty)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCulture)
            .ToList();
        return names.Count == 0 ? "(名称为空，需补充项目侧目录)" : string.Join(" / ", names);
    }

    private static string BuildProjectEquipmentEffectDescription(IEnumerable<ItemEffectCatalogEntry> entries)
    {
        return string.Join(" / ", entries
            .Select(entry => entry.Description?.Trim() ?? string.Empty)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.CurrentCulture));
    }

    private static string SanitizeEquipmentEffectText(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return new string(value.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
    }

    private static TableReferenceNavigationTarget BuildUnrecognizedNavigationTarget(
        HexTableDefinition sourceTable,
        HexFieldDefinition sourceField,
        string sourceRowId,
        string sourceValue,
        string summary)
        => new()
        {
            IsRecognized = false,
            TargetRowExists = false,
            SourceTableName = sourceTable.TableName,
            SourceRowId = sourceRowId,
            SourceFieldName = sourceField.ColumnName,
            SourceValue = sourceValue,
            Summary = summary
        };

    private static string GetPreferredNavigationField(HexTableDefinition table, DataTable data)
    {
        foreach (var name in new[] { "名称", "介绍", "内容" })
        {
            if (data.Columns.Contains(name))
            {
                return name;
            }
        }

        if (data.Columns.Contains("ID"))
        {
            return "ID";
        }

        return table.Fields.FirstOrDefault()?.ColumnName ?? "ID";
    }

    private string LookupName(CczProject project, IReadOnlyList<HexTableDefinition> tables, string tableName, int id)
    {
        var table = tables.FirstOrDefault(x => x.TableName == tableName);
        if (table == null) return "(未找到目标表)";
        var data = ReadTable(project, table, tables);
        var row = FindRowById(data, id);
        return row == null ? "(未找到目标行)" : GetDisplayName(row);
    }

    private static ReferenceRule? GetRule(HexTableDefinition currentTable, HexFieldDefinition field)
    {
        var tableName = currentTable.TableName;
        var name = field.ColumnName;

        if (name == "撤退台词") return new ReferenceRule(ReferenceRuleKind.SingleTable, "6.5-0-3 撤退台词", "撤退台词编号", "人物表字段名直接指向撤退台词表。");
        if (name == "暴击台词") return new ReferenceRule(ReferenceRuleKind.SingleTable, "6.5-0-2 暴击台词", "暴击台词编号", "人物表字段名直接指向暴击台词表。");
        if (name == "职业" || name == "兵种") return new ReferenceRule(ReferenceRuleKind.SingleTable, "6.5-4 详细兵种", "职业/详细兵种编号", "职业、兵种字段通常引用详细兵种表。");
        if (name == "装备特效号") return new ReferenceRule(ReferenceRuleKind.EquipmentEffect, string.Empty, "装备特效号", "装备特效号通常优先引用项目侧宝物特效目录，未命中时再回退到基础装备特效名称表。");
        if (name.Contains("武将", StringComparison.Ordinal) || name is "开关仓库人物" or "买卖物品人物")
        {
            return new ReferenceRule(ReferenceRuleKind.SingleTable, "6.5-0 人物", "人物/武将编号", "字段名包含武将/人物，通常引用人物主表。");
        }

        if (IsItemReference(tableName, name))
        {
            return new ReferenceRule(ReferenceRuleKind.Item, string.Empty, "物品/装备编号", "字段名或表名显示这是装备、道具、商店或专属物品引用。");
        }

        return null;
    }

    private static bool TryParseLeadingHexId(string columnName, out int id)
    {
        id = 0;
        var text = new string(columnName.TakeWhile(IsHexChar).ToArray());
        return text.Length > 0 && int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);
    }

    private static bool IsHexChar(char ch) =>
        (ch >= '0' && ch <= '9') ||
        (ch >= 'a' && ch <= 'f') ||
        (ch >= 'A' && ch <= 'F');

    private static bool IsItemReference(string tableName, string fieldName)
    {
        if (fieldName.Contains("装备特效", StringComparison.Ordinal)) return false;
        if (fieldName.Contains("装备", StringComparison.Ordinal) || fieldName.Contains("道具", StringComparison.Ordinal) || fieldName.Contains("物品", StringComparison.Ordinal)) return true;
        if (tableName.Contains("商店数据", StringComparison.Ordinal) &&
            fieldName is not "开关仓库人物" and not "买卖物品人物")
        {
            return true;
        }

        return false;
    }

    private static bool IsLikelyOptionalReference(HexTableDefinition table, HexFieldDefinition field)
    {
        var name = field.ColumnName;
        return table.TableName.Contains("商店", StringComparison.Ordinal) ||
               table.TableName.Contains("专属", StringComparison.Ordinal) ||
               name.Contains("装备", StringComparison.Ordinal) ||
               name.Contains("道具", StringComparison.Ordinal) ||
               name.Contains("武将", StringComparison.Ordinal);
    }

    private DataTable ReadTable(CczProject project, HexTableDefinition table, IReadOnlyList<HexTableDefinition> tables)
    {
        var key = project.GameRoot + "|" + table.TableName;
        if (_tableCache.TryGetValue(key, out var cached)) return cached;

        var reader = new HexTableReader();
        var result = reader.Read(project, table, tables);
        _tableCache[key] = result.Data;
        return result.Data;
    }

    private static DataRow? FindRowById(DataTable data, int id)
    {
        if (!data.Columns.Contains("ID")) return null;
        return data.Rows.Cast<DataRow>().FirstOrDefault(row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) == id);
    }

    private static string GetDisplayName(DataRow row)
    {
        if (row.Table.Columns.Contains("名称"))
        {
            return Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        }

        for (var i = 1; i < row.Table.Columns.Count; i++)
        {
            var value = Convert.ToString(row[i], CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return "(未命名)";
    }

    private static bool TryParseInt(object value, out int id)
    {
        switch (value)
        {
            case byte b:
                id = b;
                return true;
            case short s:
                id = s;
                return true;
            case ushort us:
                id = us;
                return true;
            case int i:
                id = i;
                return true;
            case long l when l >= int.MinValue && l <= int.MaxValue:
                id = (int)l;
                return true;
            default:
                var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    return int.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);
                }

                return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
        }
    }

    private sealed record ReferenceRule(ReferenceRuleKind Kind, string TargetTableName, string DisplayName, string Reason);

    private enum ReferenceRuleKind
    {
        SingleTable,
        Item,
        EquipmentEffect
    }
}
