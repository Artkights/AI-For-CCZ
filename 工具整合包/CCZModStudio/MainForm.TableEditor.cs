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
    private void RefreshTableList()
    {
        IEnumerable<HexTableDefinition> query = _tables;
        if (!_showAllTables.Checked)
        {
            var tableVersion = _project == null ? "6.5" : _engineProfileService.Detect(_project).TableVersionPrefix;
            query = query.Where(t => t.Enabled && t.Version == tableVersion);
        }

        var selectedId = (_tableList.SelectedItem as HexTableDefinition)?.Id;
        var list = query.OrderBy(t => t.Id).ToList();
        _tableList.DataSource = list;
        if (selectedId.HasValue)
        {
            var match = list.FirstOrDefault(t => t.Id == selectedId.Value);
            if (match != null) _tableList.SelectedItem = match;
        }
    }

    private void LoadSelectedTable()
    {
        if (_project == null || _tableList.SelectedItem is not HexTableDefinition table) return;

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = _tableReader.Read(_project, table, _tables);
            _currentTableResult = result;
            _dataGrid.DataSource = result.Data;
            _tableColumnFilterBox.Clear();
            _dangerTableColumnsOnly.Checked = false;
            _tableRowFilterBox.Clear();
            _changedTableRowsOnly.Checked = false;
            _tableRowSearchVisibleColumnsOnly.Checked = true;
            ClearCurrentTableReferenceTarget();
            ConfigureDataGrid(result);
            ConfigureChartColumns(result.Data);
            var canEdit = CanEditTable(result);
            _fieldAnnotationBox.Text = _fieldAnnotationService.BuildTableSummary(table, result.Validation, canEdit);
            LogTableValidation(result.Validation);
            SetStatus(result.Validation.IsUsable
                ? $"已读取：{table.TableName}，{result.Data.Rows.Count} 行。当前项目可编辑，保存前会自动备份。"
                : $"表不可读取：{table.TableName}，请查看诊断。");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"读取表失败：{table.TableName}\r\n{ex}");
            SetStatus("读取失败");
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ConfigureDataGrid(TableReadResult result)
    {
        var canEdit = CanEditTable(result);
        _dataGrid.ReadOnly = !canEdit;
        _saveTableButton.Enabled = canEdit;
        _exportCsvButton.Enabled = result.Validation.IsUsable && result.Data.Rows.Count > 0;
        _importCsvButton.Enabled = canEdit;
        _copyTableSelectionButton.Enabled = result.Validation.IsUsable && result.Data.Rows.Count > 0;
        _pasteTableSelectionButton.Enabled = canEdit;
        _batchFillTableColumnButton.Enabled = canEdit;
        _filterTableColumnsButton.Enabled = result.Validation.IsUsable && result.Data.Columns.Count > 0;
        _clearTableColumnFilterButton.Enabled = result.Validation.IsUsable && result.Data.Columns.Count > 0;
        _dangerTableColumnsOnly.Enabled = result.Validation.IsUsable && result.Data.Columns.Count > 0;
        _exportFieldAnnotationsButton.Enabled = result.Validation.IsUsable && result.Table.Fields.Count > 0;
        _exportVisibleColumnsCsvButton.Enabled = result.Validation.IsUsable && result.Data.Rows.Count > 0;
        _visibleColumnsCsvWithNotes.Enabled = result.Validation.IsUsable && result.Data.Rows.Count > 0;
        _filterTableRowsButton.Enabled = result.Validation.IsUsable && result.Data.Rows.Count > 0;
        _clearTableRowFilterButton.Enabled = result.Validation.IsUsable && result.Data.Rows.Count > 0;
        _changedTableRowsOnly.Enabled = result.Validation.IsUsable && result.Data.Rows.Count > 0;
        _tableRowSearchVisibleColumnsOnly.Enabled = result.Validation.IsUsable && result.Data.Rows.Count > 0;

        foreach (DataGridViewColumn column in _dataGrid.Columns)
        {
            column.ReadOnly = !canEdit || column.Index == 0;
            if (column.Index == 0)
            {
                column.HeaderText = "ID\n行号/编号";
                column.ToolTipText = "工具生成的行号/编号，用于和原版数据下标对应；不直接写入二进制。";
            }
            if (column.DataPropertyName is { Length: > 0 } propertyName &&
                result.Data.Columns.Contains(propertyName) &&
                result.Data.Columns[propertyName]!.ExtendedProperties["FieldDefinition"] is HexFieldDefinition field &&
                !IsWritableTableField(result.Table, field))
            {
                column.ReadOnly = true;
            }
            if (column.DataPropertyName is { Length: > 0 } fieldProperty &&
                result.Data.Columns.Contains(fieldProperty) &&
                result.Data.Columns[fieldProperty]!.ExtendedProperties["FieldDefinition"] is HexFieldDefinition fieldDefinition)
            {
                column.HeaderText = fieldProperty + "\n" + _fieldAnnotationService.BuildShortFieldAnnotation(result.Table, fieldDefinition);
                column.ToolTipText = _fieldAnnotationService.BuildFieldAnnotation(result.Table, fieldDefinition);
            }
        }

        RefreshDataGridRowStyles();
    }

    private static bool CanEditTable(TableReadResult result)
        => result.Validation.IsUsable;

    private static bool IsWritableTableField(HexTableDefinition table, HexFieldDefinition field)
        => field.ConsumesBytes ||
           (table.Fields.Count == 1 && field.Size == 0 && table.RowSize > 0);

    private void ShowSelectedDataCellAnnotation(int rowIndex, int columnIndex)
    {
        if (_currentTableResult == null || rowIndex < 0 || columnIndex < 0)
        {
            ClearCurrentTableReferenceTarget();
            return;
        }

        if (columnIndex >= _dataGrid.Columns.Count || rowIndex >= _dataGrid.Rows.Count)
        {
            ClearCurrentTableReferenceTarget();
            return;
        }

        var column = _dataGrid.Columns[columnIndex];
        if (columnIndex == 0)
        {
            _fieldAnnotationBox.Text = _fieldAnnotationService.BuildTableSummary(
                _currentTableResult.Table,
                _currentTableResult.Validation,
                CanEditTable(_currentTableResult));
            ClearCurrentTableReferenceTarget("当前选中的是 ID 列；请选择具体字段查看跨表引用。");
            return;
        }

        var propertyName = column.DataPropertyName;
        if (string.IsNullOrWhiteSpace(propertyName) ||
            !_currentTableResult.Data.Columns.Contains(propertyName) ||
            _currentTableResult.Data.Columns[propertyName]!.ExtendedProperties["FieldDefinition"] is not HexFieldDefinition field)
        {
            ClearCurrentTableReferenceTarget();
            return;
        }

        var row = _dataGrid.Rows[rowIndex];
        var rowId = row.Cells[0].Value is null ? rowIndex : Convert.ToInt32(row.Cells[0].Value, CultureInfo.InvariantCulture);
        var value = row.Cells[columnIndex].Value;
        var annotation = _fieldAnnotationService.BuildCellAnnotation(_currentTableResult.Table, field, rowId, value);
        UpdateCurrentTableReferenceNavigation(_currentTableResult.Table, field, value, rowId);
        _fieldAnnotationBox.Text = annotation
                                   + BuildImageResourceEvidence(field.ColumnName, value)
                                   + BuildTableReferenceEvidence(_currentTableResult.Table, field, value, rowId);
    }


    private string BuildTableReferenceEvidence(HexTableDefinition table, HexFieldDefinition field, object? value, int rowId)
    {
        if (_project == null || _tables.Count == 0)
        {
            return string.Empty;
        }

        try
        {
            return _tableReferenceLookupService.BuildCellReferenceEvidence(_project, _tables, table, field, value, rowId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("\u8de8\u8868\u5f15\u7528\u89e3\u91ca\u5931\u8d25\uff1a" + ex.Message);
            return "\r\n\r\n\u8de8\u8868\u5f15\u7528\u89e3\u91ca\uff1a\u5f53\u524d\u5b57\u6bb5\u53ef\u80fd\u5173\u8054\u5176\u4ed6\u8868\uff0c\u4f46\u672c\u6b21\u89e3\u6790\u5931\u8d25\u3002\u5efa\u8bae\u4fdd\u7559\u539f\u503c\u5e76\u7ed3\u5408\u539f\u5de5\u5177\u9a8c\u8bc1\u3002";
        }
    }

    private void UpdateCurrentTableReferenceNavigation(HexTableDefinition table, HexFieldDefinition field, object? value, int rowId)
    {
        if (_project == null || _tables.Count == 0)
        {
            ClearCurrentTableReferenceTarget();
            return;
        }

        try
        {
            var target = _tableReferenceLookupService.ResolveCellReferenceTarget(_project, _tables, table, field, value, rowId);
            _currentTableReferenceTarget = target;
            _jumpTableReferenceButton.Enabled = target.CanNavigate;
            _tableReferenceNavigationBox.Text = target.IsRecognized
                ? (target.CanNavigate
                    ? target.Summary
                    : target.Summary + (string.IsNullOrWhiteSpace(target.SafetyNote) ? string.Empty : "；提示：" + target.SafetyNote))
                : target.Summary;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("数据表引用导航解析失败：" + ex.Message);
            ClearCurrentTableReferenceTarget("跨表引用导航解析失败；请查看字段说明并结合原工具验证。");
        }
    }

    private void ClearCurrentTableReferenceTarget(string text = "选中引用字段后显示可跳转目标。")
    {
        _currentTableReferenceTarget = null;
        _jumpTableReferenceButton.Enabled = false;
        _tableReferenceNavigationBox.Text = text;
    }

    private void JumpCurrentTableReferenceTarget()
    {
        var target = _currentTableReferenceTarget;
        if (target == null || !target.CanNavigate)
        {
            MessageBox.Show(this, "当前单元格没有可跳转的跨表目标。请先选择人物职业、物品槽位、装备特效号、行对齐说明等已识别字段。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (SelectDataTableCell(target.TargetTableName, target.TargetRowId, target.TargetFieldName))
        {
            SetStatus($"已跳转到引用目标：{target.TargetTableName} / ID={target.TargetRowId} / {target.TargetFieldName}");
            return;
        }

        MessageBox.Show(this,
            $"未能定位引用目标：{target.TargetTableName} / ID={target.TargetRowId} / {target.TargetFieldName}\r\n{target.SafetyNote}",
            "未找到引用目标",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private string BuildImageResourceEvidence(string columnName, object? value)
    {
        if (_project == null || value == null || value == DBNull.Value)
        {
            return string.Empty;
        }

        var prefix = columnName switch
        {
            "R\u5f62\u8c61\u7f16\u53f7" => "R",
            "S\u5f62\u8c61\u7f16\u53f7" => "S",
            _ when columnName.Contains("R\u5f62\u8c61", StringComparison.Ordinal) => "R",
            _ when columnName.Contains("S\u5f62\u8c61", StringComparison.Ordinal) => "S",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(prefix) ||
            !int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return string.Empty;
        }

        var fileName = ImageAssignmentService.GetImageResourceFileName(prefix, id);
        var path = ImageAssignmentService.GetImageResourcePath(_project, prefix, id);
        var status = ImageAssignmentService.GetImageResourceStatus(_project, prefix, id);
        if (!File.Exists(path))
        {
            return $"\r\n\r\n\u8d44\u6e90\u5b9e\u8bc1\u68c0\u67e5\uff1a{status}\r\n\u671f\u671b\u8def\u5f84\uff1a{path}\r\n\u5efa\u8bae\uff1a\u82e5\u8fd9\u662f\u5b9e\u9645\u8981\u4f7f\u7528\u7684\u4eba\u7269\u5f62\u8c61\uff0c\u8bf7\u8865\u9f50 RS \u8d44\u6e90\u6216\u6539\u6210\u5df2\u5b58\u5728\u7f16\u53f7\u3002";
        }

        var info = new FileInfo(path);
        return $"\r\n\r\n\u8d44\u6e90\u5b9e\u8bc1\u68c0\u67e5\uff1a{status}\r\n\u8def\u5f84\uff1a{path}\r\n\u5927\u5c0f\uff1a{info.Length:N0} \u5b57\u8282\uff0c\u4fee\u6539\u65f6\u95f4\uff1a{info.LastWriteTime:yyyy-MM-dd HH:mm:ss}";
    }

    private void ApplyTableColumnFilter()
    {
        if (_currentTableResult == null)
        {
            return;
        }

        var keyword = _tableColumnFilterBox.Text.Trim();
        var dangerOnly = _dangerTableColumnsOnly.Checked;
        var visibleCount = 0;

        foreach (DataGridViewColumn column in _dataGrid.Columns)
        {
            if (column.Index == 0)
            {
                column.Visible = true;
                continue;
            }

            var visible = true;
            var propertyName = column.DataPropertyName;
            HexFieldDefinition? field = null;
            if (!string.IsNullOrWhiteSpace(propertyName) &&
                _currentTableResult.Data.Columns.Contains(propertyName) &&
                _currentTableResult.Data.Columns[propertyName]!.ExtendedProperties["FieldDefinition"] is HexFieldDefinition fieldDefinition)
            {
                field = fieldDefinition;
            }

            if (dangerOnly)
            {
                visible = field != null && _fieldAnnotationService.IsHighRiskField(_currentTableResult.Table, field);
            }

            if (visible && !string.IsNullOrWhiteSpace(keyword))
            {
                var haystack = string.Join("\n",
                    column.DataPropertyName,
                    column.HeaderText,
                    column.ToolTipText,
                    field == null ? string.Empty : _fieldAnnotationService.BuildShortFieldAnnotation(_currentTableResult.Table, field),
                    field == null ? string.Empty : _fieldAnnotationService.BuildFieldAnnotation(_currentTableResult.Table, field));
                visible = haystack.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) >= 0;
            }

            column.Visible = visible;
            if (visible) visibleCount++;
        }

        SetStatus($"数据表列筛选：显示 {visibleCount}/{Math.Max(0, _dataGrid.Columns.Count - 1)} 个字段列");
    }

    private void ClearTableColumnFilter()
    {
        _tableColumnFilterBox.Clear();
        _dangerTableColumnsOnly.Checked = false;
        foreach (DataGridViewColumn column in _dataGrid.Columns)
        {
            column.Visible = true;
        }

        if (_currentTableResult != null)
        {
            SetStatus($"已显示全部字段列：{Math.Max(0, _dataGrid.Columns.Count - 1)} 列");
        }
    }

    private void ApplyTableRowFilter()
    {
        if (_currentTableResult == null)
        {
            return;
        }

        _dataGrid.EndEdit();
        var keyword = _tableRowFilterBox.Text.Trim();
        var changedOnly = _changedTableRowsOnly.Checked;
        var visibleCount = 0;
        _dataGrid.CurrentCell = null;
        _dataGrid.SuspendLayout();
        try
        {
            foreach (DataGridViewRow gridRow in _dataGrid.Rows)
            {
                var row = TryGetDataRow(gridRow);
                if (row == null)
                {
                    gridRow.Visible = false;
                    continue;
                }

                var visible = true;
                if (changedOnly)
                {
                    visible = IsDataRowChanged(row);
                }

                if (visible && !string.IsNullOrWhiteSpace(keyword))
                {
                    var searchColumns = GetTableRowSearchColumns();
                    visible = searchColumns
                        .Any(column => (Convert.ToString(row[column], CultureInfo.InvariantCulture) ?? string.Empty)
                            .IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) >= 0);
                }

                gridRow.Visible = visible;
                if (visible) visibleCount++;
            }
        }
        finally
        {
            _dataGrid.ResumeLayout();
        }

        SetStatus($"数据表行筛选：显示 {visibleCount}/{_currentTableResult.Data.Rows.Count} 行");
    }

    private IReadOnlyList<DataColumn> GetTableRowSearchColumns()
    {
        if (_currentTableResult == null) return Array.Empty<DataColumn>();
        if (!_tableRowSearchVisibleColumnsOnly.Checked)
        {
            return _currentTableResult.Data.Columns.Cast<DataColumn>().ToList();
        }

        var visibleColumnNames = _dataGrid.Columns
            .Cast<DataGridViewColumn>()
            .Where(c => c.Visible && !string.IsNullOrWhiteSpace(c.DataPropertyName))
            .OrderBy(c => c.DisplayIndex)
            .Select(c => c.DataPropertyName)
            .Distinct(StringComparer.Ordinal)
            .Where(name => _currentTableResult.Data.Columns.Contains(name))
            .ToList();
        return visibleColumnNames.Count == 0
            ? _currentTableResult.Data.Columns.Cast<DataColumn>().ToList()
            : visibleColumnNames.Select(name => _currentTableResult.Data.Columns[name]!).ToList();
    }

    private void ClearTableRowFilter()
    {
        _tableRowFilterBox.Clear();
        _changedTableRowsOnly.Checked = false;
        foreach (DataGridViewRow row in _dataGrid.Rows)
        {
            row.Visible = true;
        }

        if (_currentTableResult != null)
        {
            SetStatus($"已显示全部数据行：{_currentTableResult.Data.Rows.Count} 行");
        }
        RefreshDataGridRowStyles();
    }

    private void ExportCurrentTableFieldAnnotations()
    {
        if (_project == null || _currentTableResult == null)
        {
            MessageBox.Show(this, "请先读取一个数据表。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var currentTable = _currentTableResult.Table;
            var path = _fieldAnnotationService.ExportAnnotations(
                _project,
                currentTable,
                _currentTableResult.Validation,
                _currentTableResult.Data,
                field => _tableReferenceLookupService.BuildFieldReferenceHint(currentTable, field));
            System.Diagnostics.Debug.WriteLine("已导出字段注释：" + path);
            SetStatus("字段注释导出完成");
            MessageBox.Show(this, "字段注释导出完成：\r\n" + path, "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("字段注释导出失败：" + ex);
            MessageBox.Show(this, ex.Message, "字段注释导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ConfigureChartColumns(DataTable data)
    {
        var numericColumns = data.Columns
            .Cast<DataColumn>()
            .Where(c => c.ColumnName != "ID" && IsNumericColumn(data, c))
            .Select(c => c.ColumnName)
            .ToList();

        _chartColumnCombo.DataSource = numericColumns;
        _renderChartButton.Enabled = numericColumns.Count > 0;
        _tableChartBox.Image?.Dispose();
        _tableChartBox.Image = null;
        _tableChartInfoBox.Text = numericColumns.Count > 0 ? $"可绘制 {numericColumns.Count} 个数值列。" : "当前表未发现可绘制数值列。";
    }

    private static bool IsNumericColumn(DataTable data, DataColumn column)
    {
        var checkedRows = 0;
        foreach (DataRow row in data.Rows)
        {
            var text = Convert.ToString(row[column], CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text)) continue;
            checkedRows++;
            if (!TryParseDouble(text, out _)) return false;
            if (checkedRows >= 200) break;
        }
        return checkedRows > 0;
    }

    private static bool TryParseDouble(string? text, out double value)
    {
        text = (text ?? string.Empty).Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (ulong.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
            {
                value = hex;
                return true;
            }
        }

        return double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
    }

    private void SaveCurrentTable()
    {
        if (_project == null || _currentTableResult == null)
        {
            return;
        }

        if (!CanEditTable(_currentTableResult))
        {
            MessageBox.Show(this, "当前表不开放直接保存。装备特效名称表使用 00 分隔的变长字符串，请通过“宝物设定 -> 宝物特效”维护项目侧特效目录。", "不能保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _dataGrid.EndEdit();
        if (_currentTableResult.Data.GetChanges() == null)
        {
            MessageBox.Show(this, "当前表格没有检测到改动。", "无需保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var preview = BuildChangePreview(_currentTableResult.Data, maxItems: 40);
        if (MessageBox.Show(this,
                $"即将保存当前表到 MOD 项目：\r\n{_currentTableResult.Table.TableName}\r\n\r\n变更预览：\r\n{preview}\r\n\r\n保存前会自动备份目标文件，保存后会重新读取校验。是否继续？",
                "确认保存",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var savedTable = _currentTableResult.Table;
            var result = _tableWriter.Save(_project, savedTable, _currentTableResult.Data);
            var verifyRead = _tableReader.Read(_project, savedTable, _tables);
            if (!verifyRead.Validation.IsUsable)
            {
                throw new InvalidOperationException("保存后重新读取失败，请查看诊断和备份。");
            }

            _currentTableResult = verifyRead;
            _dataGrid.DataSource = verifyRead.Data;
            ConfigureDataGrid(verifyRead);
            ConfigureChartColumns(verifyRead.Data);
            System.Diagnostics.Debug.WriteLine($"已保存表：{result.Table.TableName}");
            System.Diagnostics.Debug.WriteLine($"写入文件：{result.FilePath}");
            System.Diagnostics.Debug.WriteLine($"写入行数：{result.RowsWritten}，变化字节数：{result.ChangedBytes}");
            System.Diagnostics.Debug.WriteLine($"保存前备份：{result.BackupPath}");
            System.Diagnostics.Debug.WriteLine($"结构化报告：{result.ReportJsonPath}");
            SetStatus($"保存完成并已复读：变化 {result.ChangedBytes} 字节");
            MessageBox.Show(this,
                $"保存完成并已重新读取校验。\r\n变化字节数：{result.ChangedBytes}\r\n备份：{result.BackupPath}\r\n结构化报告：{result.ReportJsonPath}",
                "保存完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("保存失败：" + ex);
            MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void RenderCurrentTableChart()
    {
        if (_currentTableResult == null || _chartColumnCombo.SelectedItem is not string columnName)
        {
            return;
        }

        var values = new List<double>();
        foreach (DataRow row in _currentTableResult.Data.Rows)
        {
            var text = Convert.ToString(row[columnName], CultureInfo.InvariantCulture);
            if (TryParseDouble(text, out var value)) values.Add(value);
        }

        if (values.Count == 0)
        {
            _tableChartInfoBox.Text = "没有可绘制的数据。";
            return;
        }

        var bitmap = DrawHistogram(_currentTableResult.Table.TableName, columnName, values, _tableChartBox.Width, _tableChartBox.Height);
        var old = _tableChartBox.Image;
        _tableChartBox.Image = bitmap;
        old?.Dispose();
        _tableChartInfoBox.Text = $"数量 {values.Count}，最小 {values.Min():0.##}，最大 {values.Max():0.##}，平均 {values.Average():0.##}";
    }

    private static Bitmap DrawHistogram(string tableName, string columnName, IReadOnlyList<double> values, int width, int height)
    {
        width = Math.Max(640, width);
        height = Math.Max(220, height);
        var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.White);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var textBrush = new SolidBrush(Color.FromArgb(35, 35, 35));
        using var axisPen = new Pen(Color.FromArgb(90, 90, 90));
        using var gridPen = new Pen(Color.FromArgb(230, 230, 230));
        using var barBrush = new SolidBrush(Color.FromArgb(64, 121, 191));
        using var titleFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        using var font = new Font("Microsoft YaHei UI", 8.5F);

        var plot = new Rectangle(54, 42, width - 80, height - 86);
        g.DrawString($"{tableName} - {columnName} 分布", titleFont, textBrush, 12, 10);

        var min = values.Min();
        var max = values.Max();
        var average = values.Average();
        if (Math.Abs(max - min) < 0.000001)
        {
            max = min + 1;
        }

        var binCount = Math.Clamp((int)Math.Ceiling(Math.Sqrt(values.Count)), 6, 24);
        var bins = new int[binCount];
        foreach (var value in values)
        {
            var index = (int)Math.Floor((value - min) / (max - min) * binCount);
            index = Math.Clamp(index, 0, binCount - 1);
            bins[index]++;
        }

        var maxCount = Math.Max(1, bins.Max());
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - (plot.Height * i / 4);
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            g.DrawString(((int)Math.Round(maxCount * i / 4.0)).ToString(CultureInfo.InvariantCulture), font, textBrush, 8, y - 8);
        }

        var gap = 3;
        var barWidth = Math.Max(1, (plot.Width - gap * (binCount - 1)) / binCount);
        for (var i = 0; i < binCount; i++)
        {
            var barHeight = (int)Math.Round((double)bins[i] / maxCount * plot.Height);
            var x = plot.Left + i * (barWidth + gap);
            var y = plot.Bottom - barHeight;
            g.FillRectangle(barBrush, x, y, barWidth, barHeight);
        }

        g.DrawRectangle(axisPen, plot);
        g.DrawString($"min {min:0.##}", font, textBrush, plot.Left, plot.Bottom + 8);
        g.DrawString($"max {values.Max():0.##}", font, textBrush, plot.Right - 80, plot.Bottom + 8);
        g.DrawString($"avg {average:0.##}", font, textBrush, plot.Left + plot.Width / 2 - 34, plot.Bottom + 8);
        return bitmap;
    }

    private void ExportCurrentTableCsv()
    {
        if (_currentTableResult == null) return;
        using var dialog = new SaveFileDialog
        {
            Title = "导出当前表为 CSV",
            Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
            FileName = MakeSafeFileName(_currentTableResult.Table.TableName) + ".csv"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            CsvService.Export(_currentTableResult.Data, dialog.FileName);
            System.Diagnostics.Debug.WriteLine("已导出 CSV：" + dialog.FileName);
            SetStatus("CSV 导出完成");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("CSV 导出失败：" + ex);
            MessageBox.Show(this, ex.Message, "CSV 导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportVisibleTableColumnsCsv()
    {
        if (_currentTableResult == null) return;

        _dataGrid.EndEdit();
        var visibleColumns = _dataGrid.Columns
            .Cast<DataGridViewColumn>()
            .Where(c => c.Visible && !string.IsNullOrWhiteSpace(c.DataPropertyName) && _currentTableResult.Data.Columns.Contains(c.DataPropertyName))
            .OrderBy(c => c.DisplayIndex)
            .Select(c => c.DataPropertyName)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var visibleRows = GetVisibleTableDataRowsFromGrid();
        if (visibleColumns.Count == 0)
        {
            MessageBox.Show(this, "当前没有可导出的可见列。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (visibleRows.Count == 0)
        {
            MessageBox.Show(this, "当前没有可导出的可见行。请清除行筛选或调整筛选条件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "导出当前表可见列为 CSV",
            Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
            FileName = MakeSafeFileName(_currentTableResult.Table.TableName) + "_可见列.csv"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            if (_visibleColumnsCsvWithNotes.Checked)
            {
                CsvService.ExportColumnsRowsWithAnnotationRow(_currentTableResult.Data, dialog.FileName, visibleColumns, BuildVisibleColumnCsvNotes(visibleColumns), visibleRows);
            }
            else
            {
                CsvService.ExportColumnsRows(_currentTableResult.Data, dialog.FileName, visibleColumns, visibleRows);
            }

            var noteText = _visibleColumnsCsvWithNotes.Checked ? "，含字段说明行" : string.Empty;
            System.Diagnostics.Debug.WriteLine($"已导出可见行列 CSV：{dialog.FileName}，行数 {visibleRows.Count}/{_currentTableResult.Data.Rows.Count}，列数 {visibleColumns.Count}/{_currentTableResult.Data.Columns.Count}{noteText}");
            SetStatus($"可见行列 CSV 导出完成：{visibleRows.Count} 行，{visibleColumns.Count} 列{noteText}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("可见列 CSV 导出失败：" + ex);
            MessageBox.Show(this, ex.Message, "可见列 CSV 导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private List<DataRow> GetVisibleTableDataRowsFromGrid()
    {
        var rows = new List<DataRow>();
        foreach (DataGridViewRow gridRow in _dataGrid.Rows)
        {
            if (!gridRow.Visible) continue;
            var row = TryGetDataRow(gridRow);
            if (row != null) rows.Add(row);
        }
        return rows;
    }

    private Dictionary<string, string> BuildVisibleColumnCsvNotes(IReadOnlyList<string> visibleColumns)
    {
        var notes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_currentTableResult == null) return notes;

        foreach (var columnName in visibleColumns)
        {
            if (columnName == "ID")
            {
                notes[columnName] = "行号/编号；用于回查原始数据，不直接写入二进制。";
                continue;
            }

            if (!_currentTableResult.Data.Columns.Contains(columnName) ||
                _currentTableResult.Data.Columns[columnName]!.ExtendedProperties["FieldDefinition"] is not HexFieldDefinition field)
            {
                notes[columnName] = string.Empty;
                continue;
            }

            var shortNote = _fieldAnnotationService.BuildShortFieldAnnotation(_currentTableResult.Table, field);
            var semantic = _fieldAnnotationService.GetSemanticShortName(_currentTableResult.Table, field);
            var risk = _fieldAnnotationService.GetRiskReason(_currentTableResult.Table, field);
            notes[columnName] = string.IsNullOrWhiteSpace(risk)
                ? $"{shortNote}；语义：{semantic}"
                : $"{shortNote}；语义：{semantic}；风险：{risk}";
        }

        return notes;
    }

    private static DataRow? TryGetDataRow(DataGridViewRow gridRow)
    {
        return gridRow.DataBoundItem switch
        {
            DataRowView rowView => rowView.Row,
            DataRow row => row,
            _ => null
        };
    }

    private static bool IsDataRowChanged(DataRow row)
        => row.RowState is not DataRowState.Unchanged and not DataRowState.Detached;

    private void RefreshDataGridRowStyles()
    {
        foreach (DataGridViewRow row in _dataGrid.Rows)
        {
            RefreshDataGridRowStyle(row.Index);
        }

    }

    private void RefreshDataGridRowStyle(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _dataGrid.Rows.Count) return;
        var gridRow = _dataGrid.Rows[rowIndex];
        var dataRow = TryGetDataRow(gridRow);
        if (dataRow == null) return;

        if (IsDataRowChanged(dataRow))
        {
            gridRow.DefaultCellStyle.BackColor = Color.LightCyan;
            gridRow.DefaultCellStyle.SelectionBackColor = Color.Teal;
        }
        else
        {
            gridRow.DefaultCellStyle.BackColor = Color.Empty;
            gridRow.DefaultCellStyle.SelectionBackColor = Color.Empty;
        }
    }


    private void ImportCurrentTableCsv()
    {
        if (_project == null || _currentTableResult == null) return;

        using var dialog = new OpenFileDialog
        {
            Title = "导入 CSV 到当前表",
            Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var count = CsvService.ImportInto(_currentTableResult.Data, dialog.FileName, allowPartialColumns: true, matchByIdWhenPresent: true);
            System.Diagnostics.Debug.WriteLine("已导入 CSV：" + dialog.FileName);
            SetStatus($"CSV 导入完成：更新 {count} 行，请检查变更后点击保存；保存前会自动备份。");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("CSV 导入失败：" + ex);
            MessageBox.Show(this, ex.Message, "CSV 导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string MakeSafeFileName(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(ch, '_');
        }
        return name;
    }

    private static string BuildChangePreview(DataTable data, int maxItems)
    {
        var lines = new List<string>();
        var total = 0;
        foreach (DataRow row in data.Rows)
        {
            if (row.RowState != DataRowState.Modified) continue;
            var id = data.Columns.Contains("ID") ? Convert.ToString(row["ID"], CultureInfo.InvariantCulture) : data.Rows.IndexOf(row).ToString(CultureInfo.InvariantCulture);
            for (var c = 1; c < data.Columns.Count; c++)
            {
                var original = row[c, DataRowVersion.Original];
                var current = row[c, DataRowVersion.Current];
                var originalText = Convert.ToString(original, CultureInfo.InvariantCulture) ?? string.Empty;
                var currentText = Convert.ToString(current, CultureInfo.InvariantCulture) ?? string.Empty;
                if (originalText == currentText) continue;

                total++;
                if (lines.Count < maxItems)
                {
                    lines.Add($"ID={id} {data.Columns[c].ColumnName}: {TrimPreview(originalText)} -> {TrimPreview(currentText)}");
                }
            }
        }

        if (total == 0) return "未发现单元格变更。";
        if (total > lines.Count) lines.Add($"……另有 {total - lines.Count} 项变更未显示。");
        return string.Join("\r\n", lines);
    }

    private static string TrimPreview(string value)
    {
        value = value.Replace("\r", "\\r").Replace("\n", "\\n");
        return value.Length > 40 ? value[..40] + "…" : value;
    }

    private void ValidateEditedCell(DataGridViewCellValidatingEventArgs e)
    {
        if (_dataGrid.ReadOnly || e.RowIndex < 0 || e.ColumnIndex <= 0) return;
        if (_dataGrid.Columns[e.ColumnIndex].ReadOnly) return;
        if (_dataGrid.DataSource is not DataTable data) return;
        var propertyName = _dataGrid.Columns[e.ColumnIndex].DataPropertyName;
        if (string.IsNullOrWhiteSpace(propertyName) || !data.Columns.Contains(propertyName)) return;
        if (data.Columns[propertyName]!.ExtendedProperties["FieldDefinition"] is not HexFieldDefinition field) return;

        var value = Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture) ?? string.Empty;
        var error = ValidateFieldValue(field, value, _currentPageHexButton.Checked);
        _dataGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = error ?? string.Empty;
        if (error != null)
        {
            e.Cancel = true;
            SetStatus(error);
        }
    }

    private static string? ValidateFieldValue(HexFieldDefinition field, string value, bool allowBareHex)
    {
        try
        {
            switch (field.Kind)
            {
                case HexFieldKind.UInt8:
                    return TryParseInteger(value, 0, byte.MaxValue, field.ColumnName, allowBareHex);
                case HexFieldKind.UInt16:
                    return TryParseInteger(value, 0, ushort.MaxValue, field.ColumnName, allowBareHex);
                case HexFieldKind.UInt32:
                    return TryParseUnsignedInteger(value, uint.MaxValue, field.ColumnName, allowBareHex);
                case HexFieldKind.FixedString:
                    var bytes = EncodingService.GetGbkByteCount(value);
                    return bytes <= field.Size ? null : $"字段 {field.ColumnName} 超长：GBK {bytes} 字节，容量 {field.Size} 字节。";
                case HexFieldKind.RawBytes:
                    var parts = value.Replace("-", " ").Replace(",", " ").Replace("0x", "", StringComparison.OrdinalIgnoreCase)
                        .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != field.Size) return $"字段 {field.ColumnName} 需要 {field.Size} 个十六进制字节。";
                    foreach (var part in parts) byte.Parse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    return null;
                default:
                    return null;
            }
        }
        catch (Exception ex)
        {
            return $"字段 {field.ColumnName} 无效：{ex.Message}";
        }
    }

    private static string? TryParseInteger(string value, long min, long max, string fieldName, bool allowBareHex = false)
    {
        if (!TryParseIntegerInput(value, allowBareHex, out var parsedValue) ||
            !TryConvertParsedIntegerToSigned(parsedValue, min, max, out var parsed))
        {
            return allowBareHex ? $"字段 {fieldName} 需要十六进制整数。" : $"字段 {fieldName} 需要整数。";
        }

        return parsed < min || parsed > max ? $"字段 {fieldName} 超出范围：{min}..{max}。" : null;
    }

    private static string? TryParseUnsignedInteger(string value, ulong max, string fieldName, bool allowBareHex = false)
    {
        if (!TryParseIntegerInput(value, allowBareHex, out var parsedValue) ||
            parsedValue.IsNegative)
        {
            return allowBareHex ? $"字段 {fieldName} 需要无符号十六进制整数。" : $"字段 {fieldName} 需要无符号整数。";
        }

        return parsedValue.Magnitude > max ? $"字段 {fieldName} 超出范围：0..{max}。" : null;
    }

    private void LogTableValidation(HexTableValidationResult validation)
    {
        System.Diagnostics.Debug.WriteLine(new string('-', 80));
        System.Diagnostics.Debug.WriteLine($"表：{validation.Table.Id} {validation.Table.TableName}");
        System.Diagnostics.Debug.WriteLine($"文件：{validation.FilePath}");
        System.Diagnostics.Debug.WriteLine($"文件存在：{validation.FileExists}，长度：{validation.FileLength}，结束偏移：{validation.Table.EndOffsetExclusive}");
        System.Diagnostics.Debug.WriteLine($"列/Bytes 匹配：{validation.ColumnsMatchBytes}，范围有效：{validation.FitsInFile}，保留/未命名字节：{validation.PaddingBytes}");
        foreach (var warning in validation.Warnings)
        {
            System.Diagnostics.Debug.WriteLine("警告：" + warning);
        }
    }
}
