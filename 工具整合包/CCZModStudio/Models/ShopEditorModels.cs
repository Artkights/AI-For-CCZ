using System.Data;

namespace CCZModStudio.Models;

public sealed record ShopItemInfo(
    int Id,
    string Name,
    string Category,
    string TypeDescription,
    int PriceUnit,
    string EffectName,
    string EffectHint,
    string Description)
{
    public string DisplayName => Id == 255
        ? "255 空槽"
        : $"{Id:D3} {Name}｜{Category}｜{TypeDescription}";
}

public sealed record ShopSlotValidationIssue(
    int RowId,
    int Slot,
    string ColumnName,
    int ItemId,
    string ItemName,
    string Message);

public sealed class ShopEditorBuildResult
{
    public required DataTable Data { get; init; }
    public required TableReadResult CampaignNameRead { get; init; }
    public required TableReadResult ShopDataRead { get; init; }
    public required IReadOnlyDictionary<int, string> PersonNames { get; init; }
    public required IReadOnlyDictionary<int, ShopItemInfo> ItemInfos { get; init; }
    public required IReadOnlyDictionary<int, string> ItemNames { get; init; }
}
