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
            Log("剧本字典未找到：" + path);
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
                "当前阶段用于剧本模块的命令 ID->名称映射和参数候选表预览，尚未开放 Scene 二进制结构写入。";
            PopulateScriptNewCommandCombo(_currentSceneStringDocument);
            _probeScenarioCommandsButton.Enabled = _currentScenarioFiles.Count > 0;
            _buildScenarioStructureButton.Enabled = _currentScenarioFiles.Count > 0;
            if (showMessages)
            {
                LoadScenarioCommandTemplates(silent: true);
            }

            Log($"已读取剧本字典：命令 {_currentSceneStringDocument.Commands.Count} 项，字符串组 {_currentSceneStringDocument.Groups.Count} 组。");
            if (showMessages)
            {
                SetStatus("剧本字典读取完成");
            }

            return true;
        }
        catch (Exception ex)
        {
            _sceneDictionaryInfoBox.Text = ex.ToString();
            Log("剧本字典读取失败：" + ex);
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
        var workspace = _project?.WorkspaceRoot ?? Directory.GetCurrentDirectory();
        try
        {
            var resolvedRoot = _project == null
                ? MaterialLibraryIndexer.ResolveMaterialLibraryRoot(workspace)
                : MaterialLibraryIndexer.ResolveMaterialLibraryRoot(_project);
            var materialRoot = resolvedRoot
                ?? Path.Combine(workspace, "普罗-综合工具v0.3", "素材库");
            _currentMaterialAssets = _project == null
                ? _materialLibraryIndexer.Index(workspace)
                : _materialLibraryIndexer.Index(_project);
            _materialGrid.DataSource = new BindingList<MaterialAsset>(_currentMaterialAssets.ToList());
            ConfigureMaterialGrid();
            var categorySummary = string.Join("，", _currentMaterialAssets
                .GroupBy(x => x.Category)
                .Select(g => $"{g.Key}:{g.Count()}"));
            _materialInfoBox.Text =
                $"素材根目录：{materialRoot}\r\n" +
                $"素材数量：{_currentMaterialAssets.Count}    分类：{categorySummary}\r\n" +
                "当前阶段提供素材索引、hex 标记/说明和图片预览；地图写入和素材导入需等待地图格式验证。";
            Log($"已索引素材库：{_currentMaterialAssets.Count} 个图片素材。");
            if (resolvedRoot == null)
            {
                Log("素材库目录未找到，当前素材索引为空：" + materialRoot);
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
            Log("素材库索引失败：" + ex);
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
            var old = _materialPreview.Image;
            _materialPreview.Image = null;
            old?.Dispose();
            using var source = Image.FromFile(asset.FilePath);
            _materialPreview.Image = new Bitmap(source);
            SetStatus($"素材预览：{asset.Category}/{asset.FileName}");
        }
        catch (Exception ex)
        {
            var old = _materialPreview.Image;
            _materialPreview.Image = null;
            old?.Dispose();
            Log($"素材预览失败：{asset.FilePath} {ex.Message}");
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
            _currentGameResources = _gameResourceIndexer.Index(_project);
            PopulateGameResourceCategoryFilter();
            BindGameResourceRows(_currentGameResources);
            var summary = string.Join("，", _currentGameResources
                .GroupBy(x => x.Category)
                .OrderBy(g => g.Key)
                .Select(g => $"{g.Key}:{g.Count()}"));
            _gameResourceInfoBox.Text =
                $"游戏资源根目录：{_project.GameRoot}\r\n" +
                $"索引项：{_currentGameResources.Count}    分类：{summary}\r\n" +
                "当前阶段可索引、预览、定位、导出资源；已索引资源支持整文件替换预览、自动备份和从备份还原。EEX/E5S/E5 的内部重封包仍需后续格式验证。";
            var maps = _currentGameResources
                .Where(x => x.Category == "地图图片")
                .OrderBy(x => x.Id)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            _mapImageList.DisplayMember = nameof(ResourceIndexItem.Name);
            _mapImageList.DataSource = new BindingList<ResourceIndexItem>(maps);
            Log($"已索引游戏资源：{_currentGameResources.Count} 项。");
            SetStatus("游戏资源索引完成");
        }
        catch (Exception ex)
        {
            _gameResourceInfoBox.Text = ex.ToString();
            Log("游戏资源索引失败：" + ex);
            MessageBox.Show(this, ex.Message, "游戏资源索引失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void BindGameResourceRows(IEnumerable<ResourceIndexItem> rows)
    {
        _gameResourceGrid.DataSource = new BindingList<ResourceIndexItem>(rows.ToList());
        ConfigureGameResourceGrid();
        HighlightRowsWithCreatorNotes<ResourceIndexItem>(
            _gameResourceGrid,
            item => ("游戏资源", $"{item.Category}/{item.Name}"));
    }

    private void ConfigureGameResourceGrid()
    {
        if (_gameResourceGrid.Columns.Count == 0) return;
        HideNonAuthoringColumns(
            _gameResourceGrid,
            nameof(ResourceIndexItem.Annotation),
            nameof(ResourceIndexItem.Path));
    }

    private void PopulateGameResourceCategoryFilter()
    {
        var previous = Convert.ToString(_gameResourceCategoryFilterCombo.SelectedItem, CultureInfo.InvariantCulture);
        _gameResourceCategoryFilterCombo.Items.Clear();
        _gameResourceCategoryFilterCombo.Items.Add("\u5168\u90e8");
        foreach (var category in _currentGameResources.Select(x => x.Category).Distinct().OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
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
        if (_currentGameResources.Count == 0) return;
        var category = Convert.ToString(_gameResourceCategoryFilterCombo.SelectedItem, CultureInfo.InvariantCulture) ?? "\u5168\u90e8";
        var keyword = _gameResourceSearchBox.Text.Trim();
        var filtered = _currentGameResources.Where(item =>
            (category == "\u5168\u90e8" || string.Equals(item.Category, category, StringComparison.Ordinal)) &&
            (string.IsNullOrWhiteSpace(keyword) || GameResourceMatchesKeyword(item, keyword)))
            .ToList();
        BindGameResourceRows(filtered);
        UpdateGameResourceInfo(filtered.Count, category, keyword);
        SetStatus($"\u6e38\u620f\u8d44\u6e90\u7b5b\u9009\uff1a{filtered.Count}/{_currentGameResources.Count}");
    }

    private void ClearGameResourceFilter()
    {
        _gameResourceSearchBox.Clear();
        if (_gameResourceCategoryFilterCombo.Items.Count > 0) _gameResourceCategoryFilterCombo.SelectedIndex = 0;
        BindGameResourceRows(_currentGameResources);
        UpdateGameResourceInfo(_currentGameResources.Count, "\u5168\u90e8", string.Empty);
        SetStatus("\u5df2\u663e\u793a\u5168\u90e8\u6e38\u620f\u8d44\u6e90");
    }

    private static bool GameResourceMatchesKeyword(ResourceIndexItem item, string keyword)
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
        var summary = string.Join("\uff0c", _currentGameResources
            .GroupBy(x => x.Category)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key}:{g.Count()}"));
        var filterText = category == "\u5168\u90e8" && string.IsNullOrWhiteSpace(keyword)
            ? "\u672a\u7b5b\u9009"
            : $"\u5206\u7c7b={category}\uff0c\u5173\u952e\u5b57={keyword}";
        _gameResourceInfoBox.Text =
            $"\u6e38\u620f\u8d44\u6e90\u6839\u76ee\u5f55\uff1a{_project.GameRoot}\r\n" +
            $"\u7d22\u5f15\u9879\uff1a{_currentGameResources.Count}    \u5f53\u524d\u663e\u793a\uff1a{visibleCount}    \u5206\u7c7b\uff1a{summary}\r\n" +
            $"\u7b5b\u9009\uff1a{filterText}\r\n" +
            "当前阶段可索引、预览、定位、导出资源；已索引资源支持整文件替换预览、自动备份和从备份还原。EEX/E5S/E5 的内部解码、扩容和重封包仍需后续格式验证。";
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
            MessageBox.Show(this, "请先在游戏资源索引页选择一个文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            Log($"资源替换预览：{preview.TargetRelativePath} <- {preview.ReplacementPath}，改动估算 {preview.ChangedBytesEstimate:N0} 字节");
            SetStatus("资源替换预览完成，请确认");
        }
        catch (Exception ex)
        {
            Log("资源替换预览失败：" + ex);
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
            _currentGameResources = _gameResourceIndexer.Index(_project);
            PopulateGameResourceCategoryFilter();
            BindGameResourceRows(_currentGameResources);
            UpdateGameResourceInfo(_currentGameResources.Count, "全部", string.Empty);
            _gameResourceInfoBox.Text =
                $"资源替换完成：{item.Category}/{item.Name}\r\n" +
                $"旧大小：{result.OldSizeBytes:N0} 字节    新大小：{result.NewSizeBytes:N0} 字节    改动估算：{result.ChangedBytesEstimate:N0} 字节\r\n" +
                $"格式检查：{result.FormatCheckSummary}\r\n" +
                $"格式警告：{(result.FormatWarnings.Count == 0 ? "无" : string.Join("；", result.FormatWarnings))}\r\n" +
                $"风险提示：{result.RiskSummary}\r\n" +
                $"备份：{result.BackupPath}\r\n报告：{result.ReportPath}\r\n结构化报告：{result.ReportJsonPath}\r\n" +
                "说明：这是项目资源整文件替换，不会重封包未知格式；替换后请运行资源诊断和实机测试。";
            RefreshResourceDiagnosticsAfterFileChange(item);
            Log($"已替换项目资源：{item.Path} <- {dialog.FileName}，备份 {result.BackupPath}，结构化报告 {result.ReportJsonPath}");
            SetStatus("项目资源替换完成");
        }
        catch (Exception ex)
        {
            Log("替换项目资源失败：" + ex);
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
            MessageBox.Show(this, "请先在游戏资源索引页选择一个要还原的文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            Log($"资源备份还原预览：{preview.TargetRelativePath} <- {preview.ReplacementPath}");
            SetStatus("资源备份还原预览完成，请确认");
        }
        catch (Exception ex)
        {
            Log("资源备份还原预览失败：" + ex);
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
            _currentGameResources = _gameResourceIndexer.Index(_project);
            PopulateGameResourceCategoryFilter();
            BindGameResourceRows(_currentGameResources);
            UpdateGameResourceInfo(_currentGameResources.Count, "全部", string.Empty);
            _gameResourceInfoBox.Text =
                $"资源备份还原完成：{item.Category}/{item.Name}\r\n" +
                $"旧大小：{result.OldSizeBytes:N0} 字节    还原后大小：{result.NewSizeBytes:N0} 字节    改动估算：{result.ChangedBytesEstimate:N0} 字节\r\n" +
                $"格式检查：{result.FormatCheckSummary}\r\n" +
                $"格式警告：{(result.FormatWarnings.Count == 0 ? "无" : string.Join("；", result.FormatWarnings))}\r\n" +
                $"风险提示：{result.RiskSummary}\r\n" +
                $"还原前当前文件备份：{result.BackupPath}\r\n报告：{result.ReportPath}\r\n结构化报告：{result.ReportJsonPath}\r\n" +
                "说明：已从选定备份整文件还原到当前项目；建议重新运行资源诊断和差异检查。";
            RefreshResourceDiagnosticsAfterFileChange(item);
            Log($"已从备份还原项目资源：{item.Path} <- {dialog.FileName}，还原前备份 {result.BackupPath}，结构化报告 {result.ReportJsonPath}");
            SetStatus("项目资源备份还原完成");
        }
        catch (Exception ex)
        {
            Log("从备份还原资源失败：" + ex);
            MessageBox.Show(this, ex.Message, "还原失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private static string BuildResourceReplacePreviewText(ResourceIndexItem item, ResourceReplacePreviewResult preview)
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

    private static string BuildResourceReplaceConfirmText(ResourceIndexItem item, ResourceReplacePreviewResult preview)
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

    private DialogResult ShowResourceReplacePreviewDialog(string title, string confirmText, ResourceIndexItem item, ResourceReplacePreviewResult preview, string detailText)
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
        ConfigureSplitContainerDistanceAfterLayout(imageSplit, desiredDistance: 520, desiredPanel1Min: 25, desiredPanel2Min: 25);
        layout.Controls.Add(imageSplit, 0, 1);
        imageSplit.Panel1.Controls.Add(BuildResourcePreviewPanel("当前目标文件", preview.TargetPath, $"{item.Category}/{item.Name}", disposeOnClose: dialog));
        imageSplit.Panel2.Controls.Add(BuildResourcePreviewPanel("替换/还原来源文件", preview.ReplacementPath, Path.GetFileName(preview.ReplacementPath), disposeOnClose: dialog));

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

    private void RefreshResourceDiagnosticsAfterFileChange(ResourceIndexItem changedItem)
    {
        if (_project == null)
        {
            return;
        }

        try
        {
            var shouldReviewScenarioMap = _resourceChangeReviewHintService.MayAffectScenarioMap(changedItem);
            if (shouldReviewScenarioMap)
            {
                InvalidateScenarioMapCachesAfterResourceChange(changedItem);
            }

            RunResourceDiagnostics();
            var errors = _currentResourceDiagnostics.Count(x => x.Severity == "Error");
            var warnings = _currentResourceDiagnostics.Count(x => x.Severity == "Warn");
            var infos = _currentResourceDiagnostics.Count(x => x.Severity == "Info");
            var related = _currentResourceDiagnostics
                .Where(x => ResourceDiagnosticMayRelateToResource(x, changedItem))
                .Take(5)
                .ToList();
            var relatedText = related.Count == 0
                ? "未发现与当前资源直接匹配的诊断项。"
                : "可能相关诊断：" + string.Join("；", related.Select(x => $"[{x.Severity}] {x.Category}/{x.Rule}:{x.Status}"));
            var scenarioMapReviewText = shouldReviewScenarioMap
                ? RefreshScenarioMapReviewAfterResourceChange(changedItem)
                : string.Empty;
            _gameResourceInfoBox.AppendText(
                $"\r\n\r\n已自动刷新资源诊断：Error={errors}，Warn={warnings}，Info={infos}。\r\n" +
                relatedText +
                (string.IsNullOrWhiteSpace(scenarioMapReviewText) ? string.Empty : "\r\n\r\n" + scenarioMapReviewText));
            Log($"资源变更后已自动刷新诊断：{changedItem.Name}，Error={errors}，Warn={warnings}，Info={infos}，相关 {related.Count} 项，联动复查={(shouldReviewScenarioMap ? "已提示" : "不适用")}。");
        }
        catch (Exception ex)
        {
            _gameResourceInfoBox.AppendText("\r\n\r\n资源已写入，但自动刷新资源诊断失败：" + ex.Message + "\r\n请手动切换到“资源诊断”页重新诊断。");
            Log("资源变更后自动刷新诊断失败：" + ex);
        }
    }

    private void InvalidateScenarioMapCachesAfterResourceChange(ResourceIndexItem changedItem)
    {
        _currentScenarioMapLinks = Array.Empty<ScenarioMapLinkInfo>();

        if (changedItem.Category == "E5S存档信息" || PathContainsDirectorySegment(changedItem.Path, "RS"))
        {
            _currentScenarioFiles = Array.Empty<ScenarioFileInfo>();
        }

        if (changedItem.Name.Equals("Hexzmap.e5", StringComparison.OrdinalIgnoreCase))
        {
            _currentHexzmapProbe = null;
        }
    }

    private string RefreshScenarioMapReviewAfterResourceChange(ResourceIndexItem changedItem)
    {
        try
        {
            if (_currentScenarioMapLinks.Count == 0)
            {
                _currentScenarioMapLinks = EnsureScenarioMapLinksForDiagnostics();
            }

            if (_currentScenarioMapLinks.Count > 0)
            {
                PopulateScenarioMapLinkStatusFilter();
                BindScenarioMapLinkRows(_currentScenarioMapLinks);
                _exportScenarioMapLinksCsvButton.Enabled = true;
                _locateScenarioMapScenarioButton.Enabled = true;
                _locateScenarioMapImageButton.Enabled = _currentScenarioMapLinks.Any(x => x.MapImageExists);
                _jumpScenarioMapScenarioButton.Enabled = true;
                _jumpScenarioMapHexzmapButton.Enabled = _currentScenarioMapLinks.Any(x => x.HexzmapBlockExists);
                _jumpScenarioMapViewerButton.Enabled = _currentScenarioMapLinks.Any(x => x.MapImageExists);
                _exportScenarioMapPreviewPngButton.Enabled = _currentScenarioMapLinks.Any(x => x.MapImageExists || x.HexzmapBlockExists);
                _writeScenarioMapReportButton.Enabled = true;
                _filterScenarioMapLinksButton.Enabled = true;
                _clearScenarioMapLinkFilterButton.Enabled = true;
                _scenarioMapLinksIncompleteOnly.Enabled = true;
                UpdateScenarioMapLinkSummary(_currentScenarioMapLinks.Count, "全部", string.Empty, false);
            }

            return _resourceChangeReviewHintService.BuildScenarioMapReviewHint(changedItem, _currentScenarioMapLinks);
        }
        catch (Exception ex)
        {
            Log("资源变更后刷新关卡地图联动复查提示失败：" + ex);
            return "关卡地图联动复查：资源可能影响地图/R/S eex/Hexzmap 联动，但自动重新生成联动提示失败。请手动打开“关卡地图联动”页重新生成、导出检查报告和预览 PNG。错误：" + ex.Message;
        }
    }

    private static bool PathContainsDirectorySegment(string path, string segment)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var normalized = path.Replace('/', '\\');
        return normalized.Contains("\\" + segment + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ResourceDiagnosticMayRelateToResource(ResourceDiagnosticItem diagnostic, ResourceIndexItem item)
    {
        var fileName = Path.GetFileName(item.Path);
        return (!string.IsNullOrWhiteSpace(diagnostic.Path) && Path.GetFullPath(diagnostic.Path).Equals(Path.GetFullPath(item.Path), StringComparison.OrdinalIgnoreCase)) ||
               ContainsKeyword(diagnostic.Path, fileName) ||
               ContainsKeyword(diagnostic.Name, item.Name) ||
               (!string.IsNullOrWhiteSpace(item.Id) && ContainsKeyword(diagnostic.Id, item.Id)) ||
               (!string.IsNullOrWhiteSpace(item.Category) && ContainsKeyword(diagnostic.Category, item.Category));
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
            previewBox.Image = new Bitmap(image);
            infoLabel.Text = $"图片可读：{image.Width}x{image.Height}    文件：{path}";
            disposeOnClose.FormClosed += (_, _) => previewBox.Image?.Dispose();
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

    private void ExportGameResourcesCsv()
    {
        if (_currentGameResources.Count == 0)
        {
            MessageBox.Show(this, "\u8bf7\u5148\u7d22\u5f15\u6e38\u620f\u8d44\u6e90\u3002", "\u63d0\u793a", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "\u5bfc\u51fa\u6e38\u620f\u8d44\u6e90\u7d22\u5f15",
            Filter = "CSV \u6587\u4ef6 (*.csv)|*.csv|\u6240\u6709\u6587\u4ef6 (*.*)|*.*",
            FileName = "\u6e38\u620f\u8d44\u6e90\u7d22\u5f15.csv"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var table = new DataTable("GameResources");
            table.Columns.Add("Category");
            table.Columns.Add("Id");
            table.Columns.Add("Name");
            table.Columns.Add("Extension");
            table.Columns.Add("SizeBytes", typeof(long));
            table.Columns.Add("Magic");
            table.Columns.Add("FormatHint");
            table.Columns.Add("Annotation");
            table.Columns.Add("Width", typeof(int));
            table.Columns.Add("Height", typeof(int));
            table.Columns.Add("Path");
            foreach (var item in _currentGameResources)
            {
                table.Rows.Add(item.Category, item.Id, item.Name, item.Extension, item.SizeBytes, item.Magic, item.FormatHint, item.Annotation, item.Width, item.Height, item.Path);
            }

            CsvService.Export(table, dialog.FileName);
            Log($"\u5df2\u5bfc\u51fa\u6e38\u620f\u8d44\u6e90\u7d22\u5f15\uff1a{dialog.FileName}\uff0c\u9879\u6570 {_currentGameResources.Count}");
            SetStatus($"\u6e38\u620f\u8d44\u6e90\u7d22\u5f15\u5bfc\u51fa\u5b8c\u6210\uff1a{_currentGameResources.Count} \u9879");
        }
        catch (Exception ex)
        {
            Log("\u5bfc\u51fa\u6e38\u620f\u8d44\u6e90\u7d22\u5f15\u5931\u8d25\uff1a" + ex);
            MessageBox.Show(this, ex.Message, "\u5bfc\u51fa\u5931\u8d25", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }


    private void RunResourceDiagnostics()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "\u8bf7\u5148\u52a0\u8f7d\u9879\u76ee\u3002", "\u63d0\u793a", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            if (_currentGameResources.Count == 0)
            {
                _currentGameResources = _gameResourceIndexer.Index(_project);
                PopulateGameResourceCategoryFilter();
                BindGameResourceRows(_currentGameResources);
                UpdateGameResourceInfo(_currentGameResources.Count, "\u5168\u90e8", string.Empty);
                var maps = _currentGameResources
                    .Where(x => x.Category == "\u5730\u56fe\u56fe\u7247")
                    .OrderBy(x => x.Id)
                    .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                _mapImageList.DisplayMember = nameof(ResourceIndexItem.Name);
                _mapImageList.DataSource = new BindingList<ResourceIndexItem>(maps);
                Log($"\u8d44\u6e90\u8bca\u65ad\u524d\u5df2\u81ea\u52a8\u7d22\u5f15\u6e38\u620f\u8d44\u6e90\uff1a{_currentGameResources.Count} \u9879\u3002");
            }

            var diagnostics = _resourceDiagnosticService.Analyze(_currentGameResources).ToList();
            if (_tables.Count > 0)
            {
                try
                {
                    var imageAssignments = _currentImageAssignments ?? _imageAssignmentService.Load(_project, _tables);
                    diagnostics.AddRange(_resourceReferenceDiagnosticService.AnalyzeImageAssignments(_project, imageAssignments, _currentGameResources));
                }
                catch (Exception referenceEx)
                {
                    diagnostics.Add(new ResourceDiagnosticItem
                    {
                        Severity = "Info",
                        Category = "\u8868\u683c\u5f15\u7528",
                        Rule = "\u5f15\u7528\u8bca\u65ad\u8df3\u8fc7",
                        Id = string.Empty,
                        Name = "\u4eba\u7269 R/S \u5f62\u8c61",
                        Status = referenceEx.Message,
                        Detail = "\u672a\u80fd\u8bfb\u53d6\u4eba\u7269\u5f62\u8c61\u8054\u52a8\u8868\uff1a" + referenceEx.Message,
                        Suggestion = "\u8bf7\u5148\u786e\u8ba4 6.5 \u4eba\u7269/R/S \u5f62\u8c61\u8868\u53ef\u8bfb\uff1b\u8d44\u6e90\u6587\u4ef6\u81ea\u8eab\u8bca\u65ad\u4ecd\u5df2\u751f\u6210\u3002",
                        Path = _project.GameRoot
                    });
                    Log("\u8d44\u6e90\u5f15\u7528\u8bca\u65ad\u8df3\u8fc7\uff1a" + referenceEx);
                }

                try
                {
                    diagnostics.AddRange(_tableReferenceDiagnosticService.Analyze(_project, _tables));
                }
                catch (Exception tableReferenceEx)
                {
                    diagnostics.Add(new ResourceDiagnosticItem
                    {
                        Severity = "Info",
                        Category = "表格引用/数据表",
                        Rule = "跨表引用诊断跳过",
                        Id = "0",
                        Name = "人物/物品/兵种/商店/专属装备",
                        Status = tableReferenceEx.Message,
                        Detail = "源表：6.5-0 人物；行 ID：0；字段：职业；未能生成数据表跨表引用诊断：" + tableReferenceEx.Message,
                        Suggestion = "请确认 HexTable.xml 与核心表文件可读；其他资源文件自身诊断仍已生成。可先在数据表页查看字段中文注释和跨表解释。",
                        Path = _project.GameRoot
                    });
                    Log("数据表跨表引用诊断跳过：" + tableReferenceEx);
                }
            }

            try
            {
                var scenarioMapLinks = EnsureScenarioMapLinksForDiagnostics();
                diagnostics.AddRange(_resourceReferenceDiagnosticService.AnalyzeScenarioMapLinks(_project, scenarioMapLinks));
            }
            catch (Exception scenarioMapEx)
            {
                diagnostics.Add(new ResourceDiagnosticItem
                {
                    Severity = "Info",
                    Category = "关卡地图联动",
                    Rule = "联动诊断跳过",
                    Id = string.Empty,
                    Name = "R/S eex/Map/Hexzmap",
                    Status = scenarioMapEx.Message,
                    Detail = "未能生成关卡地图联动诊断：" + scenarioMapEx.Message,
                    Suggestion = "请确认 SV 目录、Map 图片和 Hexzmap.e5 可读取；其他资源文件自身诊断仍已生成。",
                    Path = _project.GameRoot
                });
                Log("关卡地图联动诊断跳过：" + scenarioMapEx);
            }

            _currentResourceDiagnostics = diagnostics
                .OrderByDescending(x => ResourceDiagnosticSeverityRank(x.Severity))
                .ThenBy(x => x.Category, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.Rule, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.Id, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            PopulateResourceDiagnosticSeverityFilter();
            PopulateResourceDiagnosticCategoryFilter();
            BindResourceDiagnosticRows(_currentResourceDiagnostics);
            UpdateResourceDiagnosticInfo(_currentResourceDiagnostics.Count, "\u5168\u90e8", "\u5168\u90e8", string.Empty);
            var errors = _currentResourceDiagnostics.Count(x => x.Severity == "Error");
            var warnings = _currentResourceDiagnostics.Count(x => x.Severity == "Warn");
            var infos = _currentResourceDiagnostics.Count(x => x.Severity == "Info");
            Log($"\u8d44\u6e90\u8bca\u65ad\u5b8c\u6210\uff1a{_currentResourceDiagnostics.Count} \u9879\uff0cError={errors}\uff0cWarn={warnings}\uff0cInfo={infos}\u3002");
            SetStatus($"\u8d44\u6e90\u8bca\u65ad\u5b8c\u6210\uff1a{_currentResourceDiagnostics.Count} \u9879");
            RefreshWorkflowGuide(updateStatus: false);
        }
        catch (Exception ex)
        {
            _resourceDiagnosticInfoBox.Text = ex.ToString();
            Log("\u8d44\u6e90\u8bca\u65ad\u5931\u8d25\uff1a" + ex);
            MessageBox.Show(this, ex.Message, "\u8d44\u6e90\u8bca\u65ad\u5931\u8d25", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private IReadOnlyList<ScenarioMapLinkInfo> EnsureScenarioMapLinksForDiagnostics()
    {
        if (_project == null) return Array.Empty<ScenarioMapLinkInfo>();
        if (_currentScenarioMapLinks.Count > 0) return _currentScenarioMapLinks;

        if (_currentScenarioFiles.Count == 0)
        {
            var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe();
            _currentScenarioFiles = _scenarioFileReader.ReadAllIndex(_project);
        }

        if (_currentGameResources.Count == 0)
        {
            _currentGameResources = _gameResourceIndexer.Index(_project);
        }

        if (_currentHexzmapProbe == null)
        {
            var terrainLookup = BuildTerrainNameLookupForCurrentProject();
            _currentHexzmapProbe = _hexzmapProbeReader.Read(_project, terrainLookup);
        }

        _currentScenarioMapLinks = _scenarioMapLinkService.BuildLinks(_currentScenarioFiles, _currentGameResources, _currentHexzmapProbe);
        return _currentScenarioMapLinks;
    }

    private void BindResourceDiagnosticRows(IEnumerable<ResourceDiagnosticItem> rows)
    {
        _resourceDiagnosticGrid.DataSource = new BindingList<ResourceDiagnosticItem>(rows.ToList());
        ConfigureResourceDiagnosticGrid();
        HighlightRowsWithCreatorNotes<ResourceDiagnosticItem>(
            _resourceDiagnosticGrid,
            item => ("资源诊断", BuildResourceDiagnosticCreatorNoteTargetKey(item)));
    }

    private void ConfigureResourceDiagnosticGrid()
    {
        if (_resourceDiagnosticGrid.Columns.Count == 0) return;

        SetResourceDiagnosticColumn(nameof(ResourceDiagnosticItem.Severity), "\u7ea7\u522b", 70);
        SetResourceDiagnosticColumn(nameof(ResourceDiagnosticItem.Category), "\u5206\u7c7b", 100);
        SetResourceDiagnosticColumn(nameof(ResourceDiagnosticItem.Rule), "\u89c4\u5219", 130);
        SetResourceDiagnosticColumn(nameof(ResourceDiagnosticItem.Id), "\u7f16\u53f7/\u8303\u56f4", 90);
        SetResourceDiagnosticColumn(nameof(ResourceDiagnosticItem.Name), "\u6587\u4ef6/\u5bf9\u8c61", 180);
        SetResourceDiagnosticColumn(nameof(ResourceDiagnosticItem.Status), "\u72b6\u6001", 160);
        SetResourceDiagnosticColumn(nameof(ResourceDiagnosticItem.Detail), "\u8bc1\u636e\u8be6\u60c5", 360);
        SetResourceDiagnosticColumn(nameof(ResourceDiagnosticItem.Suggestion), "\u521b\u4f5c\u5efa\u8bae", 360);
        SetResourceDiagnosticColumn(nameof(ResourceDiagnosticItem.Path), "\u8def\u5f84", 260);
        HideNonAuthoringColumns(
            _resourceDiagnosticGrid,
            nameof(ResourceDiagnosticItem.Detail),
            nameof(ResourceDiagnosticItem.Suggestion),
            nameof(ResourceDiagnosticItem.Path));

        foreach (DataGridViewRow row in _resourceDiagnosticGrid.Rows)
        {
            if (row.DataBoundItem is not ResourceDiagnosticItem item) continue;
            row.DefaultCellStyle.BackColor = item.Severity switch
            {
                "Error" => Color.MistyRose,
                "Warn" => Color.LemonChiffon,
                "Info" => Color.AliceBlue,
                _ => row.DefaultCellStyle.BackColor
            };
        }
    }

    private void SetResourceDiagnosticColumn(string columnName, string headerText, int width)
    {
        if (!_resourceDiagnosticGrid.Columns.Contains(columnName)) return;
        var column = _resourceDiagnosticGrid.Columns[columnName];
        column.HeaderText = headerText;
        column.Width = width;
        column.MinimumWidth = Math.Min(width, 80);
        if (columnName is nameof(ResourceDiagnosticItem.Detail) or nameof(ResourceDiagnosticItem.Suggestion) or nameof(ResourceDiagnosticItem.Path))
        {
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        }
    }

    private void PopulateResourceDiagnosticSeverityFilter()
    {
        var previous = Convert.ToString(_resourceDiagnosticSeverityFilterCombo.SelectedItem, CultureInfo.InvariantCulture);
        _resourceDiagnosticSeverityFilterCombo.Items.Clear();
        _resourceDiagnosticSeverityFilterCombo.Items.Add("\u5168\u90e8");
        foreach (var severity in _currentResourceDiagnostics
                     .Select(x => x.Severity)
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(ResourceDiagnosticSeverityRank)
                     .ThenBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            _resourceDiagnosticSeverityFilterCombo.Items.Add(severity);
        }

        var selectedIndex = 0;
        if (!string.IsNullOrWhiteSpace(previous))
        {
            for (var i = 0; i < _resourceDiagnosticSeverityFilterCombo.Items.Count; i++)
            {
                if (string.Equals(Convert.ToString(_resourceDiagnosticSeverityFilterCombo.Items[i], CultureInfo.InvariantCulture), previous, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i;
                    break;
                }
            }
        }

        _resourceDiagnosticSeverityFilterCombo.SelectedIndex = selectedIndex;
    }

    private void PopulateResourceDiagnosticCategoryFilter()
    {
        var previous = Convert.ToString(_resourceDiagnosticCategoryFilterCombo.SelectedItem, CultureInfo.InvariantCulture);
        _resourceDiagnosticCategoryFilterCombo.Items.Clear();
        _resourceDiagnosticCategoryFilterCombo.Items.Add("\u5168\u90e8");
        foreach (var category in _currentResourceDiagnostics
                     .Select(x => x.Category)
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            _resourceDiagnosticCategoryFilterCombo.Items.Add(category);
        }

        var selectedIndex = 0;
        if (!string.IsNullOrWhiteSpace(previous))
        {
            for (var i = 0; i < _resourceDiagnosticCategoryFilterCombo.Items.Count; i++)
            {
                if (string.Equals(Convert.ToString(_resourceDiagnosticCategoryFilterCombo.Items[i], CultureInfo.InvariantCulture), previous, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i;
                    break;
                }
            }
        }

        _resourceDiagnosticCategoryFilterCombo.SelectedIndex = selectedIndex;
    }

    private void ApplyResourceDiagnosticFilter()
    {
        if (_currentResourceDiagnostics.Count == 0)
        {
            return;
        }

        var severity = Convert.ToString(_resourceDiagnosticSeverityFilterCombo.SelectedItem, CultureInfo.InvariantCulture) ?? "\u5168\u90e8";
        var category = Convert.ToString(_resourceDiagnosticCategoryFilterCombo.SelectedItem, CultureInfo.InvariantCulture) ?? "\u5168\u90e8";
        var keyword = _resourceDiagnosticSearchBox.Text.Trim();
        var filtered = _currentResourceDiagnostics.Where(item =>
                (severity == "\u5168\u90e8" || string.Equals(item.Severity, severity, StringComparison.OrdinalIgnoreCase)) &&
                (category == "\u5168\u90e8" || string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(keyword) || ResourceDiagnosticMatchesKeyword(item, keyword)))
            .ToList();
        BindResourceDiagnosticRows(filtered);
        UpdateResourceDiagnosticInfo(filtered.Count, severity, category, keyword);
        SetStatus($"\u8d44\u6e90\u8bca\u65ad\u7b5b\u9009\uff1a{filtered.Count}/{_currentResourceDiagnostics.Count}");
    }

    private void ClearResourceDiagnosticFilter()
    {
        _resourceDiagnosticSearchBox.Clear();
        if (_resourceDiagnosticSeverityFilterCombo.Items.Count > 0) _resourceDiagnosticSeverityFilterCombo.SelectedIndex = 0;
        if (_resourceDiagnosticCategoryFilterCombo.Items.Count > 0) _resourceDiagnosticCategoryFilterCombo.SelectedIndex = 0;
        BindResourceDiagnosticRows(_currentResourceDiagnostics);
        UpdateResourceDiagnosticInfo(_currentResourceDiagnostics.Count, "\u5168\u90e8", "\u5168\u90e8", string.Empty);
        SetStatus("\u5df2\u663e\u793a\u5168\u90e8\u8d44\u6e90\u8bca\u65ad\u9879");
    }

    private static bool ResourceDiagnosticMatchesKeyword(ResourceDiagnosticItem item, string keyword)
    {
        return ContainsKeyword(item.Severity, keyword) ||
               ContainsKeyword(item.Category, keyword) ||
               ContainsKeyword(item.Rule, keyword) ||
               ContainsKeyword(item.Id, keyword) ||
               ContainsKeyword(item.Name, keyword) ||
               ContainsKeyword(item.Status, keyword) ||
               ContainsKeyword(item.Detail, keyword) ||
               ContainsKeyword(item.Suggestion, keyword) ||
               ContainsKeyword(item.Path, keyword);
    }

    private void UpdateResourceDiagnosticInfo(int visibleCount, string severity, string category, string keyword)
    {
        var errors = _currentResourceDiagnostics.Count(x => x.Severity == "Error");
        var warnings = _currentResourceDiagnostics.Count(x => x.Severity == "Warn");
        var infos = _currentResourceDiagnostics.Count(x => x.Severity == "Info");
        var categorySummary = string.Join("\uff0c", _currentResourceDiagnostics
            .GroupBy(x => x.Category)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase)
            .Take(10)
            .Select(x => $"{x.Key}:{x.Count()}"));
        var filterText = severity == "\u5168\u90e8" && category == "\u5168\u90e8" && string.IsNullOrWhiteSpace(keyword)
            ? "\u672a\u7b5b\u9009"
            : $"\u7ea7\u522b={severity}\uff0c\u5206\u7c7b={category}\uff0c\u5173\u952e\u5b57={keyword}";

        _resourceDiagnosticInfoBox.Text =
            $"\u8d44\u6e90\u8bca\u65ad\u57fa\u4e8e\u5f53\u524d\u6e38\u620f\u8d44\u6e90\u7d22\u5f15\uff1a\u8d44\u6e90 {_currentGameResources.Count} \u9879\uff0c\u8bca\u65ad {_currentResourceDiagnostics.Count} \u9879\uff0c\u5f53\u524d\u663e\u793a {visibleCount} \u9879\u3002\r\n" +
            $"\u7ea7\u522b\u7edf\u8ba1\uff1aError={errors}\uff0cWarn={warnings}\uff0cInfo={infos}\uff1b\u5206\u7c7b\u6458\u8981\uff1a{categorySummary}\r\n" +
            $"\u7b5b\u9009\uff1a{filterText}\r\n" +
            "\u8bf4\u660e\uff1aError \u901a\u5e38\u9700\u8981\u4fee\u590d\uff1bWarn \u5efa\u8bae\u53d1\u5e03\u524d\u786e\u8ba4\uff1bInfo \u591a\u4e3a\u89c4\u6a21\u6982\u89c8\u3001\u7f3a\u53f7\u63d0\u793a\u6216\u53ef\u9009\u6574\u7406\u5efa\u8bae\u3002\u9009\u4e2d\u4efb\u610f\u884c\u53ef\u67e5\u770b\u8bc1\u636e\u8def\u5f84\u548c\u5904\u7406\u5efa\u8bae\uff0c\u5e76\u53ef\u7528\u201c\u8df3\u5230\u4eba\u7269R/S/\u8df3\u5230\u6570\u636e\u8868/\u8df3\u5230\u8054\u52a8\u9875/\u8df3\u5230SV/\u8df3\u5230\u5730\u5f62/\u8df3\u5230\u5730\u56fe\u201d\u76f4\u63a5\u5b9a\u4f4d\u5173\u8054\u5bf9\u8c61\u3002";
    }

    private void ShowSelectedResourceDiagnostic()
    {
        var item = GetSelectedResourceDiagnosticItem();
        if (item == null) return;

        var navigationTarget = ResolveResourceDiagnosticNavigationTarget(item, ensureScenarioLinks: false);
        UpdateResourceDiagnosticNavigationButtons(navigationTarget);
        _resourceDiagnosticInfoBox.Text =
            $"\u9009\u4e2d\u8bca\u65ad\uff1a[{item.Severity}] {item.Category}/{item.Rule}    \u7f16\u53f7={item.Id}\r\n" +
            $"\u5bf9\u8c61\uff1a{item.Name}    \u72b6\u6001\uff1a{item.Status}\r\n" +
            $"\u8bc1\u636e\uff1a{item.Detail}\r\n" +
            $"\u5efa\u8bae\uff1a{item.Suggestion}\r\n" +
            $"\u8def\u5f84\uff1a{item.Path}\r\n" +
            BuildResourceDiagnosticNavigationText(navigationTarget) +
            BuildRelatedCreatorNotesText("资源诊断", BuildResourceDiagnosticCreatorNoteTargetKey(item));
        SetLastCreatorNoteContext(
            "资源诊断",
            BuildResourceDiagnosticCreatorNoteTargetKey(item),
            $"资源诊断：{item.Category}/{item.Rule}/{item.Name}",
            "从资源诊断页抓取；用于记录处理结论、风险确认和发布前复查。",
            $"诊断级别：{item.Severity}\r\n状态：{item.Status}\r\n证据：{item.Detail}\r\n建议：{item.Suggestion}\r\n处理结论：\r\n实机验证：");
    }

    private static string BuildResourceDiagnosticCreatorNoteTargetKey(ResourceDiagnosticItem item)
        => $"资源诊断#分类={item.Category}#规则={item.Rule}#编号={item.Id}#对象={item.Name}";

    private ResourceDiagnosticNavigationTarget ResolveResourceDiagnosticNavigationTarget(ResourceDiagnosticItem item, bool ensureScenarioLinks)
    {
        if (_project != null && ensureScenarioLinks && _currentGameResources.Count == 0)
        {
            _currentGameResources = _gameResourceIndexer.Index(_project);
            PopulateGameResourceCategoryFilter();
        }

        var links = _currentScenarioMapLinks;
        if (_project != null && ensureScenarioLinks && links.Count == 0)
        {
            links = EnsureScenarioMapLinksForDiagnostics();
        }

        return _resourceDiagnosticNavigationService.Resolve(item, links, _currentGameResources);
    }

    private ResourceDiagnosticNavigationTarget? ResolveSelectedResourceDiagnosticNavigationTarget(bool ensureScenarioLinks)
    {
        var item = GetSelectedResourceDiagnosticItem();
        if (item == null)
        {
            MessageBox.Show(this, "请先在资源诊断页选择一条诊断。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        var target = ResolveResourceDiagnosticNavigationTarget(item, ensureScenarioLinks);
        UpdateResourceDiagnosticNavigationButtons(target);
        return target;
    }

    private void UpdateResourceDiagnosticNavigationButtons(ResourceDiagnosticNavigationTarget target)
    {
        _jumpDiagnosticScenarioMapButton.Enabled = target.CanOpenScenarioMapLink;
        _jumpDiagnosticScenarioButton.Enabled = target.CanJumpScenario;
        _jumpDiagnosticHexzmapButton.Enabled = target.CanJumpHexzmap;
        _jumpDiagnosticMapViewerButton.Enabled = target.CanJumpMapViewer;
        _jumpDiagnosticImageAssignmentButton.Enabled = target.CanJumpImageAssignment;
        _jumpDiagnosticTableCellButton.Enabled = target.CanJumpDataTable;
    }

    private static string BuildResourceDiagnosticNavigationText(ResourceDiagnosticNavigationTarget target)
    {
        if (!target.IsRecognized)
        {
            return "联动定位：暂未识别到可直接跳转对象；可先查看证据路径或用“定位到资源索引”。";
        }

        var actions = new List<string>();
        if (target.CanJumpImageAssignment) actions.Add("跳到人物R/S");
        if (target.CanJumpDataTable) actions.Add("跳到专用编辑");
        if (target.CanOpenScenarioMapLink) actions.Add("跳到联动页");
        if (target.CanJumpScenario) actions.Add("跳到剧本制作");
        if (target.CanJumpHexzmap) actions.Add("跳到地图制作");
        if (target.CanJumpMapViewer) actions.Add("跳到地图");
        return $"联动定位：{target.Summary}；可用按钮：{(actions.Count == 0 ? "暂无直接按钮" : string.Join("、", actions))}。";
    }

    private void JumpSelectedDiagnosticImageAssignment()
    {
        var target = ResolveSelectedResourceDiagnosticNavigationTarget(ensureScenarioLinks: false);
        if (target == null) return;
        if (!target.CanJumpImageAssignment)
        {
            MessageBox.Show(this, "该诊断没有可定位的人物 R/S 形象行。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SelectTabPageByText("形象设定");
        if (_currentImageAssignments == null || _currentImageAssignments.Rows.Count == 0)
        {
            LoadImageAssignments();
        }

        if (_currentImageAssignments == null || _currentImageAssignments.Rows.Count == 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_currentImageAssignments.DefaultView.RowFilter) ||
            !string.IsNullOrWhiteSpace(_imageAssignmentSearchBox.Text) ||
            _imageAssignmentMissingOnlyCheckBox.Checked)
        {
            ClearImageAssignmentFilter();
        }

        var preferredColumn = target.ImageAssignmentPrefix == "S" ? "S形象编号" : "R形象编号";
        var found = false;
        if (target.ImageAssignmentRowId.HasValue)
        {
            found = SelectImageAssignmentRow(row =>
                int.TryParse(Convert.ToString(row["ID"], CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowId) &&
                rowId == target.ImageAssignmentRowId.Value,
                preferredColumn);
        }

        if (!found && target.ImageResourceId.HasValue)
        {
            found = SelectImageAssignmentRow(row =>
                TryGetImageResourceId(row, target.ImageAssignmentPrefix, out var resourceId) &&
                resourceId == target.ImageResourceId.Value,
                preferredColumn);
        }

        if (found)
        {
            ShowSelectedImageAssignmentDetail();
            SetStatus($"已从资源诊断跳转到人物 {target.ImageAssignmentPrefix} 形象：行={target.ImageAssignmentRowId?.ToString(CultureInfo.InvariantCulture) ?? "按资源"}，资源={target.ImageResourceId?.ToString("00", CultureInfo.InvariantCulture) ?? "未指定"}");
            return;
        }

        MessageBox.Show(this,
            target.ImageResourceId.HasValue
                ? $"已打开人物 R/S 形象页，但没有找到引用 {target.ImageAssignmentPrefix}_{target.ImageResourceId.Value:00} 的人物行；该资源可能未被人物表引用。"
                : "已打开人物 R/S 形象页，但没有找到对应人物行。",
            "未找到对应行",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void JumpSelectedDiagnosticTableCell()
    {
        var target = ResolveSelectedResourceDiagnosticNavigationTarget(ensureScenarioLinks: false);
        if (target == null) return;
        if (!target.CanJumpDataTable)
        {
            MessageBox.Show(this, "该诊断没有可定位的数据表行/字段。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (ShowGenericTableEditorPage && SelectDataTableCell(target.TableName, target.TableRowId, target.TableFieldName))
        {
            SetStatus($"已从资源诊断跳转到数据表：{target.TableName} / ID={target.TableRowId} / {target.TableFieldName}");
            return;
        }

        SelectTabPageByText(ResolveCoreAuthoringPageForTable(target.TableName));
        MessageBox.Show(this,
            $"通用数据表编辑入口已移除。请在对应专用编辑页修改。\r\n\r\n目标表：{target.TableName}\r\nID={target.TableRowId} / 字段={target.TableFieldName}",
            "请使用专用编辑页",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void JumpSelectedDiagnosticScenarioMapLink()
    {
        var target = ResolveSelectedResourceDiagnosticNavigationTarget(ensureScenarioLinks: true);
        if (target == null) return;
        if (!target.CanOpenScenarioMapLink)
        {
            MessageBox.Show(this, "该诊断没有可定位的关卡地图联动对象。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SelectTabPageByText("关卡地图联动");
        if (_currentScenarioMapLinks.Count == 0) LoadScenarioMapLinks();
        BindScenarioMapLinkRows(_currentScenarioMapLinks);
        var found = SelectGridRow<ScenarioMapLinkInfo>(_scenarioMapLinkGrid, row =>
            (!string.IsNullOrWhiteSpace(target.ScenarioFileName) && row.ScenarioFileName.Equals(target.ScenarioFileName, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(target.MapId) && row.MapId.Equals(target.MapId, StringComparison.OrdinalIgnoreCase)));
        if (found)
        {
            ShowSelectedScenarioMapLink();
            SetStatus($"已从资源诊断跳转到关卡地图联动：{target.ScenarioFileName}->{target.MapId}");
        }
        else
        {
            MessageBox.Show(this, "关卡地图联动页没有找到对应行。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void JumpSelectedDiagnosticScenario()
    {
        var target = ResolveSelectedResourceDiagnosticNavigationTarget(ensureScenarioLinks: true);
        if (target == null) return;
        if (string.IsNullOrWhiteSpace(target.ScenarioFileName))
        {
            MessageBox.Show(this, "该诊断没有可定位的 R/S eex 或旧 E5S 兼容文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (SelectScenarioFileForNavigation(target.ScenarioFileName))
        {
            SetStatus($"已从资源诊断跳转到剧本制作：{target.ScenarioFileName}");
        }
        else
        {
            SelectTabPageByText("剧本制作");
            MessageBox.Show(this, "剧本制作页没有找到对应 R/S 剧本；如果这是旧 E5S 兼容文件，请在关卡地图联动页继续查看预览和文件定位。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void JumpSelectedDiagnosticHexzmap()
    {
        var target = ResolveSelectedResourceDiagnosticNavigationTarget(ensureScenarioLinks: true);
        if (target == null) return;
        if (string.IsNullOrWhiteSpace(target.MapId) && string.IsNullOrWhiteSpace(target.HexzmapOffsetHex))
        {
            MessageBox.Show(this, "该诊断没有可定位的 Hexzmap 地形块。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SelectTabPageByText("地图制作");
        if (_mapImageList.Items.Count == 0)
        {
            LoadMapImages();
        }

        if (!string.IsNullOrWhiteSpace(target.MapImageName) && SelectMapImageByName(target.MapImageName))
        {
            SetStatus($"已从资源诊断跳转到地图制作：{target.MapImageName}");
        }
        else if (!string.IsNullOrWhiteSpace(target.MapId) && SelectMapImageByName(target.MapId))
        {
            SetStatus($"已从资源诊断跳转到地图制作地形层：{target.MapId}");
        }
        else
        {
            SetStatus($"已打开地图制作，请在地形层中查看 Hexzmap 候选：{target.MapId}");
        }
    }

    private void JumpSelectedDiagnosticMapViewer()
    {
        var target = ResolveSelectedResourceDiagnosticNavigationTarget(ensureScenarioLinks: true);
        if (target == null) return;
        if (!target.CanJumpMapViewer)
        {
            MessageBox.Show(this, "该诊断没有可定位的地图图片。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SelectTabPageByText("地图制作");
        if (_mapImageList.Items.Count == 0) LoadMapImages();
        for (var i = 0; i < _mapImageList.Items.Count; i++)
        {
            if (_mapImageList.Items[i] is not ResourceIndexItem map) continue;
            var byPath = !string.IsNullOrWhiteSpace(target.MapImagePath) && map.Path.Equals(target.MapImagePath, StringComparison.OrdinalIgnoreCase);
            var byName = !string.IsNullOrWhiteSpace(target.MapImageName) && map.Name.Equals(target.MapImageName, StringComparison.OrdinalIgnoreCase);
            var byResource = !string.IsNullOrWhiteSpace(target.ResourcePath) && map.Path.Equals(target.ResourcePath, StringComparison.OrdinalIgnoreCase);
            var byId = !string.IsNullOrWhiteSpace(target.MapId) && map.Id.Equals(target.MapId.TrimStart('M', 'm'), StringComparison.OrdinalIgnoreCase);
            if (!byPath && !byName && !byResource && !byId) continue;

            _mapImageList.SelectedIndex = i;
            LoadSelectedMapImage();
            SetStatus($"已从资源诊断跳转到地图制作：{map.Name}");
            return;
        }

        MessageBox.Show(this, "地图制作列表中没有找到对应地图图片。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ExportResourceDiagnosticsCsv() =>
        ExportGridItemsCsv<ResourceDiagnosticItem>(_resourceDiagnosticGrid, "\u5bfc\u51fa\u8d44\u6e90\u8bca\u65adCSV", "\u8d44\u6e90\u8bca\u65ad\u62a5\u544a.csv", "ResourceDiagnostics", "\u8d44\u6e90\u8bca\u65ad\u62a5\u544a");

    private ResourceDiagnosticItem? GetSelectedResourceDiagnosticItem()
    {
        if (_resourceDiagnosticGrid.SelectedRows.Count > 0 && _resourceDiagnosticGrid.SelectedRows[0].DataBoundItem is ResourceDiagnosticItem selectedItem) return selectedItem;
        if (_resourceDiagnosticGrid.CurrentRow?.DataBoundItem is ResourceDiagnosticItem currentItem) return currentItem;
        return null;
    }

    private void LocateSelectedDiagnosticResourceInIndex()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var diagnostic = GetSelectedResourceDiagnosticItem();
        if (diagnostic == null)
        {
            MessageBox.Show(this, "请先在资源诊断页选择一条诊断。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            if (_currentGameResources.Count == 0)
            {
                _currentGameResources = _gameResourceIndexer.Index(_project);
                PopulateGameResourceCategoryFilter();
            }

            var target = FindResourceForDiagnostic(diagnostic, _currentGameResources);
            if (target == null)
            {
                MessageBox.Show(this,
                    "没有在游戏资源索引中找到与该诊断直接对应的文件。\r\n\r\n" +
                    $"诊断：[{diagnostic.Severity}] {diagnostic.Category}/{diagnostic.Rule}\r\n" +
                    $"对象：{diagnostic.Name}\r\n路径：{diagnostic.Path}",
                    "未找到资源",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            _gameResourceSearchBox.Clear();
            if (_gameResourceCategoryFilterCombo.Items.Count > 0) _gameResourceCategoryFilterCombo.SelectedIndex = 0;
            BindGameResourceRows(_currentGameResources);
            UpdateGameResourceInfo(_currentGameResources.Count, "全部", string.Empty);
            SelectResourceIndexRow(target.Path);
            SelectContainingTab(_gameResourceGrid);
            ShowSelectedGameResourcePreview();
            SetStatus($"已定位诊断对应资源：{target.Category}/{target.Name}");
        }
        catch (Exception ex)
        {
            Log("定位诊断对应资源失败：" + ex);
            MessageBox.Show(this, ex.Message, "定位失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private static ResourceIndexItem? FindResourceForDiagnostic(ResourceDiagnosticItem diagnostic, IReadOnlyList<ResourceIndexItem> resources)
    {
        if (resources.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(diagnostic.Path))
        {
            try
            {
                var fullPath = Path.GetFullPath(diagnostic.Path);
                var exact = resources.FirstOrDefault(x => Path.GetFullPath(x.Path).Equals(fullPath, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;

                var fileName = Path.GetFileName(fullPath);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    var byPathFile = resources.FirstOrDefault(x => Path.GetFileName(x.Path).Equals(fileName, StringComparison.OrdinalIgnoreCase));
                    if (byPathFile != null) return byPathFile;
                }
            }
            catch
            {
                // 某些诊断 Path 可能是说明文字而非真实路径，继续用名称/编号匹配。
            }
        }

        var byName = resources.FirstOrDefault(x => x.Name.Equals(diagnostic.Name, StringComparison.OrdinalIgnoreCase));
        if (byName != null) return byName;

        if (!string.IsNullOrWhiteSpace(diagnostic.Id))
        {
            var byId = resources.FirstOrDefault(x =>
                x.Id.Equals(diagnostic.Id, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(diagnostic.Category) || ContainsKeyword(diagnostic.Category, x.Category) || ContainsKeyword(x.Category, diagnostic.Category)));
            if (byId != null) return byId;
        }

        return resources.FirstOrDefault(x => ContainsKeyword(diagnostic.Detail, x.Name) || ContainsKeyword(diagnostic.Suggestion, x.Name));
    }

    private void SelectResourceIndexRow(string path)
    {
        foreach (DataGridViewRow row in _gameResourceGrid.Rows)
        {
            if (row.DataBoundItem is not ResourceIndexItem item) continue;
            if (!item.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) continue;
            row.Selected = true;
            if (row.Cells.Count > 0)
            {
                _gameResourceGrid.CurrentCell = row.Cells[0];
            }
            if (row.Index >= 0)
            {
                _gameResourceGrid.FirstDisplayedScrollingRowIndex = row.Index;
            }
            return;
        }
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

    private static int ResourceDiagnosticSeverityRank(string severity) => severity switch
    {
        "Error" => 3,
        "Warn" => 2,
        "Info" => 1,
        _ => 0
    };

    private ResourceIndexItem? GetSelectedGameResourceItem()
    {
        if (_gameResourceGrid.SelectedRows.Count > 0 && _gameResourceGrid.SelectedRows[0].DataBoundItem is ResourceIndexItem selectedItem) return selectedItem;
        if (_gameResourceGrid.CurrentRow?.DataBoundItem is ResourceIndexItem currentItem) return currentItem;
        return null;
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
            Log($"\u5df2\u5bfc\u51fa{logName}\uff1a{dialog.FileName}\uff0c\u884c\u6570 {rows.Count}");
            SetStatus($"{logName}\u5bfc\u51fa\u5b8c\u6210\uff1a{rows.Count} \u884c");
        }
        catch (Exception ex)
        {
            Log($"\u5bfc\u51fa{logName}\u5931\u8d25\uff1a" + ex);
            MessageBox.Show(this, ex.Message, "\u5bfc\u51fa\u5931\u8d25", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowSelectedGameResourcePreview()
    {
        if (_gameResourceGrid.SelectedRows.Count == 0) return;
        if (_gameResourceGrid.SelectedRows[0].DataBoundItem is not ResourceIndexItem item) return;

        var targetKey = $"{item.Category}/{item.Name}";
        SetLastCreatorNoteContext(
            "游戏资源",
            targetKey,
            $"{item.Category}：{item.Name}",
            "从游戏资源索引页抓取。",
            $"路径：{item.Path}\r\n格式：{item.FormatHint}\r\n中文注释：{item.Annotation}\r\n用途：\r\n替换/验证记录：");
        _gameResourceInfoBox.Text =
            $"\u9009\u4e2d\u8d44\u6e90\uff1a{item.Category}/{item.Name}    ID={item.Id}    \u5927\u5c0f={item.SizeBytes:N0} \u5b57\u8282\r\n" +
            $"\u683c\u5f0f\uff1a{item.FormatHint}    Magic\uff1a{item.Magic}\r\n" +
            $"\u8def\u5f84\uff1a{item.Path}\r\n" +
            $"\u4e2d\u6587\u6ce8\u91ca\uff1a{item.Annotation}" +
            BuildRelatedCreatorNotesText("游戏资源", targetKey);

        var old = _gameResourcePreview.Image;
        _gameResourcePreview.Image = null;
        old?.Dispose();

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
            _gameResourcePreview.Image = new Bitmap(source);
            SetStatus($"资源预览：{item.Category}/{item.Name}");
        }
        catch (Exception ex)
        {
            Log($"资源预览失败：{item.Path} {ex.Message}");
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
            Log($"已读取 EEX 资源探针：{_currentEexArchives.Count} 个文件。");
            SetStatus("EEX 资源探针读取完成");
        }
        catch (Exception ex)
        {
            _eexArchiveInfoBox.Text = ex.ToString();
            Log("EEX 资源探针读取失败：" + ex);
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
        ConfigureEexArchiveGrid();
        HighlightRowsWithCreatorNotes<EexArchiveInfo>(
            _eexArchiveGrid,
            item => ("EEX资源", $"{item.Category}/{item.FileName}"));
    }

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
            Log($"已生成 EEX 区段探针：{item.FileName}，行 {_currentEexEntryProbeRows.Count}。");
            SetStatus($"EEX 区段探针完成：{item.FileName}");
        }
        catch (Exception ex)
        {
            _eexArchiveInfoBox.Text = ex.ToString();
            Log("EEX 区段探针失败：" + ex);
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
        ConfigureEexEntryProbeGrid();
        HighlightRowsWithCreatorNotes<EexEntryProbeRow>(_eexEntryProbeGrid, row => ("EEX区段", BuildEexEntryProbeCreatorNoteTargetKey(row)));
    }

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

    private static string BuildEexEntryProbeCreatorNoteTargetKey(EexEntryProbeRow row)
        => $"EexEntry#File={row.FileName}#Category={row.Category}#Index={row.Index}#Offset={row.OffsetHex}";

    private void ShowSelectedEexEntryProbeRow()
    {
        var row = GetSelectedEexEntryProbeRow();
        if (row == null)
        {
            return;
        }

        var targetKey = BuildEexEntryProbeCreatorNoteTargetKey(row);
        SetLastCreatorNoteContext(
            "EEX区段",
            targetKey,
            $"{row.FileName} 区段 #{row.Index}",
            "从 EEX 区段探针选中行抓取；当前只读，不解包、不重封包。",
            $"文件：{row.Category}/{row.FileName}\r\n节点：{row.NodeType} #{row.Index}\r\n偏移：{row.OffsetHex}\r\n长度：{row.Length}B\r\n角色候选：{row.RoleHint}\r\n文本线索：{row.TextHints}\r\n中文注释：{row.Annotation}\r\n用途/研究结论：\r\n风险/待核对：");

        var detail = _eexEntryTreeDetailService.BuildDetail(row);
        _eexEntryTreeInfoBox.Text =
            detail +
            "\r\n\r\n创作者备注目标：EEX区段 / " + targetKey +
            "\r\n安全边界：本对象是 EEX 内部区段/头字段的只读证据；当前只用于理解动作、帧表、文本或压缩载荷候选，不直接改写封包。" +
            BuildRelatedCreatorNotesText("EEX区段", targetKey);
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
            Log($"已生成 EEX 跨文件对比：{item.FileName}，行 {_currentEexCrossFileComparison.Rows.Count}。");
            SetStatus($"EEX 跨文件对比完成：{item.FileName}");
        }
        catch (Exception ex)
        {
            _eexCrossFileInfoBox.Text = ex.ToString();
            Log("EEX 跨文件对比失败：" + ex);
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
        ConfigureEexCrossFileGrid();
        HighlightRowsWithCreatorNotes<EexCrossFileComparisonRow>(_eexCrossFileGrid, row => ("EEX跨文件对比", BuildEexCrossFileCreatorNoteTargetKey(row)));
    }

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

    private string BuildEexCrossFileCreatorNoteTargetKey(EexCrossFileComparisonRow row)
    {
        var baseFileName = _currentEexCrossFileComparison?.TargetFileName;
        if (string.IsNullOrWhiteSpace(baseFileName))
        {
            baseFileName = GetSelectedEexArchiveItem()?.FileName ?? "未知EEX";
        }

        return $"EexCross#Base={baseFileName}#PeerKind={row.PeerKind}#Category={row.Category}#File={row.FileName}#Role={row.RoleHint}";
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

        var targetKey = BuildEexCrossFileCreatorNoteTargetKey(row);
        SetLastCreatorNoteContext(
            "EEX跨文件对比",
            targetKey,
            $"EEX跨文件：{_currentEexCrossFileComparison.TargetFileName} -> {row.FileName}",
            "从 EEX 跨文件对比选中行抓取；当前只读，不解包、不重封包。",
            $"基准：{_currentEexCrossFileComparison.TargetCategory}/{_currentEexCrossFileComparison.TargetFileName}\r\n对比对象：{row.Category}/{row.FileName}\r\n关系：{row.PeerKind}\r\n角色候选：{row.RoleHint}\r\n差异：{row.DifferenceHint}\r\n中文注释：{row.Annotation}\r\n用途/研究结论：\r\n风险/待核对：");

        var annotation = string.IsNullOrWhiteSpace(row.Annotation)
            ? "暂无自动注释；建议结合相邻 R/S、同编号文件和实机动作表现补充创作者备注。"
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
            _currentEexCrossFileComparison.Summary +
            BuildRelatedCreatorNotesText("EEX跨文件对比", targetKey);

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
            var old = _eexByteHeatmapBox.Image;
            _eexByteHeatmapBox.Image = bitmap;
            old?.Dispose();
            _currentEexByteHeatmap = result;
            _eexByteHeatmapInfoBox.Text = BuildEexHeatmapInfoText(result);
            Log($"已生成 EEX 字节热力图：{result.FileName} {result.OffsetHex}-{result.EndOffsetHex}，单元 {result.CellCount}。");
            SetStatus($"EEX 字节热力图完成：{result.FileName}");
        }
        catch (Exception ex)
        {
            _eexByteHeatmapInfoBox.Text = ex.ToString();
            Log("EEX 字节热力图生成失败：" + ex);
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
            if (_eexByteHeatmapBox.Image != null)
            {
                _eexByteHeatmapBox.Image.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);
            }
            else
            {
                using var bitmap = _eexByteHeatmapService.Render(_currentEexByteHeatmap);
                bitmap.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);
            }
            Log($"已导出 EEX 字节热力图：{dialog.FileName}");
            SetStatus("EEX 字节热力图 PNG 导出完成");
        }
        catch (Exception ex)
        {
            Log("导出 EEX 字节热力图失败：" + ex);
            MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ClearEexHeatmapPreview()
    {
        _currentEexByteHeatmap = null;
        var old = _eexByteHeatmapBox.Image;
        _eexByteHeatmapBox.Image = null;
        old?.Dispose();
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
        SetLastCreatorNoteContext(
            "EEX资源",
            targetKey,
            $"{item.Category}：{item.FileName}",
            "从 EEX 资源探针页抓取；当前只读，不重封包。",
            $"路径：{item.Path}\r\nID：{item.Id}\r\n条目候选：{item.EntryCount}\r\n研究记录：");
        _eexArchiveInfoBox.Text =
            $"文件：{item.FileName}    分类：{item.Category}    ID：{item.Id}    长度：{item.Length:N0} 字节\r\n" +
            $"路径：{item.Path}\r\n" +
            $"Magic：{(item.MagicValid ? "EEX\\0 OK" : "异常")}    Version：{item.VersionHex}    EntryCount(疑似)：{item.EntryCount}\r\n" +
            $"Header14={item.Header14Hex}    Header18={item.Header18Hex}    Header22={item.Header22Hex}    Header26={item.Header26Hex}\r\n" +
            $"文本线索({item.TextHintCount})：{item.TextHints}" +
            BuildRelatedCreatorNotesText("EEX资源", targetKey);
        SetStatus($"EEX：{item.Category}/{item.FileName}");
    }
}
