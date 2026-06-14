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
    private void LoadLsResources()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            _currentLsResources = _lsResourceReader.ReadAll(_project);
            ClearLsResourceHeatmapPreview();
            PopulateLsResourceCategoryFilter();
            BindLsResourceRows(_currentLsResources);
            UpdateLsResourceInfo(_currentLsResources.Count, "\u5168\u90e8", string.Empty);
            System.Diagnostics.Debug.WriteLine($"已读取 Ls/E5 资源探针：{_currentLsResources.Count} 个文件。");
            SetStatus("Ls/E5 地图资源探针读取完成");
        }
        catch (Exception ex)
        {
            _lsResourceInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("Ls/E5 资源探针读取失败：" + ex);
            MessageBox.Show(this, ex.Message, "Ls/E5 资源探针读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ConfigureLsResourceGrid()
    {
        foreach (DataGridViewColumn column in _lsResourceGrid.Columns)
        {
            if (column.DataPropertyName is nameof(LsResourceInfo.TopBytesHex)
                or nameof(LsResourceInfo.FirstPayloadBytesHex)
                or nameof(LsResourceInfo.TextHints)
                or nameof(LsResourceInfo.Annotation)
                or nameof(LsResourceInfo.RoleReason))
            {
                column.Width = 300;
            }
            else if (column.DataPropertyName == nameof(LsResourceInfo.Path))
            {
                column.Width = 260;
            }
        }
    }


    private void BindLsResourceRows(IEnumerable<LsResourceInfo> rows)
    {
        _lsResourceGrid.DataSource = new BindingList<LsResourceInfo>(rows.ToList());
        ConfigureLsResourceGrid();
    }

    private void PopulateLsResourceCategoryFilter()
    {
        var previous = Convert.ToString(_lsResourceCategoryFilterCombo.SelectedItem, CultureInfo.InvariantCulture);
        _lsResourceCategoryFilterCombo.Items.Clear();
        _lsResourceCategoryFilterCombo.Items.Add("\u5168\u90e8");
        foreach (var category in _currentLsResources.Select(x => x.Category).Distinct().OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            _lsResourceCategoryFilterCombo.Items.Add(category);
        }
        SelectComboValueOrFirst(_lsResourceCategoryFilterCombo, previous);
    }

    private void ApplyLsResourceFilter()
    {
        if (_currentLsResources.Count == 0) return;
        var category = Convert.ToString(_lsResourceCategoryFilterCombo.SelectedItem, CultureInfo.InvariantCulture) ?? "\u5168\u90e8";
        var keyword = _lsResourceSearchBox.Text.Trim();
        var filtered = _currentLsResources.Where(item =>
            (category == "\u5168\u90e8" || string.Equals(item.Category, category, StringComparison.Ordinal)) &&
            (string.IsNullOrWhiteSpace(keyword) || LsResourceMatchesKeyword(item, keyword)))
            .ToList();
        BindLsResourceRows(filtered);
        UpdateLsResourceInfo(filtered.Count, category, keyword);
        SetStatus($"Ls/E5 \u7b5b\u9009\uff1a{filtered.Count}/{_currentLsResources.Count}");
    }

    private void ClearLsResourceFilter()
    {
        _lsResourceSearchBox.Clear();
        if (_lsResourceCategoryFilterCombo.Items.Count > 0) _lsResourceCategoryFilterCombo.SelectedIndex = 0;
        BindLsResourceRows(_currentLsResources);
        UpdateLsResourceInfo(_currentLsResources.Count, "\u5168\u90e8", string.Empty);
        SetStatus("\u5df2\u663e\u793a\u5168\u90e8 Ls/E5 \u8d44\u6e90");
    }

    private static bool LsResourceMatchesKeyword(LsResourceInfo item, string keyword)
    {
        return ContainsKeyword(item.Category, keyword) ||
               ContainsKeyword(item.Id, keyword) ||
               ContainsKeyword(item.FileName, keyword) ||
               ContainsKeyword(item.RoleHint, keyword) ||
               ContainsKeyword(item.Magic, keyword) ||
               ContainsKeyword(item.HeaderText, keyword) ||
               ContainsKeyword(item.FirstPayloadBytesHex, keyword) ||
               ContainsKeyword(item.TopBytesHex, keyword) ||
               ContainsKeyword(item.TextHints, keyword) ||
               ContainsKeyword(item.Annotation, keyword) ||
               ContainsKeyword(item.RoleReason, keyword) ||
               ContainsKeyword(item.Path, keyword);
    }

    private void UpdateLsResourceInfo(int visibleCount, string category, string keyword)
    {
        if (_project == null) return;
        var categorySummary = string.Join("\uff0c", _currentLsResources
            .GroupBy(x => x.Category)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key}:{g.Count()}"));
        var roleSummary = string.Join("\uff0c", _currentLsResources
            .GroupBy(x => x.RoleHint)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Take(8)
            .Select(g => $"{g.Key}:{g.Count()}"));
        var mapCandidates = _currentLsResources.Count(x =>
            x.RoleHint.Contains("\u5730\u56fe", StringComparison.Ordinal) ||
            x.RoleHint.Contains("\u8c03\u8272\u677f", StringComparison.Ordinal));
        var filterText = category == "\u5168\u90e8" && string.IsNullOrWhiteSpace(keyword)
            ? "\u672a\u7b5b\u9009"
            : $"\u5206\u7c7b={category}\uff0c\u5173\u952e\u5b57={keyword}";
        _lsResourceInfoBox.Text =
            $"Ls/E5 \u626b\u63cf\u8303\u56f4\uff1a{_project.GameRoot} \u6839\u76ee\u5f55 *.e5\uff1b{Path.Combine(_project.GameRoot, "E5")}\\*.e5\r\n" +
            $"\u6587\u4ef6\u6570\uff1a{_currentLsResources.Count}    \u5f53\u524d\u663e\u793a\uff1a{visibleCount}    \u5206\u7c7b\uff1a{categorySummary}    \u5730\u56fe/\u8c03\u8272\u677f\u5019\u9009\uff1a{mapCandidates}\r\n" +
            $"\u89d2\u8272\u6458\u8981\uff1a{roleSummary}\r\n" +
            $"\u7b5b\u9009\uff1a{filterText}\r\n" +
            "\u5f53\u524d\u4e3a\u53ea\u8bfb\u683c\u5f0f\u63a2\u9488\uff1a\u8bc6\u522b Ls12/Ls11/Ls10 \u5934\u300116 \u5b57\u8282\u5934\u90e8\u540e\u8f7d\u8377\u7edf\u8ba1\u3001\u9ad8\u9891\u5b57\u8282\u3001\u6587\u672c\u7ebf\u7d22\u4e0e\u8d44\u6e90\u89d2\u8272\uff1b\u6682\u4e0d\u6267\u884c Ls \u89e3\u538b\u6216\u5199\u56de\u3002";
    }

    private LsResourceInfo? GetSelectedLsResourceItem()
    {
        if (_lsResourceGrid.SelectedRows.Count > 0 && _lsResourceGrid.SelectedRows[0].DataBoundItem is LsResourceInfo selectedItem) return selectedItem;
        if (_lsResourceGrid.CurrentRow?.DataBoundItem is LsResourceInfo currentItem) return currentItem;
        return null;
    }

    private void OpenSelectedLsResourceLocation()
    {
        var item = GetSelectedLsResourceItem();
        if (item == null)
        {
            MessageBox.Show(this, "\u8bf7\u5148\u5728 Ls/E5 \u8d44\u6e90\u63a2\u9488\u9875\u9009\u62e9\u4e00\u4e2a\u6587\u4ef6\u3002", "\u63d0\u793a", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        OpenFileLocation(item.Path);
    }

    private void ExportLsResourcesCsv() =>
        ExportGridItemsCsv<LsResourceInfo>(_lsResourceGrid, "\u5bfc\u51fa Ls/E5 \u8d44\u6e90\u7d22\u5f15", "LsE5\u8d44\u6e90\u7d22\u5f15.csv", "LsResources", "Ls/E5\u8d44\u6e90\u7d22\u5f15");

    private void ShowSelectedLsResource()
    {
        if (_lsResourceGrid.SelectedRows.Count == 0) return;
        if (_lsResourceGrid.SelectedRows[0].DataBoundItem is not LsResourceInfo item) return;

        if (_currentLsResourceHeatmap != null &&
            !_currentLsResourceHeatmap.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase))
        {
            ClearLsResourceHeatmapPreview();
        }
        _lsResourceInfoBox.Text =
            $"文件：{item.FileName}    分类：{item.Category}    ID：{item.Id}    角色：{item.RoleHint}\r\n" +
            $"路径：{item.Path}\r\n" +
            $"长度：{item.Length:N0} 字节    Magic：{item.Magic}    Header：{item.HeaderText}    PayloadOffset：0x{item.PayloadOffset:X}\r\n" +
            $"载荷：{item.PayloadLength:N0} 字节    不同字节：{item.UniqueByteCount}    00占比：{item.ZeroPercent:F2}%\r\n" +
            $"前 32 载荷字节：{item.FirstPayloadBytesHex}\r\n" +
            $"高频字节：{item.TopBytesHex}\r\n" +
            $"文本线索({item.TextHintCount})：{item.TextHints}\r\n" +
            $"中文注释：{item.Annotation}\r\n" +
            $"判定依据：{item.RoleReason}\r\n" +
            "说明：Ls 资源可能为曹操传专用压缩/封装格式；当前页面只用于建立格式封装证据，避免误写。";
        SetStatus($"Ls/E5：{item.FileName}");
    }


    private void RenderSelectedLsResourceHeatmap()
    {
        var item = GetSelectedLsResourceItem();
        if (item == null)
        {
            MessageBox.Show(this, "请先在 Ls/E5 资源探针页选择一个文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (item.PayloadLength <= 0)
        {
            MessageBox.Show(this, "选中的 Ls/E5 资源没有可分析载荷。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var length = item.PayloadLength > int.MaxValue ? int.MaxValue : (int)item.PayloadLength;
            var result = _eexByteHeatmapService.Analyze(
                item.Path,
                item.Category,
                item.PayloadOffset,
                length,
                $"Ls/E5载荷/{item.RoleHint}");
            var bitmap = _eexByteHeatmapService.Render(result);
            var old = _lsResourceHeatmapBox.Image;
            _lsResourceHeatmapBox.Image = bitmap;
            old?.Dispose();
            _currentLsResourceHeatmap = result;
            _exportLsResourceHeatmapPngButton.Enabled = true;
            _lsResourceHeatmapInfoBox.Text =
                BuildEexHeatmapInfoText(result) +
                "\r\n说明：此图只观察 Ls/E5 载荷的原始字节分布，帮助判断稀疏表、参数表、图像/压缩载荷或文本线索；当前不解压、不重封包、不写入。";
            System.Diagnostics.Debug.WriteLine($"已生成 Ls/E5 字节热力图：{result.FileName} {result.OffsetHex}-{result.EndOffsetHex}，单元 {result.CellCount}。");
            SetStatus($"Ls/E5 字节热力图完成：{result.FileName}");
        }
        catch (Exception ex)
        {
            _lsResourceHeatmapInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("Ls/E5 字节热力图生成失败：" + ex);
            MessageBox.Show(this, ex.Message, "Ls/E5 字节热力图失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ExportLsResourceHeatmapPng()
    {
        if (_currentLsResourceHeatmap == null)
        {
            MessageBox.Show(this, "当前还没有 Ls/E5 热力图。请先点击“生成字节热力图”。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var exportRoot = _project != null
            ? Path.Combine(_project.WorkspaceRoot, "CCZModStudio_Exports")
            : Path.GetDirectoryName(_currentLsResourceHeatmap.Path) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(exportRoot);
        var defaultName = MakeSafeFileName($"{Path.GetFileNameWithoutExtension(_currentLsResourceHeatmap.FileName)}_{_currentLsResourceHeatmap.OffsetHex}_LsE5字节热力图.png");
        using var dialog = new SaveFileDialog
        {
            Title = "导出 Ls/E5 字节热力图 PNG",
            Filter = "PNG 图片 (*.png)|*.png|所有文件 (*.*)|*.*",
            FileName = defaultName,
            InitialDirectory = exportRoot
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            if (_lsResourceHeatmapBox.Image != null)
            {
                _lsResourceHeatmapBox.Image.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);
            }
            else
            {
                using var bitmap = _eexByteHeatmapService.Render(_currentLsResourceHeatmap);
                bitmap.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);
            }
            System.Diagnostics.Debug.WriteLine($"已导出 Ls/E5 字节热力图：{dialog.FileName}");
            SetStatus("Ls/E5 字节热力图 PNG 导出完成");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("导出 Ls/E5 字节热力图失败：" + ex);
            MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ClearLsResourceHeatmapPreview()
    {
        _currentLsResourceHeatmap = null;
        var old = _lsResourceHeatmapBox.Image;
        _lsResourceHeatmapBox.Image = null;
        old?.Dispose();
        _exportLsResourceHeatmapPngButton.Enabled = false;
        _lsResourceHeatmapInfoBox.Text = "Ls/E5 字节热力图：请选择一个 Ls/E5 资源后点击“生成字节热力图”。该预览只读，仅观察 16 字节头之后的载荷分布，不解压、不重封包、不写入。";
    }

    private static bool TryMapPictureBoxPointToTerrainCell(PictureBox box, Point point, int gridWidth, int gridHeight, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (box.Image == null || box.Width <= 0 || box.Height <= 0 || gridWidth <= 0 || gridHeight <= 0) return false;
        var rect = GetImageDisplayRectangle(box);
        if (!rect.Contains(point)) return false;
        x = (int)((point.X - rect.X) * gridWidth / (double)rect.Width);
        y = (int)((point.Y - rect.Y) * gridHeight / (double)rect.Height);
        x = Math.Clamp(x, 0, gridWidth - 1);
        y = Math.Clamp(y, 0, gridHeight - 1);
        return true;
    }

    private static Rectangle GetImageDisplayRectangle(PictureBox box)
    {
        if (box.Image == null) return Rectangle.Empty;
        if (box.SizeMode != PictureBoxSizeMode.Zoom) return box.ClientRectangle;
        var imageRatio = box.Image.Width / (double)box.Image.Height;
        var boxRatio = box.ClientSize.Width / (double)Math.Max(1, box.ClientSize.Height);
        if (imageRatio > boxRatio)
        {
            var width = box.ClientSize.Width;
            var height = (int)Math.Round(width / imageRatio);
            return new Rectangle(0, (box.ClientSize.Height - height) / 2, width, height);
        }
        else
        {
            var height = box.ClientSize.Height;
            var width = (int)Math.Round(height * imageRatio);
            return new Rectangle((box.ClientSize.Width - width) / 2, 0, width, height);
        }
    }

    private string FormatTerrainValue(byte value)
        => _terrainEditorTerrainLookup.TryGetValue(value, out var name) && !string.IsNullOrWhiteSpace(name)
            ? $"0x{value:X2}（{name}）"
            : $"0x{value:X2}";

    private void LoadHexzmapProbe()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var terrainLookup = BuildTerrainNameLookupForCurrentProject();
            _currentHexzmapProbe = _hexzmapProbeReader.Read(_project, terrainLookup);
            _hexzmapGrid.DataSource = new BindingList<HexzmapBlockInfo>(_currentHexzmapProbe.Blocks.ToList());
            ConfigureHexzmapGrid();
            _exportHexzmapProbeCsvButton.Enabled = _currentHexzmapProbe.Blocks.Count > 0;
            _exportHexzmapOverlayPngButton.Enabled = _currentHexzmapProbe.Blocks.Count > 0;
            _hexzmapInfoBox.Text =
                $"文件：{_currentHexzmapProbe.Path}\r\n" +
                $"Magic：{_currentHexzmapProbe.Magic}    有效Ls头：{_currentHexzmapProbe.MagicValid}    PayloadOffset：0x{_currentHexzmapProbe.PayloadOffset:X}\r\n" +
                $"分块解释：按 Map\\Mxxx.jpg/JPG 图片分辨率 / 48 计算地形格数；地形块数：{_currentHexzmapProbe.Blocks.Count}；尾部未解释字节：{_currentHexzmapProbe.TrailingBytes}\r\n" +
                $"素材库地形图例：{terrainLookup.Count} 个 hex 标记可用于中文注释。\r\n" +
                "说明：地形格数不再固定，960x960 地图为 20x20，37x30 地图对应 1776x1440。";
            if (_hexzmapGrid.Rows.Count > 0)
            {
                _hexzmapGrid.Rows[0].Selected = true;
                ShowSelectedHexzmapBlock();
            }
            System.Diagnostics.Debug.WriteLine($"已读取 Hexzmap 地形探针：{_currentHexzmapProbe.Blocks.Count} 个候选块，尾部 {_currentHexzmapProbe.TrailingBytes} 字节。");
            SetStatus("Hexzmap 地形探针读取完成");
        }
        catch (Exception ex)
        {
            _hexzmapInfoBox.Text = ex.ToString();
            _exportHexzmapProbeCsvButton.Enabled = false;
            _exportHexzmapOverlayPngButton.Enabled = false;
            System.Diagnostics.Debug.WriteLine("Hexzmap 地形探针读取失败：" + ex);
            MessageBox.Show(this, ex.Message, "Hexzmap 地形探针读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ConfigureHexzmapGrid()
    {
        foreach (DataGridViewColumn column in _hexzmapGrid.Columns)
        {
            if (column.DataPropertyName == nameof(HexzmapBlockInfo.Annotation))
            {
                column.Width = 460;
            }
            else if (column.DataPropertyName is nameof(HexzmapBlockInfo.TopTerrainIds) or nameof(HexzmapBlockInfo.TopTerrainNames))
            {
                column.Width = 320;
            }
            else if (column.DataPropertyName is nameof(HexzmapBlockInfo.DominantTerrainName) or nameof(HexzmapBlockInfo.UnknownTerrainIds))
            {
                column.Width = 220;
            }
        }
    }

    private void ExportHexzmapProbeCsv() =>
        ExportGridItemsCsv<HexzmapBlockInfo>(_hexzmapGrid, "导出 Hexzmap 地形探针", "Hexzmap地形探针.csv", "HexzmapBlocks", "Hexzmap地形探针");

    private HexzmapBlockInfo? GetSelectedHexzmapBlock()
    {
        if (_hexzmapGrid.SelectedRows.Count > 0 && _hexzmapGrid.SelectedRows[0].DataBoundItem is HexzmapBlockInfo selectedItem) return selectedItem;
        if (_hexzmapGrid.CurrentRow?.DataBoundItem is HexzmapBlockInfo currentItem) return currentItem;
        return null;
    }


    private void ShowSelectedHexzmapBlock()
    {
        if (_currentHexzmapProbe == null) return;
        var block = GetSelectedHexzmapBlock();
        if (block == null) return;

        var cells = _hexzmapProbeReader.GetBlockCells(_currentHexzmapProbe, block);
        _hexzmapPreviewBox.Image?.Dispose();
        _hexzmapPreviewBox.Image = cells.Length == block.BytesRead
            ? RenderHexzmapPreview(block, cells)
            : null;
        _exportHexzmapOverlayPngButton.Enabled = _hexzmapPreviewBox.Image != null;
        _hexzmapInfoBox.Text =
            $"候选地图块：{block.MapId}    Index={block.Index}    偏移：{block.OffsetHex}    {block.Width}x{block.Height}\r\n" +
            $"对应地图图片：{(block.MapImageExists ? block.MapImageName : "未找到同编号 Mxxx.jpg")}    地形种类：{block.UniqueTerrainCount}    已知图例：{block.KnownTerrainCount}    主地形：0x{block.DominantTerrainId:X2} {block.DominantTerrainName} x {block.DominantTerrainCount}\r\n" +
            $"高频地形ID：{block.TopTerrainIds}\r\n" +
            $"高频地形中文候选：{block.TopTerrainNames}\r\n" +
            $"未匹配图例ID：{block.UnknownTerrainIds}\r\n" +
            $"中文说明：{block.Annotation}\r\n" +
            $"{BuildHexzmapPreviewModeText(block)}\r\n" +
            "右侧预览用于把按地图分辨率/48 划分的地形索引与战场底图对照，帮助判断地形表、地图图片和关卡编号是否匹配。";
        SetStatus($"Hexzmap：{block.MapId}");
    }

    private void ClearHexzmapCellPreview()
    {
        _hexzmapCellPreviewLabel.Text = "地形：-    坐标：-";
    }

    private void UpdateHexzmapCellPreview(Point location)
    {
        if (_currentHexzmapProbe == null || _hexzmapPreviewBox.Image == null)
        {
            ClearHexzmapCellPreview();
            return;
        }

        var block = GetSelectedHexzmapBlock();
        if (block == null ||
            !TryMapPictureBoxPointToTerrainCell(_hexzmapPreviewBox, location, block.Width, block.Height, out var x, out var y))
        {
            ClearHexzmapCellPreview();
            return;
        }

        var cells = _hexzmapProbeReader.GetBlockCells(_currentHexzmapProbe, block);
        var index = y * block.Width + x;
        var terrain = index >= 0 && index < cells.Length ? FormatTerrainValue(cells[index]) : "未知";
        _hexzmapCellPreviewLabel.Text = $"地形：{terrain}    坐标：({x}, {y})";
    }

    private Bitmap RenderHexzmapPreview(HexzmapBlockInfo block, byte[] cells)
    {
        var mapImagePath = ResolveHexzmapMapImagePath(block);
        if (_hexzmapOverlayMapCheckBox.Checked && !string.IsNullOrWhiteSpace(mapImagePath))
        {
            try
            {
                return _hexzmapTerrainRenderService.RenderOverlay(
                    cells,
                    block.Width,
                    block.Height,
                    mapImagePath,
                    _hexzmapOverlayOpacityTrackBar.Value);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Hexzmap 地图底图叠加失败，已改用纯地形色块预览：" + ex.Message);
            }
        }

        return RenderHexzmapCells(cells, block.Width, block.Height);
    }

    private string ResolveHexzmapMapImagePath(HexzmapBlockInfo block)
    {
        if (_project == null || !block.MapImageExists || string.IsNullOrWhiteSpace(block.MapImageName))
        {
            return string.Empty;
        }

        var path = Path.Combine(_project.GameRoot, "Map", block.MapImageName);
        return File.Exists(path) ? path : string.Empty;
    }

    private string BuildHexzmapPreviewModeText(HexzmapBlockInfo block)
    {
        if (!_hexzmapOverlayMapCheckBox.Checked)
        {
            return "预览模式：纯地形色块；适合观察地形 ID 分布和异常切分。";
        }

        var mapImagePath = ResolveHexzmapMapImagePath(block);
        if (string.IsNullOrWhiteSpace(mapImagePath))
        {
            return "预览模式：已勾选底图叠加，但未找到同编号地图图片，当前退回纯地形色块。";
        }

        return $"预览模式：地图底图 + 半透明地形色块，地形透明度 {_hexzmapOverlayOpacityTrackBar.Value}%，底图路径：{mapImagePath}";
    }

    private void ExportHexzmapOverlayPng()
    {
        if (_currentHexzmapProbe == null)
        {
            MessageBox.Show(this, "请先读取 Hexzmap.e5。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var block = GetSelectedHexzmapBlock();
        if (block == null)
        {
            MessageBox.Show(this, "请先选择一个地形块。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var cells = _hexzmapProbeReader.GetBlockCells(_currentHexzmapProbe, block);
        if (cells.Length != block.BytesRead)
        {
            MessageBox.Show(this, $"当前地形块载荷长度不符合地图格数 {block.Width}x{block.Height}，无法导出预览。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var exportRoot = _project != null
            ? Path.Combine(_project.WorkspaceRoot, "CCZModStudio_Exports")
            : Directory.GetCurrentDirectory();
        Directory.CreateDirectory(exportRoot);
        var mode = _hexzmapOverlayMapCheckBox.Checked && !string.IsNullOrWhiteSpace(ResolveHexzmapMapImagePath(block))
            ? "地图叠加预览"
            : "地形色块预览";
        using var dialog = new SaveFileDialog
        {
            Title = "导出 Hexzmap 地形预览 PNG",
            Filter = "PNG 图片 (*.png)|*.png|所有文件 (*.*)|*.*",
            FileName = MakeSafeFileName($"{block.MapId}_Hexzmap_{mode}.png"),
            InitialDirectory = exportRoot
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            using var bitmap = RenderHexzmapPreview(block, cells);
            bitmap.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);
            System.Diagnostics.Debug.WriteLine("已导出 Hexzmap 地形预览 PNG：" + dialog.FileName);
            SetStatus("Hexzmap 地形预览 PNG 导出完成");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("导出 Hexzmap 地形预览 PNG 失败：" + ex);
            MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private IReadOnlyDictionary<byte, string> BuildTerrainNameLookupForCurrentProject()
    {
        if (_project == null) return new Dictionary<byte, string>();
        try
        {
            var materials = _currentMaterialAssets.Count > 0
                ? _currentMaterialAssets
                : _materialLibraryIndexer.Index(_project);
            return HexzmapProbeReader.BuildTerrainNameLookup(materials);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("素材库地形图例读取失败，Hexzmap 将只显示地形 ID：" + ex.Message);
            return new Dictionary<byte, string>();
        }
    }

    private Bitmap RenderHexzmapCells(byte[] cells, int width, int height) =>
        _hexzmapTerrainRenderService.RenderTerrainCells(cells, width, height);







    private bool EnsureMapMakerHexzmapLoaded(bool showMessage)
    {
        if (_project == null)
        {
            if (showMessage) MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        try
        {
            if (_terrainEditorTerrainLookup.Count == 0)
            {
                _terrainEditorTerrainLookup = BuildTerrainNameLookupForCurrentProject();
                RefreshMapMakerPresetCombo();
            }

            if (_currentHexzmapProbe == null)
            {
                _currentHexzmapProbe = _hexzmapProbeReader.Read(_project, _terrainEditorTerrainLookup);
            }

            if (_mapMakerTerrainPresetCombo.DataSource == null)
            {
                RefreshMapMakerPresetCombo();
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("地图制作读取 Hexzmap.e5 失败：" + ex);
            if (showMessage) MessageBox.Show(this, ex.Message, "读取地形层失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private bool TryLoadMapMakerTerrainForSelectedMap(bool showMessage)
    {
        if (_currentMapMakerItem == null) return false;
        if (!EnsureMapMakerHexzmapLoaded(showMessage)) return false;
        var block = FindHexzmapBlockForMap(_currentMapMakerItem);
        if (block == null)
        {
            if (showMessage)
            {
                MessageBox.Show(this, $"没有找到 {_currentMapMakerItem.Name} 对应的 Hexzmap.e5 地形块。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            UpdateMapMakerEditingButtons();
            return false;
        }

        if (_terrainEditorBlock == null ||
            _terrainEditorBlock.Index != block.Index ||
            _terrainEditorCells.Length != block.BytesRead)
        {
            SelectTerrainEditorBlockForMapMaker(block);
        }
        else
        {
            RenderMapMakerPreview();
        }

        UpdateMapMakerEditingButtons();
        return _terrainEditorBlock != null && _terrainEditorBlock.Index == block.Index;
    }

    private void SelectTerrainEditorBlockForMapMaker(HexzmapBlockInfo block)
    {
        if (_currentHexzmapProbe == null) return;
        var cells = _hexzmapProbeReader.GetBlockCells(_currentHexzmapProbe, block);
        if (cells.Length != block.BytesRead)
        {
            return;
        }

        _terrainEditorBlock = block;
        _terrainEditorCells = cells.ToArray();
        _terrainEditorOriginalCells = _terrainEditorCells.ToArray();
        _mapMakerOriginalTerrainCells = _terrainEditorCells.ToArray();
        if (_currentMapWorkbenchDraft != null && _currentMapWorkbenchDraft.CellCount == _terrainEditorCells.Length)
        {
            _currentMapWorkbenchDraft.TerrainCells = _terrainEditorCells;
        }

        RenderMapMakerPreview();
    }

    private HexzmapBlockInfo? FindHexzmapBlockForMap(MapResourceItem item)
    {
        if (_currentHexzmapProbe == null) return null;
        var mapId = GetMapIdForMapResource(item);
        if (string.IsNullOrWhiteSpace(mapId)) return null;
        return _currentHexzmapProbe.Blocks.FirstOrDefault(block => block.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase));
    }

    private HexzmapBlockInfo? TryGetMatchingHexzmapBlockForMap(MapResourceItem item)
    {
        if (!EnsureMapMakerHexzmapLoaded(showMessage: false)) return null;
        var block = FindHexzmapBlockForMap(item);
        if (block == null) return null;
        return item.GridWidth == block.Width && item.GridHeight == block.Height ? block : null;
    }

    private static string GetMapIdForMapResource(MapResourceItem item)
    {
        var name = Path.GetFileNameWithoutExtension(item.Name);
        if (name.Length > 1 && (name[0] == 'M' || name[0] == 'm') &&
            int.TryParse(name[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var nameNumber))
        {
            return $"M{nameNumber:D3}";
        }

        if (int.TryParse(item.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idNumber))
        {
            return $"M{idNumber:D3}";
        }

        return string.Empty;
    }

    private void LoadMapWorkbenchSettings()
    {
        if (_project == null) return;
        try
        {
            _mapWorkbenchSettings = _mapDraftService.LoadSettings(_project);
            if (!string.IsNullOrWhiteSpace(_mapWorkbenchSettings.LastMaterialRoot) &&
                Directory.Exists(_mapWorkbenchSettings.LastMaterialRoot))
            {
                IndexMapWorkbenchMaterialRoot(_mapWorkbenchSettings.LastMaterialRoot, showMessages: false);
            }
            else if (!string.IsNullOrWhiteSpace(_mapWorkbenchSettings.LastMaterialRoot))
            {
                _mapMakerMaterialInfoBox.Text = "上次选择的素材库目录不可达，请重新选择：\r\n" + _mapWorkbenchSettings.LastMaterialRoot;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("读取地图工作台设置失败：" + ex.Message);
            _mapWorkbenchSettings = new MapWorkbenchSettings();
        }
    }

    private void SaveMapWorkbenchSettings()
    {
        if (_project == null) return;
        try
        {
            _mapDraftService.SaveSettings(_project, _mapWorkbenchSettings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("保存地图工作台设置失败：" + ex.Message);
        }
    }

    private void CreateNewMapWorkbenchDraftFromInputs()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var width = (int)_mapMakerGridWidthInput.Value;
        var height = (int)_mapMakerGridHeightInput.Value;
        _currentMapMakerItem = null;
        _currentMapWorkbenchDraft = _mapDraftService.CreateBlankDraft(width, height, _mapWorkbenchSettings.LastMaterialRoot);
        BindMapWorkbenchDraftToEditor(resetHistory: true);
        _mapViewerInfoBox.Text = BuildMapMakerInfo("已新建空白草稿。");
        SetStatus($"地图草稿：{width}x{height}");
    }

    private void LoadLastMapWorkbenchDraft()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var draftId = _mapWorkbenchSettings.LastDraftId;
        if (string.IsNullOrWhiteSpace(draftId))
        {
            var latest = _mapDraftService.ListDrafts(_project).FirstOrDefault();
            draftId = latest?.DraftId ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(draftId))
        {
            MessageBox.Show(this, "当前项目还没有地图工作台草稿。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            _currentMapWorkbenchDraft = _mapDraftService.LoadDraft(_project, draftId);
            _currentMapMakerItem = FindMapResourceByMapId(_currentMapWorkbenchDraft.BoundMapId);
            BindMapWorkbenchDraftToEditor(resetHistory: true);
            var missing = _mapDraftService.FindMissingAssets(_currentMapWorkbenchDraft);
            _mapViewerInfoBox.Text = BuildMapMakerInfo(missing.Count == 0 ? "已载入地图工作台草稿。" : $"已载入草稿，但有 {missing.Count} 个素材/底稿缺失。");
            if (missing.Count > 0)
            {
                _mapMakerMaterialInfoBox.Text = "缺失清单：\r\n" + string.Join("\r\n", missing.Take(40).Select(x => $"{x.Index}: {x.RelativePath} - {x.Reason}"));
            }

            SetStatus("地图草稿载入完成");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("载入地图工作台草稿失败：" + ex);
            MessageBox.Show(this, ex.Message, "载入草稿失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveCurrentMapWorkbenchDraft()
    {
        if (_project == null || _currentMapWorkbenchDraft == null)
        {
            MessageBox.Show(this, "当前没有可保存的地图草稿。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            SyncMapWorkbenchDraftFromEditor();
            _mapDraftService.SaveDraft(_project, _currentMapWorkbenchDraft);
            _mapWorkbenchSettings.LastDraftId = _currentMapWorkbenchDraft.DraftId;
            _mapWorkbenchSettings.LastBoundMapId = _currentMapWorkbenchDraft.BoundMapId;
            _mapWorkbenchSettings.LastMaterialRoot = _currentMapWorkbenchDraft.MaterialRoot;
            SaveMapWorkbenchSettings();
            _mapViewerInfoBox.Text = BuildMapMakerInfo("草稿已保存。");
            System.Diagnostics.Debug.WriteLine($"已保存地图工作台草稿：{_currentMapWorkbenchDraft.DraftId}");
            SetStatus("地图草稿保存完成");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("保存地图工作台草稿失败：" + ex);
            MessageBox.Show(this, ex.Message, "保存草稿失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ResizeCurrentMapWorkbenchDraftFromInputs()
    {
        if (_currentMapWorkbenchDraft == null) return;
        if (!string.IsNullOrWhiteSpace(_currentMapWorkbenchDraft.BoundMapId))
        {
            _mapMakerGridWidthInput.Value = Math.Clamp(_currentMapWorkbenchDraft.GridWidth, (int)_mapMakerGridWidthInput.Minimum, (int)_mapMakerGridWidthInput.Maximum);
            _mapMakerGridHeightInput.Value = Math.Clamp(_currentMapWorkbenchDraft.GridHeight, (int)_mapMakerGridHeightInput.Minimum, (int)_mapMakerGridHeightInput.Maximum);
            return;
        }

        var width = (int)_mapMakerGridWidthInput.Value;
        var height = (int)_mapMakerGridHeightInput.Value;
        if (_currentMapWorkbenchDraft.GridWidth == width && _currentMapWorkbenchDraft.GridHeight == height) return;
        ResizeCurrentMapWorkbenchDraft(width, height);
    }

    private void ResizeCurrentMapWorkbenchDraft(int width, int height)
    {
        if (_currentMapWorkbenchDraft == null) return;
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var oldWidth = _currentMapWorkbenchDraft.GridWidth;
        var oldHeight = _currentMapWorkbenchDraft.GridHeight;
        var oldTerrain = _terrainEditorCells.ToArray();
        var newTerrain = new byte[width * height];
        var copyWidth = Math.Min(oldWidth, width);
        var copyHeight = Math.Min(oldHeight, height);
        for (var y = 0; y < copyHeight; y++)
        {
            Array.Copy(oldTerrain, y * oldWidth, newTerrain, y * width, copyWidth);
        }

        _currentMapWorkbenchDraft.GridWidth = width;
        _currentMapWorkbenchDraft.GridHeight = height;
        _currentMapWorkbenchDraft.TerrainCells = newTerrain;
        _currentMapWorkbenchDraft.MapCellOverrides = _currentMapWorkbenchDraft.MapCellOverrides
            .Where(cell =>
            {
                var x = cell.Index % oldWidth;
                var y = cell.Index / oldWidth;
                return x < width && y < height;
            })
            .Select(cell =>
            {
                var x = cell.Index % oldWidth;
                var y = cell.Index / oldWidth;
                return new MapCellOverride
                {
                    Index = y * width + x,
                    MaterialRelativePath = cell.MaterialRelativePath,
                    MaterialCategory = cell.MaterialCategory,
                    DisplayName = cell.DisplayName
                };
            })
            .OrderBy(cell => cell.Index)
            .ToList();

        BindMapWorkbenchDraftToEditor(resetHistory: true);
        _mapViewerInfoBox.Text = BuildMapMakerInfo($"草稿尺寸已调整为 {width}x{height}。");
    }

    private void BindMapWorkbenchDraftToEditor(bool resetHistory)
    {
        if (_currentMapWorkbenchDraft == null) return;
        _currentMapWorkbenchDraft.TileSize = MapResourceItem.MapTilePixelSize;
        var cellCount = _currentMapWorkbenchDraft.GridWidth * _currentMapWorkbenchDraft.GridHeight;
        if (_currentMapWorkbenchDraft.TerrainCells.Length != cellCount)
        {
            var cells = new byte[cellCount];
            Array.Copy(_currentMapWorkbenchDraft.TerrainCells, cells, Math.Min(cells.Length, _currentMapWorkbenchDraft.TerrainCells.Length));
            _currentMapWorkbenchDraft.TerrainCells = cells;
        }

        _terrainEditorCells = _currentMapWorkbenchDraft.TerrainCells.ToArray();
        _mapMakerOriginalTerrainCells = _terrainEditorCells.ToArray();
        _terrainEditorOriginalCells = _terrainEditorCells.ToArray();
        RebuildMapMakerOverrideLookup();
        RecalculateMapMakerTerrainChangedCellCount();
        SetNumericSilently(_mapMakerGridWidthInput, _currentMapWorkbenchDraft.GridWidth);
        SetNumericSilently(_mapMakerGridHeightInput, _currentMapWorkbenchDraft.GridHeight);
        _mapMakerGridWidthInput.Enabled = string.IsNullOrWhiteSpace(_currentMapWorkbenchDraft.BoundMapId);
        _mapMakerGridHeightInput.Enabled = string.IsNullOrWhiteSpace(_currentMapWorkbenchDraft.BoundMapId);
        if (!string.IsNullOrWhiteSpace(_currentMapWorkbenchDraft.MaterialRoot) &&
            Directory.Exists(_currentMapWorkbenchDraft.MaterialRoot) &&
            !_mapWorkbenchSettings.LastMaterialRoot.Equals(_currentMapWorkbenchDraft.MaterialRoot, StringComparison.OrdinalIgnoreCase))
        {
            IndexMapWorkbenchMaterialRoot(_currentMapWorkbenchDraft.MaterialRoot, showMessages: false);
        }

        if (resetHistory)
        {
            ResetMapWorkbenchHistory();
        }

        RenderMapMakerPreview();
        ApplyMapZoom();
        UpdateMapMakerEditingButtons();
    }

    private void SyncMapWorkbenchDraftFromEditor()
    {
        if (_currentMapWorkbenchDraft == null) return;
        _currentMapWorkbenchDraft.TerrainCells = _terrainEditorCells;
        _currentMapWorkbenchDraft.MaterialRoot = _mapWorkbenchSettings.LastMaterialRoot;
        SyncMapWorkbenchOverridesFromLookup();
        if (_currentMapMakerItem != null)
        {
            _currentMapWorkbenchDraft.BoundMapId = GetMapIdForMapResource(_currentMapMakerItem);
        }
    }

    private static void SetNumericSilently(NumericUpDown input, int value)
    {
        input.Value = Math.Clamp(value, (int)input.Minimum, (int)input.Maximum);
    }

    private void ResetMapWorkbenchHistory()
    {
        _mapMakerPainting = false;
        _mapMakerPendingMapPaintChanges.Clear();
        _mapMakerPendingMapPaintIndexes.Clear();
        _mapMakerPendingTerrainPaintChanges.Clear();
        _mapMakerPendingTerrainPaintIndexes.Clear();
        _mapMakerMapUndoStack.Clear();
        _mapMakerMapRedoStack.Clear();
        _mapMakerTerrainUndoStack.Clear();
        _mapMakerTerrainRedoStack.Clear();
        RecalculateMapMakerTerrainChangedCellCount();
        UpdateMapMakerEditingButtons();
    }

    private void SelectMapWorkbenchBrushMode()
    {
        var mode = _mapMakerBrushModeCombo.SelectedIndex switch
        {
            1 => MapWorkbenchBrushMode.MapBrush,
            2 => MapWorkbenchBrushMode.TerrainBrush,
            _ => MapWorkbenchBrushMode.Browse
        };
        SetMapWorkbenchBrushMode(mode);
    }

    private void SetMapWorkbenchBrushMode(MapWorkbenchBrushMode mode)
    {
        _mapWorkbenchBrushMode = mode;
        var index = mode switch
        {
            MapWorkbenchBrushMode.MapBrush => 1,
            MapWorkbenchBrushMode.TerrainBrush => 2,
            _ => 0
        };
        if (_mapMakerBrushModeCombo.SelectedIndex != index)
        {
            _mapMakerBrushModeCombo.SelectedIndex = index;
        }

        if (_mapMakerEditTerrainCheckBox.Checked != (mode == MapWorkbenchBrushMode.TerrainBrush))
        {
            _mapMakerEditTerrainCheckBox.Checked = mode == MapWorkbenchBrushMode.TerrainBrush;
        }

        UpdateMapMakerEditingButtons();
    }

    private void SelectMapWorkbenchMaterialRoot()
    {
        var initial = _mapWorkbenchSettings.LastMaterialRoot;
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择素材库根目录（包含分类子目录和 hex.txt）",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(initial) ? initial : (_project?.WorkspaceRoot ?? Directory.GetCurrentDirectory())
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        IndexMapWorkbenchMaterialRoot(dialog.SelectedPath, showMessages: true);
    }

    private void IndexMapWorkbenchMaterialRoot(string root, bool showMessages)
    {
        if (string.IsNullOrWhiteSpace(root)) return;
        try
        {
            Cursor = Cursors.WaitCursor;
            _currentMaterialAssets = _materialLibraryIndexer.IndexExplicitRoot(root);
            _mapWorkbenchSettings.LastMaterialRoot = Path.GetFullPath(root);
            if (_currentMapWorkbenchDraft != null)
            {
                _currentMapWorkbenchDraft.MaterialRoot = _mapWorkbenchSettings.LastMaterialRoot;
            }

            PopulateMapWorkbenchMaterialCategoryFilter();
            ApplyMapWorkbenchMaterialFilter();
            _materialGrid.DataSource = new BindingList<MaterialAsset>(_currentMaterialAssets.ToList());
            ConfigureMaterialGrid();
            _mapMakerMaterialInfoBox.Text =
                $"素材根目录：{_mapWorkbenchSettings.LastMaterialRoot}\r\n" +
                $"素材数量：{_currentMaterialAssets.Count}\r\n" +
                "地图画笔会把选中素材整张缩放到 48x48 覆盖当前格；地形画笔只使用 HexTag 构建中文地形候选。";
            SaveMapWorkbenchSettings();
            RefreshMapMakerPresetCombo();
            if (showMessages) SetStatus("地图工作台素材库索引完成");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("地图工作台素材库索引失败：" + ex);
            MessageBox.Show(this, ex.Message, "素材库索引失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void PopulateMapWorkbenchMaterialCategoryFilter()
    {
        var selected = _mapMakerMaterialCategoryCombo.SelectedItem?.ToString() ?? "全部";
        _mapMakerMaterialCategoryCombo.Items.Clear();
        _mapMakerMaterialCategoryCombo.Items.Add("全部");
        foreach (var category in _currentMaterialAssets.Select(x => x.Category).Distinct().OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            _mapMakerMaterialCategoryCombo.Items.Add(category);
        }

        _mapMakerMaterialCategoryCombo.SelectedItem = _mapMakerMaterialCategoryCombo.Items.Contains(selected) ? selected : "全部";
    }

    private void ApplyMapWorkbenchMaterialFilter()
    {
        var category = _mapMakerMaterialCategoryCombo.SelectedItem?.ToString() ?? "全部";
        var keyword = _mapMakerMaterialSearchBox.Text.Trim();
        var query = _currentMaterialAssets.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("全部", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.Category.Equals(category, StringComparison.CurrentCultureIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(x =>
                x.FileName.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
                x.Category.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
                x.Description.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
                x.HexTag.Contains(keyword, StringComparison.CurrentCultureIgnoreCase));
        }

        _mapMakerMaterialGrid.DataSource = new BindingList<MaterialAsset>(query.ToList());
        HideMapMakerMaterialTechnicalColumns();
    }

    private void HideMapMakerMaterialTechnicalColumns()
    {
        HideNonAuthoringColumns(
            _mapMakerMaterialGrid,
            nameof(MaterialAsset.Width),
            nameof(MaterialAsset.Height),
            nameof(MaterialAsset.SizeBytes),
            nameof(MaterialAsset.Description),
            nameof(MaterialAsset.FilePath));
    }

    private void ClearMapWorkbenchMaterialFilter()
    {
        _mapMakerMaterialSearchBox.Clear();
        if (_mapMakerMaterialCategoryCombo.Items.Count > 0) _mapMakerMaterialCategoryCombo.SelectedIndex = 0;
        ApplyMapWorkbenchMaterialFilter();
    }

    private void SelectMapWorkbenchMaterial()
    {
        if (_mapMakerMaterialGrid.SelectedRows.Count == 0) return;
        if (_mapMakerMaterialGrid.SelectedRows[0].DataBoundItem is not MaterialAsset asset) return;
        _mapMakerSelectedMaterial = asset;
        try
        {
            var old = _mapMakerMaterialPreview.Image;
            _mapMakerMaterialPreview.Image = null;
            old?.Dispose();
            using var image = Image.FromFile(asset.FilePath);
            _mapMakerMaterialPreview.Image = new Bitmap(image);
            _mapMakerMaterialInfoBox.Text =
                $"选中素材：{asset.Category}/{asset.FileName}\r\n" +
                $"尺寸：{asset.Width}x{asset.Height}    HexTag：{asset.HexTag}    说明：{asset.Description}\r\n" +
                $"路径：{asset.FilePath}";
            if (_mapWorkbenchBrushMode != MapWorkbenchBrushMode.MapBrush)
            {
                SetMapWorkbenchBrushMode(MapWorkbenchBrushMode.MapBrush);
            }
        }
        catch (Exception ex)
        {
            _mapMakerMaterialPreview.Image?.Dispose();
            _mapMakerMaterialPreview.Image = null;
            System.Diagnostics.Debug.WriteLine($"地图工作台素材预览失败：{asset.FilePath} {ex.Message}");
        }
    }

    private MapResourceItem? FindMapResourceByMapId(string mapId)
    {
        if (string.IsNullOrWhiteSpace(mapId)) return null;
        if (_currentMapResources.Count == 0 && _project != null)
        {
            _currentMapResources = _mapResourceIndexer.Index(_project);
        }

        return _currentMapResources
            .Where(x => x.Category == "地图图片")
            .FirstOrDefault(x => GetMapIdForMapResource(x).Equals(mapId, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsCurrentMapMakerTerrainLoaded()
    {
        if (_currentMapWorkbenchDraft == null)
        {
            return false;
        }

        return _terrainEditorCells.Length == _currentMapWorkbenchDraft.CellCount;
    }

    private void RenderMapMakerPreview(bool force = false)
    {
        if (!force && _mapMakerPainting)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastMapMakerRenderUtc).TotalMilliseconds < 50)
            {
                _mapMakerRenderDeferred = true;
                return;
            }

            _lastMapMakerRenderUtc = now;
            _mapMakerRenderDeferred = false;
        }
        else
        {
            _lastMapMakerRenderUtc = DateTime.UtcNow;
            _mapMakerRenderDeferred = false;
        }

        if (_currentMapWorkbenchDraft == null)
        {
            ClearMapMakerPreviewImages();
            UpdateMapMakerEditingButtons();
            return;
        }

        SyncMapWorkbenchDraftFromEditor();
        _mapViewerBox.Image = null;
        _mapViewerRenderedImage = _mapCanvasPreviewRenderer.Rebuild(
            _currentMapWorkbenchDraft,
            _mapMakerShowTerrainCheckBox.Checked,
            _mapMakerShowGridCheckBox.Checked,
            _mapMakerTerrainOpacityTrackBar.Value);
        _mapViewerBox.Image = _mapViewerRenderedImage;
        _mapMakerExportPreviewButton.Enabled = true;
        UpdateMapMakerEditingButtons();
    }

    private void ClearMapMakerPreviewImages()
    {
        _mapViewerBox.Image = null;
        _mapCanvasPreviewRenderer.Clear();
        _mapViewerRenderedImage = null;
    }

    private void RebuildMapMakerOverrideLookup()
    {
        _mapMakerMapCellOverrideLookup.Clear();
        if (_currentMapWorkbenchDraft == null) return;
        foreach (var cell in _currentMapWorkbenchDraft.MapCellOverrides)
        {
            if (cell.Index < 0 || cell.Index >= _currentMapWorkbenchDraft.CellCount) continue;
            if (string.IsNullOrWhiteSpace(cell.MaterialRelativePath)) continue;
            _mapMakerMapCellOverrideLookup[cell.Index] = CloneMapCellOverride(cell)!;
        }

        SyncMapWorkbenchOverridesFromLookup();
    }

    private void SyncMapWorkbenchOverridesFromLookup()
    {
        if (_currentMapWorkbenchDraft == null) return;
        _currentMapWorkbenchDraft.MapCellOverrides = _mapMakerMapCellOverrideLookup
            .OrderBy(pair => pair.Key)
            .Select(pair => CloneMapCellOverride(pair.Value)!)
            .ToList();
    }

    private void RecalculateMapMakerTerrainChangedCellCount()
    {
        _mapMakerTerrainChangedCellCount = 0;
        if (_terrainEditorCells.Length != _mapMakerOriginalTerrainCells.Length) return;
        for (var i = 0; i < _terrainEditorCells.Length; i++)
        {
            if (_terrainEditorCells[i] != _mapMakerOriginalTerrainCells[i]) _mapMakerTerrainChangedCellCount++;
        }
    }

    private void UpdateMapMakerTerrainChangedCount(byte oldValue, byte newValue, int index)
    {
        if (index < 0 || index >= _mapMakerOriginalTerrainCells.Length) return;
        var baseline = _mapMakerOriginalTerrainCells[index];
        var wasChanged = oldValue != baseline;
        var isChanged = newValue != baseline;
        if (wasChanged == isChanged) return;
        _mapMakerTerrainChangedCellCount += isChanged ? 1 : -1;
    }

    private void RefreshMapMakerPreviewTile(Rectangle sourceRect)
    {
        if (sourceRect.IsEmpty)
        {
            RenderMapMakerPreview(force: true);
            return;
        }

        var displayRect = MapMakerSourceRectToDisplayRect(sourceRect);
        _mapViewerBox.Invalidate(displayRect.IsEmpty ? _mapViewerBox.ClientRectangle : displayRect);
    }

    private Rectangle MapMakerSourceRectToDisplayRect(Rectangle sourceRect)
    {
        if (_mapViewerRenderedImage == null || _mapViewerBox.Width <= 0 || _mapViewerBox.Height <= 0) return Rectangle.Empty;
        var scaleX = _mapViewerBox.Width / (float)_mapViewerRenderedImage.Width;
        var scaleY = _mapViewerBox.Height / (float)_mapViewerRenderedImage.Height;
        var left = (int)Math.Floor(sourceRect.Left * scaleX);
        var top = (int)Math.Floor(sourceRect.Top * scaleY);
        var right = (int)Math.Ceiling(sourceRect.Right * scaleX);
        var bottom = (int)Math.Ceiling(sourceRect.Bottom * scaleY);
        return Rectangle.FromLTRB(
            Math.Max(0, left - 2),
            Math.Max(0, top - 2),
            Math.Min(_mapViewerBox.Width, right + 2),
            Math.Min(_mapViewerBox.Height, bottom + 2));
    }

    private void BeginMapMakerTerrainPaint(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (_currentMapWorkbenchDraft == null)
        {
            CreateNewMapWorkbenchDraftFromInputs();
            if (_currentMapWorkbenchDraft == null) return;
        }

        _mapMakerPainting = true;
        _mapMakerPendingMapPaintChanges.Clear();
        _mapMakerPendingMapPaintIndexes.Clear();
        _mapMakerPendingTerrainPaintChanges.Clear();
        _mapMakerPendingTerrainPaintIndexes.Clear();
        PaintMapWorkbenchCell(e.Location, groupWithCurrentStroke: true);
    }

    private void ContinueMapMakerTerrainPaint(MouseEventArgs e)
    {
        if (_mapMakerPainting && e.Button == MouseButtons.Left)
        {
            PaintMapWorkbenchCell(e.Location, groupWithCurrentStroke: true);
            return;
        }

        UpdateMapMakerCellInfo(e.Location);
    }

    private void EndMapMakerTerrainPaint()
    {
        if (!_mapMakerPainting) return;
        _mapMakerPainting = false;
        if (_mapMakerPendingMapPaintChanges.Count > 0)
        {
            _mapMakerMapUndoStack.Push(_mapMakerPendingMapPaintChanges.ToList());
            _mapMakerMapRedoStack.Clear();
        }
        if (_mapMakerPendingTerrainPaintChanges.Count > 0)
        {
            _mapMakerTerrainUndoStack.Push(_mapMakerPendingTerrainPaintChanges.ToList());
            _mapMakerTerrainRedoStack.Clear();
        }

        var hadChanges = _mapMakerPendingMapPaintChanges.Count > 0 || _mapMakerPendingTerrainPaintChanges.Count > 0;
        _mapMakerPendingMapPaintChanges.Clear();
        _mapMakerPendingMapPaintIndexes.Clear();
        _mapMakerPendingTerrainPaintChanges.Clear();
        _mapMakerPendingTerrainPaintIndexes.Clear();
        if (hadChanges)
        {
            SyncMapWorkbenchOverridesFromLookup();
        }

        if (_mapMakerRenderDeferred)
        {
            RenderMapMakerPreview(force: true);
        }
        else if (hadChanges)
        {
            _mapViewerInfoBox.Text = BuildMapMakerInfo("地图画笔绘制完成。");
        }
        UpdateMapMakerEditingButtons();
    }

    private void PaintMapWorkbenchCell(Point location, bool groupWithCurrentStroke)
    {
        if (_currentMapWorkbenchDraft == null || _mapViewerBox.Image == null) return;
        if (!TryMapPictureBoxPointToTerrainCell(_mapViewerBox, location, _currentMapWorkbenchDraft.GridWidth, _currentMapWorkbenchDraft.GridHeight, out var x, out var y)) return;
        var index = y * _currentMapWorkbenchDraft.GridWidth + x;
        UpdateMapMakerCellPreview(x, y, _terrainEditorCells.Length > index ? FormatTerrainValue(_terrainEditorCells[index]) : "未知");

        if (_mapWorkbenchBrushMode == MapWorkbenchBrushMode.MapBrush)
        {
            PaintMapWorkbenchMapCell(index, x, y, groupWithCurrentStroke);
            return;
        }

        if (_mapWorkbenchBrushMode == MapWorkbenchBrushMode.TerrainBrush)
        {
            PaintMapWorkbenchTerrainCell(index, x, y, groupWithCurrentStroke);
            return;
        }

        UpdateMapMakerCellInfo(location);
    }

    private void PaintMapWorkbenchMapCell(int index, int x, int y, bool groupWithCurrentStroke)
    {
        if (_currentMapWorkbenchDraft == null) return;
        if (_mapMakerSelectedMaterial == null)
        {
            _mapViewerInfoBox.Text = BuildMapMakerInfo("请先在右侧素材库选择一个图片素材。", x, y);
            return;
        }

        if (groupWithCurrentStroke && !_mapMakerPendingMapPaintIndexes.Add(index)) return;
        var oldValue = CloneMapCellOverride(GetMapCellOverride(index));
        var relative = MapDraftService.GetMaterialRelativePath(_currentMapWorkbenchDraft.MaterialRoot, _mapMakerSelectedMaterial.FilePath);
        var newValue = new MapCellOverride
        {
            Index = index,
            MaterialRelativePath = relative,
            MaterialCategory = _mapMakerSelectedMaterial.Category,
            DisplayName = _mapMakerSelectedMaterial.FileName
        };
        if (oldValue != null &&
            oldValue.MaterialRelativePath.Equals(newValue.MaterialRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            _mapViewerInfoBox.Text = BuildMapMakerInfo($"格子 ({x},{y}) 已经使用该素材。", x, y);
            return;
        }

        SetMapCellOverride(index, newValue);
        var change = new MapWorkbenchCellChange(index, oldValue, CloneMapCellOverride(newValue));
        if (groupWithCurrentStroke)
        {
            _mapMakerPendingMapPaintChanges.Add(change);
        }
        else
        {
            _mapMakerMapUndoStack.Push(new List<MapWorkbenchCellChange> { change });
            _mapMakerMapRedoStack.Clear();
        }

        var dirtyRect = _mapCanvasPreviewRenderer.UpdateMapCell(_currentMapWorkbenchDraft, index, newValue);
        RefreshMapMakerPreviewTile(dirtyRect);
        UpdateMapMakerCellPreview(x, y, _terrainEditorCells.Length > index ? FormatTerrainValue(_terrainEditorCells[index]) : "未知");
        _mapViewerInfoBox.Text = BuildMapMakerInfo($"地图画笔：格子 ({x},{y}) <- {_mapMakerSelectedMaterial.Category}/{_mapMakerSelectedMaterial.FileName}", x, y);
        SetStatus($"地图画笔：({x},{y})");
    }

    private void PaintMapWorkbenchTerrainCell(int index, int x, int y, bool groupWithCurrentStroke)
    {
        if (_currentMapWorkbenchDraft == null || _terrainEditorCells.Length != _currentMapWorkbenchDraft.CellCount) return;
        if (groupWithCurrentStroke && !_mapMakerPendingTerrainPaintIndexes.Add(index)) return;

        var oldValue = _terrainEditorCells[index];
        var newValue = (byte)_mapMakerTerrainBrushInput.Value;
        if (oldValue == newValue)
        {
            _mapViewerInfoBox.Text = BuildMapMakerInfo($"格子 ({x},{y}) 已经是 {FormatTerrainValue(newValue)}。", x, y);
            return;
        }

        _terrainEditorCells[index] = newValue;
        _currentMapWorkbenchDraft.TerrainCells = _terrainEditorCells;
        UpdateMapMakerTerrainChangedCount(oldValue, newValue, index);
        var change = new TerrainEditorCellChange(index, oldValue, newValue);
        if (groupWithCurrentStroke)
        {
            _mapMakerPendingTerrainPaintChanges.Add(change);
        }
        else
        {
            _mapMakerTerrainUndoStack.Push(new List<TerrainEditorCellChange> { change });
            _mapMakerTerrainRedoStack.Clear();
        }

        if (_mapMakerShowTerrainCheckBox.Checked)
        {
            var dirtyRect = _mapCanvasPreviewRenderer.UpdateTerrainCell(_currentMapWorkbenchDraft, index);
            RefreshMapMakerPreviewTile(dirtyRect);
        }
        UpdateMapMakerCellPreview(x, y, FormatTerrainValue(newValue));
        _mapViewerInfoBox.Text = BuildMapMakerInfo($"格子 ({x},{y})：{FormatTerrainValue(oldValue)} -> {FormatTerrainValue(newValue)}", x, y);
        SetStatus($"地形画笔：({x},{y})");
    }

    private void UndoMapWorkbenchPaint()
    {
        if (_currentMapWorkbenchDraft == null) return;
        if (_mapMakerTerrainUndoStack.Count > 0)
        {
            var changes = _mapMakerTerrainUndoStack.Pop();
            for (var i = changes.Count - 1; i >= 0; i--)
            {
                var change = changes[i];
                if (change.Index >= 0 && change.Index < _terrainEditorCells.Length)
                {
                    var previous = _terrainEditorCells[change.Index];
                    _terrainEditorCells[change.Index] = change.OldValue;
                    UpdateMapMakerTerrainChangedCount(previous, change.OldValue, change.Index);
                }
            }
            _mapMakerTerrainRedoStack.Push(changes);
            _currentMapWorkbenchDraft.TerrainCells = _terrainEditorCells;
            RenderMapMakerPreview();
            _mapViewerInfoBox.Text = BuildMapMakerInfo($"已撤销一笔地形绘制：{changes.Count} 格。");
            SetStatus("地图工作台已撤销地形绘制");
            return;
        }

        if (_mapMakerMapUndoStack.Count > 0)
        {
            var changes = _mapMakerMapUndoStack.Pop();
            for (var i = changes.Count - 1; i >= 0; i--)
            {
                var change = changes[i];
                SetMapCellOverride(change.Index, CloneMapCellOverride(change.OldValue));
            }
            _mapMakerMapRedoStack.Push(changes);
            RenderMapMakerPreview();
            _mapViewerInfoBox.Text = BuildMapMakerInfo($"已撤销一笔地图绘制：{changes.Count} 格。");
            SetStatus("地图工作台已撤销地图绘制");
        }
    }

    private void RedoMapWorkbenchPaint()
    {
        if (_currentMapWorkbenchDraft == null) return;
        if (_mapMakerTerrainRedoStack.Count > 0)
        {
            var changes = _mapMakerTerrainRedoStack.Pop();
            foreach (var change in changes)
            {
                if (change.Index >= 0 && change.Index < _terrainEditorCells.Length)
                {
                    var previous = _terrainEditorCells[change.Index];
                    _terrainEditorCells[change.Index] = change.NewValue;
                    UpdateMapMakerTerrainChangedCount(previous, change.NewValue, change.Index);
                }
            }
            _mapMakerTerrainUndoStack.Push(changes);
            _currentMapWorkbenchDraft.TerrainCells = _terrainEditorCells;
            RenderMapMakerPreview();
            _mapViewerInfoBox.Text = BuildMapMakerInfo($"已重做一笔地形绘制：{changes.Count} 格。");
            SetStatus("地图工作台已重做地形绘制");
            return;
        }

        if (_mapMakerMapRedoStack.Count > 0)
        {
            var changes = _mapMakerMapRedoStack.Pop();
            foreach (var change in changes)
            {
                SetMapCellOverride(change.Index, CloneMapCellOverride(change.NewValue));
            }
            _mapMakerMapUndoStack.Push(changes);
            RenderMapMakerPreview();
            _mapViewerInfoBox.Text = BuildMapMakerInfo($"已重做一笔地图绘制：{changes.Count} 格。");
            SetStatus("地图工作台已重做地图绘制");
        }
    }

    private void SetMapCellOverride(int index, MapCellOverride? value)
    {
        if (_currentMapWorkbenchDraft == null) return;
        if (value != null)
        {
            value.Index = index;
            _mapMakerMapCellOverrideLookup[index] = CloneMapCellOverride(value)!;
        }
        else
        {
            _mapMakerMapCellOverrideLookup.Remove(index);
        }
    }

    private MapCellOverride? GetMapCellOverride(int index)
        => _mapMakerMapCellOverrideLookup.TryGetValue(index, out var value) ? value : null;

    private static MapCellOverride? CloneMapCellOverride(MapCellOverride? value)
        => value == null
            ? null
            : new MapCellOverride
            {
                Index = value.Index,
                MaterialRelativePath = value.MaterialRelativePath,
                MaterialCategory = value.MaterialCategory,
                DisplayName = value.DisplayName
            };

    private void ClearMapMakerCellPreview()
    {
        _mapViewerCellPreviewLabel.Text = "地形：-    坐标：-";
    }

    private void UpdateMapMakerCellPreview(int x, int y, string terrain)
    {
        _mapViewerCellPreviewLabel.Text = $"地形：{terrain}    坐标：({x}, {y})";
    }

    private void UpdateMapMakerCellInfo(Point location)
    {
        if (_currentMapWorkbenchDraft == null || _mapViewerBox.Image == null)
        {
            ClearMapMakerCellPreview();
            return;
        }

        if (!TryMapPictureBoxPointToTerrainCell(_mapViewerBox, location, _currentMapWorkbenchDraft.GridWidth, _currentMapWorkbenchDraft.GridHeight, out var x, out var y))
        {
            ClearMapMakerCellPreview();
            return;
        }
        var index = y * _currentMapWorkbenchDraft.GridWidth + x;
        var terrain = _terrainEditorCells.Length > index ? FormatTerrainValue(_terrainEditorCells[index]) : "未知";
        var mapCell = GetMapCellOverride(index);
        UpdateMapMakerCellPreview(x, y, terrain);
        var mapText = mapCell == null ? "底稿/空白" : $"{mapCell.MaterialCategory}/{mapCell.DisplayName}";
        _mapViewerInfoBox.Text = BuildMapMakerInfo($"当前格子 ({x},{y})：地图={mapText}，地形={terrain}。", x, y);
    }

    private void UpdateMapMakerEditingButtons()
    {
        var hasDraft = _currentMapWorkbenchDraft != null;
        var hasBoundMap = hasDraft && _currentMapMakerItem != null;
        var canPublishMap = CanPublishCurrentMapWorkbenchMap(out _);
        var canPublishTerrain = CanPublishCurrentMapWorkbenchTerrain(out _);
        if (!hasDraft && _mapMakerEditTerrainCheckBox.Checked)
        {
            _mapMakerEditTerrainCheckBox.Checked = false;
        }

        _mapMakerSaveDraftButton.Enabled = hasDraft;
        _mapMakerEditTerrainCheckBox.Enabled = hasDraft;
        _mapMakerSaveTerrainButton.Enabled = hasDraft;
        _mapMakerUndoTerrainButton.Enabled = hasDraft && (_mapMakerMapUndoStack.Count > 0 || _mapMakerTerrainUndoStack.Count > 0);
        _mapMakerRedoTerrainButton.Enabled = hasDraft && (_mapMakerMapRedoStack.Count > 0 || _mapMakerTerrainRedoStack.Count > 0);
        _mapMakerReplaceMapImageButton.Enabled = hasBoundMap;
        _mapMakerExportPreviewButton.Enabled = _mapViewerBox.Image != null;
        _mapMakerExportJpgButton.Enabled = hasDraft;
        _mapMakerPublishMapButton.Enabled = canPublishMap;
        _mapMakerPublishTerrainButton.Enabled = canPublishTerrain;
        _mapViewerBox.Cursor = hasDraft && _mapWorkbenchBrushMode != MapWorkbenchBrushMode.Browse ? Cursors.Cross : Cursors.Default;
    }

    private void RefreshMapMakerPresetCombo()
    {
        _updatingMapMakerPresetSelection = true;
        try
        {
            var presets = _terrainEditorTerrainLookup
                .OrderBy(x => x.Key)
                .Select(x => new TerrainEditorPreset(x.Key, x.Value))
                .ToList();
            if (presets.Count == 0)
            {
                presets.Add(new TerrainEditorPreset((byte)_mapMakerTerrainBrushInput.Value, string.Empty));
            }

            _mapMakerTerrainPresetCombo.DataSource = presets;
            _mapMakerTerrainPresetCombo.DisplayMember = nameof(TerrainEditorPreset.DisplayName);
            _mapMakerTerrainPresetCombo.ValueMember = nameof(TerrainEditorPreset.Id);
        }
        finally
        {
            _updatingMapMakerPresetSelection = false;
        }

        SelectMapMakerPresetForBrush((byte)_mapMakerTerrainBrushInput.Value);
        UpdateMapMakerBrushLabel();
    }

    private void SelectMapMakerTerrainPreset()
    {
        if (_updatingMapMakerPresetSelection) return;
        if (_mapMakerTerrainPresetCombo.SelectedItem is not TerrainEditorPreset preset) return;
        _mapMakerTerrainBrushInput.Value = preset.Id;
    }

    private void UpdateMapMakerBrushLabel()
    {
        var value = (byte)_mapMakerTerrainBrushInput.Value;
        _mapMakerBrushNameLabel.Text = "地形：" + FormatTerrainValue(value);
        SelectMapMakerPresetForBrush(value);
    }

    private void SelectMapMakerPresetForBrush(byte value)
    {
        if (_updatingMapMakerPresetSelection) return;
        if (_mapMakerTerrainPresetCombo.DataSource is not IEnumerable<TerrainEditorPreset> presets) return;
        var match = presets.FirstOrDefault(x => x.Id == value);
        if (match == null) return;

        _updatingMapMakerPresetSelection = true;
        try
        {
            _mapMakerTerrainPresetCombo.SelectedItem = match;
        }
        finally
        {
            _updatingMapMakerPresetSelection = false;
        }
    }

    private string BuildMapMakerInfo(string actionText, int? cellX = null, int? cellY = null)
    {
        if (_currentMapWorkbenchDraft == null)
        {
            return "地图制作：请新建草稿，或从左侧选择已有 Mxxx 地图槽位创建绑定草稿。";
        }

        if (_mapMakerPainting)
        {
            var paintingModeText = _mapWorkbenchBrushMode switch
            {
                MapWorkbenchBrushMode.MapBrush => "map brush",
                MapWorkbenchBrushMode.TerrainBrush => "terrain brush",
                _ => "browse"
            };
            var paintingCellText = cellX.HasValue && cellY.HasValue
                ? $"    cell=({cellX},{cellY})"
                : string.Empty;
            return $"{actionText}\r\nmode={paintingModeText}    map cells={_mapMakerMapCellOverrideLookup.Count}    terrain changes={CountMapWorkbenchTerrainChangedCells()}{paintingCellText}";
        }

        var mapId = _currentMapWorkbenchDraft.BoundMapId;
        var boundText = _currentMapMakerItem == null
            ? "未绑定游戏槽位"
            : $"{_currentMapMakerItem.Name} ({_currentMapMakerItem.GridWidth}x{_currentMapMakerItem.GridHeight})";
        var imageSize = $"{_currentMapWorkbenchDraft.PixelWidth}x{_currentMapWorkbenchDraft.PixelHeight}";
        var gridSizeText = $"{_currentMapWorkbenchDraft.GridWidth}x{_currentMapWorkbenchDraft.GridHeight}（48x48/格）";
        var terrainText = BuildMapWorkbenchPublishReasonText();
        var modeText = _mapWorkbenchBrushMode switch
        {
            MapWorkbenchBrushMode.MapBrush => "地图画笔",
            MapWorkbenchBrushMode.TerrainBrush => "地形画笔",
            _ => "浏览"
        };

        var cellText = string.Empty;
        if (cellX.HasValue && cellY.HasValue)
        {
            var index = cellY.Value * _currentMapWorkbenchDraft.GridWidth + cellX.Value;
            if (index >= 0 && index < _terrainEditorCells.Length)
            {
                cellText = $"\r\n当前格：({cellX},{cellY}) = {FormatTerrainValue(_terrainEditorCells[index])}";
            }
        }

        var targetKey = _currentMapMakerItem == null ? $"地图草稿/{_currentMapWorkbenchDraft.DraftId}" : $"{_currentMapMakerItem.Category}/{_currentMapMakerItem.Name}";
        return
            $"{actionText}\r\n" +
            $"草稿：{_currentMapWorkbenchDraft.DraftId}    绑定={boundText}    地图ID={mapId}    图片={imageSize}    格数={gridSizeText}    缩放={_mapZoomTrackBar.Value}%\r\n" +
            $"模式：{modeText}    地图覆盖格={_currentMapWorkbenchDraft.MapCellOverrides.Count}    地形画笔={FormatTerrainValue((byte)_mapMakerTerrainBrushInput.Value)}    草稿地形改动={CountMapWorkbenchTerrainChangedCells()}    撤销={_mapMakerMapUndoStack.Count + _mapMakerTerrainUndoStack.Count}    重做={_mapMakerMapRedoStack.Count + _mapMakerTerrainRedoStack.Count}{cellText}\r\n" +
            $"发布状态：{terrainText}\r\n" +
            $"素材库：{(string.IsNullOrWhiteSpace(_currentMapWorkbenchDraft.MaterialRoot) ? "未选择" : _currentMapWorkbenchDraft.MaterialRoot)}\r\n" +
            $"底稿：{(string.IsNullOrWhiteSpace(_currentMapWorkbenchDraft.BaseLayerPath) ? "无底稿；导出未填充格为纯黑" : _currentMapWorkbenchDraft.BaseLayerPath)}";
    }

    private string BuildMapWorkbenchPublishReasonText()
    {
        var mapOk = CanPublishCurrentMapWorkbenchMap(out var mapReason);
        var terrainOk = CanPublishCurrentMapWorkbenchTerrain(out var terrainReason);
        return $"底图发布={(mapOk ? "可用" : "禁用：" + mapReason)}；地形发布={(terrainOk ? "可用" : "禁用：" + terrainReason)}";
    }

    private bool CanPublishCurrentMapWorkbenchMap(out string reason)
    {
        reason = string.Empty;
        if (_currentMapWorkbenchDraft == null)
        {
            reason = "没有草稿";
            return false;
        }
        if (_currentMapMakerItem == null)
        {
            reason = "未绑定已有 Mxxx 槽位";
            return false;
        }
        if (_currentMapMakerItem.GridWidth != _currentMapWorkbenchDraft.GridWidth ||
            _currentMapMakerItem.GridHeight != _currentMapWorkbenchDraft.GridHeight)
        {
            reason = $"草稿 {_currentMapWorkbenchDraft.GridWidth}x{_currentMapWorkbenchDraft.GridHeight} 与槽位 {_currentMapMakerItem.GridWidth}x{_currentMapMakerItem.GridHeight} 不一致";
            return false;
        }

        return true;
    }

    private bool CanPublishCurrentMapWorkbenchTerrain(out string reason)
    {
        if (!CanPublishCurrentMapWorkbenchMap(out reason)) return false;
        if (_currentMapWorkbenchDraft == null || _currentMapMakerItem == null)
        {
            reason = "没有草稿或绑定槽位";
            return false;
        }
        if (!EnsureMapMakerHexzmapLoaded(showMessage: false))
        {
            reason = "Hexzmap.e5 未读取或不可用";
            return false;
        }

        var block = FindHexzmapBlockForMap(_currentMapMakerItem);
        if (block == null)
        {
            reason = "没有同编号 Hexzmap 块";
            return false;
        }

        var cellCount = _currentMapWorkbenchDraft.CellCount;
        if (block.Width != _currentMapWorkbenchDraft.GridWidth ||
            block.Height != _currentMapWorkbenchDraft.GridHeight ||
            block.BytesRead != cellCount ||
            block.SegmentLength != cellCount + HexzmapProbeReader.TerrainHeaderSize)
        {
            reason = $"Hexzmap 块 {block.Width}x{block.Height} / segmentLength={block.SegmentLength} 与草稿格数不匹配";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private int CountMapWorkbenchTerrainChangedCells()
    {
        if (_terrainEditorCells.Length != _mapMakerOriginalTerrainCells.Length) return 0;
        return _mapMakerTerrainChangedCellCount;
    }

    private void ReplaceCurrentMapImage()
    {
        if (_project == null || _currentMapMakerItem == null)
        {
            MessageBox.Show(this, "请先选择要替换的地图底图。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "选择新的地图底图 JPG",
            Filter = "JPEG 地图图片 (*.jpg;*.jpeg)|*.jpg;*.jpeg|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            int oldWidth;
            int oldHeight;
            int newWidth;
            int newHeight;
            using (var oldImage = Image.FromFile(_currentMapMakerItem.Path))
            using (var newImage = Image.FromFile(dialog.FileName))
            {
                oldWidth = oldImage.Width;
                oldHeight = oldImage.Height;
                newWidth = newImage.Width;
                newHeight = newImage.Height;
            }

            var warning = oldWidth == newWidth && oldHeight == newHeight
                ? string.Empty
                : $"\r\n\r\n注意：尺寸将从 {oldWidth}x{oldHeight} 变为 {newWidth}x{newHeight}。地图底图通常建议保持尺寸一致，尺寸变化可能影响显示和坐标对齐。";
            if (MessageBox.Show(this,
                    $"即将替换 Map\\{_currentMapMakerItem.Name}。\r\n来源：{dialog.FileName}{warning}\r\n\r\n保存前会自动备份，保存后会复读校验。是否继续？",
                    "确认替换地图底图",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            Cursor = Cursors.WaitCursor;
            var selectedName = _currentMapMakerItem.Name;
            var result = _mapImageReplaceService.ReplaceMapImage(_project, _currentMapMakerItem.Path, dialog.FileName);
            LoadMapImages();
            SelectMapImageByName(selectedName);
            System.Diagnostics.Debug.WriteLine($"已替换地图底图：{selectedName} backup={result.BackupPath} report={result.ReportJsonPath}");
            MessageBox.Show(this,
                $"地图底图替换完成。\r\n尺寸：{result.OldWidth}x{result.OldHeight} -> {result.NewWidth}x{result.NewHeight}\r\n备份：{result.BackupPath}\r\n报告：{result.ReportJsonPath}",
                "替换完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("替换地图底图失败：" + ex);
            MessageBox.Show(this, ex.Message, "替换地图底图失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ExportCurrentMapMakerPreviewPng()
    {
        if (_mapViewerBox.Image == null || _currentMapMakerItem == null)
        {
            MessageBox.Show(this, "请先选择地图并生成预览。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var exportRoot = _project != null
            ? Path.Combine(_project.WorkspaceRoot, "CCZModStudio_Exports")
            : Directory.GetCurrentDirectory();
        Directory.CreateDirectory(exportRoot);
        using var dialog = new SaveFileDialog
        {
            Title = "导出地图制作预览 PNG",
            Filter = "PNG 图片 (*.png)|*.png|所有文件 (*.*)|*.*",
            FileName = MakeSafeFileName($"{Path.GetFileNameWithoutExtension(_currentMapMakerItem.Name)}_地图制作预览.png"),
            InitialDirectory = exportRoot
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            _mapViewerBox.Image.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);
            System.Diagnostics.Debug.WriteLine("已导出地图制作预览 PNG：" + dialog.FileName);
            SetStatus("地图制作预览 PNG 导出完成");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("导出地图制作预览 PNG 失败：" + ex);
            MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportCurrentMapWorkbenchJpg()
    {
        if (_currentMapWorkbenchDraft == null)
        {
            MessageBox.Show(this, "请先新建或载入地图草稿。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SyncMapWorkbenchDraftFromEditor();
        var exportRoot = _project != null
            ? Path.Combine(_project.WorkspaceRoot, "CCZModStudio_Exports", "MapWorkbench")
            : Directory.GetCurrentDirectory();
        Directory.CreateDirectory(exportRoot);
        using var dialog = new SaveFileDialog
        {
            Title = "导出地图工作台 JPG",
            Filter = "JPEG 地图图片 (*.jpg)|*.jpg|所有文件 (*.*)|*.*",
            FileName = MakeSafeFileName($"{(_currentMapMakerItem == null ? _currentMapWorkbenchDraft.DraftId : Path.GetFileNameWithoutExtension(_currentMapMakerItem.Name))}_地图草稿.jpg"),
            InitialDirectory = exportRoot
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            _mapCanvasPublishService.ExportJpeg(_currentMapWorkbenchDraft, dialog.FileName);
            using var verify = Image.FromFile(dialog.FileName);
            var expectedWidth = _currentMapWorkbenchDraft.GridWidth * MapResourceItem.MapTilePixelSize;
            var expectedHeight = _currentMapWorkbenchDraft.GridHeight * MapResourceItem.MapTilePixelSize;
            if (verify.Width != expectedWidth || verify.Height != expectedHeight)
            {
                throw new InvalidOperationException($"导出 JPG 尺寸校验失败：实际 {verify.Width}x{verify.Height}，期望 {expectedWidth}x{expectedHeight}。");
            }

            System.Diagnostics.Debug.WriteLine("已导出地图工作台 JPG：" + dialog.FileName);
            SetStatus("地图工作台 JPG 导出完成");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("导出地图工作台 JPG 失败：" + ex);
            MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PublishCurrentMapWorkbenchMapImage()
    {
        if (_project == null || _currentMapWorkbenchDraft == null || _currentMapMakerItem == null)
        {
            MessageBox.Show(this, "请先绑定已有 Mxxx 地图槽位。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!CanPublishCurrentMapWorkbenchMap(out var reason))
        {
            MessageBox.Show(this, reason, "禁止发布到底图", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SyncMapWorkbenchDraftFromEditor();
        if (MessageBox.Show(this,
                $"即将把草稿发布到 Map\\{_currentMapMakerItem.Name}。\r\n草稿尺寸：{_currentMapWorkbenchDraft.GridWidth}x{_currentMapWorkbenchDraft.GridHeight}，输出 JPG：{_currentMapWorkbenchDraft.PixelWidth}x{_currentMapWorkbenchDraft.PixelHeight}\r\n保存前会自动备份，保存后复读校验。是否继续？",
                "确认发布到底图",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var selectedName = _currentMapMakerItem.Name;
            var result = _mapCanvasPublishService.PublishToMapImage(_project, _currentMapWorkbenchDraft, _currentMapMakerItem);
            _mapDraftService.SaveDraft(_project, _currentMapWorkbenchDraft);
            _mapWorkbenchSettings.LastDraftId = _currentMapWorkbenchDraft.DraftId;
            SaveMapWorkbenchSettings();
            LoadMapImages();
            SelectMapImageByName(selectedName);
            System.Diagnostics.Debug.WriteLine($"已发布地图工作台底图：{selectedName} backup={result.BackupPath} report={result.ReportJsonPath}");
            MessageBox.Show(this,
                $"发布到底图完成。\r\n尺寸：{result.OldWidth}x{result.OldHeight} -> {result.NewWidth}x{result.NewHeight}\r\n备份：{result.BackupPath}\r\n报告：{result.ReportJsonPath}",
                "发布完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("发布地图工作台底图失败：" + ex);
            MessageBox.Show(this, ex.Message, "发布到底图失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void PublishCurrentMapWorkbenchTerrain()
    {
        if (_project == null || _currentMapWorkbenchDraft == null || _currentMapMakerItem == null)
        {
            MessageBox.Show(this, "请先绑定已有 Mxxx 地图槽位。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!CanPublishCurrentMapWorkbenchTerrain(out var reason))
        {
            MessageBox.Show(this, reason, "禁止发布到地形层", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_currentHexzmapProbe == null)
        {
            MessageBox.Show(this, "Hexzmap.e5 尚未读取。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var block = FindHexzmapBlockForMap(_currentMapMakerItem);
        if (block == null)
        {
            MessageBox.Show(this, "没有找到同编号 Hexzmap 地形块。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SyncMapWorkbenchDraftFromEditor();
        var currentCells = _hexzmapProbeReader.GetBlockCells(_currentHexzmapProbe, block);
        var changed = CountChangedBytes(currentCells, _currentMapWorkbenchDraft.TerrainCells);
        if (changed == 0)
        {
            MessageBox.Show(this, "草稿地形层与当前 Hexzmap 块完全一致，无需发布。", "无需发布", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(this,
                $"即将发布草稿地形层到 Hexzmap.e5 的 {block.MapId}。\r\n改动格子：{changed}\r\n保存前会自动备份，保存后复读校验。是否继续？",
                "确认发布到地形层",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = _hexzmapEditorService.SaveBlock(_project, _currentHexzmapProbe, block, _currentMapWorkbenchDraft.TerrainCells, _terrainEditorTerrainLookup);
            _currentHexzmapProbe = _hexzmapProbeReader.Read(_project, _terrainEditorTerrainLookup);
            var refreshedBlock = _currentHexzmapProbe.Blocks.First(x => x.Index == block.Index);
            var reread = _hexzmapProbeReader.GetBlockCells(_currentHexzmapProbe, refreshedBlock);
            if (!reread.SequenceEqual(_currentMapWorkbenchDraft.TerrainCells))
            {
                throw new InvalidOperationException("地形层发布后复读校验失败：Hexzmap 块内容与草稿地形层不一致。");
            }

            _terrainEditorBlock = refreshedBlock;
            _terrainEditorOriginalCells = reread.ToArray();
            _terrainEditorCells = reread.ToArray();
            _mapMakerOriginalTerrainCells = reread.ToArray();
            _currentMapWorkbenchDraft.TerrainCells = reread.ToArray();
            _mapDraftService.SaveDraft(_project, _currentMapWorkbenchDraft);
            RenderMapMakerPreview();
            System.Diagnostics.Debug.WriteLine($"已发布地图工作台地形层：{result.MapId} changed={result.ChangedCells} backup={result.BackupPath}");
            MessageBox.Show(this,
                $"发布到地形层完成。\r\n改动格子：{result.ChangedCells}\r\n备份：{result.BackupPath}\r\n报告：{result.ReportJsonPath}",
                "发布完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("发布地图工作台地形层失败：" + ex);
            MessageBox.Show(this, ex.Message, "发布地形层失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private static int CountChangedBytes(byte[] left, byte[] right)
    {
        var length = Math.Min(left.Length, right.Length);
        var count = Math.Abs(left.Length - right.Length);
        for (var i = 0; i < length; i++)
        {
            if (left[i] != right[i]) count++;
        }

        return count;
    }

    private bool SelectMapImageByName(string mapImageName)
    {
        var mapId = mapImageName.TrimStart('M', 'm');
        for (var i = 0; i < _mapImageList.Items.Count; i++)
        {
            if (_mapImageList.Items[i] is not MapResourceItem map) continue;
            if (!map.Name.Equals(mapImageName, StringComparison.OrdinalIgnoreCase) &&
                !map.Id.Equals(mapId, StringComparison.OrdinalIgnoreCase)) continue;
            _mapImageList.SelectedIndex = i;
            LoadSelectedMapImage();
            return true;
        }

        return false;
    }

    private void LoadMapImages()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            if (_currentMapResources.Count == 0)
            {
                _currentMapResources = _mapResourceIndexer.Index(_project);
            }

            var maps = _currentMapResources
                .Where(x => x.Category == "地图图片")
                .OrderBy(x => x.Id)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            _mapImageList.DisplayMember = nameof(MapResourceItem.Name);
            _mapImageList.DataSource = new BindingList<MapResourceItem>(maps);
            _mapViewerInfoBox.Text = $"Map 目录地图图片：{maps.Count} 张。选择左侧条目后，可叠加 Hexzmap.e5 地形层、查看格坐标、直接绘制地形或替换底图。";
            System.Diagnostics.Debug.WriteLine($"已读取 Map 图片：{maps.Count} 张。");
            SetStatus("Map 图片读取完成");
            if (maps.Count > 0) _mapImageList.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            _mapViewerInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("Map 图片读取失败：" + ex);
            MessageBox.Show(this, ex.Message, "Map 图片读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void LoadSelectedMapImage()
    {
        if (_mapImageList.SelectedItem is not MapResourceItem item) return;
        if (_project == null) return;

        try
        {
            Cursor = Cursors.WaitCursor;
            _currentMapMakerItem = item;
            ClearMapMakerPreviewImages();
            using var source = Image.FromFile(item.Path);
            var targetKey = $"{item.Category}/{item.Name}";
            _currentMapWorkbenchDraft = _mapDraftService.CreateDraftFromMap(_project, item, _mapWorkbenchSettings.LastMaterialRoot);
            var block = TryGetMatchingHexzmapBlockForMap(item);
            if (block != null && _currentHexzmapProbe != null)
            {
                var cells = _hexzmapProbeReader.GetBlockCells(_currentHexzmapProbe, block);
                if (cells.Length == _currentMapWorkbenchDraft.CellCount)
                {
                    _currentMapWorkbenchDraft.TerrainCells = cells.ToArray();
                    _terrainEditorBlock = block;
                }
            }

            BindMapWorkbenchDraftToEditor(resetHistory: true);
            _mapViewerInfoBox.Text = BuildMapMakerInfo("已载入地图底图。");
            FitMapToView();
            SetStatus($"地图制作：{item.Name}");
        }
        catch (Exception ex)
        {
            _currentMapMakerItem = null;
            _currentMapWorkbenchDraft = null;
            ClearMapMakerPreviewImages();
            _mapViewerInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("地图图片加载失败：" + ex);
            UpdateMapMakerEditingButtons();
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void FitMapToView()
    {
        if (_mapViewerRenderedImage == null || _mapViewerBox.Parent == null) return;
        var client = _mapViewerBox.Parent.ClientSize;
        if (client.Width <= 0 || client.Height <= 0) return;
        var zoomX = client.Width * 100.0 / _mapViewerRenderedImage.Width;
        var zoomY = client.Height * 100.0 / _mapViewerRenderedImage.Height;
        var zoom = (int)Math.Clamp(Math.Floor(Math.Min(zoomX, zoomY)), _mapZoomTrackBar.Minimum, _mapZoomTrackBar.Maximum);
        _mapZoomTrackBar.Value = Math.Max(_mapZoomTrackBar.Minimum, zoom);
        ApplyMapZoom();
    }

    private void ApplyMapZoom()
    {
        if (_mapViewerRenderedImage == null) return;
        var zoom = _mapZoomTrackBar.Value / 100.0;
        _mapViewerBox.Width = Math.Max(1, (int)Math.Round(_mapViewerRenderedImage.Width * zoom));
        _mapViewerBox.Height = Math.Max(1, (int)Math.Round(_mapViewerRenderedImage.Height * zoom));
        if (_currentMapMakerItem != null)
        {
            _mapViewerInfoBox.Text = BuildMapMakerInfo("缩放已调整。");
            return;
        }

        if (!_mapViewerInfoBox.Text.Contains("缩放", StringComparison.Ordinal))
        {
            _mapViewerInfoBox.AppendText($"\r\n缩放：{_mapZoomTrackBar.Value}%");
        }
        else
        {
            var lines = _mapViewerInfoBox.Lines.ToList();
            if (lines.Count > 0 && lines[^1].StartsWith("缩放：", StringComparison.Ordinal))
            {
                lines[^1] = $"缩放：{_mapZoomTrackBar.Value}%";
                _mapViewerInfoBox.Lines = lines.ToArray();
            }
        }
    }

    private void HandleMapViewerMouseWheel(MouseEventArgs e)
    {
        if (_mapViewerRenderedImage == null || _mapViewerBox.Parent is not Panel scrollPanel || e.Delta == 0) return;

        var oldZoom = Math.Max(0.01, _mapZoomTrackBar.Value / 100.0);
        var panelPoint = scrollPanel.PointToClient(Control.MousePosition);
        var imagePointX = (panelPoint.X - _mapViewerBox.Left) / oldZoom;
        var imagePointY = (panelPoint.Y - _mapViewerBox.Top) / oldZoom;
        var step = ModifierKeys.HasFlag(Keys.Control) ? 25 : 10;
        var nextZoom = _mapZoomTrackBar.Value + (e.Delta > 0 ? step : -step);
        _mapZoomTrackBar.Value = Math.Clamp(nextZoom, _mapZoomTrackBar.Minimum, _mapZoomTrackBar.Maximum);
        ApplyMapZoom();

        var newZoom = _mapZoomTrackBar.Value / 100.0;
        var scrollX = Math.Max(0, (int)Math.Round(imagePointX * newZoom - panelPoint.X));
        var scrollY = Math.Max(0, (int)Math.Round(imagePointY * newZoom - panelPoint.Y));
        scrollPanel.AutoScrollPosition = new Point(scrollX, scrollY);
    }
}
