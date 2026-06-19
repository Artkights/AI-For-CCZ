using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
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
            _saveRoleEditorButton.Enabled = true;
            _importRoleFaceButton.Enabled = true;
            _batchImportRoleFaceButton.Enabled = true;
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
        output.Columns.Add("R形象编号", typeof(int));
        output.Columns.Add("S形象编号", typeof(int));
        output.Columns.Add("R资源状态", typeof(string));
        output.Columns.Add("S资源状态", typeof(string));

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
            row["R形象编号"] = rId;
            row["S形象编号"] = sId;
            row["R资源状态"] = ImageAssignmentService.GetImageResourceStatus(project, "R", rId);
            row["S资源状态"] = ImageAssignmentService.GetImageResourceStatus(project, "S", sId);
            output.Rows.Add(row);
        }

        output.AcceptChanges();
        foreach (DataColumn column in output.Columns)
        {
            column.ReadOnly = column.ColumnName is "ID";
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
        var hiddenColumns = new HashSet<string>(StringComparer.Ordinal)
        {
            "Army",
            "级别",
            "经验",
            "职业名称",
            "头像说明",
            "R资源状态",
            "S资源状态"
        };
        foreach (DataGridViewColumn column in _roleEditorGrid.Columns)
        {
            column.ReadOnly = column.DataPropertyName is "ID" or "职业名称" or "头像说明" or "R资源状态" or "S资源状态";
            column.Visible = !hiddenColumns.Contains(column.DataPropertyName);
            column.ToolTipText = BuildRoleColumnAnnotation(column.DataPropertyName);
            column.HeaderText = column.DataPropertyName switch
            {
                "职业" => "职业\n详细兵种",
                "职业名称" => "职业名称\n引用",
                "头像说明" => "头像说明\n资源",
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

    private string BuildRoleColumnHeader(string columnName)
    {
        if (columnName == "ID") return "ID\n行号";
        if (columnName is "职业名称" or "头像说明") return columnName;
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

    private static string BuildRoleEditorSummary(DataTable data)
    {
        var named = data.Rows.Cast<DataRow>().Count(row => !string.IsNullOrWhiteSpace(Convert.ToString(row["名称"], CultureInfo.InvariantCulture)));
        var missingR = data.Rows.Cast<DataRow>().Count(row => Convert.ToString(row["R资源状态"], CultureInfo.InvariantCulture)?.StartsWith("缺失", StringComparison.Ordinal) == true);
        var missingS = data.Rows.Cast<DataRow>().Count(row => Convert.ToString(row["S资源状态"], CultureInfo.InvariantCulture)?.StartsWith("缺失", StringComparison.Ordinal) == true);
        return
            $"角色设定已读取：总行 {data.Rows.Count}，有名称 {named}。\r\n" +
            $"R 资源缺失 {missingR}，S 资源缺失 {missingS}。\r\n" +
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
        var searchableColumns = new[] { "ID", "名称", "职业", "职业名称", "头像", "头像说明", "R形象编号", "S形象编号", "R资源状态", "S资源状态" }
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

        _roleBiographyBox.Text = Convert.ToString(bioRow?["介绍"], CultureInfo.InvariantCulture) ?? string.Empty;
        ShowCriticalQuoteEditor(criticalMapping);
        _roleRetreatQuoteBox.Text = Convert.ToString(retreatMapping.QuoteRow?["介绍"], CultureInfo.InvariantCulture) ?? string.Empty;

        var canSaveAny = bioRow != null || criticalMapping.QuoteRows.Count > 0 || retreatMapping.QuoteRow != null;
        _saveRoleTextDetailButton.Enabled = canSaveAny;

        var criticalByteHint = BuildCriticalQuoteByteHint(criticalMapping);
        var retreatHint = retreatMapping.QuoteRow == null
            ? retreatMapping.Explanation
            : $"{retreatMapping.Explanation} GBK {EncodingService.GetGbkByteCount(_roleRetreatQuoteBox.Text)}/200 字节。";
        _roleTextDetailInfoBox.Text =
            $"人物列传：人物 ID={roleId}，GBK {EncodingService.GetGbkByteCount(_roleBiographyBox.Text)}/200 字节。\r\n" +
            $"{criticalMapping.Explanation} {criticalByteHint}\r\n" +
            $"{retreatHint}\r\n" +
            "保存会写回 Imsg.e5，保存前自动备份，保存后复读校验。";
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
        var criticalMapping = _roleQuoteMappingService.ResolveCriticalQuote(_project, roleRow, _roleCriticalQuoteRead.Data);
        var retreatMapping = _roleQuoteMappingService.ResolveRetreatQuote(roleRow, _roleRetreatQuoteRead.Data);

        bioRow["介绍"] = _roleBiographyBox.Text;
        ApplyCriticalQuoteEditorToRows(criticalMapping);

        if (retreatMapping.QuoteRow != null)
        {
            retreatMapping.QuoteRow["介绍"] = _roleRetreatQuoteBox.Text;
        }

        if (_roleBiographyRead.Data.GetChanges() == null &&
            _roleCriticalQuoteRead.Data.GetChanges() == null &&
            _roleRetreatQuoteRead.Data.GetChanges() == null)
        {
            MessageBox.Show(this, "当前角色列传/台词没有检测到改动。", "无需保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var preview =
            $"角色：{roleId} {roleName}\r\n" +
            $"列传 GBK：{EncodingService.GetGbkByteCount(_roleBiographyBox.Text)}/200\r\n" +
            $"暴击台词：{string.Join(", ", criticalMapping.QuoteIds.Select(id => "#" + id.ToString(CultureInfo.InvariantCulture)))} {BuildCriticalQuoteByteHint(criticalMapping)}\r\n" +
            (retreatMapping.QuoteRow == null
                ? "撤退台词：未找到实际会使用的撤退台词行，本次不写回撤退台词。"
                : $"撤退台词 #{retreatMapping.QuoteId} GBK：{EncodingService.GetGbkByteCount(_roleRetreatQuoteBox.Text)}/200");
        if (MessageBox.Show(this,
                $"即将保存当前角色列传/台词到 Imsg.e5。\r\n\r\n{preview}\r\n\r\n保存前会自动备份，保存后会重新读取校验。是否继续？",
                "确认保存角色文本",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var saves = new List<TableSaveResult>();
            if (_roleBiographyRead.Data.GetChanges() != null) saves.Add(_tableWriter.Save(_project, _roleBiographyRead.Table, _roleBiographyRead.Data));
            if (_roleCriticalQuoteRead.Data.GetChanges() != null) saves.Add(_tableWriter.Save(_project, _roleCriticalQuoteRead.Table, _roleCriticalQuoteRead.Data));
            if (_roleRetreatQuoteRead.Data.GetChanges() != null) saves.Add(_tableWriter.Save(_project, _roleRetreatQuoteRead.Table, _roleRetreatQuoteRead.Data));

            LoadRoleTextTables();
            ShowRoleTextDetails(roleRow);
            var changedBytes = saves.Sum(x => x.ChangedBytes);
            System.Diagnostics.Debug.WriteLine($"已保存角色文本：{roleId} {roleName}，保存表 {saves.Count} 个，变化字节 {changedBytes}");
            foreach (var save in saves) System.Diagnostics.Debug.WriteLine("角色文本备份：" + save.BackupPath);
            SetStatus($"角色文本保存完成并已复读：变化 {changedBytes} 字节");
            MessageBox.Show(this,
                $"保存完成并已重新读取校验。\r\n保存表数量：{saves.Count}\r\n变化字节：{changedBytes}\r\n备份：{string.Join("; ", saves.Select(x => x.BackupPath))}",
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
            var saves = SaveRoleEditorData(_project, _tables, _currentRoleEditorData);
            LoadRoleEditor();
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
        if (personRead.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, personTable, personRead.Data));
        if (rRead.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, rTable, rRead.Data));
        if (sRead.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, sTable, sRead.Data));
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
            Dock = DockStyle.Fill,
            AutoSize = true,
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
            data.Clear();
            foreach (DataRow row in reloaded.Rows)
            {
                data.ImportRow(row);
            }
            data.AcceptChanges();
            grid.DataSource = null;
            ConfigureExclusiveSetScenarioGrid(grid);
            grid.DataSource = data;
            RefreshExclusiveSetScenarioRowStyles(grid);
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
            Dock = DockStyle.Fill,
            AutoSize = true,
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
            var reloaded = BuildRolePersonalEffectEditorData(_project, _tables);
            data.Clear();
            foreach (DataRow row in reloaded.Rows)
            {
                data.ImportRow(row);
            }
            data.AcceptChanges();
            grid.DataSource = data;
            ConfigureRolePersonalEffectGrid(grid);
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
            saves.Add(_tableWriter.Save(project, _rolePersonalEffectRead.Table, _rolePersonalEffectRead.Data));
        }
        return saves;
    }

    private void LoadJobEditor()
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
            _currentJobEditorData = BuildJobEditorData(_project, _tables);
            _jobEditorGrid.DataSource = _currentJobEditorData;
            ConfigureJobEditorGrid();
            ResetJobEditorHistory();
            _saveJobEditorButton.Enabled = true;
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

        var output = new DataTable("兵种设定");
        output.Columns.Add("ID", typeof(int));
        output.Columns.Add("名称", typeof(string));
        foreach (DataColumn column in _jobGrowthRead.Data.Columns)
        {
            if (column.ColumnName is "ID" or "名称") continue;
            output.Columns.Add(column.ColumnName, column.DataType);
        }
        output.Columns.Add("穿透", typeof(int));
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
        foreach (DataGridViewColumn column in _jobEditorGrid.Columns)
        {
            column.ReadOnly = column.DataPropertyName == "ID";
            column.ToolTipText = BuildJobColumnAnnotation(column.DataPropertyName);
            column.HeaderText = column.DataPropertyName switch
            {
                "ID" => "ID\n兵种编号",
                "介绍" => "介绍\n兵种说明",
                _ => BuildJobColumnHeader(column.DataPropertyName)
            };
            if (column.DataPropertyName == "介绍") column.Width = 240;
        }

        RefreshJobEditorRowStyles();
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
        if (columnName == "攻击范围") return "兵种普通攻击距离/目标选择范围，写入 6.5-4-2 兵种成长；选择该字段会按字段值+1 从 E5\\Hitarea.e5 预览范围图。建议修改后进战场验证可选格。";
        if (columnName == "穿透") return "兵种普通攻击穿透模板，写入 6.5-4-3 兵种穿透；选择该字段会按字段值+1 从 E5\\Effarea.e5 预览穿透图。0 通常是不穿透，扩展值需实机验证。";
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

    private static string BuildJobEditorSummary(DataTable data)
    {
        var named = data.Rows.Cast<DataRow>().Count(row => !string.IsNullOrWhiteSpace(Convert.ToString(row["名称"], CultureInfo.InvariantCulture)));
        return
            $"兵种设定已读取：总行 {data.Rows.Count}，已命名 {named}。\r\n" +
            "来源表：6.5-4 详细兵种、6.5-4-1 兵种说明、6.5-4-2 兵种成长、6.5-4-3 兵种穿透。\r\n" +
            "保存会写回 Ekd5.exe、Data.e5、Imsg.e5，保存前自动备份，保存后重新读取校验。";
    }

    private void ApplyJobEditorFilter()
    {
        if (_currentJobEditorData == null) return;
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
            .Where(column => column.ColumnName is "ID" or "名称" or "介绍" or "移动力" or "攻击范围" or "攻击" or "防御" or "精神" or "爆发" or "士气" or "HP" or "MP" or "穿透")
            .Select(column => $"CONVERT([{column.ColumnName}], 'System.String') LIKE '*{escaped}*'");
        _currentJobEditorData.DefaultView.RowFilter = string.Join(" OR ", filters);
        SetStatus($"兵种筛选：{_currentJobEditorData.DefaultView.Count}/{_currentJobEditorData.Rows.Count}");
    }

    private void ClearJobEditorFilter()
    {
        _jobEditorSearchBox.Clear();
        if (_currentJobEditorData != null) _currentJobEditorData.DefaultView.RowFilter = string.Empty;
        SetStatus("兵种筛选已清除");
    }

    private void ExportJobEditorCsv()
        => ExportDataTableCsv(_currentJobEditorData, "兵种设定.csv");

    private void ImportJobEditorCsv()
        => ImportDataTableCsv(_currentJobEditorData, "兵种设定", () =>
        {
            RefreshJobEditorAfterBulkEdit();
            ResetJobEditorHistory();
        });

    private void RefreshJobEditorAfterBulkEdit()
    {
        RefreshJobEditorRowStyles();
        ShowSelectedJobEditorCell();
        UpdateJobEditorHistoryButtons();
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

    private void BeginJobEditorCellEdit(int rowIndex, int columnIndex)
    {
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
        RefreshJobEditorAfterBulkEdit();
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
            RefreshJobEditorAfterBulkEdit();
        }

        SetStatus($"兵种设定粘贴完成：更新 {edits.Count} 个单元格。");
    }

    private void FillJobEditorSelectionWithCurrentValue()
    {
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
        if (edits.Count > 0) RefreshJobEditorAfterBulkEdit();
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
            error = TryParseInteger(value, 0, byte.MaxValue, columnName, _currentPageHexButton.Checked) ?? string.Empty;
        }

        return string.IsNullOrEmpty(error);
    }

    private object? ConvertJobEditorValueForColumn(string columnName, string text)
    {
        if (_currentJobEditorData == null || !_currentJobEditorData.Columns.Contains(columnName)) return text;
        var dataColumn = _currentJobEditorData.Columns[columnName];
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

        RefreshJobEditorAfterBulkEdit();
    }

    private void ResetJobEditorHistory()
    {
        _jobEditorUndoStack.Clear();
        _jobEditorRedoStack.Clear();
        _jobEditorSelectionSnapshotTargets = [];
        _jobEditorPendingCellEditOriginals = [];
        _jobEditorSelectionChangeStartedByMouse = false;
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
        if (_currentJobEditorData == null || _jobEditorGrid.CurrentCell == null) return;
        var cell = _jobEditorGrid.CurrentCell;
        var columnName = _jobEditorGrid.Columns[cell.ColumnIndex].DataPropertyName;
        var row = _jobEditorGrid.Rows[cell.RowIndex];
        var id = row.Cells["ID"].Value;
        var name = row.Cells["名称"].Value;
        var value = cell.Value;
        var extra = columnName == "介绍"
            ? $"\r\nGBK 字节：{EncodingService.GetGbkByteCount(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)}/200"
            : string.Empty;
        _jobEditorInfoBox.Text =
            $"兵种：ID={id}    名称={name}\r\n" +
            $"字段：{columnName}    当前值：{value}{extra}\r\n\r\n" +
            BuildJobColumnAnnotation(columnName);
        UpdateJobAreaPreview(row, columnName);
    }

    private void UpdateJobAreaPreview(DataGridViewRow row, string columnName)
    {
        if (_project == null)
        {
            ClearJobAreaPreview("请先打开 MOD 项目。");
            return;
        }

        if (columnName is not ("攻击范围" or "穿透"))
        {
            ClearJobAreaPreview("选择“攻击范围”或“穿透”单元格可显示右侧范围图。");
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
        SetPictureBoxImage(_jobAreaPreviewBox, result.Bitmap);
        var id = Convert.ToString(row.Cells["ID"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        var name = Convert.ToString(row.Cells["名称"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        _jobAreaPreviewInfoBox.Text =
            $"兵种 ID={id}  名称={name}\r\n" +
            $"字段={columnName}  值={fieldValue}\r\n" +
            result.Message + "\r\n" +
            $"资源路径：{result.SourcePath}";
    }

    private void ClearJobAreaPreview(string message)
    {
        SetPictureBoxImage(_jobAreaPreviewBox, null);
        _jobAreaPreviewInfoBox.Text = message;
    }

    private void ValidateJobEditorCell(DataGridViewCellValidatingEventArgs e)
    {
        if (_jobEditorGrid.ReadOnly || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var column = _jobEditorGrid.Columns[e.ColumnIndex];
        if (column.ReadOnly) return;
        var columnName = column.DataPropertyName;
        var value = Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture) ?? string.Empty;
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
            error = TryParseInteger(value, 0, byte.MaxValue, columnName, _currentPageHexButton.Checked);
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
            var saves = SaveJobEditorData(_project, _currentJobEditorData);
            LoadJobEditor();
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
        if (_jobNameRead.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, _jobNameRead.Table, _jobNameRead.Data));
        if (_jobDescriptionRead.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, _jobDescriptionRead.Table, _jobDescriptionRead.Data));
        if (_jobGrowthRead.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, _jobGrowthRead.Table, _jobGrowthRead.Data));
        if (_jobPierceRead.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, _jobPierceRead.Table, _jobPierceRead.Data));
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

    private DataTable BuildItemEditorData(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        _itemBaseLowRead = _tableReader.Read(project, FindTable(tables, "6.5-1 物品（0-103）"), tables);
        _itemBaseHighRead = _tableReader.Read(project, FindTable(tables, "6.5-2 物品（104-255）"), tables);
        _itemDescriptionLowRead = _tableReader.Read(project, FindTable(tables, "6.5-1-1 物品说明（0-103）"), tables);
        _itemDescriptionHighRead = _tableReader.Read(project, FindTable(tables, "6.5-2-1 物品说明（104-255）"), tables);
        if (!_itemBaseLowRead.Validation.IsUsable || !_itemBaseHighRead.Validation.IsUsable ||
            !_itemDescriptionLowRead.Validation.IsUsable || !_itemDescriptionHighRead.Validation.IsUsable)
        {
            throw new InvalidOperationException("物品表或物品说明表有不可读取项，请先查看数据表诊断。");
        }

        _itemEffectNames = BuildItemEffectNameLookup(project, tables);

        var output = new DataTable("宝物设定");
        output.Columns.Add("ID", typeof(int));
        output.Columns.Add("分段", typeof(string));
        output.Columns.Add("大类", typeof(string));
        foreach (DataColumn column in _itemBaseLowRead.Data.Columns)
        {
            if (column.ColumnName == "ID") continue;
            output.Columns.Add(column.ColumnName, column.DataType);
        }
        output.Columns.Add("类型说明", typeof(string));
        output.Columns.Add("价格显示", typeof(string));
        output.Columns.Add("装备特效名", typeof(string));
        output.Columns.Add("实际效果号", typeof(string));
        output.Columns.Add("实际效果说明", typeof(string));
        output.Columns.Add("特效提示", typeof(string));
        output.Columns.Add("介绍", typeof(string));
        output.Columns.Add("来源文件", typeof(string));

        var boundary = ItemCategoryBoundaryService.Resolve(project);
        AddItemEditorRows(output, _itemBaseLowRead, _itemDescriptionLowRead, "0-103", boundary);
        AddItemEditorRows(output, _itemBaseHighRead, _itemDescriptionHighRead, "104-255", boundary);

        output.AcceptChanges();
        output.Columns["ID"]!.ReadOnly = true;
        output.Columns["分段"]!.ReadOnly = true;
        output.Columns["大类"]!.ReadOnly = true;
        output.Columns["来源文件"]!.ReadOnly = true;
        return output;
    }

    private IReadOnlyList<ItemEffectCatalogEntry> BuildDefaultItemEffectCatalogEntries(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var entries = new List<ItemEffectCatalogEntry>();
        foreach (var pair in BuildBaseItemEffectNameLookup(project, tables).OrderBy(pair => pair.Key))
        {
            entries.Add(new ItemEffectCatalogEntry
            {
                EffectId = pair.Key,
                Name = pair.Value,
                Description = string.Empty
            });
        }

        return entries;
    }

    private void AddItemEditorRows(DataTable output, TableReadResult itemRead, TableReadResult descriptionRead, string segment, ItemCategoryBoundary boundary)
    {
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
            var effectValue = output.Columns.Contains("装备特效号-效果值")
                ? Convert.ToInt32(row["装备特效号-效果值"], CultureInfo.InvariantCulture)
                : 0;
            var growth = output.Columns.Contains("升级能力成长")
                ? Convert.ToInt32(row["升级能力成长"], CultureInfo.InvariantCulture)
                : 0;
            var majorCategory = Convert.ToString(row["大类"], CultureInfo.InvariantCulture) ?? string.Empty;
            var effectiveEffectId = ItemEffectInterpretationService.ResolveEffectiveEffectId(majorCategory, typeId, effectId);
            row["类型说明"] = BuildItemTypeDescription(typeId, majorCategory, catalog);
            row["价格显示"] = BuildItemPriceText(priceUnit);
            row["装备特效名"] = BuildItemEffectNameDisplay(majorCategory, typeId, effectId);
            row["实际效果号"] = ItemEffectInterpretationService.BuildEffectiveEffectIdText(majorCategory, typeId, effectId);
            row["实际效果说明"] = ItemEffectInterpretationService.BuildEffectiveEffectDescription(majorCategory, typeId, effectId, effectiveEffectId, GetItemEffectName);
            row["特效提示"] = ItemEffectInterpretationService.BuildEffectHint(
                majorCategory,
                typeId,
                effectId,
                Convert.ToString(row["装备特效名"], CultureInfo.InvariantCulture) ?? string.Empty,
                effectValue,
                growth);
            var descriptionRow = TryFindRowById(descriptionRead.Data, id);
            row["介绍"] = descriptionRow == null
                ? string.Empty
                : Convert.ToString(descriptionRow["介绍"], CultureInfo.InvariantCulture) ?? string.Empty;
            row["来源文件"] = $"{itemRead.Table.FileName} / {descriptionRead.Table.FileName}";
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
            Dock = DockStyle.Fill,
            AutoSize = true,
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

    private static string BuildItemTypeDescription(int typeId, string majorCategory, int catalog)
    {
        return ItemTypeCatalogService.BuildShortName(typeId, majorCategory);
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
            or "宝物图鉴"
            or "类型说明"
            or "价格显示"
            or "实际效果号"
            or "实际效果说明"
            or "特效提示"
            or "来源文件";

    private static bool IsVisibleItemEditorColumn(string columnName)
        => columnName is "ID"
            or "图标"
            or "名称"
            or "类型"
            or "初始能力"
            or "升级能力成长"
            or "价格（/100）"
            or "装备特效号"
            or "装备特效名"
            or "装备特效号-效果值"
            or "介绍";

    private static string BuildItemEditorColumnHeader(string columnName)
        => columnName switch
        {
            "类型" => "类别",
            "升级能力成长" => "能力成长",
            "装备特效号" => "特效号",
            "装备特效名" => "特效名",
            "装备特效号-效果值" => "特效值",
            _ => columnName
        };

    private void ApplyItemEditorColumnDisplayOrder()
    {
        var displayOrder = new[]
        {
            "ID",
            "名称",
            "图标",
            "类型",
            "初始能力",
            "升级能力成长",
            "价格（/100）",
            "装备特效号",
            "装备特效名",
            "装备特效号-效果值",
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

    private static DataTable BuildItemTypeLookup(DataTable itemData)
    {
        var catalogEntries = ItemTypeCatalogService.GetEntries();
        var names = Enumerable.Range(0, byte.MaxValue + 1)
            .ToDictionary(
                id => id,
                id => ItemTypeCatalogService.BuildShortName(id, string.Empty));

        if (itemData.Columns.Contains("类型"))
        {
            foreach (DataRow row in itemData.Rows)
            {
                var typeId = Convert.ToInt32(row["类型"], CultureInfo.InvariantCulture);
                if (catalogEntries.ContainsKey(typeId)) continue;

                var majorCategory = itemData.Columns.Contains("大类")
                    ? Convert.ToString(row["大类"], CultureInfo.InvariantCulture) ?? string.Empty
                    : string.Empty;
                names[typeId] = ItemTypeCatalogService.BuildShortName(typeId, majorCategory);
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
            column.ReadOnly = column.DataPropertyName is "ID" or "分段" or "大类" or "类型说明" or "价格显示" or "装备特效名" or "实际效果号" or "实际效果说明" or "特效提示" or "来源文件";
            column.ToolTipText = BuildItemColumnAnnotation(column.DataPropertyName);
            column.HeaderText = BuildItemEditorColumnHeader(column.DataPropertyName);
            if (column.DataPropertyName == "ID") column.Width = 48;
            if (column.DataPropertyName == "图标") column.Width = 56;
            if (column.DataPropertyName == "名称") column.Width = 130;
            if (column.DataPropertyName == "类型") column.Width = 150;
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

    private string BuildItemColumnAnnotation(string columnName)
    {
        if (columnName == "ID") return "物品/宝物编号；0-103 来源 Data.e5，104 以后来源 Star.e5。";
        if (columnName == "分段") return "物品表分段，只读显示，用于确认写回 Data.e5 或 Star.e5。";
        if (columnName == "大类") return "按 B形象指定器 System.ini 的 DefID/AssID 分段推断：DefID=防具起始，AssID=辅助起始。";
        if (columnName == "类型") return "旧版“类别”显示：界面显示映射名称，保存仍写回原始类型码。";
        if (columnName == "类型说明") return "隐藏筛选列：只保留类型映射短名。";
        if (columnName == "价格显示") return "隐藏重复列；界面只保留原始可编辑的“价格（/100）”。";
        if (columnName == "装备特效名") return "根据装备特效号读取中文特效名称；普通/无特效显示“无”。";
        if (columnName == "实际效果号") return "创作者视角的实际效果候选：武器/防具优先取装备特效号；辅助装备常见原始值 2、道具常见原始值 3 时，当前优先改看类型字段。";
        if (columnName == "实际效果说明") return "对原始字段的保守纠偏说明，用于避免把辅助/道具类别标记误读成真实装备特效。";
        if (columnName == "特效提示") return "把装备特效号、效果值和成长集中提示；辅助/道具段会明确提示 2/3 可能只是类别标记，具体参数仍需对照旧工具和实机。";
        if (columnName == "图标") return "物品图标编号；界面会按该编号从 Itemicon.dll 提取候选图标预览，最终映射仍建议实机确认。";
        if (columnName == "介绍") return "物品说明文本，写入 Imsg.e5；固定 200 字节 GBK 容量。";
        if (columnName == "来源文件") return "本行基础字段与说明字段的目标文件，只读显示。";
        if (_itemBaseLowRead?.Table.Fields.FirstOrDefault(f => f.ColumnName == columnName) is { } field)
        {
            return _fieldAnnotationService.BuildFieldAnnotation(_itemBaseLowRead.Table, field);
        }
        return columnName;
    }

    private static string BuildItemEditorSummary(DataTable data)
    {
        var named = data.Rows.Cast<DataRow>().Count(row => !string.IsNullOrWhiteSpace(Convert.ToString(row["名称"], CultureInfo.InvariantCulture)));
        var low = data.Rows.Cast<DataRow>().Count(row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) <= 103);
        var high = data.Rows.Count - low;
        return
            $"宝物设定已读取：总行 {data.Rows.Count}，已命名 {named}，0-103 段 {low} 行，104+ 段 {high} 行。\r\n" +
            "界面按旧版宝物编辑器顺序显示：ID、名称、图标、类别、初始能力、能力成长、价格、特效号、特效名、特效值、介绍；隐藏分段/大类/图鉴/长解释列和重复价格列。\r\n" +
            "右侧图标预览按“图标”字段从 Itemicon.dll 枚举候选图标，保存仍写回 Data.e5、Star.e5、Imsg.e5 的原始字段。\r\n" +
            "保存会写回 Data.e5、Star.e5、Imsg.e5，保存前自动备份，保存后重新读取校验。";
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
            .Where(column => column.ColumnName is "ID" or "分段" or "大类" or "名称" or "类型" or "类型说明" or "装备特效号" or "装备特效名" or "特效提示" or "价格（/100）" or "图标" or "宝物图鉴" or "介绍")
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
        => ExportDataTableCsv(_currentItemEditorData, "宝物设定.csv");

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

        var readOnlySnapshot = CaptureItemEditorCsvReadOnlySnapshot(table);
        try
        {
            SetItemEditorCsvDerivedColumnsReadOnly(table, readOnly: true);
            var count = CsvService.ImportInto(table, dialog.FileName, allowPartialColumns: true, matchByIdWhenPresent: true);
            RestoreItemEditorCsvReadOnlySnapshot(table, readOnlySnapshot);
            RefreshItemEditorAfterBulkEdit();
            ResetItemEditorHistory();
            System.Diagnostics.Debug.WriteLine($"已导入宝物设定 CSV：{dialog.FileName}，更新行 {count}");
            SetStatus($"宝物设定 CSV 导入完成：更新 {count} 行，请检查后保存。");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("宝物设定 CSV 导入失败：" + ex);
            MessageBox.Show(this, ex.Message, "宝物设定 CSV 导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            RestoreItemEditorCsvReadOnlySnapshot(table, readOnlySnapshot);
        }
    }

    private static Dictionary<string, bool> CaptureItemEditorCsvReadOnlySnapshot(DataTable table)
        => GetItemEditorDerivedColumnNames()
            .Where(table.Columns.Contains)
            .ToDictionary(columnName => columnName, columnName => table.Columns[columnName]!.ReadOnly, StringComparer.Ordinal);

    private static void RestoreItemEditorCsvReadOnlySnapshot(DataTable table, IReadOnlyDictionary<string, bool> snapshot)
    {
        foreach (var pair in snapshot)
        {
            if (table.Columns.Contains(pair.Key)) table.Columns[pair.Key]!.ReadOnly = pair.Value;
        }
    }

    private static void SetItemEditorCsvDerivedColumnsReadOnly(DataTable table, bool readOnly)
    {
        foreach (var columnName in GetItemEditorDerivedColumnNames())
        {
            if (table.Columns.Contains(columnName)) table.Columns[columnName]!.ReadOnly = readOnly;
        }
    }

    private static string[] GetItemEditorDerivedColumnNames()
        =>
        [
            "类型说明",
            "价格显示",
            "装备特效名",
            "实际效果号",
            "实际效果说明",
            "特效提示"
        ];

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
        RefreshItemEditorAfterBulkEdit();
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
            RefreshItemEditorAfterBulkEdit();
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
        if (edits.Count > 0) RefreshItemEditorAfterBulkEdit();
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
        return !cell.ReadOnly && TryGetDataRow(_itemEditorGrid.Rows[rowIndex]) != null;
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

        RefreshItemEditorAfterBulkEdit();
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
            return;
        }

        var category = Convert.ToString(dataRow["大类"], CultureInfo.InvariantCulture) ?? string.Empty;
        _itemEditorGrid.Rows[rowIndex].DefaultCellStyle.BackColor = category switch
        {
            "武器" => Color.FromArgb(255, 250, 240),
            "防具" => Color.FromArgb(245, 250, 255),
            "辅助/道具" => Color.FromArgb(248, 255, 248),
            _ => Color.Empty
        };
    }

    private void UpdateItemEditorDerivedCells(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || rowIndex >= _itemEditorGrid.Rows.Count) return;
        if (columnIndex < 0 || columnIndex >= _itemEditorGrid.Columns.Count) return;
        var columnName = _itemEditorGrid.Columns[columnIndex].DataPropertyName;
        var row = TryGetDataRow(_itemEditorGrid.Rows[rowIndex]);
        if (row == null) return;

        if ((columnName is "类型" or "宝物图鉴") &&
            row.Table.Columns.Contains("类型说明") &&
            row.Table.Columns.Contains("类型") &&
            row.Table.Columns.Contains("宝物图鉴"))
        {
            row["类型说明"] = BuildItemTypeDescription(
                Convert.ToInt32(row["类型"], CultureInfo.InvariantCulture),
                Convert.ToString(row["大类"], CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToInt32(row["宝物图鉴"], CultureInfo.InvariantCulture));
        }

        if ((columnName is "类型" or "装备特效号" or "装备特效号-效果值" or "升级能力成长") &&
            row.Table.Columns.Contains("实际效果号") &&
            row.Table.Columns.Contains("实际效果说明") &&
            row.Table.Columns.Contains("装备特效名") &&
            row.Table.Columns.Contains("特效提示"))
        {
            var majorCategory = Convert.ToString(row["大类"], CultureInfo.InvariantCulture) ?? string.Empty;
            var typeId = Convert.ToInt32(row["类型"], CultureInfo.InvariantCulture);
            var effectId = Convert.ToInt32(row["装备特效号"], CultureInfo.InvariantCulture);
            var effectiveEffectId = ItemEffectInterpretationService.ResolveEffectiveEffectId(majorCategory, typeId, effectId);
            row["装备特效名"] = BuildItemEffectNameDisplay(majorCategory, typeId, effectId);
            row["实际效果号"] = ItemEffectInterpretationService.BuildEffectiveEffectIdText(majorCategory, typeId, effectId);
            row["实际效果说明"] = ItemEffectInterpretationService.BuildEffectiveEffectDescription(majorCategory, typeId, effectId, effectiveEffectId, GetItemEffectName);
            row["特效提示"] = ItemEffectInterpretationService.BuildEffectHint(
                majorCategory,
                typeId,
                effectId,
                Convert.ToString(row["装备特效名"], CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToInt32(row["装备特效号-效果值"], CultureInfo.InvariantCulture),
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
            row["类型说明"] = BuildItemTypeDescription(
                Convert.ToInt32(row["类型"], CultureInfo.InvariantCulture),
                Convert.ToString(row["大类"], CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToInt32(row["宝物图鉴"], CultureInfo.InvariantCulture));
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
            var majorCategory = Convert.ToString(row["大类"], CultureInfo.InvariantCulture) ?? string.Empty;
            var typeId = Convert.ToInt32(row["类型"], CultureInfo.InvariantCulture);
            var effectId = Convert.ToInt32(row["装备特效号"], CultureInfo.InvariantCulture);
            var effectiveEffectId = ItemEffectInterpretationService.ResolveEffectiveEffectId(majorCategory, typeId, effectId);
            row["装备特效名"] = BuildItemEffectNameDisplay(majorCategory, typeId, effectId);
            row["实际效果号"] = ItemEffectInterpretationService.BuildEffectiveEffectIdText(majorCategory, typeId, effectId);
            row["实际效果说明"] = ItemEffectInterpretationService.BuildEffectiveEffectDescription(majorCategory, typeId, effectId, effectiveEffectId, GetItemEffectName);
            row["特效提示"] = ItemEffectInterpretationService.BuildEffectHint(
                majorCategory,
                typeId,
                effectId,
                Convert.ToString(row["装备特效名"], CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToInt32(row["装备特效号-效果值"], CultureInfo.InvariantCulture),
                Convert.ToInt32(row["升级能力成长"], CultureInfo.InvariantCulture));
        }
    }

    private void ShowSelectedItemEditorCell()
    {
        if (_currentItemEditorData == null || _itemEditorGrid.CurrentCell == null) return;
        var cell = _itemEditorGrid.CurrentCell;
        var columnName = _itemEditorGrid.Columns[cell.ColumnIndex].DataPropertyName;
        var row = _itemEditorGrid.Rows[cell.RowIndex];
        var id = row.Cells["ID"].Value;
        var name = row.Cells["名称"].Value;
        var value = cell.FormattedValue;
        var effectText = row.Cells["装备特效号"].Value is { } effectValue
            ? $"    特效：{effectValue} / {row.Cells["装备特效名"].Value}"
            : string.Empty;
        var typeText = _itemEditorGrid.Columns.Contains("类型")
            ? row.Cells["类型"].FormattedValue
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
            $"宝物/物品：ID={id}    名称={name}    类别={typeText}    价格字段={priceText}{effectText}\r\n" +
            $"字段：{BuildItemEditorColumnHeader(columnName)}    当前值：{value}{extra}\r\n\r\n" +
            BuildItemColumnAnnotation(columnName);
        UpdateItemIconPreview(row);
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
        var oldImage = _itemIconPreviewBox.Image;
        _itemIconPreviewBox.Image = result.Bitmap;
        oldImage?.Dispose();
        var id = Convert.ToString(row.Cells["ID"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        var name = Convert.ToString(row.Cells["名称"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        _itemIconPreviewInfoBox.Text =
            $"物品 ID={id}  名称={name}\r\n" +
            $"图标字段={iconIndex}\r\n" +
            $"{result.Message}\r\n" +
            $"资源路径：{result.SourcePath}";
    }

    private void ClearItemIconPreview(string message)
    {
        var oldImage = _itemIconPreviewBox.Image;
        _itemIconPreviewBox.Image = null;
        oldImage?.Dispose();
        _itemIconPreviewInfoBox.Text = message;
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
        return _itemEditorGrid.SelectedCells
            .Cast<DataGridViewCell>()
            .Where(cell => cell.RowIndex >= 0)
            .Select(cell => _itemEditorGrid.Rows[cell.RowIndex])
            .Where(row => !row.IsNewRow)
            .Distinct()
            .OrderBy(row => row.Index)
            .ToArray();
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
            var saves = SaveItemEditorData(_project, _currentItemEditorData);
            LoadItemEditor();
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
        if (_itemBaseLowRead == null || _itemBaseHighRead == null || _itemDescriptionLowRead == null || _itemDescriptionHighRead == null)
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
            var descriptionRow = FindRowById(descriptionRead.Data, id);

            foreach (DataColumn column in baseRead.Data.Columns)
            {
                if (column.ColumnName == "ID") continue;
                if (!itemData.Columns.Contains(column.ColumnName) || !IsRoleColumnChanged(itemRow, column.ColumnName)) continue;
                baseRow[column.ColumnName] = itemRow[column.ColumnName, DataRowVersion.Current];
            }
            if (IsRoleColumnChanged(itemRow, "介绍")) descriptionRow["介绍"] = itemRow["介绍", DataRowVersion.Current];
        }

        var saves = new List<TableSaveResult>();
        if (_itemBaseLowRead.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, _itemBaseLowRead.Table, _itemBaseLowRead.Data));
        if (_itemBaseHighRead.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, _itemBaseHighRead.Table, _itemBaseHighRead.Data));
        if (_itemDescriptionLowRead.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, _itemDescriptionLowRead.Table, _itemDescriptionLowRead.Data));
        if (_itemDescriptionHighRead.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, _itemDescriptionHighRead.Table, _itemDescriptionHighRead.Data));
        return saves;
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
                     .Where(item => includeEmpty || item.Id != 255)
                     .OrderBy(item => item.Id == 255 ? -1 : item.Id))
        {
            lookup.Rows.Add(item.Id, item.DisplayName);
        }
        return lookup;
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
            return "1 字节物品编号，通常引用 6.5 物品表；255 常作为空槽候选。下拉项显示编号、名称、大类和类型说明；选中单元格后下方显示价格、特效和物品说明。修改后建议同步检查物品价格、图标、说明和实机商店显示。";
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
        ImportDataTableCsv(_currentShopEditorData, "商店编辑", RefreshShopEditorAfterBulkEdit);
        ValidateAllShopEditorEditableCells();
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
        ApplyShopBatchUpdate("批量清空为空槽", (_, _) => 255, onlyWhenMatches: null);
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
        itemId = 255;
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

    private string? ValidateShopEditorCellText(string columnName, string value, DataRow? row, bool comboBoxColumn = false)
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
            return comboBoxColumn
                ? null
                : TryParseInteger(value, 0, byte.MaxValue, columnName, _currentPageHexButton.Checked);
        }

        return null;
    }

    private object? ConvertShopEditorValueForColumn(string columnName, string text)
    {
        if (_currentShopEditorData == null || !_currentShopEditorData.Columns.Contains(columnName)) return text;
        var dataColumn = _currentShopEditorData.Columns[columnName];
        if (dataColumn == null || dataColumn.DataType == typeof(string)) return text;
        if (IsSupportedIntegerType(dataColumn.DataType) &&
            TryParseIntegerInput(text, _currentPageHexButton.Checked, out var parsed) &&
            TryConvertParsedIntegerToType(parsed, dataColumn.DataType, out var converted))
        {
            return converted;
        }

        return Convert.ChangeType(text, dataColumn.DataType, CultureInfo.InvariantCulture);
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
                    comboBoxColumn: false);
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
            var saves = SaveShopEditorData(_project, _currentShopEditorData);
            LoadShopEditor();
            var changedBytes = saves.Sum(x => x.ChangedBytes);
            System.Diagnostics.Debug.WriteLine($"已保存商店编辑：保存表 {saves.Count} 个，变化字节 {changedBytes}");
            foreach (var save in saves) System.Diagnostics.Debug.WriteLine("商店编辑备份：" + save.BackupPath);
            SetStatus($"商店编辑保存完成并已复读：变化 {changedBytes} 字节");
            MessageBox.Show(this,
                $"保存完成并已重新读取校验。\r\n保存表数量：{saves.Count}\r\n变化字节：{changedBytes}\r\n备份：{string.Join("; ", saves.Select(x => x.BackupPath))}",
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
        var campaignRowsById = BuildRowsById(_shopCampaignNameRead.Data);
        var shopRowsById = BuildRowsById(_shopDataRead.Data);
        foreach (DataRow shopRow in shopEditorData.Rows)
        {
            if (shopRow.RowState != DataRowState.Modified) continue;
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
        if (_shopCampaignNameRead.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, _shopCampaignNameRead.Table, _shopCampaignNameRead.Data));
        if (_shopDataRead.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, _shopDataRead.Table, _shopDataRead.Data));
        return saves;
    }

    private static Dictionary<int, DataRow> BuildRowsById(DataTable table)
        => table.Rows
            .Cast<DataRow>()
            .ToDictionary(row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture));

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
            var saves = SaveJobTerrainEditorData(_project, _currentJobTerrainData);
            LoadJobTerrainEditor();
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
        if (_jobSeriesRead.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, _jobSeriesRead.Table, _jobSeriesRead.Data));
        if (_jobTerrainPowerRead.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, _jobTerrainPowerRead.Table, _jobTerrainPowerRead.Data));
        if (_jobMoveCostRead.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, _jobMoveCostRead.Table, _jobMoveCostRead.Data));
        return saves;
    }

    private void LoadJobStrategyEditor()
    {
        if (_project == null) return;
        _importJobStrategyIconButton.Enabled = false;
        _editJobStrategyIconButton.Enabled = false;
        if (_tables.Count == 0)
        {
            ReloadCurrentProject();
            if (_tables.Count == 0) return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            CloseJobStrategyLearningDialogs();
            _currentJobStrategyData = BuildJobStrategyEditorData(_project, _tables);
            _jobStrategyEditorGrid.DataSource = _currentJobStrategyData;
            ConfigureJobStrategyGrid();
            _saveJobStrategyEditorButton.Enabled = true;
            _importJobStrategyIconButton.Enabled = true;
            _editJobStrategyIconButton.Enabled = true;
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
        _jobStrategyCompanionReads.Clear();
        foreach (var companion in JobStrategyCompanionColumns)
        {
            _jobStrategyCompanionReads[companion.ColumnName] = _tableReader.Read(project, FindTable(tables, companion.TableName), tables);
        }

        _jobStrategyJobNames = BuildIdNameLookup(project, tables, "6.5-4 详细兵种");
        (_jobStrategyConfiguredMagicCount, _jobStrategyConfiguredMagicSource) = ResolveStrategyMagicCount(project);
        if (!_jobStrategyRead.Validation.IsUsable || _jobStrategyCompanionReads.Values.Any(read => !read.Validation.IsUsable))
        {
            throw new InvalidOperationException("策略主表或 EKD5 策略附表有不可读取项，请先查看数据表诊断。");
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
            column.ReadOnly = column.DataPropertyName is "ID" or "可学摘要";
            column.ToolTipText = BuildJobStrategyColumnAnnotation(column.DataPropertyName);
            column.HeaderText = BuildJobStrategyColumnHeader(column.DataPropertyName);
            if (column.DataPropertyName == "名称") column.Width = 110;
            if (column.DataPropertyName == "可学摘要")
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
            "AI策略（战场）" => "AI策略（战场）",
            "AI策略（练武）" => "AI策略（练武）",
            _ => columnName
        };
    }

    private string BuildJobStrategyColumnAnnotation(string columnName)
    {
        if (columnName == "ID") return "策略编号。策略主表、说明表和 EKD5 策略附表按这个编号逐行对齐。";
        if (columnName == "名称") return "策略名称，写入 6.5-5 策略 / Data.e5；固定 11 字节 GBK 容量。";
        if (columnName == "可学摘要") return "只读摘要：列出当前策略中学会等级不为 0 的详细兵种。0 表示该兵种不能学习。";
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
        if (_currentJobStrategyData == null || row.RowState == DataRowState.Detached) return;
        foreach (var (jobId, level) in levels)
        {
            var columnName = BuildJobStrategyLearningColumnName(jobId.ToString(CultureInfo.InvariantCulture));
            if (!row.Table.Columns.Contains(columnName)) continue;
            if (Convert.ToInt32(row[columnName], CultureInfo.InvariantCulture) == level) continue;
            row[columnName] = level;
        }

        SetJobStrategyLearningSummary(row);
        var gridRowIndex = FindJobStrategyGridRowIndex(row);
        if (gridRowIndex >= 0)
        {
            RefreshJobStrategyRowStyle(gridRowIndex);
            if (_jobStrategyEditorGrid.CurrentCell?.RowIndex == gridRowIndex) ShowSelectedJobStrategyCell();
        }

        SetStatus($"策略 {Convert.ToString(row["ID"], CultureInfo.InvariantCulture)} 学习等级已同步到可学摘要");
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
            "来源表：6.5-5 策略（基础属性 + 80 个兵种学会等级）、6.5-5-2 至 6.5-5-9 策略附表。\r\n" +
            "保存会写回 Data.e5 与 Ekd5.exe，保存前自动备份，保存后重新读取校验。";
    }

    private void ApplyJobStrategyFilter()
    {
        if (_currentJobStrategyData == null) return;
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

    private void ShowSelectedJobStrategyCell()
    {
        if (_currentJobStrategyData == null || _jobStrategyEditorGrid.CurrentCell == null) return;
        var cell = _jobStrategyEditorGrid.CurrentCell;
        var columnName = _jobStrategyEditorGrid.Columns[cell.ColumnIndex].DataPropertyName;
        var row = _jobStrategyEditorGrid.Rows[cell.RowIndex];
        var id = row.Cells["ID"].Value;
        var name = row.Cells["名称"].Value;
        var value = cell.Value;
        var formattedValue = FormatJobStrategyCellValue(columnName, value);
        var extra = columnName == "名称"
            ? $"\r\nGBK 字节：{EncodingService.GetGbkByteCount(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)}/11"
            : string.Empty;
        var summary = Convert.ToString(row.Cells["可学摘要"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        var magicInfo = _jobStrategyConfiguredMagicCount > 0
            ? $"\r\n形象指定器 SMagic={_jobStrategyConfiguredMagicCount}；来源：{_jobStrategyConfiguredMagicSource}"
            : string.Empty;
        _jobStrategyEditorInfoBox.Text =
            $"策略：ID={id}    名称={name}\r\n" +
            $"字段：{columnName}    当前值：{formattedValue}{extra}\r\n" +
            $"可学摘要：{summary}{magicInfo}\r\n\r\n" +
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
        if (!IsJobStrategyResourcePreviewColumn(columnName))
        {
            ShowJobStrategyLearningPreview(row, columnName, id, name);
            return;
        }

        var rawValue = row.Cells[columnName].Value;
        if (!TryConvertToInt(rawValue, out var fieldValue))
        {
            ClearJobStrategyPreview($"{columnName} 字段不是整数：{Convert.ToString(rawValue, CultureInfo.InvariantCulture)}");
            return;
        }

        switch (columnName)
        {
            case "施法范围":
            {
                ClearJobStrategyAnimationPreview();
                var result = _attackAreaPreviewService.BuildPreview(_project, "攻击范围", fieldValue);
                SetJobStrategyPreviewImageVisible(true);
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
                ClearJobStrategyAnimationPreview();
                var result = _attackAreaPreviewService.BuildPreview(_project, "穿透范围", fieldValue);
                SetJobStrategyPreviewImageVisible(true);
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
                ClearJobStrategyAnimationPreview();
                var result = _itemIconPreviewService.BuildPreview(_project, fieldValue, Ccz66RevisedLayout.ResolveStrategyIconResourceFile(_project), "策略图标");
                SetJobStrategyPreviewImageVisible(true);
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
                var previewKind = columnName == "大动画"
                    ? StrategyAnimationPreviewKind.BigMcall
                    : StrategyAnimationPreviewKind.SmallMeff;
                var result = _strategyAnimationPreviewService.BuildAnimatedPreview(_project, previewKind, fieldValue);
                SetJobStrategyPreviewImageVisible(true);
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
        ClearJobStrategyAnimationPreview();
        SetJobStrategyPreviewImageVisible(false);
        SetPictureBoxImage(_jobStrategyPreviewBox, null);
        _jobStrategyPreviewInfoBox.Text = message;
    }

    private void SetJobStrategyPreviewImageVisible(bool visible)
    {
        _jobStrategyPreviewBox.Visible = visible;
        if (_jobStrategyPreviewBox.Parent is not TableLayoutPanel panel || panel.RowStyles.Count < 3) return;
        panel.RowStyles[1].SizeType = visible ? SizeType.Percent : SizeType.Absolute;
        panel.RowStyles[1].Height = visible ? 58 : 0;
        panel.RowStyles[2].SizeType = SizeType.Percent;
        panel.RowStyles[2].Height = visible ? 42 : 100;
    }

    private static bool IsJobStrategyResourcePreviewColumn(string columnName)
        => columnName is "施法范围" or "穿透范围" or "策略图标" or "小动画" or "大动画";

    private void ShowJobStrategyLearningPreview(DataGridViewRow row, string columnName, string id, string name)
    {
        ClearJobStrategyAnimationPreview();
        SetJobStrategyPreviewImageVisible(false);
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
        if (_project == null || _currentJobStrategyData == null || _jobStrategyRead == null) return;

        _jobStrategyEditorGrid.EndEdit();
        if (!CommitJobStrategyLearningDialogs()) return;
        if (_currentJobStrategyData.GetChanges() == null)
        {
            MessageBox.Show(this, "兵种策略没有检测到改动。", "无需保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var preview = BuildChangePreview(_currentJobStrategyData, maxItems: 80);
        if (MessageBox.Show(this,
                $"即将保存兵种策略到当前 MOD 项目。\r\n\r\n变更预览：\r\n{preview}\r\n\r\n保存前会自动备份 Data.e5/Ekd5.exe，保存后会重新读取校验。是否继续？",
                "确认保存兵种策略",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var saves = SaveJobStrategyEditorData(_project, _currentJobStrategyData);
            LoadJobStrategyEditor();
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

        var selectedRows = GetSelectedJobStrategyRowsForIconImport();
        if (selectedRows.Count == 0)
        {
            MessageBox.Show(this, "请先在兵种策略表中选中要导入图标的策略行。", "导入策略图标", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        return _jobStrategyEditorGrid.SelectedCells
            .Cast<DataGridViewCell>()
            .Where(cell => cell.RowIndex >= 0)
            .Select(cell => _jobStrategyEditorGrid.Rows[cell.RowIndex])
            .Where(row => !row.IsNewRow)
            .Distinct()
            .OrderBy(row => row.Index)
            .ToArray();
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
        var itemByIcon = preview.Items.ToDictionary(item => item.IconIndex);
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
        var itemByImage = preview.Operations.ToDictionary(item => item.ImageNumber);
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
        if (_jobStrategyRead == null) return Array.Empty<TableSaveResult>();
        foreach (DataRow editorRow in strategyData.Rows)
        {
            if (editorRow.RowState != DataRowState.Modified) continue;
            var id = Convert.ToInt32(editorRow["ID"], CultureInfo.InvariantCulture);
            var strategyRow = FindRowById(_jobStrategyRead.Data, id);

            foreach (var columnName in JobStrategyPrimaryColumns)
            {
                if (IsRoleColumnChanged(editorRow, columnName)) strategyRow[columnName] = editorRow[columnName, DataRowVersion.Current];
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
        if (_jobStrategyRead.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, _jobStrategyRead.Table, _jobStrategyRead.Data));
        foreach (var companion in JobStrategyCompanionColumns)
        {
            var read = _jobStrategyCompanionReads[companion.ColumnName];
            if (read.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, read.Table, read.Data));
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
            _jobSeriesRead ??= _tableReader.Read(_project, FindTable(_tables, "6.5-3 兵种系"), _tables);
            _jobRestraintRead = _tableReader.Read(_project, FindTable(_tables, "6.5-3-3 兵种相克"), _tables);
            if (!_jobSeriesRead.Validation.IsUsable || !_jobRestraintRead.Validation.IsUsable)
            {
                throw new InvalidOperationException("兵种相克矩阵有不可读取项，请先查看数据表诊断。");
            }

            _jobRestraintGrid.DataSource = _jobRestraintRead.Data;
            ConfigureJobMatrixGrid(_jobRestraintGrid);
            _saveJobMatrixButton.Enabled = true;
            _jobMatrixInfoBox.Text = BuildJobMatrixSummary();
            SetStatus("兵种相克矩阵读取完成");
        }
        catch (Exception ex)
        {
            _jobMatrixInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("读取兵种相克矩阵失败：" + ex);
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

    private string BuildJobMatrixColumnHeader(string columnName)
    {
        if (columnName == "ID") return "ID";
        if (columnName == "名称") return "攻击方\n兵种系";
        return TryGetJobSeriesName(columnName, out var name)
            ? $"{columnName}\n{name}"
            : columnName;
    }

    private string BuildJobMatrixColumnAnnotation(string columnName)
    {
        if (columnName == "ID") return "兵种系编号。";
        if (columnName == "名称") return "攻击方兵种系名称。";
        var target = TryGetJobSeriesName(columnName, out var name) ? $"{columnName}:{name}" : columnName;
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
        return
            $"兵种相克矩阵已读取：相克 {restraintRows}x{restraintCols}。\r\n" +
            "来源表：6.5-3-3 兵种相克；列名 0..39 已按 6.5-3 兵种系补充表头。\r\n" +
            "保存会写回 Ekd5.exe，保存前自动备份，保存后重新读取校验。";
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

    private void ShowSelectedJobMatrixCell(DataGridView grid, string matrixName)
    {
        if (grid.CurrentCell == null) return;
        var cell = grid.CurrentCell;
        if (cell.RowIndex < 0 || cell.ColumnIndex < 0) return;
        var columnName = grid.Columns[cell.ColumnIndex].DataPropertyName;
        var row = grid.Rows[cell.RowIndex];
        var rowName = row.Cells["名称"].Value;
        var targetName = TryGetJobSeriesName(columnName, out var jobName) ? $"{columnName}:{jobName}" : columnName;
        _jobMatrixInfoBox.Text =
            $"{matrixName}：行={rowName}    列={targetName}\r\n" +
            $"当前值：{cell.Value}\r\n\r\n" +
            BuildJobMatrixColumnAnnotation(columnName);
    }

    private void ValidateJobMatrixCell(DataGridView grid, DataGridViewCellValidatingEventArgs e)
    {
        if (grid.ReadOnly || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var column = grid.Columns[e.ColumnIndex];
        if (column.ReadOnly) return;
        var value = Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture) ?? string.Empty;
        var error = TryParseInteger(value, 0, byte.MaxValue, column.DataPropertyName, _currentPageHexButton.Checked);
        grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = error ?? string.Empty;
        if (error == null) return;
        e.Cancel = true;
        _jobMatrixInfoBox.Text = error;
        SetStatus(error);
    }

    private void SaveJobMatrixEditor()
    {
        if (_project == null || _jobRestraintRead == null) return;

        _jobRestraintGrid.EndEdit();
        if (_jobRestraintRead.Data.GetChanges() == null)
        {
            MessageBox.Show(this, "兵种相克矩阵没有检测到改动。", "无需保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var preview = BuildChangePreview(_jobRestraintRead.Data, maxItems: 30);
        if (MessageBox.Show(this,
                $"即将保存兵种相克矩阵到当前 MOD 项目。\r\n\r\n变更预览：\r\n{preview}\r\n\r\n保存前会自动备份 Ekd5.exe，保存后会重新读取校验。是否继续？",
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
            if (_jobRestraintRead.Data.GetChanges() != null) saves.Add(_tableWriter.Save(_project, _jobRestraintRead.Table, _jobRestraintRead.Data));
            LoadJobMatrixEditor();
            var changedBytes = saves.Sum(x => x.ChangedBytes);
            System.Diagnostics.Debug.WriteLine($"已保存兵种相克矩阵：保存表 {saves.Count} 个，变化字节 {changedBytes}");
            foreach (var save in saves) System.Diagnostics.Debug.WriteLine("兵种矩阵备份：" + save.BackupPath);
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
            var saves = SaveJobEffectEditorData(_project, _currentJobEffectData);
            LoadJobEffectEditor();
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
        if (_jobEffectDescriptionRead.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, _jobEffectDescriptionRead.Table, _jobEffectDescriptionRead.Data));
        if (_jobEffectAssignmentRead.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, _jobEffectAssignmentRead.Table, _jobEffectAssignmentRead.Data));
        return saves;
    }
}
