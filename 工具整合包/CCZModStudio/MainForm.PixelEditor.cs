using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.ComponentModel;
using System.Data;
using System.Globalization;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private void EditSelectedItemIcon()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_currentItemEditorData == null || _itemEditorGrid.CurrentRow == null)
        {
            MessageBox.Show(this, "请先读取宝物/物品并选择一行。", "像素编辑", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!_itemEditorGrid.Columns.Contains("图标"))
        {
            MessageBox.Show(this, "当前物品表没有“图标”字段，无法定位图标资源。", "像素编辑", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!TryConvertToInt(_itemEditorGrid.CurrentRow.Cells["图标"].Value, out var iconIndex))
        {
            MessageBox.Show(this, "当前行“图标”字段不是有效整数。", "像素编辑", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var target = BuildItemIconEditableTarget(iconIndex);
            OpenPixelEditor(target, _itemEditorInfoBox, () =>
            {
                _itemIconPreviewService.ClearCache();
                UpdateItemIconPreview(_itemEditorGrid.CurrentRow);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("宝物图标像素编辑打开失败：" + ex);
            MessageBox.Show(this, ex.Message, "像素编辑", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void EditSelectedJobStrategyIcon()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_currentJobStrategyData == null || _jobStrategyEditorGrid.CurrentRow == null)
        {
            MessageBox.Show(this, "请先读取兵种策略并选择一行。", "像素编辑", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!_jobStrategyEditorGrid.Columns.Contains("策略图标"))
        {
            MessageBox.Show(this, "当前策略表没有“策略图标”字段，无法定位图标资源。", "像素编辑", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!TryConvertToInt(_jobStrategyEditorGrid.CurrentRow.Cells["策略图标"].Value, out var iconIndex))
        {
            MessageBox.Show(this, "当前行“策略图标”字段不是有效整数。", "像素编辑", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var target = BuildStrategyIconEditableTarget(iconIndex);
            OpenPixelEditor(target, _jobStrategyPreviewInfoBox, () =>
            {
                _itemIconPreviewService.ClearCache();
                ShowSelectedJobStrategyCell();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("策略图标像素编辑打开失败：" + ex);
            MessageBox.Show(this, ex.Message, "像素编辑", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void EditSelectedImageResourceEntry()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var entry = GetSelectedImageResourceEntry();
        if (entry == null)
        {
            MessageBox.Show(this, "请先选择一个图片资源条目。", "像素编辑", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!entry.CanReplace)
        {
            MessageBox.Show(this, "当前资源条目不开放写回，已拒绝像素编辑。", "像素编辑", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var target = BuildImageResourceEditableTarget(entry);
            var imageNumber = entry.ImageNumber;
            OpenPixelEditor(target, _imageResourceEntryInfoBox, () =>
            {
                _imageResourceCatalogService.ClearCache();
                _itemIconPreviewService.ClearCache();
                RefreshSelectedImageResourceEntries(imageNumber);
                ShowSelectedImageResourceEntry();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("图片资源条目像素编辑打开失败：" + ex);
            MessageBox.Show(this, ex.Message, "像素编辑", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void EditSelectedImageAssignmentResource(ImageAssignmentResourceKind kind)
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var row = GetSelectedImageAssignmentRow();
        if (row == null)
        {
            MessageBox.Show(this, "请先在人物形象设定页面选择一行。", "像素编辑", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!TryGetImageResourceId(row, kind, out var id))
        {
            MessageBox.Show(this, $"无法读取 {GetImageAssignmentResourceKindText(kind)} 编号。", "像素编辑", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var targets = BuildE5ImageReplacementTargets(row, kind, id);
        if (targets.Count == 0)
        {
            MessageBox.Show(this,
                $"当前 {GetImageAssignmentResourceKindText(kind)}={id} 没有可编辑的有效 E5 图片条目。\r\n请确认 Pmapobj.e5 / Unit_*.e5 已存在，且映射图号没有超过索引表条目数。",
                "像素编辑",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var selected = SelectE5ImageReplacementTarget(targets, restoreMode: false);
        if (selected == null) return;

        try
        {
            var target = BuildImageAssignmentEditableTarget(selected);
            OpenPixelEditor(target, _imageAssignmentInfoBox, () =>
            {
                ClearImageAssignmentCaches();
                _imageResourceCatalogService.ClearCache();
                ShowSelectedImageAssignmentDetail();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("人物形象像素编辑打开失败：" + ex);
            MessageBox.Show(this, ex.Message, "像素编辑", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenPixelEditor(EditableImageTarget target, TextBox infoBox, Action refreshAfterWrite)
    {
        if (_project == null) return;

        if (!File.Exists(target.TargetPath))
        {
            MessageBox.Show(this, "目标资源文件不存在：\r\n" + target.TargetPath, "像素编辑", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        EditableImageDocument document;
        try
        {
            Cursor = Cursors.WaitCursor;
            document = _editableImageCodecService.Load(_project, target);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("像素编辑载入失败：" + ex);
            MessageBox.Show(this, ex.Message, "像素编辑载入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        using (document)
        using (var dialog = new PixelImageEditorDialog(document))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            EditableImageWritePreview preview;
            try
            {
                Cursor = Cursors.WaitCursor;
                preview = _editableImageCodecService.PreviewWrite(_project, target, dialog.EditedBitmap);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("像素编辑写回预览失败：" + ex);
                MessageBox.Show(this, ex.Message, "像素编辑写回预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            var previewText = BuildPixelEditorPreviewText(preview);
            infoBox.Text = previewText;
            if (MessageBox.Show(this,
                    previewText + "\r\n\r\n确认后会先备份目标资源，再写回当前像素画布。是否继续？",
                    "确认像素编辑写回",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                Cursor = Cursors.WaitCursor;
                var result = _editableImageCodecService.Write(_project, target, dialog.EditedBitmap);
                _itemIconPreviewService.ClearCache();
                _imageResourceCatalogService.ClearCache();
                ClearImageAssignmentCaches();
                refreshAfterWrite();
                infoBox.AppendText("\r\n\r\n" + BuildPixelEditorResultText(result));
                SetStatus("像素编辑写回完成：" + result.TargetRelativePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("像素编辑写回失败：" + ex);
                MessageBox.Show(this, ex.Message, "像素编辑写回失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
    }

    private EditableImageTarget BuildItemIconEditableTarget(int iconIndex)
    {
        if (_project == null) throw new InvalidOperationException("请先打开 MOD 项目。");

        var resourceFile = Ccz66RevisedLayout.ResolveItemIconResourceFile(_project);
        var targetPath = Ccz66RevisedLayout.ResolveResourcePath(_project, resourceFile);
        if (Ccz66RevisedLayout.IsE5IconResource(resourceFile))
        {
            var (small, large) = Ccz66RevisedLayout.ResolveItemIconImageNumbers(iconIndex);
            return new EditableImageTarget
            {
                Kind = EditableImageTargetKind.E5Standard,
                DisplayName = $"宝物图标 字段={iconIndex}",
                TargetPath = targetPath,
                ImageNumber = large,
                IsItemIconPair = true,
                SmallImageNumber = small,
                LargeImageNumber = large,
                ResourceFormat = "E5 图标",
                OperationKind = "宝物图标像素编辑"
            };
        }

        return new EditableImageTarget
        {
            Kind = EditableImageTargetKind.DllBitmapIcon,
            DisplayName = $"宝物图标 字段={iconIndex}",
            TargetPath = targetPath,
            IconIndex = iconIndex,
            ResourceFormat = "DLL RT_BITMAP",
            OperationKind = "宝物图标像素编辑"
        };
    }

    private EditableImageTarget BuildStrategyIconEditableTarget(int iconIndex)
    {
        if (_project == null) throw new InvalidOperationException("请先打开 MOD 项目。");

        var resourceFile = Ccz66RevisedLayout.ResolveStrategyIconResourceFile(_project);
        var targetPath = Ccz66RevisedLayout.ResolveResourcePath(_project, resourceFile);
        if (Ccz66RevisedLayout.IsE5IconResource(resourceFile))
        {
            return new EditableImageTarget
            {
                Kind = EditableImageTargetKind.E5Standard,
                DisplayName = $"策略图标 字段={iconIndex}",
                TargetPath = targetPath,
                ImageNumber = Ccz66RevisedLayout.ResolveStrategyIconImageNumber(iconIndex),
                ResourceFormat = "E5 图标",
                OperationKind = "策略图标像素编辑"
            };
        }

        return new EditableImageTarget
        {
            Kind = EditableImageTargetKind.DllBitmapIcon,
            DisplayName = $"策略图标 字段={iconIndex}",
            TargetPath = targetPath,
            IconIndex = iconIndex,
            ResourceFormat = "DLL RT_BITMAP",
            OperationKind = "策略图标像素编辑"
        };
    }

    private static EditableImageTarget BuildImageResourceEditableTarget(ImageResourceEntryInfo entry)
    {
        if (entry.Kind.Equals("DLL图标", StringComparison.OrdinalIgnoreCase))
        {
            return new EditableImageTarget
            {
                Kind = EditableImageTargetKind.DllBitmapIcon,
                DisplayName = $"{entry.ResourceName} 图标#{entry.ImageNumber}",
                TargetPath = entry.Path,
                IconIndex = entry.ImageNumber,
                ResourceFormat = "DLL RT_BITMAP",
                OperationKind = "图片资源图标像素编辑"
            };
        }

        var rawSpec = EditableImageCodecService.TryResolveRawFrameSpec(entry.Path);
        return new EditableImageTarget
        {
            Kind = rawSpec.HasValue ? EditableImageTargetKind.E5RawStrip : EditableImageTargetKind.E5Standard,
            DisplayName = $"{entry.ResourceName} 图#{entry.ImageNumber}",
            TargetPath = entry.Path,
            ImageNumber = entry.ImageNumber,
            ResourceFormat = rawSpec.HasValue ? "E5 RAW 帧条" : entry.Kind,
            FrameWidth = rawSpec?.Width,
            FrameHeight = rawSpec?.FrameHeight,
            OperationKind = "图片资源像素编辑"
        };
    }

    private static EditableImageTarget BuildImageAssignmentEditableTarget(E5ImageReplacementTarget selected)
    {
        var rawSpec = EditableImageCodecService.TryResolveRawFrameSpec(selected.FilePath);
        return new EditableImageTarget
        {
            Kind = rawSpec.HasValue ? EditableImageTargetKind.E5RawStrip : EditableImageTargetKind.E5Standard,
            DisplayName = $"{selected.Prefix} {selected.Label} #{selected.ImageNumber}",
            TargetPath = selected.FilePath,
            ImageNumber = selected.ImageNumber,
            ResourceFormat = rawSpec.HasValue ? "E5 RAW 帧条" : selected.Kind,
            FrameWidth = rawSpec?.Width,
            FrameHeight = rawSpec?.FrameHeight,
            OperationKind = $"{selected.Prefix}形象像素编辑"
        };
    }

    private static string BuildPixelEditorPreviewText(EditableImageWritePreview preview)
    {
        if (preview.E5Preview != null)
        {
            return BuildE5BatchReplacePreviewText(preview.E5Preview) + BuildPixelEditorWarningsText(preview.Warnings);
        }

        if (preview.DllPreview != null)
        {
            return BuildDllIconBatchReplacePreviewText(preview.DllPreview) + BuildPixelEditorWarningsText(preview.Warnings);
        }

        return preview.Summary + BuildPixelEditorWarningsText(preview.Warnings);
    }

    private static string BuildPixelEditorResultText(EditableImageWriteResult result)
    {
        if (result.E5Result != null)
        {
            return BuildE5BatchReplaceResultText(result.E5Result);
        }

        if (result.DllResult != null)
        {
            return BuildDllIconBatchReplaceResultText(result.DllResult);
        }

        return result.Summary +
               $"\r\n备份：{result.BackupPath}" +
               $"\r\n报告：{result.ReportPath}";
    }

    private static string BuildPixelEditorWarningsText(IReadOnlyList<string> warnings)
    {
        var filtered = warnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return filtered.Length == 0
            ? string.Empty
            : "\r\n像素编辑提示：\r\n" + string.Join("\r\n", filtered.Take(20).Select(warning => "- " + warning));
    }

    private void RefreshSelectedImageResourceEntries(int preferredImageNumber)
    {
        var file = GetSelectedImageResourceFile();
        if (file == null) return;

        var refreshed = _imageResourceCatalogService.BuildCatalog(_project!);
        var refreshedFile = refreshed.FirstOrDefault(item => item.Path.Equals(file.Path, StringComparison.OrdinalIgnoreCase));
        if (refreshedFile != null)
        {
            _currentImageResourceFiles = refreshed;
            _currentImageResourceEntries = _imageResourceCatalogService.ReadEntries(refreshedFile);
        }
        else
        {
            _currentImageResourceEntries = _imageResourceCatalogService.ReadEntries(file);
        }

        _imageResourceEntryGrid.DataSource = new System.ComponentModel.BindingList<ImageResourceEntryInfo>(_currentImageResourceEntries.ToList());
        ConfigureImageResourceEntryGrid();
        foreach (DataGridViewRow row in _imageResourceEntryGrid.Rows)
        {
            if (row.DataBoundItem is not ImageResourceEntryInfo entry || entry.ImageNumber != preferredImageNumber) continue;
            row.Selected = true;
            _imageResourceEntryGrid.CurrentCell = row.Cells.Cast<DataGridViewCell>().FirstOrDefault(cell => cell.Visible) ?? row.Cells[0];
            break;
        }
    }
}
