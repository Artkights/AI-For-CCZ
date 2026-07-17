using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Globalization;

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

        if (_tables.Count == 0) ReloadCurrentProject();
        if (SelectDataTableCell(tableName, "0", "ID"))
        {
            SetStatus($"已打开核心表：{tableName}");
        }
    }

    private void OpenCoreImageAssignments()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SelectTabPageByText("形象设定");
        LoadImageAssignments();
    }

    private void OpenCoreImageResources()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SelectTabPageByText("图片设定");
        LoadImageResources();
    }

    private static string ResolveCoreAuthoringPageForTable(string tableName)
    {
        if (tableName.Contains("人物", StringComparison.Ordinal)) return "角色设定";
        if (tableName.Contains("兵种", StringComparison.Ordinal) || tableName.Contains("策略", StringComparison.Ordinal)) return "兵种设定";
        if (tableName.Contains("物品", StringComparison.Ordinal) || tableName.Contains("宝物", StringComparison.Ordinal)) return "宝物设定";
        if (tableName.Contains("商店", StringComparison.Ordinal)) return "商店编辑";
        return "角色设定";
    }

    private bool SelectDataTableCell(string tableName, string targetRowId, string fieldName)
    {
        if (!ShowGenericTableEditorPage) return false;

        SelectTabPageByText("数据表编辑");
        if (_project == null || !HexTableNameResolver.TryResolveForProject(_project, _tables, tableName, out var table)) return false;

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
            TryScrollGridRowIntoView(_dataGrid, row.Index);
            ShowSelectedDataCellAnnotation(row.Index, cell.ColumnIndex);
            return true;
        }

        return false;
    }

    private bool SelectScenarioFileForNavigation(string fileName)
    {
        SelectTabPageByText("剧本编辑");
        if (_scriptScenarioCombo.Items.Count == 0) LoadScriptScenarios();

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

            if (!IsCurrentScriptScenarioLoaded(fileName))
            {
                LoadSelectedScriptScenario();
            }

            return true;
        }

        return false;
    }

    internal static bool SelectGridRow<T>(DataGridView grid, Func<T, bool> predicate)
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.DataBoundItem is not T item || !predicate(item)) continue;
            grid.ClearSelection();
            row.Selected = true;
            var firstVisibleCell = row.Cells.Cast<DataGridViewCell>().FirstOrDefault(cell => cell.Visible);
            if (firstVisibleCell != null) grid.CurrentCell = firstVisibleCell;
            TryScrollGridRowIntoView(grid, row.Index);
            return true;
        }

        return false;
    }

    private bool SelectScenarioStructureTreeNode(ScenarioStructureRow row)
    {
        foreach (TreeNode node in _scenarioStructureTree.Nodes)
        {
            var found = FindScenarioStructureTreeNode(node, row.Index);
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

    private static void HideNonAuthoringColumns(DataGridView grid, params string[] names)
    {
        foreach (var name in names)
        {
            if (grid.Columns.Contains(name)) grid.Columns[name]!.Visible = false;
        }
    }


    private bool SelectTabPageByText(string text) => SelectTabPageByText(this, text);

    private static bool SelectTabPageByText(Control root, string text)
    {
        foreach (Control control in root.Controls)
        {
            if (control is TabControl tabControl)
            {
                foreach (TabPage page in tabControl.TabPages)
                {
                    if (page.Text.Equals(text, StringComparison.Ordinal))
                    {
                        tabControl.SelectedTab = page;
                        return true;
                    }
                }
            }

            if (SelectTabPageByText(control, text)) return true;
        }

        return false;
    }
}
