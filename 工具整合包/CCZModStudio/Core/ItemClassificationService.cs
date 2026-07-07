using System.Data;
using System.Globalization;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public enum ItemKind
{
    Weapon,
    Armor,
    AccessoryEquipment,
    Consumable,
    Reserved,
    Unknown
}

public sealed record ItemClassification(
    int ItemId,
    ItemKind Kind,
    string DisplayName,
    bool IsEquipmentCandidate,
    bool IsConsumable,
    int TypeId,
    int EffectId,
    int Catalog);

public sealed class ItemClassificationService
{
    private readonly HexTableReader _reader = new();
    private readonly CczEngineProfileService _engineProfile = new();

    public IReadOnlyDictionary<int, ItemClassification> BuildLookup(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(tables);

        var boundary = ItemCategoryBoundaryService.Resolve(project);
        var lookup = new Dictionary<int, ItemClassification>();
        var hints = _engineProfile.Detect(project).TableHints;
        foreach (var tableName in new[] { hints.ItemLowTable, hints.ItemHighTable })
        {
            var table = HexTableNameResolver.ResolveForProject(project, tables, tableName);
            var read = _reader.Read(project, table, tables);
            if (!read.Validation.IsUsable) continue;
            foreach (DataRow row in read.Data.Rows)
            {
                var classification = Classify(row, boundary);
                lookup[classification.ItemId] = classification;
            }
        }

        return lookup;
    }

    public static ItemClassification Classify(DataRow row, ItemCategoryBoundary boundary)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(boundary);

        var id = ReadInt(row, "ID");
        var name = ReadString(row, "名称");
        var typeId = ReadInt(row, "类型");
        var effectId = ReadInt(row, "装备特效号");
        var kindEffectId = row.Table.Columns.Contains("原始装备特效号")
            ? ReadInt(row, "原始装备特效号")
            : row.Table.Columns.Contains(Ccz66ItemLayoutService.RawEffectMarkerColumnName)
            ? ReadInt(row, Ccz66ItemLayoutService.RawEffectMarkerColumnName)
            : effectId;
        var catalog = ReadInt(row, "宝物图鉴");
        var isEmptyName = string.IsNullOrWhiteSpace(name);
        var kind = ClassifyKind(id, isEmptyName, kindEffectId, boundary);
        return new ItemClassification(
            id,
            kind,
            BuildKindDisplayName(kind),
            IsEquipmentCandidate(kind),
            kind == ItemKind.Consumable,
            typeId,
            effectId,
            catalog);
    }

    public static ItemKind ClassifyKind(int itemId, bool isEmptyName, int effectId, ItemCategoryBoundary boundary)
    {
        if (itemId < boundary.DefenseStartId)
        {
            return isEmptyName && IsReservedEffect(effectId) ? ItemKind.Reserved : ItemKind.Weapon;
        }

        if (itemId < boundary.AccessoryStartId)
        {
            return isEmptyName && IsReservedEffect(effectId) ? ItemKind.Reserved : ItemKind.Armor;
        }

        if (isEmptyName && IsReservedEffect(effectId)) return ItemKind.Reserved;
        return effectId switch
        {
            2 => ItemKind.AccessoryEquipment,
            3 => ItemKind.Consumable,
            0 or 102 => ItemKind.Reserved,
            _ => ItemKind.Unknown
        };
    }

    public static string BuildKindDisplayName(ItemKind kind)
        => kind switch
        {
            ItemKind.Weapon => "武器",
            ItemKind.Armor => "防具",
            ItemKind.AccessoryEquipment => "辅助装备",
            ItemKind.Consumable => "道具/消耗品",
            ItemKind.Reserved => "预留/空位",
            _ => "未知"
        };

    public static bool IsEquipmentCandidate(ItemKind kind)
        => kind is ItemKind.Weapon or ItemKind.Armor or ItemKind.AccessoryEquipment;

    public static bool IsConsumableOrReserved(ItemKind kind)
        => kind is ItemKind.Consumable or ItemKind.Reserved;

    private static bool IsReservedEffect(int effectId)
        => effectId is 0 or 102 or 255;

    private static int ReadInt(DataRow row, string columnName)
    {
        if (!row.Table.Columns.Contains(columnName)) return 0;
        var value = row[columnName];
        if (value == null || value == DBNull.Value) return 0;
        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(text)
            ? 0
            : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static string ReadString(DataRow row, string columnName)
        => row.Table.Columns.Contains(columnName)
            ? Convert.ToString(row[columnName], CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;
}
