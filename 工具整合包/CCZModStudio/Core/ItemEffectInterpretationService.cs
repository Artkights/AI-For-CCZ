using System.Globalization;

namespace CCZModStudio.Core;

public static class ItemEffectInterpretationService
{
    public static int ResolveEffectiveEffectId(string majorCategory, int typeId, int effectId)
    {
        if (majorCategory == "辅助/道具")
        {
            return effectId is 2 or 3 ? typeId : effectId;
        }

        return effectId;
    }

    public static string BuildEffectiveEffectIdText(string majorCategory, int typeId, int effectId)
    {
        if (majorCategory == "辅助/道具" && effectId is 2 or 3)
        {
            return $"类型={typeId}（原始装备特效号={effectId} 为类别标记候选）";
        }

        return $"装备特效号={effectId}";
    }

    public static string BuildEffectiveEffectDescription(
        string majorCategory,
        int typeId,
        int effectId,
        int effectiveEffectId,
        Func<int, string> effectNameResolver)
    {
        if (majorCategory == "辅助/道具")
        {
            if (effectId == 2)
            {
                var typeDescription = ItemTypeCatalogService.BuildDescription(typeId, majorCategory, catalog: 0);
                return $"辅助装备候选：原始装备特效号 2 更像类别标记；当前按类型字段 {typeId} 作为实际效果候选。{typeDescription}";
            }

            if (effectId == 3)
            {
                var typeDescription = ItemTypeCatalogService.BuildDescription(typeId, majorCategory, catalog: 0);
                return $"普通道具/消耗品候选：原始装备特效号 3 更像类别标记；当前按类型字段 {typeId} 作为实际效果候选。{typeDescription}";
            }
        }

        return effectNameResolver(effectiveEffectId);
    }

    public static string BuildEffectHint(string majorCategory, int typeId, int effectId, string effectName, int effectValue, int growth)
    {
        if (effectId == 255) return "普通商店装备常见值；一般不启用扩展装备特效。";
        if (effectId == 0) return "无特效/空位候选；若是装备请结合旧工具确认。";
        if (majorCategory == "辅助/道具" && effectId == 2)
        {
            return $"辅助装备段常见值 2；当前按“类别标记候选”处理，创作时优先核对类型字段={typeId}。效果值={effectValue}，成长={growth}。";
        }

        if (majorCategory == "辅助/道具" && effectId == 3)
        {
            return $"道具段常见值 3；当前按“类别标记候选”处理，创作时优先核对类型字段={typeId}。效果值={effectValue}，成长={growth}。";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{effectName}；效果值={effectValue}，成长={growth}。效果值含义随特效变化，写后需实机验证。");
    }
}
