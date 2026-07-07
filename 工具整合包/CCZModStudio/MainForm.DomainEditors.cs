using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private enum RoleEquipmentSlot
    {
        Weapon,
        Armor,
        Assist
    }

    private enum JobStrategyPreviewLayoutMode
    {
        ImageOnly,
        LearningAndDescription,
        LearningEditorOnly
    }

    private sealed class RoleCriticalQuoteComboItem
    {
        public int ID { get; init; }
        public string 显示 { get; init; } = string.Empty;
    }

    private sealed class RoleCriticalAssignmentPreview
    {
        public bool HasChanges { get; init; }
        public string Summary { get; init; } = string.Empty;
    }

    private sealed class RoleCriticalAssignmentSaveResult
    {
        public List<TableSaveResult> TableSaves { get; } = [];
        public RoleCriticalSpecialSlotsSaveResult? SpecialSlotsSave { get; set; }
    }

    private sealed class RoleTextSaveResult
    {
        private readonly List<TableSaveResult> _tableSaves = [];
        private readonly List<RoleCriticalSpecialSlotsSaveResult> _specialSlotSaves = [];

        public int SaveCount => _tableSaves.Count + _specialSlotSaves.Count;
        public int ChangedBytes => _tableSaves.Sum(x => x.ChangedBytes) + _specialSlotSaves.Sum(x => x.ChangedBytes);
        public IReadOnlyList<string> BackupPaths => _tableSaves.Select(x => x.BackupPath).Concat(_specialSlotSaves.Select(x => x.BackupPath)).ToArray();

        public void Add(TableSaveResult save) => _tableSaves.Add(save);
        public void Add(RoleCriticalSpecialSlotsSaveResult save) => _specialSlotSaves.Add(save);
        public void AddRange(IEnumerable<TableSaveResult> saves) => _tableSaves.AddRange(saves);
    }

    private bool _updatingRoleEquipmentDetailControls;

    private void OpenRoleEditor()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SelectTabPageByText("角色设定");
        LoadRoleEditor();
    }

    private void OpenGlobalSettingsDialog()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_tables.Count == 0)
        {
            ReloadCurrentProject();
            if (_tables.Count == 0) return;
        }

        try
        {
            using var dialog = new GlobalSettingsDialog(_project, _tables);
            if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.LastSaveSummary))
            {
                SetStatus("全局设定：" + dialog.LastSaveSummary);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("打开全局设定失败：" + ex);
            MessageBox.Show(this, ex.Message, "打开全局设定失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadRoleEditor()
    {
        if (_project == null) return;
        if (_tables.Count == 0)
        {
            ReloadCurrentProject();
            if (_tables.Count == 0) return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            _currentRoleEditorData = BuildRoleEditorData(_project, _tables);
            LoadRoleTextTables();
            _roleEditorGrid.DataSource = _currentRoleEditorData;
            ConfigureRoleEditorGrid();
            ConfigureRoleEquipmentDetailControls();
            ShowSelectedRoleEditorCell();
            _saveRoleEditorButton.Enabled = true;
            _importRoleFaceButton.Enabled = true;
            _batchImportRoleFaceButton.Enabled = true;
            _exportRoleFaceBmpButton.Enabled = true;
            _exportRoleEditorCsvButton.Enabled = true;
            _importRoleEditorCsvButton.Enabled = true;
            _roleEditorInfoBox.Text = BuildRoleEditorSummary(_currentRoleEditorData);
            SetStatus($"角色设定读取完成：{_currentRoleEditorData.Rows.Count} 行");
        }
        catch (Exception ex)
        {
            _roleEditorInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("读取角色设定失败：" + ex);
            MessageBox.Show(this, ex.Message, "读取角色设定失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private DataTable BuildRoleEditorData(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var personTable = FindTable(tables, "6.5-0 人物");
        var rTable = FindTable(tables, "6.5-0-4 R形象");
        var sTable = FindTable(tables, "6.5-0-5 S形象");
        _roleEditorJobLookup = BuildRoleJobLookup(project, tables, out var jobNames);
        _roleEditorJobNames = jobNames;
        var personRead = _tableReader.Read(project, personTable, tables);
        var rRead = _tableReader.Read(project, rTable, tables);
        var sRead = _tableReader.Read(project, sTable, tables);
        if (!personRead.Validation.IsUsable || !rRead.Validation.IsUsable || !sRead.Validation.IsUsable)
        {
            throw new InvalidOperationException("人物/R/S 形象表有不可读取项，请先查看数据表诊断。");
        }

        var output = new DataTable("角色设定");
        foreach (DataColumn sourceColumn in personRead.Data.Columns)
        {
            output.Columns.Add(sourceColumn.ColumnName, sourceColumn.DataType);
        }
        output.Columns.Add("职业名称", typeof(string));
        output.Columns.Add("头像说明", typeof(string));
        output.Columns.Add("武器名", typeof(string));
        output.Columns.Add("防具名", typeof(string));
        output.Columns.Add("辅助名", typeof(string));
        output.Columns.Add("R形象编号", typeof(int));
        output.Columns.Add("S形象编号", typeof(int));
        output.Columns.Add("R资源状态", typeof(string));
        output.Columns.Add("S资源状态", typeof(string));

        var itemNames = BuildItemNameLookup(project, tables);
        var itemClassifications = new ItemClassificationService().BuildLookup(project, tables);
        var count = Math.Min(personRead.Data.Rows.Count, Math.Min(rRead.Data.Rows.Count, sRead.Data.Rows.Count));
        for (var i = 0; i < count; i++)
        {
            var row = output.NewRow();
            foreach (DataColumn sourceColumn in personRead.Data.Columns)
            {
                row[sourceColumn.ColumnName] = personRead.Data.Rows[i][sourceColumn.ColumnName];
            }
            var jobId = Convert.ToInt32(personRead.Data.Rows[i]["职业"], CultureInfo.InvariantCulture);
            var faceId = Convert.ToInt32(personRead.Data.Rows[i]["头像"], CultureInfo.InvariantCulture);
            var rId = Convert.ToInt32(rRead.Data.Rows[i]["R形象编号"], CultureInfo.InvariantCulture);
            var sId = Convert.ToInt32(sRead.Data.Rows[i]["S形象编号"], CultureInfo.InvariantCulture);
            row["职业名称"] = BuildRoleJobName(jobId);
            row["头像说明"] = BuildRoleFaceHint(faceId);
            RefreshRoleEquipmentNameCells(row, itemNames, itemClassifications);
            row["R形象编号"] = rId;
            row["S形象编号"] = sId;
            row["R资源状态"] = ImageAssignmentService.GetImageResourceStatus(project, "R", rId);
            row["S资源状态"] = ImageAssignmentService.GetImageResourceStatus(project, "S", sId);
            output.Rows.Add(row);
        }

        output.AcceptChanges();
        foreach (DataColumn column in output.Columns)
        {
            column.ReadOnly = column.ColumnName is "ID" or "武器名" or "防具名" or "辅助名";
        }
        return output;
    }

    private void LoadRoleTextTables()
    {
        if (_project == null) return;
        _roleBiographyRead = _tableReader.Read(_project, FindTable(_tables, "6.5-0-1 人物列传"), _tables);
        _roleCriticalQuoteRead = _tableReader.Read(_project, FindTable(_tables, "6.5-0-2 暴击台词"), _tables);
        _roleRetreatQuoteRead = _tableReader.Read(_project, FindTable(_tables, "6.5-0-3 撤退台词"), _tables);
        _saveRoleTextDetailButton.Enabled =
            _roleBiographyRead.Validation.IsUsable &&
            _roleCriticalQuoteRead.Validation.IsUsable &&
            _roleRetreatQuoteRead.Validation.IsUsable;
    }

    private DataTable BuildRoleJobLookup(CczProject project, IReadOnlyList<HexTableDefinition> tables, out IReadOnlyDictionary<int, string> jobNames)
    {
        var jobTable = FindTable(tables, "6.5-4 详细兵种");
        var jobRead = _tableReader.Read(project, jobTable, tables);
        if (!jobRead.Validation.IsUsable)
        {
            throw new InvalidOperationException("详细兵种表不可读取，无法生成角色职业下拉。");
        }

        var lookup = new DataTable("RoleJobLookup");
        lookup.Columns.Add("ID", typeof(int));
        lookup.Columns.Add("显示", typeof(string));
        var names = new Dictionary<int, string>();
        foreach (DataRow row in jobRead.Data.Rows)
        {
            var id = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
            var name = Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
            var display = string.IsNullOrWhiteSpace(name)
                ? $"{id}：未命名兵种"
                : $"{id}：{name}";
            lookup.Rows.Add(id, display);
            names[id] = display;
        }

        jobNames = names;
        return lookup;
    }

    private void ConfigureRoleEditorGrid()
    {
        if (_currentRoleEditorData == null) return;

        _roleEditorGrid.ReadOnly = false;
        ReplaceRoleJobColumnWithCombo();
        ReplaceRoleEquipmentColumnsWithCombos();
        var hiddenColumns = new HashSet<string>(StringComparer.Ordinal)
        {
            "Army",
            "级别",
            "经验",
            "职业名称",
            "头像说明",
            "武器",
            "防具",
            "辅助",
            "武器名",
            "防具名",
            "辅助名",
            "R形象编号",
            "S形象编号",
            "R资源状态",
            "S资源状态"
        };
        foreach (DataGridViewColumn column in _roleEditorGrid.Columns)
        {
            column.ReadOnly = column.DataPropertyName is "ID" or "职业名称" or "头像说明" or "武器名" or "防具名" or "辅助名" or "R资源状态" or "S资源状态";
            column.Visible = !hiddenColumns.Contains(column.DataPropertyName);
            column.ToolTipText = BuildRoleColumnAnnotation(column.DataPropertyName);
            column.HeaderText = column.DataPropertyName switch
            {
                "职业" => "职业\n详细兵种",
                "职业名称" => "职业名称\n引用",
                "头像说明" => "头像说明\n资源",
                "武器" => "默认武器\n物品ID",
                "防具" => "默认防具\n物品ID",
                "辅助" => "默认辅助\n物品ID",
                "武器名" or "防具名" or "辅助名" => column.DataPropertyName + "\n引用",
                "R形象编号" => "R形象编号\n资源",
                "S形象编号" => "S形象编号\n资源",
                "R资源状态" or "S资源状态" => column.DataPropertyName,
                _ => BuildRoleColumnHeader(column.DataPropertyName)
            };
            if (column.DataPropertyName is "R资源状态" or "S资源状态")
            {
                column.Width = 150;
            }
        }

        RefreshRoleEditorRowStyles();
    }

    private void ReplaceRoleJobColumnWithCombo()
    {
        if (_roleEditorJobLookup == null) return;
        if (!_roleEditorGrid.Columns.Contains("职业")) return;
        if (_roleEditorGrid.Columns["职业"] is DataGridViewComboBoxColumn) return;

        var old = _roleEditorGrid.Columns["职业"];
        var index = old.Index;
        _roleEditorGrid.Columns.Remove(old);
        var combo = new DataGridViewComboBoxColumn
        {
            Name = "职业",
            DataPropertyName = "职业",
            DataSource = _roleEditorJobLookup,
            ValueMember = "ID",
            DisplayMember = "显示",
            DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
            FlatStyle = FlatStyle.Flat,
            SortMode = DataGridViewColumnSortMode.Automatic,
            Width = 160
        };
        _roleEditorGrid.Columns.Insert(index, combo);
    }

    private void ReplaceRoleEquipmentColumnsWithCombos()
    {
        if (_project == null) return;
        var boundary = ItemCategoryBoundaryService.Resolve(_project);
        var itemNames = BuildItemNameLookup(_project, _tables);
        var classifications = new ItemClassificationService().BuildLookup(_project, _tables);
        ReplaceRoleEquipmentColumnWithCombo("武器", BuildRoleEquipmentLookup(RoleEquipmentSlot.Weapon, boundary, itemNames, classifications));
        ReplaceRoleEquipmentColumnWithCombo("防具", BuildRoleEquipmentLookup(RoleEquipmentSlot.Armor, boundary, itemNames, classifications));
        ReplaceRoleEquipmentColumnWithCombo("辅助", BuildRoleEquipmentLookup(RoleEquipmentSlot.Assist, boundary, itemNames, classifications));
    }

    private void ConfigureRoleEquipmentDetailControls()
    {
        if (_project == null)
        {
            ClearRoleEquipmentDetailControls();
            return;
        }

        var boundary = ItemCategoryBoundaryService.Resolve(_project);
        var itemNames = BuildItemNameLookup(_project, _tables);
        var classifications = new ItemClassificationService().BuildLookup(_project, _tables);

        _updatingRoleEquipmentDetailControls = true;
        try
        {
            ConfigureRoleEquipmentDetailCombo(_roleWeaponCombo, BuildRoleEquipmentLookup(RoleEquipmentSlot.Weapon, boundary, itemNames, classifications));
            ConfigureRoleEquipmentDetailCombo(_roleArmorCombo, BuildRoleEquipmentLookup(RoleEquipmentSlot.Armor, boundary, itemNames, classifications));
            ConfigureRoleEquipmentDetailCombo(_roleAssistCombo, BuildRoleEquipmentLookup(RoleEquipmentSlot.Assist, boundary, itemNames, classifications));
            SetRoleEquipmentDetailControlsEnabled(false);
        }
        finally
        {
            _updatingRoleEquipmentDetailControls = false;
        }
    }

    private static void ConfigureRoleEquipmentDetailCombo(ComboBox combo, DataTable lookup)
    {
        combo.DisplayMember = "显示";
        combo.ValueMember = "ID";
        combo.DataSource = lookup;
        combo.SelectedValue = 255;
    }

    private void ClearRoleEquipmentDetailControls()
    {
        _updatingRoleEquipmentDetailControls = true;
        try
        {
            _roleWeaponCombo.DataSource = null;
            _roleArmorCombo.DataSource = null;
            _roleAssistCombo.DataSource = null;
            SetRoleEquipmentDetailControlsEnabled(false);
        }
        finally
        {
            _updatingRoleEquipmentDetailControls = false;
        }
    }

    private void ClearRoleCriticalQuoteAssignmentControls()
    {
        _updatingRoleCriticalQuoteAssignmentControls = true;
        try
        {
            _roleCriticalQuoteModeCombo.DataSource = null;
            _roleCriticalQuoteAssignmentCombo.DataSource = null;
            SetRoleCriticalQuoteAssignmentControlsEnabled(false);
            _roleRetreatQuoteBox.Text = string.Empty;
            _roleRetreatQuoteBox.Enabled = false;
            _roleRetreatQuoteBox.ReadOnly = true;
            _loadedRoleCriticalQuoteSelection = null;
        }
        finally
        {
            _updatingRoleCriticalQuoteAssignmentControls = false;
        }
    }

    private void SetRoleEquipmentDetailControlsEnabled(bool enabled)
    {
        _roleWeaponCombo.Enabled = enabled;
        _roleArmorCombo.Enabled = enabled;
        _roleAssistCombo.Enabled = enabled;
    }

    private void ReplaceRoleEquipmentColumnWithCombo(string columnName, DataTable lookup)
    {
        if (!_roleEditorGrid.Columns.Contains(columnName)) return;
        if (_roleEditorGrid.Columns[columnName] is DataGridViewComboBoxColumn) return;

        var old = _roleEditorGrid.Columns[columnName];
        var index = old.Index;
        _roleEditorGrid.Columns.Remove(old);
        var combo = new DataGridViewComboBoxColumn
        {
            Name = columnName,
            DataPropertyName = columnName,
            DataSource = lookup,
            ValueMember = "ID",
            DisplayMember = "显示",
            DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
            FlatStyle = FlatStyle.Flat,
            SortMode = DataGridViewColumnSortMode.Automatic,
            Width = 170
        };
        _roleEditorGrid.Columns.Insert(index, combo);
    }

    private static DataTable BuildRoleEquipmentLookup(
        RoleEquipmentSlot slot,
        ItemCategoryBoundary boundary,
        IReadOnlyDictionary<int, string> itemNames,
        IReadOnlyDictionary<int, ItemClassification> classifications)
    {
        var lookup = new DataTable("RoleEquipmentLookup");
        lookup.Columns.Add("ID", typeof(int));
        lookup.Columns.Add("显示", typeof(string));
        lookup.Rows.Add(255, "255：空/未指定");

        var (start, endExclusive) = slot switch
        {
            RoleEquipmentSlot.Weapon => (boundary.WeaponStartId, boundary.DefenseStartId),
            RoleEquipmentSlot.Armor => (boundary.DefenseStartId, boundary.AccessoryStartId),
            RoleEquipmentSlot.Assist => (boundary.AccessoryStartId, 256),
            _ => (0, 0)
        };

        for (var itemId = start; itemId < endExclusive; itemId++)
        {
            var classification = classifications.TryGetValue(itemId, out var known)
                ? known
                : new ItemClassification(itemId, RoleEquipmentSlotToItemKind(slot), boundary.GetMajorCategory(itemId), true, false, 0, 0, 0);
            if (slot == RoleEquipmentSlot.Assist &&
                classification.Kind is ItemKind.Consumable or ItemKind.Reserved)
            {
                continue;
            }

            var name = itemNames.TryGetValue(itemId, out var itemName) && !string.IsNullOrWhiteSpace(itemName)
                ? itemName
                : "未命名";
            lookup.Rows.Add(itemId, $"{itemId}：{name}（{classification.DisplayName}/类型{classification.TypeId}）");
        }

        return lookup;
    }

    private static ItemKind RoleEquipmentSlotToItemKind(RoleEquipmentSlot slot)
        => slot switch
        {
            RoleEquipmentSlot.Weapon => ItemKind.Weapon,
            RoleEquipmentSlot.Armor => ItemKind.Armor,
            RoleEquipmentSlot.Assist => ItemKind.AccessoryEquipment,
            _ => ItemKind.Unknown
        };

    private string BuildRoleColumnHeader(string columnName)
    {
        if (columnName == "ID") return "ID\n行号";
        if (columnName is "职业名称" or "头像说明" or "武器名" or "防具名" or "辅助名") return columnName;
        var personTable = _project != null && HexTableNameResolver.TryResolveForProject(_project, _tables, "6.5-0 人物", out var resolvedPersonTable)
            ? resolvedPersonTable
            : null;
        var field = personTable?.Fields.FirstOrDefault(f => f.ColumnName == columnName);
        return field == null
            ? columnName
            : columnName + "\n" + _fieldAnnotationService.BuildShortFieldAnnotation(personTable!, field);
    }

    private string BuildRoleColumnAnnotation(string columnName)
    {
        if (columnName == "ID") return "人物行号/编号，用于和曹操传人物表下标对应。";
        if (columnName == "职业名称") return "根据人物表“职业”编号自动引用 `6.5-4 详细兵种` 的名称，便于确认角色当前兵种。";
        if (columnName == "头像说明") return "根据人物表“头像”编号生成的头像映射说明：Data 头像号 -> Face.e5 小头像号（0 号使用 1-8 候选）以及 Tou.dll 真彩资源号（=小头像号+300，语言2052）。";
        if (columnName is "武器" or "防具" or "辅助") return BuildRoleEquipmentColumnAnnotation(columnName);
        if (columnName is "武器名" or "防具名" or "辅助名") return "只读显示列：根据人物默认装备物品 ID 和当前物品表解析名称。";
        if (columnName == "暴击台词") return "暴击台词类型号，不是直接文本行号。若人物命中 Ekd5.exe @ 0x89C30 的 21 组特殊人物表，则使用对应特殊台词行 #0..#20；否则按 `21 + 类型号 * 3` 起连续 3 行作为普通暴击随机台词。";
        if (columnName == "撤退台词") return "6.5 实机显示撤退台词时通常按人物行 ID 读取 `6.5-0-3 撤退台词` 同 ID 行（仅 0..48），不是直接使用该字段值定位文本行；该字段保留为兼容/旧工具数据。";
        if (columnName is "R形象编号") return "人物 R 形象编号：对应 Pmapobj.e5 的正/反两张图（正=2n+1，反=2n+2，按 1-based 图号解释）。";
        if (columnName is "S形象编号") return "人物 S 形象紧凑编号：S=0 按职业和预览阵营取默认兵种图；S=1..32 对应三转特殊三张图；S>=33 从 Unit 图337 起对应一转特殊单张图。";
        if (columnName is "R资源状态" or "S资源状态") return "状态列用于提示相关资源文件是否已定位（例如 Pmapobj.e5、Unit_*.e5）。";

        var personTable = _project != null && HexTableNameResolver.TryResolveForProject(_project, _tables, "6.5-0 人物", out var resolvedPersonTable)
            ? resolvedPersonTable
            : null;
        var field = personTable?.Fields.FirstOrDefault(f => f.ColumnName == columnName);
        return field == null ? columnName : _fieldAnnotationService.BuildFieldAnnotation(personTable!, field);
    }

    private string BuildRoleJobName(int jobId)
        => _roleEditorJobNames.TryGetValue(jobId, out var name) ? name : $"{jobId}：未在详细兵种表中找到";

    private string BuildRoleFaceHint(int faceId)
    {
        if (_project == null) return $"头像号 {faceId}";
        return new CharacterImageResourceService().BuildFaceHint(_project, faceId);
    }

    private string BuildRoleEquipmentColumnAnnotation(string columnName)
    {
        var boundary = _project != null
            ? ItemCategoryBoundaryService.Resolve(_project)
            : new ItemCategoryBoundary(ItemCategoryBoundaryService.MinItemId, ItemCategoryBoundaryService.DefaultDefenseStartId, ItemCategoryBoundaryService.DefaultAccessoryStartId, "默认边界", IsFallback: true);
        var range = columnName switch
        {
            "武器" => $"{boundary.WeaponStartId}..{boundary.DefenseStartId - 1}",
            "防具" => $"{boundary.DefenseStartId}..{boundary.AccessoryStartId - 1}",
            "辅助" => $"{boundary.AccessoryStartId}..255",
            _ => "0..255"
        };
        return $"{columnName}：Data.e5 人物记录尾部默认装备槽，保存为物品绝对 ID；255 表示空/未指定，0 是合法物品 ID。允许范围：{range}。不要和 R/S 0x3E 战场装备设定的 0=默认、1=卸去、2+=分段相对编号混用。装备分段：{boundary.DisplayText}";
    }

    private string BuildRoleEditorSummary(DataTable data)
    {
        var named = data.Rows.Cast<DataRow>().Count(row => !string.IsNullOrWhiteSpace(Convert.ToString(row["名称"], CultureInfo.InvariantCulture)));
        var missingR = data.Rows.Cast<DataRow>().Count(row => Convert.ToString(row["R资源状态"], CultureInfo.InvariantCulture)?.StartsWith("缺失", StringComparison.Ordinal) == true);
        var missingS = data.Rows.Cast<DataRow>().Count(row => Convert.ToString(row["S资源状态"], CultureInfo.InvariantCulture)?.StartsWith("缺失", StringComparison.Ordinal) == true);
        var equipped = data.Rows.Cast<DataRow>().Count(row =>
            ReadRoleEquipmentCell(row, "武器") != 255 ||
            ReadRoleEquipmentCell(row, "防具") != 255 ||
            ReadRoleEquipmentCell(row, "辅助") != 255);
        var boundary = _project != null
            ? ItemCategoryBoundaryService.Resolve(_project).DisplayText
            : "DefID=70, AssID=109";
        return
            $"角色设定已读取：总行 {data.Rows.Count}，有名称 {named}。\r\n" +
            $"R 资源缺失 {missingR}，S 资源缺失 {missingS}。\r\n" +
            $"人物默认装备：已设置 {equipped} 行；武器/防具/辅助为 Data.e5 绝对物品 ID，255 为空。装备分段：{boundary}。\r\n" +
            "可编辑字段来自 `6.5-0 人物`、`6.5-0-4 R形象`、`6.5-0-5 S形象`；保存前自动备份 Data.e5/Ekd5.exe，保存后重新读取校验。";
    }

    private void ApplyRoleEditorFilter()
    {
        if (_currentRoleEditorData == null) return;
        var keyword = _roleEditorSearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            _currentRoleEditorData.DefaultView.RowFilter = string.Empty;
            SetStatus("角色筛选已清除");
            return;
        }

        var escaped = EscapeDataViewLikeValue(keyword);
        var searchableColumns = new[] { "ID", "名称", "职业", "职业名称", "头像", "头像说明", "武器", "武器名", "防具", "防具名", "辅助", "辅助名", "R形象编号", "S形象编号", "R资源状态", "S资源状态" }
            .Where(name => _currentRoleEditorData.Columns.Contains(name))
            .Select(name => $"CONVERT([{name}], 'System.String') LIKE '*{escaped}*'");
        _currentRoleEditorData.DefaultView.RowFilter = string.Join(" OR ", searchableColumns);
        SetStatus($"角色筛选：{_currentRoleEditorData.DefaultView.Count}/{_currentRoleEditorData.Rows.Count}");
    }

    private void ClearRoleEditorFilter()
    {
        _roleEditorSearchBox.Clear();
        if (_currentRoleEditorData != null) _currentRoleEditorData.DefaultView.RowFilter = string.Empty;
        SetStatus("角色筛选已清除");
    }

    private void ExportRoleEditorCsv()
        => ExportDataTableCsv(_currentRoleEditorData, "角色设定.csv");

    private void ImportRoleEditorCsv()
        => ImportDataTableCsv(_currentRoleEditorData, "角色设定", RefreshRoleEditorAfterBulkEdit);

    private void RefreshRoleEditorAfterBulkEdit()
    {
        if (_currentRoleEditorData != null)
        {
            foreach (DataRow row in _currentRoleEditorData.Rows)
            {
                RefreshRoleEditorDerivedCells(row);
            }
        }

        RefreshRoleEditorRowStyles();
        ShowSelectedRoleEditorCell();
    }

    private void RefreshRoleEditorCellsAfterEdit(IReadOnlyList<GridCellKey> changedCells)
    {
        RefreshChangedGridCells(_roleEditorGrid, changedCells, UpdateRoleEditorDerivedCells);
        RefreshChangedGridRowsOnly(_roleEditorGrid, changedCells, RefreshRoleEditorRowStyle);
        ShowSelectedRoleEditorCell();
    }

    private static string EscapeDataViewLikeValue(string value)
        => value.Replace("'", "''")
            .Replace("[", "[[]")
            .Replace("*", "[*]")
            .Replace("%", "[%]");

    private void RefreshRoleEditorRowStyles()
    {
        foreach (DataGridViewRow row in _roleEditorGrid.Rows)
        {
            RefreshRoleEditorRowStyle(row.Index);
        }
    }

    private void RefreshRoleEditorRowStyle(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _roleEditorGrid.Rows.Count) return;
        var gridRow = _roleEditorGrid.Rows[rowIndex];
        var dataRow = TryGetDataRow(gridRow);
        if (dataRow == null) return;

        gridRow.DefaultCellStyle.BackColor = IsDataRowChanged(dataRow) ? Color.LightCyan : Color.Empty;
        foreach (DataGridViewCell cell in gridRow.Cells)
        {
            if (cell.OwningColumn.DataPropertyName is not ("R资源状态" or "S资源状态")) continue;
            var text = Convert.ToString(cell.Value, CultureInfo.InvariantCulture) ?? string.Empty;
            cell.Style.BackColor = text.StartsWith("缺失", StringComparison.Ordinal) ? Color.MistyRose : Color.Empty;
        }
    }

    private void UpdateRoleEditorDerivedCells(int rowIndex, int columnIndex)
    {
        if (_project == null || rowIndex < 0 || rowIndex >= _roleEditorGrid.Rows.Count) return;
        if (columnIndex < 0 || columnIndex >= _roleEditorGrid.Columns.Count) return;
        var gridRow = _roleEditorGrid.Rows[rowIndex];
        var dataRow = TryGetDataRow(gridRow);
        if (dataRow == null) return;

        var columnName = _roleEditorGrid.Columns[columnIndex].DataPropertyName;
        if (columnName == "职业" && _currentRoleEditorData?.Columns.Contains("职业名称") == true)
        {
            var jobId = Convert.ToInt32(dataRow["职业"], CultureInfo.InvariantCulture);
            dataRow["职业名称"] = BuildRoleJobName(jobId);
        }
        if (columnName == "头像" && _currentRoleEditorData?.Columns.Contains("头像说明") == true)
        {
            var faceId = Convert.ToInt32(dataRow["头像"], CultureInfo.InvariantCulture);
            dataRow["头像说明"] = BuildRoleFaceHint(faceId);
        }
        if (columnName == "R形象编号" && _currentRoleEditorData?.Columns.Contains("R资源状态") == true)
        {
            var rId = Convert.ToInt32(dataRow["R形象编号"], CultureInfo.InvariantCulture);
            dataRow["R资源状态"] = ImageAssignmentService.GetImageResourceStatus(_project, "R", rId);
        }
        if (columnName == "S形象编号" && _currentRoleEditorData?.Columns.Contains("S资源状态") == true)
        {
            var sId = Convert.ToInt32(dataRow["S形象编号"], CultureInfo.InvariantCulture);
            dataRow["S资源状态"] = ImageAssignmentService.GetImageResourceStatus(_project, "S", sId);
        }
        if (IsRoleEquipmentColumn(columnName))
        {
            RefreshRoleEquipmentNameCells(dataRow);
        }
    }

    private void RefreshRoleEditorDerivedCells(DataRow dataRow)
    {
        if (_project == null || _currentRoleEditorData == null) return;
        if (_currentRoleEditorData.Columns.Contains("职业") && _currentRoleEditorData.Columns.Contains("职业名称"))
        {
            var jobId = Convert.ToInt32(dataRow["职业"], CultureInfo.InvariantCulture);
            dataRow["职业名称"] = BuildRoleJobName(jobId);
        }

        if (_currentRoleEditorData.Columns.Contains("头像") && _currentRoleEditorData.Columns.Contains("头像说明"))
        {
            var faceId = Convert.ToInt32(dataRow["头像"], CultureInfo.InvariantCulture);
            dataRow["头像说明"] = BuildRoleFaceHint(faceId);
        }

        if (_currentRoleEditorData.Columns.Contains("R形象编号") && _currentRoleEditorData.Columns.Contains("R资源状态"))
        {
            var rId = Convert.ToInt32(dataRow["R形象编号"], CultureInfo.InvariantCulture);
            dataRow["R资源状态"] = ImageAssignmentService.GetImageResourceStatus(_project, "R", rId);
        }

        if (_currentRoleEditorData.Columns.Contains("S形象编号") && _currentRoleEditorData.Columns.Contains("S资源状态"))
        {
            var sId = Convert.ToInt32(dataRow["S形象编号"], CultureInfo.InvariantCulture);
            dataRow["S资源状态"] = ImageAssignmentService.GetImageResourceStatus(_project, "S", sId);
        }

        RefreshRoleEquipmentNameCells(dataRow);
    }

    private void RefreshRoleEquipmentNameCells(DataRow row)
    {
        if (_project == null) return;
        RefreshRoleEquipmentNameCells(
            row,
            BuildItemNameLookup(_project, _tables),
            new ItemClassificationService().BuildLookup(_project, _tables));
    }

    private static void RefreshRoleEquipmentNameCells(
        DataRow row,
        IReadOnlyDictionary<int, string> itemNames,
        IReadOnlyDictionary<int, ItemClassification> classifications)
    {
        RefreshRoleEquipmentNameCell(row, "武器", "武器名", itemNames, classifications);
        RefreshRoleEquipmentNameCell(row, "防具", "防具名", itemNames, classifications);
        RefreshRoleEquipmentNameCell(row, "辅助", "辅助名", itemNames, classifications);
    }

    private static void RefreshRoleEquipmentNameCell(
        DataRow row,
        string valueColumn,
        string nameColumn,
        IReadOnlyDictionary<int, string> itemNames,
        IReadOnlyDictionary<int, ItemClassification> classifications)
    {
        if (!row.Table.Columns.Contains(valueColumn) || !row.Table.Columns.Contains(nameColumn)) return;
        var itemId = ReadRoleEquipmentCell(row, valueColumn);
        SetReadOnlyDerivedCell(row, nameColumn, BuildRoleEquipmentDisplayName(itemId, itemNames, classifications));
    }

    private static void SetReadOnlyDerivedCell(DataRow row, string columnName, object value)
    {
        var current = row[columnName];
        if (string.Equals(
                Convert.ToString(current, CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparison.Ordinal))
        {
            return;
        }

        var column = row.Table.Columns[columnName]!;
        var wasReadOnly = column.ReadOnly;
        var rowState = row.RowState;
        if (wasReadOnly) column.ReadOnly = false;
        try
        {
            row[columnName] = value;
        }
        finally
        {
            column.ReadOnly = wasReadOnly;
        }

        if (rowState == DataRowState.Unchanged && row.RowState == DataRowState.Modified)
        {
            row.AcceptChanges();
        }
    }

    private static string BuildRoleEquipmentDisplayName(
        int itemId,
        IReadOnlyDictionary<int, string> itemNames,
        IReadOnlyDictionary<int, ItemClassification> classifications)
    {
        if (itemId == 255) return "空/未指定";
        var name = itemNames.TryGetValue(itemId, out var itemName) && !string.IsNullOrWhiteSpace(itemName)
            ? itemName
            : "未找到物品名";
        var kind = classifications.TryGetValue(itemId, out var classification)
            ? classification.DisplayName
            : "未知";
        return $"{itemId}：{name}（{kind}）";
    }

    private static int ReadRoleEquipmentCell(DataRow row, string columnName)
    {
        if (!row.Table.Columns.Contains(columnName)) return 255;
        var value = row[columnName];
        if (value == null || value == DBNull.Value) return 255;
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static bool IsRoleEquipmentColumn(string columnName)
        => columnName is "武器" or "防具" or "辅助";

    private void ShowRoleEquipmentDetails(DataRow? roleRow)
    {
        _updatingRoleEquipmentDetailControls = true;
        try
        {
            if (roleRow == null || _currentRoleEditorData == null)
            {
                SetRoleEquipmentDetailControlsEnabled(false);
                SetRoleEquipmentComboValue(_roleWeaponCombo, 255);
                SetRoleEquipmentComboValue(_roleArmorCombo, 255);
                SetRoleEquipmentComboValue(_roleAssistCombo, 255);
                return;
            }

            SetRoleEquipmentComboValue(_roleWeaponCombo, ReadRoleEquipmentCell(roleRow, "武器"));
            SetRoleEquipmentComboValue(_roleArmorCombo, ReadRoleEquipmentCell(roleRow, "防具"));
            SetRoleEquipmentComboValue(_roleAssistCombo, ReadRoleEquipmentCell(roleRow, "辅助"));
            SetRoleEquipmentDetailControlsEnabled(true);
        }
        finally
        {
            _updatingRoleEquipmentDetailControls = false;
        }
    }

    private static void SetRoleEquipmentComboValue(ComboBox combo, int value)
    {
        if (combo.DataSource == null)
        {
            combo.SelectedIndex = -1;
            return;
        }

        combo.SelectedValue = value;
        if (combo.SelectedIndex < 0)
        {
            combo.SelectedValue = 255;
        }
    }

    private void ApplyRoleEquipmentDetailSelection(string columnName, ComboBox combo)
    {
        if (_updatingRoleEquipmentDetailControls || _currentRoleEditorData == null || _roleEditorGrid.CurrentRow == null) return;
        if (TryGetDataRow(_roleEditorGrid.CurrentRow) is not { } roleRow) return;
        if (combo.SelectedValue == null || combo.SelectedValue == DBNull.Value) return;

        var value = Convert.ToInt32(combo.SelectedValue, CultureInfo.InvariantCulture);
        if (!TryValidateRoleEquipmentValue(columnName, value, out var error))
        {
            SetStatus(error);
            _updatingRoleEquipmentDetailControls = true;
            try
            {
                SetRoleEquipmentComboValue(combo, ReadRoleEquipmentCell(roleRow, columnName));
            }
            finally
            {
                _updatingRoleEquipmentDetailControls = false;
            }
            return;
        }

        var current = ReadRoleEquipmentCell(roleRow, columnName);
        if (current == value)
        {
            return;
        }

        roleRow[columnName] = value;
        RefreshRoleEquipmentNameCells(roleRow);
        RefreshRoleEditorRowStyle(_roleEditorGrid.CurrentRow.Index);
        _saveRoleEditorButton.Enabled = true;
        SetStatus($"角色默认装备已更新：{columnName}={value}");
    }

    private void ValidateRoleEditorCell(DataGridViewCellValidatingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || _project == null || _currentRoleEditorData == null) return;
        var column = _roleEditorGrid.Columns[e.ColumnIndex];
        var columnName = column.DataPropertyName;
        if (!IsRoleEquipmentColumn(columnName)) return;

        var text = Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture) ?? string.Empty;
        if (!TryValidateRoleEquipmentValue(columnName, text, out var error))
        {
            e.Cancel = true;
            _roleEditorGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = error;
            SetStatus(error);
            return;
        }

        _roleEditorGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = string.Empty;
    }

    private bool TryValidateRoleEquipmentValue(string columnName, string text, out string error)
    {
        error = string.Empty;
        if (_project == null) return true;
        if (!TryParseRoleEquipmentInput(text, out var value))
        {
            error = $"{columnName} 必须是 0..255 的物品绝对 ID，255 表示空/未指定。";
            return false;
        }

        return TryValidateRoleEquipmentValue(columnName, value, out error);
    }

    private static bool TryParseRoleEquipmentInput(string text, out int value)
    {
        text = text.Trim();
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
        var separatorIndex = text.IndexOfAny(['：', ':']);
        if (separatorIndex > 0 &&
            int.TryParse(text[..separatorIndex].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private bool TryValidateRoleEquipmentValue(string columnName, int value, out string error)
    {
        error = string.Empty;
        if (_project == null) return true;
        if (!IsRoleEquipmentColumn(columnName)) return true;
        if (value == 255) return true;

        var boundary = ItemCategoryBoundaryService.Resolve(_project);
        var slot = columnName switch
        {
            "武器" => RoleEquipmentSlot.Weapon,
            "防具" => RoleEquipmentSlot.Armor,
            "辅助" => RoleEquipmentSlot.Assist,
            _ => RoleEquipmentSlot.Weapon
        };
        var allowed = slot switch
        {
            RoleEquipmentSlot.Weapon => value >= boundary.WeaponStartId && value < boundary.DefenseStartId,
            RoleEquipmentSlot.Armor => value >= boundary.DefenseStartId && value < boundary.AccessoryStartId,
            RoleEquipmentSlot.Assist => value >= boundary.AccessoryStartId && value <= 255,
            _ => false
        };
        if (!allowed)
        {
            error = $"{columnName}={value} 不在当前装备分段内。{BuildRoleEquipmentColumnAnnotation(columnName)}";
            return false;
        }

        if (slot == RoleEquipmentSlot.Assist)
        {
            var classifications = new ItemClassificationService().BuildLookup(_project, _tables);
            if (classifications.TryGetValue(value, out var classification) &&
                classification.Kind is ItemKind.Consumable or ItemKind.Reserved)
            {
                error = $"辅助={value} 是{classification.DisplayName}，不能作为人物默认辅助装备。";
                return false;
            }
        }

        return true;
    }

    private void ValidateRoleEditorEquipmentValues(DataTable roleData)
    {
        foreach (DataRow row in roleData.Rows)
        {
            foreach (var columnName in new[] { "武器", "防具", "辅助" })
            {
                if (!roleData.Columns.Contains(columnName)) continue;
                if (row.RowState != DataRowState.Modified || !IsRoleColumnChanged(row, columnName)) continue;
                var value = ReadRoleEquipmentCell(row, columnName);
                if (!TryValidateRoleEquipmentValue(columnName, value, out var error))
                {
                    var id = row.Table.Columns.Contains("ID")
                        ? Convert.ToString(row["ID"], CultureInfo.InvariantCulture)
                        : "?";
                    var name = row.Table.Columns.Contains("名称")
                        ? Convert.ToString(row["名称"], CultureInfo.InvariantCulture)
                        : string.Empty;
                    throw new InvalidOperationException($"角色设定默认装备非法：ID={id} {name}，{error}");
                }
            }
        }
    }

    private void ShowSelectedRoleEditorCell()
    {
        if (_currentRoleEditorData == null || _roleEditorGrid.CurrentCell == null)
        {
            return;
        }

        var cell = _roleEditorGrid.CurrentCell;
        var columnName = _roleEditorGrid.Columns[cell.ColumnIndex].DataPropertyName;
        var row = _roleEditorGrid.Rows[cell.RowIndex];
        var dataRow = TryGetDataRow(row);
        var id = row.Cells["ID"].Value;
        var name = row.Cells["名称"].Value;
        var value = cell.Value;
        _roleEditorInfoBox.Text =
            $"角色：ID={id}    名称={name}\r\n" +
            $"字段：{columnName}    当前值：{value}\r\n\r\n" +
            BuildRoleColumnAnnotation(columnName);
        if (dataRow != null)
        {
            ShowRoleTextDetails(dataRow);
        }
        else
        {
            ShowRoleEquipmentDetails(null);
        }
    }

    private void ShowRoleTextDetails(DataRow roleRow)
    {
        if (_roleBiographyRead == null || _roleCriticalQuoteRead == null || _roleRetreatQuoteRead == null)
        {
            return;
        }

        var roleId = Convert.ToInt32(roleRow["ID"], CultureInfo.InvariantCulture);
        var bioRow = TryFindRowById(_roleBiographyRead.Data, roleId);
        var criticalMapping = _roleQuoteMappingService.ResolveCriticalQuote(_project!, roleRow, _roleCriticalQuoteRead.Data);
        var retreatMapping = _roleQuoteMappingService.ResolveRetreatQuote(roleRow, _roleRetreatQuoteRead.Data);
        var criticalSelection = criticalMapping.IsSpecialRoleQuote
            ? new RoleCriticalQuoteSelection(RoleCriticalQuoteMode.Special, criticalMapping.QuoteIds.FirstOrDefault())
            : new RoleCriticalQuoteSelection(RoleCriticalQuoteMode.Generic, ReadRoleCriticalGenericType(roleRow));

        _roleBiographyBox.Text = Convert.ToString(bioRow?["介绍"], CultureInfo.InvariantCulture) ?? string.Empty;
        ShowRoleCriticalQuoteAssignmentControls(roleRow, criticalSelection);
        ShowCriticalQuoteEditor(_roleQuoteMappingService.ResolveCriticalQuoteSelection(roleRow, _roleCriticalQuoteRead.Data, criticalSelection));
        _roleRetreatQuoteBox.Text = Convert.ToString(retreatMapping.QuoteRow?["介绍"], CultureInfo.InvariantCulture) ?? string.Empty;
        ShowRetreatQuoteRule(retreatMapping);
        ShowRoleEquipmentDetails(roleRow);

        var canSaveAny = bioRow != null || criticalMapping.QuoteRows.Count > 0 || retreatMapping.QuoteRow != null;
        _saveRoleTextDetailButton.Enabled = canSaveAny;
    }

    private void ShowRoleCriticalQuoteAssignmentControls(DataRow roleRow, RoleCriticalQuoteSelection selection)
    {
        _updatingRoleCriticalQuoteAssignmentControls = true;
        try
        {
            _loadedRoleCriticalQuoteSelection = selection;
            _roleCriticalQuoteModeCombo.DataSource = new[]
            {
                new RoleCriticalQuoteComboItem { ID = (int)RoleCriticalQuoteMode.Special, 显示 = "特殊人物台词" },
                new RoleCriticalQuoteComboItem { ID = (int)RoleCriticalQuoteMode.Generic, 显示 = "普通类型台词" }
            };
            _roleCriticalQuoteModeCombo.SelectedValue = (int)selection.Mode;
            RefreshRoleCriticalQuoteAssignmentCombo(roleRow, selection);
            SetRoleCriticalQuoteAssignmentControlsEnabled(true);
        }
        finally
        {
            _updatingRoleCriticalQuoteAssignmentControls = false;
        }
    }

    private void RefreshRoleCriticalQuoteAssignmentCombo(DataRow roleRow, RoleCriticalQuoteSelection selection)
    {
        _roleCriticalQuoteAssignmentCombo.DataSource = selection.Mode == RoleCriticalQuoteMode.Special
            ? BuildRoleCriticalSpecialSlotLookup()
            : BuildRoleCriticalGenericTypeLookup();
        _roleCriticalQuoteAssignmentCombo.SelectedValue = selection.Value;
        if (_roleCriticalQuoteAssignmentCombo.SelectedIndex < 0 && _roleCriticalQuoteAssignmentCombo.Items.Count > 0)
        {
            _roleCriticalQuoteAssignmentCombo.SelectedIndex = 0;
            selection = new RoleCriticalQuoteSelection(selection.Mode, Convert.ToInt32(_roleCriticalQuoteAssignmentCombo.SelectedValue, CultureInfo.InvariantCulture));
        }

    }

    private List<RoleCriticalQuoteComboItem> BuildRoleCriticalSpecialSlotLookup()
    {
        var specialRoleIds = _project == null
            ? Array.Empty<int>()
            : _roleQuoteMappingService.ReadSpecialCriticalRoleIds(_project).ToArray();
        var items = new List<RoleCriticalQuoteComboItem>(RoleQuoteMappingService.CriticalSpecialQuoteCount);
        for (var i = 0; i < RoleQuoteMappingService.CriticalSpecialQuoteCount; i++)
        {
            var roleId = i < specialRoleIds.Length ? specialRoleIds[i] : RoleQuoteMappingService.CriticalSpecialEmptyRoleId;
            var roleText = roleId == RoleQuoteMappingService.CriticalSpecialEmptyRoleId
                ? "空槽"
                : $"{roleId} {FindRoleNameForDisplay(roleId)}";
            items.Add(new RoleCriticalQuoteComboItem
            {
                ID = i,
                显示 = $"特殊 #{i}：{roleText}"
            });
        }

        return items;
    }

    private List<RoleCriticalQuoteComboItem> BuildRoleCriticalGenericTypeLookup()
    {
        var items = new List<RoleCriticalQuoteComboItem>(RoleQuoteMappingService.CriticalGenericTypeCount);
        for (var type = 0; type < RoleQuoteMappingService.CriticalGenericTypeCount; type++)
        {
            var firstId = RoleQuoteMappingService.FirstGenericCriticalQuoteId(type);
            items.Add(new RoleCriticalQuoteComboItem
            {
                ID = type,
                显示 = $"类型 {type}：#{firstId}-#{firstId + RoleQuoteMappingService.CriticalGenericGroupSize - 1} {BuildGenericCriticalQuoteSummary(firstId)}"
            });
        }

        return items;
    }

    private string BuildGenericCriticalQuoteSummary(int firstId)
    {
        if (_roleCriticalQuoteRead == null) return string.Empty;
        var snippets = new List<string>(RoleQuoteMappingService.CriticalGenericGroupSize);
        for (var id = firstId; id < firstId + RoleQuoteMappingService.CriticalGenericGroupSize; id++)
        {
            var row = TryFindRowById(_roleCriticalQuoteRead.Data, id);
            var text = Convert.ToString(row?["介绍"], CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text)) continue;
            snippets.Add(text.Length > 8 ? text[..8] + "..." : text);
        }

        return snippets.Count == 0 ? "未填写" : string.Join(" / ", snippets);
    }

    private string FindRoleNameForDisplay(int roleId)
    {
        if (_currentRoleEditorData != null && TryFindRowById(_currentRoleEditorData, roleId) is { } row)
        {
            return Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? "未命名";
        }

        return "未读取";
    }

    private void SetRoleCriticalQuoteAssignmentControlsEnabled(bool enabled)
    {
        _roleCriticalQuoteModeCombo.Enabled = enabled;
        _roleCriticalQuoteAssignmentCombo.Enabled = enabled;
    }

    private void ChangeRoleCriticalQuoteModeFromUi()
    {
        if (_updatingRoleCriticalQuoteAssignmentControls || _roleEditorGrid.CurrentRow == null) return;
        if (TryGetDataRow(_roleEditorGrid.CurrentRow) is not { } roleRow) return;
        if (_roleCriticalQuoteModeCombo.SelectedValue == null) return;

        var mode = (RoleCriticalQuoteMode)Convert.ToInt32(_roleCriticalQuoteModeCombo.SelectedValue, CultureInfo.InvariantCulture);
        var value = mode == RoleCriticalQuoteMode.Special
            ? ResolveDefaultSpecialCriticalSlotForRole(roleRow)
            : ReadRoleCriticalGenericType(roleRow);
        var selection = new RoleCriticalQuoteSelection(mode, value);
        _updatingRoleCriticalQuoteAssignmentControls = true;
        try
        {
            RefreshRoleCriticalQuoteAssignmentCombo(roleRow, selection);
            _roleCriticalQuoteAssignmentCombo.SelectedValue = value;
        }
        finally
        {
            _updatingRoleCriticalQuoteAssignmentControls = false;
        }

        RefreshCriticalQuoteEditorFromAssignment(roleRow);
    }

    private void ChangeRoleCriticalQuoteAssignmentFromUi()
    {
        if (_updatingRoleCriticalQuoteAssignmentControls || _roleEditorGrid.CurrentRow == null) return;
        if (TryGetDataRow(_roleEditorGrid.CurrentRow) is not { } roleRow) return;
        RefreshCriticalQuoteEditorFromAssignment(roleRow);
    }

    private void RefreshCriticalQuoteEditorFromAssignment(DataRow roleRow)
    {
        if (_roleCriticalQuoteRead == null) return;
        var selection = GetCurrentRoleCriticalQuoteSelection();
        var mapping = _roleQuoteMappingService.ResolveCriticalQuoteSelection(roleRow, _roleCriticalQuoteRead.Data, selection);
        ShowCriticalQuoteEditor(mapping);
        _saveRoleTextDetailButton.Enabled = true;
    }

    private RoleCriticalQuoteSelection GetCurrentRoleCriticalQuoteSelection()
    {
        var modeValue = _roleCriticalQuoteModeCombo.SelectedValue == null
            ? (int)RoleCriticalQuoteMode.Generic
            : Convert.ToInt32(_roleCriticalQuoteModeCombo.SelectedValue, CultureInfo.InvariantCulture);
        var assignmentValue = _roleCriticalQuoteAssignmentCombo.SelectedValue == null
            ? 0
            : Convert.ToInt32(_roleCriticalQuoteAssignmentCombo.SelectedValue, CultureInfo.InvariantCulture);
        return new RoleCriticalQuoteSelection((RoleCriticalQuoteMode)modeValue, assignmentValue);
    }

    private int ResolveDefaultSpecialCriticalSlotForRole(DataRow roleRow)
    {
        if (_project == null) return 0;
        var roleId = Convert.ToInt32(roleRow["ID"], CultureInfo.InvariantCulture);
        return _roleQuoteMappingService.FindSpecialCriticalQuoteId(_project, roleId)
            ?? FindFirstEmptySpecialCriticalSlot()
            ?? 0;
    }

    private int? FindFirstEmptySpecialCriticalSlot()
    {
        if (_project == null) return null;
        var ids = _roleQuoteMappingService.ReadSpecialCriticalRoleIds(_project);
        for (var i = 0; i < ids.Count; i++)
        {
            if (ids[i] == RoleQuoteMappingService.CriticalSpecialEmptyRoleId) return i;
        }

        return null;
    }

    private static int ReadRoleCriticalGenericType(DataRow roleRow)
    {
        var value = Convert.ToInt32(roleRow["暴击台词"], CultureInfo.InvariantCulture);
        return value >= 0 && value < RoleQuoteMappingService.CriticalGenericTypeCount ? value : 0;
    }

    private RoleCriticalAssignmentPreview BuildRoleCriticalAssignmentPreview(DataRow roleRow, RoleCriticalQuoteSelection selection)
    {
        var roleId = Convert.ToInt32(roleRow["ID"], CultureInfo.InvariantCulture);
        var currentSpecial = _project == null ? null : _roleQuoteMappingService.FindSpecialCriticalQuoteId(_project, roleId);
        var currentGeneric = ReadRoleCriticalGenericType(roleRow);
        if (selection.Mode == RoleCriticalQuoteMode.Special)
        {
            var specialRoleIds = _project == null
                ? Array.Empty<int>()
                : _roleQuoteMappingService.ReadSpecialCriticalRoleIds(_project).ToArray();
            var targetOwner = selection.Value >= 0 && selection.Value < specialRoleIds.Length
                ? specialRoleIds[selection.Value]
                : RoleQuoteMappingService.CriticalSpecialEmptyRoleId;
            var replacesOther = targetOwner != RoleQuoteMappingService.CriticalSpecialEmptyRoleId && targetOwner != roleId;
            var summary = $"暴击分配：特殊人物台词槽 #{selection.Value}";
            if (currentSpecial is { } oldSlot && oldSlot != selection.Value)
            {
                summary += $"；将从原特殊槽 #{oldSlot} 移至 #{selection.Value}";
            }
            else if (currentSpecial == null)
            {
                summary += "；将从普通类型切换为特殊人物台词";
            }

            if (replacesOther)
            {
                summary += $"；将替换 ID={targetOwner} {FindRoleNameForDisplay(targetOwner)}";
            }

            return new RoleCriticalAssignmentPreview
            {
                HasChanges = currentSpecial != selection.Value || replacesOther,
                Summary = summary
            };
        }

        var firstId = RoleQuoteMappingService.FirstGenericCriticalQuoteId(selection.Value);
        var hasChanges = currentSpecial != null || currentGeneric != selection.Value;
        var text = $"暴击分配：普通类型 {selection.Value}，实际行 #{firstId}..#{firstId + 2}";
        if (currentSpecial is { } oldSpecial)
        {
            text += $"；将移除原特殊槽 #{oldSpecial}";
        }
        else if (currentGeneric != selection.Value)
        {
            text += $"；人物表字段从 {currentGeneric} 改为 {selection.Value}";
        }

        return new RoleCriticalAssignmentPreview { HasChanges = hasChanges, Summary = text };
    }

    private void ShowRetreatQuoteRule(RoleRetreatQuoteMapping mapping)
    {
        if (mapping.QuoteRow == null)
        {
            _roleRetreatQuoteBox.Text = string.Empty;
            _roleRetreatQuoteBox.Enabled = false;
            _roleRetreatQuoteBox.ReadOnly = true;
            return;
        }

        _roleRetreatQuoteBox.Enabled = true;
        _roleRetreatQuoteBox.ReadOnly = false;
    }

    private void ShowCriticalQuoteEditor(RoleCriticalQuoteMapping mapping)
    {
        for (var i = 0; i < _roleCriticalQuoteBoxes.Length; i++)
        {
            var hasRow = i < mapping.QuoteRows.Count;
            var box = _roleCriticalQuoteBoxes[i];
            var label = _roleCriticalQuoteLabels[i];

            box.Text = hasRow
                ? Convert.ToString(mapping.QuoteRows[i]["介绍"], CultureInfo.InvariantCulture) ?? string.Empty
                : string.Empty;
            box.Enabled = hasRow;
            box.ReadOnly = !hasRow;

            if (!hasRow)
            {
                label.Text = $"第{i + 1}句";
            }
            else if (mapping.IsSpecialRoleQuote)
            {
                label.Text = $"特殊台词 #{mapping.QuoteIds[i]}";
            }
            else
            {
                label.Text = $"第{i + 1}句 #{mapping.QuoteIds[i]}";
            }
        }
    }

    private void ApplyCriticalQuoteEditorToRows(RoleCriticalQuoteMapping mapping)
    {
        for (var i = 0; i < mapping.QuoteRows.Count && i < _roleCriticalQuoteBoxes.Length; i++)
        {
            mapping.QuoteRows[i]["介绍"] = _roleCriticalQuoteBoxes[i].Text;
        }
    }

    private string BuildCriticalQuoteByteHint(RoleCriticalQuoteMapping mapping)
    {
        if (mapping.QuoteRows.Count == 0)
        {
            return "没有可编辑的暴击台词行。";
        }

        var hints = mapping.QuoteIds
            .Select((id, index) => $"#{id}={EncodingService.GetGbkByteCount(_roleCriticalQuoteBoxes[index].Text)}/200")
            .ToArray();
        return "每条 GBK 字节：" + string.Join("，", hints) + "。";
    }

    private void SaveSelectedRoleTextDetails()
    {
        if (_project == null || _roleEditorGrid.CurrentRow == null ||
            _roleBiographyRead == null || _roleCriticalQuoteRead == null || _roleRetreatQuoteRead == null)
        {
            return;
        }

        var roleRow = TryGetDataRow(_roleEditorGrid.CurrentRow);
        if (roleRow == null) return;

        var roleId = Convert.ToInt32(roleRow["ID"], CultureInfo.InvariantCulture);
        var roleName = Convert.ToString(roleRow["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        var bioRow = FindRowById(_roleBiographyRead.Data, roleId);
        var criticalSelection = GetCurrentRoleCriticalQuoteSelection();
        var criticalMapping = _roleQuoteMappingService.ResolveCriticalQuoteSelection(roleRow, _roleCriticalQuoteRead.Data, criticalSelection);
        var retreatMapping = _roleQuoteMappingService.ResolveRetreatQuote(roleRow, _roleRetreatQuoteRead.Data);

        ValidateRoleTextCapacities(criticalMapping, retreatMapping);

        bioRow["介绍"] = _roleBiographyBox.Text;
        ApplyCriticalQuoteEditorToRows(criticalMapping);

        if (retreatMapping.QuoteRow != null)
        {
            retreatMapping.QuoteRow["介绍"] = _roleRetreatQuoteBox.Text;
        }

        var assignmentPreview = BuildRoleCriticalAssignmentPreview(roleRow, criticalSelection);
        if (!assignmentPreview.HasChanges &&
            _roleBiographyRead.Data.GetChanges() == null &&
            _roleCriticalQuoteRead.Data.GetChanges() == null &&
            _roleRetreatQuoteRead.Data.GetChanges() == null)
        {
            MessageBox.Show(this, "当前角色列传/台词没有检测到改动。", "无需保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var preview =
            $"角色：{roleId} {roleName}\r\n" +
            $"列传 GBK：{EncodingService.GetGbkByteCount(_roleBiographyBox.Text)}/200\r\n" +
            assignmentPreview.Summary + "\r\n" +
            $"暴击台词：{string.Join(", ", criticalMapping.QuoteIds.Select(id => "#" + id.ToString(CultureInfo.InvariantCulture)))} {BuildCriticalQuoteByteHint(criticalMapping)}\r\n" +
            (retreatMapping.QuoteRow == null
                ? "撤退台词：人物 ID 不在 0..48，原生撤退台词本次不写回；请用 S 战场事件处理。"
                : $"撤退台词 #{retreatMapping.QuoteId} GBK：{EncodingService.GetGbkByteCount(_roleRetreatQuoteBox.Text)}/200");
        if (MessageBox.Show(this,
                $"即将保存当前角色列传/台词与暴击分配。\r\n\r\n{preview}\r\n\r\n可能写入 Data.e5、Imsg.e5、Ekd5.exe；保存前会自动备份，保存后会重新读取校验。是否继续？",
                "确认保存角色文本",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = SaveRoleTextDetailsCore(roleRow, criticalSelection);
            ShowRoleTextDetails(roleRow);
            var changedBytes = result.ChangedBytes;
            System.Diagnostics.Debug.WriteLine($"已保存角色文本：{roleId} {roleName}，保存项 {result.SaveCount} 个，变化字节 {changedBytes}");
            foreach (var backup in result.BackupPaths) System.Diagnostics.Debug.WriteLine("角色文本备份：" + backup);
            SetStatus($"角色文本保存完成并已复读：变化 {changedBytes} 字节");
            MessageBox.Show(this,
                $"保存完成并已重新读取校验。\r\n保存项数量：{result.SaveCount}\r\n变化字节：{changedBytes}\r\n备份：{string.Join("; ", result.BackupPaths)}",
                "保存完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("保存角色文本失败：" + ex);
            MessageBox.Show(this, ex.Message, "保存角色文本失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void SaveRoleEditor()
    {
        if (_project == null || _currentRoleEditorData == null) return;

        _roleEditorGrid.EndEdit();
        if (_currentRoleEditorData.GetChanges() == null)
        {
            MessageBox.Show(this, "角色设定没有检测到改动。", "无需保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var preview = BuildChangePreview(_currentRoleEditorData, maxItems: 40);
        if (MessageBox.Show(this,
                $"即将保存角色设定到当前 MOD 项目。\r\n\r\n变更预览：\r\n{preview}\r\n\r\n保存前会自动备份 Data.e5/Ekd5.exe，保存后会重新读取校验。是否继续？",
                "确认保存角色设定",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            ValidateRoleEditorEquipmentValues(_currentRoleEditorData);
            var changedCells = GetChangedCellKeys(_currentRoleEditorData);
            var saves = SaveRoleEditorData(_project, _tables, _currentRoleEditorData);
            AcceptSavedDataTable(_currentRoleEditorData);
            RefreshRoleEditorCellsAfterEdit(changedCells);
            _roleEditorInfoBox.Text = BuildRoleEditorSummary(_currentRoleEditorData);
            var changedBytes = saves.Sum(x => x.ChangedBytes);
            System.Diagnostics.Debug.WriteLine($"已保存角色设定：保存表 {saves.Count} 个，变化字节 {changedBytes}");
            foreach (var save in saves)
            {
                System.Diagnostics.Debug.WriteLine($"角色设定备份：{save.BackupPath}");
            }
            SetStatus($"角色设定保存完成并已复读：变化 {changedBytes} 字节");
            MessageBox.Show(this,
                $"保存完成并已重新读取校验。\r\n保存表数量：{saves.Count}\r\n变化字节：{changedBytes}\r\n备份：{string.Join("; ", saves.Select(x => x.BackupPath))}",
                "保存完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("保存角色设定失败：" + ex);
            MessageBox.Show(this, ex.Message, "保存角色设定失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ValidateRoleTextCapacities(RoleCriticalQuoteMapping criticalMapping, RoleRetreatQuoteMapping retreatMapping)
    {
        _ = EncodingService.EncodeFixedString(_roleBiographyBox.Text, 200);
        for (var i = 0; i < criticalMapping.QuoteRows.Count && i < _roleCriticalQuoteBoxes.Length; i++)
        {
            _ = EncodingService.EncodeFixedString(_roleCriticalQuoteBoxes[i].Text, 200);
        }

        if (retreatMapping.QuoteRow != null)
        {
            _ = EncodingService.EncodeFixedString(_roleRetreatQuoteBox.Text, 200);
        }
    }

    private RoleTextSaveResult SaveRoleTextDetailsCore(DataRow? roleRow = null, RoleCriticalQuoteSelection? criticalSelection = null)
    {
        var result = new RoleTextSaveResult();
        if (_project != null && roleRow != null && criticalSelection is { } selection)
        {
            var assignmentResult = SaveRoleCriticalQuoteAssignment(roleRow, selection);
            result.AddRange(assignmentResult.TableSaves);
            if (assignmentResult.SpecialSlotsSave != null) result.Add(assignmentResult.SpecialSlotsSave);
        }

        if (_roleBiographyRead != null && SaveChangedTableAndVerify(_roleBiographyRead) is { } biographySave) result.Add(biographySave);
        if (_roleCriticalQuoteRead != null && SaveChangedTableAndVerify(_roleCriticalQuoteRead) is { } criticalSave) result.Add(criticalSave);
        if (_roleRetreatQuoteRead != null && SaveChangedTableAndVerify(_roleRetreatQuoteRead) is { } retreatSave) result.Add(retreatSave);
        return result;
    }

    private RoleCriticalAssignmentSaveResult SaveRoleCriticalQuoteAssignment(DataRow roleRow, RoleCriticalQuoteSelection selection)
    {
        var result = new RoleCriticalAssignmentSaveResult();
        if (_project == null) return result;

        var roleId = Convert.ToInt32(roleRow["ID"], CultureInfo.InvariantCulture);
        var specialRoleIds = _roleQuoteMappingService.ReadSpecialCriticalRoleIds(_project).ToList();
        if (specialRoleIds.Count != RoleQuoteMappingService.CriticalSpecialQuoteCount)
        {
            throw new InvalidOperationException("无法读取完整的 21 槽特殊暴击人物表，已取消保存。");
        }

        for (var i = 0; i < specialRoleIds.Count; i++)
        {
            if (specialRoleIds[i] == roleId)
            {
                specialRoleIds[i] = RoleQuoteMappingService.CriticalSpecialEmptyRoleId;
            }
        }

        if (selection.Mode == RoleCriticalQuoteMode.Special)
        {
            if (selection.Value < 0 || selection.Value >= RoleQuoteMappingService.CriticalSpecialQuoteCount)
            {
                throw new InvalidOperationException("特殊暴击槽超出 0..20。");
            }

            specialRoleIds[selection.Value] = roleId;
        }

        result.SpecialSlotsSave = _roleQuoteMappingService.SaveSpecialCriticalRoleIds(_project, specialRoleIds);

        if (selection.Mode == RoleCriticalQuoteMode.Generic)
        {
            result.TableSaves.AddRange(SaveRoleCriticalGenericTypeField(roleId, selection.Value));
            SetCurrentRoleEditorCriticalType(roleRow, selection.Value);
        }

        return result;
    }

    private IReadOnlyList<TableSaveResult> SaveRoleCriticalGenericTypeField(int roleId, int genericType)
    {
        if (_project == null) return Array.Empty<TableSaveResult>();
        if (genericType < 0 || genericType >= RoleQuoteMappingService.CriticalGenericTypeCount)
        {
            throw new InvalidOperationException($"普通暴击台词类型必须在 0..{RoleQuoteMappingService.CriticalGenericTypeCount - 1}。");
        }

        var personTable = FindTable(_tables, "6.5-0 人物");
        var personRead = _tableReader.Read(_project, personTable, _tables);
        if (!personRead.Validation.IsUsable)
        {
            throw new InvalidOperationException("人物表不可读取，无法保存暴击台词普通类型字段。");
        }

        var personRow = FindRowById(personRead.Data, roleId);
        var oldValue = Convert.ToInt32(personRow["暴击台词"], CultureInfo.InvariantCulture);
        if (oldValue == genericType)
        {
            return Array.Empty<TableSaveResult>();
        }

        personRow["暴击台词"] = genericType;
        return SaveChangedTableAndVerify(personRead) is { } save
            ? new[] { save }
            : Array.Empty<TableSaveResult>();
    }

    private void SetCurrentRoleEditorCriticalType(DataRow roleRow, int genericType)
    {
        if (!roleRow.Table.Columns.Contains("暴击台词")) return;
        roleRow["暴击台词"] = genericType;
        if (_roleEditorGrid.CurrentRow != null)
        {
            foreach (DataGridViewCell cell in _roleEditorGrid.CurrentRow.Cells)
            {
                if (!string.Equals(_roleEditorGrid.Columns[cell.ColumnIndex].DataPropertyName, "暴击台词", StringComparison.Ordinal)) continue;
                cell.Value = genericType;
                break;
            }
        }
    }

    private IReadOnlyList<TableSaveResult> SaveRoleEditorData(CczProject project, IReadOnlyList<HexTableDefinition> tables, DataTable roleData)
    {
        var personTable = FindTable(tables, "6.5-0 人物");
        var rTable = FindTable(tables, "6.5-0-4 R形象");
        var sTable = FindTable(tables, "6.5-0-5 S形象");
        var personRead = _tableReader.Read(project, personTable, tables);
        var rRead = _tableReader.Read(project, rTable, tables);
        var sRead = _tableReader.Read(project, sTable, tables);

        foreach (DataRow roleRow in roleData.Rows)
        {
            if (roleRow.RowState != DataRowState.Modified) continue;
            var id = Convert.ToInt32(roleRow["ID"], CultureInfo.InvariantCulture);
            var personRow = FindRowById(personRead.Data, id);
            var rRow = FindRowById(rRead.Data, id);
            var sRow = FindRowById(sRead.Data, id);

            foreach (DataColumn column in personRead.Data.Columns)
            {
                if (column.ColumnName == "ID" || !roleData.Columns.Contains(column.ColumnName)) continue;
                if (!IsRoleColumnChanged(roleRow, column.ColumnName)) continue;
                personRow[column.ColumnName] = roleRow[column.ColumnName, DataRowVersion.Current];
            }

            if (IsRoleColumnChanged(roleRow, "R形象编号"))
            {
                rRow["R形象编号"] = roleRow["R形象编号", DataRowVersion.Current];
            }
            if (IsRoleColumnChanged(roleRow, "S形象编号"))
            {
                sRow["S形象编号"] = roleRow["S形象编号", DataRowVersion.Current];
            }
        }

        var saves = new List<TableSaveResult>();
        if (SaveChangedTableAndVerify(personRead) is { } personSave) saves.Add(personSave);
        if (SaveChangedTableAndVerify(rRead) is { } rSave) saves.Add(rSave);
        if (SaveChangedTableAndVerify(sRead) is { } sSave) saves.Add(sSave);
        return saves;
    }

    private static DataRow FindRowById(DataTable table, int id)
    {
        var row = TryFindRowById(table, id);
        if (row != null) return row;
        throw new InvalidOperationException($"没有找到 ID={id} 的数据行。");
    }

    private static DataRow? TryFindRowById(DataTable table, int id)
    {
        foreach (DataRow row in table.Rows)
        {
            if (Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) == id) return row;
        }
        return null;
    }

    private static bool IsRoleColumnChanged(DataRow row, string columnName)
    {
        if (!row.Table.Columns.Contains(columnName)) return false;
        if (!row.HasVersion(DataRowVersion.Original)) return true;
        var original = row[columnName, DataRowVersion.Original];
        var current = row[columnName, DataRowVersion.Current];
        return !Equals(Convert.ToString(original, CultureInfo.InvariantCulture), Convert.ToString(current, CultureInfo.InvariantCulture));
    }

    private HexTableDefinition FindTable(IReadOnlyList<HexTableDefinition> tables, string tableName)
    {
        if (_project != null)
        {
            return HexTableNameResolver.ResolveForProject(_project, tables, tableName);
        }

        return HexTableNameResolver.Resolve(tables, tableName);
    }

    private void OpenJobEditor()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SelectTabPageByText("兵种设定");
        LoadJobEditor();
    }

    private void OpenJobEffectEditor()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SelectTabPageByText("兵种设定");
        if (_jobEditorTabs != null)
        {
            foreach (var page in _jobEditorTabs.TabPages.Cast<TabPage>())
            {
                if (!string.Equals(page.Text, "兵种特效", StringComparison.Ordinal)) continue;
                _jobEditorTabs.SelectedTab = page;
                break;
            }
        }
        else
        {
            SelectTabPageByText("兵种特效");
        }

        LoadJobEffectEditor();
    }

    private void OpenRolePersonalEffectEditor()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_tables.Count == 0)
        {
            ReloadCurrentProject();
            if (_tables.Count == 0) return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var data = BuildExclusiveSetScenarioEditorData(_project, _tables);
            Cursor = Cursors.Default;
            ShowExclusiveSetScenarioEditorDialog(data);
        }
        catch (Exception ex)
        {
            Cursor = Cursors.Default;
            System.Diagnostics.Debug.WriteLine("读取个人专属/套装失败：" + ex);
            MessageBox.Show(this, ex.Message, "读取个人专属/套装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenRolePersonalEffectTableEditor()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_tables.Count == 0)
        {
            ReloadCurrentProject();
            if (_tables.Count == 0) return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var data = BuildRolePersonalEffectEditorData(_project, _tables);
            Cursor = Cursors.Default;
            ShowRolePersonalEffectEditorDialog(data);
        }
        catch (Exception ex)
        {
            Cursor = Cursors.Default;
            System.Diagnostics.Debug.WriteLine("读取个人特效 EXE 表失败：" + ex);
            MessageBox.Show(this, ex.Message, "读取个人特效 EXE 表失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private DataTable BuildExclusiveSetScenarioEditorData(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe();
        if (dictionary == null)
        {
            throw new InvalidOperationException($"未找到 CczString.ini：{ProjectDetector.FindSceneDictionaryPath(project)}");
        }

        _currentSceneStringDocument ??= dictionary;
        _jobEffectNameTable = FindTable(tables, "6.5-7 兵种特效");
        _rolePersonalEffectNames = ReadJobEffectNames(project, _jobEffectNameTable);
        _rolePersonalEffectPersonNames = BuildIdNameLookup(project, tables, "6.5-0 人物");
        _rolePersonalEffectItemNames = BuildItemNameLookup(project, tables);
        _currentExclusiveSetScenarioRead = _exclusiveSetScenarioService.Read(
            project,
            dictionary,
            _rolePersonalEffectNames,
            _rolePersonalEffectPersonNames,
            _rolePersonalEffectItemNames);

        var output = new DataTable("个人专属/套装");
        output.Columns.Add("EntryId", typeof(string));
        output.Columns.Add("类型值", typeof(int));
        output.Columns.Add("类型", typeof(string));
        output.Columns.Add("位置", typeof(int));
        output.Columns.Add("特效编号", typeof(int));
        output.Columns.Add("特效", typeof(string));
        output.Columns.Add("武器", typeof(int));
        output.Columns.Add("防具", typeof(int));
        output.Columns.Add("辅助", typeof(int));
        output.Columns.Add("装备", typeof(string));
        output.Columns.Add("角色编号", typeof(int));
        output.Columns.Add("角色", typeof(string));
        output.Columns.Add("特效值", typeof(int));
        output.Columns.Add("出处", typeof(string));
        output.Columns.Add("备注", typeof(string));
        output.Columns.Add("RelativePath", typeof(string));
        output.Columns.Add("SceneIndex", typeof(int));
        output.Columns.Add("SectionIndex", typeof(int));
        output.Columns.Add("CommandIndex", typeof(int));
        output.Columns.Add("CommandOrdinal", typeof(int));
        output.Columns.Add("FileOffset", typeof(int));
        output.Columns.Add("SourceTextHash", typeof(string));

        foreach (var entry in _currentExclusiveSetScenarioRead.Entries)
        {
            var row = output.NewRow();
            row["EntryId"] = entry.EntryId;
            row["类型值"] = (int)entry.Kind;
            row["类型"] = entry.KindText;
            row["位置"] = entry.Position;
            row["特效编号"] = entry.EffectId;
            row["特效"] = entry.EffectDisplay;
            row["武器"] = entry.WeaponId;
            row["防具"] = entry.ArmorId;
            row["辅助"] = entry.AccessoryId;
            row["装备"] = entry.EquipmentDisplay;
            row["角色编号"] = entry.PersonId;
            row["角色"] = entry.PersonDisplay;
            row["特效值"] = entry.EffectValue;
            row["出处"] = entry.SourceDisplay;
            row["备注"] = entry.Remarks;
            row["RelativePath"] = entry.RelativePath;
            row["SceneIndex"] = entry.SceneIndex;
            row["SectionIndex"] = entry.SectionIndex;
            row["CommandIndex"] = entry.CommandIndex;
            row["CommandOrdinal"] = entry.CommandOrdinal;
            row["FileOffset"] = entry.FileOffset;
            row["SourceTextHash"] = entry.SourceTextHash;
            output.Rows.Add(row);
        }

        output.AcceptChanges();
        return output;
    }

    private static bool IsExclusiveSetScenarioReadOnlyColumn(string columnName)
        => columnName is "EntryId"
            or "类型值"
            or "类型"
            or "特效"
            or "装备"
            or "角色"
            or "出处"
            or "RelativePath"
            or "SceneIndex"
            or "SectionIndex"
            or "CommandIndex"
            or "CommandOrdinal"
            or "FileOffset"
            or "SourceTextHash";

    private void RefreshExclusiveSetScenarioDerivedCells(DataRow row)
    {
        var effectId = Convert.ToInt32(row["特效编号"], CultureInfo.InvariantCulture);
        var weaponId = Convert.ToInt32(row["武器"], CultureInfo.InvariantCulture);
        var armorId = Convert.ToInt32(row["防具"], CultureInfo.InvariantCulture);
        var accessoryId = Convert.ToInt32(row["辅助"], CultureInfo.InvariantCulture);
        var personId = Convert.ToInt32(row["角色编号"], CultureInfo.InvariantCulture);
        var kind = Convert.ToInt32(row["类型值"], CultureInfo.InvariantCulture);

        row["特效"] = ExclusiveSetScenarioService.FormatEffect(effectId, _rolePersonalEffectNames);
        row["装备"] = ExclusiveSetScenarioService.FormatEquipment(weaponId, armorId, accessoryId, _rolePersonalEffectItemNames);
        row["角色"] = kind == 0
            ? ExclusiveSetScenarioService.FormatPerson(personId, _rolePersonalEffectPersonNames)
            : "255";
    }

    private void ShowExclusiveSetScenarioEditorDialog(DataTable data)
    {
        using var dialog = new Form
        {
            Text = "个人专属/套装",
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = true
        };
        ApplyAdaptiveDialogSizing(dialog, new Size(1280, 760), new Size(900, 540));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        dialog.Controls.Add(layout);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        var saveButton = new Button { Text = "保存到剧本", AutoSize = true };
        var typeFilter = new ComboBox { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
        typeFilter.Items.AddRange(new object[] { "全部", "专属", "套装" });
        typeFilter.SelectedIndex = 0;
        var filterBox = new TextBox { Width = 240, PlaceholderText = "特效/角色/装备/出处/编号" };
        var filterButton = new Button { Text = "筛选", AutoSize = true };
        var clearButton = new Button { Text = "清除", AutoSize = true };
        var malformedButton = new Button { Text = "未解析", AutoSize = true };
        var closeButton = new Button { Text = "关闭", AutoSize = true };
        toolbar.Controls.AddRange(new Control[]
        {
            saveButton,
            new Label { Text = "类型：", AutoSize = true, Padding = new Padding(12, 7, 0, 0) },
            typeFilter,
            new Label { Text = "搜索：", AutoSize = true, Padding = new Padding(12, 7, 0, 0) },
            filterBox,
            filterButton,
            clearButton,
            malformedButton,
            closeButton
        });
        layout.Controls.Add(toolbar, 0, 0);

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            SelectionMode = DataGridViewSelectionMode.CellSelect
        };
        ConfigureExclusiveSetScenarioGrid(grid);
        grid.DataSource = data;
        layout.Controls.Add(grid, 0, 1);

        var infoBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Text = BuildExclusiveSetScenarioSummary(data)
        };
        layout.Controls.Add(infoBox, 0, 2);

        void ShowSelectedCell()
        {
            if (grid.CurrentCell == null) return;
            var columnName = grid.Columns[grid.CurrentCell.ColumnIndex].DataPropertyName;
            var row = grid.Rows[grid.CurrentCell.RowIndex];
            infoBox.Text =
                $"类型={row.Cells["类型"].Value}    位置={row.Cells["位置"].Value}    特效={row.Cells["特效"].Value}\r\n" +
                $"出处：{row.Cells["出处"].Value}    双击本行可跳转到对应剧本指令。\r\n" +
                $"字段：{columnName}    当前值：{grid.CurrentCell.Value}\r\n\r\n" +
                BuildExclusiveSetScenarioColumnAnnotation(columnName);
        }

        void RefreshRowStyle(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count) return;
            var row = TryGetDataRow(grid.Rows[rowIndex]);
            if (row == null) return;
            grid.Rows[rowIndex].DefaultCellStyle.BackColor = IsDataRowChanged(row) ? Color.LightCyan : Color.Empty;
        }

        void ApplyFilter()
        {
            var clauses = new List<string>();
            var selectedType = Convert.ToString(typeFilter.SelectedItem, CultureInfo.InvariantCulture) ?? "全部";
            if (selectedType is "专属" or "套装")
            {
                clauses.Add($"[类型] = '{selectedType}'");
            }

            var keyword = filterBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var escaped = EscapeDataViewLikeValue(keyword);
                var filters = data.Columns
                    .Cast<DataColumn>()
                    .Select(column => $"CONVERT([{column.ColumnName}], 'System.String') LIKE '*{escaped}*'");
                clauses.Add("(" + string.Join(" OR ", filters) + ")");
            }

            data.DefaultView.RowFilter = string.Join(" AND ", clauses);
            SetStatus($"个人专属/套装筛选：{data.DefaultView.Count}/{data.Rows.Count}");
        }

        grid.SelectionChanged += (_, _) => ShowSelectedCell();
        grid.CellEndEdit += (_, e) =>
        {
            if (e.RowIndex >= 0 && TryGetDataRow(grid.Rows[e.RowIndex]) is { } row)
            {
                RefreshExclusiveSetScenarioDerivedCells(row);
            }
            RefreshRowStyle(e.RowIndex);
            ShowSelectedCell();
        };
        grid.CellValidating += (_, e) => ValidateExclusiveSetScenarioCell(grid, infoBox, e);
        grid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
            {
                infoBox.Text = $"个人专属/套装单元格显示失败：{e.Exception?.Message}";
            }
        };
        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (TryGetDataRow(grid.Rows[e.RowIndex]) is not { } row) return;
            var target = row;
            dialog.BeginInvoke(new Action(async () =>
            {
                dialog.Close();
                await JumpToExclusiveSetScenarioCommandAsync(target);
            }));
        };
        filterButton.Click += (_, _) => ApplyFilter();
        typeFilter.SelectedIndexChanged += (_, _) => ApplyFilter();
        filterBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            ApplyFilter();
            e.SuppressKeyPress = true;
        };
        clearButton.Click += (_, _) =>
        {
            filterBox.Clear();
            typeFilter.SelectedIndex = 0;
            data.DefaultView.RowFilter = string.Empty;
            SetStatus("个人专属/套装筛选已清除");
        };
        saveButton.Click += (_, _) => SaveExclusiveSetScenarioEditor(dialog, grid, data, infoBox);
        malformedButton.Click += (_, _) => ShowExclusiveSetScenarioMalformedDialog(dialog);
        closeButton.Click += (_, _) => dialog.Close();

        RefreshExclusiveSetScenarioRowStyles(grid);
        ShowSelectedCell();
        dialog.ShowDialog(this);
    }

    private void ConfigureExclusiveSetScenarioGrid(DataGridView grid)
    {
        grid.ReadOnly = false;
        grid.AutoGenerateColumns = false;
        grid.Columns.Clear();

        foreach (var columnName in GetExclusiveSetScenarioColumnOrder())
        {
            AddExclusiveSetScenarioGridColumn(grid, columnName);
        }
    }

    private void AddExclusiveSetScenarioGridColumn(DataGridView grid, string columnName)
    {
        DataGridViewColumn column = columnName is "武器" or "防具" or "辅助"
            ? new DataGridViewComboBoxColumn
            {
                FlatStyle = FlatStyle.Flat,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                ValueType = typeof(int),
                DisplayMember = "Text",
                ValueMember = "Id",
                DataSource = BuildExclusiveSetScenarioItemChoices()
            }
            : new DataGridViewTextBoxColumn();

        column.Name = columnName;
        column.DataPropertyName = columnName;
        column.ReadOnly = IsExclusiveSetScenarioReadOnlyColumn(columnName);
        column.HeaderText = BuildExclusiveSetScenarioColumnHeader(columnName);
        column.ToolTipText = BuildExclusiveSetScenarioColumnAnnotation(columnName);
        column.Visible = IsVisibleExclusiveSetScenarioColumn(columnName);
        column.Width = columnName switch
        {
            "类型" => 70,
            "位置" or "特效值" => 80,
            "特效" => 170,
            "武器" or "防具" or "辅助" => 180,
            "角色" => 140,
            "备注" => 260,
            _ => 100
        };
        if (column.ReadOnly) column.DefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);
        grid.Columns.Add(column);
    }

    private static IReadOnlyList<string> GetExclusiveSetScenarioColumnOrder() =>
    [
        "EntryId",
        "类型值",
        "类型",
        "位置",
        "特效编号",
        "特效",
        "武器",
        "防具",
        "辅助",
        "装备",
        "角色编号",
        "角色",
        "特效值",
        "出处",
        "备注",
        "RelativePath",
        "SceneIndex",
        "SectionIndex",
        "CommandIndex",
        "CommandOrdinal",
        "FileOffset",
        "SourceTextHash"
    ];

    private void ConfigureExclusiveSetScenarioItemComboColumn(DataGridView grid, string columnName)
    {
        if (!grid.Columns.Contains(columnName)) return;
        if (grid.Columns[columnName] is DataGridViewComboBoxColumn) return;

        var old = grid.Columns[columnName];
        var index = old.Index;
        var combo = new DataGridViewComboBoxColumn
        {
            Name = columnName,
            DataPropertyName = columnName,
            HeaderText = old.HeaderText,
            ToolTipText = old.ToolTipText,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            ValueType = typeof(int),
            DisplayMember = "Text",
            ValueMember = "Id"
        };
        combo.DataSource = BuildExclusiveSetScenarioItemChoices();
        grid.Columns.Remove(old);
        grid.Columns.Insert(index, combo);
    }

    private DataTable BuildExclusiveSetScenarioItemChoices()
    {
        var choices = new DataTable();
        choices.Columns.Add("Id", typeof(int));
        choices.Columns.Add("Text", typeof(string));

        for (var id = 0; id <= byte.MaxValue; id++)
        {
            var row = choices.NewRow();
            row["Id"] = id;
            row["Text"] = ExclusiveSetScenarioService.FormatItem(id, _rolePersonalEffectItemNames);
            choices.Rows.Add(row);
        }

        choices.AcceptChanges();
        return choices;
    }

    private static bool IsVisibleExclusiveSetScenarioColumn(string columnName)
        => columnName is "类型" or "位置" or "特效" or "武器" or "防具" or "辅助" or "角色" or "特效值" or "备注";

    private static string BuildExclusiveSetScenarioColumnHeader(string columnName)
    {
        return columnName switch
        {
            "类型" => "类型\n专属/套装",
            "位置" => "位置\n可编辑",
            "特效" => "特效\n编号+名称",
            "武器" or "防具" or "辅助" => columnName + "\n下拉选择",
            "角色" => "角色\n编号+名称",
            "特效值" => "特效值\n可编辑",
            "备注" => "备注\n保留写回",
            _ => columnName
        };
    }

    private static string BuildExclusiveSetScenarioColumnAnnotation(string columnName)
    {
        if (columnName == "类型") return "来自 72/10 文本第一行：0=专属，1=套装。";
        if (columnName == "位置") return "写回第二行第一个数值。";
        if (columnName == "特效") return "只读展示：特效编号:兵种特效名称，未命中显示未命名。";
        if (columnName is "武器" or "防具" or "辅助") return "物品 ID。空槽统一使用 255；专属行保存时三槽必须且只能有一个不是 255。";
        if (columnName == "角色") return "只读展示：专属为编号:角色名，套装为 255。";
        if (columnName == "特效值") return "写回第二行最后一个数值。";
        if (columnName == "备注") return "文本第三行及之后内容，保存时原样追加到机器数据之后。";
        return columnName;
    }

    private string BuildExclusiveSetScenarioSummary(DataTable data)
    {
        var personal = data.Rows.Cast<DataRow>().Count(row => Convert.ToInt32(row["类型值"], CultureInfo.InvariantCulture) == 0);
        var set = data.Rows.Count - personal;
        var malformed = _currentExclusiveSetScenarioRead?.MalformedEntries.Count ?? 0;
        var warnings = _currentExclusiveSetScenarioRead?.Warnings.Count ?? 0;
        return
            $"已从全剧本 72/10 读取个人专属/套装：总行 {data.Rows.Count}，专属 {personal}，套装 {set}。\r\n" +
            $"扫描剧本：{_currentExclusiveSetScenarioRead?.ScannedFileCount ?? 0} 个；命令总数：{_currentExclusiveSetScenarioRead?.TotalCommandCount ?? 0}；未解析 72/10：{malformed}；警告：{warnings}。\r\n" +
            "来源：RS/R_*.eex 与 RS/S_*.eex 的 0x72 信息传送子功能 10。保存只更新已有命令文本，不新增、不删除命令，不再写入 Ekd5.exe 的 6.5-7-3。";
    }

    private void RefreshExclusiveSetScenarioRowStyles(DataGridView grid)
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            var dataRow = TryGetDataRow(row);
            row.DefaultCellStyle.BackColor = dataRow != null && IsDataRowChanged(dataRow) ? Color.LightCyan : Color.Empty;
        }
    }

    private void ValidateExclusiveSetScenarioCell(DataGridView grid, TextBox infoBox, DataGridViewCellValidatingEventArgs e)
    {
        if (grid.ReadOnly || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var column = grid.Columns[e.ColumnIndex];
        if (column.ReadOnly) return;
        if (column is DataGridViewComboBoxColumn) return;
        var columnName = column.DataPropertyName;
        var value = Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture) ?? string.Empty;
        string? error = null;
        if (columnName is "武器" or "防具" or "辅助")
        {
            error = TryParseInteger(value, 0, byte.MaxValue, columnName, _currentPageHexButton.Checked);
        }
        else if (columnName is "位置" or "特效编号" or "角色编号" or "特效值")
        {
            error = TryParseInteger(value, 0, ushort.MaxValue, columnName, _currentPageHexButton.Checked);
        }

        grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = error ?? string.Empty;
        if (error == null) return;
        e.Cancel = true;
        infoBox.Text = error;
        SetStatus(error);
    }

    private void ShowExclusiveSetScenarioMalformedDialog(IWin32Window owner)
    {
        var malformed = _currentExclusiveSetScenarioRead?.MalformedEntries ?? Array.Empty<ExclusiveSetScenarioMalformedEntry>();
        using var dialog = new Form
        {
            Text = "未解析 72/10 命令",
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = true
        };
        ApplyAdaptiveDialogSizing(dialog, new Size(1000, 620), new Size(760, 460));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        dialog.Controls.Add(layout);

        var table = new DataTable("未解析");
        table.Columns.Add("出处", typeof(string));
        table.Columns.Add("原因", typeof(string));
        table.Columns.Add("原文", typeof(string));
        foreach (var entry in malformed)
        {
            var row = table.NewRow();
            row["出处"] = entry.SourceDisplay;
            row["原因"] = entry.Reason;
            row["原文"] = entry.SourceText;
            table.Rows.Add(row);
        }
        table.AcceptChanges();

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            DataSource = table,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        if (grid.Columns.Contains("出处")) grid.Columns["出处"]!.Width = 260;
        if (grid.Columns.Contains("原因")) grid.Columns["原因"]!.Width = 260;
        if (grid.Columns.Contains("原文")) grid.Columns["原文"]!.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells;
        layout.Controls.Add(grid, 0, 0);

        var closeButton = new Button { Text = "关闭", AutoSize = true, Anchor = AnchorStyles.Right };
        closeButton.Click += (_, _) => dialog.Close();
        layout.Controls.Add(closeButton, 0, 1);
        dialog.ShowDialog(owner);
    }

    private void SaveExclusiveSetScenarioEditor(Form owner, DataGridView grid, DataTable data, TextBox infoBox)
    {
        if (_project == null) return;
        grid.EndEdit();
        if (data.GetChanges() == null)
        {
            MessageBox.Show(owner, "个人专属/套装没有检测到改动。", "无需保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var updates = BuildExclusiveSetScenarioUpdates(data);
        if (updates.Count == 0)
        {
            MessageBox.Show(owner, "没有可写入的实际改动。", "无需保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var preview = BuildChangePreview(data, maxItems: 80);
        if (MessageBox.Show(owner,
                $"即将把个人专属/套装写回现有 72/10 剧本命令。\r\n\r\n变更预览：\r\n{preview}\r\n\r\n保存前会按出处与原始文本哈希复核，外部修改会拒绝覆盖。是否继续？",
                "确认保存个人专属/套装",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe();
            if (dictionary == null)
            {
                throw new InvalidOperationException($"未找到 CczString.ini：{ProjectDetector.FindSceneDictionaryPath(_project)}");
            }

            Cursor = Cursors.WaitCursor;
            var result = _exclusiveSetScenarioService.Save(_project, dictionary, updates);
            var reloaded = BuildExclusiveSetScenarioEditorData(_project, _tables);
            RunPreservingGridViewport(grid, () =>
            {
                SyncDataTableRowsByKey(data, reloaded, "EntryId");
                data.AcceptChanges();
                RefreshExclusiveSetScenarioRowStyles(grid);
            }, "EntryId");
            var changedBytes = result.Writes.Sum(x => x.ChangedBytes);
            infoBox.Text =
                $"保存完成并已重新读取校验。\r\n写回命令：{result.ChangedEntryCount}\r\n保存剧本：{result.Writes.Count}\r\n变化字节：{changedBytes}\r\n备份：{string.Join("; ", result.Writes.Select(x => x.BackupPath))}";
            SetStatus($"个人专属/套装保存完成：命令 {result.ChangedEntryCount}，变化 {changedBytes} 字节");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("保存个人专属/套装失败：" + ex);
            MessageBox.Show(owner, ex.Message, "保存个人专属/套装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private static IReadOnlyList<ExclusiveSetScenarioUpdate> BuildExclusiveSetScenarioUpdates(DataTable data)
    {
        var updates = new List<ExclusiveSetScenarioUpdate>();
        foreach (DataRow row in data.Rows)
        {
            if (row.RowState != DataRowState.Modified) continue;
            updates.Add(new ExclusiveSetScenarioUpdate
            {
                EntryId = Convert.ToString(row["EntryId"], CultureInfo.InvariantCulture) ?? string.Empty,
                Kind = Convert.ToInt32(row["类型值"], CultureInfo.InvariantCulture) == 1 ? ExclusiveSetDesignKind.Set : ExclusiveSetDesignKind.Personal,
                RelativePath = Convert.ToString(row["RelativePath"], CultureInfo.InvariantCulture) ?? string.Empty,
                SceneIndex = Convert.ToInt32(row["SceneIndex"], CultureInfo.InvariantCulture),
                SectionIndex = Convert.ToInt32(row["SectionIndex"], CultureInfo.InvariantCulture),
                CommandIndex = Convert.ToInt32(row["CommandIndex"], CultureInfo.InvariantCulture),
                CommandOrdinal = Convert.ToInt32(row["CommandOrdinal"], CultureInfo.InvariantCulture),
                FileOffset = Convert.ToInt32(row["FileOffset"], CultureInfo.InvariantCulture),
                OriginalTextHash = Convert.ToString(row["SourceTextHash"], CultureInfo.InvariantCulture) ?? string.Empty,
                Position = Convert.ToInt32(row["位置"], CultureInfo.InvariantCulture),
                EffectId = Convert.ToInt32(row["特效编号"], CultureInfo.InvariantCulture),
                PersonId = Convert.ToInt32(row["角色编号"], CultureInfo.InvariantCulture),
                WeaponId = Convert.ToInt32(row["武器"], CultureInfo.InvariantCulture),
                ArmorId = Convert.ToInt32(row["防具"], CultureInfo.InvariantCulture),
                AccessoryId = Convert.ToInt32(row["辅助"], CultureInfo.InvariantCulture),
                EffectValue = Convert.ToInt32(row["特效值"], CultureInfo.InvariantCulture),
                Remarks = Convert.ToString(row["备注"], CultureInfo.InvariantCulture) ?? string.Empty
            });
        }

        return updates;
    }

    private async Task JumpToExclusiveSetScenarioCommandAsync(DataRow row)
    {
        if (_project == null) return;
        var relativePath = Convert.ToString(row["RelativePath"], CultureInfo.InvariantCulture) ?? string.Empty;
        var fileName = Path.GetFileName(relativePath);
        var sceneIndex = Convert.ToInt32(row["SceneIndex"], CultureInfo.InvariantCulture);
        var sectionIndex = Convert.ToInt32(row["SectionIndex"], CultureInfo.InvariantCulture);
        var commandIndex = Convert.ToInt32(row["CommandIndex"], CultureInfo.InvariantCulture);
        var commandOrdinal = Convert.ToInt32(row["CommandOrdinal"], CultureInfo.InvariantCulture);
        var fileOffset = Convert.ToInt32(row["FileOffset"], CultureInfo.InvariantCulture);

        SelectTabPageByText("剧本编辑");
        if (!await EnsureScriptScenarioLoadedAsync(fileName))
        {
            return;
        }

        if (_currentLegacyScriptDocument == null)
        {
            MessageBox.Show(this, "Target script is not loaded as a legacy command tree: " + fileName, "Navigation failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var command = _currentLegacyScriptDocument.EnumerateCommands().FirstOrDefault(x =>
                          x.CommandOrdinal == commandOrdinal &&
                          x.SceneIndex == sceneIndex &&
                          x.SectionIndex == sectionIndex &&
                          x.CommandIndex == commandIndex &&
                          x.FileOffset == fileOffset)
                      ?? _currentLegacyScriptDocument.EnumerateCommands().FirstOrDefault(x =>
                          x.SceneIndex == sceneIndex &&
                          x.SectionIndex == sectionIndex &&
                          x.CommandIndex == commandIndex &&
                          x.FileOffset == fileOffset);
        if (command == null || !TrySelectLegacyScriptCommand(LegacyScriptEditorScope.Script, command))
        {
            MessageBox.Show(this, "Script opened, but the target command was not found.", "Navigation failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ShowSelectedLegacyScriptTreeNode(LegacyScriptEditorScope.Script);
        TrySelectLegacyScriptParameterRow(1);
        ShowSelectedLegacyScriptParameter();
        SetStatus($"Located exclusive set script command: {fileName} Scene {sceneIndex} / Section {sectionIndex} / Command {commandIndex}");
    }
    private DataTable BuildRolePersonalEffectEditorData(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        _jobEffectNameTable = FindTable(tables, "6.5-7 兵种特效");
        _rolePersonalEffectNames = ReadJobEffectNames(project, _jobEffectNameTable);
        _rolePersonalEffectRead = _tableReader.Read(project, FindTable(tables, "6.5-7-3 人物专属、套装专属"), tables);
        _rolePersonalEffectPersonNames = BuildIdNameLookup(project, tables, "6.5-0 人物");
        _rolePersonalEffectItemNames = BuildItemNameLookup(project, tables);
        if (!_rolePersonalEffectRead.Validation.IsUsable)
        {
            throw new InvalidOperationException("人物专属/套装专属表有不可读取项，请先查看数据表诊断。");
        }

        var output = new DataTable("个人专属");
        output.Columns.Add("ID", typeof(int));
        output.Columns.Add("名称", typeof(string));
        output.Columns.Add("武将1", typeof(int));
        output.Columns.Add("武将1名", typeof(string));
        output.Columns.Add("装备1", typeof(int));
        output.Columns.Add("装备1名", typeof(string));
        output.Columns.Add("特效值1", typeof(int));
        output.Columns.Add("武将2", typeof(int));
        output.Columns.Add("武将2名", typeof(string));
        output.Columns.Add("装备2", typeof(int));
        output.Columns.Add("装备2名", typeof(string));
        output.Columns.Add("特效值2", typeof(int));
        output.Columns.Add("装备3-1", typeof(int));
        output.Columns.Add("装备3-1名", typeof(string));
        output.Columns.Add("装备3-2", typeof(int));
        output.Columns.Add("装备3-2名", typeof(string));
        output.Columns.Add("装备3-3", typeof(int));
        output.Columns.Add("装备3-3名", typeof(string));
        output.Columns.Add("特效值3", typeof(int));
        output.Columns.Add("装备4-1", typeof(int));
        output.Columns.Add("装备4-1名", typeof(string));
        output.Columns.Add("装备4-2", typeof(int));
        output.Columns.Add("装备4-2名", typeof(string));
        output.Columns.Add("装备4-3", typeof(int));
        output.Columns.Add("装备4-3名", typeof(string));
        output.Columns.Add("特效值4", typeof(int));

        foreach (DataRow sourceRow in _rolePersonalEffectRead.Data.Rows)
        {
            var id = Convert.ToInt32(sourceRow["ID"], CultureInfo.InvariantCulture);
            var row = output.NewRow();
            row["ID"] = id;
            row["名称"] = BuildRolePersonalEffectName(id, sourceRow);
            foreach (var columnName in GetRolePersonalEffectRawColumns())
            {
                row[columnName] = Convert.ToInt32(sourceRow[columnName], CultureInfo.InvariantCulture);
            }
            RefreshRolePersonalEffectDerivedCells(row);
            output.Rows.Add(row);
        }

        output.AcceptChanges();
        foreach (DataColumn column in output.Columns)
        {
            column.ReadOnly =
                column.ColumnName is "ID" or "名称" ||
                column.ColumnName.EndsWith("名", StringComparison.Ordinal);
        }
        return output;
    }

    private IReadOnlyDictionary<int, string> BuildItemNameLookup(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var result = new Dictionary<int, string>();
        foreach (var tableName in new[] { "6.5-1 物品（0-103）", "6.5-2 物品（104-255）" })
        {
            var read = _tableReader.Read(project, FindTable(tables, tableName), tables);
            if (!read.Validation.IsUsable || !read.Data.Columns.Contains("名称")) continue;
            foreach (DataRow row in read.Data.Rows)
            {
                var id = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
                result[id] = Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }
        return result;
    }

    private string BuildRolePersonalEffectName(int id, DataRow sourceRow)
    {
        if (_rolePersonalEffectNames.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var tableName = sourceRow.Table.Columns.Contains("名称")
            ? Convert.ToString(sourceRow["名称"], CultureInfo.InvariantCulture)
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(tableName) && !tableName.StartsWith("#", StringComparison.Ordinal))
        {
            return tableName;
        }

        return $"#{id}";
    }

    private static string[] GetRolePersonalEffectRawColumns() =>
    [
        "武将1",
        "装备1",
        "特效值1",
        "武将2",
        "装备2",
        "特效值2",
        "装备3-1",
        "装备3-2",
        "装备3-3",
        "特效值3",
        "装备4-1",
        "装备4-2",
        "装备4-3",
        "特效值4"
    ];

    private void RefreshRolePersonalEffectDerivedCells(DataRow row)
    {
        row["武将1名"] = BuildRolePersonalEffectPersonName(Convert.ToInt32(row["武将1"], CultureInfo.InvariantCulture));
        row["武将2名"] = BuildRolePersonalEffectPersonName(Convert.ToInt32(row["武将2"], CultureInfo.InvariantCulture));
        foreach (var columnName in new[] { "装备1", "装备2", "装备3-1", "装备3-2", "装备3-3", "装备4-1", "装备4-2", "装备4-3" })
        {
            row[columnName + "名"] = BuildRolePersonalEffectItemName(Convert.ToInt32(row[columnName], CultureInfo.InvariantCulture));
        }
    }

    private string BuildRolePersonalEffectPersonName(int id)
    {
        if (id >= 1024) return "无/不限";
        return _rolePersonalEffectPersonNames.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : $"{id}：未找到人物名";
    }

    private string BuildRolePersonalEffectItemName(int id)
    {
        if (id >= 255) return "空/不限";
        return _rolePersonalEffectItemNames.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : $"{id}：未找到物品名";
    }

    private void ShowRolePersonalEffectEditorDialog(DataTable data)
    {
        using var dialog = new Form
        {
            Text = "个人专属",
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = true
        };
        ApplyAdaptiveDialogSizing(dialog, new Size(1180, 720), new Size(820, 520));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        dialog.Controls.Add(layout);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        var saveButton = new Button { Text = "保存个人专属", AutoSize = true };
        var filterBox = new TextBox { Width = 220, PlaceholderText = "特效名/武将/装备/编号" };
        var filterButton = new Button { Text = "筛选", AutoSize = true };
        var clearButton = new Button { Text = "清除", AutoSize = true };
        var closeButton = new Button { Text = "关闭", AutoSize = true };
        toolbar.Controls.AddRange(new Control[]
        {
            saveButton,
            new Label { Text = "搜索：", AutoSize = true, Padding = new Padding(12, 7, 0, 0) },
            filterBox,
            filterButton,
            clearButton,
            closeButton
        });
        layout.Controls.Add(toolbar, 0, 0);

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            DataSource = data,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            SelectionMode = DataGridViewSelectionMode.CellSelect
        };
        ConfigureRolePersonalEffectGrid(grid);
        layout.Controls.Add(grid, 0, 1);

        var infoBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Text = BuildRolePersonalEffectSummary(data)
        };
        layout.Controls.Add(infoBox, 0, 2);

        void ShowSelectedCell()
        {
            if (grid.CurrentCell == null) return;
            var columnName = grid.Columns[grid.CurrentCell.ColumnIndex].DataPropertyName;
            var row = grid.Rows[grid.CurrentCell.RowIndex];
            infoBox.Text =
                $"个人专属：ID={row.Cells["ID"].Value}    名称={row.Cells["名称"].Value}\r\n" +
                $"字段：{columnName}    当前值：{grid.CurrentCell.Value}\r\n\r\n" +
                BuildRolePersonalEffectColumnAnnotation(columnName);
        }

        void RefreshRowStyle(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count) return;
            var row = TryGetDataRow(grid.Rows[rowIndex]);
            if (row == null) return;
            grid.Rows[rowIndex].DefaultCellStyle.BackColor = IsDataRowChanged(row) ? Color.LightCyan : Color.Empty;
        }

        void ApplyFilter()
        {
            var keyword = filterBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(keyword))
            {
                data.DefaultView.RowFilter = string.Empty;
                SetStatus("个人专属筛选已清除");
                return;
            }

            var escaped = EscapeDataViewLikeValue(keyword);
            var filters = data.Columns
                .Cast<DataColumn>()
                .Select(column => $"CONVERT([{column.ColumnName}], 'System.String') LIKE '*{escaped}*'");
            data.DefaultView.RowFilter = string.Join(" OR ", filters);
            SetStatus($"个人专属筛选：{data.DefaultView.Count}/{data.Rows.Count}");
        }

        grid.SelectionChanged += (_, _) => ShowSelectedCell();
        grid.CellEndEdit += (_, e) =>
        {
            if (e.RowIndex >= 0 && TryGetDataRow(grid.Rows[e.RowIndex]) is { } row)
            {
                RefreshRolePersonalEffectDerivedCells(row);
            }
            RefreshRowStyle(e.RowIndex);
            ShowSelectedCell();
        };
        grid.CellValidating += (_, e) => ValidateRolePersonalEffectCell(grid, infoBox, e);
        filterButton.Click += (_, _) => ApplyFilter();
        filterBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            ApplyFilter();
            e.SuppressKeyPress = true;
        };
        clearButton.Click += (_, _) =>
        {
            filterBox.Clear();
            data.DefaultView.RowFilter = string.Empty;
            SetStatus("个人专属筛选已清除");
        };
        saveButton.Click += (_, _) => SaveRolePersonalEffectEditor(dialog, grid, data, infoBox);
        closeButton.Click += (_, _) => dialog.Close();

        RefreshRolePersonalEffectRowStyles(grid);
        ShowSelectedCell();
        dialog.ShowDialog(this);
    }

    private void ConfigureRolePersonalEffectGrid(DataGridView grid)
    {
        grid.ReadOnly = false;
        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.ReadOnly =
                column.DataPropertyName is "ID" or "名称" ||
                column.DataPropertyName.EndsWith("名", StringComparison.Ordinal);
            column.HeaderText = BuildRolePersonalEffectColumnHeader(column.DataPropertyName);
            column.ToolTipText = BuildRolePersonalEffectColumnAnnotation(column.DataPropertyName);
            if (column.ReadOnly) column.DefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);
            column.Width = column.DataPropertyName switch
            {
                "ID" => 60,
                "名称" => 150,
                _ when column.DataPropertyName.EndsWith("名", StringComparison.Ordinal) => 150,
                _ => 86
            };
        }
    }

    private static string BuildRolePersonalEffectColumnHeader(string columnName)
    {
        return columnName switch
        {
            "ID" => "ID\n特效编号",
            "名称" => "名称\n只读",
            "武将1" or "武将2" => columnName + "\n人物ID",
            "装备1" or "装备2" or "装备3-1" or "装备3-2" or "装备3-3" or "装备4-1" or "装备4-2" or "装备4-3" => columnName + "\n物品ID",
            "特效值1" or "特效值2" or "特效值3" or "特效值4" => columnName + "\n1B",
            _ => columnName
        };
    }

    private static string BuildRolePersonalEffectColumnAnnotation(string columnName)
    {
        if (columnName == "ID") return "个人专属/套装专属编号。名称来自 6.5-7 兵种特效名称区，绑定写入 6.5-7-3。";
        if (columnName == "名称") return "特效名称只读显示，读取方式与兵种特效一致，来自 6.5-7 兵种特效原始名称区。";
        if (columnName is "武将1" or "武将2") return columnName + "：人物专属槽的武将 ID，写入 6.5-7-3。0..1023 通常对应人物 ID，1024 可作为无/不限候选。";
        if (columnName.EndsWith("武将1名", StringComparison.Ordinal) || columnName.EndsWith("武将2名", StringComparison.Ordinal)) return "根据人物表自动解析出的武将名称，只读显示。";
        if (columnName.StartsWith("装备", StringComparison.Ordinal) && !columnName.EndsWith("名", StringComparison.Ordinal)) return columnName + "：装备/套装物品 ID，写入 6.5-7-3。255 常作为空/不限候选。";
        if (columnName.EndsWith("名", StringComparison.Ordinal)) return "根据物品表自动解析出的装备名称，只读显示。";
        if (columnName.StartsWith("特效值", StringComparison.Ordinal)) return columnName + "：专属/套装特效参数值，写入 6.5-7-3。具体含义随特效而变，修改后应实机验证。";
        return columnName;
    }

    private static string BuildRolePersonalEffectSummary(DataTable data)
    {
        var named = data.Rows.Cast<DataRow>().Count(row => !string.IsNullOrWhiteSpace(Convert.ToString(row["名称"], CultureInfo.InvariantCulture)) && !Convert.ToString(row["名称"], CultureInfo.InvariantCulture)!.StartsWith("#", StringComparison.Ordinal));
        return
            $"个人专属已读取：总行 {data.Rows.Count}，可识别名称 {named}。\r\n" +
            "来源表：6.5-7 兵种特效（名称只读显示）、6.5-7-3 人物专属、套装专属。\r\n" +
            "保存会写回 Ekd5.exe，保存前自动备份，保存后重新读取校验。";
    }

    private void RefreshRolePersonalEffectRowStyles(DataGridView grid)
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            var dataRow = TryGetDataRow(row);
            row.DefaultCellStyle.BackColor = dataRow != null && IsDataRowChanged(dataRow) ? Color.LightCyan : Color.Empty;
        }
    }

    private void ValidateRolePersonalEffectCell(DataGridView grid, TextBox infoBox, DataGridViewCellValidatingEventArgs e)
    {
        if (grid.ReadOnly || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var column = grid.Columns[e.ColumnIndex];
        if (column.ReadOnly) return;
        var columnName = column.DataPropertyName;
        var value = Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture) ?? string.Empty;
        string? error = null;
        if (columnName is "武将1" or "武将2")
        {
            error = TryParseInteger(value, 0, ushort.MaxValue, columnName, _currentPageHexButton.Checked);
        }
        else if (columnName.StartsWith("装备", StringComparison.Ordinal) || columnName.StartsWith("特效值", StringComparison.Ordinal))
        {
            error = TryParseInteger(value, 0, byte.MaxValue, columnName, _currentPageHexButton.Checked);
        }

        grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = error ?? string.Empty;
        if (error == null) return;
        e.Cancel = true;
        infoBox.Text = error;
        SetStatus(error);
    }

    private void SaveRolePersonalEffectEditor(Form owner, DataGridView grid, DataTable data, TextBox infoBox)
    {
        if (_project == null || _rolePersonalEffectRead == null) return;
        grid.EndEdit();
        if (data.GetChanges() == null)
        {
            MessageBox.Show(owner, "个人专属没有检测到改动。", "无需保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var preview = BuildChangePreview(data, maxItems: 80);
        if (MessageBox.Show(owner,
                $"即将保存个人专属/套装专属到当前 MOD 项目。\r\n\r\n变更预览：\r\n{preview}\r\n\r\n保存前会自动备份 Ekd5.exe，保存后会重新读取校验。是否继续？",
                "确认保存个人专属",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var saves = SaveRolePersonalEffectEditorData(_project, data);
            data.AcceptChanges();
            RefreshRolePersonalEffectRowStyles(grid);
            var changedBytes = saves.Sum(x => x.ChangedBytes);
            infoBox.Text =
                $"保存完成并已重新读取校验。\r\n保存表数量：{saves.Count}\r\n变化字节：{changedBytes}\r\n备份：{string.Join("; ", saves.Select(x => x.BackupPath))}";
            System.Diagnostics.Debug.WriteLine($"已保存个人专属：保存表 {saves.Count} 个，变化字节 {changedBytes}");
            foreach (var save in saves) System.Diagnostics.Debug.WriteLine("个人专属备份：" + save.BackupPath);
            SetStatus($"个人专属保存完成并已复读：变化 {changedBytes} 字节");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("保存个人专属失败：" + ex);
            MessageBox.Show(owner, ex.Message, "保存个人专属失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private IReadOnlyList<TableSaveResult> SaveRolePersonalEffectEditorData(CczProject project, DataTable data)
    {
        if (_rolePersonalEffectRead == null) return Array.Empty<TableSaveResult>();
        foreach (DataRow effectRow in data.Rows)
        {
            if (effectRow.RowState != DataRowState.Modified) continue;
            var id = Convert.ToInt32(effectRow["ID"], CultureInfo.InvariantCulture);
            var targetRow = FindRowById(_rolePersonalEffectRead.Data, id);
            foreach (var columnName in GetRolePersonalEffectRawColumns())
            {
                if (IsRoleColumnChanged(effectRow, columnName))
                {
                    targetRow[columnName] = effectRow[columnName, DataRowVersion.Current];
                }
            }
        }

        var saves = new List<TableSaveResult>();
        if (_rolePersonalEffectRead.Data.GetChanges() != null)
        {
            if (SaveChangedTableAndVerify(_rolePersonalEffectRead) is { } save) saves.Add(save);
        }
        return saves;
    }

    private void LoadJobEditor()
    {
        if (_project == null) return;
        CommitJobDescriptionBoxEdit();
        CommitJobEquipmentEditorChanges();
        if (_tables.Count == 0)
        {
            ReloadCurrentProject();
            if (_tables.Count == 0) return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            ClearJobDescriptionBox("读取兵种后，在此编辑当前兵种介绍。");
            _jobEquipmentEditorBoundRow = null;
            HideJobEquipmentEditor();
            _currentJobEditorData = BuildJobEditorData(_project, _tables);
            _jobEditorGrid.DataSource = _currentJobEditorData;
            ConfigureJobEditorGrid();
            ResetJobEditorHistory();
            _saveJobEditorButton.Enabled = true;
            _editAccessoryJobGroupsButton.Enabled = _currentAccessoryJobGroupProfile != null;
            _replaceJobSImageButton.Enabled = true;
            _batchReplaceJobSImageButton.Enabled = true;
            _exportJobSImageBmpButton.Enabled = true;
            _exportJobEditorCsvButton.Enabled = true;
            _importJobEditorCsvButton.Enabled = true;
            _jobEditorInfoBox.Text = BuildJobEditorSummary(_currentJobEditorData);
            ShowSelectedJobEditorCell();
            SetStatus($"兵种设定读取完成：{_currentJobEditorData.Rows.Count} 行");
        }
        catch (Exception ex)
        {
            _jobEditorInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("读取兵种设定失败：" + ex);
            MessageBox.Show(this, ex.Message, "读取兵种设定失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private DataTable BuildJobEditorData(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        _jobNameRead = _tableReader.Read(project, FindTable(tables, "6.5-4 详细兵种"), tables);
        _jobDescriptionRead = _tableReader.Read(project, FindTable(tables, "6.5-4-1 兵种说明"), tables);
        _jobGrowthRead = _tableReader.Read(project, FindTable(tables, "6.5-4-2 兵种成长"), tables);
        _jobPierceRead = _tableReader.Read(project, FindTable(tables, "6.5-4-3 兵种穿透"), tables);
        if (!_jobNameRead.Validation.IsUsable || !_jobDescriptionRead.Validation.IsUsable || !_jobGrowthRead.Validation.IsUsable || !_jobPierceRead.Validation.IsUsable)
        {
            throw new InvalidOperationException("兵种名称/说明/成长/穿透表有不可读取项，请先查看数据表诊断。");
        }

        _currentEquipmentTypeProfile = _equipmentTypeProfileService.Build(project, tables, ResolveJobEquipmentStorageColumns());
        _jobEquipmentPermissionSlots = _currentEquipmentTypeProfile.JobPermissionSlots;
        _jobSeriesNames = BuildIdNameLookup(project, tables, "6.5-3 兵种系");
        _currentAccessoryJobGroupProfile = _accessoryJobGroupService.Read(project, tables);

        var output = new DataTable("兵种设定");
        output.Columns.Add("ID", typeof(int));
        output.Columns.Add("名称", typeof(string));
        foreach (DataColumn column in _jobGrowthRead.Data.Columns)
        {
            if (column.ColumnName is "ID" or "名称") continue;
            output.Columns.Add(column.ColumnName, column.DataType);
        }
        output.Columns.Add("穿透", typeof(int));
        output.Columns.Add(JobEquipmentSummaryColumn, typeof(string));
        output.Columns.Add("介绍", typeof(string));

        var count = Math.Min(_jobNameRead.Data.Rows.Count, Math.Min(_jobDescriptionRead.Data.Rows.Count, Math.Min(_jobGrowthRead.Data.Rows.Count, _jobPierceRead.Data.Rows.Count)));
        for (var i = 0; i < count; i++)
        {
            var row = output.NewRow();
            row["ID"] = Convert.ToInt32(_jobNameRead.Data.Rows[i]["ID"], CultureInfo.InvariantCulture);
            row["名称"] = Convert.ToString(_jobNameRead.Data.Rows[i]["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
            foreach (DataColumn column in _jobGrowthRead.Data.Columns)
            {
                if (column.ColumnName is "ID" or "名称") continue;
                row[column.ColumnName] = _jobGrowthRead.Data.Rows[i][column.ColumnName];
            }
            row["穿透"] = _jobPierceRead.Data.Rows[i]["穿透"];
            row[JobEquipmentSummaryColumn] = BuildJobEquipmentSummary(row);
            row["介绍"] = Convert.ToString(_jobDescriptionRead.Data.Rows[i]["介绍"], CultureInfo.InvariantCulture) ?? string.Empty;
            output.Rows.Add(row);
        }

        output.AcceptChanges();
        output.Columns["ID"]!.ReadOnly = true;
        return output;
    }

    private void ConfigureJobEditorGrid()
    {
        if (_currentJobEditorData == null) return;
        _jobEditorGrid.ReadOnly = false;
        ConfigureJobAreaComboColumn("攻击范围", BuildJobAreaComboItems("攻击范围"));
        ConfigureJobAreaComboColumn("穿透", BuildJobAreaComboItems("穿透"));
        foreach (DataGridViewColumn column in _jobEditorGrid.Columns)
        {
            column.ReadOnly = column.DataPropertyName == "ID" || column.DataPropertyName == JobEquipmentSummaryColumn;
            column.ToolTipText = BuildJobColumnAnnotation(column.DataPropertyName);
            column.HeaderText = column.DataPropertyName switch
            {
                "ID" => "ID\n兵种编号",
                JobEquipmentSummaryColumn => "可装备类别\n单击右侧编辑",
                "介绍" => "介绍\n兵种说明",
                _ => BuildJobColumnHeader(column.DataPropertyName)
            };
            if (column.DataPropertyName == "介绍") column.Width = 240;
            if (column.DataPropertyName == JobEquipmentSummaryColumn) column.Width = 260;
            if (column.DataPropertyName == "介绍") column.Visible = false;
            if (IsJobEquipmentCategoryColumn(column.DataPropertyName)) column.Visible = false;
            if (column.ReadOnly) column.DefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);
        }

        RefreshJobEditorRowStyles();
    }

    private void ConfigureJobAreaComboColumn(string columnName, IReadOnlyList<JobAreaComboItem> items)
    {
        if (_currentJobEditorData == null || !_jobEditorGrid.Columns.Contains(columnName)) return;
        var existing = _jobEditorGrid.Columns[columnName];
        if (existing is DataGridViewComboBoxColumn comboColumn)
        {
            comboColumn.DataSource = items;
            return;
        }

        var index = existing.Index;
        var width = existing.Width;
        var readOnly = existing.ReadOnly;
        var visible = existing.Visible;
        _jobEditorGrid.Columns.Remove(existing);

        var combo = new DataGridViewComboBoxColumn
        {
            Name = columnName,
            DataPropertyName = columnName,
            HeaderText = BuildJobColumnHeader(columnName),
            ToolTipText = BuildJobColumnAnnotation(columnName),
            DataSource = items,
            ValueMember = nameof(JobAreaComboItem.Value),
            DisplayMember = nameof(JobAreaComboItem.Text),
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            ReadOnly = readOnly,
            Visible = visible,
            Width = Math.Max(width, 140)
        };
        _jobEditorGrid.Columns.Insert(index, combo);
    }

    private IReadOnlyList<JobAreaComboItem> BuildJobAreaComboItems(string columnName)
    {
        var known = columnName switch
        {
            "攻击范围" => JobAttackRangeNames,
            "穿透" => JobPierceRangeNames,
            _ => new Dictionary<int, string>()
        };

        var values = known.Keys
            .Concat(_currentJobEditorData?.Rows.Cast<DataRow>()
                .Where(row => row.Table.Columns.Contains(columnName) && row.RowState != DataRowState.Deleted)
                .Select(row => Convert.ToInt32(row[columnName], CultureInfo.InvariantCulture)) ?? Enumerable.Empty<int>())
            .Distinct()
            .OrderBy(static value => value)
            .Select(value => new JobAreaComboItem(value, FormatJobAreaComboText(value, known, columnName)))
            .ToList();

        return values;
    }

    private static string FormatJobAreaComboText(int value, IReadOnlyDictionary<int, string> known, string columnName)
    {
        if (known.TryGetValue(value, out var text))
        {
            return $"{value}：{text}";
        }

        var fallback = columnName == "穿透" ? "扩展穿透/需实测" : "扩展范围/需实测";
        return $"{value}：{fallback}";
    }

    private string BuildJobColumnHeader(string columnName)
    {
        if (_jobGrowthRead?.Table.Fields.FirstOrDefault(f => f.ColumnName == columnName) is { } growthField)
        {
            return columnName + "\n" + _fieldAnnotationService.BuildShortFieldAnnotation(_jobGrowthRead.Table, growthField);
        }
        if (_jobNameRead?.Table.Fields.FirstOrDefault(f => f.ColumnName == columnName) is { } nameField)
        {
            return columnName + "\n" + _fieldAnnotationService.BuildShortFieldAnnotation(_jobNameRead.Table, nameField);
        }
        if (_jobPierceRead?.Table.Fields.FirstOrDefault(f => f.ColumnName == columnName) is { } pierceField)
        {
            return columnName + "\n" + _fieldAnnotationService.BuildShortFieldAnnotation(_jobPierceRead.Table, pierceField);
        }
        return columnName;
    }

    private string BuildJobColumnAnnotation(string columnName)
    {
        if (columnName == "ID") return "详细兵种编号，用于人物职业、兵种说明、成长、穿透等表按 ID 对齐。";
        if (columnName == "介绍") return "兵种说明文本，写入 Imsg.e5；固定 200 字节 GBK 容量。";
        if (columnName == JobEquipmentSummaryColumn) return "当前详细兵种可装备的装备类别摘要。单击该列后在右侧复选框编辑；切换到其他单元格或保存时同步摘要。底层写入 6.5-4-2 兵种成长行内的 26 个装备类别字节。";
        if (columnName == "攻击范围") return "兵种普通攻击距离/目标选择范围，写入 6.5-4-2 兵种成长；选择该字段会按字段值+1 从 E5\\Hitarea.e5 预览范围图。下拉说明来自旧资料和当前资源预览口径，扩展值需进战场实测。";
        if (columnName == "穿透") return "兵种普通攻击穿透模板，写入 6.5-4-3 兵种穿透；选择该字段会按字段值+1 从 E5\\Effarea.e5 预览穿透图。下拉说明来自旧资料和当前资源预览口径，0 通常是不穿透，扩展值需进战场实测。";
        if (FindJobEquipmentSlot(columnName) is { } slot)
        {
            var sample = string.IsNullOrWhiteSpace(slot.SampleText) ? "暂无当前项目样例" : $"样例：{slot.SampleText}";
            return
                $"装备许可槽 {slot.SlotIndex:D2}：{slot.SummaryName}。\r\n" +
                $"原始存储列：{slot.StorageColumnName}；对应类型码：{slot.TypeId}；{sample}。\r\n" +
                $"名称来源：{slot.SourceDisplayName}。0 表示该详细兵种不能装备，非 0 表示允许装备。建议单击“可装备类别”列后在右侧复选框编辑。";
        }
        if (_jobGrowthRead?.Table.Fields.FirstOrDefault(f => f.ColumnName == columnName) is { } growthField)
        {
            return _fieldAnnotationService.BuildFieldAnnotation(_jobGrowthRead.Table, growthField);
        }
        if (_jobNameRead?.Table.Fields.FirstOrDefault(f => f.ColumnName == columnName) is { } nameField)
        {
            return _fieldAnnotationService.BuildFieldAnnotation(_jobNameRead.Table, nameField);
        }
        if (_jobPierceRead?.Table.Fields.FirstOrDefault(f => f.ColumnName == columnName) is { } pierceField)
        {
            return _fieldAnnotationService.BuildFieldAnnotation(_jobPierceRead.Table, pierceField);
        }
        return columnName;
    }

    private bool IsJobEquipmentCategoryColumn(string columnName)
        => GetJobEquipmentPermissionSlots().Any(slot => string.Equals(slot.StorageColumnName, columnName, StringComparison.Ordinal)) ||
           JobEquipmentCategoryColumns.Contains(columnName, StringComparer.Ordinal);

    private IReadOnlyList<string> ResolveJobEquipmentStorageColumns()
    {
        if (_jobGrowthRead?.Data != null)
        {
            var direct = JobEquipmentCategoryColumns
                .Where(column => _jobGrowthRead.Data.Columns.Contains(column))
                .ToArray();
            if (direct.Length == JobEquipmentCategoryColumns.Length) return direct;

            var growthColumns = new HashSet<string>(StringComparer.Ordinal)
            {
                "移动力",
                "攻击范围",
                "攻击",
                "防御",
                "精神",
                "爆发",
                "士气",
                "HP",
                "MP"
            };
            var candidates = _jobGrowthRead.Data.Columns
                .Cast<DataColumn>()
                .Select(column => column.ColumnName)
                .Where(column => column is not "ID" and not "名称" && !growthColumns.Contains(column))
                .ToArray();
            if (candidates.Length >= ProjectEquipmentTypeProfileService.JobPermissionSlotCount)
            {
                return candidates.TakeLast(ProjectEquipmentTypeProfileService.JobPermissionSlotCount).ToArray();
            }
        }

        return JobEquipmentCategoryColumns;
    }

    private IReadOnlyList<JobEquipmentPermissionSlotDefinition> GetJobEquipmentPermissionSlots()
    {
        if (_jobEquipmentPermissionSlots.Count > 0) return _jobEquipmentPermissionSlots;
        return BuildFallbackJobEquipmentPermissionSlots();
    }

    private static IReadOnlyList<JobEquipmentPermissionSlotDefinition> BuildFallbackJobEquipmentPermissionSlots()
    {
        return JobEquipmentCategoryColumns
            .Select((column, index) =>
            {
                var displayName = ItemTypeCatalogService.TryGetEntry(index, out var entry)
                    ? entry.Name
                    : column;
                return new JobEquipmentPermissionSlotDefinition(
                    index,
                    column,
                    index,
                    displayName,
                    Array.Empty<string>(),
                    ItemTypeCatalogService.TryGetEntry(index, out _) ? EquipmentTypeSourceConfidence.LegacyFallback : EquipmentTypeSourceConfidence.Unknown,
                    "未读取当前项目 profile，使用旧列名兜底");
            })
            .ToArray();
    }

    private JobEquipmentPermissionSlotDefinition? FindJobEquipmentSlot(string columnName)
        => GetJobEquipmentPermissionSlots()
            .FirstOrDefault(slot => string.Equals(slot.StorageColumnName, columnName, StringComparison.Ordinal));

    private string BuildJobEquipmentSummary(DataRow row)
    {
        var enabled = GetJobEquipmentPermissionSlots()
            .Where(slot => row.Table.Columns.Contains(slot.StorageColumnName) &&
                           Convert.ToInt32(row[slot.StorageColumnName], CultureInfo.InvariantCulture) != 0)
            .Select(slot => slot.SummaryName)
            .ToList();
        return enabled.Count == 0
            ? "无"
            : string.Join("、", enabled);
    }

    private void RefreshJobEquipmentSummary(DataRow row)
    {
        if (row.Table.Columns.Contains(JobEquipmentSummaryColumn))
        {
            var summary = BuildJobEquipmentSummary(row);
            var current = Convert.ToString(row[JobEquipmentSummaryColumn], CultureInfo.InvariantCulture) ?? string.Empty;
            if (!string.Equals(current, summary, StringComparison.Ordinal))
            {
                row[JobEquipmentSummaryColumn] = summary;
            }
        }
    }

    private string BuildJobEditorSummary(DataTable data)
    {
        var named = data.Rows.Cast<DataRow>().Count(row => !string.IsNullOrWhiteSpace(Convert.ToString(row["名称"], CultureInfo.InvariantCulture)));
        var profileLine = _currentEquipmentTypeProfile == null
            ? "装备类别显示：未读取项目化 profile，使用旧内置列名兜底。"
            : $"装备类别显示：按当前项目物品类型 profile 解析，人工校正文件：{_currentEquipmentTypeProfile.NotesPath}";
        var accessoryGroupLine = _currentAccessoryJobGroupProfile == null
            ? "辅助多兵种分组：未读取。"
            : _currentAccessoryJobGroupProfile.SummaryText;
        return
            $"兵种设定已读取：总行 {data.Rows.Count}，已命名 {named}。\r\n" +
            "来源表：6.5-4 详细兵种、6.5-4-1 兵种说明、6.5-4-2 兵种成长、6.5-4-3 兵种穿透。\r\n" +
            "可装备类别来自 6.5-4-2 兵种成长每行后 26 个字节；单击“可装备类别”列后可在右侧复选框编辑，切换单元格时同步摘要。\r\n" +
            "辅助多兵种分组来自 Ekd5.exe:0044C341，使用 6.5-3 兵种系编号；它不属于物品表字段。\r\n" +
            profileLine + "\r\n" +
            accessoryGroupLine + "\r\n" +
            "保存会写回 Ekd5.exe、Data.e5、Imsg.e5，保存前自动备份，保存后重新读取校验。";
    }

    private void ApplyJobEditorFilter()
    {
        if (_currentJobEditorData == null) return;
        CommitJobDescriptionBoxEdit();
        CommitJobEquipmentEditorChanges();
        var keyword = _jobEditorSearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            _currentJobEditorData.DefaultView.RowFilter = string.Empty;
            SetStatus("兵种筛选已清除");
            return;
        }

        var escaped = EscapeDataViewLikeValue(keyword);
        var filters = _currentJobEditorData.Columns
            .Cast<DataColumn>()
            .Where(column => column.ColumnName is "ID" or "名称" or "介绍" or "移动力" or "攻击范围" or "攻击" or "防御" or "精神" or "爆发" or "士气" or "HP" or "MP" or "穿透" or JobEquipmentSummaryColumn ||
                             IsJobEquipmentCategoryColumn(column.ColumnName))
            .Select(column => $"CONVERT([{column.ColumnName}], 'System.String') LIKE '*{escaped}*'");
        _currentJobEditorData.DefaultView.RowFilter = string.Join(" OR ", filters);
        SetStatus($"兵种筛选：{_currentJobEditorData.DefaultView.Count}/{_currentJobEditorData.Rows.Count}");
    }

    private void ClearJobEditorFilter()
    {
        CommitJobDescriptionBoxEdit();
        CommitJobEquipmentEditorChanges();
        _jobEditorSearchBox.Clear();
        if (_currentJobEditorData != null) _currentJobEditorData.DefaultView.RowFilter = string.Empty;
        SetStatus("兵种筛选已清除");
    }

    private void ExportJobEditorCsv()
    {
        CommitJobDescriptionBoxEdit();
        CommitJobEquipmentEditorChanges();
        ExportDataTableCsv(_currentJobEditorData, "兵种设定.csv");
    }

    private void ImportJobEditorCsv()
    {
        CommitJobDescriptionBoxEdit();
        CommitJobEquipmentEditorChanges();
        ImportDataTableCsv(
            _currentJobEditorData,
            "兵种设定",
            () => ResetJobEditorHistory(),
            _jobEditorGrid,
            RefreshJobEditorCellsAfterCsvImport);
    }

    private void RefreshJobEditorAfterBulkEdit()
    {
        if (_currentJobEditorData != null)
        {
            foreach (DataRow row in _currentJobEditorData.Rows)
            {
                RefreshJobEquipmentSummary(row);
            }
        }

        RefreshJobEditorRowStyles();
        ShowSelectedJobEditorCell();
        UpdateJobEditorHistoryButtons();
    }

    private void OpenJobEquipmentAttributeEditor(int rowIndex)
    {
        if (_currentJobEditorData == null || rowIndex < 0 || rowIndex >= _jobEditorGrid.Rows.Count) return;
        _jobEditorGrid.EndEdit();
        var row = TryGetDataRow(_jobEditorGrid.Rows[rowIndex]);
        if (row == null) return;

        var jobId = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
        var jobName = Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        using var dialog = new JobEquipmentAttributeDialog(jobId, jobName, row, GetJobEquipmentPermissionSlots());
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        ApplyJobEquipmentValues(row, dialog.EquipmentValues);
    }

    private void EditAccessoryJobGroups()
    {
        if (_project == null) return;
        if (_currentAccessoryJobGroupProfile == null)
        {
            try
            {
                _currentAccessoryJobGroupProfile = _accessoryJobGroupService.Read(_project, _tables);
                _editAccessoryJobGroupsButton.Enabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "读取辅助分组失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        using var dialog = new AccessoryJobGroupDialog(_currentAccessoryJobGroupProfile, _jobSeriesNames);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        AccessoryJobGroupPreview preview;
        try
        {
            preview = _accessoryJobGroupService.Preview(_project, _tables, dialog.Groups);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "辅助分组预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (!preview.CanWrite)
        {
            MessageBox.Show(this, string.Join("\r\n", preview.Diagnostics), "辅助分组格式错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var groupSummary = preview.Groups.Count == 0
            ? "无"
            : string.Join("\r\n", preview.Groups.Select(group => group.SummaryText));
        if (MessageBox.Show(this,
                $"即将保存辅助装备多兵种分组到 Ekd5.exe。\r\n\r\n地址：0x{AccessoryJobGroupService.StartVirtualAddress:X8} -> {preview.FileOffsetHex}\r\n写入长度：{preview.PaddedBytes.Count}/{preview.WritableLength} 字节\r\n原始字节：{preview.PayloadBytesHex}\r\n\r\n{groupSummary}\r\n\r\n保存前会自动备份，保存后会重新读取校验。是否继续？",
                "确认保存辅助分组",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var save = _accessoryJobGroupService.Save(_project, _tables, dialog.Groups);
            _currentAccessoryJobGroupProfile = _accessoryJobGroupService.Read(_project, _tables);
            _jobEditorInfoBox.Text = _currentJobEditorData == null
                ? _currentAccessoryJobGroupProfile.SummaryText
                : BuildJobEditorSummary(_currentJobEditorData);
            ShowSelectedJobEditorCell();
            SetStatus($"辅助装备多兵种分组保存完成：变化 {save.ChangedBytes} 字节");
            MessageBox.Show(this,
                $"保存完成并已重新读取校验。\r\n变化字节：{save.ChangedBytes}\r\n备份：{save.BackupPath}\r\n报告：{save.ReportJsonPath}",
                "保存完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "辅助分组保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ApplyJobEquipmentValues(DataRow row, IReadOnlyDictionary<string, int> values)
    {
        if (_currentJobEditorData == null || row.RowState == DataRowState.Detached) return;

        var edits = new List<JobEditorCellEdit>();
        foreach (var slot in GetJobEquipmentPermissionSlots())
        {
            var columnName = slot.StorageColumnName;
            if (!row.Table.Columns.Contains(columnName) || !values.TryGetValue(columnName, out var newValue)) continue;
            var oldValue = NormalizeGridCellValue(row[columnName]);
            if (Equals(oldValue, newValue)) continue;

            row[columnName] = newValue;
            edits.Add(new JobEditorCellEdit(row, columnName, oldValue, NormalizeGridCellValue(row[columnName])));
        }

        RefreshJobEquipmentSummary(row);
        if (edits.Count > 0)
        {
            PushJobEditorHistory(edits);
            RefreshJobEditorCellsAfterEdits(edits);
            SetStatus($"详细兵种 {Convert.ToString(row["ID"], CultureInfo.InvariantCulture)} 可装备类别已更新：{edits.Count} 项。");
        }
        else
        {
            ShowSelectedJobEditorCell();
            SetStatus("可装备类别没有产生改动。");
        }
    }

    private void EnsureJobEquipmentEditorChecks()
    {
        var slots = GetJobEquipmentPermissionSlots();
        var signature = string.Join("|", slots.Select(slot => $"{slot.StorageColumnName}:{slot.TypeId}:{slot.CheckBoxText}:{slot.SourceDisplayName}"));
        if (_jobEquipmentEditorChecks.Count == slots.Count &&
            slots.All(slot => _jobEquipmentEditorChecks.ContainsKey(slot.StorageColumnName)) &&
            string.Equals(_jobEquipmentEditorSlotSignature, signature, StringComparison.Ordinal))
        {
            return;
        }

        _bindingJobEquipmentEditor = true;
        try
        {
            _jobEquipmentEditorChecks.Clear();
            _jobEquipmentCheckGrid.SuspendLayout();
            _jobEquipmentCheckGrid.Controls.Clear();
            _jobEquipmentCheckGrid.RowStyles.Clear();
            _jobEquipmentCheckGrid.RowCount = 0;

            for (var i = 0; i < slots.Count; i++)
            {
                if (i % 2 == 0)
                {
                    _jobEquipmentCheckGrid.RowCount++;
                    _jobEquipmentCheckGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                }

                var slot = slots[i];
                var check = new CheckBox
                {
                    AutoSize = true,
                    Padding = new Padding(4, 6, 12, 6),
                    Text = slot.CheckBoxText,
                    Tag = slot.StorageColumnName
                };
                _jobEquipmentEditorToolTip.SetToolTip(check, slot.PreviewText + $"；原始列={slot.StorageColumnName}");
                check.CheckedChanged += (_, _) => UpdateJobEquipmentEditorStatus();
                _jobEquipmentEditorChecks[slot.StorageColumnName] = check;
                _jobEquipmentCheckGrid.Controls.Add(check, i % 2, i / 2);
            }
        }
        finally
        {
            _jobEquipmentCheckGrid.ResumeLayout();
            _bindingJobEquipmentEditor = false;
        }

        _jobEquipmentEditorSlotSignature = signature;
    }

    private void ShowJobEquipmentEditor(DataGridViewRow gridRow)
    {
        if (_currentJobEditorData == null)
        {
            ClearJobAreaPreview("请先读取兵种。");
            return;
        }

        var row = TryGetDataRow(gridRow);
        if (row == null)
        {
            ClearJobAreaPreview("当前兵种行无法解析。");
            return;
        }

        EnsureJobEquipmentEditorChecks();
        _jobEquipmentEditorBoundRow = row;
        _bindingJobEquipmentEditor = true;
        try
        {
            foreach (var slot in GetJobEquipmentPermissionSlots())
            {
                if (!_jobEquipmentEditorChecks.TryGetValue(slot.StorageColumnName, out var check)) continue;
                check.Checked = row.Table.Columns.Contains(slot.StorageColumnName) &&
                                Convert.ToInt32(row[slot.StorageColumnName], CultureInfo.InvariantCulture) != 0;
            }
        }
        finally
        {
            _bindingJobEquipmentEditor = false;
        }

        var id = Convert.ToString(row["ID"], CultureInfo.InvariantCulture) ?? string.Empty;
        var name = Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        _jobEquipmentEditorTitleLabel.Text = $"可装备类别：ID={id} {name}";
        _jobAreaPreviewBox.Visible = false;
        _jobAreaPreviewInfoBox.Visible = false;
        _jobEquipmentEditorPanel.Visible = true;
        _jobEquipmentEditorPanel.BringToFront();
        UpdateJobEquipmentEditorStatus();
    }

    private void HideJobEquipmentEditor()
    {
        _jobEquipmentEditorPanel.Visible = false;
        _jobAreaPreviewBox.Visible = true;
        _jobAreaPreviewInfoBox.Visible = true;
    }

    private int CountPendingJobEquipmentEditorChanges()
    {
        if (_jobEquipmentEditorBoundRow == null || _jobEquipmentEditorBoundRow.RowState == DataRowState.Detached) return 0;

        var count = 0;
        foreach (var slot in GetJobEquipmentPermissionSlots())
        {
            if (!_jobEquipmentEditorChecks.TryGetValue(slot.StorageColumnName, out var check) ||
                !_jobEquipmentEditorBoundRow.Table.Columns.Contains(slot.StorageColumnName))
            {
                continue;
            }

            var oldValue = Convert.ToInt32(_jobEquipmentEditorBoundRow[slot.StorageColumnName], CultureInfo.InvariantCulture) != 0;
            if (oldValue != check.Checked) count++;
        }

        return count;
    }

    private void UpdateJobEquipmentEditorStatus()
    {
        if (_bindingJobEquipmentEditor) return;

        var enabled = _jobEquipmentEditorChecks.Values.Count(check => check.Checked);
        var pending = CountPendingJobEquipmentEditorChanges();
        _jobEquipmentEditorStatusLabel.Text =
            $"已勾选：{enabled}/{GetJobEquipmentPermissionSlots().Count}。切换到其他单元格或保存时同步到主表；待应用改动：{pending} 项。";
        if (pending > 0) SetStatus($"可装备类别待应用改动：{pending} 项。切换到其他单元格或保存时同步。");
    }

    private void CommitJobEquipmentEditorChanges()
    {
        if (_jobEquipmentEditorBoundRow == null || _jobEquipmentEditorBoundRow.RowState == DataRowState.Detached)
        {
            _jobEquipmentEditorBoundRow = null;
            return;
        }

        if (_bindingJobEquipmentEditor) return;
        if (CountPendingJobEquipmentEditorChanges() == 0)
        {
            return;
        }

        var row = _jobEquipmentEditorBoundRow;
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var slot in GetJobEquipmentPermissionSlots())
        {
            if (!_jobEquipmentEditorChecks.TryGetValue(slot.StorageColumnName, out var check)) continue;
            values[slot.StorageColumnName] = check.Checked ? 1 : 0;
        }

        ApplyJobEquipmentValues(row, values);
    }

    private void SnapshotJobEditorSelectionForEdit()
    {
        var targets = CaptureJobEditorSelectedTargets();
        if (targets.Count > 1)
        {
            _jobEditorSelectionSnapshotTargets = targets;
        }
    }

    private void MarkJobEditorSelectionChangeFromMouse()
    {
        _jobEditorSelectionChangeStartedByMouse = true;
        _jobEditorSelectionSnapshotTargets = [];
    }

    private static bool IsPotentialJobEditorTextInput(KeyEventArgs e)
    {
        if (e.Control || e.Alt) return false;
        if (e.KeyCode is Keys.Enter or Keys.Escape or Keys.Tab or Keys.Delete or Keys.Back) return false;
        if (e.KeyCode >= Keys.Left && e.KeyCode <= Keys.Down) return false;
        if (e.KeyCode >= Keys.F1 && e.KeyCode <= Keys.F24) return false;
        if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) return true;
        if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9) return true;
        if (e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z) return true;
        if (e.KeyCode is Keys.OemMinus or Keys.Subtract or Keys.Oemplus or Keys.Decimal or Keys.OemPeriod or Keys.Space) return true;
        return false;
    }

    private void HandleJobEditorSelectionChanged()
    {
        if (!IsCurrentJobEquipmentSummaryCellBoundToEditor())
        {
            CommitJobEquipmentEditorChanges();
        }

        if (!IsCurrentJobDescriptionBoxBoundToSelection())
        {
            CommitJobDescriptionBoxEdit();
        }

        if (!_applyingJobEditorHistory && !_jobEditorGrid.IsCurrentCellInEditMode)
        {
            var targets = CaptureJobEditorSelectedTargets();
            if (_jobEditorSelectionChangeStartedByMouse)
            {
                _jobEditorSelectionSnapshotTargets = targets;
                _jobEditorSelectionChangeStartedByMouse = false;
                ShowSelectedJobEditorCell();
                return;
            }

            if (targets.Count > 1 ||
                targets.Count == 0 ||
                _jobEditorSelectionSnapshotTargets.Count == 0 ||
                !JobEditorTargetListContains(_jobEditorSelectionSnapshotTargets, targets[0]))
            {
                _jobEditorSelectionSnapshotTargets = targets;
            }
        }

        ShowSelectedJobEditorCell();
    }

    private bool IsCurrentJobDescriptionBoxBoundToSelection()
    {
        if (_jobDescriptionEditorBoundRow == null ||
            _jobDescriptionEditorBoundRow.RowState == DataRowState.Detached ||
            _jobEditorGrid.CurrentCell == null)
        {
            return false;
        }

        var row = TryGetDataRow(_jobEditorGrid.Rows[_jobEditorGrid.CurrentCell.RowIndex]);
        return ReferenceEquals(row, _jobDescriptionEditorBoundRow);
    }

    private bool IsCurrentJobEquipmentSummaryCellBoundToEditor()
    {
        if (_jobEquipmentEditorBoundRow == null ||
            _jobEquipmentEditorBoundRow.RowState == DataRowState.Detached ||
            _jobEditorGrid.CurrentCell == null)
        {
            return false;
        }

        var columnName = _jobEditorGrid.Columns[_jobEditorGrid.CurrentCell.ColumnIndex].DataPropertyName;
        if (!string.Equals(columnName, JobEquipmentSummaryColumn, StringComparison.Ordinal)) return false;
        var row = TryGetDataRow(_jobEditorGrid.Rows[_jobEditorGrid.CurrentCell.RowIndex]);
        return ReferenceEquals(row, _jobEquipmentEditorBoundRow);
    }

    private void BeginJobEditorCellEdit(int rowIndex, int columnIndex)
    {
        CommitJobDescriptionBoxEdit();
        if (_applyingJobEditorHistory || rowIndex < 0 || columnIndex < 0)
        {
            _jobEditorPendingCellEditOriginals = [];
            return;
        }

        _jobEditorPendingCellEditOriginals = CaptureJobEditorSelectionOriginals(rowIndex, columnIndex);
    }

    private void CompleteJobEditorCellEdit(int rowIndex, int columnIndex)
    {
        if (_applyingJobEditorHistory || rowIndex < 0 || columnIndex < 0) return;
        if (_jobEditorPendingCellEditOriginals.Count == 0) return;

        var column = _jobEditorGrid.Columns[columnIndex];
        var columnName = column.DataPropertyName;
        var editedRow = TryGetDataRow(_jobEditorGrid.Rows[rowIndex]);
        if (editedRow == null)
        {
            _jobEditorPendingCellEditOriginals = [];
            return;
        }

        var source = _jobEditorPendingCellEditOriginals
            .FirstOrDefault(edit => ReferenceEquals(edit.Row, editedRow) &&
                                    string.Equals(edit.ColumnName, columnName, StringComparison.Ordinal));
        if (source == null)
        {
            _jobEditorPendingCellEditOriginals = [];
            return;
        }

        var newValue = NormalizeGridCellValue(source.Row[columnName]);
        var edits = new List<JobEditorCellEdit>
        {
            new(source.Row, columnName, source.OldValue, newValue)
        };

        if (_jobEditorPendingCellEditOriginals.Count > 1)
        {
            var text = FormatGridValueForBatchInput(newValue);
            foreach (var target in _jobEditorPendingCellEditOriginals)
            {
                if (ReferenceEquals(target.Row, source.Row) &&
                    string.Equals(target.ColumnName, columnName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryValidateJobEditorCellText(target.ColumnName, text, out var error))
                {
                    _jobEditorInfoBox.Text = error;
                    SetStatus(error);
                    continue;
                }

                var targetNewValue = ConvertJobEditorValueForColumn(target.ColumnName, text);
                if (Equals(target.OldValue, targetNewValue)) continue;
                target.Row[target.ColumnName] = targetNewValue ?? DBNull.Value;
                edits.Add(new JobEditorCellEdit(target.Row, target.ColumnName, target.OldValue, NormalizeGridCellValue(target.Row[target.ColumnName])));
            }
        }

        _jobEditorPendingCellEditOriginals = [];
        PushJobEditorHistory(edits);
        RefreshJobEditorCellsAfterEdits(edits);
        if (edits.Count > 1)
        {
            SetStatus($"兵种设定已将当前输入应用到选区：{edits.Count} 个单元格。");
        }
        else if (edits.Count == 1 && !Equals(edits[0].OldValue, edits[0].NewValue))
        {
            SetStatus("兵种设定已更新 1 个单元格。");
        }
    }

    private void PasteJobEditorSelection()
    {
        CommitJobDescriptionBoxEdit();
        CommitJobEquipmentEditorChanges();
        if (_jobEditorGrid.ReadOnly)
        {
            SetStatus("当前表格不可编辑。");
            return;
        }

        if (!Clipboard.ContainsText())
        {
            SetStatus("剪贴板没有文本。");
            return;
        }

        var start = GetJobEditorPasteStartCell();
        if (start == null)
        {
            SetStatus("请先选中粘贴起点。");
            return;
        }

        if (!_jobEditorGrid.EndEdit())
        {
            SetStatus("当前单元格未能提交，无法粘贴。");
            return;
        }

        var matrix = ParseClipboardMatrix(Clipboard.GetText());
        var edits = new List<JobEditorCellEdit>();
        var lastCell = start.Value;
        for (var r = 0; r < matrix.Count; r++)
        {
            var rowIndex = start.Value.Row + r;
            if (rowIndex >= _jobEditorGrid.Rows.Count) break;

            for (var c = 0; c < matrix[r].Count; c++)
            {
                var columnIndex = start.Value.Column + c;
                if (columnIndex >= _jobEditorGrid.Columns.Count) break;

                if (TrySetJobEditorCellValue(rowIndex, columnIndex, matrix[r][c], edits, out _))
                {
                    lastCell = (rowIndex, columnIndex);
                }
            }
        }

        PushJobEditorHistory(edits);
        if (edits.Count > 0)
        {
            _jobEditorGrid.CurrentCell = _jobEditorGrid.Rows[lastCell.Row].Cells[lastCell.Column];
            RefreshJobEditorCellsAfterEdits(edits);
        }

        SetStatus($"兵种设定粘贴完成：更新 {edits.Count} 个单元格。");
    }

    private void FillJobEditorSelectionWithCurrentValue()
    {
        CommitJobDescriptionBoxEdit();
        CommitJobEquipmentEditorChanges();
        if (_jobEditorGrid.ReadOnly)
        {
            SetStatus("当前表格不可编辑。");
            return;
        }

        if (_jobEditorGrid.CurrentCell == null)
        {
            SetStatus("请先选中用于批量填列的单元格。");
            return;
        }

        if (!_jobEditorGrid.EndEdit())
        {
            SetStatus("当前单元格未能提交，无法批量填列。");
            return;
        }

        var value = GetJobEditorCurrentInputText();
        var targets = GetJobEditorBatchFillTargets();
        if (targets.Count <= 1)
        {
            SetStatus("请先滑动选中多个要批量填列的单元格。");
            return;
        }

        var edits = new List<JobEditorCellEdit>();
        var lastError = string.Empty;
        foreach (var target in targets)
        {
            if (!TrySetJobEditorCellTargetValue(target, value, edits, out var error))
            {
                lastError = error;
            }
        }

        PushJobEditorHistory(edits);
        if (edits.Count > 0) RefreshJobEditorCellsAfterEdits(edits);
        SetStatus(edits.Count > 0
            ? $"兵种设定批量填列完成：更新 {edits.Count} 个单元格。"
            : string.IsNullOrWhiteSpace(lastError) ? "兵种设定批量填列没有产生改动。" : lastError);
    }

    private List<JobEditorCellEdit> CaptureJobEditorSelectionOriginals(int fallbackRowIndex, int fallbackColumnIndex)
    {
        var targets = CaptureJobEditorSelectedTargets();
        var fallbackTarget = TryCreateJobEditorCellTarget(fallbackRowIndex, fallbackColumnIndex, out var currentTarget)
            ? currentTarget
            : null;
        if (fallbackTarget != null &&
            targets.Count <= 1 &&
            _jobEditorSelectionSnapshotTargets.Count > 1 &&
            JobEditorTargetListContains(_jobEditorSelectionSnapshotTargets, fallbackTarget))
        {
            targets = _jobEditorSelectionSnapshotTargets;
        }
        else if (targets.Count == 0 && fallbackTarget != null)
        {
            targets.Add(fallbackTarget);
        }

        return targets
            .Where(target => IsJobEditorTargetValid(target))
            .Select(target => new JobEditorCellEdit(target.Row, target.ColumnName, NormalizeGridCellValue(target.Row[target.ColumnName]), null))
            .ToList();
    }

    private List<JobEditorCellTarget> GetJobEditorBatchFillTargets()
    {
        var targets = CaptureJobEditorSelectedTargets();
        if (targets.Count > 1 || _jobEditorGrid.CurrentCell == null)
        {
            return targets;
        }

        if (TryCreateJobEditorCellTarget(
                _jobEditorGrid.CurrentCell.RowIndex,
                _jobEditorGrid.CurrentCell.ColumnIndex,
                out var currentTarget) &&
            _jobEditorSelectionSnapshotTargets.Count > 1 &&
            JobEditorTargetListContains(_jobEditorSelectionSnapshotTargets, currentTarget))
        {
            return _jobEditorSelectionSnapshotTargets;
        }

        return targets;
    }

    private string GetJobEditorCurrentInputText()
    {
        if (_jobEditorGrid.IsCurrentCellInEditMode &&
            _jobEditorGrid.EditingControl is TextBoxBase textBox)
        {
            return textBox.Text;
        }

        return FormatGridValueForBatchInput(_jobEditorGrid.CurrentCell?.Value);
    }

    private List<JobEditorCellTarget> CaptureJobEditorSelectedTargets()
        => GetJobEditorSelectedEditableCells()
            .Select(cell => TryCreateJobEditorCellTarget(cell.RowIndex, cell.ColumnIndex, out var target) ? target : null)
            .OfType<JobEditorCellTarget>()
            .ToList();

    private bool TryCreateJobEditorCellTarget(int rowIndex, int columnIndex, out JobEditorCellTarget target)
    {
        target = null!;
        if (!TryResolveJobEditorCell(rowIndex, columnIndex, out var row, out var columnName)) return false;
        target = new JobEditorCellTarget(row, columnName);
        return true;
    }

    private static bool JobEditorTargetListContains(IReadOnlyList<JobEditorCellTarget> targets, JobEditorCellTarget target)
        => targets.Any(candidate => ReferenceEquals(candidate.Row, target.Row) &&
                                    string.Equals(candidate.ColumnName, target.ColumnName, StringComparison.Ordinal));

    private bool IsJobEditorTargetValid(JobEditorCellTarget target)
        => target.Row.RowState != DataRowState.Detached &&
           _currentJobEditorData?.Columns.Contains(target.ColumnName) == true;

    private bool TrySetJobEditorCellTargetValue(
        JobEditorCellTarget target,
        string text,
        List<JobEditorCellEdit> edits,
        out string error)
    {
        error = string.Empty;
        if (!IsJobEditorTargetValid(target)) return false;
        if (!TryValidateJobEditorCellText(target.ColumnName, text, out error))
        {
            SetJobEditorCellError(target, error);
            return false;
        }

        try
        {
            var oldValue = NormalizeGridCellValue(target.Row[target.ColumnName]);
            var newValue = ConvertJobEditorValueForColumn(target.ColumnName, text);
            if (Equals(oldValue, newValue)) return false;

            target.Row[target.ColumnName] = newValue ?? DBNull.Value;
            SetJobEditorCellError(target, string.Empty);
            edits.Add(new JobEditorCellEdit(target.Row, target.ColumnName, oldValue, NormalizeGridCellValue(target.Row[target.ColumnName])));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            SetJobEditorCellError(target, error);
            return false;
        }
    }

    private void SetJobEditorCellError(JobEditorCellTarget target, string error)
    {
        foreach (DataGridViewRow row in _jobEditorGrid.Rows)
        {
            if (!ReferenceEquals(TryGetDataRow(row), target.Row)) continue;
            foreach (DataGridViewColumn column in _jobEditorGrid.Columns)
            {
                if (!string.Equals(column.DataPropertyName, target.ColumnName, StringComparison.Ordinal)) continue;
                row.Cells[column.Index].ErrorText = error;
                return;
            }
        }
    }

    private List<DataGridViewCell> GetJobEditorSelectedEditableCells()
        => _jobEditorGrid.SelectedCells.Cast<DataGridViewCell>()
            .Where(cell => IsJobEditorEditableCell(cell.RowIndex, cell.ColumnIndex))
            .OrderBy(cell => cell.RowIndex)
            .ThenBy(cell => cell.ColumnIndex)
            .ToList();

    private bool IsJobEditorEditableCell(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || rowIndex >= _jobEditorGrid.Rows.Count ||
            columnIndex < 0 || columnIndex >= _jobEditorGrid.Columns.Count)
        {
            return false;
        }

        var column = _jobEditorGrid.Columns[columnIndex];
        if (column.ReadOnly || !column.Visible) return false;
        var cell = _jobEditorGrid.Rows[rowIndex].Cells[columnIndex];
        return !cell.ReadOnly && TryGetDataRow(_jobEditorGrid.Rows[rowIndex]) != null;
    }

    private (int Row, int Column)? GetJobEditorPasteStartCell()
    {
        if (_jobEditorGrid.CurrentCell != null)
        {
            return (_jobEditorGrid.CurrentCell.RowIndex, _jobEditorGrid.CurrentCell.ColumnIndex);
        }

        return _jobEditorGrid.SelectedCells.Cast<DataGridViewCell>()
            .Where(cell => cell.RowIndex >= 0 && cell.ColumnIndex >= 0)
            .OrderBy(cell => cell.RowIndex)
            .ThenBy(cell => cell.ColumnIndex)
            .Select(cell => ((int Row, int Column)?)(cell.RowIndex, cell.ColumnIndex))
            .FirstOrDefault();
    }

    private bool TrySetJobEditorCellValue(
        int rowIndex,
        int columnIndex,
        string text,
        List<JobEditorCellEdit> edits,
        out string error)
    {
        error = string.Empty;
        if (!TryResolveJobEditorCell(rowIndex, columnIndex, out var row, out var columnName)) return false;
        if (!TryValidateJobEditorCellText(columnName, text, out error))
        {
            _jobEditorGrid.Rows[rowIndex].Cells[columnIndex].ErrorText = error;
            return false;
        }

        try
        {
            var oldValue = NormalizeGridCellValue(row[columnName]);
            var newValue = ConvertJobEditorValueForColumn(columnName, text);
            if (Equals(oldValue, newValue)) return false;

            row[columnName] = newValue ?? DBNull.Value;
            _jobEditorGrid.Rows[rowIndex].Cells[columnIndex].ErrorText = string.Empty;
            edits.Add(new JobEditorCellEdit(row, columnName, oldValue, NormalizeGridCellValue(row[columnName])));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _jobEditorGrid.Rows[rowIndex].Cells[columnIndex].ErrorText = error;
            return false;
        }
    }

    private bool TryResolveJobEditorCell(int rowIndex, int columnIndex, out DataRow row, out string columnName)
    {
        row = null!;
        columnName = string.Empty;
        if (rowIndex < 0 || rowIndex >= _jobEditorGrid.Rows.Count ||
            columnIndex < 0 || columnIndex >= _jobEditorGrid.Columns.Count)
        {
            return false;
        }

        var column = _jobEditorGrid.Columns[columnIndex];
        if (column.ReadOnly || !column.Visible) return false;
        var cell = _jobEditorGrid.Rows[rowIndex].Cells[columnIndex];
        if (cell.ReadOnly) return false;
        columnName = column.DataPropertyName;
        if (string.IsNullOrEmpty(columnName) || _currentJobEditorData?.Columns.Contains(columnName) != true) return false;
        row = TryGetDataRow(_jobEditorGrid.Rows[rowIndex])!;
        return row != null;
    }

    private bool TryValidateJobEditorCellText(string columnName, string value, out string error)
    {
        error = string.Empty;
        value = NormalizeJobAreaComboInput(columnName, value);
        if (columnName == "名称")
        {
            var bytes = EncodingService.GetGbkByteCount(value);
            if (bytes > 9) error = $"详细兵种名称超长：GBK {bytes} 字节，容量 9 字节。";
        }
        else if (columnName == "介绍")
        {
            var bytes = EncodingService.GetGbkByteCount(value);
            if (bytes > 200) error = $"兵种说明超长：GBK {bytes} 字节，容量 200 字节。";
        }
        else if (columnName != "ID")
        {
            error = TryParseInteger(value, 0, byte.MaxValue, columnName, ShouldUseHexForJobEditorColumn(columnName)) ?? string.Empty;
        }

        return string.IsNullOrEmpty(error);
    }

    private object? ConvertJobEditorValueForColumn(string columnName, string text)
    {
        text = NormalizeJobAreaComboInput(columnName, text);
        if (_currentJobEditorData == null || !_currentJobEditorData.Columns.Contains(columnName)) return text;
        var dataColumn = _currentJobEditorData.Columns[columnName];
        if (dataColumn == null) return text;
        var targetType = dataColumn.DataType;
        if (targetType == typeof(string)) return text;
        if (IsSupportedIntegerType(targetType) &&
            TryParseIntegerInput(text, ShouldUseHexForJobEditorColumn(columnName), out var parsed) &&
            TryConvertParsedIntegerToType(parsed, targetType, out var converted))
        {
            return converted;
        }

        return Convert.ChangeType(text, targetType, CultureInfo.InvariantCulture);
    }

    private static string NormalizeJobAreaComboInput(string columnName, string text)
    {
        if (columnName is not ("攻击范围" or "穿透")) return text;
        var index = text.IndexOf('：', StringComparison.Ordinal);
        if (index < 0) index = text.IndexOf(':', StringComparison.Ordinal);
        return index > 0 ? text[..index].Trim() : text.Trim();
    }

    private bool ShouldUseHexForJobEditorColumn(string columnName)
        => _currentPageHexButton.Checked && columnName is not ("攻击范围" or "穿透");

    private static object? NormalizeGridCellValue(object? value)
        => value == DBNull.Value ? null : value;

    private void PushJobEditorHistory(List<JobEditorCellEdit> edits)
    {
        var effective = edits
            .Where(edit => !Equals(edit.OldValue, edit.NewValue))
            .ToList();
        if (effective.Count == 0)
        {
            UpdateJobEditorHistoryButtons();
            return;
        }

        _jobEditorUndoStack.Push(effective);
        _jobEditorRedoStack.Clear();
        UpdateJobEditorHistoryButtons();
    }

    private void UndoJobEditorChange()
    {
        if (!CommitJobDescriptionBoxEdit(showValidationMessage: true)) return;
        if (_jobEditorUndoStack.Count == 0)
        {
            SetStatus("兵种设定没有可后退的编辑。");
            return;
        }

        var edits = _jobEditorUndoStack.Pop();
        ApplyJobEditorHistory(edits, useOldValue: true);
        _jobEditorRedoStack.Push(edits);
        SetStatus($"兵种设定已后退一步：还原 {edits.Count} 个单元格。");
    }

    private void RedoJobEditorChange()
    {
        if (!CommitJobDescriptionBoxEdit(showValidationMessage: true)) return;
        if (_jobEditorRedoStack.Count == 0)
        {
            SetStatus("兵种设定没有可前进的编辑。");
            return;
        }

        var edits = _jobEditorRedoStack.Pop();
        ApplyJobEditorHistory(edits, useOldValue: false);
        _jobEditorUndoStack.Push(edits);
        SetStatus($"兵种设定已前进一步：恢复 {edits.Count} 个单元格。");
    }

    private void ApplyJobEditorHistory(List<JobEditorCellEdit> edits, bool useOldValue)
    {
        _applyingJobEditorHistory = true;
        try
        {
            foreach (var edit in edits)
            {
                if (edit.Row.RowState == DataRowState.Detached || !edit.Row.Table.Columns.Contains(edit.ColumnName)) continue;
                edit.Row[edit.ColumnName] = (useOldValue ? edit.OldValue : edit.NewValue) ?? DBNull.Value;
            }
        }
        finally
        {
            _applyingJobEditorHistory = false;
        }

        RefreshJobEditorCellsAfterEdits(edits);
    }

    private void ResetJobEditorHistory()
    {
        _jobEditorUndoStack.Clear();
        _jobEditorRedoStack.Clear();
        _jobEditorSelectionSnapshotTargets = [];
        _jobEditorPendingCellEditOriginals = [];
        _jobEditorSelectionChangeStartedByMouse = false;
        _jobDescriptionEditorHasPendingEdit = false;
        if (_jobDescriptionEditorBoundRow != null &&
            _jobDescriptionEditorBoundRow.RowState != DataRowState.Detached &&
            _jobDescriptionEditorBoundRow.Table.Columns.Contains("介绍"))
        {
            _jobDescriptionEditorOriginalValue = NormalizeGridCellValue(_jobDescriptionEditorBoundRow["介绍"]);
        }
        else
        {
            _jobDescriptionEditorOriginalValue = null;
        }
        UpdateJobEditorHistoryButtons();
    }

    private void UpdateJobEditorHistoryButtons()
    {
        _undoJobEditorButton.Enabled = _jobEditorUndoStack.Count > 0;
        _redoJobEditorButton.Enabled = _jobEditorRedoStack.Count > 0;
    }

    private void RefreshJobEditorRowStyles()
    {
        foreach (DataGridViewRow row in _jobEditorGrid.Rows)
        {
            RefreshJobEditorRowStyle(row.Index);
        }
    }

    private void RefreshJobEditorRowStyle(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _jobEditorGrid.Rows.Count) return;
        var dataRow = TryGetDataRow(_jobEditorGrid.Rows[rowIndex]);
        if (dataRow == null) return;
        _jobEditorGrid.Rows[rowIndex].DefaultCellStyle.BackColor = IsDataRowChanged(dataRow) ? Color.LightCyan : Color.Empty;
    }

    private void ShowSelectedJobEditorCell()
    {
        if (_currentJobEditorData == null || _jobEditorGrid.CurrentCell == null)
        {
            ClearJobDescriptionBox("读取兵种后，在此编辑当前兵种介绍。");
            return;
        }

        var cell = _jobEditorGrid.CurrentCell;
        var columnName = _jobEditorGrid.Columns[cell.ColumnIndex].DataPropertyName;
        var row = _jobEditorGrid.Rows[cell.RowIndex];
        var id = row.Cells["ID"].Value;
        var name = row.Cells["名称"].Value;
        var value = cell.Value;
        var dataRow = TryGetDataRow(row);
        var description = dataRow?.Table.Columns.Contains("介绍") == true
            ? Convert.ToString(dataRow["介绍"], CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;
        var descriptionBytes = EncodingService.GetGbkByteCount(description);
        _jobEditorInfoBox.Text =
            $"兵种：ID={id}    名称={name}\r\n" +
            $"字段：{columnName}    当前值：{value}\r\n" +
            $"介绍 GBK：{descriptionBytes}/200\r\n\r\n" +
            BuildJobColumnAnnotation(columnName);
        BindJobDescriptionBox(row);
        UpdateJobAreaPreview(row, columnName);
    }

    private void BindJobDescriptionBox(DataGridViewRow gridRow)
    {
        var row = TryGetDataRow(gridRow);
        if (row == null || !row.Table.Columns.Contains("介绍"))
        {
            ClearJobDescriptionBox("读取兵种后，在此编辑当前兵种介绍。");
            return;
        }

        var isSameRow = ReferenceEquals(row, _jobDescriptionEditorBoundRow);
        if (!isSameRow)
        {
            if (!CommitJobDescriptionBoxEdit()) return;
            _jobDescriptionEditorBoundRow = row;
            _jobDescriptionEditorOriginalValue = NormalizeGridCellValue(row["介绍"]);
            _jobDescriptionEditorHasPendingEdit = false;
        }
        else if (!_jobDescriptionEditorHasPendingEdit)
        {
            _jobDescriptionEditorOriginalValue = NormalizeGridCellValue(row["介绍"]);
        }

        if (isSameRow && _jobDescriptionBoxHasValidationError) return;

        var text = Convert.ToString(row["介绍"], CultureInfo.InvariantCulture) ?? string.Empty;
        _bindingJobDescriptionBox = true;
        try
        {
            _jobDescriptionBoxHasValidationError = false;
            _jobDescriptionBoxValidationError = string.Empty;
            _jobAreaPreviewInfoBox.ReadOnly = false;
            _jobAreaPreviewInfoBox.BackColor = SystemColors.Window;
            if (!string.Equals(_jobAreaPreviewInfoBox.Text, text, StringComparison.Ordinal))
            {
                _jobAreaPreviewInfoBox.Text = text;
            }
        }
        finally
        {
            _bindingJobDescriptionBox = false;
        }
    }

    private void ClearJobDescriptionBox(string text)
    {
        _bindingJobDescriptionBox = true;
        try
        {
            _jobDescriptionEditorBoundRow = null;
            _jobDescriptionEditorOriginalValue = null;
            _jobDescriptionEditorHasPendingEdit = false;
            _jobDescriptionBoxHasValidationError = false;
            _jobDescriptionBoxValidationError = string.Empty;
            _jobAreaPreviewInfoBox.BackColor = SystemColors.Window;
            _jobAreaPreviewInfoBox.Text = text;
        }
        finally
        {
            _bindingJobDescriptionBox = false;
        }
    }

    private void ApplyJobDescriptionBoxEdit()
    {
        if (_bindingJobDescriptionBox || _applyingJobEditorHistory) return;
        if (_currentJobEditorData == null ||
            _jobDescriptionEditorBoundRow == null ||
            _jobDescriptionEditorBoundRow.RowState == DataRowState.Detached ||
            !_jobDescriptionEditorBoundRow.Table.Columns.Contains("介绍"))
        {
            return;
        }

        var text = _jobAreaPreviewInfoBox.Text;
        var bytes = EncodingService.GetGbkByteCount(text);
        if (bytes > 200)
        {
            var error = $"兵种说明超长：GBK {bytes} 字节，容量 200 字节。";
            var currentValue = Convert.ToString(_jobDescriptionEditorBoundRow["介绍"], CultureInfo.InvariantCulture) ?? string.Empty;
            _bindingJobDescriptionBox = true;
            try
            {
                _jobAreaPreviewInfoBox.Text = currentValue;
                _jobAreaPreviewInfoBox.SelectionStart = _jobAreaPreviewInfoBox.TextLength;
                _jobAreaPreviewInfoBox.SelectionLength = 0;
            }
            finally
            {
                _bindingJobDescriptionBox = false;
            }

            _jobDescriptionBoxHasValidationError = false;
            _jobDescriptionBoxValidationError = string.Empty;
            _jobAreaPreviewInfoBox.BackColor = SystemColors.Window;
            SetStatus(error);
            return;
        }

        _jobDescriptionBoxHasValidationError = false;
        _jobDescriptionBoxValidationError = string.Empty;
        _jobAreaPreviewInfoBox.BackColor = SystemColors.Window;

        var oldValue = NormalizeGridCellValue(_jobDescriptionEditorBoundRow["介绍"]);
        if (Equals(oldValue, text))
        {
            _jobDescriptionEditorHasPendingEdit = !Equals(_jobDescriptionEditorOriginalValue, text);
            SetStatus($"兵种介绍 GBK：{bytes}/200");
            return;
        }

        _jobDescriptionEditorBoundRow["介绍"] = text;
        _jobDescriptionEditorHasPendingEdit = !Equals(_jobDescriptionEditorOriginalValue, text);
        var rowIndex = FindDataRowGridIndex(_jobEditorGrid, _jobDescriptionEditorBoundRow);
        if (rowIndex >= 0)
        {
            RefreshJobEditorRowStyle(rowIndex);
            _jobEditorGrid.InvalidateRow(rowIndex);
        }

        SetStatus($"兵种介绍已更新：GBK {bytes}/200");
    }

    private bool CommitJobDescriptionBoxEdit(bool showValidationMessage = false)
    {
        if (_bindingJobDescriptionBox) return true;
        if (_jobDescriptionBoxHasValidationError)
        {
            SetStatus(_jobDescriptionBoxValidationError);
            if (showValidationMessage)
            {
                MessageBox.Show(this, _jobDescriptionBoxValidationError, "兵种介绍无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return false;
        }

        if (_jobDescriptionEditorBoundRow == null ||
            _jobDescriptionEditorBoundRow.RowState == DataRowState.Detached ||
            !_jobDescriptionEditorBoundRow.Table.Columns.Contains("介绍"))
        {
            _jobDescriptionEditorBoundRow = null;
            _jobDescriptionEditorOriginalValue = null;
            _jobDescriptionEditorHasPendingEdit = false;
            return true;
        }

        if (_jobDescriptionEditorHasPendingEdit)
        {
            var newValue = NormalizeGridCellValue(_jobDescriptionEditorBoundRow["介绍"]);
            PushJobEditorHistory([
                new JobEditorCellEdit(_jobDescriptionEditorBoundRow, "介绍", _jobDescriptionEditorOriginalValue, newValue)
            ]);
            _jobDescriptionEditorOriginalValue = newValue;
            _jobDescriptionEditorHasPendingEdit = false;
        }

        return true;
    }

    private void UpdateJobAreaPreview(DataGridViewRow row, string columnName)
    {
        if (_project == null)
        {
            ClearJobAreaPreview("请先打开 MOD 项目。");
            return;
        }

        if (columnName == JobEquipmentSummaryColumn)
        {
            ShowJobEquipmentEditor(row);
            return;
        }

        if (columnName is not ("攻击范围" or "穿透"))
        {
            HideJobEquipmentEditor();
            UpdateJobSImagePreview(row);
            return;
        }

        var rawValue = row.Cells[columnName].Value;
        var text = Convert.ToString(rawValue, CultureInfo.InvariantCulture) ?? string.Empty;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fieldValue))
        {
            ClearJobAreaPreview($"{columnName} 字段不是整数：{text}");
            return;
        }

        var result = _attackAreaPreviewService.BuildPreview(_project, columnName, fieldValue);
        HideJobEquipmentEditor();
        SetPictureBoxImage(_jobAreaPreviewBox, result.Bitmap);
        SetStatus($"{columnName}预览：{result.Message}");
    }

    private void UpdateJobSImagePreview(DataGridViewRow row)
    {
        if (_project == null)
        {
            ClearJobAreaPreview("请先打开 MOD 项目。");
            return;
        }

        if (!TryGetJobEditorRowIdentity(row, out var jobId, out var name))
        {
            ClearJobAreaPreview("当前兵种行无法解析 ID。");
            return;
        }

        try
        {
            var preview = _imageAssignmentPreviewService.TryRenderSImageFactionStackPreview(_project, 0, jobId, out _);
            SetPictureBoxImage(_jobAreaPreviewBox, preview);
            SetStatus($"兵种 S 形象预览：ID={jobId:D2} {name}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("兵种 S 形象预览失败：" + ex);
            ClearJobAreaPreview("兵种 S 形象预览失败：" + ex.Message);
        }
    }

    private bool TryGetJobEditorRowIdentity(DataGridViewRow row, out int jobId, out string name)
    {
        jobId = -1;
        name = string.Empty;
        if (row.Cells["ID"].Value == null ||
            !int.TryParse(Convert.ToString(row.Cells["ID"].Value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out jobId))
        {
            return false;
        }

        name = Convert.ToString(row.Cells["名称"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        return true;
    }

    private void ReplaceSelectedJobSImage()
    {
        if (!CommitJobDescriptionBoxEdit(showValidationMessage: true)) return;
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_currentJobEditorData == null)
        {
            MessageBox.Show(this, "请先读取兵种。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_jobEditorGrid.CurrentCell == null)
        {
            MessageBox.Show(this, "请先选择一个详细兵种。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var row = _jobEditorGrid.Rows[_jobEditorGrid.CurrentCell.RowIndex];
        if (!TryGetJobEditorRowIdentity(row, out var jobId, out var name))
        {
            MessageBox.Show(this, "当前兵种行无法解析 ID。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new JobSImageReplaceDialog(jobId, name);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var request = new JobSImageReplaceRequest
        {
            JobId = jobId,
            MaterialFolder = dialog.MaterialFolder,
            FactionSlots = dialog.FactionSlots,
            WriteMode = _project.IsTestCopy ? "test_copy" : "direct"
        };

        var service = new JobSImageReplaceService();
        JobSImageReplacePreviewResult preview;
        try
        {
            Cursor = Cursors.WaitCursor;
            preview = service.Preview(_project, request);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("一键替换兵种 S 形象预览失败：" + ex);
            MessageBox.Show(this, ex.Message, "一键替换兵种形象预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        var previewText = BuildJobSImageReplacePreviewText(preview, name);
        _jobEditorInfoBox.Text = previewText;
        if (MessageBox.Show(this,
                previewText + "\r\n\r\n确认后会把素材转为 RAW，并只写入已勾选的阵营槽；写入前自动备份。是否继续？",
                "确认一键替换兵种形象",
                MessageBoxButtons.YesNo,
                _project.IsTestCopy ? MessageBoxIcon.Question : MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = service.Replace(_project, request);
            ClearImageAssignmentCaches();
            UpdateJobSImagePreview(row);
            _jobEditorInfoBox.Text = BuildJobSImageReplaceResultText(result, name);
            SetStatus($"一键替换兵种形象完成：写入 {result.TotalOperationCount} 条");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("一键替换兵种 S 形象写入失败：" + ex);
            MessageBox.Show(this, ex.Message, "一键替换兵种形象写入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void RefreshJobEditorCellsAfterCsvImport(IReadOnlyList<GridCellKey> changedCells)
    {
        if (_currentJobEditorData == null) return;

        foreach (var row in changedCells
                     .Select(key => FindDataRowByGridCellKey(_currentJobEditorData, key, "ID"))
                     .Where(row => row is { RowState: not DataRowState.Detached })
                     .Cast<DataRow>()
                     .Distinct())
        {
            RefreshJobEquipmentSummary(row);
        }

        RefreshChangedGridCells(_jobEditorGrid, changedCells);
        RefreshChangedGridRowsOnly(_jobEditorGrid, changedCells, RefreshJobEditorRowStyle);
        ShowSelectedJobEditorCell();
        UpdateJobEditorHistoryButtons();
    }

    private void RefreshJobEditorCellsAfterEdits(IReadOnlyList<JobEditorCellEdit> edits)
    {
        foreach (var row in edits
                     .Select(edit => edit.Row)
                     .Where(row => row.RowState != DataRowState.Detached)
                     .Distinct())
        {
            RefreshJobEquipmentSummary(row);
            var rowIndex = FindDataRowGridIndex(_jobEditorGrid, row);
            if (rowIndex >= 0)
            {
                RefreshJobEditorRowStyle(rowIndex);
                _jobEditorGrid.InvalidateRow(rowIndex);
            }
        }

        ShowSelectedJobEditorCell();
        UpdateJobEditorHistoryButtons();
    }

    private void BatchReplaceSelectedJobSImages()
    {
        if (!CommitJobDescriptionBoxEdit(showValidationMessage: true)) return;
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_currentJobEditorData == null)
        {
            MessageBox.Show(this, "请先读取兵种。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var selectedRows = GetSelectedRowsForBmpExport(_jobEditorGrid);
        var jobIds = new HashSet<int>();
        foreach (var row in selectedRows)
        {
            if (TryGetJobEditorRowIdentity(row, out var jobId, out _))
            {
                jobIds.Add(jobId);
            }
        }

        using var folderDialog = new FolderBrowserDialog
        {
            Description = "选择兵种 S 形象批量素材根目录；子目录使用 Job12，且包含 mov.bmp / atk.bmp / spc.bmp。",
            UseDescriptionForTitle = true
        };
        if (folderDialog.ShowDialog(this) != DialogResult.OK) return;

        var slots = SelectJobSBatchFactionSlots();
        if (slots.Count == 0) return;

        var request = new BatchJobSImageReplaceRequest
        {
            MaterialRoot = folderDialog.SelectedPath,
            AllowedJobIds = jobIds,
            IncludeOnlySelectedOrFiltered = jobIds.Count > 0,
            FactionSlots = slots,
            WriteMode = _project.IsTestCopy ? "test_copy" : "direct"
        };

        BatchJobSImageReplacePreviewResult preview;
        try
        {
            Cursor = Cursors.WaitCursor;
            preview = _batchJobSImageReplaceService.Preview(_project, request);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("批量导入兵种 S 形象预览失败：" + ex);
            MessageBox.Show(this, ex.Message, "批量导入兵种 S 形象预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        var previewText = BuildBatchJobSImageReplacePreviewText(preview);
        _jobEditorInfoBox.Text = previewText;
        if (!preview.CanWrite)
        {
            MessageBox.Show(this, previewText, "批量导入兵种 S 形象存在阻断项", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (MessageBox.Show(
                this,
                previewText + "\r\n\r\n确认后会把这些兵种 S 形象一次写入 Unit_*.e5，并自动备份。是否继续？",
                "确认批量导入兵种 S 形象",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = _batchJobSImageReplaceService.Replace(_project, request);
            ClearImageAssignmentCaches();
            if (_jobEditorGrid.CurrentRow != null)
            {
                UpdateJobSImagePreview(_jobEditorGrid.CurrentRow);
            }

            _jobEditorInfoBox.Text = BuildBatchJobSImageReplaceResultText(result);
            SetStatus($"批量导入兵种 S 形象完成：写入 {result.Results.Sum(item => item.Result.TotalOperationCount)} 条");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("批量导入兵种 S 形象失败：" + ex);
            MessageBox.Show(this, ex.Message, "批量导入兵种 S 形象失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private IReadOnlyList<int> SelectJobSBatchFactionSlots()
    {
        using var dialog = new Form
        {
            Text = "选择写入阵营",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(260, 180)
        };
        var list = new CheckedListBox
        {
            Dock = DockStyle.Top,
            Height = 110,
            CheckOnClick = true
        };
        list.Items.Add(CharacterImageResourceService.BuildSPreviewFactionText(1), true);
        list.Items.Add(CharacterImageResourceService.BuildSPreviewFactionText(2), true);
        list.Items.Add(CharacterImageResourceService.BuildSPreviewFactionText(3), true);
        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Left = 70, Top = 125, Width = 75 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Left = 155, Top = 125, Width = 75 };
        dialog.Controls.Add(list);
        dialog.Controls.Add(ok);
        dialog.Controls.Add(cancel);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog(this) != DialogResult.OK) return Array.Empty<int>();

        return list.CheckedIndices.Cast<int>().Select(index => index + 1).ToArray();
    }

    private static string BuildBatchJobSImageReplacePreviewText(BatchJobSImageReplacePreviewResult preview)
    {
        var builder = new StringBuilder();
        builder.AppendLine("批量导入兵种 S 形象预览");
        builder.AppendLine($"素材根目录：{preview.Request.MaterialRoot}");
        builder.AppendLine($"阵营槽：{string.Join("、", preview.Request.FactionSlots.Select(CharacterImageResourceService.BuildSPreviewFactionText))}");
        builder.AppendLine($"可写入兵种：{preview.Items.Count}");
        builder.AppendLine($"写入条目：{preview.TotalOperationCount}");
        foreach (var item in preview.Items.Take(30))
        {
            builder.AppendLine($"- Job{item.JobId}: {item.MaterialFolder} -> {item.Preview.TotalOperationCount} 条");
        }

        AppendBatchImageImportIssues(builder, preview.SkippedItems, preview.Warnings);
        return builder.ToString();
    }

    private static string BuildBatchJobSImageReplaceResultText(BatchJobSImageReplaceResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("批量导入兵种 S 形象完成");
        builder.AppendLine($"完成兵种：{result.Results.Count}");
        builder.AppendLine($"写入条目：{result.Results.Sum(item => item.Result.TotalOperationCount)}");
        foreach (var item in result.Results.Take(30))
        {
            builder.AppendLine($"- Job{item.JobId}: {item.Result.TotalOperationCount} 条");
        }

        AppendBatchImageImportIssues(builder, result.SkippedItems, result.Warnings);
        return builder.ToString();
    }

    private static string BuildJobSImageReplacePreviewText(JobSImageReplacePreviewResult preview, string jobName)
        => "一键替换兵种形象预览\r\n" +
           $"兵种：ID={preview.Request.JobId:D2}  名称={jobName}\r\n" +
           $"素材目录：{preview.Request.MaterialFolder}\r\n" +
           $"选择阵营：{string.Join("、", preview.Factions.Select(faction => faction.FactionName))}\r\n" +
           $"写入条目：{preview.TotalOperationCount} 条\r\n" +
           string.Join("\r\n", preview.Factions.Select(faction =>
               $"- {faction.FactionName}: {faction.Preview.Mapping.Detail}; " +
               string.Join("；", faction.Preview.Files.Select(file =>
                   $"{file.TargetFileName} <- {Path.GetFileName(file.SourcePath)} 图号 {string.Join(", ", file.BatchPreview.Operations.Select(op => "#" + op.ImageNumber.ToString(CultureInfo.InvariantCulture)))}")))) +
           "\r\n提示：" + (preview.Warnings.Count == 0 ? "无" : string.Join("；", preview.Warnings));

    private static string BuildJobSImageReplaceResultText(JobSImageReplaceResult result, string jobName)
        => "一键替换兵种形象完成\r\n" +
           $"兵种：ID={result.Request.JobId:D2}  名称={jobName}\r\n" +
           $"写入条目：{result.TotalOperationCount} 条\r\n" +
           string.Join("\r\n", result.Factions.Select(faction =>
               $"- {faction.FactionName}: Unit 图号 {string.Join("/", faction.Result.Mapping.ImageNumbers.Select(x => "#" + x.ToString(CultureInfo.InvariantCulture)))}；" +
               $"{faction.Result.TotalOperationCount} 条；报告 {faction.Result.AggregateReportPath}")) +
           "\r\n" +
           string.Join("\r\n", result.Factions.SelectMany(faction =>
               faction.Result.Files.Select(file => $"  {faction.FactionName} {file.TargetFileName}: 备份 {file.WriteResult.BackupPath}")));

    private void ClearJobAreaPreview(string message)
    {
        HideJobEquipmentEditor();
        SetPictureBoxImage(_jobAreaPreviewBox, null);
        SetStatus(message);
    }

    private void ValidateJobEditorCell(DataGridViewCellValidatingEventArgs e)
    {
        if (_jobEditorGrid.ReadOnly || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var column = _jobEditorGrid.Columns[e.ColumnIndex];
        if (column.ReadOnly) return;
        var columnName = column.DataPropertyName;
        var value = NormalizeJobAreaComboInput(columnName, Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture) ?? string.Empty);
        string? error = null;
        if (columnName == "名称")
        {
            var bytes = EncodingService.GetGbkByteCount(value);
            if (bytes > 9) error = $"详细兵种名称超长：GBK {bytes} 字节，容量 9 字节。";
        }
        else if (columnName == "介绍")
        {
            var bytes = EncodingService.GetGbkByteCount(value);
            if (bytes > 200) error = $"兵种说明超长：GBK {bytes} 字节，容量 200 字节。";
        }
        else if (columnName != "ID")
        {
            error = TryParseInteger(value, 0, byte.MaxValue, columnName, ShouldUseHexForJobEditorColumn(columnName));
        }

        _jobEditorGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = error ?? string.Empty;
        if (error == null) return;
        e.Cancel = true;
        _jobEditorInfoBox.Text = error;
        SetStatus(error);
    }

    private void SaveJobEditor()
    {
        if (_project == null || _currentJobEditorData == null || _jobNameRead == null || _jobDescriptionRead == null || _jobGrowthRead == null || _jobPierceRead == null) return;

        _jobEditorGrid.EndEdit();
        if (!CommitJobDescriptionBoxEdit(showValidationMessage: true)) return;
        CommitJobEquipmentEditorChanges();
        if (_currentJobEditorData.GetChanges() == null)
        {
            MessageBox.Show(this, "兵种设定没有检测到改动。", "无需保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var preview = BuildChangePreview(_currentJobEditorData, maxItems: 40);
        if (MessageBox.Show(this,
                $"即将保存兵种设定到当前 MOD 项目。\r\n\r\n变更预览：\r\n{preview}\r\n\r\n保存前会自动备份，保存后会重新读取校验。是否继续？",
                "确认保存兵种设定",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var changedCells = GetChangedCellKeys(_currentJobEditorData);
            var saves = SaveJobEditorData(_project, _currentJobEditorData);
            AcceptSavedDataTable(_currentJobEditorData);
            RefreshJobEditorCellsAfterCsvImport(changedCells);
            var changedBytes = saves.Sum(x => x.ChangedBytes);
            System.Diagnostics.Debug.WriteLine($"已保存兵种设定：保存表 {saves.Count} 个，变化字节 {changedBytes}");
            foreach (var save in saves) System.Diagnostics.Debug.WriteLine("兵种设定备份：" + save.BackupPath);
            SetStatus($"兵种设定保存完成并已复读：变化 {changedBytes} 字节");
            MessageBox.Show(this,
                $"保存完成并已重新读取校验。\r\n保存表数量：{saves.Count}\r\n变化字节：{changedBytes}\r\n备份：{string.Join("; ", saves.Select(x => x.BackupPath))}",
                "保存完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("保存兵种设定失败：" + ex);
            MessageBox.Show(this, ex.Message, "保存兵种设定失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private IReadOnlyList<TableSaveResult> SaveJobEditorData(CczProject project, DataTable jobData)
    {
        if (_jobNameRead == null || _jobDescriptionRead == null || _jobGrowthRead == null || _jobPierceRead == null) return Array.Empty<TableSaveResult>();
        foreach (DataRow jobRow in jobData.Rows)
        {
            if (jobRow.RowState != DataRowState.Modified) continue;
            var id = Convert.ToInt32(jobRow["ID"], CultureInfo.InvariantCulture);
            var nameRow = FindRowById(_jobNameRead.Data, id);
            var descriptionRow = FindRowById(_jobDescriptionRead.Data, id);
            var growthRow = FindRowById(_jobGrowthRead.Data, id);
            var pierceRow = FindRowById(_jobPierceRead.Data, id);

            if (IsRoleColumnChanged(jobRow, "名称")) nameRow["名称"] = jobRow["名称", DataRowVersion.Current];
            if (IsRoleColumnChanged(jobRow, "介绍")) descriptionRow["介绍"] = jobRow["介绍", DataRowVersion.Current];
            foreach (DataColumn column in _jobGrowthRead.Data.Columns)
            {
                if (column.ColumnName is "ID" or "名称") continue;
                if (!jobData.Columns.Contains(column.ColumnName) || !IsRoleColumnChanged(jobRow, column.ColumnName)) continue;
                growthRow[column.ColumnName] = jobRow[column.ColumnName, DataRowVersion.Current];
            }
            if (IsRoleColumnChanged(jobRow, "穿透")) pierceRow["穿透"] = jobRow["穿透", DataRowVersion.Current];
        }

        var saves = new List<TableSaveResult>();
        if (SaveChangedTableAndVerify(_jobNameRead) is { } nameSave) saves.Add(nameSave);
        if (SaveChangedTableAndVerify(_jobDescriptionRead) is { } descriptionSave) saves.Add(descriptionSave);
        if (SaveChangedTableAndVerify(_jobGrowthRead) is { } growthSave) saves.Add(growthSave);
        if (SaveChangedTableAndVerify(_jobPierceRead) is { } pierceSave) saves.Add(pierceSave);
        return saves;
    }

    private void OpenItemEditor()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SelectTabPageByText("宝物设定");
        LoadItemEditor();
    }

    private void LoadItemEditor()
    {
        if (_project == null) return;
        if (_tables.Count == 0)
        {
            ReloadCurrentProject();
            if (_tables.Count == 0) return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            _currentItemEditorData = BuildItemEditorData(_project, _tables);
            _itemEditorGrid.DataSource = _currentItemEditorData;
            ConfigureItemEditorGrid();
            _saveItemEditorButton.Enabled = true;
            _batchImportItemIconButton.Enabled = true;
            _editItemIconButton.Enabled = true;
            _exportItemIconBmpButton.Enabled = true;
            _exportItemEditorCsvButton.Enabled = true;
            _importItemEditorCsvButton.Enabled = true;
            ResetItemEditorHistory();
            _itemEditorInfoBox.Text = BuildItemEditorSummary(_currentItemEditorData);
            ShowSelectedItemEditorCell();
            SetStatus($"宝物设定读取完成：{_currentItemEditorData.Rows.Count} 行");
        }
        catch (Exception ex)
        {
            _itemEditorInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("读取宝物设定失败：" + ex);
            MessageBox.Show(this, ex.Message, "读取宝物设定失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private const string ItemDescriptionColumnName = "\u4ECB\u7ECD";

    private static bool IsItemDescriptionReadUsable(TableReadResult? read)
        => read?.Validation.IsUsable == true;

    private bool CanWriteItemDescriptions
        => IsItemDescriptionReadUsable(_itemDescriptionLowRead) &&
           IsItemDescriptionReadUsable(_itemDescriptionHighRead);

    private static readonly string[] ItemEditorDerivedColumnNames =
    [
        "物品大类",
        "项目类型",
        "类型样例",
        "类型来源",
        "类型说明",
        "价格显示",
        "装备特效名",
        "特效值",
        "实际效果号",
        "实际效果说明",
        "特效提示"
    ];
    private const string ItemEditorRawEffectColumnName = "原始装备特效号";
    private const string ItemEditorVisibleEffectValueColumnName = "特效值";

    private DataTable BuildItemEditorData(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        _itemBaseLowRead = _tableReader.Read(project, FindTable(tables, "6.5-1 物品（0-103）"), tables);
        _itemBaseHighRead = _tableReader.Read(project, FindTable(tables, "6.5-2 物品（104-255）"), tables);
        _itemDescriptionLowRead = _tableReader.Read(project, FindTable(tables, "6.5-1-1 物品说明（0-103）"), tables);
        _itemDescriptionHighRead = _tableReader.Read(project, FindTable(tables, "6.5-2-1 物品说明（104-255）"), tables);
        if (!_itemBaseLowRead.Validation.IsUsable || !_itemBaseHighRead.Validation.IsUsable)
        {
            throw new InvalidOperationException("物品基础表有不可读取项，请先查看数据表诊断。物品说明表缺失时会降级为空介绍，不再阻断基础物品读取。");
        }

        _itemEffectNames = BuildItemEffectNameLookup(project, tables);
        _currentEquipmentTypeProfile = _equipmentTypeProfileService.Build(project, tables, ResolveJobEquipmentStorageColumns());
        _jobEquipmentPermissionSlots = _currentEquipmentTypeProfile.JobPermissionSlots;

        var output = new DataTable("宝物设定");
        output.Columns.Add("ID", typeof(int));
        output.Columns.Add("分段", typeof(string));
        output.Columns.Add("大类", typeof(string));
        output.Columns.Add("物品大类", typeof(string));
        foreach (DataColumn column in _itemBaseLowRead.Data.Columns)
        {
            if (column.ColumnName == "ID") continue;
            output.Columns.Add(
                column.ColumnName,
                column.ColumnName == "装备特效号" ? typeof(int) : column.DataType);
        }
        if (!output.Columns.Contains(ItemEditorRawEffectColumnName))
        {
            output.Columns.Add(ItemEditorRawEffectColumnName, typeof(int));
        }
        output.Columns.Add("项目类型", typeof(string));
        output.Columns.Add("类型样例", typeof(string));
        output.Columns.Add("类型来源", typeof(string));
        output.Columns.Add("类型说明", typeof(string));
        output.Columns.Add("价格显示", typeof(string));
        output.Columns.Add("装备特效名", typeof(string));
        output.Columns.Add(ItemEditorVisibleEffectValueColumnName, typeof(int));
        output.Columns.Add("实际效果号", typeof(string));
        output.Columns.Add("实际效果说明", typeof(string));
        output.Columns.Add("特效提示", typeof(string));
        output.Columns.Add("介绍", typeof(string));
        output.Columns.Add("来源文件", typeof(string));

        var boundary = ItemCategoryBoundaryService.Resolve(project);
        AddItemEditorRows(output, _itemBaseLowRead, _itemDescriptionLowRead, "0-103", boundary);
        AddItemEditorRows(output, _itemBaseHighRead, _itemDescriptionHighRead, "104-255", boundary);

        output.AcceptChanges();
        return output;
    }

    private IReadOnlyList<ItemEffectCatalogEntry> BuildDefaultItemEffectCatalogEntries(CczProject project, IReadOnlyList<HexTableDefinition> tables)
        => _itemEffectNameReader.ReadBaseCatalogEntries(project, tables);

    private void AddItemEditorRows(DataTable output, TableReadResult itemRead, TableReadResult descriptionRead, string segment, ItemCategoryBoundary boundary)
    {
        var canReadDescription = IsItemDescriptionReadUsable(descriptionRead);
        foreach (DataRow source in itemRead.Data.Rows)
        {
            var id = Convert.ToInt32(source["ID"], CultureInfo.InvariantCulture);
            var row = output.NewRow();
            row["ID"] = id;
            row["分段"] = segment;
            row["大类"] = boundary.GetMajorCategory(id);
            foreach (DataColumn column in itemRead.Data.Columns)
            {
                if (column.ColumnName == "ID") continue;
                row[column.ColumnName] = source[column.ColumnName];
            }

            var typeId = output.Columns.Contains("类型")
                ? Convert.ToInt32(row["类型"], CultureInfo.InvariantCulture)
                : 0;
            var priceUnit = output.Columns.Contains("价格（/100）")
                ? Convert.ToInt32(row["价格（/100）"], CultureInfo.InvariantCulture)
                : 0;
            var catalog = output.Columns.Contains("宝物图鉴")
                ? Convert.ToInt32(row["宝物图鉴"], CultureInfo.InvariantCulture)
                : 0;
            var effectId = output.Columns.Contains("装备特效号")
                ? Convert.ToInt32(row["装备特效号"], CultureInfo.InvariantCulture)
                : 0;
            var rawEffectMarker = row.Table.Columns.Contains(Ccz66ItemLayoutService.RawEffectMarkerColumnName)
                ? Convert.ToInt32(row[Ccz66ItemLayoutService.RawEffectMarkerColumnName], CultureInfo.InvariantCulture)
                : effectId;
            row[ItemEditorRawEffectColumnName] = rawEffectMarker;
            var growth = output.Columns.Contains("升级能力成长")
                ? Convert.ToInt32(row["升级能力成长"], CultureInfo.InvariantCulture)
                : 0;
            var classification = ItemClassificationService.Classify(row, boundary);
            row["物品大类"] = classification.DisplayName;
            var majorCategory = classification.DisplayName;
            ApplyVisibleItemEffectCells(row, majorCategory, typeId, rawEffectMarker);
            var effect = ResolveVisibleItemEffect(row, majorCategory, typeId, rawEffectMarker);
            var effectValue = GetVisibleItemEffectValue(row, majorCategory);
            row[ItemEditorVisibleEffectValueColumnName] = effectValue;
            RefreshItemEditorTypeProfileCells(row, typeId, majorCategory);
            row["价格显示"] = BuildItemPriceText(priceUnit);
            row["装备特效名"] = BuildItemEffectNameDisplay(effect);
            row["实际效果号"] = $"装备特效号={GetVisibleItemEffectId(row, typeId, rawEffectMarker)}";
            row["实际效果说明"] = effect.Description;
            row["特效提示"] = ItemEffectInterpretationService.BuildEffectHint(
                majorCategory,
                typeId,
                GetVisibleItemEffectId(row, typeId, rawEffectMarker),
                Convert.ToString(row["装备特效名"], CultureInfo.InvariantCulture) ?? string.Empty,
                effectValue,
                growth);
            var descriptionRow = canReadDescription
                ? TryFindRowById(descriptionRead.Data, id)
                : null;
            row["介绍"] = descriptionRow == null || !descriptionRead.Data.Columns.Contains("介绍")
                ? string.Empty
                : Convert.ToString(descriptionRow["介绍"], CultureInfo.InvariantCulture) ?? string.Empty;
            row["来源文件"] = canReadDescription
                ? $"{itemRead.Table.FileName} / {descriptionRead.Table.FileName}"
                : $"{itemRead.Table.FileName} / 物品说明块不可用";
            output.Rows.Add(row);
        }
    }

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

    private string GetItemEffectName(int effectId)
    {
        if (effectId == 0) return "无特效/未启用";
        if (effectId == 255) return "普通装备/无扩展特效";
        return _itemEffectNames.TryGetValue(effectId, out var name)
            ? name
            : $"未命名或未确认：{HexDisplayFormatter.Format(effectId, 2)}";
    }

    private ItemEffectResolutionResult ResolveItemEffect(string majorCategory, int typeId, int effectId)
    {
        if (_project == null)
        {
            return new ItemEffectResolutionResult
            {
                RawEffectId = effectId,
                EffectiveEffectId = ItemEffectInterpretationService.ResolveEffectiveEffectId(majorCategory, typeId, effectId),
                DisplayName = GetItemEffectName(effectId),
                Description = GetItemEffectName(effectId),
                Source = "UiFallback",
                Confidence = "Unknown"
            };
        }

        return _itemEffectResolutionService.Resolve(_project, _tables, majorCategory, typeId, effectId);
    }

    private void OpenItemEffectCatalogEditor()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_tables.Count == 0)
        {
            ReloadCurrentProject();
            if (_tables.Count == 0) return;
        }

        var seedEntries = BuildDefaultItemEffectCatalogEntries(_project, _tables);
        var loadedEntries = _itemEffectCatalogService.Load(_project, seedEntries);
        var data = new DataTable("宝物特效");
        data.Columns.Add("特效号", typeof(int));
        data.Columns.Add("特效名", typeof(string));
        data.Columns.Add("特效说明", typeof(string));
        foreach (var entry in loadedEntries)
        {
            var row = data.NewRow();
            row["特效号"] = entry.EffectId;
            row["特效名"] = entry.Name;
            row["特效说明"] = entry.Description;
            data.Rows.Add(row);
        }
        data.AcceptChanges();

        using var dialog = new Form
        {
            Text = "宝物特效",
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = true
        };
        ApplyAdaptiveDialogSizing(dialog, new Size(980, 680), new Size(760, 480));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        dialog.Controls.Add(layout);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        var saveButton = new Button { Text = "保存宝物特效", AutoSize = true };
        var closeButton = new Button { Text = "关闭", AutoSize = true };
        toolbar.Controls.Add(saveButton);
        toolbar.Controls.Add(closeButton);
        layout.Controls.Add(toolbar, 0, 0);

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            DataSource = data,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.CellSelect
        };
        if (grid.Columns.Contains("特效号")) grid.Columns["特效号"]!.FillWeight = 18;
        if (grid.Columns.Contains("特效名")) grid.Columns["特效名"]!.FillWeight = 28;
        if (grid.Columns.Contains("特效说明")) grid.Columns["特效说明"]!.FillWeight = 54;
        layout.Controls.Add(grid, 0, 1);

        var infoBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Text =
                "项目侧宝物特效目录：保存到 CCZModStudio_Notes 下的 UTF-8 JSON，不修改 Ekd5.exe 固定宽度特效名称表。\r\n" +
                "表头：特效号 / 特效名 / 特效说明。特效号允许重复；特效名和特效说明支持变长中文。\r\n" +
                "宝物页会优先使用这里的映射显示特效名；同一特效号有多条记录时，宝物页会按“名称1 / 名称2”合并显示。"
        };
        grid.CellValidating += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var columnName = grid.Columns[e.ColumnIndex].DataPropertyName;
            if (columnName != "特效号") return;
            var value = Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture) ?? string.Empty;
            var error = TryParseInteger(value, 0, byte.MaxValue, columnName);
            grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = error ?? string.Empty;
            if (error == null) return;
            e.Cancel = true;
            infoBox.Text = error;
        };

        saveButton.Click += (_, _) =>
        {
            try
            {
                grid.EndEdit();
                for (var rowIndex = 0; rowIndex < data.Rows.Count; rowIndex++)
                {
                    var row = data.Rows[rowIndex];
                    var effectIdText = Convert.ToString(row["特效号"], CultureInfo.InvariantCulture) ?? string.Empty;
                    var effectNameText = Convert.ToString(row["特效名"], CultureInfo.InvariantCulture) ?? string.Empty;
                    var effectDescriptionText = Convert.ToString(row["特效说明"], CultureInfo.InvariantCulture) ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(effectIdText) &&
                        string.IsNullOrWhiteSpace(effectNameText) &&
                        string.IsNullOrWhiteSpace(effectDescriptionText))
                    {
                        continue;
                    }

                    var error = TryParseInteger(effectIdText, 0, byte.MaxValue, "特效号");
                    if (error != null)
                    {
                        throw new InvalidOperationException($"第 {rowIndex + 1} 行特效号无效：{error}");
                    }
                }

                var entries = data.Rows.Cast<DataRow>()
                    .Where(row => row.RowState != DataRowState.Deleted)
                    .Where(row =>
                        !(Convert.IsDBNull(row["特效号"]) &&
                          string.IsNullOrWhiteSpace(Convert.ToString(row["特效名"], CultureInfo.InvariantCulture)) &&
                          string.IsNullOrWhiteSpace(Convert.ToString(row["特效说明"], CultureInfo.InvariantCulture))))
                    .Select(row => new ItemEffectCatalogEntry
                    {
                        EffectId = Convert.ToInt32(row["特效号"], CultureInfo.InvariantCulture),
                        Name = Convert.ToString(row["特效名"], CultureInfo.InvariantCulture) ?? string.Empty,
                        Description = Convert.ToString(row["特效说明"], CultureInfo.InvariantCulture) ?? string.Empty
                    })
                    .ToList();

                var storePath = _itemEffectCatalogService.Save(_project, entries);
                _itemEffectNames = BuildItemEffectNameLookup(_project, _tables);
                if (_currentItemEditorData != null)
                {
                    LoadItemEditor();
                }

                infoBox.Text =
                    $"保存完成：{entries.Count} 条。\r\n" +
                    $"路径：{storePath}\r\n" +
                    "当前目录使用 UTF-8 JSON 保存，支持重复特效号与变长中文。";
                SetStatus($"宝物特效目录已保存：{entries.Count} 条");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("保存宝物特效目录失败：" + ex);
                MessageBox.Show(dialog, ex.Message, "保存宝物特效失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        closeButton.Click += (_, _) => dialog.Close();
        dialog.ShowDialog(this);
    }

    private string BuildItemTypeDescription(int typeId, string majorCategory, int catalog)
    {
        return GetEquipmentTypeDefinition(typeId, majorCategory).ShortDisplayName;
    }

    private ProjectEquipmentTypeDefinition GetEquipmentTypeDefinition(int typeId, string majorCategory)
    {
        if (_currentEquipmentTypeProfile != null)
        {
            return _currentEquipmentTypeProfile.GetTypeOrFallback(typeId, majorCategory);
        }

        var name = ItemTypeCatalogService.BuildShortName(typeId, majorCategory);
        return new ProjectEquipmentTypeDefinition(
            typeId,
            name,
            Array.Empty<string>(),
            ItemTypeCatalogService.TryGetEntry(typeId, out _) ? EquipmentTypeSourceConfidence.LegacyFallback : EquipmentTypeSourceConfidence.Unknown,
            "未读取当前项目 profile");
    }

    private void RefreshItemEditorTypeProfileCells(DataRow row, int typeId, string majorCategory)
    {
        var definition = GetEquipmentTypeDefinition(typeId, majorCategory);
        if (row.Table.Columns.Contains("项目类型")) row["项目类型"] = definition.ShortDisplayName;
        if (row.Table.Columns.Contains("类型样例")) row["类型样例"] = definition.SampleText;
        if (row.Table.Columns.Contains("类型来源")) row["类型来源"] = definition.SourceDisplayName;
        if (row.Table.Columns.Contains("类型说明")) row["类型说明"] = definition.ShortDisplayName;
    }

    private static string BuildItemPriceText(int priceUnit)
    {
        return priceUnit.ToString(CultureInfo.InvariantCulture);
    }

    private string BuildItemEffectNameDisplay(string majorCategory, int typeId, int effectId)
    {
        var effectiveEffectId = ItemEffectInterpretationService.ResolveEffectiveEffectId(majorCategory, typeId, effectId);
        return effectId is 0 or 255 || effectiveEffectId is 0 or 255
            ? "无"
            : GetItemEffectName(effectiveEffectId);
    }

    private static string BuildItemEffectNameDisplay(ItemEffectResolutionResult effect)
        => effect.RawEffectId is 0 or 255 ||
           effect.EffectiveEffectId is 0 or 255
            ? "无"
            : effect.DisplayName;

    private ItemEffectResolutionResult ResolveVisibleItemEffect(DataRow row, string majorCategory, int typeId, int rawEffectMarker)
    {
        var visibleEffectId = ReadNullableInt(row, "装备特效号") ?? rawEffectMarker;
        return ResolveItemEffect(majorCategory, typeId, visibleEffectId);
    }

    private void ApplyVisibleItemEffectCells(DataRow row, string majorCategory, int typeId, int rawEffectMarker)
    {
        if (!row.Table.Columns.Contains("装备特效号")) return;

        if (UsesTypeAsVisibleItemEffect(rawEffectMarker, majorCategory))
        {
            row["装备特效号"] = typeId;
        }
    }

    private static int GetVisibleItemEffectId(DataRow row, int typeId, int rawEffectMarker)
    {
        if (IsItemEditorTypeBackedVisibleEffectMarker(row))
        {
            return ReadNullableInt(row, "装备特效号") ?? typeId;
        }

        return ReadNullableInt(row, "装备特效号") ?? rawEffectMarker;
    }

    private static int GetVisibleItemEffectValue(DataRow row, string majorCategory)
    {
        if (IsConsumableMajorCategory(majorCategory) && row.Table.Columns.Contains("初始能力"))
        {
            return Convert.ToInt32(row["初始能力"], CultureInfo.InvariantCulture);
        }

        return row.Table.Columns.Contains("装备特效号-效果值")
            ? Convert.ToInt32(row["装备特效号-效果值"], CultureInfo.InvariantCulture)
            : 0;
    }

    private static void SetVisibleItemEffectValue(DataRow row, string majorCategory, int value)
    {
        if (IsConsumableMajorCategory(majorCategory))
        {
            if (row.Table.Columns.Contains("初始能力")) row["初始能力"] = value;
            if (row.Table.Columns.Contains(ItemEditorVisibleEffectValueColumnName)) row[ItemEditorVisibleEffectValueColumnName] = value;
            return;
        }

        if (row.Table.Columns.Contains("装备特效号-效果值")) row["装备特效号-效果值"] = value;
        if (row.Table.Columns.Contains(ItemEditorVisibleEffectValueColumnName)) row[ItemEditorVisibleEffectValueColumnName] = value;
    }

    private int GetAndSynchronizeVisibleItemEffectIdForRefresh(
        DataRow row,
        string majorCategory,
        int typeId,
        int rawEffectMarker,
        string? editedColumnName)
    {
        if (!UsesTypeAsVisibleItemEffect(rawEffectMarker, majorCategory))
        {
            return ReadNullableInt(row, "装备特效号") ?? rawEffectMarker;
        }

        var visibleEffectId = ReadNullableInt(row, "装备特效号");
        if (string.Equals(editedColumnName, "装备特效号", StringComparison.Ordinal) && visibleEffectId.HasValue)
        {
            if (row.Table.Columns.Contains("类型")) row["类型"] = visibleEffectId.Value;
            return visibleEffectId.Value;
        }

        if (string.Equals(editedColumnName, "类型", StringComparison.Ordinal))
        {
            row["装备特效号"] = typeId;
            return typeId;
        }

        if (!row.HasVersion(DataRowVersion.Original))
        {
            row["装备特效号"] = typeId;
            return typeId;
        }

        var typeChanged = IsRoleColumnChanged(row, "类型");
        var effectChanged = IsRoleColumnChanged(row, "装备特效号");
        if (effectChanged && visibleEffectId.HasValue && (!typeChanged || visibleEffectId.Value == typeId))
        {
            row["类型"] = visibleEffectId.Value;
            return visibleEffectId.Value;
        }

        if (typeChanged && (!effectChanged || !visibleEffectId.HasValue || visibleEffectId.Value == typeId))
        {
            row["装备特效号"] = typeId;
            return typeId;
        }

        if (visibleEffectId.HasValue)
        {
            return visibleEffectId.Value;
        }

        row["装备特效号"] = typeId;
        return typeId;
    }

    private static bool IsItemEditorAccessoryMarker(DataRow row)
    {
        var majorCategory = row.Table.Columns.Contains("物品大类")
            ? Convert.ToString(row["物品大类"], CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;
        return IsAccessoryMarker(GetItemEditorRawEffectMarker(row, fallbackEffectId: 0), majorCategory);
    }

    private static bool IsItemEditorConsumableMarker(DataRow row)
    {
        var majorCategory = row.Table.Columns.Contains("物品大类")
            ? Convert.ToString(row["物品大类"], CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;
        return IsConsumableMarker(GetItemEditorRawEffectMarker(row, fallbackEffectId: 0), majorCategory);
    }

    private static bool IsItemEditorTypeBackedVisibleEffectMarker(DataRow row)
    {
        var majorCategory = row.Table.Columns.Contains("物品大类")
            ? Convert.ToString(row["物品大类"], CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;
        return UsesTypeAsVisibleItemEffect(GetItemEditorRawEffectMarker(row, fallbackEffectId: 0), majorCategory);
    }

    private static bool IsAccessoryMarker(int rawEffectMarker, string majorCategory)
        => rawEffectMarker == 2 && string.Equals(majorCategory, "辅助装备", StringComparison.Ordinal);

    private static bool IsConsumableMarker(int rawEffectMarker, string majorCategory)
        => rawEffectMarker == 3 && string.Equals(majorCategory, "道具/消耗品", StringComparison.Ordinal);

    private static bool IsConsumableMajorCategory(string majorCategory)
        => ConsumableItemEffectCatalogService.IsConsumableCategory(majorCategory);

    private static bool UsesTypeAsVisibleItemEffect(int rawEffectMarker, string majorCategory)
        => IsAccessoryMarker(rawEffectMarker, majorCategory) ||
           IsConsumableMarker(rawEffectMarker, majorCategory);

    private static int? ReadNullableInt(DataRow row, string columnName)
    {
        if (!row.Table.Columns.Contains(columnName)) return null;
        var value = row[columnName];
        if (value == null || value == DBNull.Value) return null;
        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(text)
            ? null
            : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    // Keep these wrappers temporarily so existing smoke tests and private-call sites can continue to resolve.
    private static int ResolveEffectiveItemEffectId(string majorCategory, int typeId, int effectId)
        => ItemEffectInterpretationService.ResolveEffectiveEffectId(majorCategory, typeId, effectId);

    private static string BuildItemEffectiveEffectIdText(string majorCategory, int typeId, int effectId)
        => ItemEffectInterpretationService.BuildEffectiveEffectIdText(majorCategory, typeId, effectId);

    private string BuildItemEffectiveEffectDescription(string majorCategory, int typeId, int effectId, int effectiveEffectId)
        => ItemEffectInterpretationService.BuildEffectiveEffectDescription(majorCategory, typeId, effectId, effectiveEffectId, GetItemEffectName);

    private static string BuildItemEffectHint(string majorCategory, int typeId, int effectId, string effectName, int effectValue, int growth)
        => ItemEffectInterpretationService.BuildEffectHint(majorCategory, typeId, effectId, effectName, effectValue, growth);

    private static bool IsHiddenItemEditorColumn(string columnName)
        => columnName is "分段"
            or "大类"
            or "类型说明"
            or "价格显示"
            or "装备特效号-效果值"
            or "实际效果号"
            or "实际效果说明"
            or "特效提示"
            or ItemEditorRawEffectColumnName
            or "来源文件";

    private static bool IsVisibleItemEditorColumn(string columnName)
        => columnName is "ID"
            or "图标"
            or "名称"
            or "物品大类"
            or "类型"
            or "初始能力"
            or "升级能力成长"
            or "价格（/100）"
            or "装备特效号"
            or "装备特效名"
            or ItemEditorVisibleEffectValueColumnName
            or "宝物图鉴"
            or "介绍";

    private static string BuildItemEditorColumnHeader(string columnName)
        => columnName switch
        {
            "类型" => "类型码",
            "项目类型" => "项目类型",
            "类型样例" => "类型样例",
            "类型来源" => "来源",
            "升级能力成长" => "能力成长",
            "装备特效号" => "特效号",
            "装备特效名" => "特效名",
            "装备特效号-效果值" => "特效值",
            ItemEditorVisibleEffectValueColumnName => "特效值",
            "宝物图鉴" => "图鉴",
            _ => columnName
        };

    private void ApplyItemEditorColumnDisplayOrder()
    {
        var displayOrder = new[]
        {
            "ID",
            "名称",
            "图标",
            "物品大类",
            "类型",
            "初始能力",
            "升级能力成长",
            "价格（/100）",
            "装备特效号",
            "装备特效名",
            ItemEditorVisibleEffectValueColumnName,
            "宝物图鉴",
            "介绍"
        };

        var displayIndex = 0;
        foreach (var propertyName in displayOrder)
        {
            var column = _itemEditorGrid.Columns
                .Cast<DataGridViewColumn>()
                .FirstOrDefault(candidate => candidate.DataPropertyName == propertyName);
            if (column == null || !column.Visible) continue;
            column.DisplayIndex = displayIndex++;
        }
    }

    private void ReplaceItemTypeColumnWithCombo()
    {
        if (_currentItemEditorData == null) return;
        if (!_itemEditorGrid.Columns.Contains("类型")) return;
        if (_itemEditorGrid.Columns["类型"] is DataGridViewComboBoxColumn) return;

        var old = _itemEditorGrid.Columns["类型"];
        var index = old.Index;
        _itemEditorGrid.Columns.Remove(old);
        var combo = new DataGridViewComboBoxColumn
        {
            Name = "类型",
            DataPropertyName = "类型",
            DataSource = BuildItemTypeLookup(_currentItemEditorData),
            ValueMember = "ID",
            DisplayMember = "显示",
            DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
            FlatStyle = FlatStyle.Flat,
            SortMode = DataGridViewColumnSortMode.Automatic,
            Width = 150
        };
        _itemEditorGrid.Columns.Insert(index, combo);
    }

    private DataTable BuildItemTypeLookup(DataTable itemData)
    {
        var names = Enumerable.Range(0, byte.MaxValue + 1)
            .ToDictionary(
                id => id,
                id => GetEquipmentTypeDefinition(id, string.Empty).BuildComboText());

        if (itemData.Columns.Contains("类型"))
        {
            foreach (DataRow row in itemData.Rows)
            {
                var typeId = Convert.ToInt32(row["类型"], CultureInfo.InvariantCulture);
                var majorCategory = row.Table.Columns.Contains("物品大类")
                    ? Convert.ToString(row["物品大类"], CultureInfo.InvariantCulture) ?? string.Empty
                    : row.Table.Columns.Contains("大类")
                        ? Convert.ToString(row["大类"], CultureInfo.InvariantCulture) ?? string.Empty
                    : string.Empty;
                names[typeId] = GetEquipmentTypeDefinition(typeId, majorCategory).BuildComboText();
            }
        }

        var lookup = new DataTable("ItemTypeLookup");
        lookup.Columns.Add("ID", typeof(int));
        lookup.Columns.Add("显示", typeof(string));
        foreach (var pair in names.OrderBy(pair => pair.Key))
        {
            lookup.Rows.Add(pair.Key, pair.Value);
        }

        return lookup;
    }

    private void ConfigureItemEditorGrid()
    {
        if (_currentItemEditorData == null) return;
        _itemEditorGrid.ReadOnly = false;
        ReplaceItemTypeColumnWithCombo();
        foreach (DataGridViewColumn column in _itemEditorGrid.Columns)
        {
            column.Visible = IsVisibleItemEditorColumn(column.DataPropertyName) && !IsHiddenItemEditorColumn(column.DataPropertyName);
            column.ReadOnly = IsItemEditorUserReadOnlyColumn(column.DataPropertyName);
            column.DefaultCellStyle.BackColor = column.ReadOnly ? Color.FromArgb(245, 245, 245) : Color.Empty;
            column.DefaultCellStyle.ForeColor = column.ReadOnly ? SystemColors.GrayText : SystemColors.ControlText;
            column.ToolTipText = BuildItemColumnAnnotation(column.DataPropertyName);
            column.HeaderText = BuildItemEditorColumnHeader(column.DataPropertyName);
            if (column.DataPropertyName == "ID") column.Width = 48;
            if (column.DataPropertyName == "图标") column.Width = 56;
            if (column.DataPropertyName == "名称") column.Width = 130;
            if (column.DataPropertyName == "物品大类") column.Width = 92;
            if (column.DataPropertyName == "类型") column.Width = 220;
            if (column.DataPropertyName == "宝物图鉴") column.Width = 60;
            if (column.DataPropertyName == "装备特效名")
            {
                column.MinimumWidth = 120;
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            }
            if (column.DataPropertyName == "介绍") column.Width = 300;
        }

        ApplyItemEditorColumnDisplayOrder();
        RefreshItemEditorRowStyles();
    }

    private bool IsItemEditorUserReadOnlyColumn(string columnName)
        => IsItemEditorAlwaysUserReadOnlyColumn(columnName) ||
           columnName == ItemDescriptionColumnName && !CanWriteItemDescriptions;

    private static bool IsItemEditorAlwaysUserReadOnlyColumn(string columnName)
        => columnName is "ID" or "分段" or "大类" or "来源文件" ||
           columnName == ItemEditorRawEffectColumnName ||
           columnName != ItemEditorVisibleEffectValueColumnName &&
           ItemEditorDerivedColumnNames.Contains(columnName, StringComparer.Ordinal);

    private bool IsItemEditorUserEditableColumn(string columnName)
        => !IsItemEditorUserReadOnlyColumn(columnName);

    private string BuildItemColumnAnnotation(string columnName)
    {
        if (columnName == "ID") return "物品/宝物编号；0-103 来源 Data.e5，104 以后来源 Star.e5。";
        if (columnName == "分段") return "物品表分段，只读显示，用于确认写回 Data.e5 或 Star.e5。";
        if (columnName == "大类") return "按 B形象指定器 System.ini 的 DefID/AssID 分段推断：AssID..255 只是辅助段编码范围，仍需按物品内容区分辅助装备/道具。";
        if (columnName == "物品大类") return "内容感知分类：武器、防具、辅助装备、道具/消耗品、预留/空位。辅助段内按装备特效号 2/3 区分。";
        if (columnName == "类型") return "物品表的类型码字段，旧工具常显示为“类别”；它不是兵种设定里的可装备类别。保存仍写回原始单字节类型码。";
        if (columnName == "项目类型") return "按当前 MOD 的 Data.e5/Star.e5 物品样例、Ekd5.exe 装备类型名称表和可选人工 JSON 解析出的项目化类型名，只读显示。";
        if (columnName == "类型样例") return "当前项目中同一类型码的物品样例，用来判断作者自定义分类，例如弩、戟、长柄、炮车等。";
        if (columnName == "类型来源") return "项目类型名来源：人工确认 JSON > EXE 名称表 > Data/Star 样例推断 > 旧内置目录兜底 > 待确认。";
        if (columnName == "宝物图鉴") return "物品 25 字节行最后一字节；按旧资料作为宝物图鉴/宝物标记处理，不作为辅助装备可装备部队列表。";
        if (columnName == "类型说明") return "隐藏筛选列：保留项目化类型短名，兼容旧筛选/烟测入口。";
        if (columnName == "价格显示") return "隐藏重复列；界面只保留原始可编辑的“价格（/100）”。";
        if (columnName == "装备特效号") return "武器/防具显示原始装备特效号；辅助装备和道具/消耗品显示与特效名一致的真实特效号，底层写回类型字段并保留原始 2/3 类别标记。";
        if (columnName == "装备特效名") return "根据可见特效号读取中文特效名称；辅助装备和道具/消耗品都与真实特效号绑定刷新。";
        if (columnName == "实际效果号") return "隐藏诊断列：武器/防具取装备特效号；辅助装备和道具/消耗品在原始 2/3 类别标记下取类型字段作为真实特效号。";
        if (columnName == "实际效果说明") return "对原始字段的保守纠偏说明，用于避免把辅助装备/道具类别标记误读成真实装备特效。";
        if (columnName == ItemEditorVisibleEffectValueColumnName) return "可见特效值：武器/防具/辅助装备写回“装备特效号-效果值”；道具/消耗品对齐 B形象指定器，写回“初始能力”字节。";
        if (columnName == "特效提示") return "把特效号、可见特效值和成长集中提示；辅助/道具段会明确提示 2/3 可能只是类别标记，具体参数仍需对照旧工具和实机。";
        if (columnName == "图标")
        {
            return _project != null && Ccz66RevisedLayout.Is66(_project)
                ? "物品表图标字段号；6.6 按字段 N 映射 E5\\Item.e5 小图 #2N+1、大图 #2N+2，默认预览大图。"
                : "物品图标编号；界面会按该编号从 Itemicon.dll 提取候选图标预览，最终映射仍建议实机确认。";
        }
        if (columnName == ItemDescriptionColumnName)
        {
            return CanWriteItemDescriptions
                ? "物品说明文本，写入 Imsg.e5；固定 200 字节 GBK 容量。"
                : "当前项目的 Imsg.e5 物品说明块缺失或不匹配，此列只读显示为空，不会向文件尾追加说明数据。";
        }
        if (columnName == "来源文件") return "本行基础字段与说明字段的目标文件，只读显示。";
        if (_itemBaseLowRead?.Table.Fields.FirstOrDefault(f => f.ColumnName == columnName) is { } field)
        {
            return _fieldAnnotationService.BuildFieldAnnotation(_itemBaseLowRead.Table, field);
        }
        return columnName;
    }

    private string BuildItemEditorSummary(DataTable data)
    {
        var named = data.Rows.Cast<DataRow>().Count(row => !string.IsNullOrWhiteSpace(Convert.ToString(row["名称"], CultureInfo.InvariantCulture)));
        var low = data.Rows.Cast<DataRow>().Count(row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) <= 103);
        var high = data.Rows.Count - low;
        var kindGroups = data.Rows.Cast<DataRow>()
            .GroupBy(row => Convert.ToString(row["物品大类"], CultureInfo.InvariantCulture) ?? "未知")
            .OrderBy(group => group.Key, StringComparer.CurrentCulture)
            .Select(group => $"{group.Key}{group.Count()}");
        var typeSourceGroups = data.Columns.Contains("类型来源")
            ? data.Rows.Cast<DataRow>()
                .GroupBy(row => Convert.ToString(row["类型来源"], CultureInfo.InvariantCulture) ?? "待确认")
                .OrderBy(group => group.Key, StringComparer.CurrentCulture)
                .Select(group => $"{group.Key}{group.Count()}")
            : Array.Empty<string>();
        var notesPath = _currentEquipmentTypeProfile?.NotesPath ?? ProjectEquipmentTypeProfileService.BuildNotesPath(_project!);
        var descriptionStatus = CanWriteItemDescriptions
            ? "物品说明表可读写，保存会写回 Imsg.e5。"
            : "物品说明块不可用：介绍列只读为空，保存只写 Data.e5/Star.e5 基础字段，不会追加或覆盖 Imsg.e5 越界区域。";
        return
            $"宝物设定已读取：总行 {data.Rows.Count}，已命名 {named}，0-103 段 {low} 行，104+ 段 {high} 行。\r\n" +
            $"物品大类：{string.Join("，", kindGroups)}。\r\n" +
            $"项目类型来源：{string.Join("，", typeSourceGroups)}；人工校正文件：{notesPath}\r\n" +
            $"{descriptionStatus}\r\n" +
            "界面按旧版宝物编辑器顺序显示：ID、名称、图标、物品大类、类型码、初始能力、能力成长、价格、特效号、特效名、特效值、图鉴、介绍；隐藏分段/项目类型诊断/长解释列和重复价格列。\r\n" +
            (Ccz66RevisedLayout.Is66(_project!)
                ? "右侧图标预览按“图标”字段映射 E5\\Item.e5 小图 #2N+1 / 大图 #2N+2，保存仍写回原始字段。\r\n"
                : "右侧图标预览按“图标”字段从 Itemicon.dll 枚举候选图标，保存仍写回原始字段。\r\n") +
            "保存前自动备份，保存后重新读取校验。";
    }

    private void ApplyItemEditorFilter()
    {
        if (_currentItemEditorData == null) return;
        var keyword = _itemEditorSearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            _currentItemEditorData.DefaultView.RowFilter = string.Empty;
            SetStatus("宝物筛选已清除");
            return;
        }

        var escaped = EscapeDataViewLikeValue(keyword);
        var filters = _currentItemEditorData.Columns
            .Cast<DataColumn>()
            .Where(column => column.ColumnName is "ID" or "分段" or "大类" or "物品大类" or "名称" or "类型" or "项目类型" or "类型样例" or "类型来源" or "类型说明" or "装备特效号" or "装备特效名" or "特效提示" or "价格（/100）" or "图标" or "宝物图鉴" or "介绍")
            .Select(column => $"CONVERT([{column.ColumnName}], 'System.String') LIKE '*{escaped}*'");
        _currentItemEditorData.DefaultView.RowFilter = string.Join(" OR ", filters);
        SetStatus($"宝物筛选：{_currentItemEditorData.DefaultView.Count}/{_currentItemEditorData.Rows.Count}");
    }

    private void ClearItemEditorFilter()
    {
        _itemEditorSearchBox.Clear();
        if (_currentItemEditorData != null) _currentItemEditorData.DefaultView.RowFilter = string.Empty;
        SetStatus("宝物筛选已清除");
    }

    private void ExportItemEditorCsv()
        => ExportItemEditorVisibleCsv();

    private void ExportItemEditorVisibleCsv()
    {
        if (_currentItemEditorData == null) return;
        var columns = new[]
            {
                "ID",
                "名称",
                "图标",
                "物品大类",
                "类型",
                "初始能力",
                "升级能力成长",
                "价格（/100）",
                "装备特效号",
                "装备特效名",
                ItemEditorVisibleEffectValueColumnName,
                "宝物图鉴",
                "介绍"
            }
            .Where(_currentItemEditorData.Columns.Contains)
            .ToArray();

        ExportDataTableCsv(_currentItemEditorData, "宝物设定.csv", columns);
    }

    private void ImportItemEditorCsv()
    {
        var table = _currentItemEditorData;
        if (table == null) return;

        using var dialog = new OpenFileDialog
        {
            Title = "导入宝物设定 CSV",
            Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var importResult = CsvService.ImportIntoWithChanges(
                table,
                dialog.FileName,
                allowPartialColumns: true,
                matchByIdWhenPresent: true,
                IsItemEditorUserEditableColumn);
            var count = importResult.ImportedRows;
            var changedCells = importResult.ChangedCells
                .Select(cell => new GridCellKey(cell.RowKey, cell.RowIndex, cell.ColumnName))
                .ToList();
            RefreshChangedGridCells(_itemEditorGrid, changedCells, UpdateItemEditorDerivedCells);
            RefreshChangedGridRowsOnly(_itemEditorGrid, changedCells, RefreshItemEditorRowStyle);
            ShowSelectedItemEditorCell();
            ResetItemEditorHistory();
            System.Diagnostics.Debug.WriteLine($"已导入宝物设定 CSV：{dialog.FileName}，更新行 {count}");
            var skippedText = importResult.SkippedReadOnlyCells > 0
                ? $"，跳过只读/匹配列单元格 {importResult.SkippedReadOnlyCells} 个"
                : string.Empty;
            SetStatus($"宝物设定 CSV 导入完成：更新 {count} 行{skippedText}，请检查后保存。");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("宝物设定 CSV 导入失败：" + ex);
            MessageBox.Show(this, ex.Message, "宝物设定 CSV 导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static IReadOnlyList<string> GetItemEditorDerivedColumnNames()
        => ItemEditorDerivedColumnNames;

    private void RefreshItemEditorAfterBulkEdit()
    {
        if (_currentItemEditorData != null)
        {
            foreach (DataRow row in _currentItemEditorData.Rows)
            {
                RefreshItemEditorDerivedCells(row);
            }
        }

        RefreshItemEditorRowStyles();
        ShowSelectedItemEditorCell();
        UpdateItemEditorHistoryButtons();
    }

    private static bool IsPotentialItemEditorTextInput(KeyEventArgs e)
        => IsPotentialJobEditorTextInput(e);

    private void SnapshotItemEditorSelectionForEdit()
    {
        var targets = CaptureItemEditorSelectedTargets();
        if (targets.Count > 1)
        {
            _itemEditorSelectionSnapshotTargets = targets;
        }
    }

    private void MarkItemEditorSelectionChangeFromMouse()
    {
        _itemEditorSelectionChangeStartedByMouse = true;
        _itemEditorSelectionSnapshotTargets = [];
    }

    private void HandleItemEditorSelectionChanged()
    {
        if (!_applyingItemEditorHistory && !_itemEditorGrid.IsCurrentCellInEditMode)
        {
            var targets = CaptureItemEditorSelectedTargets();
            if (_itemEditorSelectionChangeStartedByMouse)
            {
                _itemEditorSelectionSnapshotTargets = targets;
                _itemEditorSelectionChangeStartedByMouse = false;
                ShowSelectedItemEditorCell();
                return;
            }

            if (targets.Count > 1 ||
                targets.Count == 0 ||
                _itemEditorSelectionSnapshotTargets.Count == 0 ||
                !ItemEditorTargetListContains(_itemEditorSelectionSnapshotTargets, targets[0]))
            {
                _itemEditorSelectionSnapshotTargets = targets;
            }
        }

        ShowSelectedItemEditorCell();
    }

    private void BeginItemEditorCellEdit(int rowIndex, int columnIndex)
    {
        if (_applyingItemEditorHistory || rowIndex < 0 || columnIndex < 0)
        {
            _itemEditorPendingCellEditOriginals = [];
            return;
        }

        _itemEditorPendingCellEditOriginals = CaptureItemEditorSelectionOriginals(rowIndex, columnIndex);
    }

    private void CompleteItemEditorCellEdit(int rowIndex, int columnIndex)
    {
        if (_applyingItemEditorHistory || rowIndex < 0 || columnIndex < 0) return;
        if (_itemEditorPendingCellEditOriginals.Count == 0)
        {
            UpdateItemEditorDerivedCells(rowIndex, columnIndex);
            return;
        }

        var column = _itemEditorGrid.Columns[columnIndex];
        var columnName = column.DataPropertyName;
        var editedRow = TryGetDataRow(_itemEditorGrid.Rows[rowIndex]);
        if (editedRow == null)
        {
            _itemEditorPendingCellEditOriginals = [];
            return;
        }

        var source = _itemEditorPendingCellEditOriginals
            .FirstOrDefault(edit => ReferenceEquals(edit.Row, editedRow) &&
                                    string.Equals(edit.ColumnName, columnName, StringComparison.Ordinal));
        if (source == null)
        {
            _itemEditorPendingCellEditOriginals = [];
            UpdateItemEditorDerivedCells(rowIndex, columnIndex);
            return;
        }

        var newValue = NormalizeGridCellValue(source.Row[columnName]);
        var edits = new List<ItemEditorCellEdit>
        {
            new(source.Row, columnName, source.OldValue, newValue)
        };

        if (_itemEditorPendingCellEditOriginals.Count > 1)
        {
            var text = FormatGridValueForBatchInput(newValue);
            foreach (var target in _itemEditorPendingCellEditOriginals)
            {
                if (ReferenceEquals(target.Row, source.Row) &&
                    string.Equals(target.ColumnName, columnName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryValidateItemEditorCellText(target.ColumnName, text, out var error))
                {
                    _itemEditorInfoBox.Text = error;
                    SetStatus(error);
                    continue;
                }

                var targetNewValue = ConvertItemEditorValueForColumn(target.ColumnName, text);
                if (Equals(target.OldValue, targetNewValue)) continue;
                target.Row[target.ColumnName] = targetNewValue ?? DBNull.Value;
                RefreshItemEditorDerivedCells(target.Row);
                edits.Add(new ItemEditorCellEdit(target.Row, target.ColumnName, target.OldValue, NormalizeGridCellValue(target.Row[target.ColumnName])));
            }
        }

        _itemEditorPendingCellEditOriginals = [];
        RefreshItemEditorDerivedCells(source.Row);
        PushItemEditorHistory(edits);
        RefreshItemEditorCellsAfterEdits(edits);
        if (edits.Count > 1)
        {
            SetStatus($"宝物设定已将当前输入应用到选区：{edits.Count} 个单元格。");
        }
        else if (edits.Count == 1 && !Equals(edits[0].OldValue, edits[0].NewValue))
        {
            SetStatus("宝物设定已更新 1 个单元格。");
        }
    }

    private void PasteItemEditorSelection()
    {
        if (_itemEditorGrid.ReadOnly)
        {
            SetStatus("当前表格不可编辑。");
            return;
        }

        if (!Clipboard.ContainsText())
        {
            SetStatus("剪贴板没有文本。");
            return;
        }

        var start = GetItemEditorPasteStartCell();
        if (start == null)
        {
            SetStatus("请先选中粘贴起点。");
            return;
        }

        if (!_itemEditorGrid.EndEdit())
        {
            SetStatus("当前单元格未能提交，无法粘贴。");
            return;
        }

        var matrix = ParseClipboardMatrix(Clipboard.GetText());
        var edits = new List<ItemEditorCellEdit>();
        var lastCell = start.Value;
        for (var r = 0; r < matrix.Count; r++)
        {
            var rowIndex = start.Value.Row + r;
            if (rowIndex >= _itemEditorGrid.Rows.Count) break;

            for (var c = 0; c < matrix[r].Count; c++)
            {
                var columnIndex = start.Value.Column + c;
                if (columnIndex >= _itemEditorGrid.Columns.Count) break;

                if (TrySetItemEditorCellValue(rowIndex, columnIndex, matrix[r][c], edits, out _))
                {
                    lastCell = (rowIndex, columnIndex);
                }
            }
        }

        PushItemEditorHistory(edits);
        if (edits.Count > 0)
        {
            _itemEditorGrid.CurrentCell = _itemEditorGrid.Rows[lastCell.Row].Cells[lastCell.Column];
            RefreshItemEditorCellsAfterEdits(edits);
            RefreshItemEditorRowStyles();
        }

        SetStatus($"宝物设定粘贴完成：更新 {edits.Count} 个单元格。");
    }

    private void FillItemEditorSelectionWithCurrentValue()
    {
        if (_itemEditorGrid.ReadOnly)
        {
            SetStatus("当前表格不可编辑。");
            return;
        }

        if (_itemEditorGrid.CurrentCell == null)
        {
            SetStatus("请先选中用于批量填列的单元格。");
            return;
        }

        if (!_itemEditorGrid.EndEdit())
        {
            SetStatus("当前单元格未能提交，无法批量填列。");
            return;
        }

        var value = GetItemEditorCurrentInputText();
        var targets = GetItemEditorBatchFillTargets();
        if (targets.Count <= 1)
        {
            SetStatus("请先滑动选中多个要批量填列的单元格。");
            return;
        }

        var edits = new List<ItemEditorCellEdit>();
        var lastError = string.Empty;
        foreach (var target in targets)
        {
            if (!TrySetItemEditorCellTargetValue(target, value, edits, out var error))
            {
                lastError = error;
            }
        }

        PushItemEditorHistory(edits);
        if (edits.Count > 0)
        {
            RefreshItemEditorCellsAfterEdits(edits);
            RefreshItemEditorRowStyles();
        }
        SetStatus(edits.Count > 0
            ? $"宝物设定批量填列完成：更新 {edits.Count} 个单元格。"
            : string.IsNullOrWhiteSpace(lastError) ? "宝物设定批量填列没有产生改动。" : lastError);
    }

    private List<ItemEditorCellEdit> CaptureItemEditorSelectionOriginals(int fallbackRowIndex, int fallbackColumnIndex)
    {
        var targets = CaptureItemEditorSelectedTargets();
        var fallbackTarget = TryCreateItemEditorCellTarget(fallbackRowIndex, fallbackColumnIndex, out var currentTarget)
            ? currentTarget
            : null;
        if (fallbackTarget != null &&
            targets.Count <= 1 &&
            _itemEditorSelectionSnapshotTargets.Count > 1 &&
            ItemEditorTargetListContains(_itemEditorSelectionSnapshotTargets, fallbackTarget))
        {
            targets = _itemEditorSelectionSnapshotTargets;
        }
        else if (targets.Count == 0 && fallbackTarget != null)
        {
            targets.Add(fallbackTarget);
        }

        return targets
            .Where(target => IsItemEditorTargetValid(target))
            .Select(target => new ItemEditorCellEdit(target.Row, target.ColumnName, NormalizeGridCellValue(target.Row[target.ColumnName]), null))
            .ToList();
    }

    private List<ItemEditorCellTarget> GetItemEditorBatchFillTargets()
    {
        var targets = CaptureItemEditorSelectedTargets();
        if (targets.Count > 1 || _itemEditorGrid.CurrentCell == null)
        {
            return targets;
        }

        if (TryCreateItemEditorCellTarget(
                _itemEditorGrid.CurrentCell.RowIndex,
                _itemEditorGrid.CurrentCell.ColumnIndex,
                out var currentTarget) &&
            _itemEditorSelectionSnapshotTargets.Count > 1 &&
            ItemEditorTargetListContains(_itemEditorSelectionSnapshotTargets, currentTarget))
        {
            return _itemEditorSelectionSnapshotTargets;
        }

        return targets;
    }

    private string GetItemEditorCurrentInputText()
    {
        if (_itemEditorGrid.IsCurrentCellInEditMode &&
            _itemEditorGrid.EditingControl is TextBoxBase textBox)
        {
            return textBox.Text;
        }

        return FormatGridValueForBatchInput(_itemEditorGrid.CurrentCell?.Value);
    }

    private List<ItemEditorCellTarget> CaptureItemEditorSelectedTargets()
        => GetItemEditorSelectedEditableCells()
            .Select(cell => TryCreateItemEditorCellTarget(cell.RowIndex, cell.ColumnIndex, out var target) ? target : null)
            .OfType<ItemEditorCellTarget>()
            .ToList();

    private bool TryCreateItemEditorCellTarget(int rowIndex, int columnIndex, out ItemEditorCellTarget target)
    {
        target = null!;
        if (!TryResolveItemEditorCell(rowIndex, columnIndex, out var row, out var columnName)) return false;
        target = new ItemEditorCellTarget(row, columnName);
        return true;
    }

    private static bool ItemEditorTargetListContains(IReadOnlyList<ItemEditorCellTarget> targets, ItemEditorCellTarget target)
        => targets.Any(candidate => ReferenceEquals(candidate.Row, target.Row) &&
                                    string.Equals(candidate.ColumnName, target.ColumnName, StringComparison.Ordinal));

    private bool IsItemEditorTargetValid(ItemEditorCellTarget target)
        => target.Row.RowState != DataRowState.Detached &&
           _currentItemEditorData?.Columns.Contains(target.ColumnName) == true;

    private bool TrySetItemEditorCellTargetValue(
        ItemEditorCellTarget target,
        string text,
        List<ItemEditorCellEdit> edits,
        out string error)
    {
        error = string.Empty;
        if (!IsItemEditorTargetValid(target)) return false;
        if (!TryValidateItemEditorCellText(target.ColumnName, text, out error))
        {
            SetItemEditorCellError(target, error);
            return false;
        }

        try
        {
            var oldValue = NormalizeGridCellValue(target.Row[target.ColumnName]);
            var newValue = ConvertItemEditorValueForColumn(target.ColumnName, text);
            if (Equals(oldValue, newValue)) return false;

            target.Row[target.ColumnName] = newValue ?? DBNull.Value;
            RefreshItemEditorDerivedCells(target.Row);
            SetItemEditorCellError(target, string.Empty);
            edits.Add(new ItemEditorCellEdit(target.Row, target.ColumnName, oldValue, NormalizeGridCellValue(target.Row[target.ColumnName])));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            SetItemEditorCellError(target, error);
            return false;
        }
    }

    private void SetItemEditorCellError(ItemEditorCellTarget target, string error)
    {
        foreach (DataGridViewRow row in _itemEditorGrid.Rows)
        {
            if (!ReferenceEquals(TryGetDataRow(row), target.Row)) continue;
            foreach (DataGridViewColumn column in _itemEditorGrid.Columns)
            {
                if (!string.Equals(column.DataPropertyName, target.ColumnName, StringComparison.Ordinal)) continue;
                row.Cells[column.Index].ErrorText = error;
                return;
            }
        }
    }

    private List<DataGridViewCell> GetItemEditorSelectedEditableCells()
        => _itemEditorGrid.SelectedCells.Cast<DataGridViewCell>()
            .Where(cell => IsItemEditorEditableCell(cell.RowIndex, cell.ColumnIndex))
            .OrderBy(cell => cell.RowIndex)
            .ThenBy(cell => cell.ColumnIndex)
            .ToList();

    private bool IsItemEditorEditableCell(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || rowIndex >= _itemEditorGrid.Rows.Count ||
            columnIndex < 0 || columnIndex >= _itemEditorGrid.Columns.Count)
        {
            return false;
        }

        var column = _itemEditorGrid.Columns[columnIndex];
        if (column.ReadOnly || !column.Visible) return false;
        var cell = _itemEditorGrid.Rows[rowIndex].Cells[columnIndex];
        return !cell.ReadOnly &&
               !IsItemEditorUserReadOnlyColumn(column.DataPropertyName) &&
               TryGetDataRow(_itemEditorGrid.Rows[rowIndex]) != null;
    }

    private (int Row, int Column)? GetItemEditorPasteStartCell()
    {
        if (_itemEditorGrid.CurrentCell != null)
        {
            return (_itemEditorGrid.CurrentCell.RowIndex, _itemEditorGrid.CurrentCell.ColumnIndex);
        }

        return _itemEditorGrid.SelectedCells.Cast<DataGridViewCell>()
            .Where(cell => cell.RowIndex >= 0 && cell.ColumnIndex >= 0)
            .OrderBy(cell => cell.RowIndex)
            .ThenBy(cell => cell.ColumnIndex)
            .Select(cell => ((int Row, int Column)?)(cell.RowIndex, cell.ColumnIndex))
            .FirstOrDefault();
    }

    private bool TrySetItemEditorCellValue(
        int rowIndex,
        int columnIndex,
        string text,
        List<ItemEditorCellEdit> edits,
        out string error)
    {
        error = string.Empty;
        if (!TryResolveItemEditorCell(rowIndex, columnIndex, out var row, out var columnName)) return false;
        if (!TryValidateItemEditorCellText(columnName, text, out error))
        {
            _itemEditorGrid.Rows[rowIndex].Cells[columnIndex].ErrorText = error;
            return false;
        }

        try
        {
            var oldValue = NormalizeGridCellValue(row[columnName]);
            var newValue = ConvertItemEditorValueForColumn(columnName, text);
            if (Equals(oldValue, newValue)) return false;

            row[columnName] = newValue ?? DBNull.Value;
            RefreshItemEditorDerivedCells(row);
            _itemEditorGrid.Rows[rowIndex].Cells[columnIndex].ErrorText = string.Empty;
            edits.Add(new ItemEditorCellEdit(row, columnName, oldValue, NormalizeGridCellValue(row[columnName])));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _itemEditorGrid.Rows[rowIndex].Cells[columnIndex].ErrorText = error;
            return false;
        }
    }

    private bool TryResolveItemEditorCell(int rowIndex, int columnIndex, out DataRow row, out string columnName)
    {
        row = null!;
        columnName = string.Empty;
        if (rowIndex < 0 || rowIndex >= _itemEditorGrid.Rows.Count ||
            columnIndex < 0 || columnIndex >= _itemEditorGrid.Columns.Count)
        {
            return false;
        }

        var column = _itemEditorGrid.Columns[columnIndex];
        if (column.ReadOnly || !column.Visible) return false;
        var cell = _itemEditorGrid.Rows[rowIndex].Cells[columnIndex];
        if (cell.ReadOnly) return false;
        columnName = column.DataPropertyName;
        if (string.IsNullOrEmpty(columnName) || _currentItemEditorData?.Columns.Contains(columnName) != true) return false;
        if (IsItemEditorUserReadOnlyColumn(columnName)) return false;
        row = TryGetDataRow(_itemEditorGrid.Rows[rowIndex])!;
        return row != null;
    }

    private bool TryValidateItemEditorCellText(string columnName, string value, out string error)
    {
        error = string.Empty;
        if (columnName == "名称")
        {
            var bytes = EncodingService.GetGbkByteCount(value);
            if (bytes > 17) error = $"物品名称超长：GBK {bytes} 字节，容量 17 字节。";
        }
        else if (columnName == "介绍")
        {
            var bytes = EncodingService.GetGbkByteCount(value);
            if (bytes > 200) error = $"物品说明超长：GBK {bytes} 字节，容量 200 字节。";
        }
        else if (columnName != "ID")
        {
            error = TryParseInteger(value, 0, byte.MaxValue, columnName, _currentPageHexButton.Checked) ?? string.Empty;
        }

        return string.IsNullOrEmpty(error);
    }

    private object? ConvertItemEditorValueForColumn(string columnName, string text)
    {
        if (_currentItemEditorData == null || !_currentItemEditorData.Columns.Contains(columnName)) return text;
        var dataColumn = _currentItemEditorData.Columns[columnName];
        if (dataColumn == null) return text;
        var targetType = dataColumn.DataType;
        if (targetType == typeof(string)) return text;
        if (IsSupportedIntegerType(targetType) &&
            TryParseIntegerInput(text, _currentPageHexButton.Checked, out var parsed) &&
            TryConvertParsedIntegerToType(parsed, targetType, out var converted))
        {
            return converted;
        }

        return Convert.ChangeType(text, targetType, CultureInfo.InvariantCulture);
    }

    private void PushItemEditorHistory(List<ItemEditorCellEdit> edits)
    {
        var effective = edits
            .Where(edit => !Equals(edit.OldValue, edit.NewValue))
            .ToList();
        if (effective.Count == 0)
        {
            UpdateItemEditorHistoryButtons();
            return;
        }

        _itemEditorUndoStack.Push(effective);
        _itemEditorRedoStack.Clear();
        UpdateItemEditorHistoryButtons();
    }

    private void UndoItemEditorChange()
    {
        if (_itemEditorUndoStack.Count == 0)
        {
            SetStatus("宝物设定没有可后退的编辑。");
            return;
        }

        var edits = _itemEditorUndoStack.Pop();
        ApplyItemEditorHistory(edits, useOldValue: true);
        _itemEditorRedoStack.Push(edits);
        SetStatus($"宝物设定已后退一步：还原 {edits.Count} 个单元格。");
    }

    private void RedoItemEditorChange()
    {
        if (_itemEditorRedoStack.Count == 0)
        {
            SetStatus("宝物设定没有可前进的编辑。");
            return;
        }

        var edits = _itemEditorRedoStack.Pop();
        ApplyItemEditorHistory(edits, useOldValue: false);
        _itemEditorUndoStack.Push(edits);
        SetStatus($"宝物设定已前进一步：恢复 {edits.Count} 个单元格。");
    }

    private void ApplyItemEditorHistory(List<ItemEditorCellEdit> edits, bool useOldValue)
    {
        _applyingItemEditorHistory = true;
        try
        {
            foreach (var edit in edits)
            {
                if (edit.Row.RowState == DataRowState.Detached || !edit.Row.Table.Columns.Contains(edit.ColumnName)) continue;
                edit.Row[edit.ColumnName] = (useOldValue ? edit.OldValue : edit.NewValue) ?? DBNull.Value;
                RefreshItemEditorDerivedCells(edit.Row);
            }
        }
        finally
        {
            _applyingItemEditorHistory = false;
        }

        RefreshItemEditorCellsAfterEdits(edits);
    }

    private void ResetItemEditorHistory()
    {
        _itemEditorUndoStack.Clear();
        _itemEditorRedoStack.Clear();
        _itemEditorSelectionSnapshotTargets = [];
        _itemEditorPendingCellEditOriginals = [];
        _itemEditorSelectionChangeStartedByMouse = false;
        UpdateItemEditorHistoryButtons();
    }

    private void UpdateItemEditorHistoryButtons()
    {
        _undoItemEditorButton.Enabled = _itemEditorUndoStack.Count > 0;
        _redoItemEditorButton.Enabled = _itemEditorRedoStack.Count > 0;
    }

    private void RefreshItemEditorRowStyles()
    {
        foreach (DataGridViewRow row in _itemEditorGrid.Rows)
        {
            RefreshItemEditorRowStyle(row.Index);
        }
    }

    private void RefreshItemEditorRowStyle(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _itemEditorGrid.Rows.Count) return;
        var dataRow = TryGetDataRow(_itemEditorGrid.Rows[rowIndex]);
        if (dataRow == null) return;
        if (IsDataRowChanged(dataRow))
        {
            _itemEditorGrid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.LightCyan;
            ApplyItemEditorReadOnlyCellStyles(_itemEditorGrid.Rows[rowIndex]);
            return;
        }

        var category = dataRow.Table.Columns.Contains("物品大类")
            ? Convert.ToString(dataRow["物品大类"], CultureInfo.InvariantCulture) ?? string.Empty
            : Convert.ToString(dataRow["大类"], CultureInfo.InvariantCulture) ?? string.Empty;
        _itemEditorGrid.Rows[rowIndex].DefaultCellStyle.BackColor = category switch
        {
            "武器" => Color.FromArgb(255, 250, 240),
            "防具" => Color.FromArgb(245, 250, 255),
            "辅助装备" => Color.FromArgb(248, 255, 248),
            "道具/消耗品" => Color.FromArgb(255, 252, 242),
            "预留/空位" => Color.FromArgb(245, 245, 245),
            _ => Color.Empty
        };
        ApplyItemEditorReadOnlyCellStyles(_itemEditorGrid.Rows[rowIndex]);
    }

    private void ApplyItemEditorReadOnlyCellStyles(DataGridViewRow row)
    {
        foreach (DataGridViewCell cell in row.Cells)
        {
            var isReadOnly = IsItemEditorUserReadOnlyColumn(cell.OwningColumn.DataPropertyName);
            cell.ReadOnly = isReadOnly;
            cell.Style.BackColor = isReadOnly ? Color.FromArgb(245, 245, 245) : Color.Empty;
            cell.Style.ForeColor = isReadOnly ? SystemColors.GrayText : Color.Empty;
        }
    }

    private void UpdateItemEditorDerivedCells(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || rowIndex >= _itemEditorGrid.Rows.Count) return;
        if (columnIndex < 0 || columnIndex >= _itemEditorGrid.Columns.Count) return;
        var columnName = _itemEditorGrid.Columns[columnIndex].DataPropertyName;
        var row = TryGetDataRow(_itemEditorGrid.Rows[rowIndex]);
        if (row == null) return;

        if ((columnName is "类型" or "装备特效号" or "宝物图鉴" or "名称") &&
            row.Table.Columns.Contains("类型说明") &&
            row.Table.Columns.Contains("类型") &&
            row.Table.Columns.Contains("宝物图鉴"))
        {
            RefreshItemEditorClassificationCells(row);
            RefreshItemEditorTypeProfileCells(
                row,
                Convert.ToInt32(row["类型"], CultureInfo.InvariantCulture),
                Convert.ToString(row["物品大类"], CultureInfo.InvariantCulture) ?? string.Empty);
        }

        if ((columnName is "类型" or "装备特效号" or "装备特效号-效果值" or "初始能力" or ItemEditorVisibleEffectValueColumnName or "升级能力成长" or "名称") &&
            row.Table.Columns.Contains("实际效果号") &&
            row.Table.Columns.Contains("实际效果说明") &&
            row.Table.Columns.Contains("装备特效名") &&
            row.Table.Columns.Contains("特效提示"))
        {
            RefreshItemEditorClassificationCells(row);
            var majorCategory = Convert.ToString(row["物品大类"], CultureInfo.InvariantCulture) ?? string.Empty;
            var typeId = Convert.ToInt32(row["类型"], CultureInfo.InvariantCulture);
            var effectId = ReadNullableInt(row, "装备特效号") ?? 0;
            var rawEffectMarker = GetItemEditorRawEffectMarker(row, effectId);
            var visibleEffectId = GetAndSynchronizeVisibleItemEffectIdForRefresh(row, majorCategory, typeId, rawEffectMarker, columnName);
            if (string.Equals(columnName, ItemEditorVisibleEffectValueColumnName, StringComparison.Ordinal))
            {
                SetVisibleItemEffectValue(
                    row,
                    majorCategory,
                    Convert.ToInt32(row[ItemEditorVisibleEffectValueColumnName], CultureInfo.InvariantCulture));
            }

            var visibleEffectValue = GetVisibleItemEffectValue(row, majorCategory);
            if (row.Table.Columns.Contains(ItemEditorVisibleEffectValueColumnName))
            {
                row[ItemEditorVisibleEffectValueColumnName] = visibleEffectValue;
            }

            var effect = ResolveItemEffect(majorCategory, typeId, visibleEffectId);
            row["装备特效名"] = BuildItemEffectNameDisplay(effect);

            row["实际效果号"] = $"装备特效号={visibleEffectId}";
            row["实际效果说明"] = effect.Description;
            row["特效提示"] = ItemEffectInterpretationService.BuildEffectHint(
                majorCategory,
                typeId,
                visibleEffectId,
                Convert.ToString(row["装备特效名"], CultureInfo.InvariantCulture) ?? string.Empty,
                visibleEffectValue,
                Convert.ToInt32(row["升级能力成长"], CultureInfo.InvariantCulture));
        }
    }

    private void RefreshItemEditorDerivedCells(DataRow row)
    {
        if (_currentItemEditorData == null || row.RowState == DataRowState.Detached) return;
        if (row.Table.Columns.Contains("类型说明") &&
            row.Table.Columns.Contains("类型") &&
            row.Table.Columns.Contains("宝物图鉴"))
        {
            RefreshItemEditorClassificationCells(row);
            RefreshItemEditorTypeProfileCells(
                row,
                Convert.ToInt32(row["类型"], CultureInfo.InvariantCulture),
                Convert.ToString(row["物品大类"], CultureInfo.InvariantCulture) ?? string.Empty);
        }

        if (row.Table.Columns.Contains("价格显示") &&
            row.Table.Columns.Contains("价格（/100）"))
        {
            row["价格显示"] = BuildItemPriceText(Convert.ToInt32(row["价格（/100）"], CultureInfo.InvariantCulture));
        }

        if (row.Table.Columns.Contains("实际效果号") &&
            row.Table.Columns.Contains("实际效果说明") &&
            row.Table.Columns.Contains("装备特效名") &&
            row.Table.Columns.Contains("特效提示") &&
            row.Table.Columns.Contains("类型") &&
            row.Table.Columns.Contains("装备特效号") &&
            row.Table.Columns.Contains("装备特效号-效果值") &&
            row.Table.Columns.Contains("升级能力成长"))
        {
            RefreshItemEditorClassificationCells(row);
            var majorCategory = Convert.ToString(row["物品大类"], CultureInfo.InvariantCulture) ?? string.Empty;
            var typeId = Convert.ToInt32(row["类型"], CultureInfo.InvariantCulture);
            var effectId = ReadNullableInt(row, "装备特效号") ?? 0;
            var rawEffectMarker = GetItemEditorRawEffectMarker(row, effectId);
            var visibleEffectId = GetAndSynchronizeVisibleItemEffectIdForRefresh(row, majorCategory, typeId, rawEffectMarker, editedColumnName: null);
            var visibleEffectValue = GetVisibleItemEffectValue(row, majorCategory);
            if (row.Table.Columns.Contains(ItemEditorVisibleEffectValueColumnName))
            {
                row[ItemEditorVisibleEffectValueColumnName] = visibleEffectValue;
            }

            var effect = ResolveItemEffect(majorCategory, typeId, visibleEffectId);
            row["装备特效名"] = BuildItemEffectNameDisplay(effect);

            row["实际效果号"] = $"装备特效号={visibleEffectId}";
            row["实际效果说明"] = effect.Description;
            row["特效提示"] = ItemEffectInterpretationService.BuildEffectHint(
                majorCategory,
                typeId,
                visibleEffectId,
                Convert.ToString(row["装备特效名"], CultureInfo.InvariantCulture) ?? string.Empty,
                visibleEffectValue,
                Convert.ToInt32(row["升级能力成长"], CultureInfo.InvariantCulture));
        }
    }

    private void RefreshItemEditorClassificationCells(DataRow row)
    {
        if (!row.Table.Columns.Contains("物品大类")) return;
        var boundary = _project != null
            ? ItemCategoryBoundaryService.Resolve(_project)
            : new ItemCategoryBoundary(
                ItemCategoryBoundaryService.MinItemId,
                ItemCategoryBoundaryService.DefaultDefenseStartId,
                ItemCategoryBoundaryService.DefaultAccessoryStartId,
                "默认：未打开项目",
                IsFallback: true);
        row["物品大类"] = ItemClassificationService.Classify(row, boundary).DisplayName;
    }

    private static int GetItemEditorRawEffectMarker(DataRow row, int fallbackEffectId)
        => row.Table.Columns.Contains(ItemEditorRawEffectColumnName)
            ? Convert.ToInt32(row[ItemEditorRawEffectColumnName], CultureInfo.InvariantCulture)
            : row.Table.Columns.Contains(Ccz66ItemLayoutService.RawEffectMarkerColumnName)
            ? Convert.ToInt32(row[Ccz66ItemLayoutService.RawEffectMarkerColumnName], CultureInfo.InvariantCulture)
            : fallbackEffectId;

    private void ShowSelectedItemEditorCell()
    {
        if (_currentItemEditorData == null || _itemEditorGrid.CurrentCell == null) return;
        var cell = _itemEditorGrid.CurrentCell;
        var columnName = _itemEditorGrid.Columns[cell.ColumnIndex].DataPropertyName;
        var row = _itemEditorGrid.Rows[cell.RowIndex];
        var id = row.Cells["ID"].Value;
        var name = row.Cells["名称"].Value;
        var value = cell.FormattedValue;
        var dataRow = TryGetDataRow(row);
        var effectText = BuildSelectedItemEffectText(dataRow, row);
        var typeText = _itemEditorGrid.Columns.Contains("类型")
            ? row.Cells["类型"].FormattedValue
            : string.Empty;
        var kindText = _itemEditorGrid.Columns.Contains("物品大类")
            ? row.Cells["物品大类"].FormattedValue
            : string.Empty;
        var priceText = _itemEditorGrid.Columns.Contains("价格（/100）")
            ? row.Cells["价格（/100）"].Value
            : string.Empty;
        var extra = columnName switch
        {
            "名称" => $"\r\nGBK 字节：{EncodingService.GetGbkByteCount(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)}/17",
            "介绍" => $"\r\nGBK 字节：{EncodingService.GetGbkByteCount(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)}/200",
            _ => string.Empty
        };
        _itemEditorInfoBox.Text =
            $"宝物/物品：ID={id}    名称={name}    物品大类={kindText}    类型码={typeText}    价格字段={priceText}{effectText}\r\n" +
            $"字段：{BuildItemEditorColumnHeader(columnName)}    当前值：{value}{extra}\r\n\r\n" +
            BuildItemColumnAnnotation(columnName);
        UpdateItemIconPreview(row);
    }

    private static string BuildSelectedItemEffectText(DataRow? dataRow, DataGridViewRow row)
    {
        var grid = row.DataGridView;
        if (dataRow == null ||
            grid == null ||
            !grid.Columns.Contains("装备特效号") ||
            !grid.Columns.Contains("装备特效名") ||
            row.Cells["装备特效号"].Value is not { } effectValue ||
            effectValue == DBNull.Value ||
            string.IsNullOrWhiteSpace(Convert.ToString(effectValue, CultureInfo.InvariantCulture)))
        {
            return string.Empty;
        }

        var effectId = Convert.ToInt32(effectValue, CultureInfo.InvariantCulture);
        var majorCategory = dataRow.Table.Columns.Contains("物品大类")
            ? Convert.ToString(dataRow["物品大类"], CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;
        var effectValueText = dataRow.Table.Columns.Contains(ItemEditorVisibleEffectValueColumnName)
            ? Convert.ToString(dataRow[ItemEditorVisibleEffectValueColumnName], CultureInfo.InvariantCulture)
            : string.Empty;
        var idText = IsConsumableMajorCategory(majorCategory)
            ? $"{effectId} ({HexDisplayFormatter.Format(effectId, 2)})"
            : effectId.ToString(CultureInfo.InvariantCulture);
        var valueSuffix = string.IsNullOrWhiteSpace(effectValueText)
            ? string.Empty
            : $"    特效值：{effectValueText}";
        return $"    特效：{idText} / {row.Cells["装备特效名"].Value}{valueSuffix}";
    }

    private void UpdateItemIconPreview(DataGridViewRow row)
    {
        if (_project == null)
        {
            ClearItemIconPreview("请先打开 MOD 项目。");
            return;
        }

        if (!_itemEditorGrid.Columns.Contains("图标"))
        {
            ClearItemIconPreview("当前物品表没有“图标”字段。");
            return;
        }

        var iconText = Convert.ToString(row.Cells["图标"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        if (!int.TryParse(iconText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iconIndex))
        {
            ClearItemIconPreview($"图标字段不是整数：{iconText}");
            return;
        }

        var result = _itemIconPreviewService.BuildPreview(_project, iconIndex);
        var largeSource = result.LargeBitmap ?? result.NativeBitmap ?? result.Bitmap;
        SetItemIconPreviewSources(largeSource, result.SmallBitmap);
        var id = Convert.ToString(row.Cells["ID"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        var name = Convert.ToString(row.Cells["名称"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        _itemIconPreviewInfoBox.Text =
            $"物品 ID={id}  名称={name}\r\n" +
            $"图标字段={iconIndex}\r\n" +
            $"{result.Message}\r\n" +
            $"资源路径：{result.SourcePath}";
        var previewWarnings = result.Warnings?
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();
        if (previewWarnings.Length > 0)
        {
            _itemIconPreviewInfoBox.AppendText("\r\nWarnings:\r\n" + string.Join("\r\n", previewWarnings.Select(warning => "- " + warning)));
        }

        _itemEditorInfoBox.Text = _itemIconPreviewInfoBox.Text;
        DisposeItemIconPreviewResultBitmaps(result);
    }

    private void RefreshItemEditorCellsAfterEdits(IReadOnlyList<ItemEditorCellEdit> edits)
    {
        foreach (var row in edits
                     .Select(edit => edit.Row)
                     .Where(row => row.RowState != DataRowState.Detached)
                     .Distinct())
        {
            RefreshItemEditorDerivedCells(row);
            var rowIndex = FindDataRowGridIndex(_itemEditorGrid, row);
            if (rowIndex >= 0)
            {
                RefreshItemEditorRowStyle(rowIndex);
                _itemEditorGrid.InvalidateRow(rowIndex);
            }
        }

        ShowSelectedItemEditorCell();
        UpdateItemEditorHistoryButtons();
    }

    private void ClearItemIconPreview(string message)
    {
        ClearItemIconPreviewImages();
        _itemIconPreviewInfoBox.Text = message;
    }

    private void SetItemIconPreviewSources(Bitmap? largeSource, Bitmap? smallSource)
    {
        _itemIconLargeSourceBitmap?.Dispose();
        _itemIconSmallSourceBitmap?.Dispose();
        _itemIconLargeSourceBitmap = largeSource == null ? null : new Bitmap(largeSource);
        _itemIconSmallSourceBitmap = smallSource == null ? null : new Bitmap(smallSource);
        _itemIconLargeZoomPercent = 0;
        _itemIconSmallZoomPercent = 0;
        RenderItemIconPreview(ItemIconPreviewRole.Large);
        RenderItemIconPreview(ItemIconPreviewRole.Small);
    }

    private void ClearItemIconPreviewImages()
    {
        _itemIconLargeSourceBitmap?.Dispose();
        _itemIconSmallSourceBitmap?.Dispose();
        _itemIconLargeSourceBitmap = null;
        _itemIconSmallSourceBitmap = null;
        _itemIconLargeZoomPercent = 0;
        _itemIconSmallZoomPercent = 0;
        SetPictureBoxImage(_itemIconPreviewBox, null);
        SetPictureBoxImage(_itemIconSmallPreviewBox, null);
        _itemIconPreviewBox.Size = Size.Empty;
        _itemIconSmallPreviewBox.Size = Size.Empty;
        _itemIconLargePreviewTitle.Text = "大图";
        _itemIconSmallPreviewTitle.Text = "小图";
    }

    private void RenderItemIconPreview(ItemIconPreviewRole role)
    {
        var source = role == ItemIconPreviewRole.Large ? _itemIconLargeSourceBitmap : _itemIconSmallSourceBitmap;
        var pictureBox = role == ItemIconPreviewRole.Large ? _itemIconPreviewBox : _itemIconSmallPreviewBox;
        var title = role == ItemIconPreviewRole.Large ? _itemIconLargePreviewTitle : _itemIconSmallPreviewTitle;
        var scrollPanel = role == ItemIconPreviewRole.Large ? _itemIconLargePreviewScrollPanel : _itemIconSmallPreviewScrollPanel;
        var baseTitle = role == ItemIconPreviewRole.Large ? "大图" : "小图";
        if (source == null)
        {
            SetPictureBoxImage(pictureBox, null);
            pictureBox.Size = Size.Empty;
            title.Text = baseTitle;
            return;
        }

        var zoomPercent = role == ItemIconPreviewRole.Large ? _itemIconLargeZoomPercent : _itemIconSmallZoomPercent;
        if (zoomPercent <= 0)
        {
            zoomPercent = CalculateItemIconFitZoomPercent(source, scrollPanel);
        }

        var zoom = Math.Clamp(zoomPercent / 100, 1, 64);
        var rendered = RenderItemIconZoomedPreview(source, zoom);
        SetPictureBoxImage(pictureBox, rendered);
        pictureBox.Size = rendered.Size;
        pictureBox.Location = Point.Empty;
        scrollPanel.AutoScrollMinSize = rendered.Size;
        title.Text = $"{baseTitle} {source.Width}x{source.Height} {zoom}x";
    }

    private static int CalculateItemIconFitZoomPercent(Bitmap source, ScrollableControl scrollPanel)
    {
        var viewportWidth = Math.Max(1, scrollPanel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
        var viewportHeight = Math.Max(1, scrollPanel.ClientSize.Height - SystemInformation.HorizontalScrollBarHeight);
        var zoom = Math.Max(1, Math.Min(viewportWidth / Math.Max(1, source.Width), viewportHeight / Math.Max(1, source.Height)));
        return Math.Clamp(zoom, 1, 64) * 100;
    }

    private void HandleItemIconPreviewMouseWheel(ItemIconPreviewRole role, MouseEventArgs e)
    {
        var source = role == ItemIconPreviewRole.Large ? _itemIconLargeSourceBitmap : _itemIconSmallSourceBitmap;
        if (source == null) return;

        var panel = role == ItemIconPreviewRole.Large ? _itemIconLargePreviewScrollPanel : _itemIconSmallPreviewScrollPanel;
        var currentPercent = role == ItemIconPreviewRole.Large ? _itemIconLargeZoomPercent : _itemIconSmallZoomPercent;
        if (currentPercent <= 0)
        {
            currentPercent = CalculateItemIconFitZoomPercent(source, panel);
        }

        var currentZoom = Math.Clamp(currentPercent / 100, 1, 64);
        var nextZoom = Math.Clamp(currentZoom + (e.Delta > 0 ? 1 : -1), 1, 64);
        if (role == ItemIconPreviewRole.Large)
        {
            _itemIconLargeZoomPercent = nextZoom * 100;
        }
        else
        {
            _itemIconSmallZoomPercent = nextZoom * 100;
        }

        RenderItemIconPreview(role);
    }

    private void ResetItemIconPreviewZoom(ItemIconPreviewRole role)
    {
        if (role == ItemIconPreviewRole.Large)
        {
            _itemIconLargeZoomPercent = 0;
        }
        else
        {
            _itemIconSmallZoomPercent = 0;
        }

        RenderItemIconPreview(role);
    }

    private static Bitmap RenderItemIconZoomedPreview(Bitmap source, int zoom)
    {
        var scale = Math.Clamp(zoom, 1, 64);
        var width = checked(source.Width * scale);
        var height = checked(source.Height * scale);
        var output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(output);
        graphics.Clear(Color.Transparent);
        DrawItemIconChecker(graphics, new Rectangle(0, 0, width, height), Math.Max(4, scale * 2));
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var pixel = source.GetPixel(x, y);
                if (pixel.A == 0) continue;
                using var brush = new SolidBrush(pixel);
                graphics.FillRectangle(brush, x * scale, y * scale, scale, scale);
            }
        }

        return output;
    }

    private static void DrawItemIconChecker(Graphics graphics, Rectangle rect, int size)
    {
        using var light = new SolidBrush(Color.FromArgb(230, 230, 230));
        using var dark = new SolidBrush(Color.FromArgb(190, 190, 190));
        for (var y = rect.Top; y < rect.Bottom; y += size)
        {
            for (var x = rect.Left; x < rect.Right; x += size)
            {
                var even = ((x / size) + (y / size)) % 2 == 0;
                graphics.FillRectangle(even ? light : dark, x, y, size, size);
            }
        }
    }

    private static void DisposeItemIconPreviewResultBitmaps(ItemIconPreviewResult result)
    {
        var bitmap = result.Bitmap;
        var native = result.NativeBitmap;
        var small = result.SmallBitmap;
        var large = result.LargeBitmap;

        bitmap?.Dispose();
        if (!ReferenceEquals(native, bitmap)) native?.Dispose();
        if (!ReferenceEquals(small, bitmap) && !ReferenceEquals(small, native)) small?.Dispose();
        if (!ReferenceEquals(large, bitmap) && !ReferenceEquals(large, native) && !ReferenceEquals(large, small)) large?.Dispose();
    }

    private void BatchImportSelectedItemIcons()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_currentItemEditorData == null)
        {
            MessageBox.Show(this, "请先读取宝物/物品。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selectedRows = GetSelectedItemRowsForIconImport();
        if (selectedRows.Count == 0)
        {
            MessageBox.Show(this, "请先在宝物表中选中要导入图标的行。", "批量导入宝物图标", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (UseAlignedBatchImageImportDialogs())
        {
            BatchImportSelectedItemIconsAligned(selectedRows);
            return;
        }

        if (selectedRows.Count == 0)
        {
            MessageBox.Show(this, "请先在宝物表中选中要导入图标的行。", "批量导入宝物图标", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "选择要导入到所选宝物图标的图片",
            Filter = "图片文件 (*.bmp;*.jpg;*.jpeg;*.png)|*.bmp;*.jpg;*.jpeg;*.png|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.FileNames.Length == 0) return;

        BatchItemIconImportRequest request;
        try
        {
            request = new BatchItemIconImportRequest
            {
                SourceFiles = dialog.FileNames.OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ThenBy(path => path, StringComparer.CurrentCultureIgnoreCase).ToArray(),
                TargetRows = BuildItemIconImportTargetRows(selectedRows),
                MatchMode = "auto",
                WriteMode = _project.IsTestCopy ? "test_copy" : "direct"
            };
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "批量导入宝物图标", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        BatchItemIconImportPreviewResult preview;
        try
        {
            Cursor = Cursors.WaitCursor;
            preview = _batchItemIconImportService.Preview(_project, request);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Batch item icon import preview failed: " + ex);
            MessageBox.Show(this, ex.Message, "宝物图标批量导入预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        var previewText = BuildBatchItemIconImportPreviewText(preview);
        _itemIconPreviewInfoBox.Text = previewText;
        _itemEditorInfoBox.Text = previewText;
        if (!preview.CanWrite)
        {
            MessageBox.Show(this, previewText, "宝物图标批量导入存在阻断项", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (MessageBox.Show(this,
                previewText + "\r\n\r\n确认后会批量写入目标图标资源，并自动备份；不会修改宝物表“图标”字段。是否继续？",
                "确认批量导入宝物图标",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = _batchItemIconImportService.Replace(_project, request);
            _imageResourceCatalogService.ClearCache();
            _itemIconPreviewService.ClearCache();
            ShowSelectedItemEditorCell();
            _itemIconPreviewInfoBox.Text = BuildBatchItemIconImportResultText(result);
            _itemEditorInfoBox.Text = _itemIconPreviewInfoBox.Text;
            SetStatus($"宝物图标批量导入完成：{result.TotalOperationCount} 条");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Batch item icon import failed: " + ex);
            MessageBox.Show(this, ex.Message, "宝物图标批量导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void BatchImportSelectedItemIconsAligned(IReadOnlyList<DataGridViewRow> selectedRows)
    {
        if (_project == null) return;

        var sourceSelection = SelectBatchImageImportSources("选择要导入到所选宝物图标的图片或导出根目录");
        if (sourceSelection == null) return;

        BatchItemIconImportRequest request;
        try
        {
            request = new BatchItemIconImportRequest
            {
                SourceFiles = sourceSelection.SourceFiles,
                SourceRoot = sourceSelection.SourceRoot,
                TargetRows = BuildItemIconImportTargetRows(selectedRows),
                MatchMode = "auto",
                WriteMode = _project.IsTestCopy ? "test_copy" : "direct"
            };
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "批量导入宝物图标", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        BatchItemIconImportPreviewResult preview;
        try
        {
            Cursor = Cursors.WaitCursor;
            preview = _batchItemIconImportService.Preview(_project, request);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Batch item icon import preview failed: " + ex);
            MessageBox.Show(this, ex.Message, "宝物图标批量导入预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        var previewText = BuildBatchItemIconImportPreviewText(preview);
        _itemIconPreviewInfoBox.Text = previewText;
        _itemEditorInfoBox.Text = previewText;
        if (!preview.CanWrite)
        {
            MessageBox.Show(this, previewText, "宝物图标批量导入存在阻断项", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (MessageBox.Show(
                this,
                previewText + "\r\n\r\n确认后会批量写入目标图标资源，并自动备份；不会修改宝物表“图标”字段。是否继续？",
                "确认批量导入宝物图标",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = _batchItemIconImportService.Replace(_project, request);
            _imageResourceCatalogService.ClearCache();
            _itemIconPreviewService.ClearCache();
            ShowSelectedItemEditorCell();
            _itemIconPreviewInfoBox.Text = BuildBatchItemIconImportResultText(result);
            _itemEditorInfoBox.Text = _itemIconPreviewInfoBox.Text;
            SetStatus($"宝物图标批量导入完成：{result.TotalOperationCount} 条");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Batch item icon import failed: " + ex);
            MessageBox.Show(this, ex.Message, "宝物图标批量导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private IReadOnlyList<DataGridViewRow> GetSelectedItemRowsForIconImport()
    {
        var rows = _itemEditorGrid.SelectedCells
            .Cast<DataGridViewCell>()
            .Where(cell => cell.RowIndex >= 0)
            .Select(cell => _itemEditorGrid.Rows[cell.RowIndex])
            .Where(row => !row.IsNewRow)
            .Distinct()
            .OrderBy(row => row.Index)
            .ToList();
        if (rows.Count == 0 && _itemEditorGrid.CurrentRow is { IsNewRow: false } current)
        {
            rows.Add(current);
        }

        return rows;
    }

    private static IReadOnlyList<BatchItemIconTargetRow> BuildItemIconImportTargetRows(IReadOnlyList<DataGridViewRow> selectedRows)
    {
        var targets = new List<BatchItemIconTargetRow>();
        foreach (var row in selectedRows)
        {
            var dataRow = TryGetDataRow(row) ?? throw new InvalidOperationException("选中行无法解析为宝物数据行。");
            var itemId = Convert.ToInt32(dataRow["ID"], CultureInfo.InvariantCulture);
            var nameColumn = dataRow.Table.Columns.Contains("名称") ? "名称" : "鍚嶇О";
            var iconColumn = dataRow.Table.Columns.Contains("图标") ? "图标" : "鍥炬爣";
            var itemName = dataRow.Table.Columns.Contains(nameColumn)
                ? Convert.ToString(dataRow[nameColumn], CultureInfo.InvariantCulture) ?? string.Empty
                : string.Empty;
            if (!dataRow.Table.Columns.Contains(iconColumn) || !TryConvertToInt(dataRow[iconColumn], out var iconIndex))
            {
                throw new InvalidOperationException($"宝物 ID={itemId} 的图标字段不是有效整数。");
            }

            targets.Add(new BatchItemIconTargetRow(itemId, itemName, iconIndex));
        }

        var duplicateIcon = targets.GroupBy(target => target.IconIndex).FirstOrDefault(group => group.Count() > 1);
        if (duplicateIcon != null)
        {
            var ids = string.Join(", ", duplicateIcon.Select(target => target.RowId.ToString(CultureInfo.InvariantCulture)));
            throw new InvalidOperationException($"选中宝物中有多个条目指向同一图标编号 {duplicateIcon.Key}: {ids}。请调整选择或图标字段。");
        }

        return targets;
    }

    private static string BuildBatchItemIconImportPreviewText(BatchItemIconImportPreviewResult preview)
    {
        var builder = new StringBuilder();
        builder.AppendLine("宝物图标批量导入预览");
        builder.AppendLine($"目标：{preview.TargetRelativePath} ({preview.ResourceKind})");
        builder.AppendLine($"匹配成功：{preview.Items.Count}，写入条目：{preview.TotalOperationCount}");
        builder.AppendLine($"跳过/问题：{preview.SkippedItems.Count}");
        foreach (var item in preview.Items.Take(30))
        {
            var target = preview.ResourceKind.Equals("E5", StringComparison.OrdinalIgnoreCase)
                ? string.Join("/", item.TargetImageNumbers.Select(number => "#" + number.ToString(CultureInfo.InvariantCulture)))
                : "RT_BITMAP " + string.Join("/", item.ResourceIds);
            builder.AppendLine($"- ID={item.RowId} {item.DisplayName} -> 图标#{item.IconIndex} {target} <- {Path.GetFileName(item.SourcePath)}");
            if (!string.IsNullOrWhiteSpace(item.NormalizeSummary))
            {
                builder.AppendLine($"  normalize: {item.NormalizeSummary}");
            }
        }

        AppendBatchSkippedAndWarnings(builder, preview.SkippedItems, preview.Warnings);
        return builder.ToString();
    }

    private static string BuildBatchItemIconImportResultText(BatchItemIconImportResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("宝物图标批量导入完成");
        builder.AppendLine($"目标：{result.TargetRelativePath} ({result.ResourceKind})");
        builder.AppendLine($"写入条目：{result.TotalOperationCount}");
        if (result.DllResult != null)
        {
            builder.AppendLine($"备份：{result.DllResult.BackupPath}");
            builder.AppendLine($"报告：{result.DllResult.ReportJsonPath}");
        }
        if (result.E5Result != null)
        {
            builder.AppendLine($"备份：{result.E5Result.BackupPath}");
            builder.AppendLine($"报告：{result.E5Result.ReportJsonPath}");
        }
        builder.AppendLine($"汇总报告：{result.AggregateReportPath}");
        return builder.ToString();
    }

    private static void AppendBatchSkippedAndWarnings(StringBuilder builder, IReadOnlyList<BatchImageImportSkippedItem> skipped, IReadOnlyList<string> warnings)
    {
        if (skipped.Count > 0)
        {
            builder.AppendLine("跳过/问题：");
            foreach (var item in skipped.Take(30))
            {
                builder.AppendLine($"- {item.Key}: {item.Reason} {item.SourcePath}");
            }
            if (skipped.Count > 30) builder.AppendLine($"- ... 还有 {skipped.Count - 30} 项");
        }

        if (warnings.Count > 0)
        {
            builder.AppendLine("提示：");
            foreach (var warning in warnings.Take(20))
            {
                builder.AppendLine("- " + warning);
            }
            if (warnings.Count > 20) builder.AppendLine($"- ... 还有 {warnings.Count - 20} 条");
        }
        else
        {
            builder.AppendLine("提示：无");
        }
    }

    private void ValidateItemEditorCell(DataGridViewCellValidatingEventArgs e)
    {
        if (_itemEditorGrid.ReadOnly || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var column = _itemEditorGrid.Columns[e.ColumnIndex];
        if (column.ReadOnly) return;
        if (column is DataGridViewComboBoxColumn) return;
        var columnName = column.DataPropertyName;
        var value = Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture) ?? string.Empty;
        TryValidateItemEditorCellText(columnName, value, out var error);

        _itemEditorGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = error;
        if (string.IsNullOrEmpty(error)) return;
        e.Cancel = true;
        _itemEditorInfoBox.Text = error;
        SetStatus(error);
    }

    private void SaveItemEditor()
    {
        if (_project == null || _currentItemEditorData == null) return;

        _itemEditorGrid.EndEdit();
        if (_currentItemEditorData.GetChanges() == null)
        {
            MessageBox.Show(this, "宝物设定没有检测到改动。", "无需保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var preview = BuildChangePreview(_currentItemEditorData, maxItems: 40);
        if (MessageBox.Show(this,
                $"即将保存宝物/物品设定到当前 MOD 项目。\r\n\r\n变更预览：\r\n{preview}\r\n\r\n保存前会自动备份，保存后会重新读取校验。是否继续？",
                "确认保存宝物设定",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var changedCells = GetChangedCellKeys(_currentItemEditorData);
            var saves = SaveItemEditorData(_project, _currentItemEditorData);
            AcceptSavedDataTable(_currentItemEditorData);
            RefreshChangedGridCells(_itemEditorGrid, changedCells, UpdateItemEditorDerivedCells);
            RefreshChangedGridRowsOnly(_itemEditorGrid, changedCells, RefreshItemEditorRowStyle);
            ShowSelectedItemEditorCell();
            var changedBytes = saves.Sum(x => x.ChangedBytes);
            System.Diagnostics.Debug.WriteLine($"已保存宝物设定：保存表 {saves.Count} 个，变化字节 {changedBytes}");
            foreach (var save in saves) System.Diagnostics.Debug.WriteLine("宝物设定备份：" + save.BackupPath);
            SetStatus($"宝物设定保存完成并已复读：变化 {changedBytes} 字节");
            MessageBox.Show(this,
                $"保存完成并已重新读取校验。\r\n保存表数量：{saves.Count}\r\n变化字节：{changedBytes}\r\n备份：{string.Join("; ", saves.Select(x => x.BackupPath))}",
                "保存完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("保存宝物设定失败：" + ex);
            MessageBox.Show(this, ex.Message, "保存宝物设定失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private IReadOnlyList<TableSaveResult> SaveItemEditorData(CczProject project, DataTable itemData)
    {
        if (_itemBaseLowRead == null || _itemBaseHighRead == null)
        {
            return Array.Empty<TableSaveResult>();
        }

        foreach (DataRow itemRow in itemData.Rows)
        {
            if (itemRow.RowState != DataRowState.Modified) continue;
            var id = Convert.ToInt32(itemRow["ID"], CultureInfo.InvariantCulture);
            var baseRead = id <= 103 ? _itemBaseLowRead : _itemBaseHighRead;
            var descriptionRead = id <= 103 ? _itemDescriptionLowRead : _itemDescriptionHighRead;
            var baseRow = FindRowById(baseRead.Data, id);
            SynchronizeItemEditorVisibleEffectColumnsForSave(itemRow, baseRow);
            SynchronizeItemEditorVisibleEffectValueForSave(itemRow, baseRow);
            SynchronizeQinger66AccessoryEffectColumnsForSave(itemRow, baseRow);

            foreach (DataColumn column in baseRead.Data.Columns)
            {
                if (column.ColumnName == "ID") continue;
                if (!itemData.Columns.Contains(column.ColumnName) || !IsRoleColumnChanged(itemRow, column.ColumnName)) continue;
                if (ShouldSkipItemEditorBaseColumnSave(itemRow, column.ColumnName)) continue;
                baseRow[column.ColumnName] = itemRow[column.ColumnName, DataRowVersion.Current];
            }

            if (IsRoleColumnChanged(itemRow, ItemDescriptionColumnName))
            {
                if (!IsItemDescriptionReadUsable(descriptionRead))
                {
                    throw new InvalidOperationException("当前项目的物品说明表不可写：Imsg.e5 中没有匹配当前 HexTable 布局的物品说明块。基础物品字段仍可保存，请不要向文件尾追加标准 6.5 说明表。");
                }

                if (!descriptionRead!.Data.Columns.Contains(ItemDescriptionColumnName))
                {
                    throw new InvalidOperationException("当前物品说明表缺少“介绍”列，无法写回说明文本。");
                }

                var descriptionRow = FindRowById(descriptionRead.Data, id);
                descriptionRow[ItemDescriptionColumnName] = itemRow[ItemDescriptionColumnName, DataRowVersion.Current];
            }
        }

        var saves = new List<TableSaveResult>();
        if (SaveChangedTableAndVerify(_itemBaseLowRead) is { } lowSave) saves.Add(lowSave);
        if (SaveChangedTableAndVerify(_itemBaseHighRead) is { } highSave) saves.Add(highSave);
        if (IsItemDescriptionReadUsable(_itemDescriptionLowRead) && SaveChangedTableAndVerify(_itemDescriptionLowRead!) is { } descriptionLowSave) saves.Add(descriptionLowSave);
        if (IsItemDescriptionReadUsable(_itemDescriptionHighRead) && SaveChangedTableAndVerify(_itemDescriptionHighRead!) is { } descriptionHighSave) saves.Add(descriptionHighSave);
        return saves;
    }

    private static void SynchronizeItemEditorVisibleEffectColumnsForSave(DataRow itemRow, DataRow baseRow)
    {
        if (!itemRow.Table.Columns.Contains("类型") ||
            !itemRow.Table.Columns.Contains("装备特效号") ||
            !baseRow.Table.Columns.Contains("类型"))
        {
            return;
        }

        if (!IsItemEditorTypeBackedVisibleEffectMarker(itemRow))
        {
            return;
        }

        var typeChanged = IsRoleColumnChanged(itemRow, "类型");
        var effectChanged = IsRoleColumnChanged(itemRow, "装备特效号");
        if (!typeChanged && !effectChanged)
        {
            return;
        }

        var typeId = Convert.ToInt32(itemRow["类型"], CultureInfo.InvariantCulture);
        var effectId = ReadNullableInt(itemRow, "装备特效号") ?? typeId;
        if (typeChanged && effectChanged && typeId != effectId)
        {
            throw new InvalidOperationException(
                $"辅助/道具行 ID={itemRow["ID"]} 的“类型码”和“特效号”映射到同一物理字节；两者不能同时改成不同值。");
        }

        var synchronized = typeChanged ? typeId : effectId;
        itemRow["类型"] = synchronized;
        itemRow["装备特效号"] = synchronized;
        baseRow["类型"] = synchronized;
    }

    private static void SynchronizeItemEditorVisibleEffectValueForSave(DataRow itemRow, DataRow baseRow)
    {
        if (!itemRow.Table.Columns.Contains(ItemEditorVisibleEffectValueColumnName))
        {
            return;
        }

        if (!IsRoleColumnChanged(itemRow, ItemEditorVisibleEffectValueColumnName))
        {
            return;
        }

        var majorCategory = itemRow.Table.Columns.Contains("物品大类")
            ? Convert.ToString(itemRow["物品大类"], CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;
        var value = Convert.ToInt32(itemRow[ItemEditorVisibleEffectValueColumnName], CultureInfo.InvariantCulture);
        if (IsConsumableMajorCategory(majorCategory))
        {
            if (itemRow.Table.Columns.Contains("初始能力")) itemRow["初始能力"] = value;
            if (baseRow.Table.Columns.Contains("初始能力")) baseRow["初始能力"] = value;
            return;
        }

        if (itemRow.Table.Columns.Contains("装备特效号-效果值")) itemRow["装备特效号-效果值"] = value;
        if (baseRow.Table.Columns.Contains("装备特效号-效果值")) baseRow["装备特效号-效果值"] = value;
    }

    private static bool ShouldSkipItemEditorBaseColumnSave(DataRow itemRow, string columnName)
        => columnName == "装备特效号" &&
           (IsItemEditorAccessoryMarker(itemRow) || IsItemEditorConsumableMarker(itemRow));

    private static void SynchronizeQinger66AccessoryEffectColumnsForSave(DataRow itemRow, DataRow baseRow)
    {
        if (!itemRow.Table.Columns.Contains(Ccz66ItemLayoutService.RawEffectMarkerColumnName) ||
            !itemRow.Table.Columns.Contains("类型") ||
            !itemRow.Table.Columns.Contains("装备特效号") ||
            !baseRow.Table.Columns.Contains("类型"))
        {
            return;
        }

        var marker = Convert.ToInt32(itemRow[Ccz66ItemLayoutService.RawEffectMarkerColumnName], CultureInfo.InvariantCulture);
        if (marker is not (2 or 3))
        {
            return;
        }

        var typeChanged = IsRoleColumnChanged(itemRow, "类型");
        var effectChanged = IsRoleColumnChanged(itemRow, "装备特效号");
        if (!typeChanged && !effectChanged)
        {
            return;
        }

        var typeId = Convert.ToInt32(itemRow["类型"], CultureInfo.InvariantCulture);
        var effectId = ReadNullableInt(itemRow, "装备特效号") ?? typeId;
        if (typeChanged && effectChanged && typeId != effectId)
        {
            throw new InvalidOperationException(
                $"6.6 辅助/道具行 ID={itemRow["ID"]} 的“类型”和“装备特效号”映射到同一物理字节 row+0x11；两者不能同时改成不同值。");
        }

        var synchronized = typeChanged ? typeId : effectId;
        itemRow["类型"] = synchronized;
        itemRow["装备特效号"] = synchronized;
        baseRow["类型"] = synchronized;
        baseRow["装备特效号"] = synchronized;
    }

    private void OpenShopEditor()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SelectTabPageByText("商店编辑");
        LoadShopEditor();
    }

    private void LoadShopEditor()
    {
        if (_project == null) return;
        if (_tables.Count == 0)
        {
            ReloadCurrentProject();
            if (_tables.Count == 0) return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            _currentShopEditorData = BuildShopEditorData(_project, _tables);
            _shopEditorGrid.DataSource = _currentShopEditorData;
            ConfigureShopEditorGrid();
            _saveShopEditorButton.Enabled = true;
            _exportShopEditorCsvButton.Enabled = true;
            _importShopEditorCsvButton.Enabled = true;
            _shopEditorInfoBox.Text = BuildShopEditorSummary(_currentShopEditorData);
            ShowSelectedShopEditorCell();
            SetStatus($"商店编辑读取完成：{_currentShopEditorData.Rows.Count} 行");
        }
        catch (Exception ex)
        {
            _shopEditorInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("读取商店编辑失败：" + ex);
            MessageBox.Show(this, ex.Message, "读取商店编辑失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private DataTable BuildShopEditorData(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var result = _shopEditorService.Build(project, tables);
        _shopCampaignNameRead = result.CampaignNameRead;
        _shopDataRead = result.ShopDataRead;
        _shopEditorPersonNames = result.PersonNames;
        _shopEditorItemInfos = result.ItemInfos;
        _shopEditorItemNames = result.ItemNames;
        return result.Data;
    }

    private void ConfigureShopEditorGrid()
    {
        if (_currentShopEditorData == null) return;
        _shopEditorGrid.ReadOnly = false;
        ReplaceShopItemSlotColumnsWithCombo();
        ConfigureShopBatchControls();
        foreach (DataGridViewColumn column in _shopEditorGrid.Columns)
        {
            column.ReadOnly = IsShopReadOnlyColumn(column.DataPropertyName);
            column.ToolTipText = BuildShopColumnAnnotation(column.DataPropertyName);
            column.HeaderText = BuildShopColumnHeader(column.DataPropertyName);
            column.SortMode = DataGridViewColumnSortMode.Automatic;

            column.Width = column.DataPropertyName switch
            {
                "ID" => 58,
                "槽位类型" => 96,
                "关卡名称" => 180,
                "开关仓库人物" or "买卖物品人物" => 92,
                "开关仓库人物名" or "买卖物品人物名" => 130,
                "装备摘要" or "道具摘要" => 260,
                _ when IsShopItemSlotColumn(column.DataPropertyName) => 180,
                _ => 100
            };

            if (column.ReadOnly)
            {
                column.DefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);
            }
        }

        RefreshShopEditorRowStyles();
    }

    private void ReplaceShopItemSlotColumnsWithCombo()
    {
        if (_currentShopEditorData == null) return;
        var lookup = BuildShopItemLookupTable(includeEmpty: true);
        foreach (var columnName in _currentShopEditorData.Columns
                     .Cast<DataColumn>()
                     .Select(column => column.ColumnName)
                     .Where(IsShopItemSlotColumn)
                     .ToList())
        {
            if (!_shopEditorGrid.Columns.Contains(columnName)) continue;
            if (_shopEditorGrid.Columns[columnName] is DataGridViewComboBoxColumn) continue;

            var old = _shopEditorGrid.Columns[columnName];
            var index = old.Index;
            _shopEditorGrid.Columns.Remove(old);
            var combo = new DataGridViewComboBoxColumn
            {
                Name = columnName,
                DataPropertyName = columnName,
                DataSource = lookup.Copy(),
                ValueMember = "ID",
                DisplayMember = "显示",
                DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
                FlatStyle = FlatStyle.Flat,
                SortMode = DataGridViewColumnSortMode.Automatic,
                Width = 180
            };
            _shopEditorGrid.Columns.Insert(index, combo);
        }
    }

    private DataTable BuildShopItemLookupTable(bool includeEmpty)
    {
        var lookup = new DataTable("ShopItemLookup");
        lookup.Columns.Add("ID", typeof(int));
        lookup.Columns.Add("显示", typeof(string));
        foreach (var item in _shopEditorItemInfos.Values
                     .Where(item => includeEmpty || item.Id != ShopEditorService.EmptyShopItemId)
                     .OrderBy(item => item.Id == ShopEditorService.EmptyShopItemId ? -1 : item.Id))
        {
            lookup.Rows.Add(item.Id, BuildShopItemLookupDisplayName(item));
        }
        return lookup;
    }

    private static string BuildShopItemLookupDisplayName(ShopItemInfo item)
    {
        if (item.Id == ShopEditorService.EmptyShopItemId) return item.DisplayName;
        var suffix = ShopEditorService.IsPlaceholderShopItemName(item.Name) ? "｜占位" : string.Empty;
        return item.DisplayName + suffix;
    }

    private void ConfigureShopBatchControls()
    {
        if (_currentShopEditorData == null) return;
        var slotLookup = new DataTable("ShopSlotLookup");
        slotLookup.Columns.Add("列名", typeof(string));
        slotLookup.Columns.Add("显示", typeof(string));
        slotLookup.Rows.Add("装备1-16", "装备1-16");
        slotLookup.Rows.Add("道具17-32", "道具17-32");
        slotLookup.Rows.Add("全部物品槽", "全部物品槽");
        foreach (var columnName in _currentShopEditorData.Columns
                     .Cast<DataColumn>()
                     .Select(column => column.ColumnName)
                     .Where(IsShopItemSlotColumn)
                     .OrderBy(GetShopSlotSortKey))
        {
            slotLookup.Rows.Add(columnName, BuildShopSlotDisplayName(columnName));
        }

        _shopBatchSlotCombo.DataSource = slotLookup;
        _shopBatchSlotCombo.ValueMember = "列名";
        _shopBatchSlotCombo.DisplayMember = "显示";

        _shopBatchSetItemCombo.DataSource = BuildShopItemLookupTable(includeEmpty: true);
        _shopBatchSetItemCombo.ValueMember = "ID";
        _shopBatchSetItemCombo.DisplayMember = "显示";
        _shopBatchFindItemCombo.DataSource = BuildShopItemLookupTable(includeEmpty: true);
        _shopBatchFindItemCombo.ValueMember = "ID";
        _shopBatchFindItemCombo.DisplayMember = "显示";
        _shopBatchReplaceItemCombo.DataSource = BuildShopItemLookupTable(includeEmpty: true);
        _shopBatchReplaceItemCombo.ValueMember = "ID";
        _shopBatchReplaceItemCombo.DisplayMember = "显示";

        _shopBatchSetButton.Enabled = true;
        _shopBatchClearButton.Enabled = true;
        _shopBatchReplaceButton.Enabled = true;
    }

    private static bool IsShopReadOnlyColumn(string columnName)
        => columnName is "ID" or "槽位类型" or "开关仓库人物名" or "买卖物品人物名" or "装备摘要" or "道具摘要";

    private bool IsShopRawColumn(string columnName)
        => _shopDataRead?.Data.Columns.Contains(columnName) == true && columnName != "ID";

    private bool IsShopPersonColumn(string columnName)
        => columnName is "开关仓库人物" or "买卖物品人物";

    private bool IsShopItemSlotColumn(string columnName)
        => IsShopRawColumn(columnName) && !IsShopPersonColumn(columnName);

    private int GetShopSlotSortKey(string columnName)
        => _shopEditorService.GetSlotSortKey(columnName);

    private bool TryGetShopSlotNumber(string columnName, out int slot)
        => _shopEditorService.TryGetSlotNumber(columnName, out slot);

    private string BuildShopSlotDisplayName(string columnName)
        => _shopEditorService.BuildSlotDisplayName(columnName);

    private string BuildShopSlotDisplayName(int slot)
        => _shopEditorService.BuildSlotDisplayName(slot);

    private string BuildShopColumnHeader(string columnName)
    {
        return columnName switch
        {
            "ID" => "ID\n关卡/商店编号",
            "槽位类型" => "槽位类型\n普通/扩展",
            "关卡名称" => "关卡名称\n6.5-8 战役名称",
            "开关仓库人物名" or "买卖物品人物名" => columnName + "\n人物名预览",
            "装备摘要" => "装备摘要\n非空槽预览",
            "道具摘要" => "道具摘要\n非空槽预览",
            _ when IsShopPersonColumn(columnName) => columnName + "\n人物编号",
            _ when TryGetShopSlotNumber(columnName, out var slot) => $"{BuildShopSlotDisplayName(slot)}\n{(slot <= 16 ? "装备槽" : "道具槽")} {slot}",
            _ => columnName
        };
    }

    private string BuildShopColumnAnnotation(string columnName)
    {
        if (columnName == "ID") return "商店/关卡编号。普通关卡名称只在 6.5-8 战役名称表已有范围内写回。";
        if (columnName == "槽位类型") return "普通关卡来自战役名称表；扩展商店槽来自商店数据表的额外行，当前没有对应战役名称写回位置。";
        if (columnName == "关卡名称") return "写入 6.5-8 战役名称 / Imsg.e5，固定 200 字节 GBK 容量。";
        if (columnName == "开关仓库人物") return "2 字节人物编号，通常用于控制仓库/商店打开角色；255/65535 可作为空或不指定候选，仍需实机验证。";
        if (columnName == "买卖物品人物") return "2 字节人物编号，通常用于控制买卖物品角色；255/65535 可作为空或不指定候选，仍需实机验证。";
        if (columnName is "开关仓库人物名" or "买卖物品人物名") return "只读预览列，根据人物表名称解析当前人物编号。";
        if (columnName == "装备摘要") return "只读预览列，汇总装备 1-16 中非 255 的物品槽。";
        if (columnName == "道具摘要") return "只读预览列，汇总道具 17-32 中非 255 的物品槽。";
        if (IsShopItemSlotColumn(columnName))
        {
            return "物品编号；255 为空槽。";
        }

        return string.Empty;
    }

    private string BuildShopEditorSummary(DataTable data)
        => _shopEditorService.BuildSummary(data);

    private int CountShopNonEmptyItemSlots(DataRow row)
        => _shopEditorService.CountNonEmptyItemSlots(row);

    private void ApplyShopEditorFilter()
    {
        if (_currentShopEditorData == null) return;
        var keyword = _shopEditorSearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            _currentShopEditorData.DefaultView.RowFilter = string.Empty;
            SetStatus("商店编辑筛选已清除");
            RefreshShopEditorRowStyles();
            return;
        }

        var escaped = EscapeDataViewLikeValue(keyword);
        var filters = _currentShopEditorData.Columns
            .Cast<DataColumn>()
            .Select(column => $"CONVERT([{column.ColumnName}], 'System.String') LIKE '*{escaped}*'");
        _currentShopEditorData.DefaultView.RowFilter = string.Join(" OR ", filters);
        SetStatus($"商店编辑筛选：{_currentShopEditorData.DefaultView.Count}/{_currentShopEditorData.Rows.Count}");
        RefreshShopEditorRowStyles();
    }

    private void ClearShopEditorFilter()
    {
        _shopEditorSearchBox.Clear();
        if (_currentShopEditorData != null) _currentShopEditorData.DefaultView.RowFilter = string.Empty;
        SetStatus("商店编辑筛选已清除");
        RefreshShopEditorRowStyles();
    }

    private void ExportShopEditorCsv()
        => ExportDataTableCsv(_currentShopEditorData, "商店编辑.csv");

    private void ImportShopEditorCsv()
    {
        if (_currentShopEditorData == null) return;
        using var dialog = new OpenFileDialog
        {
            Title = "Import CSV",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var changedCells = Array.Empty<GridCellKey>() as IReadOnlyList<GridCellKey>;
            CsvImportResult result = null!;
            SuspendGridRepaintPreservingViewport(_shopEditorGrid, () =>
            {
                result = CsvService.ImportIntoWithChanges(_currentShopEditorData, dialog.FileName, allowPartialColumns: true, matchByIdWhenPresent: true);
                NormalizeShopItemSlotEmptyValues(_currentShopEditorData);
                changedCells = result.ChangedCells
                    .Select(cell => new GridCellKey(cell.RowKey, cell.RowIndex, cell.ColumnName))
                    .ToList();
                RefreshShopEditorCellsAfterEdit(changedCells);
            });

            ValidateAllShopEditorEditableCells();
            System.Diagnostics.Debug.WriteLine($"已导入商店 CSV：{dialog.FileName}；行 {result.ImportedRows}；格 {changedCells.Count}");
            SetStatus($"导入完成：{result.ImportedRows} 行，{changedCells.Count} 格");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("商店 CSV 导入失败：" + ex);
            MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void NormalizeShopItemSlotEmptyValues(DataTable? data)
    {
        if (data == null) return;
        foreach (DataRow row in data.Rows)
        {
            foreach (DataColumn column in data.Columns)
            {
                if (!IsShopItemSlotColumn(column.ColumnName)) continue;
                var value = row[column];
                if (value != null &&
                    value != DBNull.Value &&
                    !string.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture)))
                {
                    continue;
                }

                row[column] = Convert.ChangeType(ShopEditorService.EmptyShopItemId, column.DataType, CultureInfo.InvariantCulture);
            }
        }
    }

    private void RefreshShopEditorAfterBulkEdit()
    {
        if (_currentShopEditorData != null)
        {
            foreach (DataRow row in _currentShopEditorData.Rows)
            {
                RefreshShopDerivedCells(row);
            }
        }

        RefreshShopEditorRowStyles();
        ShowSelectedShopEditorCell();
    }

    private void ApplyShopBatchSet()
    {
        if (!TryGetSelectedShopBatchItem(_shopBatchSetItemCombo, out var itemId)) return;
        ApplyShopBatchUpdate(
            $"批量填入 {BuildShopItemName(itemId)}",
            (_, columnName) => itemId,
            onlyWhenMatches: null);
    }

    private void ApplyShopBatchClear()
    {
        ApplyShopBatchUpdate("批量清空为空槽", (_, _) => ShopEditorService.EmptyShopItemId, onlyWhenMatches: null);
    }

    private void ApplyShopBatchReplace()
    {
        if (!TryGetSelectedShopBatchItem(_shopBatchFindItemCombo, out var findId)) return;
        if (!TryGetSelectedShopBatchItem(_shopBatchReplaceItemCombo, out var replaceId)) return;
        ApplyShopBatchUpdate(
            $"批量替换 {BuildShopItemName(findId)} -> {BuildShopItemName(replaceId)}",
            (_, _) => replaceId,
            onlyWhenMatches: findId);
    }

    private bool TryGetSelectedShopBatchItem(ComboBox combo, out int itemId)
    {
        itemId = ShopEditorService.EmptyShopItemId;
        if (combo.SelectedValue == null ||
            !int.TryParse(Convert.ToString(combo.SelectedValue, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out itemId))
        {
            MessageBox.Show(this, "请先选择一个物品候选。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        return true;
    }

    private void ApplyShopBatchUpdate(string actionName, Func<DataRow, string, int> valueFactory, int? onlyWhenMatches)
    {
        if (_currentShopEditorData == null) return;
        _shopEditorGrid.EndEdit();
        var rows = GetShopBatchTargetRows();
        var columns = GetShopBatchTargetColumns();
        if (rows.Count == 0 || columns.Count == 0)
        {
            MessageBox.Show(this, "当前批量范围没有可修改的商店物品槽。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var changed = 0;
        var changedRows = new HashSet<DataRow>();
        foreach (var row in rows)
        {
            foreach (var columnName in columns)
            {
                if (!row.Table.Columns.Contains(columnName)) continue;
                var current = Convert.ToInt32(row[columnName], CultureInfo.InvariantCulture);
                if (onlyWhenMatches.HasValue && current != onlyWhenMatches.Value) continue;
                var next = valueFactory(row, columnName);
                if (current == next) continue;
                row[columnName] = next;
                changed++;
                changedRows.Add(row);
            }
        }

        RefreshChangedShopEditorRows(changedRows);
        ShowSelectedShopEditorCell();
        SetStatus($"{actionName}：修改 {changed} 个槽位，需点击“保存商店”写回");
    }

    private List<DataRow> GetShopBatchTargetRows()
    {
        if (_currentShopEditorData == null) return new List<DataRow>();
        var scope = Convert.ToString(_shopBatchScopeCombo.SelectedItem, CultureInfo.InvariantCulture) ?? "当前筛选行";
        if (scope == "选中行")
        {
            var rows = _shopEditorGrid.SelectedRows
                .Cast<DataGridViewRow>()
                .Concat(_shopEditorGrid.SelectedCells.Cast<DataGridViewCell>().Select(cell => cell.OwningRow))
                .Where(row => row != null && !row.IsNewRow)
                .Select(TryGetDataRow)
                .Where(row => row != null)
                .Cast<DataRow>()
                .Distinct()
                .ToList();
            if (rows.Count == 0 && _shopEditorGrid.CurrentRow != null && TryGetDataRow(_shopEditorGrid.CurrentRow) is { } currentRow)
            {
                rows.Add(currentRow);
            }
            return rows;
        }

        if (scope == "全部行")
        {
            return _currentShopEditorData.Rows.Cast<DataRow>().ToList();
        }

        return _currentShopEditorData.DefaultView
            .Cast<DataRowView>()
            .Select(view => view.Row)
            .ToList();
    }

    private List<string> GetShopBatchTargetColumns()
    {
        if (_currentShopEditorData == null) return new List<string>();
        var selected = Convert.ToString(_shopBatchSlotCombo.SelectedValue, CultureInfo.InvariantCulture) ?? string.Empty;
        return _currentShopEditorData.Columns
            .Cast<DataColumn>()
            .Select(column => column.ColumnName)
            .Where(IsShopItemSlotColumn)
            .Where(columnName => selected switch
            {
                "装备1-16" => TryGetShopSlotNumber(columnName, out var slot) && slot <= 16,
                "道具17-32" => TryGetShopSlotNumber(columnName, out var slot) && slot >= 17,
                "全部物品槽" => true,
                _ => string.Equals(columnName, selected, StringComparison.Ordinal)
            })
            .OrderBy(GetShopSlotSortKey)
            .ToList();
    }

    private void RefreshShopEditorRowStyles()
    {
        foreach (DataGridViewRow row in _shopEditorGrid.Rows)
        {
            RefreshShopEditorRowStyle(row.Index);
        }
    }

    private void RefreshChangedShopEditorRows(IEnumerable<DataRow> changedRows)
    {
        var changedRowSet = changedRows.ToHashSet();
        if (changedRowSet.Count == 0) return;

        foreach (var row in changedRowSet)
        {
            RefreshShopDerivedCells(row);
        }

        foreach (DataGridViewRow gridRow in _shopEditorGrid.Rows)
        {
            var dataRow = TryGetDataRow(gridRow);
            if (dataRow != null && changedRowSet.Contains(dataRow))
            {
                RefreshShopEditorRowStyle(gridRow.Index);
            }
        }
    }

    private void RefreshShopEditorCellsAfterEdit(IReadOnlyList<GridCellKey> changedCells)
    {
        if (_currentShopEditorData == null) return;

        var changedRows = changedCells
            .Select(key => FindDataRowByGridCellKey(_currentShopEditorData, key, "ID"))
            .Where(row => row is { RowState: not DataRowState.Detached })
            .Cast<DataRow>()
            .Distinct()
            .ToList();

        RefreshChangedShopEditorRows(changedRows);
        RefreshChangedGridCells(_shopEditorGrid, changedCells, UpdateShopEditorDerivedCells);
        ShowSelectedShopEditorCell();
    }

    private void RefreshShopEditorRowStyle(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _shopEditorGrid.Rows.Count) return;
        var gridRow = _shopEditorGrid.Rows[rowIndex];
        var dataRow = TryGetDataRow(gridRow);
        if (dataRow == null) return;

        gridRow.DefaultCellStyle.BackColor = IsDataRowChanged(dataRow) ? Color.LightCyan : Color.Empty;
        var id = Convert.ToInt32(dataRow["ID"], CultureInfo.InvariantCulture);
        if (_shopCampaignNameRead != null && id >= _shopCampaignNameRead.Data.Rows.Count && gridRow.Cells["关卡名称"] is { } nameCell)
        {
            nameCell.ReadOnly = true;
            nameCell.Style.BackColor = Color.FromArgb(245, 245, 245);
        }

        foreach (DataGridViewCell cell in gridRow.Cells)
        {
            if (!IsShopItemSlotColumn(cell.OwningColumn.DataPropertyName)) continue;
            var text = Convert.ToString(cell.Value, CultureInfo.InvariantCulture) ?? string.Empty;
            cell.Style.BackColor = text == "255" ? Color.FromArgb(248, 248, 248) : Color.Empty;
        }
    }

    private void ShowSelectedShopEditorCell()
    {
        if (_currentShopEditorData == null || _shopEditorGrid.CurrentCell == null) return;
        var cell = _shopEditorGrid.CurrentCell;
        var columnName = _shopEditorGrid.Columns[cell.ColumnIndex].DataPropertyName;
        var row = _shopEditorGrid.Rows[cell.RowIndex];
        var dataRow = TryGetDataRow(row);
        if (dataRow == null) return;

        var id = Convert.ToInt32(dataRow["ID"], CultureInfo.InvariantCulture);
        var title = Convert.ToString(dataRow["关卡名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        var value = Convert.ToString(cell.Value, CultureInfo.InvariantCulture) ?? string.Empty;
        var extra = columnName switch
        {
            "关卡名称" => $"\r\nGBK 字节：{EncodingService.GetGbkByteCount(value)}/{GetShopCampaignNameCapacity()}",
            "开关仓库人物" or "买卖物品人物" => $"\r\n人物预览：{BuildShopPersonName(ParseIntOrDefault(value, -1))}",
            _ when IsShopItemSlotColumn(columnName) => "\r\n" + BuildShopItemDetailText(ParseIntOrDefault(value, -1)),
            _ => string.Empty
        };

        _shopEditorInfoBox.Text =
            $"商店：ID={id}    关卡名称={title}    类型={dataRow["槽位类型"]}\r\n" +
            $"字段：{columnName}    当前值：{value}{extra}\r\n\r\n" +
            BuildShopColumnAnnotation(columnName) +
            "\r\n\r\n当前商店摘要：\r\n" +
            $"开关仓库人物：{dataRow["开关仓库人物"]} / {dataRow["开关仓库人物名"]}\r\n" +
            $"买卖物品人物：{dataRow["买卖物品人物"]} / {dataRow["买卖物品人物名"]}\r\n" +
            $"装备：{dataRow["装备摘要"]}\r\n" +
            $"道具：{dataRow["道具摘要"]}";
    }

    private int GetShopCampaignNameCapacity()
    {
        return _shopCampaignNameRead?.Table.Fields.FirstOrDefault(field => field.ColumnName == "名称")?.Size ?? 200;
    }

    private static int ParseIntOrDefault(string value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private string BuildShopPersonName(int id)
        => _shopEditorService.BuildPersonName(_shopEditorPersonNames, id);

    private string BuildShopItemName(int id)
        => _shopEditorService.BuildItemName(_shopEditorItemInfos, _shopEditorItemNames, id);

    private string BuildShopItemDetailText(int id)
        => _shopEditorService.BuildItemDetailText(_shopEditorItemInfos, id);

    private void RefreshShopDerivedCells(DataRow row)
        => _shopEditorService.RefreshDerivedCells(row, _shopEditorPersonNames, _shopEditorItemInfos, _shopEditorItemNames);

    private string BuildShopSlotSummary(DataRow row, bool equipmentSlots)
        => _shopEditorService.BuildSlotSummary(row, equipmentSlots, _shopEditorItemInfos, _shopEditorItemNames);

    private void UpdateShopEditorDerivedCells(int rowIndex, int columnIndex)
    {
        if (_currentShopEditorData == null || rowIndex < 0 || rowIndex >= _shopEditorGrid.Rows.Count || columnIndex < 0) return;
        var row = TryGetDataRow(_shopEditorGrid.Rows[rowIndex]);
        if (row == null) return;
        var columnName = _shopEditorGrid.Columns[columnIndex].DataPropertyName;
        if (IsShopPersonColumn(columnName) || IsShopItemSlotColumn(columnName))
        {
            RefreshShopDerivedCells(row);
        }
    }

    private void ValidateShopEditorCell(DataGridViewCellValidatingEventArgs e)
    {
        if (_shopEditorGrid.ReadOnly || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var column = _shopEditorGrid.Columns[e.ColumnIndex];
        if (column.ReadOnly) return;
        var columnName = column.DataPropertyName;
        var value = Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture) ?? string.Empty;
        var row = TryGetDataRow(_shopEditorGrid.Rows[e.RowIndex]);
        var error = ValidateShopEditorCellText(columnName, value, row, comboBoxColumn: column is DataGridViewComboBoxColumn);

        _shopEditorGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = error ?? string.Empty;
        if (error == null) return;
        e.Cancel = true;
        _shopEditorInfoBox.Text = error;
        SetStatus(error);
    }

    private string? ValidateShopEditorCellText(
        string columnName,
        string value,
        DataRow? row,
        bool comboBoxColumn = false,
        bool validateShopItemSemantics = true)
    {
        if (columnName == "关卡名称")
        {
            var id = row == null ? -1 : Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
            if (_shopCampaignNameRead != null && id >= _shopCampaignNameRead.Data.Rows.Count && !string.IsNullOrWhiteSpace(value))
            {
                return "扩展商店槽没有对应的战役名称写回位置，请只修改商店属性。";
            }

            var bytes = EncodingService.GetGbkByteCount(value);
            var capacity = GetShopCampaignNameCapacity();
            if (bytes > capacity) return $"关卡名称超长：GBK {bytes} 字节，容量 {capacity} 字节。";
        }
        else if (IsShopPersonColumn(columnName))
        {
            return TryParseInteger(value, 0, ushort.MaxValue, columnName, _currentPageHexButton.Checked);
        }
        else if (IsShopItemSlotColumn(columnName))
        {
            var normalizedValue = NormalizeShopItemSlotEditorText(value);
            var rangeError = TryParseInteger(normalizedValue, 0, byte.MaxValue, columnName, _currentPageHexButton.Checked);
            if (rangeError != null) return rangeError;
            if (!TryParseIntegerInput(normalizedValue, _currentPageHexButton.Checked, out var parsed) ||
                parsed.IsNegative ||
                parsed.Magnitude > byte.MaxValue)
            {
                return $"字段 {columnName} 需要整数。";
            }

            if (validateShopItemSemantics)
            {
                var itemId = (int)parsed.Magnitude;
                return _shopEditorService.TryValidateShopItemSlotValue(_shopEditorItemInfos, itemId, out var error)
                    ? null
                    : error;
            }
        }

        return null;
    }

    private object? ConvertShopEditorValueForColumn(string columnName, string text)
    {
        if (_currentShopEditorData == null || !_currentShopEditorData.Columns.Contains(columnName)) return text;
        var dataColumn = _currentShopEditorData.Columns[columnName];
        if (dataColumn == null || dataColumn.DataType == typeof(string)) return text;
        if (IsShopItemSlotColumn(columnName))
        {
            text = NormalizeShopItemSlotEditorText(text);
            if (TryParseIntegerInput(text, _currentPageHexButton.Checked, out var shopItemParsed) &&
                !shopItemParsed.IsNegative &&
                shopItemParsed.Magnitude <= byte.MaxValue)
            {
                return (int)shopItemParsed.Magnitude;
            }
        }

        if (IsSupportedIntegerType(dataColumn.DataType) &&
            TryParseIntegerInput(text, _currentPageHexButton.Checked, out var parsed) &&
            TryConvertParsedIntegerToType(parsed, dataColumn.DataType, out var converted))
        {
            return converted;
        }

        return Convert.ChangeType(text, dataColumn.DataType, CultureInfo.InvariantCulture);
    }

    private string NormalizeShopItemSlotEditorText(string text)
    {
        text = text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return ShopEditorService.EmptyShopItemId.ToString(CultureInfo.InvariantCulture);
        if (TryParseIntegerInput(text, _currentPageHexButton.Checked, out _)) return text;

        var match = Regex.Match(text, @"^\s*(\d{1,3})(?=\s|:|：|｜|$)");
        return match.Success ? match.Groups[1].Value : text;
    }

    private bool TryResolveShopEditorCell(int rowIndex, int columnIndex, out DataRow row, out string columnName)
    {
        row = null!;
        columnName = string.Empty;
        if (rowIndex < 0 || rowIndex >= _shopEditorGrid.Rows.Count ||
            columnIndex < 0 || columnIndex >= _shopEditorGrid.Columns.Count)
        {
            return false;
        }

        var column = _shopEditorGrid.Columns[columnIndex];
        if (column.ReadOnly || !column.Visible) return false;
        var cell = _shopEditorGrid.Rows[rowIndex].Cells[columnIndex];
        if (cell.ReadOnly) return false;
        columnName = column.DataPropertyName;
        if (string.IsNullOrEmpty(columnName) || _currentShopEditorData?.Columns.Contains(columnName) != true) return false;
        row = TryGetDataRow(_shopEditorGrid.Rows[rowIndex])!;
        return row != null;
    }

    private bool TrySetShopEditorCellValue(
        int rowIndex,
        int columnIndex,
        string text,
        ISet<DataRow> changedRows,
        out string error)
    {
        error = string.Empty;
        if (!TryResolveShopEditorCell(rowIndex, columnIndex, out var row, out var columnName)) return false;
        error = ValidateShopEditorCellText(
            columnName,
            text,
            row,
            comboBoxColumn: false) ?? string.Empty;
        if (!string.IsNullOrEmpty(error))
        {
            _shopEditorGrid.Rows[rowIndex].Cells[columnIndex].ErrorText = error;
            return false;
        }

        try
        {
            var oldValue = NormalizeGridCellValue(row[columnName]);
            var newValue = ConvertShopEditorValueForColumn(columnName, text);
            if (Equals(oldValue, newValue)) return false;

            row[columnName] = newValue ?? DBNull.Value;
            _shopEditorGrid.Rows[rowIndex].Cells[columnIndex].ErrorText = string.Empty;
            changedRows.Add(row);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _shopEditorGrid.Rows[rowIndex].Cells[columnIndex].ErrorText = error;
            return false;
        }
    }

    private void PasteShopEditorSelection()
    {
        if (_shopEditorGrid.ReadOnly)
        {
            SetStatus("当前表格不可编辑。");
            return;
        }

        if (!Clipboard.ContainsText())
        {
            SetStatus("剪贴板没有文本。");
            return;
        }

        var start = GetPasteStartCell(_shopEditorGrid);
        if (start == null)
        {
            SetStatus("请先选中粘贴起点。");
            return;
        }

        if (!_shopEditorGrid.EndEdit())
        {
            SetStatus("当前单元格未能提交，无法粘贴。");
            return;
        }

        var matrix = ParseClipboardMatrix(Clipboard.GetText());
        var changedRows = new HashSet<DataRow>();
        var changed = 0;
        var lastCell = start.Value;
        var lastError = string.Empty;
        for (var r = 0; r < matrix.Count; r++)
        {
            var rowIndex = start.Value.Row + r;
            if (rowIndex >= _shopEditorGrid.Rows.Count) break;

            for (var c = 0; c < matrix[r].Count; c++)
            {
                var columnIndex = start.Value.Column + c;
                if (columnIndex >= _shopEditorGrid.Columns.Count) break;

                if (TrySetShopEditorCellValue(rowIndex, columnIndex, matrix[r][c], changedRows, out var error))
                {
                    changed++;
                    lastCell = (rowIndex, columnIndex);
                }
                else if (!string.IsNullOrWhiteSpace(error))
                {
                    lastError = error;
                }
            }
        }

        if (changed > 0)
        {
            _shopEditorGrid.CurrentCell = _shopEditorGrid.Rows[lastCell.Row].Cells[lastCell.Column];
            RefreshChangedShopEditorRows(changedRows);
            ShowSelectedShopEditorCell();
        }

        SetStatus(changed > 0
            ? $"商店编辑粘贴完成：更新 {changed} 个单元格。"
            : string.IsNullOrWhiteSpace(lastError) ? "商店编辑粘贴没有产生改动。" : lastError);
    }

    private void FillShopEditorSelectionWithCurrentValue()
    {
        if (_shopEditorGrid.ReadOnly)
        {
            SetStatus("当前表格不可编辑。");
            return;
        }

        if (_shopEditorGrid.CurrentCell == null)
        {
            SetStatus("请先选中用于批量填列的单元格。");
            return;
        }

        if (!_shopEditorGrid.EndEdit())
        {
            SetStatus("当前单元格未能提交，无法批量填列。");
            return;
        }

        var columnIndex = _shopEditorGrid.CurrentCell.ColumnIndex;
        var value = FormatGridValueForBatchInput(_shopEditorGrid.CurrentCell.Value);
        var targetCells = GetGridFillTargetCells(_shopEditorGrid, columnIndex);
        if (targetCells.Count <= 1)
        {
            SetStatus("请先选中要批量填列的多个商店单元格。");
            return;
        }

        var changedRows = new HashSet<DataRow>();
        var changed = 0;
        var lastError = string.Empty;
        foreach (var cell in targetCells)
        {
            if (TrySetShopEditorCellValue(cell.RowIndex, cell.ColumnIndex, value, changedRows, out var error))
            {
                changed++;
            }
            else if (!string.IsNullOrWhiteSpace(error))
            {
                lastError = error;
            }
        }

        if (changed > 0)
        {
            RefreshChangedShopEditorRows(changedRows);
            ShowSelectedShopEditorCell();
        }

        SetStatus(changed > 0
            ? $"商店编辑批量填列完成：更新 {changed} 个单元格。"
            : string.IsNullOrWhiteSpace(lastError) ? "商店编辑批量填列没有产生改动。" : lastError);
    }

    private bool ValidateAllShopEditorEditableCells()
    {
        if (_currentShopEditorData == null) return true;
        var invalid = 0;
        foreach (DataRow dataRow in _currentShopEditorData.Rows)
        {
            foreach (DataColumn column in _currentShopEditorData.Columns)
            {
                if (IsShopReadOnlyColumn(column.ColumnName)) continue;
                var value = Convert.ToString(dataRow[column], CultureInfo.InvariantCulture) ?? string.Empty;
                var error = ValidateShopEditorCellText(
                    column.ColumnName,
                    value,
                    dataRow,
                    comboBoxColumn: false,
                    validateShopItemSemantics: !IsShopItemSlotColumn(column.ColumnName) || IsRoleColumnChanged(dataRow, column.ColumnName));
                SetShopEditorCellError(dataRow, column.ColumnName, error ?? string.Empty);
                if (error != null) invalid++;
            }
        }

        if (invalid > 0)
        {
            SetStatus($"商店编辑 CSV 导入后发现 {invalid} 个无效单元格，请修正后再保存。");
            return false;
        }

        return true;
    }

    private void SetShopEditorCellError(DataRow dataRow, string columnName, string error)
    {
        foreach (DataGridViewRow gridRow in _shopEditorGrid.Rows)
        {
            if (!ReferenceEquals(TryGetDataRow(gridRow), dataRow)) continue;
            if (!_shopEditorGrid.Columns.Contains(columnName)) return;
            gridRow.Cells[columnName].ErrorText = error;
            return;
        }
    }

    private void SaveShopEditor()
    {
        if (_project == null || _currentShopEditorData == null || _shopCampaignNameRead == null || _shopDataRead == null) return;

        if (!_shopEditorGrid.EndEdit())
        {
            SetStatus("当前商店单元格未能提交，无法保存。");
            return;
        }

        if (!ValidateAllShopEditorEditableCells())
        {
            MessageBox.Show(this, "商店编辑存在无效单元格，请修正后再保存。", "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_currentShopEditorData.GetChanges() == null)
        {
            MessageBox.Show(this, "商店编辑没有检测到改动。", "无需保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var preview = BuildShopEditorChangePreview(_currentShopEditorData, maxItems: 60);
        if (MessageBox.Show(this,
                $"即将保存商店编辑到当前 MOD 项目。\r\n\r\n变更预览：\r\n{preview}\r\n\r\n保存前会自动备份 Imsg.e5/Data.e5，保存后会重新读取校验。是否继续？",
                "确认保存商店编辑",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var changedCells = GetChangedCellKeys(_currentShopEditorData);
            var saves = SaveShopEditorData(_project, _currentShopEditorData);
            AcceptSavedDataTable(_currentShopEditorData);
            RefreshShopEditorCellsAfterEdit(changedCells);
            var changedBytes = saves.Sum(x => x.ChangedBytes);
            System.Diagnostics.Debug.WriteLine($"已保存商店编辑：保存表 {saves.Count} 个，变化字节 {changedBytes}");
            foreach (var save in saves) System.Diagnostics.Debug.WriteLine("商店编辑备份：" + save.BackupPath);
            SetStatus($"保存完成：{changedBytes} 字节");
            MessageBox.Show(this,
                $"保存完成：{changedBytes} 字节",
                "保存完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("保存商店编辑失败：" + ex);
            MessageBox.Show(this, ex.Message, "保存商店编辑失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private IReadOnlyList<TableSaveResult> SaveShopEditorData(CczProject project, DataTable shopEditorData)
    {
        if (_shopCampaignNameRead == null || _shopDataRead == null) return Array.Empty<TableSaveResult>();
        var changedShopRows = shopEditorData.Rows
            .Cast<DataRow>()
            .Where(row => row.RowState == DataRowState.Modified)
            .ToList();
        var changedRowIds = changedShopRows
            .Select(row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture))
            .ToHashSet();
        var changedShopSlotKeys = BuildChangedShopSlotKeys(changedShopRows);
        var changedShopSlotIssues = _shopEditorService.ValidateShopItemSlots(
            shopEditorData,
            _shopEditorItemInfos,
            row => row.RowState == DataRowState.Modified,
            changedItemSlotsOnly: true,
            slotFilter: (rowId, columnName) => changedShopSlotKeys.Contains((rowId, columnName)));
        if (changedShopSlotIssues.Count > 0)
        {
            throw new InvalidOperationException(BuildShopSlotValidationErrorText(changedShopSlotIssues, null));
        }

        var campaignRowsById = BuildRowsById(_shopCampaignNameRead.Data);
        var shopRowsById = BuildRowsById(_shopDataRead.Data);
        foreach (DataRow shopRow in changedShopRows)
        {
            var id = Convert.ToInt32(shopRow["ID"], CultureInfo.InvariantCulture);

            if (campaignRowsById.TryGetValue(id, out var campaignRow) && IsRoleColumnChanged(shopRow, "关卡名称"))
            {
                campaignRow["名称"] = shopRow["关卡名称", DataRowVersion.Current];
            }

            if (!shopRowsById.TryGetValue(id, out var rawRow)) continue;
            foreach (DataColumn column in _shopDataRead.Data.Columns)
            {
                if (column.ColumnName == "ID" || !shopEditorData.Columns.Contains(column.ColumnName)) continue;
                if (!IsRoleColumnChanged(shopRow, column.ColumnName)) continue;
                rawRow[column.ColumnName] = shopRow[column.ColumnName, DataRowVersion.Current];
            }
        }

        var saves = new List<TableSaveResult>();
        if (SaveChangedTableAndVerify(_shopCampaignNameRead) is { } campaignSave) saves.Add(campaignSave);
        if (SaveChangedTableAndVerify(_shopDataRead) is { } shopSave) saves.Add(shopSave);
        VerifySavedShopSlots(project, changedRowIds, changedShopSlotKeys, saves);
        return saves;
    }

    private HashSet<(int RowId, string ColumnName)> BuildChangedShopSlotKeys(IEnumerable<DataRow> changedShopRows)
    {
        var keys = new HashSet<(int RowId, string ColumnName)>();
        foreach (var row in changedShopRows)
        {
            var rowId = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
            foreach (DataColumn column in row.Table.Columns)
            {
                if (!IsShopItemSlotColumn(column.ColumnName)) continue;
                if (!IsRoleColumnChanged(row, column.ColumnName)) continue;
                keys.Add((rowId, column.ColumnName));
            }
        }

        return keys;
    }

    private void VerifySavedShopSlots(
        CczProject project,
        IReadOnlySet<int> changedRowIds,
        IReadOnlySet<(int RowId, string ColumnName)> changedShopSlotKeys,
        IReadOnlyList<TableSaveResult> saves)
    {
        if (_shopDataRead == null || changedRowIds.Count == 0 || changedShopSlotKeys.Count == 0) return;
        var verifyRead = _tableReader.Read(project, _shopDataRead.Table, _tables);
        if (!verifyRead.Validation.IsUsable)
        {
            throw new InvalidOperationException(
                "商店复读失败。\r\n备份：" +
                BuildShopSaveBackupText(saves));
        }

        var issues = _shopEditorService.ValidateShopItemSlots(
            verifyRead.Data,
            _shopEditorItemInfos,
            row => changedRowIds.Contains(Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture)),
            slotFilter: (rowId, columnName) => changedShopSlotKeys.Contains((rowId, columnName)));
        if (issues.Count > 0)
        {
            throw new InvalidOperationException(BuildShopSlotValidationErrorText(issues, saves));
        }
    }

    private static string BuildShopSlotValidationErrorText(
        IReadOnlyList<ShopSlotValidationIssue> issues,
        IReadOnlyList<TableSaveResult>? saves)
    {
        var lines = new List<string> { "占位物品不能入店；空槽用 255。" };
        lines.AddRange(issues
            .Take(40)
            .Select(issue => $"row={issue.RowId}, slot={issue.Slot}, value={issue.ItemId}, name={issue.ItemName}"));
        if (issues.Count > 40) lines.Add($"... 另有 {issues.Count - 40} 项");
        if (saves is { Count: > 0 }) lines.Add("备份：" + BuildShopSaveBackupText(saves));
        return string.Join("\r\n", lines);
    }

    private static string BuildShopSaveBackupText(IReadOnlyList<TableSaveResult> saves)
        => saves.Count == 0 ? "无" : string.Join("; ", saves.Select(save => save.BackupPath));

    private static Dictionary<int, DataRow> BuildRowsById(DataTable table)
    {
        var rows = table.Rows.Cast<DataRow>().ToList();
        var duplicates = DictionaryBuild.FindDuplicateKeys(
            rows,
            row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture));
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                "表格存在重复 ID，无法安全保存：" +
                string.Join(", ", duplicates.Select(duplicate => duplicate.Key)));
        }

        return DictionaryBuild.ToDictionaryFirstByKey(
            rows,
            row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture),
            row => row);
    }

    private string BuildShopEditorChangePreview(DataTable data, int maxItems)
    {
        var previewColumns = data.Columns
            .Cast<DataColumn>()
            .Where(column => column.ColumnName == "关卡名称" || IsShopRawColumn(column.ColumnName))
            .ToList();
        var lines = new List<string>();
        var total = 0;
        foreach (DataRow row in data.Rows)
        {
            if (row.RowState != DataRowState.Modified) continue;
            var id = Convert.ToString(row["ID"], CultureInfo.InvariantCulture) ?? string.Empty;
            foreach (var column in previewColumns)
            {
                if (!IsRoleColumnChanged(row, column.ColumnName)) continue;
                var originalText = Convert.ToString(row[column.ColumnName, DataRowVersion.Original], CultureInfo.InvariantCulture) ?? string.Empty;
                var currentText = Convert.ToString(row[column.ColumnName, DataRowVersion.Current], CultureInfo.InvariantCulture) ?? string.Empty;
                total++;
                if (lines.Count < maxItems)
                {
                    lines.Add($"ID={id} {column.ColumnName}: {TrimPreview(originalText)} -> {TrimPreview(currentText)}");
                }
            }
        }

        if (total == 0) return "未发现可写字段变更。";
        if (total > lines.Count) lines.Add($"……另有 {total - lines.Count} 项变更未显示。");
        return string.Join("\r\n", lines);
    }

    private void LoadJobTerrainEditor()
    {
        if (_project == null) return;
        if (_tables.Count == 0)
        {
            ReloadCurrentProject();
            if (_tables.Count == 0) return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            _currentJobTerrainData = BuildJobTerrainEditorData(_project, _tables);
            _jobTerrainGrid.DataSource = _currentJobTerrainData;
            ConfigureJobTerrainGrid();
            _saveJobTerrainButton.Enabled = true;
            _jobTerrainInfoBox.Text = BuildJobTerrainSummary(_currentJobTerrainData);
            SetStatus($"兵种系/地形读取完成：{_currentJobTerrainData.Rows.Count} 行");
        }
        catch (Exception ex)
        {
            _jobTerrainInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("读取兵种系/地形失败：" + ex);
            MessageBox.Show(this, ex.Message, "读取兵种系/地形失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private DataTable BuildJobTerrainEditorData(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        _jobSeriesRead = _tableReader.Read(project, FindTable(tables, "6.5-3 兵种系"), tables);
        _jobTerrainPowerRead = _tableReader.Read(project, FindTable(tables, "6.5-3-1 地形发挥"), tables);
        _jobMoveCostRead = _tableReader.Read(project, FindTable(tables, "6.5-3-2 移动消耗"), tables);
        if (!_jobSeriesRead.Validation.IsUsable || !_jobTerrainPowerRead.Validation.IsUsable || !_jobMoveCostRead.Validation.IsUsable)
        {
            throw new InvalidOperationException("兵种系/地形发挥/移动消耗表有不可读取项，请先查看数据表诊断。");
        }

        var output = new DataTable("兵种系/地形");
        output.Columns.Add("ID", typeof(int));
        output.Columns.Add("名称", typeof(string));

        var terrainNames = _jobTerrainPowerRead.Data.Columns
            .Cast<DataColumn>()
            .Select(column => column.ColumnName)
            .Where(name => name is not "ID" and not "名称")
            .Where(name => _jobMoveCostRead.Data.Columns.Contains(name))
            .ToList();
        foreach (var terrainName in terrainNames)
        {
            output.Columns.Add(BuildJobTerrainPowerColumnName(terrainName), typeof(int));
            output.Columns.Add(BuildJobTerrainMoveColumnName(terrainName), typeof(int));
        }

        var count = Math.Min(_jobSeriesRead.Data.Rows.Count, Math.Min(_jobTerrainPowerRead.Data.Rows.Count, _jobMoveCostRead.Data.Rows.Count));
        for (var i = 0; i < count; i++)
        {
            var row = output.NewRow();
            row["ID"] = Convert.ToInt32(_jobSeriesRead.Data.Rows[i]["ID"], CultureInfo.InvariantCulture);
            row["名称"] = Convert.ToString(_jobSeriesRead.Data.Rows[i]["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
            foreach (var terrainName in terrainNames)
            {
                row[BuildJobTerrainPowerColumnName(terrainName)] = _jobTerrainPowerRead.Data.Rows[i][terrainName];
                row[BuildJobTerrainMoveColumnName(terrainName)] = _jobMoveCostRead.Data.Rows[i][terrainName];
            }
            output.Rows.Add(row);
        }

        output.AcceptChanges();
        output.Columns["ID"]!.ReadOnly = true;
        return output;
    }

    private void ConfigureJobTerrainGrid()
    {
        if (_currentJobTerrainData == null) return;
        _jobTerrainGrid.ReadOnly = false;
        foreach (DataGridViewColumn column in _jobTerrainGrid.Columns)
        {
            column.ReadOnly = column.DataPropertyName == "ID";
            column.ToolTipText = BuildJobTerrainColumnAnnotation(column.DataPropertyName);
            column.HeaderText = BuildJobTerrainColumnHeader(column.DataPropertyName);
            if (column.DataPropertyName == "名称") column.Width = 120;
            if (TryGetJobTerrainSourceColumn(column.DataPropertyName, out _, out var kind))
            {
                column.DefaultCellStyle.BackColor = kind == "发挥"
                    ? Color.FromArgb(244, 250, 255)
                    : Color.FromArgb(250, 248, 239);
            }
        }

        RefreshJobTerrainRowStyles();
    }

    private static string BuildJobTerrainPowerColumnName(string terrainName) => terrainName + "发挥";

    private static string BuildJobTerrainMoveColumnName(string terrainName) => terrainName + "消耗";

    private static bool TryGetJobTerrainSourceColumn(string columnName, out string sourceColumnName, out string kind)
    {
        if (columnName.EndsWith("发挥", StringComparison.Ordinal))
        {
            sourceColumnName = columnName[..^2];
            kind = "发挥";
            return sourceColumnName.Length > 0;
        }
        if (columnName.EndsWith("消耗", StringComparison.Ordinal))
        {
            sourceColumnName = columnName[..^2];
            kind = "消耗";
            return sourceColumnName.Length > 0;
        }

        sourceColumnName = string.Empty;
        kind = string.Empty;
        return false;
    }

    private string BuildJobTerrainColumnHeader(string columnName)
    {
        if (columnName == "ID") return "ID\n兵种系编号";
        if (columnName == "名称") return "名称\n兵种系名称";
        if (TryGetJobTerrainSourceColumn(columnName, out var sourceColumnName, out var kind))
        {
            return $"{sourceColumnName}\n{kind}";
        }

        return columnName;
    }

    private string BuildJobTerrainColumnAnnotation(string columnName)
    {
        if (columnName == "ID") return "兵种系编号。地形发挥、移动消耗、兵种相克等表按这个编号逐行对齐。";
        if (columnName == "名称") return "兵种系名称，写入 Ekd5.exe；固定 9 字节 GBK 容量。这里是兵种大类，不是 80 行详细兵种名称。";
        if (!TryGetJobTerrainSourceColumn(columnName, out var sourceColumnName, out var kind)) return columnName;

        var tableRead = kind == "发挥" ? _jobTerrainPowerRead : _jobMoveCostRead;
        var field = tableRead?.Table.Fields.FirstOrDefault(f => f.ColumnName == sourceColumnName);
        var baseAnnotation = field == null || tableRead == null
            ? string.Empty
            : _fieldAnnotationService.BuildFieldAnnotation(tableRead.Table, field);
        var creatorHint = kind == "发挥"
            ? $"地形发挥：当前兵种系在“{sourceColumnName}”地形上的战斗适性/能力修正。数值越高通常越有利，常见平衡值需要结合原版习惯和实机测试确认。"
            : $"移动消耗：当前兵种系进入或通过“{sourceColumnName}”地形时消耗的移动力。数值越高越难通过，特殊不可通行值需要结合实机测试确认。";
        return string.IsNullOrWhiteSpace(baseAnnotation)
            ? creatorHint
            : baseAnnotation + "\r\n\r\n" + creatorHint;
    }

    private static string BuildJobTerrainSummary(DataTable data)
    {
        var named = data.Rows.Cast<DataRow>().Count(row => !string.IsNullOrWhiteSpace(Convert.ToString(row["名称"], CultureInfo.InvariantCulture)));
        var terrainCount = data.Columns.Cast<DataColumn>().Count(column => column.ColumnName.EndsWith("发挥", StringComparison.Ordinal));
        return
            $"兵种系/地形已读取：兵种系 {data.Rows.Count} 行，已命名 {named}，地形 {terrainCount} 种。\r\n" +
            "来源表：6.5-3 兵种系、6.5-3-1 地形发挥、6.5-3-2 移动消耗。\r\n" +
            "保存会写回 Ekd5.exe 与 Data.e5，保存前自动备份，保存后重新读取校验。";
    }

    private void ApplyJobTerrainFilter()
    {
        if (_currentJobTerrainData == null) return;
        var keyword = _jobTerrainSearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            _currentJobTerrainData.DefaultView.RowFilter = string.Empty;
            SetStatus("兵种系/地形筛选已清除");
            return;
        }

        var escaped = EscapeDataViewLikeValue(keyword);
        var filters = _currentJobTerrainData.Columns
            .Cast<DataColumn>()
            .Select(column => $"CONVERT([{column.ColumnName}], 'System.String') LIKE '*{escaped}*'");
        _currentJobTerrainData.DefaultView.RowFilter = string.Join(" OR ", filters);
        SetStatus($"兵种系/地形筛选：{_currentJobTerrainData.DefaultView.Count}/{_currentJobTerrainData.Rows.Count}");
    }

    private void ClearJobTerrainFilter()
    {
        _jobTerrainSearchBox.Clear();
        if (_currentJobTerrainData != null) _currentJobTerrainData.DefaultView.RowFilter = string.Empty;
        SetStatus("兵种系/地形筛选已清除");
    }

    private void RefreshJobTerrainRowStyles()
    {
        foreach (DataGridViewRow row in _jobTerrainGrid.Rows)
        {
            RefreshJobTerrainRowStyle(row.Index);
        }
    }

    private void RefreshJobTerrainRowStyle(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _jobTerrainGrid.Rows.Count) return;
        var dataRow = TryGetDataRow(_jobTerrainGrid.Rows[rowIndex]);
        if (dataRow == null) return;
        _jobTerrainGrid.Rows[rowIndex].DefaultCellStyle.BackColor = IsDataRowChanged(dataRow) ? Color.LightCyan : Color.Empty;
    }

    private void RefreshJobTerrainCellsAfterEdit(IReadOnlyList<GridCellKey> changedCells)
    {
        RefreshChangedGridCells(_jobTerrainGrid, changedCells);
        RefreshChangedGridRowsOnly(_jobTerrainGrid, changedCells, RefreshJobTerrainRowStyle);
        ShowSelectedJobTerrainCell();
    }

    private void ShowSelectedJobTerrainCell()
    {
        if (_currentJobTerrainData == null || _jobTerrainGrid.CurrentCell == null) return;
        var cell = _jobTerrainGrid.CurrentCell;
        var columnName = _jobTerrainGrid.Columns[cell.ColumnIndex].DataPropertyName;
        var row = _jobTerrainGrid.Rows[cell.RowIndex];
        var id = row.Cells["ID"].Value;
        var name = row.Cells["名称"].Value;
        var value = cell.Value;
        var extra = columnName == "名称"
            ? $"\r\nGBK 字节：{EncodingService.GetGbkByteCount(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)}/9"
            : string.Empty;
        _jobTerrainInfoBox.Text =
            $"兵种系：ID={id}    名称={name}\r\n" +
            $"字段：{columnName}    当前值：{value}{extra}\r\n\r\n" +
            BuildJobTerrainColumnAnnotation(columnName);
    }

    private void ValidateJobTerrainCell(DataGridViewCellValidatingEventArgs e)
    {
        if (_jobTerrainGrid.ReadOnly || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var column = _jobTerrainGrid.Columns[e.ColumnIndex];
        if (column.ReadOnly) return;
        var columnName = column.DataPropertyName;
        var value = Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture) ?? string.Empty;
        string? error = null;
        if (columnName == "名称")
        {
            var bytes = EncodingService.GetGbkByteCount(value);
            if (bytes > 9) error = $"兵种系名称超长：GBK {bytes} 字节，容量 9 字节。";
        }
        else if (TryGetJobTerrainSourceColumn(columnName, out _, out _))
        {
            error = TryParseInteger(value, 0, byte.MaxValue, columnName, _currentPageHexButton.Checked);
        }

        _jobTerrainGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = error ?? string.Empty;
        if (error == null) return;
        e.Cancel = true;
        _jobTerrainInfoBox.Text = error;
        SetStatus(error);
    }

    private void SaveJobTerrainEditor()
    {
        if (_project == null || _currentJobTerrainData == null || _jobSeriesRead == null || _jobTerrainPowerRead == null || _jobMoveCostRead == null) return;

        _jobTerrainGrid.EndEdit();
        if (_currentJobTerrainData.GetChanges() == null)
        {
            MessageBox.Show(this, "兵种系/地形没有检测到改动。", "无需保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var preview = BuildChangePreview(_currentJobTerrainData, maxItems: 60);
        if (MessageBox.Show(this,
                $"即将保存兵种系/地形到当前 MOD 项目。\r\n\r\n变更预览：\r\n{preview}\r\n\r\n保存前会自动备份 Ekd5.exe/Data.e5，保存后会重新读取校验。是否继续？",
                "确认保存兵种系/地形",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var changedCells = GetChangedCellKeys(_currentJobTerrainData);
            var saves = SaveJobTerrainEditorData(_project, _currentJobTerrainData);
            AcceptSavedDataTable(_currentJobTerrainData);
            RefreshJobTerrainCellsAfterEdit(changedCells);
            var changedBytes = saves.Sum(x => x.ChangedBytes);
            System.Diagnostics.Debug.WriteLine($"已保存兵种系/地形：保存表 {saves.Count} 个，变化字节 {changedBytes}");
            foreach (var save in saves) System.Diagnostics.Debug.WriteLine("兵种系/地形备份：" + save.BackupPath);
            SetStatus($"兵种系/地形保存完成并已复读：变化 {changedBytes} 字节");
            MessageBox.Show(this,
                $"保存完成并已重新读取校验。\r\n保存表数量：{saves.Count}\r\n变化字节：{changedBytes}\r\n备份：{string.Join("; ", saves.Select(x => x.BackupPath))}",
                "保存完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("保存兵种系/地形失败：" + ex);
            MessageBox.Show(this, ex.Message, "保存兵种系/地形失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private IReadOnlyList<TableSaveResult> SaveJobTerrainEditorData(CczProject project, DataTable terrainData)
    {
        if (_jobSeriesRead == null || _jobTerrainPowerRead == null || _jobMoveCostRead == null) return Array.Empty<TableSaveResult>();
        foreach (DataRow terrainRow in terrainData.Rows)
        {
            if (terrainRow.RowState != DataRowState.Modified) continue;
            var id = Convert.ToInt32(terrainRow["ID"], CultureInfo.InvariantCulture);
            var seriesRow = FindRowById(_jobSeriesRead.Data, id);
            var powerRow = FindRowById(_jobTerrainPowerRead.Data, id);
            var moveRow = FindRowById(_jobMoveCostRead.Data, id);

            if (IsRoleColumnChanged(terrainRow, "名称")) seriesRow["名称"] = terrainRow["名称", DataRowVersion.Current];
            foreach (DataColumn column in terrainData.Columns)
            {
                if (!IsRoleColumnChanged(terrainRow, column.ColumnName)) continue;
                if (!TryGetJobTerrainSourceColumn(column.ColumnName, out var sourceColumnName, out var kind)) continue;
                if (kind == "发挥" && _jobTerrainPowerRead.Data.Columns.Contains(sourceColumnName))
                {
                    powerRow[sourceColumnName] = terrainRow[column.ColumnName, DataRowVersion.Current];
                }
                else if (kind == "消耗" && _jobMoveCostRead.Data.Columns.Contains(sourceColumnName))
                {
                    moveRow[sourceColumnName] = terrainRow[column.ColumnName, DataRowVersion.Current];
                }
            }
        }

        var saves = new List<TableSaveResult>();
        if (SaveChangedTableAndVerify(_jobSeriesRead) is { } seriesSave) saves.Add(seriesSave);
        if (SaveChangedTableAndVerify(_jobTerrainPowerRead) is { } powerSave) saves.Add(powerSave);
        if (SaveChangedTableAndVerify(_jobMoveCostRead) is { } moveSave) saves.Add(moveSave);
        return saves;
    }

    private void LoadJobStrategyEditor()
    {
        if (_project == null) return;
        if (!CommitJobStrategyLearningEditorChanges(showMessage: true)) return;
        _importJobStrategyIconButton.Enabled = false;
        _editJobStrategyIconButton.Enabled = false;
        _exportJobStrategyIconBmpButton.Enabled = false;
        if (_tables.Count == 0)
        {
            ReloadCurrentProject();
            if (_tables.Count == 0) return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            CloseJobStrategyLearningDialogs();
            _jobStrategyLearningEditorBoundRow = null;
            HideJobStrategyLearningEditor();
            _jobStrategyLearningEditorData.Clear();
            _currentJobStrategyData = BuildJobStrategyEditorData(_project, _tables);
            _jobStrategyEditorGrid.DataSource = _currentJobStrategyData;
            ConfigureJobStrategyGrid();
            _saveJobStrategyEditorButton.Enabled = true;
            _importJobStrategyIconButton.Enabled = true;
            _editJobStrategyIconButton.Enabled = true;
            _exportJobStrategyIconBmpButton.Enabled = true;
            _jobStrategyEditorInfoBox.Text = BuildJobStrategySummary(_currentJobStrategyData);
            ShowSelectedJobStrategyCell();
            SetStatus($"兵种策略读取完成：{_currentJobStrategyData.Rows.Count} 个策略");
        }
        catch (Exception ex)
        {
            _jobStrategyEditorInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("读取兵种策略失败：" + ex);
            MessageBox.Show(this, ex.Message, "读取兵种策略失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private DataTable BuildJobStrategyEditorData(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        _jobStrategyRead = _tableReader.Read(project, FindTable(tables, "6.5-5 策略"), tables);
        _jobStrategyDescriptionRead = _tableReader.Read(project, FindTable(tables, "6.5-5-1 策略说明"), tables);
        _jobStrategyCompanionReads.Clear();
        foreach (var companion in JobStrategyCompanionColumns)
        {
            _jobStrategyCompanionReads[companion.ColumnName] = _tableReader.Read(project, FindTable(tables, companion.TableName), tables);
        }

        _jobStrategyJobNames = BuildIdNameLookup(project, tables, "6.5-4 详细兵种");
        (_jobStrategyConfiguredMagicCount, _jobStrategyConfiguredMagicSource) = ResolveStrategyMagicCount(project);
        if (!_jobStrategyRead.Validation.IsUsable || !_jobStrategyDescriptionRead.Validation.IsUsable || _jobStrategyCompanionReads.Values.Any(read => !read.Validation.IsUsable))
        {
            throw new InvalidOperationException("策略主表、策略说明或 EKD5 策略附表有不可读取项，请先查看数据表诊断。");
        }

        var output = new DataTable("兵种策略");
        output.Columns.Add("ID", typeof(int));
        foreach (var columnName in JobStrategyPrimaryColumns)
        {
            output.Columns.Add(columnName, columnName == "名称" ? typeof(string) : typeof(int));
        }

        foreach (var companion in JobStrategyCompanionColumns)
        {
            output.Columns.Add(companion.ColumnName, typeof(int));
        }

        foreach (var jobColumn in GetJobStrategyLearningSourceColumns(_jobStrategyRead.Data))
        {
            output.Columns.Add(BuildJobStrategyLearningColumnName(jobColumn), typeof(int));
        }

        output.Columns.Add("可学摘要", typeof(string));
        output.Columns.Add("策略介绍", typeof(string));

        for (var i = 0; i < _jobStrategyRead.Data.Rows.Count; i++)
        {
            var strategyRow = _jobStrategyRead.Data.Rows[i];
            var id = Convert.ToInt32(strategyRow["ID"], CultureInfo.InvariantCulture);
            var row = output.NewRow();
            row["ID"] = id;
            foreach (var columnName in JobStrategyPrimaryColumns)
            {
                row[columnName] = strategyRow[columnName];
            }

            foreach (var companion in JobStrategyCompanionColumns)
            {
                var companionData = _jobStrategyCompanionReads[companion.ColumnName].Data;
                var companionRow = FindRowById(companionData, id);
                row[companion.ColumnName] = companionRow["内容"];
            }

            foreach (var jobColumn in GetJobStrategyLearningSourceColumns(_jobStrategyRead.Data))
            {
                row[BuildJobStrategyLearningColumnName(jobColumn)] = strategyRow[jobColumn];
            }

            row["可学摘要"] = BuildJobStrategyLearningSummary(row);
            var descriptionRow = TryFindRowById(_jobStrategyDescriptionRead.Data, id);
            row["策略介绍"] = descriptionRow == null
                ? string.Empty
                : Convert.ToString(descriptionRow["介绍"], CultureInfo.InvariantCulture) ?? string.Empty;
            output.Rows.Add(row);
        }

        output.AcceptChanges();
        output.Columns["ID"]!.ReadOnly = true;
        output.Columns["可学摘要"]!.ReadOnly = true;
        return output;
    }

    private static IReadOnlyList<string> GetJobStrategyLearningSourceColumns(DataTable strategyData)
        => strategyData.Columns
            .Cast<DataColumn>()
            .Select(column => column.ColumnName)
            .Where(static name => int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id is >= 0 and <= 79)
            .OrderBy(static name => int.Parse(name, CultureInfo.InvariantCulture))
            .ToList();

    private static string BuildJobStrategyLearningColumnName(string jobIdColumnName) => JobStrategyLearningPrefix + jobIdColumnName;

    private static bool TryGetJobStrategyLearningSourceColumn(string columnName, out string sourceColumnName)
    {
        if (columnName.StartsWith(JobStrategyLearningPrefix, StringComparison.Ordinal) &&
            int.TryParse(columnName[JobStrategyLearningPrefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            sourceColumnName = columnName[JobStrategyLearningPrefix.Length..];
            return true;
        }

        sourceColumnName = string.Empty;
        return false;
    }

    private void ConfigureJobStrategyGrid()
    {
        if (_currentJobStrategyData == null) return;
        _jobStrategyEditorGrid.ReadOnly = false;
        ConfigureJobStrategyComboColumn("策略类型", JobStrategyTypeNames, "未命名策略类型");
        ConfigureJobStrategyComboColumn("施展对象", JobStrategyTargetNames, "施展对象");
        foreach (DataGridViewColumn column in _jobStrategyEditorGrid.Columns)
        {
            column.ReadOnly = column.DataPropertyName is "ID" or "可学摘要" or "策略介绍";
            column.ToolTipText = BuildJobStrategyColumnAnnotation(column.DataPropertyName);
            column.HeaderText = BuildJobStrategyColumnHeader(column.DataPropertyName);
            if (column.DataPropertyName == "名称") column.Width = 110;
            if (column.DataPropertyName is "可学摘要" or "策略介绍")
            {
                column.Visible = false;
                column.Width = 300;
            }

            if (column.ReadOnly)
            {
                column.DefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);
            }
            else if (TryGetJobStrategyLearningSourceColumn(column.DataPropertyName, out _))
            {
                column.Visible = false;
                column.MinimumWidth = Math.Max(column.MinimumWidth, 92);
                column.Width = Math.Max(column.Width, 92);
                column.DefaultCellStyle.BackColor = Color.FromArgb(244, 250, 255);
            }
            else if (JobStrategyCompanionColumns.Any(x => x.ColumnName == column.DataPropertyName))
            {
                column.DefaultCellStyle.BackColor = Color.FromArgb(250, 248, 239);
            }
        }

        RefreshJobStrategyRowStyles();
    }

    private void ConfigureJobStrategyComboColumn(string columnName, IReadOnlyDictionary<int, string> knownNames, string fallbackLabel)
    {
        if (_currentJobStrategyData == null || !_jobStrategyEditorGrid.Columns.Contains(columnName)) return;
        var existing = _jobStrategyEditorGrid.Columns[columnName];
        if (existing is DataGridViewComboBoxColumn) return;
        var index = existing.Index;
        var width = existing.Width;
        _jobStrategyEditorGrid.Columns.Remove(existing);

        var values = knownNames.Keys
            .Concat(_currentJobStrategyData.Rows.Cast<DataRow>().Select(row => Convert.ToInt32(row[columnName], CultureInfo.InvariantCulture)))
            .Distinct()
            .OrderBy(static value => value)
            .Select(value => new JobStrategyComboItem(value, knownNames.TryGetValue(value, out var name) ? name : $"{fallbackLabel}{value}"))
            .ToList();

        var combo = new DataGridViewComboBoxColumn
        {
            Name = columnName,
            DataPropertyName = columnName,
            HeaderText = BuildJobStrategyColumnHeader(columnName),
            ToolTipText = BuildJobStrategyColumnAnnotation(columnName),
            DataSource = values,
            ValueMember = nameof(JobStrategyComboItem.Value),
            DisplayMember = nameof(JobStrategyComboItem.Text),
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            Width = Math.Max(width, 120)
        };
        _jobStrategyEditorGrid.Columns.Insert(index, combo);
    }

    private string BuildJobStrategyColumnHeader(string columnName)
    {
        if (columnName == "ID") return "ID\n策略编号";
        if (columnName == "名称") return "策略名称\n11B";
        if (TryGetJobStrategyLearningSourceColumn(columnName, out var jobIdText) &&
            int.TryParse(jobIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var jobId))
        {
            return $"{jobId:D2}:{BuildJobStrategyLearningHeaderName(jobId)}\n学会等级";
        }

        return columnName switch
        {
            "可学摘要" => "可学摘要\n只读",
            "策略介绍" => "策略介绍\n200B",
            "AI策略（战场）" => "AI策略（战场）",
            "AI策略（练武）" => "AI策略（练武）",
            _ => columnName
        };
    }

    private string BuildJobStrategyColumnAnnotation(string columnName)
    {
        if (columnName == "ID") return "策略编号。策略主表、说明表和 EKD5 策略附表按这个编号逐行对齐。";
        if (columnName == "名称") return "策略名称，写入 6.5-5 策略 / Data.e5；固定 11 字节 GBK 容量。单击该列会在右侧编辑该策略对 80 个兵种的学习等级，切换到其他单元格时同步可学摘要。";
        if (columnName == "可学摘要") return "只读摘要：列出当前策略中学会等级不为 0 的详细兵种。0 表示该兵种不能学习。";
        if (columnName == "策略介绍") return "策略说明文本，写入 6.5-5-1 策略说明 / Imsg.e5；固定 200 字节 GBK 容量。列表中隐藏，当前在右侧策略预览页框中编辑。";
        if (columnName == "策略类型") return "策略实际效果类型，写入 6.5-5 策略 / Data.e5。下拉项按 6.5 样本策略名反推为中文候选，保存仍写入原始编号。注意它不是右侧“学会策略/效果索引”的七类复选。";
        if (columnName == "施展对象") return "策略可施展对象，写入 6.5-5 策略 / Data.e5。旧形象指定器可见候选包括敌方、我方/气合类、全屏气合类；修改后需实机验证目标选择。";
        if (columnName == "施法范围") return "策略施法目标选择范围，写入 6.5-5 策略 / Data.e5；选择该字段会按字段值+1 从 E5\\Hitarea.e5 预览范围图。";
        if (columnName == "穿透范围") return "策略效果范围/穿透模板，写入 6.5-5 策略 / Data.e5；选择该字段会按字段值+1 从 E5\\Effarea.e5 预览范围图。";
        if (columnName == "策略图标") return "策略图标编号，写入 6.5-5 策略 / Data.e5；选择该字段会按编号从 Mgcicon.dll 预览候选图标。";
        if (TryGetJobStrategyLearningSourceColumn(columnName, out var sourceColumnName) &&
            int.TryParse(sourceColumnName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var jobId))
        {
            return $"详细兵种 {jobId:D2}：{BuildJobStrategyJobName(jobId)} 学会当前策略的等级，写入 6.5-5 策略主表的兵种列；0 表示无法学习。";
        }

        var primaryField = _jobStrategyRead?.Table.Fields.FirstOrDefault(f => f.ColumnName == columnName);
        if (primaryField != null && _jobStrategyRead != null)
        {
            return _fieldAnnotationService.BuildFieldAnnotation(_jobStrategyRead.Table, primaryField);
        }

        var companion = JobStrategyCompanionColumns.FirstOrDefault(x => x.ColumnName == columnName);
        if (!string.IsNullOrWhiteSpace(companion.TableName))
        {
            return columnName switch
            {
                "大动画" => "策略大动画 / Mcall 编号，写入 6.5-5-2 策略动画1 / Ekd5.exe（MgMcall=668144）。值 >=100 时按 Mcall{值-100}.e5 定位；小于 100 或 255 表示无 Mcall 大动画。",
                "小动画" => "策略小动画 / Meff 编号，写入 6.5-5-3 策略动画2 / Ekd5.exe（MgMeff=668000）。值 N 按 Meff.e5 图号 N+1 定位；255 表示无动画或保留。",
                "是否伤血" => "策略伤害类型/是否伤血候选，写入 6.5-5-4 策略伤害类型 / Ekd5.exe；语义需结合实机验证。",
                "伤害系数" => "策略伤害比例/倍率参数，写入 6.5-5-5 策略伤害比例 / Ekd5.exe。",
                "命中上限" => "策略命中率参数，写入 6.5-5-6 策略命中率 / Ekd5.exe；旧界面标为“命中上限”。",
                "效果索引" => "策略学习/AI 分类位掩码，写入 6.5-5-7 学会策略 / Ekd5.exe；常见位：1 四系、2 降能力、4 妨碍、8 补给、0x10 升能力、0x20 气候、0x40 绝、0x80 四神。",
                "AI策略（战场）" => "战场 AI 策略限制候选，写入 6.5-5-8 战场AI策略限制 / Ekd5.exe。",
                "AI策略（练武）" => "练武场 AI 策略限制候选，写入 6.5-5-9 练武场AI策略限制 / Ekd5.exe。",
                _ => $"写入 {companion.TableName} / Ekd5.exe 的同 ID 单字节字段。"
            };
        }

        return columnName;
    }

    private void OpenJobStrategyLearningEditor(int rowIndex)
    {
        if (_currentJobStrategyData == null || rowIndex < 0 || rowIndex >= _jobStrategyEditorGrid.Rows.Count) return;
        _jobStrategyEditorGrid.EndEdit();
        var row = TryGetDataRow(_jobStrategyEditorGrid.Rows[rowIndex]);
        if (row == null) return;

        var strategyId = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
        if (_jobStrategyLearningDialogs.TryGetValue(strategyId, out var existing) && !existing.IsDisposed)
        {
            existing.Activate();
            existing.Focus();
            return;
        }

        var strategyName = Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        var levels = GetJobStrategyLearningLevels(row);
        var dialog = new JobStrategyLearningDialog(strategyId, strategyName, row, _jobStrategyJobNames, levels);
        dialog.FormClosed += (_, _) =>
        {
            _jobStrategyLearningDialogs.Remove(strategyId);
            if (dialog.DialogResult == DialogResult.OK)
            {
                ApplyJobStrategyLearningDialogChanges(row, dialog.LearningLevels);
            }
        };
        _jobStrategyLearningDialogs[strategyId] = dialog;
        dialog.Show(this);
    }

    private IReadOnlyDictionary<int, int> GetJobStrategyLearningLevels(DataRow row)
    {
        var levels = new Dictionary<int, int>();
        foreach (DataColumn column in row.Table.Columns)
        {
            if (!TryGetJobStrategyLearningSourceColumn(column.ColumnName, out var jobIdText)) continue;
            var jobId = int.Parse(jobIdText, CultureInfo.InvariantCulture);
            levels[jobId] = Convert.ToInt32(row[column.ColumnName], CultureInfo.InvariantCulture);
        }

        return levels;
    }

    private void ApplyJobStrategyLearningDialogChanges(DataRow row, IReadOnlyDictionary<int, int> levels)
    {
        ApplyJobStrategyLearningLevels(row, levels, refreshCurrentPreview: true);
    }

    private void ApplyJobStrategyLearningLevels(DataRow row, IReadOnlyDictionary<int, int> levels, bool refreshCurrentPreview)
    {
        if (_currentJobStrategyData == null || row.RowState == DataRowState.Detached) return;
        var changed = 0;
        foreach (var (jobId, level) in levels)
        {
            var columnName = BuildJobStrategyLearningColumnName(jobId.ToString(CultureInfo.InvariantCulture));
            if (!row.Table.Columns.Contains(columnName)) continue;
            if (Convert.ToInt32(row[columnName], CultureInfo.InvariantCulture) == level) continue;
            row[columnName] = level;
            changed++;
        }

        SetJobStrategyLearningSummary(row);
        var gridRowIndex = FindJobStrategyGridRowIndex(row);
        if (gridRowIndex >= 0)
        {
            RefreshJobStrategyRowStyle(gridRowIndex);
            _jobStrategyEditorGrid.InvalidateRow(gridRowIndex);
            if (refreshCurrentPreview && _jobStrategyEditorGrid.CurrentCell?.RowIndex == gridRowIndex)
            {
                ShowSelectedJobStrategyCell();
            }
        }

        SetStatus(changed > 0
            ? $"策略 {Convert.ToString(row["ID"], CultureInfo.InvariantCulture)} 学习等级已同步到可学摘要：{changed} 项。"
            : $"策略 {Convert.ToString(row["ID"], CultureInfo.InvariantCulture)} 学习等级没有产生改动。");
    }

    private void EnsureJobStrategyLearningEditorDataColumns()
    {
        if (_jobStrategyLearningEditorData.Columns.Contains("兵种ID") &&
            _jobStrategyLearningEditorData.Columns.Contains("兵种名称") &&
            _jobStrategyLearningEditorData.Columns.Contains("学会等级"))
        {
            return;
        }

        _jobStrategyLearningEditorData.Columns.Clear();
        _jobStrategyLearningEditorData.Columns.Add("兵种ID", typeof(int));
        _jobStrategyLearningEditorData.Columns.Add("兵种名称", typeof(string));
        _jobStrategyLearningEditorData.Columns.Add("学会等级", typeof(int));
    }

    private void ConfigureJobStrategyLearningEditorGridColumns()
    {
        if (_jobStrategyLearningEditorGrid.Columns["兵种ID"] is { } idColumn)
        {
            idColumn.FillWeight = 24;
            idColumn.ReadOnly = true;
            idColumn.DefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);
        }

        if (_jobStrategyLearningEditorGrid.Columns["兵种名称"] is { } nameColumn)
        {
            nameColumn.FillWeight = 56;
            nameColumn.ReadOnly = true;
            nameColumn.DefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);
        }

        if (_jobStrategyLearningEditorGrid.Columns["学会等级"] is { } levelColumn)
        {
            levelColumn.FillWeight = 30;
            levelColumn.ReadOnly = false;
            levelColumn.DefaultCellStyle.BackColor = Color.FromArgb(244, 250, 255);
        }
    }

    private void ShowJobStrategyLearningEditor(DataGridViewRow gridRow)
    {
        if (_currentJobStrategyData == null)
        {
            ClearJobStrategyPreview("请先读取兵种策略。");
            return;
        }

        var row = TryGetDataRow(gridRow);
        if (row == null)
        {
            ClearJobStrategyPreview("当前策略行无法解析。");
            return;
        }

        if (ReferenceEquals(_jobStrategyLearningEditorBoundRow, row) && _jobStrategyLearningEditorPanel.Visible)
        {
            SetJobStrategyPreviewLayout(JobStrategyPreviewLayoutMode.LearningEditorOnly);
            UpdateJobStrategyLearningEditorStatus();
            return;
        }

        EnsureJobStrategyLearningEditorDataColumns();
        _jobStrategyLearningEditorBoundRow = row;
        _bindingJobStrategyLearningEditor = true;
        try
        {
            _jobStrategyLearningEditorData.Columns["兵种ID"]!.ReadOnly = false;
            _jobStrategyLearningEditorData.Columns["兵种名称"]!.ReadOnly = false;
            _jobStrategyLearningEditorData.Columns["学会等级"]!.ReadOnly = false;
            _jobStrategyLearningEditorData.Clear();

            foreach (var (jobId, level) in GetJobStrategyLearningLevels(row).OrderBy(static pair => pair.Key))
            {
                var editorRow = _jobStrategyLearningEditorData.NewRow();
                editorRow["兵种ID"] = jobId;
                editorRow["兵种名称"] = _jobStrategyJobNames.TryGetValue(jobId, out var jobName) && !string.IsNullOrWhiteSpace(jobName)
                    ? jobName
                    : "未找到兵种名";
                editorRow["学会等级"] = level;
                _jobStrategyLearningEditorData.Rows.Add(editorRow);
            }

            _jobStrategyLearningEditorData.Columns["兵种ID"]!.ReadOnly = true;
            _jobStrategyLearningEditorData.Columns["兵种名称"]!.ReadOnly = true;
            _jobStrategyLearningEditorData.AcceptChanges();
            if (!ReferenceEquals(_jobStrategyLearningEditorGrid.DataSource, _jobStrategyLearningEditorData))
            {
                _jobStrategyLearningEditorGrid.DataSource = _jobStrategyLearningEditorData;
            }

            ConfigureJobStrategyLearningEditorGridColumns();
        }
        finally
        {
            _bindingJobStrategyLearningEditor = false;
        }

        var id = Convert.ToString(row["ID"], CultureInfo.InvariantCulture) ?? string.Empty;
        var name = Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        _jobStrategyLearningEditorTitleLabel.Text = $"策略学习等级：ID={id} {name}";
        ClearJobStrategyAnimationPreview();
        SetPictureBoxImage(_jobStrategyPreviewBox, null);
        SetJobStrategyPreviewLayout(JobStrategyPreviewLayoutMode.LearningEditorOnly);
        UpdateJobStrategyLearningEditorStatus();
    }

    private void HideJobStrategyLearningEditor()
    {
        _jobStrategyLearningEditorPanel.Visible = false;
    }

    private int CountPendingJobStrategyLearningEditorChanges()
    {
        if (_jobStrategyLearningEditorBoundRow == null || _jobStrategyLearningEditorBoundRow.RowState == DataRowState.Detached) return 0;

        var count = 0;
        foreach (DataRow editorRow in _jobStrategyLearningEditorData.Rows)
        {
            if (!TryConvertToInt(editorRow["兵种ID"], out var jobId) ||
                !TryConvertToInt(editorRow["学会等级"], out var newLevel))
            {
                continue;
            }

            var columnName = BuildJobStrategyLearningColumnName(jobId.ToString(CultureInfo.InvariantCulture));
            if (!_jobStrategyLearningEditorBoundRow.Table.Columns.Contains(columnName)) continue;
            var oldLevel = Convert.ToInt32(_jobStrategyLearningEditorBoundRow[columnName], CultureInfo.InvariantCulture);
            if (oldLevel != newLevel) count++;
        }

        return count;
    }

    private void UpdateJobStrategyLearningEditorStatus()
    {
        if (_bindingJobStrategyLearningEditor) return;

        var total = _jobStrategyLearningEditorData.Rows.Count;
        var learned = _jobStrategyLearningEditorData.Rows
            .Cast<DataRow>()
            .Count(row => TryConvertToInt(row["学会等级"], out var level) && level > 0);
        var pending = CountPendingJobStrategyLearningEditorChanges();
        _jobStrategyLearningEditorStatusLabel.Text =
            $"可学习：{learned}/{total}。切换到其他单元格或保存兵种策略时同步到主表；待应用改动：{pending} 项。";
        if (pending > 0) SetStatus($"策略学习等级待应用改动：{pending} 项。切换到其他单元格或保存时同步。");
    }

    private bool CommitJobStrategyLearningEditorChanges(bool showMessage = false, bool restoreSelectionOnFailure = false)
    {
        if (_jobStrategyLearningEditorBoundRow == null || _jobStrategyLearningEditorBoundRow.RowState == DataRowState.Detached)
        {
            _jobStrategyLearningEditorBoundRow = null;
            return true;
        }

        if (_bindingJobStrategyLearningEditor) return true;
        if (!FinishJobStrategyLearningEditorEdit(showMessage))
        {
            if (restoreSelectionOnFailure) RestoreJobStrategyLearningEditorSelection();
            return false;
        }

        var pending = CountPendingJobStrategyLearningEditorChanges();
        if (pending == 0)
        {
            UpdateJobStrategyLearningEditorStatus();
            return true;
        }

        var row = _jobStrategyLearningEditorBoundRow;
        var levels = _jobStrategyLearningEditorData.Rows
            .Cast<DataRow>()
            .Where(editorRow => TryConvertToInt(editorRow["兵种ID"], out _) && TryConvertToInt(editorRow["学会等级"], out _))
            .ToDictionaryFirstByKey(
                editorRow => Convert.ToInt32(editorRow["兵种ID"], CultureInfo.InvariantCulture),
                editorRow => Convert.ToInt32(editorRow["学会等级"], CultureInfo.InvariantCulture));
        ApplyJobStrategyLearningLevels(row, levels, refreshCurrentPreview: false);
        _jobStrategyLearningEditorData.AcceptChanges();
        if (FindJobStrategyGridRowIndex(row) is var gridRowIndex && gridRowIndex >= 0)
        {
            UpdateJobStrategyDerivedCells(gridRowIndex, _jobStrategyEditorGrid.Columns["可学摘要"].Index);
            _jobStrategyEditorGrid.InvalidateRow(gridRowIndex);
        }

        UpdateJobStrategyLearningEditorStatus();
        return true;
    }

    private bool FinishJobStrategyLearningEditorEdit(bool showMessage)
    {
        if (_jobStrategyLearningEditorGrid.IsCurrentCellInEditMode && !_jobStrategyLearningEditorGrid.EndEdit())
        {
            var error = _jobStrategyLearningEditorGrid.CurrentCell?.ErrorText;
            if (string.IsNullOrWhiteSpace(error)) error = "学会等级必须是 0..255 的整数。";
            _jobStrategyLearningEditorStatusLabel.Text = error;
            SetStatus(error);
            if (showMessage) MessageBox.Show(this, error, "学习等级无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (ReferenceEquals(_jobStrategyLearningEditorGrid.DataSource, _jobStrategyLearningEditorData) &&
            BindingContext?[_jobStrategyLearningEditorData] is CurrencyManager manager)
        {
            manager.EndCurrentEdit();
        }

        return ValidateJobStrategyLearningEditorRows(showMessage);
    }

    private bool ValidateJobStrategyLearningEditorRows(bool showMessage)
    {
        foreach (DataGridViewRow row in _jobStrategyLearningEditorGrid.Rows)
        {
            if (row.IsNewRow) continue;
            if (row.Cells["学会等级"] is not { } cell) continue;
            var value = Convert.ToString(cell.Value, CultureInfo.InvariantCulture) ?? string.Empty;
            var error = ValidateJobStrategyLearningLevelValue(value);
            cell.ErrorText = error ?? string.Empty;
            if (error == null) continue;

            SetJobStrategyPreviewLayout(JobStrategyPreviewLayoutMode.LearningEditorOnly);
            _jobStrategyLearningEditorGrid.CurrentCell = cell;
            _jobStrategyLearningEditorStatusLabel.Text = error;
            SetStatus(error);
            if (showMessage) MessageBox.Show(this, error, "学习等级无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        return true;
    }

    private void ValidateJobStrategyLearningEditorCell(DataGridViewCellValidatingEventArgs e)
    {
        if (_bindingJobStrategyLearningEditor || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var column = _jobStrategyLearningEditorGrid.Columns[e.ColumnIndex];
        if (column.DataPropertyName != "学会等级") return;

        var value = Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture) ?? string.Empty;
        var error = ValidateJobStrategyLearningLevelValue(value);
        _jobStrategyLearningEditorGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = error ?? string.Empty;
        if (error == null) return;

        e.Cancel = true;
        _jobStrategyLearningEditorStatusLabel.Text = error;
        SetStatus(error);
    }

    private static string? ValidateJobStrategyLearningLevelValue(string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
        {
            return "学会等级必须是 0..255 的整数。";
        }

        return level is < 0 or > byte.MaxValue ? "学会等级必须在 0..255 之间。" : null;
    }

    private void HandleJobStrategySelectionChanged()
    {
        if (_restoringJobStrategyDescriptionSelection) return;
        if (!CommitJobStrategyDescriptionBoxEdit(showMessage: true, restoreSelectionOnFailure: true)) return;
        if (!IsCurrentJobStrategyNameCellBoundToLearningEditor() &&
            !CommitJobStrategyLearningEditorChanges(showMessage: true, restoreSelectionOnFailure: true))
        {
            return;
        }

        ShowSelectedJobStrategyCell();
    }

    private bool IsCurrentJobStrategyNameCellBoundToLearningEditor()
    {
        if (_jobStrategyLearningEditorBoundRow == null ||
            _jobStrategyLearningEditorBoundRow.RowState == DataRowState.Detached ||
            _jobStrategyEditorGrid.CurrentCell == null)
        {
            return false;
        }

        var columnName = _jobStrategyEditorGrid.Columns[_jobStrategyEditorGrid.CurrentCell.ColumnIndex].DataPropertyName;
        if (!string.Equals(columnName, "名称", StringComparison.Ordinal)) return false;
        var row = TryGetDataRow(_jobStrategyEditorGrid.Rows[_jobStrategyEditorGrid.CurrentCell.RowIndex]);
        return ReferenceEquals(row, _jobStrategyLearningEditorBoundRow);
    }

    private void RestoreJobStrategyLearningEditorSelection()
    {
        if (_jobStrategyLearningEditorBoundRow == null || _jobStrategyLearningEditorBoundRow.RowState == DataRowState.Detached) return;
        var rowIndex = FindJobStrategyGridRowIndex(_jobStrategyLearningEditorBoundRow);
        if (rowIndex < 0 || _jobStrategyEditorGrid.Columns["名称"] is not { } nameColumn) return;
        _jobStrategyEditorGrid.CurrentCell = _jobStrategyEditorGrid.Rows[rowIndex].Cells[nameColumn.Index];
    }

    private int FindJobStrategyGridRowIndex(DataRow row)
    {
        foreach (DataGridViewRow gridRow in _jobStrategyEditorGrid.Rows)
        {
            if (ReferenceEquals(TryGetDataRow(gridRow), row)) return gridRow.Index;
        }

        return -1;
    }

    private void SetJobStrategyLearningSummary(DataRow row)
    {
        var column = row.Table.Columns["可学摘要"];
        if (column == null) return;
        var wasReadOnly = column.ReadOnly;
        column.ReadOnly = false;
        var summary = BuildJobStrategyLearningSummary(row);
        if (!string.Equals(Convert.ToString(row["可学摘要"], CultureInfo.InvariantCulture), summary, StringComparison.Ordinal))
        {
            row["可学摘要"] = summary;
        }

        column.ReadOnly = wasReadOnly;
    }

    private bool CommitJobStrategyLearningDialogs()
    {
        foreach (var dialog in _jobStrategyLearningDialogs.Values.ToList())
        {
            if (dialog.IsDisposed) continue;
            if (!dialog.CommitPendingChanges()) return false;
            if (dialog.BoundStrategyRow is { RowState: not DataRowState.Detached } row)
            {
                ApplyJobStrategyLearningDialogChanges(row, dialog.LearningLevels);
            }
        }

        return true;
    }

    private void CloseJobStrategyLearningDialogs()
    {
        foreach (var dialog in _jobStrategyLearningDialogs.Values.ToList())
        {
            dialog.ApplyChangesOnClose = false;
            dialog.Close();
            dialog.Dispose();
        }

        _jobStrategyLearningDialogs.Clear();
    }

    private string BuildJobStrategyJobName(int jobId)
    {
        return _jobStrategyJobNames.TryGetValue(jobId, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : $"{jobId}：未找到兵种名";
    }

    private string BuildJobStrategyLearningHeaderName(int jobId)
    {
        return _jobStrategyJobNames.TryGetValue(jobId, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : "未找到兵种名";
    }

    private string BuildJobStrategyLearningSummary(DataRow row)
    {
        var parts = new List<string>();
        foreach (DataColumn column in row.Table.Columns)
        {
            if (!TryGetJobStrategyLearningSourceColumn(column.ColumnName, out var jobIdText)) continue;
            var level = Convert.ToInt32(row[column.ColumnName], CultureInfo.InvariantCulture);
            if (level <= 0) continue;
            var jobId = int.Parse(jobIdText, CultureInfo.InvariantCulture);
            parts.Add($"{BuildJobStrategyJobName(jobId)} {level}级");
        }

        return parts.Count == 0 ? "无可学兵种" : string.Join("，", parts);
    }

    private void UpdateJobStrategyDerivedCells(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 0) return;
        if (!TryGetJobStrategyLearningSourceColumn(_jobStrategyEditorGrid.Columns[columnIndex].DataPropertyName, out _)) return;
        var row = TryGetDataRow(_jobStrategyEditorGrid.Rows[rowIndex]);
        if (row == null) return;
        SetJobStrategyLearningSummary(row);
    }

    private static string BuildJobStrategySummary(DataTable data)
    {
        var named = data.Rows.Cast<DataRow>().Count(row => !string.IsNullOrWhiteSpace(Convert.ToString(row["名称"], CultureInfo.InvariantCulture)));
        var learningColumns = data.Columns.Cast<DataColumn>().Count(column => TryGetJobStrategyLearningSourceColumn(column.ColumnName, out _));
        return
            $"兵种策略已读取：策略 {data.Rows.Count} 行，已命名 {named}，兵种学会等级列 {learningColumns} 个。\r\n" +
            "来源表：6.5-5 策略（基础属性 + 80 个兵种学会等级）、6.5-5-1 策略说明、6.5-5-2 至 6.5-5-9 策略附表。\r\n" +
            "保存会写回 Data.e5、Imsg.e5 与 Ekd5.exe，保存前自动备份，保存后重新读取校验。";
    }

    private void ApplyJobStrategyFilter()
    {
        if (_currentJobStrategyData == null) return;
        if (!CommitJobStrategyLearningEditorChanges(showMessage: true, restoreSelectionOnFailure: true)) return;
        var keyword = _jobStrategyEditorSearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            _currentJobStrategyData.DefaultView.RowFilter = string.Empty;
            SetStatus("兵种策略筛选已清除");
            return;
        }

        var escaped = EscapeDataViewLikeValue(keyword);
        var filters = _currentJobStrategyData.Columns
            .Cast<DataColumn>()
            .Select(column => $"CONVERT([{column.ColumnName}], 'System.String') LIKE '*{escaped}*'");
        _currentJobStrategyData.DefaultView.RowFilter = string.Join(" OR ", filters);
        SetStatus($"兵种策略筛选：{_currentJobStrategyData.DefaultView.Count}/{_currentJobStrategyData.Rows.Count}");
    }

    private void ClearJobStrategyFilter()
    {
        if (!CommitJobStrategyLearningEditorChanges(showMessage: true, restoreSelectionOnFailure: true)) return;
        _jobStrategyEditorSearchBox.Clear();
        if (_currentJobStrategyData != null) _currentJobStrategyData.DefaultView.RowFilter = string.Empty;
        SetStatus("兵种策略筛选已清除");
    }

    private void RefreshJobStrategyRowStyles()
    {
        foreach (DataGridViewRow row in _jobStrategyEditorGrid.Rows)
        {
            RefreshJobStrategyRowStyle(row.Index);
        }
    }

    private void RefreshJobStrategyRowStyle(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _jobStrategyEditorGrid.Rows.Count) return;
        var dataRow = TryGetDataRow(_jobStrategyEditorGrid.Rows[rowIndex]);
        if (dataRow == null) return;
        _jobStrategyEditorGrid.Rows[rowIndex].DefaultCellStyle.BackColor = IsDataRowChanged(dataRow) ? Color.LightCyan : Color.Empty;
    }

    private void RefreshJobStrategyCellsAfterEdit(IReadOnlyList<GridCellKey> changedCells)
    {
        RefreshChangedGridCells(_jobStrategyEditorGrid, changedCells, UpdateJobStrategyDerivedCells);
        RefreshChangedGridRowsOnly(_jobStrategyEditorGrid, changedCells, RefreshJobStrategyRowStyle);
        ShowSelectedJobStrategyCell();
    }

    private bool BindJobStrategyDescriptionBoxes(DataRow row)
    {
        if (!row.Table.Columns.Contains("策略介绍"))
        {
            ClearJobStrategyDescriptionBoxes("读取兵种策略后，在此编辑当前策略介绍。");
            return true;
        }

        if (!ReferenceEquals(row, _jobStrategyDescriptionEditorBoundRow))
        {
            if (!CommitJobStrategyDescriptionBoxEdit(showMessage: true, restoreSelectionOnFailure: true)) return false;
            _jobStrategyDescriptionEditorBoundRow = row;
        }

        if (_jobStrategyDescriptionBoxHasValidationError) return true;

        var text = GetJobStrategyDescription(row);
        _bindingJobStrategyDescriptionBox = true;
        try
        {
            _jobStrategyDescriptionBoxHasValidationError = false;
            _jobStrategyDescriptionBoxValidationError = string.Empty;
            SetJobStrategyDescriptionBoxErrorState(false);
            if (!string.Equals(_jobStrategyDescriptionBox.Text, text, StringComparison.Ordinal))
            {
                _jobStrategyDescriptionBox.Text = text;
            }

            if (!string.Equals(_jobStrategyLearningDescriptionBox.Text, text, StringComparison.Ordinal))
            {
                _jobStrategyLearningDescriptionBox.Text = text;
            }
        }
        finally
        {
            _bindingJobStrategyDescriptionBox = false;
        }

        return true;
    }

    private void ClearJobStrategyDescriptionBoxes(string text)
    {
        _bindingJobStrategyDescriptionBox = true;
        try
        {
            _jobStrategyDescriptionEditorBoundRow = null;
            _jobStrategyDescriptionBoxHasValidationError = false;
            _jobStrategyDescriptionBoxValidationError = string.Empty;
            SetJobStrategyDescriptionBoxErrorState(false);
            _jobStrategyDescriptionBox.Text = text;
            _jobStrategyLearningDescriptionBox.Text = text;
        }
        finally
        {
            _bindingJobStrategyDescriptionBox = false;
        }
    }

    private void ApplyJobStrategyDescriptionBoxEdit(TextBox sourceBox)
    {
        if (_bindingJobStrategyDescriptionBox) return;
        if (_currentJobStrategyData == null ||
            _jobStrategyDescriptionEditorBoundRow == null ||
            _jobStrategyDescriptionEditorBoundRow.RowState == DataRowState.Detached ||
            !_jobStrategyDescriptionEditorBoundRow.Table.Columns.Contains("策略介绍"))
        {
            return;
        }

        var text = sourceBox.Text;
        var bytes = EncodingService.GetGbkByteCount(text);
        if (bytes > 200)
        {
            _jobStrategyDescriptionBoxHasValidationError = true;
            _jobStrategyDescriptionBoxValidationError = $"策略介绍超长：GBK {bytes} 字节，容量 200 字节。";
            SetJobStrategyDescriptionBoxErrorState(true);
            SetStatus(_jobStrategyDescriptionBoxValidationError);
            return;
        }

        _jobStrategyDescriptionBoxHasValidationError = false;
        _jobStrategyDescriptionBoxValidationError = string.Empty;
        SetJobStrategyDescriptionBoxErrorState(false);

        var oldValue = GetJobStrategyDescription(_jobStrategyDescriptionEditorBoundRow);
        if (!string.Equals(oldValue, text, StringComparison.Ordinal))
        {
            _jobStrategyDescriptionEditorBoundRow["策略介绍"] = text;
            var rowIndex = FindJobStrategyGridRowIndex(_jobStrategyDescriptionEditorBoundRow);
            if (rowIndex >= 0)
            {
                RefreshJobStrategyRowStyle(rowIndex);
                _jobStrategyEditorGrid.InvalidateRow(rowIndex);
            }
        }

        _bindingJobStrategyDescriptionBox = true;
        try
        {
            var targetBox = ReferenceEquals(sourceBox, _jobStrategyDescriptionBox)
                ? _jobStrategyLearningDescriptionBox
                : _jobStrategyDescriptionBox;
            if (!string.Equals(targetBox.Text, text, StringComparison.Ordinal))
            {
                targetBox.Text = text;
            }
        }
        finally
        {
            _bindingJobStrategyDescriptionBox = false;
        }

        SetStatus($"策略介绍 GBK：{bytes}/200");
    }

    private bool CommitJobStrategyDescriptionBoxEdit(bool showMessage = false, bool restoreSelectionOnFailure = false)
    {
        if (_bindingJobStrategyDescriptionBox) return true;
        if (_jobStrategyDescriptionBoxHasValidationError)
        {
            SetStatus(_jobStrategyDescriptionBoxValidationError);
            if (showMessage)
            {
                MessageBox.Show(this, _jobStrategyDescriptionBoxValidationError, "策略介绍无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            if (restoreSelectionOnFailure) RestoreJobStrategyDescriptionSelection();
            return false;
        }

        if (_jobStrategyDescriptionEditorBoundRow == null ||
            _jobStrategyDescriptionEditorBoundRow.RowState == DataRowState.Detached ||
            !_jobStrategyDescriptionEditorBoundRow.Table.Columns.Contains("策略介绍"))
        {
            _jobStrategyDescriptionEditorBoundRow = null;
            return true;
        }

        ApplyJobStrategyDescriptionBoxEdit(_jobStrategyDescriptionBox);
        if (_jobStrategyDescriptionBoxHasValidationError)
        {
            if (showMessage)
            {
                MessageBox.Show(this, _jobStrategyDescriptionBoxValidationError, "策略介绍无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            if (restoreSelectionOnFailure) RestoreJobStrategyDescriptionSelection();
            return false;
        }

        return true;
    }

    private void RestoreJobStrategyDescriptionSelection()
    {
        if (_jobStrategyDescriptionEditorBoundRow == null || _jobStrategyDescriptionEditorBoundRow.RowState == DataRowState.Detached) return;
        var rowIndex = FindJobStrategyGridRowIndex(_jobStrategyDescriptionEditorBoundRow);
        if (rowIndex < 0) return;
        _restoringJobStrategyDescriptionSelection = true;
        try
        {
            var column = _jobStrategyEditorGrid.CurrentCell?.ColumnIndex >= 0
                ? _jobStrategyEditorGrid.Columns[_jobStrategyEditorGrid.CurrentCell.ColumnIndex]
                : _jobStrategyEditorGrid.Columns["名称"];
            _jobStrategyEditorGrid.CurrentCell = _jobStrategyEditorGrid.Rows[rowIndex].Cells[column?.Index ?? 0];
        }
        finally
        {
            _restoringJobStrategyDescriptionSelection = false;
        }
    }

    private void SetJobStrategyDescriptionBoxErrorState(bool hasError)
    {
        var color = hasError ? Color.MistyRose : SystemColors.Window;
        _jobStrategyDescriptionBox.BackColor = color;
        _jobStrategyLearningDescriptionBox.BackColor = color;
    }

    private void ShowSelectedJobStrategyCell()
    {
        if (_currentJobStrategyData == null || _jobStrategyEditorGrid.CurrentCell == null) return;
        var cell = _jobStrategyEditorGrid.CurrentCell;
        var columnName = _jobStrategyEditorGrid.Columns[cell.ColumnIndex].DataPropertyName;
        var row = _jobStrategyEditorGrid.Rows[cell.RowIndex];
        var dataRow = TryGetDataRow(row);
        if (dataRow == null || !BindJobStrategyDescriptionBoxes(dataRow)) return;
        var id = row.Cells["ID"].Value;
        var name = row.Cells["名称"].Value;
        var value = cell.Value;
        var formattedValue = FormatJobStrategyCellValue(columnName, value);
        var extra = columnName == "名称"
            ? $"\r\nGBK 字节：{EncodingService.GetGbkByteCount(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)}/11"
            : string.Empty;
        var summary = Convert.ToString(row.Cells["可学摘要"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        var descriptionBytes = EncodingService.GetGbkByteCount(GetJobStrategyDescription(dataRow));
        var magicInfo = _jobStrategyConfiguredMagicCount > 0
            ? $"\r\n形象指定器 SMagic={_jobStrategyConfiguredMagicCount}；来源：{_jobStrategyConfiguredMagicSource}"
            : string.Empty;
        _jobStrategyEditorInfoBox.Text =
            $"策略：ID={id}    名称={name}\r\n" +
            $"字段：{columnName}    当前值：{formattedValue}{extra}\r\n" +
            $"可学摘要：{summary}{magicInfo}\r\n" +
            $"策略介绍 GBK：{descriptionBytes}/200\r\n\r\n" +
            BuildJobStrategyColumnAnnotation(columnName);
        UpdateJobStrategyPreview(row, columnName);
    }

    private static string FormatJobStrategyCellValue(string columnName, object? value)
    {
        if (!TryConvertToInt(value, out var number)) return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return columnName switch
        {
            "策略类型" => JobStrategyTypeNames.TryGetValue(number, out var typeName) ? typeName : $"未命名策略类型{number}",
            "施展对象" => JobStrategyTargetNames.TryGetValue(number, out var targetName) ? targetName : $"施展对象{number}",
            "效果索引" => $"{number}（{BuildJobStrategyEffectMaskText(number)}）",
            _ => number.ToString(CultureInfo.InvariantCulture)
        };
    }

    private void UpdateJobStrategyPreview(DataGridViewRow row, string columnName)
    {
        if (_project == null)
        {
            ClearJobStrategyPreview("请先打开 MOD 项目。");
            return;
        }

        var id = Convert.ToString(row.Cells["ID"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        var name = Convert.ToString(row.Cells["名称"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        if (columnName == "名称")
        {
            ShowJobStrategyLearningEditor(row);
            return;
        }

        if (!IsJobStrategyResourcePreviewColumn(columnName))
        {
            HideJobStrategyLearningEditor();
            ShowJobStrategyLearningPreview(row, columnName, id, name);
            return;
        }

        var rawValue = row.Cells[columnName].Value;
        if (!TryConvertToInt(rawValue, out var fieldValue))
        {
            ClearJobStrategyAnimationPreview();
            SetJobStrategyPreviewLayout(JobStrategyPreviewLayoutMode.ImageOnly);
            SetPictureBoxImage(_jobStrategyPreviewBox, null);
            _jobStrategyPreviewInfoBox.Text = $"{columnName} 字段不是整数：{Convert.ToString(rawValue, CultureInfo.InvariantCulture)}";
            return;
        }

        switch (columnName)
        {
            case "施法范围":
            {
                HideJobStrategyLearningEditor();
                ClearJobStrategyAnimationPreview();
                var result = _attackAreaPreviewService.BuildPreview(_project, "攻击范围", fieldValue);
                SetJobStrategyPreviewLayout(JobStrategyPreviewLayoutMode.ImageOnly);
                SetPictureBoxImage(_jobStrategyPreviewBox, result.Bitmap);
                _jobStrategyPreviewInfoBox.Text =
                    $"策略 ID={id}  名称={name}\r\n" +
                    $"字段=施法范围  值={fieldValue}\r\n" +
                    result.Message + "\r\n" +
                    $"资源路径：{result.SourcePath}";
                return;
            }
            case "穿透范围":
            {
                HideJobStrategyLearningEditor();
                ClearJobStrategyAnimationPreview();
                var result = _attackAreaPreviewService.BuildPreview(_project, "穿透范围", fieldValue);
                SetJobStrategyPreviewLayout(JobStrategyPreviewLayoutMode.ImageOnly);
                SetPictureBoxImage(_jobStrategyPreviewBox, result.Bitmap);
                _jobStrategyPreviewInfoBox.Text =
                    $"策略 ID={id}  名称={name}\r\n" +
                    $"字段=穿透范围  值={fieldValue}\r\n" +
                    result.Message + "\r\n" +
                    $"资源路径：{result.SourcePath}";
                return;
            }
            case "策略图标":
            {
                HideJobStrategyLearningEditor();
                ClearJobStrategyAnimationPreview();
                var result = _itemIconPreviewService.BuildPreview(_project, fieldValue, Ccz66RevisedLayout.ResolveStrategyIconResourceFile(_project), "策略图标");
                SetJobStrategyPreviewLayout(JobStrategyPreviewLayoutMode.ImageOnly);
                SetPictureBoxImage(_jobStrategyPreviewBox, result.Bitmap);
                _jobStrategyPreviewInfoBox.Text =
                    $"策略 ID={id}  名称={name}\r\n" +
                    $"策略图标字段={fieldValue}\r\n" +
                    result.Message + "\r\n" +
                    $"资源路径：{result.SourcePath}";
                return;
            }
            case "小动画":
            case "大动画":
            {
                HideJobStrategyLearningEditor();
                var previewKind = columnName == "大动画"
                    ? StrategyAnimationPreviewKind.BigMcall
                    : StrategyAnimationPreviewKind.SmallMeff;
                var result = _strategyAnimationPreviewService.BuildAnimatedPreview(_project, previewKind, fieldValue);
                SetJobStrategyPreviewLayout(JobStrategyPreviewLayoutMode.ImageOnly);
                SetJobStrategyAnimatedPreview(result);
                var resourceCountLabel = previewKind == StrategyAnimationPreviewKind.BigMcall
                    ? "Mcall条目数"
                    : "Meff条目数";
                _jobStrategyPreviewInfoBox.Text =
                    $"策略 ID={id}  名称={name}\r\n" +
                    $"字段={columnName}  值={fieldValue}\r\n" +
                    $"资源编号=#{result.ImageNumber}  {resourceCountLabel}={result.EntryCount}  帧数={result.Frames.Count}  间隔={result.FrameIntervalMs}ms  模式={result.RenderMode}\r\n" +
                    result.Message + "\r\n" +
                    $"资源路径：{result.SourcePath}";
                return;
            }
        }
    }

    private void ClearJobStrategyPreview(string message)
    {
        HideJobStrategyLearningEditor();
        ClearJobStrategyAnimationPreview();
        SetJobStrategyPreviewLayout(JobStrategyPreviewLayoutMode.LearningAndDescription);
        SetPictureBoxImage(_jobStrategyPreviewBox, null);
        _jobStrategyPreviewInfoBox.Text = message;
    }

    private void SetJobStrategyPreviewLayout(JobStrategyPreviewLayoutMode mode)
    {
        _jobStrategyLearningEditorPanel.Visible = mode == JobStrategyPreviewLayoutMode.LearningEditorOnly;
        _jobStrategyPreviewBox.Visible = mode == JobStrategyPreviewLayoutMode.ImageOnly;
        _jobStrategyPreviewInfoBox.Visible = mode == JobStrategyPreviewLayoutMode.LearningAndDescription;
        _jobStrategyDescriptionBox.Visible = mode == JobStrategyPreviewLayoutMode.LearningAndDescription;
        _jobStrategyLearningDescriptionBox.Visible = false;

        if (mode == JobStrategyPreviewLayoutMode.LearningEditorOnly)
        {
            _jobStrategyLearningEditorPanel.BringToFront();
        }

        if (_jobStrategyPreviewBox.Parent is not TableLayoutPanel panel) return;
        if (panel.RowStyles.Count >= 3)
        {
            switch (mode)
            {
                case JobStrategyPreviewLayoutMode.ImageOnly:
                    panel.RowStyles[0].SizeType = SizeType.Percent;
                    panel.RowStyles[0].Height = 100;
                    panel.RowStyles[1].SizeType = SizeType.Absolute;
                    panel.RowStyles[1].Height = 0;
                    panel.RowStyles[2].SizeType = SizeType.Absolute;
                    panel.RowStyles[2].Height = 0;
                    break;
                case JobStrategyPreviewLayoutMode.LearningAndDescription:
                case JobStrategyPreviewLayoutMode.LearningEditorOnly:
                    panel.RowStyles[0].SizeType = SizeType.Absolute;
                    panel.RowStyles[0].Height = 0;
                    panel.RowStyles[1].SizeType = SizeType.Percent;
                    panel.RowStyles[1].Height = 70;
                    panel.RowStyles[2].SizeType = SizeType.Percent;
                    panel.RowStyles[2].Height = 30;
                    break;
            }

            return;
        }

        if (panel.RowStyles.Count < 2) return;
        panel.RowStyles[0].SizeType = mode == JobStrategyPreviewLayoutMode.ImageOnly ? SizeType.Percent : SizeType.Absolute;
        panel.RowStyles[0].Height = mode == JobStrategyPreviewLayoutMode.ImageOnly ? 100 : 0;
        panel.RowStyles[1].SizeType = mode == JobStrategyPreviewLayoutMode.ImageOnly ? SizeType.Absolute : SizeType.Percent;
        panel.RowStyles[1].Height = mode == JobStrategyPreviewLayoutMode.ImageOnly ? 0 : 100;
    }

    private static bool IsJobStrategyResourcePreviewColumn(string columnName)
        => columnName is "施法范围" or "穿透范围" or "策略图标" or "小动画" or "大动画";

    private void ShowJobStrategyLearningPreview(DataGridViewRow row, string columnName, string id, string name)
    {
        HideJobStrategyLearningEditor();
        ClearJobStrategyAnimationPreview();
        SetJobStrategyPreviewLayout(JobStrategyPreviewLayoutMode.LearningAndDescription);
        SetPictureBoxImage(_jobStrategyPreviewBox, null);
        var dataRow = TryGetDataRow(row);
        var summary = dataRow == null
            ? Convert.ToString(row.Cells["可学摘要"].Value, CultureInfo.InvariantCulture) ?? "无可学兵种"
            : BuildJobStrategyLearningSummary(dataRow);
        var entries = SplitJobStrategyLearningSummary(summary);
        var body = entries.Count == 0
            ? "无可学兵种"
            : string.Join("\r\n", entries.Select(static entry => $"· {entry}"));
        _jobStrategyPreviewInfoBox.Text =
            $"策略 ID={id}  名称={name}\r\n" +
            "兵种学习情况：\r\n" +
            body;
    }

    private static string GetJobStrategyDescription(DataRow? row)
    {
        if (row == null || !row.Table.Columns.Contains("策略介绍")) return string.Empty;
        return Convert.ToString(row["策略介绍"], CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private void SetJobStrategyAnimatedPreview(StrategyAnimationAnimatedPreviewResult result)
    {
        ClearJobStrategyAnimationPreview();
        if (result.Frames.Count == 0)
        {
            SetPictureBoxImage(_jobStrategyPreviewBox, null);
            return;
        }

        _jobStrategyAnimationFrames = result.Frames;
        _jobStrategyAnimationFrameIndex = 0;
        SetPictureBoxImage(_jobStrategyPreviewBox, new Bitmap(_jobStrategyAnimationFrames[0]));
        if (result.Frames.Count <= 1)
        {
            return;
        }

        _jobStrategyAnimationTimer.Interval = Math.Clamp(result.FrameIntervalMs, 40, 2000);
        _jobStrategyAnimationTimer.Start();
    }

    private void AdvanceJobStrategyAnimationPreview()
    {
        if (_jobStrategyAnimationFrames.Count <= 1)
        {
            _jobStrategyAnimationTimer.Stop();
            return;
        }

        _jobStrategyAnimationFrameIndex = (_jobStrategyAnimationFrameIndex + 1) % _jobStrategyAnimationFrames.Count;
        SetPictureBoxImage(_jobStrategyPreviewBox, new Bitmap(_jobStrategyAnimationFrames[_jobStrategyAnimationFrameIndex]));
    }

    private void ClearJobStrategyAnimationPreview()
    {
        _jobStrategyAnimationTimer.Stop();
        _jobStrategyAnimationFrameIndex = 0;
        foreach (var frame in _jobStrategyAnimationFrames)
        {
            frame.Dispose();
        }

        _jobStrategyAnimationFrames = Array.Empty<Bitmap>();
    }

    private static IReadOnlyList<string> SplitJobStrategyLearningSummary(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary) || summary == "无可学兵种")
        {
            return Array.Empty<string>();
        }

        return summary
            .Split('，', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static bool TryConvertToInt(object? value, out int number)
    {
        switch (value)
        {
            case null:
                number = 0;
                return false;
            case int intValue:
                number = intValue;
                return true;
            case byte byteValue:
                number = byteValue;
                return true;
            case short shortValue:
                number = shortValue;
                return true;
            default:
                return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
        }
    }

    private static string BuildJobStrategyEffectMaskText(int value)
    {
        var parts = new List<string>();
        if ((value & 0x01) != 0) parts.Add("四系");
        if ((value & 0x02) != 0) parts.Add("降能力");
        if ((value & 0x04) != 0) parts.Add("妨碍");
        if ((value & 0x08) != 0) parts.Add("补给");
        if ((value & 0x10) != 0) parts.Add("升能力");
        if ((value & 0x20) != 0) parts.Add("气候");
        if ((value & 0x40) != 0) parts.Add("绝");
        if ((value & 0x80) != 0) parts.Add("四神");
        return parts.Count == 0 ? "无分类位" : string.Join("、", parts);
    }

    private void ValidateJobStrategyCell(DataGridViewCellValidatingEventArgs e)
    {
        if (_jobStrategyEditorGrid.ReadOnly || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var column = _jobStrategyEditorGrid.Columns[e.ColumnIndex];
        if (column.ReadOnly) return;
        var columnName = column.DataPropertyName;
        if (column is DataGridViewComboBoxColumn) return;
        var value = Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture) ?? string.Empty;
        string? error = null;
        if (columnName == "名称")
        {
            var bytes = EncodingService.GetGbkByteCount(value);
            if (bytes > 11) error = $"策略名称超长：GBK {bytes} 字节，容量 11 字节。";
        }
        else if (JobStrategyPrimaryColumns.Contains(columnName) ||
                 JobStrategyCompanionColumns.Any(x => x.ColumnName == columnName) ||
                 TryGetJobStrategyLearningSourceColumn(columnName, out _))
        {
            error = TryParseInteger(value, 0, byte.MaxValue, columnName, _currentPageHexButton.Checked);
        }

        _jobStrategyEditorGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = error ?? string.Empty;
        if (error == null) return;
        e.Cancel = true;
        _jobStrategyEditorInfoBox.Text = error;
        SetStatus(error);
    }

    private void SaveJobStrategyEditor()
    {
        if (_project == null || _currentJobStrategyData == null || _jobStrategyRead == null || _jobStrategyDescriptionRead == null) return;

        _jobStrategyEditorGrid.EndEdit();
        if (!CommitJobStrategyDescriptionBoxEdit(showMessage: true, restoreSelectionOnFailure: true)) return;
        if (!CommitJobStrategyLearningEditorChanges(showMessage: true, restoreSelectionOnFailure: true)) return;
        if (!CommitJobStrategyLearningDialogs()) return;
        if (_currentJobStrategyData.GetChanges() == null)
        {
            MessageBox.Show(this, "兵种策略没有检测到改动。", "无需保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var preview = BuildChangePreview(_currentJobStrategyData, maxItems: 80);
        if (MessageBox.Show(this,
                $"即将保存兵种策略到当前 MOD 项目。\r\n\r\n变更预览：\r\n{preview}\r\n\r\n保存前会自动备份 Data.e5/Imsg.e5/Ekd5.exe，保存后会重新读取校验。是否继续？",
                "确认保存兵种策略",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var changedCells = GetChangedCellKeys(_currentJobStrategyData);
            var saves = SaveJobStrategyEditorData(_project, _currentJobStrategyData);
            AcceptSavedDataTable(_currentJobStrategyData);
            RefreshJobStrategyCellsAfterEdit(changedCells);
            var changedBytes = saves.Sum(x => x.ChangedBytes);
            System.Diagnostics.Debug.WriteLine($"已保存兵种策略：保存表 {saves.Count} 个，变化字节 {changedBytes}");
            foreach (var save in saves) System.Diagnostics.Debug.WriteLine("兵种策略备份：" + save.BackupPath);
            SetStatus($"兵种策略保存完成并已复读：变化 {changedBytes} 字节");
            MessageBox.Show(this,
                $"保存完成并已重新读取校验。\r\n保存表数量：{saves.Count}\r\n变化字节：{changedBytes}\r\n备份：{string.Join("; ", saves.Select(x => x.BackupPath))}",
                "保存完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("保存兵种策略失败：" + ex);
            MessageBox.Show(this, ex.Message, "保存兵种策略失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ImportSelectedJobStrategyIcons()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_currentJobStrategyData == null)
        {
            MessageBox.Show(this, "请先读取兵种策略。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!CommitJobStrategyLearningEditorChanges(showMessage: true, restoreSelectionOnFailure: true)) return;
        var selectedRows = GetSelectedJobStrategyRowsForIconImport();
        if (selectedRows.Count == 0)
        {
            MessageBox.Show(this, "请先在兵种策略表中选中要导入图标的策略行。", "导入策略图标", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (UseAlignedBatchImageImportDialogs())
        {
            ImportSelectedJobStrategyIconsAligned(selectedRows);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "选择要导入到所选策略图标的图片",
            Filter = "图片文件 (*.bmp;*.jpg;*.jpeg;*.png)|*.bmp;*.jpg;*.jpeg;*.png|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.FileNames.Length == 0) return;

        var orderedFiles = dialog.FileNames
            .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        if (orderedFiles.Length != selectedRows.Count)
        {
            MessageBox.Show(this,
                $"选中策略行数为 {selectedRows.Count}，选择图片数为 {orderedFiles.Length}。请保持数量一致。\r\n\r\n匹配规则：策略按当前表格显示顺序，图片按文件名排序。",
                "导入策略图标",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        IReadOnlyList<JobStrategyIconImportTarget> targets;
        try
        {
            targets = BuildJobStrategyIconImportTargets(selectedRows, orderedFiles);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导入策略图标", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var targetResource = Ccz66RevisedLayout.ResolveStrategyIconResourceFile(_project);
        var targetPath = Ccz66RevisedLayout.ResolveResourcePath(_project, targetResource);
        if (Ccz66RevisedLayout.IsE5IconResource(targetResource))
        {
            ImportSelectedJobStrategyIconsToE5(targets, targetPath);
            return;
        }

        var requests = targets.Select(target => new IconResourceBatchReplaceRequest
        {
            IconIndex = target.IconIndex,
            SourcePath = target.SourcePath,
            SourceLabel = target.SourcePath,
            OperationKind = "兵种策略图标批量导入"
        }).ToArray();

        IconResourceBatchReplacePreviewResult preview;
        try
        {
            Cursor = Cursors.WaitCursor;
            preview = _iconResourceReplaceService.PreviewReplaceBitmapIcons(_project, targetPath, requests);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("兵种策略图标批量导入预览失败：" + ex);
            MessageBox.Show(this, ex.Message, "策略图标导入预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        var previewText = BuildJobStrategyIconImportPreviewText(targets, preview);
        _jobStrategyPreviewInfoBox.Text = previewText;
        if (MessageBox.Show(this,
                previewText + "\r\n\r\n确认后会先备份 Mgcicon.dll，再一次写入这些 RT_BITMAP 图标资源；不会修改兵种策略表字段。是否继续？",
                "确认导入策略图标",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = _iconResourceReplaceService.ReplaceBitmapIcons(_project, targetPath, requests);
            _imageResourceCatalogService.ClearCache();
            _itemIconPreviewService.ClearCache();
            ShowSelectedJobStrategyCell();
            _jobStrategyPreviewInfoBox.Text = BuildJobStrategyIconImportResultText(result);
            System.Diagnostics.Debug.WriteLine($"兵种策略图标批量导入完成：{result.TargetRelativePath} count={result.Items.Count}");
            SetStatus($"策略图标导入完成：{result.Items.Count} 个");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("兵种策略图标批量导入失败：" + ex);
            MessageBox.Show(this, ex.Message, "策略图标导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ImportSelectedJobStrategyIconsAligned(IReadOnlyList<DataGridViewRow> selectedRows)
    {
        if (_project == null) return;

        var sourceSelection = SelectBatchImageImportSources("选择要导入到所选策略图标的图片或导出根目录");
        if (sourceSelection == null) return;

        BatchStrategyIconImportRequest request;
        try
        {
            request = new BatchStrategyIconImportRequest
            {
                SourceFiles = sourceSelection.SourceFiles,
                SourceRoot = sourceSelection.SourceRoot,
                TargetRows = BuildJobStrategyIconImportTargetRows(selectedRows),
                MatchMode = "auto",
                WriteMode = _project.IsTestCopy ? "test_copy" : "direct"
            };
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导入策略图标", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        BatchStrategyIconImportPreviewResult preview;
        try
        {
            Cursor = Cursors.WaitCursor;
            preview = _batchStrategyIconImportService.Preview(_project, request);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Batch strategy icon import preview failed: " + ex);
            MessageBox.Show(this, ex.Message, "策略图标导入预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        var previewText = BuildBatchStrategyIconImportPreviewText(preview);
        _jobStrategyPreviewInfoBox.Text = previewText;
        if (!preview.CanWrite)
        {
            MessageBox.Show(this, previewText, "策略图标导入存在阻断项", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (MessageBox.Show(
                this,
                previewText + "\r\n\r\n确认后会批量写入目标策略图标资源，并自动备份；不会修改兵种策略表字段。是否继续？",
                "确认导入策略图标",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = _batchStrategyIconImportService.Replace(_project, request);
            _imageResourceCatalogService.ClearCache();
            _itemIconPreviewService.ClearCache();
            ShowSelectedJobStrategyCell();
            _jobStrategyPreviewInfoBox.Text = BuildBatchStrategyIconImportResultText(result);
            SetStatus($"策略图标导入完成：{result.TotalOperationCount} 个");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Batch strategy icon import failed: " + ex);
            MessageBox.Show(this, ex.Message, "策略图标导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private static IReadOnlyList<BatchStrategyIconTargetRow> BuildJobStrategyIconImportTargetRows(IReadOnlyList<DataGridViewRow> selectedRows)
    {
        var targets = new List<BatchStrategyIconTargetRow>();
        foreach (var row in selectedRows)
        {
            var dataRow = TryGetDataRow(row) ?? throw new InvalidOperationException("选中行无法解析为兵种策略数据行。");
            var strategyId = Convert.ToInt32(dataRow["ID"], CultureInfo.InvariantCulture);
            var strategyName = Convert.ToString(dataRow["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
            if (!TryConvertToInt(dataRow["策略图标"], out var iconIndex))
            {
                throw new InvalidOperationException($"策略 ID={strategyId} 的“策略图标”字段不是有效整数。");
            }

            targets.Add(new BatchStrategyIconTargetRow(strategyId, strategyName, iconIndex));
        }

        var duplicateIcon = targets.GroupBy(target => target.IconIndex).FirstOrDefault(group => group.Count() > 1);
        if (duplicateIcon != null)
        {
            var ids = string.Join(", ", duplicateIcon.Select(target => target.RowId.ToString(CultureInfo.InvariantCulture)));
            throw new InvalidOperationException($"选中策略中有多个策略指向同一图标编号 {duplicateIcon.Key}: {ids}。请调整选择或策略图标字段。");
        }

        return targets;
    }

    private static string BuildBatchStrategyIconImportPreviewText(BatchStrategyIconImportPreviewResult preview)
    {
        var builder = new StringBuilder();
        builder.AppendLine("策略图标批量导入预览");
        builder.AppendLine($"目标：{preview.TargetRelativePath}");
        builder.AppendLine($"资源类型：{preview.ResourceKind}");
        builder.AppendLine($"可写入：{preview.Items.Count}");
        foreach (var item in preview.Items.Take(30))
        {
            var target = item.TargetImageNumbers.Count > 0
                ? $"E5 #{string.Join("/", item.TargetImageNumbers)}"
                : $"RT_BITMAP {string.Join("/", item.ResourceIds)}";
            builder.AppendLine($"- 策略 {item.RowId:D2} {item.DisplayName} -> 图标#{item.IconIndex} {target} <- {Path.GetFileName(item.SourcePath)}");
        }

        AppendBatchImageImportIssues(builder, preview.SkippedItems, preview.Warnings);
        return builder.ToString();
    }

    private static string BuildBatchStrategyIconImportResultText(BatchStrategyIconImportResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("策略图标批量导入完成");
        builder.AppendLine($"目标：{result.TargetRelativePath}");
        builder.AppendLine($"资源类型：{result.ResourceKind}");
        builder.AppendLine($"写入：{result.TotalOperationCount}");
        foreach (var item in result.Items.Take(30))
        {
            builder.AppendLine($"- 策略 {item.RowId:D2} {item.DisplayName} -> 图标#{item.IconIndex} <- {Path.GetFileName(item.SourcePath)}");
        }

        AppendBatchImageImportIssues(builder, result.SkippedItems, result.Warnings);
        return builder.ToString();
    }

    private static void AppendBatchImageImportIssues(
        StringBuilder builder,
        IReadOnlyList<BatchImageImportSkippedItem> skipped,
        IReadOnlyList<string> warnings)
    {
        if (skipped.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("跳过：");
            foreach (var item in skipped.Take(20))
            {
                builder.AppendLine($"- {item.Key} {item.SourcePath}: {item.Reason}");
            }
        }

        if (warnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("提示：");
            foreach (var warning in warnings.Take(20))
            {
                builder.AppendLine("- " + warning);
            }
        }
    }

    private void ImportSelectedJobStrategyIconsToE5(IReadOnlyList<JobStrategyIconImportTarget> targets, string targetPath)
    {
        var requests = targets.Select(target => new E5ImageBatchReplaceRequest
        {
            ImageNumber = target.IconIndex + 1,
            SourcePath = target.SourcePath,
            SourceLabel = target.SourcePath,
            OperationKind = "6.6 strategy icon E5 batch import"
        }).ToArray();

        E5ImageBatchReplacePreviewResult preview;
        try
        {
            Cursor = Cursors.WaitCursor;
            preview = _e5ImageReplaceService.PreviewBatchReplacement(_project!, targetPath, requests);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("6.6 strategy icon E5 batch import preview failed: " + ex);
            MessageBox.Show(this, ex.Message, "策略图标导入预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        var previewText = BuildJobStrategyIconImportE5PreviewText(targets, preview);
        _jobStrategyPreviewInfoBox.Text = previewText;
        if (MessageBox.Show(this,
                previewText + "\r\n\r\n6.6 修正版将策略图标写入 E5\\Mtem.e5，确认后会先备份目标 E5，再按“策略图标字段+1”的 E5 图号写入；不会修改兵种策略表字段。是否继续？",
                "确认导入策略图标",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = _e5ImageReplaceService.ReplaceBatch(_project!, targetPath, requests);
            _imageResourceCatalogService.ClearCache();
            _itemIconPreviewService.ClearCache();
            ShowSelectedJobStrategyCell();
            _jobStrategyPreviewInfoBox.Text = BuildJobStrategyIconImportE5ResultText(result);
            System.Diagnostics.Debug.WriteLine($"6.6 strategy icon E5 batch import completed: {result.TargetRelativePath} count={result.OperationCount}");
            SetStatus($"策略图标导入完成：{result.OperationCount} 个");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("6.6 strategy icon E5 batch import failed: " + ex);
            MessageBox.Show(this, ex.Message, "策略图标导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private IReadOnlyList<DataGridViewRow> GetSelectedJobStrategyRowsForIconImport()
    {
        var rows = _jobStrategyEditorGrid.SelectedCells
            .Cast<DataGridViewCell>()
            .Where(cell => cell.RowIndex >= 0)
            .Select(cell => _jobStrategyEditorGrid.Rows[cell.RowIndex])
            .Where(row => !row.IsNewRow)
            .Distinct()
            .OrderBy(row => row.Index)
            .ToList();
        if (rows.Count == 0 && _jobStrategyEditorGrid.CurrentRow is { IsNewRow: false } current)
        {
            rows.Add(current);
        }

        return rows;
    }

    private static IReadOnlyList<JobStrategyIconImportTarget> BuildJobStrategyIconImportTargets(
        IReadOnlyList<DataGridViewRow> selectedRows,
        IReadOnlyList<string> orderedFiles)
    {
        var targets = new List<JobStrategyIconImportTarget>();
        for (var i = 0; i < selectedRows.Count; i++)
        {
            var row = selectedRows[i];
            var dataRow = TryGetDataRow(row) ?? throw new InvalidOperationException("选中行无法解析为兵种策略数据行。");
            var strategyId = Convert.ToInt32(dataRow["ID"], CultureInfo.InvariantCulture);
            var strategyName = Convert.ToString(dataRow["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
            if (!TryConvertToInt(dataRow["策略图标"], out var iconIndex))
            {
                throw new InvalidOperationException($"策略 ID={strategyId} 的“策略图标”字段不是有效整数。");
            }

            targets.Add(new JobStrategyIconImportTarget(
                strategyId,
                strategyName,
                iconIndex,
                orderedFiles[i]));
        }

        var duplicateIcon = targets
            .GroupBy(target => target.IconIndex)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateIcon != null)
        {
            var ids = string.Join("、", duplicateIcon.Select(target => target.StrategyId.ToString(CultureInfo.InvariantCulture)));
            throw new InvalidOperationException($"选中策略中有多个策略指向同一个图标编号 {duplicateIcon.Key}：策略 {ids}。为避免重复覆盖，请调整选择或策略图标字段。");
        }

        return targets;
    }

    private static string BuildJobStrategyIconImportPreviewText(
        IReadOnlyList<JobStrategyIconImportTarget> targets,
        IconResourceBatchReplacePreviewResult preview)
    {
        var itemByIcon = preview.Items.ToDictionaryFirstByKey(item => item.IconIndex, item => item);
        var builder = new StringBuilder();
        builder.AppendLine($"目标：{preview.TargetRelativePath}");
        builder.AppendLine($"操作：{preview.OperationKind}");
        builder.AppendLine($"数量：{targets.Count}");
        builder.AppendLine("匹配：");
        foreach (var target in targets)
        {
            var fileName = Path.GetFileName(target.SourcePath);
            if (itemByIcon.TryGetValue(target.IconIndex, out var item))
            {
                builder.AppendLine($"- 策略 {target.StrategyId:D2} {target.StrategyName} -> 图标#{target.IconIndex} RT_BITMAP ID={string.Join("/", item.ResourceIds)} <- {fileName} ({item.SourceWidth}x{item.SourceHeight})");
            }
            else
            {
                builder.AppendLine($"- 策略 {target.StrategyId:D2} {target.StrategyName} -> 图标#{target.IconIndex} <- {fileName}");
            }
        }

        if (preview.FormatWarnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("提示：");
            foreach (var warning in preview.FormatWarnings.Take(12))
            {
                builder.AppendLine("- " + warning);
            }
            if (preview.FormatWarnings.Count > 12)
            {
                builder.AppendLine($"- 还有 {preview.FormatWarnings.Count - 12} 条提示未显示。");
            }
        }

        builder.AppendLine();
        builder.AppendLine(preview.RiskSummary);
        return builder.ToString();
    }

    private string BuildJobStrategyIconImportResultText(IconResourceBatchReplaceResult result)
    {
        return
            $"策略图标导入完成。\r\n" +
            $"目标：{result.TargetRelativePath}\r\n" +
            $"数量：{result.Items.Count}\r\n" +
            $"变化字节估计：{result.ChangedBytesEstimate:N0}\r\n" +
            $"备份：{result.BackupPath}\r\n" +
            $"报告：{result.ReportPath}\r\n" +
            _writeOperationReportFormatter.FormatForCreator(result.ReportJsonPath, maxChanges: 12);
    }

    private static string BuildJobStrategyIconImportE5PreviewText(
        IReadOnlyList<JobStrategyIconImportTarget> targets,
        E5ImageBatchReplacePreviewResult preview)
    {
        var itemByImage = preview.Operations.ToDictionaryFirstByKey(item => item.ImageNumber, item => item);
        var builder = new StringBuilder();
        builder.AppendLine($"目标：{preview.TargetRelativePath}");
        builder.AppendLine("操作：6.6 strategy icon E5 batch import");
        builder.AppendLine($"数量：{targets.Count}");
        builder.AppendLine("匹配：");
        foreach (var target in targets)
        {
            var imageNumber = target.IconIndex + 1;
            var fileName = Path.GetFileName(target.SourcePath);
            if (itemByImage.TryGetValue(imageNumber, out var item))
            {
                builder.AppendLine($"- 策略 {target.StrategyId:D2} {target.StrategyName} -> 字段#{target.IconIndex} / E5图号#{imageNumber} <- {fileName} ({item.SourceWidth}x{item.SourceHeight})");
            }
            else
            {
                builder.AppendLine($"- 策略 {target.StrategyId:D2} {target.StrategyName} -> 字段#{target.IconIndex} / E5图号#{imageNumber} <- {fileName}");
            }
        }

        if (preview.FormatWarnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("提示：");
            foreach (var warning in preview.FormatWarnings.Take(12))
            {
                builder.AppendLine("- " + warning);
            }
            if (preview.FormatWarnings.Count > 12)
            {
                builder.AppendLine($"- 还有 {preview.FormatWarnings.Count - 12} 条提示未显示。");
            }
        }

        builder.AppendLine();
        builder.AppendLine(preview.RiskSummary);
        return builder.ToString();
    }

    private string BuildJobStrategyIconImportE5ResultText(E5ImageBatchReplaceResult result)
    {
        return
            $"策略图标导入完成。\r\n" +
            $"目标：{result.TargetRelativePath}\r\n" +
            $"数量：{result.OperationCount}\r\n" +
            $"变化字节估计：{result.ChangedBytesEstimate:N0}\r\n" +
            $"备份：{result.BackupPath}\r\n" +
            $"报告：{result.ReportPath}\r\n" +
            _writeOperationReportFormatter.FormatForCreator(result.ReportJsonPath, maxChanges: 12);
    }

    private IReadOnlyList<TableSaveResult> SaveJobStrategyEditorData(CczProject project, DataTable strategyData)
    {
        if (_jobStrategyRead == null || _jobStrategyDescriptionRead == null) return Array.Empty<TableSaveResult>();
        foreach (DataRow editorRow in strategyData.Rows)
        {
            if (editorRow.RowState != DataRowState.Modified) continue;
            var id = Convert.ToInt32(editorRow["ID"], CultureInfo.InvariantCulture);
            var strategyRow = FindRowById(_jobStrategyRead.Data, id);
            var descriptionRow = FindRowById(_jobStrategyDescriptionRead.Data, id);

            foreach (var columnName in JobStrategyPrimaryColumns)
            {
                if (IsRoleColumnChanged(editorRow, columnName)) strategyRow[columnName] = editorRow[columnName, DataRowVersion.Current];
            }

            if (IsRoleColumnChanged(editorRow, "策略介绍"))
            {
                descriptionRow["介绍"] = editorRow["策略介绍", DataRowVersion.Current];
            }

            foreach (DataColumn column in strategyData.Columns)
            {
                if (!TryGetJobStrategyLearningSourceColumn(column.ColumnName, out var sourceColumnName)) continue;
                if (!IsRoleColumnChanged(editorRow, column.ColumnName)) continue;
                strategyRow[sourceColumnName] = editorRow[column.ColumnName, DataRowVersion.Current];
            }

            foreach (var companion in JobStrategyCompanionColumns)
            {
                if (!IsRoleColumnChanged(editorRow, companion.ColumnName)) continue;
                var companionRead = _jobStrategyCompanionReads[companion.ColumnName];
                var companionRow = FindRowById(companionRead.Data, id);
                companionRow["内容"] = editorRow[companion.ColumnName, DataRowVersion.Current];
            }
        }

        var saves = new List<TableSaveResult>();
        if (SaveChangedTableAndVerify(_jobStrategyRead) is { } strategySave) saves.Add(strategySave);
        if (SaveChangedTableAndVerify(_jobStrategyDescriptionRead) is { } descriptionSave) saves.Add(descriptionSave);
        foreach (var companion in JobStrategyCompanionColumns)
        {
            var read = _jobStrategyCompanionReads[companion.ColumnName];
            if (SaveChangedTableAndVerify(read) is { } companionSave) saves.Add(companionSave);
        }

        return saves;
    }

    private static (int Count, string Source) ResolveStrategyMagicCount(CczProject project)
    {
        var path = !string.IsNullOrWhiteSpace(project.ImageAssignerSystemIniPath) && File.Exists(project.ImageAssignerSystemIniPath)
            ? project.ImageAssignerSystemIniPath
            : ProjectDetector.FindPortableFile(
                project,
                "System.ini",
                Path.Combine("老版游戏制作工具", "B形象指定器", "6.6x形象指定器", "System.ini"),
                Path.Combine("B形象指定器", "6.6x形象指定器", "System.ini"),
                Path.Combine("老版游戏制作工具", "B形象指定器", "形象指定器6.5", "System.ini"),
                Path.Combine("B形象指定器", "形象指定器6.5", "System.ini"));
        if (path != null && File.Exists(path))
        {
            foreach (var rawLine in File.ReadLines(path, EncodingService.Gbk))
            {
                var line = rawLine.Split(';')[0].Trim();
                if (line.StartsWith("SMagic=", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(line["SMagic=".Length..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return (parsed, path);
                }
            }

            return (0, path);
        }

        return (0, "B形象指定器 System.ini 未找到");
    }

    private void LoadJobMatrixEditor()
    {
        if (_project == null) return;
        if (_tables.Count == 0)
        {
            ReloadCurrentProject();
            if (_tables.Count == 0) return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            _jobSeriesRead = _tableReader.Read(_project, FindTable(_tables, "6.5-3 兵种系"), _tables);
            _jobRestraintRead = _tableReader.Read(_project, FindTable(_tables, "6.5-3-3 兵种相克"), _tables);
            _jobAttributeRead = _tableReader.Read(_project, FindTable(_tables, "6.5-3-4 兵种属性"), _tables);
            if (!_jobSeriesRead.Validation.IsUsable || !_jobRestraintRead.Validation.IsUsable || !_jobAttributeRead.Validation.IsUsable)
            {
                throw new InvalidOperationException("兵种相克/属性矩阵有不可读取项，请先查看数据表诊断。");
            }

            _jobRestraintGrid.DataSource = _jobRestraintRead.Data;
            ConfigureJobMatrixGrid(_jobRestraintGrid);
            _currentJobAttributeEditorData = BuildJobAttributeEditorData();
            _jobAttributeGrid.DataSource = _currentJobAttributeEditorData;
            ConfigureJobAttributeEditorGrid();
            _saveJobMatrixButton.Enabled = true;
            _saveJobAttributeMatrixButton.Enabled = true;
            SetJobMatrixBulkButtonsEnabled(true);
            _jobMatrixInfoBox.Text = BuildJobMatrixSummary();
            SetStatus("兵种相克/属性矩阵读取完成");
        }
        catch (Exception ex)
        {
            _jobMatrixInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("读取兵种相克/属性矩阵失败：" + ex);
            MessageBox.Show(this, ex.Message, "读取兵种矩阵失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ConfigureJobMatrixGrid(DataGridView grid)
    {
        grid.ReadOnly = false;
        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.ReadOnly = column.DataPropertyName == "ID" || column.DataPropertyName == "名称";
            column.HeaderText = BuildJobMatrixColumnHeader(column.DataPropertyName);
            column.ToolTipText = BuildJobMatrixColumnAnnotation(column.DataPropertyName);
            if (column.DataPropertyName == "名称") column.Width = 90;
            if (column.ReadOnly) column.DefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);
        }

        RefreshJobMatrixRowStyles(grid);
    }

    private void SetJobMatrixBulkButtonsEnabled(bool enabled)
    {
        _exportJobAttributeCsvButton.Enabled = enabled;
        _importJobAttributeCsvButton.Enabled = enabled;
        _pasteJobMatrixSelectionButton.Enabled = enabled;
        _fillJobMatrixSelectionButton.Enabled = enabled;
        _batchModifyJobMatrixButton.Enabled = enabled;
    }

    private DataGridView GetActiveJobMatrixGrid()
        => _jobAttributeGrid;

    private void PasteActiveJobMatrixSelection()
    {
        var grid = GetActiveJobMatrixGrid();
        PasteGridSelection(grid, (_, _) => { }, null, RefreshJobMatrixCellsAfterEdit);
    }

    private void FillActiveJobMatrixSelectionWithCurrentValue()
    {
        var grid = GetActiveJobMatrixGrid();
        FillSelectedGridColumnWithCurrentValue(grid, (_, _) => { }, null, RefreshJobMatrixCellsAfterEdit);
    }

    private void ShowActiveJobMatrixBatchModifyDialog()
    {
        var grid = GetActiveJobMatrixGrid();
        ShowGridBatchModifyDialog(grid, (_, _) => { }, null, RefreshJobMatrixCellsAfterEdit);
    }

    private DataTable BuildJobAttributeEditorData()
    {
        if (_jobSeriesRead == null || _jobAttributeRead == null)
        {
            throw new InvalidOperationException("兵种属性转置表需要先读取兵种系与兵种属性矩阵。");
        }

        var output = new DataTable("兵种属性");
        output.Columns.Add("ID", typeof(int));
        output.Columns.Add("兵种", typeof(string));
        foreach (var definition in JobAttributeDefinitions.Values.OrderBy(static definition => definition.RowId))
        {
            output.Columns.Add(definition.Name, typeof(int));
        }

        var seriesCount = Math.Min(40, _jobSeriesRead.Data.Rows.Count);
        for (var seriesId = 0; seriesId < seriesCount; seriesId++)
        {
            var outputRow = output.NewRow();
            outputRow["ID"] = seriesId;
            outputRow["兵种"] = BuildJobSeriesName(seriesId);
            foreach (var definition in JobAttributeDefinitions.Values.OrderBy(static definition => definition.RowId))
            {
                outputRow[definition.Name] = ReadJobAttributeRawValue(definition.RowId, seriesId);
            }

            output.Rows.Add(outputRow);
        }

        output.AcceptChanges();
        return output;
    }

    private void ConfigureJobAttributeEditorGrid()
    {
        if (_configuringJobAttributeGrid) return;
        var grid = _jobAttributeGrid;
        try
        {
            _configuringJobAttributeGrid = true;
            grid.ReadOnly = false;
            grid.RowHeadersVisible = true;
            grid.RowHeadersWidth = 112;
            grid.TopLeftHeaderCell.Value = "兵种";
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
                column.ReadOnly = column.DataPropertyName is "ID" or "兵种";
                column.HeaderText = column.DataPropertyName;
                column.ToolTipText = BuildJobAttributeColumnAnnotation(column.DataPropertyName);
                if (column.DataPropertyName == "ID")
                {
                    column.Visible = false;
                    column.HeaderText = "ID";
                    column.Width = 52;
                    column.Frozen = true;
                }
                else if (column.DataPropertyName == "兵种")
                {
                    column.Visible = false;
                    column.Width = 92;
                    column.Frozen = true;
                }
                else
                {
                    column.Visible = true;
                    column.Frozen = false;
                }

                if (column.ReadOnly) column.DefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);
            }

            var attributeColumnIndexes = grid.Columns
                .Cast<DataGridViewColumn>()
                .Where(column => GetJobAttributeDefinitionByName(column.DataPropertyName) != null)
                .Select(column => column.Index)
                .ToArray();

            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow || row.Cells["ID"].Value == null) continue;
                var seriesId = Convert.ToInt32(row.Cells["ID"].Value, CultureInfo.InvariantCulture);
                row.HeaderCell.Value = BuildJobSeriesColumnLabel(seriesId);

                foreach (var columnIndex in attributeColumnIndexes)
                {
                    var cell = row.Cells[columnIndex];
                    var definition = GetJobAttributeDefinitionByName(cell.OwningColumn.DataPropertyName);
                    if (definition == null) continue;
                    var value = Convert.ToInt32(cell.Value, CultureInfo.InvariantCulture);
                    if (definition.EditorKind == JobAttributeEditorKind.Numeric)
                    {
                        if (cell is DataGridViewComboBoxCell)
                        {
                            row.Cells[columnIndex] = new DataGridViewTextBoxCell { Value = value, ToolTipText = definition.ValueRule };
                        }
                        else
                        {
                            cell.ToolTipText = definition.ValueRule;
                        }

                        continue;
                    }

                    var choices = BuildJobAttributeChoicesWithCustomValue(definition, value);
                    row.Cells[columnIndex] = new DataGridViewComboBoxCell
                    {
                        DataSource = choices,
                        ValueMember = nameof(JobAttributeValueChoice.Value),
                        DisplayMember = nameof(JobAttributeValueChoice.DisplayText),
                        Value = value,
                        FlatStyle = FlatStyle.Popup,
                        ToolTipText = definition.ValueRule
                    };
                }
            }
        }
        finally
        {
            _configuringJobAttributeGrid = false;
        }
    }

    private static IReadOnlyList<JobAttributeValueChoice> BuildJobAttributeChoicesWithCustomValue(JobAttributeDefinition definition, int value)
        => definition.Choices.Any(choice => choice.Value == value)
            ? definition.Choices
            : definition.Choices.Concat([new JobAttributeValueChoice(value, "自定义")]).ToArray();

    private int ReadJobAttributeRawValue(int attributeRowId, int jobSeriesId)
    {
        if (_jobAttributeRead == null) return 0;
        var row = FindRowById(_jobAttributeRead.Data, attributeRowId);
        var columnName = jobSeriesId.ToString(CultureInfo.InvariantCulture);
        return _jobAttributeRead.Data.Columns.Contains(columnName)
            ? Convert.ToInt32(row[columnName], CultureInfo.InvariantCulture)
            : 0;
    }

    private void WriteJobAttributeRawValue(int attributeRowId, int jobSeriesId, int value)
    {
        if (_jobAttributeRead == null) return;
        var row = FindRowById(_jobAttributeRead.Data, attributeRowId);
        var columnName = jobSeriesId.ToString(CultureInfo.InvariantCulture);
        if (!_jobAttributeRead.Data.Columns.Contains(columnName)) return;
        row[columnName] = value;
    }

    private static JobAttributeDefinition? GetJobAttributeDefinitionByName(string? columnName)
        => string.IsNullOrWhiteSpace(columnName)
            ? null
            : JobAttributeDefinitions.Values.FirstOrDefault(definition => definition.Name.Equals(columnName, StringComparison.Ordinal));

    private static string BuildJobAttributeColumnAnnotation(string columnName)
    {
        var definition = GetJobAttributeDefinitionByName(columnName);
        if (definition == null)
        {
            return columnName switch
            {
                "ID" => "兵种系编号，来自 6.5-3 兵种系。",
                "兵种" => "兵种系名称，来自 6.5-3 兵种系。",
                _ => string.Empty
            };
        }

        return $"兵种属性：{definition.Name}。{definition.ValueRule}";
    }

    private void ExportJobAttributeEditorCsv()
    {
        if (_currentJobAttributeEditorData == null) return;
        using var dialog = new SaveFileDialog
        {
            Title = "导出兵种属性 CSV",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = "兵种属性.csv"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var records = BuildJobAttributeCsvExportRecords();
            CsvService.WriteRecords(dialog.FileName, records);
            System.Diagnostics.Debug.WriteLine("兵种属性 CSV exported: " + dialog.FileName);
            SetStatus("兵种属性 CSV 导出完成。");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("兵种属性 CSV 导出失败：" + ex);
            MessageBox.Show(this, ex.Message, "兵种属性 CSV 导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private IReadOnlyList<IReadOnlyList<string>> BuildJobAttributeCsvExportRecords()
    {
        if (_currentJobAttributeEditorData == null) return Array.Empty<IReadOnlyList<string>>();
        var columns = BuildJobAttributeCsvColumnNames();
        var records = new List<IReadOnlyList<string>> { columns };
        foreach (DataRow row in _currentJobAttributeEditorData.Rows)
        {
            var values = new List<string>(columns.Count);
            foreach (var columnName in columns)
            {
                if (TryGetJobAttributeColumnId(columnName, out var attributeRowId))
                {
                    var value = Convert.ToInt32(row[columnName], CultureInfo.InvariantCulture);
                    values.Add(FormatJobAttributeValue(attributeRowId, value));
                    continue;
                }

                values.Add(Convert.ToString(row[columnName], CultureInfo.InvariantCulture) ?? string.Empty);
            }

            records.Add(values);
        }

        return records;
    }

    private void ImportJobAttributeEditorCsv()
    {
        if (_currentJobAttributeEditorData == null || _jobAttributeRead == null) return;
        using var dialog = new OpenFileDialog
        {
            Title = "导入兵种属性 CSV",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var records = CsvService.ReadRecords(dialog.FileName);
            var changedCells = ApplyJobAttributeCsvImport(records);
            RefreshJobMatrixCellsAfterEdit(changedCells);
            System.Diagnostics.Debug.WriteLine($"兵种属性 CSV imported: {dialog.FileName}; changed cells {changedCells.Count}");
            SetStatus($"兵种属性 CSV 导入完成：更新 {changedCells.Count} 个单元格。");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("兵种属性 CSV 导入失败：" + ex);
            MessageBox.Show(this, ex.Message, "兵种属性 CSV 导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private IReadOnlyList<GridCellKey> ApplyJobAttributeCsvImport(IReadOnlyList<IReadOnlyList<string>> records)
    {
        if (_currentJobAttributeEditorData == null) return Array.Empty<GridCellKey>();
        if (records.Count == 0) throw new InvalidOperationException("CSV 文件为空。");
        var headers = records[0].Select(static header => header.Trim()).ToList();
        if (headers.Count == 0) throw new InvalidOperationException("CSV 表头为空。");

        var seenHeaders = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            if (!seenHeaders.Add(header))
            {
                throw new InvalidOperationException($"CSV 存在重复列：{header}。");
            }

            if (header is "ID" or "兵种") continue;
            if (!TryGetJobAttributeColumnId(header, out _))
            {
                throw new InvalidOperationException($"CSV 第 {i + 1} 列不是兵种属性字段：{header}。");
            }
        }

        var idIndex = headers.FindIndex(static header => string.Equals(header, "ID", StringComparison.Ordinal));
        if (idIndex < 0) throw new InvalidOperationException("兵种属性 CSV 必须包含 ID 列，用于匹配兵种行。");
        var changedCells = new List<GridCellKey>();
        var seenIds = new HashSet<int>();
        for (var r = 1; r < records.Count; r++)
        {
            var record = records[r];
            if (record.All(string.IsNullOrWhiteSpace)) continue;
            if (record.Count != headers.Count)
            {
                throw new InvalidOperationException($"CSV 第 {r + 1} 行列数 {record.Count} 与表头列数 {headers.Count} 不一致。");
            }

            if (!int.TryParse(record[idIndex].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seriesId))
            {
                throw new InvalidOperationException($"CSV 第 {r + 1} 行 ID 不是有效数字：{record[idIndex]}。");
            }

            if (!seenIds.Add(seriesId))
            {
                throw new InvalidOperationException($"CSV 第 {r + 1} 行 ID={seriesId} 重复。");
            }

            var targetRow = TryFindRowById(_currentJobAttributeEditorData, seriesId)
                            ?? throw new InvalidOperationException($"CSV 第 {r + 1} 行 ID={seriesId} 在当前兵种属性表中不存在。");
            var rowIndex = _currentJobAttributeEditorData.Rows.IndexOf(targetRow);
            for (var c = 0; c < headers.Count; c++)
            {
                var columnName = headers[c];
                if (columnName is "ID" or "兵种") continue;
                if (!TryGetJobAttributeColumnId(columnName, out var attributeRowId)) continue;

                var text = record[c];
                if (!TryParseJobAttributeImportValue(attributeRowId, text, out var value, out var error))
                {
                    throw new InvalidOperationException($"CSV 第 {r + 1} 行列 {columnName} 的值无效：{text}。{error}");
                }

                var current = Convert.ToInt32(targetRow[columnName], CultureInfo.InvariantCulture);
                if (current == value) continue;
                targetRow[columnName] = value;
                changedCells.Add(new GridCellKey(seriesId.ToString(CultureInfo.InvariantCulture), rowIndex, columnName));
            }
        }

        return changedCells;
    }

    private static IReadOnlyList<string> BuildJobAttributeCsvColumnNames()
        => ["ID", "兵种", .. JobAttributeDefinitions.Values
            .OrderBy(static definition => definition.RowId)
            .Select(static definition => definition.Name)];

    private static bool TryParseJobAttributeImportValue(int attributeRowId, string text, out int value, out string error)
    {
        error = string.Empty;
        var definition = GetJobAttributeDefinition(attributeRowId);
        if (definition.EditorKind == JobAttributeEditorKind.Combo)
        {
            if (TryParseJobAttributeDisplay(attributeRowId, text, out value)) return true;
            error = "下拉字段支持 中文名称：数字、中文名称、0..255、0xNN 或 自定义：N。";
            return false;
        }

        if (TryParseIntegerInput(text.Trim(), out var parsed) &&
            TryConvertParsedIntegerToSigned(parsed, byte.MinValue, byte.MaxValue, out var parsedValue) &&
            parsedValue is >= byte.MinValue and <= byte.MaxValue)
        {
            value = (int)parsedValue;
            return true;
        }

        value = 0;
        error = "策略伤害必须是 0..255 的单字节数值。";
        return false;
    }

    private bool TryConvertJobAttributeGridTextValue(
        DataGridView grid,
        int columnIndex,
        string text,
        out object value,
        out string error)
    {
        value = 0;
        error = string.Empty;
        if (!ReferenceEquals(grid, _jobAttributeGrid) ||
            columnIndex < 0 ||
            columnIndex >= _jobAttributeGrid.Columns.Count ||
            !TryGetJobAttributeColumnId(_jobAttributeGrid.Columns[columnIndex].DataPropertyName, out var attributeRowId))
        {
            return false;
        }

        if (TryParseJobAttributeImportValue(attributeRowId, text, out var parsed, out error))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private string BuildJobMatrixColumnHeader(string columnName, bool isAttributeGrid = false)
    {
        if (columnName == "ID") return isAttributeGrid ? "属性序号" : "ID";
        if (columnName == "名称") return "攻击方\n兵种系";
        return TryGetJobSeriesName(columnName, out var name)
            ? $"{int.Parse(columnName, CultureInfo.InvariantCulture):00}\n{name}"
            : columnName;
    }

    private string BuildJobMatrixColumnAnnotation(string columnName, bool isAttributeGrid = false)
    {
        if (columnName == "ID") return isAttributeGrid ? "兵种属性行序号：0..7，对应形象指定器 Option1 控件数组顺序。" : "兵种系编号。";
        if (columnName == "名称") return "攻击方兵种系名称。";
        var target = TryGetJobSeriesName(columnName, out var name) ? $"{columnName}:{name}" : columnName;
        if (isAttributeGrid)
        {
            return $"兵种属性矩阵：当前行属性作用于列 {target}。{BuildJobAttributeValueRuleForCurrentRow()}";
        }

        return $"兵种相克矩阵：当前行兵种系攻击/作用于列 {target} 时的相克数值。具体倍率含义需结合实机确认。";
    }

    private bool TryGetJobSeriesName(string columnName, out string name)
    {
        name = string.Empty;
        if (!int.TryParse(columnName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) return false;
        if (_jobSeriesRead == null || id < 0 || id >= _jobSeriesRead.Data.Rows.Count) return false;
        name = Convert.ToString(_jobSeriesRead.Data.Rows[id]["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(name);
    }

    private string BuildJobMatrixSummary()
    {
        var restraintRows = _jobRestraintRead?.Data.Rows.Count ?? 0;
        var restraintCols = _jobRestraintRead?.Data.Columns.Count - 2 ?? 0;
        var attributeRows = _jobAttributeRead?.Data.Rows.Count ?? 0;
        var attributeCols = _jobAttributeRead == null
            ? 0
            : _jobAttributeRead.Data.Columns
                .Cast<DataColumn>()
                .Count(column => int.TryParse(column.ColumnName, NumberStyles.Integer, CultureInfo.InvariantCulture, out _));
        return
            $"兵种矩阵已读取：相克 {restraintRows}x{restraintCols}，属性底层 {attributeRows}x{attributeCols}，GUI 转置显示为 {attributeCols}x{attributeRows}。\r\n" +
            "相克来源：Ekd5.exe@0xA3280，offset = 0xA3280 + 攻方兵种系ID*40 + 守方兵种系ID。\r\n" +
            "属性来源：Ekd5.exe@0xA38C0，offset = 0xA38C0 + 属性序号*40 + 兵种系ID；GUI 每行是兵种系、每列是属性，除策略伤害列使用数值输入外，其余属性列使用中文下拉。\r\n" +
            "UserXK 尾部 0xA3A00..0xA3A27 只作为证据候选归档，不在本页开放写入。保存前自动备份，保存后重新读取校验。";
    }

    private void RefreshJobMatrixRowStyles(DataGridView grid)
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            RefreshJobMatrixRowStyle(grid, row.Index);
        }
    }

    private void RefreshJobMatrixRowStyle(DataGridView grid, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= grid.Rows.Count) return;
        var dataRow = TryGetDataRow(grid.Rows[rowIndex]);
        if (dataRow == null) return;
        grid.Rows[rowIndex].DefaultCellStyle.BackColor = IsDataRowChanged(dataRow) ? Color.LightCyan : Color.Empty;
    }

    private void RefreshJobMatrixCellsAfterEdit(IReadOnlyList<GridCellKey> changedCells)
    {
        RefreshChangedGridCells(_jobRestraintGrid, changedCells);
        RefreshChangedGridRowsOnly(_jobRestraintGrid, changedCells, rowIndex => RefreshJobMatrixRowStyle(_jobRestraintGrid, rowIndex));
        SyncJobAttributeEditorCellsToSource(changedCells);
        var attributeEditorCells = BuildJobAttributeEditorCellKeys(changedCells);
        RefreshChangedGridCells(_jobAttributeGrid, attributeEditorCells);
        ConfigureJobAttributeEditorGrid();
        RefreshChangedGridRowsOnly(_jobAttributeGrid, attributeEditorCells, rowIndex => RefreshJobMatrixRowStyle(_jobAttributeGrid, rowIndex));
        if (_jobAttributeGrid.Focused || _jobAttributeGrid.ContainsFocus)
        {
            ShowSelectedJobMatrixCell(_jobAttributeGrid, "兵种属性");
        }
        else
        {
            ShowSelectedJobMatrixCell(_jobRestraintGrid, "兵种相克");
        }
    }

    private IReadOnlyList<GridCellKey> BuildJobAttributeEditorCellKeys(IReadOnlyList<GridCellKey> changedCells)
    {
        if (_currentJobAttributeEditorData == null) return changedCells;
        var result = new List<GridCellKey>();
        foreach (var key in changedCells)
        {
            if (key.RowKey != null &&
                int.TryParse(key.RowKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) &&
                key.ColumnName != null &&
                TryGetJobAttributeColumnId(key.ColumnName, out _))
            {
                result.Add(key);
                continue;
            }

            if (key.RowKey != null &&
                int.TryParse(key.RowKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var attributeRowId) &&
                int.TryParse(key.ColumnName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seriesId))
            {
                var definition = GetJobAttributeDefinition(attributeRowId);
                var rowIndex = FindJobAttributeEditorGridRowIndex(seriesId);
                if (rowIndex >= 0)
                {
                    result.Add(new GridCellKey(seriesId.ToString(CultureInfo.InvariantCulture), rowIndex, definition.Name));
                }
            }
        }

        return result.Count == 0 ? changedCells : result;
    }

    private int FindJobAttributeEditorGridRowIndex(int seriesId)
    {
        for (var i = 0; i < _jobAttributeGrid.Rows.Count; i++)
        {
            if (_jobAttributeGrid.Rows[i].IsNewRow) continue;
            var value = _jobAttributeGrid.Rows[i].Cells["ID"].Value;
            if (value != null && Convert.ToInt32(value, CultureInfo.InvariantCulture) == seriesId)
            {
                return i;
            }
        }

        return -1;
    }

    private void SyncJobAttributeEditorCellsToSource(IReadOnlyList<GridCellKey> changedCells)
    {
        if (_jobAttributeRead == null || _currentJobAttributeEditorData == null) return;
        foreach (var key in changedCells)
        {
            if (string.IsNullOrWhiteSpace(key.ColumnName) ||
                !TryGetJobAttributeColumnId(key.ColumnName, out var attributeRowId))
            {
                continue;
            }

            var editorRow = key.RowKey != null &&
                            int.TryParse(key.RowKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowKey)
                ? TryFindRowById(_currentJobAttributeEditorData, rowKey)
                : key.RowIndex >= 0 && key.RowIndex < _currentJobAttributeEditorData.Rows.Count
                    ? _currentJobAttributeEditorData.Rows[key.RowIndex]
                    : null;
            if (editorRow == null) continue;
            var seriesId = Convert.ToInt32(editorRow["ID"], CultureInfo.InvariantCulture);
            var value = Convert.ToInt32(editorRow[key.ColumnName], CultureInfo.InvariantCulture);
            WriteJobAttributeRawValue(attributeRowId, seriesId, value);
        }
    }

    private void ShowSelectedJobMatrixCell(DataGridView grid, string matrixName)
    {
        if (grid.CurrentCell == null) return;
        var cell = grid.CurrentCell;
        if (cell.RowIndex < 0 || cell.ColumnIndex < 0) return;
        var columnName = grid.Columns[cell.ColumnIndex].DataPropertyName;
        if (string.IsNullOrWhiteSpace(columnName)) return;
        var row = grid.Rows[cell.RowIndex];
        var isAttributeGrid = ReferenceEquals(grid, _jobAttributeGrid);
        var rowName = isAttributeGrid ? BuildJobSeriesColumnLabel(Convert.ToInt32(row.Cells["ID"].Value, CultureInfo.InvariantCulture)) : row.Cells["名称"].Value;
        var targetName = isAttributeGrid ? columnName : TryGetJobSeriesName(columnName, out var jobName) ? $"{columnName}:{jobName}" : columnName;
        var offsetText = TryBuildJobMatrixCellOffsetText(grid, row, columnName, out var fileOffset)
            ? $"文件偏移：{fileOffset}\r\n"
            : string.Empty;
        var rawValueText = Convert.ToString(cell.Value, CultureInfo.InvariantCulture) ?? string.Empty;
        var displayValueText = isAttributeGrid &&
                               int.TryParse(rawValueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawValue) &&
                               TryGetJobAttributeColumnId(columnName, out var attributeRowId)
            ? FormatJobAttributeValue(attributeRowId, rawValue)
            : rawValueText;
        _jobMatrixInfoBox.Text =
            $"{matrixName}：行={rowName}    列={targetName}\r\n" +
            $"当前值：{displayValueText}\r\n" +
            (displayValueText == rawValueText ? string.Empty : $"原始数值：{rawValueText}\r\n") +
            "\r\n" +
            offsetText +
            (isAttributeGrid
                ? BuildJobAttributeColumnAnnotation(columnName)
                : BuildJobMatrixColumnAnnotation(columnName));
    }

    private void ValidateJobMatrixCell(DataGridView grid, DataGridViewCellValidatingEventArgs e)
    {
        if (grid.ReadOnly || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var column = grid.Columns[e.ColumnIndex];
        if (column.ReadOnly) return;
        var value = Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture) ?? string.Empty;
        if (ReferenceEquals(grid, _jobAttributeGrid) &&
            TryGetJobAttributeColumnId(column.DataPropertyName, out var rowId))
        {
            var definition = GetJobAttributeDefinition(rowId);
            if (definition.EditorKind == JobAttributeEditorKind.Combo &&
                TryParseJobAttributeDisplay(rowId, value, out _))
            {
                grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = string.Empty;
                return;
            }
        }

        var error = TryParseInteger(value, 0, byte.MaxValue, column.DataPropertyName, _currentPageHexButton.Checked);
        grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = error ?? string.Empty;
        if (error == null) return;
        e.Cancel = true;
        _jobMatrixInfoBox.Text = error;
        SetStatus(error);
    }

    private void ParseJobAttributeMatrixCell(DataGridViewCellParsingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (e.ColumnIndex >= _jobAttributeGrid.Columns.Count ||
            !TryGetJobAttributeColumnId(_jobAttributeGrid.Columns[e.ColumnIndex].DataPropertyName, out var rowId))
        {
            return;
        }

        var definition = GetJobAttributeDefinition(rowId);
        if (definition.EditorKind != JobAttributeEditorKind.Combo) return;
        var text = Convert.ToString(e.Value, CultureInfo.InvariantCulture) ?? string.Empty;
        if (!TryParseJobAttributeDisplay(rowId, text, out var value)) return;
        EnsureJobAttributeComboCellContainsValue(e.RowIndex, e.ColumnIndex, rowId, value);
        e.Value = value;
        e.ParsingApplied = true;
    }

    private void ConfigureJobAttributeEditingControl(Control control)
    {
        if (control is not DataGridViewComboBoxEditingControl combo) return;
        if (_jobAttributeGrid.CurrentCell == null ||
            !TryGetJobAttributeColumnId(_jobAttributeGrid.Columns[_jobAttributeGrid.CurrentCell.ColumnIndex].DataPropertyName, out var rowId) ||
            GetJobAttributeDefinition(rowId).EditorKind != JobAttributeEditorKind.Combo)
        {
            return;
        }

        combo.DropDownStyle = ComboBoxStyle.DropDown;
        combo.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        combo.AutoCompleteSource = AutoCompleteSource.ListItems;
    }

    private void HandleJobAttributeGridDataError(DataGridViewDataErrorEventArgs e)
    {
        e.ThrowException = false;
        SetStatus("兵种属性单元格显示值无法匹配；可输入 0..255 原始数值，或重新选择下拉项。");
    }

    private void EnsureJobAttributeComboCellContainsValue(int rowIndex, int columnIndex, int rowId, int value)
    {
        if (rowIndex < 0 || rowIndex >= _jobAttributeGrid.Rows.Count ||
            columnIndex < 0 || columnIndex >= _jobAttributeGrid.Columns.Count ||
            _jobAttributeGrid.Rows[rowIndex].Cells[columnIndex] is not DataGridViewComboBoxCell comboCell)
        {
            return;
        }

        var definition = GetJobAttributeDefinition(rowId);
        if (definition.EditorKind != JobAttributeEditorKind.Combo) return;
        comboCell.DataSource = BuildJobAttributeChoicesWithCustomValue(definition, value);
        comboCell.ValueMember = nameof(JobAttributeValueChoice.Value);
        comboCell.DisplayMember = nameof(JobAttributeValueChoice.DisplayText);
    }

    private bool TryGetJobAttributeSeriesId(int rowIndex, out int seriesId)
    {
        seriesId = -1;
        if (rowIndex < 0 || rowIndex >= _jobAttributeGrid.Rows.Count) return false;
        var value = _jobAttributeGrid.Rows[rowIndex].Cells["ID"].Value;
        return value != null && int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out seriesId);
    }

    private static bool TryGetJobAttributeColumnId(string? columnName, out int rowId)
    {
        rowId = -1;
        var definition = GetJobAttributeDefinitionByName(columnName);
        if (definition == null) return false;
        rowId = definition.RowId;
        return true;
    }

    private void RefreshJobAttributeCellAfterEdit(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 0 ||
            rowIndex >= _jobAttributeGrid.Rows.Count ||
            columnIndex >= _jobAttributeGrid.Columns.Count ||
            !TryGetJobAttributeSeriesId(rowIndex, out var seriesId) ||
            !TryGetJobAttributeColumnId(_jobAttributeGrid.Columns[columnIndex].DataPropertyName, out var attributeRowId))
        {
            return;
        }

        var definition = GetJobAttributeDefinition(attributeRowId);
        var cell = _jobAttributeGrid.Rows[rowIndex].Cells[columnIndex];
        if (!int.TryParse(Convert.ToString(cell.Value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return;
        WriteJobAttributeRawValue(attributeRowId, seriesId, value);
        if (definition.EditorKind != JobAttributeEditorKind.Combo) return;
        EnsureJobAttributeComboCellContainsValue(rowIndex, columnIndex, attributeRowId, value);
    }

    private void SaveJobMatrixEditor()
    {
        if (_project == null || _jobRestraintRead == null || _jobAttributeRead == null) return;

        _jobRestraintGrid.EndEdit();
        _jobAttributeGrid.EndEdit();
        if (_jobRestraintRead.Data.GetChanges() == null && _jobAttributeRead.Data.GetChanges() == null)
        {
            MessageBox.Show(this, "兵种相克/属性矩阵没有检测到改动。", "无需保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var preview = BuildJobMatrixChangePreview(maxItems: 40);
        if (MessageBox.Show(this,
                $"即将保存兵种相克/属性矩阵到当前 MOD 项目。\r\n\r\n变更预览：\r\n{preview}\r\n\r\n保存前会自动备份 Ekd5.exe，保存后会重新读取同一偏移校验。是否继续？",
                "确认保存兵种矩阵",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var saves = new List<TableSaveResult>();
            if (SaveChangedTableAndVerify(_jobRestraintRead) is { } restraintSave) saves.Add(restraintSave);
            if (SaveChangedTableAndVerify(_jobAttributeRead) is { } attributeSave) saves.Add(attributeSave);
            _currentJobAttributeEditorData?.AcceptChanges();
            RefreshJobMatrixRowStyles(_jobRestraintGrid);
            RefreshJobMatrixRowStyles(_jobAttributeGrid);
            ShowSelectedJobMatrixCell(
                _jobAttributeGrid.Focused || _jobAttributeGrid.ContainsFocus ? _jobAttributeGrid : _jobRestraintGrid,
                _jobAttributeGrid.Focused || _jobAttributeGrid.ContainsFocus ? "兵种属性" : "兵种相克");
            var changedBytes = saves.Sum(x => x.ChangedBytes);
            System.Diagnostics.Debug.WriteLine($"已保存兵种相克/属性矩阵：保存表 {saves.Count} 个，变化字节 {changedBytes}");
            foreach (var savedTable in saves) System.Diagnostics.Debug.WriteLine("兵种矩阵备份：" + savedTable.BackupPath);
            SetStatus($"兵种矩阵保存完成并已复读：变化 {changedBytes} 字节");
            MessageBox.Show(this,
                $"保存完成并已重新读取校验。\r\n保存表数量：{saves.Count}\r\n变化字节：{changedBytes}\r\n备份：{string.Join("; ", saves.Select(x => x.BackupPath))}",
                "保存完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("保存兵种矩阵失败：" + ex);
            MessageBox.Show(this, ex.Message, "保存兵种矩阵失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private string BuildJobMatrixChangePreview(int maxItems)
    {
        var lines = new List<string>();
        var total = 0;
        AppendJobMatrixChangePreview(_jobRestraintRead, "兵种相克", maxItems, lines, ref total);
        AppendJobMatrixChangePreview(_jobAttributeRead, "兵种属性", maxItems, lines, ref total);
        if (total == 0) return "未发现单元格变更。";
        if (total > lines.Count) lines.Add($"……另有 {total - lines.Count} 项变更未显示。");
        return string.Join("\r\n", lines);
    }

    private void AppendJobMatrixChangePreview(
        TableReadResult? read,
        string matrixName,
        int maxItems,
        List<string> lines,
        ref int total)
    {
        if (read == null) return;
        foreach (DataRow row in read.Data.Rows)
        {
            if (row.RowState != DataRowState.Modified) continue;
            var rowId = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
            foreach (DataColumn column in read.Data.Columns)
            {
                if (!int.TryParse(column.ColumnName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var columnId)) continue;
                var original = row[column, DataRowVersion.Original];
                var current = row[column, DataRowVersion.Current];
                var originalText = Convert.ToString(original, CultureInfo.InvariantCulture) ?? string.Empty;
                var currentText = Convert.ToString(current, CultureInfo.InvariantCulture) ?? string.Empty;
                if (originalText == currentText) continue;

                total++;
                if (lines.Count >= maxItems) continue;
                var offset = HexDisplayFormatter.FormatOffset(read.Table.DataPos + ((long)rowId * read.Table.RowSize) + columnId);
                var columnLabel = BuildJobSeriesColumnLabel(columnId);
                var rowLabel = ReferenceEquals(read, _jobAttributeRead)
                    ? BuildJobAttributeRowLabel(rowId)
                    : BuildJobSeriesColumnLabel(rowId);
                if (ReferenceEquals(read, _jobAttributeRead) &&
                    int.TryParse(originalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var originalValue) &&
                    int.TryParse(currentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var currentValue))
                {
                    originalText = FormatJobAttributeValue(rowId, originalValue);
                    currentText = FormatJobAttributeValue(rowId, currentValue);
                }

                lines.Add($"{matrixName} {rowLabel}[{columnLabel}] 0x{offset} {originalText} -> {currentText}");
            }
        }
    }

    private bool TryBuildJobMatrixCellOffsetText(DataGridView grid, DataGridViewRow row, string columnName, out string offsetText)
    {
        offsetText = string.Empty;
        var read = ReferenceEquals(grid, _jobAttributeGrid) ? _jobAttributeRead : _jobRestraintRead;
        if (read == null) return false;
        if (ReferenceEquals(grid, _jobAttributeGrid))
        {
            if (!TryGetJobAttributeColumnId(columnName, out var attributeRowId)) return false;
            var jobSeriesId = Convert.ToInt32(row.Cells["ID"].Value, CultureInfo.InvariantCulture);
            offsetText = "0x" + HexDisplayFormatter.FormatOffset(read.Table.DataPos + ((long)attributeRowId * read.Table.RowSize) + jobSeriesId);
            return true;
        }

        if (!int.TryParse(columnName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var columnId)) return false;
        var rowId = Convert.ToInt32(row.Cells["ID"].Value, CultureInfo.InvariantCulture);
        offsetText = "0x" + HexDisplayFormatter.FormatOffset(read.Table.DataPos + ((long)rowId * read.Table.RowSize) + columnId);
        return true;
    }

    private string BuildJobSeriesColumnLabel(int id)
    {
        var name = BuildJobSeriesName(id);
        return string.IsNullOrWhiteSpace(name)
            ? $"{id:00}:未命名"
            : $"{id:00}:{name}";
    }

    private string BuildJobSeriesName(int id)
    {
        var row = _jobSeriesRead == null ? null : TryFindRowById(_jobSeriesRead.Data, id);
        if (row == null && _jobSeriesRead != null && id >= 0 && id < _jobSeriesRead.Data.Rows.Count)
        {
            row = _jobSeriesRead.Data.Rows[id];
        }

        return row == null
            ? string.Empty
            : Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string BuildJobAttributeRowLabel(int rowId)
        => rowId switch
        {
            0 => "移动声音",
            1 => "移动速度",
            2 => "攻击声音",
            3 => "远程兵种",
            4 => "攻击延迟",
            5 => "兵种类型",
            6 => "策略伤害",
            7 => "参与围攻",
            _ => $"属性{rowId}"
        };

    private static string BuildJobAttributeValueRule(int rowId)
        => GetJobAttributeDefinition(rowId).ValueRule;

    private static string FormatJobAttributeValue(int rowId, int value)
    {
        var definition = GetJobAttributeDefinition(rowId);
        if (definition.EditorKind == JobAttributeEditorKind.Numeric)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        return definition.Choices.FirstOrDefault(choice => choice.Value == value)?.DisplayText ??
               new JobAttributeValueChoice(value, "自定义").DisplayText;
    }

    private static bool TryParseJobAttributeDisplay(int rowId, string text, out int value)
    {
        value = 0;
        text = text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return false;

        var definition = GetJobAttributeDefinition(rowId);
        foreach (var choice in definition.Choices)
        {
            if (text.Equals(choice.DisplayText, StringComparison.Ordinal) ||
                text.Equals(choice.Label, StringComparison.Ordinal))
            {
                value = choice.Value;
                return true;
            }
        }

        var separatorIndex = text.LastIndexOf('：');
        if (separatorIndex < 0) separatorIndex = text.LastIndexOf(':');
        if (separatorIndex >= 0 && separatorIndex < text.Length - 1)
        {
            text = text[(separatorIndex + 1)..].Trim();
        }

        return TryParseIntegerInput(text, out var parsed) &&
               TryConvertParsedIntegerToSigned(parsed, byte.MinValue, byte.MaxValue, out var parsedValue) &&
               parsedValue is >= byte.MinValue and <= byte.MaxValue &&
               (value = (int)parsedValue) >= byte.MinValue;
    }

    private static JobAttributeDefinition GetJobAttributeDefinition(int rowId)
        => JobAttributeDefinitions.TryGetValue(rowId, out var definition)
            ? definition
            : new JobAttributeDefinition(rowId, $"属性{rowId}", JobAttributeEditorKind.Numeric, "值口径：原始单字节。", []);

    private static readonly IReadOnlyDictionary<int, JobAttributeDefinition> JobAttributeDefinitions =
        new Dictionary<int, JobAttributeDefinition>
        {
            [0] = new(0, "移动声音", JobAttributeEditorKind.Combo, "值口径：马蹄声：0，车轮声：1，脚步声：2，无声：3。", [new(0, "马蹄声"), new(1, "车轮声"), new(2, "脚步声"), new(3, "无声")]),
            [1] = new(1, "移动速度", JobAttributeEditorKind.Combo, "值口径：速度档位0：0，速度档位1：1。中性档位名，具体语义待进一步验证。", [new(0, "速度档位0"), new(1, "速度档位1")]),
            [2] = new(2, "攻击声音", JobAttributeEditorKind.Combo, "值口径：攻击音效0：0，攻击音效1：1。中性档位名，具体语义待进一步验证。", [new(0, "攻击音效0"), new(1, "攻击音效1")]),
            [3] = new(3, "远程兵种", JobAttributeEditorKind.Combo, "值口径：否：0，是：1。", [new(0, "否"), new(1, "是")]),
            [4] = new(4, "攻击延迟", JobAttributeEditorKind.Combo, "值口径：无延迟：0，有延迟：1。", [new(0, "无延迟"), new(1, "有延迟")]),
            [5] = new(5, "兵种类型", JobAttributeEditorKind.Combo, "值口径：类型0：0，类型1：1，类型2：2。中性类型名，具体语义待进一步验证。", [new(0, "类型0"), new(1, "类型1"), new(2, "类型2")]),
            [6] = new(6, "策略伤害", JobAttributeEditorKind.Numeric, "值口径：百分比式单字节，常见值 90/100/110/120/125/130；本行使用数值输入。", []),
            [7] = new(7, "参与围攻", JobAttributeEditorKind.Combo, "值口径：不参与：0，参与：1；当前 6.5 基底默认全 1。", [new(0, "不参与"), new(1, "参与")])
        };

    private sealed record JobAttributeDefinition(int RowId, string Name, JobAttributeEditorKind EditorKind, string ValueRule, IReadOnlyList<JobAttributeValueChoice> Choices);

    private sealed record JobAttributeValueChoice(int Value, string Label)
    {
        public string DisplayText => $"{Label}：{Value}";
    }

    private enum JobAttributeEditorKind
    {
        Combo,
        Numeric
    }

    private string BuildJobAttributeValueRuleForCurrentRow()
    {
        if (_jobAttributeGrid.CurrentCell == null || _jobAttributeGrid.CurrentCell.ColumnIndex < 0) return "值口径：原始单字节。";
        if (!TryGetJobAttributeColumnId(_jobAttributeGrid.Columns[_jobAttributeGrid.CurrentCell.ColumnIndex].DataPropertyName, out var id))
        {
            return "值口径：原始单字节。";
        }

        return BuildJobAttributeValueRule(id);
    }

    private void LoadJobEffectEditor()
    {
        if (_project == null) return;
        if (_tables.Count == 0)
        {
            ReloadCurrentProject();
            if (_tables.Count == 0) return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            _currentJobEffectData = BuildJobEffectEditorData(_project, _tables);
            _jobEffectEditorGrid.DataSource = _currentJobEffectData;
            ConfigureJobEffectGrid();
            _saveJobEffectEditorButton.Enabled = true;
            _jobEffectEditorInfoBox.Text = BuildJobEffectSummary(_currentJobEffectData);
            SetStatus($"兵种特效读取完成：{_currentJobEffectData.Rows.Count} 行");
        }
        catch (Exception ex)
        {
            _jobEffectEditorInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("读取兵种特效失败：" + ex);
            MessageBox.Show(this, ex.Message, "读取兵种特效失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private DataTable BuildJobEffectEditorData(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        _jobEffectNameTable = FindTable(tables, "6.5-7 兵种特效");
        _jobEffectNames = ReadJobEffectNames(project, _jobEffectNameTable);
        _jobEffectDescriptionRead = _tableReader.Read(project, FindTable(tables, "6.5-7-1 兵种特效说明"), tables);
        _jobEffectAssignmentRead = _tableReader.Read(project, FindTable(tables, "6.5-7-2 兵种特效分配"), tables);
        _jobEffectPersonNames = BuildIdNameLookup(project, tables, "6.5-0 人物");
        _jobEffectJobNames = BuildIdNameLookup(project, tables, "6.5-4 详细兵种");
        if (!_jobEffectDescriptionRead.Validation.IsUsable || !_jobEffectAssignmentRead.Validation.IsUsable)
        {
            throw new InvalidOperationException("兵种特效说明/分配表有不可读取项，请先查看数据表诊断。");
        }

        var output = new DataTable("兵种特效");
        output.Columns.Add("ID", typeof(int));
        output.Columns.Add("名称", typeof(string));
        output.Columns.Add("介绍", typeof(string));
        output.Columns.Add("1号武将", typeof(int));
        output.Columns.Add("1号武将名", typeof(string));
        output.Columns.Add("2号武将", typeof(int));
        output.Columns.Add("2号武将名", typeof(string));
        output.Columns.Add("3号武将", typeof(int));
        output.Columns.Add("3号武将名", typeof(string));
        output.Columns.Add("兵种", typeof(int));
        output.Columns.Add("兵种名称", typeof(string));
        output.Columns.Add("特效值", typeof(int));

        var count = Math.Min(_jobEffectDescriptionRead.Data.Rows.Count, _jobEffectAssignmentRead.Data.Rows.Count);
        for (var i = 0; i < count; i++)
        {
            var id = Convert.ToInt32(_jobEffectAssignmentRead.Data.Rows[i]["ID"], CultureInfo.InvariantCulture);
            var row = output.NewRow();
            row["ID"] = id;
            row["名称"] = BuildJobEffectName(id);
            row["介绍"] = Convert.ToString(_jobEffectDescriptionRead.Data.Rows[i]["介绍"], CultureInfo.InvariantCulture) ?? string.Empty;
            row["1号武将"] = Convert.ToInt32(_jobEffectAssignmentRead.Data.Rows[i]["1号武将"], CultureInfo.InvariantCulture);
            row["2号武将"] = Convert.ToInt32(_jobEffectAssignmentRead.Data.Rows[i]["2号武将"], CultureInfo.InvariantCulture);
            row["3号武将"] = Convert.ToInt32(_jobEffectAssignmentRead.Data.Rows[i]["3号武将"], CultureInfo.InvariantCulture);
            row["兵种"] = Convert.ToInt32(_jobEffectAssignmentRead.Data.Rows[i]["兵种"], CultureInfo.InvariantCulture);
            row["特效值"] = Convert.ToInt32(_jobEffectAssignmentRead.Data.Rows[i]["特效值"], CultureInfo.InvariantCulture);
            RefreshJobEffectDerivedCells(row);
            output.Rows.Add(row);
        }

        output.AcceptChanges();
        output.Columns["ID"]!.ReadOnly = true;
        output.Columns["名称"]!.ReadOnly = true;
        output.Columns["1号武将名"]!.ReadOnly = true;
        output.Columns["2号武将名"]!.ReadOnly = true;
        output.Columns["3号武将名"]!.ReadOnly = true;
        output.Columns["兵种名称"]!.ReadOnly = true;
        return output;
    }

    private IReadOnlyDictionary<int, string> ReadJobEffectNames(CczProject project, HexTableDefinition table)
        => new JobEffectNameReader().ReadNames(project, table);

    private IReadOnlyDictionary<int, string> BuildIdNameLookup(CczProject project, IReadOnlyList<HexTableDefinition> tables, string tableName)
    {
        var table = FindTable(tables, tableName);
        var read = _tableReader.Read(project, table, tables);
        if (!read.Validation.IsUsable || !read.Data.Columns.Contains("名称")) return new Dictionary<int, string>();
        return read.Data.Rows.Cast<DataRow>()
            .Where(row => row.Table.Columns.Contains("ID"))
            .ToDictionary(
                row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture),
                row => Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty);
    }

    private string BuildJobEffectName(int id)
        => _jobEffectNames.TryGetValue(id, out var name) ? name : $"#{id}";

    private string BuildJobEffectPersonName(int id)
    {
        if (id >= 1024) return "无/不限";
        return _jobEffectPersonNames.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : $"{id}：未找到人物名";
    }

    private string BuildJobEffectJobName(int id)
    {
        if (id >= 255) return "任意/无";
        return _jobEffectJobNames.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : $"{id}：未找到兵种名";
    }

    private void RefreshJobEffectDerivedCells(DataRow row)
    {
        row["1号武将名"] = BuildJobEffectPersonName(Convert.ToInt32(row["1号武将"], CultureInfo.InvariantCulture));
        row["2号武将名"] = BuildJobEffectPersonName(Convert.ToInt32(row["2号武将"], CultureInfo.InvariantCulture));
        row["3号武将名"] = BuildJobEffectPersonName(Convert.ToInt32(row["3号武将"], CultureInfo.InvariantCulture));
        row["兵种名称"] = BuildJobEffectJobName(Convert.ToInt32(row["兵种"], CultureInfo.InvariantCulture));
    }

    private void UpdateJobEffectDerivedCells(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 0) return;
        if (_jobEffectEditorGrid.Columns[columnIndex].DataPropertyName is not ("1号武将" or "2号武将" or "3号武将" or "兵种")) return;
        var row = TryGetDataRow(_jobEffectEditorGrid.Rows[rowIndex]);
        if (row == null) return;
        RefreshJobEffectDerivedCells(row);
    }

    private void ConfigureJobEffectGrid()
    {
        if (_currentJobEffectData == null) return;
        _jobEffectEditorGrid.ReadOnly = false;
        foreach (DataGridViewColumn column in _jobEffectEditorGrid.Columns)
        {
            column.ReadOnly = column.DataPropertyName is "ID" or "名称" or "1号武将名" or "2号武将名" or "3号武将名" or "兵种名称";
            column.ToolTipText = BuildJobEffectColumnAnnotation(column.DataPropertyName);
            column.HeaderText = BuildJobEffectColumnHeader(column.DataPropertyName);
            if (column.DataPropertyName == "介绍") column.Width = 260;
            if (column.ReadOnly) column.DefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);
        }

        RefreshJobEffectRowStyles();
    }

    private string BuildJobEffectColumnHeader(string columnName)
    {
        return columnName switch
        {
            "ID" => "ID\n特效编号",
            "名称" => "名称\n只读",
            "介绍" => "介绍\n200B",
            "1号武将" or "2号武将" or "3号武将" => columnName + "\n人物ID",
            "兵种" => "兵种\n兵种ID",
            "特效值" => "特效值\n1B",
            _ => columnName
        };
    }

    private string BuildJobEffectColumnAnnotation(string columnName)
    {
        if (columnName == "ID") return "兵种特效编号。说明表、分配表和专属/套装表按这个编号逐行对齐。";
        if (columnName == "名称") return "特效名称来自 6.5-7 兵种特效原始名称区。该表在 HexTable.xml 中标记 Bytes=0，行尾可能含额外参数，本页暂不开放改名，避免破坏未拆分字节。";
        if (columnName == "介绍") return "兵种特效说明文本，写入 Imsg.e5；固定 200 字节 GBK 容量。";
        if (columnName is "1号武将" or "2号武将" or "3号武将")
        {
            return columnName + "：写入 6.5-7-2 兵种特效分配。0..1023 通常对应人物 ID，1024 在当前 6.5 数据中常见为无/不限。";
        }
        if (columnName.EndsWith("武将名", StringComparison.Ordinal)) return "根据人物表自动解析出的武将名称，只读显示，便于核对分配对象。";
        if (columnName == "兵种") return "兵种限制，写入 6.5-7-2 兵种特效分配。0..79 通常对应详细兵种，255 在当前 6.5 数据中常见为任意/无。";
        if (columnName == "兵种名称") return "根据详细兵种表自动解析出的兵种名称，只读显示。";
        if (columnName == "特效值") return "特效参数值，写入 6.5-7-2 兵种特效分配。具体含义随特效而变，修改后应结合实机验证。";
        return columnName;
    }

    private static string BuildJobEffectSummary(DataTable data)
    {
        var named = data.Rows.Cast<DataRow>().Count(row => !string.IsNullOrWhiteSpace(Convert.ToString(row["名称"], CultureInfo.InvariantCulture)) && !Convert.ToString(row["名称"], CultureInfo.InvariantCulture)!.StartsWith("#", StringComparison.Ordinal));
        return
            $"兵种特效已读取：总行 {data.Rows.Count}，可识别名称 {named}。\r\n" +
            "来源表：6.5-7 兵种特效（名称只读显示）、6.5-7-1 兵种特效说明、6.5-7-2 兵种特效分配。\r\n" +
            "保存会写回 Imsg.e5 与 Ekd5.exe，保存前自动备份，保存后重新读取校验。";
    }

    private void ApplyJobEffectFilter()
    {
        if (_currentJobEffectData == null) return;
        var keyword = _jobEffectEditorSearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            _currentJobEffectData.DefaultView.RowFilter = string.Empty;
            SetStatus("兵种特效筛选已清除");
            return;
        }

        var escaped = EscapeDataViewLikeValue(keyword);
        var filters = _currentJobEffectData.Columns
            .Cast<DataColumn>()
            .Select(column => $"CONVERT([{column.ColumnName}], 'System.String') LIKE '*{escaped}*'");
        _currentJobEffectData.DefaultView.RowFilter = string.Join(" OR ", filters);
        SetStatus($"兵种特效筛选：{_currentJobEffectData.DefaultView.Count}/{_currentJobEffectData.Rows.Count}");
    }

    private void ClearJobEffectFilter()
    {
        _jobEffectEditorSearchBox.Clear();
        if (_currentJobEffectData != null) _currentJobEffectData.DefaultView.RowFilter = string.Empty;
        SetStatus("兵种特效筛选已清除");
    }

    private void RefreshJobEffectRowStyles()
    {
        foreach (DataGridViewRow row in _jobEffectEditorGrid.Rows)
        {
            RefreshJobEffectRowStyle(row.Index);
        }
    }

    private void RefreshJobEffectRowStyle(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _jobEffectEditorGrid.Rows.Count) return;
        var dataRow = TryGetDataRow(_jobEffectEditorGrid.Rows[rowIndex]);
        if (dataRow == null) return;
        _jobEffectEditorGrid.Rows[rowIndex].DefaultCellStyle.BackColor = IsDataRowChanged(dataRow) ? Color.LightCyan : Color.Empty;
    }

    private void RefreshJobEffectCellsAfterEdit(IReadOnlyList<GridCellKey> changedCells)
    {
        RefreshChangedGridCells(_jobEffectEditorGrid, changedCells, UpdateJobEffectDerivedCells);
        RefreshChangedGridRowsOnly(_jobEffectEditorGrid, changedCells, RefreshJobEffectRowStyle);
        ShowSelectedJobEffectCell();
    }

    private void ShowSelectedJobEffectCell()
    {
        if (_currentJobEffectData == null || _jobEffectEditorGrid.CurrentCell == null) return;
        var cell = _jobEffectEditorGrid.CurrentCell;
        var columnName = _jobEffectEditorGrid.Columns[cell.ColumnIndex].DataPropertyName;
        var row = _jobEffectEditorGrid.Rows[cell.RowIndex];
        var id = row.Cells["ID"].Value;
        var name = row.Cells["名称"].Value;
        var value = cell.Value;
        var extra = columnName == "介绍"
            ? $"\r\nGBK 字节：{EncodingService.GetGbkByteCount(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)}/200"
            : string.Empty;
        _jobEffectEditorInfoBox.Text =
            $"兵种特效：ID={id}    名称={name}\r\n" +
            $"字段：{columnName}    当前值：{value}{extra}\r\n\r\n" +
            BuildJobEffectColumnAnnotation(columnName);
    }

    private void ValidateJobEffectCell(DataGridViewCellValidatingEventArgs e)
    {
        if (_jobEffectEditorGrid.ReadOnly || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var column = _jobEffectEditorGrid.Columns[e.ColumnIndex];
        if (column.ReadOnly) return;
        var columnName = column.DataPropertyName;
        var value = Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture) ?? string.Empty;
        string? error = null;
        if (columnName == "介绍")
        {
            var bytes = EncodingService.GetGbkByteCount(value);
            if (bytes > 200) error = $"兵种特效说明超长：GBK {bytes} 字节，容量 200 字节。";
        }
        else if (columnName is "1号武将" or "2号武将" or "3号武将")
        {
            error = TryParseInteger(value, 0, ushort.MaxValue, columnName, _currentPageHexButton.Checked);
        }
        else if (columnName is "兵种" or "特效值")
        {
            error = TryParseInteger(value, 0, byte.MaxValue, columnName, _currentPageHexButton.Checked);
        }

        _jobEffectEditorGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = error ?? string.Empty;
        if (error == null) return;
        e.Cancel = true;
        _jobEffectEditorInfoBox.Text = error;
        SetStatus(error);
    }

    private void SaveJobEffectEditor()
    {
        if (_project == null || _currentJobEffectData == null || _jobEffectDescriptionRead == null || _jobEffectAssignmentRead == null) return;

        _jobEffectEditorGrid.EndEdit();
        if (_currentJobEffectData.GetChanges() == null)
        {
            MessageBox.Show(this, "兵种特效没有检测到改动。", "无需保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var preview = BuildChangePreview(_currentJobEffectData, maxItems: 60);
        if (MessageBox.Show(this,
                $"即将保存兵种特效说明/分配到当前 MOD 项目。\r\n\r\n变更预览：\r\n{preview}\r\n\r\n保存前会自动备份 Imsg.e5/Ekd5.exe，保存后会重新读取校验。是否继续？",
                "确认保存兵种特效",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var changedCells = GetChangedCellKeys(_currentJobEffectData);
            var saves = SaveJobEffectEditorData(_project, _currentJobEffectData);
            AcceptSavedDataTable(_currentJobEffectData);
            RefreshJobEffectCellsAfterEdit(changedCells);
            var changedBytes = saves.Sum(x => x.ChangedBytes);
            System.Diagnostics.Debug.WriteLine($"已保存兵种特效：保存表 {saves.Count} 个，变化字节 {changedBytes}");
            foreach (var save in saves) System.Diagnostics.Debug.WriteLine("兵种特效备份：" + save.BackupPath);
            SetStatus($"兵种特效保存完成并已复读：变化 {changedBytes} 字节");
            MessageBox.Show(this,
                $"保存完成并已重新读取校验。\r\n保存表数量：{saves.Count}\r\n变化字节：{changedBytes}\r\n备份：{string.Join("; ", saves.Select(x => x.BackupPath))}",
                "保存完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("保存兵种特效失败：" + ex);
            MessageBox.Show(this, ex.Message, "保存兵种特效失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private IReadOnlyList<TableSaveResult> SaveJobEffectEditorData(CczProject project, DataTable effectData)
    {
        if (_jobEffectDescriptionRead == null || _jobEffectAssignmentRead == null) return Array.Empty<TableSaveResult>();
        foreach (DataRow effectRow in effectData.Rows)
        {
            if (effectRow.RowState != DataRowState.Modified) continue;
            var id = Convert.ToInt32(effectRow["ID"], CultureInfo.InvariantCulture);
            var descriptionRow = FindRowById(_jobEffectDescriptionRead.Data, id);
            var assignmentRow = FindRowById(_jobEffectAssignmentRead.Data, id);

            if (IsRoleColumnChanged(effectRow, "介绍")) descriptionRow["介绍"] = effectRow["介绍", DataRowVersion.Current];
            foreach (var columnName in new[] { "1号武将", "2号武将", "3号武将", "兵种", "特效值" })
            {
                if (IsRoleColumnChanged(effectRow, columnName)) assignmentRow[columnName] = effectRow[columnName, DataRowVersion.Current];
            }
        }

        var saves = new List<TableSaveResult>();
        if (SaveChangedTableAndVerify(_jobEffectDescriptionRead) is { } descriptionSave) saves.Add(descriptionSave);
        if (SaveChangedTableAndVerify(_jobEffectAssignmentRead) is { } assignmentSave) saves.Add(assignmentSave);
        return saves;
    }
}
