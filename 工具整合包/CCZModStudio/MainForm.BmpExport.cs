using CCZModStudio.Core;
using System.Data;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private void ExportSelectedRoleFacesBmp()
    {
        var rows = GetSelectedRowsForBmpExport(_roleEditorGrid);
        var targets = BuildRoleFaceBmpExportTargets(rows, "头像");
        ExecuteBmpExport(BmpExportKind.Face, targets, CharacterImageResourceService.DefaultSPreviewFactionSlot, Array.Empty<int>(), _roleEditorInfoBox, "导出头像BMP");
    }

    private void ExportSelectedJobSImagesBmp()
    {
        var targets = new List<BmpExportTarget>();
        foreach (var row in GetSelectedRowsForBmpExport(_jobEditorGrid))
        {
            if (!TryGetJobEditorRowIdentity(row, out var jobId, out var name)) continue;
            targets.Add(new BmpExportTarget
            {
                RowId = jobId,
                DisplayName = name,
                FieldValue = 0,
                JobId = jobId
            });
        }

        ExecuteBmpExport(BmpExportKind.JobSImage, targets, CharacterImageResourceService.DefaultSPreviewFactionSlot, Array.Empty<int>(), _jobAreaPreviewInfoBox, "导出兵种S形象BMP");
    }

    private void ExportSelectedItemIconsBmp()
    {
        var targets = new List<BmpExportTarget>();
        foreach (var row in GetSelectedRowsForBmpExport(_itemEditorGrid))
        {
            var dataRow = TryGetDataRow(row);
            if (dataRow == null) continue;

            var id = ReadIntOrDefault(dataRow, "ID", targets.Count);
            var iconColumn = dataRow.Table.Columns.Contains("图标") ? "图标" : "鍥炬爣";
            if (!dataRow.Table.Columns.Contains(iconColumn) || !TryConvertToInt(dataRow[iconColumn], out var iconIndex))
            {
                continue;
            }

            targets.Add(new BmpExportTarget
            {
                RowId = id,
                DisplayName = ReadDisplayName(dataRow, "名称", "鍚嶇О"),
                FieldValue = iconIndex
            });
        }

        ExecuteBmpExport(BmpExportKind.ItemIcon, targets, CharacterImageResourceService.DefaultSPreviewFactionSlot, Array.Empty<int>(), _itemEditorInfoBox, "导出宝物图标BMP");
    }

    private void ExportSelectedJobStrategyIconsBmp()
    {
        var targets = new List<BmpExportTarget>();
        foreach (var row in GetSelectedRowsForBmpExport(_jobStrategyEditorGrid))
        {
            var dataRow = TryGetDataRow(row);
            if (dataRow == null) continue;

            if (!dataRow.Table.Columns.Contains("策略图标") || !TryConvertToInt(dataRow["策略图标"], out var iconIndex))
            {
                continue;
            }

            targets.Add(new BmpExportTarget
            {
                RowId = ReadIntOrDefault(dataRow, "ID", targets.Count),
                DisplayName = ReadDisplayName(dataRow, "名称", "鍚嶇О"),
                FieldValue = iconIndex
            });
        }

        ExecuteBmpExport(BmpExportKind.StrategyIcon, targets, CharacterImageResourceService.DefaultSPreviewFactionSlot, Array.Empty<int>(), _jobStrategyPreviewInfoBox, "导出策略图标BMP");
    }

    private void ExportSelectedImageAssignmentBmp(ImageAssignmentResourceKind kind)
    {
        var targets = new List<BmpExportTarget>();
        foreach (var row in GetSelectedRowsForBmpExport(_imageAssignmentGrid))
        {
            var dataRow = TryGetDataRow(row);
            if (dataRow == null || !TryGetImageResourceId(dataRow, kind, out var resourceId)) continue;

            var jobId = kind == ImageAssignmentResourceKind.S
                ? ReadNullableInt(dataRow, "职业", "鑱屼笟")
                : null;
            targets.Add(new BmpExportTarget
            {
                RowId = ReadIntOrDefault(dataRow, "ID", targets.Count),
                DisplayName = TryGetRoleDisplayName(dataRow),
                FieldValue = resourceId,
                JobId = jobId
            });
        }

        var exportKind = kind switch
        {
            ImageAssignmentResourceKind.Face => BmpExportKind.Face,
            ImageAssignmentResourceKind.S => BmpExportKind.SImage,
            _ => BmpExportKind.RImage
        };
        var title = kind switch
        {
            ImageAssignmentResourceKind.Face => "导出头像BMP",
            ImageAssignmentResourceKind.S => "导出S形象BMP",
            _ => "导出R形象BMP"
        };
        var stageSlots = kind == ImageAssignmentResourceKind.S
            ? SelectSImageStageSlots("选择导出 S 形象转数", "选择要导出的人物 S 形象转。三转 S 会按选择导出；单转 S 只支持第一转。")
            : Array.Empty<int>();
        if (kind == ImageAssignmentResourceKind.S && stageSlots.Count == 0) return;

        ExecuteBmpExport(exportKind, targets, GetImageAssignmentSPreviewFactionSlot(), stageSlots, _imageAssignmentInfoBox, title);
    }

    private void ExecuteBmpExport(
        BmpExportKind kind,
        IReadOnlyList<BmpExportTarget> targets,
        int factionSlot,
        IReadOnlyList<int> sImageStageSlots,
        TextBox infoBox,
        string title)
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (targets.Count == 0)
        {
            MessageBox.Show(this, "请先选择要导出的有效行。", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var singleMode = targets.Count == 1;
        using var folderDialog = new FolderBrowserDialog
        {
            Description = singleMode
                ? "选择导出目录；单行导出会直接在该目录写入可回导 BMP 文件。"
                : "选择批量导出根目录；多行导出会为每行创建一个可回导素材子目录。",
            UseDescriptionForTitle = true
        };
        if (folderDialog.ShowDialog(this) != DialogResult.OK) return;

        var overwrite = false;
        if (singleMode && Directory.Exists(folderDialog.SelectedPath) &&
            Directory.EnumerateFiles(folderDialog.SelectedPath, "*.bmp", SearchOption.TopDirectoryOnly).Any())
        {
            overwrite = MessageBox.Show(
                this,
                "所选目录中已有 BMP 文件。是否允许覆盖同名文件？选择“否”会跳过已有文件。",
                title,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes;
        }

        var request = new BmpExportRequest
        {
            Kind = kind,
            OutputRoot = folderDialog.SelectedPath,
            SingleMode = singleMode,
            OverwriteExisting = overwrite,
            FactionSlot = factionSlot,
            SImageStageSlots = sImageStageSlots,
            Targets = targets
        };

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = _bmpImageExportService.Export(_project, request);
            infoBox.Text = BuildBmpExportResultText(result);
            SetStatus($"{title}完成：导出 {result.Files.Count} 个 BMP，跳过 {result.SkippedItems.Count} 项");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(title + " failed: " + ex);
            MessageBox.Show(this, ex.Message, title + "失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private IReadOnlyList<DataGridViewRow> GetSelectedRowsForBmpExport(DataGridView grid)
    {
        var rows = grid.SelectedCells
            .Cast<DataGridViewCell>()
            .Where(cell => cell.RowIndex >= 0)
            .Select(cell => grid.Rows[cell.RowIndex])
            .Concat(grid.SelectedRows.Cast<DataGridViewRow>())
            .Where(row => !row.IsNewRow)
            .Distinct()
            .OrderBy(row => row.Index)
            .ToList();

        if (rows.Count == 0 && grid.CurrentRow is { IsNewRow: false } current)
        {
            rows.Add(current);
        }

        return rows;
    }

    private static IReadOnlyList<BmpExportTarget> BuildRoleFaceBmpExportTargets(
        IReadOnlyList<DataGridViewRow> rows,
        string faceColumnName)
    {
        var targets = new List<BmpExportTarget>();
        foreach (var row in rows)
        {
            var dataRow = TryGetDataRow(row);
            if (dataRow == null) continue;
            if (!dataRow.Table.Columns.Contains(faceColumnName) || !TryConvertToInt(dataRow[faceColumnName], out var faceId))
            {
                continue;
            }

            targets.Add(new BmpExportTarget
            {
                RowId = ReadIntOrDefault(dataRow, "ID", targets.Count),
                DisplayName = TryGetRoleDisplayName(dataRow),
                FieldValue = faceId
            });
        }

        return targets;
    }

    private static int ReadIntOrDefault(DataRow row, string columnName, int fallback)
        => row.Table.Columns.Contains(columnName) && TryConvertToInt(row[columnName], out var value) ? value : fallback;

    private static int? ReadNullableInt(DataRow row, params string[] columnNames)
    {
        foreach (var columnName in columnNames)
        {
            if (row.Table.Columns.Contains(columnName) && TryConvertToInt(row[columnName], out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static string ReadDisplayName(DataRow row, params string[] columnNames)
    {
        foreach (var columnName in columnNames)
        {
            if (!row.Table.Columns.Contains(columnName)) continue;
            var value = Convert.ToString(row[columnName], CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(value)) return value!;
        }

        return string.Empty;
    }

    private static string BuildBmpExportResultText(BmpExportResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine(result.Request.SingleMode ? "BMP一键导出完成" : "BMP批量导出完成");
        builder.AppendLine($"类型：{result.Request.Kind}");
        builder.AppendLine($"输出目录：{result.Request.OutputRoot}");
        builder.AppendLine($"导出文件：{result.Files.Count}");
        builder.AppendLine($"跳过项：{result.SkippedItems.Count}");

        foreach (var file in result.Files.Take(20))
        {
            var sourceId = file.ImageNumber.HasValue
                ? $"#{file.ImageNumber.Value}"
                : file.ResourceId.HasValue
                    ? $"RT_BITMAP {file.ResourceId.Value}"
                    : "";
            builder.AppendLine($"- {file.Role} {file.Width}x{file.Height} {sourceId} -> {file.OutputPath}");
        }

        if (result.Files.Count > 20)
        {
            builder.AppendLine($"... 其余 {result.Files.Count - 20} 个文件已省略。");
        }

        if (result.SkippedItems.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("跳过：");
            foreach (var item in result.SkippedItems.Take(20))
            {
                builder.AppendLine($"- ID={item.RowId} value={item.FieldValue} {item.DisplayName} {item.OutputPath}：{item.Reason}");
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
        }

        builder.AppendLine();
        builder.AppendLine("未生成JSON报告；导出文件为可直接回导的BMP素材。");
        return builder.ToString();
    }

    private BatchImageImportSourceSelection? SelectBatchImageImportSources(string title)
    {
        var choice = MessageBox.Show(
            this,
            "选择“是”多选图片文件；选择“否”选择导出根目录；选择“取消”放弃导入。",
            title,
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);
        if (choice == DialogResult.Cancel) return null;

        if (choice == DialogResult.No)
        {
            using var folderDialog = new FolderBrowserDialog
            {
                Description = "选择由 BMP 导出功能生成的根目录",
                UseDescriptionForTitle = true
            };
            return folderDialog.ShowDialog(this) == DialogResult.OK
                ? new BatchImageImportSourceSelection(Array.Empty<string>(), folderDialog.SelectedPath)
                : null;
        }

        using var fileDialog = new OpenFileDialog
        {
            Title = title,
            Filter = "图片文件 (*.bmp;*.jpg;*.jpeg;*.png)|*.bmp;*.jpg;*.jpeg;*.png|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };
        return fileDialog.ShowDialog(this) == DialogResult.OK && fileDialog.FileNames.Length > 0
            ? new BatchImageImportSourceSelection(
                fileDialog.FileNames
                    .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(path => path, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray(),
                string.Empty)
            : null;
    }

    private static bool UseAlignedBatchImageImportDialogs() => true;

    private sealed record BatchImageImportSourceSelection(IReadOnlyList<string> SourceFiles, string SourceRoot);
}
