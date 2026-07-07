namespace CCZModStudio.Core;

public static class ConsumableItemEffectCatalogService
{
    private static readonly IReadOnlyDictionary<int, ConsumableItemEffectDefinition> Definitions = new Dictionary<int, ConsumableItemEffectDefinition>
    {
        [0x1A] = new("HP", "HP 恢复类道具；效果值通常来自物品行“初始能力”字节。"),
        [0x1B] = new("MP", "MP 恢复类道具；效果值通常来自物品行“初始能力”字节。"),
        [0x1C] = new("治疗混乱", "状态恢复类道具。"),
        [0x1D] = new("治疗中毒", "状态恢复类道具。"),
        [0x1E] = new("治疗麻痹", "状态恢复类道具。"),
        [0x1F] = new("治疗禁咒", "状态恢复类道具。"),
        [0x20] = new("治疗异常", "万能药等综合状态恢复类道具。"),
        [0x21] = new("武力", "能力果类道具。"),
        [0x22] = new("统帅", "能力果类道具。"),
        [0x23] = new("智力", "能力果类道具。"),
        [0x24] = new("敏捷", "能力果类道具。"),
        [0x25] = new("好运", "能力果类道具。"),
        [0x26] = new("Exp", "经验果类道具。"),
        [0x27] = new("印绶", "印绶类转职道具。"),
        [0x29] = new("功勋", "功勋类道具。"),
        [0x2A] = new("仙桃", "HP 上限或特殊恢复类道具，具体效果需结合引擎验证。"),
        [0x2B] = new("仙酒", "MP 上限或特殊恢复类道具，具体效果需结合引擎验证。"),
        [0x3D] = new("装备升级书", "装备升级类道具。")
    };

    public static bool IsConsumableCategory(string majorCategory)
        => string.Equals(majorCategory, "道具/消耗品", StringComparison.Ordinal);

    public static bool TryResolve(int effectId, out ConsumableItemEffectDefinition definition)
        => Definitions.TryGetValue(effectId, out definition!);

    public static string BuildDisplayName(int effectId)
        => TryResolve(effectId, out var definition)
            ? definition.Name
            : $"未确认道具特效：0x{effectId:X2}";

    public static string BuildDescription(int effectId)
        => TryResolve(effectId, out var definition)
            ? $"{definition.Description} 编号={effectId} (0x{effectId:X2})。"
            : $"未在 B形象指定器道具特效目录中确认编号 {effectId} (0x{effectId:X2})；不要回退为装备特效名。";
}

public sealed record ConsumableItemEffectDefinition(string Name, string Description);
