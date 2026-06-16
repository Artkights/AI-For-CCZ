using System.Globalization;
using System.Data;
using System.Text;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class FieldAnnotationService
{
    public string BuildShortFieldAnnotation(HexTableDefinition table, HexFieldDefinition field)
    {
        var semantic = ClassifySemantic(table, field);
        var storage = BuildShortStorage(field);
        return string.IsNullOrWhiteSpace(semantic.ShortName) ? storage : $"{semantic.ShortName}/{storage}";
    }

    public string GetSemanticShortName(HexTableDefinition table, HexFieldDefinition field)
        => ClassifySemantic(table, field).ShortName;

    public bool IsHighRiskField(HexTableDefinition table, HexFieldDefinition field)
    {
        return !string.IsNullOrWhiteSpace(GetRiskReason(table, field));
    }

    public string GetRiskReason(HexTableDefinition table, HexFieldDefinition field)
    {
        if (!field.ConsumesBytes) return string.Empty;
        if (field.Kind == HexFieldKind.RawBytes) return "原始字节字段：内部结构未完全拆分，误改容易破坏相邻含义。";

        var semantic = ClassifySemantic(table, field).ShortName;
        if (semantic is "原始" or "资源" or "AI" or "效果" or "地形" or "策略" or "兵种" or "装备特效" or "特效参数")
        {
            return semantic switch
            {
                "资源" => "资源编号字段：可能关联 RS/EEX/头像/形象等外部资源，编号不存在时容易显示异常或崩溃。",
                "装备特效" => "装备特效编号字段：会改变装备附带效果，且常与效果值参数联动，需要实战验证。",
                "特效参数" => "特效参数字段：含义取决于同一行特效号，误改可能导致效果过强、无效或异常。",
                "AI" => "AI/行动字段：可能改变敌军行动逻辑，误改可能导致不行动、卡死或战斗节奏异常。",
                "效果" => "效果/状态字段：可能关联战斗效果、动画或状态编号，需要实战验证。",
                "地形" => "地形/适性字段：会影响移动、攻防修正或地图事件，需要逐关验证。",
                "策略" => "策略/法术字段：可能影响目标选择、范围、消耗或特效，需要进战场测试。",
                "兵种" => "兵种/职业字段：会联动成长、装备限制、移动和策略，跨表影响较大。",
                _ => "高风险字段：含义未完全确认或跨资源/跨脚本联动较多。"
            };
        }

        if (ContainsAny(field.ColumnName, "地址", "偏移", "指针")) return "地址/偏移/指针字段：误改可能指向错误数据区域。";
        if (ContainsAny(field.ColumnName, "脚本", "命令", "事件")) return "脚本/命令/事件字段：可能影响剧情流程或战场事件。";
        if (ContainsAny(field.ColumnName, "特效", "状态", "异常")) return "特效/状态字段：可能关联战斗效果、动画或异常状态编号。";
        if (ContainsAny(field.ColumnName, "形象", "头像")) return "形象/头像字段：可能关联外部资源编号。";

        return string.Empty;
    }

    public string BuildFieldAnnotation(HexTableDefinition table, HexFieldDefinition field)
    {
        if (!field.ConsumesBytes)
        {
            return $"【中文注释】{field.ColumnName} 是辅助显示列，通常由索引表推导而来，不写入 {table.FileName}。它常用于让创作者把编号和名称对应起来。";
        }

        var semantic = ClassifySemantic(table, field);
        var riskReason = GetRiskReason(table, field);
        return
            $"【中文注释】表：{table.TableName}；文件：{table.FileName}；字段：{field.ColumnName}。\r\n" +
            $"存储：{BuildStorageAnnotation(field)}\r\n" +
            $"用途：{semantic.Description}\r\n" +
            (string.IsNullOrWhiteSpace(riskReason) ? string.Empty : $"风险提示：{riskReason}\r\n") +
            $"创作提示：{semantic.AuthoringTip}";
    }

    public string BuildTableSummary(HexTableDefinition table, HexTableValidationResult validation, bool canWrite)
    {
        var writable = canWrite && validation.IsUsable;
        var writeMode = writable
            ? "当前表可编辑：保存会直接写入当前 MOD 项目，并在写入前自动备份目标文件。"
            : validation.IsUsable
                ? "当前表可读取；如界面仍无法编辑，请检查具体列是否为 ID/辅助显示列。"
                : "当前表结构检查未通过，不能保存。";
        var warnings = validation.Warnings.Count == 0 ? "无结构警告。" : string.Join("；", validation.Warnings);
        return
            $"表说明：{table.TableName}\r\n" +
            $"文件：{table.FileName}    行数：{table.RowCount}    行长：{table.RowSize} 字节    起始偏移：{HexDisplayFormatter.FormatOffset(table.DataPos)}\r\n" +
            $"字段数：{table.Fields.Count}    版本：{table.Version}    ReadOnly标记={table.ReadOnly}（不再作为写入拦截）\r\n" +
            $"{writeMode}\r\n" +
            $"结构检查：{warnings}\r\n" +
            "操作建议：先选中单元格查看字段说明；不确定含义的原始字节字段不要直接大批量修改。";
    }

    public string BuildCellAnnotation(HexTableDefinition table, HexFieldDefinition field, int rowId, object? value)
    {
        var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        var extra = field.Kind == HexFieldKind.FixedString
            ? $"当前文本 GBK 字节数：{EncodingService.GetGbkByteCount(text)}/{field.Size}。"
            : string.Empty;
        return
            $"当前单元格：ID={rowId}，字段={field.ColumnName}，值={text}\r\n" +
            extra +
            (string.IsNullOrEmpty(extra) ? string.Empty : "\r\n") +
            BuildFieldAnnotation(table, field);
    }

    public string ExportAnnotations(CczProject project, HexTableDefinition table, HexTableValidationResult validation, DataTable? data = null, Func<HexFieldDefinition, string>? referenceHintProvider = null)
    {
        var dir = project.IsTestCopy
            ? Path.Combine(project.GameRoot, "_CCZModStudio_Exports", "TableAnnotations")
            : Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", "TableAnnotations", project.Name);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, MakeSafeFileName(table.TableName) + "_字段注释.csv");
        var lines = new List<string>
        {
            "TableName,FileName,DataPos,RowCount,ColumnName,ShortAnnotation,Semantic,Kind,Size,ConsumesBytes,HighRisk,RiskReason,ReferenceHint,SampleValues,MinValue,MaxValue,TopValues,Annotation"
        };

        foreach (var field in table.Fields)
        {
            var profile = BuildValueProfile(data, field);
            var referenceHint = referenceHintProvider?.Invoke(field) ?? string.Empty;
            lines.Add(string.Join(',',
                Csv(table.TableName),
                Csv(table.FileName),
                Csv(HexDisplayFormatter.FormatOffset(table.DataPos)),
                Csv(table.RowCount.ToString(CultureInfo.InvariantCulture)),
                Csv(field.ColumnName),
                Csv(BuildShortFieldAnnotation(table, field)),
                Csv(GetSemanticShortName(table, field)),
                Csv(field.Kind.ToString()),
                Csv(field.Size.ToString(CultureInfo.InvariantCulture)),
                Csv(field.ConsumesBytes ? "true" : "false"),
                Csv(IsHighRiskField(table, field) ? "true" : "false"),
                Csv(GetRiskReason(table, field)),
                Csv(referenceHint),
                Csv(profile.SampleValues),
                Csv(profile.MinValue),
                Csv(profile.MaxValue),
                Csv(profile.TopValues),
                Csv(BuildFieldAnnotation(table, field))));
        }

        lines.Add(string.Empty);
        lines.Add(Csv(BuildTableSummary(table, validation, validation.IsUsable)));
        File.WriteAllLines(path, lines, Encoding.UTF8);
        return path;
    }

    private static ValueProfile BuildValueProfile(DataTable? data, HexFieldDefinition field)
    {
        if (data == null || !data.Columns.Contains(field.ColumnName))
        {
            return new ValueProfile(string.Empty, string.Empty, string.Empty, string.Empty);
        }

        var values = data.Rows
            .Cast<DataRow>()
            .Select(row => Convert.ToString(row[field.ColumnName], CultureInfo.InvariantCulture) ?? string.Empty)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        if (values.Count == 0)
        {
            return new ValueProfile(string.Empty, string.Empty, string.Empty, string.Empty);
        }

        var samples = string.Join(" / ", values.Distinct(StringComparer.Ordinal).Take(8));
        var topValues = string.Join(" / ", values
            .GroupBy(x => x, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Take(8)
            .Select(g => $"{g.Key}:{g.Count()}"));

        var numericValues = values
            .Select(TryParseInvariantDouble)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToList();
        if (numericValues.Count == values.Count)
        {
            return new ValueProfile(
                samples,
                numericValues.Min().ToString("0.###", CultureInfo.InvariantCulture),
                numericValues.Max().ToString("0.###", CultureInfo.InvariantCulture),
                topValues);
        }

        return new ValueProfile(samples, string.Empty, string.Empty, topValues);
    }

    private static double? TryParseInvariantDouble(string text)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            ulong.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
        {
            return hex;
        }

        return double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string BuildShortStorage(HexFieldDefinition field)
    {
        if (!field.ConsumesBytes) return "引用";
        return field.Kind switch
        {
            HexFieldKind.UInt8 => "1B",
            HexFieldKind.UInt16 => "2B",
            HexFieldKind.UInt32 => "4B",
            HexFieldKind.FixedString => $"GBK≤{field.Size}B",
            HexFieldKind.RawBytes => $"{field.Size}B",
            _ => "字段"
        };
    }

    private static string BuildStorageAnnotation(HexFieldDefinition field)
    {
        return field.Kind switch
        {
            HexFieldKind.UInt8 => "无符号 1 字节数值，通常范围 0..255。",
            HexFieldKind.UInt16 => "小端 2 字节数值，通常范围 0..65535。",
            HexFieldKind.UInt32 => "小端 4 字节数值，常用于大数值、地址、标志组合或资源编号。",
            HexFieldKind.FixedString => $"GBK 固定长度文本，容量 {field.Size} 字节；中文通常占 2 字节，保存时不能超长。",
            HexFieldKind.RawBytes => $"{field.Size} 字节原始数据；字段内部结构未完全拆分，建议只在理解格式后编辑。",
            _ => "字段类型未完全确认。"
        };
    }

    private static SemanticAnnotation ClassifySemantic(HexTableDefinition table, HexFieldDefinition field)
    {
        var name = field.ColumnName;
        var tableName = table.TableName;

        if (ContainsAny(name, "名称", "名字", "称号")) return new("名称", "显示名称/列表名，常用于人物、物品、兵种、策略或地形的可读名称。", "改名最常见，但必须注意 GBK 字节容量；保存前建议导出 CSV 备份。");
        if (ContainsAny(name, "介绍", "说明", "列传", "台词")) return new("文本", "面向玩家显示的说明、列传或台词文本。", "适合做剧情和说明修改；请控制长度，避免覆盖后续固定字段。");
        if (ContainsAny(name, "级别", "等级", "Lv", "LV")) return new("等级", "等级、级别或成长阶段字段，可能影响初始强度和出场状态。", "建议小范围调整并进游戏验证，避免超出原游戏逻辑期望。");
        if (ContainsAny(name, "HP", "MP", "生命", "策略值", "气力")) return new("HP/MP", "生命、策略值或资源量相关字段。", "改动会直接影响难度；建议结合兵种、装备和策略消耗一起平衡。");
        if (ContainsAny(name, "攻击", "防御", "精神", "爆发", "士气", "武力", "智力", "统率", "敏捷", "运气")) return new("能力", "人物五维、战斗能力或成长相关字段。", "数值越高通常越强；批量修改前建议用分布图观察原始范围。");
        if (ContainsAny(name, "移动", "Move", "MOV")) return new("移动", "移动力或移动相关参数，影响战场走位和 AI 可达范围。", "过高可能破坏地图节奏；需要实际开局测试。");
        if (ContainsAny(name, "价格", "售价", "买价", "卖价", "金钱", "功勋")) return new("经济", "商店、奖励或经济相关数值。", "建议与掉落、奖励和商店开放节奏一起检查。");
        if (ContainsAny(name, "R形象", "S形象", "B形象", "形象", "头像", "Face", "face")) return new("资源", "人物形象、头像或资源编号，通常关联 Face.e5、Pmapobj.e5、Unit_*.e5 或其他资源封包。", "修改后建议使用人物 R/S 形象页联动检查，并确认对应资源确实存在。");
        if (ContainsAny(name, "兵种", "职业", "部队")) return new("兵种", "兵种、职业或部队类别编号，会影响能力成长、移动适性和可用策略。", "跨兵种修改要同时检查成长、装备限制和剧本出场。");
        if (name.Contains("装备特效号-效果值", StringComparison.Ordinal)) return new("特效参数", "装备特效的参数值，具体含义取决于同一行的装备特效号。", "建议先选中装备特效号查看名称，再小范围调整效果值并进战场验证。");
        if (name.Contains("装备特效号", StringComparison.Ordinal)) return new("装备特效", "装备附加效果编号，当前优先读取项目侧宝物特效目录，未命中时再回退到基础装备特效名称表。", "建议在说明面板确认编号对应的特效名称；若需变长中文名或补充说明，请在“宝物设定 -> 宝物特效”维护，并同步检查效果值参数。");
        if (ContainsAny(name, "装备", "武器", "防具", "辅助", "宝物", "物品", "道具")) return new("物品", "物品、装备、宝物或道具关联字段。", "建议结合物品表、宝物效果和商店/掉落一起验证。");
        if (ContainsAny(name, "策略", "法术", "MP", "消耗", "范围", "射程")) return new("策略", "策略/法术、消耗、范围或射程相关字段。", "需要结合策略表、地形、AI 和动画特效测试。");
        if (ContainsAny(name, "地形", "适性", "天气", "Map", "map")) return new("地形", "地形、适性、地图或环境参数。", "改动可能影响移动、攻防修正和地图事件；建议逐关验证。");
        if (ContainsAny(name, "效果", "特效", "状态", "异常", "Buff", "Debuff")) return new("效果", "效果、特效或状态编号，可能与战斗脚本和动画资源关联。", "若值含义不明，请先复制一项小范围试验。");
        if (ContainsAny(name, "AI", "ai", "行动", "优先", "策略倾向")) return new("AI", "AI 行动、倾向或优先级相关字段。", "改动后需要在战场内观察敌军行动，避免卡死或不行动。");
        if (field.Kind == HexFieldKind.RawBytes) return new("原始", "尚未拆分语义的原始字节块。", "建议先记录原值和用途证据；不要在未确认格式时大批量改动。");
        if (tableName.Contains("人物", StringComparison.Ordinal)) return new("人物", "人物表字段，通常影响角色属性、出场或资源关联。", "优先结合人物、R/S 形象、列传和剧本出场做联动检查。");
        if (tableName.Contains("物品", StringComparison.Ordinal) || tableName.Contains("宝物", StringComparison.Ordinal)) return new("物品", "物品/宝物表字段，通常影响装备、道具、价格或特殊效果。", "改动后建议检查商店、掉落、装备限制和战斗效果。");
        if (tableName.Contains("策略", StringComparison.Ordinal)) return new("策略", "策略表字段，通常影响法术消耗、范围、效果或动画。", "改动后需要进战场测试释放条件、目标选择和显示效果。");
        if (tableName.Contains("兵种", StringComparison.Ordinal)) return new("兵种", "兵种表字段，通常影响成长、地形、移动和装备适性。", "建议配合人物表和关卡敌军配置一起测试。");
        return new("字段", "来自 HexTable.xml 的字段；当前仅能根据字段名和存储类型给出通用解释。", "若含义不明确，请结合原工具、已有 MOD 经验、导出 CSV 和测试副本反复验证。");
    }

    private static bool ContainsAny(string text, params string[] keywords)
        => keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static string MakeSafeFileName(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(ch, '_');
        }
        return name;
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private sealed record SemanticAnnotation(string ShortName, string Description, string AuthoringTip);
    private sealed record ValueProfile(string SampleValues, string MinValue, string MaxValue, string TopValues);
}
