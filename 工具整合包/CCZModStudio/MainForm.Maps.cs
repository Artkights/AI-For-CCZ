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
            $"长度：{item.Length:N0} 字节    Magic：{item.Magic}    Header：{item.HeaderText}    PayloadOffset：{HexDisplayFormatter.FormatOffset(item.PayloadOffset)}\r\n" +
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
            ? $"{HexDisplayFormatter.FormatByte(value)}（{name}）"
            : HexDisplayFormatter.FormatByte(value);

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
                $"Magic：{_currentHexzmapProbe.Magic}    有效Ls头：{_currentHexzmapProbe.MagicValid}    PayloadOffset：{HexDisplayFormatter.FormatOffset(_currentHexzmapProbe.PayloadOffset)}\r\n" +
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
            $"对应地图图片：{(block.MapImageExists ? block.MapImageName : "未找到同编号 Mxxx.jpg")}    地形种类：{block.UniqueTerrainCount}    已知图例：{block.KnownTerrainCount}    主地形：{HexDisplayFormatter.Format(block.DominantTerrainId, 2)} {block.DominantTerrainName} x {block.DominantTerrainCount}\r\n" +
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
                : _materialLibraryCache.GetOrIndexExplicitRoot(MaterialLibraryIndexer.ResolveMaterialLibraryRoot(_project));
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
            _currentMapWorkbenchDraft.OriginalTerrainCells = _terrainEditorCells.ToArray();
            _currentMapWorkbenchDraft.TerrainCells = _terrainEditorCells.ToArray();
            DeriveCurrentMapWorkbenchTerrain();
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
        if (!string.IsNullOrWhiteSpace(item.MapId)) return item.MapId;
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
        using var perf = TracePerf("LoadMapWorkbenchSettings");
        if (_project == null) return;
        try
        {
            _mapWorkbenchSettings = _mapDraftService.LoadSettings(_project);
            _mapWorkbenchSettings.PersistedTerrainMaterialPlans ??= new List<PersistedTerrainMaterialPlan>();
            var materialRoot = ResolveDefaultMapWorkbenchMaterialRoot();
            if (!string.IsNullOrWhiteSpace(materialRoot))
            {
                _mapWorkbenchSettings.LastMaterialRoot = Path.GetFullPath(materialRoot);
                _mapMakerMaterialInfoBox.Text =
                    $"素材根目录：{_mapWorkbenchSettings.LastMaterialRoot}\r\n" +
                    "素材库将在首次进入地图编辑、读取地图或使用素材功能时加载。";
            }
            else if (!string.IsNullOrWhiteSpace(_mapWorkbenchSettings.LastMaterialRoot))
            {
                _mapMakerMaterialInfoBox.Text = "自动素材目录不可达；真实底图仍可预览，但右侧素材绘制需要重新选择素材库。";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("读取地图工作台设置失败：" + ex.Message);
            _mapWorkbenchSettings = new MapWorkbenchSettings();
        }
    }

    private string ResolveDefaultMapWorkbenchMaterialRoot()
    {
        if (_project == null) return string.Empty;
        if (TryResolveExistingMaterialRoot(_mapWorkbenchSettings.LastMaterialRoot, out var existingRoot)) return existingRoot;
        if (TryResolveExistingMaterialRoot(_project.MaterialLibraryRoot, out existingRoot)) return existingRoot;

        var candidates = new List<string>();
        AddMaterialRootCandidate(candidates, _mapWorkbenchSettings.LastMaterialRoot);
        AddMaterialRootCandidate(candidates, MaterialLibraryIndexer.ResolveMaterialLibraryRoot(_project));
        AddMaterialRootCandidate(candidates, MaterialLibraryIndexer.ResolveMaterialLibraryRoot(_project.WorkspaceRoot));
        AddMaterialRootCandidate(candidates, Path.Combine(_project.GameRoot, "素材库"));
        AddMaterialRootCandidate(candidates, Path.Combine(_project.WorkspaceRoot, "素材库"));
        AddMaterialRootCandidate(candidates, Path.Combine(_project.WorkspaceRoot, "老版游戏制作工具", "素材库"));
        AddMaterialRootCandidate(candidates, Path.Combine(_project.WorkspaceRoot, "老版游戏制作工具", "普罗-综合工具v0.3", "素材库"));
        AddMaterialRootCandidate(candidates, PortableInstallPaths.LegacyResource("普罗-综合工具v0.3", "素材库"));

        return MaterialLibraryIndexer.SelectBestMaterialLibraryRoot(candidates) ?? string.Empty;
    }

    private static bool TryResolveExistingMaterialRoot(string? path, out string root)
    {
        root = string.Empty;
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath)) return false;
            root = fullPath;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void AddMaterialRootCandidate(List<string> candidates, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!candidates.Any(x => x.Equals(fullPath, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add(fullPath);
            }
        }
        catch
        {
            // Ignore malformed persisted paths; fallback candidates below keep the workbench usable.
        }
    }

    private bool EnsureMapWorkbenchMaterialLibraryIndexed(bool showMessages)
    {
        if (!string.IsNullOrWhiteSpace(_mapWorkbenchSettings.LastMaterialRoot) &&
            Directory.Exists(_mapWorkbenchSettings.LastMaterialRoot) &&
            _currentMaterialAssets.Count > 0)
        {
            return true;
        }

        var root = ResolveDefaultMapWorkbenchMaterialRoot();
        if (string.IsNullOrWhiteSpace(root))
        {
            if (showMessages)
            {
                MessageBox.Show(this, "未找到素材库目录。真实底图仍可预览；如需绘制素材，请先选择包含“地形 / 建筑 / 景物”的素材库。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            return false;
        }

        IndexMapWorkbenchMaterialRoot(root, showMessages, populateBrowser: false);
        return _currentMaterialAssets.Count > 0;
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

    private string GetMapWorkbenchProjectKey()
    {
        if (_project == null) return string.Empty;
        try
        {
            return Path.GetFullPath(_project.GameRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
        }
        catch
        {
            return _project.GameRoot.Trim().ToUpperInvariant();
        }
    }

    private void InheritPersistedTerrainMaterialPlan(MapWorkbenchDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.BoundMapId)) return;
        var projectKey = GetMapWorkbenchProjectKey();
        if (string.IsNullOrWhiteSpace(projectKey)) return;

        var persisted = _mapWorkbenchSettings.PersistedTerrainMaterialPlans
            .FirstOrDefault(plan =>
                plan.ProjectKey.Equals(projectKey, StringComparison.OrdinalIgnoreCase) &&
                plan.MapId.Equals(draft.BoundMapId, StringComparison.OrdinalIgnoreCase));
        if (persisted == null || persisted.Items.Count == 0) return;

        draft.TerrainMaterialPlan = persisted.Items
            .Select(CloneTerrainMaterialPlanItem)
            .ToList();
    }

    private void PersistCurrentTerrainMaterialPlan()
    {
        if (_project == null || _currentMapWorkbenchDraft == null) return;
        if (string.IsNullOrWhiteSpace(_currentMapWorkbenchDraft.BoundMapId)) return;
        var projectKey = GetMapWorkbenchProjectKey();
        if (string.IsNullOrWhiteSpace(projectKey)) return;

        _mapWorkbenchSettings.PersistedTerrainMaterialPlans ??= new List<PersistedTerrainMaterialPlan>();
        var items = _currentMapWorkbenchDraft.TerrainMaterialPlan
            .Select(CloneTerrainMaterialPlanItem)
            .ToList();
        var existing = _mapWorkbenchSettings.PersistedTerrainMaterialPlans
            .FirstOrDefault(plan =>
                plan.ProjectKey.Equals(projectKey, StringComparison.OrdinalIgnoreCase) &&
                plan.MapId.Equals(_currentMapWorkbenchDraft.BoundMapId, StringComparison.OrdinalIgnoreCase));
        if (existing != null && TerrainMaterialPlanItemsEqual(existing.Items, items))
        {
            return;
        }

        _mapWorkbenchSettings.PersistedTerrainMaterialPlans.RemoveAll(plan =>
            plan.ProjectKey.Equals(projectKey, StringComparison.OrdinalIgnoreCase) &&
            plan.MapId.Equals(_currentMapWorkbenchDraft.BoundMapId, StringComparison.OrdinalIgnoreCase));
        _mapWorkbenchSettings.PersistedTerrainMaterialPlans.Add(new PersistedTerrainMaterialPlan
        {
            ProjectKey = projectKey,
            MapId = _currentMapWorkbenchDraft.BoundMapId,
            Items = items
        });
        _mapWorkbenchSettings.PersistedTerrainMaterialPlans = _mapWorkbenchSettings.PersistedTerrainMaterialPlans
            .OrderBy(plan => plan.ProjectKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(plan => plan.MapId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        SaveMapWorkbenchSettings();
    }

    private void EnsureCurrentTerrainMaterialPlan(bool persist)
    {
        if (_currentMapWorkbenchDraft == null) return;
        _currentMapWorkbenchDraft.MaterialRoot = _mapWorkbenchSettings.LastMaterialRoot;
        _terrainDrivenMapGenerationService.EnsureMaterialPlan(_currentMapWorkbenchDraft, _currentMaterialAssets);
        if (persist)
        {
            PersistCurrentTerrainMaterialPlan();
        }
    }

    private static TerrainMaterialPlanItem CloneTerrainMaterialPlanItem(TerrainMaterialPlanItem item)
        => new()
        {
            MapId = item.MapId,
            TerrainId = item.TerrainId,
            VisualFamilyKey = item.VisualFamilyKey,
            MaterialRelativePath = item.MaterialRelativePath,
            MaterialCategory = item.MaterialCategory,
            DisplayName = item.DisplayName,
            SelectionMode = item.SelectionMode,
            MaterialRootFingerprint = item.MaterialRootFingerprint
        };

    private static bool TerrainMaterialPlanItemsEqual(IReadOnlyList<TerrainMaterialPlanItem> left, IReadOnlyList<TerrainMaterialPlanItem> right)
    {
        if (left.Count != right.Count) return false;
        var orderedLeft = left.OrderBy(item => item.VisualFamilyKey, StringComparer.OrdinalIgnoreCase).ToList();
        var orderedRight = right.OrderBy(item => item.VisualFamilyKey, StringComparer.OrdinalIgnoreCase).ToList();
        for (var i = 0; i < orderedLeft.Count; i++)
        {
            if (orderedLeft[i].TerrainId != orderedRight[i].TerrainId ||
                !orderedLeft[i].VisualFamilyKey.Equals(orderedRight[i].VisualFamilyKey, StringComparison.OrdinalIgnoreCase) ||
                !orderedLeft[i].MaterialRelativePath.Equals(orderedRight[i].MaterialRelativePath, StringComparison.OrdinalIgnoreCase) ||
                !orderedLeft[i].MaterialCategory.Equals(orderedRight[i].MaterialCategory, StringComparison.OrdinalIgnoreCase) ||
                !orderedLeft[i].DisplayName.Equals(orderedRight[i].DisplayName, StringComparison.OrdinalIgnoreCase) ||
                !orderedLeft[i].SelectionMode.Equals(orderedRight[i].SelectionMode, StringComparison.OrdinalIgnoreCase) ||
                !orderedLeft[i].MaterialRootFingerprint.Equals(orderedRight[i].MaterialRootFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private void CreateNewMapWorkbenchDraftFromInputs()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        EnsureMapWorkbenchMaterialLibraryIndexed(showMessages: false);
        var width = (int)_mapMakerGridWidthInput.Value;
        var height = (int)_mapMakerGridHeightInput.Value;
        _currentMapMakerItem = null;
        _currentMapWorkbenchDraft = _mapDraftService.CreateBlankDraft(width, height, _mapWorkbenchSettings.LastMaterialRoot);
        _currentMapWorkbenchDraft.AutoGenerateMapFromTerrain = true;
        _currentMapWorkbenchDraft.BeautifyGeneratedMap = false;
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
            RefreshDraftBaseLayerFromCurrentMap(_currentMapWorkbenchDraft, _currentMapMakerItem);
            InheritPersistedTerrainMaterialPlan(_currentMapWorkbenchDraft);
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
            EnsureCurrentTerrainMaterialPlan(persist: true);
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
        var oldOriginalTerrain = _currentMapWorkbenchDraft.OriginalTerrainCells.ToArray();
        var newTerrain = new byte[width * height];
        var newOriginalTerrain = new byte[width * height];
        var copyWidth = Math.Min(oldWidth, width);
        var copyHeight = Math.Min(oldHeight, height);
        for (var y = 0; y < copyHeight; y++)
        {
            Array.Copy(oldTerrain, y * oldWidth, newTerrain, y * width, copyWidth);
            if (oldOriginalTerrain.Length >= (y + 1) * oldWidth)
            {
                Array.Copy(oldOriginalTerrain, y * oldWidth, newOriginalTerrain, y * width, copyWidth);
            }
        }

        _currentMapWorkbenchDraft.GridWidth = width;
        _currentMapWorkbenchDraft.GridHeight = height;
        _currentMapWorkbenchDraft.OriginalTerrainCells = newOriginalTerrain;
        _currentMapWorkbenchDraft.TerrainCells = newTerrain;
        _currentMapWorkbenchDraft.MapCellOverrides = RemapWorkbenchCells(_currentMapWorkbenchDraft.MapCellOverrides, oldWidth, width, height, MapCellOverrideSources.ManualOverride);
        _currentMapWorkbenchDraft.TerrainBaseCells = RemapWorkbenchCells(_currentMapWorkbenchDraft.TerrainBaseCells, oldWidth, width, height, MapCellOverrideSources.TerrainBase);
        _currentMapWorkbenchDraft.BuildingOverlayCells = RemapWorkbenchCells(_currentMapWorkbenchDraft.BuildingOverlayCells, oldWidth, width, height, MapCellOverrideSources.BuildingOverlay);
        _currentMapWorkbenchDraft.SceneryOverlayCells = RemapWorkbenchCells(_currentMapWorkbenchDraft.SceneryOverlayCells, oldWidth, width, height, MapCellOverrideSources.SceneryOverlay);

        BindMapWorkbenchDraftToEditor(resetHistory: true);
        _mapViewerInfoBox.Text = BuildMapMakerInfo($"草稿尺寸已调整为 {width}x{height}。");
    }

    private void BindMapWorkbenchDraftToEditor(bool resetHistory)
    {
        if (_currentMapWorkbenchDraft == null) return;
        CancelPendingMapMakerBeautify();
        _currentMapWorkbenchDraft.TileSize = MapResourceItem.MapTilePixelSize;
        _currentMapWorkbenchDraft.AutoGenerateMapFromTerrain = true;
        _currentMapWorkbenchDraft.GenerationMode = IsMapWorkbenchTerrainGenerateMode
            ? MapWorkbenchGenerationModes.TerrainDrivenVisual
            : MapWorkbenchGenerationModes.MaterialDriven;
        var cellCount = _currentMapWorkbenchDraft.GridWidth * _currentMapWorkbenchDraft.GridHeight;
        if (_currentMapWorkbenchDraft.TerrainCells.Length != cellCount)
        {
            var cells = new byte[cellCount];
            Array.Copy(_currentMapWorkbenchDraft.TerrainCells, cells, Math.Min(cells.Length, _currentMapWorkbenchDraft.TerrainCells.Length));
            _currentMapWorkbenchDraft.TerrainCells = cells;
        }

        if (_currentMapWorkbenchDraft.OriginalTerrainCells.Length != cellCount)
        {
            var cells = new byte[cellCount];
            Array.Copy(_currentMapWorkbenchDraft.TerrainCells, cells, Math.Min(cells.Length, _currentMapWorkbenchDraft.TerrainCells.Length));
            _currentMapWorkbenchDraft.OriginalTerrainCells = cells;
        }

        DeriveCurrentMapWorkbenchTerrain();
        _terrainEditorCells = _currentMapWorkbenchDraft.TerrainCells.ToArray();
        _mapMakerOriginalTerrainCells = _currentMapWorkbenchDraft.OriginalTerrainCells.ToArray();
        _terrainEditorOriginalCells = _currentMapWorkbenchDraft.OriginalTerrainCells.ToArray();
        _mapMakerAutoGenerateCheckBox.Checked = true;
        SetNumericSilently(_mapMakerBeautifyStrengthInput, Math.Clamp(_currentMapWorkbenchDraft.BeautifyStrength, 0, 3));
        SetNumericSilently(_mapMakerFeatherRadiusInput, Math.Clamp(_currentMapWorkbenchDraft.FeatherRadius, 0, 24));
        SetSelectedBeautifyFilterProfile(_currentMapWorkbenchDraft.BeautifyFilterProfile);
        SetMapWorkbenchBrushMode(MapWorkbenchBrushMode.TerrainBrush);
        ClearMapMakerCellSelection(invalidate: false);
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
            IndexMapWorkbenchMaterialRoot(_currentMapWorkbenchDraft.MaterialRoot, showMessages: false, populateBrowser: false);
        }

        if (resetHistory)
        {
            ResetMapWorkbenchHistory();
        }

        EnsureCurrentTerrainMaterialPlan(persist: true);
        RenderMapMakerPreview();
        ApplyMapZoom();
        UpdateMapMakerEditingButtons();
    }

    private void SyncMapWorkbenchDraftFromEditor()
    {
        if (_currentMapWorkbenchDraft == null) return;
        _currentMapWorkbenchDraft.MaterialRoot = _mapWorkbenchSettings.LastMaterialRoot;
        _currentMapWorkbenchDraft.AutoGenerateMapFromTerrain = true;
        _currentMapWorkbenchDraft.BeautifyStrength = (int)_mapMakerBeautifyStrengthInput.Value;
        _currentMapWorkbenchDraft.FeatherRadius = (int)_mapMakerFeatherRadiusInput.Value;
        _currentMapWorkbenchDraft.BeautifyFilterProfile = GetSelectedBeautifyFilterProfile();
        if (_currentMapWorkbenchDraft.BeautifyFilterProfile.Equals(TerrainBeautifyFilterProfiles.Custom, StringComparison.OrdinalIgnoreCase) &&
            _currentMapWorkbenchDraft.CustomBeautifyFilter == null)
        {
            _currentMapWorkbenchDraft.CustomBeautifyFilter = _mapWorkbenchSettings.DefaultCustomBeautifyFilter?.Clone()
                ?? BeautifyCustomFilterSettings.CreateDefault();
        }
        EnsureCurrentTerrainMaterialPlan(persist: false);
        if (IsMapWorkbenchTerrainGenerateMode ||
            _currentMapWorkbenchDraft.GenerationMode.Equals(MapWorkbenchGenerationModes.TerrainDrivenVisual, StringComparison.OrdinalIgnoreCase))
        {
            if (_terrainEditorCells.Length == _currentMapWorkbenchDraft.CellCount)
            {
                _currentMapWorkbenchDraft.TerrainCells = _terrainEditorCells.ToArray();
            }
        }
        else if (!_mapMakerPainting)
        {
            RefreshGeneratedMapCells();
        }

        SyncMapWorkbenchOverridesFromLookup();
        if (_currentMapMakerItem != null)
        {
            _currentMapWorkbenchDraft.BoundMapId = GetMapIdForMapResource(_currentMapMakerItem);
        }
    }

    private void MarkCurrentGeneratedMapNeedsBeautify()
    {
        if (_currentMapWorkbenchDraft == null) return;
        _mapMakerBeautifyStale = true;
        CancelPendingMapMakerBeautify();
        _currentMapWorkbenchDraft.BeautifyGeneratedMap = false;

        UpdateMapMakerBeautifyButtonState();
    }

    private void ForceBeautifiedGeneratedMapForOutput()
    {
        if (_currentMapWorkbenchDraft == null) return;
        FlushMapMakerDirtyBasePreview(runBeautify: false);
        CancelPendingMapMakerBeautify();
        _currentMapWorkbenchDraft.AutoGenerateMapFromTerrain = true;
        _currentMapWorkbenchDraft.BeautifyGeneratedMap = true;
        _mapMakerAutoGenerateCheckBox.Checked = true;
        _mapMakerBeautifyStale = false;
        DeriveCurrentMapWorkbenchTerrain();
        UpdateMapMakerBeautifyButtonState();
    }

    private static void SetNumericSilently(NumericUpDown input, int value)
    {
        input.Value = Math.Clamp(value, (int)input.Minimum, (int)input.Maximum);
    }

    private string GetSelectedBeautifyFilterProfile()
        => _mapMakerBeautifyFilterCombo.SelectedItem is BeautifyFilterComboItem item
            ? item.Profile
            : TerrainBeautifyFilterProfiles.Natural;

    private bool TryConfigureCustomBeautifyFilter(bool requireDialog)
    {
        if (_currentMapWorkbenchDraft == null) return false;
        var current = _currentMapWorkbenchDraft.CustomBeautifyFilter?.Clone()
            ?? _mapWorkbenchSettings.DefaultCustomBeautifyFilter?.Clone()
            ?? BeautifyCustomFilterSettings.CreateDefault();
        if (!requireDialog && _currentMapWorkbenchDraft.CustomBeautifyFilter != null)
        {
            return true;
        }

        using var preview = BuildCustomBeautifyPreviewSource();
        using var dialog = new CustomBeautifyFilterDialog(
            current,
            _mapWorkbenchSettings.DefaultCustomBeautifyFilter,
            preview,
            (int)_mapMakerBeautifyStrengthInput.Value);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return false;
        }

        _currentMapWorkbenchDraft.CustomBeautifyFilter = dialog.Settings.Clone();
        _currentMapWorkbenchDraft.BeautifyFilterProfile = TerrainBeautifyFilterProfiles.Custom;
        if (dialog.SaveAsGlobalDefault)
        {
            _mapWorkbenchSettings.DefaultCustomBeautifyFilter = dialog.Settings.Clone();
            SaveMapWorkbenchSettings();
        }

        return true;
    }

    private Bitmap BuildCustomBeautifyPreviewSource()
    {
        if (_currentMapWorkbenchDraft != null)
        {
            using var bitmap = _mapCanvasPreviewRenderer.Rebuild(
                _currentMapWorkbenchDraft,
                _currentMaterialAssets,
                showTerrain: false,
                showGrid: false,
                terrainOpacityPercent: 0,
                beautifyGeneratedMap: false);
            return BuildCustomBeautifyPreviewBitmap(bitmap, 420, 420);
        }

        var fallback = new Bitmap(96, 96);
        using var g = Graphics.FromImage(fallback);
        g.Clear(Color.FromArgb(64, 64, 64));
        return fallback;
    }

    private static Bitmap BuildCustomBeautifyPreviewBitmap(Image image, int maxWidth, int maxHeight)
    {
        var scale = Math.Min(maxWidth / (double)Math.Max(1, image.Width), maxHeight / (double)Math.Max(1, image.Height));
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));
        var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(image, new Rectangle(0, 0, width, height), new Rectangle(0, 0, image.Width, image.Height), GraphicsUnit.Pixel);
        return bitmap;
    }

    private void SetSelectedBeautifyFilterProfile(string profile)
    {
        if (_mapMakerBeautifyFilterCombo.Items.Count == 0) return;
        _updatingMapMakerBeautifyFilterSelection = true;
        try
        {
            for (var i = 0; i < _mapMakerBeautifyFilterCombo.Items.Count; i++)
            {
                if (_mapMakerBeautifyFilterCombo.Items[i] is BeautifyFilterComboItem item &&
                    item.Profile.Equals(profile, StringComparison.OrdinalIgnoreCase))
                {
                    _mapMakerBeautifyFilterCombo.SelectedIndex = i;
                    return;
                }
            }

            _mapMakerBeautifyFilterCombo.SelectedIndex = 0;
        }
        finally
        {
            _updatingMapMakerBeautifyFilterSelection = false;
        }
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

    private bool IsMapWorkbenchTerrainGenerateMode
        => _mapWorkbenchSubPageMode == MapWorkbenchSubPageMode.TerrainGenerate;

    private bool ShouldRenderMapWorkbenchTerrainLayerOnly()
        => IsMapWorkbenchTerrainGenerateMode
            ? _mapMakerTerrainLayerViewRadio.Checked
            : _mapMakerShowTerrainCheckBox.Checked;

    private void HandleMapWorkbenchModeTabChanged()
    {
        var mode = _mapWorkbenchModeTabs.SelectedIndex == 1
            ? MapWorkbenchSubPageMode.TerrainGenerate
            : MapWorkbenchSubPageMode.MaterialPaint;
        SetMapWorkbenchSubPageMode(mode);
    }

    private void SetMapWorkbenchSubPageMode(MapWorkbenchSubPageMode mode)
    {
        _mapWorkbenchSubPageMode = mode;
        MoveMapWorkbenchCanvasToActiveSubPage();

        if (mode == MapWorkbenchSubPageMode.TerrainGenerate)
        {
            PrepareMapWorkbenchTerrainGenerateMode();
        }
        else if (_currentMapWorkbenchDraft != null)
        {
            _currentMapWorkbenchDraft.GenerationMode = MapWorkbenchGenerationModes.MaterialDriven;
        }

        UpdateMapWorkbenchTerrainGenerationInfo();
        UpdateMapMakerEditingButtons();
        RenderMapMakerPreview(force: true);
    }

    private void MoveMapWorkbenchCanvasToActiveSubPage()
    {
        if (_mapWorkbenchCanvasControl == null) return;
        Control? target = IsMapWorkbenchTerrainGenerateMode
            ? _mapWorkbenchTerrainGenerateSplit?.Panel1
            : _mapWorkbenchMaterialPaintSplit?.Panel1;
        if (target == null || ReferenceEquals(_mapWorkbenchCanvasControl.Parent, target)) return;

        _mapWorkbenchCanvasControl.Parent?.Controls.Remove(_mapWorkbenchCanvasControl);
        target.Controls.Add(_mapWorkbenchCanvasControl);
        _mapWorkbenchCanvasControl.Dock = DockStyle.Fill;
        _mapWorkbenchCanvasControl.BringToFront();
    }

    private void PrepareMapWorkbenchTerrainGenerateMode()
    {
        if (_currentMapWorkbenchDraft == null) return;

        _currentMapWorkbenchDraft.GenerationMode = MapWorkbenchGenerationModes.TerrainDrivenVisual;
        _currentMapWorkbenchDraft.AutoGenerateMapFromTerrain = true;
        _currentMapWorkbenchDraft.TerrainVisualProfile ??= new TerrainVisualProfile();
        _currentMapWorkbenchDraft.TerrainVisualProfile.UseCurrentMapSamples = true;
        _currentMapWorkbenchDraft.TerrainVisualProfile.AutoExtractCurrentMapSamples = true;
        _currentMapWorkbenchDraft.TerrainVisualProfile.RedrawChangedCellsOnly = true;

        if (_terrainEditorCells.Length != _currentMapWorkbenchDraft.CellCount)
        {
            TryLoadMapMakerTerrainForSelectedMap(showMessage: false);
        }

        if (_terrainEditorCells.Length == _currentMapWorkbenchDraft.CellCount)
        {
            _currentMapWorkbenchDraft.TerrainCells = _terrainEditorCells.ToArray();
        }

        SetMapWorkbenchBrushMode(MapWorkbenchBrushMode.TerrainBrush);
        if (!_mapMakerTerrainGeneratedViewRadio.Checked)
        {
            _mapMakerTerrainLayerViewRadio.Checked = true;
        }
    }

    private void UpdateMapWorkbenchTerrainGenerationInfo(CurrentMapStyleProfile? styleProfile = null, TerrainVisualSynthesisDiagnostics? diagnostics = null)
    {
        if (_mapMakerTerrainGenerationInfoBox.IsDisposed) return;
        if (styleProfile != null && diagnostics != null)
        {
            _mapMakerTerrainGenerationInfoBox.Text = BuildTerrainStyleDiagnosticsText(styleProfile, diagnostics);
            return;
        }

        if (_currentMapWorkbenchDraft == null)
        {
            _mapMakerTerrainGenerationInfoBox.Text = "Select a map or create a draft, then paint Hexzmap terrain cells here.";
            return;
        }

        var terrainCount = _terrainEditorCells.Length == _currentMapWorkbenchDraft.CellCount
            ? _terrainEditorCells.Distinct().Count()
            : 0;
        var changedCount = _terrainEditorCells.Length == _mapMakerOriginalTerrainCells.Length
            ? _terrainEditorCells.Where((value, index) => value != _mapMakerOriginalTerrainCells[index]).Count()
            : 0;
        var mode = _currentMapWorkbenchDraft.GenerationMode;
        var view = ShouldRenderMapWorkbenchTerrainLayerOnly() ? "terrain layer" : "generated preview";
        _mapMakerTerrainGenerationInfoBox.Text =
            $"Mode: {mode}\r\n" +
            $"View: {view}\r\n" +
            $"Grid: {_currentMapWorkbenchDraft.GridWidth}x{_currentMapWorkbenchDraft.GridHeight}\r\n" +
            $"Terrain ids: {terrainCount}\r\n" +
            $"Changed cells: {changedCount}\r\n" +
            "Paint terrain cells, then click Generate to build a style-aligned visual preview.";
    }

    private void SelectMapWorkbenchBrushMode()
    {
        var mode = _mapMakerBrushModeCombo.SelectedIndex switch
        {
            1 => MapWorkbenchBrushMode.MapBrush,
            2 => MapWorkbenchBrushMode.TerrainBrush,
            3 => MapWorkbenchBrushMode.BuildingBrush,
            _ => MapWorkbenchBrushMode.Browse
        };
        SetMapWorkbenchBrushMode(mode);
    }

    private void SetMapWorkbenchBrushMode(MapWorkbenchBrushMode mode)
    {
        _mapWorkbenchBrushMode = mode;
        if (_mapMakerBrushModeCombo.Visible && _mapMakerBrushModeCombo.Items.Count > 0)
        {
            var index = mode switch
            {
                MapWorkbenchBrushMode.MapBrush => 1,
                MapWorkbenchBrushMode.TerrainBrush => 2,
                MapWorkbenchBrushMode.BuildingBrush => 3,
                _ => 0
            };
            if (index >= 0 && index < _mapMakerBrushModeCombo.Items.Count && _mapMakerBrushModeCombo.SelectedIndex != index)
            {
                _mapMakerBrushModeCombo.SelectedIndex = index;
            }
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
        IndexMapWorkbenchMaterialRoot(dialog.SelectedPath, showMessages: true, populateBrowser: true);
    }

    private void IndexMapWorkbenchMaterialRoot(string root, bool showMessages, bool populateBrowser = true)
    {
        using var perf = TracePerf("IndexMapWorkbenchMaterialRoot");
        if (string.IsNullOrWhiteSpace(root)) return;
        try
        {
            Cursor = Cursors.WaitCursor;
            var fullRoot = Path.GetFullPath(root);
            _currentMaterialAssets = _materialLibraryCache.GetOrIndexExplicitRoot(fullRoot);
            _currentMaterialRoot = fullRoot;
            _mapWorkbenchMaterialBrowserPopulated = false;
            ClearMapWorkbenchMaterialThumbnailCache();
            _mapWorkbenchSettings.LastMaterialRoot = Path.GetFullPath(root);
            if (_currentMapWorkbenchDraft != null)
            {
                _currentMapWorkbenchDraft.MaterialRoot = _mapWorkbenchSettings.LastMaterialRoot;
                RefreshGeneratedMapCells();
            }

            PopulateMapWorkbenchMaterialCategoryFilter();
            ApplyMapWorkbenchMaterialFilter();
            if (populateBrowser)
            {
                PopulateMapWorkbenchMaterialBrowser();
            }
            else
            {
                ClearMapWorkbenchMaterialBrowser();
            }
            _materialGrid.DataSource = new BindingList<MaterialAsset>(_currentMaterialAssets.ToList());
            ConfigureMaterialGrid();
            _mapMakerMaterialInfoBox.Text =
                $"素材根目录：{_mapWorkbenchSettings.LastMaterialRoot}\r\n" +
                $"素材数量：{_currentMaterialAssets.Count}\r\n" +
                "地形分类会按 HexTag 自动生成地图；建筑分类作为上层覆盖；微调覆盖会把选中素材缩放到 48x48 修补当前格。";
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
        if (_mapWorkbenchMaterialBrowserPopulated)
        {
            PopulateMapWorkbenchMaterialBrowser();
        }
    }

    private void HandleMainTabSelectionChanged()
    {
        using var perf = TracePerf("MainTab.SelectedIndexChanged");
        var beforeTab = _lastMainTabText;
        var afterTab = _mainTabs.SelectedTab?.Text ?? string.Empty;
        var beforeLayoutRequests = CompactToolbarLayoutRequestCount;
        var browserPopulatedBefore = _mapWorkbenchMaterialBrowserPopulated;
        var treeNodeCountBefore = _mapMakerMaterialTree.Nodes.Count;
        _lastMainTabText = afterTab;
        void WriteTabSwitchPerfDetail()
        {
            var layoutRequests = CompactToolbarLayoutRequestCount - beforeLayoutRequests;
            Debug.WriteLine(
                $"[PERF] MainTab.SelectedIndexChanged.Detail: from={beforeTab} to={afterTab} compactRequests={layoutRequests} materialLoaded=false browserBefore={browserPopulatedBefore} browserAfter={_mapWorkbenchMaterialBrowserPopulated} treeBefore={treeNodeCountBefore} treeAfter={_mapMakerMaterialTree.Nodes.Count}");
        }

        if (!IsDisposed && IsHandleCreated)
        {
            try
            {
                BeginInvoke((Action)WriteTabSwitchPerfDetail);
                return;
            }
            catch (InvalidOperationException)
            {
                // The form may be closing while a tab event is still unwinding.
            }
        }

        WriteTabSwitchPerfDetail();
    }

    private void HandleMapWorkbenchMaterialSearchChanged()
    {
        EnsureMapWorkbenchMaterialBrowserPopulated();
    }

    private void HandleMapWorkbenchMaterialBrowserInteraction()
    {
        EnsureMapWorkbenchMaterialBrowserPopulated();
    }

    private void EnsureMapWorkbenchMaterialBrowserPopulated()
    {
        if (_mapWorkbenchMaterialBrowserPopulated) return;
        if (_currentMaterialAssets.Count == 0 && !EnsureMapWorkbenchMaterialLibraryIndexed(showMessages: false)) return;
        PopulateMapWorkbenchMaterialBrowser();
    }

    private void ClearMapWorkbenchMaterialBrowser()
    {
        _mapWorkbenchMaterialBrowserPopulated = false;
        _mapMakerMaterialTree.Nodes.Clear();
        PopulateMapWorkbenchMaterialList(Array.Empty<MaterialAsset>());
    }

    private void PopulateMapWorkbenchMaterialBrowser()
    {
        using var perf = TracePerf("PopulateMapWorkbenchMaterialBrowser");
        var previousGroup = (_mapMakerMaterialTree.SelectedNode?.Tag as string) ?? string.Empty;
        var keyword = _mapMakerMaterialSearchBox.Text.Trim();
        var roots = new[]
        {
            (Type: MaterialAssetTypes.Terrain, Text: "地形"),
            (Type: MaterialAssetTypes.Building, Text: "建筑"),
            (Type: MaterialAssetTypes.Scenery, Text: "景物")
        };

        _mapMakerMaterialTree.BeginUpdate();
        try
        {
            _populatingMapWorkbenchMaterialBrowser = true;
            _mapMakerMaterialTree.Nodes.Clear();
            foreach (var root in roots)
            {
                var assets = _currentMaterialAssets
                    .Where(asset => asset.AssetType.Equals(root.Type, StringComparison.OrdinalIgnoreCase))
                    .Where(asset => MaterialMatchesMapWorkbenchKeyword(asset, keyword))
                    .OrderBy(asset => asset.TerrainId ?? byte.MaxValue)
                    .ThenBy(asset => BuildMapWorkbenchMaterialGroupText(asset), StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(asset => asset.VariantIndex)
                    .ToList();
                var rootNode = new TreeNode($"{root.Text} ({assets.Count})") { Tag = root.Type };
                foreach (var group in assets.GroupBy(BuildMapWorkbenchMaterialGroupKey).OrderBy(group => group.First().TerrainId ?? byte.MaxValue).ThenBy(group => BuildMapWorkbenchMaterialGroupText(group.First()), StringComparer.CurrentCultureIgnoreCase))
                {
                    var first = group.First();
                    var groupText = BuildMapWorkbenchMaterialGroupText(first);
                    var node = new TreeNode($"{groupText} ({group.Count()})")
                    {
                        Tag = group.Key,
                        ToolTipText = groupText
                    };
                    rootNode.Nodes.Add(node);
                }

                _mapMakerMaterialTree.Nodes.Add(rootNode);
                rootNode.Expand();
            }

            var selected = FindMapWorkbenchMaterialTreeNode(previousGroup) ?? _mapMakerMaterialTree.Nodes.Cast<TreeNode>().FirstOrDefault(node => node.Nodes.Count > 0)?.Nodes[0];
            if (selected != null)
            {
                _mapMakerMaterialTree.SelectedNode = selected;
            }
            else
            {
                PopulateMapWorkbenchMaterialList(Array.Empty<MaterialAsset>());
            }

            _mapWorkbenchMaterialBrowserPopulated = true;
        }
        finally
        {
            _populatingMapWorkbenchMaterialBrowser = false;
            _mapMakerMaterialTree.EndUpdate();
        }
    }

    private static bool RefreshDraftBaseLayerFromCurrentMap(MapWorkbenchDraft draft, MapResourceItem? mapItem)
    {
        if (mapItem == null || string.IsNullOrWhiteSpace(mapItem.Path) || !File.Exists(mapItem.Path))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(mapItem.Path);
        if (draft.BaseLayerPath.Equals(fullPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        draft.BaseLayerPath = fullPath;
        return true;
    }

    private TreeNode? FindMapWorkbenchMaterialTreeNode(string groupKey)
    {
        if (string.IsNullOrWhiteSpace(groupKey)) return null;
        foreach (TreeNode root in _mapMakerMaterialTree.Nodes)
        {
            foreach (TreeNode child in root.Nodes)
            {
                if ((child.Tag as string)?.Equals(groupKey, StringComparison.OrdinalIgnoreCase) == true)
                {
                    return child;
                }
            }
        }

        return null;
    }

    private void PopulateMapWorkbenchMaterialListForSelection()
    {
        if (_populatingMapWorkbenchMaterialBrowser) return;
        var node = _mapMakerMaterialTree.SelectedNode;
        if (node == null)
        {
            PopulateMapWorkbenchMaterialList(Array.Empty<MaterialAsset>());
            return;
        }

        var key = node.Tag as string ?? string.Empty;
        var assets = node.Parent == null
            ? _currentMaterialAssets.Where(asset => asset.AssetType.Equals(key, StringComparison.OrdinalIgnoreCase))
            : _currentMaterialAssets.Where(asset => BuildMapWorkbenchMaterialGroupKey(asset).Equals(key, StringComparison.OrdinalIgnoreCase));
        var keyword = _mapMakerMaterialSearchBox.Text.Trim();
        PopulateMapWorkbenchMaterialList(SelectMapWorkbenchDisplayMaterials(assets.Where(asset => MaterialMatchesMapWorkbenchKeyword(asset, keyword))).ToList());
    }

    private static IReadOnlyList<MaterialAsset> SelectMapWorkbenchDisplayMaterials(IEnumerable<MaterialAsset> assets)
    {
        return assets
            .GroupBy(BuildMapWorkbenchAutoTileSetKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(asset => asset.AutoTileRole.Equals(MaterialAutoTileRoles.Default, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(asset => asset.AutoTilePriority)
                .ThenBy(asset => asset.VariantIndex)
                .ThenBy(asset => asset.AutoTileRole, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(asset => asset.TerrainId ?? byte.MaxValue)
            .ThenBy(BuildMapWorkbenchMaterialGroupText, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(asset => asset.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void PopulateMapWorkbenchMaterialList(IReadOnlyList<MaterialAsset> assets)
    {
        using var perf = TracePerf($"PopulateMapWorkbenchMaterialList({assets.Count})");
        _mapMakerMaterialImageList.Images.Clear();
        _mapMakerMaterialListView.BeginUpdate();
        try
        {
            _mapMakerMaterialListView.Items.Clear();
            for (var i = 0; i < assets.Count; i++)
            {
                var asset = assets[i];
                _mapMakerMaterialImageList.Images.Add(BuildMapWorkbenchMaterialThumbnail(asset));
                var item = new ListViewItem(BuildMapWorkbenchMaterialListText(asset), i)
                {
                    Tag = asset,
                    ToolTipText = BuildMapWorkbenchMaterialInfoText(asset)
                };
                _mapMakerMaterialListView.Items.Add(item);
            }
        }
        finally
        {
            _mapMakerMaterialListView.EndUpdate();
        }

        if (_mapMakerMaterialListView.Items.Count > 0)
        {
            _mapMakerMaterialListView.Items[0].Selected = true;
        }
        else
        {
            SetMapWorkbenchSelectedMaterial(null);
        }
    }

    private Bitmap BuildMapWorkbenchMaterialThumbnail(MaterialAsset asset)
    {
        var cacheKey = BuildMapWorkbenchMaterialThumbnailCacheKey(asset);
        if (_mapWorkbenchMaterialThumbnailCache.TryGetValue(cacheKey, out var cached))
        {
            return new Bitmap(cached);
        }

        var bitmap = new Bitmap(48, 48);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(245, 245, 245));
        try
        {
            using var image = Image.FromFile(asset.FilePath);
            if (asset.AssetType.Equals(MaterialAssetTypes.Scenery, StringComparison.OrdinalIgnoreCase))
            {
                var target = FitRectangle(image.Width, image.Height, 48, 48);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(image, target, new Rectangle(0, 0, image.Width, image.Height), GraphicsUnit.Pixel);
            }
            else
            {
                var sourceRect = new Rectangle(
                    Math.Clamp(asset.SourceX, 0, Math.Max(0, image.Width - 1)),
                    Math.Clamp(asset.SourceY, 0, Math.Max(0, image.Height - 1)),
                    Math.Clamp(asset.SourceWidth <= 0 ? Math.Min(48, image.Width) : asset.SourceWidth, 1, image.Width),
                    Math.Clamp(asset.SourceHeight <= 0 ? Math.Min(48, image.Height) : asset.SourceHeight, 1, image.Height));
                g.DrawImage(image, new Rectangle(0, 0, 48, 48), sourceRect, GraphicsUnit.Pixel);
            }
        }
        catch
        {
            using var pen = new Pen(Color.DarkGray);
            g.DrawRectangle(pen, 0, 0, 47, 47);
        }

        _mapWorkbenchMaterialThumbnailCache[cacheKey] = new Bitmap(bitmap);
        return bitmap;
    }

    private string BuildMapWorkbenchMaterialThumbnailCacheKey(MaterialAsset asset)
    {
        long ticks = 0;
        try
        {
            if (File.Exists(asset.FilePath))
            {
                ticks = File.GetLastWriteTimeUtc(asset.FilePath).Ticks;
            }
        }
        catch
        {
            ticks = 0;
        }

        return string.Join("|",
            asset.FilePath,
            ticks.ToString(CultureInfo.InvariantCulture),
            asset.SourceX.ToString(CultureInfo.InvariantCulture),
            asset.SourceY.ToString(CultureInfo.InvariantCulture),
            asset.SourceWidth.ToString(CultureInfo.InvariantCulture),
            asset.SourceHeight.ToString(CultureInfo.InvariantCulture),
            asset.AssetType);
    }

    private void ClearMapWorkbenchMaterialThumbnailCache()
    {
        foreach (var image in _mapWorkbenchMaterialThumbnailCache.Values)
        {
            image.Dispose();
        }

        _mapWorkbenchMaterialThumbnailCache.Clear();
    }

    private void SelectMapWorkbenchMaterialFromListView()
    {
        if (_mapMakerMaterialListView.SelectedItems.Count == 0) return;
        SetMapWorkbenchSelectedMaterial(_mapMakerMaterialListView.SelectedItems[0].Tag as MaterialAsset);
    }

    private void SetMapWorkbenchSelectedMaterial(MaterialAsset? asset)
    {
        _mapMakerSelectedMaterial = asset;
        _mapMakerMaterialPreview.Image?.Dispose();
        _mapMakerMaterialPreview.Image = null;
        if (asset == null)
        {
            _mapMakerMaterialInfoBox.Text = "请选择右侧素材后绘制。";
            UpdateMapMakerBrushLabel();
            return;
        }

        try
        {
            using var image = Image.FromFile(asset.FilePath);
            _mapMakerMaterialPreview.Image = asset.AssetType.Equals(MaterialAssetTypes.Scenery, StringComparison.OrdinalIgnoreCase)
                ? BuildZoomedPreviewBitmap(image, 160, 160)
                : new Bitmap(image);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"地图工作台素材预览失败：{asset.FilePath} {ex.Message}");
        }

        _mapMakerMaterialInfoBox.Text = BuildMapWorkbenchMaterialInfoText(asset);
        if (asset.TerrainId.HasValue)
        {
            _mapMakerTerrainBrushInput.Value = asset.TerrainId.Value;
        }

        var targetMode = asset.AssetType switch
        {
            MaterialAssetTypes.Terrain => MapWorkbenchBrushMode.TerrainBrush,
            MaterialAssetTypes.Building => MapWorkbenchBrushMode.BuildingBrush,
            MaterialAssetTypes.Scenery => MapWorkbenchBrushMode.SceneryBrush,
            _ => MapWorkbenchBrushMode.SceneryBrush
        };
        if (_mapWorkbenchBrushMode != targetMode)
        {
            SetMapWorkbenchBrushMode(targetMode);
        }

        UpdateMapMakerBrushLabel();
    }

    private static Rectangle FitRectangle(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || targetWidth <= 0 || targetHeight <= 0)
        {
            return Rectangle.Empty;
        }

        var scale = Math.Min(targetWidth / (double)sourceWidth, targetHeight / (double)sourceHeight);
        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        return new Rectangle((targetWidth - width) / 2, (targetHeight - height) / 2, width, height);
    }

    private static Bitmap BuildZoomedPreviewBitmap(Image image, int maxWidth, int maxHeight)
    {
        var bitmap = new Bitmap(maxWidth, maxHeight);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(245, 245, 245));
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        var target = FitRectangle(image.Width, image.Height, maxWidth, maxHeight);
        if (!target.IsEmpty)
        {
            g.DrawImage(image, target, new Rectangle(0, 0, image.Width, image.Height), GraphicsUnit.Pixel);
        }

        return bitmap;
    }

    private static bool MaterialMatchesMapWorkbenchKeyword(MaterialAsset asset, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        return asset.FileName.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
               asset.Category.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
               asset.TerrainName.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
               asset.Description.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
               asset.AutoTileRole.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
               (asset.TerrainId.HasValue && asset.TerrainId.Value.ToString(CultureInfo.InvariantCulture).Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildMapWorkbenchMaterialGroupKey(MaterialAsset asset)
        => string.IsNullOrWhiteSpace(asset.GroupKey)
            ? $"{asset.AssetType}:{asset.Category}"
            : asset.GroupKey;

    private static string BuildMapWorkbenchAutoTileSetKey(MaterialAsset asset)
        => string.IsNullOrWhiteSpace(asset.AutoTileSetKey)
            ? $"{BuildMapWorkbenchMaterialGroupKey(asset)}:{asset.FileName}"
            : asset.AutoTileSetKey;

    private static string BuildMapWorkbenchMaterialGroupText(MaterialAsset asset)
    {
        if (asset.AssetType.Equals(MaterialAssetTypes.Scenery, StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(asset.Category) ? "景物" : asset.Category;
        }

        return asset.TerrainId.HasValue
            ? $"{asset.TerrainId.Value}：{(string.IsNullOrWhiteSpace(asset.TerrainName) ? asset.Category : asset.TerrainName)}"
            : asset.Category;
    }

    private string BuildMapWorkbenchMaterialListText(MaterialAsset asset)
        => string.IsNullOrWhiteSpace(asset.AutoTileSetKey)
            ? asset.FileName
            : $"{asset.FileName} [{BuildMapWorkbenchAutoTileRoleCountText(asset)}]";

    private string BuildMapWorkbenchMaterialInfoText(MaterialAsset asset)
        => $"素材：{BuildMapWorkbenchMaterialGroupText(asset)} / {asset.FileName}\r\n" +
           $"类型：{asset.AssetType}    地形：{(asset.TerrainId.HasValue ? asset.TerrainId.Value.ToString(CultureInfo.InvariantCulture) : "不改变地形")}\r\n" +
           $"角色：{BuildMapWorkbenchAutoTileRoleInfo(asset)}    源矩形：{asset.SourceX},{asset.SourceY},{asset.SourceWidth}x{asset.SourceHeight}\r\n" +
           $"尺寸：{asset.Width}x{asset.Height}\r\n" +
           $"路径：{asset.FilePath}";

    private string BuildMapWorkbenchAutoTileRoleCountText(MaterialAsset asset)
    {
        var masks = GetMapWorkbenchAutoTileMasks(asset);
        return masks.Count > 1 ? $"{masks.Count}帧" : "单帧";
    }

    private string BuildMapWorkbenchAutoTileRoleInfo(MaterialAsset asset)
    {
        var masks = GetMapWorkbenchAutoTileMasks(asset);
        if (masks.Count <= 1)
        {
            return masks.Count == 0 ? "默认" : FormatAutoTileMask(masks[0]);
        }

        return "自动拼接：" + string.Join(", ", masks.Select(FormatAutoTileMask));
    }

    private IReadOnlyList<int> GetMapWorkbenchAutoTileMasks(MaterialAsset asset)
    {
        var key = BuildMapWorkbenchAutoTileSetKey(asset);
        return _currentMaterialAssets
            .Where(candidate => BuildMapWorkbenchAutoTileSetKey(candidate).Equals(key, StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate.AutoTileMask ?? MaterialLibraryIndexer.RoleToMask(candidate.AutoTileRole))
            .Distinct()
            .OrderBy(GetAutoTileMaskSortPriority)
            .ToList();
    }

    private static int GetAutoTileMaskSortPriority(int mask)
        => mask switch
        {
            0 => 0,
            10 => 10,
            5 => 11,
            1 or 2 or 4 or 8 => 20,
            3 or 6 or 9 or 12 => 30,
            7 or 11 or 13 or 14 => 40,
            15 => 50,
            MaterialAutoTileMasks.InnerCornerNE or MaterialAutoTileMasks.InnerCornerSE or MaterialAutoTileMasks.InnerCornerSW or MaterialAutoTileMasks.InnerCornerNW => 60,
            _ => 100 + mask
        };

    private static string FormatAutoTileMask(int mask)
        => mask switch
        {
            0 => "默认",
            10 => "横",
            5 => "竖",
            1 => "端N",
            2 => "端E",
            4 => "端S",
            8 => "端W",
            3 => "角NE",
            9 => "角NW",
            6 => "角SE",
            12 => "角SW",
            14 => "T缺N",
            13 => "T缺E",
            11 => "T缺S",
            7 => "T缺W",
            15 => "十字",
            MaterialAutoTileMasks.InnerCornerNE => "Inner NE",
            MaterialAutoTileMasks.InnerCornerSE => "Inner SE",
            MaterialAutoTileMasks.InnerCornerSW => "Inner SW",
            MaterialAutoTileMasks.InnerCornerNW => "Inner NW",
            _ => $"Mask {mask}"
        };

    private void SelectMapWorkbenchMaterial()
    {
        if (_mapMakerMaterialGrid.SelectedRows.Count == 0) return;
        if (_mapMakerMaterialGrid.SelectedRows[0].DataBoundItem is not MaterialAsset asset) return;
        SetMapWorkbenchSelectedMaterial(asset);
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
        var stopwatch = Stopwatch.StartNew();
        _mapViewerRenderedImage = _mapCanvasPreviewRenderer.GetCurrentPreviewImage(
            _currentMapWorkbenchDraft,
            _currentMaterialAssets,
            terrainLayerOnly: ShouldRenderMapWorkbenchTerrainLayerOnly(),
            showGrid: _mapMakerShowGridCheckBox.Checked,
            terrainOpacityPercent: 0,
            showBeautifiedMap: _currentMapWorkbenchDraft.BeautifyGeneratedMap);
        stopwatch.Stop();
        if (force)
        {
            _mapMakerLastBaseRefreshMs = stopwatch.ElapsedMilliseconds;
        }
        _mapViewerBox.Image = _mapViewerRenderedImage;
        _mapMakerExportPreviewButton.Enabled = true;
        UpdateMapMakerEditingButtons();
    }

    private async Task BeautifyCurrentGeneratedMapAsync()
    {
        if (_currentMapWorkbenchDraft == null)
        {
            MessageBox.Show(this, "Please create or load a map draft first.", "Notice", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        System.Threading.CancellationTokenSource cts;
        int requestId;
        try
        {
            _mapMakerDirtyBaseRefreshTimer.Stop();
            EnsureMapWorkbenchMaterialLibraryIndexed(showMessages: false);
            SyncMapWorkbenchDraftFromEditor();
            if (_currentMapWorkbenchDraft.BeautifyFilterProfile.Equals(TerrainBeautifyFilterProfiles.Custom, StringComparison.OrdinalIgnoreCase) &&
                !TryConfigureCustomBeautifyFilter(requireDialog: _currentMapWorkbenchDraft.CustomBeautifyFilter == null))
            {
                _currentMapWorkbenchDraft.BeautifyGeneratedMap = false;
                UpdateMapMakerBeautifyButtonState();
                return;
            }

            FlushMapMakerDirtyBasePreview(runBeautify: false);
            _currentMapWorkbenchDraft.AutoGenerateMapFromTerrain = true;
            _currentMapWorkbenchDraft.BeautifyGeneratedMap = true;
            _mapMakerAutoGenerateCheckBox.Checked = true;
            if (_mapMakerShowTerrainCheckBox.Checked)
            {
                _mapMakerShowTerrainCheckBox.Checked = false;
            }

            _mapMakerBeautifyCts?.Cancel();
            cts = new System.Threading.CancellationTokenSource();
            _mapMakerBeautifyCts = cts;
            requestId = ++_mapMakerBeautifyRequestId;
            _mapMakerBeautifyRunning = true;
            _mapMakerBeautifyStale = false;
            UpdateMapMakerBeautifyButtonState();
            RenderMapMakerPreview(force: true);
            _mapViewerInfoBox.Text = BuildMapMakerInfo("Beautify is running.");
            SetStatus("Beautify is running...");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Beautify map failed: " + ex);
            MessageBox.Show(this, ex.Message, "Beautify failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            var draft = CloneMapWorkbenchDraftForBackground(_currentMapWorkbenchDraft);
            var materials = _currentMaterialAssets.ToList();
            var stopwatch = Stopwatch.StartNew();
            var result = await Task.Run(() =>
            {
                cts.Token.ThrowIfCancellationRequested();
                using var renderer = new MaterialDrivenTerrainService();
                draft.TerrainCells = renderer.DeriveTerrainCells(draft, materials);
                cts.Token.ThrowIfCancellationRequested();
                return renderer.ComposeVisualMap(draft, materials, checkerboardBlank: true, beautifyTerrain: true);
            }, cts.Token);
            stopwatch.Stop();

            if (cts.IsCancellationRequested || requestId != _mapMakerBeautifyRequestId)
            {
                result.Dispose();
                return;
            }

            _mapMakerLastBeautifyMs = stopwatch.ElapsedMilliseconds;
            _mapCanvasPreviewRenderer.SetBeautifiedMapCache(_currentMapWorkbenchDraft!, result);
            result.Dispose();
            _currentMapWorkbenchDraft!.BeautifyGeneratedMap = true;
            _mapMakerBeautifyStale = false;
            RenderMapMakerPreview(force: true);
            _mapViewerInfoBox.Text = BuildMapMakerInfo("美化预览已生成。");
            SetStatus("地图美化完成");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Beautify map failed: " + ex);
            if (!IsDisposed)
            {
                MessageBox.Show(this, ex.Message, "Beautify failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            if (ReferenceEquals(_mapMakerBeautifyCts, cts))
            {
                _mapMakerBeautifyCts = null;
            }

            if (requestId == _mapMakerBeautifyRequestId)
            {
                _mapMakerBeautifyRunning = false;
                UpdateMapMakerBeautifyButtonState();
            }

            cts.Dispose();
        }
    }

    private void RollbackCurrentMapBeautify()
    {
        if (_currentMapWorkbenchDraft == null) return;
        CancelPendingMapMakerBeautify();
        _currentMapWorkbenchDraft.BeautifyGeneratedMap = false;
        _mapMakerBeautifyStale = false;
        RenderMapMakerPreview(force: true);
        UpdateMapMakerBeautifyButtonState();
        _mapViewerInfoBox.Text = BuildMapMakerInfo("已回退到未美化的素材绘制预览。");
        SetStatus("已回退美化预览");
    }

    private static List<MapCellOverride> RemapWorkbenchCells(
        IEnumerable<MapCellOverride> cells,
        int oldWidth,
        int newWidth,
        int newHeight,
        string source)
        => cells
            .Where(cell =>
            {
                if (oldWidth <= 0 || cell.Index < 0) return false;
                var x = cell.Index % oldWidth;
                var y = cell.Index / oldWidth;
                return x < newWidth && y < newHeight;
            })
            .Select(cell =>
            {
                var x = cell.Index % oldWidth;
                var y = cell.Index / oldWidth;
                return new MapCellOverride
                {
                    Index = y * newWidth + x,
                    MaterialRelativePath = cell.MaterialRelativePath,
                    MaterialCategory = cell.MaterialCategory,
                    DisplayName = cell.DisplayName,
                    Source = string.IsNullOrWhiteSpace(cell.Source) ? source : cell.Source
                };
            })
            .OrderBy(cell => cell.Index)
            .ToList();

    private void BeautifyCurrentGeneratedMap()
        => _ = BeautifyCurrentGeneratedMapAsync();

    private void ClearMapMakerPreviewImages()
    {
        CancelPendingMapMakerBeautify();
        _mapViewerBox.Image = null;
        _mapCanvasPreviewRenderer.Clear();
        _mapViewerRenderedImage = null;
        _mapMakerDirtyTerrainPreviewIndexes.Clear();
        _mapMakerDirtyBaseRefreshTimer.Stop();
    }

    private void CancelPendingMapMakerBeautify()
    {
        _mapMakerBeautifyRequestId++;
        _mapMakerBeautifyCts?.Cancel();
        _mapMakerBeautifyRunning = false;
    }

    private void ScheduleMapMakerDirtyBasePreviewRefresh()
    {
        _mapMakerDirtyBaseRefreshTimer.Stop();
        _mapMakerDirtyBaseRefreshTimer.Start();
    }

    private void FlushMapMakerDirtyBasePreview(bool runBeautify)
    {
        _mapMakerDirtyBaseRefreshTimer.Stop();
        if (_currentMapWorkbenchDraft == null) return;
        if (_mapMakerDirtyTerrainPreviewIndexes.Count == 0) return;
        if (_mapMakerPainting)
        {
            ScheduleMapMakerDirtyBasePreviewRefresh();
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var dirtyRect = _mapCanvasPreviewRenderer.RefreshDirtyBaseMap(_currentMapWorkbenchDraft, _currentMaterialAssets);
        stopwatch.Stop();
        _mapMakerLastBaseRefreshMs = stopwatch.ElapsedMilliseconds;
        _mapMakerDirtyTerrainPreviewIndexes.Clear();
        RefreshMapMakerPreviewTile(dirtyRect);
        UpdateMapMakerBeautifyButtonState();
        if (runBeautify && _currentMapWorkbenchDraft.BeautifyGeneratedMap && !_mapMakerShowTerrainCheckBox.Checked)
        {
            _ = BeautifyCurrentGeneratedMapAsync();
        }
    }

    private void UpdateMapMakerBeautifyButtonState()
    {
        if (_currentMapWorkbenchDraft == null)
        {
            _mapMakerBeautifyCheckBox.Text = "美化当前地图";
            _mapMakerRollbackBeautifyButton.Enabled = false;
            return;
        }

        if (_mapMakerBeautifyRunning)
        {
            _mapMakerBeautifyCheckBox.Text = "美化中";
        }
        else if (_mapMakerBeautifyStale)
        {
            _mapMakerBeautifyCheckBox.Text = "重新美化";
        }
        else if (_currentMapWorkbenchDraft.BeautifyGeneratedMap)
        {
            _mapMakerBeautifyCheckBox.Text = "已美化";
        }
        else
        {
            _mapMakerBeautifyCheckBox.Text = "美化当前地图";
        }

        _mapMakerRollbackBeautifyButton.Enabled = _currentMapWorkbenchDraft.BeautifyGeneratedMap || _mapMakerBeautifyRunning;
    }

    private static MapWorkbenchDraft CloneMapWorkbenchDraftForBackground(MapWorkbenchDraft source)
        => new()
        {
            DraftId = source.DraftId,
            BoundMapId = source.BoundMapId,
            GridWidth = source.GridWidth,
            GridHeight = source.GridHeight,
            TileSize = source.TileSize,
            BaseLayerPath = source.BaseLayerPath,
            MaterialRoot = source.MaterialRoot,
            TerrainMaterialPlan = source.TerrainMaterialPlan.Select(CloneTerrainMaterialPlanItem).ToList(),
            MapCellOverrides = source.MapCellOverrides.Select(CloneMapCellOverrideForBackground).ToList(),
            TerrainBaseCells = source.TerrainBaseCells.Select(CloneMapCellOverrideForBackground).ToList(),
            GeneratedMapCells = source.GeneratedMapCells.Select(CloneMapCellOverrideForBackground).ToList(),
            BuildingOverlayCells = source.BuildingOverlayCells.Select(CloneMapCellOverrideForBackground).ToList(),
            SceneryOverlayCells = source.SceneryOverlayCells.Select(CloneMapCellOverrideForBackground).ToList(),
            SceneryOverlays = source.SceneryOverlays.Select(CloneMapSceneryOverlayForBackground).ToList(),
            OriginalTerrainCells = source.OriginalTerrainCells.ToArray(),
            TerrainCells = source.TerrainCells.ToArray(),
            GenerationMode = source.GenerationMode,
            TerrainVisualProfile = source.TerrainVisualProfile.Clone(),
            AutoGenerateMapFromTerrain = source.AutoGenerateMapFromTerrain,
            BeautifyGeneratedMap = true,
            BeautifyStrength = source.BeautifyStrength,
            FeatherRadius = source.FeatherRadius,
            BeautifyFilterProfile = source.BeautifyFilterProfile,
            CustomBeautifyFilter = source.CustomBeautifyFilter?.Clone(),
            CreatedAtText = source.CreatedAtText,
            UpdatedAtText = source.UpdatedAtText
        };

    private static MapCellOverride CloneMapCellOverrideForBackground(MapCellOverride value)
        => new()
        {
            Index = value.Index,
            MaterialRelativePath = value.MaterialRelativePath,
            MaterialCategory = value.MaterialCategory,
            DisplayName = value.DisplayName,
            Source = value.Source
        };

    private static MapSceneryOverlay CloneMapSceneryOverlayForBackground(MapSceneryOverlay value)
        => new()
        {
            MaterialRelativePath = value.MaterialRelativePath,
            MaterialCategory = value.MaterialCategory,
            DisplayName = value.DisplayName,
            X = value.X,
            Y = value.Y,
            Width = value.Width,
            Height = value.Height,
            RotationDegrees = value.RotationDegrees,
            ZOrder = value.ZOrder
        };

    private static void DrawMapWorkbenchCellForBackground(
        Graphics graphics,
        MapWorkbenchDraft draft,
        MapCellOverride cell,
        IReadOnlyList<MaterialAsset> materials)
    {
        if (cell.Index < 0 || cell.Index >= draft.CellCount) return;
        var materialPath = MapDraftService.ResolveMaterialPath(draft.MaterialRoot, cell.MaterialRelativePath);
        if (!File.Exists(materialPath))
        {
            var material = materials.FirstOrDefault(asset =>
                MapDraftService.GetMaterialRelativePath(draft.MaterialRoot, asset.FilePath).Equals(cell.MaterialRelativePath, StringComparison.OrdinalIgnoreCase));
            materialPath = material?.FilePath ?? materialPath;
        }

        if (!File.Exists(materialPath)) return;
        using var image = Image.FromFile(materialPath);
        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var x = cell.Index % draft.GridWidth;
        var y = cell.Index / draft.GridWidth;
        graphics.DrawImage(image, new Rectangle(x * tileSize, y * tileSize, tileSize, tileSize));
    }

    private void RebuildMapMakerOverrideLookup()
    {
        _mapMakerMapCellOverrideLookup.Clear();
        if (_currentMapWorkbenchDraft == null) return;
        foreach (var cell in _currentMapWorkbenchDraft.MapCellOverrides)
        {
            if (cell.Index < 0 || cell.Index >= _currentMapWorkbenchDraft.CellCount) continue;
            if (string.IsNullOrWhiteSpace(cell.MaterialRelativePath)) continue;
            if (cell.Source.Equals(MapCellOverrideSources.Generated, StringComparison.OrdinalIgnoreCase) ||
                cell.Source.Equals(MapCellOverrideSources.BuildingOverlay, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
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

    private void RefreshGeneratedMapCells()
    {
        if (_currentMapWorkbenchDraft == null) return;
        DeriveCurrentMapWorkbenchTerrain();
    }

    private void DeriveCurrentMapWorkbenchTerrain()
    {
        if (_currentMapWorkbenchDraft == null) return;
        _currentMapWorkbenchDraft.TerrainCells = _materialDrivenTerrainService.DeriveTerrainCells(_currentMapWorkbenchDraft, _currentMaterialAssets);
        _terrainEditorCells = _currentMapWorkbenchDraft.TerrainCells.ToArray();
    }

    private string GetMapWorkbenchFinalTerrainText(int index)
        => _terrainEditorCells.Length > index && index >= 0
            ? FormatTerrainValue(_terrainEditorCells[index])
            : "未知";

    private void MarkMapWorkbenchMaterialDirty(int index)
    {
        if (_currentMapWorkbenchDraft == null) return;
        var dirtyIndexes = ExpandIndexesWithNeighbors(new[] { index }).ToList();
        foreach (var dirtyIndex in dirtyIndexes)
        {
            _mapMakerDirtyTerrainPreviewIndexes.Add(dirtyIndex);
        }

        DeriveCurrentMapWorkbenchTerrain();
        var dirtyRect = _mapCanvasPreviewRenderer.MarkTerrainDirty(_currentMapWorkbenchDraft, dirtyIndexes);
        if (_mapMakerPainting && !_mapMakerShowTerrainCheckBox.Checked)
        {
            _mapMakerRenderDeferred = true;
            ScheduleMapMakerDirtyBasePreviewRefresh();
        }
        else
        {
            RefreshMapMakerPreviewTile(dirtyRect);
            ScheduleMapMakerDirtyBasePreviewRefresh();
        }
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
        if (e.Button == MouseButtons.Right)
        {
            if (IsMapWorkbenchTerrainGenerateMode)
            {
                UpdateMapMakerCellInfo(e.Location);
                return;
            }

            SelectMapMakerExtractionCell(e.Location);
            return;
        }

        if (e.Button == MouseButtons.Left && ModifierKeys.HasFlag(Keys.Shift))
        {
            if (IsMapWorkbenchTerrainGenerateMode)
            {
                UpdateMapMakerCellInfo(e.Location);
                return;
            }

            BeginMapMakerCellSelection(e.Location);
            return;
        }

        if (e.Button != MouseButtons.Left) return;
        if (_currentMapWorkbenchDraft == null)
        {
            CreateNewMapWorkbenchDraftFromInputs();
            if (_currentMapWorkbenchDraft == null) return;
        }

        _mapViewerBox.Focus();
        if (IsMapWorkbenchTerrainGenerateMode)
        {
            PrepareMapWorkbenchTerrainGenerateMode();
            if (!_mapMakerTerrainLayerViewRadio.Checked)
            {
                _mapMakerTerrainLayerViewRadio.Checked = true;
            }
        }

        if (_mapWorkbenchBrushMode == MapWorkbenchBrushMode.SceneryBrush &&
            TryBeginMapWorkbenchSceneryObjectEdit(e))
        {
            return;
        }

        _mapMakerPainting = true;
        _mapMakerPendingMapPaintChanges.Clear();
        _mapMakerPendingMapPaintIndexes.Clear();
        _mapMakerPendingTerrainPaintChanges.Clear();
        _mapMakerPendingTerrainPaintIndexes.Clear();
        PaintMapWorkbenchCell(e.Location, groupWithCurrentStroke: true, erase: e.Button == MouseButtons.Right);
    }

    private void ContinueMapMakerTerrainPaint(MouseEventArgs e)
    {
        if (_mapMakerSelectingCells)
        {
            ContinueMapMakerCellSelection(e.Location);
            return;
        }

        if (_sceneryOverlayDragging)
        {
            ContinueMapWorkbenchSceneryObjectEdit(e);
            return;
        }

        if (_mapMakerPainting && e.Button is MouseButtons.Left or MouseButtons.Right)
        {
            PaintMapWorkbenchCell(e.Location, groupWithCurrentStroke: true, erase: e.Button == MouseButtons.Right);
            return;
        }

        if (_mapWorkbenchBrushMode == MapWorkbenchBrushMode.SceneryBrush)
        {
            UpdateMapWorkbenchSceneryCursor(e.Location);
        }

        UpdateMapMakerCellInfo(e.Location);
    }

    private void EndMapMakerTerrainPaint()
    {
        if (_mapMakerSelectingCells)
        {
            EndMapMakerCellSelection();
            return;
        }

        if (_sceneryOverlayDragging)
        {
            EndMapWorkbenchSceneryObjectEdit();
            return;
        }

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
            RefreshGeneratedMapCells();
            SyncMapWorkbenchOverridesFromLookup();
            FlushMapMakerDirtyBasePreview(runBeautify: false);
        }

        if (_mapMakerRenderDeferred)
        {
            if (_mapMakerDirtyTerrainPreviewIndexes.Count == 0)
            {
                RenderMapMakerPreview(force: true);
            }
        }
        else if (hadChanges)
        {
            _mapViewerInfoBox.Text = BuildMapMakerInfo("地形绘制完成。");
        }
        UpdateMapMakerEditingButtons();
    }

    private void ConfigureMapViewerContextMenu()
    {
        if (_mapViewerContextMenu.Items.Count > 0) return;
        var extractItem = new ToolStripMenuItem("提取当前格为素材");
        extractItem.Click += (_, _) => OpenMapMaterialExtractionDialogFromSelection();
        _mapViewerContextMenu.Items.Add(extractItem);
        _mapViewerContextMenu.Opening += (_, e) =>
        {
            var hasSelection = _currentMapWorkbenchDraft != null && !_mapMakerSelectedCellRange.IsEmpty;
            extractItem.Enabled = hasSelection;
            e.Cancel = !hasSelection;
        };
        _mapViewerBox.ContextMenuStrip = _mapViewerContextMenu;
    }

    private void SelectMapMakerExtractionCell(Point location)
    {
        if (!TryGetMapMakerCell(location, out var x, out var y)) return;
        _mapViewerContextMenuCell = new Point(x, y);
        SetMapMakerSelectedCellRange(new Rectangle(x, y, 1, 1));
        UpdateMapMakerCellPreview(x, y, GetMapWorkbenchFinalTerrainText(y * _currentMapWorkbenchDraft!.GridWidth + x));
    }

    private void BeginMapMakerCellSelection(Point location)
    {
        if (!TryGetMapMakerCell(location, out var x, out var y)) return;
        _mapMakerSelectingCells = true;
        _mapMakerSelectionStartCell = new Point(x, y);
        _mapMakerSelectionEndCell = new Point(x, y);
        SetMapMakerSelectedCellRange(BuildMapMakerCellRange(_mapMakerSelectionStartCell.Value, _mapMakerSelectionEndCell.Value));
        _mapViewerBox.Capture = true;
    }

    private void ContinueMapMakerCellSelection(Point location)
    {
        if (_mapMakerSelectionStartCell == null) return;
        if (!TryGetMapMakerCell(location, out var x, out var y)) return;
        var nextEnd = new Point(x, y);
        if (_mapMakerSelectionEndCell == nextEnd) return;
        _mapMakerSelectionEndCell = nextEnd;
        SetMapMakerSelectedCellRange(BuildMapMakerCellRange(_mapMakerSelectionStartCell.Value, nextEnd));
        UpdateMapMakerCellPreview(x, y, GetMapWorkbenchFinalTerrainText(y * _currentMapWorkbenchDraft!.GridWidth + x));
    }

    private void EndMapMakerCellSelection()
    {
        _mapMakerSelectingCells = false;
        _mapViewerBox.Capture = false;
        UpdateMapMakerEditingButtons();
    }

    private void ClearMapMakerCellSelection(bool invalidate)
    {
        _mapMakerSelectionStartCell = null;
        _mapMakerSelectionEndCell = null;
        _mapMakerSelectedCellRange = Rectangle.Empty;
        _mapMakerSelectingCells = false;
        _mapViewerContextMenuCell = new Point(-1, -1);
        if (invalidate)
        {
            _mapViewerBox.Invalidate();
        }
    }

    private bool TryGetMapMakerCell(Point location, out int x, out int y)
    {
        x = 0;
        y = 0;
        return _currentMapWorkbenchDraft != null &&
               _mapViewerBox.Image != null &&
               TryMapPictureBoxPointToTerrainCell(_mapViewerBox, location, _currentMapWorkbenchDraft.GridWidth, _currentMapWorkbenchDraft.GridHeight, out x, out y);
    }

    private void SetMapMakerSelectedCellRange(Rectangle range)
    {
        if (_mapMakerSelectedCellRange == range) return;
        var oldDisplay = GetMapMakerSelectionDisplayRect(_mapMakerSelectedCellRange);
        _mapMakerSelectedCellRange = range;
        var newDisplay = GetMapMakerSelectionDisplayRect(_mapMakerSelectedCellRange);
        if (!oldDisplay.IsEmpty) _mapViewerBox.Invalidate(oldDisplay);
        if (!newDisplay.IsEmpty) _mapViewerBox.Invalidate(newDisplay);
        if (!range.IsEmpty)
        {
            var text = range.Width == 1 && range.Height == 1
                ? $"已选中格子 ({range.X},{range.Y})"
                : $"已选中区域 ({range.X},{range.Y}) - ({range.Right - 1},{range.Bottom - 1})，共 {range.Width * range.Height} 格";
            _mapViewerInfoBox.Text = BuildMapMakerInfo(text, range.X, range.Y);
        }
    }

    private Rectangle GetMapMakerSelectionDisplayRect(Rectangle cellRange)
    {
        if (cellRange.IsEmpty || _currentMapWorkbenchDraft == null) return Rectangle.Empty;
        var tileSize = _currentMapWorkbenchDraft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : _currentMapWorkbenchDraft.TileSize;
        var source = new Rectangle(cellRange.X * tileSize, cellRange.Y * tileSize, cellRange.Width * tileSize, cellRange.Height * tileSize);
        return MapMakerSourceRectToDisplayRect(source);
    }

    private static Rectangle BuildMapMakerCellRange(Point start, Point end)
    {
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var right = Math.Max(start.X, end.X);
        var bottom = Math.Max(start.Y, end.Y);
        return new Rectangle(left, top, right - left + 1, bottom - top + 1);
    }

    private void OpenMapMaterialExtractionDialogFromSelection()
    {
        if (_currentMapWorkbenchDraft == null)
        {
            MessageBox.Show(this, "请先选择或新建地图草稿。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_mapMakerSelectedCellRange.IsEmpty)
        {
            MessageBox.Show(this, "请先右键选择一个格子，或按住 Shift 拖拽选择矩形区域。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!EnsureMapWorkbenchMaterialLibraryIndexed(showMessages: true))
        {
            return;
        }

        SyncMapWorkbenchDraftFromEditor();
        EnsureCurrentTerrainMaterialPlan(persist: false);

        var selection = _mapMakerSelectedCellRange;
        var defaultTerrainId = GetDefaultMapMaterialExtractionTerrainId(selection, out var mixedTerrain);
        var defaultTarget = GetDefaultMapMaterialExtractionTargetType(defaultTerrainId);
        MapMaterialExtractionResult? extractionResult = null;
        MapMaterialExtractionTargetType extractedTarget = defaultTarget;
        byte? extractedTerrainId = defaultTerrainId;

        using var dialog = new Form
        {
            Text = "提取地图素材",
            StartPosition = FormStartPosition.CenterParent,
            Width = 560,
            Height = 430,
            MinimizeBox = false,
            MaximizeBox = false,
            FormBorderStyle = FormBorderStyle.FixedDialog
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        dialog.Controls.Add(layout);

        var selectionLabel = new Label
        {
            AutoSize = true,
            Text = selection.Width == 1 && selection.Height == 1
                ? $"选区：格子 ({selection.X},{selection.Y})"
                : $"选区：({selection.X},{selection.Y}) - ({selection.Right - 1},{selection.Bottom - 1})，共 {selection.Width * selection.Height} 格"
        };
        layout.Controls.Add(selectionLabel, 0, 0);
        layout.SetColumnSpan(selectionLabel, 2);

        var warningLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.DarkOrange,
            Text = mixedTerrain ? "选区包含多种地形，请确认目标地形编号。" : string.Empty
        };
        layout.Controls.Add(warningLabel, 0, 1);
        layout.SetColumnSpan(warningLabel, 2);

        var targetTypeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 180
        };
        targetTypeCombo.Items.AddRange(new object[]
        {
            new MapMaterialExtractionTargetComboItem(MapMaterialExtractionTargetType.Terrain, "地形"),
            new MapMaterialExtractionTargetComboItem(MapMaterialExtractionTargetType.Building, "建筑"),
            new MapMaterialExtractionTargetComboItem(MapMaterialExtractionTargetType.Scenery, "景物")
        });
        for (var i = 0; i < targetTypeCombo.Items.Count; i++)
        {
            if (targetTypeCombo.Items[i] is MapMaterialExtractionTargetComboItem item && item.TargetType == defaultTarget)
            {
                targetTypeCombo.SelectedIndex = i;
                break;
            }
        }
        if (targetTypeCombo.SelectedIndex < 0) targetTypeCombo.SelectedIndex = 0;

        var terrainInput = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 255,
            Value = defaultTerrainId,
            Width = 90
        };

        layout.Controls.Add(new Label { Text = "目标类型：", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 2);
        layout.Controls.Add(targetTypeCombo, 1, 2);
        layout.Controls.Add(new Label { Text = "地形编号：", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 3);
        layout.Controls.Add(terrainInput, 1, 3);

        var previewBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true
        };
        layout.Controls.Add(previewBox, 0, 4);
        layout.SetColumnSpan(previewBox, 2);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true
        };
        var extractButton = new Button { Text = "提取", AutoSize = true };
        var cancelButton = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(extractButton);
        layout.Controls.Add(buttonPanel, 0, 6);
        layout.SetColumnSpan(buttonPanel, 2);
        dialog.CancelButton = cancelButton;

        MapMaterialExtractionTargetType SelectedTarget()
            => targetTypeCombo.SelectedItem is MapMaterialExtractionTargetComboItem item
                ? item.TargetType
                : MapMaterialExtractionTargetType.Terrain;

        MapMaterialExtractionRequest BuildRequest()
        {
            var targetType = SelectedTarget();
            return new MapMaterialExtractionRequest
            {
                Draft = _currentMapWorkbenchDraft,
                MaterialRoot = _mapWorkbenchSettings.LastMaterialRoot,
                CellRange = selection,
                TargetType = targetType,
                TerrainId = targetType == MapMaterialExtractionTargetType.Scenery ? null : (byte)terrainInput.Value,
                Source = MapMaterialExtractionSource.CurrentComposite,
                Materials = _currentMaterialAssets
            };
        }

        void RefreshPreview()
        {
            try
            {
                var targetType = SelectedTarget();
                terrainInput.Enabled = targetType != MapMaterialExtractionTargetType.Scenery;
                var preview = _mapMaterialExtractionService.Preview(BuildRequest());
                previewBox.Text =
                    $"素材库：{_mapWorkbenchSettings.LastMaterialRoot}\r\n" +
                    $"目标目录：{preview.TargetDirectory}\r\n" +
                    $"文件数量：{preview.FileCount}\r\n" +
                    $"序号范围：{preview.StartSequence} - {preview.EndSequence}\r\n" +
                    $"首个文件：{(preview.PlannedPaths.Count == 0 ? string.Empty : Path.GetFileName(preview.PlannedPaths[0]))}\r\n" +
                    $"提取来源：当前合成图（不含网格 / 地形叠色 / 选框）";
                extractButton.Enabled = true;
            }
            catch (Exception ex)
            {
                previewBox.Text = ex.Message;
                extractButton.Enabled = false;
            }
        }

        targetTypeCombo.SelectedIndexChanged += (_, _) => RefreshPreview();
        terrainInput.ValueChanged += (_, _) => RefreshPreview();
        extractButton.Click += (_, _) =>
        {
            try
            {
                var request = BuildRequest();
                extractedTarget = request.TargetType;
                extractedTerrainId = request.TerrainId;
                extractionResult = _mapMaterialExtractionService.Extract(request);
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(dialog, ex.Message, "提取素材失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                RefreshPreview();
            }
        };

        RefreshPreview();
        if (dialog.ShowDialog(this) != DialogResult.OK || extractionResult == null)
        {
            return;
        }

        IndexMapWorkbenchMaterialRoot(_mapWorkbenchSettings.LastMaterialRoot, showMessages: false, populateBrowser: false);
        SelectMapWorkbenchMaterialGroupForExtraction(extractedTarget, extractedTerrainId);
        var result = extractionResult;
        var fileNames = string.Join(", ", result.Files.Take(8).Select(file => Path.GetFileName(file.Path)));
        if (result.Files.Count > 8) fileNames += ", ...";
        _mapMakerMaterialInfoBox.Text =
            $"已提取素材：{result.Files.Count} 个\r\n" +
            $"目标目录：{result.TargetDirectory}\r\n" +
            $"序号范围：{result.StartSequence} - {result.EndSequence}\r\n" +
            $"文件：{fileNames}";
        SetStatus($"地图素材提取完成：{result.Files.Count} 个");
    }

    private byte GetDefaultMapMaterialExtractionTerrainId(Rectangle selection, out bool mixed)
    {
        mixed = false;
        if (_currentMapWorkbenchDraft == null)
        {
            return (byte)_mapMakerTerrainBrushInput.Value;
        }

        var cells = _materialDrivenTerrainService.DeriveTerrainCells(_currentMapWorkbenchDraft, _currentMaterialAssets);
        if (cells.Length != _currentMapWorkbenchDraft.CellCount)
        {
            cells = _terrainEditorCells.Length == _currentMapWorkbenchDraft.CellCount
                ? _terrainEditorCells
                : _currentMapWorkbenchDraft.TerrainCells;
        }

        byte? first = null;
        for (var y = selection.Top; y < selection.Bottom; y++)
        {
            for (var x = selection.Left; x < selection.Right; x++)
            {
                var index = y * _currentMapWorkbenchDraft.GridWidth + x;
                if ((uint)index >= (uint)cells.Length) continue;
                var value = cells[index];
                first ??= value;
                if (value != first.Value)
                {
                    mixed = true;
                }
            }
        }

        return first ?? (byte)_mapMakerTerrainBrushInput.Value;
    }

    private static MapMaterialExtractionTargetType GetDefaultMapMaterialExtractionTargetType(byte terrainId)
        => terrainId == 8 || terrainId is >= 14 and <= 28
            ? MapMaterialExtractionTargetType.Building
            : MapMaterialExtractionTargetType.Terrain;

    private void SelectMapWorkbenchMaterialGroupForExtraction(MapMaterialExtractionTargetType targetType, byte? terrainId)
    {
        var assetType = targetType switch
        {
            MapMaterialExtractionTargetType.Terrain => MaterialAssetTypes.Terrain,
            MapMaterialExtractionTargetType.Building => MaterialAssetTypes.Building,
            MapMaterialExtractionTargetType.Scenery => MaterialAssetTypes.Scenery,
            _ => string.Empty
        };
        var asset = _currentMaterialAssets.FirstOrDefault(candidate =>
            candidate.AssetType.Equals(assetType, StringComparison.OrdinalIgnoreCase) &&
            (targetType == MapMaterialExtractionTargetType.Scenery || candidate.TerrainId == terrainId));
        if (asset == null) return;
        var node = FindMapWorkbenchMaterialTreeNode(BuildMapWorkbenchMaterialGroupKey(asset));
        if (node == null) return;
        _mapMakerMaterialTree.SelectedNode = node;
        PopulateMapWorkbenchMaterialListForSelection();
    }

    private MapMaterialExtractionResult ExtractMapMaterialSelectionDirect(MapMaterialExtractionTargetType targetType, byte? terrainId)
    {
        if (_currentMapWorkbenchDraft == null)
        {
            throw new InvalidOperationException("Map draft is required for material extraction.");
        }

        if (_mapMakerSelectedCellRange.IsEmpty)
        {
            throw new InvalidOperationException("No map cells were selected for material extraction.");
        }

        if (!EnsureMapWorkbenchMaterialLibraryIndexed(showMessages: false))
        {
            throw new InvalidOperationException("Material library is not available.");
        }

        SyncMapWorkbenchDraftFromEditor();
        EnsureCurrentTerrainMaterialPlan(persist: false);
        var request = new MapMaterialExtractionRequest
        {
            Draft = _currentMapWorkbenchDraft,
            MaterialRoot = _mapWorkbenchSettings.LastMaterialRoot,
            CellRange = _mapMakerSelectedCellRange,
            TargetType = targetType,
            TerrainId = targetType == MapMaterialExtractionTargetType.Scenery ? null : terrainId,
            Source = MapMaterialExtractionSource.CurrentComposite,
            Materials = _currentMaterialAssets
        };
        var result = _mapMaterialExtractionService.Extract(request);
        IndexMapWorkbenchMaterialRoot(_mapWorkbenchSettings.LastMaterialRoot, showMessages: false, populateBrowser: false);
        SelectMapWorkbenchMaterialGroupForExtraction(targetType, request.TerrainId);
        return result;
    }

    private void PaintMapWorkbenchCell(Point location, bool groupWithCurrentStroke, bool erase)
    {
        if (_currentMapWorkbenchDraft == null || _mapViewerBox.Image == null) return;
        if (!TryMapPictureBoxPointToTerrainCell(_mapViewerBox, location, _currentMapWorkbenchDraft.GridWidth, _currentMapWorkbenchDraft.GridHeight, out var x, out var y)) return;
        var index = y * _currentMapWorkbenchDraft.GridWidth + x;
        UpdateMapMakerCellPreview(x, y, GetMapWorkbenchFinalTerrainText(index));
        if (IsMapWorkbenchTerrainGenerateMode)
        {
            if (erase) return;
            PrepareMapWorkbenchTerrainGenerateMode();
            PaintMapWorkbenchTerrainCell(index, x, y, groupWithCurrentStroke);
            return;
        }

        switch (_mapWorkbenchBrushMode)
        {
            case MapWorkbenchBrushMode.BuildingBrush:
                PaintMapWorkbenchBuildingCell(index, x, y, groupWithCurrentStroke, erase);
                break;
            case MapWorkbenchBrushMode.SceneryBrush:
            case MapWorkbenchBrushMode.MapBrush:
                PaintMapWorkbenchSceneryCell(index, x, y, groupWithCurrentStroke, erase);
                break;
            case MapWorkbenchBrushMode.TerrainBrush:
            default:
                PaintMapWorkbenchTerrainMaterialCell(index, x, y, groupWithCurrentStroke, erase);
                break;
        }
    }

    private void PaintMapWorkbenchTerrainMaterialCell(int index, int x, int y, bool groupWithCurrentStroke, bool erase)
    {
        if (_currentMapWorkbenchDraft == null) return;
        if (!erase && _mapMakerSelectedMaterial?.AssetType != MaterialAssetTypes.Terrain)
        {
            PaintMapWorkbenchTerrainCell(index, x, y, groupWithCurrentStroke);
            return;
        }

        PaintMapWorkbenchLayerCell(
            index,
            x,
            y,
            groupWithCurrentStroke,
            erase,
            _currentMapWorkbenchDraft.TerrainBaseCells,
            MapCellOverrideSources.TerrainBase,
            "地形素材");
    }

    private void PaintMapWorkbenchBuildingCell(int index, int x, int y, bool groupWithCurrentStroke, bool erase)
    {
        if (_currentMapWorkbenchDraft == null) return;
        PaintMapWorkbenchLayerCell(
            index,
            x,
            y,
            groupWithCurrentStroke,
            erase,
            _currentMapWorkbenchDraft.BuildingOverlayCells,
            MapCellOverrideSources.BuildingOverlay,
            "建筑素材");
    }

    private void PaintMapWorkbenchSceneryCell(int index, int x, int y, bool groupWithCurrentStroke, bool erase)
    {
        if (_currentMapWorkbenchDraft == null) return;
        if (groupWithCurrentStroke && !_mapMakerPendingMapPaintIndexes.Add(index)) return;

        var tileSize = _currentMapWorkbenchDraft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : _currentMapWorkbenchDraft.TileSize;
        var pixelX = x * tileSize;
        var pixelY = y * tileSize;
        var oldValue = CloneMapSceneryOverlay(FindSceneryOverlayAt(pixelX, pixelY, index));
        MapSceneryOverlay? newValue = null;

        if (!erase)
        {
            if (_mapMakerSelectedMaterial == null || !_mapMakerSelectedMaterial.AssetType.Equals(MaterialAssetTypes.Scenery, StringComparison.OrdinalIgnoreCase))
            {
                _mapViewerInfoBox.Text = BuildMapMakerInfo("请先选择一个景物素材。", x, y);
                return;
            }

            var relative = MapDraftService.GetMaterialRelativePath(_currentMapWorkbenchDraft.MaterialRoot, _mapMakerSelectedMaterial.FilePath);
            newValue = new MapSceneryOverlay
            {
                OverlayId = Guid.NewGuid().ToString("N"),
                MaterialRelativePath = relative,
                MaterialCategory = _mapMakerSelectedMaterial.Category,
                DisplayName = _mapMakerSelectedMaterial.FileName,
                X = pixelX,
                Y = pixelY,
                Width = Math.Max(1, _mapMakerSelectedMaterial.Width),
                Height = Math.Max(1, _mapMakerSelectedMaterial.Height),
                ZOrder = GetNextSceneryOverlayZOrder()
            };

            if (oldValue != null &&
                oldValue.MaterialRelativePath.Equals(newValue.MaterialRelativePath, StringComparison.OrdinalIgnoreCase) &&
                oldValue.X == newValue.X &&
                oldValue.Y == newValue.Y)
            {
                _mapViewerInfoBox.Text = BuildMapMakerInfo($"格子 ({x},{y}) 已经贴入该景物。", x, y);
                return;
            }
        }
        else if (oldValue == null)
        {
            _mapViewerInfoBox.Text = BuildMapMakerInfo($"格子 ({x},{y}) 没有可删除的景物。", x, y);
            return;
        }

        RemoveSceneryOverlay(oldValue);
        if (newValue != null)
        {
            _currentMapWorkbenchDraft.SceneryOverlays.Add(CloneMapSceneryOverlay(newValue)!);
            SortSceneryOverlays();
            _selectedSceneryOverlayId = newValue.OverlayId;
        }

        MarkCurrentGeneratedMapNeedsBeautify();
        var change = new MapWorkbenchCellChange(index, null, null, oldValue, CloneMapSceneryOverlay(newValue));
        if (groupWithCurrentStroke)
        {
            _mapMakerPendingMapPaintChanges.Add(change);
        }
        else
        {
            _mapMakerMapUndoStack.Push(new List<MapWorkbenchCellChange> { change });
            _mapMakerMapRedoStack.Clear();
        }

        RenderMapMakerPreview(force: true);
        var action = erase ? "删除" : "贴入";
        UpdateMapMakerCellPreview(x, y, GetMapWorkbenchFinalTerrainText(index));
        _mapViewerInfoBox.Text = BuildMapMakerInfo($"{action}景物：格子 ({x},{y})", x, y);
        SetStatus($"{action}景物: ({x},{y})");
    }

    private void PaintMapWorkbenchLayerCell(
        int index,
        int x,
        int y,
        bool groupWithCurrentStroke,
        bool erase,
        List<MapCellOverride> layer,
        string source,
        string layerName)
    {
        if (_currentMapWorkbenchDraft == null) return;
        if (groupWithCurrentStroke && !_mapMakerPendingMapPaintIndexes.Add(index)) return;

        var oldValue = CloneMapCellOverride(layer.LastOrDefault(cell => cell.Index == index));
        MapCellOverride? newValue = null;
        if (!erase)
        {
            if (_mapMakerSelectedMaterial == null)
            {
                _mapViewerInfoBox.Text = BuildMapMakerInfo($"请先选择一个{layerName}。", x, y);
                return;
            }

            var relative = MapDraftService.GetMaterialRelativePath(_currentMapWorkbenchDraft.MaterialRoot, _mapMakerSelectedMaterial.FilePath);
            newValue = new MapCellOverride
            {
                Index = index,
                MaterialRelativePath = relative,
                MaterialCategory = _mapMakerSelectedMaterial.Category,
                DisplayName = _mapMakerSelectedMaterial.FileName,
                Source = source
            };

            if (oldValue != null &&
                oldValue.MaterialRelativePath.Equals(newValue.MaterialRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                _mapViewerInfoBox.Text = BuildMapMakerInfo($"格子 ({x},{y}) 已经使用该{layerName}。", x, y);
                return;
            }
        }
        else if (oldValue == null)
        {
            _mapViewerInfoBox.Text = BuildMapMakerInfo($"格子 ({x},{y}) 没有可擦除的{layerName}。", x, y);
            return;
        }

        layer.RemoveAll(cell => cell.Index == index);
        if (newValue != null)
        {
            layer.Add(CloneMapCellOverride(newValue)!);
        }

        layer.Sort((left, right) => left.Index.CompareTo(right.Index));
        var previousTerrain = _terrainEditorCells.Length > index ? _terrainEditorCells[index] : (byte)0;
        DeriveCurrentMapWorkbenchTerrain();
        var currentTerrain = _terrainEditorCells.Length > index ? _terrainEditorCells[index] : previousTerrain;
        UpdateMapMakerTerrainChangedCount(previousTerrain, currentTerrain, index);
        MarkCurrentGeneratedMapNeedsBeautify();

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

        MarkMapWorkbenchMaterialDirty(index);
        var action = erase ? "擦除" : "绘制";
        UpdateMapMakerCellPreview(x, y, GetMapWorkbenchFinalTerrainText(index));
        _mapViewerInfoBox.Text = BuildMapMakerInfo($"{action}{layerName}：格子 ({x},{y})", x, y);
        SetStatus($"{action}{layerName}: ({x},{y})");
    }

    private MapSceneryOverlay? FindSceneryOverlayAt(int pixelX, int pixelY, int index)
    {
        if (_currentMapWorkbenchDraft == null) return null;
        var tileSize = _currentMapWorkbenchDraft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : _currentMapWorkbenchDraft.TileSize;
        var tileRect = new Rectangle((index % _currentMapWorkbenchDraft.GridWidth) * tileSize, (index / _currentMapWorkbenchDraft.GridWidth) * tileSize, tileSize, tileSize);
        return _currentMapWorkbenchDraft.SceneryOverlays
            .OrderByDescending(overlay => overlay.ZOrder)
            .FirstOrDefault(overlay =>
            {
                var rect = GetSceneryOverlayRectangle(overlay);
                return rect.Contains(pixelX, pixelY) || rect.IntersectsWith(tileRect);
            });
    }

    private bool TryBeginMapWorkbenchSceneryObjectEdit(MouseEventArgs e)
    {
        if (_currentMapWorkbenchDraft == null || _mapViewerBox.Image == null) return false;
        if (!TryMapPictureBoxPointToImagePoint(_mapViewerBox, e.Location, out var imagePoint)) return false;
        var hitKind = MapSceneryOverlayHitKind.None;
        var overlay = FindSelectedSceneryOverlayHandleAtPoint(imagePoint, out hitKind)
            ?? FindSceneryOverlayAtPoint(imagePoint);
        if (overlay == null)
        {
            _selectedSceneryOverlayId = string.Empty;
            _mapViewerBox.Invalidate();
            return false;
        }

        _selectedSceneryOverlayId = EnsureSceneryOverlayId(overlay);
        if (e.Button == MouseButtons.Right)
        {
            DeleteSelectedSceneryOverlay();
            return true;
        }

        _sceneryOverlayDragging = true;
        _sceneryDragOriginalOverlay = CloneMapSceneryOverlay(overlay);
        _sceneryDragStartImagePoint = imagePoint;
        _sceneryDragHitKind = hitKind == MapSceneryOverlayHitKind.None ? MapSceneryOverlayHitKind.Body : hitKind;
        _mapViewerBox.Capture = true;
        _mapViewerBox.Invalidate();
        return true;
    }

    private void ContinueMapWorkbenchSceneryObjectEdit(MouseEventArgs e)
    {
        if (_currentMapWorkbenchDraft == null || !_sceneryOverlayDragging || _sceneryDragOriginalOverlay == null) return;
        if (!TryMapPictureBoxPointToImagePoint(_mapViewerBox, e.Location, out var imagePoint)) return;
        var overlay = FindSceneryOverlayById(_selectedSceneryOverlayId);
        if (overlay == null) return;
        switch (_sceneryDragHitKind)
        {
            case MapSceneryOverlayHitKind.ScaleNorthWest:
            case MapSceneryOverlayHitKind.ScaleNorthEast:
            case MapSceneryOverlayHitKind.ScaleSouthEast:
            case MapSceneryOverlayHitKind.ScaleSouthWest:
                ScaleSceneryOverlayFromDrag(overlay, _sceneryDragOriginalOverlay, _sceneryDragStartImagePoint, imagePoint);
                break;
            case MapSceneryOverlayHitKind.Rotate:
                RotateSceneryOverlayFromDrag(overlay, _sceneryDragOriginalOverlay, _sceneryDragStartImagePoint, imagePoint);
                break;
            case MapSceneryOverlayHitKind.Body:
            default:
                overlay.X = _sceneryDragOriginalOverlay.X + (int)MathF.Round(imagePoint.X - _sceneryDragStartImagePoint.X);
                overlay.Y = _sceneryDragOriginalOverlay.Y + (int)MathF.Round(imagePoint.Y - _sceneryDragStartImagePoint.Y);
                break;
        }
        ClampSceneryOverlayToDraft(overlay);
        MarkCurrentGeneratedMapNeedsBeautify();
        RenderMapMakerPreview(force: true);
        _mapViewerInfoBox.Text = BuildMapMakerInfo(BuildSceneryOverlayTransformInfo(overlay, _sceneryDragHitKind));
    }

    private void EndMapWorkbenchSceneryObjectEdit()
    {
        if (!_sceneryOverlayDragging) return;
        _mapViewerBox.Capture = false;
        _sceneryOverlayDragging = false;
        var oldValue = _sceneryDragOriginalOverlay;
        var newValue = CloneMapSceneryOverlay(FindSceneryOverlayById(_selectedSceneryOverlayId));
        _sceneryDragOriginalOverlay = null;
        _sceneryDragHitKind = MapSceneryOverlayHitKind.None;
        if (oldValue != null && newValue != null && !SameSceneryOverlayTransform(oldValue, newValue))
        {
            _mapMakerMapUndoStack.Push(new List<MapWorkbenchCellChange>
            {
                new(-1, null, null, oldValue, newValue)
            });
            _mapMakerMapRedoStack.Clear();
        }

        RenderMapMakerPreview(force: true);
        UpdateMapMakerEditingButtons();
    }

    private MapSceneryOverlay? FindSceneryOverlayAtPoint(PointF imagePoint)
        => _currentMapWorkbenchDraft?.SceneryOverlays
            .OrderByDescending(overlay => overlay.ZOrder)
            .FirstOrDefault(overlay => IsPointInsideSceneryOverlay(overlay, imagePoint));

    private MapSceneryOverlay? FindSelectedSceneryOverlayHandleAtPoint(PointF imagePoint, out MapSceneryOverlayHitKind hitKind)
    {
        hitKind = MapSceneryOverlayHitKind.None;
        var overlay = FindSceneryOverlayById(_selectedSceneryOverlayId);
        if (overlay == null) return null;

        var hitRadius = GetSceneryOverlayHandleHitRadiusInSource();
        if (Distance(imagePoint, GetSceneryOverlayRotationHandlePoint(overlay)) <= hitRadius)
        {
            hitKind = MapSceneryOverlayHitKind.Rotate;
            return overlay;
        }

        var corners = GetSceneryOverlayCorners(overlay);
        var kinds = new[]
        {
            MapSceneryOverlayHitKind.ScaleNorthWest,
            MapSceneryOverlayHitKind.ScaleNorthEast,
            MapSceneryOverlayHitKind.ScaleSouthEast,
            MapSceneryOverlayHitKind.ScaleSouthWest
        };
        for (var i = 0; i < corners.Length && i < kinds.Length; i++)
        {
            if (Distance(imagePoint, corners[i]) > hitRadius) continue;
            hitKind = kinds[i];
            return overlay;
        }

        return null;
    }

    private void UpdateMapWorkbenchSceneryCursor(Point location)
    {
        if (_currentMapWorkbenchDraft == null || _mapViewerBox.Image == null)
        {
            _mapViewerBox.Cursor = Cursors.Default;
            return;
        }

        if (!TryMapPictureBoxPointToImagePoint(_mapViewerBox, location, out var imagePoint))
        {
            _mapViewerBox.Cursor = Cursors.Cross;
            return;
        }

        if (FindSelectedSceneryOverlayHandleAtPoint(imagePoint, out var hitKind) != null)
        {
            _mapViewerBox.Cursor = hitKind switch
            {
                MapSceneryOverlayHitKind.ScaleNorthWest or MapSceneryOverlayHitKind.ScaleSouthEast => Cursors.SizeNWSE,
                MapSceneryOverlayHitKind.ScaleNorthEast or MapSceneryOverlayHitKind.ScaleSouthWest => Cursors.SizeNESW,
                MapSceneryOverlayHitKind.Rotate => Cursors.Hand,
                _ => Cursors.SizeAll
            };
            return;
        }

        _mapViewerBox.Cursor = FindSceneryOverlayAtPoint(imagePoint) == null ? Cursors.Cross : Cursors.SizeAll;
    }

    private MapSceneryOverlay? FindSceneryOverlayById(string overlayId)
        => string.IsNullOrWhiteSpace(overlayId) || _currentMapWorkbenchDraft == null
            ? null
            : _currentMapWorkbenchDraft.SceneryOverlays.FirstOrDefault(overlay => EnsureSceneryOverlayId(overlay).Equals(overlayId, StringComparison.Ordinal));

    private string EnsureSceneryOverlayId(MapSceneryOverlay overlay)
    {
        if (string.IsNullOrWhiteSpace(overlay.OverlayId))
        {
            overlay.OverlayId = Guid.NewGuid().ToString("N");
        }

        return overlay.OverlayId;
    }

    private static bool IsPointInsideSceneryOverlay(MapSceneryOverlay overlay, PointF point)
    {
        var rect = GetSceneryOverlayRectangle(overlay);
        var centerX = rect.Left + rect.Width / 2f;
        var centerY = rect.Top + rect.Height / 2f;
        var dx = point.X - centerX;
        var dy = point.Y - centerY;
        var radians = -overlay.RotationDegrees * MathF.PI / 180f;
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);
        var localX = dx * cos - dy * sin;
        var localY = dx * sin + dy * cos;
        return MathF.Abs(localX) <= rect.Width / 2f && MathF.Abs(localY) <= rect.Height / 2f;
    }

    private void DeleteSelectedSceneryOverlay()
    {
        var overlay = CloneMapSceneryOverlay(FindSceneryOverlayById(_selectedSceneryOverlayId));
        if (overlay == null) return;
        RemoveSceneryOverlay(overlay);
        _selectedSceneryOverlayId = string.Empty;
        MarkCurrentGeneratedMapNeedsBeautify();
        _mapMakerMapUndoStack.Push(new List<MapWorkbenchCellChange>
        {
            new(-1, null, null, overlay, null)
        });
        _mapMakerMapRedoStack.Clear();
        RenderMapMakerPreview(force: true);
    }

    private void HandleMapWorkbenchSceneryKeyDown(KeyEventArgs e)
    {
        if (_currentMapWorkbenchDraft == null || string.IsNullOrWhiteSpace(_selectedSceneryOverlayId)) return;
        var overlay = FindSceneryOverlayById(_selectedSceneryOverlayId);
        if (overlay == null) return;
        var oldValue = CloneMapSceneryOverlay(overlay);
        var step = e.Shift ? 10 : 1;
        var handled = true;
        switch (e.KeyCode)
        {
            case Keys.Delete:
                DeleteSelectedSceneryOverlay();
                e.Handled = true;
                return;
            case Keys.Left:
                overlay.X -= step;
                break;
            case Keys.Right:
                overlay.X += step;
                break;
            case Keys.Up:
                overlay.Y -= step;
                break;
            case Keys.Down:
                overlay.Y += step;
                break;
            case Keys.Oemplus:
            case Keys.Add:
                ScaleSceneryOverlay(overlay, e.Shift ? 1.25f : 1.05f);
                break;
            case Keys.OemMinus:
            case Keys.Subtract:
                ScaleSceneryOverlay(overlay, e.Shift ? 0.8f : 0.95f);
                break;
            case Keys.OemOpenBrackets:
                overlay.RotationDegrees = NormalizeRotationDegrees(overlay.RotationDegrees - (e.Shift ? 15f : 5f));
                break;
            case Keys.OemCloseBrackets:
                overlay.RotationDegrees = NormalizeRotationDegrees(overlay.RotationDegrees + (e.Shift ? 15f : 5f));
                break;
            default:
                handled = false;
                break;
        }

        if (!handled || oldValue == null) return;
        ClampSceneryOverlayToDraft(overlay);
        MarkCurrentGeneratedMapNeedsBeautify();
        _mapMakerMapUndoStack.Push(new List<MapWorkbenchCellChange>
        {
            new(-1, null, null, oldValue, CloneMapSceneryOverlay(overlay))
        });
        _mapMakerMapRedoStack.Clear();
        RenderMapMakerPreview(force: true);
        e.Handled = true;
    }

    private void ScaleSceneryOverlay(MapSceneryOverlay overlay, float factor)
    {
        var oldWidth = Math.Max(1, overlay.Width);
        var oldHeight = Math.Max(1, overlay.Height);
        var centerX = overlay.X + oldWidth / 2f;
        var centerY = overlay.Y + oldHeight / 2f;
        overlay.Width = Math.Max(1, (int)MathF.Round(oldWidth * factor));
        overlay.Height = Math.Max(1, (int)MathF.Round(oldHeight * factor));
        overlay.X = (int)MathF.Round(centerX - overlay.Width / 2f);
        overlay.Y = (int)MathF.Round(centerY - overlay.Height / 2f);
    }

    private void ScaleSceneryOverlayFromDrag(
        MapSceneryOverlay overlay,
        MapSceneryOverlay original,
        PointF startPoint,
        PointF imagePoint)
    {
        var rect = GetSceneryOverlayRectangle(original);
        var center = GetSceneryOverlayCenter(original);
        var startDistance = MathF.Max(1f, Distance(center, startPoint));
        var currentDistance = MathF.Max(1f, Distance(center, imagePoint));
        var factor = MathF.Max(0.05f, currentDistance / startDistance);
        overlay.Width = Math.Max(1, (int)MathF.Round(rect.Width * factor));
        overlay.Height = Math.Max(1, (int)MathF.Round(rect.Height * factor));
        overlay.X = (int)MathF.Round(center.X - overlay.Width / 2f);
        overlay.Y = (int)MathF.Round(center.Y - overlay.Height / 2f);
        overlay.RotationDegrees = original.RotationDegrees;
    }

    private static void RotateSceneryOverlayFromDrag(MapSceneryOverlay overlay, MapSceneryOverlay original, PointF startPoint, PointF imagePoint)
    {
        var center = GetSceneryOverlayCenter(original);
        var currentAngle = AngleFromCenter(center, imagePoint);
        var startAngle = AngleFromCenter(center, startPoint);
        overlay.X = original.X;
        overlay.Y = original.Y;
        overlay.Width = original.Width;
        overlay.Height = original.Height;
        overlay.RotationDegrees = NormalizeRotationDegrees(original.RotationDegrees + currentAngle - startAngle);
    }

    private void ClampSceneryOverlayToDraft(MapSceneryOverlay overlay)
    {
        if (_currentMapWorkbenchDraft == null) return;
        var pixelWidth = Math.Max(MapResourceItem.MapTilePixelSize, _currentMapWorkbenchDraft.PixelWidth);
        var pixelHeight = Math.Max(MapResourceItem.MapTilePixelSize, _currentMapWorkbenchDraft.PixelHeight);
        overlay.X = Math.Clamp(overlay.X, -pixelWidth, pixelWidth);
        overlay.Y = Math.Clamp(overlay.Y, -pixelHeight, pixelHeight);
        overlay.Width = Math.Clamp(overlay.Width, 1, pixelWidth * 2);
        overlay.Height = Math.Clamp(overlay.Height, 1, pixelHeight * 2);
        overlay.RotationDegrees = NormalizeRotationDegrees(overlay.RotationDegrees);
    }

    private static float NormalizeRotationDegrees(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return 0;
        value %= 360f;
        if (value < 0) value += 360f;
        return value;
    }

    private static bool SameSceneryOverlayTransform(MapSceneryOverlay left, MapSceneryOverlay right)
        => left.OverlayId.Equals(right.OverlayId, StringComparison.Ordinal) &&
           left.MaterialRelativePath.Equals(right.MaterialRelativePath, StringComparison.OrdinalIgnoreCase) &&
           left.X == right.X &&
           left.Y == right.Y &&
           left.Width == right.Width &&
           left.Height == right.Height &&
           Math.Abs(left.RotationDegrees - right.RotationDegrees) < 0.001f &&
           left.ZOrder == right.ZOrder;

    private static string BuildSceneryOverlayTransformInfo(MapSceneryOverlay overlay, MapSceneryOverlayHitKind hitKind)
    {
        var action = hitKind switch
        {
            MapSceneryOverlayHitKind.Rotate => "旋转",
            MapSceneryOverlayHitKind.ScaleNorthWest or MapSceneryOverlayHitKind.ScaleNorthEast or MapSceneryOverlayHitKind.ScaleSouthEast or MapSceneryOverlayHitKind.ScaleSouthWest => "缩放",
            _ => "移动"
        };
        return string.Format(
            CultureInfo.InvariantCulture,
            "景物对象：{0} X={1} Y={2} W={3} H={4} R={5:0.#}°",
            action,
            overlay.X,
            overlay.Y,
            overlay.Width,
            overlay.Height,
            overlay.RotationDegrees);
    }

    private void PaintMapWorkbenchScenerySelection(Graphics g)
    {
        PaintMapWorkbenchCellSelection(g);
        var overlay = FindSceneryOverlayById(_selectedSceneryOverlayId);
        if (overlay == null || _mapViewerRenderedImage == null) return;
        var points = GetSceneryOverlayCorners(overlay)
            .Select(MapMakerSourcePointToDisplayPoint)
            .ToArray();
        if (points.Length != 4) return;
        var rotationHandle = MapMakerSourcePointToDisplayPoint(GetSceneryOverlayRotationHandlePoint(overlay));
        var topCenter = new PointF((points[0].X + points[1].X) / 2f, (points[0].Y + points[1].Y) / 2f);
        using var pen = new Pen(Color.FromArgb(255, 255, 220, 40), 2f);
        g.DrawPolygon(pen, points);
        g.DrawLine(pen, topCenter, rotationHandle);
        using var brush = new SolidBrush(Color.FromArgb(240, 255, 255, 255));
        using var border = new Pen(Color.FromArgb(220, 30, 30, 30), 1f);
        foreach (var point in points)
        {
            var rect = new RectangleF(point.X - 4, point.Y - 4, 8, 8);
            g.FillRectangle(brush, rect);
            g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
        }

        var handleRect = new RectangleF(rotationHandle.X - 5, rotationHandle.Y - 5, 10, 10);
        g.FillEllipse(brush, handleRect);
        g.DrawEllipse(border, handleRect);
    }

    private void PaintMapWorkbenchCellSelection(Graphics g)
    {
        if (_mapMakerSelectedCellRange.IsEmpty || _currentMapWorkbenchDraft == null || _mapViewerRenderedImage == null) return;
        var rect = GetMapMakerSelectionDisplayRect(_mapMakerSelectedCellRange);
        if (rect.IsEmpty) return;
        rect = Rectangle.Intersect(rect, _mapViewerBox.ClientRectangle);
        if (rect.IsEmpty) return;
        using var fill = new SolidBrush(Color.FromArgb(48, 255, 220, 40));
        using var pen = new Pen(Color.FromArgb(230, 255, 220, 40), 2f);
        using var border = new Pen(Color.FromArgb(200, 30, 30, 30), 1f);
        g.FillRectangle(fill, rect);
        var outline = new Rectangle(rect.X, rect.Y, Math.Max(1, rect.Width - 1), Math.Max(1, rect.Height - 1));
        g.DrawRectangle(border, outline);
        g.DrawRectangle(pen, outline);
    }

    private static PointF[] GetSceneryOverlayCorners(MapSceneryOverlay overlay)
    {
        var rect = GetSceneryOverlayRectangle(overlay);
        var center = GetSceneryOverlayCenter(overlay);
        var halfW = rect.Width / 2f;
        var halfH = rect.Height / 2f;
        var radians = overlay.RotationDegrees * MathF.PI / 180f;
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);
        PointF Transform(float x, float y) => new(center.X + x * cos - y * sin, center.Y + x * sin + y * cos);
        return
        [
            Transform(-halfW, -halfH),
            Transform(halfW, -halfH),
            Transform(halfW, halfH),
            Transform(-halfW, halfH)
        ];
    }

    private static PointF GetSceneryOverlayCenter(MapSceneryOverlay overlay)
    {
        var rect = GetSceneryOverlayRectangle(overlay);
        return new PointF(rect.Left + rect.Width / 2f, rect.Top + rect.Height / 2f);
    }

    private static PointF GetSceneryOverlayRotationHandlePoint(MapSceneryOverlay overlay)
    {
        var rect = GetSceneryOverlayRectangle(overlay);
        var center = GetSceneryOverlayCenter(overlay);
        var offset = MathF.Max(18f, MathF.Min(48f, MathF.Min(rect.Width, rect.Height) * 0.28f));
        return RotatePoint(new PointF(center.X, rect.Top - offset), center, overlay.RotationDegrees);
    }

    private float GetSceneryOverlayHandleHitRadiusInSource()
    {
        if (_mapViewerRenderedImage == null || _mapViewerBox.Width <= 0 || _mapViewerBox.Height <= 0) return 8f;
        var scaleX = _mapViewerBox.Width / (float)Math.Max(1, _mapViewerRenderedImage.Width);
        var scaleY = _mapViewerBox.Height / (float)Math.Max(1, _mapViewerRenderedImage.Height);
        var scale = Math.Max(0.01f, Math.Min(scaleX, scaleY));
        return MathF.Max(6f, 10f / scale);
    }

    private static PointF RotatePoint(PointF point, PointF center, float degrees)
    {
        var radians = degrees * MathF.PI / 180f;
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        return new PointF(center.X + dx * cos - dy * sin, center.Y + dx * sin + dy * cos);
    }

    private static float AngleFromCenter(PointF center, PointF point)
        => MathF.Atan2(point.Y - center.Y, point.X - center.X) * 180f / MathF.PI;

    private static float Distance(PointF left, PointF right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private PointF MapMakerSourcePointToDisplayPoint(PointF source)
    {
        if (_mapViewerRenderedImage == null || _mapViewerRenderedImage.Width <= 0 || _mapViewerRenderedImage.Height <= 0) return PointF.Empty;
        var scaleX = _mapViewerBox.Width / (float)_mapViewerRenderedImage.Width;
        var scaleY = _mapViewerBox.Height / (float)_mapViewerRenderedImage.Height;
        return new PointF(source.X * scaleX, source.Y * scaleY);
    }

    private static bool TryMapPictureBoxPointToImagePoint(PictureBox box, Point point, out PointF imagePoint)
    {
        imagePoint = PointF.Empty;
        if (box.Image == null || box.Width <= 0 || box.Height <= 0) return false;
        if (box.SizeMode == PictureBoxSizeMode.StretchImage)
        {
            imagePoint = new PointF(
                point.X * box.Image.Width / (float)Math.Max(1, box.Width),
                point.Y * box.Image.Height / (float)Math.Max(1, box.Height));
            return imagePoint.X >= 0 && imagePoint.Y >= 0 && imagePoint.X < box.Image.Width && imagePoint.Y < box.Image.Height;
        }

        var rect = GetImageDisplayRectangle(box);
        if (!rect.Contains(point)) return false;
        imagePoint = new PointF(
            (point.X - rect.X) * box.Image.Width / (float)Math.Max(1, rect.Width),
            (point.Y - rect.Y) * box.Image.Height / (float)Math.Max(1, rect.Height));
        return true;
    }

    private int GetNextSceneryOverlayZOrder()
        => _currentMapWorkbenchDraft == null || _currentMapWorkbenchDraft.SceneryOverlays.Count == 0
            ? 0
            : _currentMapWorkbenchDraft.SceneryOverlays.Max(overlay => overlay.ZOrder) + 1;

    private void RemoveSceneryOverlay(MapSceneryOverlay? overlay)
    {
        if (_currentMapWorkbenchDraft == null || overlay == null) return;
        _currentMapWorkbenchDraft.SceneryOverlays.RemoveAll(existing => SameSceneryOverlay(existing, overlay));
    }

    private void UpsertSceneryOverlay(MapSceneryOverlay? overlay)
    {
        if (_currentMapWorkbenchDraft == null || overlay == null) return;
        RemoveSceneryOverlay(overlay);
        _currentMapWorkbenchDraft.SceneryOverlays.Add(CloneMapSceneryOverlay(overlay)!);
        SortSceneryOverlays();
    }

    private void SortSceneryOverlays()
    {
        if (_currentMapWorkbenchDraft == null) return;
        _currentMapWorkbenchDraft.SceneryOverlays = _currentMapWorkbenchDraft.SceneryOverlays
            .OrderBy(overlay => overlay.ZOrder)
            .ThenBy(overlay => overlay.Y)
            .ThenBy(overlay => overlay.X)
            .ToList();
    }

    private static Rectangle GetSceneryOverlayRectangle(MapSceneryOverlay overlay)
        => new(
            overlay.X,
            overlay.Y,
            Math.Max(1, overlay.Width),
            Math.Max(1, overlay.Height));

    private static bool SameSceneryOverlay(MapSceneryOverlay left, MapSceneryOverlay right)
    {
        if (!string.IsNullOrWhiteSpace(left.OverlayId) && !string.IsNullOrWhiteSpace(right.OverlayId))
        {
            return left.OverlayId.Equals(right.OverlayId, StringComparison.Ordinal);
        }

        return left.ZOrder == right.ZOrder &&
               left.X == right.X &&
               left.Y == right.Y &&
               left.MaterialRelativePath.Equals(right.MaterialRelativePath, StringComparison.OrdinalIgnoreCase);
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
            DisplayName = _mapMakerSelectedMaterial.FileName,
            Source = MapCellOverrideSources.ManualOverride
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
        _mapViewerInfoBox.Text = BuildMapMakerInfo($"微调覆盖：格子 ({x},{y}) <- {_mapMakerSelectedMaterial.Category}/{_mapMakerSelectedMaterial.FileName}", x, y);
        SetStatus($"微调覆盖：({x},{y})");
    }

    private void PaintMapWorkbenchBuildingCell(int index, int x, int y, bool groupWithCurrentStroke)
    {
        if (_currentMapWorkbenchDraft == null) return;
        if (_mapMakerSelectedMaterial == null)
        {
            _mapViewerInfoBox.Text = BuildMapMakerInfo("请先选择一个建筑素材。", x, y);
            return;
        }

        var oldValue = _currentMapWorkbenchDraft.BuildingOverlayCells.FirstOrDefault(cell => cell.Index == index);
        if (groupWithCurrentStroke && !_mapMakerPendingMapPaintIndexes.Add(index)) return;
        var relative = MapDraftService.GetMaterialRelativePath(_currentMapWorkbenchDraft.MaterialRoot, _mapMakerSelectedMaterial.FilePath);
        var newValue = new MapCellOverride
        {
            Index = index,
            MaterialRelativePath = relative,
            MaterialCategory = _mapMakerSelectedMaterial.Category,
            DisplayName = _mapMakerSelectedMaterial.FileName,
            Source = MapCellOverrideSources.BuildingOverlay
        };

        if (oldValue != null &&
            oldValue.MaterialRelativePath.Equals(newValue.MaterialRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            _mapViewerInfoBox.Text = BuildMapMakerInfo($"格子 ({x},{y}) 已经使用该建筑素材。", x, y);
            return;
        }

        _currentMapWorkbenchDraft.BuildingOverlayCells.RemoveAll(cell => cell.Index == index);
        _currentMapWorkbenchDraft.BuildingOverlayCells.Add(newValue);
        _currentMapWorkbenchDraft.BuildingOverlayCells = _currentMapWorkbenchDraft.BuildingOverlayCells.OrderBy(cell => cell.Index).ToList();
        var change = new MapWorkbenchCellChange(index, CloneMapCellOverride(oldValue), CloneMapCellOverride(newValue));
        if (groupWithCurrentStroke)
        {
            _mapMakerPendingMapPaintChanges.Add(change);
        }
        else
        {
            _mapMakerMapUndoStack.Push(new List<MapWorkbenchCellChange> { change });
            _mapMakerMapRedoStack.Clear();
        }

        RenderMapMakerPreview(force: true);
        _mapViewerInfoBox.Text = BuildMapMakerInfo($"建筑覆盖：格子 ({x},{y}) <- {_mapMakerSelectedMaterial.Category}/{_mapMakerSelectedMaterial.FileName}", x, y);
        SetStatus($"建筑覆盖：({x},{y})");
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
        _currentMapWorkbenchDraft.AutoGenerateMapFromTerrain = true;
        MarkCurrentGeneratedMapNeedsBeautify();
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

        var terrainLayerOnly = ShouldRenderMapWorkbenchTerrainLayerOnly();
        var dirtyRect = terrainLayerOnly
            ? _mapCanvasPreviewRenderer.UpdateTerrainCell(_currentMapWorkbenchDraft, index)
            : _mapCanvasPreviewRenderer.MarkTerrainDirty(_currentMapWorkbenchDraft, index);
        foreach (var dirtyIndex in ExpandIndexesWithNeighbors(new[] { index }))
        {
            _mapMakerDirtyTerrainPreviewIndexes.Add(dirtyIndex);
        }

        if (_mapMakerPainting && !terrainLayerOnly)
        {
            _mapMakerRenderDeferred = true;
            ScheduleMapMakerDirtyBasePreviewRefresh();
        }
        else
        {
            RefreshMapMakerPreviewTile(dirtyRect);
            ScheduleMapMakerDirtyBasePreviewRefresh();
        }
        UpdateMapMakerCellPreview(x, y, FormatTerrainValue(newValue));
        _mapViewerInfoBox.Text = BuildMapMakerInfo($"格子 ({x},{y})：{FormatTerrainValue(oldValue)} -> {FormatTerrainValue(newValue)}", x, y);
        SetStatus($"Terrain brush: ({x},{y})");
    }

    private void RefreshMapMakerTerrainChangePreview(IEnumerable<int> indexes, bool runBeautify)
    {
        if (_currentMapWorkbenchDraft == null) return;
        var expanded = ExpandIndexesWithNeighbors(indexes).ToList();
        if (expanded.Count == 0) return;
        foreach (var index in expanded)
        {
            _mapMakerDirtyTerrainPreviewIndexes.Add(index);
        }

        var dirtyRect = _mapCanvasPreviewRenderer.MarkTerrainDirty(_currentMapWorkbenchDraft, expanded);
        RefreshGeneratedMapCells();
        FlushMapMakerDirtyBasePreview(runBeautify);
        if (!dirtyRect.IsEmpty)
        {
            RefreshMapMakerPreviewTile(dirtyRect);
        }
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
            MarkCurrentGeneratedMapNeedsBeautify();
            RefreshMapMakerTerrainChangePreview(changes.Select(change => change.Index), runBeautify: false);
            _mapViewerInfoBox.Text = BuildMapMakerInfo($"Undo terrain paint: {changes.Count} cells.");
            SetStatus("地图工作台已撤销地形绘制");
            return;
        }

        if (_mapMakerMapUndoStack.Count > 0)
        {
            var changes = _mapMakerMapUndoStack.Pop();
            for (var i = changes.Count - 1; i >= 0; i--)
            {
                var change = changes[i];
                ApplyMapWorkbenchChange(change, undo: true);
            }
            _mapMakerMapRedoStack.Push(changes);
            RefreshMapWorkbenchMapChanges(changes);
            _mapViewerInfoBox.Text = BuildMapMakerInfo($"已撤销一笔覆盖绘制：{changes.Count} 格。");
            SetStatus("地图工作台已撤销覆盖绘制");
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
            MarkCurrentGeneratedMapNeedsBeautify();
            RefreshMapMakerTerrainChangePreview(changes.Select(change => change.Index), runBeautify: false);
            _mapViewerInfoBox.Text = BuildMapMakerInfo($"Redo terrain paint: {changes.Count} cells.");
            SetStatus("地图工作台已重做地形绘制");
            return;
        }

        if (_mapMakerMapRedoStack.Count > 0)
        {
            var changes = _mapMakerMapRedoStack.Pop();
            foreach (var change in changes)
            {
                ApplyMapWorkbenchChange(change, undo: false);
            }
            _mapMakerMapUndoStack.Push(changes);
            RefreshMapWorkbenchMapChanges(changes);
            _mapViewerInfoBox.Text = BuildMapMakerInfo($"已重做一笔覆盖绘制：{changes.Count} 格。");
            SetStatus("地图工作台已重做覆盖绘制");
        }
    }

    private void ApplyMapWorkbenchChange(MapWorkbenchCellChange change, bool undo)
    {
        if (change.OldSceneryOverlay != null || change.NewSceneryOverlay != null)
        {
            RemoveSceneryOverlay(undo ? change.NewSceneryOverlay : change.OldSceneryOverlay);
            UpsertSceneryOverlay(undo ? change.OldSceneryOverlay : change.NewSceneryOverlay);
            return;
        }

        var sourceHint = undo ? change.OldValue ?? change.NewValue : change.NewValue ?? change.OldValue;
        var value = undo ? change.OldValue : change.NewValue;
        ApplyMapWorkbenchCellChangeValue(change.Index, sourceHint, value);
    }

    private void RefreshMapWorkbenchMapChanges(IReadOnlyList<MapWorkbenchCellChange> changes)
    {
        if (changes.Any(change => change.OldSceneryOverlay != null || change.NewSceneryOverlay != null))
        {
            MarkCurrentGeneratedMapNeedsBeautify();
            RenderMapMakerPreview(force: true);
            return;
        }

        DeriveCurrentMapWorkbenchTerrain();
        RefreshMapMakerTerrainChangePreview(changes.Select(change => change.Index), runBeautify: false);
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

    private void SetBuildingOverlayCell(int index, MapCellOverride? value)
    {
        if (_currentMapWorkbenchDraft == null) return;
        _currentMapWorkbenchDraft.BuildingOverlayCells.RemoveAll(cell => cell.Index == index);
        if (value != null)
        {
            value.Index = index;
            value.Source = MapCellOverrideSources.BuildingOverlay;
            _currentMapWorkbenchDraft.BuildingOverlayCells.Add(CloneMapCellOverride(value)!);
            _currentMapWorkbenchDraft.BuildingOverlayCells = _currentMapWorkbenchDraft.BuildingOverlayCells.OrderBy(cell => cell.Index).ToList();
        }
    }

    private void ApplyMapWorkbenchCellChangeValue(int index, MapCellOverride? sourceHint, MapCellOverride? value)
    {
        var source = sourceHint?.Source ?? value?.Source ?? MapCellOverrideSources.ManualOverride;
        if (source.Equals(MapCellOverrideSources.TerrainBase, StringComparison.OrdinalIgnoreCase))
        {
            SetLayerCell(_currentMapWorkbenchDraft?.TerrainBaseCells, index, CloneMapCellOverride(value), MapCellOverrideSources.TerrainBase);
            return;
        }

        if (source.Equals(MapCellOverrideSources.BuildingOverlay, StringComparison.OrdinalIgnoreCase))
        {
            SetLayerCell(_currentMapWorkbenchDraft?.BuildingOverlayCells, index, CloneMapCellOverride(value), MapCellOverrideSources.BuildingOverlay);
            return;
        }

        if (source.Equals(MapCellOverrideSources.SceneryOverlay, StringComparison.OrdinalIgnoreCase))
        {
            SetLayerCell(_currentMapWorkbenchDraft?.SceneryOverlayCells, index, CloneMapCellOverride(value), MapCellOverrideSources.SceneryOverlay);
            return;
        }

        SetMapCellOverride(index, CloneMapCellOverride(value));
    }

    private static void SetLayerCell(List<MapCellOverride>? layer, int index, MapCellOverride? value, string source)
    {
        if (layer == null) return;
        layer.RemoveAll(cell => cell.Index == index);
        if (value != null)
        {
            value.Index = index;
            value.Source = source;
            layer.Add(value);
            layer.Sort((left, right) => left.Index.CompareTo(right.Index));
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
                DisplayName = value.DisplayName,
                Source = string.IsNullOrWhiteSpace(value.Source) ? MapCellOverrideSources.ManualOverride : value.Source
            };

    private static MapSceneryOverlay? CloneMapSceneryOverlay(MapSceneryOverlay? value)
        => value == null
            ? null
            : new MapSceneryOverlay
            {
                OverlayId = value.OverlayId,
                MaterialRelativePath = value.MaterialRelativePath,
                MaterialCategory = value.MaterialCategory,
                DisplayName = value.DisplayName,
                X = value.X,
                Y = value.Y,
                Width = value.Width,
                Height = value.Height,
                RotationDegrees = value.RotationDegrees,
                ZOrder = value.ZOrder
            };

    private void ClearMapMakerCellPreview()
    {
        _mapViewerCellPreviewLabel.Text = "Terrain:    Cell:";
    }

    private void OpenMapWorkbenchMaterialPlanDialog()
    {
        if (_currentMapWorkbenchDraft == null)
        {
            MessageBox.Show(this, "Load or create a map draft first.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        EnsureMapWorkbenchMaterialLibraryIndexed(showMessages: false);
        SyncMapWorkbenchDraftFromEditor();
        EnsureCurrentTerrainMaterialPlan(persist: true);

        using var dialog = new Form
        {
            Text = "Main Material Settings",
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            Width = 900,
            Height = 520,
            Font = Font
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(10)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        dialog.Controls.Add(layout);

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoGenerateColumns = true
        };
        layout.Controls.Add(grid, 0, 0);

        var info = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 8),
            Text = "Each terrain family keeps one stable primary material for this map. Manual picks override auto selection."
        };
        layout.Controls.Add(info, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var closeButton = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
        var manualButton = new Button { Text = "Pick Material", AutoSize = true };
        var rerollButton = new Button { Text = "Reroll", AutoSize = true };
        var autoButton = new Button { Text = "Auto", AutoSize = true };
        buttons.Controls.AddRange(new Control[] { closeButton, autoButton, rerollButton, manualButton });
        layout.Controls.Add(buttons, 0, 2);
        dialog.AcceptButton = closeButton;
        dialog.CancelButton = closeButton;

        void ReloadRows()
        {
            EnsureCurrentTerrainMaterialPlan(persist: true);
            grid.DataSource = new BindingList<TerrainMaterialPlanRow>(BuildTerrainMaterialPlanRows().ToList());
            if (grid.Columns[nameof(TerrainMaterialPlanRow.TerrainId)] != null)
            {
                grid.Columns[nameof(TerrainMaterialPlanRow.TerrainId)].Visible = false;
            }

            if (grid.Columns[nameof(TerrainMaterialPlanRow.Terrain)] != null) grid.Columns[nameof(TerrainMaterialPlanRow.Terrain)].Width = 150;
            if (grid.Columns[nameof(TerrainMaterialPlanRow.VisualFamily)] != null) grid.Columns[nameof(TerrainMaterialPlanRow.VisualFamily)].Width = 100;
            if (grid.Columns[nameof(TerrainMaterialPlanRow.CurrentMaterial)] != null) grid.Columns[nameof(TerrainMaterialPlanRow.CurrentMaterial)].Width = 360;
            if (grid.Columns[nameof(TerrainMaterialPlanRow.SelectionMode)] != null) grid.Columns[nameof(TerrainMaterialPlanRow.SelectionMode)].Width = 120;
            if (grid.Columns[nameof(TerrainMaterialPlanRow.CandidateCount)] != null) grid.Columns[nameof(TerrainMaterialPlanRow.CandidateCount)].Width = 90;
        }

        TerrainMaterialPlanRow? SelectedRow()
            => grid.SelectedRows.Count > 0
                ? grid.SelectedRows[0].DataBoundItem as TerrainMaterialPlanRow
                : grid.CurrentRow?.DataBoundItem as TerrainMaterialPlanRow;

        void RefreshAfterPlanChange(byte terrainId, IReadOnlyDictionary<string, string> beforePlan, string message)
        {
            EnsureCurrentTerrainMaterialPlan(persist: true);
            var affectedIndexes = GetChangedTerrainMaterialIndexes(beforePlan, terrainId).ToList();
            RefreshGeneratedMapCells();
            PersistCurrentTerrainMaterialPlan();
            if (affectedIndexes.Count > 0)
            {
                RefreshMapMakerGeneratedMaterialPreview(affectedIndexes);
            }
            ReloadRows();
            var suffix = affectedIndexes.Count == 0 ? " (material unchanged; preview not refreshed)" : $" (refreshed {affectedIndexes.Count} related cells)";
            _mapViewerInfoBox.Text = BuildMapMakerInfo(message + suffix);
            SetStatus(message + suffix);
        }

        manualButton.Click += (_, _) =>
        {
            var row = SelectedRow();
            if (row == null) return;
            var candidate = SelectTerrainMaterialCandidate(row.TerrainId);
            if (candidate == null) return;
            var beforePlan = SnapshotTerrainMaterialPlanPaths();
            _terrainDrivenMapGenerationService.SetManualPlanItem(_currentMapWorkbenchDraft, row.TerrainId, candidate);
            RefreshAfterPlanChange(row.TerrainId, beforePlan, $"Selected primary material for {FormatTerrainValue(row.TerrainId)}: {candidate.Category}/{candidate.FileName}");
        };
        rerollButton.Click += (_, _) =>
        {
            var row = SelectedRow();
            if (row == null) return;
            var beforePlan = SnapshotTerrainMaterialPlanPaths();
            _terrainDrivenMapGenerationService.RerandomizePlanItem(_currentMapWorkbenchDraft, row.TerrainId, _currentMaterialAssets);
            RefreshAfterPlanChange(row.TerrainId, beforePlan, $"Rerolled primary material for {FormatTerrainValue(row.TerrainId)}.");
        };
        autoButton.Click += (_, _) =>
        {
            var row = SelectedRow();
            if (row == null) return;
            var beforePlan = SnapshotTerrainMaterialPlanPaths();
            _terrainDrivenMapGenerationService.ResetPlanItemToAuto(_currentMapWorkbenchDraft, row.TerrainId, _currentMaterialAssets);
            RefreshAfterPlanChange(row.TerrainId, beforePlan, $"Restored auto primary material for {FormatTerrainValue(row.TerrainId)}.");
        };

        ReloadRows();
        dialog.ShowDialog(this);
    }

    private void OpenTerrainStyleAlignedGenerationDialog()
    {
        if (_project == null || _currentMapWorkbenchDraft == null)
        {
            MessageBox.Show(this, "Create or load a map draft first.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        EnsureMapWorkbenchMaterialLibraryIndexed(showMessages: false);
        SyncTerrainVisualDraftFromEditor(redrawChangedOnly: true);

        using var dialog = new Form
        {
            Text = "Terrain Style Generator",
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            Width = 900,
            Height = 560,
            Font = Font
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(10)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        dialog.Controls.Add(layout);

        var info = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true
        };
        layout.Controls.Add(info, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var closeButton = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
        var fullButton = new Button { Text = "Full Redraw", AutoSize = true };
        var changedButton = new Button { Text = "Changed Only", AutoSize = true };
        var rerollButton = new Button { Text = "Reroll", AutoSize = true };
        var sampleButton = new Button { Text = "Sample Map", AutoSize = true };
        buttons.Controls.AddRange(new Control[] { closeButton, fullButton, changedButton, rerollButton, sampleButton });
        layout.Controls.Add(buttons, 0, 1);
        dialog.AcceptButton = closeButton;
        dialog.CancelButton = closeButton;

        void ReloadInfo(CurrentMapStyleProfile? styleProfile = null, TerrainVisualSynthesisDiagnostics? diagnostics = null)
        {
            styleProfile ??= PrepareCurrentMapStyleProfile(writeSamples: false, redrawChangedOnly: _currentMapWorkbenchDraft!.TerrainVisualProfile.RedrawChangedCellsOnly);
            diagnostics ??= _terrainVisualSynthesisService.Analyze(_currentMapWorkbenchDraft!, _currentMaterialAssets, styleProfile);
            info.Text = BuildTerrainStyleDiagnosticsText(styleProfile, diagnostics);
        }

        void Generate(bool redrawChangedOnly, bool writeSamples)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                SyncTerrainVisualDraftFromEditor(redrawChangedOnly);
                var styleProfile = PrepareCurrentMapStyleProfile(writeSamples, redrawChangedOnly);
                var diagnostics = _terrainVisualSynthesisService.Analyze(_currentMapWorkbenchDraft!, _currentMaterialAssets, styleProfile);
                _mapCanvasPreviewRenderer.Clear();
                _currentMapWorkbenchDraft!.BeautifyGeneratedMap = false;
                _mapMakerBeautifyStale = true;
                RenderMapMakerPreview(force: true);
                ReloadInfo(styleProfile, diagnostics);
                _mapViewerInfoBox.Text = BuildMapMakerInfo("Terrain style generation preview refreshed.");
                UpdateMapMakerEditingButtons();
                SetStatus("Terrain style generation refreshed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Terrain style generation failed: " + ex);
                MessageBox.Show(dialog, ex.Message, "Terrain style generation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        sampleButton.Click += (_, _) =>
        {
            try
            {
                SyncTerrainVisualDraftFromEditor(redrawChangedOnly: true);
                var styleProfile = PrepareCurrentMapStyleProfile(writeSamples: true, redrawChangedOnly: true);
                var diagnostics = _terrainVisualSynthesisService.Analyze(_currentMapWorkbenchDraft!, _currentMaterialAssets, styleProfile);
                ReloadInfo(styleProfile, diagnostics);
                SetStatus("Current map style samples refreshed");
            }
            catch (Exception ex)
            {
                MessageBox.Show(dialog, ex.Message, "Sample map failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        rerollButton.Click += (_, _) =>
        {
            _currentMapWorkbenchDraft!.TerrainVisualProfile.Seed = Guid.NewGuid().ToString("N");
            Generate(redrawChangedOnly: true, writeSamples: true);
        };
        changedButton.Click += (_, _) => Generate(redrawChangedOnly: true, writeSamples: true);
        fullButton.Click += (_, _) => Generate(redrawChangedOnly: false, writeSamples: true);

        ReloadInfo();
        dialog.ShowDialog(this);
    }

    private void GenerateTerrainStyleAlignedPreviewFromPage()
    {
        if (_project == null || _currentMapWorkbenchDraft == null)
        {
            MessageBox.Show(this, "Create or load a map draft first.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            SetMapWorkbenchSubPageMode(MapWorkbenchSubPageMode.TerrainGenerate);
            EnsureMapWorkbenchMaterialLibraryIndexed(showMessages: false);
            SyncTerrainVisualDraftFromEditor(redrawChangedOnly: true);
            var styleProfile = PrepareCurrentMapStyleProfile(writeSamples: true, redrawChangedOnly: true);
            var diagnostics = _terrainVisualSynthesisService.Analyze(_currentMapWorkbenchDraft, _currentMaterialAssets, styleProfile);
            _mapCanvasPreviewRenderer.Clear();
            _currentMapWorkbenchDraft.BeautifyGeneratedMap = false;
            _mapMakerBeautifyStale = true;
            UpdateMapMakerBeautifyButtonState();

            var switchedToGeneratedPreview = false;
            if (!_mapMakerTerrainGeneratedViewRadio.Checked)
            {
                _mapMakerTerrainGeneratedViewRadio.Checked = true;
                switchedToGeneratedPreview = true;
            }

            if (!switchedToGeneratedPreview)
            {
                RenderMapMakerPreview(force: true);
            }

            UpdateMapWorkbenchTerrainGenerationInfo(styleProfile, diagnostics);
            _mapViewerInfoBox.Text = BuildMapMakerInfo("Terrain style generation preview refreshed.");
            UpdateMapMakerEditingButtons();
            SetStatus("Terrain style generation refreshed");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Terrain style generation failed: " + ex);
            MessageBox.Show(this, ex.Message, "Terrain style generation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void SyncTerrainVisualDraftFromEditor(bool redrawChangedOnly)
    {
        if (_currentMapWorkbenchDraft == null) return;
        _currentMapWorkbenchDraft.MaterialRoot = _mapWorkbenchSettings.LastMaterialRoot;
        _currentMapWorkbenchDraft.AutoGenerateMapFromTerrain = true;
        _currentMapWorkbenchDraft.GenerationMode = MapWorkbenchGenerationModes.TerrainDrivenVisual;
        _currentMapWorkbenchDraft.TerrainVisualProfile ??= new TerrainVisualProfile();
        _currentMapWorkbenchDraft.TerrainVisualProfile.RedrawChangedCellsOnly = redrawChangedOnly;
        _currentMapWorkbenchDraft.TerrainVisualProfile.EdgeFeatherRadius = Math.Clamp((int)_mapMakerFeatherRadiusInput.Value <= 0 ? 8 : (int)_mapMakerFeatherRadiusInput.Value, 1, 24);
        _currentMapWorkbenchDraft.TerrainVisualProfile.BlendStrength = Math.Clamp((int)_mapMakerBeautifyStrengthInput.Value <= 0 ? 2 : (int)_mapMakerBeautifyStrengthInput.Value, 1, 3);
        if (_terrainEditorCells.Length == _currentMapWorkbenchDraft.CellCount)
        {
            _currentMapWorkbenchDraft.TerrainCells = _terrainEditorCells.ToArray();
        }

        SyncMapWorkbenchOverridesFromLookup();
        if (_currentMapMakerItem != null)
        {
            _currentMapWorkbenchDraft.BoundMapId = GetMapIdForMapResource(_currentMapMakerItem);
            RefreshDraftBaseLayerFromCurrentMap(_currentMapWorkbenchDraft, _currentMapMakerItem);
        }
    }

    private CurrentMapStyleProfile PrepareCurrentMapStyleProfile(bool writeSamples, bool redrawChangedOnly)
    {
        if (_project == null || _currentMapWorkbenchDraft == null)
        {
            throw new InvalidOperationException("Project and map draft are required.");
        }

        var draft = _currentMapWorkbenchDraft;
        draft.GenerationMode = MapWorkbenchGenerationModes.TerrainDrivenVisual;
        draft.TerrainVisualProfile ??= new TerrainVisualProfile();
        draft.TerrainVisualProfile.RedrawChangedCellsOnly = redrawChangedOnly;
        draft.TerrainVisualProfile.UseCurrentMapSamples = true;
        draft.TerrainVisualProfile.AutoExtractCurrentMapSamples = true;
        var mapId = string.IsNullOrWhiteSpace(draft.BoundMapId) ? draft.DraftId : draft.BoundMapId;
        draft.TerrainVisualProfile.StyleSampleRoot = Path.Combine(
            _mapDraftService.GetDraftAssetRoot(_project, draft.DraftId),
            "StyleSamples",
            MakeSafeFileName(mapId));
        return _currentMapStyleProfileService.BuildProfile(draft, draft.TerrainVisualProfile.StyleSampleRoot, writeSamples);
    }

    private static string BuildTerrainStyleDiagnosticsText(CurrentMapStyleProfile styleProfile, TerrainVisualSynthesisDiagnostics diagnostics)
    {
        var terrainLines = styleProfile.Terrains
            .OrderBy(terrain => terrain.TerrainId)
            .Take(32)
            .Select(terrain =>
                $"{terrain.TerrainId:D2} {terrain.TerrainName}: samples={terrain.Samples.Count}, pure={terrain.PureSampleCount}, boundary={terrain.BoundarySampleCount}, avg=({terrain.Stats.AverageR:0},{terrain.Stats.AverageG:0},{terrain.Stats.AverageB:0})");
        var missing = diagnostics.MissingTerrainIds.Count == 0
            ? "none"
            : string.Join(", ", diagnostics.MissingTerrainIds.Select(id => id.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)));
        var notes = diagnostics.Notes.Count == 0 ? string.Empty : "\r\nNotes:\r\n" + string.Join("\r\n", diagnostics.Notes);
        return
            $"Mode: {MapWorkbenchGenerationModes.TerrainDrivenVisual}\r\n" +
            $"Source map: {styleProfile.SourceMapPath}\r\n" +
            $"Private samples: {styleProfile.SampleRoot}\r\n" +
            $"Style samples: {styleProfile.SampleCount}; used={diagnostics.UsedCurrentMapStyle}\r\n" +
            $"Redrawn cells: {diagnostics.RedrawnCellCount}; preserved cells: {diagnostics.PreservedCellCount}\r\n" +
            $"Material matched cells: {diagnostics.MaterialMatchedCellCount}; fallback cells: {diagnostics.FallbackCellCount}; boundary blends: {diagnostics.BoundaryBlendCount}\r\n" +
            $"Regions: {diagnostics.RegionCount}; locked material groups: {diagnostics.RegionLockedMaterialCount}; fallback groups: {diagnostics.FallbackGroupCount}\r\n" +
            $"Expanded redraw cells: {diagnostics.ExpandedRedrawCellCount}; mixed terrain cells: {diagnostics.MixedTerrainCellCount}; boundary mask pixels: {diagnostics.BoundaryMaskPixelCount}\r\n" +
            $"Local color transfer pixels: {diagnostics.LocalColorTransferPixelCount}; missing transition masks: {diagnostics.MissingTransitionMaskCount}\r\n" +
            $"Interior naturalized regions: {diagnostics.NaturalizedRegionCount}; seam pixels: {diagnostics.InteriorSeamBlendPixelCount}; secondary blend pixels: {diagnostics.SecondaryPatchBlendPixelCount}\r\n" +
            $"Tile transforms: {diagnostics.TileTransformCount}; structure transform skips: {diagnostics.StructureTransformSkippedCount}; repeat penalties: {diagnostics.RepeatedPatchPenaltyCount}\r\n" +
            $"Global transition field pixels: {diagnostics.TransitionFieldPixelCount}; multi-terrain junction pixels: {diagnostics.MultiTerrainJunctionPixels}; repeated boundary blends prevented: {diagnostics.RepeatedBoundaryBlendPreventedCount}\r\n" +
            $"Region texture canvases: {diagnostics.RegionTextureCanvasCount}; quilted patches: {diagnostics.QuiltedPatchCount}; overlap rejects: {diagnostics.PatchOverlapRejectedCount}; macro-noise pixels: {diagnostics.MacroNoiseAppliedPixels}\r\n" +
            $"Building overlays: {diagnostics.BuildingOverlayCellCount}; ground redraw under buildings: {diagnostics.BuildingGroundRedrawCellCount}\r\n" +
            $"Object ground footprints: {diagnostics.ObjectGroundFootprintCellCount}; inpaint redraw cells: {diagnostics.ObjectGroundInpaintCellCount}; inferred ground cells: {diagnostics.ObjectGroundInferredCellCount}; fallback ground cells: {diagnostics.ObjectGroundFallbackCellCount}; context samples: {diagnostics.ObjectGroundContextSampleCount}; terrain object cells: {diagnostics.TerrainObjectOverlayCellCount}\r\n" +
            $"Building visual plan cells: {diagnostics.BuildingVisualPlanCellCount}; object contact blend pixels: {diagnostics.ObjectContactBlendPixelCount}\r\n" +
            $"Current-map pure samples: {diagnostics.CurrentMapPureSampleUsedCount}; rejected samples: {diagnostics.CurrentMapSampleRejectedCount}; material fallback regions: {diagnostics.MaterialLibraryFallbackCount}\r\n" +
            $"Alpha repaired objects: {diagnostics.AlphaRepairedObjectCount}; repaired pixels: {diagnostics.AlphaRepairedPixelCount}; black pixels kept: {diagnostics.BlackBackgroundRejectedPixelCount}\r\n" +
            $"Fast pipeline: {diagnostics.FastPipelineEnabled}; total={diagnostics.TotalMs}ms; plan={diagnostics.PlanMs}ms; tile={diagnostics.TileRenderMs}ms; interior={diagnostics.InteriorBlendMs}ms; boundary={diagnostics.BoundaryBlendMs}ms; color={diagnostics.ColorTransferMs}ms\r\n" +
            $"Missing terrain ids: {missing}\r\n\r\n" +
            "Terrain style samples:\r\n" +
            string.Join("\r\n", terrainLines) +
            notes;
    }

    private Dictionary<string, string> SnapshotTerrainMaterialPlanPaths()
    {
        if (_currentMapWorkbenchDraft == null) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return _currentMapWorkbenchDraft.TerrainMaterialPlan
            .GroupBy(item => item.VisualFamilyKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().MaterialRelativePath,
                StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<int> GetChangedTerrainMaterialIndexes(IReadOnlyDictionary<string, string> beforePlan, byte terrainId)
    {
        if (_currentMapWorkbenchDraft == null || _currentMapWorkbenchDraft.TerrainCells.Length != _currentMapWorkbenchDraft.CellCount)
        {
            yield break;
        }

        var familyKey = _terrainDrivenMapGenerationService.GetVisualFamilyKey(terrainId);
        beforePlan.TryGetValue(familyKey, out var beforePath);
        var afterPath = _currentMapWorkbenchDraft.TerrainMaterialPlan
            .LastOrDefault(item => item.VisualFamilyKey.Equals(familyKey, StringComparison.OrdinalIgnoreCase))
            ?.MaterialRelativePath ?? string.Empty;
        if (string.Equals(beforePath, afterPath, StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        for (var index = 0; index < _currentMapWorkbenchDraft.TerrainCells.Length; index++)
        {
            if (_terrainDrivenMapGenerationService.GetVisualFamilyKey(_currentMapWorkbenchDraft.TerrainCells[index])
                .Equals(familyKey, StringComparison.OrdinalIgnoreCase))
            {
                yield return index;
            }
        }
    }

    private void RefreshMapMakerGeneratedMaterialPreview(IReadOnlyCollection<int> affectedIndexes)
    {
        if (_currentMapWorkbenchDraft == null || affectedIndexes.Count == 0) return;
        if (_mapMakerShowTerrainCheckBox.Checked)
        {
            foreach (var index in ExpandIndexesWithNeighbors(affectedIndexes))
            {
                _mapMakerDirtyTerrainPreviewIndexes.Add(index);
            }

            _mapCanvasPreviewRenderer.MarkTerrainDirty(_currentMapWorkbenchDraft, affectedIndexes);
            _mapMakerBeautifyStale = _currentMapWorkbenchDraft.BeautifyGeneratedMap;
            UpdateMapMakerBeautifyButtonState();
            _mapViewerInfoBox.Text = BuildMapMakerInfo("Primary material updated. Terrain layer is visible; map preview will refresh when switching back.");
            return;
        }

        var refreshIndexes = _currentMapWorkbenchDraft.BeautifyGeneratedMap
            ? ExpandIndexesWithNeighbors(affectedIndexes).ToList()
            : affectedIndexes.ToList();
        var dirtyRect = _mapCanvasPreviewRenderer.UpdateTerrainMaterialCells(_currentMapWorkbenchDraft, refreshIndexes);
        foreach (var index in refreshIndexes)
        {
            _mapMakerDirtyTerrainPreviewIndexes.Add(index);
        }

        _mapMakerBeautifyStale = _currentMapWorkbenchDraft.BeautifyGeneratedMap;
        FlushMapMakerDirtyBasePreview(runBeautify: false);
        RefreshMapMakerPreviewTile(dirtyRect);
    }

    private IEnumerable<int> ExpandIndexesWithNeighbors(IEnumerable<int> indexes)
    {
        if (_currentMapWorkbenchDraft == null) yield break;
        var width = _currentMapWorkbenchDraft.GridWidth;
        var height = _currentMapWorkbenchDraft.GridHeight;
        var seen = new HashSet<int>();
        foreach (var index in indexes)
        {
            var x = index % width;
            var y = index / width;
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    var neighborIndex = ny * width + nx;
                    if (seen.Add(neighborIndex))
                    {
                        yield return neighborIndex;
                    }
                }
            }
        }
    }

    private IEnumerable<TerrainMaterialPlanRow> BuildTerrainMaterialPlanRows()
    {
        if (_currentMapWorkbenchDraft == null) yield break;
        var usedTerrainIds = _currentMapWorkbenchDraft.TerrainCells
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        foreach (var terrainId in usedTerrainIds)
        {
            var familyKey = _terrainDrivenMapGenerationService.GetVisualFamilyKey(terrainId);
            var item = _currentMapWorkbenchDraft.TerrainMaterialPlan.FirstOrDefault(plan =>
                plan.VisualFamilyKey.Equals(familyKey, StringComparison.OrdinalIgnoreCase));
            var candidates = _terrainDrivenMapGenerationService.GetCandidateMaterialsForTerrain(terrainId, _currentMaterialAssets);
            yield return new TerrainMaterialPlanRow
            {
                TerrainId = terrainId,
                Terrain = FormatTerrainValue(terrainId),
                VisualFamily = familyKey,
                CurrentMaterial = item == null
                    ? "Not generated"
                    : $"{item.MaterialCategory}/{item.DisplayName}",
                SelectionMode = item?.SelectionMode ?? TerrainMaterialSelectionModes.Auto,
                CandidateCount = candidates.Count
            };
        }
    }

    private MaterialAsset? SelectTerrainMaterialCandidate(byte terrainId)
    {
        var candidates = _terrainDrivenMapGenerationService
            .GetCandidateMaterialsForTerrain(terrainId, _currentMaterialAssets)
            .Select(asset => new TerrainMaterialCandidateRow { Asset = asset })
            .ToList();
        if (candidates.Count == 0)
        {
            MessageBox.Show(this, $"No candidate materials found for {FormatTerrainValue(terrainId)}.", "No candidates", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        using var dialog = new Form
        {
            Text = $"Pick Primary Material - {FormatTerrainValue(terrainId)}",
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            Width = 760,
            Height = 460,
            Font = Font
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(10)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        dialog.Controls.Add(layout);

        var list = new ListBox
        {
            Dock = DockStyle.Fill,
            DataSource = candidates,
            DisplayMember = nameof(TerrainMaterialCandidateRow.DisplayText)
        };
        layout.Controls.Add(list, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft
        };
        var okButton = new Button { Text = "纭畾", AutoSize = true, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "鍙栨秷", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange(new Control[] { okButton, cancelButton });
        layout.Controls.Add(buttons, 0, 1);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        return dialog.ShowDialog(this) == DialogResult.OK && list.SelectedItem is TerrainMaterialCandidateRow row
            ? row.Asset
            : null;
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
        UpdateMapMakerCellPreview(x, y, terrain);
        _mapViewerInfoBox.Text = BuildMapMakerInfo($"当前格子 ({x},{y})：地形={terrain}。", x, y);
    }

    private void UpdateMapMakerEditingButtons()
    {
        var hasDraft = _currentMapWorkbenchDraft != null;
        var hasBoundMap = hasDraft && _currentMapMakerItem != null;
        var canPublishMap = CanPublishCurrentMapWorkbenchMap(out _);
        var terrainGenerateMode = IsMapWorkbenchTerrainGenerateMode;
        if (!hasDraft && _mapMakerEditTerrainCheckBox.Checked)
        {
            _mapMakerEditTerrainCheckBox.Checked = false;
        }
        _mapMakerBeautifyFilterCombo.Visible = !terrainGenerateMode;
        _mapMakerBeautifyCheckBox.Visible = !terrainGenerateMode;
        _mapMakerRollbackBeautifyButton.Visible = !terrainGenerateMode;
        _mapMakerTerrainStyleButton.Visible = terrainGenerateMode;
        _mapMakerSaveDraftButton.Enabled = hasDraft;
        _mapMakerEditTerrainCheckBox.Enabled = hasDraft;
        _mapMakerBeautifyCheckBox.Enabled = hasDraft && !terrainGenerateMode;
        _mapMakerSaveTerrainButton.Enabled = hasDraft && terrainGenerateMode;
        _mapMakerUndoTerrainButton.Enabled = hasDraft && (_mapMakerMapUndoStack.Count > 0 || _mapMakerTerrainUndoStack.Count > 0);
        _mapMakerRedoTerrainButton.Enabled = hasDraft && (_mapMakerMapRedoStack.Count > 0 || _mapMakerTerrainRedoStack.Count > 0);
        _mapMakerMaterialPlanButton.Enabled = hasDraft && !terrainGenerateMode;
        _mapMakerTerrainStyleButton.Enabled = hasDraft && terrainGenerateMode;
        _mapMakerReplaceMapImageButton.Enabled = hasBoundMap;
        _mapMakerExportPreviewButton.Enabled = _mapViewerBox.Image != null;
        _mapMakerExportJpgButton.Enabled = hasDraft;
        _mapMakerExtractMaterialButton.Enabled = hasDraft && !terrainGenerateMode && !_mapMakerSelectedCellRange.IsEmpty;
        _mapMakerPublishAllButton.Enabled = canPublishMap;
        _mapMakerPublishMapButton.Enabled = canPublishMap;
        _mapMakerPublishTerrainButton.Enabled = hasBoundMap;
        _mapViewerBox.Cursor = hasDraft
            ? (!terrainGenerateMode && _mapWorkbenchBrushMode == MapWorkbenchBrushMode.SceneryBrush ? Cursors.SizeAll : Cursors.Cross)
            : Cursors.Default;
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
            return "Map workbench: create or load a draft first.";
        }

        if (_mapMakerPainting)
        {
            var paintingCellText = cellX.HasValue && cellY.HasValue
                ? $"    cell=({cellX},{cellY})"
                : string.Empty;
            return $"{actionText}\r\nPainting terrain. changed={CountMapWorkbenchTerrainChangedCells()}{paintingCellText}";
        }

        var mapId = _currentMapWorkbenchDraft.BoundMapId;
        var boundText = _currentMapMakerItem == null
            ? "unbound"
            : $"{_currentMapMakerItem.Name} ({_currentMapMakerItem.GridWidth}x{_currentMapMakerItem.GridHeight})";
        var imageSize = $"{_currentMapWorkbenchDraft.PixelWidth}x{_currentMapWorkbenchDraft.PixelHeight}";
        var gridSizeText = $"{_currentMapWorkbenchDraft.GridWidth}x{_currentMapWorkbenchDraft.GridHeight} (48x48/cell)";
        var terrainText = BuildMapWorkbenchPublishReasonText();
        var viewText = _mapMakerShowTerrainCheckBox.Checked
            ? "terrain layer"
            : _currentMapWorkbenchDraft.BeautifyGeneratedMap ? "beautified map" : "base generated map";
        var generationText = BuildTerrainGenerationDiagnosticsText();
        var renderText = BuildMapMakerRenderDiagnosticsText();
        var baseLayerText = BuildMapMakerBaseLayerDiagnosticsText();
        var legacyOverlayCount = _currentMapWorkbenchDraft.MapCellOverrides.Count + _currentMapWorkbenchDraft.BuildingOverlayCells.Count;
        var legacyOverlayText = legacyOverlayCount > 0
            ? $"\r\nNote: {legacyOverlayCount} legacy overlay cells are still composed."
            : string.Empty;

        var cellText = string.Empty;
        if (cellX.HasValue && cellY.HasValue)
        {
            var index = cellY.Value * _currentMapWorkbenchDraft.GridWidth + cellX.Value;
            if (index >= 0 && index < _terrainEditorCells.Length)
            {
                cellText = $"\r\nCurrent cell: ({cellX},{cellY}) = {FormatTerrainValue(_terrainEditorCells[index])}";
            }
        }

        return
            $"{actionText}\r\n" +
            $"Draft={_currentMapWorkbenchDraft.DraftId}    Bound={boundText}    MapId={mapId}    Image={imageSize}    Grid={gridSizeText}    Zoom={_mapZoomTrackBar.Value}%\r\n" +
            $"View={viewText}    Brush={FormatTerrainValue((byte)_mapMakerTerrainBrushInput.Value)}    TerrainChanged={CountMapWorkbenchTerrainChangedCells()}    Undo={_mapMakerMapUndoStack.Count + _mapMakerTerrainUndoStack.Count}    Redo={_mapMakerMapRedoStack.Count + _mapMakerTerrainRedoStack.Count}{cellText}\r\n" +
            $"Base: {baseLayerText}\r\n" +
            $"Cache: {renderText}\r\n" +
            $"Generation: {generationText}\r\n" +
            $"Publish: {terrainText}{legacyOverlayText}";
    }

    private string BuildMapMakerBaseLayerDiagnosticsText()
    {
        if (_currentMapWorkbenchDraft == null)
        {
            return "no draft";
        }

        var path = _currentMapWorkbenchDraft.BaseLayerPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return "未绑定底图；当前会显示棋盘草稿背景";
        }

        if (!File.Exists(path))
        {
            return "未找到当前地图底图：" + path;
        }

        var source = "草稿底图";
        if (_currentMapMakerItem != null && _currentMapMakerItem.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
        {
            source = "真实 Map 文件";
        }
        else if (!string.IsNullOrWhiteSpace(_currentMapWorkbenchDraft.BoundMapId))
        {
            var mapItem = FindMapResourceByMapId(_currentMapWorkbenchDraft.BoundMapId);
            if (mapItem != null && mapItem.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
            {
                source = "真实 Map 文件";
            }
        }

        return $"{source}; {path}";
    }

    private string BuildMapMakerRenderDiagnosticsText()
    {
        var preview = _mapCanvasPreviewRenderer.GetDiagnostics();
        return $"base={_mapMakerLastBaseRefreshMs}ms beautify={_mapMakerLastBeautifyMs}ms dirty={preview.DirtyCellCount + _mapMakerDirtyTerrainPreviewIndexes.Count} hit={_mapMakerLastMaterialHitPercent}%";
    }

    private string BuildTerrainGenerationDiagnosticsText()
    {
        if (_currentMapWorkbenchDraft == null)
        {
            return "no draft";
        }

        if (_mapMakerShowTerrainCheckBox.Checked)
        {
            return "showing terrain id layer";
        }

        var diagnostics = _terrainDrivenMapGenerationService.Analyze(_currentMapWorkbenchDraft, _currentMaterialAssets);
        _mapMakerLastMaterialHitPercent = _currentMapWorkbenchDraft.CellCount <= 0
            ? 0
            : (int)Math.Round(diagnostics.MatchedCellCount * 100.0 / _currentMapWorkbenchDraft.CellCount);
        var rootText = string.IsNullOrWhiteSpace(_mapWorkbenchSettings.LastMaterialRoot)
            ? "no material root"
            : Path.GetFileName(_mapWorkbenchSettings.LastMaterialRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!diagnostics.CanGenerate)
        {
            return $"cannot generate; root={rootText}; materials={diagnostics.MaterialCount}";
        }

        var missingText = diagnostics.MissingTerrainIds.Count == 0
            ? "none"
            : string.Join("/", diagnostics.MissingTerrainIds.Take(12).Select(id => $"{id}/0x{HexDisplayFormatter.FormatByte(id)}"));
        var allFallback = diagnostics.MatchedCellCount == 0 && _currentMapWorkbenchDraft.CellCount > 0;
        var warning = allFallback ? "; warning: all terrain cells use color fallback" : string.Empty;
        var planText = BuildTerrainMaterialPlanDiagnosticsText();
        return $"root={rootText}; materials={diagnostics.MaterialCount}; ids={diagnostics.TerrainAssetIdCount}; matched={diagnostics.MatchedCellCount}; fallback={diagnostics.FallbackCellCount}; missing={missingText}; plan={planText}{warning}";
    }

    private string BuildTerrainMaterialPlanDiagnosticsText()
    {
        if (_currentMapWorkbenchDraft == null || _currentMapWorkbenchDraft.TerrainMaterialPlan.Count == 0)
        {
            return "not generated";
        }

        var manual = _currentMapWorkbenchDraft.TerrainMaterialPlan.Count(item =>
            item.SelectionMode.Equals(TerrainMaterialSelectionModes.Manual, StringComparison.OrdinalIgnoreCase));
        var missingManual = _currentMapWorkbenchDraft.TerrainMaterialPlan.Count(item =>
            item.SelectionMode.Equals(TerrainMaterialSelectionModes.MissingManual, StringComparison.OrdinalIgnoreCase));
        var recovered = _currentMapWorkbenchDraft.TerrainMaterialPlan.Count(item =>
            item.SelectionMode.Equals(TerrainMaterialSelectionModes.AutoRecovered, StringComparison.OrdinalIgnoreCase));
        var text = $"{_currentMapWorkbenchDraft.TerrainMaterialPlan.Count} items";
        if (manual > 0) text += $", manual={manual}";
        if (recovered > 0) text += $", recovered={recovered}";
        if (missingManual > 0) text += $", missingManual={missingManual}";
        return text;
    }

    private string BuildMapWorkbenchPublishReasonText()
    {
        var mapOk = CanPublishCurrentMapWorkbenchMap(out var mapReason);
        var terrainText = _currentMapMakerItem == null
            ? "禁用：未绑定已有 Mxxx 槽位"
            : _currentHexzmapProbe == null
                ? "发布时自动校验 Hexzmap.e5"
                : CanPublishCurrentMapWorkbenchTerrain(out var terrainReason)
                    ? "可用"
                    : "禁用：" + terrainReason;
        return $"一键发布={(mapOk ? "可用" : "禁用：" + mapReason)}；地形层={terrainText}";
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
            !block.CanEdit)
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
        if (_mapViewerBox.Image == null || _currentMapWorkbenchDraft == null)
        {
            MessageBox.Show(this, "请先新建或载入地图草稿并生成预览。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            FileName = MakeSafeFileName($"{(_currentMapMakerItem == null ? _currentMapWorkbenchDraft.DraftId : Path.GetFileNameWithoutExtension(_currentMapMakerItem.Name))}_地图制作预览.png"),
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

        EnsureMapWorkbenchMaterialLibraryIndexed(showMessages: false);
        SyncMapWorkbenchDraftFromEditor();
        EnsureCurrentTerrainMaterialPlan(persist: true);
        ForceBeautifiedGeneratedMapForOutput();
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
            _mapCanvasPublishService.ExportJpeg(_currentMapWorkbenchDraft, _currentMaterialAssets, dialog.FileName);
            using var verify = Image.FromFile(dialog.FileName);
            var expectedWidth = _currentMapWorkbenchDraft.GridWidth * MapResourceItem.MapTilePixelSize;
            var expectedHeight = _currentMapWorkbenchDraft.GridHeight * MapResourceItem.MapTilePixelSize;
            if (verify.Width != expectedWidth || verify.Height != expectedHeight)
            {
                throw new InvalidOperationException($"导出 JPG 尺寸校验失败：实际 {verify.Width}x{verify.Height}，期望 {expectedWidth}x{expectedHeight}。");
            }

            System.Diagnostics.Debug.WriteLine("已导出地图工作台 JPG：" + dialog.FileName);
            RenderMapMakerPreview(force: true);
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

        EnsureMapWorkbenchMaterialLibraryIndexed(showMessages: false);
        SyncMapWorkbenchDraftFromEditor();
        EnsureCurrentTerrainMaterialPlan(persist: true);
        ForceBeautifiedGeneratedMapForOutput();
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
            var result = _mapCanvasPublishService.PublishToMapImage(_project, _currentMapWorkbenchDraft, _currentMapMakerItem, _currentMaterialAssets);
            _mapDraftService.SaveDraft(_project, _currentMapWorkbenchDraft);
            _mapWorkbenchSettings.LastDraftId = _currentMapWorkbenchDraft.DraftId;
            PersistCurrentTerrainMaterialPlan();
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
        EnsureCurrentTerrainMaterialPlan(persist: true);
        DeriveCurrentMapWorkbenchTerrain();
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
            _currentMapWorkbenchDraft.OriginalTerrainCells = reread.ToArray();
            _currentMapWorkbenchDraft.TerrainCells = reread.ToArray();
            _mapDraftService.SaveDraft(_project, _currentMapWorkbenchDraft);
            PersistCurrentTerrainMaterialPlan();
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

    private void PublishCurrentMapWorkbenchMapAndTerrain()
    {
        if (_project == null || _currentMapWorkbenchDraft == null || _currentMapMakerItem == null)
        {
            MessageBox.Show(this, "请先绑定已有 Mxxx 地图槽位。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!CanPublishCurrentMapWorkbenchMap(out var mapReason))
        {
            MessageBox.Show(this, mapReason, "禁止一键发布", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!CanPublishCurrentMapWorkbenchTerrain(out var terrainReason))
        {
            MessageBox.Show(this, terrainReason, "禁止一键发布", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        EnsureMapWorkbenchMaterialLibraryIndexed(showMessages: false);
        SyncMapWorkbenchDraftFromEditor();
        EnsureCurrentTerrainMaterialPlan(persist: true);
        ForceBeautifiedGeneratedMapForOutput();
        DeriveCurrentMapWorkbenchTerrain();
        var currentCells = _hexzmapProbeReader.GetBlockCells(_currentHexzmapProbe, block);
        var changedTerrain = CountChangedBytes(currentCells, _currentMapWorkbenchDraft.TerrainCells);
        if (MessageBox.Show(this,
                $"即将一键发布 Map\\{_currentMapMakerItem.Name} 和 Hexzmap.e5 {block.MapId}。\r\n地形改动格：{changedTerrain}\r\n会分别生成底图备份、地形备份和结构化报告。是否继续？",
                "确认一键发布地图与地形",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var selectedName = _currentMapMakerItem.Name;
            var mapResult = _mapCanvasPublishService.PublishToMapImage(_project, _currentMapWorkbenchDraft, _currentMapMakerItem, _currentMaterialAssets);
            HexzmapSaveResult? terrainResult = null;
            if (changedTerrain > 0)
            {
                terrainResult = _hexzmapEditorService.SaveBlock(_project, _currentHexzmapProbe, block, _currentMapWorkbenchDraft.TerrainCells, _terrainEditorTerrainLookup);
                _currentHexzmapProbe = _hexzmapProbeReader.Read(_project, _terrainEditorTerrainLookup);
                var refreshedBlock = _currentHexzmapProbe.Blocks.First(x => x.Index == block.Index);
                var reread = _hexzmapProbeReader.GetBlockCells(_currentHexzmapProbe, refreshedBlock);
                if (!reread.SequenceEqual(_currentMapWorkbenchDraft.TerrainCells))
                {
                    throw new InvalidOperationException("一键发布后 Hexzmap 复读校验失败。");
                }

                _terrainEditorBlock = refreshedBlock;
                _terrainEditorOriginalCells = reread.ToArray();
                _terrainEditorCells = reread.ToArray();
                _mapMakerOriginalTerrainCells = reread.ToArray();
                _currentMapWorkbenchDraft.OriginalTerrainCells = reread.ToArray();
                _currentMapWorkbenchDraft.TerrainCells = reread.ToArray();
            }

            _mapDraftService.SaveDraft(_project, _currentMapWorkbenchDraft);
            _mapWorkbenchSettings.LastDraftId = _currentMapWorkbenchDraft.DraftId;
            PersistCurrentTerrainMaterialPlan();
            SaveMapWorkbenchSettings();
            LoadMapImages();
            SelectMapImageByName(selectedName);
            RenderMapMakerPreview(force: true);

            var terrainText = terrainResult == null
                ? "地形层与 Hexzmap 已一致，未写入。"
                : $"地形报告：{terrainResult.ReportJsonPath}\r\n地形备份：{terrainResult.BackupPath}";
            MessageBox.Show(this,
                $"一键发布完成。\r\n底图报告：{mapResult.ReportJsonPath}\r\n底图备份：{mapResult.BackupPath}\r\n{terrainText}",
                "一键发布完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("一键发布地图与地形失败：" + ex);
            MessageBox.Show(this, ex.Message, "一键发布失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            _mapViewerInfoBox.Text = $"Map 目录地图图片：{maps.Count} 张。选择左侧地图后显示真实底图；在右侧素材库选择素材即可绘制，点击“美化当前地图”生成美化预览。";
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
            EnsureMapWorkbenchMaterialLibraryIndexed(showMessages: false);
            _currentMapMakerItem = item;
            ClearMapMakerPreviewImages();
            _currentMapWorkbenchDraft = _mapDraftService.CreateDraftFromMap(_project, item, _mapWorkbenchSettings.LastMaterialRoot);
            RefreshDraftBaseLayerFromCurrentMap(_currentMapWorkbenchDraft, item);
            _currentMapWorkbenchDraft.AutoGenerateMapFromTerrain = true;
            _currentMapWorkbenchDraft.BeautifyGeneratedMap = false;
            InheritPersistedTerrainMaterialPlan(_currentMapWorkbenchDraft);
            _terrainEditorBlock = null;
            var terrainLoadText = "Loaded map slots and terrain layer.";
            var block = TryGetMatchingHexzmapBlockForMap(item);
            if (block != null && _currentHexzmapProbe != null)
            {
                var cells = _hexzmapProbeReader.GetBlockCells(_currentHexzmapProbe, block);
                if (cells.Length == _currentMapWorkbenchDraft.CellCount)
                {
                    _currentMapWorkbenchDraft.OriginalTerrainCells = cells.ToArray();
                    _currentMapWorkbenchDraft.TerrainCells = cells.ToArray();
                    _terrainEditorBlock = block;
                }
                else
                {
                    terrainLoadText = $"Loaded map slots, but Hexzmap terrain block length {cells.Length} does not match map cell count {_currentMapWorkbenchDraft.CellCount}; using terrain 0 preview.";
                }
            }
            else
            {
                terrainLoadText = "Loaded map slots, but no matching Hexzmap terrain block was found; using terrain 0 preview.";
            }

            BindMapWorkbenchDraftToEditor(resetHistory: true);
            _mapViewerInfoBox.Text = BuildMapMakerInfo(terrainLoadText);
            FitMapToView();
            SetStatus($"地图制作：{item.Name}");
        }
        catch (Exception ex)
        {
            _currentMapMakerItem = null;
            _currentMapWorkbenchDraft = null;
            ClearMapMakerPreviewImages();
            _mapViewerInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("Map image load failed: " + ex);
            SetStatus("Map preview load failed: " + ex.Message);
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
