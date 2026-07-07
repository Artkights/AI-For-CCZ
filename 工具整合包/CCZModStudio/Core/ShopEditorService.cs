using System.Data;
using System.Globalization;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ShopEditorService
{
    public const int EmptyShopItemId = 255;
    public const int UnsetShopPersonId = 65535;

    private static readonly string[] PlaceholderShopItemNames =
    {
        "空闲",
        "空槽",
        "未使用",
        "保留",
        "FF-End",
        "End"
    };

    private readonly HexTableReader _tableReader = new();
    private readonly ItemEffectCatalogService _itemEffectCatalogService = new();
    private readonly ItemEffectNameReader _itemEffectNameReader = new();
    private readonly ItemEffectResolutionService _itemEffectResolutionService = new();
    private readonly CczEngineProfileService _engineProfile = new();

    public IReadOnlyDictionary<int, ShopItemInfo> BuildShopItemInfoLookup(CczProject project, IReadOnlyList<HexTableDefinition> tables)
        => BuildItemInfoLookup(project, tables);

    public bool IsShopDataTable(CczProject project, IReadOnlyList<HexTableDefinition> tables, HexTableDefinition table)
    {
        var hints = _engineProfile.Detect(project).TableHints;
        var shopTable = FindTable(project, tables, hints.ShopDataTable);
        return table.Id == shopTable.Id ||
               table.TableName.Equals(shopTable.TableName, StringComparison.OrdinalIgnoreCase);
    }

    public ShopEditorBuildResult Build(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var hints = _engineProfile.Detect(project).TableHints;
        var campaignNameRead = _tableReader.Read(project, FindTable(project, tables, hints.CampaignNameTable), tables);
        var shopDataRead = _tableReader.Read(project, FindTable(project, tables, hints.ShopDataTable), tables);
        if (!campaignNameRead.Validation.IsUsable || !shopDataRead.Validation.IsUsable)
        {
            throw new InvalidOperationException("战役名称表或商店数据表有不可读取项，请先查看数据表诊断。");
        }

        var personNames = BuildIdNameLookup(project, tables, hints.PersonTable);
        var itemInfos = BuildItemInfoLookup(project, tables);
        var itemNames = itemInfos.ToDictionary(pair => pair.Key, pair => pair.Value.Name);
        var data = BuildDataTable(campaignNameRead, shopDataRead, personNames, itemInfos);

        return new ShopEditorBuildResult
        {
            Data = data,
            CampaignNameRead = campaignNameRead,
            ShopDataRead = shopDataRead,
            PersonNames = personNames,
            ItemInfos = itemInfos,
            ItemNames = itemNames
        };
    }

    public string BuildPersonName(IReadOnlyDictionary<int, string> personNames, int id)
    {
        if (id is EmptyShopItemId or UnsetShopPersonId) return "无/不指定";
        return personNames.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : $"{id}：未找到人物名";
    }

    public string BuildItemName(
        IReadOnlyDictionary<int, ShopItemInfo> itemInfos,
        IReadOnlyDictionary<int, string> itemNames,
        int id)
    {
        if (id == EmptyShopItemId) return "空槽";

        if (itemInfos.TryGetValue(id, out var item) && !string.IsNullOrWhiteSpace(item.Name))
        {
            return item.Name;
        }

        return itemNames.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : $"{id}：未找到物品名";
    }

    public string BuildItemDetailText(IReadOnlyDictionary<int, ShopItemInfo> itemInfos, int id)
    {
        if (id == EmptyShopItemId)
        {
            return "物品预览：255 / 空槽，不会出现在商店。";
        }

        if (!itemInfos.TryGetValue(id, out var item))
        {
            return $"物品预览：{id} 未在 6.5 物品表中找到。";
        }

        return
            $"物品预览：{item.Id} / {item.Name}\r\n" +
            $"大类：{item.Category}\r\n" +
            $"类型：{item.TypeDescription}\r\n" +
            $"价格字段：{item.PriceUnit}（游戏内价格单位沿用原表定义）\r\n" +
            $"特效：{item.EffectName}\r\n" +
            $"特效说明：{item.EffectHint}\r\n" +
            $"物品说明：{TrimPreview(item.Description)}";
    }

    public void RefreshDerivedCells(
        DataRow row,
        IReadOnlyDictionary<int, string> personNames,
        IReadOnlyDictionary<int, ShopItemInfo> itemInfos,
        IReadOnlyDictionary<int, string> itemNames)
    {
        row["开关仓库人物名"] = BuildPersonName(personNames, Convert.ToInt32(row["开关仓库人物"], CultureInfo.InvariantCulture));
        row["买卖物品人物名"] = BuildPersonName(personNames, Convert.ToInt32(row["买卖物品人物"], CultureInfo.InvariantCulture));
        row["装备摘要"] = BuildSlotSummary(row, equipmentSlots: true, itemInfos, itemNames);
        row["道具摘要"] = BuildSlotSummary(row, equipmentSlots: false, itemInfos, itemNames);
    }

    public string BuildSummary(DataTable data)
    {
        var named = data.Rows.Cast<DataRow>().Count(row => !string.IsNullOrWhiteSpace(Convert.ToString(row["关卡名称"], CultureInfo.InvariantCulture)));
        var normal = data.Rows.Cast<DataRow>().Count(row => string.Equals(Convert.ToString(row["槽位类型"], CultureInfo.InvariantCulture), "普通关卡", StringComparison.Ordinal));
        var activeRows = data.Rows.Cast<DataRow>().Count(row => CountNonEmptyItemSlots(row) > 0);
        return
            $"商店编辑已读取：商店槽 {data.Rows.Count} 行，普通关卡 {normal} 行，已命名 {named} 行，有物品槽 {activeRows} 行。\r\n" +
            "来源表：当前引擎的战役名称表（Imsg.e5）与商店数据表（Data.e5）。\r\n" +
            "保存会分别写回关卡名称和商店属性，保存前自动备份，保存后重新读取校验。";
    }

    public bool TryGetSlotNumber(string columnName, out int slot)
    {
        var text = columnName;
        if (text.StartsWith("装备", StringComparison.Ordinal)) text = text[2..];
        if (text.StartsWith("道具", StringComparison.Ordinal)) text = text[2..];
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out slot) && slot is >= 1 and <= 32;
    }

    public static bool IsShopPersonColumnName(string columnName)
        => columnName is "开关仓库人物" or "买卖物品人物";

    public static bool IsPlaceholderShopItemName(string? name)
    {
        var normalized = (name ?? string.Empty).Trim();
        return normalized.Length == 0 ||
               PlaceholderShopItemNames.Any(placeholder => string.Equals(normalized, placeholder, StringComparison.OrdinalIgnoreCase));
    }

    public bool TryValidateShopItemSlotValue(
        IReadOnlyDictionary<int, ShopItemInfo> itemInfos,
        int itemId,
        out string error)
    {
        error = string.Empty;
        if (itemId == EmptyShopItemId) return true;
        if (itemId is < 0 or > EmptyShopItemId)
        {
            error = "占位物品不能入店；空槽用 255。";
            return false;
        }

        if (!itemInfos.TryGetValue(itemId, out var item))
        {
            error = "占位物品不能入店；空槽用 255。";
            return false;
        }

        if (IsPlaceholderShopItemName(item.Name))
        {
            error = "占位物品不能入店；空槽用 255。";
            return false;
        }

        return true;
    }

    public IReadOnlyList<ShopSlotValidationIssue> ValidateShopItemSlots(
        DataTable data,
        IReadOnlyDictionary<int, ShopItemInfo> itemInfos,
        Predicate<DataRow>? rowFilter = null,
        bool changedItemSlotsOnly = false,
        Func<int, string, bool>? slotFilter = null)
    {
        var issues = new List<ShopSlotValidationIssue>();
        foreach (DataRow row in data.Rows)
        {
            if (row.RowState is DataRowState.Detached or DataRowState.Deleted) continue;
            if (rowFilter != null && !rowFilter(row)) continue;

            var rowId = row.Table.Columns.Contains("ID") ? Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) : data.Rows.IndexOf(row);
            foreach (DataColumn column in data.Columns)
            {
                if (!TryGetSlotNumber(column.ColumnName, out var slot)) continue;
                if (slotFilter != null && !slotFilter(rowId, column.ColumnName)) continue;
                if (changedItemSlotsOnly && row.RowState != DataRowState.Added && !IsDataColumnChanged(row, column.ColumnName)) continue;
                if (!TryConvertShopItemId(row[column.ColumnName, DataRowVersion.Current], out var itemId))
                {
                    var rawText = Convert.ToString(row[column.ColumnName, DataRowVersion.Current], CultureInfo.InvariantCulture) ?? string.Empty;
                    issues.Add(new ShopSlotValidationIssue(
                        rowId,
                        slot,
                        column.ColumnName,
                        -1,
                        rawText,
                        $"第 {rowId} 行 {BuildSlotDisplayName(slot)} 的物品槽值无法解析为 0..255。空商品槽请使用 255。"));
                    continue;
                }

                if (TryValidateShopItemSlotValue(itemInfos, itemId, out var error)) continue;
                issues.Add(new ShopSlotValidationIssue(
                    rowId,
                    slot,
                    column.ColumnName,
                    itemId,
                    BuildShopItemValidationName(itemInfos, itemId),
                    $"第 {rowId} 行 {BuildSlotDisplayName(slot)}：{error}"));
            }
        }

        return issues;
    }

    public IReadOnlyList<ShopSlotValidationIssue> ValidateShopDataTable(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        HexTableDefinition table,
        DataTable data,
        bool changedItemSlotsOnly = false,
        Func<int, string, bool>? slotFilter = null)
    {
        if (!IsShopDataTable(project, tables, table)) return Array.Empty<ShopSlotValidationIssue>();
        var itemInfos = BuildShopItemInfoLookup(project, tables);
        return ValidateShopItemSlots(data, itemInfos, changedItemSlotsOnly: changedItemSlotsOnly, slotFilter: slotFilter);
    }

    public void EnsureShopDataTableValidForSave(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        HexTableDefinition table,
        DataTable data,
        bool changedItemSlotsOnly = true,
        Func<int, string, bool>? slotFilter = null)
    {
        var issues = ValidateShopDataTable(project, tables, table, data, changedItemSlotsOnly, slotFilter);
        if (issues.Count > 0)
        {
            throw new InvalidOperationException(BuildShopSlotValidationErrorText(issues));
        }
    }

    public static string BuildShopSlotValidationErrorText(IReadOnlyList<ShopSlotValidationIssue> issues, int maxIssues = 40)
    {
        var lines = new List<string> { "占位物品不能入店；空槽用 255。" };
        lines.AddRange(issues
            .Take(maxIssues)
            .Select(issue => $"row={issue.RowId}, slot={issue.Slot}, value={issue.ItemId}, name={issue.ItemName}"));
        if (issues.Count > maxIssues) lines.Add($"... 另有 {issues.Count - maxIssues} 项");
        return string.Join("\r\n", lines);
    }

    private static bool TryConvertShopItemId(object? value, out int itemId)
    {
        itemId = 0;
        if (value == null || value == DBNull.Value) return false;

        try
        {
            itemId = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            return itemId is >= 0 and <= EmptyShopItemId;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool IsDataColumnChanged(DataRow row, string columnName)
    {
        if (!row.Table.Columns.Contains(columnName)) return false;
        if (row.RowState == DataRowState.Added || !row.HasVersion(DataRowVersion.Original)) return true;
        var original = Convert.ToString(row[columnName, DataRowVersion.Original], CultureInfo.InvariantCulture) ?? string.Empty;
        var current = Convert.ToString(row[columnName, DataRowVersion.Current], CultureInfo.InvariantCulture) ?? string.Empty;
        return !string.Equals(original, current, StringComparison.Ordinal);
    }

    private static string BuildShopItemValidationName(IReadOnlyDictionary<int, ShopItemInfo> itemInfos, int itemId)
        => itemInfos.TryGetValue(itemId, out var item) ? BuildShopItemValidationName(item) : "未找到物品名";

    private static string BuildShopItemValidationName(ShopItemInfo item)
        => string.IsNullOrWhiteSpace(item.Name) ? "空白名" : item.Name;

    public string BuildSlotDisplayName(string columnName)
        => TryGetSlotNumber(columnName, out var slot) ? BuildSlotDisplayName(slot) : columnName;

    public string BuildSlotDisplayName(int slot)
        => slot <= 16 ? $"装备{slot}" : $"道具{slot}";

    public int GetSlotSortKey(string columnName)
        => TryGetSlotNumber(columnName, out var slot) ? slot : int.MaxValue;

    public int CountNonEmptyItemSlots(DataRow row)
    {
        var count = 0;
        foreach (DataColumn column in row.Table.Columns)
        {
            if (!TryGetSlotNumber(column.ColumnName, out _)) continue;
            if (!int.TryParse(Convert.ToString(row[column], CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
            if (id != EmptyShopItemId) count++;
        }
        return count;
    }

    private DataTable BuildDataTable(
        TableReadResult campaignNameRead,
        TableReadResult shopDataRead,
        IReadOnlyDictionary<int, string> personNames,
        IReadOnlyDictionary<int, ShopItemInfo> itemInfos)
    {
        var itemNames = itemInfos.ToDictionary(pair => pair.Key, pair => pair.Value.Name);
        var output = new DataTable("商店编辑");
        output.Columns.Add("ID", typeof(int));
        output.Columns.Add("槽位类型", typeof(string));
        output.Columns.Add("关卡名称", typeof(string));
        foreach (DataColumn column in shopDataRead.Data.Columns)
        {
            if (column.ColumnName == "ID") continue;
            output.Columns.Add(column.ColumnName, column.DataType);
            if (column.ColumnName == "开关仓库人物") output.Columns.Add("开关仓库人物名", typeof(string));
            if (column.ColumnName == "买卖物品人物") output.Columns.Add("买卖物品人物名", typeof(string));
        }
        output.Columns.Add("装备摘要", typeof(string));
        output.Columns.Add("道具摘要", typeof(string));

        var rowCount = Math.Max(campaignNameRead.Data.Rows.Count, shopDataRead.Data.Rows.Count);
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var row = output.NewRow();
            row["ID"] = rowIndex;
            row["槽位类型"] = rowIndex < campaignNameRead.Data.Rows.Count ? "普通关卡" : "扩展商店槽";
            row["关卡名称"] = rowIndex < campaignNameRead.Data.Rows.Count
                ? Convert.ToString(campaignNameRead.Data.Rows[rowIndex]["名称"], CultureInfo.InvariantCulture) ?? string.Empty
                : string.Empty;

            if (rowIndex < shopDataRead.Data.Rows.Count)
            {
                foreach (DataColumn column in shopDataRead.Data.Columns)
                {
                    if (column.ColumnName == "ID") continue;
                    row[column.ColumnName] = shopDataRead.Data.Rows[rowIndex][column.ColumnName];
                }
            }
            else
            {
                foreach (DataColumn column in shopDataRead.Data.Columns)
                {
                    if (column.ColumnName == "ID") continue;
                    row[column.ColumnName] = GetDefaultShopRawValueForColumn(column.ColumnName, column.DataType);
                }
            }

            RefreshDerivedCells(row, personNames, itemInfos, itemNames);
            output.Rows.Add(row);
        }

        output.AcceptChanges();
        foreach (DataColumn column in output.Columns)
        {
            column.ReadOnly = column.ColumnName is "ID" or "槽位类型";
        }
        return output;
    }

    public object GetDefaultShopRawValueForColumn(string columnName, Type dataType)
    {
        if (TryGetSlotNumber(columnName, out _)) return EmptyShopItemId;
        if (IsShopPersonColumnName(columnName)) return UnsetShopPersonId;
        return dataType == typeof(string) ? string.Empty : 0;
    }

    private IReadOnlyDictionary<int, ShopItemInfo> BuildItemInfoLookup(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var lookup = new Dictionary<int, ShopItemInfo>
        {
            [255] = new(255, "空槽", "空槽", "无", 0, "无", "无", string.Empty)
        };

        var boundary = ItemCategoryBoundaryService.Resolve(project);
        var hints = _engineProfile.Detect(project).TableHints;
        var lowDescription = HexTableNameResolver.BuildVersionedTableName(_engineProfile.Detect(project).TableVersionPrefix, "6.5-1-1 物品说明（0-103）");
        var highDescription = HexTableNameResolver.BuildVersionedTableName(_engineProfile.Detect(project).TableVersionPrefix, "6.5-2-1 物品说明（104-255）");
        var itemSegments = new[]
        {
            (_tableReader.Read(project, FindTable(project, tables, hints.ItemLowTable), tables), _tableReader.Read(project, FindTable(project, tables, lowDescription), tables)),
            (_tableReader.Read(project, FindTable(project, tables, hints.ItemHighTable), tables), _tableReader.Read(project, FindTable(project, tables, highDescription), tables))
        };

        foreach (var (itemRead, descriptionRead) in itemSegments)
        {
            if (!itemRead.Validation.IsUsable || !itemRead.Data.Columns.Contains("名称")) continue;
            foreach (DataRow row in itemRead.Data.Rows)
            {
                var id = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
                if (id == EmptyShopItemId) continue;
                var name = Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
                var typeId = row.Table.Columns.Contains("类型") ? Convert.ToInt32(row["类型"], CultureInfo.InvariantCulture) : 0;
                var price = row.Table.Columns.Contains("价格（/100）") ? Convert.ToInt32(row["价格（/100）"], CultureInfo.InvariantCulture) : 0;
                var effectId = row.Table.Columns.Contains("装备特效号") ? Convert.ToInt32(row["装备特效号"], CultureInfo.InvariantCulture) : 0;
                var growth = row.Table.Columns.Contains("升级能力成长") ? Convert.ToInt32(row["升级能力成长"], CultureInfo.InvariantCulture) : 0;
                var catalog = row.Table.Columns.Contains("宝物图鉴") ? Convert.ToInt32(row["宝物图鉴"], CultureInfo.InvariantCulture) : 0;
                var classification = ItemClassificationService.Classify(row, boundary);
                var category = classification.DisplayName;
                var effectValue = ResolveItemEffectValue(row, category);
                var typeText = ItemTypeCatalogService.BuildDescription(typeId, category, catalog);
                var effect = _itemEffectResolutionService.Resolve(project, tables, category, typeId, effectId);
                var effectName = effect.DisplayName;
                var effectHint = BuildEffectHint(effect, effectValue, growth);
                var description = TryFindRowById(descriptionRead.Data, id) is { } descriptionRow
                    ? Convert.ToString(descriptionRow["介绍"], CultureInfo.InvariantCulture) ?? string.Empty
                    : string.Empty;
                lookup[id] = new ShopItemInfo(id, name, category, typeText, price, effectName, effectHint, description);
            }
        }
        return lookup;
    }

    public string BuildSlotSummary(
        DataRow row,
        bool equipmentSlots,
        IReadOnlyDictionary<int, ShopItemInfo> itemInfos,
        IReadOnlyDictionary<int, string> itemNames)
    {
        var parts = new List<string>();
        foreach (DataColumn column in row.Table.Columns)
        {
            if (!TryGetSlotNumber(column.ColumnName, out var slot)) continue;
            if ((slot <= 16) != equipmentSlots) continue;
            var itemId = Convert.ToInt32(row[column.ColumnName], CultureInfo.InvariantCulture);
            if (itemId == EmptyShopItemId) continue;
            parts.Add($"{BuildSlotDisplayName(slot)}:{itemId}-{BuildItemName(itemInfos, itemNames, itemId)}");
            if (parts.Count >= 8) break;
        }

        return parts.Count == 0 ? "无" : string.Join("，", parts);
    }

    private IReadOnlyList<ItemEffectCatalogEntry> BuildDefaultItemEffectCatalogEntries(CczProject project, IReadOnlyList<HexTableDefinition> tables)
        => _itemEffectNameReader.ReadBaseCatalogEntries(project, tables);

    private IReadOnlyDictionary<int, string> BuildBaseItemEffectNameLookup(CczProject project, IReadOnlyList<HexTableDefinition> tables)
        => _itemEffectNameReader.ReadBaseNames(project, tables);

    private IReadOnlyDictionary<int, string> BuildItemEffectNameLookup(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var result = new Dictionary<int, string>(BuildBaseItemEffectNameLookup(project, tables));
        var catalogEntries = _itemEffectCatalogService.Load(project, BuildDefaultItemEffectCatalogEntries(project, tables));
        foreach (var pair in _itemEffectCatalogService.BuildDisplayLookup(catalogEntries))
        {
            result[pair.Key] = pair.Value;
        }

        return result;
    }

    private static string GetItemEffectName(IReadOnlyDictionary<int, string> itemEffectNames, int effectId)
    {
        if (effectId == 0) return "无特效/未启用";
        if (effectId == 255) return "普通装备/无扩展特效";
        return itemEffectNames.TryGetValue(effectId, out var name)
            ? name
            : $"未命名或未确认：{HexDisplayFormatter.Format(effectId, 2)}";
    }

    private static string BuildItemEffectNameDisplay(
        string majorCategory,
        int typeId,
        int effectId,
        IReadOnlyDictionary<int, string> itemEffectNames)
    {
        var effectiveEffectId = ItemEffectInterpretationService.ResolveEffectiveEffectId(majorCategory, typeId, effectId);
        return effectId is 0 or 255 || effectiveEffectId is 0 or 255
            ? "无"
            : GetItemEffectName(itemEffectNames, effectiveEffectId);
    }

    private static string BuildEffectHint(ItemEffectResolutionResult effect, int effectValue, int growth)
    {
        var warnings = effect.Warnings.Count == 0 ? string.Empty : "; warnings=" + string.Join(" | ", effect.Warnings);
        return $"{effect.DisplayName}; source={effect.Source}; confidence={effect.Confidence}; value={effectValue}; growth={growth}{warnings}";
    }

    private static int ResolveItemEffectValue(DataRow row, string category)
    {
        if (ConsumableItemEffectCatalogService.IsConsumableCategory(category) && row.Table.Columns.Contains("初始能力"))
        {
            return Convert.ToInt32(row["初始能力"], CultureInfo.InvariantCulture);
        }

        return row.Table.Columns.Contains("装备特效号-效果值")
            ? Convert.ToInt32(row["装备特效号-效果值"], CultureInfo.InvariantCulture)
            : 0;
    }
    private static IReadOnlyDictionary<int, string> BuildIdNameLookup(CczProject project, IReadOnlyList<HexTableDefinition> tables, string tableName)
    {
        var table = FindTable(project, tables, tableName);
        var read = new HexTableReader().Read(project, table, tables);
        var lookup = new Dictionary<int, string>();
        if (!read.Validation.IsUsable || !read.Data.Columns.Contains("名称")) return lookup;
        foreach (DataRow row in read.Data.Rows)
        {
            var id = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
            lookup[id] = Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        }
        return lookup;
    }

    private static DataRow? TryFindRowById(DataTable table, int id)
    {
        foreach (DataRow row in table.Rows)
        {
            if (Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) == id) return row;
        }
        return null;
    }

    private static HexTableDefinition FindTable(CczProject project, IReadOnlyList<HexTableDefinition> tables, string tableName)
        => HexTableNameResolver.ResolveForProject(project, tables, tableName);

    private static string TrimPreview(string text)
    {
        text = text.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return text.Length <= 120 ? text : text[..120] + "…";
    }
}
