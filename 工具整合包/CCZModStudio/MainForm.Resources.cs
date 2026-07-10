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
    private void LoadSceneDictionary() => LoadSceneDictionary(showMessages: true);

    private bool LoadSceneDictionary(bool showMessages)
    {
        var workspace = _project?.WorkspaceRoot ?? Directory.GetCurrentDirectory();
        var path = _project == null ? ProjectDetector.FindSceneDictionaryPath(workspace) : ProjectDetector.FindSceneDictionaryPath(_project);
        if (!File.Exists(path))
        {
            var message = "找不到 CczString.ini：" + path + "\r\n\r\n请把 CczString.ini 放到当前项目的 a新剧本编辑器v0.23 目录，或确认工具内置 LegacyResources 备份未被删除。";
            _sceneDictionaryInfoBox.Text = message;
            System.Diagnostics.Debug.WriteLine("剧本字典未找到：" + path);
            if (showMessages)
            {
                MessageBox.Show(this, message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("剧本字典未找到");
            }

            return false;
        }

        try
        {
            _currentSceneStringDocument = _sceneStringParser.Parse(path);
            _sceneCommandGrid.DataSource = new BindingList<SceneCommandDefinition>(_currentSceneStringDocument.Commands.ToList());
            _sceneGroupGrid.DataSource = new BindingList<SceneStringGroup>(_currentSceneStringDocument.Groups.ToList());
            _sceneDictionaryInfoBox.Text =
                $"来源：{_currentSceneStringDocument.SourcePath}\r\n" +
                $"命令字典：{_currentSceneStringDocument.Commands.Count} 项    附加字符串组：{_currentSceneStringDocument.Groups.Count} 组\r\n" +
                $"解码：{_currentSceneStringDocument.DecodeDiagnostic}\r\n" +
                "当前阶段用于剧本模块的命令 ID->名称映射和参数候选表预览，尚未开放 Scene 二进制结构写入。";
            PopulateScriptNewCommandCombo(_currentSceneStringDocument);
            _probeScenarioCommandsButton.Enabled = _currentScenarioFiles.Count > 0;
            _buildScenarioStructureButton.Enabled = _currentScenarioFiles.Count > 0;
            if (showMessages)
            {
                LoadScenarioCommandTemplates(silent: true);
            }

            System.Diagnostics.Debug.WriteLine($"已读取剧本字典：命令 {_currentSceneStringDocument.Commands.Count} 项，字符串组 {_currentSceneStringDocument.Groups.Count} 组。");
            if (showMessages)
            {
                SetStatus("剧本字典读取完成");
            }

            return true;
        }
        catch (Exception ex)
        {
            _sceneDictionaryInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("剧本字典读取失败：" + ex);
            if (showMessages)
            {
                MessageBox.Show(this, ex.Message, "剧本字典读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return false;
        }
    }

    private void IndexMaterialLibrary() => IndexMaterialLibrary(showMessages: true);

    private bool IndexMaterialLibrary(bool showMessages)
    {
        using var perf = TracePerf("IndexMaterialLibrary");
        var workspace = _project?.WorkspaceRoot ?? Directory.GetCurrentDirectory();
        try
        {
            var resolvedRoot = _project == null
                ? MaterialLibraryIndexer.ResolveMaterialLibraryRoot(workspace)
                : MaterialLibraryIndexer.ResolveMaterialLibraryRoot(_project);
            var materialRoot = resolvedRoot
                ?? Path.Combine(workspace, "普罗-综合工具v0.3", "素材库");
            _currentMaterialAssets = string.IsNullOrWhiteSpace(resolvedRoot)
                ? Array.Empty<MaterialAsset>()
                : _materialLibraryCache.GetOrIndexExplicitRoot(resolvedRoot);
            _currentMaterialRoot = resolvedRoot ?? string.Empty;
            _materialGrid.DataSource = new BindingList<MaterialAsset>(_currentMaterialAssets.ToList());
            ConfigureMaterialGrid();
            var categorySummary = string.Join("，", _currentMaterialAssets
                .GroupBy(x => x.Category)
                .Select(g => $"{g.Key}:{g.Count()}"));
            _materialInfoBox.Text =
                $"素材根目录：{materialRoot}\r\n" +
                $"素材数量：{_currentMaterialAssets.Count}    分类：{categorySummary}\r\n" +
                "当前阶段提供素材索引、hex 标记/说明和图片预览；地图写入和素材导入需等待地图格式验证。";
            System.Diagnostics.Debug.WriteLine($"已索引素材库：{_currentMaterialAssets.Count} 个图片素材。");
            if (resolvedRoot == null)
            {
                System.Diagnostics.Debug.WriteLine("素材库目录未找到，当前素材索引为空：" + materialRoot);
            }

            if (showMessages)
            {
                SetStatus("素材库索引完成");
            }

            return true;
        }
        catch (Exception ex)
        {
            _materialInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("素材库索引失败：" + ex);
            if (showMessages)
            {
                MessageBox.Show(this, ex.Message, "素材库索引失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return false;
        }
    }

    private void ConfigureMaterialGrid()
    {
        if (_materialGrid.Columns.Count == 0) return;
        HideNonAuthoringColumns(
            _materialGrid,
            nameof(MaterialAsset.Description),
            nameof(MaterialAsset.FilePath));
    }

    private void ShowSelectedMaterialPreview()
    {
        if (_materialGrid.SelectedRows.Count == 0) return;
        if (_materialGrid.SelectedRows[0].DataBoundItem is not MaterialAsset asset) return;

        try
        {
            using var source = Image.FromFile(asset.FilePath);
            SetPictureBoxImage(_materialPreview, new Bitmap(source));
            SetStatus($"素材预览：{asset.Category}/{asset.FileName}");
        }
        catch (Exception ex)
        {
            SetPictureBoxImage(_materialPreview, null);
            System.Diagnostics.Debug.WriteLine($"素材预览失败：{asset.FilePath} {ex.Message}");
        }
    }

    private void IndexGameResources()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            _currentMapResources = _mapResourceIndexer.Index(_project);
            PopulateGameResourceCategoryFilter();
            BindGameResourceRows(_currentMapResources);
            var summary = string.Join("，", _currentMapResources
                .GroupBy(x => x.Category)
                .OrderBy(g => g.Key)
                .Select(g => $"{g.Key}:{g.Count()}"));
            _gameResourceInfoBox.Text =
                $"游戏资源根目录：{_project.GameRoot}\r\n" +
                $"索引项：{_currentMapResources.Count}    分类：{summary}\r\n" +
                "当前阶段可索引、预览、定位、导出资源；已索引资源支持整文件替换预览、自动备份和从备份还原。EEX/E5S/E5 的内部重封包仍需后续格式验证。";
            var maps = _currentMapResources
                .Where(x => x.Category == "地图图片")
                .OrderBy(x => x.Id)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            _mapImageList.DisplayMember = nameof(MapResourceItem.Name);
            _mapImageList.DataSource = new BindingList<MapResourceItem>(maps);
            System.Diagnostics.Debug.WriteLine($"已索引游戏资源：{_currentMapResources.Count} 项。");
            SetStatus("地图图片索引完成");
        }
        catch (Exception ex)
        {
            _gameResourceInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("地图图片索引失败：" + ex);
            MessageBox.Show(this, ex.Message, "地图图片索引失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void BindGameResourceRows(IEnumerable<MapResourceItem> rows)
    {
        _gameResourceGrid.DataSource = new BindingList<MapResourceItem>(rows.ToList());
        ConfigureGameResourceGrid();
    }

    private void ConfigureGameResourceGrid()
    {
        if (_gameResourceGrid.Columns.Count == 0) return;
        HideNonAuthoringColumns(
            _gameResourceGrid,
            nameof(MapResourceItem.Annotation),
            nameof(MapResourceItem.Path));
    }

    private void PopulateGameResourceCategoryFilter()
    {
        var previous = Convert.ToString(_gameResourceCategoryFilterCombo.SelectedItem, CultureInfo.InvariantCulture);
        _gameResourceCategoryFilterCombo.Items.Clear();
        _gameResourceCategoryFilterCombo.Items.Add("\u5168\u90e8");
        foreach (var category in _currentMapResources.Select(x => x.Category).Distinct().OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            _gameResourceCategoryFilterCombo.Items.Add(category);
        }

        var selectedIndex = 0;
        if (!string.IsNullOrWhiteSpace(previous))
        {
            for (var i = 0; i < _gameResourceCategoryFilterCombo.Items.Count; i++)
            {
                if (string.Equals(Convert.ToString(_gameResourceCategoryFilterCombo.Items[i], CultureInfo.InvariantCulture), previous, StringComparison.Ordinal))
                {
                    selectedIndex = i;
                    break;
                }
            }
        }
        _gameResourceCategoryFilterCombo.SelectedIndex = selectedIndex;
    }

    private void ApplyGameResourceFilter()
    {
        if (_currentMapResources.Count == 0) return;
        var category = Convert.ToString(_gameResourceCategoryFilterCombo.SelectedItem, CultureInfo.InvariantCulture) ?? "\u5168\u90e8";
        var keyword = _gameResourceSearchBox.Text.Trim();
        var filtered = _currentMapResources.Where(item =>
            (category == "\u5168\u90e8" || string.Equals(item.Category, category, StringComparison.Ordinal)) &&
            (string.IsNullOrWhiteSpace(keyword) || GameResourceMatchesKeyword(item, keyword)))
            .ToList();
        BindGameResourceRows(filtered);
        UpdateGameResourceInfo(filtered.Count, category, keyword);
        SetStatus($"\u6e38\u620f\u8d44\u6e90\u7b5b\u9009\uff1a{filtered.Count}/{_currentMapResources.Count}");
    }

    private void ClearGameResourceFilter()
    {
        _gameResourceSearchBox.Clear();
        if (_gameResourceCategoryFilterCombo.Items.Count > 0) _gameResourceCategoryFilterCombo.SelectedIndex = 0;
        BindGameResourceRows(_currentMapResources);
        UpdateGameResourceInfo(_currentMapResources.Count, "\u5168\u90e8", string.Empty);
        SetStatus("\u5df2\u663e\u793a\u5168\u90e8\u6e38\u620f\u8d44\u6e90");
    }

    private static bool GameResourceMatchesKeyword(MapResourceItem item, string keyword)
    {
        return ContainsKeyword(item.Category, keyword) ||
               ContainsKeyword(item.Id, keyword) ||
               ContainsKeyword(item.Name, keyword) ||
               ContainsKeyword(item.Extension, keyword) ||
               ContainsKeyword(item.Magic, keyword) ||
               ContainsKeyword(item.FormatHint, keyword) ||
               ContainsKeyword(item.Annotation, keyword) ||
               ContainsKeyword(item.Path, keyword);
    }

    private static bool ContainsKeyword(string value, string keyword) =>
        value.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);

    private void UpdateGameResourceInfo(int visibleCount, string category, string keyword)
    {
        if (_project == null) return;
        var summary = string.Join("\uff0c", _currentMapResources
            .GroupBy(x => x.Category)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key}:{g.Count()}"));
        var filterText = category == "\u5168\u90e8" && string.IsNullOrWhiteSpace(keyword)
            ? "\u672a\u7b5b\u9009"
            : $"\u5206\u7c7b={category}\uff0c\u5173\u952e\u5b57={keyword}";
        _gameResourceInfoBox.Text =
            $"\u6e38\u620f\u8d44\u6e90\u6839\u76ee\u5f55\uff1a{_project.GameRoot}\r\n" +
            $"\u7d22\u5f15\u9879\uff1a{_currentMapResources.Count}    \u5f53\u524d\u663e\u793a\uff1a{visibleCount}    \u5206\u7c7b\uff1a{summary}\r\n" +
            $"\u7b5b\u9009\uff1a{filterText}\r\n" +
            "当前阶段可索引、预览、定位、导出资源；已索引资源支持整文件替换预览、自动备份和从备份还原。EEX/E5S/E5 的内部解码、扩容和重封包仍需后续格式验证。";
    }

    private MapResourceItem? GetSelectedGameResourceItem()
    {
        if (_gameResourceGrid.SelectedRows.Count > 0 && _gameResourceGrid.SelectedRows[0].DataBoundItem is MapResourceItem selectedItem) return selectedItem;
        if (_gameResourceGrid.CurrentRow?.DataBoundItem is MapResourceItem currentItem) return currentItem;
        return null;
    }

    private void RefreshResourceCachesAfterFileChange(MapResourceItem changedItem)
    {
        if (changedItem.Name.Equals("Hexzmap.e5", StringComparison.OrdinalIgnoreCase))
        {
            _currentHexzmapProbe = null;
        }

        if (changedItem.Path.Contains("RS" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            changedItem.Path.Contains("RS/", StringComparison.OrdinalIgnoreCase))
        {
            _currentScenarioFiles = Array.Empty<ScenarioFileInfo>();
        }
    }

    private void OpenSelectedGameResourceLocation()
    {
        var item = GetSelectedGameResourceItem();
        if (item == null)
        {
            MessageBox.Show(this, "\u8bf7\u5148\u5728\u6e38\u620f\u8d44\u6e90\u7d22\u5f15\u9875\u9009\u62e9\u4e00\u4e2a\u6587\u4ef6\u3002", "\u63d0\u793a", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!File.Exists(item.Path) && !Directory.Exists(item.Path))
        {
            MessageBox.Show(this, "\u627e\u4e0d\u5230\u8d44\u6e90\u6587\u4ef6\uff1a" + item.Path, "\u63d0\u793a", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        OpenFileLocation(item.Path);
        SetStatus($"\u5df2\u5b9a\u4f4d\u8d44\u6e90\uff1a{item.Category}/{item.Name}");
    }

    private void ReplaceSelectedGameResourceInTestCopy()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var item = GetSelectedGameResourceItem();
        if (item == null)
        {
            MessageBox.Show(this, "请先在地图图片索引页选择一个文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!File.Exists(item.Path))
        {
            MessageBox.Show(this, "选中资源文件不存在：" + item.Path, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var extension = string.IsNullOrWhiteSpace(item.Extension) ? Path.GetExtension(item.Path) : item.Extension;
        using var dialog = new OpenFileDialog
        {
            Title = "选择替换来源文件",
            Filter = string.IsNullOrWhiteSpace(extension)
                ? "所有文件 (*.*)|*.*"
                : $"同类资源 (*{extension})|*{extension}|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        ResourceReplacePreviewResult preview;
        try
        {
            Cursor = Cursors.WaitCursor;
            preview = _resourceReplaceService.PreviewReplacement(_project, item.Path, dialog.FileName);
            _gameResourceInfoBox.Text = BuildResourceReplacePreviewText(item, preview);
            System.Diagnostics.Debug.WriteLine($"资源替换预览：{preview.TargetRelativePath} <- {preview.ReplacementPath}，改动估算 {preview.ChangedBytesEstimate:N0} 字节");
            SetStatus("资源替换预览完成，请确认");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("资源替换预览失败：" + ex);
            MessageBox.Show(this, ex.Message, "替换预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        var confirm = ShowResourceReplacePreviewDialog("确认替换项目资源", "确认替换", item, preview, BuildResourceReplaceConfirmText(item, preview));
        if (confirm != DialogResult.Yes) return;

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = _resourceReplaceService.Replace(_project, item.Path, dialog.FileName);
            _currentMapResources = _mapResourceIndexer.Index(_project);
            PopulateGameResourceCategoryFilter();
            BindGameResourceRows(_currentMapResources);
            UpdateGameResourceInfo(_currentMapResources.Count, "全部", string.Empty);
            _gameResourceInfoBox.Text =
                $"资源替换完成：{item.Category}/{item.Name}\r\n" +
                $"旧大小：{result.OldSizeBytes:N0} 字节    新大小：{result.NewSizeBytes:N0} 字节    改动估算：{result.ChangedBytesEstimate:N0} 字节\r\n" +
                $"格式检查：{result.FormatCheckSummary}\r\n" +
                $"格式警告：{(result.FormatWarnings.Count == 0 ? "无" : string.Join("；", result.FormatWarnings))}\r\n" +
                $"风险提示：{result.RiskSummary}\r\n" +
                $"备份：{result.BackupPath}\r\n报告：{result.ReportPath}\r\n结构化报告：{result.ReportJsonPath}\r\n" +
                "说明：这是项目资源整文件替换，不会重封包未知格式；替换后请进行格式核对和实机测试。";
            RefreshResourceCachesAfterFileChange(item);
            System.Diagnostics.Debug.WriteLine($"已替换项目资源：{item.Path} <- {dialog.FileName}，备份 {result.BackupPath}，结构化报告 {result.ReportJsonPath}");
            SetStatus("项目资源替换完成");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("替换项目资源失败：" + ex);
            MessageBox.Show(this, ex.Message, "替换失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void RestoreSelectedGameResourceFromBackup()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var item = GetSelectedGameResourceItem();
        if (item == null)
        {
            MessageBox.Show(this, "请先在地图图片索引页选择一个要还原的文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!File.Exists(item.Path))
        {
            MessageBox.Show(this, "选中资源文件不存在：" + item.Path, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var backupRoot = Path.Combine(_project.GameRoot, "_CCZModStudio_Backups");
        if (!Directory.Exists(backupRoot))
        {
            MessageBox.Show(this, "当前项目还没有备份目录：" + backupRoot, "没有可用备份", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var extension = string.IsNullOrWhiteSpace(item.Extension) ? Path.GetExtension(item.Path) : item.Extension;
        using var dialog = new OpenFileDialog
        {
            Title = "选择用于还原的备份文件",
            InitialDirectory = backupRoot,
            Filter = string.IsNullOrWhiteSpace(extension)
                ? "所有备份文件 (*.*)|*.*"
                : $"同扩展名备份 (*{extension})|*{extension}|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        ResourceReplacePreviewResult preview;
        try
        {
            Cursor = Cursors.WaitCursor;
            preview = _resourceReplaceService.PreviewReplacement(_project, item.Path, dialog.FileName);
            _gameResourceInfoBox.Text =
                "资源备份还原预览：将把所选备份整文件写回当前项目资源。\r\n" +
                BuildResourceReplacePreviewText(item, preview);
            System.Diagnostics.Debug.WriteLine($"资源备份还原预览：{preview.TargetRelativePath} <- {preview.ReplacementPath}");
            SetStatus("资源备份还原预览完成，请确认");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("资源备份还原预览失败：" + ex);
            MessageBox.Show(this, ex.Message, "还原预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        var confirm = ShowResourceReplacePreviewDialog(
            "确认从备份还原",
            "确认还原",
            item,
            preview,
            "即将从备份文件还原选中资源。\r\n\r\n" +
            BuildResourceReplaceConfirmText(item, preview) +
            "\r\n\r\n注意：还原前仍会自动备份当前文件，因此可以再次回退。");
        if (confirm != DialogResult.Yes) return;

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = _resourceReplaceService.Replace(_project, item.Path, dialog.FileName);
            _currentMapResources = _mapResourceIndexer.Index(_project);
            PopulateGameResourceCategoryFilter();
            BindGameResourceRows(_currentMapResources);
            UpdateGameResourceInfo(_currentMapResources.Count, "全部", string.Empty);
            _gameResourceInfoBox.Text =
                $"资源备份还原完成：{item.Category}/{item.Name}\r\n" +
                $"旧大小：{result.OldSizeBytes:N0} 字节    还原后大小：{result.NewSizeBytes:N0} 字节    改动估算：{result.ChangedBytesEstimate:N0} 字节\r\n" +
                $"格式检查：{result.FormatCheckSummary}\r\n" +
                $"格式警告：{(result.FormatWarnings.Count == 0 ? "无" : string.Join("；", result.FormatWarnings))}\r\n" +
                $"风险提示：{result.RiskSummary}\r\n" +
                $"还原前当前文件备份：{result.BackupPath}\r\n报告：{result.ReportPath}\r\n结构化报告：{result.ReportJsonPath}\r\n" +
                "说明：已从选定备份整文件还原到当前项目；建议重新进行格式核对和实机测试。";
            RefreshResourceCachesAfterFileChange(item);
            System.Diagnostics.Debug.WriteLine($"已从备份还原项目资源：{item.Path} <- {dialog.FileName}，还原前备份 {result.BackupPath}，结构化报告 {result.ReportJsonPath}");
            SetStatus("项目资源备份还原完成");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("从备份还原资源失败：" + ex);
            MessageBox.Show(this, ex.Message, "还原失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private static string BuildResourceReplacePreviewText(MapResourceItem item, ResourceReplacePreviewResult preview)
    {
        return
            $"资源替换预览：{item.Category}/{item.Name}    ID={item.Id}\r\n" +
            $"目标：{preview.TargetRelativePath}\r\n" +
            $"来源：{preview.ReplacementPath}\r\n" +
            $"大小：目标 {preview.OldSizeBytes:N0} 字节 -> 来源 {preview.NewSizeBytes:N0} 字节（差值 {FormatSignedBytes(preview.SizeDeltaBytes)}）\r\n" +
            $"改动估算：{preview.ChangedBytesEstimate:N0} 字节（约 {preview.ChangedPercent:F2}%）；内容相同：{(preview.IsContentIdentical ? "是" : "否")}\r\n" +
            $"SHA256：目标 {ShortSha256(preview.OldSha256)}    来源 {ShortSha256(preview.NewSha256)}\r\n" +
            $"格式检查：{preview.FormatCheckSummary}\r\n" +
            $"格式警告：{(preview.FormatWarnings.Count == 0 ? "无" : string.Join("；", preview.FormatWarnings))}\r\n" +
            $"风险提示：{preview.RiskSummary}\r\n" +
            "说明：这只是预览，尚未写入。确认后会先备份目标文件，再进行整文件替换。";
    }

    private static string BuildResourceReplaceConfirmText(MapResourceItem item, ResourceReplacePreviewResult preview)
    {
        return
            $"将用来源文件替换项目资源：\r\n\r\n" +
            $"资源：{item.Category}/{item.Name}\r\n" +
            $"目标：{preview.TargetRelativePath}\r\n" +
            $"来源：{preview.ReplacementPath}\r\n\r\n" +
            $"大小变化：{preview.OldSizeBytes:N0} -> {preview.NewSizeBytes:N0} 字节（{FormatSignedBytes(preview.SizeDeltaBytes)}）\r\n" +
            $"改动估算：{preview.ChangedBytesEstimate:N0} 字节，约 {preview.ChangedPercent:F2}%\r\n" +
            $"SHA256：{ShortSha256(preview.OldSha256)} -> {ShortSha256(preview.NewSha256)}\r\n" +
            $"格式检查：{preview.FormatCheckSummary}\r\n" +
            $"格式警告：{(preview.FormatWarnings.Count == 0 ? "无" : string.Join("；", preview.FormatWarnings))}\r\n\r\n" +
            $"风险提示：{preview.RiskSummary}\r\n\r\n" +
            "选择“是”后会先备份目标文件，再写入当前项目。是否继续？";
    }

    private DialogResult ShowResourceReplacePreviewDialog(string title, string confirmText, MapResourceItem item, ResourceReplacePreviewResult preview, string detailText)
    {
        using var dialog = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = true,
            ShowInTaskbar = false
        };
        ApplyAdaptiveDialogSizing(dialog, new Size(1100, 760), new Size(840, 560));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(10)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        dialog.Controls.Add(layout);

        var infoBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
            Text = detailText
        };
        layout.Controls.Add(infoBox, 0, 0);

        var imageSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
        };
        ConfigureSplitContainerDistanceAfterLayout("ResourcePreviewDialog.TargetReplacement", imageSplit, desiredDistance: 520, desiredPanel1Min: 25, desiredPanel2Min: 25);
        layout.Controls.Add(imageSplit, 0, 1);
        AddCollapsibleSplitPanel(
            imageSplit,
            1,
            "当前目标文件",
            BuildResourcePreviewPanel("当前目标文件", preview.TargetPath, $"{item.Category}/{item.Name}", disposeOnClose: dialog),
            "ResourcePreviewDialog.TargetReplacement.Target");
        AddCollapsibleSplitPanel(
            imageSplit,
            2,
            "替换/还原来源文件",
            BuildResourcePreviewPanel("替换/还原来源文件", preview.ReplacementPath, Path.GetFileName(preview.ReplacementPath), disposeOnClose: dialog),
            "ResourcePreviewDialog.TargetReplacement.Replacement");

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 10, 0, 0)
        };
        var confirmButton = new Button
        {
            Text = confirmText,
            DialogResult = DialogResult.Yes,
            AutoSize = true,
            MinimumSize = new Size(120, 34)
        };
        var cancelButton = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            MinimumSize = new Size(100, 34)
        };
        buttonPanel.Controls.Add(confirmButton);
        buttonPanel.Controls.Add(cancelButton);
        layout.Controls.Add(buttonPanel, 0, 2);
        dialog.AcceptButton = confirmButton;
        dialog.CancelButton = cancelButton;

        return dialog.ShowDialog(this);
    }

    private static bool PathContainsDirectorySegment(string path, string segment)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var normalized = path.Replace('/', '\\');
        return normalized.Contains("\\" + segment + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static Control BuildResourcePreviewPanel(string title, string path, string subtitle, Form disposeOnClose)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titleLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Text = title + "： " + subtitle
        };
        panel.Controls.Add(titleLabel, 0, 0);

        var previewBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Black
        };
        panel.Controls.Add(previewBox, 0, 1);

        var infoLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            Padding = new Padding(0, 6, 0, 0)
        };
        panel.Controls.Add(infoLabel, 0, 2);

        if (!IsImageFile(path))
        {
            previewBox.BackColor = SystemColors.ControlDark;
            infoLabel.Text = "非图片资源：当前显示文本预览、格式检查和风险提示；EEX/E5/E5S/WAV/MP3 的内部可视化仍在后续计划中。";
            return panel;
        }

        try
        {
            using var image = Image.FromFile(path);
            SetPictureBoxImage(previewBox, new Bitmap(image));
            infoLabel.Text = $"图片可读：{image.Width}x{image.Height}    文件：{path}";
            disposeOnClose.FormClosed += (_, _) => SetPictureBoxImage(previewBox, null);
        }
        catch (Exception ex)
        {
            previewBox.BackColor = SystemColors.ControlDark;
            infoLabel.Text = "图片预览失败：" + ex.Message + "\r\n文件：" + path;
        }

        return panel;
    }

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatSignedBytes(long value)
    {
        return value switch
        {
            > 0 => "+" + value.ToString("N0", CultureInfo.CurrentCulture) + " 字节",
            < 0 => value.ToString("N0", CultureInfo.CurrentCulture) + " 字节",
            _ => "0 字节"
        };
    }

    private static string ShortSha256(string sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256)) return string.Empty;
        return sha256.Length <= 16 ? sha256 : sha256[..16] + "…";
    }




    private static void SelectContainingTab(Control child)
    {
        for (Control? current = child; current != null; current = current.Parent)
        {
            if (current is TabPage page && page.Parent is TabControl tabs)
            {
                tabs.SelectedTab = page;
                return;
            }
        }
    }


    private static List<T> GetGridItems<T>(DataGridView grid)
    {
        var items = new List<T>();
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.DataBoundItem is T item)
            {
                items.Add(item);
            }
        }
        return items;
    }

    private static DataTable BuildStringDataTable<T>(IEnumerable<T> items, string tableName)
    {
        var table = new DataTable(tableName);
        var properties = typeof(T).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        foreach (var property in properties)
        {
            table.Columns.Add(property.Name, typeof(string));
        }

        foreach (var item in items)
        {
            var values = properties.Select(property => Convert.ToString(property.GetValue(item), CultureInfo.InvariantCulture) ?? string.Empty).ToArray();
            table.Rows.Add(values);
        }

        return table;
    }

    private void ExportGridItemsCsv<T>(DataGridView grid, string title, string defaultFileName, string tableName, string logName)
    {
        var rows = GetGridItems<T>(grid);
        if (rows.Count == 0)
        {
            MessageBox.Show(this, "\u5f53\u524d\u6ca1\u6709\u53ef\u5bfc\u51fa\u7684\u884c\u3002", "\u63d0\u793a", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = "CSV \u6587\u4ef6 (*.csv)|*.csv|\u6240\u6709\u6587\u4ef6 (*.*)|*.*",
            FileName = defaultFileName
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            CsvService.Export(BuildStringDataTable(rows, tableName), dialog.FileName);
            System.Diagnostics.Debug.WriteLine($"\u5df2\u5bfc\u51fa{logName}\uff1a{dialog.FileName}\uff0c\u884c\u6570 {rows.Count}");
            SetStatus($"{logName}\u5bfc\u51fa\u5b8c\u6210\uff1a{rows.Count} \u884c");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"\u5bfc\u51fa{logName}\u5931\u8d25\uff1a" + ex);
            MessageBox.Show(this, ex.Message, "\u5bfc\u51fa\u5931\u8d25", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowSelectedGameResourcePreview()
    {
        if (_gameResourceGrid.SelectedRows.Count == 0) return;
        if (_gameResourceGrid.SelectedRows[0].DataBoundItem is not MapResourceItem item) return;

        var targetKey = $"{item.Category}/{item.Name}";
        _gameResourceInfoBox.Text =
            $"\u9009\u4e2d\u8d44\u6e90\uff1a{item.Category}/{item.Name}    ID={item.Id}    \u5927\u5c0f={item.SizeBytes:N0} \u5b57\u8282\r\n" +
            $"\u683c\u5f0f\uff1a{item.FormatHint}    Magic\uff1a{item.Magic}\r\n" +
            $"\u8def\u5f84\uff1a{item.Path}\r\n" +
            $"\u4e2d\u6587\u6ce8\u91ca\uff1a{item.Annotation}";

        SetPictureBoxImage(_gameResourcePreview, null);

        var ext = Path.GetExtension(item.Path);
        if (!ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus($"资源：{item.Category}/{item.Name}");
            return;
        }

        try
        {
            using var source = Image.FromFile(item.Path);
            SetPictureBoxImage(_gameResourcePreview, new Bitmap(source));
            SetStatus($"资源预览：{item.Category}/{item.Name}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"资源预览失败：{item.Path} {ex.Message}");
        }
    }

    private void LoadEexArchives()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            _currentEexArchives = _eexArchiveReader.ReadAll(_project);
            _currentEexEntryProbeRows = Array.Empty<EexEntryProbeRow>();
            _eexEntryProbeGrid.DataSource = null;
            _eexEntryTree.Nodes.Clear();
            _eexEntryTreeInfoBox.Text = "EEX 区段树：请选择一个 EEX 文件并点击“解析选中EEX区段”。";
            _currentEexCrossFileComparison = null;
            _eexCrossFileGrid.DataSource = null;
            _eexCrossFileInfoBox.Text = "EEX 跨文件对比：请选择一个 R/S/Map EEX 后点击“跨文件对比”。";
            ClearEexHeatmapPreview();
            PopulateEexArchiveCategoryFilter();
            BindEexArchiveRows(_currentEexArchives);
            UpdateEexArchiveInfo(_currentEexArchives.Count, "\u5168\u90e8", string.Empty);
            System.Diagnostics.Debug.WriteLine($"已读取 EEX 资源探针：{_currentEexArchives.Count} 个文件。");
            SetStatus("EEX 资源探针读取完成");
        }
        catch (Exception ex)
        {
            _eexArchiveInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("EEX 资源探针读取失败：" + ex);
            MessageBox.Show(this, ex.Message, "EEX 资源探针读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ConfigureEexArchiveGrid()
    {
        foreach (DataGridViewColumn column in _eexArchiveGrid.Columns)
        {
            if (column.DataPropertyName is nameof(EexArchiveInfo.TextHints)
                or nameof(EexArchiveInfo.Annotation)
                or nameof(EexArchiveInfo.HeaderAnnotation))
            {
                column.Width = 380;
            }
            else if (column.DataPropertyName == nameof(EexArchiveInfo.Path))
            {
                column.Width = 260;
            }
        }
    }



    private static void SelectComboValueOrFirst(ComboBox comboBox, string? value)
    {
        var selectedIndex = 0;
        if (!string.IsNullOrWhiteSpace(value))
        {
            for (var i = 0; i < comboBox.Items.Count; i++)
            {
                if (string.Equals(Convert.ToString(comboBox.Items[i], CultureInfo.InvariantCulture), value, StringComparison.Ordinal))
                {
                    selectedIndex = i;
                    break;
                }
            }
        }
        if (comboBox.Items.Count > 0) comboBox.SelectedIndex = selectedIndex;
    }

    private void BindEexArchiveRows(IEnumerable<EexArchiveInfo> rows)
    {
        _eexArchiveGrid.DataSource = new BindingList<EexArchiveInfo>(rows.ToList());
        ConfigureEexArchiveGrid();    }

    private void PopulateEexArchiveCategoryFilter()
    {
        var previous = Convert.ToString(_eexArchiveCategoryFilterCombo.SelectedItem, CultureInfo.InvariantCulture);
        _eexArchiveCategoryFilterCombo.Items.Clear();
        _eexArchiveCategoryFilterCombo.Items.Add("\u5168\u90e8");
        foreach (var category in _currentEexArchives.Select(x => x.Category).Distinct().OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            _eexArchiveCategoryFilterCombo.Items.Add(category);
        }
        SelectComboValueOrFirst(_eexArchiveCategoryFilterCombo, previous);
    }

    private void ApplyEexArchiveFilter()
    {
        if (_currentEexArchives.Count == 0) return;
        var category = Convert.ToString(_eexArchiveCategoryFilterCombo.SelectedItem, CultureInfo.InvariantCulture) ?? "\u5168\u90e8";
        var keyword = _eexArchiveSearchBox.Text.Trim();
        var filtered = _currentEexArchives.Where(item =>
            (category == "\u5168\u90e8" || string.Equals(item.Category, category, StringComparison.Ordinal)) &&
            (string.IsNullOrWhiteSpace(keyword) || EexArchiveMatchesKeyword(item, keyword)))
            .ToList();
        BindEexArchiveRows(filtered);
        UpdateEexArchiveInfo(filtered.Count, category, keyword);
        SetStatus($"EEX \u7b5b\u9009\uff1a{filtered.Count}/{_currentEexArchives.Count}");
    }

    private void ClearEexArchiveFilter()
    {
        _eexArchiveSearchBox.Clear();
        if (_eexArchiveCategoryFilterCombo.Items.Count > 0) _eexArchiveCategoryFilterCombo.SelectedIndex = 0;
        BindEexArchiveRows(_currentEexArchives);
        UpdateEexArchiveInfo(_currentEexArchives.Count, "\u5168\u90e8", string.Empty);
        SetStatus("\u5df2\u663e\u793a\u5168\u90e8 EEX \u8d44\u6e90");
    }

    private static bool EexArchiveMatchesKeyword(EexArchiveInfo item, string keyword)
    {
        return ContainsKeyword(item.Category, keyword) ||
               ContainsKeyword(item.Id, keyword) ||
               ContainsKeyword(item.FileName, keyword) ||
               ContainsKeyword(item.VersionHex, keyword) ||
               ContainsKeyword(item.Header14Hex, keyword) ||
               ContainsKeyword(item.Header18Hex, keyword) ||
               ContainsKeyword(item.Header22Hex, keyword) ||
               ContainsKeyword(item.Header26Hex, keyword) ||
               ContainsKeyword(item.TextHints, keyword) ||
               ContainsKeyword(item.Annotation, keyword) ||
               ContainsKeyword(item.HeaderAnnotation, keyword) ||
               ContainsKeyword(item.Path, keyword);
    }

    private void UpdateEexArchiveInfo(int visibleCount, string category, string keyword)
    {
        if (_project == null) return;
        var summary = string.Join("\uff0c", _currentEexArchives
            .GroupBy(x => x.Category)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key}:{g.Count()}"));
        var invalid = _currentEexArchives.Count(x => !x.MagicValid);
        var filterText = category == "\u5168\u90e8" && string.IsNullOrWhiteSpace(keyword)
            ? "\u672a\u7b5b\u9009"
            : $"\u5206\u7c7b={category}\uff0c\u5173\u952e\u5b57={keyword}";
        _eexArchiveInfoBox.Text =
            $"EEX \u626b\u63cf\u8303\u56f4\uff1a{Path.Combine(_project.GameRoot, "RS")}\uff1b{Path.Combine(_project.GameRoot, "Map")}\r\n" +
            $"\u6587\u4ef6\u6570\uff1a{_currentEexArchives.Count}    \u5f53\u524d\u663e\u793a\uff1a{visibleCount}    \u5206\u7c7b\uff1a{summary}    \u9b54\u6570\u5f02\u5e38\uff1a{invalid}\r\n" +
            $"\u7b5b\u9009\uff1a{filterText}\r\n" +
            "\u5f53\u524d\u4e3a\u53ea\u8bfb\u683c\u5f0f\u63a2\u9488\uff1a\u8bc6\u522b EEX\\0 \u9b54\u6570\u3001\u7248\u672c\u3001\u5934\u90e8\u5b57\u6bb5\u3001\u7591\u4f3c\u6761\u76ee\u6570\u548c GBK \u6587\u672c\u7ebf\u7d22\uff1b\u53ef\u5bf9\u9009\u4e2d\u6587\u4ef6\u751f\u6210\u5934\u5b57\u6bb5/\u533a\u6bb5/\u5e27\u8868\u5019\u9009\u5206\u6790\uff1b\u6682\u4e0d\u89e3\u5305/\u5199\u5165\u3002";
    }

    private EexArchiveInfo? GetSelectedEexArchiveItem()
    {
        if (_eexArchiveGrid.SelectedRows.Count > 0 && _eexArchiveGrid.SelectedRows[0].DataBoundItem is EexArchiveInfo selectedItem) return selectedItem;
        if (_eexArchiveGrid.CurrentRow?.DataBoundItem is EexArchiveInfo currentItem) return currentItem;
        return null;
    }

    private void OpenSelectedEexArchiveLocation()
    {
        var item = GetSelectedEexArchiveItem();
        if (item == null)
        {
            MessageBox.Show(this, "\u8bf7\u5148\u5728 EEX \u8d44\u6e90\u63a2\u9488\u9875\u9009\u62e9\u4e00\u4e2a\u6587\u4ef6\u3002", "\u63d0\u793a", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        OpenFileLocation(item.Path);
    }

    private void ExportEexArchivesCsv() =>
        ExportGridItemsCsv<EexArchiveInfo>(_eexArchiveGrid, "\u5bfc\u51fa EEX \u8d44\u6e90\u7d22\u5f15", "EEX\u8d44\u6e90\u7d22\u5f15.csv", "EexArchives", "EEX\u8d44\u6e90\u7d22\u5f15");

    private void ProbeSelectedEexEntries()
    {
        var item = GetSelectedEexArchiveItem();
        if (item == null)
        {
            MessageBox.Show(this, "请先在 EEX 资源探针页选择一个文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            _currentEexEntryProbeRows = _eexEntryProbeReader.Probe(item.Path, item.Category);
            BindEexEntryProbeRows(_currentEexEntryProbeRows);
            PopulateEexEntryTree(_currentEexEntryProbeRows);
            ShowSelectedEexEntryProbeRow();
            var sections = _currentEexEntryProbeRows.Count(x => x.NodeType == "区段候选");
            var textSections = _currentEexEntryProbeRows.Count(x => x.TextHintCount > 0);
            _eexArchiveInfoBox.Text =
                $"EEX 区段探针：{item.Category}/{item.FileName}\r\n" +
                $"文件：{item.Path}\r\n" +
                $"探针行：{_currentEexEntryProbeRows.Count}    区段候选：{sections}    含文本线索区段：{textSections}\r\n" +
                "说明：右侧表格展示文件头、头字段、候选区段、文本线索、字节多样性、00占比、小整数16位词占比和中文解释。当前仍是只读结构证据，不解压、不还原帧图、不写回。";
            System.Diagnostics.Debug.WriteLine($"已生成 EEX 区段探针：{item.FileName}，行 {_currentEexEntryProbeRows.Count}。");
            SetStatus($"EEX 区段探针完成：{item.FileName}");
        }
        catch (Exception ex)
        {
            _eexArchiveInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("EEX 区段探针失败：" + ex);
            MessageBox.Show(this, ex.Message, "EEX 区段探针失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void BindEexEntryProbeRows(IEnumerable<EexEntryProbeRow> rows)
    {
        _eexEntryProbeGrid.DataSource = new BindingList<EexEntryProbeRow>(rows.ToList());
        ConfigureEexEntryProbeGrid();    }

    private void ConfigureEexEntryProbeGrid()
    {
        foreach (DataGridViewColumn column in _eexEntryProbeGrid.Columns)
        {
            if (column.DataPropertyName is nameof(EexEntryProbeRow.Annotation)
                or nameof(EexEntryProbeRow.TextHints)
                or nameof(EexEntryProbeRow.FirstBytesHex))
            {
                column.Width = 320;
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            }
            else if (column.DataPropertyName == nameof(EexEntryProbeRow.Path))
            {
                column.Width = 220;
            }
        }

        foreach (DataGridViewRow row in _eexEntryProbeGrid.Rows)
        {
            if (row.DataBoundItem is not EexEntryProbeRow item) continue;
            row.DefaultCellStyle.BackColor = item.NodeType switch
            {
                "文件头" => Color.AliceBlue,
                "头字段" => Color.Honeydew,
                "区段候选" when item.TextHintCount > 0 => Color.LemonChiffon,
                "区段候选" => Color.White,
                _ => row.DefaultCellStyle.BackColor
            };
        }
    }

    private void PopulateEexEntryTree(IReadOnlyList<EexEntryProbeRow> rows)
    {
        _eexEntryTree.BeginUpdate();
        try
        {
            _eexEntryTree.Nodes.Clear();
            _eexEntryTreeInfoBox.Text = _eexEntryTreeDetailService.BuildTreeSummary(rows);
            if (rows.Count == 0) return;

            var root = new TreeNode($"{rows[0].Category}/{rows[0].FileName}｜{rows.Count} 行探针")
            {
                ToolTipText = _eexEntryTreeInfoBox.Text
            };
            _eexEntryTree.Nodes.Add(root);

            foreach (var group in _eexEntryTreeDetailService.BuildGroups(rows))
            {
                var groupNode = new TreeNode($"{group.Name}｜{group.Rows.Count} 项")
                {
                    ToolTipText = group.Explanation,
                    ForeColor = group.Name switch
                    {
                        var name when name.Contains("文本", StringComparison.Ordinal) => Color.DarkGreen,
                        var name when name.Contains("动作", StringComparison.Ordinal) || name.Contains("帧表", StringComparison.Ordinal) => Color.MidnightBlue,
                        var name when name.Contains("图像", StringComparison.Ordinal) || name.Contains("压缩", StringComparison.Ordinal) => Color.DarkOrange,
                        var name when name.Contains("透明", StringComparison.Ordinal) || name.Contains("稀疏", StringComparison.Ordinal) => Color.DarkCyan,
                        _ => Color.Black
                    }
                };
                root.Nodes.Add(groupNode);

                foreach (var row in group.Rows)
                {
                    var node = new TreeNode(BuildEexEntryTreeNodeText(row))
                    {
                        Tag = row,
                        ToolTipText = _eexEntryTreeDetailService.BuildDetail(row),
                        ForeColor = row.RoleHint switch
                        {
                            var role when role.Contains("文本", StringComparison.Ordinal) => Color.DarkGreen,
                            var role when role.Contains("动作", StringComparison.Ordinal) || role.Contains("帧表", StringComparison.Ordinal) => Color.MidnightBlue,
                            var role when role.Contains("图像", StringComparison.Ordinal) || role.Contains("压缩", StringComparison.Ordinal) => Color.DarkOrange,
                            var role when role.Contains("透明", StringComparison.Ordinal) || role.Contains("稀疏", StringComparison.Ordinal) => Color.DarkCyan,
                            _ => Color.Black
                        }
                    };
                    groupNode.Nodes.Add(node);
                }
            }

            ExpandTreeToDepth(root, maxDepth: 1);
        }
        finally
        {
            _eexEntryTree.EndUpdate();
        }
    }

    private static string BuildEexEntryTreeNodeText(EexEntryProbeRow row)
    {
        if (row.NodeType == "区段候选")
        {
            var textMark = row.TextHintCount > 0 ? $"｜文本{row.TextHintCount}" : string.Empty;
            return $"#{row.Index} {row.OffsetHex} 长度{row.Length:N0}｜{row.RoleHint}{textMark}";
        }

        return $"#{row.Index} {row.NodeType} {row.OffsetHex}｜{row.RoleHint}｜{row.ValueHex}";
    }

    private void SelectEexEntryProbeRowFromTree(EexEntryProbeRow? row)
    {
        if (row == null) return;
        foreach (DataGridViewRow gridRow in _eexEntryProbeGrid.Rows)
        {
            if (gridRow.DataBoundItem is not EexEntryProbeRow candidate || candidate.Index != row.Index) continue;
            gridRow.Selected = true;
            if (gridRow.Cells.Count > 0)
            {
                _eexEntryProbeGrid.CurrentCell = gridRow.Cells[0];
            }
            if (gridRow.Index >= 0 && gridRow.Index < _eexEntryProbeGrid.RowCount)
            {
                _eexEntryProbeGrid.FirstDisplayedScrollingRowIndex = gridRow.Index;
            }
            break;
        }

        ShowSelectedEexEntryProbeRow();
    }


    private void ShowSelectedEexEntryProbeRow()
    {
        var row = GetSelectedEexEntryProbeRow();
        if (row == null)
        {
            return;
        }

        var detail = _eexEntryTreeDetailService.BuildDetail(row);
        _eexEntryTreeInfoBox.Text =
            detail +
            "\r\n安全边界：本对象是 EEX 内部区段/头字段的只读证据；当前只用于理解动作、帧表、文本或压缩载荷候选，不直接改写封包。";
        SetStatus($"EEX区段：{row.FileName} #{row.Index} {row.OffsetHex}");
    }

    private void ExportEexEntryProbeCsv() =>
        ExportGridItemsCsv<EexEntryProbeRow>(_eexEntryProbeGrid, "导出 EEX 区段探针", "EEX区段探针.csv", "EexEntryProbe", "EEX区段探针");

    private void CompareSelectedEexAcrossFiles()
    {
        var item = GetSelectedEexArchiveItem();
        if (item == null)
        {
            MessageBox.Show(this, "请先在 EEX 资源探针页选择一个文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_currentEexArchives.Count == 0)
        {
            MessageBox.Show(this, "请先点击“读取 RS/Map .eex”建立 EEX 索引。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            _currentEexCrossFileComparison = _eexCrossFileComparisonService.Compare(item, _currentEexArchives);
            BindEexCrossFileRows(_currentEexCrossFileComparison.Rows);
            if (_eexCrossFileGrid.Rows.Count > 0)
            {
                _eexCrossFileGrid.Rows[0].Selected = true;
                _eexCrossFileGrid.CurrentCell = _eexCrossFileGrid.Rows[0].Cells[0];
                ShowSelectedEexCrossFileRow();
            }
            else
            {
                _eexCrossFileInfoBox.Text = _currentEexCrossFileComparison.Summary +
                    "\r\n创作提示：优先查看“同编号R/S”和“同分类邻近”的同角色区段；若长度、00占比或小整数比例变化明显，说明该资源可能有额外动作、帧表或压缩载荷差异。";
            }
            System.Diagnostics.Debug.WriteLine($"已生成 EEX 跨文件对比：{item.FileName}，行 {_currentEexCrossFileComparison.Rows.Count}。");
            SetStatus($"EEX 跨文件对比完成：{item.FileName}");
        }
        catch (Exception ex)
        {
            _eexCrossFileInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("EEX 跨文件对比失败：" + ex);
            MessageBox.Show(this, ex.Message, "EEX 跨文件对比失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void BindEexCrossFileRows(IEnumerable<EexCrossFileComparisonRow> rows)
    {
        _eexCrossFileGrid.DataSource = new BindingList<EexCrossFileComparisonRow>(rows.ToList());
        ConfigureEexCrossFileGrid();    }

    private void ConfigureEexCrossFileGrid()
    {
        foreach (DataGridViewColumn column in _eexCrossFileGrid.Columns)
        {
            if (column.DataPropertyName is nameof(EexCrossFileComparisonRow.DifferenceHint)
                or nameof(EexCrossFileComparisonRow.Annotation)
                or nameof(EexCrossFileComparisonRow.Path))
            {
                column.Width = 320;
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            }
            else if (column.DataPropertyName == nameof(EexCrossFileComparisonRow.RoleHint))
            {
                column.Width = 180;
            }
        }

        foreach (DataGridViewRow row in _eexCrossFileGrid.Rows)
        {
            if (row.DataBoundItem is not EexCrossFileComparisonRow item) continue;
            row.DefaultCellStyle.BackColor = item.PeerKind switch
            {
                "选中文件" => Color.AliceBlue,
                "同编号R/S" => Color.Honeydew,
                "同编号" => Color.Honeydew,
                _ when item.DifferenceHint.Contains("较大", StringComparison.Ordinal)
                    || item.DifferenceHint.Contains("没有同角色", StringComparison.Ordinal) => Color.LemonChiffon,
                _ => row.DefaultCellStyle.BackColor
            };
        }
    }


    private EexCrossFileComparisonRow? GetSelectedEexCrossFileRow()
    {
        if (_eexCrossFileGrid.SelectedRows.Count > 0 && _eexCrossFileGrid.SelectedRows[0].DataBoundItem is EexCrossFileComparisonRow selectedItem) return selectedItem;
        if (_eexCrossFileGrid.CurrentRow?.DataBoundItem is EexCrossFileComparisonRow currentItem) return currentItem;
        return null;
    }

    private void ShowSelectedEexCrossFileRow()
    {
        var row = GetSelectedEexCrossFileRow();
        if (row == null || _currentEexCrossFileComparison == null)
        {
            return;
        }

        var annotation = string.IsNullOrWhiteSpace(row.Annotation)
            ? "暂无自动注释；建议结合相邻 R/S、同编号文件和实机动作表现核对。"
            : row.Annotation;

        _eexCrossFileInfoBox.Text =
            "EEX 跨文件对比行详情\r\n" +
            $"基准文件：{_currentEexCrossFileComparison.TargetCategory}/{_currentEexCrossFileComparison.TargetFileName}    ID：{_currentEexCrossFileComparison.TargetId}\r\n" +
            $"对比对象：{row.Category}/{row.FileName}    ID：{row.Id}    关系：{row.PeerKind}    角色候选：{row.RoleHint}\r\n" +
            $"文件长度：{row.FileLength:N0} 字节    Magic：{(row.MagicValid ? "EEX\\0 OK" : "异常/待确认")}    区段数：{row.SectionCount}\r\n" +
            $"区段长度：总计 {row.TotalLength:N0} 字节；平均 {row.AverageLength:N1}；最小 {row.MinLength:N0}；最大 {row.MaxLength:N0}\r\n" +
            $"字节画像：平均 00 占比 {row.AverageZeroPercent:F1}%；平均小整数16位词 {row.AverageSmallWordPercent:F1}%；文本线索 {row.TextHintCount}；首偏移 {row.FirstOffsets}\r\n" +
            $"差异判定：{row.DifferenceHint}\r\n" +
            $"中文注释：{annotation}\r\n" +
            $"路径：{row.Path}\r\n" +
            "安全边界：本页只读分析 EEX 文件头、疑似区段长度和字节统计；当前不解压、不重封包、不写入。若未来开放 EEX 写回，必须先明确封包格式、备份、复读校验和实机验证。\r\n\r\n" +
            _currentEexCrossFileComparison.Summary;

        SetStatus($"EEX跨文件对比：{_currentEexCrossFileComparison.TargetFileName} -> {row.FileName}");
    }

    private EexEntryProbeRow? GetSelectedEexEntryProbeRow()
    {
        if (_eexEntryProbeGrid.SelectedRows.Count > 0 && _eexEntryProbeGrid.SelectedRows[0].DataBoundItem is EexEntryProbeRow selectedItem) return selectedItem;
        if (_eexEntryProbeGrid.CurrentRow?.DataBoundItem is EexEntryProbeRow currentItem) return currentItem;
        return null;
    }

    private void RenderSelectedEexHeatmap()
    {
        var archive = GetSelectedEexArchiveItem();
        var probeRow = GetSelectedEexEntryProbeRow();
        if (archive == null && probeRow == null)
        {
            MessageBox.Show(this, "请先选择一个 EEX 文件；如需观察局部，请先解析区段并选择右侧区段行。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var path = probeRow?.Path ?? archive!.Path;
        var category = !string.IsNullOrWhiteSpace(probeRow?.Category) ? probeRow!.Category : archive?.Category ?? "EEX";
        int? offset = null;
        int? length = null;
        var sourceKind = "整文件";
        if (probeRow != null && probeRow.Length > 0 && TryParseHexOffset(probeRow.OffsetHex, out var parsedOffset))
        {
            offset = parsedOffset;
            length = probeRow.Length;
            sourceKind = string.IsNullOrWhiteSpace(probeRow.RoleHint)
                ? probeRow.NodeType
                : $"{probeRow.NodeType}/{probeRow.RoleHint}";
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = _eexByteHeatmapService.Analyze(path, category, offset, length, sourceKind);
            var bitmap = _eexByteHeatmapService.Render(result);
            SetPictureBoxImage(_eexByteHeatmapBox, bitmap);
            _currentEexByteHeatmap = result;
            _eexByteHeatmapInfoBox.Text = BuildEexHeatmapInfoText(result);
            System.Diagnostics.Debug.WriteLine($"已生成 EEX 字节热力图：{result.FileName} {result.OffsetHex}-{result.EndOffsetHex}，单元 {result.CellCount}。");
            SetStatus($"EEX 字节热力图完成：{result.FileName}");
        }
        catch (Exception ex)
        {
            _eexByteHeatmapInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("EEX 字节热力图生成失败：" + ex);
            MessageBox.Show(this, ex.Message, "EEX 字节热力图失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ExportEexHeatmapPng()
    {
        if (_currentEexByteHeatmap == null)
        {
            MessageBox.Show(this, "当前还没有热力图。请先点击“生成字节热力图”。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var exportRoot = _project != null
            ? Path.Combine(_project.WorkspaceRoot, "CCZModStudio_Exports")
            : Path.GetDirectoryName(_currentEexByteHeatmap.Path) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(exportRoot);
        var defaultName = MakeSafeFileName($"{Path.GetFileNameWithoutExtension(_currentEexByteHeatmap.FileName)}_{_currentEexByteHeatmap.OffsetHex}_EEX字节热力图.png");
        using var dialog = new SaveFileDialog
        {
            Title = "导出 EEX 字节热力图 PNG",
            Filter = "PNG 图片 (*.png)|*.png|所有文件 (*.*)|*.*",
            FileName = defaultName,
            InitialDirectory = exportRoot
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            if (TryGetPictureBoxImageSize(_eexByteHeatmapBox, out _))
            {
                _eexByteHeatmapBox.Image.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);
            }
            else
            {
                using var bitmap = _eexByteHeatmapService.Render(_currentEexByteHeatmap);
                bitmap.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);
            }
            System.Diagnostics.Debug.WriteLine($"已导出 EEX 字节热力图：{dialog.FileName}");
            SetStatus("EEX 字节热力图 PNG 导出完成");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("导出 EEX 字节热力图失败：" + ex);
            MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ClearEexHeatmapPreview()
    {
        _currentEexByteHeatmap = null;
        SetPictureBoxImage(_eexByteHeatmapBox, null);
        _eexByteHeatmapInfoBox.Text = "EEX 字节热力图：请选择左侧 EEX 文件，或先解析区段后选择右侧候选区段，再点击“生成字节热力图”。该预览只读，不解压、不写入。";
    }

    private static bool TryParseHexOffset(string text, out int value)
    {
        var normalized = text.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }

        return int.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static string BuildEexHeatmapInfoText(EexByteHeatmapResult result)
    {
        return
            $"热力图：{result.Category}/{result.FileName}    来源：{result.SourceKind}\r\n" +
            $"范围：{result.OffsetHex}-{result.EndOffsetHex}    长度：{result.Length:N0} 字节    网格：{result.Width}x{result.Height}    每格约：{result.BytesPerCell:N0} 字节\r\n" +
            $"候选解释：{result.RoleHint}    不同字节：{result.UniqueByteCount}    00：{result.ZeroPercent:F1}%    FF：{result.FFPercent:F1}%    小整数16位词：{result.SmallWordPercent:F1}%    熵：{result.Entropy:F2}\r\n" +
            $"高频字节：{result.TopBytes}\r\n" +
            $"高频16位词：{result.TopWords}\r\n" +
            $"文本线索({result.TextHintCount})：{result.TextHints}\r\n" +
            result.Annotation;
    }

    private void ShowSelectedEexArchive()
    {
        if (_eexArchiveGrid.SelectedRows.Count == 0) return;
        if (_eexArchiveGrid.SelectedRows[0].DataBoundItem is not EexArchiveInfo item) return;

        var targetKey = $"{item.Category}/{item.FileName}";
        _eexArchiveInfoBox.Text =
            $"文件：{item.FileName}    分类：{item.Category}    ID：{item.Id}    长度：{item.Length:N0} 字节\r\n" +
            $"路径：{item.Path}\r\n" +
            $"Magic：{(item.MagicValid ? "EEX\\0 OK" : "异常")}    Version：{item.VersionHex}    EntryCount(疑似)：{item.EntryCount}\r\n" +
            $"Header14={item.Header14Hex}    Header18={item.Header18Hex}    Header22={item.Header22Hex}    Header26={item.Header26Hex}\r\n" +
            $"文本线索({item.TextHintCount})：{item.TextHints}";
        SetStatus($"EEX：{item.Category}/{item.FileName}");
    }
}
