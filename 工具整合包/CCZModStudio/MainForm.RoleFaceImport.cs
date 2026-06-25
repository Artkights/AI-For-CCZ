using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Data;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private void ExportSelectedRoleFacesBmp()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_currentRoleEditorData == null)
        {
            MessageBox.Show(this, "请先读取角色并选中要导出的行。", "导出头像BMP", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selectedRows = GetSelectedRoleRowsForFaceImport();
        if (selectedRows.Count == 0 && _roleEditorGrid.CurrentRow != null)
        {
            selectedRows = [_roleEditorGrid.CurrentRow];
        }

        if (selectedRows.Count == 0)
        {
            MessageBox.Show(this, "请先在角色表中选中要导出头像的行。", "导出头像BMP", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        IReadOnlyList<BatchRoleFaceTargetRow> faceTargets;
        try
        {
            faceTargets = BuildRoleFaceImportTargetRows(selectedRows);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导出头像BMP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var requestTargets = faceTargets.Select(target => new BmpExportTarget
        {
            RowId = target.RowId,
            DisplayName = target.DisplayName,
            FieldValue = target.FaceId
        }).ToArray();
        var result = ExecuteBmpExport(BmpExportKind.Face, requestTargets, "导出角色头像 BMP", "导出头像BMP", singleMode: requestTargets.Length == 1);
        if (result != null)
        {
            _roleEditorInfoBox.Text = BuildBmpExportResultText(result);
        }
    }

    private void ImportSelectedRoleFace()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_currentRoleEditorData == null || _roleEditorGrid.CurrentRow == null)
        {
            MessageBox.Show(this, "请先读取角色并选中一行。", "导入头像", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var roleRow = TryGetDataRow(_roleEditorGrid.CurrentRow);
        if (roleRow == null)
        {
            MessageBox.Show(this, "当前行无法解析为角色数据行。", "导入头像", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!_currentRoleEditorData.Columns.Contains("头像"))
        {
            MessageBox.Show(this, "当前角色表没有“头像”字段，无法定位头像资源。", "导入头像", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!TryConvertToInt(roleRow["头像"], out var faceId))
        {
            MessageBox.Show(this, "当前行“头像”字段不是有效整数。", "导入头像", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = $"选择导入到角色头像 #{faceId} 的图片",
            Filter = "图片文件 (*.bmp;*.jpg;*.jpeg;*.png)|*.bmp;*.jpg;*.jpeg;*.png|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName)) return;

        BatchRoleFaceImportPreviewResult preview;
        var request = new BatchRoleFaceImportRequest
        {
            SourceFiles = [dialog.FileName],
            TargetRows = BuildRoleFaceImportTargetRows([_roleEditorGrid.CurrentRow]),
            MatchMode = "selected-row-order",
            WriteMode = _project.IsTestCopy ? "test_copy" : "direct"
        };

        try
        {
            Cursor = Cursors.WaitCursor;
            preview = _batchRoleFaceImportService.Preview(_project, request);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("角色头像导入预览失败: " + ex);
            MessageBox.Show(this, ex.Message, "头像导入预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        var previewText = BuildBatchRoleFaceImportPreviewText(preview);
        _roleEditorInfoBox.Text = previewText;
        if (!preview.CanWrite)
        {
            MessageBox.Show(this, previewText, "头像导入存在阻断项", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (MessageBox.Show(this,
                previewText + "\r\n\r\n确认后会批量写入头像资源，并自动备份；不会修改角色表“头像”字段。是否继续？",
                "确认导入头像",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = _batchRoleFaceImportService.Replace(_project, request);
            _imageResourceCatalogService.ClearCache();
            _imageAssignmentPreviewService.ClearCache();
            ShowSelectedRoleEditorCell();
            _roleEditorInfoBox.Text = BuildBatchRoleFaceImportResultText(result);
            SetStatus($"角色头像导入完成：{result.TotalOperationCount} 条");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("角色头像导入失败: " + ex);
            MessageBox.Show(this, ex.Message, "头像导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void BatchImportSelectedRoleFaces()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_currentRoleEditorData == null)
        {
            MessageBox.Show(this, "请先读取角色设定。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selectedRows = GetSelectedRoleRowsForFaceImport();
        if (selectedRows.Count == 0)
        {
            MessageBox.Show(this, "请先在角色表中选中要导入头像的行。", "批量导入头像", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "选择要导入到所选角色头像的图片",
            Filter = "图片文件 (*.bmp;*.jpg;*.jpeg;*.png)|*.bmp;*.jpg;*.jpeg;*.png|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.FileNames.Length == 0) return;

        BatchRoleFaceImportRequest request;
        try
        {
            request = new BatchRoleFaceImportRequest
            {
                SourceFiles = dialog.FileNames.OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ThenBy(path => path, StringComparer.CurrentCultureIgnoreCase).ToArray(),
                TargetRows = BuildRoleFaceImportTargetRows(selectedRows),
                MatchMode = "auto",
                WriteMode = _project.IsTestCopy ? "test_copy" : "direct"
            };
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "批量导入头像", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        BatchRoleFaceImportPreviewResult preview;
        try
        {
            Cursor = Cursors.WaitCursor;
            preview = _batchRoleFaceImportService.Preview(_project, request);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("角色头像批量导入预览失败: " + ex);
            MessageBox.Show(this, ex.Message, "头像批量导入预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        var previewText = BuildBatchRoleFaceImportPreviewText(preview);
        _roleEditorInfoBox.Text = previewText;
        if (!preview.CanWrite)
        {
            MessageBox.Show(this, previewText, "头像批量导入存在阻断项", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (MessageBox.Show(this,
                previewText + "\r\n\r\n确认后会批量写入头像资源，并自动备份；不会修改角色表“头像”字段。是否继续？",
                "确认批量导入头像",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = _batchRoleFaceImportService.Replace(_project, request);
            _imageResourceCatalogService.ClearCache();
            _imageAssignmentPreviewService.ClearCache();
            ShowSelectedRoleEditorCell();
            _roleEditorInfoBox.Text = BuildBatchRoleFaceImportResultText(result);
            SetStatus($"角色头像批量导入完成：{result.TotalOperationCount} 条");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("角色头像批量导入失败: " + ex);
            MessageBox.Show(this, ex.Message, "头像批量导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ImportSelectedImageAssignmentFace()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_currentImageAssignments == null || _imageAssignmentGrid.CurrentRow == null)
        {
            MessageBox.Show(this, "请先读取人物 R/S 并选中一行。", "导入头像", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var targetRows = BuildImageAssignmentFaceImportTargetRows([_imageAssignmentGrid.CurrentRow]);
        if (targetRows.Count == 0)
        {
            MessageBox.Show(this, "当前行无法解析为人物 R/S 数据行。", "导入头像", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = $"选择导入到头像 #{targetRows[0].FaceId} 的图片",
            Filter = "图片文件 (*.bmp;*.jpg;*.jpeg;*.png)|*.bmp;*.jpg;*.jpeg;*.png|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName)) return;

        var request = new BatchRoleFaceImportRequest
        {
            SourceFiles = [dialog.FileName],
            TargetRows = targetRows,
            MatchMode = "selected-row-order",
            WriteMode = _project.IsTestCopy ? "test_copy" : "direct"
        };

        ExecuteImageAssignmentFaceImport(request, singleMode: true);
    }

    private void BatchImportSelectedImageAssignmentFaces()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_currentImageAssignments == null)
        {
            MessageBox.Show(this, "请先读取人物 R/S。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selectedRows = GetSelectedImageAssignmentRowsForFaceImport();
        if (selectedRows.Count == 0)
        {
            MessageBox.Show(this, "请先在人物 R/S 表中选中要导入头像的行。", "批量导入头像", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "选择要导入到所选人物头像的图片",
            Filter = "图片文件 (*.bmp;*.jpg;*.jpeg;*.png)|*.bmp;*.jpg;*.jpeg;*.png|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.FileNames.Length == 0) return;

        BatchRoleFaceImportRequest request;
        try
        {
            request = new BatchRoleFaceImportRequest
            {
                SourceFiles = dialog.FileNames.OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ThenBy(path => path, StringComparer.CurrentCultureIgnoreCase).ToArray(),
                TargetRows = BuildImageAssignmentFaceImportTargetRows(selectedRows),
                MatchMode = "auto",
                WriteMode = _project.IsTestCopy ? "test_copy" : "direct"
            };
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "批量导入头像", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ExecuteImageAssignmentFaceImport(request, singleMode: false);
    }

    private void ExecuteImageAssignmentFaceImport(BatchRoleFaceImportRequest request, bool singleMode)
    {
        if (_project == null) return;

        BatchRoleFaceImportPreviewResult preview;
        try
        {
            Cursor = Cursors.WaitCursor;
            preview = _batchRoleFaceImportService.Preview(_project, request);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("人物 R/S 头像导入预览失败: " + ex);
            MessageBox.Show(this, ex.Message, "头像导入预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        var previewText = BuildBatchRoleFaceImportPreviewText(preview);
        _imageAssignmentInfoBox.Text = previewText;
        if (!preview.CanWrite)
        {
            MessageBox.Show(this, previewText, "头像导入存在阻断项", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (MessageBox.Show(this,
                previewText + "\r\n\r\n确认后会写入 Face.e5，并自动备份；不会修改人物表“头像”字段。是否继续？",
                singleMode ? "确认导入头像" : "确认批量导入头像",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = _batchRoleFaceImportService.Replace(_project, request);
            _imageResourceCatalogService.ClearCache();
            _imageAssignmentPreviewService.ClearCache();
            ShowSelectedImageAssignmentDetail();
            _imageAssignmentInfoBox.Text = BuildBatchRoleFaceImportResultText(result);
            SetStatus($"{(singleMode ? "人物 R/S 头像导入" : "人物 R/S 头像批量导入")}完成：{result.TotalOperationCount} 条");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("人物 R/S 头像导入失败: " + ex);
            MessageBox.Show(this, ex.Message, "头像导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private IReadOnlyList<DataGridViewRow> GetSelectedRoleRowsForFaceImport()
    {
        return _roleEditorGrid.SelectedCells
            .Cast<DataGridViewCell>()
            .Where(cell => cell.RowIndex >= 0)
            .Select(cell => _roleEditorGrid.Rows[cell.RowIndex])
            .Where(row => !row.IsNewRow)
            .Distinct()
            .OrderBy(row => row.Index)
            .ToArray();
    }

    private IReadOnlyList<DataGridViewRow> GetSelectedImageAssignmentRowsForFaceImport()
    {
        var rows = _imageAssignmentGrid.SelectedCells
            .Cast<DataGridViewCell>()
            .Where(cell => cell.RowIndex >= 0)
            .Select(cell => _imageAssignmentGrid.Rows[cell.RowIndex])
            .Where(row => !row.IsNewRow)
            .Distinct()
            .OrderBy(row => row.Index)
            .ToArray();
        if (rows.Length > 0) return rows;

        return _imageAssignmentGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Where(row => !row.IsNewRow)
            .Distinct()
            .OrderBy(row => row.Index)
            .ToArray();
    }

    private static IReadOnlyList<BatchRoleFaceTargetRow> BuildRoleFaceImportTargetRows(IReadOnlyList<DataGridViewRow> selectedRows)
    {
        var targets = new List<BatchRoleFaceTargetRow>();
        foreach (var row in selectedRows)
        {
            var dataRow = TryGetDataRow(row) ?? throw new InvalidOperationException("选中行无法解析为角色数据行。");
            var roleId = Convert.ToInt32(dataRow["ID"], CultureInfo.InvariantCulture);
            var displayName = TryGetRoleDisplayName(dataRow);
            if (!dataRow.Table.Columns.Contains("头像") || !TryConvertToInt(dataRow["头像"], out var faceId))
            {
                throw new InvalidOperationException($"角色 ID={roleId} 的头像字段不是有效整数。");
            }

            targets.Add(new BatchRoleFaceTargetRow(roleId, displayName, faceId));
        }

        var duplicateFace = targets.GroupBy(target => target.FaceId).FirstOrDefault(group => group.Count() > 1);
        if (duplicateFace != null)
        {
            var ids = string.Join(", ", duplicateFace.Select(target => target.RowId.ToString(CultureInfo.InvariantCulture)));
            throw new InvalidOperationException($"选中角色中有多个条目指向同一头像编号 {duplicateFace.Key}: {ids}。请调整选择或头像字段。");
        }

        return targets;
    }

    private static IReadOnlyList<BatchRoleFaceTargetRow> BuildImageAssignmentFaceImportTargetRows(IReadOnlyList<DataGridViewRow> selectedRows)
    {
        var targets = new List<BatchRoleFaceTargetRow>();
        foreach (var row in selectedRows)
        {
            var dataRow = TryGetDataRow(row) ?? throw new InvalidOperationException("选中行无法解析为人物 R/S 数据行。");
            var roleId = Convert.ToInt32(dataRow["ID"], CultureInfo.InvariantCulture);
            var displayName = TryGetRoleDisplayName(dataRow);
            if (!dataRow.Table.Columns.Contains("头像编号") || !TryConvertToInt(dataRow["头像编号"], out var faceId))
            {
                throw new InvalidOperationException($"人物 R/S 行 ID={roleId} 的头像编号不是有效整数。");
            }

            targets.Add(new BatchRoleFaceTargetRow(roleId, displayName, faceId));
        }

        var duplicateFace = targets.GroupBy(target => target.FaceId).FirstOrDefault(group => group.Count() > 1);
        if (duplicateFace != null)
        {
            var ids = string.Join(", ", duplicateFace.Select(target => target.RowId.ToString(CultureInfo.InvariantCulture)));
            throw new InvalidOperationException($"选中人物中有多个条目指向同一头像编号 {duplicateFace.Key}: {ids}。请调整选择或头像字段。");
        }

        return targets;
    }

    private static string TryGetRoleDisplayName(DataRow dataRow)
    {
        foreach (var columnName in new[] { "名称", "姓名", "人物名称", "角色名称" })
        {
            if (!dataRow.Table.Columns.Contains(columnName)) continue;
            var value = Convert.ToString(dataRow[columnName], CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(value)) return value!;
        }

        return string.Empty;
    }

    private static string BuildBatchRoleFaceImportPreviewText(BatchRoleFaceImportPreviewResult preview)
    {
        var builder = new StringBuilder();
        builder.AppendLine("角色头像批量导入预览");
        builder.AppendLine($"目标：{preview.TargetRelativePath}");
        builder.AppendLine("格式要求：支持任意尺寸 BMP/JPG/PNG；导入时自动居中裁切并缩放为 120x120 PNG；本功能不写 Tou.dll 真彩头像。");
        builder.AppendLine($"匹配成功：{preview.Items.Count}，写入条目：{preview.TotalOperationCount}");
        builder.AppendLine($"跳过/问题：{preview.SkippedItems.Count}");
        foreach (var item in preview.Items.Take(30))
        {
            var target = string.Join("/", item.TargetImageNumbers.Select(number => "#" + number.ToString(CultureInfo.InvariantCulture)));
            var sourceSize = item.SourceWidth.HasValue && item.SourceHeight.HasValue
                ? $"{item.SourceWidth.Value}x{item.SourceHeight.Value}"
                : "?x?";
            var sourceKind = string.IsNullOrWhiteSpace(item.SourceKind) ? "未知格式" : item.SourceKind;
            builder.AppendLine($"- ID={item.RowId} {item.DisplayName} -> 头像#{item.FaceId} {target} <- {Path.GetFileName(item.SourcePath)} ({sourceKind}, {sourceSize} -> {item.OutputKind} {item.OutputWidth}x{item.OutputHeight})");
        }

        AppendBatchSkippedAndWarnings(builder, preview.SkippedItems, preview.Warnings);
        return builder.ToString();
    }

    private static string BuildBatchRoleFaceImportResultText(BatchRoleFaceImportResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("角色头像批量导入完成");
        builder.AppendLine($"目标：{result.TargetRelativePath}");
        builder.AppendLine($"写入条目：{result.TotalOperationCount}");
        builder.AppendLine($"汇总报告：{result.AggregateReportPath}");
        if (result.E5Result != null)
        {
            builder.AppendLine($"备份：{result.E5Result.BackupPath}");
            builder.AppendLine($"报告：{result.E5Result.ReportJsonPath}");
        }

        return builder.ToString();
    }

    private BmpExportResult? ExecuteBmpExport(
        BmpExportKind kind,
        IReadOnlyList<BmpExportTarget> targets,
        string dialogTitle,
        string messageTitle,
        bool singleMode,
        int? factionSlot = null)
    {
        if (_project == null) return null;
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "没有可导出的目标。", messageTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        using var folderDialog = new FolderBrowserDialog
        {
            Description = dialogTitle,
            UseDescriptionForTitle = true
        };
        if (folderDialog.ShowDialog(this) != DialogResult.OK) return null;

        var overwrite = false;
        if (singleMode)
        {
            overwrite = MessageBox.Show(this,
                "如果导出目录中已有同名 BMP 文件，是否覆盖？",
                messageTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes;
        }

        var request = new BmpExportRequest
        {
            Kind = kind,
            OutputRoot = folderDialog.SelectedPath,
            OverwriteExisting = overwrite,
            FactionSlot = factionSlot ?? CharacterImageResourceService.DefaultSPreviewFactionSlot,
            Targets = targets
        };

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = _bmpImageExportService.Export(_project, request);
            SetStatus($"BMP 导出完成：成功 {result.Files.Count} 个文件，跳过 {result.SkippedItems.Count} 项");
            if (result.Files.Count == 0)
            {
                MessageBox.Show(this, BuildBmpExportResultText(result), messageTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("BMP export failed: " + ex);
            MessageBox.Show(this, ex.Message, messageTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private static string BuildBmpExportResultText(BmpExportResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("BMP 导出完成");
        builder.AppendLine($"类型：{GetBmpExportKindText(result.Request.Kind)}");
        builder.AppendLine($"成功文件：{result.Files.Count}");
        builder.AppendLine($"跳过/问题：{result.SkippedItems.Count}");
        builder.AppendLine($"输出目录：{result.Request.OutputRoot}");
        foreach (var file in result.Files.Take(30))
        {
            var source = file.ImageNumber.HasValue
                ? $"E5 #{file.ImageNumber.Value}"
                : file.ResourceId.HasValue
                    ? $"RT_BITMAP {file.ResourceId.Value}"
                    : string.Empty;
            builder.AppendLine($"- {file.Role}: {file.Width}x{file.Height} {source} -> {file.OutputPath}");
        }

        if (result.Files.Count > 30)
        {
            builder.AppendLine($"- 还有 {result.Files.Count - 30} 个文件未显示。");
        }

        if (result.SkippedItems.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("跳过/问题：");
            foreach (var item in result.SkippedItems.Take(20))
            {
                builder.AppendLine($"- {item.RowId} {item.DisplayName} 值={item.FieldValue}: {item.Reason}");
            }

            if (result.SkippedItems.Count > 20)
            {
                builder.AppendLine($"- 还有 {result.SkippedItems.Count - 20} 项未显示。");
            }
        }

        if (result.Warnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("提示：");
            foreach (var warning in result.Warnings.Take(20))
            {
                builder.AppendLine("- " + warning);
            }

            if (result.Warnings.Count > 20)
            {
                builder.AppendLine($"- 还有 {result.Warnings.Count - 20} 条提示未显示。");
            }
        }

        builder.AppendLine();
        builder.AppendLine($"报告：{result.ReportJsonPath}");
        return builder.ToString();
    }

    private static string GetBmpExportKindText(BmpExportKind kind)
        => kind switch
        {
            BmpExportKind.ItemIcon => "宝物图标",
            BmpExportKind.StrategyIcon => "兵种策略图标",
            BmpExportKind.RImage => "R形象",
            BmpExportKind.SImage => "S形象",
            BmpExportKind.Face => "头像",
            _ => kind.ToString()
        };
}
