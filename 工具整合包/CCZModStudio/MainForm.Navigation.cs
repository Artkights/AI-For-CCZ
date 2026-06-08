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
    private void OpenCoreTable(string tableName)
    {
        if (!ShowGenericTableEditorPage)
        {
            MessageBox.Show(
                this,
                $"通用数据表编辑入口已从主界面移除。请使用角色、兵种、宝物、商店等专用编辑页修改相关内容。\r\n\r\n目标表：{tableName}",
                "通用表入口已移除",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (_project == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_tables.Count == 0)
        {
            ReloadCurrentProject();
        }

        if (!SelectDataTableCell(tableName, "0", "ID"))
        {
            MessageBox.Show(this, $"没有找到可打开的表：{tableName}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetStatus($"已打开核心表：{tableName}");
    }

    private void OpenCoreImageAssignments()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SelectTabPageByText("图片处理");
        SelectTabPageByText("人物R/S指定");
        LoadImageAssignments();
    }

    private void OpenCoreImageResources()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SelectTabPageByText("图片处理");
        SelectTabPageByText("图片资源");
        LoadImageResources();
    }

    private static string ResolveCoreAuthoringPageForTable(string tableName)
    {
        if (tableName.Contains("人物", StringComparison.Ordinal)) return "角色设定";
        if (tableName.Contains("兵种", StringComparison.Ordinal) ||
            tableName.Contains("策略", StringComparison.Ordinal)) return "兵种设定";
        if (tableName.Contains("物品", StringComparison.Ordinal) ||
            tableName.Contains("宝物", StringComparison.Ordinal)) return "宝物设定";
        if (tableName.Contains("商店", StringComparison.Ordinal)) return "商店编辑";
        return "核心创作";
    }

    private bool SelectDataTableCell(string tableName, string targetRowId, string fieldName)
    {
        if (!ShowGenericTableEditorPage)
        {
            return false;
        }

        SelectTabPageByText("数据表编辑");
        if (!HexTableNameResolver.TryResolve(_tables, tableName, out var table)) return false;

        if (!_tableList.Items.Cast<object>().OfType<HexTableDefinition>().Any(x => x.Id == table.Id))
        {
            _showAllTables.Checked = true;
            RefreshTableList();
        }

        _tableList.SelectedItem = _tableList.Items.Cast<object>().OfType<HexTableDefinition>().FirstOrDefault(x => x.Id == table.Id) ?? table;
        LoadSelectedTable();
        if (_currentTableResult == null || !_dataGrid.Columns.Contains(fieldName)) return false;

        foreach (DataGridViewRow row in _dataGrid.Rows)
        {
            if (row.IsNewRow) continue;
            var currentRowId = row.Cells.Count > 0 ? Convert.ToString(row.Cells[0].Value, CultureInfo.InvariantCulture) : string.Empty;
            if (!string.Equals(currentRowId, targetRowId, StringComparison.Ordinal)) continue;
            _dataGrid.ClearSelection();
            var cell = row.Cells[fieldName];
            _dataGrid.CurrentCell = cell;
            cell.Selected = true;
            if (row.Index >= 0 && row.Index < _dataGrid.RowCount) _dataGrid.FirstDisplayedScrollingRowIndex = row.Index;
            ShowSelectedDataCellAnnotation(row.Index, cell.ColumnIndex);
            return true;
        }

        return false;
    }

    private bool NavigateToScenarioCommandTarget(CreatorNoteNavigationTarget target)
    {
        if (!SelectScenarioFileForNavigation(target.FileName)) return false;
        if (_currentScriptStructure == null) return false;
        var row = _currentScriptStructure.Rows.FirstOrDefault(row =>
            row.NodeType == "Command候选" &&
            (!target.CommandIndex.HasValue || row.CommandIndex == target.CommandIndex.Value) &&
            (string.IsNullOrWhiteSpace(target.OffsetHex) || HexOffsetEquals(row.OffsetHex, target.OffsetHex)));
        if (row == null) return false;

        var found = SelectScriptTreeNode(row, suppressEvents: true);
        _selectedScriptCommandRow = row;
        _selectedScriptTextEntry = null;
        BindScriptParameterRows(BuildScriptParameterRows(row));
        _scriptTextEditorBox.Clear();
        UpdateScriptTextCapacityLabel();
        _scriptDetailBox.Text = BuildScriptRowDetail(row);
        _scriptPreviewBox.Text = BuildScriptCommandPreview(
            row,
            BuildScriptParameterRows(row),
            GetScriptTextsForRows(new[] { row }, _currentScriptTextEntries).ToList());
        UpdateScriptImagePreview(row);
        SetStatus($"已定位到剧本制作命令：{target.FileName} {row.OffsetHex}");
        return found;
    }

    private bool NavigateToScenarioTextTarget(CreatorNoteNavigationTarget target)
    {
        if (!SelectScenarioFileForNavigation(target.FileName)) return false;
        if (_currentScriptTextEntries.Count == 0) return false;

        var entry = _currentScriptTextEntries.FirstOrDefault(row =>
            (!target.TextIndex.HasValue || row.Index == target.TextIndex.Value) &&
            (string.IsNullOrWhiteSpace(target.OffsetHex) || HexOffsetEquals(row.OffsetHex, target.OffsetHex)));
        if (entry == null) return false;

        var selectedInTree = SelectScriptTextTreeNode(entry, suppressEvents: true);
        var relatedRows = _currentScriptStructure == null
            ? Array.Empty<ScenarioStructureRow>()
            : GetScriptCommandsForText(entry).ToArray();
        SuppressScriptSelectionEvents(() =>
        {
            BindScriptTextRows(new[] { entry });
            BindScriptCommandRows(relatedRows);
            SelectScriptTextEntry(entry, showSelection: false);
        });
        _selectedScriptTextEntry = entry;
        _selectedScriptCommandRow = null;
        _scriptDetailBox.Text = BuildScriptTextDetail(entry);
        _scriptPreviewBox.Text = BuildScriptTextPreview(entry, relatedRows);
        SetStatus($"已定位到剧本制作文本：{target.FileName} {entry.OffsetHex}");
        return selectedInTree || _scriptTextGrid.CurrentRow?.DataBoundItem is ScenarioTextEntry;
    }

    private bool NavigateToGameResourceTarget(CreatorNoteNavigationTarget target)
    {
        SelectTabPageByText("游戏资源索引");
        if (_currentGameResources.Count == 0) IndexGameResources();
        BindGameResourceRows(_currentGameResources);
        var found = SelectGridRow<ResourceIndexItem>(_gameResourceGrid, row =>
            row.Category.Equals(target.Category, StringComparison.OrdinalIgnoreCase) &&
            row.Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase));
        if (found) ShowSelectedGameResourcePreview();
        return found;
    }

    private bool NavigateToResourceDiagnosticTarget(CreatorNoteNavigationTarget target)
    {
        SelectTabPageByText("资源诊断");
        if (_currentResourceDiagnostics.Count == 0) RunResourceDiagnostics();
        BindResourceDiagnosticRows(_currentResourceDiagnostics);
        var found = SelectGridRow<ResourceDiagnosticItem>(_resourceDiagnosticGrid, row =>
            row.Category.Equals(target.Category, StringComparison.OrdinalIgnoreCase) &&
            row.Rule.Equals(target.Rule, StringComparison.OrdinalIgnoreCase) &&
            row.Id.Equals(target.Id, StringComparison.OrdinalIgnoreCase) &&
            row.Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase));
        if (found) ShowSelectedResourceDiagnostic();
        return found;
    }

    private string BuildRelatedCreatorNotesText(string scope, string targetKey)
    {
        if (_currentCreatorNotes.Count == 0 || string.IsNullOrWhiteSpace(targetKey))
        {
            return string.Empty;
        }

        return _creatorNoteRelationService.BuildSummary(_currentCreatorNotes, scope, targetKey);
    }

    private void HighlightRowsWithCreatorNotes<T>(DataGridView grid, Func<T, (string Scope, string TargetKey)> keyFactory)
    {
        if (_currentCreatorNotes.Count == 0) return;

        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.DataBoundItem is not T item) continue;
            var (scope, targetKey) = keyFactory(item);
            var count = _creatorNoteRelationService.CountExact(_currentCreatorNotes, scope, targetKey);
            if (count <= 0) continue;

            row.DefaultCellStyle.BackColor = BlendRowColor(row.DefaultCellStyle.BackColor, Color.FromArgb(232, 245, 255));
            row.DefaultCellStyle.SelectionBackColor = Color.SteelBlue;
            row.HeaderCell.Value = "注";
            var tip = BuildCreatorNoteTip(count);
            row.HeaderCell.ToolTipText = tip;
            foreach (DataGridViewCell cell in row.Cells)
            {
                cell.ToolTipText = tip;
            }
        }
    }

    private static string BuildCreatorNoteTip(int count)
        => $"已有 {count} 条创作者备注。可到“创作者备注”页筛选或直接使用“定位备注目标”。";

    private static Color BlendRowColor(Color current, Color noteColor)
    {
        if (current == Color.Empty || current.ToArgb() == Color.White.ToArgb())
        {
            return noteColor;
        }

        return Color.FromArgb(
            (current.R + noteColor.R) / 2,
            (current.G + noteColor.G) / 2,
            (current.B + noteColor.B) / 2);
    }

    private static void HideNonAuthoringColumns(DataGridView grid, params string[] names)
    {
        foreach (var name in names)
        {
            if (grid.Columns.Contains(name))
            {
                grid.Columns[name]!.Visible = false;
            }
        }
    }

    private bool NavigateToEexResourceTarget(CreatorNoteNavigationTarget target)
    {
        if (!ShowLegacyProbePages)
        {
            return NavigateLegacyProbeNoteToResourceIndex(target);
        }

        SelectTabPageByText("EEX资源探针");
        if (_currentEexArchives.Count == 0) LoadEexArchives();
        BindEexArchiveRows(_currentEexArchives);
        var found = SelectGridRow<EexArchiveInfo>(_eexArchiveGrid, row =>
            row.Category.Equals(target.Category, StringComparison.OrdinalIgnoreCase) &&
            row.FileName.Equals(target.Name, StringComparison.OrdinalIgnoreCase));
        if (found) ShowSelectedEexArchive();
        return found;
    }

    private bool NavigateToEexEntryTarget(CreatorNoteNavigationTarget target)
    {
        if (!ShowLegacyProbePages)
        {
            return NavigateLegacyProbeNoteToResourceIndex(target);
        }

        SelectTabPageByText("EEX资源探针");
        if (_currentEexArchives.Count == 0) LoadEexArchives();
        BindEexArchiveRows(_currentEexArchives);
        var foundArchive = SelectGridRow<EexArchiveInfo>(_eexArchiveGrid, row =>
            row.Category.Equals(target.Category, StringComparison.OrdinalIgnoreCase) &&
            row.FileName.Equals(target.FileName, StringComparison.OrdinalIgnoreCase));
        if (!foundArchive) return false;

        ShowSelectedEexArchive();
        ProbeSelectedEexEntries();
        SelectTabPageByText("区段表格");

        var found = SelectGridRow<EexEntryProbeRow>(_eexEntryProbeGrid, row =>
            row.FileName.Equals(target.FileName, StringComparison.OrdinalIgnoreCase) &&
            row.Category.Equals(target.Category, StringComparison.OrdinalIgnoreCase) &&
            (!target.SectionIndex.HasValue || row.Index == target.SectionIndex.Value) &&
            (string.IsNullOrWhiteSpace(target.OffsetHex) || HexOffsetEquals(row.OffsetHex, target.OffsetHex)));
        if (found) ShowSelectedEexEntryProbeRow();
        return found;
    }

    private bool NavigateToEexCrossFileTarget(CreatorNoteNavigationTarget target)
    {
        if (!ShowLegacyProbePages)
        {
            return NavigateLegacyProbeNoteToResourceIndex(target);
        }

        SelectTabPageByText("EEX资源探针");
        if (_currentEexArchives.Count == 0) LoadEexArchives();
        BindEexArchiveRows(_currentEexArchives);

        var baseFileName = string.IsNullOrWhiteSpace(target.BaseFileName)
            ? target.FileName
            : target.BaseFileName;
        var foundBase = SelectGridRow<EexArchiveInfo>(_eexArchiveGrid, row =>
            row.FileName.Equals(baseFileName, StringComparison.OrdinalIgnoreCase));
        if (!foundBase) return false;

        ShowSelectedEexArchive();
        CompareSelectedEexAcrossFiles();
        SelectTabPageByText("跨文件对比");

        var found = SelectGridRow<EexCrossFileComparisonRow>(_eexCrossFileGrid, row =>
            row.Category.Equals(target.Category, StringComparison.OrdinalIgnoreCase) &&
            row.FileName.Equals(target.FileName, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(target.PeerKind) || row.PeerKind.Equals(target.PeerKind, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(target.RoleHint) || row.RoleHint.Equals(target.RoleHint, StringComparison.OrdinalIgnoreCase)));
        if (found) ShowSelectedEexCrossFileRow();
        return found;
    }

    private bool NavigateToLsResourceTarget(CreatorNoteNavigationTarget target)
    {
        if (!ShowLegacyProbePages)
        {
            return NavigateLegacyProbeNoteToResourceIndex(target);
        }

        SelectTabPageByText("Ls/E5地图资源探针");
        if (_currentLsResources.Count == 0) LoadLsResources();
        BindLsResourceRows(_currentLsResources);
        var found = SelectGridRow<LsResourceInfo>(_lsResourceGrid, row =>
            row.Category.Equals(target.Category, StringComparison.OrdinalIgnoreCase) &&
            row.FileName.Equals(target.Name, StringComparison.OrdinalIgnoreCase));
        if (found) ShowSelectedLsResource();
        return found;
    }

    private bool NavigateToHexzmapTarget(CreatorNoteNavigationTarget target)
    {
        if (!ShowLegacyProbePages)
        {
            return NavigateLegacyHexzmapNoteToMapWorkbench(target);
        }

        SelectTabPageByText("Hexzmap地形探针");
        if (_currentHexzmapProbe == null) LoadHexzmapProbe();
        if (_currentHexzmapProbe == null) return false;
        var found = SelectGridRow<HexzmapBlockInfo>(_hexzmapGrid, row =>
            row.MapId.Equals(target.MapId, StringComparison.OrdinalIgnoreCase) ||
            HexOffsetEquals(row.OffsetHex, target.OffsetHex));
        if (found) ShowSelectedHexzmapBlock();
        return found;
    }

    private bool NavigateLegacyProbeNoteToResourceIndex(CreatorNoteNavigationTarget target)
    {
        SelectTabPageByText("游戏资源索引");
        if (_currentGameResources.Count == 0) IndexGameResources();
        BindGameResourceRows(_currentGameResources);
        var fileName = string.IsNullOrWhiteSpace(target.FileName)
            ? target.Name
            : target.FileName;
        var found = SelectGridRow<ResourceIndexItem>(_gameResourceGrid, row =>
            (!string.IsNullOrWhiteSpace(target.Category) && row.Category.Equals(target.Category, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(fileName) && row.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(fileName) && row.Path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)));
        if (found)
        {
            ShowSelectedGameResourcePreview();
        }
        else
        {
            SetStatus("旧探针备注已改为跳转到游戏资源索引；未找到完全匹配的资源行。");
        }

        return found;
    }

    private bool NavigateLegacyHexzmapNoteToMapWorkbench(CreatorNoteNavigationTarget target)
    {
        SelectTabPageByText("地图制作");
        if (_mapImageList.Items.Count == 0) LoadMapImages();
        var mapId = target.MapId.TrimStart('M', 'm');
        for (var i = 0; i < _mapImageList.Items.Count; i++)
        {
            if (_mapImageList.Items[i] is not ResourceIndexItem map) continue;
            if (!map.Id.Equals(mapId, StringComparison.OrdinalIgnoreCase) &&
                !map.Name.Contains(target.MapId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _mapImageList.SelectedIndex = i;
            LoadSelectedMapImage();
            return true;
        }

        SetStatus("旧 Hexzmap 备注已改为跳转到地图制作；未找到同编号地图。");
        return false;
    }

    private bool NavigateToScenarioMapLinkTarget(CreatorNoteNavigationTarget target)
    {
        SelectTabPageByText("关卡地图联动");
        if (_currentScenarioMapLinks.Count == 0) LoadScenarioMapLinks();
        BindScenarioMapLinkRows(_currentScenarioMapLinks);
        var found = SelectGridRow<ScenarioMapLinkInfo>(_scenarioMapLinkGrid, row =>
            row.ScenarioFileName.Equals(target.FileName, StringComparison.OrdinalIgnoreCase) &&
            row.MapId.Equals(target.MapId, StringComparison.OrdinalIgnoreCase));
        if (found) ShowSelectedScenarioMapLink();
        return found;
    }

    private bool NavigateToProjectDiffTarget(CreatorNoteNavigationTarget target)
    {
        SelectTabPageByText("测试副本差异/发布");
        if (_currentProjectDiffItems.Count == 0) AnalyzeProjectDiff();
        BindProjectDiffRows(_currentProjectDiffItems);
        return SelectGridRow<ProjectDiffItem>(_projectDiffGrid, row =>
            row.RelativePath.Equals(target.RelativePath, StringComparison.OrdinalIgnoreCase));
    }

    private bool NavigateToBackupTarget(CreatorNoteNavigationTarget target)
    {
        SelectTabPageByText("备份历史/回滚");
        if (_currentBackupHistoryItems.Count == 0) LoadBackupHistory();
        BindBackupHistoryRows(_currentBackupHistoryItems);
        return SelectGridRow<BackupHistoryItem>(_backupHistoryGrid, row =>
            row.TargetRelativePath.Equals(target.RelativePath, StringComparison.OrdinalIgnoreCase));
    }

    private bool SelectScenarioFileForNavigation(string fileName)
    {
        SelectTabPageByText("剧本制作");
        if (_scriptScenarioCombo.Items.Count == 0)
        {
            LoadScriptScenarios();
        }

        for (var i = 0; i < _scriptScenarioCombo.Items.Count; i++)
        {
            if (_scriptScenarioCombo.Items[i] is not ScenarioFileInfo scenario ||
                !scenario.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (_scriptScenarioCombo.SelectedIndex != i)
            {
                _updatingScriptScenarioSelection = true;
                try
                {
                    _scriptScenarioCombo.SelectedIndex = i;
                }
                finally
                {
                    _updatingScriptScenarioSelection = false;
                }
            }

            if (_currentScriptScenario == null ||
                !_currentScriptScenario.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase) ||
                _currentScriptStructure == null)
            {
                LoadSelectedScriptScenario();
            }

            return _currentScriptScenario != null &&
                   _currentScriptScenario.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase) &&
                   _currentScriptStructure != null;
        }

        return false;
    }

    private bool SelectScenarioStructureTreeNode(ScenarioStructureRow row)
    {
        foreach (TreeNode root in _scenarioStructureTree.Nodes)
        {
            var found = FindScenarioStructureTreeNode(root, row.Index);
            if (found == null) continue;
            _scenarioStructureTree.SelectedNode = found;
            found.EnsureVisible();
            return true;
        }

        return false;
    }

    private static TreeNode? FindScenarioStructureTreeNode(TreeNode node, int rowIndex)
    {
        if (node.Tag is ScenarioStructureRow row && row.Index == rowIndex) return node;
        foreach (TreeNode child in node.Nodes)
        {
            var found = FindScenarioStructureTreeNode(child, rowIndex);
            if (found != null) return found;
        }

        return null;
    }

    private static bool SelectGridRow<T>(DataGridView grid, Func<T, bool> predicate)
    {
        grid.ClearSelection();
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.DataBoundItem is not T item || !predicate(item)) continue;
            row.Selected = true;
            var cell = row.Cells.Cast<DataGridViewCell>().FirstOrDefault(x => x.Visible);
            if (cell != null) grid.CurrentCell = cell;
            if (row.Index >= 0 && row.Index < grid.RowCount) grid.FirstDisplayedScrollingRowIndex = row.Index;
            return true;
        }

        return false;
    }

    private bool SelectTabPageByText(string text) => SelectTabPageByText(this, text);

    private static bool SelectTabPageByText(Control root, string text)
    {
        if (text.Equals("形象设定", StringComparison.Ordinal))
        {
            text = "图片处理";
        }

        foreach (Control control in root.Controls)
        {
            if (control is TabControl tabs)
            {
                var direct = tabs.TabPages.Cast<TabPage>().FirstOrDefault(page => page.Text.Equals(text, StringComparison.Ordinal));
                direct ??= tabs.TabPages.Cast<TabPage>().FirstOrDefault(page => page.Text.Contains(text, StringComparison.Ordinal));
                if (direct != null)
                {
                    tabs.SelectedTab = direct;
                    return true;
                }

                foreach (TabPage page in tabs.TabPages)
                {
                    if (!SelectTabPageByText(page, text)) continue;
                    tabs.SelectedTab = page;
                    return true;
                }
            }

            if (SelectTabPageByText(control, text)) return true;
        }

        return false;
    }
}
