using System.Globalization;
using System.Text;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio;

internal sealed class LegacyScenarioCommandDisplayFormatter
{
    private readonly LegacyMfcDialogDataSources _dataSources;

    public LegacyScenarioCommandDisplayFormatter(LegacyMfcDialogDataSources dataSources)
    {
        _dataSources = dataSources;
    }

    public string FormatCommand(LegacyScenarioCommandNode command, bool includeIdentity = false)
    {
        var label = $"{command.CommandId:X}:{_dataSources.CommandName(command.CommandId, command.CommandName)}";
        var suffix = BuildLegacyUpdateShowSuffix(command);
        return string.IsNullOrWhiteSpace(suffix) ? label : $"{label} {suffix}";
    }

    public string FormatValuesPreview(LegacyScenarioCommandNode command, int maxVisibleValues)
    {
        var suffix = BuildLegacyUpdateShowSuffix(command);
        if (!string.IsNullOrWhiteSpace(suffix))
        {
            return TrimSingleLine(suffix, 132);
        }

        var values = command.Parameters
            .Select(parameter => parameter.Kind switch
            {
                LegacyScenarioParameterKind.Text => $"T{parameter.Index}=\"{TrimSingleLine(parameter.Text, 24)}\"",
                LegacyScenarioParameterKind.VariableArray => $"V{parameter.Index}[{parameter.Values.Count}]",
                _ => $"P{parameter.Index}={parameter.IntValue.ToString(CultureInfo.InvariantCulture)}"
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(Math.Max(0, maxVisibleValues))
            .ToList();
        return values.Count == 0 ? string.Empty : TrimSingleLine(string.Join(" ", values), 132);
    }

    private string BuildLegacyUpdateShowSuffix(LegacyScenarioCommandNode command)
    {
        var id = command.CommandId;
        var t = Text(command);

        if (id is 0x02 or 0x14 or 0x16 or 0x17 or 0x18 or 0x19 or 0x69 or 0x7A)
        {
            return EscapeTreeText(t);
        }

        return id switch
        {
            0x04 => I(command, 0) == 1 ? "选是" : "选否",
            0x05 => FormatVariableArrayTest(command),
            0x06 or 0x4A => FormatPerson1List(command, id == 0x06),
            0x08 => I(command, 0) == 1 ? "true" : "false",
            0x09 or 0x13 or 0x28 or 0x6E or 0x71 or 0x76 => N(I(command, 0)),
            0x0B => $"{N(I(command, 0))} {(I(command, 1) == 0 ? "false" : "true")}",
            0x0F => $"结局{I(command, 0) + 1}",
            0x11 => FormatScenarioFile(I(command, 0)),
            0x12 => $"{EscapeTreeText(t)} {Per2(I(command, 0))}",
            0x15 => $"{EscapeTreeText(t)} {Per2(I(command, 0))} {Per2(I(command, 1))}",
            0x1B => $"{Per2(I(command, 0))} {(I(command, 1) == 0 ? "false" : "true")}",
            0x1F => $"{N(I(command, 0))} {N(I(command, 1))}",
            0x20 => FaceCondition(I(command, 0)),
            0x21 => $"({N(I(command, 0))} {N(I(command, 1))}) {(I(command, 2) == 0 ? "放火" : "恢复")} {(I(command, 3) == 0 ? "视点不切换" : "视点切换")} {(I(command, 4) == 0 ? "无音效" : "播放音效")}",
            0x22 => At(_dataSources.Movie, I(command, 0)),
            0x23 => $"{SoundCode(I(command, 0))} {N(I(command, 1))}",
            0x24 => $"Track{(I(command, 0) == 255 ? " 无" : N(I(command, 0) + 2))}",
            0x25 or 0x29 or 0x2A => $"{Per2(I(command, 0))} ({N(I(command, 1))},{N(I(command, 2))})",
            0x26 => $"{Per2(I(command, 0))} ({N(I(command, 1))},{N(I(command, 2))}) - ({N(I(command, 3))},{N(I(command, 4))})",
            0x27 => FormatBackground(command),
            0x2B or 0x2D or 0x5C => Per2(I(command, 0)),
            0x2C => $"{EscapeTreeText(t)} {(I(command, 0) == 1 ? "不" : string.Empty)}换页 {(I(command, 0) == 0 ? "不" : string.Empty)}换行 {(I(command, 0) == 0 ? "不" : string.Empty)}等待",
            0x2E or 0x5E or 0x6C => FormatTwoPersons(command, id == 0x2E),
            0x30 => $"{Per2(I(command, 0))} ({N(I(command, 1))},{N(I(command, 2))}) {Sentinel(_dataSources.Direction, I(command, 3), 4)} {Sentinel(_dataSources.Gesture, I(command, 4), 20)}",
            0x31 => FormatSingleOrArea(command, 0),
            0x32 or 0x55 => FormatMovementDataOrBattleNo(command),
            0x33 => $"{Per2(I(command, 0))} {Sentinel(_dataSources.Gesture, I(command, 1), 20)} {Sentinel(_dataSources.Direction, I(command, 2), 4)}",
            0x34 => $"{Per2(I(command, 0))} {Sentinel(_dataSources.Gesture, I(command, 1), 20)}",
            0x35 or 0x39 or 0x75 => $"{Per2(I(command, 0))} {N(I(command, 1))}",
            0x36 => $"{Per2(I(command, 0))} {At(_dataSources.PersonCondition, I(command, 1))} {N(I(command, 2))} {At(_dataSources.Compare, I(command, 3))}",
            0x37 => $"{GlobalValueName(I(command, 0))} {N(I(command, 1))} {At(_dataSources.Compare, I(command, 2))}",
            0x38 => $"{Per2(I(command, 0))} {At(_dataSources.PersonCondition, I(command, 1))} {At(_dataSources.Operate, I(command, 2))} {N(I(command, 3))}",
            0x3A => $"{GlobalValueName(I(command, 0))} {At(_dataSources.Operate, I(command, 1))} {N(I(command, 2))}",
            0x3B => FormatJoinLevel(command),
            0x3C => $"{Per2(I(command, 0))} {JoinCondition(I(command, 1))} {(I(command, 2) == 0 ? "!=" : "=")}",
            0x3D => FormatItemGain(command),
            0x3E or 0x48 => FormatEquipmentSet(command),
            0x3F => $"{N(I(command, 0))} {At(_dataSources.Compare, I(command, 1))}",
            0x40 => I(command, 0) switch { 0 => "我军阶段", 1 => "友军阶段", _ => "敌军阶段" },
            0x41 => FormatCampCount(command),
            0x45 => FormatBattleInit(command),
            0x4B => $"{N(I(command, 0))} ({N(I(command, 1))},{N(I(command, 2))}) {Sentinel(_dataSources.Direction, I(command, 3), 4)} {(I(command, 4) == 0 ? "不隐藏" : "隐藏")}",
            0x4C => FormatDataOrBattleNoShort(command),
            0x4D => FormatBattleConditionChange(command),
            0x4E => FormatPolicy(command),
            0x4F => $"{Per2(I(command, 0))} {Per2(I(command, 1))} {Sentinel(_dataSources.Direction, I(command, 2), 4)} {(I(command, 3) <= 0 ? "转向" : "不转向")} {(I(command, 4) == 0 ? "无延迟" : "动作前延迟")} {(I(command, 5) == 0 ? "无延迟" : "动作后延迟")}",
            0x50 => $"{Per2(I(command, 0))} {Sentinel(_dataSources.WarGesture, I(command, 1), 13)} {(I(command, 2) == 0 ? "无延迟" : "动作前延迟")} {(I(command, 3) == 0 ? "无延迟" : "动作后延迟")}",
            0x52 => $"{Per2(I(command, 0))} {At(_dataSources.Job, I(command, 1))}",
            0x53 => $"{FormatSingleOrArea(command, 0)} {(I(command, 7) == 0 ? "撤退" : "死亡")}",
            0x56 => At(_dataSources.Weather, I(command, 0)),
            0x57 => At(_dataSources.Weather2, I(command, 0)),
            0x58 => $"{At(_dataSources.Object, I(command, 0))} {(I(command, 1) == 0 ? "显示" : "消失")} {At(_dataSources.Terrain, I(command, 2))} ({N(I(command, 3))},{N(I(command, 4))}) {(I(command, 5) == 0 ? "视点不切换" : "视点切换")} {(I(command, 6) == 0 ? "无音效" : "播放音效")}",
            0x59 => FormatTreasure(command),
            0x5B => $"({N(I(command, 0))},{N(I(command, 1))}) - ({N(I(command, 2))},{N(I(command, 3))}) {(I(command, 4) == 0 ? "胜利条件" : "战斗中")}",
            0x5D => $"{At(_dataSources.Operate, I(command, 0))} {N(I(command, 1))}",
            0x60 => $"{(I(command, 0) == 0 ? "敌方武将" : "我方武将")} {EscapeTreeText(t)} {At(_dataSources.SoloGesture, I(command, 1))}",
            0x62 => I(command, 0) == 1 ? "敌方武将" : "我方武将",
            0x63 => $"{(I(command, 0) == 0 ? "敌方武将" : "我方武将")} {EscapeTreeText(t)} {(I(command, 1) == 0 ? "不延时" : "延时")}",
            0x64 => $"{(I(command, 0) == 0 ? "敌方武将" : "我方武将")} {At(_dataSources.SoloGesture, I(command, 1))}",
            0x65 => $"{(I(command, 0) == 0 ? "敌方武将" : "我方武将")} {At(_dataSources.SoloAttack1, I(command, 1))} {(I(command, 2) == 0 ? "普通攻击" : "致命一击")}",
            0x66 => $"{(I(command, 0) == 0 ? "敌方武将" : "我方武将")} {At(_dataSources.SoloAttack2, I(command, 1))} {(I(command, 2) == 0 ? "命中" : "未命中")}",
            0x67 or 0x72 or 0x7B => $"{N(I(command, 0))} {EscapeTreeText(t)}",
            0x68 => $"{Per2(I(command, 0))} {Per2(I(command, 1))} Logo-{N(I(command, 2))}",
            0x6B => FormatMagicEffect(command),
            0x6D => $"{FormatDataOrBattleNoShort(command)} {Per2(I(command, 3))} ({N(I(command, 4))},{N(I(command, 5))}) {Sentinel(_dataSources.Direction, I(command, 6), 4)}",
            0x6F => ItemName(I(command, 0) < 255 ? I(command, 0) : 255),
            0x70 => $"{Per2(I(command, 0))} {Per2(I(command, 1))} {N(I(command, 2))}",
            0x73 => FormatPersonStatFlags(command),
            0x74 => I(command, 0) == 0 ? "不允许存档" : "允许存档",
            0x77 => $"{At(_dataSources.VariableKind, I(command, 0))} {N(I(command, 1))} {At(_dataSources.Operate2, I(command, 2))} {At(_dataSources.VariableKind2, I(command, 3))} {FormatLargeNumber(I(command, 4))}",
            0x78 => $"{N(I(command, 0))} {(I(command, 1) == 0 ? "<==" : "==>")} {Per2(I(command, 2))} {At(_dataSources.AllCondition, I(command, 3))}",
            0x79 => $"{At(_dataSources.VariableKind2, I(command, 0))} {FormatLargeNumber(I(command, 1))} {At(_dataSources.Compare2, I(command, 2))} {At(_dataSources.VariableKind2, I(command, 3))} {FormatLargeNumber(I(command, 4))}",
            _ => string.Empty
        };
    }

    private static string EscapeTreeText(string text)
        => string.IsNullOrEmpty(text)
            ? string.Empty
            : text.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string TrimSingleLine(string text, int maxLength)
    {
        var normalized = text.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..Math.Max(0, maxLength - 1)] + "…";
    }

    private static int I(LegacyScenarioCommandNode command, int index)
    {
        var ints = FlattenIntData(command);
        return index >= 0 && index < ints.Count ? ints[index] : 0;
    }

    private static IReadOnlyList<int> FlattenIntData(LegacyScenarioCommandNode command)
    {
        var values = new List<int>();
        foreach (var parameter in command.Parameters)
        {
            switch (parameter.Kind)
            {
                case LegacyScenarioParameterKind.Text:
                    break;
                case LegacyScenarioParameterKind.VariableArray:
                    while (values.Count % 25 != 0) values.Add(0);
                    values.AddRange(parameter.Values.Take(25));
                    while (values.Count % 25 != 0) values.Add(-1);
                    break;
                default:
                    values.Add(parameter.IntValue);
                    break;
            }
        }

        return values;
    }

    private static string Text(LegacyScenarioCommandNode command)
        => command.TextParameters.FirstOrDefault()?.Text ?? string.Empty;

    private static string FormatScenarioFile(int value)
    {
        var jb = value + 1;
        return $"{(jb % 2 == 0 ? "R_" : "S_")}{jb / 2:00}.eex";
    }

    private string At(IReadOnlyList<string> values, int index)
        => index >= 0 && index < values.Count ? values[index] : N(index);

    private string Sentinel(IReadOnlyList<string> values, int value, int fallbackIndex)
        => value >= 0 ? At(values, value) : At(values, fallbackIndex);

    private string Per1(int code)
        => At(_dataSources.Person1, LegacyMfcDialogDataSources.Per1CodeToList(code));

    private string Per2(int code)
        => ScriptVariableValueResolver.FormatPerson2Reference(code);

    private string ItemName(int index)
        => At(_dataSources.Item, Math.Clamp(index, 0, Math.Max(0, _dataSources.Item.Count - 1)));

    private string FaceCondition(int code)
    {
        var index = code switch
        {
            <= 4 and >= 0 => code,
            16 => 5,
            32 => 6,
            128 => 7,
            255 => 8,
            _ => -1
        };
        return index >= 0 ? At(_dataSources.FaceCondition, index) : N(code);
    }

    private static string SoundCode(int code)
    {
        if (code < 100) return $"SE{code:00}";
        if (code < 200) return $"SE_M_{code - 100:00}";
        return $"SE_E_{code - 200:00}";
    }

    private string FormatVariableArrayTest(LegacyScenarioCommandNode command)
    {
        var values = FlattenIntData(command);
        return $"{FormatVarGroup(values, 0)};{FormatVarGroup(values, 25)}";
    }

    private static string FormatVarGroup(IReadOnlyList<int> values, int start)
    {
        var parts = new List<string>();
        for (var i = start; i < start + 25 && i < values.Count; i++)
        {
            if (values[i] == -1) break;
            parts.Add("Var" + values[i].ToString(CultureInfo.InvariantCulture));
        }

        return parts.Count == 0 ? "无" : string.Join(" ", parts);
    }

    private string FormatPerson1List(LegacyScenarioCommandNode command, bool includeBoolean)
    {
        var offset = includeBoolean ? 0 : 1;
        var parts = new List<string>();
        if (includeBoolean) parts.Add(I(command, 0) == 0 ? "false" : "true");
        parts.Add(N(I(command, 1 - offset)));
        for (var i = 0; i < 10; i++) parts.Add(Per1(I(command, i + 2 - offset)));
        return string.Join(" ", parts);
    }

    private string FormatBackground(LegacyScenarioCommandNode command)
    {
        var category = I(command, 0);
        var name = category switch
        {
            0 => "外场景",
            1 => "中国地图",
            2 => "内场景",
            3 => "战场地图",
            _ => N(category)
        };
        var index = I(command, category + 1);
        if (category == 0) index += 1;
        else if (category == 2) index += 41;
        return $"{name} {index}";
    }

    private string FormatTwoPersons(LegacyScenarioCommandNode command, bool adjacent)
    {
        var result = $"{Per2(I(command, 0))} {Per2(I(command, 1))}";
        if (adjacent)
        {
            result += " 相邻";
            if (I(command, 2) == 0) result += "(可攻击)";
        }

        return result;
    }

    private string FormatSingleOrArea(LegacyScenarioCommandNode command, int baseIndex)
    {
        if (I(command, baseIndex) == 0)
        {
            return $"单人 {Per2(I(command, baseIndex + 1))}";
        }

        return $"区域 ({N(I(command, baseIndex + 2))},{N(I(command, baseIndex + 3))}) - ({N(I(command, baseIndex + 4))},{N(I(command, baseIndex + 5))}) {At(_dataSources.Camp, I(command, baseIndex + 6))}";
    }

    private string FormatMovementDataOrBattleNo(LegacyScenarioCommandNode command)
        => $"{FormatMovementDataOrBattleNoShort(command)} ({N(I(command, 3))},{N(I(command, 4))}) {Sentinel(_dataSources.Direction, I(command, 5), 4)}";

    private string FormatMovementDataOrBattleNoShort(LegacyScenarioCommandNode command)
        => I(command, 0) != 1
            ? Per2(I(command, 1))
            : $"战场编号 {N(I(command, 2))}";

    private string FormatDataOrBattleNoShort(LegacyScenarioCommandNode command)
        => I(command, 0) == 0
            ? $"data角色 {Per2(I(command, 1))}"
            : $"战场编号 {N(I(command, 2))}";

    private static string GlobalValueName(int code)
        => code == 0 ? "钱" : code == 1 ? "剧本编号" : "红蓝条";

    private string JoinCondition(int code)
        => At(_dataSources.JoinCondition, code == 255 ? 2 : code);

    private string FormatJoinLevel(LegacyScenarioCommandNode command)
    {
        var p = I(command, 2);
        var level = p <= _dataSources.LevelMax
            ? "+" + N(p)
            : p <= _dataSources.LevelMax * 2
                ? N(_dataSources.LevelMax - p)
                : "默认等";
        return $"{Per2(I(command, 0))} {JoinCondition(I(command, 1))} {level}级";
    }

    private string FormatItemGain(LegacyScenarioCommandNode command)
    {
        var item = ItemName(I(command, 0) >= 0 ? I(command, 0) : 255);
        var level = I(command, 1) <= 0 ? "默认等级" : "Lv" + N(I(command, 1));
        return $"{item} {level} {(I(command, 2) == 0 ? "不显示动作" : "显示动作")} {Per2(I(command, 3))}";
    }

    private string FormatEquipmentSet(LegacyScenarioCommandNode command)
    {
        var parts = new List<string> { Per2(I(command, 0)) };
        parts.Add(EquipmentName(I(command, 1), 0));
        parts.Add(EquipmentLevel(I(command, 2)));
        parts.Add(EquipmentName(I(command, 3), _dataSources.WeaponCount));
        parts.Add(EquipmentLevel(I(command, 4)));
        parts.Add(EquipmentName(I(command, 5), _dataSources.WeaponCount + _dataSources.ArmorCount));
        return string.Join(" ", parts);
    }

    private string EquipmentName(int value, int offset)
        => value <= 0 ? "默认装备" : value == 1 ? "卸去装备" : ItemName(offset + value - 2);

    private static string EquipmentLevel(int value)
        => value <= 0 ? "默认等级" : "Lv" + N(value);

    private string FormatCampCount(LegacyScenarioCommandNode command)
    {
        var result = $"{At(_dataSources.Camp, I(command, 0))} {N(I(command, 1))} {At(_dataSources.Compare, I(command, 2))} {(I(command, 3) == 0 ? "整个战场" : "指定区域")}";
        if (I(command, 3) == 1)
        {
            result += $" ({N(I(command, 4))},{N(I(command, 5))}) - ({N(I(command, 6))},{N(I(command, 7))})";
        }

        return result;
    }

    private string FormatBattleInit(LegacyScenarioCommandNode command)
    {
        var p = I(command, 3);
        var level = p <= _dataSources.LevelMax
            ? "+" + N(p)
            : p <= _dataSources.LevelMax * 2
                ? N(_dataSources.LevelMax - p)
                : "默认等";
        return $"未知{N(I(command, 0))} 未知{N(I(command, 1))} {N(I(command, 2))}回合 {level}级 {N(I(command, 4))} {Per2(I(command, 5))} {N(I(command, 6))} {Per2(I(command, 7))} {At(_dataSources.Weather, I(command, 8))} {At(_dataSources.Weather2, I(command, 9))}";
    }

    private string FormatBattleConditionChange(LegacyScenarioCommandNode command)
    {
        var target = I(command, 0) switch
        {
            0 => "data角色" + Per2(I(command, 1)),
            1 => "战场编号" + N(I(command, 2)),
            _ => $"区域 ({N(I(command, 3))},{N(I(command, 4))}) - ({N(I(command, 5))},{N(I(command, 6))}) {At(_dataSources.Camp, I(command, 7))}"
        };
        return $"{target} {Sentinel(_dataSources.WarPersonCondition, I(command, 8), 6)} {Sentinel(_dataSources.Change, I(command, 9), 3)} {DebuffText(I(command, 10))} {N(I(command, 11))} {N(I(command, 12))}";
    }

    private string DebuffText(int value)
    {
        if (value <= 0) return "无状态变更";
        var names = new List<string>();
        for (var bit = 1; bit <= 6; bit++)
        {
            if ((value & (1 << bit)) != 0) names.Add(At(_dataSources.Debuff, bit - 1));
        }

        return (value < 128 ? "赋予" : "取消") + string.Join("+", names);
    }

    private string FormatPolicy(LegacyScenarioCommandNode command)
    {
        var result = I(command, 0) != 1 ? $"单人 {Per2(I(command, 1))}" : $"区域 ({N(I(command, 2))},{N(I(command, 3))}) - ({N(I(command, 4))},{N(I(command, 5))}) {At(_dataSources.Camp, I(command, 6))}";
        var policy = I(command, 7);
        result += " " + At(_dataSources.Policy, policy);
        if (policy is 3 or 5) result += " " + Per2(I(command, 8));
        if (policy is 4 or 6) result += $" ({N(I(command, 9))},{N(I(command, 10))})";
        return result;
    }

    private string FormatTreasure(LegacyScenarioCommandNode command)
    {
        var parts = new List<string> { N(I(command, 0)) };
        for (var i = 0; i < 3; i++)
        {
            parts.Add(ItemName(I(command, 1 + i * 2) >= 0 ? I(command, 1 + i * 2) : 255));
            parts.Add(EquipmentLevel(I(command, 2 + i * 2)));
        }

        parts.Add(I(command, 7) == 0 ? "平时" : "结局");
        return string.Join(" ", parts);
    }

    private string FormatMagicEffect(LegacyScenarioCommandNode command)
    {
        var p = I(command, 2);
        var effect = p < 100 ? $"MEff-{p + 1}" : $"MCall-{p - 100}.e5";
        return $"({N(I(command, 0))},{N(I(command, 1))}) {effect} {(I(command, 3) == 0 ? "不切换视点" : "切换视点")}";
    }

    private string FormatPersonStatFlags(LegacyScenarioCommandNode command)
    {
        var names = new[] { "武力", "智力", "统率", "敏捷", "运气" };
        var builder = new StringBuilder($"{Per2(I(command, 0))} {N(I(command, 1))} {At(_dataSources.Compare, I(command, 2))}");
        for (var i = 0; i < names.Length; i++)
        {
            builder.Append(' ').Append(I(command, i + 3) == 0 ? "无" : names[i]);
        }

        return builder.ToString();
    }

    private static string FormatLargeNumber(int value)
        => value < 0x400000 ? N(value) : "0x" + value.ToString("X", CultureInfo.InvariantCulture);
}
