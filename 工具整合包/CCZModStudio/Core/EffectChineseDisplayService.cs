using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class EffectChineseDisplayService
{
    public string Source(string value) => value switch
    {
        EffectInstanceSourceKind.Injected => "已注入",
        EffectInstanceSourceKind.Native => "原生",
        EffectInstanceSourceKind.Combined => "已注入并含原生实现",
        _ => "未知来源"
    };

    public string DetectionLevel(string value) => value switch
    {
        "KnownExact" => "已确认样本",
        "KnownVariant" => "已确认变体",
        "SemanticCandidate" => "语义候选",
        "VerifiedStatic" => "静态验证",
        "VerifiedDynamic" => "动态验证",
        "KnownSample" => "已知样本",
        "Hypothesis" => "待验证假设",
        _ => string.IsNullOrWhiteSpace(value) ? "尚未验证" : "内部诊断状态"
    };

    public string ParameterRole(string value) => value switch
    {
        InjectedEffectParameterRole.Personal => "个人特技号",
        InjectedEffectParameterRole.Equipment => "宝物特效号",
        InjectedEffectParameterRole.EffectValue => "效果值方式",
        InjectedEffectParameterRole.BooleanOption => "叠加方式",
        InjectedEffectParameterRole.Range => "作用范围",
        _ => "其他参数"
    };

    public string TriggerPhase(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "尚未确定";
        return value.Trim().ToLowerInvariant() switch
        {
            "attack" or "physical" => "物理攻击阶段",
            "strategy" => "策略结算阶段",
            "turn" or "turn-end" => "回合结算阶段",
            "damage" or "post-damage" => "伤害结算阶段",
            "action" => "行动控制阶段",
            _ => value.Contains("阶段", StringComparison.Ordinal) ? value : value + "阶段"
        };
    }

    public string EffectValueMode(int value) => value == 0 ? "读取特效值" : "只判断是否拥有";

    public string StackingMode(int value) => value switch
    {
        0 => "个人与宝物数值相加",
        1 => "个人与宝物不相加",
        2 => "宝物优先，个人作为备选",
        _ => "尚未确认的叠加方式"
    };
}
