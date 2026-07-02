using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using CCZModStudio;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

internal partial class Program
{
    static void RunShopEditorSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var buildShopEditorData = typeof(MainForm).GetMethod("BuildShopEditorData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingMethodException("MainForm.BuildShopEditorData");
        var data = buildShopEditorData.Invoke(smokeForm, new object[] { project, tables }) as DataTable
            ?? throw new InvalidOperationException("商店编辑聚合数据构建失败。");
    
        var shopTable = tables.Single(t => t.TableName == "6.5-8-1 商店数据");
        var campaignNameTable = tables.Single(t => t.TableName == "6.5-8 战役名称");
        if (data.Rows.Count != shopTable.RowCount)
        {
            throw new InvalidOperationException($"商店编辑行数不正确：actual={data.Rows.Count}, expected={shopTable.RowCount}");
        }
    
        foreach (var columnName in new[] { "ID", "槽位类型", "关卡名称", "开关仓库人物", "开关仓库人物名", "买卖物品人物", "买卖物品人物名", "装备1", "道具17", "装备摘要", "道具摘要" })
        {
            if (!data.Columns.Contains(columnName))
            {
                throw new InvalidOperationException($"商店编辑缺少列：{columnName}");
            }
        }
    
        var normalRows = data.Rows.Cast<DataRow>()
            .Count(row => string.Equals(Convert.ToString(row["槽位类型"], CultureInfo.InvariantCulture), "普通关卡", StringComparison.Ordinal));
        if (normalRows != campaignNameTable.RowCount)
        {
            throw new InvalidOperationException($"普通关卡行数不正确：actual={normalRows}, expected={campaignNameTable.RowCount}");
        }
    
        var first = data.Rows[0];
        var firstName = Convert.ToString(first["关卡名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        var firstWarehousePreview = Convert.ToString(first["开关仓库人物名"], CultureInfo.InvariantCulture) ?? string.Empty;
        var firstBuySellPreview = Convert.ToString(first["买卖物品人物名"], CultureInfo.InvariantCulture) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(firstWarehousePreview) || string.IsNullOrWhiteSpace(firstBuySellPreview))
        {
            throw new InvalidOperationException("商店编辑人物预览列为空。");
        }
    
        var itemSlotValues = data.Rows.Cast<DataRow>()
            .SelectMany(row => data.Columns.Cast<DataColumn>()
                .Where(column => column.ColumnName.StartsWith("装备", StringComparison.Ordinal) || column.ColumnName.StartsWith("道具", StringComparison.Ordinal))
                .Select(column => Convert.ToString(row[column], CultureInfo.InvariantCulture) ?? string.Empty))
            .Where(value => value.Length > 0)
            .ToList();
        if (itemSlotValues.Count == 0)
        {
            throw new InvalidOperationException("商店编辑没有读取到任何物品槽值。");
        }
    
        var firstItemId = itemSlotValues
            .Select(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 255)
            .FirstOrDefault(id => id != 255);
        if (firstItemId == 0)
        {
            firstItemId = 1;
        }
    
        var buildShopItemLookupTable = typeof(MainForm).GetMethod("BuildShopItemLookupTable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingMethodException("MainForm.BuildShopItemLookupTable");
        var itemLookup = buildShopItemLookupTable.Invoke(smokeForm, new object[] { true }) as DataTable
            ?? throw new InvalidOperationException("Shop item lookup table was not built.");
        var emptyItemDisplay = itemLookup.Rows.Count == 0
            ? string.Empty
            : Convert.ToString(itemLookup.Rows[0]["\u663e\u793a"], CultureInfo.InvariantCulture) ?? string.Empty;
        var emptyItemId = itemLookup.Rows.Count == 0
            ? -1
            : Convert.ToInt32(itemLookup.Rows[0]["ID"], CultureInfo.InvariantCulture);
        if (emptyItemId != ShopEditorService.EmptyShopItemId ||
            !emptyItemDisplay.Contains("\u7a7a\u69fd", StringComparison.Ordinal) ||
            emptyItemDisplay.Contains("FF-End", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Shop item lookup did not keep 255 as the synthetic empty slot entry: " + emptyItemDisplay);
        }

        var firstValidLookupItem = itemLookup.Rows.Cast<DataRow>()
            .Select(row => new
            {
                Id = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture),
                Display = Convert.ToString(row["\u663e\u793a"], CultureInfo.InvariantCulture) ?? string.Empty
            })
            .FirstOrDefault(item =>
                item.Id != ShopEditorService.EmptyShopItemId &&
                !item.Display.Contains("\u5360\u4f4d", StringComparison.Ordinal));
        if (firstValidLookupItem == null)
        {
            throw new InvalidOperationException("Shop item lookup has no valid non-placeholder item for edit-path smoke tests.");
        }
        firstItemId = firstValidLookupItem.Id;

        var mappedItemDisplay = itemLookup.Rows.Cast<DataRow>()
            .Select(row => Convert.ToString(row["\u663e\u793a"], CultureInfo.InvariantCulture) ?? string.Empty)
            .FirstOrDefault(text => text.Contains("\uFF5C", StringComparison.Ordinal) && !text.StartsWith("255 ", StringComparison.Ordinal))
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(mappedItemDisplay) || int.TryParse(mappedItemDisplay, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            throw new InvalidOperationException("Shop item display is still numeric-only; expected Chinese name/category/type text.");
        }
    
        var buildShopItemDetailText = typeof(MainForm).GetMethod("BuildShopItemDetailText", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingMethodException("MainForm.BuildShopItemDetailText");
        var emptyItemDetail = Convert.ToString(buildShopItemDetailText.Invoke(smokeForm, new object[] { ShopEditorService.EmptyShopItemId }), CultureInfo.InvariantCulture) ?? string.Empty;
        if (!emptyItemDetail.Contains("\u7a7a\u69fd", StringComparison.Ordinal) ||
            emptyItemDetail.Contains("FF-End", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Shop item detail did not keep 255 as the synthetic empty slot.");
        }

        var itemDetail = Convert.ToString(buildShopItemDetailText.Invoke(smokeForm, new object[] { firstItemId }), CultureInfo.InvariantCulture) ?? string.Empty;
        foreach (var marker in new[] { "\u7269\u54c1\u9884\u89c8", "\u5927\u7c7b", "\u7c7b\u578b", "\u4ef7\u683c\u5b57\u6bb5", "\u7279\u6548", "\u7269\u54c1\u8bf4\u660e" })
        {
            if (!itemDetail.Contains(marker, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Shop item detail is missing creator-facing mapping text: " + marker);
            }
        }
    
        var currentShopEditorDataField = typeof(MainForm).GetField("_currentShopEditorData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_currentShopEditorData");
        var shopEditorItemInfosField = typeof(MainForm).GetField("_shopEditorItemInfos", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_shopEditorItemInfos");
        var shopEditorGridField = typeof(MainForm).GetField("_shopEditorGrid", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_shopEditorGrid");
        var shopBatchScopeComboField = typeof(MainForm).GetField("_shopBatchScopeCombo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_shopBatchScopeCombo");
        var shopBatchSlotComboField = typeof(MainForm).GetField("_shopBatchSlotCombo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_shopBatchSlotCombo");
        var shopBatchSetItemComboField = typeof(MainForm).GetField("_shopBatchSetItemCombo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_shopBatchSetItemCombo");
        var shopBatchFindItemComboField = typeof(MainForm).GetField("_shopBatchFindItemCombo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_shopBatchFindItemCombo");
        var shopBatchReplaceItemComboField = typeof(MainForm).GetField("_shopBatchReplaceItemCombo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_shopBatchReplaceItemCombo");
        foreach (var buttonFieldName in new[] { "_exportShopEditorCsvButton", "_importShopEditorCsvButton", "_copyShopEditorSelectionButton", "_pasteShopEditorSelectionButton", "_batchFillShopEditorColumnButton" })
        {
            if (typeof(MainForm).GetField(buttonFieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) == null)
            {
                throw new MissingFieldException("MainForm", buttonFieldName);
            }
        }

        var configureShopEditorGrid = typeof(MainForm).GetMethod("ConfigureShopEditorGrid", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingMethodException("MainForm.ConfigureShopEditorGrid");
        var getShopBatchTargetColumns = typeof(MainForm).GetMethod("GetShopBatchTargetColumns", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingMethodException("MainForm.GetShopBatchTargetColumns");
        var applyShopBatchSet = typeof(MainForm).GetMethod("ApplyShopBatchSet", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingMethodException("MainForm.ApplyShopBatchSet");
        var applyShopBatchClear = typeof(MainForm).GetMethod("ApplyShopBatchClear", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingMethodException("MainForm.ApplyShopBatchClear");
        var applyShopBatchReplace = typeof(MainForm).GetMethod("ApplyShopBatchReplace", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingMethodException("MainForm.ApplyShopBatchReplace");
        _ = typeof(MainForm).GetMethod("PasteShopEditorSelection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingMethodException("MainForm.PasteShopEditorSelection");
        var fillShopEditorSelectionWithCurrentValue = typeof(MainForm).GetMethod("FillShopEditorSelectionWithCurrentValue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingMethodException("MainForm.FillShopEditorSelectionWithCurrentValue");
        var trySetShopEditorCellValue = typeof(MainForm).GetMethod("TrySetShopEditorCellValue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingMethodException("MainForm.TrySetShopEditorCellValue");
        var validateAllShopEditorEditableCells = typeof(MainForm).GetMethod("ValidateAllShopEditorEditableCells", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingMethodException("MainForm.ValidateAllShopEditorEditableCells");
        var normalizeShopItemSlotEmptyValues = typeof(MainForm).GetMethod("NormalizeShopItemSlotEmptyValues", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingMethodException("MainForm.NormalizeShopItemSlotEmptyValues");

        var service = new ShopEditorService();
        if (Convert.ToInt32(service.GetDefaultShopRawValueForColumn("装备1", typeof(object)), CultureInfo.InvariantCulture) != ShopEditorService.EmptyShopItemId ||
            Convert.ToInt32(service.GetDefaultShopRawValueForColumn("道具32", typeof(object)), CultureInfo.InvariantCulture) != ShopEditorService.EmptyShopItemId ||
            Convert.ToInt32(service.GetDefaultShopRawValueForColumn("开关仓库人物", typeof(object)), CultureInfo.InvariantCulture) != ShopEditorService.UnsetShopPersonId ||
            Convert.ToString(service.GetDefaultShopRawValueForColumn("备注", typeof(string)), CultureInfo.InvariantCulture) != string.Empty)
        {
            throw new InvalidOperationException("Shop default raw value rules are not using 255 for item slots and 65535 for person fields.");
        }
    
        currentShopEditorDataField.SetValue(smokeForm, data);
        var shopEditorGrid = shopEditorGridField.GetValue(smokeForm) as DataGridView
            ?? throw new InvalidOperationException("Unable to read shop editor grid.");
        shopEditorGrid.DataSource = data;
        configureShopEditorGrid.Invoke(smokeForm, Array.Empty<object>());
        if (shopEditorGrid.Columns["\u88c5\u59071"] is not DataGridViewComboBoxColumn equipmentColumn ||
            shopEditorGrid.Columns["\u9053\u517717"] is not DataGridViewComboBoxColumn itemColumn ||
            equipmentColumn.DisplayMember != "\u663e\u793a" ||
            equipmentColumn.ValueMember != "ID" ||
            itemColumn.DisplayMember != "\u663e\u793a" ||
            itemColumn.ValueMember != "ID")
        {
            throw new InvalidOperationException("Shop item slots were not converted to Chinese mapped dropdown columns.");
        }
    
        var scopeCombo = shopBatchScopeComboField.GetValue(smokeForm) as ComboBox
            ?? throw new InvalidOperationException("Unable to read shop batch scope combo.");
        var slotCombo = shopBatchSlotComboField.GetValue(smokeForm) as ComboBox
            ?? throw new InvalidOperationException("Unable to read shop batch slot combo.");
        var setItemCombo = shopBatchSetItemComboField.GetValue(smokeForm) as ComboBox
            ?? throw new InvalidOperationException("Unable to read shop batch set combo.");
        var findItemCombo = shopBatchFindItemComboField.GetValue(smokeForm) as ComboBox
            ?? throw new InvalidOperationException("Unable to read shop batch find combo.");
        var replaceItemCombo = shopBatchReplaceItemComboField.GetValue(smokeForm) as ComboBox
            ?? throw new InvalidOperationException("Unable to read shop batch replace combo.");
    
        var batchSlotDisplays = ((DataTable?)slotCombo.DataSource)?.Rows.Cast<DataRow>()
            .Select(row => Convert.ToString(row["\u663e\u793a"], CultureInfo.InvariantCulture) ?? string.Empty)
            .ToList() ?? new List<string>();
        if (!batchSlotDisplays.Contains("\u88c5\u59071", StringComparer.Ordinal) ||
            !batchSlotDisplays.Contains("\u9053\u517717", StringComparer.Ordinal) ||
            batchSlotDisplays.Contains("2", StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Shop batch slot dropdown is missing Chinese equipment/item slot labels.");
        }
    
        scopeCombo.SelectedItem = "\u5f53\u524d\u7b5b\u9009\u884c";
        data.DefaultView.RowFilter = "ID = 0";
        slotCombo.SelectedValue = "\u88c5\u59071-16";
        var equipmentBatchColumns = ((IEnumerable<string>?)getShopBatchTargetColumns.Invoke(smokeForm, Array.Empty<object>()))?.ToList() ?? new List<string>();
        if (equipmentBatchColumns.Count != 16 || equipmentBatchColumns.Select(ExtractShopSlotNumber).Order().SequenceEqual(Enumerable.Range(1, 16)) == false)
        {
            throw new InvalidOperationException(
                "Shop batch equipment 1-16 range is incorrect. selected=" +
                Convert.ToString(slotCombo.SelectedValue, CultureInfo.InvariantCulture) +
                " columns=" + string.Join(",", equipmentBatchColumns));
        }
    
        setItemCombo.SelectedValue = firstItemId;
        applyShopBatchSet.Invoke(smokeForm, Array.Empty<object>());
        var batchRow = data.Rows[0];
        if (equipmentBatchColumns.Any(columnName => Convert.ToInt32(batchRow[columnName], CultureInfo.InvariantCulture) != firstItemId))
        {
            throw new InvalidOperationException("Shop batch set did not update equipment 1-16.");
        }
    
        findItemCombo.SelectedValue = firstItemId;
        replaceItemCombo.SelectedValue = 255;
        applyShopBatchReplace.Invoke(smokeForm, Array.Empty<object>());
        if (equipmentBatchColumns.Any(columnName => Convert.ToInt32(batchRow[columnName], CultureInfo.InvariantCulture) != 255))
        {
            throw new InvalidOperationException("Shop batch replace did not update equipment 1-16.");
        }
    
        slotCombo.SelectedValue = "\u9053\u517717-32";
        var itemBatchColumns = ((IEnumerable<string>?)getShopBatchTargetColumns.Invoke(smokeForm, Array.Empty<object>()))?.ToList() ?? new List<string>();
        if (itemBatchColumns.Count != 16 || itemBatchColumns.Select(ExtractShopSlotNumber).Order().SequenceEqual(Enumerable.Range(17, 16)) == false)
        {
            throw new InvalidOperationException("Shop batch item 17-32 range is incorrect.");
        }
    
        setItemCombo.SelectedValue = firstItemId;
        applyShopBatchSet.Invoke(smokeForm, Array.Empty<object>());
        applyShopBatchClear.Invoke(smokeForm, Array.Empty<object>());
        if (itemBatchColumns.Any(columnName => Convert.ToInt32(batchRow[columnName], CultureInfo.InvariantCulture) != 255))
        {
            throw new InvalidOperationException("Shop batch clear did not update item 17-32.");
        }
        data.DefaultView.RowFilter = string.Empty;

        var csvPath = Path.Combine(Path.GetTempPath(), "CCZModStudio_ShopEditorSmoke_" + Guid.NewGuid().ToString("N") + ".csv");
        CsvService.Export(data, csvPath);
        batchRow["装备1"] = 254;
        var imported = CsvService.ImportInto(data, csvPath, allowPartialColumns: true, matchByIdWhenPresent: true);
        File.Delete(csvPath);
        if (imported != data.Rows.Count || Convert.ToInt32(batchRow["装备1"], CultureInfo.InvariantCulture) == 254)
        {
            throw new InvalidOperationException("Shop CSV round-trip did not restore imported item slot values.");
        }

        var hexCsvPath = Path.Combine(Path.GetTempPath(), "CCZModStudio_ShopEditorSmoke_Hex_" + Guid.NewGuid().ToString("N") + ".csv");
        File.WriteAllText(hexCsvPath, "ID,装备1\r\n0,0xFF\r\n", Encoding.UTF8);
        CsvService.ImportInto(data, hexCsvPath, allowPartialColumns: true, matchByIdWhenPresent: true);
        File.Delete(hexCsvPath);
        if (Convert.ToInt32(batchRow["装备1"], CultureInfo.InvariantCulture) != ShopEditorService.EmptyShopItemId)
        {
            throw new InvalidOperationException("Shop CSV import did not normalize 0xFF item slot value to 255.");
        }

        batchRow["装备1"] = DBNull.Value;
        normalizeShopItemSlotEmptyValues.Invoke(smokeForm, new object?[] { data });
        if (Convert.ToInt32(batchRow["装备1"], CultureInfo.InvariantCulture) != ShopEditorService.EmptyShopItemId)
        {
            throw new InvalidOperationException("Shop item slot empty/DBNull normalization did not use 255.");
        }

        var pasteId = firstItemId == 255 ? 1 : firstItemId;
        shopEditorGrid.ClearSelection();
        shopEditorGrid.CurrentCell = shopEditorGrid.Rows[0].Cells["装备1"];
        var changedRows = new HashSet<DataRow>();
        var setArgs = new object?[] { 0, shopEditorGrid.Columns["装备1"].Index, pasteId.ToString(CultureInfo.InvariantCulture), changedRows, null };
        var setOk = (bool)(trySetShopEditorCellValue.Invoke(smokeForm, setArgs) ?? false);
        if (!setOk)
        {
            throw new InvalidOperationException("Shop validated cell setter rejected a valid item slot value: " + Convert.ToString(setArgs[4], CultureInfo.InvariantCulture));
        }

        if (Convert.ToInt32(batchRow["装备1"], CultureInfo.InvariantCulture) != pasteId)
        {
            throw new InvalidOperationException("Shop validated edit path did not update an item slot value.");
        }

        changedRows.Clear();
        var emptyArgs = new object?[] { 0, shopEditorGrid.Columns["装备1"].Index, string.Empty, changedRows, null };
        var emptySetOk = (bool)(trySetShopEditorCellValue.Invoke(smokeForm, emptyArgs) ?? false);
        if (!emptySetOk ||
            Convert.ToInt32(batchRow["装备1"], CultureInfo.InvariantCulture) != ShopEditorService.EmptyShopItemId)
        {
            throw new InvalidOperationException("Shop validated edit path did not normalize empty item slot text to 255: " + Convert.ToString(emptyArgs[4], CultureInfo.InvariantCulture));
        }

        changedRows.Clear();
        var hexEmptyArgs = new object?[] { 0, shopEditorGrid.Columns["装备1"].Index, "0xFF", changedRows, null };
        var hexEmptySetOk = (bool)(trySetShopEditorCellValue.Invoke(smokeForm, hexEmptyArgs) ?? false);
        _ = hexEmptySetOk;
        if (Convert.ToInt32(batchRow["装备1"], CultureInfo.InvariantCulture) != ShopEditorService.EmptyShopItemId ||
            !string.IsNullOrWhiteSpace(Convert.ToString(hexEmptyArgs[4], CultureInfo.InvariantCulture)))
        {
            throw new InvalidOperationException("Shop validated edit path should treat 0xFF as empty slot value 255 without error.");
        }

        var originalItemInfos = (IReadOnlyDictionary<int, ShopItemInfo>)shopEditorItemInfosField.GetValue(smokeForm)!;
        var placeholderItemId = originalItemInfos.Keys
            .Where(id => id is >= 0 and < ShopEditorService.EmptyShopItemId && id != pasteId)
            .DefaultIfEmpty(pasteId)
            .First();
        var placeholderInfos = new Dictionary<int, ShopItemInfo>(originalItemInfos)
        {
            [placeholderItemId] = new ShopItemInfo(placeholderItemId, "\u7a7a\u95f2", "测试", "测试", 0, "无", "无", string.Empty)
        };
        shopEditorItemInfosField.SetValue(smokeForm, placeholderInfos);
        try
        {
            changedRows.Clear();
            var placeholderArgs = new object?[] { 0, shopEditorGrid.Columns["装备1"].Index, placeholderItemId.ToString(CultureInfo.InvariantCulture), changedRows, null };
            var placeholderOk = (bool)(trySetShopEditorCellValue.Invoke(smokeForm, placeholderArgs) ?? false);
            var placeholderError = Convert.ToString(placeholderArgs[4], CultureInfo.InvariantCulture) ?? string.Empty;
            if (placeholderOk ||
                !placeholderError.Contains("\u5360\u4f4d", StringComparison.Ordinal) ||
                !placeholderError.Contains("255", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Shop validation did not block placeholder item names in item slots: " + placeholderError);
            }

            batchRow["装备1"] = placeholderItemId;
            validateAllShopEditorEditableCells.Invoke(smokeForm, Array.Empty<object>());
            if (string.IsNullOrWhiteSpace(shopEditorGrid.Rows[0].Cells["装备1"].ErrorText))
            {
                throw new InvalidOperationException("Shop full validation did not flag a modified placeholder item slot.");
            }
        }
        finally
        {
            shopEditorItemInfosField.SetValue(smokeForm, originalItemInfos);
            batchRow["装备1"] = pasteId;
            validateAllShopEditorEditableCells.Invoke(smokeForm, Array.Empty<object>());
        }

        shopEditorGrid.ClearSelection();
        shopEditorGrid.CurrentCell = shopEditorGrid.Rows[0].Cells["装备1"];
        shopEditorGrid.Rows[0].Cells["装备1"].Selected = true;
        var fillColumns = equipmentBatchColumns.Take(3).ToArray();
        foreach (var columnName in fillColumns)
        {
            shopEditorGrid.Rows[0].Cells[columnName].Selected = true;
        }
        fillShopEditorSelectionWithCurrentValue.Invoke(smokeForm, Array.Empty<object>());
        if (fillColumns.Any(columnName => Convert.ToInt32(batchRow[columnName], CultureInfo.InvariantCulture) != pasteId))
        {
            throw new InvalidOperationException("Shop batch fill did not update selected cells through the validated path.");
        }

        var invalidItemCell = shopEditorGrid.Rows[0].Cells["装备1"];
        invalidItemCell.Value = 256;
        validateAllShopEditorEditableCells.Invoke(smokeForm, Array.Empty<object>());
        if (string.IsNullOrWhiteSpace(invalidItemCell.ErrorText))
        {
            throw new InvalidOperationException("Shop validation did not flag an out-of-range item slot after bulk import/edit.");
        }
        invalidItemCell.Value = pasteId;
        validateAllShopEditorEditableCells.Invoke(smokeForm, Array.Empty<object>());
        if (!string.IsNullOrWhiteSpace(invalidItemCell.ErrorText))
        {
            throw new InvalidOperationException("Shop validation did not clear the repaired item slot error.");
        }

        RunShopServiceValidationSmoke(project, tables, data, originalItemInfos);
        RunShopRuntimeDiagnosticSmoke(project, tables);

        Console.WriteLine($"SHOP_EDITOR_SMOKE rows={data.Rows.Count} normal={normalRows} firstName={firstName} warehouse={firstWarehousePreview} buySell={firstBuySellPreview} itemSlots={itemSlotValues.Count} mapped={mappedItemDisplay} detailId={firstItemId} batchEquip={equipmentBatchColumns.Count} batchItem={itemBatchColumns.Count} csvRows={imported}");
        Console.WriteLine("SHOP_EDITOR_SMOKE OK");
    }

    private static void RunShopServiceValidationSmoke(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        DataTable shopEditorData,
        IReadOnlyDictionary<int, ShopItemInfo> originalItemInfos)
    {
        var service = new ShopEditorService();
        var placeholderId = originalItemInfos.Keys
            .Where(id => id is >= 0 and < ShopEditorService.EmptyShopItemId)
            .FirstOrDefault(id => ShopEditorService.IsPlaceholderShopItemName(originalItemInfos[id].Name));
        if (placeholderId == 0)
        {
            placeholderId = originalItemInfos.Keys
                .Where(id => id is > 0 and < ShopEditorService.EmptyShopItemId)
                .DefaultIfEmpty(1)
                .First();
        }

        var placeholderInfos = new Dictionary<int, ShopItemInfo>(originalItemInfos)
        {
            [placeholderId] = new ShopItemInfo(placeholderId, "\u7a7a\u95f2", "测试", "测试", 0, "无", "无", string.Empty)
        };
        var validationTable = new DataTable("ShopValidationSmoke");
        validationTable.Columns.Add("ID", typeof(int));
        validationTable.Columns.Add("装备1", typeof(int));
        validationTable.Rows.Add(0, placeholderId);
        var issues = service.ValidateShopItemSlots(validationTable, placeholderInfos);
        if (issues.Count == 0 ||
            !ShopEditorService.BuildShopSlotValidationErrorText(issues).Contains("占位物品不能入店；空槽用 255", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Shop shared validation did not block placeholder item slots.");
        }

        validationTable.Rows[0]["装备1"] = ShopEditorService.EmptyShopItemId;
        issues = service.ValidateShopItemSlots(validationTable, placeholderInfos);
        if (issues.Count != 0)
        {
            throw new InvalidOperationException("Shop shared validation did not allow 255 empty slot.");
        }

        var shopTable = HexTableNameResolver.ResolveForProject(project, tables, "6.5-8-1 商店数据");
        if (!service.IsShopDataTable(project, tables, shopTable))
        {
            throw new InvalidOperationException("Shop data table resolver did not recognize the shop table.");
        }
    }

    private static void RunShopRuntimeDiagnosticSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var targetRoot = Path.Combine(project.WorkspaceRoot, "基底", "氪金霸王传");
        if (Directory.Exists(targetRoot) &&
            File.Exists(Path.Combine(targetRoot, "Data.e5")) &&
            File.Exists(Path.Combine(targetRoot, "Ekd5.exe")))
        {
            project = new ProjectDetector().CreateProjectFromGameRoot(targetRoot);
            tables = new HexTableParser().Load(project.HexTableXmlPath);
        }

        var diagnostic = new ShopRuntimeDiagnosticService().Diagnose(project, tables, limit: 200);
        if (diagnostic.AutoShop.RawOffset >= 0 &&
            diagnostic.AutoShop.VirtualAddress == 0x00482427 &&
            Path.GetFileName(project.GameRoot).Equals("氪金霸王传", StringComparison.Ordinal))
        {
            if (diagnostic.AutoShop.RawOffset != 0x81827)
            {
                throw new InvalidOperationException("Auto-shop PE mapping changed: " + diagnostic.AutoShop.RawOffsetHex);
            }

            if (!diagnostic.AutoShop.BytesHex.Equals("00 03 06 09 0C FF FF 15 18 1B 46 49 FF", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Auto-shop bytes changed: " + diagnostic.AutoShop.BytesHex);
            }

            if (diagnostic.ExplicitShopIssueCount != 0)
            {
                throw new InvalidOperationException("Current project explicit shop should have no placeholder risks.");
            }

            foreach (var itemId in new[] { 66, 67, 68 })
            {
                if (!diagnostic.PlaceholderItems.Any(item => item.ItemId == itemId && item.Name.Contains("空闲", StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException("Current project placeholder item list is missing " + itemId);
                }
            }
        }

        if (diagnostic.ScenarioFiles.All(file => !file.FileName.Equals("R_00.eex", StringComparison.OrdinalIgnoreCase)) ||
            diagnostic.ScenarioFiles.All(file => !file.FileName.Equals("R_01.eex", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Shop runtime diagnostic did not compare R_00 and R_01.");
        }
    }
}
