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
    private enum ImageAssignmentResourceKind
    {
        Face,
        R,
        S
    }

    private void LoadImageResources()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            _imageResourceCatalogService.ClearCache();
            _currentImageResourceFiles = _imageResourceCatalogService.BuildCatalog(_project);
            PopulateImageResourceCategoryFilter();
            BindImageResourceFiles(_currentImageResourceFiles);
            SetPictureBoxImage(_imageResourcePreviewBox, null);
            _imageResourceEntryInfoBox.Clear();
            var indexed = _currentImageResourceFiles.Count(x => x.SupportsE5Index);
            var replaceable = _currentImageResourceFiles.Count(x => x.CanReplace);
            var previewable = _currentImageResourceFiles.Count(x => x.SupportsPreview);
            _imageResourceInfoBox.Text =
                $"图片资源已读取：文件 {_currentImageResourceFiles.Count} 个，可预览 {previewable} 个，可读取 0x110 E5 图片索引 {indexed} 个，可替换 {replaceable} 个。\r\n" +
                "覆盖：角色头像、R/S形象、道具/策略图标、攻击/穿透范围、策略动画、Logo/Mmap/Tr/U_select/Gate/Weather 等图片资源；战场地图底图不在此模块。\r\n" +
                "支持：E5 单条替换、E5 批量导入、E5 批量清空；DLL 图标按 RT_BITMAP 资源替换/清空。";
            System.Diagnostics.Debug.WriteLine($"已读取图片资源目录：{_currentImageResourceFiles.Count} 个文件。");
            SetStatus("图片资源读取完成");
        }
        catch (Exception ex)
        {
            _imageResourceInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("图片资源读取失败：" + ex);
            MessageBox.Show(this, ex.Message, "图片资源读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void PopulateImageResourceCategoryFilter()
    {
        var previous = Convert.ToString(_imageResourceCategoryFilterCombo.SelectedItem, CultureInfo.InvariantCulture);
        _imageResourceCategoryFilterCombo.Items.Clear();
        _imageResourceCategoryFilterCombo.Items.Add("全部");
        foreach (var category in _currentImageResourceFiles.Select(x => x.Category).Distinct().OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            _imageResourceCategoryFilterCombo.Items.Add(category);
        }

        SelectComboValueOrFirst(_imageResourceCategoryFilterCombo, previous);
    }

    private void ApplyImageResourceFilter()
    {
        if (_currentImageResourceFiles.Count == 0) return;
        var category = Convert.ToString(_imageResourceCategoryFilterCombo.SelectedItem, CultureInfo.InvariantCulture) ?? "全部";
        var keyword = _imageResourceSearchBox.Text.Trim();
        var filtered = _currentImageResourceFiles.Where(item =>
            (category == "全部" || item.Category.Equals(category, StringComparison.Ordinal)) &&
            (string.IsNullOrWhiteSpace(keyword) || ImageResourceFileMatchesKeyword(item, keyword)))
            .ToList();
        BindImageResourceFiles(filtered);
        SetStatus($"图片资源筛选：{filtered.Count}/{_currentImageResourceFiles.Count}");
    }

    private void ClearImageResourceFilter()
    {
        _imageResourceSearchBox.Clear();
        if (_imageResourceCategoryFilterCombo.Items.Count > 0) _imageResourceCategoryFilterCombo.SelectedIndex = 0;
        BindImageResourceFiles(_currentImageResourceFiles);
        SetStatus("已显示全部图片资源");
    }

    private static bool ImageResourceFileMatchesKeyword(ImageResourceFileInfo item, string keyword)
        => item.DisplayName.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
           item.FileName.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
           item.Aliases.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
           item.Usage.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
           item.Status.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
           item.KindSummary.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);

    private void BindImageResourceFiles(IEnumerable<ImageResourceFileInfo> rows)
    {
        _imageResourceFileGrid.DataSource = new BindingList<ImageResourceFileInfo>(rows.ToList());
        ConfigureImageResourceFileGrid();
        _currentImageResourceEntries = Array.Empty<ImageResourceEntryInfo>();
        _imageResourceEntryGrid.DataSource = null;
        SetPictureBoxImage(_imageResourcePreviewBox, null);
        _imageResourceEntryInfoBox.Clear();
    }

    private void ConfigureImageResourceFileGrid()
    {
        foreach (DataGridViewColumn column in _imageResourceFileGrid.Columns)
        {
            column.ReadOnly = true;

            column.HeaderText = column.DataPropertyName switch
            {
                nameof(ImageResourceFileInfo.Category) => "分类",
                nameof(ImageResourceFileInfo.DisplayName) => "资源",
                nameof(ImageResourceFileInfo.FileName) => "文件",
                nameof(ImageResourceFileInfo.Aliases) => "别名",
                nameof(ImageResourceFileInfo.Exists) => "存在",
                nameof(ImageResourceFileInfo.SizeBytes) => "大小",
                nameof(ImageResourceFileInfo.EntryCount) => "条目",
                nameof(ImageResourceFileInfo.SupportsE5Index) => "E5索引",
                nameof(ImageResourceFileInfo.SupportsPreview) => "可预览",
                nameof(ImageResourceFileInfo.CanReplace) => "可替换",
                nameof(ImageResourceFileInfo.ResourceFormat) => "类型",
                nameof(ImageResourceFileInfo.KindSummary) => "格式",
                nameof(ImageResourceFileInfo.Status) => "状态",
                _ => column.HeaderText
            };
        }
        HideNonAuthoringColumns(
            _imageResourceFileGrid,
            nameof(ImageResourceFileInfo.FileName),
            nameof(ImageResourceFileInfo.Aliases),
            nameof(ImageResourceFileInfo.Exists),
            nameof(ImageResourceFileInfo.SizeBytes),
            nameof(ImageResourceFileInfo.SupportsE5Index),
            nameof(ImageResourceFileInfo.SupportsPreview),
            nameof(ImageResourceFileInfo.CanReplace),
            nameof(ImageResourceFileInfo.ResourceFormat),
            nameof(ImageResourceFileInfo.KindSummary),
            nameof(ImageResourceFileInfo.Status),
            nameof(ImageResourceFileInfo.Path),
            nameof(ImageResourceFileInfo.RelativePath),
            nameof(ImageResourceFileInfo.SafetyNote),
            nameof(ImageResourceFileInfo.Usage));
    }

    private void ConfigureImageResourceEntryGrid()
    {
        foreach (DataGridViewColumn column in _imageResourceEntryGrid.Columns)
        {
            column.ReadOnly = true;

            column.HeaderText = column.DataPropertyName switch
            {
                nameof(ImageResourceEntryInfo.ImageNumber) => "编号",
                nameof(ImageResourceEntryInfo.IndexOffset) => "索引偏移",
                nameof(ImageResourceEntryInfo.DataOffset) => "数据偏移",
                nameof(ImageResourceEntryInfo.StoredLength) => "存储",
                nameof(ImageResourceEntryInfo.DecodedLength) => "解码",
                nameof(ImageResourceEntryInfo.IsCompressed) => "压缩",
                nameof(ImageResourceEntryInfo.Kind) => "格式",
                nameof(ImageResourceEntryInfo.Usage) => "用途候选",
                _ => column.HeaderText
            };
        }
        HideNonAuthoringColumns(
            _imageResourceEntryGrid,
            nameof(ImageResourceEntryInfo.Path),
            nameof(ImageResourceEntryInfo.ResourceKey),
            nameof(ImageResourceEntryInfo.Category),
            nameof(ImageResourceEntryInfo.ResourceName),
            nameof(ImageResourceEntryInfo.FileName),
            nameof(ImageResourceEntryInfo.CanReplace));
    }

    private ImageResourceFileInfo? GetSelectedImageResourceFile()
    {
        if (_imageResourceFileGrid.CurrentRow?.DataBoundItem is ImageResourceFileInfo current) return current;
        if (_imageResourceFileGrid.SelectedRows.Count > 0 && _imageResourceFileGrid.SelectedRows[0].DataBoundItem is ImageResourceFileInfo selected) return selected;
        return null;
    }

    private ImageResourceEntryInfo? GetSelectedImageResourceEntry()
    {
        if (_imageResourceEntryGrid.CurrentRow?.DataBoundItem is ImageResourceEntryInfo current) return current;
        if (_imageResourceEntryGrid.SelectedRows.Count > 0 && _imageResourceEntryGrid.SelectedRows[0].DataBoundItem is ImageResourceEntryInfo selected) return selected;
        return null;
    }

    private IReadOnlyList<ImageResourceEntryInfo> GetSelectedImageResourceEntries()
    {
        var rows = _imageResourceEntryGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.DataBoundItem as ImageResourceEntryInfo)
            .Where(item => item != null)
            .Cast<ImageResourceEntryInfo>()
            .GroupBy(item => item.ImageNumber)
            .Select(group => group.First())
            .OrderBy(item => item.ImageNumber)
            .ToList();
        if (rows.Count > 0) return rows;

        var current = GetSelectedImageResourceEntry();
        return current == null ? Array.Empty<ImageResourceEntryInfo>() : new[] { current };
    }

    private void ShowSelectedImageResourceFile()
    {
        if (_project == null) return;
        var item = GetSelectedImageResourceFile();
        if (item == null) return;

        _currentImageResourceEntries = _imageResourceCatalogService.ReadEntries(item);
        _imageResourceEntryGrid.DataSource = new BindingList<ImageResourceEntryInfo>(_currentImageResourceEntries.ToList());
        ConfigureImageResourceEntryGrid();
        SetPictureBoxImage(_imageResourcePreviewBox, null);
        _imageResourceInfoBox.Text =
            $"资源：{item.DisplayName}\r\n" +
            $"分类：{item.Category}    文件：{item.FileName}    别名：{(string.IsNullOrWhiteSpace(item.Aliases) ? "无" : item.Aliases)}\r\n" +
            $"用途：{item.Usage}\r\n" +
            $"状态：{item.Status}    条目：{item.EntryCount}    类型：{item.ResourceFormat}    格式：{(string.IsNullOrWhiteSpace(item.KindSummary) ? "未识别" : item.KindSummary)}\r\n" +
            $"路径：{item.Path}\r\n" +
            $"安全边界：{item.SafetyNote}";
        _imageResourceEntryInfoBox.Text = item.SupportsPreview
            ? item.SupportsE5Index
                ? "选择下方编号可预览；替换只更新选中的 0x110 E5 索引条目，批量导入按文件名数字或选中行匹配。"
                : "选择下方编号可预览；DLL 图标可替换/清空对应 RT_BITMAP 位图资源。"
            : "当前文件没有可读取/预览的图片条目，暂不开放条目替换。";
        SetStatus($"图片资源：{item.DisplayName}");
    }

    private void ShowSelectedImageResourceEntry()
    {
        if (_project == null) return;
        var entry = GetSelectedImageResourceEntry();
        if (entry == null) return;

        Bitmap? bitmap = null;
        try
        {
            bitmap = _imageResourceCatalogService.RenderEntryPreview(_project, entry);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("图片资源条目预览失败：" + ex);
        }

        SetPictureBoxImage(_imageResourcePreviewBox, bitmap);
        _imageResourceEntryInfoBox.Text = entry.Kind.Equals("DLL图标", StringComparison.OrdinalIgnoreCase)
            ? $"{entry.ResourceName}  编号 #{entry.ImageNumber}\r\n" +
              $"用途候选：{entry.Usage}\r\n" +
              $"来源：{entry.FileName}\r\n" +
              $"路径：{entry.Path}\r\n" +
              (bitmap == null
                  ? "预览：已生成编号条目，但 DLL 图标解析/渲染失败。"
                  : "预览：已复用宝物/策略图标预览服务按字段编号渲染。")
            : $"{entry.ResourceName}  图号 #{entry.ImageNumber}\r\n" +
              $"用途候选：{entry.Usage}\r\n" +
              $"索引偏移：{HexDisplayFormatter.FormatOffset(entry.IndexOffset)}    数据偏移：{HexDisplayFormatter.FormatOffset(entry.DataOffset)}\r\n" +
              $"大小：stored={entry.StoredLength:N0}    decoded={entry.DecodedLength:N0}    格式={entry.Kind}    压缩={entry.IsCompressed}\r\n" +
              $"路径：{entry.Path}\r\n" +
              (bitmap == null
                  ? "预览：条目存在，但不是当前可直接解码/渲染的 BMP/JPG/PNG/已知 RAW 帧。"
                  : "预览：已按 E5 条目内容渲染。");
        SetStatus($"图片条目：{entry.FileName} #{entry.ImageNumber}");
    }

    private void OpenSelectedImageResourceLocation()
    {
        var item = GetSelectedImageResourceFile();
        if (item == null)
        {
            MessageBox.Show(this, "请先选择一个图片资源文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        OpenFileLocation(File.Exists(item.Path) ? item.Path : Path.GetDirectoryName(item.Path) ?? item.Path);
    }

    private void ImportOrReplaceSelectedImageResourceEntry(bool restoreMode)
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var entry = GetSelectedImageResourceEntry();
        if (entry == null)
        {
            MessageBox.Show(this, "请先选择一个 E5 图片条目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!entry.CanReplace)
        {
            MessageBox.Show(this, "当前资源不开放直接替换。请先确认该资源的写回规则。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (entry.Kind.Equals("DLL图标", StringComparison.OrdinalIgnoreCase))
        {
            ImportOrReplaceSelectedDllIconEntry(entry, restoreMode);
            return;
        }

        var target = new E5ImageReplacementTarget(
            1,
            entry.Category,
            $"{entry.ResourceName} #{entry.ImageNumber}",
            entry.Path,
            entry.ImageNumber,
            entry.IndexOffset,
            entry.DataOffset,
            entry.StoredLength,
            entry.Kind,
            entry.Usage);

        string sourcePath;
        E5ImageReplacePreviewResult preview;
        if (restoreMode)
        {
            var backupRoot = Path.Combine(_project.GameRoot, "_CCZModStudio_Backups");
            using var dialog = new OpenFileDialog
            {
                Title = $"选择包含图 #{entry.ImageNumber} 的备份 E5 文件",
                InitialDirectory = Directory.Exists(backupRoot) ? backupRoot : _project.GameRoot,
                Filter = "E5 文件 (*.e5)|*.e5|所有文件 (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            sourcePath = dialog.FileName;

            try
            {
                Cursor = Cursors.WaitCursor;
                preview = _e5ImageReplaceService.PreviewReplacementFromEntry(_project, entry.Path, entry.ImageNumber, sourcePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("图片资源 E5 还原预览失败：" + ex);
                MessageBox.Show(this, ex.Message, "E5 还原预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
        else
        {
            using var dialog = new OpenFileDialog
            {
                Title = $"选择导入到 {entry.FileName} 图 #{entry.ImageNumber} 的图片",
                Filter = "图片条目 (*.bmp;*.jpg;*.jpeg;*.png)|*.bmp;*.jpg;*.jpeg;*.png|所有文件 (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            sourcePath = dialog.FileName;

            try
            {
                Cursor = Cursors.WaitCursor;
                preview = _e5ImageReplaceService.PreviewReplacement(_project, entry.Path, entry.ImageNumber, sourcePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("图片资源 E5 替换预览失败：" + ex);
                MessageBox.Show(this, ex.Message, "E5 替换预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        var previewText = BuildE5ImageReplacePreviewText(target, preview, restoreMode);
        _imageResourceEntryInfoBox.Text = previewText;
        if (MessageBox.Show(this,
                previewText + "\r\n\r\n确认后会先备份目标 E5 文件，再写入该单个图片条目。是否继续？",
                restoreMode ? "确认还原 E5 图片条目" : "确认替换 E5 图片条目",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = restoreMode
                ? _e5ImageReplaceService.ReplaceFromEntry(_project, entry.Path, entry.ImageNumber, sourcePath)
                : _e5ImageReplaceService.Replace(_project, entry.Path, entry.ImageNumber, sourcePath);
            _imageResourceCatalogService.ClearCache();
            LoadImageResources();
            _imageResourceEntryInfoBox.Text = BuildE5ImageReplaceResultText(result);
            System.Diagnostics.Debug.WriteLine($"图片资源 E5 条目{(restoreMode ? "还原" : "替换")}完成：{result.TargetRelativePath} #{result.ImageNumber}");
            SetStatus(restoreMode ? "图片资源 E5 条目还原完成" : "图片资源 E5 条目替换完成");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("图片资源 E5 条目写入失败：" + ex);
            MessageBox.Show(this, ex.Message, restoreMode ? "E5 还原失败" : "E5 替换失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ImportOrReplaceSelectedDllIconEntry(ImageResourceEntryInfo entry, bool restoreMode)
    {
        if (_project == null) return;
        if (restoreMode)
        {
            MessageBox.Show(this, "DLL 图标还原请使用保存前生成的备份文件恢复整文件，或用“替换E5条目/导入”重新导入图片。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = $"选择导入到 {entry.FileName} 图标 #{entry.ImageNumber} 的图片",
            Filter = "图片文件 (*.bmp;*.jpg;*.jpeg;*.png)|*.bmp;*.jpg;*.jpeg;*.png|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        IconResourceReplacePreviewResult preview;
        try
        {
            Cursor = Cursors.WaitCursor;
            preview = _iconResourceReplaceService.PreviewReplaceBitmapIcon(_project, entry.Path, entry.ImageNumber, dialog.FileName);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("DLL 图标替换预览失败：" + ex);
            MessageBox.Show(this, ex.Message, "DLL 图标替换预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        var previewText = BuildDllIconReplacePreviewText(preview);
        _imageResourceEntryInfoBox.Text = previewText;
        if (MessageBox.Show(this,
                previewText + "\r\n\r\n确认后会先备份目标 DLL，再写入对应 RT_BITMAP 位图资源。是否继续？",
                "确认替换 DLL 图标",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = _iconResourceReplaceService.ReplaceBitmapIcon(_project, entry.Path, entry.ImageNumber, dialog.FileName);
            _imageResourceCatalogService.ClearCache();
            LoadImageResources();
            _imageResourceEntryInfoBox.Text = BuildDllIconReplaceResultText(result);
            System.Diagnostics.Debug.WriteLine($"DLL 图标替换完成：{result.TargetRelativePath} #{result.IconIndex}");
            SetStatus("DLL 图标替换完成");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("DLL 图标替换失败：" + ex);
            MessageBox.Show(this, ex.Message, "DLL 图标替换失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void BatchImportSelectedImageResourceEntries()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var file = GetSelectedImageResourceFile();
        if (file == null || !file.CanReplace)
        {
            MessageBox.Show(this, "请先选择一个可替换的图片资源文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = $"选择批量导入到 {file.FileName} 的图片",
            Filter = "图片文件 (*.bmp;*.jpg;*.jpeg;*.png)|*.bmp;*.jpg;*.jpeg;*.png|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.FileNames.Length == 0) return;

        if (file.ResourceFormat.Equals("DLL图标", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "DLL 图标当前先开放单个编号替换和批量删除。批量导入请逐个导入，以便确认 RT_BITMAP 尺寸缩放结果。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var entries = _currentImageResourceEntries.Count > 0 ? _currentImageResourceEntries : _imageResourceCatalogService.ReadEntries(file);
        var selectedEntries = GetSelectedImageResourceEntries();
        var requests = BuildBatchImportRequests(entries, selectedEntries, dialog.FileNames);
        if (requests.Count == 0)
        {
            MessageBox.Show(this, "没有从文件名或选中行中匹配到可导入的图号。文件名建议包含目标编号，例如 12.png 或 #12.png。", "批量导入", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        E5ImageBatchReplacePreviewResult preview;
        try
        {
            Cursor = Cursors.WaitCursor;
            preview = _e5ImageReplaceService.PreviewBatchReplacement(_project, file.Path, requests);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("E5 批量导入预览失败：" + ex);
            MessageBox.Show(this, ex.Message, "E5 批量导入预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        var previewText = BuildE5BatchReplacePreviewText(preview);
        _imageResourceEntryInfoBox.Text = previewText;
        if (MessageBox.Show(this,
                previewText + "\r\n\r\n确认后会先备份目标 E5，再一次写入这些条目。是否继续？",
                "确认批量导入 E5 图片",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = _e5ImageReplaceService.ReplaceBatch(_project, file.Path, requests);
            _imageResourceCatalogService.ClearCache();
            LoadImageResources();
            _imageResourceEntryInfoBox.Text = BuildE5BatchReplaceResultText(result);
            System.Diagnostics.Debug.WriteLine($"E5 批量导入完成：{result.TargetRelativePath} count={result.OperationCount}");
            SetStatus($"E5 批量导入完成：{result.OperationCount} 条");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("E5 批量导入失败：" + ex);
            MessageBox.Show(this, ex.Message, "E5 批量导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void BatchClearSelectedImageResourceEntries()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var file = GetSelectedImageResourceFile();
        if (file == null || !file.CanReplace)
        {
            MessageBox.Show(this, "请先选择一个可替换的图片资源文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var selectedEntries = GetSelectedImageResourceEntries().Where(x => x.CanReplace).ToList();
        if (selectedEntries.Count == 0)
        {
            MessageBox.Show(this, "请先在条目列表中选择要删除/清空的编号。", "批量删除", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (MessageBox.Show(this,
                $"即将清空 {file.FileName} 中 {selectedEntries.Count} 个选中条目。\r\n\r\nE5：写入 1x1 透明 PNG 占位，不重排索引。\r\nDLL：写入透明 RT_BITMAP，占用原资源 ID。\r\n\r\n是否继续？",
                "确认批量删除/清空图片条目",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        if (file.ResourceFormat.Equals("DLL图标", StringComparison.OrdinalIgnoreCase))
        {
            ClearSelectedDllIconEntries(file, selectedEntries);
            return;
        }

        var transparentPng = BuildTransparentPngBytes();
        var requests = selectedEntries
            .Select(entry => new E5ImageBatchReplaceRequest
            {
                ImageNumber = entry.ImageNumber,
                SourceBytes = transparentPng,
                SourceLabel = "<1x1透明PNG>",
                OperationKind = "删除/清空"
            })
            .ToList();

        try
        {
            Cursor = Cursors.WaitCursor;
            var preview = _e5ImageReplaceService.PreviewBatchReplacement(_project, file.Path, requests);
            var result = _e5ImageReplaceService.ReplaceBatch(_project, file.Path, requests);
            _imageResourceCatalogService.ClearCache();
            LoadImageResources();
            _imageResourceEntryInfoBox.Text = BuildE5BatchReplaceResultText(result);
            System.Diagnostics.Debug.WriteLine($"E5 批量删除/清空完成：{preview.TargetRelativePath} count={result.OperationCount}");
            SetStatus($"E5 批量删除/清空完成：{result.OperationCount} 条");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("E5 批量删除/清空失败：" + ex);
            MessageBox.Show(this, ex.Message, "E5 批量删除/清空失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ClearSelectedDllIconEntries(ImageResourceFileInfo file, IReadOnlyList<ImageResourceEntryInfo> selectedEntries)
    {
        if (_project == null) return;
        var results = new List<IconResourceReplaceResult>();
        try
        {
            Cursor = Cursors.WaitCursor;
            foreach (var entry in selectedEntries)
            {
                results.Add(_iconResourceReplaceService.ClearBitmapIcon(_project, file.Path, entry.ImageNumber));
            }

            _imageResourceCatalogService.ClearCache();
            LoadImageResources();
            _imageResourceEntryInfoBox.Text =
                $"DLL 图标批量删除/清空完成：{file.FileName}\r\n" +
                $"条目：{results.Count}\r\n" +
                $"编号：{string.Join(", ", results.Select(x => x.IconIndex))}\r\n" +
                $"最后备份：{results.LastOrDefault()?.BackupPath}\r\n" +
                $"最后报告：{results.LastOrDefault()?.ReportJsonPath}\r\n" +
                "说明：每个编号均写入透明 RT_BITMAP，占用原资源 ID；如需撤销，请使用保存前生成的备份文件恢复对应 DLL。";
            System.Diagnostics.Debug.WriteLine($"DLL 图标批量删除/清空完成：{file.FileName} count={results.Count}");
            SetStatus($"DLL 图标批量删除/清空完成：{results.Count} 条");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("DLL 图标批量删除/清空失败：" + ex);
            MessageBox.Show(this, ex.Message, "DLL 图标批量删除/清空失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private static IReadOnlyList<E5ImageBatchReplaceRequest> BuildBatchImportRequests(
        IReadOnlyList<ImageResourceEntryInfo> entries,
        IReadOnlyList<ImageResourceEntryInfo> selectedEntries,
        IReadOnlyList<string> fileNames)
    {
        var entryByNumber = entries.ToDictionary(x => x.ImageNumber);
        if (selectedEntries.Count == fileNames.Count)
        {
            return selectedEntries
                .OrderBy(x => x.ImageNumber)
                .Zip(fileNames.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase), (entry, fileName) => new E5ImageBatchReplaceRequest
                {
                    ImageNumber = entry.ImageNumber,
                    SourcePath = fileName,
                    SourceLabel = fileName,
                    OperationKind = "批量导入"
                })
                .ToList();
        }

        var requests = new List<E5ImageBatchReplaceRequest>();
        foreach (var fileName in fileNames.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            var number = ExtractImageNumberFromFileName(Path.GetFileNameWithoutExtension(fileName));
            if (!number.HasValue || !entryByNumber.ContainsKey(number.Value)) continue;
            requests.Add(new E5ImageBatchReplaceRequest
            {
                ImageNumber = number.Value,
                SourcePath = fileName,
                SourceLabel = fileName,
                OperationKind = "批量导入"
            });
        }

        return requests
            .GroupBy(x => x.ImageNumber)
            .Select(group => group.First())
            .OrderBy(x => x.ImageNumber)
            .ToList();
    }

    private static int? ExtractImageNumberFromFileName(string fileName)
    {
        var matches = Regex.Matches(fileName, @"\d+");
        if (matches.Count == 0) return null;
        var value = matches[^1].Value;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    private static byte[] BuildTransparentPngBytes()
    {
        using var bitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        bitmap.SetPixel(0, 0, Color.Transparent);
        using var memory = new MemoryStream();
        bitmap.Save(memory, ImageFormat.Png);
        return memory.ToArray();
    }

    private static string BuildE5BatchReplacePreviewText(E5ImageBatchReplacePreviewResult preview)
        => $"E5 批量写入预览：{preview.TargetRelativePath}\r\n" +
           $"操作条目：{preview.OperationCount}\r\n" +
           $"文件大小：{preview.OldFileSizeBytes:N0} -> {preview.NewFileSizeBytes:N0}（{preview.FileSizeDeltaBytes:+#;-#;0} 字节）\r\n" +
           $"估算变化字节：{preview.ChangedBytesEstimate:N0}\r\n" +
           $"条目：{string.Join(", ", preview.Operations.Take(30).Select(x => $"#{x.ImageNumber} {x.OldKind}->{x.NewKind}"))}{(preview.OperationCount > 30 ? " ..." : string.Empty)}\r\n" +
           $"提示：{(preview.FormatWarnings.Count == 0 ? "无" : string.Join("；", preview.FormatWarnings))}\r\n" +
           $"风险：{preview.RiskSummary}";

    private static string BuildE5BatchReplaceResultText(E5ImageBatchReplaceResult result)
        => BuildE5BatchReplacePreviewText(result) + "\r\n" +
           $"备份：{result.BackupPath}\r\n" +
           $"报告：{result.ReportJsonPath}";

    private static string BuildDllIconReplacePreviewText(IconResourceReplacePreviewResult preview)
        => $"DLL 图标写入预览：{preview.TargetRelativePath}\r\n" +
           $"编号：#{preview.IconIndex}    资源ID：{string.Join(", ", preview.ResourceIds)}\r\n" +
           $"操作：{preview.OperationKind}\r\n" +
           $"来源：{preview.SourcePath}\r\n" +
           $"来源尺寸：{(preview.SourceWidth.HasValue ? $"{preview.SourceWidth}x{preview.SourceHeight}" : "无")}\r\n" +
           $"目标大小：{preview.OldFileSizeBytes:N0} 字节\r\n" +
           $"提示：{(preview.FormatWarnings.Count == 0 ? "无" : string.Join("；", preview.FormatWarnings))}\r\n" +
           $"风险：{preview.RiskSummary}";

    private static string BuildDllIconReplaceResultText(IconResourceReplaceResult result)
        => BuildDllIconReplacePreviewText(result) + "\r\n" +
           $"新大小：{result.NewFileSizeBytes:N0} 字节    变化字节：{result.ChangedBytesEstimate:N0}\r\n" +
           $"备份：{result.BackupPath}\r\n" +
           $"报告：{result.ReportJsonPath}";

    private void NormalizeRoleRawImages()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        E5RoleRawNormalizePreviewResult preview;
        try
        {
            Cursor = Cursors.WaitCursor;
            preview = _e5RoleRawNormalizeService.Preview(_project);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("角色图片统一 RAW 预览失败：" + ex);
            MessageBox.Show(this, ex.Message, "角色图片统一 RAW 预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        var previewText = BuildRoleRawNormalizePreviewText(preview);
        _imageResourceEntryInfoBox.Text = previewText;
        if (preview.ConvertCount == 0)
        {
            MessageBox.Show(this, "没有找到需要转换为 RAW 的角色图片条目。", "角色图片统一 RAW", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirmText = previewText + "\r\n\r\n确认后会批量转换 Pmapobj.e5 / Unit_*.e5 中可转换的标准图片条目，并自动备份。是否继续？";
        var icon = _project.IsTestCopy ? MessageBoxIcon.Question : MessageBoxIcon.Warning;
        if (MessageBox.Show(this, confirmText, "确认角色图片统一 RAW", MessageBoxButtons.YesNo, icon) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = _e5RoleRawNormalizeService.Normalize(_project);
            _imageResourceCatalogService.ClearCache();
            LoadImageResources();
            _imageResourceEntryInfoBox.Text = BuildRoleRawNormalizeResultText(result);
            System.Diagnostics.Debug.WriteLine($"角色图片统一 RAW 完成：convert={result.ConvertCount} skip={result.SkipCount} report={result.AggregateReportPath}");
            SetStatus($"角色图片统一 RAW 完成：转换 {result.ConvertCount} 条，跳过 {result.SkipCount} 条");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("角色图片统一 RAW 写入失败：" + ex);
            MessageBox.Show(this, ex.Message, "角色图片统一 RAW 写入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private static string BuildRoleRawNormalizePreviewText(E5RoleRawNormalizePreviewResult preview)
        => "角色图片统一 RAW 预览\r\n" +
           $"转换：{preview.ConvertCount} 条    跳过：{preview.SkipCount} 条\r\n" +
           string.Join("\r\n", preview.Files.Select(file => $"{file.TargetFileName}: 转换 {file.ConvertCount}，跳过 {file.SkipCount}")) +
           "\r\n提示：" + (preview.Warnings.Count == 0 ? "无" : string.Join("；", preview.Warnings));

    private static string BuildRoleRawNormalizeResultText(E5RoleRawNormalizeResult result)
        => "角色图片统一 RAW 完成\r\n" +
           $"转换：{result.ConvertCount} 条    跳过：{result.SkipCount} 条\r\n" +
           string.Join("\r\n", result.Files.Select(file => $"{file.TargetFileName}: 转换 {file.ConvertCount}，跳过 {file.SkipCount}")) +
           $"\r\n汇总报告：{result.AggregateReportPath}";

    private void ExportImageResourceEntriesCsv()
    {
        if (_currentImageResourceEntries.Count == 0)
        {
            MessageBox.Show(this, "当前没有可导出的图片条目。请先读取图片资源并选择一个 E5 文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ExportGridItemsCsv<ImageResourceEntryInfo>(_imageResourceEntryGrid, "导出图片资源条目", "图片资源E5条目.csv", "ImageResourceEntries", "图片资源E5条目");
    }

    private void LoadImageAssignments()
    {
        if (_project == null || _tables.Count == 0)
        {
            MessageBox.Show(this, "请先加载项目和表定义。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            _currentImageAssignments = _imageAssignmentService.Load(_project, _tables);
            _imageAssignmentSearchBox.Clear();
            _imageAssignmentMissingOnlyCheckBox.Checked = false;
            _imageAssignmentGrid.DataSource = _currentImageAssignments;
            _clearImageAssignmentFilterButton.Enabled = false;
            var canEdit = true;
            _imageAssignmentGrid.ReadOnly = !canEdit;
            _saveImageAssignmentsButton.Enabled = canEdit;
            foreach (DataGridViewColumn column in _imageAssignmentGrid.Columns)
            {
                column.ReadOnly = !canEdit || column.DataPropertyName is "ID" or "名称" or "头像编号" or "职业" or "职业名称" or "R资源状态" or "S资源状态";
                if (column.DataPropertyName == "头像编号")
                {
                    column.Width = 76;
                    column.ToolTipText = "来自人物表“头像”字段；右侧会尝试从 E5\\Face.e5 抽取对应 PNG 头像作为人物确认预览。";
                }
                if (column.DataPropertyName == "职业")
                {
                    column.Width = 60;
                    column.ToolTipText = "来自人物表“职业”字段；S=0 默认兵种预览会用 职业*3+阵营槽 计算 Unit 图号。";
                }
                if (column.DataPropertyName == "职业名称")
                {
                    column.Width = 110;
                    column.ToolTipText = "根据 6.5-4 详细兵种自动引用的职业名称。";
                }
                if (column.DataPropertyName is "R资源状态" or "S资源状态")
                {
                    column.Visible = false;
                    column.Width = 150;
                    column.ToolTipText = "R：根据 Pmapobj.e5 正/反图号解释编号；S：按紧凑编号映射到 Unit_atk/mov/spc.e5。预览按 E5 0x110 索引表取图。";
                }
            }
            ColorImageAssignmentResourceRows();
            var missingR = _currentImageAssignments.AsEnumerable().Count(row => CharacterImageResourceService.IsMissingStatus(Convert.ToString(row["R资源状态"], CultureInfo.InvariantCulture) ?? string.Empty));
            var missingS = _currentImageAssignments.AsEnumerable().Count(row => CharacterImageResourceService.IsMissingStatus(Convert.ToString(row["S资源状态"], CultureInfo.InvariantCulture) ?? string.Empty));
            _imageAssignmentSummaryText =
                $"已读取人物 R/S 形象联动表：{_currentImageAssignments.Rows.Count} 行。\r\n" +
                $"资源解释检查：R 未定位 {missingR} 项，S 未/部分定位 {missingS} 项。\r\n" +
                "右侧显示人物表头像预览，并按 E5 0x110 索引表显示 R/S 形象预览：R=n 取 Pmapobj.e5 图 2n+1；S=0 按职业和预览阵营取默认兵种图，S=1..32 取三转特殊三张图，S>=33 取一转特殊单张图。当前项目可直接编辑 R形象编号 / S形象编号，保存时会写入 Ekd5.exe，保存前自动备份，保存后复读校验。";
            _imageAssignmentInfoBox.Text = _imageAssignmentSummaryText;
            ShowSelectedImageAssignmentDetail();
            System.Diagnostics.Debug.WriteLine("已读取人物 R/S 形象联动表。");
            SetStatus("人物 R/S 形象读取完成");
        }
        catch (Exception ex)
        {
            _imageAssignmentInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("读取人物 R/S 形象失败：" + ex);
            MessageBox.Show(this, ex.Message, "读取人物 R/S 形象失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ApplyImageAssignmentFilter()
    {
        if (_currentImageAssignments == null)
        {
            return;
        }

        var keyword = _imageAssignmentSearchBox.Text.Trim();
        var missingOnly = _imageAssignmentMissingOnlyCheckBox.Checked;
        if (string.IsNullOrWhiteSpace(keyword) && !missingOnly)
        {
            _currentImageAssignments.DefaultView.RowFilter = string.Empty;
            _clearImageAssignmentFilterButton.Enabled = false;
            ColorImageAssignmentResourceRows();
            ShowSelectedImageAssignmentDetail();
            SetStatus("人物 R/S 筛选已清除");
            return;
        }

        var filters = new List<string>();
        if (missingOnly)
        {
            filters.Add("([R资源状态] LIKE '缺失*' OR [S资源状态] LIKE '缺失*')");
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var escaped = EscapeDataViewLikeValue(keyword);
            var searchableColumns = new[] { "ID", "名称", "头像编号", "职业", "职业名称", "R形象编号", "S形象编号", "R资源状态", "S资源状态" }
                .Where(name => _currentImageAssignments.Columns.Contains(name))
                .Select(name => $"CONVERT([{name}], 'System.String') LIKE '*{escaped}*'")
                .ToList();
            if (searchableColumns.Count > 0)
            {
                filters.Add("(" + string.Join(" OR ", searchableColumns) + ")");
            }
        }

        _currentImageAssignments.DefaultView.RowFilter = string.Join(" AND ", filters);
        _clearImageAssignmentFilterButton.Enabled = true;
        ColorImageAssignmentResourceRows();
        ShowSelectedImageAssignmentDetail();
        SetStatus($"人物 R/S 筛选：显示 {_currentImageAssignments.DefaultView.Count}/{_currentImageAssignments.Rows.Count} 行");
    }

    private void ClearImageAssignmentFilter()
    {
        _imageAssignmentSearchBox.Clear();
        if (_imageAssignmentMissingOnlyCheckBox.Checked)
        {
            _imageAssignmentMissingOnlyCheckBox.Checked = false;
        }

        if (_currentImageAssignments != null)
        {
            _currentImageAssignments.DefaultView.RowFilter = string.Empty;
        }

        _clearImageAssignmentFilterButton.Enabled = false;
        ColorImageAssignmentResourceRows();
        ShowSelectedImageAssignmentDetail();
        SetStatus("人物 R/S 筛选已清除，已显示全部人物。");
    }

    private void UpdateImageAssignmentResourceStatus(int rowIndex)
    {
        if (_project == null || _currentImageAssignments == null || rowIndex < 0 || rowIndex >= _imageAssignmentGrid.Rows.Count) return;
        if (_imageAssignmentGrid.Rows[rowIndex].DataBoundItem is not DataRowView rowView) return;
        var row = rowView.Row;
        if (!_currentImageAssignments.Columns.Contains("R资源状态") || !_currentImageAssignments.Columns.Contains("S资源状态")) return;

        var r = Convert.ToInt32(row["R形象编号"], CultureInfo.InvariantCulture);
        var s = Convert.ToInt32(row["S形象编号"], CultureInfo.InvariantCulture);
        row["R资源状态"] = ImageAssignmentService.GetImageResourceStatus(_project, "R", r);
        row["S资源状态"] = ImageAssignmentService.GetImageResourceStatus(_project, "S", s);
        ColorImageAssignmentResourceRow(_imageAssignmentGrid.Rows[rowIndex]);
        SetStatus($"资源检查：R={row["R资源状态"]}，S={row["S资源状态"]}");
        ShowSelectedImageAssignmentDetail();
    }

    private void ColorImageAssignmentResourceRows()
    {
        foreach (DataGridViewRow row in _imageAssignmentGrid.Rows)
        {
            ColorImageAssignmentResourceRow(row);
        }
    }

    private static void ColorImageAssignmentResourceRow(DataGridViewRow gridRow)
    {
        if (gridRow.DataBoundItem is not DataRowView rowView) return;
        var rStatus = Convert.ToString(rowView.Row["R资源状态"], CultureInfo.InvariantCulture) ?? string.Empty;
        var sStatus = Convert.ToString(rowView.Row["S资源状态"], CultureInfo.InvariantCulture) ?? string.Empty;
        gridRow.DefaultCellStyle.BackColor =
            rStatus.StartsWith("缺失", StringComparison.Ordinal) || sStatus.StartsWith("缺失", StringComparison.Ordinal)
                ? Color.MistyRose
                : Color.Empty;
    }

    private void OpenRsDirectory()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "\u8bf7\u5148\u52a0\u8f7d\u9879\u76ee\u3002", "\u63d0\u793a", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var rsDir = Path.Combine(_project.GameRoot, "RS");
        if (!Directory.Exists(rsDir))
        {
            MessageBox.Show(this, "\u627e\u4e0d\u5230 RS \u76ee\u5f55\uff1a" + rsDir, "\u63d0\u793a", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = rsDir,
                UseShellExecute = true
            });
            System.Diagnostics.Debug.WriteLine("\u5df2\u6253\u5f00 RS \u76ee\u5f55\uff1a" + rsDir);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("\u6253\u5f00 RS \u76ee\u5f55\u5931\u8d25\uff1a" + ex);
            MessageBox.Show(this, ex.Message, "\u6253\u5f00 RS \u76ee\u5f55\u5931\u8d25", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowSelectedImageAssignmentDetail()
    {
        if (_currentImageAssignments == null || _project == null)
        {
            _imageAssignmentInfoBox.Text = _imageAssignmentSummaryText;
            ClearImageAssignmentPreview();
            return;
        }

        var row = GetSelectedImageAssignmentRow();
        if (row == null)
        {
            _imageAssignmentInfoBox.Text = _imageAssignmentSummaryText;
            ClearImageAssignmentPreview();
            return;
        }

        var id = Convert.ToString(row["ID"], CultureInfo.InvariantCulture) ?? string.Empty;
        var name = Convert.ToString(row["\u540d\u79f0"], CultureInfo.InvariantCulture) ?? string.Empty;
        var faceId = row.Table.Columns.Contains("头像编号") ? Convert.ToInt32(row["头像编号"], CultureInfo.InvariantCulture) : (int?)null;
        var jobId = row.Table.Columns.Contains("职业") ? Convert.ToInt32(row["职业"], CultureInfo.InvariantCulture) : (int?)null;
        var jobName = row.Table.Columns.Contains("职业名称") ? Convert.ToString(row["职业名称"], CultureInfo.InvariantCulture) ?? string.Empty : string.Empty;
        TryGetImageResourceId(row, "R", out var rId);
        TryGetImageResourceId(row, "S", out var sId);
        var sFactionSlot = GetImageAssignmentSPreviewFactionSlot();
        var sMapping = CharacterImageResourceService.ResolveSUnitImageMapping(sId, jobId, sFactionSlot);
        var rPath = ImageAssignmentService.GetImageResourcePath(_project, "R", rId);
        var sPath = ImageAssignmentService.GetImageResourcePath(_project, "S", sId);
        var rStatus = Convert.ToString(row["R\u8d44\u6e90\u72b6\u6001"], CultureInfo.InvariantCulture) ?? string.Empty;
        var sStatus = Convert.ToString(row["S\u8d44\u6e90\u72b6\u6001"], CultureInfo.InvariantCulture) ?? string.Empty;
        var detail =
            $"\u5f53\u524d\u9009\u4e2d\uff1aID={id}  \u540d\u79f0={name}\r\n" +
            $"头像={faceId?.ToString(CultureInfo.InvariantCulture) ?? "未读取"}  来源：人物表“头像”字段，预览来自 E5\\Face.e5\r\n" +
            $"职业={jobId?.ToString(CultureInfo.InvariantCulture) ?? "未读取"} {jobName}  S预览阵营={CharacterImageResourceService.BuildSPreviewFactionText(sFactionSlot)}\r\n" +
            $"R={rId:00}  {rStatus}  \u8def\u5f84\uff1a{rPath}\r\n" +
            $"S={sId:00}  {sStatus}  \u8def\u5f84\uff1a{sPath}\r\n" +
            $"S图号映射：{sMapping.Detail}\r\n" +
            "R/S 实图预览：按 E5 0x110 索引表取图；R=n -> Pmapobj.e5 图 2n+1；S 按默认兵种/三转特殊/一转特殊紧凑映射取 Unit 图。裸扫图片不会再显示。\r\n" +
            "\u63d0\u793a\uff1a\u5f53\u524d\u5355\u5143\u683c\u5728 S \u5217\u65f6\uff0c\u201c\u5b9a\u4f4d\u9009\u4e2d\u8d44\u6e90\u201d\u4f1a\u5b9a\u4f4d S\uff1b\u5176\u4ed6\u5217\u9ed8\u8ba4\u5b9a\u4f4d R\u3002";

        _imageAssignmentInfoBox.Text = string.IsNullOrWhiteSpace(_imageAssignmentSummaryText)
            ? detail
            : _imageAssignmentSummaryText + "\r\n\r\n" + detail;
        RefreshSelectedImageAssignmentPreview(name, rId, sId, faceId, jobId, sFactionSlot);
    }

    private void RefreshSelectedImageAssignmentPreview(string personName, int rId, int sId, int? faceId, int? jobId, int sFactionSlot)
    {
        if (_project == null)
        {
            ClearImageAssignmentPreview();
            return;
        }

        try
        {
            // R/S preview uses the E5 image index at 0x110; do not fall back to raw magic-order scans.
            SetPictureBoxImage(_imageAssignmentFacePreviewBox,
                faceId.HasValue ? _imageAssignmentPreviewService.TryRenderFaceImage(_project, faceId.Value) : null);
            SetPictureBoxImage(_imageAssignmentRPreviewBox, _imageAssignmentPreviewService.TryRenderCharacterResourceImage(_project, "R", rId));
            SetPictureBoxImage(_imageAssignmentSPreviewBox, _imageAssignmentPreviewService.TryRenderCharacterResourceImage(_project, "S", sId, jobId, sFactionSlot));
        }
        catch (Exception ex)
        {
            ClearImageAssignmentPreview();
            System.Diagnostics.Debug.WriteLine("ImageAssignment preview failed: " + ex);
        }
    }

    private void ClearImageAssignmentPreview()
    {
        SetPictureBoxImage(_imageAssignmentFacePreviewBox, null);
        SetPictureBoxImage(_imageAssignmentRPreviewBox, null);
        SetPictureBoxImage(_imageAssignmentSPreviewBox, null);
    }

    private int GetImageAssignmentSPreviewFactionSlot()
    {
        var selected = _imageAssignmentSPreviewFactionCombo.SelectedIndex + 1;
        return CharacterImageResourceService.NormalizeSPreviewFactionSlot(selected);
    }

    private string BuildCompactImageAssignmentInfo(string prefix, int id)
    {
        if (_project == null) return string.Empty;
        prefix = prefix.Equals("S", StringComparison.OrdinalIgnoreCase) ? "S" : "R";
        var resolver = new CharacterImageResourceService();
        var status = prefix == "S" ? resolver.BuildSStatus(_project, id) : resolver.BuildRStatus(_project, id);
        var fileText = File.Exists(status.Path) ? "已找到" : "未找到";
        return $"{prefix}={id:00}  {status.Status}  {status.ResourceName}\r\n{fileText}：{status.Path}\r\n{status.Detail}";
    }

    private static void SetPictureBoxImage(PictureBox box, Image? image)
    {
        var old = box.Image;
        box.Image = image;
        old?.Dispose();
    }

    private void LocateSelectedImageResource()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "\u8bf7\u5148\u52a0\u8f7d\u9879\u76ee\u3002", "\u63d0\u793a", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var row = GetSelectedImageAssignmentRow();
        if (row == null)
        {
            MessageBox.Show(this, "\u8bf7\u5148\u5728\u4eba\u7269 R/S \u9875\u9009\u62e9\u4e00\u884c\u3002", "\u63d0\u793a", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var targetKind = GetPreferredImageAssignmentResourceKind();
        if (!TryGetImageResourceId(row, targetKind, out var id))
        {
            MessageBox.Show(this, $"无法读取 {GetImageAssignmentResourceKindText(targetKind)} 编号。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var path = ImageAssignmentService.GetImageResourcePath(_project, ResourceKindToPrefix(targetKind), id);
        if (File.Exists(path))
        {
            OpenFileLocation(path);
            SetStatus($"已定位 {GetImageAssignmentResourceKindText(targetKind)} 资源：{Path.GetFileName(path)}");
            return;
        }

        OpenFileLocation(_project.GameRoot);
        MessageBox.Show(this,
            $"当前选中的 {GetImageAssignmentResourceKindText(targetKind)} 资源未定位：{Path.GetFileName(path)}\r\n已为你打开项目根目录。头像请检查 E5\\Face.e5；R 形象请检查 Pmapobj.e5；S 形象请检查 Unit_atk.e5 / Unit_mov.e5 / Unit_spc.e5。",
            "资源未定位",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private void ImportOrReplaceSelectedImageResource(bool restoreMode)
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var row = GetSelectedImageAssignmentRow();
        if (row == null)
        {
            MessageBox.Show(this, "请先在人物 R/S 页面选择一行。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var targetKind = GetPreferredImageAssignmentResourceKind();
        if (!TryGetImageResourceId(row, targetKind, out var id))
        {
            MessageBox.Show(this, $"无法读取 {GetImageAssignmentResourceKindText(targetKind)} 编号。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var targets = BuildE5ImageReplacementTargets(row, targetKind, id);
        if (targets.Count == 0)
        {
            MessageBox.Show(this,
                $"当前 {GetImageAssignmentResourceKindText(targetKind)}={id} 没有可替换的有效 E5 图片条目。\r\n请确认 Face.e5 / Pmapobj.e5 / Unit_*.e5 已存在，且映射图号没有超过索引表条目数。",
                "没有可替换条目",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var target = SelectE5ImageReplacementTarget(targets, restoreMode);
        if (target == null) return;

        string sourcePath;
        E5ImageReplacePreviewResult preview;
        if (restoreMode)
        {
            var backupRoot = Path.Combine(_project.GameRoot, "_CCZModStudio_Backups");
            using var dialog = new OpenFileDialog
            {
                Title = $"选择包含图 #{target.ImageNumber} 的备份 E5 文件",
                InitialDirectory = Directory.Exists(backupRoot) ? backupRoot : _project.GameRoot,
                Filter = "E5 文件 (*.e5)|*.e5|所有文件 (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            sourcePath = dialog.FileName;

            try
            {
                Cursor = Cursors.WaitCursor;
                preview = _e5ImageReplaceService.PreviewReplacementFromEntry(_project, target.FilePath, target.ImageNumber, sourcePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("E5 条目还原预览失败：" + ex);
                MessageBox.Show(this, ex.Message, "E5 还原预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
        else
        {
            using var dialog = new OpenFileDialog
            {
                Title = $"选择导入到 {Path.GetFileName(target.FilePath)} 图 #{target.ImageNumber} 的图片",
                Filter = "图片条目 (*.bmp;*.jpg;*.jpeg;*.png)|*.bmp;*.jpg;*.jpeg;*.png|所有文件 (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            sourcePath = dialog.FileName;

            try
            {
                Cursor = Cursors.WaitCursor;
                preview = _e5ImageReplaceService.PreviewReplacement(_project, target.FilePath, target.ImageNumber, sourcePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("E5 条目替换预览失败：" + ex);
                MessageBox.Show(this, ex.Message, "E5 替换预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        var previewText = BuildE5ImageReplacePreviewText(target, preview, restoreMode);
        _imageAssignmentInfoBox.Text = previewText;
        System.Diagnostics.Debug.WriteLine($"E5 条目{(restoreMode ? "还原" : "替换")}预览：{preview.TargetRelativePath} #{preview.ImageNumber} <- {preview.SourcePath}");

        if (MessageBox.Show(this,
                previewText + "\r\n\r\n确认后会先备份目标 E5 文件，再写入该单个图片条目。是否继续？",
                restoreMode ? "确认还原 E5 图片条目" : "确认替换 E5 图片条目",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = restoreMode
                ? _e5ImageReplaceService.ReplaceFromEntry(_project, target.FilePath, target.ImageNumber, sourcePath)
                : _e5ImageReplaceService.Replace(_project, target.FilePath, target.ImageNumber, sourcePath);
            _imageAssignmentPreviewService.ClearCache();
            ShowSelectedImageAssignmentDetail();
            _imageAssignmentInfoBox.AppendText("\r\n\r\n" + BuildE5ImageReplaceResultText(result));
            System.Diagnostics.Debug.WriteLine($"E5 条目{(restoreMode ? "还原" : "替换")}完成：{result.TargetRelativePath} #{result.ImageNumber}，备份 {result.BackupPath}，结构化报告 {result.ReportJsonPath}");
            SetStatus(restoreMode ? "E5 图片条目还原完成" : "E5 图片条目替换完成");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("E5 条目写入失败：" + ex);
            MessageBox.Show(this, ex.Message, restoreMode ? "E5 还原失败" : "E5 替换失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ReplaceSelectedRImageSet()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var row = GetSelectedImageAssignmentRow();
        if (row == null)
        {
            MessageBox.Show(this, "请先在人物 R/S 页面选择一行。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!TryGetImageResourceId(row, ImageAssignmentResourceKind.R, out var rImageId))
        {
            MessageBox.Show(this, "无法读取当前人物的 R 形象编号。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (rImageId < 0)
        {
            MessageBox.Show(this, $"R形象编号不能小于 0：{rImageId}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var roleId = row.Table.Columns.Contains("ID")
            ? Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture)
            : (int?)null;

        using var folderDialog = new FolderBrowserDialog
        {
            Description = "选择包含 front.bmp / back.bmp 的 R 形象素材目录，素材尺寸必须为 48x1280。",
            UseDescriptionForTitle = true
        };
        if (folderDialog.ShowDialog(this) != DialogResult.OK) return;

        var request = new RImageReplaceRequest
        {
            RImageId = rImageId,
            MaterialFolder = folderDialog.SelectedPath,
            CharacterId = roleId,
            WriteMode = _project.IsTestCopy ? "test_copy" : "direct"
        };

        RImageReplacePreviewResult preview;
        try
        {
            Cursor = Cursors.WaitCursor;
            preview = _rImageReplaceService.Preview(_project, request);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("一键替换 R 形象预览失败：" + ex);
            MessageBox.Show(this, ex.Message, "一键替换 R 形象预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        var previewText = BuildRImageReplacePreviewText(preview);
        _imageAssignmentInfoBox.Text = previewText;
        if (MessageBox.Show(this,
                previewText + "\r\n\r\n确认后会把 front.bmp / back.bmp 转为 RAW 并写入 Pmapobj.e5，写入前自动备份。是否继续？",
                "确认一键替换 R 形象",
                MessageBoxButtons.YesNo,
                _project.IsTestCopy ? MessageBoxIcon.Question : MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = _rImageReplaceService.Replace(_project, request);
            _imageAssignmentPreviewService.ClearCache();
            ShowSelectedImageAssignmentDetail();
            _imageAssignmentInfoBox.AppendText("\r\n\r\n" + BuildRImageReplaceResultText(result));
            System.Diagnostics.Debug.WriteLine($"一键替换 R 形象完成：R={rImageId} count={result.TotalOperationCount} report={result.AggregateReportPath}");
            SetStatus($"一键替换 R 形象完成：写入 {result.TotalOperationCount} 条");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("一键替换 R 形象写入失败：" + ex);
            MessageBox.Show(this, ex.Message, "一键替换 R 形象写入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private static string BuildRImageReplacePreviewText(RImageReplacePreviewResult preview)
        => "一键替换 R 形象预览\r\n" +
           $"{preview.Mapping.Detail}\r\n" +
           $"素材目录：{preview.Request.MaterialFolder}\r\n" +
           $"写入条目：{preview.TotalOperationCount} 条\r\n" +
           string.Join("\r\n", preview.Files.Select(file =>
               $"{file.Role}: {Path.GetFileName(file.SourcePath)} {file.Encode.SourceWidth}x{file.Encode.SourceHeight} -> RAW {file.Encode.RawLength:N0} bytes；图号 #{file.ImageNumber}")) +
           "\r\n提示：" + (preview.Warnings.Count == 0 ? "无" : string.Join("；", preview.Warnings));

    private static string BuildRImageReplaceResultText(RImageReplaceResult result)
        => "一键替换 R 形象完成\r\n" +
           $"{result.Mapping.Detail}    写入条目：{result.TotalOperationCount} 条\r\n" +
           string.Join("\r\n", result.Files.Select(file =>
               $"{file.Role}: Pmapobj.e5 #{file.ImageNumber} <- {Path.GetFileName(file.SourcePath)}")) +
           $"\r\n备份：{result.WriteResult.BackupPath}" +
           $"\r\n汇总报告：{result.AggregateReportPath}";

    private void ReplaceSelectedSImageSet()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var row = GetSelectedImageAssignmentRow();
        if (row == null)
        {
            MessageBox.Show(this, "请先在人物 R/S 页面选择一行。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!TryGetImageResourceId(row, ImageAssignmentResourceKind.S, out var sImageId))
        {
            MessageBox.Show(this, "无法读取当前人物的 S 形象编号。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var jobId = row.Table.Columns.Contains("职业")
            ? Convert.ToInt32(row["职业"], CultureInfo.InvariantCulture)
            : (int?)null;
        var roleId = row.Table.Columns.Contains("ID")
            ? Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture)
            : (int?)null;

        using var folderDialog = new FolderBrowserDialog
        {
            Description = "选择包含 mov.bmp / atk.bmp / spc.bmp 的 S 形象素材目录",
            UseDescriptionForTitle = true
        };
        if (folderDialog.ShowDialog(this) != DialogResult.OK) return;

        var request = new SImageReplaceRequest
        {
            SImageId = sImageId,
            MaterialFolder = folderDialog.SelectedPath,
            CharacterId = roleId,
            JobId = jobId,
            FactionSlot = GetImageAssignmentSPreviewFactionSlot(),
            WriteMode = _project.IsTestCopy ? "test_copy" : "direct"
        };

        SImageReplacePreviewResult preview;
        try
        {
            Cursor = Cursors.WaitCursor;
            preview = _sImageReplaceService.Preview(_project, request);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("一键替换 S 形象预览失败：" + ex);
            MessageBox.Show(this, ex.Message, "一键替换 S 形象预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        var previewText = BuildSImageReplacePreviewText(preview);
        _imageAssignmentInfoBox.Text = previewText;
        if (MessageBox.Show(this,
                previewText + "\r\n\r\n确认后会把三条 BMP 转为 RAW 并写入 Unit_atk.e5 / Unit_mov.e5 / Unit_spc.e5，写入前自动备份。是否继续？",
                "确认一键替换 S 形象",
                MessageBoxButtons.YesNo,
                _project.IsTestCopy ? MessageBoxIcon.Question : MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = _sImageReplaceService.Replace(_project, request);
            _imageAssignmentPreviewService.ClearCache();
            ShowSelectedImageAssignmentDetail();
            _imageAssignmentInfoBox.AppendText("\r\n\r\n" + BuildSImageReplaceResultText(result));
            System.Diagnostics.Debug.WriteLine($"一键替换 S 形象完成：S={sImageId} count={result.TotalOperationCount} report={result.AggregateReportPath}");
            SetStatus($"一键替换 S 形象完成：写入 {result.TotalOperationCount} 条");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("一键替换 S 形象写入失败：" + ex);
            MessageBox.Show(this, ex.Message, "一键替换 S 形象写入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private static string BuildSImageReplacePreviewText(SImageReplacePreviewResult preview)
        => "一键替换 S 形象预览\r\n" +
           $"S={preview.Request.SImageId}    映射：{preview.Mapping.Detail}\r\n" +
           $"素材目录：{preview.Request.MaterialFolder}\r\n" +
           $"写入条目：{preview.TotalOperationCount} 条\r\n" +
           string.Join("\r\n", preview.Files.Select(file =>
               $"{file.TargetFileName}: {Path.GetFileName(file.SourcePath)} {file.Encode.SourceWidth}x{file.Encode.SourceHeight} -> RAW {file.Encode.RawLength:N0} bytes；图号 {string.Join(", ", file.BatchPreview.Operations.Select(op => "#" + op.ImageNumber.ToString(CultureInfo.InvariantCulture)))}")) +
           "\r\n提示：" + (preview.Warnings.Count == 0 ? "无" : string.Join("；", preview.Warnings));

    private static string BuildSImageReplaceResultText(SImageReplaceResult result)
        => "一键替换 S 形象完成\r\n" +
           $"S={result.Request.SImageId}    写入条目：{result.TotalOperationCount} 条\r\n" +
           string.Join("\r\n", result.Files.Select(file =>
               $"{file.TargetFileName}: {file.WriteResult.OperationCount} 条，备份 {file.WriteResult.BackupPath}")) +
           $"\r\n汇总报告：{result.AggregateReportPath}";

    private IReadOnlyList<E5ImageReplacementTarget> BuildE5ImageReplacementTargets(DataRow row, ImageAssignmentResourceKind targetKind, int id)
    {
        if (_project == null) return Array.Empty<E5ImageReplacementTarget>();
        var result = new List<E5ImageReplacementTarget>();

        if (targetKind == ImageAssignmentResourceKind.Face)
        {
            var path = CharacterImageResourceService.ResolveFaceFile(_project) ?? Path.Combine(_project.GameRoot, "E5", "Face.e5");
            var faceMapping = new CharacterImageResourceService().MapFaceId(id);
            foreach (var imageNumber in faceMapping.FaceImageNumbers)
            {
                AddE5ImageReplacementTarget(result, $"头像#{imageNumber}", "Face", path, imageNumber, $"头像编号={id} -> Face.e5 图 #{imageNumber}");
            }

            return result;
        }

        if (targetKind == ImageAssignmentResourceKind.R)
        {
            if (id < 0) return result;
            var path = CharacterImageResourceService.ResolveGameFile(_project, "Pmapobj.e5");
            AddE5ImageReplacementTarget(result, "R正面", "R", path, checked(id * 2 + 1), $"R={id} 正面图");
            AddE5ImageReplacementTarget(result, "R反面", "R", path, checked(id * 2 + 2), $"R={id} 反面图");
            return result;
        }

        var jobId = row.Table.Columns.Contains("职业") ? Convert.ToInt32(row["职业"], CultureInfo.InvariantCulture) : (int?)null;
        var sFactionSlot = GetImageAssignmentSPreviewFactionSlot();
        var mapping = CharacterImageResourceService.ResolveSUnitImageMapping(id, jobId, sFactionSlot);
        var unitFiles = new[]
        {
            ("移动", "Unit_mov.e5"),
            ("攻击", "Unit_atk.e5"),
            ("特技", "Unit_spc.e5")
        };

        foreach (var imageNumber in mapping.ImageNumbers)
        {
            foreach (var (action, fileName) in unitFiles)
            {
                var path = CharacterImageResourceService.ResolveGameFile(_project, fileName);
                AddE5ImageReplacementTarget(result, $"{action}#{imageNumber}", "S", path, imageNumber, $"S={id} -> Unit 图 #{imageNumber} {action}");
            }
        }

        return result;
    }

    private void AddE5ImageReplacementTarget(List<E5ImageReplacementTarget> result, string label, string prefix, string path, int imageNumber, string detail)
    {
        if (!File.Exists(path)) return;
        var entries = _e5ImageReplaceService.ReadIndex(path);
        if (imageNumber <= 0 || imageNumber > entries.Count) return;
        var entry = entries[imageNumber - 1];
        var sizeText = entry.IsCompressed
            ? $"stored={entry.StoredLength:N0} / decoded={entry.DecodedLength:N0}"
            : $"size={entry.Length:N0}";
        result.Add(new E5ImageReplacementTarget(
            result.Count + 1,
            prefix,
            label,
            path,
            imageNumber,
            entry.IndexOffset,
            entry.DataOffset,
            entry.Length,
            entry.Kind,
            $"{detail}；index={HexDisplayFormatter.FormatOffset(entry.IndexOffset)}；offset={HexDisplayFormatter.FormatOffset(entry.DataOffset)}；{sizeText}；kind={entry.Kind}"));
    }

    private E5ImageReplacementTarget? SelectE5ImageReplacementTarget(IReadOnlyList<E5ImageReplacementTarget> targets, bool restoreMode)
    {
        if (targets.Count == 1) return targets[0];

        using var dialog = new Form
        {
            Text = restoreMode ? "选择要还原的 E5 图片条目" : "选择要替换的 E5 图片条目",
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = true,
            ShowInTaskbar = false
        };
        ApplyAdaptiveDialogSizing(dialog, new Size(980, 520), new Size(760, 420));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(10)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        dialog.Controls.Add(layout);

        layout.Controls.Add(new Label
        {
            Text = "请选择一个具体 E5 文件和图号。工具只会替换这一条索引，不会自动猜测其它动作帧。",
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 8)
        }, 0, 0);

        var table = new DataTable();
        table.Columns.Add("序号", typeof(int));
        table.Columns.Add("条目", typeof(string));
        table.Columns.Add("E5文件", typeof(string));
        table.Columns.Add("图号", typeof(int));
        table.Columns.Add("格式", typeof(string));
        table.Columns.Add("大小", typeof(int));
        table.Columns.Add("说明", typeof(string));
        foreach (var target in targets)
        {
            table.Rows.Add(target.Index, target.Label, Path.GetFileName(target.FilePath), target.ImageNumber, target.Kind, target.OldSizeBytes, target.Detail);
        }

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            DataSource = table
        };
        layout.Controls.Add(grid, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0)
        };
        var okButton = new Button { Text = "选择", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(96, 32) };
        var cancelButton = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(96, 32) };
        buttons.Controls.Add(okButton);
        buttons.Controls.Add(cancelButton);
        layout.Controls.Add(buttons, 0, 2);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        if (grid.Rows.Count > 0)
        {
            grid.Rows[0].Selected = true;
            grid.CurrentCell = grid.Rows[0].Cells[0];
        }

        if (dialog.ShowDialog(this) != DialogResult.OK) return null;
        if (grid.CurrentRow == null) return null;
        var selectedTargetIndex = Convert.ToInt32(grid.CurrentRow.Cells["序号"].Value, CultureInfo.InvariantCulture);
        return targets.FirstOrDefault(target => target.Index == selectedTargetIndex);
    }

    private static string BuildE5ImageReplacePreviewText(E5ImageReplacementTarget target, E5ImageReplacePreviewResult preview, bool restoreMode)
    {
        var sourceSize = preview.SourceWidth.HasValue ? $"{preview.SourceWidth}x{preview.SourceHeight}" : "原始帧条/未知尺寸";
        return
            $"E5 图片条目{(restoreMode ? "还原" : "替换")}预览：{target.Label}\r\n" +
            $"目标：{preview.TargetRelativePath}    图号：#{preview.ImageNumber}\r\n" +
            $"来源：{preview.SourcePath}\r\n" +
            $"索引偏移：{HexDisplayFormatter.FormatOffset(preview.IndexOffset)}    数据偏移：{HexDisplayFormatter.FormatOffset(preview.OldDataOffset)} -> {HexDisplayFormatter.FormatOffset(preview.NewDataOffset)}\r\n" +
            $"条目大小：{preview.OldSizeBytes:N0} -> {preview.NewSizeBytes:N0} 字节    格式：{preview.OldKind} -> {preview.NewKind}    来源尺寸：{sourceSize}\r\n" +
            $"文件大小：{preview.OldFileSizeBytes:N0} -> {preview.NewFileSizeBytes:N0} 字节（{FormatSignedBytes(preview.FileSizeDeltaBytes)}）\r\n" +
            $"写入方式：{preview.Placement}    改动估算：{preview.ChangedBytesEstimate:N0} 字节\r\n" +
            $"SHA256：{ShortSha256(preview.OldFileSha256)} -> {ShortSha256(preview.NewFileSha256)}    来源：{ShortSha256(preview.SourceSha256)}\r\n" +
            $"格式提示：{(preview.FormatWarnings.Count == 0 ? "无" : string.Join("；", preview.FormatWarnings))}\r\n" +
            $"风险提示：{preview.RiskSummary}\r\n" +
            "说明：只更新 0x110 索引表中的指定图号，不重排其它图片条目。";
    }

    private static string BuildE5ImageReplaceResultText(E5ImageReplaceResult result)
    {
        return
            $"E5 图片条目写入完成：{result.TargetRelativePath} #{result.ImageNumber}\r\n" +
            $"条目：offset={HexDisplayFormatter.FormatOffset(result.OldDataOffset)}/size={result.OldSizeBytes:N0}/{result.OldKind} -> offset={HexDisplayFormatter.FormatOffset(result.NewDataOffset)}/size={result.NewSizeBytes:N0}/{result.NewKind}\r\n" +
            $"写入方式：{result.Placement}    文件大小变化：{FormatSignedBytes(result.FileSizeDeltaBytes)}    改动估算：{result.ChangedBytesEstimate:N0} 字节\r\n" +
            $"备份：{result.BackupPath}\r\n报告：{result.ReportPath}\r\n结构化报告：{result.ReportJsonPath}";
    }

    private void ExportMissingImageResourceReport()
    {
        if (_currentImageAssignments == null)
        {
            MessageBox.Show(this, "\u8bf7\u5148\u8bfb\u53d6\u4eba\u7269 R/S\u3002", "\u63d0\u793a", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var missingRows = _currentImageAssignments.Rows.Cast<DataRow>()
            .Where(row => IsImageResourceMissing(row, "R") || IsImageResourceMissing(row, "S"))
            .ToList();
        if (missingRows.Count == 0)
        {
            MessageBox.Show(this, "\u5f53\u524d\u4eba\u7269 R/S \u8054\u52a8\u8868\u6ca1\u6709\u68c0\u6d4b\u5230\u7f3a\u5931\u8d44\u6e90\u3002", "\u65e0\u9700\u5bfc\u51fa", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "\u5bfc\u51fa R/S \u7f3a\u5931\u8d44\u6e90\u62a5\u544a",
            Filter = "CSV \u6587\u4ef6 (*.csv)|*.csv|\u6240\u6709\u6587\u4ef6 (*.*)|*.*",
            FileName = "\u4eba\u7269RS\u7f3a\u5931\u8d44\u6e90\u62a5\u544a.csv"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var columns = _currentImageAssignments.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
            var notes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ID"] = "\u4eba\u7269\u884c\u53f7/\u7f16\u53f7\uff0c\u7528\u4e8e\u56de\u67e5\u4eba\u7269\u8868\u3002",
                ["\u540d\u79f0"] = "\u4eba\u7269\u540d\u79f0\u3002",
                ["R\u5f62\u8c61\u7f16\u53f7"] = "R \u5f62\u8c61\u7f16\u53f7\uff1a\u4eba\u7269\u6307\u5b9a\u8868\u4e2d\u7684\u7f16\u53f7 n\uff0c\u6309\u6559\u7a0b\u53e3\u5f84\u5b9a\u4f4d Pmapobj.e5 \u56fe 2n+1/2n+2\u3002",
                ["S\u5f62\u8c61\u7f16\u53f7"] = "S 形象紧凑编号：S=0 按职业和预览阵营取默认兵种图；S=1..32 对应三转特殊三张图；S>=33 从 Unit 图337 起对应一转特殊单张图。E5 索引表从 0x110 开始，每项 12 字节。",
                ["R\u8d44\u6e90\u72b6\u6001"] = "R \u8d44\u6e90\u5b9a\u4f4d\u68c0\u67e5\u7ed3\u679c\uff1a\u4e0d\u518d\u6309 RS\\R_XX.eex \u5224\u65ad\u4eba\u7269\u56fe\u50cf\u3002",
                ["S\u8d44\u6e90\u72b6\u6001"] = "S \u8d44\u6e90\u5b9a\u4f4d\u68c0\u67e5\u7ed3\u679c\uff1a\u4e0d\u518d\u6309 RS\\S_XX.eex \u5224\u65ad\u4eba\u7269\u56fe\u50cf\u3002"
            };
            CsvService.ExportColumnsRowsWithAnnotationRow(_currentImageAssignments, dialog.FileName, columns, notes, missingRows);
            System.Diagnostics.Debug.WriteLine($"\u5df2\u5bfc\u51fa R/S \u7f3a\u5931\u8d44\u6e90\u62a5\u544a\uff1a{dialog.FileName}\uff0c\u884c\u6570 {missingRows.Count}");
            SetStatus($"R/S \u7f3a\u5931\u8d44\u6e90\u62a5\u544a\u5bfc\u51fa\u5b8c\u6210\uff1a{missingRows.Count} \u884c");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("\u5bfc\u51fa R/S \u7f3a\u5931\u8d44\u6e90\u62a5\u544a\u5931\u8d25\uff1a" + ex);
            MessageBox.Show(this, ex.Message, "\u5bfc\u51fa\u5931\u8d25", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private DataRow? GetSelectedImageAssignmentRow()
    {
        if (_imageAssignmentGrid.CurrentRow?.DataBoundItem is DataRowView rowView) return rowView.Row;
        if (_imageAssignmentGrid.SelectedRows.Count > 0 && _imageAssignmentGrid.SelectedRows[0].DataBoundItem is DataRowView selectedRowView) return selectedRowView.Row;
        return null;
    }

    private bool SelectImageAssignmentRow(Func<DataRow, bool> predicate, string preferredColumn)
    {
        _imageAssignmentGrid.ClearSelection();
        foreach (DataGridViewRow gridRow in _imageAssignmentGrid.Rows)
        {
            if (gridRow.DataBoundItem is not DataRowView rowView || !predicate(rowView.Row)) continue;

            var preferred = _imageAssignmentGrid.Columns
                .Cast<DataGridViewColumn>()
                .FirstOrDefault(column => column.Visible && column.DataPropertyName.Equals(preferredColumn, StringComparison.Ordinal));
            var cell = preferred != null
                ? gridRow.Cells[preferred.Index]
                : gridRow.Cells.Cast<DataGridViewCell>().FirstOrDefault(x => x.Visible);
            if (cell != null)
            {
                cell.Selected = true;
                _imageAssignmentGrid.CurrentCell = cell;
            }

            if (gridRow.Index >= 0 && gridRow.Index < _imageAssignmentGrid.RowCount)
            {
                _imageAssignmentGrid.FirstDisplayedScrollingRowIndex = gridRow.Index;
            }

            return true;
        }

        return false;
    }

    private ImageAssignmentResourceKind GetPreferredImageAssignmentResourceKind()
    {
        if (_imageAssignmentGrid.CurrentCell != null)
        {
            var propertyName = _imageAssignmentGrid.Columns[_imageAssignmentGrid.CurrentCell.ColumnIndex].DataPropertyName;
            if (string.Equals(propertyName, "头像编号", StringComparison.Ordinal)) return ImageAssignmentResourceKind.Face;
            if (propertyName.StartsWith("S", StringComparison.Ordinal)) return ImageAssignmentResourceKind.S;
        }

        return ImageAssignmentResourceKind.R;
    }

    private string GetPreferredImageResourcePrefix() => ResourceKindToPrefix(GetPreferredImageAssignmentResourceKind());

    private static string ResourceKindToPrefix(ImageAssignmentResourceKind kind) =>
        kind == ImageAssignmentResourceKind.Face ? "Face" : kind == ImageAssignmentResourceKind.S ? "S" : "R";

    private static string GetImageAssignmentResourceKindText(ImageAssignmentResourceKind kind) =>
        kind switch
        {
            ImageAssignmentResourceKind.Face => "头像",
            ImageAssignmentResourceKind.S => "S形象",
            _ => "R形象"
        };

    private static bool TryGetImageResourceId(DataRow row, ImageAssignmentResourceKind kind, out int id)
    {
        var columnName = kind switch
        {
            ImageAssignmentResourceKind.Face => "头像编号",
            ImageAssignmentResourceKind.S => "S形象编号",
            _ => "R形象编号"
        };
        return int.TryParse(Convert.ToString(row[columnName], CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
    }

    private static bool TryGetImageResourceId(DataRow row, string prefix, out int id)
    {
        var kind = prefix.Equals("Face", StringComparison.OrdinalIgnoreCase)
            ? ImageAssignmentResourceKind.Face
            : prefix.Equals("S", StringComparison.OrdinalIgnoreCase)
                ? ImageAssignmentResourceKind.S
                : ImageAssignmentResourceKind.R;
        return TryGetImageResourceId(row, kind, out id);
    }

    private static bool IsImageResourceMissing(DataRow row, string prefix)
    {
        var columnName = prefix == "S" ? "S\u8d44\u6e90\u72b6\u6001" : "R\u8d44\u6e90\u72b6\u6001";
        return Convert.ToString(row[columnName], CultureInfo.InvariantCulture)?.StartsWith("\u7f3a\u5931", StringComparison.Ordinal) == true;
    }

    private void OpenFileLocation(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
            System.Diagnostics.Debug.WriteLine("\u5df2\u5b9a\u4f4d\u6587\u4ef6\uff1a" + path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("\u5b9a\u4f4d\u6587\u4ef6\u5931\u8d25\uff1a" + ex);
            MessageBox.Show(this, ex.Message, "\u5b9a\u4f4d\u6587\u4ef6\u5931\u8d25", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveImageAssignments()
    {
        if (_project == null || _currentImageAssignments == null) return;

        _imageAssignmentGrid.EndEdit();
        if (_currentImageAssignments.GetChanges() == null)
        {
            MessageBox.Show(this, "人物 R/S 形象没有检测到改动。", "无需保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var preview = BuildChangePreview(_currentImageAssignments, maxItems: 40);
        if (MessageBox.Show(this,
                $"即将保存人物 R/S 形象到当前 MOD 项目的 Ekd5.exe。\r\n\r\n变更预览：\r\n{preview}\r\n\r\n保存前会自动备份，保存后会重新读取校验。是否继续？",
                "确认保存人物 R/S",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = _imageAssignmentService.Save(_project, _tables, _currentImageAssignments);
            _currentImageAssignments = _imageAssignmentService.Load(_project, _tables);
            _imageAssignmentGrid.DataSource = _currentImageAssignments;
            ColorImageAssignmentResourceRows();
            ShowSelectedImageAssignmentDetail();
            System.Diagnostics.Debug.WriteLine($"已保存人物 R/S 形象：保存表 {result.Saves.Count} 个，变化字节 {result.ChangedBytes}");
            System.Diagnostics.Debug.WriteLine("备份：" + result.BackupSummary);
            SetStatus($"人物 R/S 保存完成并已复读：变化 {result.ChangedBytes} 字节");
            MessageBox.Show(this,
                $"保存完成并已重新读取校验。\r\n保存表数量：{result.Saves.Count}\r\n变化字节：{result.ChangedBytes}\r\n备份：{result.BackupSummary}",
                "保存完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("保存人物 R/S 形象失败：" + ex);
            MessageBox.Show(this, ex.Message, "保存人物 R/S 形象失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ValidateImageAssignmentCell(DataGridViewCellValidatingEventArgs e)
    {
        if (_imageAssignmentGrid.ReadOnly || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_imageAssignmentGrid.Columns[e.ColumnIndex].ReadOnly) return;
        var columnName = _imageAssignmentGrid.Columns[e.ColumnIndex].DataPropertyName;
        if (columnName is not ("R形象编号" or "S形象编号")) return;

        var value = Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture) ?? string.Empty;
        var error = TryParseInteger(value, 0, ushort.MaxValue, columnName, _currentPageHexButton.Checked);
        _imageAssignmentGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = error ?? string.Empty;
        if (error != null)
        {
            e.Cancel = true;
            SetStatus(error);
        }
    }
}
