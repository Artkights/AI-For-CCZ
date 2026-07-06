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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CCZModStudio;

public sealed partial class MainForm
{
    internal static Action<LegacyScenarioCommandNode>? ScriptCommandEditInterceptForSmoke { get; set; }

    private async Task LoadScriptScenariosAsync()
    {
        if (_loadingScriptScenarioList) return;
        if (_project == null)
        {
            MessageBox.Show(this, "\u8bf7\u5148\u52a0\u8f7d\u9879\u76ee\u3002", "\u63d0\u793a", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _loadingScriptScenarioList = true;
            _loadScriptButton.Enabled = false;
            _scriptScenarioCombo.Enabled = false;
            Cursor = Cursors.WaitCursor;
            SetStatus("\u5267\u672c\u5236\u4f5c\uff1a\u6b63\u5728\u8bfb\u53d6\u5267\u672c\u7d22\u5f15...");
            var previousScriptDetailText = _scriptDetailBox.Text;
            var previousScriptPreviewText = _scriptPreviewBox.Text;
            var previousScriptHeaderText = _scriptHeaderLabel.Text;
            _scriptDetailBox.Text = "\u6b63\u5728\u8bfb\u53d6 RS \u76ee\u5f55 R/S eex \u5267\u672c\u7d22\u5f15\u3002\u4e3a\u907f\u514d\u754c\u9762\u5361\u6b7b\uff0c\u672c\u6b65\u9aa4\u53ea\u8bfb\u53d6\u6587\u4ef6\u540d\u3001\u7f16\u53f7\u548c\u957f\u5ea6\uff1b\u9009\u62e9\u5177\u4f53\u5267\u672c\u540e\u518d\u89e3\u6790\u547d\u4ee4\u6811\u4e0e\u6587\u672c\u3002";
            await Task.Yield();

            var project = _project;
            var rows = await Task.Run(() => new ScenarioFileReader()
                .ReadAllIndex(project)
                .Where(x => ScenarioFileReader.IsRsScriptFile(x.FileName))
                // 按传统流程排序：R_00..R_nn 在前，S_00..S_nn 在后；同类内按数字排序。
                .OrderBy(x => ScenarioFileReader.IsBattlefieldScriptFile(x.FileName) ? 1 : 0)
                .ThenBy(x => int.TryParse(x.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : int.MaxValue)
                .ThenBy(x => x.FileName, StringComparer.CurrentCultureIgnoreCase)
                .ToList());

            var preservedCurrentScenario = BindScriptScenarioIndexRows(rows);

            if (!preservedCurrentScenario)
            {
                ClearScriptDocumentView();
            _scriptDetailBox.Text = rows.Count > 0
                ? $"\u5df2\u8bfb\u53d6\u5267\u672c\u7d22\u5f15\uff1a{rows.Count} \u4e2a\u3002\u8bf7\u5728\u4e0a\u65b9\u4e0b\u62c9\u6846\u9009\u62e9\u8981\u7f16\u8f91/\u67e5\u770b\u7684 R/S eex \u5267\u672c\uff1b\u9009\u62e9\u540e\u4f1a\u540e\u53f0\u89e3\u6790\u547d\u4ee4\u6811\u548c\u6587\u672c\u3002"
                : "\u5267\u672c\u5236\u4f5c\uff1a\u6ca1\u6709\u627e\u5230 R/S eex \u5267\u672c\u6587\u4ef6\u3002";
            _scriptPreviewBox.Text = rows.Count > 0
                ? $"剧本列表：{rows.Count} 个\r\n选择上方剧本后加载事件树。"
                : "未找到 R/S eex 剧本文件。";
            _scriptHeaderLabel.Text = $"字典：{(_currentSceneStringDocument == null ? "未加载" : "已加载")}    剧本：未选择    列表：{rows.Count}";
            }
            else
            {
                _scriptDetailBox.Text = previousScriptDetailText;
                _scriptPreviewBox.Text = previousScriptPreviewText;
                _scriptHeaderLabel.Text = previousScriptHeaderText;
            }
            SetStatus($"\u5267\u672c\u5236\u4f5c\uff1a\u5df2\u8bfb\u53d6\u5267\u672c\u7d22\u5f15 {rows.Count} \u4e2a");
        }
        catch (Exception ex)
        {
            _updatingScriptScenarioSelection = false;
            _scriptDetailBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("Load script scenario index failed: " + ex);
            MessageBox.Show(this, ex.Message, "\u8bfb\u53d6\u5267\u672c\u7d22\u5f15\u5931\u8d25", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _loadingScriptScenarioList = false;
            _loadScriptButton.Enabled = true;
            _scriptScenarioCombo.Enabled = true;
            Cursor = Cursors.Default;
        }
    }

    private async Task LoadSelectedScriptScenarioAsync()
    {
        if (_updatingScriptScenarioSelection || _loadingScriptScenarioDocument) return;
        if (_project == null) return;
        if (_scriptScenarioCombo.SelectedItem is not ScenarioFileInfo scenario) return;

        try
        {
            _loadingScriptScenarioDocument = true;
            _scriptScenarioCombo.Enabled = false;
            Cursor = Cursors.WaitCursor;
            SetStatus($"\u5267\u672c\u5236\u4f5c\uff1a\u6b63\u5728\u8bfb\u53d6 {scenario.FileName}...");
            await Task.Yield();
            var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe();
            if (dictionary == null)
            {
                MessageBox.Show(this, $"未找到 CczString.ini：{ProjectDetector.FindSceneDictionaryPath(_project)}\r\n\r\n无法按传统命令名生成剧本树。请把字典放到当前项目的 a新剧本编辑器v0.23 目录，或确认工具内置 LegacyResources 备份未被删除。", "缺少剧本字典", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            PopulateScriptNewCommandCombo(dictionary);

            var project = _project;
            var tables = _tables;
            var result = await Task.Run(() =>
            {
                LegacyScenarioDocument? legacy = null;
                try
                {
                    legacy = new LegacyScenarioReader().Read(scenario.Path, dictionary);
                }
                catch
                {
                    // Non-standard files still fall back to the existing probe workflow.
                }

                if (legacy != null)
                {
                    return (
                        Legacy: legacy,
                        Structure: (ScenarioStructureProbeResult?)null,
                        Texts: (IReadOnlyList<ScenarioTextEntry>?)null);
                }

                var structure = new ScenarioStructureProbeReader().Build(scenario.Path, dictionary, maxCommandRows: 600, project: project, tables: tables);
                var texts = new ScenarioTextReader().Read(scenario.Path);
                return (
                    Legacy: (LegacyScenarioDocument?)null,
                    Structure: structure,
                    Texts: (IReadOnlyList<ScenarioTextEntry>)texts);
            });

            var refreshDefaultSelectionAfterLoad = false;
            _bindingScriptDocument = true;
            SuspendLayout();
            _scriptTree.SuspendLayout();
            _scriptCommandGrid.SuspendLayout();
            _scriptParameterGrid.SuspendLayout();
            _scriptTextGrid.SuspendLayout();
            _scriptSearchResultGrid.SuspendLayout();
            try
            {
                _currentScriptScenario = scenario;
                _selectedScriptCommandRow = null;
                _selectedScriptTextEntry = null;
                _currentLegacyScriptDocument = result.Legacy;
                ClearLegacyScenarioHistory(LegacyScriptEditorScope.Script);
                if (_currentLegacyScriptDocument != null)
                {
                    _currentScriptStructure = BuildLegacyScriptStructureResult(_currentLegacyScriptDocument);
                    _currentScriptTextEntries = BuildLegacyScriptTextEntries(_currentLegacyScriptDocument);
                }
                else
                {
                    _legacyScriptCommandByKey.Clear();
                    _legacyScriptRowByKey.Clear();
                    _legacyScriptTextByOffset.Clear();
                    _legacyScriptTextEntryByOffset.Clear();
                    _currentScriptStructure = result.Structure ?? throw new InvalidOperationException("剧本结构读取失败。");
                    _currentScriptTextEntries = result.Texts ?? Array.Empty<ScenarioTextEntry>();
                }

                var modeText = _currentLegacyScriptDocument != null ? "旧版完整树" : "兼容探针";
                _scriptHeaderLabel.Text = $"字典：已加载    剧本：{scenario.FileName}    模式：{modeText}    命令：{_currentScriptStructure.Rows.Count(x => x.NodeType == "Command候选")}    文本：{_currentScriptTextEntries.Count}";
                BuildScriptTree(_currentScriptStructure, _currentScriptTextEntries);
                BindScriptCommandRows(Array.Empty<ScenarioStructureRow>());
                BindScriptParameterRows(Array.Empty<ScenarioCommandParameterRow>());
                BindScriptTextRows(_currentScriptTextEntries);
                _currentScriptSearchResults = Array.Empty<ScenarioSearchResultRow>();
                BindScriptSearchResultRows(_currentScriptSearchResults);
                _scriptTextEditorBox.Clear();
                UpdateScriptTextCapacityLabel();
                _saveScriptTextButton.Enabled = false;
                _saveScriptStructureButton.Enabled = _currentLegacyScriptDocument != null;
                _jumpScriptBattlefieldButton.Enabled = true;
                _showScriptVariablesButton.Enabled = _currentLegacyScriptDocument != null;
                _locateScriptCommandButton.Enabled = true;
                _copyScriptCommandButton.Enabled = true;
                _previewPasteScriptCommandButton.Enabled = _scriptCommandClipboardItem != null ||
                                                           _legacyScriptCommandClipboardItems.Count > 0 ||
                                                           _legacyScriptSceneClipboardItems.Count > 0 ||
                                                           _legacyScriptSectionClipboardItems.Count > 0;
                UpdateScriptStructureEditButtons();
                if (SelectDefaultScriptTreeNode())
                {
                    refreshDefaultSelectionAfterLoad = true;
                }
                else
                {
                    _scriptDetailBox.Text = BuildScriptOverview(_currentScriptStructure, _currentScriptTextEntries);
                    _scriptPreviewBox.Text = BuildScriptOverviewPreview(_currentScriptStructure, _currentScriptTextEntries);
                }
            }
            finally
            {
                _scriptSearchResultGrid.ResumeLayout();
                _scriptTextGrid.ResumeLayout();
                _scriptParameterGrid.ResumeLayout();
                _scriptCommandGrid.ResumeLayout();
                _scriptTree.ResumeLayout();
                ResumeLayout(true);
                _bindingScriptDocument = false;
            }
            if (refreshDefaultSelectionAfterLoad)
            {
                ShowSelectedScriptTreeNode();
            }
            SetStatus($"\u5267\u672c\u5236\u4f5c\uff1a{scenario.FileName}");
        }
        catch (Exception ex)
        {
            _scriptDetailBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("Load script document failed: " + ex);
            MessageBox.Show(this, ex.Message, "\u8bfb\u53d6\u5267\u672c\u5236\u4f5c\u5931\u8d25", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _loadingScriptScenarioDocument = false;
            _scriptScenarioCombo.Enabled = true;
            _scriptVariableUsageDialog?.RefreshCurrentScenario();
            Cursor = Cursors.Default;
        }
    }

    private bool BindScriptScenarioIndexRows(IReadOnlyList<ScenarioFileInfo> rows)
    {
        var previousScenarioName = _currentScriptScenario?.FileName;
        var canPreserveCurrentDocument =
            _currentScriptScenario != null &&
            _currentScriptStructure != null &&
            !string.IsNullOrWhiteSpace(previousScenarioName);
        var preservedScenario = canPreserveCurrentDocument
            ? rows.FirstOrDefault(x => x.FileName.Equals(previousScenarioName!, StringComparison.OrdinalIgnoreCase))
            : null;

        _updatingScriptScenarioSelection = true;
        try
        {
            _scriptScenarioCombo.DataSource = new BindingList<ScenarioFileInfo>(rows.ToList());
            _scriptScenarioCombo.DisplayMember = nameof(ScenarioFileInfo.FileName);
            _scriptScenarioCombo.ValueMember = nameof(ScenarioFileInfo.FileName);

            if (preservedScenario != null)
            {
                _scriptScenarioCombo.SelectedItem = preservedScenario;
                _currentScriptScenario = preservedScenario;
                return true;
            }

            _scriptScenarioCombo.SelectedIndex = -1;
            return false;
        }
        finally
        {
            _updatingScriptScenarioSelection = false;
        }
    }

    private bool IsCurrentScriptScenarioLoaded(string fileName)
        => _currentScriptScenario != null &&
           _currentScriptStructure != null &&
           _currentScriptScenario.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase);

    private async Task<bool> EnsureScriptScenarioLoadedAsync(string fileName, bool forceReload = false)
    {
        if (_project == null)
        {
            MessageBox.Show(this, "Please load the project first.", "Notice", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        if (!forceReload && IsCurrentScriptScenarioLoaded(fileName))
        {
            return true;
        }

        if (_saveScriptStructureButton.Enabled && _currentLegacyScriptDocument != null)
        {
            var result = MessageBox.Show(
                this,
                "The current script has unsaved structure changes. Save before switching scripts?",
                "Unsaved script changes",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            if (result != DialogResult.OK)
            {
                return false;
            }

            if (!await SaveCurrentLegacyScriptStructureCoreAsync())
            {
                return false;
            }
        }

        if (_scriptScenarioCombo.Items.Count == 0)
        {
            await LoadScriptScenariosAsync();
        }

        ScenarioFileInfo? target = null;
        foreach (var item in _scriptScenarioCombo.Items)
        {
            if (item is not ScenarioFileInfo scenario ||
                !scenario.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            target = scenario;
            break;
        }

        if (target == null)
        {
            MessageBox.Show(this, $"Cannot find script in current project: {fileName}", "Navigation failed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        _updatingScriptScenarioSelection = true;
        try
        {
            _scriptScenarioCombo.SelectedItem = target;
        }
        finally
        {
            _updatingScriptScenarioSelection = false;
        }

        if (!forceReload && IsCurrentScriptScenarioLoaded(fileName))
        {
            return true;
        }

        await LoadSelectedScriptScenarioAsync();
        return IsCurrentScriptScenarioLoaded(fileName);
    }

    private void LoadScriptScenarios()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "\u8bf7\u5148\u52a0\u8f7d\u9879\u76ee\u3002", "\u63d0\u793a", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var rows = _scenarioFileReader.ReadAllIndex(_project)
                .Where(x => ScenarioFileReader.IsRsScriptFile(x.FileName))
                .OrderBy(x => ScenarioFileReader.IsBattlefieldScriptFile(x.FileName) ? 1 : 0)
                .ThenBy(x => int.TryParse(x.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : int.MaxValue)
                .ThenBy(x => x.FileName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var preservedCurrentScenario = BindScriptScenarioIndexRows(rows);
            if (!preservedCurrentScenario)
            {
                ClearScriptDocumentView();
            _scriptDetailBox.Text = rows.Count > 0
                ? $"\u5df2\u8bfb\u53d6\u5267\u672c\u7d22\u5f15\uff1a{rows.Count} \u4e2a\u3002\u8bf7\u5728\u4e0a\u65b9\u4e0b\u62c9\u6846\u9009\u62e9\u8981\u7f16\u8f91/\u67e5\u770b\u7684 R/S eex \u5267\u672c\u3002"
                : "\u5267\u672c\u5236\u4f5c\uff1a\u6ca1\u6709\u627e\u5230 R/S eex \u5267\u672c\u6587\u4ef6\u3002";
            _scriptPreviewBox.Text = rows.Count > 0
                ? $"剧本列表：{rows.Count} 个\r\n选择上方剧本后加载事件树。"
                : "未找到 R/S eex 剧本文件。";
            }
            SetStatus($"\u5267\u672c\u5236\u4f5c\uff1a\u5df2\u8bfb\u53d6\u5267\u672c\u7d22\u5f15 {rows.Count} \u4e2a");
        }
        catch (Exception ex)
        {
            _updatingScriptScenarioSelection = false;
            _scriptDetailBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("\u8bfb\u53d6\u5267\u672c\u7d22\u5f15\u5931\u8d25\uff1a" + ex);
            MessageBox.Show(this, ex.Message, "\u8bfb\u53d6\u5267\u672c\u7d22\u5f15\u5931\u8d25", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void LoadSelectedScriptScenario()
    {
        if (_updatingScriptScenarioSelection) return;
        if (_project == null) return;
        if (_scriptScenarioCombo.SelectedItem is not ScenarioFileInfo scenario) return;

        try
        {
            Cursor = Cursors.WaitCursor;
            var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe();
            if (dictionary == null)
            {
                MessageBox.Show(this, $"未找到 CczString.ini：{ProjectDetector.FindSceneDictionaryPath(_project)}\r\n\r\n无法按传统命令名生成剧本树。请把字典放到当前项目的 a新剧本编辑器v0.23 目录，或确认工具内置 LegacyResources 备份未被删除。", "缺少剧本字典", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            PopulateScriptNewCommandCombo(dictionary);

            _currentScriptScenario = scenario;
            _selectedScriptCommandRow = null;
            _selectedScriptTextEntry = null;
            try
            {
                _currentLegacyScriptDocument = _legacyScenarioReader.Read(scenario.Path, dictionary);
            }
            catch
            {
                _currentLegacyScriptDocument = null;
            }
            ClearLegacyScenarioHistory(LegacyScriptEditorScope.Script);

            if (_currentLegacyScriptDocument != null)
            {
                _currentScriptStructure = BuildLegacyScriptStructureResult(_currentLegacyScriptDocument);
                _currentScriptTextEntries = BuildLegacyScriptTextEntries(_currentLegacyScriptDocument);
            }
            else
            {
                _legacyScriptCommandByKey.Clear();
                _legacyScriptRowByKey.Clear();
                _legacyScriptTextByOffset.Clear();
                _legacyScriptTextEntryByOffset.Clear();
                _currentScriptStructure = _scenarioStructureProbeReader.Build(scenario.Path, dictionary, maxCommandRows: 600, project: _project, tables: _tables);
                _currentScriptTextEntries = _scenarioTextReader.Read(scenario.Path);
            }

            BuildScriptTree(_currentScriptStructure, _currentScriptTextEntries);
            BindScriptCommandRows(Array.Empty<ScenarioStructureRow>());
            BindScriptParameterRows(Array.Empty<ScenarioCommandParameterRow>());
            BindScriptTextRows(_currentScriptTextEntries);
            _currentScriptSearchResults = Array.Empty<ScenarioSearchResultRow>();
            BindScriptSearchResultRows(_currentScriptSearchResults);
            _scriptTextEditorBox.Clear();
            UpdateScriptTextCapacityLabel();
            _saveScriptTextButton.Enabled = false;
            _saveScriptStructureButton.Enabled = _currentLegacyScriptDocument != null;
            _jumpScriptBattlefieldButton.Enabled = true;
            _showScriptVariablesButton.Enabled = _currentLegacyScriptDocument != null;
            _locateScriptCommandButton.Enabled = true;
            _copyScriptCommandButton.Enabled = true;
            _previewPasteScriptCommandButton.Enabled = _scriptCommandClipboardItem != null ||
                                                       _legacyScriptCommandClipboardItems.Count > 0 ||
                                                       _legacyScriptSceneClipboardItems.Count > 0 ||
                                                       _legacyScriptSectionClipboardItems.Count > 0;
            UpdateScriptStructureEditButtons();
            _scriptDetailBox.Text = BuildScriptOverview(_currentScriptStructure, _currentScriptTextEntries);
            _scriptPreviewBox.Text = BuildScriptOverviewPreview(_currentScriptStructure, _currentScriptTextEntries);
            ClearScriptImagePreview();
            SetStatus($"剧本制作：{scenario.FileName}");
        }
        catch (Exception ex)
        {
            _scriptDetailBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("读取剧本制作文档失败：" + ex);
            MessageBox.Show(this, ex.Message, "读取剧本制作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ClearScriptDocumentView()
    {
        _bindingScriptDocument = true;
        try
        {
            _currentScriptScenario = null;
            _currentScriptStructure = null;
            _currentLegacyScriptDocument = null;
            ClearLegacyScenarioHistory(LegacyScriptEditorScope.Script);
            _currentScriptTextEntries = Array.Empty<ScenarioTextEntry>();
            _currentScriptSearchResults = Array.Empty<ScenarioSearchResultRow>();
            _selectedScriptCommandRow = null;
            _selectedScriptTextEntry = null;
            _legacyScriptCommandByKey.Clear();
            _legacyScriptRowByKey.Clear();
            _legacyScriptItemDataByCommand.Clear();
            _legacyScriptItemDataByRow.Clear();
            _legacyScriptTextByOffset.Clear();
            _legacyScriptTextEntryByOffset.Clear();
            _scriptTree.Nodes.Clear();
            _scriptPreviewBox.Text = "选择剧本后显示当前对象。";
            ClearScriptImagePreview();
            _scriptCommandGrid.DataSource = null;
            _scriptParameterGrid.DataSource = null;
            ClearLegacyScriptParameterEditor();
            _scriptTextGrid.DataSource = null;
            _scriptSearchResultGrid.DataSource = null;
            _scriptTextEditorBox.Clear();
            _scriptTextCapacityLabel.Text = "文本：未选择";
            _saveScriptTextButton.Enabled = false;
            _saveScriptStructureButton.Enabled = false;
            _jumpScriptBattlefieldButton.Enabled = false;
            _showScriptVariablesButton.Enabled = false;
            _locateScriptCommandButton.Enabled = false;
            _copyScriptCommandButton.Enabled = false;
            _previewPasteScriptCommandButton.Enabled = _scriptCommandClipboardItem != null ||
                                                       _legacyScriptCommandClipboardItems.Count > 0 ||
                                                       _legacyScriptSceneClipboardItems.Count > 0 ||
                                                       _legacyScriptSectionClipboardItems.Count > 0;
            UpdateScriptStructureEditButtons();
        }
        finally
        {
            _bindingScriptDocument = false;
        }
    }

    private void PopulateScriptNewCommandCombo(SceneStringDocument dictionary)
    {
        var table = ScenarioStructureProbeReader.LegacyCommandInstructionTable;
        var previousId = (_scriptNewCommandCombo.SelectedItem as ScriptCommandComboItem)?.Id;
        var items = dictionary.Commands
            .Where(command => command.Id >= 0 && command.Id < table.Count)
            .Where(command => !IsBlockedNewLegacyCommandId(command.Id))
            .OrderBy(command => command.Id)
            .Select(command => new ScriptCommandComboItem(command.Id, command.Name))
            .ToList();

        _scriptNewCommandCombo.BeginUpdate();
        try
        {
            _scriptNewCommandCombo.DataSource = null;
            _scriptNewCommandCombo.Items.Clear();
            foreach (var item in items)
            {
                _scriptNewCommandCombo.Items.Add(item);
            }

            if (_scriptNewCommandCombo.Items.Count == 0)
            {
                _scriptNewCommandCombo.SelectedIndex = -1;
                return;
            }

            var selectedIndex = 0;
            if (previousId.HasValue)
            {
                for (var i = 0; i < _scriptNewCommandCombo.Items.Count; i++)
                {
                    if (_scriptNewCommandCombo.Items[i] is ScriptCommandComboItem item && item.Id == previousId.Value)
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }

            if (selectedIndex >= 0 && selectedIndex < _scriptNewCommandCombo.Items.Count)
            {
                _scriptNewCommandCombo.SelectedIndex = selectedIndex;
            }
        }
        finally
        {
            _scriptNewCommandCombo.EndUpdate();
            UpdateScriptStructureEditButtons();
        }
    }

    private bool EnsureScriptCommandComboReady()
    {
        if (_scriptNewCommandCombo.Items.Count > 0)
        {
            return true;
        }

        var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe();
        if (dictionary == null)
        {
            return false;
        }

        _currentSceneStringDocument ??= dictionary;
        PopulateScriptNewCommandCombo(dictionary);
        return _scriptNewCommandCombo.Items.Count > 0;
    }

    private void UpdateScriptStructureEditButtons()
    {
        var selectedRow = GetSelectedScriptCommandRow();
        var selectedScene = TryGetSelectedLegacyScriptSceneNode(LegacyScriptEditorScope.Script, out _);
        var selectedSection = TryGetSelectedLegacyScriptSectionNode(LegacyScriptEditorScope.Script, out _);
        var checkedScenes = GetCheckedLegacyScriptScenes(LegacyScriptEditorScope.Script);
        var checkedSections = checkedScenes.Count == 0
            ? GetCheckedLegacyScriptSections(LegacyScriptEditorScope.Script)
            : Array.Empty<LegacyScenarioSection>();
        var checkedCommands = GetCheckedLegacyScriptCommands();
        var hasCopySource = checkedScenes.Count > 0 ||
                            checkedSections.Count > 0 ||
                            checkedCommands.Count > 0 ||
                            selectedRow != null ||
                            selectedScene ||
                            selectedSection;
        var hasPasteTarget = selectedRow != null || selectedScene || selectedSection;
        var hasLegacyDocument = _currentLegacyScriptDocument != null;
        var hasCommandTemplate = _scriptNewCommandCombo.SelectedItem is ScriptCommandComboItem;
        var selectedCommand = TryGetSelectedLegacyScriptCommand(out var command);
        var canInsertNear = hasLegacyDocument &&
                            hasCommandTemplate &&
                            selectedCommand &&
                            CanInsertNearLegacyScriptCommand(command, out _) &&
                            TryFindLegacyCommandList(_currentLegacyScriptDocument!, command, out _, out _);
        var deleteCommands = GetLegacyScriptCommandDeleteSelection(LegacyScriptEditorScope.Script);
        var canDelete = checkedScenes.Count > 0 && _currentLegacyScriptDocument != null
            ? CanDeleteLegacyScriptSceneBatch(_currentLegacyScriptDocument, checkedScenes, out _)
            : checkedSections.Count > 0 && _currentLegacyScriptDocument != null
                ? CanDeleteLegacyScriptSectionBatch(_currentLegacyScriptDocument, checkedSections, out _)
                : deleteCommands.Count > 0
            ? CanDeleteLegacyScriptCommandBatch(LegacyScriptEditorScope.Script, out _)
            : selectedScene && _currentLegacyScriptDocument != null && TryGetSelectedLegacyScriptSceneNode(LegacyScriptEditorScope.Script, out var scene)
                ? CanDeleteLegacyScriptScene(_currentLegacyScriptDocument, scene, out _)
                : selectedSection && _currentLegacyScriptDocument != null && TryGetSelectedLegacyScriptSectionNode(LegacyScriptEditorScope.Script, out var section)
                    ? CanDeleteLegacyScriptSection(_currentLegacyScriptDocument, section, out _)
                    : selectedCommand && CanDeleteLegacyScriptCommand(command, out _);

        _scriptNewCommandCombo.Enabled = hasLegacyDocument;
        _appendScriptCommandToSectionButton.Enabled = hasLegacyDocument && hasCommandTemplate && selectedSection;
        _insertScriptCommandBeforeButton.Enabled = canInsertNear;
        _insertScriptCommandAfterButton.Enabled = canInsertNear;
        _appendScriptCommandToChildBlockButton.Enabled = hasLegacyDocument &&
                                                        hasCommandTemplate &&
                                                        selectedCommand &&
                                                        command.ChildBlock != null;
        _deleteScriptCommandButton.Enabled = canDelete;
        _cutScriptCommandButton.Enabled = CanCutLegacyScriptSelection(LegacyScriptEditorScope.Script, out _, out _);
        _copyScriptCommandButton.Enabled = hasCopySource;
        var pasteBeforeValidation = ValidateLegacyScriptPaste(LegacyScriptEditorScope.Script, beforeSelected: true);
        var pasteAfterValidation = ValidateLegacyScriptPaste(LegacyScriptEditorScope.Script, beforeSelected: false);
        _previewPasteScriptCommandButton.Enabled = hasPasteTarget &&
                                                   (_scriptCommandClipboardItem != null ||
                                                    (pasteBeforeValidation.PayloadKind != LegacyScriptClipboardPayloadKind.None &&
                                                     TryHasLegacyScriptClipboardCache()));
        _pasteScriptCommandBeforeButton.Enabled = pasteBeforeValidation.CanPaste;
        _pasteScriptCommandAfterButton.Enabled = pasteAfterValidation.CanPaste;
        _moveScriptCommandUpButton.Enabled = false;
        _moveScriptCommandDownButton.Enabled = false;
        _editScriptParametersButton.Enabled = TryGetSelectedLegacyItemData(out var itemData) && LegacyCommandEditDispatcher.CanEdit(itemData.Id);
    }

    private void AppendLegacyScriptCommandToSection()
    {
        if (_currentLegacyScriptDocument == null || !TryGetSelectedLegacyScriptSection(out var section)) return;
        var command = CreateLegacyScriptCommand(section.SceneIndex, section.SectionIndex);
        if (command == null) return;

        var bodyRoot = section.Commands.FirstOrDefault(candidate => candidate.StartsBodyBlock && candidate.ChildBlock != null);
        var targetList = bodyRoot?.ChildBlock?.Commands ?? section.Commands;
        var targetName = bodyRoot == null
            ? $"Scene {section.SceneIndex} / Section {section.SectionIndex} 顶层末尾"
            : $"Scene {section.SceneIndex} / Section {section.SectionIndex} 事件正文末尾";
        var insertIndex = GetLegacyScriptAppendIndex(targetList);

        ApplyLegacyScriptStructureEdit(
            () => targetList.Insert(insertIndex, command),
            command,
            $"已添加命令到 {targetName}。",
            new LegacyScriptStructureEditOptions(section));
    }

    private void InsertLegacyScriptCommandNearSelected(bool beforeSelected)
    {
        if (_currentLegacyScriptDocument == null || !TryGetSelectedLegacyScriptCommand(out var selected)) return;
        if (!CanInsertNearLegacyScriptCommand(selected, out var reason))
        {
            MessageBox.Show(this, reason, "无法在该命令旁插入", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!TryFindLegacyCommandList(_currentLegacyScriptDocument, selected, out var list, out var index)) return;

        var command = CreateLegacyScriptCommand(selected.SceneIndex, selected.SectionIndex);
        if (command == null) return;
        var insertIndex = GetLegacyScriptNearInsertIndex(list, index, beforeSelected);

        ApplyLegacyScriptStructureEdit(
            () => list.Insert(insertIndex, command),
            command,
            $"已{(beforeSelected ? "前插" : "后插")}命令：{command.CommandIdHex} {command.CommandName}。",
            new LegacyScriptStructureEditOptions(FindLegacyScriptSectionForCommand(_currentLegacyScriptDocument, selected)));
    }

    private void AppendLegacyScriptCommandToChildBlock()
    {
        if (!TryGetSelectedLegacyScriptCommand(out var selected) || selected.ChildBlock == null) return;
        var command = CreateLegacyScriptCommand(selected.SceneIndex, selected.SectionIndex);
        if (command == null) return;
        var insertIndex = GetLegacyScriptAppendIndex(selected.ChildBlock.Commands);

        ApplyLegacyScriptStructureEdit(
            () => selected.ChildBlock.Commands.Insert(insertIndex, command),
            command,
            $"已追加命令到 {selected.CommandIdHex} {selected.CommandName} 的子块。",
            new LegacyScriptStructureEditOptions(FindLegacyScriptSectionForCommand(_currentLegacyScriptDocument, selected)));
    }

    private void DeleteSelectedLegacyScriptCommand()
        => DeleteSelectedLegacyScriptCommand(LegacyScriptEditorScope.Script);

    private void DeleteSelectedLegacyScriptCommand(LegacyScriptEditorScope scope)
    {
        var document = GetCurrentLegacyScriptDocument(scope);
        if (document == null) return;

        var scenesToDelete = GetCheckedLegacyScriptScenes(scope);
        if (scenesToDelete.Count > 0)
        {
            DeleteLegacyScriptSceneBatch(scope, document, scenesToDelete);
            return;
        }

        var sectionsToDelete = GetCheckedLegacyScriptSections(scope);
        if (sectionsToDelete.Count > 0)
        {
            DeleteLegacyScriptSectionBatch(scope, document, sectionsToDelete);
            return;
        }

        var commandsToDelete = GetLegacyScriptCommandDeleteSelection(scope);
        if (commandsToDelete.Count > 0)
        {
            DeleteLegacyScriptCommandBatch(scope, document, commandsToDelete);
            return;
        }

        if (TryGetSelectedLegacyScriptSceneNode(scope, out var selectedScene))
        {
            DeleteSelectedLegacyScriptScene(scope, document, selectedScene);
            return;
        }

        if (TryGetSelectedLegacyScriptSectionNode(scope, out var selectedSection))
        {
            DeleteSelectedLegacyScriptSection(scope, document, selectedSection);
            return;
        }

        if (!TryGetSelectedLegacyScriptCommand(scope, out var selected)) return;
        DeleteLegacyScriptCommandBatch(scope, document, new[] { selected });
    }

    private IReadOnlyList<LegacyScenarioCommandNode> GetLegacyScriptCommandDeleteSelection(LegacyScriptEditorScope scope)
    {
        var result = new List<LegacyScenarioCommandNode>();
        var seen = new HashSet<LegacyScenarioCommandNode>();
        foreach (var command in GetCheckedLegacyScriptCommands(scope))
        {
            if (seen.Add(command))
            {
                result.Add(command);
            }
        }

        if (TryGetSelectedLegacyScriptCommand(scope, out var selected) && seen.Add(selected))
        {
            result.Add(selected);
        }

        return RemoveLegacyScriptDescendantDeleteCommands(result);
    }

    private static IReadOnlyList<LegacyScenarioCommandNode> RemoveLegacyScriptDescendantDeleteCommands(
        IReadOnlyList<LegacyScenarioCommandNode> commands)
    {
        if (commands.Count <= 1)
        {
            return commands;
        }

        var result = new List<LegacyScenarioCommandNode>(commands.Count);
        foreach (var command in commands)
        {
            if (!commands.Any(candidate =>
                    !ReferenceEquals(candidate, command) &&
                    LegacyScriptCommandTreeContainsCommand(candidate, command)))
            {
                result.Add(command);
            }
        }

        return result;
    }

    private static bool LegacyScriptCommandTreeContainsCommand(
        LegacyScenarioCommandNode root,
        LegacyScenarioCommandNode target)
    {
        if (root.ChildBlock == null)
        {
            return false;
        }

        foreach (var child in root.ChildBlock.Commands)
        {
            if (ReferenceEquals(child, target) || LegacyScriptCommandTreeContainsCommand(child, target))
            {
                return true;
            }
        }

        return false;
    }

    private bool CanDeleteLegacyScriptCommandBatch(LegacyScriptEditorScope scope, out string reason)
    {
        foreach (var command in GetLegacyScriptCommandDeleteSelection(scope))
        {
            if (!CanDeleteLegacyScriptCommand(scope, command, out reason))
            {
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private void DeleteLegacyScriptCommandBatch(
        LegacyScriptEditorScope scope,
        LegacyScenarioDocument document,
        IReadOnlyList<LegacyScenarioCommandNode> sourceCommands)
    {
        var commands = RemoveLegacyScriptDescendantDeleteCommands(sourceCommands);
        if (commands.Count == 0) return;

        foreach (var command in commands)
        {
            if (!CanDeleteLegacyScriptCommand(scope, command, out var reason))
            {
                MessageBox.Show(this, reason, "无法删除该命令", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }

        var ranges = new List<LegacyScriptCommandDeleteRange>();
        foreach (var command in commands)
        {
            if (!TryFindLegacyCommandList(document, command, out var list, out var index))
            {
                MessageBox.Show(this, "没有在当前旧版命令树中定位到要删除的命令。", "无法删除该命令", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var deleteStart = index;
            var deleteCount = 1;
            if (command.CommandId == 0x01 && index + 1 < list.Count && IsLegacyScriptSubEventCarrier(list[index + 1]))
            {
                deleteCount = 2;
            }
            else if (IsLegacyScriptSubEventCarrier(command) && index > 0 && list[index - 1].CommandId == 0x01)
            {
                deleteStart = index - 1;
                deleteCount = 2;
            }

            ranges.Add(new LegacyScriptCommandDeleteRange(list, deleteStart, deleteCount));
        }

        var mergedRanges = MergeLegacyScriptCommandDeleteRanges(ranges);
        if (mergedRanges.Count == 0) return;
        var firstRange = mergedRanges[0];
        var nextSelection = FindLegacyScriptDeleteNextSelection(document, mergedRanges, firstRange);
        var affectedSections = mergedRanges
            .Select(range => TryFindLegacySectionForCommandList(document, range.List, out var section) ? section : null)
            .Where(section => section != null)
            .Distinct()
            .ToList();
        var affectedSection = affectedSections.Count == 1 ? affectedSections[0] : null;
        var deletedCommandCount = mergedRanges.Sum(range => range.Count);
        var requestedCommandCount = commands.Count;

        ApplyLegacyScriptStructureEdit(
            scope,
            () =>
            {
                foreach (var group in mergedRanges.GroupBy(range => range.List))
                {
                    foreach (var range in group.OrderByDescending(candidate => candidate.Start))
                    {
                        range.List.RemoveRange(range.Start, range.Count);
                    }
                }
            },
            nextSelection,
            requestedCommandCount == 1 && deletedCommandCount == 1
                ? $"已删除命令：{commands[0].CommandIdHex} {commands[0].CommandName}。"
                : $"已批量删除 {deletedCommandCount} 条命令（来源选中 {requestedCommandCount} 条）。",
            new LegacyScriptStructureEditOptions(affectedSection));
    }

    private static IReadOnlyList<LegacyScriptCommandDeleteRange> MergeLegacyScriptCommandDeleteRanges(
        IReadOnlyList<LegacyScriptCommandDeleteRange> ranges)
    {
        if (ranges.Count <= 1)
        {
            return ranges;
        }

        var merged = new List<LegacyScriptCommandDeleteRange>();
        foreach (var group in ranges.GroupBy(range => range.List))
        {
            foreach (var range in group.OrderBy(range => range.Start))
            {
                if (merged.Count == 0 || !ReferenceEquals(merged[^1].List, range.List) || range.Start > merged[^1].EndExclusive)
                {
                    merged.Add(range);
                    continue;
                }

                var previous = merged[^1];
                var endExclusive = Math.Max(previous.EndExclusive, range.EndExclusive);
                merged[^1] = previous with { Count = endExclusive - previous.Start };
            }
        }

        return merged;
    }

    private static LegacyScenarioCommandNode? FindLegacyScriptDeleteNextSelection(
        LegacyScenarioDocument document,
        IReadOnlyList<LegacyScriptCommandDeleteRange> ranges,
        LegacyScriptCommandDeleteRange preferredRange)
    {
        if (preferredRange.Start + preferredRange.Count < preferredRange.List.Count)
        {
            return preferredRange.List[preferredRange.Start + preferredRange.Count];
        }

        if (preferredRange.Start > 0)
        {
            return preferredRange.List[preferredRange.Start - 1];
        }

        var deleted = new HashSet<LegacyScenarioCommandNode>();
        foreach (var range in ranges)
        {
            for (var i = range.Start; i < range.Start + range.Count && i < range.List.Count; i++)
            {
                deleted.Add(range.List[i]);
            }
        }

        return document.EnumerateCommands().FirstOrDefault(command => !deleted.Contains(command));
    }

    private sealed record LegacyScriptCommandDeleteRange(
        List<LegacyScenarioCommandNode> List,
        int Start,
        int Count)
    {
        public int EndExclusive => Start + Count;
    }

    private void DeleteSelectedLegacyScriptScene(
        LegacyScriptEditorScope scope,
        LegacyScenarioDocument document,
        LegacyScenarioScene selectedScene)
    {
        if (!CanDeleteLegacyScriptScene(document, selectedScene, out var reason))
        {
            MessageBox.Show(this, reason, "无法删除 Scene", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var sceneIndex = document.Scenes.IndexOf(selectedScene);
        if (sceneIndex < 0)
        {
            MessageBox.Show(this, "没有在当前旧版剧本树中定位到 Scene。", "无法删除 Scene", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var nextSelection = sceneIndex + 1 < document.Scenes.Count
            ? document.Scenes[sceneIndex + 1].Sections.FirstOrDefault()?.Commands.FirstOrDefault()
            : sceneIndex > 0
                ? document.Scenes[sceneIndex - 1].Sections.LastOrDefault()?.Commands.FirstOrDefault()
                : null;

        ApplyLegacyScriptStructureEdit(
            scope,
            () => document.Scenes.RemoveAt(sceneIndex),
            nextSelection,
            $"已删除 Scene {selectedScene.SceneIndex}。");
    }

    private void DeleteSelectedLegacyScriptSection(
        LegacyScriptEditorScope scope,
        LegacyScenarioDocument document,
        LegacyScenarioSection selectedSection)
    {
        if (!CanDeleteLegacyScriptSection(document, selectedSection, out var reason))
        {
            MessageBox.Show(this, reason, "无法删除 Section", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var scene = document.Scenes.FirstOrDefault(candidate => candidate.Sections.Contains(selectedSection));
        if (scene == null)
        {
            MessageBox.Show(this, "没有在当前旧版剧本树中定位到 Section。", "无法删除 Section", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var sectionIndex = scene.Sections.IndexOf(selectedSection);
        if (sectionIndex < 0)
        {
            MessageBox.Show(this, "没有在当前 Scene 中定位到 Section。", "无法删除 Section", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var nextSelection = sectionIndex + 1 < scene.Sections.Count
            ? scene.Sections[sectionIndex + 1].Commands.FirstOrDefault()
            : sectionIndex > 0
                ? scene.Sections[sectionIndex - 1].Commands.FirstOrDefault()
                : null;

        ApplyLegacyScriptStructureEdit(
            scope,
            () => scene.Sections.RemoveAt(sectionIndex),
            nextSelection,
            $"已删除 Scene {selectedSection.SceneIndex} / Section {selectedSection.SectionIndex}。");
    }

    private void DeleteLegacyScriptSceneBatch(
        LegacyScriptEditorScope scope,
        LegacyScenarioDocument document,
        IReadOnlyList<LegacyScenarioScene> sourceScenes)
    {
        var scenes = sourceScenes
            .Distinct()
            .ToList();
        if (scenes.Count == 0) return;

        if (!CanDeleteLegacyScriptSceneBatch(document, scenes, out var reason))
        {
            MessageBox.Show(this, reason, "无法批量删除 Scene", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var indexes = scenes
            .Select(scene => document.Scenes.IndexOf(scene))
            .Where(index => index >= 0)
            .Distinct()
            .OrderBy(index => index)
            .ToList();
        if (indexes.Count == 0)
        {
            MessageBox.Show(this, "没有在当前旧版剧本树中定位到要删除的 Scene。", "无法批量删除 Scene", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var deletedIndexes = indexes.ToHashSet();
        var firstIndex = indexes[0];
        var nextSelection = document.Scenes
            .Skip(firstIndex)
            .Where((_, relativeIndex) => !deletedIndexes.Contains(firstIndex + relativeIndex))
            .SelectMany(scene => scene.Sections)
            .SelectMany(section => section.Commands)
            .FirstOrDefault()
            ?? document.Scenes
                .Take(firstIndex)
                .Reverse()
                .Where((_, relativeIndex) => !deletedIndexes.Contains(firstIndex - relativeIndex - 1))
                .SelectMany(scene => scene.Sections)
                .SelectMany(section => section.Commands)
                .FirstOrDefault();

        ApplyLegacyScriptStructureEdit(
            scope,
            () =>
            {
                foreach (var index in indexes.OrderByDescending(index => index))
                {
                    document.Scenes.RemoveAt(index);
                }
            },
            nextSelection,
            indexes.Count == 1
                ? $"已删除 Scene {scenes[0].SceneIndex}。"
                : $"已批量删除 {indexes.Count} 个 Scene。");
    }

    private void DeleteLegacyScriptSectionBatch(
        LegacyScriptEditorScope scope,
        LegacyScenarioDocument document,
        IReadOnlyList<LegacyScenarioSection> sourceSections)
    {
        var sections = sourceSections
            .Distinct()
            .ToList();
        if (sections.Count == 0) return;

        if (!CanDeleteLegacyScriptSectionBatch(document, sections, out var reason))
        {
            MessageBox.Show(this, reason, "无法批量删除 Section", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var locations = sections
            .Select(section =>
            {
                var scene = document.Scenes.FirstOrDefault(candidate => candidate.Sections.Contains(section));
                var index = scene?.Sections.IndexOf(section) ?? -1;
                return (Scene: scene, Section: section, Index: index);
            })
            .Where(location => location.Scene != null && location.Index >= 0)
            .Select(location => (Scene: location.Scene!, location.Section, location.Index))
            .ToList();
        if (locations.Count == 0)
        {
            MessageBox.Show(this, "没有在当前旧版剧本树中定位到要删除的 Section。", "无法批量删除 Section", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var firstLocation = locations
            .OrderBy(location => document.Scenes.IndexOf(location.Scene))
            .ThenBy(location => location.Index)
            .First();
        var deletedByScene = locations
            .GroupBy(location => location.Scene)
            .ToDictionary(
                group => group.Key,
                group => group.Select(location => location.Index).ToHashSet());
        var nextSelection = firstLocation.Scene.Sections
            .Skip(firstLocation.Index)
            .Where((_, relativeIndex) => !deletedByScene[firstLocation.Scene].Contains(firstLocation.Index + relativeIndex))
            .SelectMany(section => section.Commands)
            .FirstOrDefault()
            ?? firstLocation.Scene.Sections
                .Take(firstLocation.Index)
                .Reverse()
                .Where((_, relativeIndex) => !deletedByScene[firstLocation.Scene].Contains(firstLocation.Index - relativeIndex - 1))
                .SelectMany(section => section.Commands)
                .FirstOrDefault()
            ?? document.Scenes
                .SelectMany(scene => scene.Sections)
                .Where(section => !sections.Contains(section))
                .SelectMany(section => section.Commands)
                .FirstOrDefault();

        ApplyLegacyScriptStructureEdit(
            scope,
            () =>
            {
                foreach (var group in locations.GroupBy(location => location.Scene))
                {
                    foreach (var index in group.Select(location => location.Index).Distinct().OrderByDescending(index => index))
                    {
                        group.Key.Sections.RemoveAt(index);
                    }
                }
            },
            nextSelection,
            locations.Count == 1
                ? $"已删除 Scene {locations[0].Section.SceneIndex} / Section {locations[0].Section.SectionIndex}。"
                : $"已批量删除 {locations.Count} 个 Section。");
    }

    private void PasteCopiedLegacyScriptCommandNearSelected(bool beforeSelected)
        => PasteCopiedLegacyScriptCommandNearSelected(LegacyScriptEditorScope.Script, beforeSelected);

    private void PasteCopiedLegacyScriptCommandAtDefaultTarget()
        => PasteCopiedLegacyScriptCommandAtDefaultTarget(LegacyScriptEditorScope.Script);

    private void PasteCopiedLegacyScriptCommandAtDefaultTarget(LegacyScriptEditorScope scope)
    {
        if (!TryEnsureLegacyScriptClipboardAvailable(out var reason))
        {
            MessageBox.Show(this, reason, "无法粘贴", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var pasteSceneAfterSelected = _legacyScriptSceneClipboardItems.Count > 0 &&
                                      TryGetSelectedLegacyScriptSceneNode(scope, out _);
        if (pasteSceneAfterSelected)
        {
            PasteCopiedLegacyScriptCommandNearSelected(scope, beforeSelected: false);
            return;
        }

        var pasteSectionAfterSelected = _legacyScriptSectionClipboardItems.Count > 0 &&
                                        TryGetSelectedLegacyScriptSectionNode(scope, out _);
        PasteCopiedLegacyScriptCommandNearSelected(scope, beforeSelected: !pasteSectionAfterSelected);
    }

    private void PasteCopiedLegacyScriptCommandNearSelected(LegacyScriptEditorScope scope, bool beforeSelected)
    {
        var validation = ValidateLegacyScriptPaste(scope, beforeSelected);
        if (!validation.CanPaste)
        {
            MessageBox.Show(this, validation.Reason, "无法粘贴", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var document = GetCurrentLegacyScriptDocument(scope);
        if (document == null)
        {
            return;
        }

        var sourceScenes = GetLegacyScriptClipboardScenesForPaste();
        if (sourceScenes.Count > 0)
        {
            if (!TryGetSelectedLegacyScriptSceneNode(scope, out var selectedScene))
            {
                return;
            }

            var selectedSceneIndex = document.Scenes.IndexOf(selectedScene);
            if (selectedSceneIndex < 0)
            {
                return;
            }

            var sceneInsertIndex = beforeSelected ? selectedSceneIndex : selectedSceneIndex + 1;
            var scenesToInsert = sourceScenes
                .Select(CloneLegacyScriptSceneForPaste)
                .ToList();
            var preferredSceneSelection = scenesToInsert[0].Sections.FirstOrDefault()?.Commands.FirstOrDefault();
            ApplyLegacyScriptStructureEdit(
                scope,
                () => document.Scenes.InsertRange(sceneInsertIndex, scenesToInsert),
                preferredSceneSelection,
                AppendLegacyScriptClipboardWarningToStatus(scenesToInsert.Count == 1
                    ? $"已粘贴 Scene 到 Scene {selectedScene.SceneIndex} {(beforeSelected ? "前面" : "后面")}。"
                    : $"已粘贴 {scenesToInsert.Count} 个 Scene，来源：{_legacyScriptCommandClipboardScenarioName}。",
                    validation));
            return;
        }

        var sourceSections = GetLegacyScriptClipboardSectionsForPaste();
        if (sourceSections.Count > 0)
        {
            if (!TryGetSelectedLegacyScriptSectionNode(scope, out var selectedSection) ||
                !TryFindLegacySectionList(document, selectedSection, out var sections, out var selectedSectionIndex))
            {
                return;
            }

            var sectionInsertIndex = beforeSelected ? selectedSectionIndex : selectedSectionIndex + 1;
            var sectionsToInsert = sourceSections
                .Select(section => CloneLegacyScriptSectionForPaste(section, selectedSection.SceneIndex, selectedSection.SectionIndex))
                .ToList();
            var preferredSectionSelection = sectionsToInsert[0].Commands.FirstOrDefault();
            ApplyLegacyScriptStructureEdit(
                scope,
                () => sections.InsertRange(sectionInsertIndex, sectionsToInsert),
                preferredSectionSelection,
                AppendLegacyScriptClipboardWarningToStatus(sectionsToInsert.Count == 1
                    ? $"已粘贴 Section 到 Scene {selectedSection.SceneIndex} / Section {selectedSection.SectionIndex} {(beforeSelected ? "前面" : "后面")}。"
                    : $"已粘贴 {sectionsToInsert.Count} 个 Section，来源：{_legacyScriptCommandClipboardScenarioName}。",
                    validation));
            return;
        }

        if (!TryGetLegacyScriptCommandPasteTarget(scope, beforeSelected, out var target, out _))
        {
            return;
        }

        var affectedSection = TryFindLegacySectionForCommandList(document, target.Commands, out var targetSection)
            ? targetSection
            : null;
        var sourceCommands = GetLegacyScriptClipboardCommandsForPaste();
        var commands = sourceCommands
            .Select(command => CloneLegacyScriptCommandForPaste(command, target.SceneIndex, target.SectionIndex))
            .ToList();
        var commandInsertIndex = target.InsertIndex;
        var preferredSelection = commands.LastOrDefault();
        ApplyLegacyScriptStructureEdit(
            scope,
            () =>
            {
                foreach (var command in commands)
                {
                    if (IsLegacyScriptSubEventCarrier(command) &&
                        !HasLegacyScriptSubEventMarkerBefore(target.Commands, commandInsertIndex))
                    {
                        target.Commands.Insert(commandInsertIndex, CreateLegacyScriptStructuralCommand(0x01, target.SceneIndex, target.SectionIndex));
                        commandInsertIndex++;
                    }

                    target.Commands.Insert(commandInsertIndex, command);
                    commandInsertIndex++;
                }
            },
            preferredSelection,
            AppendLegacyScriptClipboardWarningToStatus(commands.Count == 1
                ? $"已{target.StatusActionText}：{commands[0].CommandIdHex} {commands[0].CommandName}。"
                : $"已{target.StatusActionText} {commands.Count} 条命令，来源：{_legacyScriptCommandClipboardScenarioName}。",
                validation),
            new LegacyScriptStructureEditOptions(affectedSection));
    }

    private bool CanPasteCopiedLegacyScriptCommandNearSelected(bool beforeSelected, out string reason)
        => CanPasteCopiedLegacyScriptCommandNearSelected(LegacyScriptEditorScope.Script, beforeSelected, out reason);

    private bool CanPasteCopiedLegacyScriptCommandNearSelected(LegacyScriptEditorScope scope, bool beforeSelected, out string reason)
    {
        var result = ValidateLegacyScriptPaste(scope, beforeSelected);
        reason = result.Reason;
        return result.CanPaste;
    }

    private static string AppendLegacyScriptClipboardWarningToStatus(
        string statusText,
        LegacyScriptPasteValidationResult validation)
        => string.IsNullOrWhiteSpace(validation.ClipboardWarning)
            ? statusText
            : statusText + "（" + validation.ClipboardWarning + "）";

    private LegacyScriptPasteValidationResult ValidateLegacyScriptPaste(LegacyScriptEditorScope scope, bool beforeSelected)
    {
        var document = GetCurrentLegacyScriptDocument(scope);
        var sourceProjectName = _legacyScriptCommandClipboardProjectName;
        var sourceScenarioName = _legacyScriptCommandClipboardScenarioName;
        var sourceGameRoot = _legacyScriptCommandClipboardGameRoot;
        var targetProjectName = _project?.Name ?? string.Empty;
        var targetScenarioName = GetLegacyScriptScenarioName(scope);
        var targetGameRoot = _project?.GameRoot ?? string.Empty;
        if (document == null)
        {
            var clipboardWarning = _legacyScriptClipboardWarning;
            return BuildLegacyScriptPasteValidationResult(
                false,
                "当前没有可编辑的旧版完整剧本树。",
                LegacyScriptClipboardPayloadKind.None,
                false,
                Array.Empty<LegacyScriptBlockedJumpInfo>(),
                0,
                sourceProjectName,
                sourceScenarioName,
                sourceGameRoot,
                targetProjectName,
                targetScenarioName,
                targetGameRoot,
                clipboardWarning);
        }

        if (!TryEnsureLegacyScriptClipboardAvailable(out var reason))
        {
            var clipboardWarning = _legacyScriptClipboardWarning;
            return BuildLegacyScriptPasteValidationResult(
                false,
                reason,
                LegacyScriptClipboardPayloadKind.None,
                false,
                Array.Empty<LegacyScriptBlockedJumpInfo>(),
                0,
                sourceProjectName,
                sourceScenarioName,
                sourceGameRoot,
                targetProjectName,
                targetScenarioName,
                targetGameRoot,
                clipboardWarning);
        }

        sourceProjectName = _legacyScriptCommandClipboardProjectName;
        sourceScenarioName = _legacyScriptCommandClipboardScenarioName;
        sourceGameRoot = _legacyScriptCommandClipboardGameRoot;
        var successfulClipboardWarning = _legacyScriptClipboardWarning;
        var isCrossScenario = !IsLegacyScriptClipboardFromSameScenario(scope);
        var sourceScenes = GetLegacyScriptClipboardScenesForPaste();
        if (sourceScenes.Count > 0)
        {
            if (!TryGetSelectedLegacyScriptSceneNode(scope, out var selectedScene))
            {
                return BuildLegacyScriptPasteValidationResult(
                    false,
                    "请先选择要粘贴到其前面或后面的 Scene。",
                    LegacyScriptClipboardPayloadKind.Scenes,
                    isCrossScenario,
                    Array.Empty<LegacyScriptBlockedJumpInfo>(),
                    CountLegacyScriptSafeCommands(sourceScenes),
                    sourceProjectName,
                    sourceScenarioName,
                    sourceGameRoot,
                    targetProjectName,
                    targetScenarioName,
                    targetGameRoot,
                    successfulClipboardWarning);
            }

            if (!document.Scenes.Contains(selectedScene))
            {
                return BuildLegacyScriptPasteValidationResult(
                    false,
                    "没有在当前旧版剧本树中定位到粘贴目标 Scene。",
                    LegacyScriptClipboardPayloadKind.Scenes,
                    isCrossScenario,
                    Array.Empty<LegacyScriptBlockedJumpInfo>(),
                    CountLegacyScriptSafeCommands(sourceScenes),
                    sourceProjectName,
                    sourceScenarioName,
                    sourceGameRoot,
                    targetProjectName,
                    targetScenarioName,
                    targetGameRoot,
                    successfulClipboardWarning);
            }

            var blocked = CollectLegacyScriptBlockedJumpCommands(sourceScenes);
            if (isCrossScenario && blocked.Count > 0)
            {
                return BuildLegacyScriptPasteValidationResult(
                    false,
                    BuildLegacyScriptBlockedJumpReason(scope, LegacyScriptClipboardPayloadKind.Scenes, blocked),
                    LegacyScriptClipboardPayloadKind.Scenes,
                    true,
                    blocked,
                    CountLegacyScriptSafeCommands(sourceScenes),
                    sourceProjectName,
                    sourceScenarioName,
                    sourceGameRoot,
                    targetProjectName,
                    targetScenarioName,
                    targetGameRoot,
                    successfulClipboardWarning);
            }

            return BuildLegacyScriptPasteValidationResult(
                true,
                string.Empty,
                LegacyScriptClipboardPayloadKind.Scenes,
                isCrossScenario,
                blocked,
                CountLegacyScriptSafeCommands(sourceScenes),
                sourceProjectName,
                sourceScenarioName,
                sourceGameRoot,
                targetProjectName,
                targetScenarioName,
                targetGameRoot,
                successfulClipboardWarning);
        }

        var sourceSections = GetLegacyScriptClipboardSectionsForPaste();
        if (sourceSections.Count > 0)
        {
            if (!TryGetSelectedLegacyScriptSectionNode(scope, out var selectedSection))
            {
                return BuildLegacyScriptPasteValidationResult(
                    false,
                    "请先选择要粘贴到其前面或后面的 Section。",
                    LegacyScriptClipboardPayloadKind.Sections,
                    isCrossScenario,
                    Array.Empty<LegacyScriptBlockedJumpInfo>(),
                    CountLegacyScriptSafeCommands(sourceSections),
                    sourceProjectName,
                    sourceScenarioName,
                    sourceGameRoot,
                    targetProjectName,
                    targetScenarioName,
                    targetGameRoot,
                    successfulClipboardWarning);
            }

            if (!TryFindLegacySectionList(document, selectedSection, out _, out _))
            {
                return BuildLegacyScriptPasteValidationResult(
                    false,
                    "没有在当前旧版剧本树中定位到粘贴目标 Section。",
                    LegacyScriptClipboardPayloadKind.Sections,
                    isCrossScenario,
                    Array.Empty<LegacyScriptBlockedJumpInfo>(),
                    CountLegacyScriptSafeCommands(sourceSections),
                    sourceProjectName,
                    sourceScenarioName,
                    sourceGameRoot,
                    targetProjectName,
                    targetScenarioName,
                    targetGameRoot,
                    successfulClipboardWarning);
            }

            var blocked = CollectLegacyScriptBlockedJumpCommands(sourceSections);
            if (isCrossScenario && blocked.Count > 0)
            {
                return BuildLegacyScriptPasteValidationResult(
                    false,
                    BuildLegacyScriptBlockedJumpReason(scope, LegacyScriptClipboardPayloadKind.Sections, blocked),
                    LegacyScriptClipboardPayloadKind.Sections,
                    true,
                    blocked,
                    CountLegacyScriptSafeCommands(sourceSections),
                    sourceProjectName,
                    sourceScenarioName,
                    sourceGameRoot,
                    targetProjectName,
                    targetScenarioName,
                    targetGameRoot,
                    successfulClipboardWarning);
            }

            return BuildLegacyScriptPasteValidationResult(
                true,
                string.Empty,
                LegacyScriptClipboardPayloadKind.Sections,
                isCrossScenario,
                blocked,
                CountLegacyScriptSafeCommands(sourceSections),
                sourceProjectName,
                sourceScenarioName,
                sourceGameRoot,
                targetProjectName,
                targetScenarioName,
                targetGameRoot,
                successfulClipboardWarning);
        }

        var sourceCommands = GetLegacyScriptClipboardCommandsForPaste();
        if (sourceCommands.Count == 0)
        {
            return BuildLegacyScriptPasteValidationResult(
                false,
                "系统剪贴板没有可粘贴的剧本命令、Section 或 Scene 结构数据。",
                LegacyScriptClipboardPayloadKind.None,
                isCrossScenario,
                Array.Empty<LegacyScriptBlockedJumpInfo>(),
                0,
                sourceProjectName,
                sourceScenarioName,
                sourceGameRoot,
                targetProjectName,
                targetScenarioName,
                targetGameRoot,
                successfulClipboardWarning);
        }

        if (!TryGetLegacyScriptCommandPasteTarget(scope, beforeSelected, out var target, out reason))
        {
            return BuildLegacyScriptPasteValidationResult(
                false,
                reason,
                LegacyScriptClipboardPayloadKind.Commands,
                isCrossScenario,
                Array.Empty<LegacyScriptBlockedJumpInfo>(),
                CountLegacyScriptSafeCommands(sourceCommands),
                sourceProjectName,
                sourceScenarioName,
                sourceGameRoot,
                targetProjectName,
                targetScenarioName,
                targetGameRoot,
                successfulClipboardWarning);
        }

        foreach (var command in sourceCommands)
        {
            if (!CanCopyLegacyScriptCommand(command, out reason))
            {
                return BuildLegacyScriptPasteValidationResult(
                    false,
                    "当前复制的命令不能作为普通命令粘贴：" + reason,
                    LegacyScriptClipboardPayloadKind.Commands,
                    isCrossScenario,
                    Array.Empty<LegacyScriptBlockedJumpInfo>(),
                    CountLegacyScriptSafeCommands(sourceCommands),
                    sourceProjectName,
                    sourceScenarioName,
                    sourceGameRoot,
                    targetProjectName,
                    targetScenarioName,
                    targetGameRoot,
                    successfulClipboardWarning);
            }

            if (IsLegacyScriptSectionTopLevelList(document, target.Commands) &&
                !IsLegacyScriptTopLevelCommandId(command.CommandId))
            {
                return BuildLegacyScriptPasteValidationResult(
                    false,
                    $"旧版不允许在 Section 顶层粘贴该类型指令：{command.CommandIdHex} {command.CommandName}。",
                    LegacyScriptClipboardPayloadKind.Commands,
                    isCrossScenario,
                    Array.Empty<LegacyScriptBlockedJumpInfo>(),
                    CountLegacyScriptSafeCommands(sourceCommands),
                    sourceProjectName,
                    sourceScenarioName,
                    sourceGameRoot,
                    targetProjectName,
                    targetScenarioName,
                    targetGameRoot,
                    successfulClipboardWarning);
            }
        }

        var blockedCommands = CollectLegacyScriptBlockedJumpCommands(sourceCommands);
        if (isCrossScenario && blockedCommands.Count > 0)
        {
            return BuildLegacyScriptPasteValidationResult(
                false,
                BuildLegacyScriptBlockedJumpReason(scope, LegacyScriptClipboardPayloadKind.Commands, blockedCommands),
                LegacyScriptClipboardPayloadKind.Commands,
                true,
                blockedCommands,
                CountLegacyScriptSafeCommands(sourceCommands),
                sourceProjectName,
                sourceScenarioName,
                sourceGameRoot,
                targetProjectName,
                targetScenarioName,
                targetGameRoot,
                successfulClipboardWarning);
        }

        return BuildLegacyScriptPasteValidationResult(
            true,
            string.Empty,
            LegacyScriptClipboardPayloadKind.Commands,
            isCrossScenario,
            blockedCommands,
            CountLegacyScriptSafeCommands(sourceCommands),
            sourceProjectName,
            sourceScenarioName,
            sourceGameRoot,
            targetProjectName,
            targetScenarioName,
            targetGameRoot,
            successfulClipboardWarning);
    }

    private IReadOnlyList<LegacyScenarioCommandNode> GetLegacyScriptClipboardCommandsForPaste()
        => _legacyScriptCommandClipboardItems.Count > 0
            ? _legacyScriptCommandClipboardItems
            : _legacyScriptCommandClipboard == null
                ? Array.Empty<LegacyScenarioCommandNode>()
                : new[] { _legacyScriptCommandClipboard };

    private IReadOnlyList<LegacyScenarioScene> GetLegacyScriptClipboardScenesForPaste()
        => _legacyScriptSceneClipboardItems;

    private IReadOnlyList<LegacyScenarioSection> GetLegacyScriptClipboardSectionsForPaste()
        => _legacyScriptSectionClipboardItems;

    private static LegacyScriptPasteValidationResult BuildLegacyScriptPasteValidationResult(
        bool canPaste,
        string reason,
        LegacyScriptClipboardPayloadKind payloadKind,
        bool isCrossScenario,
        IReadOnlyList<LegacyScriptBlockedJumpInfo> blockedJumpCommands,
        int safeCommandCount,
        string sourceProjectName,
        string sourceScenarioName,
        string sourceGameRoot,
        string targetProjectName,
        string targetScenarioName,
        string targetGameRoot,
        string? clipboardWarning = null)
        => new(
            canPaste,
            reason,
            payloadKind,
            isCrossScenario,
            blockedJumpCommands,
            safeCommandCount,
            sourceProjectName,
            sourceScenarioName,
            sourceGameRoot,
            targetProjectName,
            targetScenarioName,
            targetGameRoot,
            clipboardWarning);

    private string BuildLegacyScriptBlockedJumpReason(
        LegacyScriptEditorScope scope,
        LegacyScriptClipboardPayloadKind payloadKind,
        IReadOnlyList<LegacyScriptBlockedJumpInfo> blocked)
    {
        var payloadText = payloadKind switch
        {
            LegacyScriptClipboardPayloadKind.Scenes => "Scene",
            LegacyScriptClipboardPayloadKind.Sections => "Section",
            LegacyScriptClipboardPayloadKind.Commands => "命令",
            _ => "内容"
        };
        var source = string.IsNullOrWhiteSpace(_legacyScriptCommandClipboardScenarioName)
            ? "未知剧本"
            : _legacyScriptCommandClipboardScenarioName;
        var target = GetLegacyScriptScenarioName(scope);
        var sourceProject = FormatLegacyScriptProjectDisplay(
            _legacyScriptCommandClipboardProjectName,
            _legacyScriptCommandClipboardGameRoot);
        var targetProject = FormatLegacyScriptProjectDisplay(
            _project?.Name ?? string.Empty,
            _project?.GameRoot ?? string.Empty);
        var countText = blocked.Count == 1 ? "1 条" : $"{blocked.Count} 条";
        var actionText = payloadKind == LegacyScriptClipboardPayloadKind.Commands
            ? "请在来源中取消勾选 0x76，或在目标剧本中手工新建并重设跳转。"
            : "Scene/Section 粘贴不会自动删除内部 0x76；请回到来源只勾选安全命令，或在目标剧本中手工新建并重设跳转。";
        return $"跨项目粘贴包含 {countText} 0x76 跳转命令的 {payloadText}。跳转目标 ord 只在来源剧本内有效。来源项目：{sourceProject}，来源剧本：{source}；目标项目：{targetProject}，目标剧本：{target}。{actionText}";
    }

    private void AppendLegacyScriptPasteValidationSummary(
        StringBuilder builder,
        LegacyScriptPasteValidationResult validation)
    {
        builder.AppendLine();
        builder.AppendLine($"来源项目：{FormatLegacyScriptProjectDisplay(validation.SourceProjectName, validation.SourceGameRoot)}");
        builder.AppendLine($"来源剧本：{(string.IsNullOrWhiteSpace(validation.SourceScenarioName) ? "未知" : validation.SourceScenarioName)}");
        builder.AppendLine($"目标项目：{FormatLegacyScriptProjectDisplay(validation.TargetProjectName, validation.TargetGameRoot)}");
        builder.AppendLine($"目标剧本：{(string.IsNullOrWhiteSpace(validation.TargetScenarioName) ? "未知" : validation.TargetScenarioName)}");
        builder.AppendLine($"跨项目判定：{(validation.IsCrossScenario ? "是" : "否")}");
        if (!string.IsNullOrWhiteSpace(validation.ClipboardWarning))
        {
            builder.AppendLine("剪贴板提示：" + validation.ClipboardWarning);
        }

        if (validation.PayloadKind == LegacyScriptClipboardPayloadKind.Commands)
        {
            builder.AppendLine($"可安全粘贴命令数（排除 0x76）：{validation.SafeCommandCount}");
        }

        if (validation.BlockedJumpCommands.Count > 0)
        {
            builder.AppendLine(validation.IsCrossScenario && !validation.CanPaste
                ? $"阻止原因：包含 {validation.BlockedJumpCommands.Count} 条 0x76 跳转命令。"
                : $"跳转提示：包含 {validation.BlockedJumpCommands.Count} 条 0x76 跳转命令；同项目内粘贴后仍需核对跳转目标。");
            foreach (var blocked in validation.BlockedJumpCommands.Take(20))
            {
                builder.AppendLine(
                    $"- Scene {blocked.SceneIndex} / Section {blocked.SectionIndex} / Command {blocked.CommandIndex} / {blocked.CommandIdHex} / JumpTargetOrdinal={FormatNullableInt(blocked.JumpTargetOrdinal)} / JumpTargetCommandIndex={FormatNullableInt(blocked.JumpTargetCommandIndex)}");
            }
            if (validation.BlockedJumpCommands.Count > 20)
            {
                builder.AppendLine($"- ... 其余 {validation.BlockedJumpCommands.Count - 20} 条 0x76 已省略");
            }
        }

        if (!validation.CanPaste && !string.IsNullOrWhiteSpace(validation.Reason))
        {
            builder.AppendLine("预览结论：" + validation.Reason);
        }
        else
        {
            builder.AppendLine("预览结论：可粘贴；粘贴后仍需点击对应的“完整保存剧本”写入文件。");
        }
    }

    private static string FormatNullableInt(int? value)
        => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "无";

    private static string FormatLegacyScriptProjectDisplay(string projectName, string gameRoot)
    {
        if (!string.IsNullOrWhiteSpace(projectName) && !string.IsNullOrWhiteSpace(gameRoot))
        {
            return $"{projectName} ({gameRoot})";
        }

        if (!string.IsNullOrWhiteSpace(projectName))
        {
            return projectName;
        }

        return string.IsNullOrWhiteSpace(gameRoot) ? "未知" : gameRoot;
    }

    private static IReadOnlyList<LegacyScriptBlockedJumpInfo> CollectLegacyScriptBlockedJumpCommands(
        IEnumerable<LegacyScenarioScene> scenes)
        => scenes
            .SelectMany(scene => scene.Sections)
            .SelectMany(section => section.EnumerateCommands())
            .Where(command => command.CommandId == 0x76)
            .Select(CreateLegacyScriptBlockedJumpInfo)
            .ToList();

    private static IReadOnlyList<LegacyScriptBlockedJumpInfo> CollectLegacyScriptBlockedJumpCommands(
        IEnumerable<LegacyScenarioSection> sections)
        => sections
            .SelectMany(section => section.EnumerateCommands())
            .Where(command => command.CommandId == 0x76)
            .Select(CreateLegacyScriptBlockedJumpInfo)
            .ToList();

    private static IReadOnlyList<LegacyScriptBlockedJumpInfo> CollectLegacyScriptBlockedJumpCommands(
        IEnumerable<LegacyScenarioCommandNode> commands)
        => commands
            .SelectMany(EnumerateLegacyScriptCommandTree)
            .Where(command => command.CommandId == 0x76)
            .Select(CreateLegacyScriptBlockedJumpInfo)
            .ToList();

    private static LegacyScriptBlockedJumpInfo CreateLegacyScriptBlockedJumpInfo(LegacyScenarioCommandNode command)
        => new(
            command.SceneIndex,
            command.SectionIndex,
            command.CommandIndex,
            command.CommandIdHex,
            command.JumpTargetOrdinal,
            command.JumpTargetCommandIndex);

    private static IEnumerable<LegacyScenarioCommandNode> EnumerateLegacyScriptCommandTree(LegacyScenarioCommandNode command)
    {
        yield return command;
        if (command.ChildBlock == null)
        {
            yield break;
        }

        foreach (var child in command.ChildBlock.Commands)
        {
            foreach (var nested in EnumerateLegacyScriptCommandTree(child))
            {
                yield return nested;
            }
        }
    }

    private static int CountLegacyScriptSafeCommands(IEnumerable<LegacyScenarioScene> scenes)
        => scenes
            .SelectMany(scene => scene.Sections)
            .SelectMany(section => section.EnumerateCommands())
            .Count(command => command.CommandId != 0x76);

    private static int CountLegacyScriptSafeCommands(IEnumerable<LegacyScenarioSection> sections)
        => sections
            .SelectMany(section => section.EnumerateCommands())
            .Count(command => command.CommandId != 0x76);

    private static int CountLegacyScriptSafeCommands(IEnumerable<LegacyScenarioCommandNode> commands)
        => commands
            .SelectMany(EnumerateLegacyScriptCommandTree)
            .Count(command => command.CommandId != 0x76);

    private bool TryEnsureLegacyScriptClipboardAvailable(out string reason)
        => TryLoadLegacyScriptClipboardFromSystemClipboard(out reason, allowMemoryFallback: true);

    private bool TryLoadLegacyScriptClipboardFromSystemClipboard(out string reason, bool allowMemoryFallback = false)
    {
        reason = string.Empty;
        string text;
        try
        {
            if (!Clipboard.ContainsText())
            {
                reason = "系统剪贴板没有文本，也没有可粘贴的 CCZModStudio 剧本结构数据。请先复制命令、Section 或 Scene。";
                if (_legacyScriptClipboardMemoryOnly)
                {
                    _legacyScriptClipboardWarning = reason + " 已保留本窗口内存剪贴板；只有系统剪贴板读取失败时才会回退使用。";
                    return false;
                }

                ClearLegacyScriptClipboardCache();
                return false;
            }

            text = Clipboard.GetText();
        }
        catch (Exception ex)
        {
            reason = "读取系统剪贴板失败：" + ex.Message;
            return TryUseLegacyScriptMemoryClipboardFallback(ref reason, allowMemoryFallback);
        }

        if (!TryReadLegacyScriptClipboardEnvelope(text, out var envelope, out reason))
        {
            if (_legacyScriptClipboardMemoryOnly)
            {
                reason = "系统剪贴板没有 CCZModStudio 剧本命令/Section/Scene 结构数据；已保留本窗口内存剪贴板，但普通粘贴不会使用它，只有系统剪贴板读取失败时才会回退。";
                _legacyScriptClipboardWarning = reason;
                return false;
            }

            ClearLegacyScriptClipboardCache();
            reason = "系统剪贴板没有 CCZModStudio 剧本命令/Section/Scene 结构数据；已清空旧的内存剪贴板，避免粘贴过期内容。请用新版剧本制作页重新复制。";
            return false;
        }

        var fingerprint = BuildLegacyScriptClipboardFingerprint(text);
        if (string.Equals(_legacyScriptClipboardFingerprint, fingerprint, StringComparison.Ordinal) &&
            !_legacyScriptClipboardMemoryOnly &&
            TryHasLegacyScriptClipboardCache())
        {
            _legacyScriptClipboardWarning = string.Empty;
            reason = string.Empty;
            return true;
        }

        var commands = envelope.Commands
            .Select(CreateLegacyScriptCommandFromClipboard)
            .ToList();
        var scenes = envelope.Scenes
            .Select(CreateLegacyScriptSceneFromClipboard)
            .ToList();
        var sections = envelope.Sections
            .Select(CreateLegacyScriptSectionFromClipboard)
            .ToList();
        if (commands.Count == 0 && scenes.Count == 0 && sections.Count == 0)
        {
            reason = "剪贴板中的剧本命令、Section 或 Scene 为空。";
            return false;
        }

        _legacyScriptCommandClipboardItems = commands;
        _legacyScriptCommandClipboard = commands.FirstOrDefault();
        _legacyScriptSceneClipboardItems = scenes;
        _legacyScriptSectionClipboardItems = sections;
        _legacyScriptCommandClipboardProjectName = string.IsNullOrWhiteSpace(envelope.SourceProjectName)
            ? "外部项目"
            : envelope.SourceProjectName;
        _legacyScriptCommandClipboardScenarioName = string.IsNullOrWhiteSpace(envelope.SourceScenarioName)
            ? "外部项目"
            : envelope.SourceScenarioName;
        _legacyScriptCommandClipboardGameRoot = envelope.SourceGameRoot ?? string.Empty;
        _legacyScriptClipboardFingerprint = fingerprint;
        _legacyScriptClipboardWarning = string.Empty;
        _legacyScriptClipboardMemoryOnly = false;
        _scriptCommandClipboardItem = null;
        reason = string.Empty;
        return true;
    }

    private bool TryUseLegacyScriptMemoryClipboardFallback(ref string reason, bool allowMemoryFallback)
    {
        if (!allowMemoryFallback || !TryHasLegacyScriptClipboardCache())
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(reason))
        {
            reason += " ";
        }
        reason += "正在使用本窗口内存剪贴板。";
        _legacyScriptClipboardWarning = reason;
        return true;
    }

    private bool TryHasLegacyScriptClipboardCache()
        => _legacyScriptSceneClipboardItems.Count > 0 ||
           _legacyScriptSectionClipboardItems.Count > 0 ||
           _legacyScriptCommandClipboardItems.Count > 0 ||
           _legacyScriptCommandClipboard != null;

    private void ClearLegacyScriptClipboardCache()
    {
        _legacyScriptCommandClipboard = null;
        _legacyScriptCommandClipboardItems = Array.Empty<LegacyScenarioCommandNode>();
        _legacyScriptSceneClipboardItems = Array.Empty<LegacyScenarioScene>();
        _legacyScriptSectionClipboardItems = Array.Empty<LegacyScenarioSection>();
        _legacyScriptCommandClipboardProjectName = string.Empty;
        _legacyScriptCommandClipboardScenarioName = string.Empty;
        _legacyScriptCommandClipboardGameRoot = string.Empty;
        _legacyScriptClipboardFingerprint = string.Empty;
        _legacyScriptClipboardWarning = string.Empty;
        _legacyScriptClipboardMemoryOnly = false;
    }

    private static string BuildLegacyScriptClipboardFingerprint(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static bool TryReadLegacyScriptClipboardEnvelope(
        string text,
        out LegacyScriptClipboardEnvelope envelope,
        out string reason)
    {
        envelope = null!;
        reason = string.Empty;
        var start = text.IndexOf(LegacyScriptClipboardBeginMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            reason = "系统剪贴板没有 CCZModStudio 剧本命令/Section/Scene 结构数据；请用新版剧本制作页复制。";
            return false;
        }

        start += LegacyScriptClipboardBeginMarker.Length;
        var end = text.IndexOf(LegacyScriptClipboardEndMarker, start, StringComparison.Ordinal);
        if (end < 0)
        {
            reason = "系统剪贴板中的 CCZModStudio 剧本命令结构数据不完整。";
            return false;
        }

        var json = text[start..end].Trim();
        try
        {
            var parsed = JsonSerializer.Deserialize<LegacyScriptClipboardEnvelope>(json, LegacyScriptClipboardJsonOptions);
            if (parsed == null ||
                !string.Equals(parsed.Format, LegacyScriptClipboardFormat, StringComparison.Ordinal) ||
                parsed.Version != 1 ||
                (parsed.Commands.Count == 0 && parsed.Scenes.Count == 0 && parsed.Sections.Count == 0))
            {
                reason = "系统剪贴板中的 CCZModStudio 剧本命令结构版本无效或为空。";
                return false;
            }

            envelope = parsed;
            return true;
        }
        catch (Exception ex)
        {
            reason = "解析系统剪贴板中的剧本命令结构失败：" + ex.Message;
            return false;
        }
    }

    private LegacyScenarioScene CreateLegacyScriptSceneFromClipboard(LegacyScriptClipboardScene source)
    {
        var scene = new LegacyScenarioScene
        {
            SceneIndex = source.SceneIndex,
            FileOffset = 0
        };

        foreach (var section in source.Sections)
        {
            scene.Sections.Add(CreateLegacyScriptSectionFromClipboard(section));
        }

        return scene;
    }

    private LegacyScenarioSection CreateLegacyScriptSectionFromClipboard(LegacyScriptClipboardSection source)
    {
        var section = new LegacyScenarioSection
        {
            SceneIndex = source.SceneIndex,
            SectionIndex = source.SectionIndex,
            FileOffset = 0,
            LengthPrefixOffset = 0,
            DeclaredLength = 0
        };

        foreach (var command in source.Commands)
        {
            section.Commands.Add(CreateLegacyScriptCommandFromClipboard(command));
        }

        return section;
    }

    private LegacyScenarioCommandNode CreateLegacyScriptCommandFromClipboard(LegacyScriptClipboardCommand source)
    {
        var command = new LegacyScenarioCommandNode
        {
            SceneIndex = source.SceneIndex,
            SectionIndex = source.SectionIndex,
            CommandIndex = source.CommandIndex,
            CommandOrdinal = source.CommandOrdinal,
            CommandId = source.CommandId,
            CommandName = string.IsNullOrWhiteSpace(source.CommandName)
                ? ResolveLegacyScriptCommandName(source.CommandId)
                : source.CommandName,
            FileOffset = 0,
            ConsumedBytes = 0,
            StartsBodyBlock = source.StartsBodyBlock,
            IsSubEventMarker = source.IsSubEventMarker,
            OpensSubEventBlock = source.OpensSubEventBlock,
            EndsSubEventBlock = source.EndsSubEventBlock,
            JumpTargetOrdinal = source.JumpTargetOrdinal,
            JumpTargetCommandIndex = source.JumpTargetCommandIndex,
            OriginalJumpDisplacement = source.OriginalJumpDisplacement
        };

        foreach (var parameter in source.Parameters)
        {
            command.Parameters.Add(CreateLegacyScriptParameterFromClipboard(parameter));
        }

        if (source.ChildBlock != null)
        {
            command.ChildBlock = CreateLegacyScriptCommandBlockFromClipboard(source.ChildBlock);
        }

        return command;
    }

    private LegacyScenarioCommandBlock CreateLegacyScriptCommandBlockFromClipboard(LegacyScriptClipboardBlock source)
    {
        var block = new LegacyScenarioCommandBlock
        {
            Kind = source.Kind,
            LengthPrefixOffset = 0,
            FileOffset = 0,
            DeclaredLength = 0
        };
        foreach (var command in source.Commands)
        {
            block.Commands.Add(CreateLegacyScriptCommandFromClipboard(command));
        }

        return block;
    }

    private LegacyScenarioCommandParameter CreateLegacyScriptParameterFromClipboard(LegacyScriptClipboardParameter source)
    {
        var parameter = new LegacyScenarioCommandParameter
        {
            Index = source.Index,
            LayoutCode = source.LayoutCode,
            Tag = source.Tag,
            FileOffset = AllocateLegacyScriptSyntheticOffset(),
            Kind = source.Kind,
            IntValue = source.IntValue,
            Text = source.Text,
            ByteLength = source.ByteLength
        };
        parameter.Values.AddRange(source.Values);
        return parameter;
    }

    private static bool LegacyScriptCommandTreeContainsCommandId(LegacyScenarioCommandNode command, int commandId)
    {
        if (command.CommandId == commandId)
        {
            return true;
        }

        return command.ChildBlock?.Commands.Any(child => LegacyScriptCommandTreeContainsCommandId(child, commandId)) == true;
    }

    private void MoveSelectedLegacyScriptCommand(bool up)
        => MoveSelectedLegacyScriptCommand(LegacyScriptEditorScope.Script, up);

    private void MoveSelectedLegacyScriptCommand(LegacyScriptEditorScope scope, bool up)
    {
        if (!TryGetSelectedLegacyScriptCommand(scope, out var selected))
        {
            MessageBox.Show(this, "请先选择要移动的命令。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!CanMoveLegacyScriptCommand(scope, selected, up, out var reason))
        {
            MessageBox.Show(this, reason, "无法移动命令", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var document = GetCurrentLegacyScriptDocument(scope);
        if (document == null ||
            !TryFindLegacyCommandList(document, selected, out var list, out var index))
        {
            return;
        }

        var (startIndex, count) = GetLegacyScriptMoveRange(list, index);
        var insertIndex = GetLegacyScriptMoveInsertIndex(list, startIndex, count, up);
        if (insertIndex < 0) return;
        var movingCommands = list.GetRange(startIndex, count);
        ApplyLegacyScriptStructureEdit(
            scope,
            () =>
            {
                list.RemoveRange(startIndex, count);
                if (insertIndex > startIndex)
                {
                    insertIndex -= count;
                }
                list.InsertRange(insertIndex, movingCommands);
            },
            selected,
            $"已{(up ? "上移" : "下移")}命令：{selected.CommandIdHex} {selected.CommandName}。",
            new LegacyScriptStructureEditOptions(FindLegacyScriptSectionForCommand(document, selected)));
    }

    private bool CanMoveLegacyScriptCommand(LegacyScenarioCommandNode? command, bool up, out string reason)
        => CanMoveLegacyScriptCommand(LegacyScriptEditorScope.Script, command, up, out reason);

    private bool CanMoveLegacyScriptCommand(LegacyScriptEditorScope scope, LegacyScenarioCommandNode? command, bool up, out string reason)
    {
        var document = GetCurrentLegacyScriptDocument(scope);
        if (document == null)
        {
            reason = "当前没有可编辑的旧版完整剧本树。";
            return false;
        }

        if (command == null)
        {
            reason = "请先选择要移动的命令。";
            return false;
        }

        if (!IsMovableLegacyScriptCommand(document, command, out reason))
        {
            return false;
        }

        if (!TryFindLegacyCommandList(document, command, out var list, out var index))
        {
            reason = "没有在当前旧版命令树中定位到该命令。";
            return false;
        }

        var (startIndex, count) = GetLegacyScriptMoveRange(list, index);
        if (GetLegacyScriptMoveInsertIndex(list, startIndex, count, up) < 0)
        {
            reason = up ? "已经到当前命令列表顶部，不能再上移。" : "已经到当前命令列表尾部，不能再下移。";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private void ApplyLegacyScriptStructureEdit(
        Action edit,
        LegacyScenarioCommandNode? preferredSelection,
        string statusText,
        LegacyScriptStructureEditOptions? options = null)
    {
        ApplyLegacyScriptStructureEdit(LegacyScriptEditorScope.Script, edit, preferredSelection, statusText, options);
    }

    private sealed record LegacyScriptStructureEditOptions(
        LegacyScenarioSection? AffectedSection = null,
        bool AllowSectionRefresh = true);

    private void RefreshLegacyScriptView(
        LegacyScenarioCommandNode? preferredSelection,
        int? preferredParameterIndex = null,
        LegacyScriptViewportSnapshot? viewportSnapshot = null,
        bool preserveViewport = true)
    {
        if (_currentLegacyScriptDocument == null) return;

        var viewport = preserveViewport
            ? viewportSnapshot ?? CaptureLegacyScriptViewport(LegacyScriptEditorScope.Script)
            : null;
        var selectionRestored = false;
        _bindingScriptDocument = true;
        _scriptTree.SuspendLayout();
        _scriptCommandGrid.SuspendLayout();
        _scriptParameterGrid.SuspendLayout();
        _scriptTextGrid.SuspendLayout();
        _scriptSearchResultGrid.SuspendLayout();
        try
        {
            _currentScriptStructure = BuildLegacyScriptStructureResult(_currentLegacyScriptDocument);
            _currentScriptTextEntries = BuildLegacyScriptTextEntries(_currentLegacyScriptDocument);
            _currentScriptSearchResults = Array.Empty<ScenarioSearchResultRow>();
            BuildScriptTree(_currentScriptStructure, _currentScriptTextEntries);
            if (!preserveViewport)
            {
                BindScriptCommandRows(Array.Empty<ScenarioStructureRow>());
                BindScriptParameterRows(Array.Empty<ScenarioCommandParameterRow>());
                BindScriptTextRows(_currentScriptTextEntries);
                BindScriptSearchResultRows(_currentScriptSearchResults);
                _scriptTextEditorBox.Clear();
                UpdateScriptTextCapacityLabel();
            }
            _scriptHeaderLabel.Text =
                $"字典：已加载    剧本：{_currentLegacyScriptDocument.FileName}    模式：旧版完整树    命令：{_currentLegacyScriptDocument.CommandCount}    文本：{_currentScriptTextEntries.Count}";
        }
        finally
        {
            _scriptSearchResultGrid.ResumeLayout();
            _scriptTextGrid.ResumeLayout();
            _scriptParameterGrid.ResumeLayout();
            _scriptCommandGrid.ResumeLayout();
            _scriptTree.ResumeLayout();
            _bindingScriptDocument = false;
        }

        if (preferredSelection != null && TrySelectLegacyScriptCommand(preferredSelection, ensureVisible: !preserveViewport))
        {
            ShowSelectedScriptTreeNode();
            if (preferredParameterIndex.HasValue && TrySelectLegacyScriptParameterRow(preferredParameterIndex.Value))
            {
                ShowSelectedLegacyScriptParameter();
            }
            selectionRestored = true;
        }
        else if (preserveViewport && TryRestoreLegacyScriptSelectedNode(viewport))
        {
            ShowSelectedScriptTreeNode();
            selectionRestored = true;
        }
        else if (SelectDefaultScriptTreeNode(ensureVisible: !preserveViewport))
        {
            ShowSelectedScriptTreeNode();
            selectionRestored = true;
        }

        RestoreLegacyScriptViewport(viewport);
        if (!selectionRestored)
        {
            if (preserveViewport)
            {
                SuppressScriptSelectionEvents(() =>
                {
                    BindScriptCommandRows(Array.Empty<ScenarioStructureRow>());
                    BindScriptParameterRows(Array.Empty<ScenarioCommandParameterRow>());
                    BindScriptTextRows(_currentScriptTextEntries);
                    BindScriptSearchResultRows(_currentScriptSearchResults);
                });
                _scriptTextEditorBox.Clear();
                UpdateScriptTextCapacityLabel();
            }
            _scriptDetailBox.Text = BuildScriptOverview(_currentScriptStructure, _currentScriptTextEntries);
        }

        UpdateScriptStructureEditButtons();
        _scriptVariableUsageDialog?.RefreshCurrentScenario();
    }

    private void RefreshLegacyScriptMetadataOnly()
    {
        if (_currentLegacyScriptDocument == null)
        {
            return;
        }

        _currentScriptStructure = BuildLegacyScriptStructureResult(_currentLegacyScriptDocument);
        _currentScriptTextEntries = BuildLegacyScriptTextEntries(_currentLegacyScriptDocument);
        _currentScriptSearchResults = Array.Empty<ScenarioSearchResultRow>();
        ReconcileLegacyScriptItemDataIndex(_currentLegacyScriptDocument);
        _scriptHeaderLabel.Text =
            $"字典：已加载    剧本：{_currentLegacyScriptDocument.FileName}    模式：旧版完整树    命令：{_currentLegacyScriptDocument.CommandCount}    文本：{_currentScriptTextEntries.Count}";
    }

    private bool RefreshLegacyEditorMetadataOnly(LegacyScriptEditorScope scope)
    {
        var document = GetCurrentLegacyScriptDocument(scope);
        if (document == null)
        {
            return false;
        }

        switch (scope)
        {
            case LegacyScriptEditorScope.Script:
                RefreshLegacyScriptMetadataOnly();
                return true;
            case LegacyScriptEditorScope.Battlefield:
                _battlefieldScriptCommandByKey.Clear();
                _battlefieldScriptTextByOffset.Clear();
                _battlefieldScriptTextEntryByOffset.Clear();
                _currentBattlefieldScriptStructure = BuildBattlefieldLegacyScriptStructureResult(document);
                _currentBattlefieldScriptTextEntries = BuildBattlefieldLegacyScriptTextEntries(document);
                ReconcileLegacyEditorItemDataIndex(
                    document,
                    _currentBattlefieldScriptStructure,
                    _battlefieldScriptItemDataByCommand,
                    _battlefieldScriptItemDataByRow);
                return true;
            case LegacyScriptEditorScope.RScene:
                _rSceneScriptCommandByKey.Clear();
                _currentRSceneScriptStructure = BuildRSceneLegacyScriptStructureResult(document);
                _currentRSceneScriptTextEntries = BuildRSceneLegacyScriptTextEntries(document);
                ReconcileLegacyEditorItemDataIndex(
                    document,
                    _currentRSceneScriptStructure,
                    _rSceneScriptItemDataByCommand,
                    _rSceneScriptItemDataByRow);
                _currentRSceneCommandCandidates = BuildRSceneCommandCandidates(document);
                _currentRSceneStateCandidates = _rSceneDraftService.BuildSceneStateCandidates(document, BuildRSceneVariableSnapshotForCommand);
                var restoreRSceneCommandSelectionBinding = _bindingRSceneCommandSelection;
                _bindingRSceneCommandSelection = true;
                try
                {
                    BindRSceneStateCandidates(_currentRSceneStateCandidates);
                }
                finally
                {
                    _bindingRSceneCommandSelection = restoreRSceneCommandSelectionBinding;
                }
                return true;
            default:
                return false;
        }
    }

    private void ReconcileLegacyEditorItemDataIndex(
        LegacyScenarioDocument document,
        ScenarioStructureProbeResult structure,
        Dictionary<LegacyScenarioCommandNode, LegacyScenarioItemData> itemDataByCommand,
        Dictionary<ScenarioStructureRow, LegacyScenarioItemData> itemDataByRow)
    {
        var activeCommands = document.EnumerateCommands().ToHashSet();
        foreach (var command in itemDataByCommand.Keys.Where(command => !activeCommands.Contains(command)).ToList())
        {
            itemDataByCommand.Remove(command);
        }

        itemDataByRow.Clear();
        var rowByKey = structure.Rows
            .Where(row => row.NodeType == "Command候选")
            .ToDictionaryFirstByKey(BuildLegacyCommandKey, row => row, StringComparer.OrdinalIgnoreCase);
        foreach (var command in activeCommands)
        {
            rowByKey.TryGetValue(BuildLegacyCommandKey(command), out var row);
            if (!itemDataByCommand.TryGetValue(command, out var itemData))
            {
                itemData = CreateLegacyScriptItemData(command, row);
                itemDataByCommand[command] = itemData;
            }
            else
            {
                itemData.Ord = command.CommandOrdinal;
                itemData.UiRow = row;
                CopyLegacyCommandToItemData(command, itemData);
            }

            if (row != null)
            {
                itemDataByRow[row] = itemData;
            }
        }
    }

    private bool RefreshLegacyScriptCommandInPlace(
        LegacyScenarioCommandNode command,
        int? preferredParameterIndex = null,
        string? detailSuffix = null)
    {
        if (_currentLegacyScriptDocument == null || _currentScriptStructure == null)
        {
            return false;
        }

        RefreshLegacyScriptMetadataOnly();
        if (!_legacyScriptRowByKey.TryGetValue(BuildLegacyCommandKey(command), out var row))
        {
            return false;
        }

        UpdateLegacyScriptCommandTreeNodes(command, row);
        if (!TrySelectLegacyScriptCommand(command, ensureVisible: false))
        {
            return false;
        }

        ShowSelectedScriptTreeNode();
        if (preferredParameterIndex.HasValue && TrySelectLegacyScriptParameterRow(preferredParameterIndex.Value))
        {
            ShowSelectedLegacyScriptParameter();
        }

        if (!string.IsNullOrWhiteSpace(detailSuffix))
        {
            _scriptDetailBox.Text += detailSuffix;
        }

        _scriptVariableUsageDialog?.RefreshCurrentScenario();
        return true;
    }

    private bool RefreshLegacyEditorCommandInPlace(
        LegacyScriptEditorScope scope,
        LegacyScenarioCommandNode command,
        int? preferredParameterIndex = null,
        string? detailSuffix = null)
    {
        if (!RefreshLegacyEditorMetadataOnly(scope))
        {
            return false;
        }

        var row = GetLegacyScriptRowForCommand(scope, command);
        if (row == null)
        {
            return false;
        }

        UpdateLegacyEditorCommandTreeNodes(scope, command, row);
        if (!TrySelectLegacyScriptCommand(scope, command, ensureVisible: false))
        {
            return false;
        }

        ShowSelectedLegacyScriptTreeNode(scope);
        if (preferredParameterIndex.HasValue)
        {
            switch (scope)
            {
                case LegacyScriptEditorScope.Script:
                    if (TrySelectLegacyScriptParameterRow(preferredParameterIndex.Value))
                    {
                        ShowSelectedLegacyScriptParameter();
                    }
                    break;
                case LegacyScriptEditorScope.Battlefield:
                    if (TrySelectBattlefieldScriptParameterRow(preferredParameterIndex.Value))
                    {
                        ShowSelectedBattlefieldScriptParameter();
                    }
                    break;
                case LegacyScriptEditorScope.RScene:
                    LoadInlineRSceneScriptDialogForSelection();
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(detailSuffix))
        {
            AppendLegacyScriptDetailText(scope, detailSuffix);
        }

        return true;
    }

    private void UpdateLegacyScriptCommandTreeNodes(LegacyScenarioCommandNode command, ScenarioStructureRow row)
    {
        foreach (TreeNode root in _scriptTree.Nodes)
        {
            UpdateLegacyScriptCommandTreeNodes(root, command, row);
        }
    }

    private void UpdateLegacyScriptCommandTreeNodes(TreeNode node, LegacyScenarioCommandNode command, ScenarioStructureRow row)
    {
        if (node.Tag is LegacyScenarioItemData itemData && ReferenceEquals(itemData.Command, command))
        {
            itemData.UiRow = row;
            itemData.Ord = command.CommandOrdinal;
            CopyLegacyCommandToItemData(command, itemData);
            _legacyScriptItemDataByCommand[command] = itemData;
            _legacyScriptItemDataByRow[row] = itemData;
            node.Text = BuildLegacyScriptCommandSummary(row, command, includeIdentity: false, maxVisibleValues: 6);
            node.ToolTipText = BuildLegacyScriptCommandTreeToolTip(row, command);
            node.ForeColor = GetLegacyScriptCommandColor(command);
        }
        else if (node.Tag is ScenarioStructureRow oldRow &&
                 oldRow.NodeType == "Command候选" &&
                 IsSameLegacyCommandIdentity(oldRow, command))
        {
            node.Tag = row;
            node.Text = BuildScriptCommandSummary(row, includeIdentity: true, maxVisibleValues: 6);
            node.ToolTipText = BuildScriptCommandTreeToolTip(row);
            node.ForeColor = GetScriptCommandColor(row.CommandId);
        }

        foreach (TreeNode child in node.Nodes)
        {
            UpdateLegacyScriptCommandTreeNodes(child, command, row);
        }
    }

    private void UpdateLegacyEditorCommandTreeNodes(
        LegacyScriptEditorScope scope,
        LegacyScenarioCommandNode command,
        ScenarioStructureRow row)
    {
        var tree = GetLegacyScriptTree(scope);
        foreach (TreeNode root in tree.Nodes)
        {
            UpdateLegacyEditorCommandTreeNodes(scope, root, command, row);
        }
    }

    private void UpdateLegacyEditorCommandTreeNodes(
        LegacyScriptEditorScope scope,
        TreeNode node,
        LegacyScenarioCommandNode command,
        ScenarioStructureRow row)
    {
        if (node.Tag is LegacyScenarioItemData itemData && ReferenceEquals(itemData.Command, command))
        {
            itemData.UiRow = row;
            itemData.Ord = command.CommandOrdinal;
            CopyLegacyCommandToItemData(command, itemData);
            SetLegacyEditorItemDataIndexes(scope, command, row, itemData);
            node.Text = BuildLegacyScriptCommandSummary(row, command, includeIdentity: false, maxVisibleValues: 6);
            node.ToolTipText = BuildLegacyScriptCommandTreeToolTip(row, command);
            node.ForeColor = GetLegacyScriptCommandColor(command);
            if (scope == LegacyScriptEditorScope.Battlefield)
            {
                ApplyBattlefieldScriptPreviewToNode(node, row);
            }
        }
        else if (node.Tag is ScenarioStructureRow oldRow &&
                 oldRow.NodeType == "Command候选" &&
                 IsSameLegacyCommandIdentity(oldRow, command))
        {
            node.Tag = row;
            node.Text = BuildScriptCommandSummary(row, includeIdentity: true, maxVisibleValues: 6);
            node.ToolTipText = BuildScriptCommandTreeToolTip(row);
            node.ForeColor = GetScriptCommandColor(row.CommandId);
            if (scope == LegacyScriptEditorScope.Battlefield)
            {
                ApplyBattlefieldScriptPreviewToNode(node, row);
            }
        }

        foreach (TreeNode child in node.Nodes)
        {
            UpdateLegacyEditorCommandTreeNodes(scope, child, command, row);
        }
    }

    private void SetLegacyEditorItemDataIndexes(
        LegacyScriptEditorScope scope,
        LegacyScenarioCommandNode command,
        ScenarioStructureRow row,
        LegacyScenarioItemData itemData)
    {
        switch (scope)
        {
            case LegacyScriptEditorScope.Script:
                _legacyScriptItemDataByCommand[command] = itemData;
                _legacyScriptItemDataByRow[row] = itemData;
                break;
            case LegacyScriptEditorScope.Battlefield:
                _battlefieldScriptItemDataByCommand[command] = itemData;
                _battlefieldScriptItemDataByRow[row] = itemData;
                break;
            case LegacyScriptEditorScope.RScene:
                _rSceneScriptItemDataByCommand[command] = itemData;
                _rSceneScriptItemDataByRow[row] = itemData;
                break;
        }
    }

    private static bool IsSameLegacyCommandIdentity(ScenarioStructureRow row, LegacyScenarioCommandNode command)
        => row.SceneIndex == command.SceneIndex &&
           row.SectionIndex == command.SectionIndex &&
           row.CommandIndex == command.CommandIndex &&
           row.CommandId == command.CommandId &&
           HexDisplayFormatter.EqualsText(row.OffsetHex, HexDisplayFormatter.FormatOffset(command.FileOffset));

    private void ReconcileLegacyScriptItemDataIndex(LegacyScenarioDocument document)
    {
        var activeCommands = document.EnumerateCommands().ToHashSet();
        foreach (var command in _legacyScriptItemDataByCommand.Keys.Where(command => !activeCommands.Contains(command)).ToList())
        {
            _legacyScriptItemDataByCommand.Remove(command);
        }

        _legacyScriptItemDataByRow.Clear();
        foreach (var command in activeCommands)
        {
            _legacyScriptRowByKey.TryGetValue(BuildLegacyCommandKey(command), out var row);
            if (!_legacyScriptItemDataByCommand.TryGetValue(command, out var itemData))
            {
                itemData = CreateLegacyScriptItemData(command, row);
                _legacyScriptItemDataByCommand[command] = itemData;
            }
            else
            {
                itemData.Ord = command.CommandOrdinal;
                itemData.UiRow = row;
                CopyLegacyCommandToItemData(command, itemData);
            }

            if (row != null)
            {
                _legacyScriptItemDataByRow[row] = itemData;
            }
        }
    }

    private void MarkLegacyScriptSavedInPlace(LegacyScenarioWriteResult result)
    {
        if (_currentLegacyScriptDocument == null || _currentScriptStructure == null)
        {
            return;
        }

        ClearLegacyScenarioHistory(LegacyScriptEditorScope.Script);
        InvalidateScriptVariableProjectCache();
        UpdateScriptStructureEditButtons();
        _saveScriptStructureButton.Enabled = false;
        _saveScriptTextButton.Enabled = false;
        _scriptVariableUsageDialog?.RefreshCurrentScenario();
        _scriptDetailBox.Text +=
            $"\r\n\r\n完整保存完成：变化 {result.ChangedBytes} 字节。\r\n校验：{result.ValidationSummary}\r\n备份：{result.BackupPath}\r\n报告：{result.ReportJsonPath}";
    }

    private void UpdateScriptTextEntryAfterSave(ScenarioTextEntry entry, string savedText)
    {
        entry.Text = savedText;
        entry.OriginalText = savedText;
        entry.CharLength = savedText.Length;
        entry.HasNewLines = savedText.Contains('\n') || savedText.Contains('\r');
        entry.Preview = savedText.Length > 60 ? savedText[..60] : savedText;
    }

    private void UpdateScriptTextTreeNodes(ScenarioTextEntry entry)
    {
        foreach (TreeNode root in _scriptTree.Nodes)
        {
            UpdateScriptTextTreeNodes(root, entry);
        }
    }

    private static void UpdateScriptTextTreeNodes(TreeNode node, ScenarioTextEntry entry)
    {
        if (node.Tag is ScenarioTextEntry text && text.Offset == entry.Offset)
        {
            node.Tag = entry;
            node.Text = BuildScriptTextNodeText(entry);
            node.ToolTipText = BuildScriptTextTreeToolTip(entry);
        }

        foreach (TreeNode child in node.Nodes)
        {
            UpdateScriptTextTreeNodes(child, entry);
        }
    }

    private bool TrySelectLegacyScriptCommand(LegacyScenarioCommandNode command, bool ensureVisible = true)
    {
        if (_currentScriptStructure == null) return false;
        var key = BuildLegacyCommandKey(command);
        if (!_legacyScriptRowByKey.TryGetValue(key, out var row)) return false;
        return SelectScriptTreeNode(row, suppressEvents: true, ensureVisible: ensureVisible);
    }

    private LegacyScenarioCommandNode? CreateLegacyScriptCommand(int sceneIndex, int sectionIndex)
    {
        if (!EnsureScriptCommandComboReady())
        {
            MessageBox.Show(this, "当前没有可新增的命令候选。请先读取 CczString.ini 剧本字典后再添加。", "无法新增命令", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        if (_scriptNewCommandCombo.SelectedItem is not ScriptCommandComboItem item)
        {
            MessageBox.Show(this, "请先选择要新增的命令。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        return CreateLegacyScriptCommand(sceneIndex, sectionIndex, item);
    }

    private LegacyScenarioCommandNode? CreateLegacyScriptCommand(
        int sceneIndex,
        int sectionIndex,
        ScriptCommandComboItem item)
    {
        if (IsBlockedNewLegacyCommandId(item.Id))
        {
            MessageBox.Show(this, "该命令属于结构/跳转控制命令，当前界面不直接新建；请复制旧命令或在旧工具中核对后再处理。", "暂不支持新增", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        var command = new LegacyScenarioCommandNode
        {
            SceneIndex = sceneIndex,
            SectionIndex = sectionIndex,
            CommandId = item.Id,
            CommandName = item.Name,
            FileOffset = 0,
            ConsumedBytes = 0
        };

        foreach (var parameter in CreateDefaultLegacyScriptParameters(item.Id))
        {
            command.Parameters.Add(parameter);
        }

        return command;
    }

    private LegacyScenarioCommandNode? CreateScenarioTextImportCommand(int commandId, int sceneIndex, int sectionIndex)
    {
        if (!EnsureScriptCommandComboReady() ||
            _scriptNewCommandCombo.Items
                .OfType<ScriptCommandComboItem>()
                .FirstOrDefault(candidate => candidate.Id == commandId) is not { } item)
        {
            return null;
        }

        return CreateLegacyScriptCommand(sceneIndex, sectionIndex, item);
    }

    private IReadOnlyList<LegacyScenarioCommandParameter> CreateDefaultLegacyScriptParameters(int commandId)
    {
        var instructions = ScenarioStructureProbeReader.LegacyCommandInstructionTable[commandId];
        var result = new List<LegacyScenarioCommandParameter>();
        var parameterCount = GetLegacyInstructionCount(commandId);
        for (var index = 0; index < parameterCount; index++)
        {
            var layoutCode = GetLegacyInstructionAt(commandId, instructions, index);
            if (layoutCode == -1) break;

            var parameter = new LegacyScenarioCommandParameter
            {
                Index = result.Count,
                LayoutCode = layoutCode,
                Tag = layoutCode,
                FileOffset = AllocateLegacyScriptSyntheticOffset(),
                Kind = layoutCode switch
                {
                    0x05 => LegacyScenarioParameterKind.Text,
                    0x35 => LegacyScenarioParameterKind.VariableArray,
                    0x04 => LegacyScenarioParameterKind.Dword32,
                    _ => LegacyScenarioParameterKind.Word16
                },
                ByteLength = layoutCode switch
                {
                    0x05 => 1,
                    0x35 => 2,
                    0x04 => 4,
                    _ => 2
                },
                IntValue = GetDefaultLegacyScriptParameterValue(commandId, index)
            };
            result.Add(parameter);
        }

        return result;
    }

    private static int GetDefaultLegacyScriptParameterValue(int commandId, int parameterIndex)
    {
        if (IsForceAllyDeploymentPersonParameter(commandId, parameterIndex))
        {
            return LegacyMfcDialogDataSources.EmptyPerson1Code;
        }

        var definition = BattlefieldDeploymentRecordDefinition.FromCommandId(commandId);
        if (definition is { WritesPerson: true } &&
            definition.PersonIndex >= 0 &&
            definition.GroupSize > 0 &&
            parameterIndex % definition.GroupSize == definition.PersonIndex)
        {
            return BattlefieldDeploymentRecordFormatter.EmptyPerson2Code;
        }

        return 0;
    }

    private static bool IsForceAllyDeploymentPersonParameter(int commandId, int parameterIndex)
        => commandId == 0x4A && parameterIndex is >= 1 and <= 10;

    private int AllocateLegacyScriptSyntheticOffset()
        => _nextLegacyScriptSyntheticOffset--;

    private static int GetLegacyInstructionCount(int commandId)
        => commandId switch
        {
            0x46 => 11 * 20,
            0x47 => 12 * 80,
            _ => 13
        };

    private static int GetLegacyInstructionAt(int commandId, IReadOnlyList<int> instructions, int index)
        => commandId switch
        {
            0x46 => instructions[index % 11],
            0x47 => instructions[index % 12],
            _ => instructions[index]
        };

    private static bool IsBlockedNewLegacyCommandId(int commandId)
        => commandId is 0x00 or 0x01;

    private bool TryGetSelectedLegacyScriptCommand(out LegacyScenarioCommandNode command)
    {
        command = null!;
        if (_scriptTree.SelectedNode?.Tag is LegacyScenarioItemData { Command: { } itemCommand })
        {
            command = itemCommand;
            return true;
        }

        return TryGetScriptTreeRow(_scriptTree.SelectedNode, out var row) &&
               row.NodeType == "Command候选" &&
               TryGetLegacyScriptCommand(row, out command);
    }

    private bool TryGetSelectedLegacyScriptCommand(LegacyScriptEditorScope scope, out LegacyScenarioCommandNode command)
    {
        command = null!;
        return scope switch
        {
            LegacyScriptEditorScope.Script => TryGetSelectedLegacyScriptCommand(out command),
            LegacyScriptEditorScope.Battlefield => TryGetSelectedBattlefieldLegacyScriptCommand(out command),
            LegacyScriptEditorScope.RScene => TryGetSelectedRSceneLegacyCommand(out command),
            _ => false
        };
    }

    private LegacyScenarioDocument? GetCurrentLegacyScriptDocument(LegacyScriptEditorScope scope)
        => scope switch
        {
            LegacyScriptEditorScope.Script => _currentLegacyScriptDocument,
            LegacyScriptEditorScope.Battlefield => _currentBattlefieldLegacyScriptDocument,
            LegacyScriptEditorScope.RScene => _currentRSceneLegacyScriptDocument,
            _ => null
        };

    private TreeView GetLegacyScriptTree(LegacyScriptEditorScope scope)
        => scope switch
        {
            LegacyScriptEditorScope.Script => _scriptTree,
            LegacyScriptEditorScope.Battlefield => _battlefieldScriptTree,
            LegacyScriptEditorScope.RScene => _rSceneScriptTree,
            _ => _scriptTree
        };

    private ContextMenuStrip GetLegacyScriptContextMenu(LegacyScriptEditorScope scope)
        => scope switch
        {
            LegacyScriptEditorScope.Script => _legacyScriptTreeContextMenu,
            LegacyScriptEditorScope.Battlefield => _battlefieldScriptTreeContextMenu,
            LegacyScriptEditorScope.RScene => _rSceneScriptTreeContextMenu,
            _ => _legacyScriptTreeContextMenu
        };

    private string GetLegacyScriptScenarioName(LegacyScriptEditorScope scope)
        => scope switch
        {
            LegacyScriptEditorScope.Script => _currentScriptScenario?.FileName ?? string.Empty,
            LegacyScriptEditorScope.Battlefield => _currentBattlefieldDocument?.Scenario.FileName ?? string.Empty,
            LegacyScriptEditorScope.RScene => _currentRSceneScenario?.FileName ?? string.Empty,
            _ => string.Empty
        };

    private bool IsLegacyScriptClipboardFromSameScenario(LegacyScriptEditorScope scope)
    {
        var targetScenarioName = GetLegacyScriptScenarioName(scope);
        if (string.IsNullOrWhiteSpace(_legacyScriptCommandClipboardScenarioName) ||
            string.IsNullOrWhiteSpace(targetScenarioName) ||
            !string.Equals(_legacyScriptCommandClipboardScenarioName, targetScenarioName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var sourceGameRoot = NormalizeLegacyScriptGameRoot(_legacyScriptCommandClipboardGameRoot);
        var targetGameRoot = NormalizeLegacyScriptGameRoot(_project?.GameRoot ?? string.Empty);
        return sourceGameRoot.Length > 0 &&
               targetGameRoot.Length > 0 &&
               string.Equals(sourceGameRoot, targetGameRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLegacyScriptGameRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim()
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private string GetLegacyScriptScopeStatusPrefix(LegacyScriptEditorScope scope)
        => scope switch
        {
            LegacyScriptEditorScope.Script => "剧本编辑",
            LegacyScriptEditorScope.Battlefield => "战场编辑 S 剧本",
            LegacyScriptEditorScope.RScene => "场景编辑 R 剧本",
            _ => "剧本"
        };

    private void SetLegacyScriptDetailText(LegacyScriptEditorScope scope, string text)
    {
        switch (scope)
        {
            case LegacyScriptEditorScope.Script:
                _scriptDetailBox.Text = text;
                break;
            case LegacyScriptEditorScope.Battlefield:
                _battlefieldScriptDetailBox.Text = text;
                break;
            case LegacyScriptEditorScope.RScene:
                _rSceneScriptDetailBox.Text = text;
                break;
        }
    }

    private void AppendLegacyScriptDetailText(LegacyScriptEditorScope scope, string text)
    {
        switch (scope)
        {
            case LegacyScriptEditorScope.Script:
                _scriptDetailBox.Text += text;
                break;
            case LegacyScriptEditorScope.Battlefield:
                _battlefieldScriptDetailBox.Text += text;
                break;
            case LegacyScriptEditorScope.RScene:
                _rSceneScriptDetailBox.Text += text;
                break;
        }
    }

    private ScenarioStructureRow? GetLegacyScriptRowForCommand(LegacyScriptEditorScope scope, LegacyScenarioCommandNode command)
        => scope switch
        {
            LegacyScriptEditorScope.Script => _legacyScriptRowByKey.TryGetValue(BuildLegacyCommandKey(command), out var scriptRow)
                ? scriptRow
                : _currentScriptStructure?.Rows.FirstOrDefault(row =>
                    row.NodeType == "Command候选" &&
                    row.SceneIndex == command.SceneIndex &&
                    row.SectionIndex == command.SectionIndex &&
                    row.CommandIndex == command.CommandIndex &&
                    row.CommandId == command.CommandId),
            LegacyScriptEditorScope.Battlefield => _currentBattlefieldScriptStructure?.Rows.FirstOrDefault(row =>
                row.NodeType == "Command候选" &&
                row.SceneIndex == command.SceneIndex &&
                row.SectionIndex == command.SectionIndex &&
                row.CommandIndex == command.CommandIndex &&
                row.CommandId == command.CommandId),
            LegacyScriptEditorScope.RScene => FindRSceneScriptRowByCommandReference(command) ??
                                              _currentRSceneScriptStructure?.Rows.FirstOrDefault(row =>
                                                  row.NodeType == "Command候选" &&
                                                  row.SceneIndex == command.SceneIndex &&
                                                  row.SectionIndex == command.SectionIndex &&
                                                  row.CommandIndex == command.CommandIndex &&
                                                  row.CommandId == command.CommandId),
            _ => null
        };

    private ScenarioStructureProbeResult? GetLegacyScriptStructure(LegacyScriptEditorScope scope)
        => scope switch
        {
            LegacyScriptEditorScope.Script => _currentScriptStructure,
            LegacyScriptEditorScope.Battlefield => _currentBattlefieldScriptStructure,
            LegacyScriptEditorScope.RScene => _currentRSceneScriptStructure,
            _ => null
        };

    private IReadOnlyList<ScenarioTextEntry> GetLegacyScriptTextEntries(LegacyScriptEditorScope scope)
        => scope switch
        {
            LegacyScriptEditorScope.Script => _currentScriptTextEntries,
            LegacyScriptEditorScope.Battlefield => _currentBattlefieldScriptTextEntries,
            LegacyScriptEditorScope.RScene => _currentRSceneScriptTextEntries,
            _ => Array.Empty<ScenarioTextEntry>()
        };

    private void SetLegacyScriptEditorBinding(LegacyScriptEditorScope scope, bool value)
    {
        switch (scope)
        {
            case LegacyScriptEditorScope.Script:
                _bindingScriptDocument = value;
                break;
            case LegacyScriptEditorScope.Battlefield:
                _bindingBattlefieldScriptEditor = value;
                break;
            case LegacyScriptEditorScope.RScene:
                _bindingRSceneScriptTree = value;
                break;
        }
    }

    private void SuspendLegacyScriptEditorGrids(LegacyScriptEditorScope scope)
    {
        switch (scope)
        {
            case LegacyScriptEditorScope.Script:
                _scriptCommandGrid.SuspendLayout();
                _scriptParameterGrid.SuspendLayout();
                _scriptTextGrid.SuspendLayout();
                _scriptSearchResultGrid.SuspendLayout();
                break;
            case LegacyScriptEditorScope.Battlefield:
                _battlefieldScriptParameterGrid.SuspendLayout();
                break;
            case LegacyScriptEditorScope.RScene:
                _rSceneCommandGrid.SuspendLayout();
                break;
        }
    }

    private void ResumeLegacyScriptEditorGrids(LegacyScriptEditorScope scope)
    {
        switch (scope)
        {
            case LegacyScriptEditorScope.Script:
                _scriptSearchResultGrid.ResumeLayout();
                _scriptTextGrid.ResumeLayout();
                _scriptParameterGrid.ResumeLayout();
                _scriptCommandGrid.ResumeLayout();
                break;
            case LegacyScriptEditorScope.Battlefield:
                _battlefieldScriptParameterGrid.ResumeLayout();
                break;
            case LegacyScriptEditorScope.RScene:
                _rSceneCommandGrid.ResumeLayout();
                break;
        }
    }

    private void SetLegacyScriptOverviewDetail(LegacyScriptEditorScope scope)
    {
        var structure = GetLegacyScriptStructure(scope);
        if (structure == null)
        {
            return;
        }

        switch (scope)
        {
            case LegacyScriptEditorScope.Script:
                _scriptDetailBox.Text = BuildScriptOverview(structure, GetLegacyScriptTextEntries(scope));
                break;
            case LegacyScriptEditorScope.Battlefield:
                _battlefieldScriptDetailBox.Text =
                    $"S剧本：{structure.FileName}\r\n" +
                    $"Scene：{structure.SceneCount}  Section：{structure.SectionCount}  Command：{structure.CommandCandidateCount}  文本：{GetLegacyScriptTextEntries(scope).Count}";
                break;
            case LegacyScriptEditorScope.RScene:
                _rSceneScriptDetailBox.Text = BuildRSceneInfoText();
                break;
        }
    }

    private bool TryGetSelectedLegacyItemData(LegacyScriptEditorScope scope, out LegacyScenarioItemData itemData)
    {
        itemData = null!;
        return scope switch
        {
            LegacyScriptEditorScope.Script => TryGetSelectedLegacyItemData(out itemData),
            LegacyScriptEditorScope.Battlefield => TryGetSelectedBattlefieldLegacyItemData(out itemData),
            LegacyScriptEditorScope.RScene => TryGetSelectedRSceneLegacyItemData(out itemData),
            _ => false
        };
    }

    private bool TrySelectLegacyScriptCommand(LegacyScriptEditorScope scope, LegacyScenarioCommandNode command, bool ensureVisible = true)
        => scope switch
        {
            LegacyScriptEditorScope.Script => TrySelectLegacyScriptCommand(command, ensureVisible),
            LegacyScriptEditorScope.Battlefield => TrySelectBattlefieldLegacyScriptCommand(command, ensureVisible),
            LegacyScriptEditorScope.RScene => TrySelectRSceneLegacyScriptCommand(command, ensureVisible),
            _ => false
        };

    private void ShowSelectedLegacyScriptTreeNode(LegacyScriptEditorScope scope)
    {
        switch (scope)
        {
            case LegacyScriptEditorScope.Script:
                ShowSelectedScriptTreeNode();
                break;
            case LegacyScriptEditorScope.Battlefield:
                ShowSelectedBattlefieldScriptNode();
                break;
            case LegacyScriptEditorScope.RScene:
                ShowSelectedRSceneScriptNode();
                break;
        }
    }

    private void ApplyLegacyScriptStructureEdit(
        LegacyScriptEditorScope scope,
        Action edit,
        LegacyScenarioCommandNode? preferredSelection,
        string statusText,
        LegacyScriptStructureEditOptions? options = null)
    {
        var document = GetCurrentLegacyScriptDocument(scope);
        if (document == null) return;

        var viewport = CaptureLegacyScriptViewport(scope);
        PushLegacyScenarioUndoSnapshot(scope, document);
        var jumpTargets = CaptureLegacyJumpTargets(document);
        edit();
        ReindexLegacyScriptDocument(document);
        RestoreLegacyJumpTargets(document, jumpTargets);
        var refreshedInPlace = options is { AllowSectionRefresh: true, AffectedSection: not null } &&
                               TryRefreshLegacyScriptSectionInPlace(scope, options.AffectedSection, preferredSelection, viewport);
        if (refreshedInPlace)
        {
            MarkLegacyScriptStructureDirty(scope);
        }
        else
        {
            RefreshLegacyScriptView(scope, preferredSelection, viewport);
        }
        SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)}：{statusText}");
    }

    private sealed record LegacyScenarioHistorySnapshot(
        LegacyScenarioDocument Document,
        int? SelectedSceneIndex,
        int? SelectedSectionIndex,
        int? SelectedCommandIndex,
        int? SelectedCommandId);

    private Stack<LegacyScenarioHistorySnapshot> GetLegacyScenarioUndoStack(LegacyScriptEditorScope scope)
        => scope switch
        {
            LegacyScriptEditorScope.Script => _scriptUndoStack,
            LegacyScriptEditorScope.Battlefield => _battlefieldScriptUndoStack,
            LegacyScriptEditorScope.RScene => _rSceneScriptUndoStack,
            _ => _scriptUndoStack
        };

    private Stack<LegacyScenarioHistorySnapshot> GetLegacyScenarioRedoStack(LegacyScriptEditorScope scope)
        => scope switch
        {
            LegacyScriptEditorScope.Script => _scriptRedoStack,
            LegacyScriptEditorScope.Battlefield => _battlefieldScriptRedoStack,
            LegacyScriptEditorScope.RScene => _rSceneScriptRedoStack,
            _ => _scriptRedoStack
        };

    private void ClearLegacyScenarioHistory(LegacyScriptEditorScope scope)
    {
        GetLegacyScenarioUndoStack(scope).Clear();
        GetLegacyScenarioRedoStack(scope).Clear();
    }

    private void PushLegacyScenarioUndoSnapshot(LegacyScriptEditorScope scope, LegacyScenarioDocument document)
    {
        GetLegacyScenarioUndoStack(scope).Push(CaptureLegacyScenarioHistorySnapshot(scope, document));
        GetLegacyScenarioRedoStack(scope).Clear();
    }

    private void PushLegacyScenarioUndoSnapshot(LegacyScriptEditorScope scope, LegacyScenarioHistorySnapshot snapshot)
    {
        GetLegacyScenarioUndoStack(scope).Push(snapshot);
        GetLegacyScenarioRedoStack(scope).Clear();
    }

    private LegacyScenarioHistorySnapshot CaptureLegacyScenarioHistorySnapshot(
        LegacyScriptEditorScope scope,
        LegacyScenarioDocument document)
    {
        var selection = TryGetSelectedLegacyScriptCommand(scope, out var selected)
            ? selected
            : null;
        return new LegacyScenarioHistorySnapshot(
            CloneLegacyScenarioDocument(document),
            selection?.SceneIndex,
            selection?.SectionIndex,
            selection?.CommandIndex,
            selection?.CommandId);
    }

    private LegacyScenarioDocument CloneLegacyScenarioDocument(LegacyScenarioDocument source)
    {
        var clone = new LegacyScenarioDocument
        {
            FilePath = source.FilePath,
            SceneOffsets = source.SceneOffsets.ToList()
        };

        foreach (var scene in source.Scenes)
        {
            clone.Scenes.Add(CloneLegacyScenarioScene(scene));
        }

        ReindexLegacyScriptDocument(clone);
        RestoreLegacyJumpTargets(clone, CaptureLegacyJumpTargets(source));
        return clone;
    }

    private LegacyScenarioScene CloneLegacyScenarioScene(LegacyScenarioScene source)
    {
        var clone = new LegacyScenarioScene
        {
            SceneIndex = source.SceneIndex,
            FileOffset = source.FileOffset
        };

        foreach (var section in source.Sections)
        {
            clone.Sections.Add(CloneLegacyScenarioSection(section));
        }

        return clone;
    }

    private LegacyScenarioSection CloneLegacyScenarioSection(LegacyScenarioSection source)
    {
        var clone = new LegacyScenarioSection
        {
            SceneIndex = source.SceneIndex,
            SectionIndex = source.SectionIndex,
            FileOffset = source.FileOffset,
            LengthPrefixOffset = source.LengthPrefixOffset,
            DeclaredLength = source.DeclaredLength
        };

        foreach (var command in source.Commands)
        {
            clone.Commands.Add(CloneLegacyScenarioCommand(command));
        }

        return clone;
    }

    private LegacyScenarioCommandBlock CloneLegacyScenarioCommandBlock(LegacyScenarioCommandBlock source)
    {
        var clone = new LegacyScenarioCommandBlock
        {
            Kind = source.Kind,
            LengthPrefixOffset = source.LengthPrefixOffset,
            FileOffset = source.FileOffset,
            DeclaredLength = source.DeclaredLength
        };

        foreach (var command in source.Commands)
        {
            clone.Commands.Add(CloneLegacyScenarioCommand(command));
        }

        return clone;
    }

    private LegacyScenarioCommandNode CloneLegacyScenarioCommand(LegacyScenarioCommandNode source)
    {
        var clone = new LegacyScenarioCommandNode
        {
            SceneIndex = source.SceneIndex,
            SectionIndex = source.SectionIndex,
            CommandIndex = source.CommandIndex,
            CommandOrdinal = source.CommandOrdinal,
            CommandId = source.CommandId,
            CommandName = source.CommandName,
            FileOffset = source.FileOffset,
            ConsumedBytes = source.ConsumedBytes,
            StartsBodyBlock = source.StartsBodyBlock,
            IsSubEventMarker = source.IsSubEventMarker,
            OpensSubEventBlock = source.OpensSubEventBlock,
            EndsSubEventBlock = source.EndsSubEventBlock,
            JumpTargetOrdinal = source.JumpTargetOrdinal,
            JumpTargetCommandIndex = source.JumpTargetCommandIndex,
            OriginalJumpDisplacement = source.OriginalJumpDisplacement
        };

        foreach (var parameter in source.Parameters)
        {
            clone.Parameters.Add(CloneLegacyScenarioParameter(parameter));
        }

        if (source.ChildBlock != null)
        {
            clone.ChildBlock = CloneLegacyScenarioCommandBlock(source.ChildBlock);
        }

        return clone;
    }

    private static LegacyScenarioCommandParameter CloneLegacyScenarioParameter(LegacyScenarioCommandParameter source)
    {
        var clone = new LegacyScenarioCommandParameter
        {
            Index = source.Index,
            LayoutCode = source.LayoutCode,
            Tag = source.Tag,
            FileOffset = source.FileOffset,
            Kind = source.Kind,
            IntValue = source.IntValue,
            Text = source.Text,
            ByteLength = source.ByteLength
        };
        clone.Values.AddRange(source.Values);
        return clone;
    }

    private bool CanUndoLegacyScenarioEdit(LegacyScriptEditorScope scope)
        => GetLegacyScenarioUndoStack(scope).Count > 0;

    private bool CanRedoLegacyScenarioEdit(LegacyScriptEditorScope scope)
        => GetLegacyScenarioRedoStack(scope).Count > 0;

    private bool UndoCurrentScenarioEdit()
        => UndoLegacyScenarioEdit(GetCurrentLegacyScenarioEditorScope());

    private bool RedoCurrentScenarioEdit()
        => RedoLegacyScenarioEdit(GetCurrentLegacyScenarioEditorScope());

    private LegacyScriptEditorScope GetCurrentLegacyScenarioEditorScope()
        => _mainTabs.SelectedTab?.Text switch
        {
            "战场编辑" => LegacyScriptEditorScope.Battlefield,
            "场景编辑" => LegacyScriptEditorScope.RScene,
            _ => LegacyScriptEditorScope.Script
        };

    private bool UndoLegacyScenarioEdit(LegacyScriptEditorScope scope)
    {
        var document = GetCurrentLegacyScriptDocument(scope);
        if (document == null || !CanUndoLegacyScenarioEdit(scope))
        {
            SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)}：没有可撤销的剧本编辑。");
            return false;
        }

        var snapshot = GetLegacyScenarioUndoStack(scope).Pop();
        GetLegacyScenarioRedoStack(scope).Push(CaptureLegacyScenarioHistorySnapshot(scope, document));
        RestoreLegacyScenarioSnapshot(scope, snapshot, "已撤销上一步剧本编辑。");
        return true;
    }

    private bool RedoLegacyScenarioEdit(LegacyScriptEditorScope scope)
    {
        var document = GetCurrentLegacyScriptDocument(scope);
        if (document == null || !CanRedoLegacyScenarioEdit(scope))
        {
            SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)}：没有可前进的剧本编辑。");
            return false;
        }

        var snapshot = GetLegacyScenarioRedoStack(scope).Pop();
        GetLegacyScenarioUndoStack(scope).Push(CaptureLegacyScenarioHistorySnapshot(scope, document));
        RestoreLegacyScenarioSnapshot(scope, snapshot, "已前进到下一步剧本编辑。");
        return true;
    }

    private void RestoreLegacyScenarioSnapshot(
        LegacyScriptEditorScope scope,
        LegacyScenarioHistorySnapshot snapshot,
        string statusText)
    {
        var restored = CloneLegacyScenarioDocument(snapshot.Document);
        SetCurrentLegacyScriptDocument(scope, restored);
        var preferredSelection = FindLegacyScenarioHistorySelection(restored, snapshot);
        RefreshLegacyScriptView(scope, preferredSelection);
        SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)}：{statusText}");
    }

    private void SetCurrentLegacyScriptDocument(LegacyScriptEditorScope scope, LegacyScenarioDocument document)
    {
        switch (scope)
        {
            case LegacyScriptEditorScope.Script:
                _currentLegacyScriptDocument = document;
                break;
            case LegacyScriptEditorScope.Battlefield:
                _currentBattlefieldLegacyScriptDocument = document;
                break;
            case LegacyScriptEditorScope.RScene:
                _currentRSceneLegacyScriptDocument = document;
                break;
        }
    }

    private static LegacyScenarioCommandNode? FindLegacyScenarioHistorySelection(
        LegacyScenarioDocument document,
        LegacyScenarioHistorySnapshot snapshot)
    {
        if (!snapshot.SelectedSceneIndex.HasValue ||
            !snapshot.SelectedSectionIndex.HasValue ||
            !snapshot.SelectedCommandIndex.HasValue)
        {
            return null;
        }

        return document.EnumerateCommands().FirstOrDefault(command =>
            command.SceneIndex == snapshot.SelectedSceneIndex.Value &&
            command.SectionIndex == snapshot.SelectedSectionIndex.Value &&
            command.CommandIndex == snapshot.SelectedCommandIndex.Value &&
            (!snapshot.SelectedCommandId.HasValue || command.CommandId == snapshot.SelectedCommandId.Value));
    }

    private void RefreshLegacyScriptView(
        LegacyScriptEditorScope scope,
        LegacyScenarioCommandNode? preferredSelection,
        LegacyScriptViewportSnapshot? viewportSnapshot = null,
        bool preserveViewport = true)
    {
        switch (scope)
        {
            case LegacyScriptEditorScope.Script:
                RefreshLegacyScriptView(preferredSelection, viewportSnapshot: viewportSnapshot, preserveViewport: preserveViewport);
                _saveScriptStructureButton.Enabled = true;
                InvalidateScriptVariableProjectCache();
                break;
            case LegacyScriptEditorScope.Battlefield:
                RefreshBattlefieldLegacyScriptView(preferredSelection, viewportSnapshot: viewportSnapshot, preserveViewport: preserveViewport);
                RefreshBattlefieldDocumentFromLegacyScript();
                _saveBattlefieldScriptStructureButton.Enabled = true;
                break;
            case LegacyScriptEditorScope.RScene:
                RefreshRSceneLegacyScriptView(preferredSelection, viewportSnapshot, preserveViewport);
                _saveRSceneScriptStructureButton.Enabled = true;
                break;
        }
    }

    private void MarkLegacyScriptStructureDirty(LegacyScriptEditorScope scope)
    {
        SetLegacyStructureDirtyFlag(scope, true);
        switch (scope)
        {
            case LegacyScriptEditorScope.Script:
                _saveScriptStructureButton.Enabled = true;
                InvalidateScriptVariableProjectCache();
                UpdateScriptStructureEditButtons();
                _scriptVariableUsageDialog?.RefreshCurrentScenario();
                break;
            case LegacyScriptEditorScope.Battlefield:
                _saveBattlefieldScriptStructureButton.Enabled = true;
                break;
            case LegacyScriptEditorScope.RScene:
                _saveRSceneScriptStructureButton.Enabled = true;
                break;
        }
    }

    private void MarkLegacyScriptEditorSavedInPlace(LegacyScriptEditorScope scope, LegacyScenarioWriteResult result)
    {
        ClearLegacyScenarioHistory(scope);
        SetLegacyStructureDirtyFlag(scope, false);
        var detailText =
            $"\r\n\r\n完整保存完成：变化 {result.ChangedBytes} 字节。\r\n校验：{result.ValidationSummary}\r\n备份：{result.BackupPath}\r\n报告：{result.ReportJsonPath}";
        switch (scope)
        {
            case LegacyScriptEditorScope.Script:
                MarkLegacyScriptSavedInPlace(result);
                break;
            case LegacyScriptEditorScope.Battlefield:
                _saveBattlefieldScriptStructureButton.Enabled = false;
                AppendLegacyScriptDetailText(scope, detailText);
                RefreshBattlefieldDocumentFromLegacyScript();
                break;
            case LegacyScriptEditorScope.RScene:
                _saveRSceneScriptStructureButton.Enabled = false;
                AppendLegacyScriptDetailText(scope, detailText);
                break;
        }
    }

    private bool TryRefreshLegacyScriptSectionInPlace(
        LegacyScriptEditorScope scope,
        LegacyScenarioSection affectedSection,
        LegacyScenarioCommandNode? preferredSelection,
        LegacyScriptViewportSnapshot? viewport)
    {
        var document = GetCurrentLegacyScriptDocument(scope);
        if (document == null ||
            !document.Scenes.SelectMany(scene => scene.Sections).Contains(affectedSection))
        {
            return false;
        }

        var tree = GetLegacyScriptTree(scope);
        var oldSectionNode = FindLegacyScriptSectionTreeNode(tree, affectedSection);
        if (oldSectionNode?.Parent == null)
        {
            return false;
        }

        SetLegacyScriptEditorBinding(scope, true);
        tree.SuspendLayout();
        SuspendLegacyScriptEditorGrids(scope);
        try
        {
            if (!RefreshLegacyEditorMetadataOnly(scope))
            {
                return false;
            }

            var structure = GetLegacyScriptStructure(scope);
            if (structure == null)
            {
                return false;
            }

            var rowByKey = structure.Rows
                .Where(row => row.NodeType == "Command候选")
                .ToDictionaryFirstByKey(BuildLegacyCommandKey, row => row, StringComparer.OrdinalIgnoreCase);
            var sectionRow = structure.Rows.FirstOrDefault(row =>
                row.NodeType == "Section候选" &&
                row.SceneIndex == affectedSection.SceneIndex &&
                row.SectionIndex == affectedSection.SectionIndex);

            if (affectedSection.EnumerateCommands().Count() > ScriptLegacyTreeCommandNodeLimitPerSection)
            {
                ReplaceLegacyScriptSectionNode(scope, oldSectionNode, affectedSection, sectionRow, rowByKey);
            }
            else
            {
                tree.BeginUpdate();
                try
                {
                    UpdateLegacyScriptSectionTreeNode(oldSectionNode, affectedSection, sectionRow);
                    ReconcileLegacyScriptCommandTreeNodes(scope, oldSectionNode.Nodes, affectedSection.Commands, rowByKey, depth: 0);
                }
                finally
                {
                    tree.EndUpdate();
                }
            }
        }
        finally
        {
            ResumeLegacyScriptEditorGrids(scope);
            tree.ResumeLayout();
            SetLegacyScriptEditorBinding(scope, false);
        }

        var selectionRestored = false;
        if (preferredSelection != null && TrySelectLegacyScriptCommand(scope, preferredSelection, ensureVisible: false))
        {
            ShowSelectedLegacyScriptTreeNode(scope);
            selectionRestored = true;
        }
        else if (TryRestoreLegacyScriptSelectedNode(viewport))
        {
            ShowSelectedLegacyScriptTreeNode(scope);
            selectionRestored = true;
        }
        else
        {
            var replacement = FindLegacyScriptSectionTreeNode(tree, affectedSection);
            if (replacement != null)
            {
                tree.SelectedNode = replacement;
                ShowSelectedLegacyScriptTreeNode(scope);
                selectionRestored = true;
            }
        }

        if (!selectionRestored)
        {
            RestoreLegacyScriptViewport(viewport);
        }
        if (!selectionRestored)
        {
            SetLegacyScriptOverviewDetail(scope);
        }

        return true;
    }

    private void ReplaceLegacyScriptSectionNode(
        LegacyScriptEditorScope scope,
        TreeNode oldSectionNode,
        LegacyScenarioSection section,
        ScenarioStructureRow? sectionRow,
        IReadOnlyDictionary<string, ScenarioStructureRow> rowByKey)
    {
        var replacement = CreateLegacyScriptSectionTreeNode(scope, section, sectionRow, rowByKey);
        if (oldSectionNode.IsExpanded)
        {
            replacement.Expand();
        }

        var parent = oldSectionNode.Parent;
        if (parent == null)
        {
            return;
        }

        var index = oldSectionNode.Index;
        parent.Nodes.RemoveAt(index);
        parent.Nodes.Insert(index, replacement);
    }

    private static void UpdateLegacyScriptSectionTreeNode(
        TreeNode sectionNode,
        LegacyScenarioSection section,
        ScenarioStructureRow? sectionRow)
    {
        if (sectionNode.Tag is LegacyScenarioItemData itemData && ReferenceEquals(itemData.Section, section))
        {
            itemData.Ord = section.SectionIndex - 1;
            itemData.UiRow = sectionRow;
        }
        else
        {
            sectionNode.Tag = new LegacyScenarioItemData { Id = -2, Ord = section.SectionIndex - 1, Section = section, UiRow = sectionRow };
        }

        sectionNode.Text = BuildLegacySectionNodeText(section, section.EnumerateCommands().ToList());
        sectionNode.ToolTipText = sectionRow?.Annotation ?? string.Empty;
    }

    private void ReconcileLegacyScriptCommandTreeNodes(
        LegacyScriptEditorScope scope,
        TreeNodeCollection nodes,
        IReadOnlyList<LegacyScenarioCommandNode> expectedCommands,
        IReadOnlyDictionary<string, ScenarioStructureRow> rowByKey,
        int depth)
    {
        var expectedSet = expectedCommands.ToHashSet();
        var activeNodes = new Dictionary<LegacyScenarioCommandNode, TreeNode>();
        foreach (TreeNode node in nodes)
        {
            if (TryGetLegacyScriptItemDataCommand(node, out var command) &&
                expectedSet.Contains(command) &&
                !activeNodes.ContainsKey(command))
            {
                activeNodes[command] = node;
            }
        }

        for (var index = 0; index < expectedCommands.Count; index++)
        {
            var command = expectedCommands[index];
            if (!rowByKey.TryGetValue(BuildLegacyCommandKey(command), out var row))
            {
                continue;
            }

            var node = activeNodes.TryGetValue(command, out var existing)
                ? existing
                : CreateLegacyScriptCommandTreeNode(
                      scope,
                      command,
                      row,
                      rowByKey,
                      depth,
                      new HashSet<LegacyScenarioCommandNode>(),
                      new HashSet<LegacyScenarioCommandBlock>());
            MoveLegacyScriptTreeNodeToIndex(nodes, node, index);
            UpdateLegacyScriptCommandTreeNode(scope, node, command, row, rowByKey, depth);
        }

        for (var index = nodes.Count - 1; index >= 0; index--)
        {
            if (!TryGetLegacyScriptItemDataCommand(nodes[index], out var command) ||
                !expectedSet.Contains(command))
            {
                nodes.RemoveAt(index);
            }
        }
    }

    private static void MoveLegacyScriptTreeNodeToIndex(TreeNodeCollection nodes, TreeNode node, int targetIndex)
    {
        targetIndex = Math.Max(0, Math.Min(targetIndex, nodes.Count));
        if (node.TreeView == null)
        {
            nodes.Insert(targetIndex, node);
            return;
        }

        if (ReferenceEquals(node.Parent?.Nodes, nodes) && node.Index == targetIndex)
        {
            return;
        }

        nodes.Remove(node);
        if (targetIndex > nodes.Count)
        {
            targetIndex = nodes.Count;
        }

        nodes.Insert(targetIndex, node);
    }

    private void UpdateLegacyScriptCommandTreeNode(
        LegacyScriptEditorScope scope,
        TreeNode node,
        LegacyScenarioCommandNode command,
        ScenarioStructureRow row,
        IReadOnlyDictionary<string, ScenarioStructureRow> rowByKey,
        int depth)
    {
        var itemData = GetLegacyScriptItemData(command, row);
        itemData.Ord = command.CommandOrdinal;
        itemData.UiRow = row;
        CopyLegacyCommandToItemData(command, itemData);
        SetLegacyEditorItemDataIndexes(scope, command, row, itemData);
        node.Tag = itemData;
        node.Text = BuildLegacyScriptCommandSummary(row, command, includeIdentity: false, maxVisibleValues: 6);
        node.ToolTipText = BuildLegacyScriptCommandTreeToolTip(row, command);
        node.ForeColor = GetLegacyScriptCommandColor(command);
        if (scope == LegacyScriptEditorScope.Battlefield)
        {
            ApplyBattlefieldScriptPreviewToNode(node, row);
        }

        if (depth >= ScriptLegacyTreeMaxNestedDepth)
        {
            node.Nodes.Clear();
            if (command.ChildBlock != null)
            {
                node.Nodes.Add(CreateLegacyScriptDepthFoldNode(command.ChildBlock));
            }
            return;
        }

        if (command.ChildBlock == null)
        {
            node.Nodes.Clear();
            return;
        }

        ReconcileLegacyScriptCommandTreeNodes(scope, node.Nodes, command.ChildBlock.Commands, rowByKey, depth + 1);
    }

    private static bool TryGetLegacyScriptItemDataCommand(TreeNode? node, out LegacyScenarioCommandNode command)
    {
        if (node?.Tag is LegacyScenarioItemData { Command: { } itemCommand })
        {
            command = itemCommand;
            return true;
        }

        command = null!;
        return false;
    }

    private static TreeNode? FindLegacyScriptSectionTreeNode(TreeView tree, LegacyScenarioSection section)
    {
        foreach (TreeNode root in tree.Nodes)
        {
            var found = FindLegacyScriptSectionTreeNode(root, section);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static TreeNode? FindLegacyScriptSectionTreeNode(TreeNode node, LegacyScenarioSection section)
    {
        if (node.Tag is LegacyScenarioItemData { Section: { } itemSection } &&
            ReferenceEquals(itemSection, section))
        {
            return node;
        }

        foreach (TreeNode child in node.Nodes)
        {
            var found = FindLegacyScriptSectionTreeNode(child, section);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private IReadOnlyList<LegacyScenarioCommandNode> GetCheckedLegacyScriptCommands()
        => GetCheckedLegacyScriptCommands(LegacyScriptEditorScope.Script);

    private IReadOnlyList<LegacyScenarioCommandNode> GetCheckedLegacyScriptCommands(LegacyScriptEditorScope scope)
    {
        var tree = GetLegacyScriptTree(scope);
        if (tree.Nodes.Count == 0)
        {
            return Array.Empty<LegacyScenarioCommandNode>();
        }

        var result = new List<LegacyScenarioCommandNode>();
        var seen = new HashSet<LegacyScenarioCommandNode>();
        CollectCheckedLegacyScriptCommands(
            scope,
            tree.Nodes,
            result,
            seen,
            hasCheckedCommandAncestor: false,
            hasCheckedStructureAncestor: false);
        return result;
    }

    private IReadOnlyList<LegacyScenarioScene> GetCheckedLegacyScriptScenes(LegacyScriptEditorScope scope)
    {
        var tree = GetLegacyScriptTree(scope);
        if (tree.Nodes.Count == 0)
        {
            return Array.Empty<LegacyScenarioScene>();
        }

        var result = new List<LegacyScenarioScene>();
        var seen = new HashSet<LegacyScenarioScene>();
        CollectCheckedLegacyScriptScenes(scope, tree.Nodes, result, seen);
        return result;
    }

    private void CollectCheckedLegacyScriptScenes(
        LegacyScriptEditorScope scope,
        TreeNodeCollection nodes,
        List<LegacyScenarioScene> result,
        HashSet<LegacyScenarioScene> seen)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.Checked &&
                TryGetLegacyScriptSceneFromTreeNode(scope, node, out var scene) &&
                seen.Add(scene))
            {
                result.Add(scene);
                continue;
            }

            if (node.Nodes.Count > 0)
            {
                CollectCheckedLegacyScriptScenes(scope, node.Nodes, result, seen);
            }
        }
    }

    private IReadOnlyList<LegacyScenarioSection> GetCheckedLegacyScriptSections(LegacyScriptEditorScope scope)
    {
        var tree = GetLegacyScriptTree(scope);
        if (tree.Nodes.Count == 0)
        {
            return Array.Empty<LegacyScenarioSection>();
        }

        var result = new List<LegacyScenarioSection>();
        var seen = new HashSet<LegacyScenarioSection>();
        CollectCheckedLegacyScriptSections(scope, tree.Nodes, result, seen, hasCheckedSceneAncestor: false);
        return result;
    }

    private void CollectCheckedLegacyScriptSections(
        LegacyScriptEditorScope scope,
        TreeNodeCollection nodes,
        List<LegacyScenarioSection> result,
        HashSet<LegacyScenarioSection> seen,
        bool hasCheckedSceneAncestor)
    {
        foreach (TreeNode node in nodes)
        {
            var nodeIsCheckedScene = node.Checked && TryGetLegacyScriptSceneFromTreeNode(scope, node, out _);
            var descendantHasCheckedSceneAncestor = hasCheckedSceneAncestor || nodeIsCheckedScene;

            if (node.Checked &&
                !hasCheckedSceneAncestor &&
                TryGetLegacyScriptSectionFromTreeNode(scope, node, out var section) &&
                seen.Add(section))
            {
                result.Add(section);
                continue;
            }

            if (node.Nodes.Count > 0 && !nodeIsCheckedScene)
            {
                CollectCheckedLegacyScriptSections(scope, node.Nodes, result, seen, descendantHasCheckedSceneAncestor);
            }
        }
    }

    private void CollectCheckedLegacyScriptCommands(
        LegacyScriptEditorScope scope,
        TreeNodeCollection nodes,
        List<LegacyScenarioCommandNode> result,
        HashSet<LegacyScenarioCommandNode> seen,
        bool hasCheckedCommandAncestor,
        bool hasCheckedStructureAncestor)
    {
        foreach (TreeNode node in nodes)
        {
            var nodeIsCheckedStructure = node.Checked &&
                (TryGetLegacyScriptSceneFromTreeNode(scope, node, out _) ||
                 TryGetLegacyScriptSectionFromTreeNode(scope, node, out _));
            LegacyScenarioCommandNode? command = null;
            var nodeIsCheckedCommand = node.Checked && TryGetLegacyScriptCommandFromTreeNode(scope, node, out command);
            var descendantHasCheckedCommandAncestor = hasCheckedCommandAncestor || nodeIsCheckedCommand;
            var descendantHasCheckedStructureAncestor = hasCheckedStructureAncestor || nodeIsCheckedStructure;
            if (nodeIsCheckedCommand &&
                command != null &&
                !hasCheckedCommandAncestor &&
                !hasCheckedStructureAncestor &&
                seen.Add(command))
            {
                result.Add(command);
            }

            if (node.Nodes.Count > 0)
            {
                CollectCheckedLegacyScriptCommands(
                    scope,
                    node.Nodes,
                    result,
                    seen,
                    descendantHasCheckedCommandAncestor,
                    descendantHasCheckedStructureAncestor);
            }
        }
    }

    private bool TryGetLegacyScriptSceneFromTreeNode(
        LegacyScriptEditorScope scope,
        TreeNode? node,
        out LegacyScenarioScene scene)
    {
        scene = null!;
        if (node?.Tag is LegacyScenarioItemData { Scene: { } itemScene })
        {
            scene = itemScene;
            return true;
        }

        if (!TryGetScriptTreeRow(node, out var row) || row.NodeType != "Scene候选")
        {
            return false;
        }

        var document = GetCurrentLegacyScriptDocument(scope);
        scene = document?.Scenes.FirstOrDefault(candidate => candidate.SceneIndex == row.SceneIndex)!;
        return scene != null;
    }

    private bool TryGetLegacyScriptSectionFromTreeNode(
        LegacyScriptEditorScope scope,
        TreeNode? node,
        out LegacyScenarioSection section)
    {
        section = null!;
        if (node?.Tag is LegacyScenarioItemData { Section: { } itemSection })
        {
            section = itemSection;
            return true;
        }

        if (!TryGetScriptTreeRow(node, out var row) || row.NodeType != "Section候选")
        {
            return false;
        }

        var document = GetCurrentLegacyScriptDocument(scope);
        section = document?.Scenes
            .FirstOrDefault(scene => scene.SceneIndex == row.SceneIndex)?
            .Sections.FirstOrDefault(candidate => candidate.SectionIndex == row.SectionIndex)!;
        return section != null;
    }

    private bool TryGetLegacyScriptCommandFromTreeNode(TreeNode? node, out LegacyScenarioCommandNode command)
    {
        command = null!;
        if (node?.Tag is LegacyScenarioItemData { Command: { } itemCommand })
        {
            command = itemCommand;
            return true;
        }

        return TryGetScriptTreeRow(node, out var row) &&
               row.NodeType == "Command候选" &&
               TryGetLegacyScriptCommand(row, out command);
    }

    private bool TryGetLegacyScriptCommandFromTreeNode(LegacyScriptEditorScope scope, TreeNode? node, out LegacyScenarioCommandNode command)
    {
        command = null!;
        if (node?.Tag is LegacyScenarioItemData { Command: { } itemCommand })
        {
            command = itemCommand;
            return true;
        }

        if (!TryGetScriptTreeRow(node, out var row) || row.NodeType != "Command候选")
        {
            return false;
        }

        return scope switch
        {
            LegacyScriptEditorScope.Script => TryGetLegacyScriptCommand(row, out command),
            LegacyScriptEditorScope.Battlefield => _battlefieldScriptCommandByKey.TryGetValue(BuildLegacyCommandKey(row), out command!),
            LegacyScriptEditorScope.RScene => _rSceneScriptCommandByKey.TryGetValue(BuildLegacyCommandKey(row), out command!),
            _ => false
        };
    }

    private bool TryGetSelectedLegacyScriptSection(out LegacyScenarioSection section)
    {
        section = null!;
        if (_currentLegacyScriptDocument == null)
        {
            return false;
        }

        if (_scriptTree.SelectedNode?.Tag is LegacyScenarioItemData itemData)
        {
            if (itemData.Section != null)
            {
                section = itemData.Section;
                return true;
            }

            if (itemData.Command != null)
            {
                section = _currentLegacyScriptDocument.Scenes
                    .FirstOrDefault(scene => scene.SceneIndex == itemData.Command.SceneIndex)?
                    .Sections.FirstOrDefault(candidate => candidate.SectionIndex == itemData.Command.SectionIndex)!;
                return section != null;
            }

            return false;
        }

        if (_scriptTree.SelectedNode?.Tag is not ScenarioStructureRow row)
        {
            return false;
        }

        var sceneIndex = row.SceneIndex;
        var sectionIndex = row.SectionIndex;
        if (row.NodeType == "Scene候选")
        {
            return false;
        }

        if (row.NodeType == "Command候选" && TryGetLegacyScriptCommand(row, out var command))
        {
            sceneIndex = command.SceneIndex;
            sectionIndex = command.SectionIndex;
        }

        section = _currentLegacyScriptDocument.Scenes
            .FirstOrDefault(scene => scene.SceneIndex == sceneIndex)?
            .Sections.FirstOrDefault(candidate => candidate.SectionIndex == sectionIndex)!;
        return section != null;
    }

    private bool TryGetSelectedLegacyScriptSectionNode(LegacyScriptEditorScope scope, out LegacyScenarioSection section)
    {
        section = null!;
        var document = GetCurrentLegacyScriptDocument(scope);
        if (document == null)
        {
            return false;
        }

        if (TryGetSelectedLegacyItemData(scope, out var itemData) && itemData.Section != null)
        {
            section = itemData.Section;
            return true;
        }

        var tree = GetLegacyScriptTree(scope);
        if (tree.SelectedNode?.Tag is ScenarioStructureRow { NodeType: "Section候选" } row)
        {
            section = document.Scenes
                .FirstOrDefault(scene => scene.SceneIndex == row.SceneIndex)?
                .Sections.FirstOrDefault(candidate => candidate.SectionIndex == row.SectionIndex)!;
            return section != null;
        }

        return false;
    }

    private bool TryGetSelectedLegacyScriptSceneNode(LegacyScriptEditorScope scope, out LegacyScenarioScene scene)
    {
        scene = null!;
        var document = GetCurrentLegacyScriptDocument(scope);
        if (document == null)
        {
            return false;
        }

        if (TryGetSelectedLegacyItemData(scope, out var itemData) && itemData.Scene != null)
        {
            scene = itemData.Scene;
            return true;
        }

        var tree = GetLegacyScriptTree(scope);
        if (tree.SelectedNode?.Tag is ScenarioStructureRow { NodeType: "Scene候选" } row)
        {
            scene = document.Scenes.FirstOrDefault(candidate => candidate.SceneIndex == row.SceneIndex)!;
            return scene != null;
        }

        return false;
    }

    private bool CanDeleteLegacyScriptCommand(LegacyScenarioCommandNode command, out string reason)
        => CanDeleteLegacyScriptCommand(LegacyScriptEditorScope.Script, command, out reason);

    private bool CanDeleteLegacyScriptCommand(LegacyScriptEditorScope scope, LegacyScenarioCommandNode command, out string reason)
    {
        if (GetCurrentLegacyScriptDocument(scope) == null)
        {
            reason = "当前没有可编辑的旧版完整剧本树。";
            return false;
        }

        if (command.CommandId == 0x00)
        {
            reason = "旧版 CczSceneEditor2 不允许手动删除 0 号事件结束/正文根命令。";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool CanDeleteLegacyScriptScene(
        LegacyScenarioDocument document,
        LegacyScenarioScene scene,
        out string reason)
    {
        if (!document.Scenes.Contains(scene))
        {
            reason = "没有在当前旧版剧本树中定位到 Scene。";
            return false;
        }

        if (document.Scenes.Count <= 1)
        {
            reason = "不能删除最后一个 Scene，旧版剧本至少需要保留一个 Scene。";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool CanDeleteLegacyScriptSceneBatch(
        LegacyScenarioDocument document,
        IReadOnlyList<LegacyScenarioScene> scenes,
        out string reason)
    {
        var distinctScenes = scenes.Distinct().ToList();
        if (distinctScenes.Count == 0)
        {
            reason = "请先勾选要删除的 Scene。";
            return false;
        }

        foreach (var scene in distinctScenes)
        {
            if (!document.Scenes.Contains(scene))
            {
                reason = "勾选列表中包含当前旧版剧本树中不存在的 Scene。";
                return false;
            }
        }

        if (document.Scenes.Count - distinctScenes.Count < 1)
        {
            reason = "不能删除全部 Scene，旧版剧本至少需要保留一个 Scene。";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool CanDeleteLegacyScriptSection(
        LegacyScenarioDocument document,
        LegacyScenarioSection section,
        out string reason)
    {
        var scene = document.Scenes.FirstOrDefault(candidate => candidate.Sections.Contains(section));
        if (scene == null)
        {
            reason = "没有在当前旧版剧本树中定位到 Section。";
            return false;
        }

        if (scene.Sections.Count <= 1)
        {
            reason = "不能删除 Scene 中最后一个 Section，旧版 Scene 至少需要保留一个 Section。";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool CanDeleteLegacyScriptSectionBatch(
        LegacyScenarioDocument document,
        IReadOnlyList<LegacyScenarioSection> sections,
        out string reason)
    {
        var distinctSections = sections.Distinct().ToList();
        if (distinctSections.Count == 0)
        {
            reason = "请先勾选要删除的 Section。";
            return false;
        }

        var selectedByScene = new Dictionary<LegacyScenarioScene, HashSet<LegacyScenarioSection>>();
        foreach (var section in distinctSections)
        {
            var scene = document.Scenes.FirstOrDefault(candidate => candidate.Sections.Contains(section));
            if (scene == null)
            {
                reason = "勾选列表中包含当前旧版剧本树中不存在的 Section。";
                return false;
            }

            if (!selectedByScene.TryGetValue(scene, out var selectedSections))
            {
                selectedSections = [];
                selectedByScene[scene] = selectedSections;
            }

            selectedSections.Add(section);
        }

        foreach (var (scene, selectedSections) in selectedByScene)
        {
            if (scene.Sections.Count - selectedSections.Count < 1)
            {
                reason = $"不能删除 Scene {scene.SceneIndex} 中的全部 Section，旧版 Scene 至少需要保留一个 Section。";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static bool CanInsertNearLegacyScriptCommand(LegacyScenarioCommandNode command, out string reason)
    {
        _ = command;
        reason = string.Empty;
        return true;
    }

    private static bool CanCopyLegacyScriptCommand(LegacyScenarioCommandNode command, out string reason)
    {
        _ = command;
        reason = string.Empty;
        return true;
    }

    private bool TryGetLegacyScriptCommandPasteTarget(
        LegacyScriptEditorScope scope,
        bool beforeSelected,
        out LegacyScriptCommandPasteTarget target,
        out string reason)
    {
        target = null!;
        var document = GetCurrentLegacyScriptDocument(scope);
        if (document == null)
        {
            reason = "当前没有可编辑的旧版完整剧本树。";
            return false;
        }

        if (TryGetSelectedLegacyScriptSectionNode(scope, out var selectedSection))
        {
            if (!TryGetLegacyScriptSectionAppendList(selectedSection, out var appendList, out var targetText))
            {
                reason = "没有在当前 Section 中定位到可追加命令的位置。";
                return false;
            }

            target = new LegacyScriptCommandPasteTarget(
                appendList,
                GetLegacyScriptAppendIndex(appendList),
                selectedSection.SceneIndex,
                selectedSection.SectionIndex,
                targetText,
                "粘贴到 Section 正文末尾");
            reason = string.Empty;
            return true;
        }

        if (TryGetSelectedLegacyScriptCommand(scope, out var selectedCommand))
        {
            if (!CanInsertNearLegacyScriptCommand(selectedCommand, out reason))
            {
                return false;
            }

            if (!TryFindLegacyCommandList(document, selectedCommand, out var list, out var index))
            {
                reason = "没有在当前旧版命令树中定位到粘贴目标。";
                return false;
            }

            target = new LegacyScriptCommandPasteTarget(
                list,
                GetLegacyScriptNearInsertIndex(list, index, beforeSelected),
                selectedCommand.SceneIndex,
                selectedCommand.SectionIndex,
                $"Scene {selectedCommand.SceneIndex} / Section {selectedCommand.SectionIndex} / Command {selectedCommand.CommandIndex}",
                beforeSelected ? "粘贴到前面" : "粘贴到后面");
            reason = string.Empty;
            return true;
        }

        reason = "请先选择粘贴目标命令或 Section。";
        return false;
    }

    private bool IsMovableLegacyScriptCommand(LegacyScenarioCommandNode command, out string reason)
        => IsMovableLegacyScriptCommand(_currentLegacyScriptDocument, command, out reason);

    private static bool IsMovableLegacyScriptCommand(LegacyScenarioDocument? document, LegacyScenarioCommandNode command, out string reason)
    {
        if (command.CommandId is 0x00 or 0x01)
        {
            reason = "旧版 CczSceneEditor2 不允许移动 0/1 号结构指令。";
            return false;
        }

        if (document != null &&
            TryFindLegacyCommandList(document, command, out var list, out _) &&
            IsLegacyScriptSectionTopLevelList(document, list))
        {
            reason = "旧版 CczSceneEditor2 不允许移动 Section 顶层头部指令。";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private int GetLegacyScriptNearInsertIndex(IReadOnlyList<LegacyScenarioCommandNode> list, int selectedIndex, bool beforeSelected)
    {
        if (beforeSelected &&
            selectedIndex > 0 &&
            IsLegacyScriptSubEventCarrier(list[selectedIndex]) &&
            list[selectedIndex - 1].CommandId == 0x01)
        {
            return selectedIndex - 1;
        }

        if (!beforeSelected &&
            list[selectedIndex].CommandId == 0x01 &&
            selectedIndex + 1 < list.Count &&
            IsLegacyScriptSubEventCarrier(list[selectedIndex + 1]))
        {
            return selectedIndex + 2;
        }

        return beforeSelected ? selectedIndex : selectedIndex + 1;
    }

    private static int GetLegacyScriptOldInsertIndex(IReadOnlyList<LegacyScenarioCommandNode> list, int selectedIndex)
        => selectedIndex > 0 &&
           IsLegacyScriptSubEventCarrier(list[selectedIndex]) &&
           list[selectedIndex - 1].CommandId == 0x01
            ? selectedIndex - 1
            : selectedIndex;

    private int GetLegacyScriptAddBelowInsertIndex(IReadOnlyList<LegacyScenarioCommandNode> list, int selectedIndex)
    {
        if (IsLegacyScriptTrailingBoundary(list[selectedIndex]))
        {
            return selectedIndex;
        }

        return GetLegacyScriptNearInsertIndex(list, selectedIndex, beforeSelected: false);
    }

    private int GetLegacyScriptAddInsertIndex(
        IReadOnlyList<LegacyScenarioCommandNode> list,
        int selectedIndex,
        bool beforeSelected)
        => beforeSelected
            ? GetLegacyScriptNearInsertIndex(list, selectedIndex, beforeSelected: true)
            : GetLegacyScriptAddBelowInsertIndex(list, selectedIndex);

    private static bool HasLegacyScriptSubEventMarkerBefore(IReadOnlyList<LegacyScenarioCommandNode> list, int insertIndex)
        => insertIndex > 0 && insertIndex <= list.Count && list[insertIndex - 1].CommandId == 0x01;

    private bool IsLegacyScriptSectionTopLevelList(IReadOnlyList<LegacyScenarioCommandNode> list)
        => _currentLegacyScriptDocument?.Scenes
            .SelectMany(scene => scene.Sections)
            .Any(section => ReferenceEquals(section.Commands, list)) == true;

    private static bool IsLegacyScriptSectionTopLevelList(
        LegacyScenarioDocument document,
        IReadOnlyList<LegacyScenarioCommandNode> list)
        => document.Scenes
            .SelectMany(scene => scene.Sections)
            .Any(section => ReferenceEquals(section.Commands, list));

    private static bool IsLegacyScriptSubEventCarrier(LegacyScenarioCommandNode command)
        => command.ChildBlock is { Kind: "SubEvent" } || command.OpensSubEventBlock;

    private static bool LegacyScriptSectionContainsCommandId(LegacyScenarioSection section, int commandId)
        => section.EnumerateCommands().Any(command => command.CommandId == commandId);

    private static bool TryGetLegacyScriptSectionAppendList(
        LegacyScenarioSection section,
        out List<LegacyScenarioCommandNode> commands,
        out string targetText)
    {
        var bodyRoot = section.Commands.FirstOrDefault(candidate => candidate.StartsBodyBlock && candidate.ChildBlock != null);
        if (bodyRoot?.ChildBlock != null)
        {
            commands = bodyRoot.ChildBlock.Commands;
            targetText = $"Scene {section.SceneIndex} / Section {section.SectionIndex} 事件正文末尾";
            return true;
        }

        commands = section.Commands;
        targetText = $"Scene {section.SceneIndex} / Section {section.SectionIndex} 顶层末尾";
        return true;
    }

    private static (int StartIndex, int Count) GetLegacyScriptMoveRange(
        IReadOnlyList<LegacyScenarioCommandNode> list,
        int selectedIndex)
    {
        if (IsLegacyScriptSubEventCarrier(list[selectedIndex]) &&
            selectedIndex > 0 &&
            list[selectedIndex - 1].CommandId == 0x01)
        {
            return (selectedIndex - 1, 2);
        }

        return (selectedIndex, 1);
    }

    private static int GetLegacyScriptMoveInsertIndex(
        IReadOnlyList<LegacyScenarioCommandNode> list,
        int startIndex,
        int count,
        bool up)
    {
        if (up)
        {
            if (startIndex <= 0) return -1;
            var previousEnd = startIndex - 1;
            return IsLegacyScriptSubEventCarrier(list[previousEnd]) &&
                   previousEnd > 0 &&
                   list[previousEnd - 1].CommandId == 0x01
                ? previousEnd - 1
                : previousEnd;
        }

        var endIndex = startIndex + count - 1;
        if (endIndex >= list.Count - 1) return -1;
        var nextStart = endIndex + 1;
        if (list[nextStart].CommandId == 0x00) return -1;
        var nextEnd = nextStart + 1 < list.Count &&
                      list[nextStart].CommandId == 0x01 &&
                      IsLegacyScriptSubEventCarrier(list[nextStart + 1])
            ? nextStart + 1
            : nextStart;
        return nextEnd + 1;
    }

    private LegacyScenarioCommandNode CloneLegacyScriptCommandForPaste(
        LegacyScenarioCommandNode source,
        int sceneIndex,
        int sectionIndex)
        => CloneLegacyScriptCommandForPaste(source, sceneIndex, sectionIndex, preserveStructuralFlags: false);

    private LegacyScenarioScene CloneLegacyScriptSceneForPaste(LegacyScenarioScene source)
    {
        var clone = new LegacyScenarioScene
        {
            SceneIndex = source.SceneIndex,
            FileOffset = 0
        };

        foreach (var section in source.Sections)
        {
            clone.Sections.Add(CloneLegacyScriptSectionForPaste(section, section.SceneIndex, section.SectionIndex));
        }

        return clone;
    }

    private LegacyScenarioSection CloneLegacyScriptSectionForPaste(
        LegacyScenarioSection source,
        int sceneIndex,
        int sectionIndex)
    {
        var clone = new LegacyScenarioSection
        {
            SceneIndex = sceneIndex,
            SectionIndex = sectionIndex,
            FileOffset = 0,
            LengthPrefixOffset = 0,
            DeclaredLength = 0
        };

        foreach (var command in source.Commands)
        {
            clone.Commands.Add(CloneLegacyScriptCommandForPaste(command, sceneIndex, sectionIndex, preserveStructuralFlags: true));
        }

        return clone;
    }

    private LegacyScenarioCommandNode CloneLegacyScriptCommandForPaste(
        LegacyScenarioCommandNode source,
        int sceneIndex,
        int sectionIndex,
        bool preserveStructuralFlags)
    {
        var clone = new LegacyScenarioCommandNode
        {
            SceneIndex = sceneIndex,
            SectionIndex = sectionIndex,
            CommandId = source.CommandId,
            CommandName = source.CommandName,
            FileOffset = 0,
            ConsumedBytes = 0,
            StartsBodyBlock = preserveStructuralFlags && source.StartsBodyBlock,
            IsSubEventMarker = source.IsSubEventMarker || (preserveStructuralFlags && source.CommandId == 0x01),
            OpensSubEventBlock = preserveStructuralFlags
                ? source.OpensSubEventBlock
                : source.ChildBlock is { Kind: "SubEvent" } || source.OpensSubEventBlock,
            EndsSubEventBlock = source.EndsSubEventBlock || (preserveStructuralFlags && source.CommandId == 0x00),
            JumpTargetOrdinal = source.JumpTargetOrdinal,
            JumpTargetCommandIndex = source.JumpTargetCommandIndex,
            OriginalJumpDisplacement = source.OriginalJumpDisplacement
        };

        foreach (var parameter in source.Parameters)
        {
            clone.Parameters.Add(CloneLegacyScriptParameterForPaste(parameter));
        }

        if (source.ChildBlock != null)
        {
            clone.ChildBlock = CloneLegacyScriptCommandBlockForPaste(source.ChildBlock, sceneIndex, sectionIndex);
        }

        return clone;
    }

    private LegacyScenarioCommandBlock CloneLegacyScriptCommandBlockForPaste(
        LegacyScenarioCommandBlock source,
        int sceneIndex,
        int sectionIndex)
    {
        var clone = new LegacyScenarioCommandBlock
        {
            Kind = source.Kind,
            LengthPrefixOffset = 0,
            FileOffset = 0,
            DeclaredLength = 0
        };

        foreach (var command in source.Commands)
        {
            clone.Commands.Add(CloneLegacyScriptCommandForPaste(command, sceneIndex, sectionIndex, preserveStructuralFlags: true));
        }

        return clone;
    }

    private LegacyScenarioCommandParameter CloneLegacyScriptParameterForPaste(LegacyScenarioCommandParameter source)
    {
        var clone = new LegacyScenarioCommandParameter
        {
            Index = source.Index,
            LayoutCode = source.LayoutCode,
            Tag = source.Tag,
            FileOffset = AllocateLegacyScriptSyntheticOffset(),
            Kind = source.Kind,
            IntValue = source.IntValue,
            Text = source.Text,
            ByteLength = source.ByteLength
        };
        clone.Values.AddRange(source.Values);
        return clone;
    }

    private LegacyScenarioCommandNode CreateLegacyScriptStructuralCommand(int commandId, int sceneIndex, int sectionIndex)
    {
        var command = new LegacyScenarioCommandNode
        {
            SceneIndex = sceneIndex,
            SectionIndex = sectionIndex,
            CommandId = commandId,
            CommandName = ResolveLegacyScriptCommandName(commandId),
            FileOffset = 0,
            ConsumedBytes = 0,
            IsSubEventMarker = commandId == 0x01,
            EndsSubEventBlock = commandId == 0x00
        };

        foreach (var parameter in CreateDefaultLegacyScriptParameters(commandId))
        {
            command.Parameters.Add(parameter);
        }

        return command;
    }

    private LegacyScenarioScene CreateLegacyScriptDefaultScene(int sceneIndex)
    {
        var scene = new LegacyScenarioScene
        {
            SceneIndex = sceneIndex,
            FileOffset = 0
        };
        scene.Sections.Add(CreateLegacyScriptDefaultSection(sceneIndex, 1));
        return scene;
    }

    private LegacyScenarioSection CreateLegacyScriptDefaultSection(int sceneIndex, int sectionIndex)
    {
        var section = new LegacyScenarioSection
        {
            SceneIndex = sceneIndex,
            SectionIndex = sectionIndex,
            FileOffset = 0,
            LengthPrefixOffset = 0,
            DeclaredLength = 0
        };

        section.Commands.Add(CreateLegacyScriptStructuralCommand(0x02, sceneIndex, sectionIndex));
        var bodyRoot = CreateLegacyScriptStructuralCommand(0x00, sceneIndex, sectionIndex);
        bodyRoot.StartsBodyBlock = true;
        bodyRoot.EndsSubEventBlock = false;
        bodyRoot.ChildBlock = new LegacyScenarioCommandBlock
        {
            Kind = "Body",
            LengthPrefixOffset = 0,
            FileOffset = 0,
            DeclaredLength = 0
        };
        bodyRoot.ChildBlock.Commands.Add(CreateLegacyScriptStructuralCommand(0x00, sceneIndex, sectionIndex));
        section.Commands.Add(bodyRoot);

        return section;
    }

    private string ResolveLegacyScriptCommandName(int commandId)
        => _currentSceneStringDocument?.Commands.FirstOrDefault(command => command.Id == commandId)?.Name
           ?? $"Command {HexDisplayFormatter.Format(commandId, 2)}";

    private static int GetLegacyScriptAppendIndex(List<LegacyScenarioCommandNode> commands)
    {
        var index = commands.Count;
        while (index > 0 && IsLegacyScriptTrailingBoundary(commands[index - 1]))
        {
            index--;
        }

        return index;
    }

    private static bool IsLegacyScriptTrailingBoundary(LegacyScenarioCommandNode command)
        => command.EndsSubEventBlock || command.CommandId is 0x0C or 0x0D;

    private static Dictionary<LegacyScenarioCommandNode, LegacyScenarioCommandNode> CaptureLegacyJumpTargets(LegacyScenarioDocument document)
    {
        var byOrdinal = document.EnumerateCommands().ToDictionaryFirstByKey(command => command.CommandOrdinal, command => command);
        var result = new Dictionary<LegacyScenarioCommandNode, LegacyScenarioCommandNode>();
        foreach (var command in byOrdinal.Values.Where(command => command.CommandId == 0x76))
        {
            if (command.JumpTargetOrdinal.HasValue && byOrdinal.TryGetValue(command.JumpTargetOrdinal.Value, out var target))
            {
                result[command] = target;
            }
        }

        return result;
    }

    private static void RestoreLegacyJumpTargets(
        LegacyScenarioDocument document,
        IReadOnlyDictionary<LegacyScenarioCommandNode, LegacyScenarioCommandNode> jumpTargets)
    {
        var activeCommands = document.EnumerateCommands().ToHashSet();
        foreach (var pair in jumpTargets)
        {
            if (!activeCommands.Contains(pair.Key)) continue;
            if (activeCommands.Contains(pair.Value))
            {
                pair.Key.JumpTargetOrdinal = pair.Value.CommandOrdinal;
                pair.Key.JumpTargetCommandIndex = pair.Value.CommandIndex;
            }
            else
            {
                pair.Key.JumpTargetOrdinal = null;
                pair.Key.JumpTargetCommandIndex = null;
            }
        }
    }

    private static void ReindexLegacyScriptDocument(LegacyScenarioDocument document)
    {
        var ordinal = 0;
        for (var sceneIndex = 0; sceneIndex < document.Scenes.Count; sceneIndex++)
        {
            var scene = document.Scenes[sceneIndex];
            scene.SceneIndex = sceneIndex + 1;
            for (var sectionIndex = 0; sectionIndex < scene.Sections.Count; sectionIndex++)
            {
                var section = scene.Sections[sectionIndex];
                section.SceneIndex = scene.SceneIndex;
                section.SectionIndex = sectionIndex + 1;
                var commandIndex = 0;
                ReindexLegacyScriptCommandList(section.Commands, section.SceneIndex, section.SectionIndex, ref commandIndex, ref ordinal);
            }
        }
    }

    private static void ReindexLegacyScriptCommandList(
        IReadOnlyList<LegacyScenarioCommandNode> commands,
        int sceneIndex,
        int sectionIndex,
        ref int commandIndex,
        ref int ordinal)
    {
        foreach (var command in commands)
        {
            command.SceneIndex = sceneIndex;
            command.SectionIndex = sectionIndex;
            command.CommandIndex = ++commandIndex;
            command.CommandOrdinal = ordinal++;
            if (command.ChildBlock != null)
            {
                ReindexLegacyScriptCommandList(command.ChildBlock.Commands, sceneIndex, sectionIndex, ref commandIndex, ref ordinal);
            }
        }
    }

    private static bool TryFindLegacyCommandList(
        LegacyScenarioDocument document,
        LegacyScenarioCommandNode target,
        out List<LegacyScenarioCommandNode> list,
        out int index)
    {
        foreach (var scene in document.Scenes)
        {
            foreach (var section in scene.Sections)
            {
                if (TryFindLegacyCommandList(section.Commands, target, out list, out index))
                {
                    return true;
                }
            }
        }

        list = null!;
        index = -1;
        return false;
    }

    private static LegacyScenarioSection? FindLegacyScriptSectionForCommand(
        LegacyScenarioDocument? document,
        LegacyScenarioCommandNode command)
    {
        if (document == null)
        {
            return null;
        }

        foreach (var scene in document.Scenes)
        {
            foreach (var section in scene.Sections)
            {
                if (section.EnumerateCommands().Any(candidate => ReferenceEquals(candidate, command)))
                {
                    return section;
                }
            }
        }

        return null;
    }

    private static bool TryFindLegacySectionForCommandList(
        LegacyScenarioDocument document,
        IReadOnlyList<LegacyScenarioCommandNode> commandList,
        out LegacyScenarioSection section)
    {
        foreach (var scene in document.Scenes)
        {
            foreach (var candidate in scene.Sections)
            {
                if (ReferenceEquals(candidate.Commands, commandList) ||
                    candidate.Commands.Any(command => ContainsLegacyCommandList(command, commandList)))
                {
                    section = candidate;
                    return true;
                }
            }
        }

        section = null!;
        return false;
    }

    private static bool ContainsLegacyCommandList(
        LegacyScenarioCommandNode command,
        IReadOnlyList<LegacyScenarioCommandNode> commandList)
    {
        if (command.ChildBlock == null)
        {
            return false;
        }

        if (ReferenceEquals(command.ChildBlock.Commands, commandList))
        {
            return true;
        }

        return command.ChildBlock.Commands.Any(child => ContainsLegacyCommandList(child, commandList));
    }

    private static bool TryFindLegacySectionList(
        LegacyScenarioDocument document,
        LegacyScenarioSection target,
        out List<LegacyScenarioSection> sections,
        out int index)
    {
        foreach (var scene in document.Scenes)
        {
            index = scene.Sections.IndexOf(target);
            if (index >= 0)
            {
                sections = scene.Sections;
                return true;
            }
        }

        sections = null!;
        index = -1;
        return false;
    }

    private static bool TryFindLegacyCommandList(
        List<LegacyScenarioCommandNode> commands,
        LegacyScenarioCommandNode target,
        out List<LegacyScenarioCommandNode> list,
        out int index)
    {
        index = commands.IndexOf(target);
        if (index >= 0)
        {
            list = commands;
            return true;
        }

        foreach (var command in commands)
        {
            if (command.ChildBlock != null &&
                TryFindLegacyCommandList(command.ChildBlock.Commands, target, out list, out index))
            {
                return true;
            }
        }

        list = null!;
        index = -1;
        return false;
    }

    private void BuildScriptTree(ScenarioStructureProbeResult structure, IReadOnlyList<ScenarioTextEntry> texts)
    {
        if (_currentLegacyScriptDocument != null)
        {
            BuildLegacyScriptTree(_currentLegacyScriptDocument, structure);
            return;
        }

        _scriptTree.BeginUpdate();
        try
        {
            _scriptTree.Nodes.Clear();

            // UI 表示（旧工具源码对照版）：
            // Scene -> Section -> 条件区 -> 0x00正文根 -> 正文/子事件
            //
            // 依据 cczEditor2View.cpp::CreateFileTree 已确认的读取规则：
            // 1. Section 开头到第一个 0x00 之前是头部/条件区。
            // 2. 第一个 0x00 会切到其子块，等价于“正文根”。
            // 3. 0x01 只是“子事件设定”标志；真正承接子树的是它后面第一条可嵌套命令。
            // 4. 正文中的 0x00 若在子事件嵌套内，优先作为子事件结束标志。
            //
            // 注意：当前底层仍是只读探针，不掌握真实子块长度；这里是在已有命令序列上尽量贴近旧编辑器语义，
            // 目的是改善浏览/定位体验，不把该树当作可直接写回的真实结构。
            var rows = structure.Rows;
            var sectionTextAssignments = BuildScriptSectionTextAssignments(structure, texts);
            var attachedOffsets = new HashSet<int>(
                sectionTextAssignments.Values
                    .SelectMany(list => list)
                    .Select(text => text.Offset));
            var sceneTextAssignments = BuildScriptSceneFallbackTextAssignments(structure, texts, attachedOffsets);
            foreach (var sceneTexts in sceneTextAssignments.Values)
            {
                foreach (var text in sceneTexts)
                {
                    attachedOffsets.Add(text.Offset);
                }
            }

            foreach (var scene in rows.Where(x => x.NodeType == "Scene候选").OrderBy(x => x.SceneIndex))
            {
                var sceneNode = new TreeNode(BuildScriptSceneNodeText(scene, rows, sectionTextAssignments, sceneTextAssignments))
                {
                    Tag = scene,
                    ToolTipText = scene.Annotation
                };
                foreach (var section in rows.Where(x => x.NodeType == "Section候选" && x.SceneIndex == scene.SceneIndex).OrderBy(x => x.SectionIndex))
                {
                    var sectionNode = new TreeNode(BuildScriptSectionNodeText(section, rows, sectionTextAssignments))
                    {
                        Tag = section,
                        ToolTipText = section.Annotation
                    };

                    var commandRows = rows
                        .Where(x => x.NodeType == "Command候选" && x.SceneIndex == scene.SceneIndex && x.SectionIndex == section.SectionIndex)
                        .OrderBy(x => x.CommandIndex)
                        .ToList();

                    if (commandRows.Count == 0)
                    {
                        AppendScriptSectionTextNode(sectionNode, section, sectionTextAssignments);
                        sectionNode.Expand();
                        sceneNode.Nodes.Add(sectionNode);
                        continue;
                    }

                    var useLegacyFlags = structure.UsedLegacyParser && commandRows.Any(row => row.StartsBodyBlock || row.OpensSubEventBlock || row.EndsSubEventBlock);
                    var firstBodySeparatorIndex = useLegacyFlags
                        ? commandRows.FindIndex(row => row.StartsBodyBlock)
                        : commandRows.FindIndex(row => row.CommandId == 0);
                    if (firstBodySeparatorIndex < 0)
                    {
                        var flatNode = new TreeNode(BuildCommandCollectionNodeText("Section命令（未识别到正文根）", commandRows));
                        foreach (var command in commandRows)
                        {
                            flatNode.Nodes.Add(CreateScriptCommandTreeNode(command));
                        }
                        sectionNode.Nodes.Add(flatNode);
                        AppendScriptSectionTextNode(sectionNode, section, sectionTextAssignments);
                        sectionNode.Expand();
                        sceneNode.Nodes.Add(sectionNode);
                        continue;
                    }

                    if (firstBodySeparatorIndex > 0)
                    {
                        var conditionNode = new TreeNode(BuildCommandCollectionNodeText("测试条件", commandRows.Take(firstBodySeparatorIndex)));
                        for (var i = 0; i < firstBodySeparatorIndex; i++)
                        {
                            conditionNode.Nodes.Add(CreateScriptCommandTreeNode(commandRows[i]));
                        }
                        sectionNode.Nodes.Add(conditionNode);
                    }

                    var bodyRootRow = commandRows[firstBodySeparatorIndex];
                    var bodyRootNode = new TreeNode(BuildBodyRootNodeText(bodyRootRow)) { Tag = bodyRootRow, ToolTipText = bodyRootRow.Annotation };
                    sectionNode.Nodes.Add(bodyRootNode);

                    var bodyContainerNode = new TreeNode("事件正文");
                    bodyRootNode.Nodes.Add(bodyContainerNode);

                    var parentStack = new Stack<TreeNode>();
                    parentStack.Push(bodyContainerNode);

                    var subEventSerial = 0;
                    var pendingSubEventMarker = false;
                    TreeNode? pendingSubEventMarkerNode = null;
                    string? pendingSubEventMarkerText = null;

                    for (var i = firstBodySeparatorIndex + 1; i < commandRows.Count; i++)
                    {
                        var command = commandRows[i];
                        var opensSubEventBlock = useLegacyFlags
                            ? command.OpensSubEventBlock
                            : pendingSubEventMarker && IsLikelySubEventCarrier(command.CommandId);
                        var endsSubEventBlock = useLegacyFlags
                            ? command.EndsSubEventBlock
                            : command.CommandId == 0 && parentStack.Count > 1;

                        if (command.CommandId == 1)
                        {
                            pendingSubEventMarker = true;
                            pendingSubEventMarkerNode = CreateScriptCommandTreeNode(command);
                            pendingSubEventMarkerText = BuildScriptCommandSummary(command, includeIdentity: false, maxVisibleValues: 4);
                            parentStack.Peek().Nodes.Add(pendingSubEventMarkerNode);
                            continue;
                        }

                        if (opensSubEventBlock)
                        {
                            subEventSerial++;
                            var subEventNode = new TreeNode($"子事件 {subEventSerial:000}｜{BuildScriptCommandSummary(command, includeIdentity: false, maxVisibleValues: 4)}");
                            if (pendingSubEventMarkerNode != null)
                            {
                                var settingWrapper = new TreeNode(string.IsNullOrWhiteSpace(pendingSubEventMarkerText)
                                    ? "子事件设定"
                                    : $"子事件设定｜{TrimSingleLine(pendingSubEventMarkerText, 48)}");
                                settingWrapper.Nodes.Add(pendingSubEventMarkerNode);
                                subEventNode.Nodes.Add(settingWrapper);
                            }

                            var carrierNode = new TreeNode("子事件主体");
                            carrierNode.Nodes.Add(CreateScriptCommandTreeNode(command));
                            carrierNode.Text = BuildChildNodeGroupText("子事件主体", carrierNode.Nodes);
                            subEventNode.Nodes.Add(carrierNode);
                            parentStack.Peek().Nodes.Add(subEventNode);
                            parentStack.Push(carrierNode);
                            pendingSubEventMarker = false;
                            pendingSubEventMarkerNode = null;
                            pendingSubEventMarkerText = null;
                            continue;
                        }

                        if (pendingSubEventMarker)
                        {
                            pendingSubEventMarker = false;
                            pendingSubEventMarkerNode = null;
                            pendingSubEventMarkerText = null;
                        }

                        var commandNode = CreateScriptCommandTreeNode(command);
                        parentStack.Peek().Nodes.Add(commandNode);

                        if (endsSubEventBlock && parentStack.Count > 1)
                        {
                            parentStack.Pop();
                        }
                    }

                    bodyContainerNode.Text = BuildChildNodeGroupText("事件正文", bodyContainerNode.Nodes);
                    AppendScriptSectionTextNode(sectionNode, section, sectionTextAssignments);
                    sectionNode.Expand();
                    sceneNode.Nodes.Add(sectionNode);
                }

                if (sceneTextAssignments.TryGetValue(scene.SceneIndex, out var sceneTexts) && sceneTexts.Count > 0)
                {
                    var textRootNode = new TreeNode(BuildTextGroupNodeText("Scene补充文本线索", sceneTexts)) { Tag = scene };
                    foreach (var text in sceneTexts)
                    {
                        textRootNode.Nodes.Add(CreateScriptTextTreeNode(text));
                    }
                    sceneNode.Nodes.Add(textRootNode);
                }

                sceneNode.Expand();
                _scriptTree.Nodes.Add(sceneNode);
            }

            var detachedTexts = texts.Where(text => !attachedOffsets.Contains(text.Offset)).ToList();
            if (detachedTexts.Count > 0)
            {
                var textRoot = new TreeNode(BuildTextGroupNodeText("未归属文本线索", detachedTexts));
                foreach (var text in detachedTexts)
                {
                    textRoot.Nodes.Add(CreateScriptTextTreeNode(text));
                }
                _scriptTree.Nodes.Add(textRoot);
            }
            if (_scriptTree.Nodes.Count > 0) _scriptTree.Nodes[0].Expand();
        }
        finally
        {
            _scriptTree.EndUpdate();
        }
    }

    private ScenarioStructureProbeResult BuildLegacyScriptStructureResult(LegacyScenarioDocument document)
    {
        _legacyScriptCommandByKey.Clear();
        _legacyScriptRowByKey.Clear();
        var rows = new List<ScenarioStructureRow>();
        var nextIndex = 1;
        foreach (var scene in document.Scenes)
        {
            rows.Add(new ScenarioStructureRow
            {
                Index = nextIndex++,
                Level = 0,
                NodeType = "Scene候选",
                SceneIndex = scene.SceneIndex,
                CommandName = $"Scene {scene.SceneIndex}",
                OffsetHex = HexDisplayFormatter.FormatOffset(scene.FileOffset),
                Confidence = "旧版源码",
                Annotation = "按 CczSceneEditor2 v0.23 Scene 偏移表读取。"
            });

            foreach (var section in scene.Sections)
            {
                rows.Add(new ScenarioStructureRow
                {
                    Index = nextIndex++,
                    Level = 1,
                    NodeType = "Section候选",
                    SceneIndex = section.SceneIndex,
                    SectionIndex = section.SectionIndex,
                    CommandName = $"Section {section.SectionIndex}",
                    OffsetHex = HexDisplayFormatter.FormatOffset(section.FileOffset),
                    Confidence = "旧版源码",
                    Annotation = $"按旧版 Section 长度前缀读取，长度 {section.DeclaredLength} 字节。"
                });

                foreach (var command in section.EnumerateCommands())
                {
                    var row = BuildLegacyScriptCommandRow(command, nextIndex++);
                    var key = BuildLegacyCommandKey(row);
                    rows.Add(row);
                    _legacyScriptCommandByKey[key] = command;
                    _legacyScriptRowByKey[key] = row;
                }
            }
        }

        return new ScenarioStructureProbeResult
        {
            FilePath = document.FilePath,
            FileName = document.FileName,
            CommandCandidateCount = document.CommandCount,
            SceneCount = document.SceneCount,
            SectionCount = document.SectionCount,
            UsedLegacyParser = true,
            Summary = $"旧版剧本树：{document.Summary}。当前主视图已绑定真实 Scene/Section/子块结构，完整保存会重建偏移表、长度前缀和 0x76 跳转。",
            Rows = rows,
            XmlText = BuildLegacyScriptXml(document)
        };
    }

    private ScenarioStructureRow BuildLegacyScriptCommandRow(LegacyScenarioCommandNode command, int index)
    {
        var textCount = command.TextParameters.Count();
        var referenceParts = new List<string>();
        if (textCount > 0) referenceParts.Add($"文本参数 {textCount} 条");
        if (command.JumpTargetOrdinal.HasValue) referenceParts.Add($"0x76 跳转目标 ord={command.JumpTargetOrdinal.Value}");
        if (command.Parameters.Any(parameter => parameter.Values.Count > 0)) referenceParts.Add("可变数组参数");

        return new ScenarioStructureRow
        {
            Index = index,
            Level = 2,
            NodeType = "Command候选",
            SceneIndex = command.SceneIndex,
            SectionIndex = command.SectionIndex,
            CommandIndex = command.CommandIndex,
            OffsetHex = HexDisplayFormatter.FormatOffset(command.FileOffset),
            CommandId = command.CommandId,
            CommandIdHex = command.CommandIdHex,
            CommandName = command.CommandName,
            ParameterPreview = BuildLegacyScriptParameterPreview(command),
            RawContextWordsHex = BuildLegacyScriptRawWords(command),
            LegacyParameterLayout = BuildLegacyScriptLayout(command),
            StartsBodyBlock = command.StartsBodyBlock,
            OpensSubEventBlock = command.OpensSubEventBlock,
            EndsSubEventBlock = command.EndsSubEventBlock,
            HasCommandTemplate = _scenarioCommandParameterTemplateService.HasTemplate(command.CommandId),
            CommandTemplateHint = _scenarioCommandParameterTemplateService.BuildShortHint(command.CommandId, command.CommandName),
            ReferenceHint = string.Join("；", referenceParts),
            Confidence = "旧版源码",
            Annotation = BuildLegacyScriptCommandAnnotation(command)
        };
    }

    private IReadOnlyList<ScenarioTextEntry> BuildLegacyScriptTextEntries(LegacyScenarioDocument document)
    {
        _legacyScriptTextByOffset.Clear();
        _legacyScriptTextEntryByOffset.Clear();
        var entries = new List<ScenarioTextEntry>();
        var index = 1;
        foreach (var command in document.EnumerateCommands())
        {
            foreach (var parameter in command.TextParameters)
            {
                var capacity = Math.Max(0, parameter.ByteLength - 1);
                var offsetText = FormatLegacyScriptOffset(parameter.FileOffset, index);
                var decodeWarning = parameter.TextDecodeWarning;
                var confidence = string.IsNullOrWhiteSpace(parameter.TextDecodeConfidence) ? "高" : parameter.TextDecodeConfidence;
                var entry = new ScenarioTextEntry
                {
                    Index = index++,
                    Offset = parameter.FileOffset,
                    OffsetHex = offsetText,
                    ByteLength = capacity,
                    CharLength = parameter.Text.Length,
                    Kind = $"旧版文本参数 {command.CommandIdHex}",
                    HasNewLines = parameter.Text.Contains('\n') || parameter.Text.Contains('\r'),
                    Preview = parameter.Text.Length > 60 ? parameter.Text[..60] : parameter.Text,
                    Text = parameter.Text,
                    OriginalText = parameter.Text,
                    SourceKind = "旧版完整树文本参数",
                    EncodingName = string.IsNullOrWhiteSpace(parameter.TextEncodingName) ? "GBK" : parameter.TextEncodingName,
                    DecodeConfidence = confidence,
                    DecodeWarning = decodeWarning,
                    IsWritable = confidence != "低",
                    Annotation = $"Scene {command.SceneIndex} / Section {command.SectionIndex} / Command {command.CommandIndex} {command.CommandName} 参数槽 {parameter.Index}。旧版完整树文本参数；解码置信度 {confidence}{(string.IsNullOrWhiteSpace(decodeWarning) ? string.Empty : "；" + decodeWarning)}。完整保存会随命令树重建，不走原地短写。"
                };
                entries.Add(entry);
                _legacyScriptTextByOffset[entry.Offset] = (command, parameter);
                _legacyScriptTextEntryByOffset[entry.Offset] = entry;
            }
        }

        return entries;
    }

    private void BuildLegacyScriptTree(LegacyScenarioDocument document, ScenarioStructureProbeResult structure)
    {
        _scriptTree.BeginUpdate();
        try
        {
            _scriptTree.Nodes.Clear();
            RebuildLegacyScriptItemDataIndex(document, structure);
            var rowByKey = structure.Rows
                .Where(row => row.NodeType == "Command候选")
                .ToDictionaryFirstByKey(BuildLegacyCommandKey, row => row, StringComparer.OrdinalIgnoreCase);
            var sceneRows = structure.Rows
                .Where(row => row.NodeType == "Scene候选")
                .ToDictionaryFirstByKey(row => row.SceneIndex, row => row);
            var sectionRows = structure.Rows
                .Where(row => row.NodeType == "Section候选")
                .ToDictionaryFirstByKey(row => (row.SceneIndex, row.SectionIndex), row => row);

            var rootNode = new TreeNode(document.FilePath)
            {
                ToolTipText = document.FilePath
            };

            foreach (var scene in document.Scenes)
            {
                sceneRows.TryGetValue(scene.SceneIndex, out var sceneRow);
                var sceneItemData = new LegacyScenarioItemData { Id = -1, Ord = scene.SceneIndex - 1, Scene = scene, UiRow = sceneRow };
                var sceneNode = new TreeNode(BuildLegacySceneNodeText(scene))
                {
                    Tag = sceneItemData,
                    ToolTipText = sceneRow?.Annotation ?? string.Empty
                };
                foreach (var section in scene.Sections)
                {
                    sectionRows.TryGetValue((section.SceneIndex, section.SectionIndex), out var sectionRow);
                    sceneNode.Nodes.Add(CreateLegacyScriptSectionTreeNode(section, sectionRow, rowByKey));
                }
                sceneNode.Expand();
                rootNode.Nodes.Add(sceneNode);
            }

            _scriptTree.Nodes.Add(rootNode);

            if (_scriptTree.Nodes.Count > 0)
            {
                _scriptTree.Nodes[0].Expand();
                if (_scriptTree.Nodes[0].Nodes.Count > 0)
                {
                    _scriptTree.Nodes[0].Nodes[0].Expand();
                    if (_scriptTree.Nodes[0].Nodes[0].Nodes.Count > 0)
                    {
                        _scriptTree.Nodes[0].Nodes[0].Nodes[0].Expand();
                    }
                }
            }
        }
        finally
        {
            _scriptTree.EndUpdate();
        }
    }

    private TreeNode CreateLegacyScriptSectionTreeNode(
        LegacyScenarioSection section,
        ScenarioStructureRow? sectionRow,
        IReadOnlyDictionary<string, ScenarioStructureRow> rowByKey)
        => CreateLegacyScriptSectionTreeNode(LegacyScriptEditorScope.Script, section, sectionRow, rowByKey);

    private TreeNode CreateLegacyScriptSectionTreeNode(
        LegacyScriptEditorScope scope,
        LegacyScenarioSection section,
        ScenarioStructureRow? sectionRow,
        IReadOnlyDictionary<string, ScenarioStructureRow> rowByKey)
    {
        var sectionItemData = new LegacyScenarioItemData { Id = -2, Ord = section.SectionIndex - 1, Section = section, UiRow = sectionRow };
        var flattenedCommands = section.EnumerateCommands().ToList();
        var sectionNode = new TreeNode(BuildLegacySectionNodeText(section, flattenedCommands))
        {
            Tag = sectionItemData,
            ToolTipText = sectionRow?.Annotation ?? string.Empty
        };
        var activeCommands = new HashSet<LegacyScenarioCommandNode>();
        var activeBlocks = new HashSet<LegacyScenarioCommandBlock>();
        if (flattenedCommands.Count > ScriptLegacyTreeCommandNodeLimitPerSection)
        {
            sectionNode.Nodes.Add(new TreeNode($"命令列表已折叠 ({flattenedCommands.Count})")
            {
                Tag = sectionRow,
                ToolTipText = "异常保护：该 Section 命令数量超过界面安全阈值，已停止继续创建 TreeNode。"
            });

            sectionNode.Expand();
            return sectionNode;
        }

        foreach (var command in section.Commands)
        {
            if (!rowByKey.TryGetValue(BuildLegacyCommandKey(command), out var row))
            {
                continue;
            }

            var node = CreateLegacyScriptCommandTreeNode(scope, command, row, rowByKey, depth: 0, activeCommands, activeBlocks);
            sectionNode.Nodes.Add(node);
        }

        sectionNode.Expand();
        return sectionNode;
    }

    private TreeNode CreateLegacyScriptCommandTreeNode(
        LegacyScenarioCommandNode command,
        ScenarioStructureRow row,
        IReadOnlyDictionary<string, ScenarioStructureRow> rowByKey,
        int depth,
        HashSet<LegacyScenarioCommandNode> activeCommands,
        HashSet<LegacyScenarioCommandBlock> activeBlocks)
        => CreateLegacyScriptCommandTreeNode(LegacyScriptEditorScope.Script, command, row, rowByKey, depth, activeCommands, activeBlocks);

    private TreeNode CreateLegacyScriptCommandTreeNode(
        LegacyScriptEditorScope scope,
        LegacyScenarioCommandNode command,
        ScenarioStructureRow row,
        IReadOnlyDictionary<string, ScenarioStructureRow> rowByKey,
        int depth,
        HashSet<LegacyScenarioCommandNode> activeCommands,
        HashSet<LegacyScenarioCommandBlock> activeBlocks)
    {
        var node = new TreeNode(BuildLegacyScriptCommandSummary(row, command, includeIdentity: false, maxVisibleValues: 6))
        {
            Tag = GetLegacyEditorItemData(scope, command, row),
            ToolTipText = BuildLegacyScriptCommandTreeToolTip(row, command),
            ForeColor = GetLegacyScriptCommandColor(command)
        };
        if (scope == LegacyScriptEditorScope.Battlefield)
        {
            ApplyBattlefieldScriptPreviewToNode(node, row);
        }

        if (!activeCommands.Add(command))
        {
            node.Nodes.Add(new TreeNode("递归引用已折叠")
            {
                ToolTipText = "检测到命令子块引用回当前路径，已停止继续展开以避免栈溢出。"
            });
            return node;
        }

        try
        {
            if (depth >= ScriptLegacyTreeMaxNestedDepth)
            {
                if (command.ChildBlock != null)
                {
                    node.Nodes.Add(CreateLegacyScriptDepthFoldNode(command.ChildBlock));
                }
                return node;
            }

            if (command.ChildBlock != null)
            {
                AppendLegacyScriptBlockNodes(scope, command.ChildBlock, node, rowByKey, depth + 1, activeCommands, activeBlocks);
            }

            return node;
        }
        finally
        {
            activeCommands.Remove(command);
        }
    }

    private void AppendLegacyScriptBlockNodes(
        LegacyScenarioCommandBlock block,
        TreeNode parent,
        IReadOnlyDictionary<string, ScenarioStructureRow> rowByKey,
        int depth,
        HashSet<LegacyScenarioCommandNode> activeCommands,
        HashSet<LegacyScenarioCommandBlock> activeBlocks)
        => AppendLegacyScriptBlockNodes(LegacyScriptEditorScope.Script, block, parent, rowByKey, depth, activeCommands, activeBlocks);

    private void AppendLegacyScriptBlockNodes(
        LegacyScriptEditorScope scope,
        LegacyScenarioCommandBlock block,
        TreeNode parent,
        IReadOnlyDictionary<string, ScenarioStructureRow> rowByKey,
        int depth,
        HashSet<LegacyScenarioCommandNode> activeCommands,
        HashSet<LegacyScenarioCommandBlock> activeBlocks)
    {
        if (depth > ScriptLegacyTreeMaxNestedDepth)
        {
            parent.Nodes.Add(CreateLegacyScriptDepthFoldNode(block));
            return;
        }

        if (!activeBlocks.Add(block))
        {
            parent.Nodes.Add(new TreeNode("递归子块已折叠")
            {
                ToolTipText = "检测到子块引用回当前展开路径，已停止继续展开以避免栈溢出。"
            });
            return;
        }

        try
        {
            foreach (var command in block.Commands)
            {
                if (!rowByKey.TryGetValue(BuildLegacyCommandKey(command), out var row))
                {
                    continue;
                }

                var node = CreateLegacyScriptCommandTreeNode(scope, command, row, rowByKey, depth, activeCommands, activeBlocks);
                parent.Nodes.Add(node);
            }
        }
        finally
        {
            activeBlocks.Remove(block);
        }
    }

    private static TreeNode CreateLegacyScriptDepthFoldNode(LegacyScenarioCommandBlock block)
        => new($"嵌套层级已折叠 ({block.Kind}, 直接命令 {block.Commands.Count})")
        {
            ToolTipText = "该子事件嵌套过深，界面停止继续展开以避免 StackOverflowException；完整结构和保存模型仍保留原始子块。"
        };

    private void BuildLegacyEditorScriptTree(
        TreeView tree,
        LegacyScenarioDocument document,
        ScenarioStructureProbeResult structure,
        Dictionary<LegacyScenarioCommandNode, LegacyScenarioItemData> itemDataByCommand,
        Dictionary<ScenarioStructureRow, LegacyScenarioItemData> itemDataByRow,
        Func<LegacyScenarioCommandNode, ScenarioStructureRow, LegacyScenarioItemData, TreeNode>? commandNodeFactory = null)
    {
        tree.BeginUpdate();
        try
        {
            tree.Nodes.Clear();
            RebuildLegacyEditorItemDataIndex(document, structure, itemDataByCommand, itemDataByRow);
            var rowByKey = structure.Rows
                .Where(row => row.NodeType == "Command候选")
                .ToDictionaryFirstByKey(BuildLegacyCommandKey, row => row, StringComparer.OrdinalIgnoreCase);
            var sceneRows = structure.Rows
                .Where(row => row.NodeType == "Scene候选")
                .ToDictionaryFirstByKey(row => row.SceneIndex, row => row);
            var sectionRows = structure.Rows
                .Where(row => row.NodeType == "Section候选")
                .ToDictionaryFirstByKey(row => (row.SceneIndex, row.SectionIndex), row => row);

            var rootNode = new TreeNode(document.FilePath)
            {
                ToolTipText = document.FilePath
            };

            foreach (var scene in document.Scenes)
            {
                sceneRows.TryGetValue(scene.SceneIndex, out var sceneRow);
                var sceneItemData = new LegacyScenarioItemData { Id = -1, Ord = scene.SceneIndex - 1, Scene = scene, UiRow = sceneRow };
                if (sceneRow != null)
                {
                    itemDataByRow[sceneRow] = sceneItemData;
                }

                var sceneNode = new TreeNode(BuildLegacySceneNodeText(scene))
                {
                    Tag = sceneItemData,
                    ToolTipText = sceneRow?.Annotation ?? string.Empty
                };

                foreach (var section in scene.Sections)
                {
                    sectionRows.TryGetValue((section.SceneIndex, section.SectionIndex), out var sectionRow);
                    var sectionItemData = new LegacyScenarioItemData { Id = -2, Ord = section.SectionIndex - 1, Section = section, UiRow = sectionRow };
                    if (sectionRow != null)
                    {
                        itemDataByRow[sectionRow] = sectionItemData;
                    }

                    var sectionNode = new TreeNode(BuildLegacySectionNodeText(section, section.EnumerateCommands().ToList()))
                    {
                        Tag = sectionItemData,
                        ToolTipText = sectionRow?.Annotation ?? string.Empty
                    };

                    var activeCommands = new HashSet<LegacyScenarioCommandNode>();
                    var activeBlocks = new HashSet<LegacyScenarioCommandBlock>();
                    foreach (var command in section.Commands)
                    {
                        if (!rowByKey.TryGetValue(BuildLegacyCommandKey(command), out var row))
                        {
                            continue;
                        }

                        var node = CreateLegacyEditorCommandTreeNode(command, row, itemDataByCommand[command], rowByKey, depth: 0, activeCommands, activeBlocks, itemDataByCommand, commandNodeFactory);
                        sectionNode.Nodes.Add(node);
                    }

                    sectionNode.Expand();
                    sceneNode.Nodes.Add(sectionNode);
                }

                sceneNode.Expand();
                rootNode.Nodes.Add(sceneNode);
            }

            tree.Nodes.Add(rootNode);
            rootNode.Expand();
            if (rootNode.Nodes.Count > 0)
            {
                rootNode.Nodes[0].Expand();
                if (rootNode.Nodes[0].Nodes.Count > 0)
                {
                    rootNode.Nodes[0].Nodes[0].Expand();
                }
            }
        }
        finally
        {
            tree.EndUpdate();
        }
    }

    private void RebuildLegacyEditorItemDataIndex(
        LegacyScenarioDocument document,
        ScenarioStructureProbeResult structure,
        Dictionary<LegacyScenarioCommandNode, LegacyScenarioItemData> itemDataByCommand,
        Dictionary<ScenarioStructureRow, LegacyScenarioItemData> itemDataByRow)
    {
        itemDataByCommand.Clear();
        itemDataByRow.Clear();

        var rowByKey = structure.Rows
            .Where(row => row.NodeType == "Command候选")
            .ToDictionaryFirstByKey(BuildLegacyCommandKey, row => row, StringComparer.OrdinalIgnoreCase);

        foreach (var command in document.EnumerateCommands())
        {
            rowByKey.TryGetValue(BuildLegacyCommandKey(command), out var row);
            var itemData = CreateLegacyScriptItemData(command, row);
            itemDataByCommand[command] = itemData;
            if (row != null)
            {
                itemDataByRow[row] = itemData;
            }
        }
    }

    private TreeNode CreateLegacyEditorCommandTreeNode(
        LegacyScenarioCommandNode command,
        ScenarioStructureRow row,
        LegacyScenarioItemData itemData)
        => new(BuildLegacyScriptCommandSummary(row, command, includeIdentity: false, maxVisibleValues: 6))
        {
            Tag = itemData,
            ToolTipText = BuildLegacyScriptCommandTreeToolTip(row, command),
            ForeColor = GetLegacyScriptCommandColor(command)
        };

    private TreeNode CreateLegacyEditorCommandTreeNode(
        LegacyScenarioCommandNode command,
        ScenarioStructureRow row,
        LegacyScenarioItemData itemData,
        IReadOnlyDictionary<string, ScenarioStructureRow> rowByKey,
        int depth,
        HashSet<LegacyScenarioCommandNode> activeCommands,
        HashSet<LegacyScenarioCommandBlock> activeBlocks,
        Dictionary<LegacyScenarioCommandNode, LegacyScenarioItemData> itemDataByCommand,
        Func<LegacyScenarioCommandNode, ScenarioStructureRow, LegacyScenarioItemData, TreeNode>? commandNodeFactory)
    {
        var node = commandNodeFactory?.Invoke(command, row, itemData)
                   ?? CreateLegacyEditorCommandTreeNode(command, row, itemData);

        if (!activeCommands.Add(command))
        {
            node.Nodes.Add(new TreeNode("递归引用已折叠"));
            return node;
        }

        try
        {
            if (depth >= ScriptLegacyTreeMaxNestedDepth)
            {
                if (command.ChildBlock != null)
                {
                    node.Nodes.Add(CreateLegacyScriptDepthFoldNode(command.ChildBlock));
                }
                return node;
            }

            if (command.ChildBlock != null)
            {
                AppendLegacyEditorBlockNodes(command.ChildBlock, node, rowByKey, depth + 1, activeCommands, activeBlocks, itemDataByCommand, commandNodeFactory);
            }

            return node;
        }
        finally
        {
            activeCommands.Remove(command);
        }
    }

    private void AppendLegacyEditorBlockNodes(
        LegacyScenarioCommandBlock block,
        TreeNode parent,
        IReadOnlyDictionary<string, ScenarioStructureRow> rowByKey,
        int depth,
        HashSet<LegacyScenarioCommandNode> activeCommands,
        HashSet<LegacyScenarioCommandBlock> activeBlocks,
        Dictionary<LegacyScenarioCommandNode, LegacyScenarioItemData> itemDataByCommand,
        Func<LegacyScenarioCommandNode, ScenarioStructureRow, LegacyScenarioItemData, TreeNode>? commandNodeFactory)
    {
        if (depth > ScriptLegacyTreeMaxNestedDepth)
        {
            parent.Nodes.Add(CreateLegacyScriptDepthFoldNode(block));
            return;
        }

        if (!activeBlocks.Add(block))
        {
            parent.Nodes.Add(new TreeNode("递归子块已折叠"));
            return;
        }

        try
        {
            foreach (var command in block.Commands)
            {
                if (!rowByKey.TryGetValue(BuildLegacyCommandKey(command), out var row))
                {
                    continue;
                }

                var itemData = itemDataByCommand.TryGetValue(command, out var existing)
                    ? existing
                    : CreateLegacyScriptItemData(command, row);
                itemDataByCommand[command] = itemData;
                var node = CreateLegacyEditorCommandTreeNode(command, row, itemData, rowByKey, depth, activeCommands, activeBlocks, itemDataByCommand, commandNodeFactory);
                parent.Nodes.Add(node);
            }
        }
        finally
        {
            activeBlocks.Remove(block);
        }
    }

    private void RebuildLegacyScriptItemDataIndex(LegacyScenarioDocument document, ScenarioStructureProbeResult structure)
    {
        _legacyScriptItemDataByCommand.Clear();
        _legacyScriptItemDataByRow.Clear();

        var rowByKey = structure.Rows
            .Where(row => row.NodeType == "Command候选")
            .ToDictionaryFirstByKey(BuildLegacyCommandKey, row => row, StringComparer.OrdinalIgnoreCase);
        foreach (var command in document.EnumerateCommands())
        {
            rowByKey.TryGetValue(BuildLegacyCommandKey(command), out var row);
            var itemData = CreateLegacyScriptItemData(command, row);
            _legacyScriptItemDataByCommand[command] = itemData;
            if (row != null)
            {
                _legacyScriptItemDataByRow[row] = itemData;
            }
        }
    }

    private LegacyScenarioItemData GetLegacyScriptItemData(LegacyScenarioCommandNode command, ScenarioStructureRow? row)
    {
        if (_legacyScriptItemDataByCommand.TryGetValue(command, out var itemData))
        {
            if (row != null) itemData.UiRow = row;
            return itemData;
        }

        itemData = CreateLegacyScriptItemData(command, row);
        _legacyScriptItemDataByCommand[command] = itemData;
        if (row != null) _legacyScriptItemDataByRow[row] = itemData;
        return itemData;
    }

    private LegacyScenarioItemData GetLegacyEditorItemData(
        LegacyScriptEditorScope scope,
        LegacyScenarioCommandNode command,
        ScenarioStructureRow? row)
    {
        if (scope == LegacyScriptEditorScope.Script)
        {
            return GetLegacyScriptItemData(command, row);
        }

        var itemDataByCommand = scope == LegacyScriptEditorScope.Battlefield
            ? _battlefieldScriptItemDataByCommand
            : _rSceneScriptItemDataByCommand;
        var itemDataByRow = scope == LegacyScriptEditorScope.Battlefield
            ? _battlefieldScriptItemDataByRow
            : _rSceneScriptItemDataByRow;

        if (itemDataByCommand.TryGetValue(command, out var itemData))
        {
            if (row != null)
            {
                itemData.UiRow = row;
                itemDataByRow[row] = itemData;
            }

            return itemData;
        }

        itemData = CreateLegacyScriptItemData(command, row);
        itemDataByCommand[command] = itemData;
        if (row != null)
        {
            itemDataByRow[row] = itemData;
        }

        return itemData;
    }

    private static LegacyScenarioItemData CreateLegacyScriptItemData(LegacyScenarioCommandNode command, ScenarioStructureRow? row)
    {
        var itemData = new LegacyScenarioItemData
        {
            Id = command.CommandId,
            Ord = command.CommandOrdinal,
            Command = command,
            UiRow = row
        };
        CopyLegacyCommandToItemData(command, itemData);
        return itemData;
    }

    private static void CopyLegacyCommandToItemData(LegacyScenarioCommandNode command, LegacyScenarioItemData itemData)
    {
        itemData.IntData.Clear();
        itemData.LongCharData = string.Empty;
        var longCharCount = 0;
        var variableArraySeen = false;

        foreach (var parameter in command.Parameters)
        {
            switch (parameter.Kind)
            {
                case LegacyScenarioParameterKind.Text:
                    itemData.LongCharData = parameter.Text;
                    longCharCount++;
                    break;
                case LegacyScenarioParameterKind.VariableArray:
                    {
                        var start = variableArraySeen ? 25 : 0;
                        EnsureLegacyItemDataIntSize(itemData, start + 25);
                        var count = Math.Min(25, parameter.Values.Count);
                        for (var i = 0; i < count; i++) itemData.IntData[start + i] = parameter.Values[i];
                        for (var i = count; i < 25; i++) itemData.IntData[start + i] = -1;
                        variableArraySeen = true;
                        break;
                    }
                default:
                    {
                        var targetIndex = Math.Max(0, parameter.Index - longCharCount);
                        EnsureLegacyItemDataIntSize(itemData, targetIndex + 1);
                        itemData.IntData[targetIndex] = command.CommandId == 0x76 && parameter.Kind == LegacyScenarioParameterKind.Dword32
                            ? command.JumpTargetOrdinal ?? parameter.IntValue
                            : parameter.IntValue;
                        break;
                    }
            }
        }

        var minimumLength = command.CommandId switch
        {
            0x05 => 50,
            0x46 => 11 * 20,
            0x47 => 12 * 80,
            0x70 or 0x71 => 1000,
            _ => 20
        };
        EnsureLegacyItemDataIntSize(itemData, minimumLength);
    }

    private static void CopyLegacyItemDataToCommand(LegacyScenarioItemData itemData)
    {
        var command = itemData.Command;
        if (command == null) return;

        var commandParameterLimit = command.CommandId switch
        {
            0x46 => 11 * 20,
            0x47 => 12 * 80,
            _ => int.MaxValue
        };
        var longCharCount = 0;
        var variableArraySeen = false;
        foreach (var parameter in command.Parameters.ToArray())
        {
            if (parameter.Index >= commandParameterLimit)
            {
                command.Parameters.Remove(parameter);
                continue;
            }

            switch (parameter.Kind)
            {
                case LegacyScenarioParameterKind.Text:
                    parameter.Text = itemData.LongCharData ?? string.Empty;
                    parameter.ByteLength = EncodingService.GetGbkByteCount(parameter.Text) + 1;
                    longCharCount++;
                    break;
                case LegacyScenarioParameterKind.VariableArray:
                    {
                        var start = variableArraySeen ? 25 : 0;
                        parameter.Values.Clear();
                        for (var i = start; i < Math.Min(itemData.IntData.Count, start + 25); i++)
                        {
                            if (itemData.IntData[i] == -1) break;
                            parameter.Values.Add(itemData.IntData[i]);
                        }
                        parameter.IntValue = parameter.Values.Count;
                        parameter.ByteLength = 2 + parameter.Values.Count * 2;
                        variableArraySeen = true;
                        break;
                    }
                default:
                    {
                        var sourceIndex = Math.Max(0, parameter.Index - longCharCount);
                        var value = sourceIndex < itemData.IntData.Count ? itemData.IntData[sourceIndex] : 0;
                        if (command.CommandId == 0x76 && parameter.Kind == LegacyScenarioParameterKind.Dword32)
                        {
                            command.JumpTargetOrdinal = value;
                            command.JumpTargetCommandIndex = null;
                            command.OriginalJumpDisplacement = null;
                        }
                        else
                        {
                            parameter.IntValue = value;
                        }
                        parameter.ByteLength = parameter.Kind == LegacyScenarioParameterKind.Dword32 ? 4 : 2;
                        break;
                    }
            }
        }
    }

    private static void EnsureLegacyItemDataIntSize(LegacyScenarioItemData itemData, int size)
    {
        while (itemData.IntData.Count < size)
        {
            itemData.IntData.Add(0);
        }
    }

    private static LegacyItemDataCommandSnapshot CaptureLegacyItemDataCommandSnapshot(LegacyScenarioItemData itemData)
    {
        var command = itemData.Command;
        return new LegacyItemDataCommandSnapshot
        {
            IntData = itemData.IntData.ToList(),
            LongCharData = itemData.LongCharData ?? string.Empty,
            JumpTargetOrdinal = command?.JumpTargetOrdinal,
            JumpTargetCommandIndex = command?.JumpTargetCommandIndex,
            OriginalJumpDisplacement = command?.OriginalJumpDisplacement,
            Parameters = command?.Parameters.Select(parameter => new LegacyScriptParameterSnapshot
            {
                Index = parameter.Index,
                Kind = parameter.Kind,
                IntValue = parameter.IntValue,
                Text = parameter.Text,
                Values = parameter.Values.ToList(),
                ByteLength = parameter.ByteLength
            }).ToList() ?? []
        };
    }

    private static bool LegacyItemDataCommandChanged(
        LegacyScenarioItemData itemData,
        LegacyItemDataCommandSnapshot before)
    {
        var after = CaptureLegacyItemDataCommandSnapshot(itemData);
        return !before.IntData.SequenceEqual(after.IntData) ||
               !string.Equals(before.LongCharData, after.LongCharData, StringComparison.Ordinal) ||
               before.JumpTargetOrdinal != after.JumpTargetOrdinal ||
               before.JumpTargetCommandIndex != after.JumpTargetCommandIndex ||
               before.OriginalJumpDisplacement != after.OriginalJumpDisplacement ||
               !LegacyParameterSnapshotsEqual(before.Parameters, after.Parameters);
    }

    private static bool LegacyParameterSnapshotsEqual(
        IReadOnlyList<LegacyScriptParameterSnapshot> left,
        IReadOnlyList<LegacyScriptParameterSnapshot> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            var a = left[i];
            var b = right[i];
            if (a.Index != b.Index ||
                a.Kind != b.Kind ||
                a.IntValue != b.IntValue ||
                !string.Equals(a.Text, b.Text, StringComparison.Ordinal) ||
                a.ByteLength != b.ByteLength ||
                !a.Values.SequenceEqual(b.Values))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class LegacyItemDataCommandSnapshot
    {
        public List<int> IntData { get; init; } = [];
        public string LongCharData { get; init; } = string.Empty;
        public int? JumpTargetOrdinal { get; init; }
        public int? JumpTargetCommandIndex { get; init; }
        public int? OriginalJumpDisplacement { get; init; }
        public List<LegacyScriptParameterSnapshot> Parameters { get; init; } = [];
    }

    private static string BuildLegacyCommandKey(LegacyScenarioCommandNode command)
        => $"{command.SceneIndex}:{command.SectionIndex}:{command.CommandIndex}:{HexDisplayFormatter.FormatOffset(command.FileOffset)}:{HexDisplayFormatter.Format(command.CommandId, 2)}";

    private static string BuildLegacyCommandKey(ScenarioStructureRow row)
        => $"{row.SceneIndex}:{row.SectionIndex}:{row.CommandIndex}:{HexDisplayFormatter.NormalizeText(row.OffsetHex)}:{HexDisplayFormatter.Format(row.CommandId, 2)}";

    private static string FormatLegacyScriptOffset(int offset, int syntheticIndex)
        => offset >= 0
            ? HexDisplayFormatter.FormatOffset(offset)
            : $"new:{syntheticIndex:000}";

    private static string BuildLegacyScriptParameterPreview(LegacyScenarioCommandNode command)
    {
        if (command.Parameters.Count == 0) return "无参数";
        return string.Join(" ", command.Parameters.Take(8).Select(parameter => parameter.Kind switch
        {
            LegacyScenarioParameterKind.Text => $"T{parameter.Index}=\"{parameter.Text[..Math.Min(parameter.Text.Length, 12)]}\"",
            LegacyScenarioParameterKind.VariableArray => $"V{parameter.Index}[{parameter.Values.Count}]",
            _ => $"P{parameter.Index}={FormatLegacyParameterPreviewValue(command, parameter)}"
        }));
    }

    private static string FormatLegacyParameterPreviewValue(LegacyScenarioCommandNode command, LegacyScenarioCommandParameter parameter)
    {
        if (command.CommandId == 0x76 && parameter.Kind == LegacyScenarioParameterKind.Dword32)
        {
            return command.JumpTargetOrdinal?.ToString(CultureInfo.InvariantCulture)
                   ?? parameter.IntValue.ToString(CultureInfo.InvariantCulture);
        }

        if (IsPerson1Parameter(command, parameter.Index))
        {
            return FormatPerson1Reference(parameter.IntValue);
        }

        if (IsPerson2Parameter(command, parameter.Index))
        {
            return ScriptVariableValueResolver.FormatPerson2Reference(parameter.IntValue);
        }

        return parameter.IntValue.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatPerson1Reference(int value)
        => value == LegacyMfcDialogDataSources.EmptyPerson1Code
            ? "无"
            : value.ToString(CultureInfo.InvariantCulture);

    private static bool IsPerson1Parameter(LegacyScenarioCommandNode command, int parameterIndex)
        => IsForceAllyDeploymentPersonParameter(command.CommandId, parameterIndex);

    private static bool IsPerson2Parameter(LegacyScenarioCommandNode command, int parameterIndex)
        => command.CommandId switch
        {
            0x12 or 0x1B or 0x25 or 0x29 or 0x2A or 0x2B or 0x2D or 0x30 or 0x33 or 0x34 or 0x35 or 0x36 or 0x38 or 0x39 or 0x3B or 0x3C or 0x4B or 0x50 or 0x52 or 0x5C or 0x68 or 0x70 or 0x75 => parameterIndex == 0,
            0x15 or 0x4F => parameterIndex is 0 or 1,
            0x26 => parameterIndex == 0,
            0x31 or 0x32 or 0x4C or 0x4D or 0x4E or 0x53 or 0x55 => parameterIndex == 1,
            0x3D or 0x6D => parameterIndex == 3,
            0x45 => parameterIndex is 5 or 7,
            0x78 => parameterIndex == 2,
            _ => false
        };

    private static string BuildLegacyScriptRawWords(LegacyScenarioCommandNode command)
    {
        var words = new List<string> { HexDisplayFormatter.FormatWord(command.CommandId) };
        foreach (var parameter in command.Parameters.Take(16))
        {
            words.Add(HexDisplayFormatter.FormatWord(parameter.Tag));
            if (parameter.Kind is LegacyScenarioParameterKind.Word16 or LegacyScenarioParameterKind.Dword32)
            {
                words.Add(HexDisplayFormatter.FormatWord(unchecked((ushort)parameter.IntValue)));
            }
        }
        return string.Join(" ", words);
    }

    private static string BuildLegacyScriptLayout(LegacyScenarioCommandNode command)
        => string.Join("；", command.Parameters.Select(parameter => $"{parameter.Index}:{parameter.LayoutCodeHex}/{parameter.TagHex}/{parameter.Kind}"));

    private static string BuildLegacyScriptCommandAnnotation(LegacyScenarioCommandNode command)
    {
        var flags = new List<string>();
        if (command.StartsBodyBlock) flags.Add("Section 头部结束并进入正文根");
        if (command.IsSubEventMarker) flags.Add("子事件设定标志");
        if (command.OpensSubEventBlock) flags.Add("承载子事件块");
        if (command.EndsSubEventBlock) flags.Add("子事件结束命令");
        if (command.JumpTargetOrdinal.HasValue) flags.Add($"跳转到 ord {command.JumpTargetOrdinal.Value}");
        return flags.Count == 0
            ? "按旧版命令参数表读取，可参与完整结构写回。"
            : string.Join("；", flags) + "。";
    }

    private static string BuildLegacyScriptXml(LegacyScenarioDocument document)
    {
        var lines = new List<string>
        {
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
            $"<LegacyScenario file=\"{System.Security.SecurityElement.Escape(document.FileName)}\">"
        };
        foreach (var scene in document.Scenes)
        {
            lines.Add($"  <Scene index=\"{scene.SceneIndex}\" offset=\"{HexDisplayFormatter.FormatOffset(scene.FileOffset)}\">");
            foreach (var section in scene.Sections)
            {
                lines.Add($"    <Section index=\"{section.SectionIndex}\" offset=\"{HexDisplayFormatter.FormatOffset(section.FileOffset)}\" length=\"{section.DeclaredLength}\">");
                foreach (var command in section.EnumerateCommands())
                {
                    lines.Add($"      <Command index=\"{command.CommandIndex}\" ord=\"{command.CommandOrdinal}\" offset=\"{HexDisplayFormatter.FormatOffset(command.FileOffset)}\" id=\"{HexDisplayFormatter.NormalizeText(command.CommandIdHex)}\" name=\"{System.Security.SecurityElement.Escape(command.CommandName)}\" />");
                }
                lines.Add("    </Section>");
            }
            lines.Add("  </Scene>");
        }
        lines.Add("</LegacyScenario>");
        return string.Join(Environment.NewLine, lines);
    }

    private string BuildScriptSceneNodeText(
        ScenarioStructureRow scene,
        IReadOnlyList<ScenarioStructureRow> rows,
        IReadOnlyDictionary<(int SceneIndex, int SectionIndex), IReadOnlyList<ScenarioTextEntry>> sectionTextAssignments,
        IReadOnlyDictionary<int, IReadOnlyList<ScenarioTextEntry>> sceneTextAssignments)
    {
        var sceneCommands = rows
            .Where(row => row.NodeType == "Command候选" && row.SceneIndex == scene.SceneIndex)
            .ToList();
        var sceneTexts = sectionTextAssignments
            .Where(pair => pair.Key.SceneIndex == scene.SceneIndex)
            .SelectMany(pair => pair.Value)
            .Concat(sceneTextAssignments.TryGetValue(scene.SceneIndex, out var extraTexts) ? extraTexts : Array.Empty<ScenarioTextEntry>())
            .DistinctBy(text => text.Offset)
            .ToList();
        var summary = BuildScriptNodeSummary(sceneTexts, sceneCommands);
        return string.IsNullOrWhiteSpace(summary)
            ? $"Scene {scene.SceneIndex}"
            : $"Scene {scene.SceneIndex}｜{summary}";
    }

    private string BuildScriptSectionNodeText(
        ScenarioStructureRow section,
        IReadOnlyList<ScenarioStructureRow> rows,
        IReadOnlyDictionary<(int SceneIndex, int SectionIndex), IReadOnlyList<ScenarioTextEntry>> sectionTextAssignments)
        => $"Section {section.SectionIndex}";

    private static string BuildLegacySceneNodeText(LegacyScenarioScene scene)
        => $"Scene {scene.SceneIndex}";

    private static string BuildLegacySectionNodeText(LegacyScenarioSection section, IReadOnlyList<LegacyScenarioCommandNode> commands)
        => $"Section {section.SectionIndex}";

    private static string BuildScriptNodeSummary(
        IReadOnlyList<ScenarioTextEntry> texts,
        IReadOnlyList<ScenarioStructureRow> commands)
    {
        var bestText = PickBestSummaryText(texts);
        if (bestText != null)
        {
            return BuildTextSummary(bestText);
        }

        var command = commands.FirstOrDefault(IsPreferredSummaryCommand) ?? commands.FirstOrDefault();
        return command == null
            ? string.Empty
            : TrimSingleLine(BuildScriptCommandValuesPreview(command, maxVisibleValues: 3), 36);
    }

    private static string BuildLegacyNodeSummary(IReadOnlyList<LegacyScenarioCommandNode> commands)
    {
        var bestText = PickBestLegacySummaryText(commands);
        if (!string.IsNullOrWhiteSpace(bestText))
        {
            return TrimSingleLine(bestText, 24);
        }

        var command = commands.FirstOrDefault(IsPreferredLegacySummaryCommand) ?? commands.FirstOrDefault();
        return command == null
            ? string.Empty
            : TrimSingleLine(BuildLegacyCommandValuesPreviewFallback(command, maxVisibleValues: 3), 24);
    }

    private static ScenarioTextEntry? PickBestSummaryText(IEnumerable<ScenarioTextEntry> texts)
        => texts
            .Where(text => !string.IsNullOrWhiteSpace(text.Text) || !string.IsNullOrWhiteSpace(text.Preview))
            .OrderByDescending(GetSummaryTextScore)
            .ThenBy(text => text.Offset)
            .FirstOrDefault();

    private static string? PickBestLegacySummaryText(IEnumerable<LegacyScenarioCommandNode> commands)
        => commands
            .SelectMany(command => command.TextParameters.Select(parameter => new
            {
                Text = parameter.Text,
                Score = GetLegacySummaryTextScore(command, parameter.Text),
                command.FileOffset,
                parameter.Index
            }))
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.FileOffset)
            .ThenBy(item => item.Index)
            .Select(item => item.Text)
            .FirstOrDefault();

    private static int GetSummaryTextScore(ScenarioTextEntry text)
    {
        var score = 0;
        if (text.Kind.Contains("标题", StringComparison.Ordinal) || text.Kind.Contains("场所", StringComparison.Ordinal)) score += 40;
        if (text.Kind.Contains("事件", StringComparison.Ordinal) || text.Kind.Contains("章名", StringComparison.Ordinal)) score += 28;
        if (text.Kind.Contains("短文本", StringComparison.Ordinal)) score += 18;
        if (!text.HasNewLines) score += 10;
        var source = string.IsNullOrWhiteSpace(text.Text) ? text.Preview : text.Text;
        var length = source.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal).Length;
        score += Math.Max(0, 24 - Math.Min(length, 24));
        return score;
    }

    private static int GetLegacySummaryTextScore(LegacyScenarioCommandNode command, string text)
    {
        var score = 0;
        if (command.CommandId is 0x17 or 0x18 or 0x67) score += 40;
        if (command.CommandName.Contains("场所", StringComparison.Ordinal)
            || command.CommandName.Contains("事件", StringComparison.Ordinal)
            || command.CommandName.Contains("章", StringComparison.Ordinal))
        {
            score += 24;
        }

        if (!text.Contains('\r') && !text.Contains('\n')) score += 10;
        var length = text.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal).Length;
        score += Math.Max(0, 24 - Math.Min(length, 24));
        return score;
    }

    private static bool IsPreferredSummaryCommand(ScenarioStructureRow row)
        => row.CommandId is not (0x00 or 0x01 or 0x0C or 0x0D)
           && !row.CommandName.Contains("结束", StringComparison.Ordinal)
           && !row.CommandName.Contains("设定", StringComparison.Ordinal);

    private static bool IsPreferredLegacySummaryCommand(LegacyScenarioCommandNode command)
        => command.CommandId is not (0x00 or 0x01 or 0x0C or 0x0D)
           && !command.CommandName.Contains("结束", StringComparison.Ordinal)
           && !command.CommandName.Contains("设定", StringComparison.Ordinal);

    private static string BuildTextSummary(ScenarioTextEntry text)
        => TrimSingleLine(string.IsNullOrWhiteSpace(text.Text) ? text.Preview : text.Text, 24);

    private static string BuildTextGroupNodeText(string title, IReadOnlyList<ScenarioTextEntry> texts)
    {
        if (texts.Count == 0)
        {
            return title;
        }

        var summary = BuildTextSummary(PickBestSummaryText(texts) ?? texts[0]);
        return $"{title}｜{summary}";
    }

    private static string BuildCommandCollectionNodeText(string title, IEnumerable<ScenarioStructureRow> commands)
        => title;

    private static string BuildChildNodeGroupText(string title, TreeNodeCollection nodes)
        => title;

    private static string BuildBodyRootNodeText(ScenarioStructureRow row)
        => "正文根";

    private static TreeNode CreateScriptCommandTreeNode(ScenarioStructureRow command)
        => new(BuildScriptCommandSummary(command, includeIdentity: true, maxVisibleValues: 6))
        {
            Tag = command,
            ToolTipText = BuildScriptCommandTreeToolTip(command),
            ForeColor = GetScriptCommandColor(command.CommandId)
        };

    private static TreeNode CreateScriptTextTreeNode(ScenarioTextEntry text)
        => new(BuildScriptTextNodeText(text))
        {
            Tag = text,
            ToolTipText = BuildScriptTextTreeToolTip(text)
        };

    private static string BuildScriptCommandSummary(ScenarioStructureRow command, bool includeIdentity, int maxVisibleValues)
    {
        var label = includeIdentity
            ? $"{HexDisplayFormatter.Format(command.CommandId)}:{command.CommandName}"
            : $"{HexDisplayFormatter.Format(command.CommandId)}:{command.CommandName}";
        var suffix = BuildScriptCommandValuesPreview(command, maxVisibleValues);
        return string.IsNullOrWhiteSpace(suffix) ? label : $"{label} {suffix}";
    }

    private LegacyScenarioCommandDisplayFormatter GetLegacyScenarioCommandDisplayFormatter()
    {
        _legacyScenarioCommandDisplayFormatter ??= new LegacyScenarioCommandDisplayFormatter(GetLegacyMfcDialogDataSources());
        return _legacyScenarioCommandDisplayFormatter;
    }

    private LegacyMfcDialogDataSources GetLegacyMfcDialogDataSources()
    {
        _legacyMfcDialogDataSources ??= LegacyMfcDialogDataSources.Create(_project, _tables);
        return _legacyMfcDialogDataSources;
    }

    private string BuildLegacyScriptCommandSummary(ScenarioStructureRow row, LegacyScenarioCommandNode command, bool includeIdentity, int maxVisibleValues)
        => GetLegacyScenarioCommandDisplayFormatter().FormatCommand(command, includeIdentity);

    private static string BuildScriptCommandValuesPreview(ScenarioStructureRow command, int maxVisibleValues)
    {
        var friendly = BuildScenarioStructureFriendlyValueText(command);
        if (!string.IsNullOrWhiteSpace(friendly))
        {
            return TrimSingleLine(friendly, 132);
        }

        var values = BuildScenarioStructurePreviewValueLines(command)
            .Where(value => !string.IsNullOrWhiteSpace(value) && value != "无参数")
            .ToList();
        return values.Count == 0
            ? string.Empty
            : TrimSingleLine(JoinValuePreview(values, maxVisibleValues), 132);
    }

    private string BuildLegacyCommandValuesPreview(LegacyScenarioCommandNode command, int maxVisibleValues)
        => GetLegacyScenarioCommandDisplayFormatter().FormatValuesPreview(command, maxVisibleValues);

    private static string BuildLegacyCommandValuesPreviewFallback(LegacyScenarioCommandNode command, int maxVisibleValues)
    {
        var friendly = BuildLegacyScriptFriendlyValueText(command);
        if (!string.IsNullOrWhiteSpace(friendly))
        {
            return TrimSingleLine(friendly, 132);
        }

        var values = BuildLegacyCommandPreviewValueLines(command)
            .Where(value => !string.IsNullOrWhiteSpace(value) && value != "无参数")
            .ToList();
        return values.Count == 0
            ? string.Empty
            : TrimSingleLine(JoinValuePreview(values, maxVisibleValues), 132);
    }

    private static string BuildScriptCommandTreeToolTip(ScenarioStructureRow command)
    {
        var values = BuildScenarioStructurePreviewValueLines(command);
        return $"{command.CommandIdHex} {command.CommandName}\r\n{command.OffsetHex}\r\n{BuildValueDetailBlock(values)}";
    }

    private static string BuildLegacyScriptCommandTreeToolTip(ScenarioStructureRow row, LegacyScenarioCommandNode command)
    {
        var values = BuildLegacyCommandPreviewValueLines(command);
        return $"{row.CommandIdHex} {row.CommandName}\r\n{row.OffsetHex}\r\n{BuildValueDetailBlock(values)}";
    }

    private static IReadOnlyList<string> BuildScenarioStructureValueTokens(ScenarioStructureRow row)
    {
        var words = ScenarioStructureParameterExtractor.ExtractLogicalWords(row).ToList();
        if (words.Count == 0)
        {
            return string.IsNullOrWhiteSpace(row.ParameterPreview)
                ? Array.Empty<string>()
                : new[] { TrimSingleLine(row.ParameterPreview, 96) };
        }

        return words
            .Select(value => $"{HexDisplayFormatter.FormatWord(value)}({value})")
            .ToList();
    }

    private static string BuildScenarioStructureFriendlyValueText(ScenarioStructureRow row)
    {
        var values = ScenarioStructureParameterExtractor.ExtractLogicalWords(row).ToList();
        if (values.Count == 0) return string.Empty;
        return BuildFriendlyCommandValueText(row.CommandId, row.CommandName, values);
    }

    private static string BuildLegacyScriptFriendlyValueText(LegacyScenarioCommandNode command)
    {
        if (command.CommandId == 0x76)
        {
            return command.JumpTargetOrdinal.HasValue
                ? command.JumpTargetCommandIndex.HasValue
                    ? $"跳到第 {command.JumpTargetCommandIndex.Value} 条命令"
                    : $"跳到 ord {command.JumpTargetOrdinal.Value}"
                : $"原位移 {command.OriginalJumpDisplacement ?? command.Parameters.FirstOrDefault()?.IntValue ?? 0}";
        }

        var values = FlattenLegacyScriptCommandValues(command).ToList();
        if (values.Count == 0) return string.Empty;
        return BuildFriendlyCommandValueText(command.CommandId, command.CommandName, values);
    }

    private static IReadOnlyList<int> FlattenLegacyScriptCommandValues(LegacyScenarioCommandNode command)
    {
        var values = new List<int>();
        foreach (var parameter in command.Parameters)
        {
            switch (parameter.Kind)
            {
                case LegacyScenarioParameterKind.Text:
                    break;
                case LegacyScenarioParameterKind.VariableArray:
                    values.AddRange(parameter.Values);
                    break;
                default:
                    values.Add(parameter.IntValue);
                    break;
            }
        }

        return values;
    }

    private static string BuildFriendlyCommandValueText(int commandId, string commandName, IReadOnlyList<int> values)
    {
        if (values.Count == 0) return string.Empty;
        return commandId switch
        {
            0x02 => BuildLegacyInternalInfoText(values[0]),
            0x08 => values.Count >= 1 ? (values[0] == 0 ? "false" : "true") : string.Empty,
            0x09 => values.Count >= 1 ? values[0].ToString(CultureInfo.InvariantCulture) : string.Empty,
            0x76 => values.Count >= 1 ? $"目标 {values[0]}" : string.Empty,
            0x77 => BuildVariableOperationFriendlyText(values),
            0x78 => BuildIntegerVariableAssignmentFriendlyText(values),
            0x79 => BuildVariableTestFriendlyText(values),
            0x17 or 0x18 or 0x27 or 0x2A or 0x33 or 0x37 or 0x3A or 0x46 or 0x47 or 0x72
                => string.Join(" ", values.Take(6).Select(value => value.ToString(CultureInfo.InvariantCulture))),
            0x23 or 0x24
                => string.Join(" ", values.Take(4).Select(value => value.ToString(CultureInfo.InvariantCulture))),
            0x30
                => BuildUnitCoordinateFriendlyText(values),
            0x32
                => BuildUnitMovementFriendlyText(values),
            _ when commandName.Contains("背景", StringComparison.Ordinal)
                => values[0].ToString(CultureInfo.InvariantCulture),
            _ => string.Empty
        };
    }

    private static string BuildVariableOperationFriendlyText(IReadOnlyList<int> values)
    {
        if (values.Count < 5) return string.Join(" ", values.Select(FormatScriptNumber));
        return $"{DecodeVariableKind(values[0])} {FormatScriptNumber(values[1])} {DecodeVariableOperation(values[2])} {DecodeVariableSourceKind(values[3])} {FormatVariableSourceValue(values[3], values[4])}";
    }

    private static string BuildIntegerVariableAssignmentFriendlyText(IReadOnlyList<int> values)
    {
        if (values.Count < 4) return string.Join(" ", values.Select(FormatScriptNumber));
        var direction = values[1] == 0 ? "<==" : "==>";
        return $"{FormatScriptNumber(values[0])} {direction} {FormatActorOrVariableReference(values[2])} {DecodeAllCondition(values[3])}";
    }

    private static string BuildVariableTestFriendlyText(IReadOnlyList<int> values)
    {
        if (values.Count < 5) return string.Join(" ", values.Select(FormatScriptNumber));
        return $"{DecodeVariableSourceKind(values[0])} {FormatVariableSourceValue(values[0], values[1])} {DecodeCompare2(values[2])} {DecodeVariableSourceKind(values[3])} {FormatVariableSourceValue(values[3], values[4])}";
    }

    private static IReadOnlyList<int> GetLegacyCommandScalarValues(LegacyScenarioCommandNode command)
        => command.Parameters
            .Select(parameter => parameter.IntValue)
            .ToList();

    private static string BuildVariableOperationParameterValueText(LegacyScenarioCommandNode command, LegacyScenarioCommandParameter parameter)
    {
        var values = GetLegacyCommandScalarValues(command);
        var value = values.ElementAtOrDefault(parameter.Index);
        return parameter.Index switch
        {
            0 => DecodeVariableKind(value),
            1 => FormatScriptNumber(value),
            2 => DecodeVariableOperation(value),
            3 => DecodeVariableSourceKind(value),
            4 => values.Count >= 4
                ? FormatVariableSourceValue(values[3], value)
                : FormatScriptNumber(value),
            _ => FormatLegacyScriptParameterReadableValue(command, parameter)
        };
    }

    private static string BuildIntegerVariableAssignmentParameterValueText(LegacyScenarioCommandNode command, LegacyScenarioCommandParameter parameter)
    {
        var values = GetLegacyCommandScalarValues(command);
        var value = values.ElementAtOrDefault(parameter.Index);
        return parameter.Index switch
        {
            0 => FormatScriptNumber(value),
            1 => value == 0 ? "<==" : "==>",
            2 => FormatActorOrVariableReference(value),
            3 => DecodeAllCondition(value),
            _ => FormatLegacyScriptParameterReadableValue(command, parameter)
        };
    }

    private static string BuildVariableTestParameterValueText(LegacyScenarioCommandNode command, LegacyScenarioCommandParameter parameter)
    {
        var values = GetLegacyCommandScalarValues(command);
        var value = values.ElementAtOrDefault(parameter.Index);
        return parameter.Index switch
        {
            0 => DecodeVariableSourceKind(value),
            1 => values.Count >= 1
                ? FormatVariableSourceValue(values[0], value)
                : FormatScriptNumber(value),
            2 => DecodeCompare2(value),
            3 => DecodeVariableSourceKind(value),
            4 => values.Count >= 4
                ? FormatVariableSourceValue(values[3], value)
                : FormatScriptNumber(value),
            _ => FormatLegacyScriptParameterReadableValue(command, parameter)
        };
    }

    private static string DecodeVariableKind(int value)
        => value switch
        {
            0 => "指针变量(*p)",
            1 => "指针变量(p)",
            2 => "整型变量",
            _ => $"变量类型{value}"
        };

    private static string DecodeVariableSourceKind(int value)
        => value switch
        {
            0 => "常数",
            1 => "指针变量(*p)",
            2 => "指针变量(p)",
            3 => "指针变量(&p)",
            4 => "整型变量(a)",
            5 => "整型变量(&a)",
            _ => $"值类型{value}"
        };

    private static string DecodeVariableOperation(int value)
        => value switch
        {
            0 => "+=",
            1 => "-=",
            2 => "=",
            3 => "*=",
            4 => "/=",
            5 => "%=",
            6 => "M=",
            _ => $"运算{value}"
        };

    private static string DecodeCompare2(int value)
        => value switch
        {
            0 => "==",
            1 => ">=",
            2 => "<",
            3 => "!=",
            _ => $"比较{value}"
        };

    private static string DecodeAllCondition(int value)
    {
        var names = new[]
        {
            "R形象", "头像", "攻击", "防御", "精神", "爆发", "士气", "HP", "MP", "武力", "统率", "智力", "敏捷", "运气",
            "出战场数", "撤退场数", "我军标识", "兵种", "人物等级", "人物经验值", "武器", "武器等级", "武器经验值",
            "防具", "防具等级", "防具经验值", "辅助", "战场特殊形象", "战场编号", "战场横坐标", "战场纵坐标",
            "战场行动标识", "战场人物朝向", "HpCur", "MpCur", "战场人物攻击状态", "战场人物防御状态",
            "战场人物精神状态", "战场人物爆发状态", "战场人物士气状态", "战场人物移动状态", "战场人物健康状态"
        };
        return value >= 0 && value < names.Length ? names[value] : $"属性{value}";
    }

    private static string FormatVariableSourceValue(int kind, int value)
        => kind == 0 ? FormatScriptNumber(value) : FormatScriptNumber(value);

    private static string FormatActorOrVariableReference(int value)
    {
        if (value == -1) return "无";
        if (value <= -2) return ScriptVariableValueResolver.FormatPerson2Reference(value);
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatScriptNumber(int value)
        => value >= 0x400000
            ? HexDisplayFormatter.Format(value)
            : value.ToString(CultureInfo.InvariantCulture);

    private static string BuildLegacyInternalInfoText(int value)
        => value switch
        {
            0 => "剧情上",
            1 => "剧情下",
            _ => value.ToString(CultureInfo.InvariantCulture)
        };

    private static string BuildUnitCoordinateFriendlyText(IReadOnlyList<int> values)
    {
        if (values.Count < 3) return string.Join(" ", values.Select(value => value.ToString(CultureInfo.InvariantCulture)));
        return values.Count >= 5
            ? $"{ScriptVariableValueResolver.FormatPerson2Reference(values[0])} ({values[1]},{values[2]}) {values[3]} {values[4]}"
            : $"{ScriptVariableValueResolver.FormatPerson2Reference(values[0])} ({values[1]},{values[2]})";
    }

    private static string BuildUnitMovementFriendlyText(IReadOnlyList<int> values)
    {
        if (values.Count < 5) return string.Join(" ", values.Select(value => value.ToString(CultureInfo.InvariantCulture)));
        var target = values[0] == 1
            ? "战场编号 " + values[2].ToString(CultureInfo.InvariantCulture)
            : ScriptVariableValueResolver.FormatPerson2Reference(values[1]);
        return values.Count >= 6
            ? $"{target} ({values[3]},{values[4]}) {values[5]}"
            : $"{target} ({values[3]},{values[4]})";
    }

    private static Color GetLegacyScriptCommandColor(LegacyScenarioCommandNode command)
        => GetScriptCommandColor(command.CommandId);

    private static Color GetScriptCommandColor(int commandId)
        => commandId switch
        {
            0x00 => Color.Blue,
            0x01 => Color.Red,
            0x02 => Color.DarkCyan,
            0x0C or 0x0D => Color.Black,
            _ => SystemColors.WindowText
        };

    private static IReadOnlyList<string> BuildLegacyCommandValueTokens(LegacyScenarioCommandNode command, bool includeFullText)
    {
        var tokens = new List<string>();
        foreach (var parameter in command.Parameters)
        {
            switch (parameter.Kind)
            {
                case LegacyScenarioParameterKind.Text:
                {
                    var content = includeFullText ? parameter.Text : TrimSingleLine(parameter.Text, 24);
                    tokens.Add($"槽{parameter.Index + 1}: \"{content}\"");
                    break;
                }
                case LegacyScenarioParameterKind.VariableArray:
                {
                    if (parameter.Values.Count == 0)
                    {
                        tokens.Add($"槽{parameter.Index + 1}: []");
                        break;
                    }

                    for (var valueIndex = 0; valueIndex < parameter.Values.Count; valueIndex++)
                    {
                        var value = parameter.Values[valueIndex];
                        tokens.Add($"槽{parameter.Index + 1}[{valueIndex}]: {HexDisplayFormatter.FormatWord(unchecked((ushort)value))}({value})");
                    }
                    break;
                }
                case LegacyScenarioParameterKind.Dword32:
                {
                    var token = $"槽{parameter.Index + 1}: {HexDisplayFormatter.FormatDword(unchecked((uint)parameter.IntValue))}({parameter.IntValue})";
                    if (command.CommandId == 0x76 && command.JumpTargetOrdinal.HasValue)
                    {
                        token += $" -> ord {command.JumpTargetOrdinal.Value} / Command {command.JumpTargetCommandIndex}";
                    }
                    tokens.Add(token);
                    break;
                }
                default:
                    tokens.Add($"槽{parameter.Index + 1}: {HexDisplayFormatter.FormatWord(unchecked((ushort)parameter.IntValue))}({parameter.IntValue})");
                    break;
            }
        }

        return tokens;
    }

    private static IReadOnlyList<string> BuildScenarioStructurePreviewValueLines(ScenarioStructureRow row)
    {
        var values = ScenarioStructureParameterExtractor.ExtractLogicalWords(row).ToList();
        if (values.Count == 0)
        {
            return string.IsNullOrWhiteSpace(row.ParameterPreview)
                ? Array.Empty<string>()
                : new[] { TrimSingleLine(row.ParameterPreview, 96) };
        }

        var friendly = BuildFriendlyCommandValueText(row.CommandId, row.CommandName, values);
        if (!string.IsNullOrWhiteSpace(friendly))
        {
            return new[] { friendly };
        }

        return values
            .Take(10)
            .Select(value => value.ToString(CultureInfo.InvariantCulture))
            .ToList();
    }

    private static IReadOnlyList<string> BuildLegacyCommandPreviewValueLines(LegacyScenarioCommandNode command)
    {
        if (command.CommandId == 0x76)
        {
            return new[] { BuildLegacyScriptFriendlyValueText(command) };
        }

        var values = new List<string>();
        foreach (var parameter in command.Parameters)
        {
            switch (parameter.Kind)
            {
                case LegacyScenarioParameterKind.Text:
                    values.Add(TrimSingleLine(parameter.Text, 96));
                    break;
                case LegacyScenarioParameterKind.VariableArray:
                    values.Add(parameter.Values.Count == 0
                        ? "[]"
                        : string.Join(" ", parameter.Values.Take(10).Select(value => value.ToString(CultureInfo.InvariantCulture))));
                    break;
                default:
                    values.Add(parameter.IntValue.ToString(CultureInfo.InvariantCulture));
                    break;
            }
        }

        if (values.Count == 0)
        {
            return Array.Empty<string>();
        }

        var friendly = BuildFriendlyCommandValueText(command.CommandId, command.CommandName, FlattenLegacyScriptCommandValues(command));
        return string.IsNullOrWhiteSpace(friendly)
            ? values
            : new[] { friendly };
    }

    private static string BuildValueDetailBlock(IReadOnlyList<string> values)
        => values.Count == 0
            ? "（无）"
            : string.Join("\r\n", values);

    private static string BuildScriptTextNodeText(ScenarioTextEntry text)
        => $"#{text.Index:000} {text.Kind} {text.OffsetHex}｜{BuildTextSummary(text)}";

    private static string BuildScriptTextTreeToolTip(ScenarioTextEntry text)
        => $"文本：{text.Kind}\r\n偏移：{text.OffsetHex}\r\n容量：{text.ByteLength}B\r\n当前内容：{text.Text}\r\n中文注释：{text.Annotation}";

    private static string AppendValueSummary(string label, IReadOnlyList<string> values, int maxVisibleValues)
    {
        if (values.Count == 0)
        {
            return label + "｜无参数";
        }

        var preview = JoinValuePreview(values, maxVisibleValues);
        return string.IsNullOrWhiteSpace(preview)
            ? label
            : $"{label}｜{TrimSingleLine(preview, 132)}";
    }

    private static string JoinValuePreview(IReadOnlyList<string> values, int maxVisibleValues)
    {
        if (values.Count == 0)
        {
            return string.Empty;
        }

        var visible = values.Take(Math.Max(1, maxVisibleValues)).ToList();
        var suffix = values.Count > visible.Count ? $" +{values.Count - visible.Count}" : string.Empty;
        return string.Join(", ", visible) + suffix;
    }

    private static string TrimSingleLine(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "（空）";
        }

        var normalized = text
            .Replace("\r\n", " / ", StringComparison.Ordinal)
            .Replace("\n", " / ", StringComparison.Ordinal)
            .Replace("\r", " / ", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..Math.Max(1, maxLength - 1)] + "…";
    }

    private static IEnumerable<TreeNode> EnumerateScriptTreeNodes(TreeNode root)
    {
        yield return root;
        foreach (TreeNode child in root.Nodes)
        {
            foreach (var nested in EnumerateScriptTreeNodes(child))
            {
                yield return nested;
            }
        }
    }

    private static void AppendScriptSectionTextNode(
        TreeNode sectionNode,
        ScenarioStructureRow section,
        IReadOnlyDictionary<(int SceneIndex, int SectionIndex), IReadOnlyList<ScenarioTextEntry>> sectionTextAssignments)
    {
        if (!sectionTextAssignments.TryGetValue((section.SceneIndex, section.SectionIndex), out var sectionTexts)
            || sectionTexts.Count == 0)
        {
            return;
        }

        var textRootNode = new TreeNode(BuildTextGroupNodeText("Section文本线索", sectionTexts)) { Tag = section };
        foreach (var text in sectionTexts)
        {
            textRootNode.Nodes.Add(CreateScriptTextTreeNode(text));
        }

        sectionNode.Nodes.Add(textRootNode);
    }

    private IReadOnlyDictionary<(int SceneIndex, int SectionIndex), IReadOnlyList<ScenarioTextEntry>> BuildScriptSectionTextAssignments(
        ScenarioStructureProbeResult structure,
        IReadOnlyList<ScenarioTextEntry> texts)
    {
        var contexts = structure.Rows
            .Where(row => row.NodeType == "Section候选")
            .OrderBy(row => row.SceneIndex)
            .ThenBy(row => row.SectionIndex)
            .Select(section => (
                Section: section,
                Rows: structure.Rows
                    .Where(row => row.NodeType == "Command候选"
                        && row.SceneIndex == section.SceneIndex
                        && row.SectionIndex == section.SectionIndex)
                    .OrderBy(row => row.CommandIndex)
                    .ToList()))
            .Where(context => context.Rows.Count > 0)
            .ToList();

        var assignments = contexts.ToDictionaryFirstByKey(
            context => (context.Section.SceneIndex, context.Section.SectionIndex),
            _ => (IReadOnlyList<ScenarioTextEntry>)Array.Empty<ScenarioTextEntry>());
        if (contexts.Count == 0 || texts.Count == 0)
        {
            return assignments;
        }

        var buckets = contexts.ToDictionaryFirstByKey(
            context => (context.Section.SceneIndex, context.Section.SectionIndex),
            _ => new List<ScenarioTextEntry>());

        foreach (var text in texts)
        {
            var bestMatch = contexts
                .Select(context => new
                {
                    context.Section.SceneIndex,
                    context.Section.SectionIndex,
                    Score = GetScriptTextContextScore(context.Rows, text, strictContext: true),
                    Distance = GetNearestScriptCommandDistance(context.Rows, text)
                })
                .Where(candidate => candidate.Score >= 60)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Distance)
                .ThenBy(candidate => candidate.SceneIndex)
                .ThenBy(candidate => candidate.SectionIndex)
                .FirstOrDefault();
            if (bestMatch == null)
            {
                continue;
            }

            buckets[(bestMatch.SceneIndex, bestMatch.SectionIndex)].Add(text);
        }

        return buckets.ToDictionaryFirstByKey(
            pair => pair.Key,
            pair => (IReadOnlyList<ScenarioTextEntry>)pair.Value
                .OrderBy(text => text.Offset)
                .ThenBy(text => text.Index)
                .ToList());
    }

    private IReadOnlyDictionary<int, IReadOnlyList<ScenarioTextEntry>> BuildScriptSceneFallbackTextAssignments(
        ScenarioStructureProbeResult structure,
        IReadOnlyList<ScenarioTextEntry> texts,
        ISet<int> attachedOffsets)
    {
        var contexts = structure.Rows
            .Where(row => row.NodeType == "Scene候选")
            .OrderBy(row => row.SceneIndex)
            .Select(scene => (
                Scene: scene,
                Rows: structure.Rows
                    .Where(row => row.NodeType == "Command候选" && row.SceneIndex == scene.SceneIndex)
                    .OrderBy(row => row.SectionIndex)
                    .ThenBy(row => row.CommandIndex)
                    .ToList()))
            .Where(context => context.Rows.Count > 0)
            .ToList();

        var assignments = contexts.ToDictionaryFirstByKey(
            context => context.Scene.SceneIndex,
            _ => (IReadOnlyList<ScenarioTextEntry>)Array.Empty<ScenarioTextEntry>());
        if (contexts.Count == 0 || texts.Count == 0)
        {
            return assignments;
        }

        var buckets = contexts.ToDictionaryFirstByKey(
            context => context.Scene.SceneIndex,
            _ => new List<ScenarioTextEntry>());

        foreach (var text in texts.Where(candidate => !attachedOffsets.Contains(candidate.Offset)))
        {
            var bestMatch = contexts
                .Select(context => new
                {
                    context.Scene.SceneIndex,
                    Score = GetScriptSceneFallbackScore(context.Rows, text),
                    Distance = GetNearestScriptCommandDistance(context.Rows, text)
                })
                .Where(candidate => candidate.Score >= 40)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Distance)
                .ThenBy(candidate => candidate.SceneIndex)
                .FirstOrDefault();
            if (bestMatch == null)
            {
                continue;
            }

            buckets[bestMatch.SceneIndex].Add(text);
            attachedOffsets.Add(text.Offset);
        }

        return buckets.ToDictionaryFirstByKey(
            pair => pair.Key,
            pair => (IReadOnlyList<ScenarioTextEntry>)pair.Value
                .OrderBy(text => text.Offset)
                .ThenBy(text => text.Index)
                .ToList());
    }

    private static int GetScriptSceneFallbackScore(IReadOnlyList<ScenarioStructureRow> rows, ScenarioTextEntry text)
    {
        if (rows.Count == 0 || !TryGetScriptOffsetRange(rows, out var minOffset, out var maxOffset))
        {
            return 0;
        }

        var nearestDistance = GetNearestScriptCommandDistance(rows, text);
        if (nearestDistance == int.MaxValue)
        {
            return 0;
        }

        var score = rows.Any(IsTextRelatedCommand) ? 16 : 8;
        if (text.Offset >= minOffset - 0x80 && text.Offset <= maxOffset + 0x180)
        {
            score += 42;
        }
        else if (text.Offset >= minOffset - 0x200 && text.Offset <= maxOffset + 0x280)
        {
            score += 28;
        }
        else if (nearestDistance <= 0x400)
        {
            score += 12;
        }

        if (text.Kind.Contains("标题", StringComparison.Ordinal) || text.Kind.Contains("场所", StringComparison.Ordinal))
        {
            score += 8;
        }

        if (text.Kind.Contains("胜败条件", StringComparison.Ordinal))
        {
            score += 8;
        }

        score += nearestDistance switch
        {
            <= 0x80 => 16,
            <= 0x120 => 12,
            <= 0x200 => 8,
            <= 0x300 => 4,
            _ => 0
        };

        return score;
    }

    private IReadOnlyList<ScenarioTextEntry> GetScriptTextsForRows(
        IReadOnlyList<ScenarioStructureRow> rows,
        IReadOnlyList<ScenarioTextEntry> texts,
        bool allowFallback,
        bool strictContext,
        int requiredScore = 56,
        int maxResults = 20)
    {
        if (rows.Count == 0 || texts.Count == 0)
        {
            return Array.Empty<ScenarioTextEntry>();
        }

        var matched = texts
            .Select(text => new
            {
                Entry = text,
                Score = GetScriptTextContextScore(rows, text, strictContext),
                Distance = GetNearestScriptCommandDistance(rows, text)
            })
            .Where(candidate => candidate.Score >= requiredScore)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Entry.Offset)
            .Select(candidate => candidate.Entry)
            .DistinctBy(text => text.Offset)
            .Take(maxResults)
            .ToList();
        if (matched.Count > 0 || !allowFallback)
        {
            return matched;
        }

        return texts
            .OrderBy(text => GetNearestScriptCommandDistance(rows, text))
            .ThenBy(text => text.Offset)
            .Take(Math.Min(maxResults, texts.Count))
            .ToList();
    }

    private static int GetScriptTextContextScore(
        IReadOnlyList<ScenarioStructureRow> rows,
        ScenarioTextEntry text,
        bool strictContext)
    {
        if (rows.Count == 0)
        {
            return 0;
        }

        var bestRelationScore = rows
            .Select(row => GetScriptTextRelationScore(row, text))
            .DefaultIfEmpty(0)
            .Max();
        if (bestRelationScore <= 0)
        {
            return 0;
        }

        if (!TryGetScriptOffsetRange(rows, out var minOffset, out var maxOffset))
        {
            return bestRelationScore;
        }

        if (text.Offset >= minOffset - 0x80 && text.Offset <= maxOffset + 0x120)
        {
            bestRelationScore += 12;
        }
        else if (text.Offset >= minOffset - 0x180 && text.Offset <= maxOffset + 0x200)
        {
            bestRelationScore += strictContext ? 4 : 8;
        }
        else if (strictContext)
        {
            bestRelationScore -= 24;
        }

        return Math.Max(bestRelationScore, 0);
    }

    private static int GetScriptTextRelationScore(ScenarioStructureRow row, ScenarioTextEntry text)
    {
        if (!IsTextRelatedCommand(row) || !TryParseHexOffset(row.OffsetHex, out var offset))
        {
            return 0;
        }

        var distance = Math.Abs(text.Offset - offset);
        var score = 20 + distance switch
        {
            <= 0x40 => 78,
            <= 0x80 => 64,
            <= 0x120 => 48,
            <= 0x200 => 34,
            <= 0x300 => 18,
            <= 0x400 => 8,
            _ => 0
        };

        if (row.ReferenceHint.Contains("文本线索", StringComparison.Ordinal))
        {
            score += 8;
        }

        if ((row.CommandName.Contains("章名", StringComparison.Ordinal)
                || row.CommandName.Contains("标题", StringComparison.Ordinal)
                || row.CommandName.Contains("事件名称", StringComparison.Ordinal))
            && (text.Kind.Contains("标题", StringComparison.Ordinal) || text.Kind.Contains("场所", StringComparison.Ordinal)))
        {
            score += 44;
        }

        if (row.CommandName.Contains("场所", StringComparison.Ordinal) && text.Kind.Contains("场所", StringComparison.Ordinal))
        {
            score += 44;
        }

        if ((row.CommandName.Contains("胜利", StringComparison.Ordinal)
                || row.CommandName.Contains("失败", StringComparison.Ordinal)
                || row.CommandName.Contains("条件", StringComparison.Ordinal))
            && text.Kind.Contains("胜败条件", StringComparison.Ordinal))
        {
            score += 44;
        }

        if (row.CommandName.Contains("对话", StringComparison.Ordinal) && (text.HasNewLines || text.Text.Length >= 12))
        {
            score += 18;
        }

        if (row.CommandName.Contains("旁白", StringComparison.Ordinal) && (text.HasNewLines || text.Text.Length >= 16))
        {
            score += 18;
        }

        if (row.CommandName.Contains("信息", StringComparison.Ordinal)
            && (text.Kind.Contains("短文本", StringComparison.Ordinal)
                || text.Kind.Contains("标题", StringComparison.Ordinal)
                || text.Text.Length <= 32))
        {
            score += 12;
        }

        if ((row.CommandName.Contains("文字", StringComparison.Ordinal)
                || row.CommandName.Contains("剧情", StringComparison.Ordinal))
            && text.Text.Length >= 8)
        {
            score += 10;
        }

        return score;
    }

    private static int GetNearestScriptCommandDistance(IReadOnlyList<ScenarioStructureRow> rows, ScenarioTextEntry text)
    {
        var nearestOffset = rows
            .Select(row => TryParseHexOffset(row.OffsetHex, out var value) ? value : (int?)null)
            .Where(offset => offset.HasValue)
            .Select(offset => Math.Abs(text.Offset - offset!.Value))
            .DefaultIfEmpty(int.MaxValue)
            .Min();
        return nearestOffset;
    }

    private static bool TryGetScriptOffsetRange(IReadOnlyList<ScenarioStructureRow> rows, out int minOffset, out int maxOffset)
    {
        var offsets = rows
            .Select(row => TryParseHexOffset(row.OffsetHex, out var value) ? value : (int?)null)
            .Where(offset => offset.HasValue)
            .Select(offset => offset!.Value)
            .OrderBy(offset => offset)
            .ToList();
        if (offsets.Count == 0)
        {
            minOffset = 0;
            maxOffset = 0;
            return false;
        }

        minOffset = offsets[0];
        maxOffset = offsets[^1];
        return true;
    }

    private static bool IsLikelySubEventCarrier(int commandId)
        => commandId > 0
           && commandId != 0x0C
           && commandId != 0x0D;

    private void BindScriptCommandRows(IReadOnlyList<ScenarioStructureRow> rows, ScenarioStructureRow? preferredRow = null)
    {
        _scriptCommandGrid.DataSource = new BindingList<ScenarioStructureRow>(BuildScriptCommandGridRows(rows, preferredRow));
        ConfigureScriptCommandGrid();
    }

    private void SuppressScriptSelectionEvents(Action action)
    {
        var restoreBindingState = _bindingScriptDocument;
        _bindingScriptDocument = true;
        try
        {
            action();
        }
        finally
        {
            _bindingScriptDocument = restoreBindingState;
        }
    }

    private static List<ScenarioStructureRow> BuildScriptCommandGridRows(IReadOnlyList<ScenarioStructureRow> rows, ScenarioStructureRow? preferredRow)
    {
        var materialized = rows as List<ScenarioStructureRow> ?? rows.ToList();
        if (materialized.Count <= ScriptCommandGridMaxRows)
        {
            return materialized;
        }

        var preferredIndex = preferredRow == null
            ? -1
            : materialized.FindIndex(row => IsSameScriptCommand(row, preferredRow));
        if (preferredIndex < 0)
        {
            return materialized.Take(ScriptCommandGridMaxRows).ToList();
        }

        var start = Math.Clamp(preferredIndex - ScriptCommandGridMaxRows / 2, 0, materialized.Count - ScriptCommandGridMaxRows);
        return materialized.Skip(start).Take(ScriptCommandGridMaxRows).ToList();
    }

    private void ConfigureScriptCommandGrid()
    {
        foreach (DataGridViewColumn column in _scriptCommandGrid.Columns)
        {
            column.HeaderText = column.DataPropertyName switch
            {
                nameof(ScenarioStructureRow.SceneIndex) => "Scene",
                nameof(ScenarioStructureRow.SectionIndex) => "Section",
                nameof(ScenarioStructureRow.CommandIndex) => "序号",
                nameof(ScenarioStructureRow.OffsetHex) => "偏移",
                nameof(ScenarioStructureRow.CommandIdHex) => "命令号",
                nameof(ScenarioStructureRow.CommandName) => "命令名",
                nameof(ScenarioStructureRow.ParameterPreview) => "逻辑参数",
                nameof(ScenarioStructureRow.CommandTemplateHint) => "参数模板",
                nameof(ScenarioStructureRow.ReferenceHint) => "引用候选",
                nameof(ScenarioStructureRow.Annotation) => "中文注释",
                _ => column.HeaderText
            };
            if (column.DataPropertyName is nameof(ScenarioStructureRow.Level)
                or nameof(ScenarioStructureRow.NodeType)
                or nameof(ScenarioStructureRow.Index)
                or nameof(ScenarioStructureRow.HasCommandTemplate)
                or nameof(ScenarioStructureRow.Confidence)
                or nameof(ScenarioStructureRow.RawContextWordsHex)
                or nameof(ScenarioStructureRow.LegacyParameterLayout)
                or nameof(ScenarioStructureRow.StartsBodyBlock)
                or nameof(ScenarioStructureRow.OpensSubEventBlock)
                or nameof(ScenarioStructureRow.EndsSubEventBlock))
            {
                column.Visible = false;
            }
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            if (column.DataPropertyName is nameof(ScenarioStructureRow.ParameterPreview)
                or nameof(ScenarioStructureRow.CommandTemplateHint)
                or nameof(ScenarioStructureRow.ReferenceHint)
                or nameof(ScenarioStructureRow.Annotation))
            {
                column.Width = 220;
            }
            else if (column.DataPropertyName is nameof(ScenarioStructureRow.CommandName))
            {
                column.Width = 180;
            }
        }
    }

    private void BindScriptParameterRows(IReadOnlyList<ScenarioCommandParameterRow> rows)
    {
        ClearLegacyScriptParameterEditor();
        _scriptParameterGrid.DataSource = new BindingList<ScenarioCommandParameterRow>(rows.ToList());
        foreach (DataGridViewColumn column in _scriptParameterGrid.Columns)
        {
            column.HeaderText = column.DataPropertyName switch
            {
                nameof(ScenarioCommandParameterRow.Index) => "序号",
                nameof(ScenarioCommandParameterRow.SlotName) => "参数名",
                nameof(ScenarioCommandParameterRow.Kind) => "类型",
                nameof(ScenarioCommandParameterRow.RawHex) => "十六进制",
                nameof(ScenarioCommandParameterRow.DecimalValue) => "十进制",
                nameof(ScenarioCommandParameterRow.DecodedValue) => "当前值/解释",
                nameof(ScenarioCommandParameterRow.Meaning) => "含义",
                nameof(ScenarioCommandParameterRow.Risk) => "风险/边界",
                nameof(ScenarioCommandParameterRow.FromTemplate) => "模板",
                nameof(ScenarioCommandParameterRow.Annotation) => "中文注释",
                _ => column.HeaderText
            };
            column.ToolTipText = column.DataPropertyName switch
            {
                nameof(ScenarioCommandParameterRow.DecodedValue) => "当前参数值及可读解释。",
                nameof(ScenarioCommandParameterRow.Risk) => "完整保存前仍会自动备份并复读校验。",
                nameof(ScenarioCommandParameterRow.Annotation) => "面向创作者的参数中文注释。",
                _ => column.ToolTipText
            };
            if (column.DataPropertyName is nameof(ScenarioCommandParameterRow.Index)
                or nameof(ScenarioCommandParameterRow.Kind)
                or nameof(ScenarioCommandParameterRow.RawHex)
                or nameof(ScenarioCommandParameterRow.DecimalValue)
                or nameof(ScenarioCommandParameterRow.Risk)
                or nameof(ScenarioCommandParameterRow.FromTemplate)
                or nameof(ScenarioCommandParameterRow.Annotation))
            {
                column.Visible = false;
            }
            if (column.DataPropertyName is nameof(ScenarioCommandParameterRow.DecodedValue))
            {
                column.Width = 280;
            }
            else if (column.DataPropertyName is nameof(ScenarioCommandParameterRow.Meaning))
            {
                column.Width = 220;
            }
            else if (column.DataPropertyName is nameof(ScenarioCommandParameterRow.SlotName))
            {
                column.Width = 150;
            }
        }

        if (_scriptParameterGrid.Rows.Count > 0 && _scriptParameterGrid.CurrentCell == null)
        {
            var firstVisibleCell = _scriptParameterGrid.Rows[0].Cells
                .Cast<DataGridViewCell>()
                .FirstOrDefault(cell => cell.Visible);
            if (firstVisibleCell != null)
            {
                _scriptParameterGrid.CurrentCell = firstVisibleCell;
                _scriptParameterGrid.Rows[0].Selected = true;
            }
        }

        if (!_bindingScriptDocument)
        {
            ShowSelectedLegacyScriptParameter();
        }
    }

    private void ShowSelectedLegacyScriptParameter()
    {
        if (_bindingScriptDocument) return;
        if (!TryGetSelectedLegacyScriptParameter(out var command, out var parameter, out _))
        {
            ClearLegacyScriptParameterEditor();
            return;
        }

        _scriptParameterValueBox.Text = FormatLegacyScriptParameterEditorValue(command, parameter);

        if (CanEditLegacyScriptParameter(command, parameter, out var reason))
        {
            _ = reason;
            _scriptParameterValueBox.Enabled = false;
            _applyScriptParameterValueButton.Enabled = TryGetSelectedLegacyItemData(out var itemData) && LegacyCommandEditDispatcher.CanEdit(itemData.Id);
            _editScriptParametersButton.Enabled = _applyScriptParameterValueButton.Enabled;
            SetStatus($"剧本参数参考：槽 {parameter.Index}，正式修改请使用旧版 ItemData Dialog。");
        }
        else
        {
            _scriptParameterValueBox.Enabled = false;
            _applyScriptParameterValueButton.Enabled = false;
            _editScriptParametersButton.Enabled = CanEditLegacyScriptCommandParameters(command, out _);
            SetStatus("剧本参数：" + reason);
        }
    }

    private void ClearLegacyScriptParameterEditor()
    {
        _scriptParameterValueBox.Clear();
        _scriptParameterValueBox.Enabled = false;
        _applyScriptParameterValueButton.Enabled = false;
        _editScriptParametersButton.Enabled = false;
        _applyScriptInlineDialogButton.Enabled = false;
        _resetScriptInlineDialogButton.Enabled = false;
    }

    private void LoadInlineLegacyScriptDialogForSelection()
    {
        _applyScriptInlineDialogButton.Enabled = false;
        _resetScriptInlineDialogButton.Enabled = false;

        if (!TryGetSelectedLegacyItemData(out var itemData) || itemData.Command == null)
        {
            _scriptInlineDialogHost.ClearDialog("请选择左侧指令。");
            return;
        }

        if (!LegacyCommandEditDispatcher.CanEdit(itemData.Id))
        {
            _scriptInlineDialogHost.ClearDialog("该命令在旧版源码中没有修改窗口。");
            return;
        }

        var dialogName = LegacyCommandEditDispatcher.GetDialogName(itemData.Id);
        if (!LegacyMfcDialogCatalog.TryGet(dialogName, out var spec))
        {
            _scriptInlineDialogHost.ClearDialog("该旧版 Dialog 尚未迁移为 MFC 控件。");
            return;
        }

        var dialogDataSources = LegacyMfcDialogDataSources.Create(_project, _tables);
        var precedingSameCommandCount = CountPrecedingSameLegacyCommands(itemData.Command);
        _scriptInlineDialogHost.LoadDialog(
            itemData,
            spec,
            dialogDataSources,
            _currentLegacyScriptDocument?.CommandCount ?? 0,
            precedingSameCommandCount,
            includeDialogButtons: false);
        _applyScriptInlineDialogButton.Enabled = true;
        _resetScriptInlineDialogButton.Enabled = true;
    }

    private void ApplyInlineLegacyScriptDialog()
    {
        if (!TryGetSelectedLegacyItemData(out var itemData) || itemData.Command == null)
        {
            MessageBox.Show(this, "请先在左侧旧版树中选择一条命令。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var oldSummary = BuildLegacyScriptParameterPreview(itemData.Command);
        var beforeCommand = CaptureLegacyItemDataCommandSnapshot(itemData);
        var beforeEdit = CaptureLegacyScenarioHistorySnapshot(LegacyScriptEditorScope.Script, _currentLegacyScriptDocument!);
        var error = _scriptInlineDialogHost.CommitToTarget();
        if (!string.IsNullOrWhiteSpace(error))
        {
            MessageBox.Show(this, error, "参数值无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        CopyLegacyItemDataToCommand(itemData);
        var newSummary = BuildLegacyScriptParameterPreview(itemData.Command);
        if (!LegacyItemDataCommandChanged(itemData, beforeCommand))
        {
            SetStatus($"旧版内嵌修改：{itemData.Command.CommandIdHex} {itemData.Command.CommandName} 未检测到改动");
            return;
        }

        PushLegacyScenarioUndoSnapshot(LegacyScriptEditorScope.Script, beforeEdit);

        if (!RefreshLegacyScriptCommandInPlace(itemData.Command))
        {
            RefreshLegacyScriptView(itemData.Command);
        }
        _saveScriptStructureButton.Enabled = true;
        SetStatus($"旧版内嵌修改：{itemData.Command.CommandIdHex} {itemData.Command.CommandName}，{oldSummary} -> {newSummary}");
    }

    private bool TryGetSelectedLegacyItemData(out LegacyScenarioItemData itemData)
        => TryGetLegacyItemDataFromScriptTreeNode(_scriptTree.SelectedNode, out itemData);

    private void EditSelectedLegacyItemDataCommand()
        => EditSelectedLegacyItemDataCommand(LegacyScriptEditorScope.Script);

    private void EditSelectedLegacyItemDataCommand(LegacyScriptEditorScope scope)
    {
        if (scope == LegacyScriptEditorScope.Battlefield)
        {
            EditSelectedBattlefieldScriptParameters();
            return;
        }

        if (scope == LegacyScriptEditorScope.RScene)
        {
            EditSelectedRSceneScriptCommand();
            return;
        }

        if (!TryGetSelectedLegacyItemData(out var itemData) || itemData.Command == null)
        {
            MessageBox.Show(this, "请先在旧版完整树中选择一条命令。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        EditSelectedLegacyItemDataCommand(itemData);
    }

    private void EditSelectedLegacyItemDataCommand(LegacyScenarioItemData itemData)
    {
        if (itemData.Command == null)
        {
            MessageBox.Show(this, "请先在旧版完整树中选择一条命令。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!LegacyCommandEditDispatcher.CanEdit(itemData.Id))
        {
            MessageBox.Show(this, "旧版源码的 OnEditModify() 没有为该命令提供修改窗口。", "该命令暂不可修改", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var oldSummary = BuildLegacyScriptParameterPreview(itemData.Command);
        var beforeCommand = CaptureLegacyItemDataCommandSnapshot(itemData);
        var commandTitle = $"{itemData.Command.CommandIdHex} {itemData.Command.CommandName} / ord {itemData.Ord}";
        var dialogDataSources = LegacyMfcDialogDataSources.Create(_project, _tables);
        var precedingSameCommandCount = CountPrecedingSameLegacyCommands(itemData.Command);
        if (ScriptCommandEditInterceptForSmoke != null)
        {
            ScriptCommandEditInterceptForSmoke(itemData.Command);
            return;
        }

        var beforeEdit = CaptureLegacyScenarioHistorySnapshot(LegacyScriptEditorScope.Script, _currentLegacyScriptDocument!);
        if (!LegacyCommandEditDispatcher.Edit(this, itemData, commandTitle, _currentLegacyScriptDocument?.CommandCount ?? 0, precedingSameCommandCount, dialogDataSources))
        {
            return;
        }

        CopyLegacyItemDataToCommand(itemData);
        var newSummary = BuildLegacyScriptParameterPreview(itemData.Command);
        if (!LegacyItemDataCommandChanged(itemData, beforeCommand))
        {
            SetStatus($"旧版修改指令：{commandTitle} 未检测到改动");
            return;
        }

        PushLegacyScenarioUndoSnapshot(LegacyScriptEditorScope.Script, beforeEdit);
        if (!RefreshLegacyScriptCommandInPlace(itemData.Command))
        {
            RefreshLegacyScriptView(itemData.Command);
        }
        _saveScriptStructureButton.Enabled = true;
        SetStatus($"旧版修改指令：{commandTitle}，{oldSummary} -> {newSummary}");
    }

    private int CountPrecedingSameLegacyCommands(LegacyScenarioCommandNode currentCommand)
        => CountPrecedingSameLegacyCommands(_currentLegacyScriptDocument, currentCommand);

    private static int CountPrecedingSameLegacyCommands(LegacyScenarioDocument? document, LegacyScenarioCommandNode currentCommand)
    {
        if (document == null) return 0;

        var previousCommands = new List<LegacyScenarioCommandNode>();
        foreach (var command in document.EnumerateCommands())
        {
            if (ReferenceEquals(command, currentCommand))
            {
                break;
            }

            previousCommands.Add(command);
        }

        var count = 0;
        for (var i = previousCommands.Count - 1; i >= 0; i--)
        {
            if (previousCommands[i].CommandId != currentCommand.CommandId) break;
            count++;
        }

        return count;
    }

    private void ApplySelectedLegacyScriptParameterValue()
    {
        if (!TryGetSelectedLegacyScriptParameter(out var command, out var parameter, out var row))
        {
            MessageBox.Show(this, "请先在参数表中选择一个旧版命令参数槽。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!CanEditLegacyScriptParameter(command, parameter, out var reason))
        {
            MessageBox.Show(this, reason, "该参数暂不开放编辑", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var beforeEdit = CaptureLegacyScenarioHistorySnapshot(LegacyScriptEditorScope.Script, _currentLegacyScriptDocument!);
        var oldValue = FormatLegacyScriptParameterEditorValue(command, parameter);
        if (!TryApplyLegacyScriptParameterValue(command, parameter, _scriptParameterValueBox.Text, out var newValue, out var error))
        {
            MessageBox.Show(this, error, "参数值无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _scriptParameterValueBox.Focus();
            _scriptParameterValueBox.SelectAll();
            return;
        }

        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            SetStatus($"剧本参数：槽 {parameter.Index} 未检测到改动");
            return;
        }

        PushLegacyScenarioUndoSnapshot(LegacyScriptEditorScope.Script, beforeEdit);
        if (!RefreshLegacyScriptCommandInPlace(command, row.Index))
        {
            RefreshLegacyScriptView(command, row.Index);
        }
        _saveScriptStructureButton.Enabled = true;
        SetStatus($"剧本参数：{command.CommandIdHex} {command.CommandName} 槽 {parameter.Index} {oldValue} -> {newValue}，需完整保存剧本");
    }

    private void EditSelectedLegacyScriptParameters()
    {
        if (!TryGetSelectedOrCurrentLegacyScriptCommand(out var command))
        {
            MessageBox.Show(this, "请先在旧版完整树中选择一条命令。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!CanEditLegacyScriptCommandParameters(command, out var reason))
        {
            MessageBox.Show(this, reason, "该命令暂无可编辑参数", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selectedParameterIndex = GetSelectedScriptParameterRow()?.Index;
        var dialogRows = BuildLegacyScriptParameterEditRows(command);
        using var dialog = new LegacyScriptParameterEditDialog(
            $"{command.CommandIdHex} {command.CommandName}",
            $"Scene {command.SceneIndex} / Section {command.SectionIndex} / Command {command.CommandIndex} / ord {command.CommandOrdinal}",
            dialogRows,
            selectedParameterIndex);
        ApplyAdaptiveDialogSizing(dialog, new Size(1080, 680), new Size(780, 500));

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var changes = dialog.ChangedParameters.ToList();
        if (changes.Count == 0)
        {
            SetStatus($"剧本参数：{command.CommandIdHex} {command.CommandName} 未检测到改动");
            return;
        }

        var beforeEdit = CaptureLegacyScenarioHistorySnapshot(LegacyScriptEditorScope.Script, _currentLegacyScriptDocument!);
        var snapshot = CaptureLegacyScriptParameterState(command);
        var summaries = new List<string>();
        foreach (var change in changes)
        {
            var parameter = command.Parameters.FirstOrDefault(candidate => candidate.Index == change.Index);
            if (parameter == null)
            {
                RestoreLegacyScriptParameterState(command, snapshot);
                MessageBox.Show(this, $"槽 {change.Index} 已不存在，请重新选择命令。", "参数应用失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!CanEditLegacyScriptParameter(command, parameter, out var editReason))
            {
                RestoreLegacyScriptParameterState(command, snapshot);
                MessageBox.Show(this, $"槽 {change.Index}：{editReason}", "参数应用失败", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var oldValue = FormatLegacyScriptParameterEditorValue(command, parameter);
            if (!TryApplyLegacyScriptParameterValue(command, parameter, change.Value, out var newValue, out var error))
            {
                RestoreLegacyScriptParameterState(command, snapshot);
                MessageBox.Show(this, $"槽 {change.Index} 的值无效：\r\n{error}", "参数值无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
            {
                summaries.Add($"槽 {change.Index}: {oldValue} -> {newValue}");
            }
        }

        if (summaries.Count == 0)
        {
            RestoreLegacyScriptParameterState(command, snapshot);
            SetStatus($"剧本参数：{command.CommandIdHex} {command.CommandName} 未检测到改动");
            return;
        }

        PushLegacyScenarioUndoSnapshot(LegacyScriptEditorScope.Script, beforeEdit);
        var detailSuffix = "\r\n\r\n本次参数修改：\r\n" + string.Join("\r\n", summaries.Take(12));
        if (!RefreshLegacyScriptCommandInPlace(command, changes[0].Index, detailSuffix))
        {
            RefreshLegacyScriptView(command, changes[0].Index);
            _scriptDetailBox.Text += detailSuffix;
        }
        _saveScriptStructureButton.Enabled = true;
        SetStatus($"剧本参数：{command.CommandIdHex} {command.CommandName} 已修改 {summaries.Count} 个槽位，需完整保存剧本");
    }

    private bool TryGetSelectedOrCurrentLegacyScriptCommand(out LegacyScenarioCommandNode command)
    {
        if (TryGetSelectedLegacyScriptCommand(out command))
        {
            return true;
        }

        var row = GetSelectedScriptCommandRow();
        if (row != null && TryGetLegacyScriptCommand(row, out command))
        {
            return true;
        }

        command = null!;
        return false;
    }

    private bool CanEditSelectedLegacyScriptCommandParameters(out string reason)
    {
        if (!TryGetSelectedOrCurrentLegacyScriptCommand(out var command))
        {
            reason = "请先选择旧版完整树中的命令。";
            return false;
        }

        return CanEditLegacyScriptCommandParameters(command, out reason);
    }

    private static bool CanEditLegacyScriptCommandParameters(LegacyScenarioCommandNode command, out string reason)
    {
        if (command.Parameters.Count == 0)
        {
            reason = "该命令没有参数。";
            return false;
        }

        if (!command.Parameters.Any(parameter => CanEditLegacyScriptParameter(command, parameter, out _)))
        {
            reason = "该命令没有当前开放编辑的参数。";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private IReadOnlyList<LegacyScriptParameterEditRow> BuildLegacyScriptParameterEditRows(LegacyScenarioCommandNode command)
    {
        var parameterRows = DictionaryBuild.ToDictionaryFirstByKey(
            BuildLegacyScriptParameterRows(command),
            row => row.Index,
            row => row);
        return command.Parameters.Select(parameter =>
        {
            parameterRows.TryGetValue(parameter.Index, out var row);
            var canEdit = CanEditLegacyScriptParameter(command, parameter, out var reason);
            return new LegacyScriptParameterEditRow
            {
                Index = parameter.Index,
                SlotName = row?.SlotName ?? $"旧版槽 {parameter.Index} / {parameter.LayoutCodeHex}",
                Kind = row?.Kind ?? parameter.Kind.ToString(),
                RawHex = row?.RawHex ?? $"{parameter.TagHex}@{FormatLegacyScriptOffset(parameter.FileOffset, parameter.Index + 1)}",
                CurrentValue = FormatLegacyScriptParameterEditorValue(command, parameter),
                EditableValue = FormatLegacyScriptParameterEditorValue(command, parameter),
                DecodedValue = row?.DecodedValue ?? parameter.DisplayValue,
                Meaning = row?.Meaning ?? DescribeLegacyScriptParameterKind(parameter),
                Risk = row?.Risk ?? "完整结构写回：保存前备份，替换前按旧版规则重读校验。",
                CanEdit = canEdit,
                EditNote = canEdit ? BuildLegacyScriptParameterEditNote(command, parameter) : reason
            };
        }).ToList();
    }

    private static string DescribeLegacyScriptParameterKind(LegacyScenarioCommandParameter parameter)
        => parameter.Kind switch
        {
            LegacyScenarioParameterKind.Text => "命令内嵌文本参数",
            LegacyScenarioParameterKind.VariableArray => "旧版可变数组参数",
            LegacyScenarioParameterKind.Dword32 => "旧版 32 位整数参数",
            _ => "旧版 16 位参数"
        };

    private static string BuildLegacyScriptParameterEditNote(
        LegacyScenarioCommandNode command,
        LegacyScenarioCommandParameter parameter)
    {
        if (IsLegacyJumpTargetParameter(command, parameter))
        {
            return "填写目标命令 ord；如需保留原始相对位移，可填写 disp:<整数> 或 raw:<整数>。";
        }

        return parameter.Kind switch
        {
            LegacyScenarioParameterKind.Text => "可输入文本；完整保存会重建旧版命令结构。",
            LegacyScenarioParameterKind.VariableArray => "用逗号、空格或分号分隔 16 位整数；支持 0x 十六进制。",
            LegacyScenarioParameterKind.Dword32 => "填写 32 位整数，或 0x 开头十六进制。",
            _ => "填写 -32768 到 65535 的整数，或 0x0000 到 0xFFFF。"
        };
    }

    private static LegacyScriptParameterStateSnapshot CaptureLegacyScriptParameterState(LegacyScenarioCommandNode command)
        => new()
        {
            JumpTargetOrdinal = command.JumpTargetOrdinal,
            JumpTargetCommandIndex = command.JumpTargetCommandIndex,
            OriginalJumpDisplacement = command.OriginalJumpDisplacement,
            Parameters = command.Parameters
                .Select(parameter => new LegacyScriptParameterSnapshot
                {
                    Index = parameter.Index,
                    Kind = parameter.Kind,
                    IntValue = parameter.IntValue,
                    Text = parameter.Text,
                    Values = parameter.Values.ToList(),
                    ByteLength = parameter.ByteLength
                })
                .ToList()
        };

    private static void RestoreLegacyScriptParameterState(
        LegacyScenarioCommandNode command,
        LegacyScriptParameterStateSnapshot snapshot)
    {
        command.JumpTargetOrdinal = snapshot.JumpTargetOrdinal;
        command.JumpTargetCommandIndex = snapshot.JumpTargetCommandIndex;
        command.OriginalJumpDisplacement = snapshot.OriginalJumpDisplacement;

        for (var i = 0; i < command.Parameters.Count && i < snapshot.Parameters.Count; i++)
        {
            var parameter = command.Parameters[i];
            var state = snapshot.Parameters[i];
            parameter.Kind = state.Kind;
            parameter.IntValue = state.IntValue;
            parameter.Text = state.Text;
            parameter.Values.Clear();
            parameter.Values.AddRange(state.Values);
            parameter.ByteLength = state.ByteLength;
        }
    }

    private sealed class LegacyScriptParameterStateSnapshot
    {
        public int? JumpTargetOrdinal { get; init; }
        public int? JumpTargetCommandIndex { get; init; }
        public int? OriginalJumpDisplacement { get; init; }
        public List<LegacyScriptParameterSnapshot> Parameters { get; init; } = [];
    }

    private sealed class LegacyScriptParameterSnapshot
    {
        public int Index { get; init; }
        public LegacyScenarioParameterKind Kind { get; init; }
        public int IntValue { get; init; }
        public string Text { get; init; } = string.Empty;
        public List<int> Values { get; init; } = [];
        public int ByteLength { get; init; }
    }

    private bool TryGetSelectedLegacyScriptParameter(
        out LegacyScenarioCommandNode command,
        out LegacyScenarioCommandParameter parameter,
        out ScenarioCommandParameterRow row)
    {
        command = null!;
        parameter = null!;
        row = null!;

        row = GetSelectedScriptParameterRow()!;
        if (row == null) return false;

        if (!TryGetSelectedLegacyScriptCommand(out command))
        {
            var commandRow = GetSelectedScriptCommandRow();
            if (commandRow == null || !TryGetLegacyScriptCommand(commandRow, out command))
            {
                return false;
            }
        }

        var parameterIndex = row.Index;
        parameter = command.Parameters.FirstOrDefault(candidate => candidate.Index == parameterIndex)!;
        return parameter != null;
    }

    private ScenarioCommandParameterRow? GetSelectedScriptParameterRow()
    {
        if (_scriptParameterGrid.SelectedRows.Count > 0 &&
            _scriptParameterGrid.SelectedRows[0].DataBoundItem is ScenarioCommandParameterRow selected)
        {
            return selected;
        }

        return _scriptParameterGrid.CurrentRow?.DataBoundItem as ScenarioCommandParameterRow;
    }

    private static bool CanEditLegacyScriptParameter(
        LegacyScenarioCommandNode command,
        LegacyScenarioCommandParameter parameter,
        out string reason)
    {
        _ = command;
        reason = parameter.Kind switch
        {
            LegacyScenarioParameterKind.Text
                or LegacyScenarioParameterKind.VariableArray
                or LegacyScenarioParameterKind.Word16
                or LegacyScenarioParameterKind.Dword32 => string.Empty,
            _ => "该参数类型暂不开放编辑。"
        };
        return string.IsNullOrEmpty(reason);
    }

    private string FormatLegacyScriptParameterEditorValue(
        LegacyScenarioCommandNode command,
        LegacyScenarioCommandParameter parameter)
    {
        if (IsLegacyJumpTargetParameter(command, parameter))
        {
            return command.JumpTargetOrdinal?.ToString(CultureInfo.InvariantCulture)
                   ?? parameter.IntValue.ToString(CultureInfo.InvariantCulture);
        }

        return parameter.Kind switch
        {
            LegacyScenarioParameterKind.Text => parameter.Text,
            LegacyScenarioParameterKind.VariableArray => parameter.Values.Count == 0
                ? string.Empty
                : string.Join(",", parameter.Values.Select(value => value.ToString(CultureInfo.InvariantCulture))),
            _ => parameter.IntValue.ToString(CultureInfo.InvariantCulture)
        };
    }

    private bool TryApplyLegacyScriptParameterValue(
        LegacyScenarioCommandNode command,
        LegacyScenarioCommandParameter parameter,
        string text,
        out string newValue,
        out string error)
    {
        newValue = string.Empty;
        error = string.Empty;

        if (IsLegacyJumpTargetParameter(command, parameter))
        {
            return TryApplyLegacyScriptJumpTargetValue(command, parameter, text, out newValue, out error);
        }

        switch (parameter.Kind)
        {
            case LegacyScenarioParameterKind.Text:
                parameter.Text = text;
                parameter.ByteLength = EncodingService.GetGbkByteCount(text) + 1;
                newValue = FormatLegacyScriptParameterEditorValue(command, parameter);
                return true;

            case LegacyScenarioParameterKind.VariableArray:
                if (!TryParseLegacyScriptVariableArray(text, out var values, out error))
                {
                    return false;
                }

                parameter.Values.Clear();
                parameter.Values.AddRange(values);
                parameter.IntValue = parameter.Values.Count;
                parameter.ByteLength = 2 + parameter.Values.Count * 2;
                newValue = FormatLegacyScriptParameterEditorValue(command, parameter);
                return true;

            case LegacyScenarioParameterKind.Word16:
            case LegacyScenarioParameterKind.Dword32:
                if (!TryParseLegacyScriptParameterValue(text, parameter.Kind, out var parsed, out error))
                {
                    return false;
                }

                parameter.IntValue = parsed;
                parameter.ByteLength = parameter.Kind == LegacyScenarioParameterKind.Dword32 ? 4 : 2;
                newValue = FormatLegacyScriptParameterEditorValue(command, parameter);
                return true;

            default:
                error = "该参数类型暂不开放编辑。";
                return false;
        }
    }

    private bool TryApplyLegacyScriptJumpTargetValue(
        LegacyScenarioCommandNode command,
        LegacyScenarioCommandParameter parameter,
        string text,
        out string newValue,
        out string error)
    {
        var trimmed = text.Trim();
        var rawDisplacement = false;
        if (trimmed.StartsWith("raw:", StringComparison.OrdinalIgnoreCase))
        {
            rawDisplacement = true;
            trimmed = trimmed[4..].Trim();
        }
        else if (trimmed.StartsWith("disp:", StringComparison.OrdinalIgnoreCase))
        {
            rawDisplacement = true;
            trimmed = trimmed[5..].Trim();
        }

        if (!TryParseLegacyScriptParameterValue(trimmed, LegacyScenarioParameterKind.Dword32, out var parsed, out error))
        {
            newValue = string.Empty;
            return false;
        }

        parameter.IntValue = parsed;
        parameter.ByteLength = 4;
        if (rawDisplacement)
        {
            command.JumpTargetOrdinal = null;
            command.JumpTargetCommandIndex = null;
            command.OriginalJumpDisplacement = parsed;
            newValue = "disp:" + parsed.ToString(CultureInfo.InvariantCulture);
            error = string.Empty;
            return true;
        }

        command.JumpTargetOrdinal = parsed;
        var target = _currentLegacyScriptDocument?
            .EnumerateCommands()
            .FirstOrDefault(candidate => candidate.CommandOrdinal == parsed);
        command.JumpTargetCommandIndex = target?.CommandIndex;
        command.OriginalJumpDisplacement = null;
        newValue = FormatLegacyScriptParameterEditorValue(command, parameter);
        error = string.Empty;
        return true;
    }

    private static bool IsLegacyJumpTargetParameter(
        LegacyScenarioCommandNode command,
        LegacyScenarioCommandParameter parameter)
        => command.CommandId == 0x76 &&
           parameter.Kind == LegacyScenarioParameterKind.Dword32 &&
           ReferenceEquals(command.Parameters.FirstOrDefault(candidate => candidate.Kind == LegacyScenarioParameterKind.Dword32), parameter);

    private static bool TryParseLegacyScriptVariableArray(
        string text,
        out List<int> values,
        out string error)
    {
        values = [];
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            error = string.Empty;
            return true;
        }

        var tokens = Regex.Split(trimmed, @"[\s,;，；]+")
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToList();
        foreach (var token in tokens)
        {
            if (!TryParseLegacyScriptParameterValue(token, LegacyScenarioParameterKind.Word16, out var value, out error))
            {
                error = $"数组元素“{token}”无效：{error}";
                return false;
            }

            values.Add(value);
        }

        error = string.Empty;
        return true;
    }

    private static bool TryParseLegacyScriptParameterValue(
        string text,
        LegacyScenarioParameterKind kind,
        out int value,
        out string error)
    {
        value = 0;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            error = "请输入参数值。支持十进制，或 0x 开头的十六进制。";
            return false;
        }

        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var hex = trimmed[2..];
            if (kind == LegacyScenarioParameterKind.Dword32)
            {
                if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
                {
                    error = "32 位参数的十六进制值应为 0x00000000 到 0xFFFFFFFF。";
                    return false;
                }

                value = unchecked((int)parsed);
                error = string.Empty;
                return true;
            }

            if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var word) || word > ushort.MaxValue)
            {
                error = "16 位参数的十六进制值应为 0x0000 到 0xFFFF。";
                return false;
            }

            value = word > 60000 ? unchecked((ushort)word) - 65536 : (int)word;
            error = string.Empty;
            return true;
        }

        if (kind == LegacyScenarioParameterKind.Dword32)
        {
            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signedDword))
            {
                value = signedDword;
                error = string.Empty;
                return true;
            }

            if (uint.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unsignedDword))
            {
                value = unchecked((int)unsignedDword);
                error = string.Empty;
                return true;
            }

            error = "32 位参数值只能填写整数，或 0x 开头的十六进制。";
            return false;
        }

        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var decimalValue))
        {
            error = "参数值只能填写整数，或 0x 开头的十六进制。";
            return false;
        }

        if (kind == LegacyScenarioParameterKind.Word16 && (decimalValue < short.MinValue || decimalValue > ushort.MaxValue))
        {
            error = "16 位参数的十进制值可填写 -32768 到 65535；也支持 0x0000 到 0xFFFF。";
            return false;
        }

        value = decimalValue;
        error = string.Empty;
        return true;
    }

    private bool TrySelectLegacyScriptParameterRow(int parameterIndex)
    {
        foreach (DataGridViewRow gridRow in _scriptParameterGrid.Rows)
        {
            if (gridRow.DataBoundItem is not ScenarioCommandParameterRow candidate || candidate.Index != parameterIndex) continue;
            gridRow.Selected = true;
            var firstVisibleCell = gridRow.Cells.Cast<DataGridViewCell>().FirstOrDefault(cell => cell.Visible);
            if (firstVisibleCell != null)
            {
                _scriptParameterGrid.CurrentCell = firstVisibleCell;
            }

            return true;
        }

        return false;
    }

    private void BindScriptTextRows(IReadOnlyList<ScenarioTextEntry> rows)
    {
        _scriptTextGrid.DataSource = new BindingList<ScenarioTextEntry>(rows.ToList());
        foreach (DataGridViewColumn column in _scriptTextGrid.Columns)
        {
            column.HeaderText = column.DataPropertyName switch
            {
                nameof(ScenarioTextEntry.Index) => "序号",
                nameof(ScenarioTextEntry.OffsetHex) => "偏移",
                nameof(ScenarioTextEntry.ByteLength) => "容量",
                nameof(ScenarioTextEntry.Kind) => "类型",
                nameof(ScenarioTextEntry.Preview) => "预览",
                nameof(ScenarioTextEntry.Text) => "当前文本",
                nameof(ScenarioTextEntry.GbkByteCount) => "GBK字节",
                nameof(ScenarioTextEntry.RemainingBytes) => "剩余",
                nameof(ScenarioTextEntry.WriteStatus) => "写回状态",
                nameof(ScenarioTextEntry.Annotation) => "中文注释",
                _ => column.HeaderText
            };
            if (column.DataPropertyName is nameof(ScenarioTextEntry.Offset)
                or nameof(ScenarioTextEntry.CharLength)
                or nameof(ScenarioTextEntry.HasNewLines)
                or nameof(ScenarioTextEntry.OriginalText))
            {
                column.Visible = false;
            }
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            if (column.DataPropertyName is nameof(ScenarioTextEntry.Preview))
            {
                column.Width = 300;
            }
            else if (column.DataPropertyName is nameof(ScenarioTextEntry.Text)
                or nameof(ScenarioTextEntry.Annotation))
            {
                column.Width = 220;
            }
        }
    }

    private void RefreshScriptTextRows(IReadOnlyList<ScenarioTextEntry> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var offsets = entries.Select(entry => entry.Offset).ToHashSet();
        for (var rowIndex = 0; rowIndex < _scriptTextGrid.Rows.Count; rowIndex++)
        {
            if (_scriptTextGrid.Rows[rowIndex].DataBoundItem is ScenarioTextEntry entry &&
                offsets.Contains(entry.Offset))
            {
                _scriptTextGrid.InvalidateRow(rowIndex);
            }
        }
    }

    private void BindScriptSearchResultRows(IReadOnlyList<ScenarioSearchResultRow> rows)
    {
        _scriptSearchResultGrid.DataSource = new BindingList<ScenarioSearchResultRow>(rows.ToList());
        ConfigureScriptSearchResultGrid(_scriptSearchResultGrid);
    }

    private string BuildScriptOverview(ScenarioStructureProbeResult structure, IReadOnlyList<ScenarioTextEntry> texts)
    {
        var dictionaryLine = BuildSceneDictionaryDiagnosticLine();
        if (_currentLegacyScriptDocument != null)
        {
            return
                $"旧版剧本制作：{structure.FileName}\r\n" +
                $"Scene：{structure.SceneCount}    Section：{structure.SectionCount}    Command：{structure.CommandCandidateCount}    文本参数：{texts.Count}\r\n" +
                dictionaryLine + "\r\n" +
                "右键事件树可新增、插入、删除和移动；双击指令或使用右侧参数区按旧版 Dialog 修改参数。";
        }

        return
            $"剧本制作：{structure.FileName}\r\n" +
            $"Scene：{structure.SceneCount}    Section：{structure.SectionCount}    Command：{structure.CommandCandidateCount}    文本：{texts.Count}\r\n" +
            dictionaryLine + "\r\n" +
            "选择左侧节点查看对象、参数、文本和图片预览。";
    }

    private void ShowSelectedScriptTreeNode()
    {
        if (_bindingScriptDocument) return;
        if (_scriptTree.SelectedNode?.Tag is LegacyScenarioItemData { UiRow: ScenarioStructureRow itemRow } itemData)
        {
            var row = itemRow;
            _selectedScriptCommandRow = row.NodeType == "Command候选" ? row : null;
            _selectedScriptTextEntry = null;
            var rows = GetScriptRowsForNode(row).Where(x => x.NodeType == "Command候选").ToList();
            var textRows = GetScriptTextsForNodeContext(row, rows, _currentScriptTextEntries).ToList();
            var parameterRows = itemData.Command != null
                ? BuildLegacyScriptParameterRows(itemData.Command)
                : Array.Empty<ScenarioCommandParameterRow>();
            SuppressScriptSelectionEvents(() =>
            {
                BindScriptCommandRows(rows);
                BindScriptTextRows(textRows);
                BindScriptParameterRows(parameterRows);
            });
            if (itemData.Command != null)
            {
                ShowSelectedLegacyScriptParameter();
            }
            LoadInlineLegacyScriptDialogForSelection();
            UpdateScriptTextCapacityLabel();
            _scriptPreviewBox.Text = BuildScriptObjectPreview(row, rows, textRows, parameterRows);
            _scriptDetailBox.Text = itemData.Command != null
                ? BuildLegacyScriptRowDetail(row, itemData.Command)
                : BuildScriptRowDetail(row);
            UpdateScriptImagePreview(row);
            SetStatus(itemData.Command != null
                ? $"旧版 ItemData：ord {itemData.Ord} id {HexDisplayFormatter.Format(itemData.Id, 2)}"
                : row.CommandName);
            UpdateScriptStructureEditButtons();
        }
        else if (_scriptTree.SelectedNode?.Tag is ScenarioStructureRow row)
        {
            _selectedScriptCommandRow = row.NodeType == "Command候选" ? row : null;
            _selectedScriptTextEntry = null;
            var rows = GetScriptRowsForNode(row).Where(x => x.NodeType == "Command候选").ToList();
            var textRows = GetScriptTextsForNodeContext(row, rows, _currentScriptTextEntries).ToList();
            var parameterRows = row.NodeType == "Command候选"
                ? BuildScriptParameterRows(row)
                : Array.Empty<ScenarioCommandParameterRow>();
            SuppressScriptSelectionEvents(() =>
            {
                BindScriptCommandRows(rows);
                BindScriptTextRows(textRows);
                BindScriptParameterRows(parameterRows);
            });
            if (row.NodeType == "Command候选")
            {
                ShowSelectedLegacyScriptParameter();
            }
            LoadInlineLegacyScriptDialogForSelection();
            UpdateScriptTextCapacityLabel();
            _scriptPreviewBox.Text = BuildScriptObjectPreview(row, rows, textRows, parameterRows);
            _scriptDetailBox.Text = BuildScriptRowDetail(row);
            UpdateScriptImagePreview(row);
            if (row.NodeType == "Command候选")
            {
                SetStatus($"剧本制作：id:{row.CommandIndex} {row.CommandName} {row.OffsetHex}");
            }
            else
            {
                SetStatus($"剧本制作：{row.CommandName}");
            }
            UpdateScriptStructureEditButtons();
        }
        else if (_scriptTree.SelectedNode?.Tag is ScenarioTextEntry text)
        {
            _selectedScriptCommandRow = null;
            _selectedScriptTextEntry = text;
            var contextOwner = FindScriptOwnerRowForTextNode(_scriptTree.SelectedNode);
            var contextRows = contextOwner == null
                ? new List<ScenarioStructureRow>()
                : GetScriptRowsForNode(contextOwner).Where(x => x.NodeType == "Command候选").ToList();
            var contextTexts = contextRows.Count == 0
                ? new List<ScenarioTextEntry> { text }
                : GetScriptTextsForNodeContext(contextOwner!, contextRows, _currentScriptTextEntries).ToList();
            if (contextTexts.All(entry => entry.Offset != text.Offset))
            {
                contextTexts.Insert(0, text);
                contextTexts = contextTexts
                    .DistinctBy(entry => entry.Offset)
                    .ToList();
            }

            var relatedRows = GetScriptCommandsForText(text);
            var commandRows = relatedRows.Count > 0
                ? relatedRows
                : contextRows.Count > 0
                    ? contextRows
                    : Array.Empty<ScenarioStructureRow>();
            SuppressScriptSelectionEvents(() =>
            {
                BindScriptTextRows(contextTexts);
                BindScriptCommandRows(commandRows);
                BindScriptParameterRows(Array.Empty<ScenarioCommandParameterRow>());
                SelectScriptTextEntry(text, showSelection: false);
            });
            _scriptInlineDialogHost.ClearDialog("文本参数请通过所属命令的旧版 Dialog 修改。");
            _scriptPreviewBox.Text = BuildScriptTextPreview(text, commandRows);
            _scriptDetailBox.Text = BuildScriptTextDetail(text);
            ClearScriptImagePreview();
            UpdateScriptTextCapacityLabel();
            SetStatus($"剧本制作：文本 {text.OffsetHex}");
            UpdateScriptStructureEditButtons();
        }
        else
        {
            _selectedScriptCommandRow = null;
            _selectedScriptTextEntry = null;
            _scriptInlineDialogHost.ClearDialog();
            _scriptTextEditorBox.Clear();
            _scriptPreviewBox.Text = _currentScriptStructure == null
                ? "选择剧本后显示当前对象。"
                : BuildScriptOverviewPreview(_currentScriptStructure, _currentScriptTextEntries);
            ClearScriptImagePreview();
            UpdateScriptTextCapacityLabel();
            UpdateScriptStructureEditButtons();
        }
    }

    private bool TryGetLegacyItemDataFromScriptTreeNode(TreeNode? node, out LegacyScenarioItemData itemData)
    {
        if (node?.Tag is LegacyScenarioItemData selected)
        {
            itemData = selected;
            return true;
        }

        if (TryGetScriptTreeRow(node, out var row) &&
            _legacyScriptItemDataByRow.TryGetValue(row, out var byRow))
        {
            itemData = byRow;
            return true;
        }

        itemData = null!;
        return false;
    }

    private IReadOnlyList<ScenarioStructureRow> GetScriptRowsForNode(ScenarioStructureRow row)
    {
        if (_currentScriptStructure == null) return Array.Empty<ScenarioStructureRow>();
        return row.NodeType switch
        {
            "Scene候选" => _currentScriptStructure.Rows.Where(x => x.SceneIndex == row.SceneIndex).ToList(),
            "Section候选" => _currentScriptStructure.Rows.Where(x => x.SceneIndex == row.SceneIndex && x.SectionIndex == row.SectionIndex).ToList(),
            _ => new[] { row }
        };
    }

    private void ClearScriptImagePreview()
    {
        SetPictureBoxImage(_scriptImagePreviewBox, null);
        _scriptImagePreviewInfoBox.Text = "无图片预览";
    }

    private void UpdateScriptImagePreview(ScenarioStructureRow row)
    {
        if (_project == null || row.NodeType != "Command候选")
        {
            ClearScriptImagePreview();
            return;
        }

        if (!TryResolveScriptImagePreview(row, out var resourceName, out var scriptImageNumber, out var title))
        {
            ClearScriptImagePreview();
            return;
        }

        var resource = _imageResourceCatalogService.FindCatalogItem(_project, resourceName);
        if (resource == null || !resource.Exists || !resource.SupportsPreview)
        {
            SetPictureBoxImage(_scriptImagePreviewBox, null);
            _scriptImagePreviewInfoBox.Text =
                $"{title} {scriptImageNumber}\r\n" +
                $"资源：{resourceName}\r\n" +
                "未找到可预览资源。";
            return;
        }

        if (!TryGetScriptImagePreviewEntry(resource, scriptImageNumber, out var entry, out var resolveNote))
        {
            SetPictureBoxImage(_scriptImagePreviewBox, null);
            _scriptImagePreviewInfoBox.Text =
                $"{title} {scriptImageNumber}\r\n" +
                $"资源：{resource.FileName}\r\n" +
                resolveNote;
            return;
        }

        try
        {
            var width = Math.Max(260, _scriptImagePreviewBox.ClientSize.Width);
            var height = Math.Max(180, _scriptImagePreviewBox.ClientSize.Height);
            var preview = _imageResourceCatalogService.RenderEntryPreview(_project, entry, width, height);
            SetPictureBoxImage(_scriptImagePreviewBox, preview);
            _scriptImagePreviewInfoBox.Text =
                $"{title} {scriptImageNumber}\r\n" +
                $"资源：{entry.FileName} #{entry.ImageNumber}\r\n" +
                $"{resolveNote}\r\n" +
                $"{entry.Kind}  {entry.DecodedLength:N0}B";
        }
        catch (Exception ex)
        {
            SetPictureBoxImage(_scriptImagePreviewBox, null);
            _scriptImagePreviewInfoBox.Text =
                $"{title} {scriptImageNumber}\r\n" +
                $"资源：{resource.FileName}\r\n" +
                "预览失败：" + ex.Message;
        }
    }

    private bool TryResolveScriptImagePreview(
        ScenarioStructureRow row,
        out string resourceName,
        out int scriptImageNumber,
        out string title)
    {
        resourceName = string.Empty;
        scriptImageNumber = 0;
        title = string.Empty;

        var values = TryGetLegacyScriptCommand(row, out var legacyCommand)
            ? FlattenLegacyScriptCommandValues(legacyCommand)
            : ScenarioStructureParameterExtractor.ExtractLogicalWords(row).ToList();
        if (values.Count == 0)
        {
            return false;
        }

        if (row.CommandId == 0x27 || row.CommandName.Contains("背景显示", StringComparison.Ordinal))
        {
            if (!TryResolveBackgroundImageNumber(values, out scriptImageNumber))
            {
                return false;
            }

            resourceName = "Mmap.e5";
            title = $"{HexDisplayFormatter.Format(row.CommandId)}:{row.CommandName}";
            return true;
        }

        if (row.CommandId == 0x72 && values.Count >= 2 && values[0] == 28)
        {
            resourceName = "Tr.e5";
            scriptImageNumber = values[1];
            title = $"{HexDisplayFormatter.Format(row.CommandId)}:{row.CommandName} R插图";
            return true;
        }

        return false;
    }

    private static bool TryResolveBackgroundImageNumber(IReadOnlyList<int> values, out int imageNumber)
    {
        imageNumber = 0;
        if (values.Count == 0) return false;

        var category = values[0];
        var valueIndex = category + 1;
        if (valueIndex < 0 || valueIndex >= values.Count) return false;

        imageNumber = values[valueIndex];
        if (category == 0)
        {
            imageNumber += 1;
        }
        else if (category == 2)
        {
            imageNumber += 41;
        }

        return imageNumber > 0;
    }

    private bool TryGetScriptImagePreviewEntry(
        ImageResourceFileInfo resource,
        int scriptImageNumber,
        out ImageResourceEntryInfo entry,
        out string note)
    {
        var candidates = new[] { scriptImageNumber, scriptImageNumber + 1, scriptImageNumber - 1 }
            .Where(number => number > 0)
            .Distinct()
            .ToList();
        foreach (var candidate in candidates)
        {
            var found = _imageResourceCatalogService.TryGetEntry(resource, candidate);
            if (found == null)
            {
                continue;
            }

            entry = found;
            note = candidate == scriptImageNumber
                ? "按脚本图号直接预览。"
                : $"脚本图号 {scriptImageNumber} 未命中，已预览相邻 E5 图号 #{candidate}。";
            return true;
        }

        entry = null!;
        note = $"未找到图号 {scriptImageNumber} 或相邻图号；当前资源共 {resource.EntryCount} 张。";
        return false;
    }

    private string BuildScriptOverviewPreview(ScenarioStructureProbeResult structure, IReadOnlyList<ScenarioTextEntry> texts)
    {
        var mode = _currentLegacyScriptDocument != null ? "旧版完整树" : "兼容探针";
        return
            $"{structure.FileName}    {mode}\r\n" +
            $"Scene {structure.SceneCount}    Section {structure.SectionCount}    Command {structure.CommandCandidateCount}    文本 {texts.Count}\r\n" +
            BuildSceneDictionaryDiagnosticLine() + "\r\n" +
            "右键事件树可新增、插入、删除和移动；双击指令或使用右侧参数区按旧版 Dialog 修改参数。";
    }

    private string BuildSceneDictionaryDiagnosticLine()
    {
        var dictionary = _currentSceneStringDocument;
        if (dictionary == null)
        {
            return "命令字典：未加载；将尝试自动探测 CczString.ini。";
        }

        return $"命令字典：{dictionary.Commands.Count} 条；{dictionary.DecodeDiagnostic}；{dictionary.SourcePath}";
    }

    private string BuildScriptObjectPreview(
        ScenarioStructureRow owner,
        IReadOnlyList<ScenarioStructureRow> commandRows,
        IReadOnlyList<ScenarioTextEntry> textRows,
        IReadOnlyList<ScenarioCommandParameterRow> parameterRows)
    {
        if (owner.NodeType == "Command候选")
        {
            return BuildScriptCommandPreview(owner, parameterRows, textRows);
        }

        var nodeName = owner.NodeType == "Section候选"
            ? $"Scene {owner.SceneIndex} / Section {owner.SectionIndex}"
            : $"Scene {owner.SceneIndex}";
        var commandPreview = commandRows.Count == 0
            ? "无命令"
            : string.Join("\r\n", commandRows.Take(10).Select(BuildScriptCommandPreviewLine));
        var textPreview = textRows.Count == 0
            ? "无文本"
            : string.Join("\r\n", textRows.Take(6).Select(text => $"{text.OffsetHex} {TrimSingleLine(text.Text, 40)}"));
        return
            $"{nodeName}\r\n" +
            $"命令：{commandRows.Count}    文本：{textRows.Count}\r\n\r\n" +
            $"命令预览：\r\n{commandPreview}\r\n\r\n" +
            $"文本预览：\r\n{textPreview}";
    }

    private string BuildScriptCommandPreview(
        ScenarioStructureRow row,
        IReadOnlyList<ScenarioCommandParameterRow> parameterRows,
        IReadOnlyList<ScenarioTextEntry> textRows)
    {
        var values = TryGetLegacyScriptCommand(row, out var legacyCommand)
            ? BuildLegacyCommandPreviewValueLines(legacyCommand)
            : BuildScenarioStructurePreviewValueLines(row);
        var valuePreview = values.Count == 0
            ? "无参数"
            : string.Join("\r\n", values.Take(10));
        var textPreview = textRows.Count == 0
            ? "无文本"
            : string.Join("\r\n", textRows.Take(4).Select(text => $"{text.OffsetHex} {TrimSingleLine(text.Text, 44)}"));
        return
            $"{row.CommandIndex:000} {row.CommandIdHex} {row.CommandName}\r\n" +
            $"Scene {row.SceneIndex} / Section {row.SectionIndex}\r\n\r\n" +
            $"参数：\r\n{valuePreview}\r\n\r\n" +
            $"关联文本：\r\n{textPreview}";
    }

    private string BuildScriptCommandPreviewLine(ScenarioStructureRow row)
    {
        if (TryGetLegacyScriptCommand(row, out var legacyCommand))
        {
            return $"{row.CommandIndex:000} {BuildLegacyScriptCommandSummary(row, legacyCommand, includeIdentity: true, maxVisibleValues: 6)}";
        }

        return $"{row.CommandIndex:000} {BuildScriptCommandSummary(row, includeIdentity: true, maxVisibleValues: 6)}";
    }

    private string BuildScriptTextPreview(ScenarioTextEntry text, IReadOnlyList<ScenarioStructureRow> commandRows)
    {
        var bytes = EncodingService.GetGbkByteCount(_scriptTextEditorBox.Text);
        var commandPreview = commandRows.Count == 0
            ? "未找到关联命令"
            : string.Join("\r\n", commandRows.Take(6).Select(BuildScriptCommandPreviewLine));
        var mode = _legacyScriptTextByOffset.ContainsKey(text.Offset)
            ? "旧版文本参数：可随完整保存扩容"
            : $"原地文本：GBK {bytes}/{text.ByteLength}";
        var decode = BuildScenarioTextDecodeLine(text);
        return
            $"文本 #{text.Index}    {text.OffsetHex}\r\n" +
            $"{text.Kind}    {mode}\r\n\r\n" +
            decode + "\r\n\r\n" +
            $"{TrimSingleLine(text.Text, 160)}\r\n\r\n" +
            $"关联命令：\r\n{commandPreview}";
    }

    private string BuildLegacyScriptCommandEditState(LegacyScenarioCommandNode command)
    {
        var parts = new List<string>();
        parts.Add(CanDeleteLegacyScriptCommand(command, out _) ? "可删除" : "不可删除");
        parts.Add(CanInsertNearLegacyScriptCommand(command, out _) ? "可前后插入" : "不可前后插入");
        if (command.ChildBlock != null) parts.Add("可追加子块");
        if (command.Parameters.Any(parameter => CanEditLegacyScriptParameter(command, parameter, out _))) parts.Add("可改参数");
        if (command.TextParameters.Any()) parts.Add("含文本参数");
        return string.Join(" / ", parts);
    }

    private IReadOnlyList<ScenarioTextEntry> GetScriptTextsForRows(IReadOnlyList<ScenarioStructureRow> rows, IReadOnlyList<ScenarioTextEntry> texts)
        => GetScriptTextsForRows(rows, texts, allowFallback: true, strictContext: false);

    private IReadOnlyList<ScenarioTextEntry> GetScriptTextsForNodeContext(
        ScenarioStructureRow owner,
        IReadOnlyList<ScenarioStructureRow> rows,
        IReadOnlyList<ScenarioTextEntry> texts)
    {
        if (_currentLegacyScriptDocument != null)
        {
            return GetLegacyScriptTextsForRows(owner, rows);
        }

        return owner.NodeType switch
        {
            "Scene候选" or "Section候选" => GetScriptTextsForRows(rows, texts, allowFallback: false, strictContext: true),
            _ => GetScriptTextsForRows(rows, texts, allowFallback: true, strictContext: false)
        };
    }

    private IReadOnlyList<ScenarioTextEntry> GetLegacyScriptTextsForRows(
        ScenarioStructureRow owner,
        IReadOnlyList<ScenarioStructureRow> rows)
    {
        var offsets = new HashSet<int>();
        if (owner.NodeType == "Command候选" && TryGetLegacyScriptCommand(owner, out var ownerCommand))
        {
            AddLegacyTextOffsets(ownerCommand, offsets);
        }
        else
        {
            foreach (var row in rows.Where(row => row.NodeType == "Command候选"))
            {
                if (TryGetLegacyScriptCommand(row, out var command))
                {
                    AddLegacyTextOffsets(command, offsets);
                }
            }
        }

        return offsets
            .Select(offset => _legacyScriptTextEntryByOffset.TryGetValue(offset, out var entry) ? entry : null)
            .Where(entry => entry != null)
            .Select(entry => entry!)
            .OrderBy(entry => entry.Offset)
            .ThenBy(entry => entry.Index)
            .ToList();
    }

    private static void AddLegacyTextOffsets(LegacyScenarioCommandNode command, ISet<int> offsets)
    {
        foreach (var parameter in command.TextParameters)
        {
            offsets.Add(parameter.FileOffset);
        }
    }

    private IReadOnlyList<ScenarioStructureRow> GetScriptCommandsForText(ScenarioTextEntry text)
    {
        if (_currentScriptStructure == null)
        {
            return Array.Empty<ScenarioStructureRow>();
        }

        if (_currentLegacyScriptDocument != null &&
            _legacyScriptTextByOffset.TryGetValue(text.Offset, out var legacyText) &&
            _legacyScriptRowByKey.TryGetValue(BuildLegacyCommandKey(legacyText.Command), out var legacyRow))
        {
            return new[] { legacyRow };
        }

        var rows = _currentScriptStructure.Rows
            .Where(x => x.NodeType == "Command候选")
            .Select(row => new
            {
                Row = row,
                Score = GetScriptTextRelationScore(row, text),
                Distance = TryParseHexOffset(row.OffsetHex, out var offset)
                    ? Math.Abs(text.Offset - offset)
                    : int.MaxValue
            })
            .Where(candidate => candidate.Score >= 56)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Row.SceneIndex)
            .ThenBy(candidate => candidate.Row.SectionIndex)
            .ThenBy(candidate => candidate.Row.CommandIndex)
            .Select(candidate => candidate.Row)
            .Take(36)
            .ToList();

        return rows;
    }

    private static bool IsTextRelatedToCommand(ScenarioStructureRow row, ScenarioTextEntry text)
        => GetScriptTextRelationScore(row, text) >= 56;

    private static bool IsTextRelatedCommand(ScenarioStructureRow row)
        => row.ReferenceHint.Contains("文本线索", StringComparison.Ordinal)
           || row.CommandName.Contains("对话", StringComparison.Ordinal)
           || row.CommandName.Contains("信息", StringComparison.Ordinal)
           || row.CommandName.Contains("旁白", StringComparison.Ordinal)
           || row.CommandName.Contains("场所", StringComparison.Ordinal)
           || row.CommandName.Contains("胜利条件", StringComparison.Ordinal)
           || row.CommandName.Contains("章名", StringComparison.Ordinal)
           || row.CommandName.Contains("文字", StringComparison.Ordinal)
           || row.CommandName.Contains("剧情", StringComparison.Ordinal);

    private void ShowSelectedScriptCommand()
    {
        if (_bindingScriptDocument) return;
        var row = GetSelectedScriptCommandRow();
        if (row == null) return;
        _selectedScriptCommandRow = row;
        _selectedScriptTextEntry = null;
        var textRows = GetScriptTextsForRows(new[] { row }, _currentScriptTextEntries).ToList();
        var parameterRows = BuildScriptParameterRows(row);
        SuppressScriptSelectionEvents(() =>
        {
            BindScriptTextRows(textRows);
            BindScriptParameterRows(parameterRows);
        });
        ShowSelectedLegacyScriptParameter();
        _scriptTextEditorBox.Clear();
        UpdateScriptTextCapacityLabel();
        _scriptPreviewBox.Text = BuildScriptCommandPreview(row, parameterRows, textRows);
        _scriptDetailBox.Text = BuildScriptRowDetail(row);
        UpdateScriptImagePreview(row);
    }

    private ScenarioStructureRow? GetSelectedScriptCommandRow()
    {
        if (TryGetScriptTreeRow(_scriptTree.SelectedNode, out var treeRow) && treeRow.NodeType == "Command候选")
        {
            return treeRow;
        }

        return _selectedScriptCommandRow;
    }

    private bool SelectScriptTreeNode(ScenarioStructureRow target, bool suppressEvents = false, bool ensureVisible = true)
    {
        TreeNode? found = null;
        foreach (TreeNode root in _scriptTree.Nodes)
        {
            found = FindScriptTreeNode(root, target);
            if (found != null) break;
        }

        if (found == null && target.NodeType == "Command候选")
        {
            foreach (TreeNode root in _scriptTree.Nodes)
            {
                found = FindScriptOwnerTreeNode(root, target, "Section候选");
                if (found != null) break;
            }
        }

        if (found == null && target.NodeType == "Command候选")
        {
            foreach (TreeNode root in _scriptTree.Nodes)
            {
                found = FindScriptOwnerTreeNode(root, target, "Scene候选");
                if (found != null) break;
            }
        }

        if (found == null)
        {
            return false;
        }

        var restoreBindingState = _bindingScriptDocument;
        if (suppressEvents)
        {
            _bindingScriptDocument = true;
        }
        try
        {
            _scriptTree.SelectedNode = found;
            if (ensureVisible)
            {
                found.EnsureVisible();
            }
        }
        finally
        {
            _bindingScriptDocument = restoreBindingState;
        }
        return true;
    }

    private bool SelectScriptTextTreeNode(ScenarioTextEntry target, bool suppressEvents = false)
    {
        TreeNode? found = null;
        foreach (TreeNode root in _scriptTree.Nodes)
        {
            found = FindScriptTextTreeNode(root, target);
            if (found != null) break;
        }

        if (found == null)
        {
            return false;
        }

        var restoreBindingState = _bindingScriptDocument;
        if (suppressEvents)
        {
            _bindingScriptDocument = true;
        }

        try
        {
            _scriptTree.SelectedNode = found;
            found.EnsureVisible();
        }
        finally
        {
            _bindingScriptDocument = restoreBindingState;
        }

        return true;
    }

    private bool SelectScriptCommandGridRow(ScenarioStructureRow target, bool suppressEvents = false)
    {
        var restoreBindingState = _bindingScriptDocument;
        if (suppressEvents)
        {
            _bindingScriptDocument = true;
        }

        try
        {
            if (TrySelectScriptCommandGridRow(target))
            {
                return true;
            }

            if (_currentScriptStructure != null)
            {
                var ownerRows = _currentScriptStructure.Rows
                    .Where(row => row.NodeType == "Command候选"
                        && row.SceneIndex == target.SceneIndex
                        && row.SectionIndex == target.SectionIndex)
                    .OrderBy(row => row.CommandIndex)
                    .ToList();
                if (ownerRows.Any(row => IsSameScriptCommand(row, target)))
                {
                    BindScriptCommandRows(ownerRows, target);
                    return TrySelectScriptCommandGridRow(target);
                }
            }

            return false;
        }
        finally
        {
            _bindingScriptDocument = restoreBindingState;
        }
    }

    private bool TrySelectScriptCommandGridRow(ScenarioStructureRow target)
    {
        foreach (DataGridViewRow gridRow in _scriptCommandGrid.Rows)
        {
            if (gridRow.DataBoundItem is not ScenarioStructureRow candidate || !IsSameScriptCommand(candidate, target)) continue;
            gridRow.Selected = true;
            _scriptCommandGrid.CurrentCell = gridRow.Cells.Cast<DataGridViewCell>().First(cell => cell.Visible);
            return true;
        }

        return false;
    }

    private static bool IsSameScriptCommand(ScenarioStructureRow left, ScenarioStructureRow right) =>
        left.SceneIndex == right.SceneIndex &&
        left.SectionIndex == right.SectionIndex &&
        left.CommandIndex == right.CommandIndex &&
        string.Equals(left.OffsetHex, right.OffsetHex, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.CommandIdHex, right.CommandIdHex, StringComparison.OrdinalIgnoreCase);

    private static TreeNode? FindScriptTreeNode(TreeNode node, ScenarioStructureRow target)
    {
        if (TryGetScriptTreeRow(node, out var row)
            && row.NodeType == target.NodeType
            && row.SceneIndex == target.SceneIndex
            && row.SectionIndex == target.SectionIndex
            && row.CommandIndex == target.CommandIndex
            && string.Equals(row.OffsetHex, target.OffsetHex, StringComparison.OrdinalIgnoreCase))
        {
            return node;
        }

        foreach (TreeNode child in node.Nodes)
        {
            var found = FindScriptTreeNode(child, target);
            if (found != null) return found;
        }

        return null;
    }

    private static TreeNode? FindScriptTextTreeNode(TreeNode node, ScenarioTextEntry target)
    {
        if (node.Tag is ScenarioTextEntry text && text.Offset == target.Offset)
        {
            return node;
        }

        foreach (TreeNode child in node.Nodes)
        {
            var found = FindScriptTextTreeNode(child, target);
            if (found != null) return found;
        }

        return null;
    }

    private void LocateSelectedScriptCommandInTree()
    {
        var row = GetSelectedScriptCommandRow();
        if (row == null)
        {
            MessageBox.Show(this, "请先在左侧事件树中选择一条命令。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (SelectScriptTreeNode(row, suppressEvents: true))
        {
            _selectedScriptCommandRow = row;
            _selectedScriptTextEntry = null;
            ShowSelectedScriptCommand();
            SetStatus($"剧本制作：已定位 {row.CommandName} {row.OffsetHex}");
        }
        else
        {
            MessageBox.Show(this, "左侧树中没有找到对应命令节点。", "定位失败", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void CopySelectedScriptCommandSummary()
        => CopySelectedScriptCommandSummary(LegacyScriptEditorScope.Script);

    private void CopySelectedScriptCommandSummary(LegacyScriptEditorScope scope)
    {
        if (scope != LegacyScriptEditorScope.Script)
        {
            CopySelectedLegacyScriptCommandSummary(scope);
            return;
        }

        var checkedScenes = GetCheckedLegacyScriptScenes(scope);
        if (checkedScenes.Count > 0)
        {
            CopyLegacyScriptSceneBatch(scope, checkedScenes);
            return;
        }

        var checkedSections = GetCheckedLegacyScriptSections(scope);
        if (checkedSections.Count > 0)
        {
            CopyLegacyScriptSectionBatch(scope, checkedSections);
            return;
        }

        var checkedCommands = GetCheckedLegacyScriptCommands(scope);
        if (checkedCommands.Count > 0)
        {
            CopyLegacyScriptCommandBatch(checkedCommands);
            return;
        }

        if (TryGetSelectedLegacyScriptSceneNode(scope, out var scene))
        {
            CopyLegacyScriptSceneBatch(scope, new[] { scene });
            return;
        }

        if (TryGetSelectedLegacyScriptSectionNode(scope, out var section))
        {
            CopyLegacyScriptSectionBatch(scope, new[] { section });
            return;
        }

        var row = GetSelectedScriptCommandRow();
        if (row == null)
        {
            MessageBox.Show(this, "请先在左侧事件树中选择一条命令、Section 或 Scene。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var parameters = BuildScriptParameterRows(row);
        _scriptCommandClipboardItem = _scenarioCommandClipboardService.CreateClipboardItem(_currentScriptScenario?.FileName ?? "RS", row, parameters);
        if (TryGetLegacyScriptCommand(row, out var legacyCommand) && CanCopyLegacyScriptCommand(legacyCommand, out _))
        {
            _legacyScriptCommandClipboard = CloneLegacyScriptCommandForPaste(legacyCommand, legacyCommand.SceneIndex, legacyCommand.SectionIndex);
            _legacyScriptCommandClipboardItems = new[] { _legacyScriptCommandClipboard };
            _legacyScriptSceneClipboardItems = Array.Empty<LegacyScenarioScene>();
            _legacyScriptSectionClipboardItems = Array.Empty<LegacyScenarioSection>();
            _legacyScriptCommandClipboardProjectName = _project?.Name ?? string.Empty;
            _legacyScriptCommandClipboardScenarioName = _currentScriptScenario?.FileName ?? "RS";
            _legacyScriptCommandClipboardGameRoot = _project?.GameRoot ?? string.Empty;
        }
        else
        {
            _legacyScriptCommandClipboard = null;
            _legacyScriptCommandClipboardItems = Array.Empty<LegacyScenarioCommandNode>();
            _legacyScriptSceneClipboardItems = Array.Empty<LegacyScenarioScene>();
            _legacyScriptSectionClipboardItems = Array.Empty<LegacyScenarioSection>();
            _legacyScriptCommandClipboardProjectName = string.Empty;
            _legacyScriptCommandClipboardScenarioName = string.Empty;
            _legacyScriptCommandClipboardGameRoot = string.Empty;
        }

        _previewPasteScriptCommandButton.Enabled = true;
        var text = _scenarioCommandClipboardService.BuildCommandCopyText(_currentScriptScenario?.FileName ?? "RS", row, parameters);
        var clipboardText = TryGetLegacyScriptCommand(row, out legacyCommand)
            ? BuildLegacyScriptCommandClipboardText(text, _currentScriptScenario?.FileName ?? "RS", new[] { legacyCommand })
            : text;
        try
        {
            SetLegacyScriptStructuredClipboardTextOrThrow(clipboardText);
            _scriptDetailBox.Text = text + "\r\n\r\n已复制到剪贴板。";
            SetStatus($"剧本制作：已复制命令摘要 {row.CommandName} {row.OffsetHex}");
        }
        catch (Exception ex)
        {
            _scriptDetailBox.Text = text + "\r\n\r\n剪贴板写入失败，可从此处手动复制。\r\n" + ex.Message;
            SetStatus("剧本制作：命令摘要已生成，剪贴板写入失败");
        }
        UpdateScriptStructureEditButtons();
    }

    private void CopySelectedLegacyScriptCommandSummary(LegacyScriptEditorScope scope)
    {
        var checkedScenes = GetCheckedLegacyScriptScenes(scope);
        if (checkedScenes.Count > 0)
        {
            CopyLegacyScriptSceneBatch(scope, checkedScenes);
            return;
        }

        var checkedSections = GetCheckedLegacyScriptSections(scope);
        if (checkedSections.Count > 0)
        {
            CopyLegacyScriptSectionBatch(scope, checkedSections);
            return;
        }

        var checkedCommands = GetCheckedLegacyScriptCommands(scope);
        if (checkedCommands.Count > 0)
        {
            CopyLegacyScriptCommandBatch(scope, checkedCommands);
            return;
        }

        if (TryGetSelectedLegacyScriptSceneNode(scope, out var scene))
        {
            CopyLegacyScriptSceneBatch(scope, new[] { scene });
            return;
        }

        if (TryGetSelectedLegacyScriptSectionNode(scope, out var section))
        {
            CopyLegacyScriptSectionBatch(scope, new[] { section });
            return;
        }

        if (!TryGetSelectedLegacyScriptCommand(scope, out var command))
        {
            MessageBox.Show(this, "请先在左侧事件树中选择一条命令、Section 或 Scene。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!CanCopyLegacyScriptCommand(command, out var reason))
        {
            MessageBox.Show(this, reason, "无法复制命令", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        CopyLegacyScriptCommandBatch(scope, new[] { command });
    }

    private void CopyLegacyScriptCommandBatch(IReadOnlyList<LegacyScenarioCommandNode> commands)
    {
        if (commands.Count == 0)
        {
            MessageBox.Show(this, "请先勾选要复制的命令。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        foreach (var command in commands)
        {
            if (!CanCopyLegacyScriptCommand(command, out var reason))
            {
                MessageBox.Show(this,
                    $"勾选列表中包含不能复制的命令：\r\n{command.CommandIndex:000} {command.CommandIdHex} {command.CommandName}\r\n\r\n{reason}",
                    "无法批量复制",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
        }

        var scenarioName = _currentScriptScenario?.FileName ?? "RS";
        var first = commands[0];
        if (_legacyScriptRowByKey.TryGetValue(BuildLegacyCommandKey(first), out var firstRow))
        {
            _scriptCommandClipboardItem = _scenarioCommandClipboardService.CreateClipboardItem(
                scenarioName,
                firstRow,
                BuildLegacyScriptParameterRows(first));
        }
        else
        {
            _scriptCommandClipboardItem = null;
        }

        var clipboardItems = commands
            .Select(command => CloneLegacyScriptCommandForPaste(command, command.SceneIndex, command.SectionIndex))
            .ToList();
        _legacyScriptCommandClipboardItems = clipboardItems;
        _legacyScriptCommandClipboard = clipboardItems[0];
        _legacyScriptSceneClipboardItems = Array.Empty<LegacyScenarioScene>();
        _legacyScriptSectionClipboardItems = Array.Empty<LegacyScenarioSection>();
        _legacyScriptCommandClipboardProjectName = _project?.Name ?? string.Empty;
        _legacyScriptCommandClipboardScenarioName = scenarioName;
        _legacyScriptCommandClipboardGameRoot = _project?.GameRoot ?? string.Empty;
        _previewPasteScriptCommandButton.Enabled = _scriptCommandClipboardItem != null ||
                                                   _legacyScriptCommandClipboardItems.Count > 0 ||
                                                   _legacyScriptSceneClipboardItems.Count > 0 ||
                                                   _legacyScriptSectionClipboardItems.Count > 0;

        var text = BuildLegacyScriptCommandBatchCopyText(scenarioName, commands);
        var clipboardText = BuildLegacyScriptCommandClipboardText(text, scenarioName, commands);
        try
        {
            SetLegacyScriptStructuredClipboardTextOrThrow(clipboardText);
            _scriptDetailBox.Text = text + "\r\n\r\n已复制到剪贴板。";
            SetStatus($"剧本制作：已批量复制 {commands.Count} 条命令，可切换剧本后粘贴");
        }
        catch (Exception ex)
        {
            _scriptDetailBox.Text = text + "\r\n\r\n剪贴板写入失败，可从此处手动复制。\r\n" + ex.Message;
            SetStatus($"剧本制作：已生成 {commands.Count} 条命令批量复制摘要，剪贴板写入失败");
        }
        UpdateScriptStructureEditButtons();
    }

    private void CopyLegacyScriptCommandBatch(LegacyScriptEditorScope scope, IReadOnlyList<LegacyScenarioCommandNode> commands)
    {
        if (scope == LegacyScriptEditorScope.Script)
        {
            CopyLegacyScriptCommandBatch(commands);
            return;
        }

        if (commands.Count == 0)
        {
            MessageBox.Show(this, "请先勾选要复制的命令。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        foreach (var command in commands)
        {
            if (!CanCopyLegacyScriptCommand(command, out var reason))
            {
                MessageBox.Show(this,
                    $"勾选列表中包含不能复制的命令：\r\n{command.CommandIndex:000} {command.CommandIdHex} {command.CommandName}\r\n\r\n{reason}",
                    "无法批量复制",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
        }

        var scenarioName = GetLegacyScriptScenarioName(scope);
        var clipboardItems = commands
            .Select(command => CloneLegacyScriptCommandForPaste(command, command.SceneIndex, command.SectionIndex))
            .ToList();
        _scriptCommandClipboardItem = null;
        _legacyScriptCommandClipboardItems = clipboardItems;
        _legacyScriptCommandClipboard = clipboardItems[0];
        _legacyScriptSceneClipboardItems = Array.Empty<LegacyScenarioScene>();
        _legacyScriptSectionClipboardItems = Array.Empty<LegacyScenarioSection>();
        _legacyScriptCommandClipboardProjectName = _project?.Name ?? string.Empty;
        _legacyScriptCommandClipboardScenarioName = string.IsNullOrWhiteSpace(scenarioName) ? GetLegacyScriptScopeStatusPrefix(scope) : scenarioName;
        _legacyScriptCommandClipboardGameRoot = _project?.GameRoot ?? string.Empty;
        _previewPasteScriptCommandButton.Enabled = _legacyScriptCommandClipboardItems.Count > 0 ||
                                                   _legacyScriptSceneClipboardItems.Count > 0 ||
                                                   _legacyScriptSectionClipboardItems.Count > 0;

        var text = BuildLegacyScriptCommandBatchCopyText(_legacyScriptCommandClipboardScenarioName, commands);
        var clipboardText = BuildLegacyScriptCommandClipboardText(text, _legacyScriptCommandClipboardScenarioName, commands);
        try
        {
            SetLegacyScriptStructuredClipboardTextOrThrow(clipboardText);
            SetLegacyScriptDetailText(scope, text + "\r\n\r\n已复制到剪贴板。");
            SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)}：已复制 {commands.Count} 条命令，可切换剧本后粘贴");
        }
        catch (Exception ex)
        {
            SetLegacyScriptDetailText(scope, text + "\r\n\r\n剪贴板写入失败，可从此处手动复制。\r\n" + ex.Message);
            SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)}：已生成 {commands.Count} 条命令复制摘要，剪贴板写入失败");
        }
        UpdateScriptStructureEditButtons();
    }

    private void CopyLegacyScriptSectionBatch(LegacyScriptEditorScope scope, IReadOnlyList<LegacyScenarioSection> sections)
    {
        if (sections.Count == 0)
        {
            MessageBox.Show(this, "请先选择要复制的 Section。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var scenarioName = scope == LegacyScriptEditorScope.Script
            ? _currentScriptScenario?.FileName ?? "RS"
            : GetLegacyScriptScenarioName(scope);
        _scriptCommandClipboardItem = null;
        _legacyScriptCommandClipboard = null;
        _legacyScriptCommandClipboardItems = Array.Empty<LegacyScenarioCommandNode>();
        _legacyScriptSceneClipboardItems = Array.Empty<LegacyScenarioScene>();
        _legacyScriptSectionClipboardItems = sections
            .Select(section => CloneLegacyScriptSectionForPaste(section, section.SceneIndex, section.SectionIndex))
            .ToList();
        _legacyScriptCommandClipboardProjectName = _project?.Name ?? string.Empty;
        _legacyScriptCommandClipboardScenarioName = string.IsNullOrWhiteSpace(scenarioName) ? GetLegacyScriptScopeStatusPrefix(scope) : scenarioName;
        _legacyScriptCommandClipboardGameRoot = _project?.GameRoot ?? string.Empty;
        _previewPasteScriptCommandButton.Enabled = _legacyScriptSectionClipboardItems.Count > 0;

        var text = BuildLegacyScriptSectionBatchCopyText(_legacyScriptCommandClipboardScenarioName, sections);
        var clipboardText = BuildLegacyScriptSectionClipboardText(text, _legacyScriptCommandClipboardScenarioName, sections);
        try
        {
            SetLegacyScriptStructuredClipboardTextOrThrow(clipboardText);
            SetLegacyScriptDetailText(scope, text + "\r\n\r\n已复制到剪贴板。");
            SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)}：已复制 {sections.Count} 个 Section，可切换剧本后粘贴到 Section 前后");
        }
        catch (Exception ex)
        {
            SetLegacyScriptDetailText(scope, text + "\r\n\r\n剪贴板写入失败，可从此处手动复制。\r\n" + ex.Message);
            SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)}：已生成 {sections.Count} 个 Section 复制摘要，剪贴板写入失败");
        }
        UpdateScriptStructureEditButtons();
    }

    private void CopyLegacyScriptSceneBatch(LegacyScriptEditorScope scope, IReadOnlyList<LegacyScenarioScene> scenes)
    {
        if (scenes.Count == 0)
        {
            MessageBox.Show(this, "请先选择要复制的 Scene。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var scenarioName = scope == LegacyScriptEditorScope.Script
            ? _currentScriptScenario?.FileName ?? "RS"
            : GetLegacyScriptScenarioName(scope);
        _scriptCommandClipboardItem = null;
        _legacyScriptCommandClipboard = null;
        _legacyScriptCommandClipboardItems = Array.Empty<LegacyScenarioCommandNode>();
        _legacyScriptSectionClipboardItems = Array.Empty<LegacyScenarioSection>();
        _legacyScriptSceneClipboardItems = scenes
            .Select(CloneLegacyScriptSceneForPaste)
            .ToList();
        _legacyScriptCommandClipboardProjectName = _project?.Name ?? string.Empty;
        _legacyScriptCommandClipboardScenarioName = string.IsNullOrWhiteSpace(scenarioName) ? GetLegacyScriptScopeStatusPrefix(scope) : scenarioName;
        _legacyScriptCommandClipboardGameRoot = _project?.GameRoot ?? string.Empty;
        _previewPasteScriptCommandButton.Enabled = _legacyScriptSceneClipboardItems.Count > 0;

        var text = BuildLegacyScriptSceneBatchCopyText(_legacyScriptCommandClipboardScenarioName, scenes);
        var clipboardText = BuildLegacyScriptSceneClipboardText(text, _legacyScriptCommandClipboardScenarioName, scenes);
        try
        {
            SetLegacyScriptStructuredClipboardTextOrThrow(clipboardText);
            SetLegacyScriptDetailText(scope, text + "\r\n\r\n已复制到剪贴板。");
            SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)}：已复制 {scenes.Count} 个 Scene，可切换剧本后粘贴到 Scene 前后");
        }
        catch (Exception ex)
        {
            SetLegacyScriptDetailText(scope, text + "\r\n\r\n剪贴板写入失败，可从此处手动复制。\r\n" + ex.Message);
            SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)}：已生成 {scenes.Count} 个 Scene 复制摘要，剪贴板写入失败");
        }
        UpdateScriptStructureEditButtons();
    }

    private string BuildLegacyScriptCommandBatchCopyText(string scenarioName, IReadOnlyList<LegacyScenarioCommandNode> commands)
    {
        var builder = new StringBuilder();
        builder.AppendLine(commands.Count == 1
            ? "CCZModStudio 剧本命令复制候选"
            : "CCZModStudio 剧本命令批量复制候选");
        builder.AppendLine($"来源剧本：{scenarioName}");
        builder.AppendLine($"命令数量：{commands.Count}");
        builder.AppendLine();

        for (var i = 0; i < commands.Count; i++)
        {
            var command = commands[i];
            builder.AppendLine($"{i + 1}. Scene {command.SceneIndex} / Section {command.SectionIndex} / Command {command.CommandIndex:000} / ord {command.CommandOrdinal}");
            builder.AppendLine($"   命令：{command.CommandIdHex} {command.CommandName}");
            builder.AppendLine($"   参数：{BuildLegacyScriptParameterPreview(command)}");
            if (command.TextParameters.Any())
            {
                builder.AppendLine("   文本：" + string.Join(" / ", command.TextParameters.Select(parameter => TrimSingleLine(parameter.Text, 36))));
            }

            if (command.ChildBlock != null)
            {
                builder.AppendLine($"   子块：{command.ChildBlock.Kind}，直接命令 {command.ChildBlock.Commands.Count} 条");
            }
        }

        builder.AppendLine();
        builder.AppendLine("安全边界：以上内容来自旧版完整树，可在本次运行内跨剧本粘贴；完整保存前请核对目标 Scene/Section、人物/物品/地图引用和实机效果。跨剧本粘贴 0x76 跳转命令会被阻止，需在目标剧本中手工重设跳转目标。");
        return builder.ToString().TrimEnd();
    }

    private string BuildLegacyScriptSectionBatchCopyText(string scenarioName, IReadOnlyList<LegacyScenarioSection> sections)
    {
        var builder = new StringBuilder();
        builder.AppendLine(sections.Count == 1
            ? "CCZModStudio 剧本 Section 复制候选"
            : "CCZModStudio 剧本 Section 批量复制候选");
        builder.AppendLine($"来源剧本：{scenarioName}");
        builder.AppendLine($"Section 数量：{sections.Count}");
        builder.AppendLine();

        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            var commands = section.EnumerateCommands().ToList();
            builder.AppendLine($"{i + 1}. Scene {section.SceneIndex} / Section {section.SectionIndex}");
            builder.AppendLine($"   命令：{commands.Count} 条");
            foreach (var command in commands.Take(8))
            {
                builder.AppendLine($"   - {command.CommandIndex:000} {command.CommandIdHex} {command.CommandName}");
            }

            if (commands.Count > 8)
            {
                builder.AppendLine($"   - ... 其余 {commands.Count - 8} 条省略");
            }
        }

        builder.AppendLine();
        builder.AppendLine("安全边界：复制 Section 会携带完整命令树和子块；粘贴后会按目标 Scene 重新编号，完整保存前请核对人物/物品/地图引用、文本和 0x76 跳转。");
        return builder.ToString().TrimEnd();
    }

    private string BuildLegacyScriptSceneBatchCopyText(string scenarioName, IReadOnlyList<LegacyScenarioScene> scenes)
    {
        var builder = new StringBuilder();
        builder.AppendLine(scenes.Count == 1
            ? "CCZModStudio 剧本 Scene 复制候选"
            : "CCZModStudio 剧本 Scene 批量复制候选");
        builder.AppendLine($"来源剧本：{scenarioName}");
        builder.AppendLine($"Scene 数量：{scenes.Count}");
        builder.AppendLine();

        for (var i = 0; i < scenes.Count; i++)
        {
            var scene = scenes[i];
            var commands = scene.Sections.SelectMany(section => section.EnumerateCommands()).ToList();
            builder.AppendLine($"{i + 1}. Scene {scene.SceneIndex}");
            builder.AppendLine($"   Section：{scene.Sections.Count} 个    命令：{commands.Count} 条");
            foreach (var section in scene.Sections.Take(8))
            {
                builder.AppendLine($"   - Section {section.SectionIndex}：{section.EnumerateCommands().Count()} 条命令");
            }

            if (scene.Sections.Count > 8)
            {
                builder.AppendLine($"   - ... 其余 {scene.Sections.Count - 8} 个 Section 省略");
            }
        }

        builder.AppendLine();
        builder.AppendLine("安全边界：复制 Scene 会携带所有 Section、完整命令树和子块；粘贴后会重建 Scene/Section 编号，完整保存前请核对人物/物品/地图引用、文本和 0x76 跳转。");
        return builder.ToString().TrimEnd();
    }

    private string BuildLegacyScriptCommandClipboardText(
        string visibleText,
        string scenarioName,
        IReadOnlyList<LegacyScenarioCommandNode> commands)
        => BuildLegacyScriptClipboardText(
            visibleText,
            scenarioName,
            commands,
            Array.Empty<LegacyScenarioScene>(),
            Array.Empty<LegacyScenarioSection>());

    private string BuildLegacyScriptSceneClipboardText(
        string visibleText,
        string scenarioName,
        IReadOnlyList<LegacyScenarioScene> scenes)
        => BuildLegacyScriptClipboardText(
            visibleText,
            scenarioName,
            Array.Empty<LegacyScenarioCommandNode>(),
            scenes,
            Array.Empty<LegacyScenarioSection>());

    private string BuildLegacyScriptSectionClipboardText(
        string visibleText,
        string scenarioName,
        IReadOnlyList<LegacyScenarioSection> sections)
        => BuildLegacyScriptClipboardText(
            visibleText,
            scenarioName,
            Array.Empty<LegacyScenarioCommandNode>(),
            Array.Empty<LegacyScenarioScene>(),
            sections);

    private string BuildLegacyScriptClipboardText(
        string visibleText,
        string scenarioName,
        IReadOnlyList<LegacyScenarioCommandNode> commands,
        IReadOnlyList<LegacyScenarioScene> scenes,
        IReadOnlyList<LegacyScenarioSection> sections)
    {
        var envelope = new LegacyScriptClipboardEnvelope
        {
            Format = LegacyScriptClipboardFormat,
            Version = 1,
            SourceProjectName = _project?.Name ?? string.Empty,
            SourceGameRoot = _project?.GameRoot ?? string.Empty,
            SourceScenarioName = scenarioName,
            CreatedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            Commands = commands.Select(CreateLegacyScriptClipboardCommand).ToList(),
            Scenes = scenes.Select(CreateLegacyScriptClipboardScene).ToList(),
            Sections = sections.Select(CreateLegacyScriptClipboardSection).ToList()
        };
        var json = JsonSerializer.Serialize(envelope, LegacyScriptClipboardJsonOptions);
        return visibleText.TrimEnd() +
               "\r\n\r\n" +
               LegacyScriptClipboardBeginMarker +
               "\r\n" +
               json +
               "\r\n" +
               LegacyScriptClipboardEndMarker;
    }

    private bool TrySetLegacyScriptStructuredClipboardText(string clipboardText, out string error)
    {
        try
        {
            Clipboard.SetText(clipboardText);
            _legacyScriptClipboardFingerprint = BuildLegacyScriptClipboardFingerprint(clipboardText);
            _legacyScriptClipboardMemoryOnly = false;
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            _legacyScriptClipboardFingerprint = BuildLegacyScriptClipboardFingerprint(clipboardText);
            _legacyScriptClipboardMemoryOnly = true;
            error = ex.Message;
            return false;
        }
    }

    private void SetLegacyScriptStructuredClipboardTextOrThrow(string clipboardText)
    {
        if (!TrySetLegacyScriptStructuredClipboardText(clipboardText, out var error))
        {
            throw new InvalidOperationException(error);
        }
    }

    private static LegacyScriptClipboardScene CreateLegacyScriptClipboardScene(LegacyScenarioScene scene)
        => new()
        {
            SceneIndex = scene.SceneIndex,
            Sections = scene.Sections.Select(CreateLegacyScriptClipboardSection).ToList()
        };

    private static LegacyScriptClipboardSection CreateLegacyScriptClipboardSection(LegacyScenarioSection section)
        => new()
        {
            SceneIndex = section.SceneIndex,
            SectionIndex = section.SectionIndex,
            DeclaredLength = section.DeclaredLength,
            Commands = section.Commands.Select(CreateLegacyScriptClipboardCommand).ToList()
        };

    private static LegacyScriptClipboardCommand CreateLegacyScriptClipboardCommand(LegacyScenarioCommandNode command)
        => new()
        {
            SceneIndex = command.SceneIndex,
            SectionIndex = command.SectionIndex,
            CommandIndex = command.CommandIndex,
            CommandOrdinal = command.CommandOrdinal,
            CommandId = command.CommandId,
            CommandName = command.CommandName,
            StartsBodyBlock = command.StartsBodyBlock,
            IsSubEventMarker = command.IsSubEventMarker,
            OpensSubEventBlock = command.OpensSubEventBlock,
            EndsSubEventBlock = command.EndsSubEventBlock,
            JumpTargetOrdinal = command.JumpTargetOrdinal,
            JumpTargetCommandIndex = command.JumpTargetCommandIndex,
            OriginalJumpDisplacement = command.OriginalJumpDisplacement,
            Parameters = command.Parameters.Select(CreateLegacyScriptClipboardParameter).ToList(),
            ChildBlock = command.ChildBlock == null ? null : CreateLegacyScriptClipboardBlock(command.ChildBlock)
        };

    private static LegacyScriptClipboardBlock CreateLegacyScriptClipboardBlock(LegacyScenarioCommandBlock block)
        => new()
        {
            Kind = block.Kind,
            Commands = block.Commands.Select(CreateLegacyScriptClipboardCommand).ToList()
        };

    private static LegacyScriptClipboardParameter CreateLegacyScriptClipboardParameter(LegacyScenarioCommandParameter parameter)
        => new()
        {
            Index = parameter.Index,
            LayoutCode = parameter.LayoutCode,
            Tag = parameter.Tag,
            Kind = parameter.Kind,
            IntValue = parameter.IntValue,
            Text = parameter.Text,
            Values = parameter.Values.ToList(),
            ByteLength = parameter.ByteLength
        };

    private void PreviewPasteScriptCommandCandidate()
        => PreviewPasteScriptCommandCandidate(LegacyScriptEditorScope.Script);

    private void PreviewPasteScriptCommandCandidate(LegacyScriptEditorScope scope)
    {
        if (scope != LegacyScriptEditorScope.Script)
        {
            PreviewLegacyScriptPaste(scope);
            return;
        }

        if (!TryEnsureLegacyScriptClipboardAvailable(out var clipboardReason) && _scriptCommandClipboardItem == null)
        {
            MessageBox.Show(this, clipboardReason, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_legacyScriptSceneClipboardItems.Count > 0)
        {
            if (!TryGetSelectedLegacyScriptSceneNode(scope, out var sceneTarget))
            {
                MessageBox.Show(this, "请先在左侧事件树中选择粘贴预览目标 Scene。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _scriptDetailBox.Text = BuildLegacyScriptSceneBatchPastePreview(scope, _legacyScriptSceneClipboardItems, sceneTarget, beforeSelected: false);
            SetStatus($"剧本制作：已生成 {_legacyScriptSceneClipboardItems.Count} 个 Scene 粘贴预览");
            return;
        }

        if (_legacyScriptSectionClipboardItems.Count > 0)
        {
            if (!TryGetSelectedLegacyScriptSectionNode(scope, out var sectionTarget))
            {
                MessageBox.Show(this, "请先在左侧事件树中选择粘贴预览目标 Section。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _scriptDetailBox.Text = BuildLegacyScriptSectionBatchPastePreview(scope, _legacyScriptSectionClipboardItems, sectionTarget, beforeSelected: false);
            SetStatus($"剧本制作：已生成 {_legacyScriptSectionClipboardItems.Count} 个 Section 粘贴预览");
            return;
        }

        if (_legacyScriptCommandClipboardItems.Count > 1)
        {
            if (!TryGetLegacyScriptCommandPasteTarget(scope, beforeSelected: true, out var pasteTarget, out _))
            {
                MessageBox.Show(this, "请先在左侧事件树中选择粘贴预览目标命令或 Section。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _scriptDetailBox.Text = BuildLegacyScriptCommandBatchPastePreview(scope, _legacyScriptCommandClipboardItems, pasteTarget);
            SetStatus($"剧本制作：已生成 {_legacyScriptCommandClipboardItems.Count} 条命令批量粘贴预览");
            return;
        }

        if (_legacyScriptCommandClipboardItems.Count == 1 && _scriptCommandClipboardItem == null)
        {
            if (!TryGetLegacyScriptCommandPasteTarget(scope, beforeSelected: true, out var pasteTarget, out _))
            {
                MessageBox.Show(this, "请先在左侧事件树中选择粘贴预览目标命令或 Section。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _scriptDetailBox.Text = BuildLegacyScriptCommandBatchPastePreview(scope, _legacyScriptCommandClipboardItems, pasteTarget);
            SetStatus("剧本制作：已生成跨项目命令粘贴预览");
            return;
        }

        if (_scriptCommandClipboardItem == null)
        {
            MessageBox.Show(this, "请先复制一条剧本命令候选。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var target = GetSelectedScriptCommandRow();
        if (target == null)
        {
            MessageBox.Show(this, "请先在左侧事件树中选择粘贴预览目标命令。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var targetParameters = BuildScriptParameterRows(target);
        _scriptDetailBox.Text = _scenarioCommandClipboardService.BuildPastePreview(
            _scriptCommandClipboardItem,
            _currentScriptScenario?.FileName ?? "RS",
            target,
            targetParameters);
        SetStatus($"剧本制作：已生成粘贴预览 {target.CommandName} {target.OffsetHex}");
    }

    private void PreviewLegacyScriptPaste(LegacyScriptEditorScope scope)
    {
        if (!TryEnsureLegacyScriptClipboardAvailable(out var clipboardReason))
        {
            MessageBox.Show(this, clipboardReason, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_legacyScriptSceneClipboardItems.Count > 0)
        {
            if (!TryGetSelectedLegacyScriptSceneNode(scope, out var sceneTarget))
            {
                MessageBox.Show(this, "请先在左侧事件树中选择粘贴预览目标 Scene。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var scenePreview = BuildLegacyScriptSceneBatchPastePreview(scope, _legacyScriptSceneClipboardItems, sceneTarget, beforeSelected: false);
            SetLegacyScriptDetailText(scope, scenePreview);
            SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)}：已生成 {_legacyScriptSceneClipboardItems.Count} 个 Scene 粘贴预览");
            return;
        }

        if (_legacyScriptSectionClipboardItems.Count > 0)
        {
            if (!TryGetSelectedLegacyScriptSectionNode(scope, out var sectionTarget))
            {
                MessageBox.Show(this, "请先在左侧事件树中选择粘贴预览目标 Section。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var sectionPreview = BuildLegacyScriptSectionBatchPastePreview(scope, _legacyScriptSectionClipboardItems, sectionTarget, beforeSelected: false);
            SetLegacyScriptDetailText(scope, sectionPreview);
            SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)}：已生成 {_legacyScriptSectionClipboardItems.Count} 个 Section 粘贴预览");
            return;
        }

        if (TryGetSelectedLegacyScriptSectionNode(scope, out _))
        {
            if (!TryGetLegacyScriptCommandPasteTarget(scope, beforeSelected: true, out var pasteTarget, out _))
            {
                MessageBox.Show(this, "没有在当前事件树中定位到目标 Section。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var sectionTargetPreview = BuildLegacyScriptCommandBatchPastePreview(scope, _legacyScriptCommandClipboardItems, pasteTarget);
            SetLegacyScriptDetailText(scope, sectionTargetPreview);
            SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)}：已生成 {_legacyScriptCommandClipboardItems.Count} 条命令粘贴预览");
            return;
        }

        if (!TryGetSelectedLegacyScriptCommand(scope, out var targetCommand))
        {
            MessageBox.Show(this, "请先在左侧事件树中选择粘贴预览目标命令。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var targetRow = GetLegacyScriptRowForCommand(scope, targetCommand);
        if (targetRow == null)
        {
            MessageBox.Show(this, "没有在当前事件树中定位到目标命令行。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var commandPreview = BuildLegacyScriptCommandBatchPastePreview(scope, _legacyScriptCommandClipboardItems, targetRow);
        SetLegacyScriptDetailText(scope, commandPreview);
        SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)}：已生成 {_legacyScriptCommandClipboardItems.Count} 条命令粘贴预览");
    }

    private string BuildLegacyScriptCommandBatchPastePreview(
        IReadOnlyList<LegacyScenarioCommandNode> sourceCommands,
        ScenarioStructureRow target)
    {
        var builder = new StringBuilder();
        var targetScenarioName = _currentScriptScenario?.FileName ?? "RS";
        builder.AppendLine("CCZModStudio 剧本命令批量粘贴预览（不写入）");
        builder.AppendLine($"来源：{(_legacyScriptCommandClipboardScenarioName.Length == 0 ? "未知剧本" : _legacyScriptCommandClipboardScenarioName)}，命令 {sourceCommands.Count} 条");
        builder.AppendLine($"目标：{targetScenarioName} Scene {target.SceneIndex} / Section {target.SectionIndex} / Command {target.CommandIndex} / {target.OffsetHex}");
        builder.AppendLine();
        foreach (var command in sourceCommands.Take(20))
        {
            builder.AppendLine($"- {command.CommandIdHex} {command.CommandName}：{BuildLegacyScriptParameterPreview(command)}");
        }

        if (sourceCommands.Count > 20)
        {
            builder.AppendLine($"- ... 其余 {sourceCommands.Count - 20} 条省略");
        }

        AppendLegacyScriptPasteValidationSummary(builder, ValidateLegacyScriptPaste(LegacyScriptEditorScope.Script, beforeSelected: true));
        return builder.ToString().TrimEnd();
    }

    private string BuildLegacyScriptCommandBatchPastePreview(
        LegacyScriptEditorScope scope,
        IReadOnlyList<LegacyScenarioCommandNode> sourceCommands,
        ScenarioStructureRow target)
    {
        var builder = new StringBuilder();
        var targetScenarioName = GetLegacyScriptScenarioName(scope);
        builder.AppendLine("CCZModStudio 剧本命令批量粘贴预览（不写入）");
        builder.AppendLine($"来源：{(_legacyScriptCommandClipboardScenarioName.Length == 0 ? "未知剧本" : _legacyScriptCommandClipboardScenarioName)}，命令 {sourceCommands.Count} 条");
        builder.AppendLine($"目标：{targetScenarioName} Scene {target.SceneIndex} / Section {target.SectionIndex} / Command {target.CommandIndex} / {target.OffsetHex}");
        builder.AppendLine();
        foreach (var command in sourceCommands.Take(20))
        {
            builder.AppendLine($"- {command.CommandIdHex} {command.CommandName}：{BuildLegacyScriptParameterPreview(command)}");
        }

        if (sourceCommands.Count > 20)
        {
            builder.AppendLine($"- ... 其余 {sourceCommands.Count - 20} 条省略");
        }

        AppendLegacyScriptPasteValidationSummary(builder, ValidateLegacyScriptPaste(scope, beforeSelected: true));
        return builder.ToString().TrimEnd();
    }

    private string BuildLegacyScriptCommandBatchPastePreview(
        LegacyScriptEditorScope scope,
        IReadOnlyList<LegacyScenarioCommandNode> sourceCommands,
        LegacyScriptCommandPasteTarget target)
    {
        var builder = new StringBuilder();
        var targetScenarioName = GetLegacyScriptScenarioName(scope);
        builder.AppendLine("CCZModStudio 剧本命令批量粘贴预览（不写入）");
        builder.AppendLine($"来源：{(_legacyScriptCommandClipboardScenarioName.Length == 0 ? "未知剧本" : _legacyScriptCommandClipboardScenarioName)}，命令 {sourceCommands.Count} 条");
        builder.AppendLine($"目标：{targetScenarioName} {target.TargetText}");
        builder.AppendLine($"位置：{target.StatusActionText}");
        builder.AppendLine();
        foreach (var command in sourceCommands.Take(20))
        {
            builder.AppendLine($"- {command.CommandIdHex} {command.CommandName}：{BuildLegacyScriptParameterPreview(command)}");
        }

        if (sourceCommands.Count > 20)
        {
            builder.AppendLine($"- ... 其余 {sourceCommands.Count - 20} 条省略");
        }

        AppendLegacyScriptPasteValidationSummary(builder, ValidateLegacyScriptPaste(scope, beforeSelected: true));
        return builder.ToString().TrimEnd();
    }

    private string BuildLegacyScriptSectionBatchPastePreview(
        LegacyScriptEditorScope scope,
        IReadOnlyList<LegacyScenarioSection> sourceSections,
        LegacyScenarioSection target,
        bool beforeSelected)
    {
        var builder = new StringBuilder();
        var targetScenarioName = GetLegacyScriptScenarioName(scope);
        builder.AppendLine("CCZModStudio 剧本 Section 批量粘贴预览（不写入）");
        builder.AppendLine($"来源：{(_legacyScriptCommandClipboardScenarioName.Length == 0 ? "未知剧本" : _legacyScriptCommandClipboardScenarioName)}，Section {sourceSections.Count} 个");
        builder.AppendLine($"目标：{targetScenarioName} Scene {target.SceneIndex} / Section {target.SectionIndex} {(beforeSelected ? "前面" : "后面")}");
        builder.AppendLine();
        foreach (var section in sourceSections.Take(12))
        {
            var commands = section.EnumerateCommands().ToList();
            builder.AppendLine($"- Scene {section.SceneIndex} / Section {section.SectionIndex}：命令 {commands.Count} 条");
            foreach (var command in commands.Take(4))
            {
                builder.AppendLine($"  {command.CommandIndex:000} {command.CommandIdHex} {command.CommandName}");
            }
        }

        if (sourceSections.Count > 12)
        {
            builder.AppendLine($"- ... 其余 {sourceSections.Count - 12} 个 Section 省略");
        }

        AppendLegacyScriptPasteValidationSummary(builder, ValidateLegacyScriptPaste(scope, beforeSelected));
        return builder.ToString().TrimEnd();
    }

    private string BuildLegacyScriptSceneBatchPastePreview(
        LegacyScriptEditorScope scope,
        IReadOnlyList<LegacyScenarioScene> sourceScenes,
        LegacyScenarioScene target,
        bool beforeSelected)
    {
        var builder = new StringBuilder();
        var targetScenarioName = GetLegacyScriptScenarioName(scope);
        builder.AppendLine("CCZModStudio 剧本 Scene 批量粘贴预览（不写入）");
        builder.AppendLine($"来源：{(_legacyScriptCommandClipboardScenarioName.Length == 0 ? "未知剧本" : _legacyScriptCommandClipboardScenarioName)}，Scene {sourceScenes.Count} 个");
        builder.AppendLine($"目标：{targetScenarioName} Scene {target.SceneIndex} {(beforeSelected ? "前面" : "后面")}");
        builder.AppendLine();
        foreach (var scene in sourceScenes.Take(8))
        {
            var commandCount = scene.Sections.Sum(section => section.EnumerateCommands().Count());
            builder.AppendLine($"- Scene {scene.SceneIndex}：Section {scene.Sections.Count} 个，命令 {commandCount} 条");
            foreach (var section in scene.Sections.Take(4))
            {
                builder.AppendLine($"  Section {section.SectionIndex}：{section.EnumerateCommands().Count()} 条命令");
            }
        }

        if (sourceScenes.Count > 8)
        {
            builder.AppendLine($"- ... 其余 {sourceScenes.Count - 8} 个 Scene 省略");
        }

        AppendLegacyScriptPasteValidationSummary(builder, ValidateLegacyScriptPaste(scope, beforeSelected));
        return builder.ToString().TrimEnd();
    }

    private string BuildScriptRowDetail(ScenarioStructureRow row)
    {
        if (row.NodeType != "Command候选")
        {
            return $"{row.CommandName}\r\n类型：{row.NodeType}\r\n中文注释：{row.Annotation}";
        }

        if (TryGetLegacyScriptCommand(row, out var legacyCommand))
        {
            return BuildLegacyScriptRowDetail(row, legacyCommand);
        }

        var valueLines = BuildValueDetailBlock(BuildScenarioStructurePreviewValueLines(row));
        return
            $"命令：{row.CommandIdHex} {row.CommandName}\r\n" +
            $"位置：Scene {row.SceneIndex} / Section {row.SectionIndex} / Command {row.CommandIndex} / {row.OffsetHex}\r\n" +
            $"参数：\r\n{valueLines}\r\n" +
            $"引用候选：{(string.IsNullOrWhiteSpace(row.ReferenceHint) ? "（无）" : row.ReferenceHint)}\r\n" +
            $"中文注释：{row.Annotation}";
    }

    private IReadOnlyList<ScenarioCommandParameterRow> BuildScriptParameterRows(ScenarioStructureRow row)
    {
        if (TryGetLegacyScriptCommand(row, out var legacyCommand))
        {
            return BuildLegacyScriptParameterRows(legacyCommand);
        }
        return _scenarioCommandParameterTemplateService.BuildParameterRows(row, _project, _tables);
    }

    private bool TryGetLegacyScriptCommand(ScenarioStructureRow row, out LegacyScenarioCommandNode command)
    {
        if (_legacyScriptItemDataByRow.TryGetValue(row, out var itemData) && itemData.Command != null)
        {
            command = itemData.Command;
            return true;
        }

        if (_currentLegacyScriptDocument != null &&
            _legacyScriptCommandByKey.TryGetValue(BuildLegacyCommandKey(row), out var found))
        {
            command = found;
            return true;
        }

        command = null!;
        return false;
    }

    private static string BuildLegacyScriptRowDetail(ScenarioStructureRow row, LegacyScenarioCommandNode command)
    {
        var textSummary = command.TextParameters.Any()
            ? string.Join(" / ", command.TextParameters.Select(parameter => TrimSingleLine(parameter.Text, 32)))
            : string.Empty;
        var jumpSummary = command.CommandId == 0x76
            ? command.JumpTargetOrdinal.HasValue
                ? command.JumpTargetCommandIndex.HasValue
                    ? $"跳到第 {command.JumpTargetCommandIndex.Value} 条命令"
                    : $"跳到 ord {command.JumpTargetOrdinal.Value}"
                : $"保留原相对位移 {command.OriginalJumpDisplacement}"
            : string.Empty;
        var valueLines = BuildValueDetailBlock(BuildLegacyCommandPreviewValueLines(command));
        var textBlock = string.IsNullOrWhiteSpace(textSummary)
            ? string.Empty
            : $"\r\n文本：{textSummary}";
        var jumpBlock = string.IsNullOrWhiteSpace(jumpSummary)
            ? string.Empty
            : $"\r\n跳转：{jumpSummary}";
        return
            $"命令：{row.CommandIdHex} {row.CommandName}\r\n" +
            $"位置：Scene {row.SceneIndex} / Section {row.SectionIndex} / Command {row.CommandIndex}\r\n" +
            $"参数：\r\n{valueLines}{textBlock}{jumpBlock}";
    }

    private static IReadOnlyList<ScenarioCommandParameterRow> BuildLegacyScriptParameterRows(LegacyScenarioCommandNode command)
        => command.Parameters.Select(parameter => new ScenarioCommandParameterRow
        {
            Index = parameter.Index,
            SlotName = BuildLegacyScriptParameterDisplayName(command, parameter),
            Kind = parameter.Kind.ToString(),
            RawHex = $"{parameter.TagHex}@{FormatLegacyScriptOffset(parameter.FileOffset, parameter.Index + 1)}",
            DecimalValue = parameter.Kind == LegacyScenarioParameterKind.Text
                ? parameter.ByteLength
                : parameter.IntValue,
            DecodedValue = BuildLegacyScriptParameterDecodedValue(command, parameter),
            Meaning = BuildLegacyScriptParameterMeaning(command, parameter),
            Risk = "完整结构写回：保存前备份，替换前按旧版规则重读校验。",
            FromTemplate = true,
            Annotation = $"Scene {command.SceneIndex} / Section {command.SectionIndex} / Command {command.CommandIndex} {command.CommandName}"
        }).ToList();

    private static string BuildLegacyScriptParameterDecodedValue(
        LegacyScenarioCommandNode command,
        LegacyScenarioCommandParameter parameter)
    {
        if (IsPerson2Parameter(command, parameter.Index))
        {
            return ScriptVariableValueResolver.FormatPerson2Reference(parameter.IntValue);
        }

        if (IsPerson1Parameter(command, parameter.Index))
        {
            return FormatPerson1Reference(parameter.IntValue);
        }

        return command.CommandId switch
        {
            0x77 => BuildVariableOperationParameterValueText(command, parameter),
            0x78 => BuildIntegerVariableAssignmentParameterValueText(command, parameter),
            0x79 => BuildVariableTestParameterValueText(command, parameter),
            _ => FormatLegacyScriptParameterReadableValue(command, parameter)
        };
    }

    private static string BuildLegacyScriptParameterMeaning(
        LegacyScenarioCommandNode command,
        LegacyScenarioCommandParameter parameter)
    {
        var commandSpecific = command.CommandId switch
        {
            0x76 when IsLegacyJumpTargetParameter(command, parameter) => "无条件跳转的目标命令",
            0x77 => parameter.Index switch
            {
                0 => "被修改的变量种类",
                1 => "被修改的变量编号",
                2 => "对左侧变量执行的运算",
                3 => "右侧值来自常数、指针变量或整型变量",
                4 => "参与运算的右侧值",
                _ => string.Empty
            },
            0x78 => parameter.Index switch
            {
                0 => "整型变量编号",
                1 => "变量与人物属性之间的赋值方向",
                2 => "读取或写回的人物、变量",
                3 => "读取或写回的属性",
                _ => string.Empty
            },
            0x79 => parameter.Index switch
            {
                0 => "左侧值来自常数、指针变量或整型变量",
                1 => "比较左侧值",
                2 => "比较关系",
                3 => "右侧值来自常数、指针变量或整型变量",
                4 => "比较右侧值",
                _ => string.Empty
            },
            _ => string.Empty
        };
        if (!string.IsNullOrWhiteSpace(commandSpecific))
        {
            return commandSpecific;
        }

        return parameter.Kind switch
        {
            LegacyScenarioParameterKind.Text => "文本内容",
            LegacyScenarioParameterKind.VariableArray => "变量列表",
            LegacyScenarioParameterKind.Dword32 when IsLegacyJumpTargetParameter(command, parameter) => "跳转目标，保存时自动换算",
            LegacyScenarioParameterKind.Dword32 => "整数",
            _ => "数值"
        };
    }

    private static string FormatLegacyScriptParameterReadableValue(
        LegacyScenarioCommandNode command,
        LegacyScenarioCommandParameter parameter)
    {
        if (IsLegacyJumpTargetParameter(command, parameter))
        {
            if (command.JumpTargetCommandIndex.HasValue)
            {
                return $"第 {command.JumpTargetCommandIndex.Value} 条命令";
            }

            return command.JumpTargetOrdinal.HasValue
                ? $"目标 ord {command.JumpTargetOrdinal.Value}"
                : $"原位移 {parameter.IntValue}";
        }

        return parameter.Kind switch
        {
            LegacyScenarioParameterKind.Text => parameter.Text,
            LegacyScenarioParameterKind.VariableArray => parameter.Values.Count == 0
                ? "无"
                : string.Join(", ", parameter.Values.Select(FormatScriptNumber)),
            _ => FormatScriptNumber(parameter.IntValue)
        };
    }

    private static string BuildLegacyScriptParameterDisplayName(LegacyScenarioCommandNode command, LegacyScenarioCommandParameter parameter)
    {
        var number = parameter.Index + 1;
        var name = command.CommandId switch
        {
            0x05 => parameter.Kind == LegacyScenarioParameterKind.VariableArray
                ? parameter.Index switch
                {
                    0 => "true 变量数组",
                    1 => "false 变量数组",
                    _ => "变量列表"
                }
                : $"参数{number}",
            0x76 => parameter.Index == 0 ? "跳转到" : $"参数{number}",
            0x77 => parameter.Index switch
            {
                0 => "左侧变量类型",
                1 => "左侧变量编号",
                2 => "运算方式",
                3 => "右侧值类型",
                4 => "右侧值",
                _ => $"参数{number}"
            },
            0x78 => parameter.Index switch
            {
                0 => "整型变量编号",
                1 => "赋值方向",
                2 => "人物/变量",
                3 => "读取属性",
                _ => $"参数{number}"
            },
            0x79 => parameter.Index switch
            {
                0 => "左侧值类型",
                1 => "左侧值",
                2 => "比较方式",
                3 => "右侧值类型",
                4 => "右侧值",
                _ => $"参数{number}"
            },
            _ => parameter.Kind switch
            {
                LegacyScenarioParameterKind.Text => "文本",
                LegacyScenarioParameterKind.VariableArray => "变量列表",
                LegacyScenarioParameterKind.Dword32 => $"整数{number}",
                _ => $"参数{number}"
            }
        };

        return $"{number}. {name}";
    }

    private void ShowSelectedScriptText()
    {
        if (_bindingScriptDocument) return;
        var entry = GetSelectedScriptTextEntry();
        if (entry == null) return;
        _selectedScriptCommandRow = null;
        _selectedScriptTextEntry = entry;
        var relatedRows = GetScriptCommandsForText(entry);
        SuppressScriptSelectionEvents(() =>
        {
            if (relatedRows.Count > 0)
            {
                BindScriptCommandRows(relatedRows);
            }
            else
            {
                BindScriptCommandRows(Array.Empty<ScenarioStructureRow>());
            }
            BindScriptParameterRows(Array.Empty<ScenarioCommandParameterRow>());
        });
        _scriptInlineDialogHost.ClearDialog("文本参数请通过所属命令的旧版 Dialog 修改。");
        _scriptPreviewBox.Text = BuildScriptTextPreview(entry, relatedRows);
        _scriptDetailBox.Text = BuildScriptTextDetail(entry);
        ClearScriptImagePreview();
        UpdateScriptTextCapacityLabel();
        _scriptTextEditorBox.Focus();
    }

    private ScenarioTextEntry? GetSelectedScriptTextEntry()
    {
        if (_scriptTree.SelectedNode?.Tag is ScenarioTextEntry treeText)
        {
            return treeText;
        }

        return _selectedScriptTextEntry;
    }

    private void SelectScriptTextEntry(ScenarioTextEntry entry, bool showSelection = true)
    {
        var restoreBindingState = _bindingScriptDocument;
        if (!showSelection)
        {
            _bindingScriptDocument = true;
        }

        foreach (DataGridViewRow row in _scriptTextGrid.Rows)
        {
            if (row.DataBoundItem is not ScenarioTextEntry candidate || candidate.Offset != entry.Offset) continue;
            row.Selected = true;
            _scriptTextGrid.CurrentCell = row.Cells.Cast<DataGridViewCell>().First(cell => cell.Visible);
            _bindingScriptDocument = restoreBindingState;
            if (showSelection)
            {
                ShowSelectedScriptText();
            }
            return;
        }

        _bindingScriptDocument = restoreBindingState;
    }

    private ScenarioSearchResultRow? GetSelectedScriptSearchResultRow()
    {
        if (_scriptSearchResultGrid.SelectedRows.Count > 0 && _scriptSearchResultGrid.SelectedRows[0].DataBoundItem is ScenarioSearchResultRow selected) return selected;
        if (_scriptSearchResultGrid.CurrentRow?.DataBoundItem is ScenarioSearchResultRow current) return current;
        return null;
    }

    private void ShowSelectedScriptSearchResult()
    {
        if (_bindingScriptDocument) return;
        var result = GetSelectedScriptSearchResultRow();
        if (result == null) return;
        ShowScriptSearchResult(result);
    }

    private void ShowScriptSearchResult(ScenarioSearchResultRow result, string? prefix = null)
    {
        if (result.CommandRow != null)
        {
            var parameterRows = BuildScriptParameterRows(result.CommandRow);
            var textRows = GetScriptTextsForRows(new[] { result.CommandRow }, _currentScriptTextEntries).ToList();
            SuppressScriptSelectionEvents(() =>
            {
                BindScriptCommandRows(new[] { result.CommandRow });
                SelectScriptTreeNode(result.CommandRow, suppressEvents: true);
                BindScriptParameterRows(parameterRows);
            });
            ShowSelectedLegacyScriptParameter();
            _selectedScriptCommandRow = result.CommandRow;
            _selectedScriptTextEntry = null;
            _scriptTextEditorBox.Clear();
            UpdateScriptTextCapacityLabel();
            _scriptPreviewBox.Text = BuildScriptCommandPreview(result.CommandRow, parameterRows, textRows);
            UpdateScriptImagePreview(result.CommandRow);
            _scriptDetailBox.Text =
                (string.IsNullOrWhiteSpace(prefix) ? string.Empty : prefix + "\r\n\r\n") +
                $"搜索结果：#{result.Index} {result.Kind}\r\n{result.Location}\r\n\r\n" +
                BuildScriptRowDetail(result.CommandRow);
            return;
        }

        if (result.TextEntry != null)
        {
            var relatedRows = GetScriptCommandsForText(result.TextEntry);
            SuppressScriptSelectionEvents(() =>
            {
                if (relatedRows.Count > 0)
                {
                    BindScriptCommandRows(relatedRows);
                }
                else
                {
                    BindScriptCommandRows(Array.Empty<ScenarioStructureRow>());
                }
                BindScriptParameterRows(Array.Empty<ScenarioCommandParameterRow>());
                SelectScriptTextEntry(result.TextEntry, showSelection: false);
            });
            var selectedInTree = SelectScriptTextTreeNode(result.TextEntry, suppressEvents: true);
            if (!selectedInTree && relatedRows.Count > 0)
            {
                SelectScriptTreeNode(relatedRows[0], suppressEvents: true);
            }
            _selectedScriptCommandRow = null;
            _selectedScriptTextEntry = result.TextEntry;
            _scriptPreviewBox.Text = BuildScriptTextPreview(result.TextEntry, relatedRows);
            ClearScriptImagePreview();
            _scriptDetailBox.Text =
                (string.IsNullOrWhiteSpace(prefix) ? string.Empty : prefix + "\r\n\r\n") +
                $"搜索结果：#{result.Index} {result.Kind}\r\n{result.Location}\r\n\r\n" +
                BuildScriptTextDetail(result.TextEntry);
            UpdateScriptTextCapacityLabel();
        }
    }

    private string BuildScriptTextDetail(ScenarioTextEntry entry)
    {
        var relatedCommands = GetScriptCommandsForText(entry);
        var currentText = GetSelectedScriptTextEntry()?.Offset == entry.Offset
            ? _scriptTextEditorBox.Text
            : entry.Text;
        var currentBytes = EncodingService.GetGbkByteCount(currentText);
        var relatedPreview = relatedCommands.Count == 0
            ? "未命中明显关联命令。"
            : string.Join("\r\n", relatedCommands.Take(8).Select(row =>
                $"- Scene {row.SceneIndex} / Section {row.SectionIndex} / {row.CommandIndex:000} {row.CommandIdHex} {row.CommandName} {row.OffsetHex}"));
        return
            $"文本：#{entry.Index} {entry.Kind} {entry.OffsetHex}\r\n" +
            $"容量：GBK {currentBytes}/{entry.ByteLength} 字节，剩余 {entry.ByteLength - currentBytes} 字节\r\n" +
            $"写回状态：{entry.WriteStatus}\r\n" +
            BuildScenarioTextDecodeLine(entry) + "\r\n" +
            $"中文注释：{entry.Annotation}\r\n\r\n" +
            $"关联命令候选：\r\n{relatedPreview}\r\n\r\n" +
            (_legacyScriptTextByOffset.ContainsKey(entry.Offset)
                ? "说明：该文本来自旧版真实命令参数；保存时会完整重建剧本结构并重读校验，可随 Section 长度一起扩容。"
                : "说明：文本可在右侧编辑框修改；只能在原容量内短写回，工具会自动备份并复读校验。");
    }

    private static string BuildScenarioTextDecodeLine(ScenarioTextEntry entry)
    {
        var source = string.IsNullOrWhiteSpace(entry.SourceKind) ? "未知来源" : entry.SourceKind;
        var encoding = string.IsNullOrWhiteSpace(entry.EncodingName) ? "GBK" : entry.EncodingName;
        var confidence = string.IsNullOrWhiteSpace(entry.DecodeConfidence) ? "高" : entry.DecodeConfidence;
        var warning = string.IsNullOrWhiteSpace(entry.DecodeWarning) ? string.Empty : "；" + entry.DecodeWarning;
        var writable = entry.IsWritable ? "可写" : "只读";
        return $"解码：{source}；{encoding}；置信度 {confidence}；{writable}{warning}";
    }

    private void UpdateScriptTextCapacityLabel()
    {
        _scriptTextEditorBox.BackColor = SystemColors.Window;
        var entry = GetSelectedScriptTextEntry();
        if (entry == null)
        {
            _scriptTextCapacityLabel.Text = "文本：未选择";
            _scriptTextCapacityLabel.ForeColor = SystemColors.ControlText;
            _saveScriptTextButton.Enabled = false;
            return;
        }

        var currentText = BattlefieldEditorService.NormalizeText(_scriptTextEditorBox.Text);
        var byteCount = EncodingService.GetGbkByteCount(currentText);
        if (_legacyScriptTextByOffset.TryGetValue(entry.Offset, out var legacyText))
        {
            _scriptTextCapacityLabel.Text = $"旧版文本参数：GBK {byteCount} 字节；完整保存可随 Section 扩容。";
            _scriptTextCapacityLabel.ForeColor = SystemColors.ControlText;
            _scriptTextEditorBox.BackColor = SystemColors.Window;
            _saveScriptTextButton.Enabled = !string.Equals(currentText, legacyText.Parameter.Text, StringComparison.Ordinal);
            return;
        }

        var remaining = entry.ByteLength - byteCount;
        var changed = !string.Equals(currentText, BattlefieldEditorService.NormalizeText(entry.OriginalText), StringComparison.Ordinal);
        _scriptTextCapacityLabel.Text = $"文本：GBK {byteCount}/{entry.ByteLength} 字节，剩余 {remaining} 字节";
        _scriptTextCapacityLabel.ForeColor = remaining < 0 ? Color.DarkRed : SystemColors.ControlText;
        _scriptTextEditorBox.BackColor = remaining < 0 ? Color.MistyRose : SystemColors.Window;
        _saveScriptTextButton.Enabled = changed && remaining >= 0;
    }

    private void ApplyScriptSearch()
        => ApplyLegacyScriptSearch(LegacyScriptEditorScope.Script);

    private void ClearScriptSearch()
        => ClearLegacyScriptSearch(LegacyScriptEditorScope.Script);

    private bool SelectDefaultScriptTreeNode(bool ensureVisible = true)
    {
        if (_scriptTree.Nodes.Count == 0)
        {
            return false;
        }

        var preferred = _scriptTree.Nodes
            .Cast<TreeNode>()
            .SelectMany(EnumerateScriptTreeNodes)
            .FirstOrDefault(node => TryGetScriptTreeRow(node, out var row) && row.NodeType == "Section候选");
        if (preferred == null)
        {
            preferred = _scriptTree.Nodes
                .Cast<TreeNode>()
                .SelectMany(EnumerateScriptTreeNodes)
                .FirstOrDefault(node => TryGetScriptTreeRow(node, out var row) && row.NodeType == "Scene候选");
        }
        if (preferred == null)
        {
            preferred = _scriptTree.Nodes[0];
        }

        _scriptTree.SelectedNode = preferred;
        if (ensureVisible)
        {
            preferred.EnsureVisible();
        }
        return true;
    }

    private static ScenarioStructureRow? FindScriptOwnerRowForTextNode(TreeNode? node)
    {
        var current = node?.Parent;
        while (current != null)
        {
            if (TryGetScriptTreeRow(current, out var owner))
            {
                return owner;
            }

            current = current.Parent;
        }

        return null;
    }

    private static TreeNode? FindScriptOwnerTreeNode(TreeNode node, ScenarioStructureRow target, string ownerNodeType)
    {
        if (TryGetScriptTreeRow(node, out var row)
            && row.NodeType == ownerNodeType
            && row.SceneIndex == target.SceneIndex
            && (ownerNodeType != "Section候选" || row.SectionIndex == target.SectionIndex))
        {
            return node;
        }

        foreach (TreeNode child in node.Nodes)
        {
            var found = FindScriptOwnerTreeNode(child, target, ownerNodeType);
            if (found != null) return found;
        }

        return null;
    }

    private static bool TryGetScriptTreeRow(TreeNode? node, out ScenarioStructureRow row)
    {
        if (node?.Tag is ScenarioStructureRow direct)
        {
            row = direct;
            return true;
        }

        if (node?.Tag is LegacyScenarioItemData { UiRow: ScenarioStructureRow itemRow })
        {
            row = itemRow;
            return true;
        }

        row = null!;
        return false;
    }


    private static string BuildScenarioRelativePath(ScenarioFileInfo scenario)
    {
        if (ScenarioFileReader.IsRsScriptFile(scenario.FileName))
        {
            return Path.Combine("RS", scenario.FileName);
        }

        return Path.Combine("SV", scenario.FileName);
    }

    private void ShowScriptVariableUsageDialog(LegacyScriptEditorScope scope = LegacyScriptEditorScope.Script)
    {
        _scriptVariableUsageScope = scope;
        if (_scriptVariableUsageDialog is { IsDisposed: false })
        {
            _scriptVariableUsageDialog.RefreshCurrentScenario();
            _scriptVariableUsageDialog.Show(this);
            _scriptVariableUsageDialog.BringToFront();
            return;
        }

        _scriptVariableUsageDialog = new ScriptVariableUsageDialog(
            this,
            BuildCurrentScriptVariableUsageSnapshot,
            ScanProjectScriptVariablesAsync,
            NavigateScriptVariableOccurrenceAsync,
            EditScriptVariableOccurrenceAsync);
        _scriptVariableUsageDialog.FormClosed += (_, _) => _scriptVariableUsageDialog = null;
        _scriptVariableUsageDialog.Show(this);
    }

    private ScriptVariableUsageSnapshot? BuildCurrentScriptVariableUsageSnapshot()
    {
        var document = GetCurrentLegacyScriptDocument(_scriptVariableUsageScope);
        if (document == null)
        {
            return null;
        }

        var snapshot = _scriptVariableUsageService.BuildCurrentScenarioSnapshot(document);
        return new ScriptVariableUsageSnapshot
        {
            Summaries = snapshot.Summaries,
            Occurrences = snapshot.Occurrences,
            SourceLabel = snapshot.SourceLabel,
            BuiltAt = snapshot.BuiltAt,
            VersionKey = BuildCurrentScriptVariableUsageVersionKey(document)
        };
    }

    private static string BuildCurrentScriptVariableUsageVersionKey(LegacyScenarioDocument document)
    {
        var hash = new HashCode();
        hash.Add(document.FilePath, StringComparer.OrdinalIgnoreCase);
        hash.Add(document.SceneCount);
        hash.Add(document.SectionCount);

        foreach (var command in document.EnumerateCommands())
        {
            hash.Add(command.SceneIndex);
            hash.Add(command.SectionIndex);
            hash.Add(command.CommandIndex);
            hash.Add(command.CommandOrdinal);
            hash.Add(command.CommandId);
            hash.Add(command.FileOffset);
            hash.Add(command.ConsumedBytes);
            hash.Add(command.Parameters.Count);

            foreach (var parameter in command.Parameters)
            {
                hash.Add(parameter.Index);
                hash.Add(parameter.LayoutCode);
                hash.Add(parameter.Tag);
                hash.Add(parameter.FileOffset);
                hash.Add((int)parameter.Kind);
                hash.Add(parameter.IntValue);
                hash.Add(parameter.Text, StringComparer.Ordinal);
                hash.Add(parameter.ByteLength);
                hash.Add(parameter.Values.Count);
                foreach (var value in parameter.Values)
                {
                    hash.Add(value);
                }
            }
        }

        return hash.ToHashCode().ToString("X8", CultureInfo.InvariantCulture);
    }

    private async Task<ScriptVariableProjectScanResult?> ScanProjectScriptVariablesAsync(
        IProgress<ScriptVariableProjectScanProgress> progress,
        CancellationToken cancellationToken)
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe();
        if (dictionary == null)
        {
            MessageBox.Show(this, "缺少 CczString.ini，无法按旧版完整树扫描剧本变量。", "缺少剧本字典", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        var project = _project;
        var cacheKey = BuildScriptVariableProjectCacheKey(project);
        if (_scriptVariableProjectCache != null &&
            _scriptVariableProjectCacheKey.Equals(cacheKey, StringComparison.Ordinal))
        {
            return _scriptVariableProjectCache;
        }

        var result = await Task.Run(
            () => _scriptVariableUsageService.ScanProject(project, dictionary, progress, cancellationToken),
            cancellationToken);
        _scriptVariableProjectCache = result;
        _scriptVariableProjectCacheKey = cacheKey;
        return result;
    }

    private string BuildScriptVariableProjectCacheKey(CczProject project)
    {
        var parts = _scenarioFileReader.ReadAllIndex(project)
            .Where(x => ScenarioFileReader.IsRsScriptFile(x.FileName))
            .Select(x =>
            {
                var info = new FileInfo(x.Path);
                return $"{x.FileName}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
            })
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        return string.Join("|", parts);
    }

    private void InvalidateScriptVariableProjectCache()
    {
        _scriptVariableProjectCache = null;
        _scriptVariableProjectCacheKey = string.Empty;
        _scriptVariableUsageDialog?.InvalidateProjectSnapshot();
    }

    private async Task NavigateScriptVariableOccurrenceAsync(ScriptVariableOccurrence occurrence, bool edit)
    {
        var scope = GetScriptVariableOccurrenceNavigationScope(occurrence);
        if (!await EnsureScriptVariableOccurrenceScenarioLoadedAsync(occurrence, scope))
        {
            return;
        }

        if (!TryFindScriptVariableCommand(scope, occurrence, out var command))
        {
            MessageBox.Show(this, "当前剧本已重新加载或修改，无法按原位置找到该变量所在命令。请刷新变量列表后重试。", "定位失败", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!TrySelectLegacyScriptCommand(scope, command))
        {
            MessageBox.Show(this, "左侧事件树中没有找到对应命令节点。", "定位失败", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ShowSelectedLegacyScriptTreeNode(scope);
        if (TrySelectScriptVariableParameterRow(scope, occurrence.ParameterIndex))
        {
            ShowSelectedScriptVariableParameter(scope);
        }

        SetStatus($"剧本变量：已定位 {occurrence.ScenarioFileName} {occurrence.VariableType} {occurrence.VariableAddressText} / {occurrence.CommandIdHex} {occurrence.CommandName}");

        if (edit)
        {
            EditSelectedLegacyItemDataCommand(scope);
        }
    }

    private async Task EditScriptVariableOccurrenceAsync(ScriptVariableOccurrence occurrence)
    {
        if (!occurrence.CanEdit)
        {
            MessageBox.Show(this, occurrence.EditHint, "该变量暂不可编辑", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        await NavigateScriptVariableOccurrenceAsync(occurrence, edit: true);
    }

    private LegacyScriptEditorScope GetScriptVariableOccurrenceNavigationScope(ScriptVariableOccurrence occurrence)
    {
        var scopeScenarioName = GetLegacyScriptScenarioName(_scriptVariableUsageScope);
        if (GetCurrentLegacyScriptDocument(_scriptVariableUsageScope) != null &&
            !string.IsNullOrWhiteSpace(scopeScenarioName) &&
            scopeScenarioName.Equals(occurrence.ScenarioFileName, StringComparison.OrdinalIgnoreCase))
        {
            return _scriptVariableUsageScope;
        }

        return LegacyScriptEditorScope.Script;
    }

    private async Task<bool> EnsureScriptVariableOccurrenceScenarioLoadedAsync(
        ScriptVariableOccurrence occurrence,
        LegacyScriptEditorScope scope)
    {
        if (scope != LegacyScriptEditorScope.Script)
        {
            SelectTabPageByText(scope == LegacyScriptEditorScope.Battlefield ? "战场编辑" : "场景编辑");
            return true;
        }

        return await EnsureScriptScenarioLoadedAsync(occurrence.ScenarioFileName) &&
               _currentLegacyScriptDocument != null;
    }
    private bool TryFindScriptVariableCommand(
        LegacyScriptEditorScope scope,
        ScriptVariableOccurrence occurrence,
        out LegacyScenarioCommandNode command)
    {
        command = null!;
        var document = GetCurrentLegacyScriptDocument(scope);
        if (document == null)
        {
            return false;
        }

        command = document.EnumerateCommands().FirstOrDefault(candidate =>
            candidate.SceneIndex == occurrence.SceneIndex &&
            candidate.SectionIndex == occurrence.SectionIndex &&
            candidate.CommandIndex == occurrence.CommandIndex &&
            candidate.CommandOrdinal == occurrence.CommandOrdinal &&
            candidate.CommandId == occurrence.CommandId &&
            candidate.FileOffset == occurrence.CommandOffset)!;
        if (command != null)
        {
            return true;
        }

        command = document.EnumerateCommands().FirstOrDefault(candidate =>
            candidate.SceneIndex == occurrence.SceneIndex &&
            candidate.SectionIndex == occurrence.SectionIndex &&
            candidate.CommandIndex == occurrence.CommandIndex &&
            candidate.CommandId == occurrence.CommandId)!;
        return command != null;
    }

    private bool TrySelectScriptVariableParameterRow(LegacyScriptEditorScope scope, int parameterIndex)
        => scope switch
        {
            LegacyScriptEditorScope.Script => TrySelectLegacyScriptParameterRow(parameterIndex),
            LegacyScriptEditorScope.Battlefield => TrySelectBattlefieldScriptParameterRow(parameterIndex),
            LegacyScriptEditorScope.RScene => true,
            _ => false
        };

    private void ShowSelectedScriptVariableParameter(LegacyScriptEditorScope scope)
    {
        switch (scope)
        {
            case LegacyScriptEditorScope.Script:
                ShowSelectedLegacyScriptParameter();
                break;
            case LegacyScriptEditorScope.Battlefield:
                ShowSelectedBattlefieldScriptParameter();
                break;
            case LegacyScriptEditorScope.RScene:
                LoadInlineRSceneScriptDialogForSelection();
                break;
        }
    }

    private async Task SaveSelectedScriptTextAsync()
    {
        if (_project == null || _currentScriptScenario == null)
        {
            MessageBox.Show(this, "请先读取一个剧本。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var entry = GetSelectedScriptTextEntry();
        if (entry == null)
        {
            MessageBox.Show(this, "请先在左侧事件树中选择一条文本参数。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!entry.IsWritable)
        {
            MessageBox.Show(this, "该文本候选解码置信度低或来源未确认，当前只读，不能写回。", "文本只读", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var newText = BattlefieldEditorService.NormalizeText(_scriptTextEditorBox.Text);
        if (newText.Contains('\0'))
        {
            MessageBox.Show(this, "剧本文本不能包含 NUL/零字节。", "文本校验失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_currentLegacyScriptDocument != null && _legacyScriptTextByOffset.TryGetValue(entry.Offset, out var legacyText))
        {
            if (string.Equals(newText, legacyText.Parameter.Text, StringComparison.Ordinal))
            {
                MessageBox.Show(this, "选中文本没有检测到改动。", "无需保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(this,
                    $"即将按旧版完整结构保存 {_currentScriptScenario.FileName}。\r\n\r\n文本参数：{entry.OffsetHex}\r\n命令：Scene {legacyText.Command.SceneIndex} / Section {legacyText.Command.SectionIndex} / Command {legacyText.Command.CommandIndex}\r\n\r\n保存会重建 Scene 偏移、Section/子块长度和 0x76 跳转；保存前自动备份，替换前重读校验。是否继续？",
                    "确认完整保存剧本文本",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            var oldParameterText = legacyText.Parameter.Text;
            var oldParameterByteLength = legacyText.Parameter.ByteLength;
            try
            {
                Cursor = Cursors.WaitCursor;
                legacyText.Parameter.Text = newText;
                legacyText.Parameter.ByteLength = EncodingService.GetGbkByteCount(newText) + 1;
                entry.Text = newText;
                var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe()
                    ?? throw new InvalidOperationException("缺少 CczString.ini，无法完成旧版结构写回校验。");
                var project = _project;
                var scenarioPath = BuildScenarioRelativePath(_currentScriptScenario);
                var document = _currentLegacyScriptDocument;
                var result = await Task.Run(() => _legacyScenarioWriter.Save(
                    project,
                    scenarioPath,
                    document,
                    dictionary,
                    "剧本制作页旧版文本参数完整保存"));

                if (!RefreshLegacyScriptCommandInPlace(legacyText.Command))
                {
                    RefreshLegacyScriptView(legacyText.Command);
                }
                MarkLegacyScriptSavedInPlace(result);
                System.Diagnostics.Debug.WriteLine($"已完整保存旧版剧本文本：{_currentScriptScenario.FileName} offset={entry.OffsetHex} backup={result.BackupPath}");
                SetStatus($"旧版剧本完整保存完成：{_currentScriptScenario.FileName} {entry.OffsetHex}");
                MessageBox.Show(this, $"完整保存完成。\r\n校验：{result.ValidationSummary}\r\n备份：{result.BackupPath}\r\n报告：{result.ReportJsonPath}", "剧本制作保存完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                legacyText.Parameter.Text = oldParameterText;
                legacyText.Parameter.ByteLength = oldParameterByteLength;
                entry.Text = oldParameterText;
                System.Diagnostics.Debug.WriteLine("完整保存旧版剧本文本失败：" + ex);
                MessageBox.Show(this, ex.Message, "完整保存旧版剧本文本失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            return;
        }

        var bytes = EncodingService.GetGbkByteCount(newText);
        if (bytes > entry.ByteLength)
        {
            MessageBox.Show(this, $"GBK 字节数 {bytes} 超过原地容量 {entry.ByteLength}，请缩短后再保存。", "文本容量校验失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.Equals(newText, BattlefieldEditorService.NormalizeText(entry.OriginalText), StringComparison.Ordinal))
        {
            MessageBox.Show(this, "选中文本没有检测到改动。", "无需保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this,
                $"即将写入 RS\\{_currentScriptScenario.FileName} 的文本 {entry.OffsetHex}。\r\n\r\n只写该文本线索，未知命令结构保持原样；保存前自动备份，保存后复读校验。是否继续？",
                "确认保存剧本文本",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var oldEntryText = entry.Text;
        try
        {
            Cursor = Cursors.WaitCursor;
            entry.Text = newText;
            var project = _project;
            var scenarioPath = BuildScenarioRelativePath(_currentScriptScenario);
            var saveResult = await Task.Run(() =>
            {
                var result = _scenarioTextWriter.SaveInPlace(project, scenarioPath, new[] { entry }, "剧本制作页保存文本前自动备份");
                var reread = _scenarioTextReader.Read(result.FilePath);
                var actual = reread.FirstOrDefault(x => x.Offset == entry.Offset);
                return (Result: result, ActualText: actual?.Text);
            });
            if (saveResult.ActualText == null || !BattlefieldEditorService.NormalizeText(saveResult.ActualText).Equals(newText, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"剧本文本保存后复读校验失败：期望“{newText}”，实际“{saveResult.ActualText ?? "<未找到>"}”。");
            }

            var result = saveResult.Result;
            UpdateScriptTextEntryAfterSave(entry, saveResult.ActualText);
            UpdateScriptTextTreeNodes(entry);
            if (_scriptTree.SelectedNode?.Tag is ScenarioTextEntry selectedText && selectedText.Offset == entry.Offset)
            {
                ShowSelectedScriptTreeNode();
            }
            else
            {
                RefreshScriptTextRows(new[] { entry });
                UpdateScriptTextCapacityLabel();
            }
            _scriptDetailBox.Text += $"\r\n\r\n保存完成：写入 {result.EntriesWritten} 条，变化 {result.ChangedBytes} 字节。\r\n备份：{result.BackupPath}\r\n报告：{result.ReportJsonPath}";
            System.Diagnostics.Debug.WriteLine($"已保存剧本文本：{_currentScriptScenario.FileName} offset={entry.OffsetHex} backup={result.BackupPath}");
            SetStatus($"剧本制作保存完成：{_currentScriptScenario.FileName} {entry.OffsetHex}");
            MessageBox.Show(this, $"保存完成。\r\n备份：{result.BackupPath}\r\n报告：{result.ReportJsonPath}", "剧本制作保存完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            entry.Text = oldEntryText;
            System.Diagnostics.Debug.WriteLine("保存剧本文本失败：" + ex);
            MessageBox.Show(this, ex.Message, "保存剧本文本失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private async Task JumpScriptBattlefieldAsync()
    {
        var scenarioName = _currentScriptScenario?.FileName;
        if (string.IsNullOrWhiteSpace(scenarioName)) return;
        SelectTabPageByText("战场编辑");
        if (_battlefieldScenarioCombo.Items.Count == 0)
        {
            await LoadBattlefieldScenariosAsync();
        }

        foreach (var item in _battlefieldScenarioCombo.Items)
        {
            if (item is not ScenarioFileInfo scenario || !scenario.FileName.Equals(scenarioName, StringComparison.OrdinalIgnoreCase)) continue;
            _updatingBattlefieldScenarioSelection = true;
            try
            {
                _battlefieldScenarioCombo.SelectedItem = item;
            }
            finally
            {
                _updatingBattlefieldScenarioSelection = false;
            }

            await LoadSelectedBattlefieldScenarioAsync();
            return;
        }
    }

    private async Task SaveCurrentLegacyScriptStructureAsync()
        => await SaveCurrentLegacyScriptStructureCoreAsync();

    private async Task<bool> SaveCurrentLegacyScriptStructureCoreAsync()
    {
        if (_project == null || _currentScriptScenario == null || _currentLegacyScriptDocument == null)
        {
            MessageBox.Show(this, "当前剧本没有进入旧版完整树模式，无法完整保存。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe()
                ?? throw new InvalidOperationException("缺少 CczString.ini，无法完成旧版结构写回校验。");
            var result = await Task.Run(() => _legacyScenarioWriter.Save(
                _project,
                BuildScenarioRelativePath(_currentScriptScenario),
                _currentLegacyScriptDocument,
                dictionary,
                "剧本制作页旧版完整结构保存"));

            MarkLegacyScriptSavedInPlace(result);
            System.Diagnostics.Debug.WriteLine($"已完整保存旧版剧本：{_currentScriptScenario.FileName} backup={result.BackupPath}");
            SetStatus($"旧版剧本完整保存完成：{_currentScriptScenario.FileName}");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("完整保存旧版剧本失败：" + ex);
            MessageBox.Show(this, ex.Message, "完整保存旧版剧本失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            InvalidateScriptVariableProjectCache();
            Cursor = Cursors.Default;
        }
    }

    private void LoadScenarioFiles()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe();
            _currentScenarioFiles = _scenarioFileReader.ReadAllIndex(_project);
            PopulateScenarioKindFilter();
            BindScenarioFileRows(_currentScenarioFiles);
            _currentScenarioCommandProbeRows = Array.Empty<ScenarioCommandProbeRow>();
            _scenarioCommandProbeGrid.DataSource = null;
            _currentScenarioStructureResult = null;
            _scenarioStructureGrid.DataSource = null;
            _scenarioStructureTree.Nodes.Clear();
            _scenarioStructureNodeInfoBox.Clear();
            _scenarioStructureXmlBox.Clear();
            ResetScenarioStructureFilterControls();
            _currentScenarioTextEntries = Array.Empty<ScenarioTextEntry>();
            _scenarioTextGrid.DataSource = null;
            _probeScenarioCommandsButton.Enabled = dictionary != null && _currentScenarioFiles.Count > 0;
            _buildScenarioStructureButton.Enabled = dictionary != null && _currentScenarioFiles.Count > 0;
            _exportScenarioStructureXmlButton.Enabled = false;
            _probeScenarioTextsButton.Enabled = _currentScenarioFiles.Count > 0;
            UpdateScenarioFileInfo(_currentScenarioFiles.Count, "\u5168\u90e8", string.Empty, dictionary);
            System.Diagnostics.Debug.WriteLine($"已读取 R/S eex 剧本探针：{_currentScenarioFiles.Count} 个文件。");
            SetStatus("R/S eex 高级探针读取完成");
        }
        catch (Exception ex)
        {
            _scenarioFileInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("R/S eex高级探针读取失败：" + ex);
            MessageBox.Show(this, ex.Message, "R/S eex高级探针读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private SceneStringDocument? TryReadSceneDictionaryForProbe()
    {
        if (_project == null) return null;
        var path = ProjectDetector.FindSceneDictionaryPath(_project);
        if (!File.Exists(path)) return null;
        try
        {
            var dictionary = _sceneStringParser.Parse(path);
            _currentSceneStringDocument ??= dictionary;
            return dictionary;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("R/S 探针读取命令字典失败，继续无字典扫描：" + ex.Message);
            return null;
        }
    }

    private void ConfigureScenarioFileGrid()
    {
        foreach (DataGridViewColumn column in _scenarioFileGrid.Columns)
        {
            if (column.DataPropertyName is nameof(ScenarioFileInfo.FirstWordsHex)
                or nameof(ScenarioFileInfo.TopWordsHex)
                or nameof(ScenarioFileInfo.FirstCommandNames)
                or nameof(ScenarioFileInfo.TextHints)
                or nameof(ScenarioFileInfo.Annotation)
                or nameof(ScenarioFileInfo.UsageAnnotation))
            {
                column.Width = 340;
            }
            else if (column.DataPropertyName == nameof(ScenarioFileInfo.Path))
            {
                column.Width = 260;
            }
        }
    }


    private void BindScenarioFileRows(IEnumerable<ScenarioFileInfo> rows)
    {
        _scenarioFileGrid.DataSource = new BindingList<ScenarioFileInfo>(rows.ToList());
        ConfigureScenarioFileGrid();
    }

    private void PopulateScenarioKindFilter()
    {
        var previous = Convert.ToString(_scenarioKindFilterCombo.SelectedItem, CultureInfo.InvariantCulture);
        _scenarioKindFilterCombo.Items.Clear();
        _scenarioKindFilterCombo.Items.Add("\u5168\u90e8");
        foreach (var kind in _currentScenarioFiles.Select(x => x.Kind).Distinct().OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            _scenarioKindFilterCombo.Items.Add(kind);
        }
        SelectComboValueOrFirst(_scenarioKindFilterCombo, previous);
    }

    private void ApplyScenarioFileFilter()
    {
        if (_currentScenarioFiles.Count == 0) return;
        var kind = Convert.ToString(_scenarioKindFilterCombo.SelectedItem, CultureInfo.InvariantCulture) ?? "\u5168\u90e8";
        var keyword = _scenarioFileSearchBox.Text.Trim();
        var textOnly = _scenarioFilesWithTextOnly.Checked;
        var filtered = _currentScenarioFiles.Where(item =>
            (kind == "\u5168\u90e8" || string.Equals(item.Kind, kind, StringComparison.Ordinal)) &&
            (!textOnly || item.TextHintCount > 0) &&
            (string.IsNullOrWhiteSpace(keyword) || ScenarioFileMatchesKeyword(item, keyword)))
            .ToList();
        BindScenarioFileRows(filtered);
        UpdateScenarioFileInfo(filtered.Count, kind, keyword, null);
        SetStatus($"R/S eex \u7b5b\u9009\uff1a{filtered.Count}/{_currentScenarioFiles.Count}");
    }

    private void ClearScenarioFileFilter()
    {
        _scenarioFileSearchBox.Clear();
        _scenarioFilesWithTextOnly.Checked = false;
        if (_scenarioKindFilterCombo.Items.Count > 0) _scenarioKindFilterCombo.SelectedIndex = 0;
        BindScenarioFileRows(_currentScenarioFiles);
        UpdateScenarioFileInfo(_currentScenarioFiles.Count, "\u5168\u90e8", string.Empty, null);
        SetStatus("\u5df2\u663e\u793a\u5168\u90e8 R/S eex \u6587\u4ef6");
    }

    private static bool ScenarioFileMatchesKeyword(ScenarioFileInfo item, string keyword)
    {
        return ContainsKeyword(item.FileName, keyword) ||
               ContainsKeyword(item.Id, keyword) ||
               ContainsKeyword(item.Kind, keyword) ||
               ContainsKeyword(item.TitleHint, keyword) ||
               ContainsKeyword(item.FirstTextOffsetHex, keyword) ||
               ContainsKeyword(item.LastNonZeroOffsetHex, keyword) ||
               ContainsKeyword(item.FirstWordsHex, keyword) ||
               ContainsKeyword(item.TopWordsHex, keyword) ||
               ContainsKeyword(item.FirstCommandNames, keyword) ||
               ContainsKeyword(item.TextHints, keyword) ||
               ContainsKeyword(item.Annotation, keyword) ||
               ContainsKeyword(item.UsageAnnotation, keyword) ||
               ContainsKeyword(item.Path, keyword);
    }

    private void UpdateScenarioFileInfo(int visibleCount, string kind, string keyword, SceneStringDocument? dictionary)
    {
        if (_project == null) return;
        var summary = string.Join("\uff0c", _currentScenarioFiles
            .GroupBy(x => x.Kind)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key}:{g.Count()}"));
        var withText = _currentScenarioFiles.Count(x => x.TextHintCount > 0);
        var filterText = kind == "\u5168\u90e8" && string.IsNullOrWhiteSpace(keyword) && !_scenarioFilesWithTextOnly.Checked
            ? "\u672a\u7b5b\u9009"
            : $"\u7c7b\u578b={kind}\uff0c\u5173\u952e\u5b57={keyword}\uff0c\u4ec5\u6709\u6587\u672c={_scenarioFilesWithTextOnly.Checked}";
        var dictionaryText = dictionary == null
            ? (_currentSceneStringDocument == null ? "未加载/自动探测" : $"{_currentSceneStringDocument.Commands.Count} 条；{_currentSceneStringDocument.DecodeDiagnostic}")
            : $"{dictionary.Commands.Count} 条；{dictionary.DecodeDiagnostic}";
        _scenarioFileInfoBox.Text =
            $"SV \u76ee\u5f55\uff1a{Path.Combine(_project.GameRoot, "RS")}\r\n" +
            $"\u6587\u4ef6\u6570\uff1a{_currentScenarioFiles.Count}    \u5f53\u524d\u663e\u793a\uff1a{visibleCount}    \u5206\u7c7b\uff1a{summary}    \u68c0\u51fa\u6587\u672c\u7ebf\u7d22\uff1a{withText}\r\n" +
            $"\u7b5b\u9009\uff1a{filterText}    \u547d\u4ee4\u5b57\u5178\uff1a{dictionaryText}\r\n" +
            "\u5f53\u524d\u4e3a\u53ea\u8bfb\u63a2\u9488\uff1a\u6309 16 \u4f4d\u5b57\u6d41\u3001\u5360\u7528\u8303\u56f4\u3001\u5e38\u89c1\u8bcd\u5206\u5e03\u3001\u6587\u672c\u7ebf\u7d22\u548c\u547d\u4ee4\u5019\u9009\u505a\u53ef\u89c6\u5316\uff1b\u5c1a\u672a\u63a8\u65ad\u5b8c\u6574\u547d\u4ee4\u53c2\u6570\u957f\u5ea6\uff0c\u6682\u4e0d\u5199\u5165\u3002";
    }

    private ScenarioFileInfo? GetSelectedScenarioFileItem()
    {
        if (_scenarioFileGrid.SelectedRows.Count > 0 && _scenarioFileGrid.SelectedRows[0].DataBoundItem is ScenarioFileInfo selectedItem) return selectedItem;
        if (_scenarioFileGrid.CurrentRow?.DataBoundItem is ScenarioFileInfo currentItem) return currentItem;
        return null;
    }

    private void OpenSelectedScenarioFileLocation()
    {
        var item = GetSelectedScenarioFileItem();
        if (item == null)
        {
            MessageBox.Show(this, "\u8bf7\u5148\u5728 SV \u5267\u672c/\u5173\u5361\u9875\u9009\u62e9\u4e00\u4e2a\u6587\u4ef6\u3002", "\u63d0\u793a", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        OpenFileLocation(item.Path);
    }

    private void ExportScenarioFileIndexCsv() =>
        ExportGridItemsCsv<ScenarioFileInfo>(_scenarioFileGrid, "\u5bfc\u51fa R/S eex \u5267\u672c\u7d22\u5f15", "RS\u5267\u672c\u7d22\u5f15.csv", "ScenarioFiles", "R/S eex\u5267\u672c\u7d22\u5f15");

    private void ShowSelectedScenarioFile()
    {
        if (_scenarioFileGrid.SelectedRows.Count == 0) return;
        if (_scenarioFileGrid.SelectedRows[0].DataBoundItem is not ScenarioFileInfo item) return;

        _currentScenarioCommandProbeRows = Array.Empty<ScenarioCommandProbeRow>();
        _scenarioCommandProbeGrid.DataSource = null;
        _currentScenarioStructureResult = null;
        _scenarioStructureGrid.DataSource = null;
        _scenarioStructureTree.Nodes.Clear();
        _scenarioStructureNodeInfoBox.Clear();
        _scenarioStructureXmlBox.Clear();
        ClearScenarioCommandReferenceTargets();
        ResetScenarioStructureFilterControls();
        _exportScenarioStructureXmlButton.Enabled = false;
        _currentScenarioTextEntries = Array.Empty<ScenarioTextEntry>();
        _scenarioTextGrid.DataSource = null;
        _exportScenarioTextsButton.Enabled = false;
        _saveScenarioTextsButton.Enabled = false;
        _scenarioTextFilterBox.Clear();
        _scenarioTextFilterButton.Enabled = false;
        _scenarioTextFilterClearButton.Enabled = false;
        _scenarioTextChangedOnly.Checked = false;
        _scenarioTextChangedOnly.Enabled = false;

        _scenarioFileInfoBox.Text =
            $"文件：{item.FileName}    ID：{item.Id}    类型：{item.Kind}    长度：{item.Length:N0} 字节\r\n" +
            $"路径：{item.Path}\r\n" +
            $"16位词：{item.WordCount:N0}    非零词：{item.NonZeroWordCount:N0}    不同词：{item.DistinctWordCount:N0}\r\n" +
            $"占用估算：{item.UsedBytes:N0} 字节 ({item.UsedPercent:F1}%)    最后非零偏移：{item.LastNonZeroOffsetHex}\r\n" +
            $"标题/文本首项：{item.TitleHint}    首个文本偏移：{item.FirstTextOffsetHex}    文本线索数：{item.TextHintCount}\r\n" +
            $"前 24 个 16位词：{item.FirstWordsHex}\r\n" +
            $"高频词：{item.TopWordsHex}\r\n" +
            $"命令候选({item.RecognizedCommandCount})：{item.FirstCommandNames}\r\n" +
            $"文本线索：{item.TextHints}";
        SetStatus($"R/S eex：{item.FileName}");
    }

    private void ProbeSelectedScenarioCommands()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_scenarioFileGrid.SelectedRows.Count == 0 || _scenarioFileGrid.SelectedRows[0].DataBoundItem is not ScenarioFileInfo item)
        {
            MessageBox.Show(this, "请先在上方剧本列表选择一个 R/S eex 文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe();
        if (dictionary == null || dictionary.Commands.Count == 0)
        {
            MessageBox.Show(this, "找不到有效的 CczString.ini 命令字典，无法进行命令候选探测。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            _currentScenarioCommandProbeRows = _scenarioCommandProbeReader.Probe(item.Path, dictionary);
            _scenarioCommandProbeGrid.DataSource = new BindingList<ScenarioCommandProbeRow>(_currentScenarioCommandProbeRows.ToList());
            ConfigureScenarioCommandProbeGrid();
            var unique = _currentScenarioCommandProbeRows.Select(x => x.CommandId).Distinct().Count();
            _scenarioFileInfoBox.Text =
                $"文件：{item.FileName}    标题/文本首项：{item.TitleHint}\r\n" +
                $"命令候选：{_currentScenarioCommandProbeRows.Count} 行    不同命令：{unique}    字典：{dictionary.Commands.Count} 条\r\n" +
                "说明：当前为逐 16 位字扫描的命令候选树雏形，用于定位 Scene/Section/Command；尚未确认每条命令参数长度，因此只读、不写入。\r\n" +
                $"路径：{item.Path}";
            System.Diagnostics.Debug.WriteLine($"已探测剧本命令候选：{item.FileName}，候选 {_currentScenarioCommandProbeRows.Count} 行。");
            SetStatus($"剧本命令候选：{item.FileName}");
        }
        catch (Exception ex)
        {
            _scenarioFileInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("剧本命令候选探测失败：" + ex);
            MessageBox.Show(this, ex.Message, "剧本命令候选探测失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void BuildSelectedScenarioStructure()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_scenarioFileGrid.SelectedRows.Count == 0 || _scenarioFileGrid.SelectedRows[0].DataBoundItem is not ScenarioFileInfo item)
        {
            MessageBox.Show(this, "请先在上方剧本列表选择一个 R/S eex 文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe();
        if (dictionary == null || dictionary.Commands.Count == 0)
        {
            MessageBox.Show(this, "找不到有效的 CczString.ini 命令字典，无法生成结构草图。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            _currentScenarioStructureResult = _scenarioStructureProbeReader.Build(item.Path, dictionary, project: _project, tables: _tables);
            EnableScenarioStructureFilterControls(true);
            ApplyScenarioStructureFilter();
            _scenarioStructureXmlBox.Text = _currentScenarioStructureResult.XmlText;
            _exportScenarioStructureXmlButton.Enabled = true;
            _scenarioFileInfoBox.Text =
                $"文件：{item.FileName}    标题/文本首项：{item.TitleHint}\r\n" +
                $"{_currentScenarioStructureResult.Summary}\r\n" +
                "说明：结构草图把命令候选按“结束Scene/结束Section/事件结束”等标记组织成树状层级，并提供 XML 文本视图；当前只读，不写回。\r\n" +
                $"路径：{item.Path}";
            System.Diagnostics.Debug.WriteLine($"已生成剧本结构草图：{item.FileName}，Scene={_currentScenarioStructureResult.SceneCount}，Section={_currentScenarioStructureResult.SectionCount}，Command={_currentScenarioStructureResult.CommandCandidateCount}。");
            SetStatus($"剧本结构草图：{item.FileName}");
        }
        catch (Exception ex)
        {
            _scenarioFileInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("剧本结构草图生成失败：" + ex);
            MessageBox.Show(this, ex.Message, "剧本结构草图生成失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ExportScenarioStructureXml()
    {
        if (_project == null || _currentScenarioStructureResult == null)
        {
            MessageBox.Show(this, "请先生成结构草图。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var exportRoot = Path.Combine(_project.WorkspaceRoot, "CCZModStudio_Exports");
            Directory.CreateDirectory(exportRoot);
            var baseName = Path.GetFileNameWithoutExtension(_currentScenarioStructureResult.FileName);
            var path = Path.Combine(exportRoot, $"{baseName}_ScenarioStructureProbe.xml");
            File.WriteAllText(path, _currentScenarioStructureResult.XmlText, System.Text.Encoding.UTF8);
            System.Diagnostics.Debug.WriteLine("已导出剧本结构 XML：" + path);
            SetStatus("剧本结构 XML 已导出");
            MessageBox.Show(this, "已导出：" + path, "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("导出剧本结构 XML 失败：" + ex);
            MessageBox.Show(this, ex.Message, "导出剧本结构 XML 失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadScenarioCommandTemplates(bool silent = false)
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe();
            _currentScenarioCommandTemplateItems = _scenarioCommandParameterTemplateService.BuildCatalogItems(dictionary);
            PopulateScenarioCommandTemplateFilters();
            BindScenarioCommandTemplateRows(_currentScenarioCommandTemplateItems);
            _filterScenarioCommandTemplatesButton.Enabled = _currentScenarioCommandTemplateItems.Count > 0;
            _clearScenarioCommandTemplateFilterButton.Enabled = _currentScenarioCommandTemplateItems.Count > 0;
            _exportScenarioCommandTemplateCatalogButton.Enabled = _project != null;

            var covered = _currentScenarioCommandTemplateItems.Count(item => item.Status == "已覆盖");
            var missing = _currentScenarioCommandTemplateItems.Count(item => item.Status == "待补充");
            var dictionaryText = dictionary == null ? "未提供字典，当前显示内置模板" : $"{dictionary.Commands.Count} 条字典命令";
            System.Diagnostics.Debug.WriteLine($"已加载 R/S eex 命令模板目录：{_currentScenarioCommandTemplateItems.Count} 行，已覆盖 {covered}，待补充 {missing}，{dictionaryText}。");
            if (!silent)
            {
                SetStatus($"R/S命令模板目录：{_currentScenarioCommandTemplateItems.Count} 行，已覆盖 {covered}，待补充 {missing}");
            }
        }
        catch (Exception ex)
        {
            _scenarioCommandTemplateInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("加载 R/S eex 命令模板目录失败：" + ex);
            if (!silent)
            {
                MessageBox.Show(this, ex.Message, "加载 R/S eex 命令模板目录失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void PopulateScenarioCommandTemplateFilters()
    {
        _updatingScenarioCommandTemplateFilters = true;
        try
        {
            var previousCategory = Convert.ToString(_scenarioCommandTemplateCategoryCombo.SelectedItem, CultureInfo.InvariantCulture);
            var previousStatus = Convert.ToString(_scenarioCommandTemplateStatusCombo.SelectedItem, CultureInfo.InvariantCulture);

            _scenarioCommandTemplateCategoryCombo.Items.Clear();
            _scenarioCommandTemplateCategoryCombo.Items.Add("全部");
            var preferredCategories = new[]
            {
                "剧情/文本",
                "流程/变量",
                "人物/战场单位",
                "物品/奖励",
                "地图/坐标/战场",
                "单挑",
                "演出/音画",
                "系统/路线",
                "其他/待研究"
            };
            var categorySet = _currentScenarioCommandTemplateItems
                .Select(item => item.Category)
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Distinct(StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var category in preferredCategories.Where(categorySet.Contains))
            {
                _scenarioCommandTemplateCategoryCombo.Items.Add(category);
            }
            foreach (var category in categorySet
                         .Where(category => !preferredCategories.Contains(category, StringComparer.Ordinal))
                         .OrderBy(category => category, StringComparer.CurrentCultureIgnoreCase))
            {
                _scenarioCommandTemplateCategoryCombo.Items.Add(category);
            }
            SelectComboValueOrFirst(_scenarioCommandTemplateCategoryCombo, previousCategory);

            _scenarioCommandTemplateStatusCombo.Items.Clear();
            _scenarioCommandTemplateStatusCombo.Items.Add("全部");
            foreach (var status in _currentScenarioCommandTemplateItems
                         .Select(item => item.Status)
                         .Where(status => !string.IsNullOrWhiteSpace(status))
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(status => status, StringComparer.CurrentCultureIgnoreCase))
            {
                _scenarioCommandTemplateStatusCombo.Items.Add(status);
            }
            SelectComboValueOrFirst(_scenarioCommandTemplateStatusCombo, previousStatus);
        }
        finally
        {
            _updatingScenarioCommandTemplateFilters = false;
        }
    }

    private void ApplyScenarioCommandTemplateFilter()
    {
        if (_updatingScenarioCommandTemplateFilters)
        {
            return;
        }

        if (_currentScenarioCommandTemplateItems.Count == 0)
        {
            _scenarioCommandTemplateInfoBox.Text = "尚未加载命令模板目录；请点击“刷新模板目录”。";
            return;
        }

        var keyword = _scenarioCommandTemplateSearchBox.Text.Trim();
        var category = Convert.ToString(_scenarioCommandTemplateCategoryCombo.SelectedItem, CultureInfo.InvariantCulture) ?? "全部";
        var status = Convert.ToString(_scenarioCommandTemplateStatusCombo.SelectedItem, CultureInfo.InvariantCulture) ?? "全部";
        var rows = _currentScenarioCommandTemplateItems.Where(item =>
            (category == "全部" || item.Category == category) &&
            (status == "全部" || item.Status == status) &&
            (string.IsNullOrWhiteSpace(keyword) || ScenarioCommandTemplateMatchesKeyword(item, keyword)))
            .ToList();

        BindScenarioCommandTemplateRows(rows);
        if (rows.Count == 0)
        {
            _scenarioCommandTemplateInfoBox.Text =
                "R/S eex 命令模板筛选无匹配结果。\r\n" +
                $"关键字：{keyword}\r\n分类：{category}\r\n状态：{status}\r\n\r\n" +
                "提示：可清空关键字或切换到“全部”继续查看；待补充模板仍只作为研究项，不写回 R/S eex。";
        }
        SetStatus($"R/S命令模板筛选：显示 {rows.Count}/{_currentScenarioCommandTemplateItems.Count} 行");
    }

    private void ClearScenarioCommandTemplateFilter()
    {
        _scenarioCommandTemplateSearchBox.Clear();
        _updatingScenarioCommandTemplateFilters = true;
        try
        {
            if (_scenarioCommandTemplateCategoryCombo.Items.Count > 0) _scenarioCommandTemplateCategoryCombo.SelectedIndex = 0;
            if (_scenarioCommandTemplateStatusCombo.Items.Count > 0) _scenarioCommandTemplateStatusCombo.SelectedIndex = 0;
        }
        finally
        {
            _updatingScenarioCommandTemplateFilters = false;
        }

        BindScenarioCommandTemplateRows(_currentScenarioCommandTemplateItems);
        SetStatus($"R/S命令模板筛选已清除：显示 {_currentScenarioCommandTemplateItems.Count} 行");
    }

    private void BindScenarioCommandTemplateRows(IEnumerable<ScenarioCommandTemplateCatalogItem> rows)
    {
        var list = rows.ToList();
        _scenarioCommandTemplateGrid.DataSource = new BindingList<ScenarioCommandTemplateCatalogItem>(list);
        ConfigureScenarioCommandTemplateGrid();
        if (list.Count == 0)
        {
            _showScenarioCommandTemplateInStructureButton.Enabled = false;
            return;
        }

        _scenarioCommandTemplateGrid.ClearSelection();
        _scenarioCommandTemplateGrid.Rows[0].Selected = true;
        var firstVisibleCell = _scenarioCommandTemplateGrid.Rows[0].Cells.Cast<DataGridViewCell>().FirstOrDefault(cell => cell.Visible);
        if (firstVisibleCell != null)
        {
            _scenarioCommandTemplateGrid.CurrentCell = firstVisibleCell;
        }
        ShowSelectedScenarioCommandTemplate();
    }

    private void ConfigureScenarioCommandTemplateGrid()
    {
        foreach (DataGridViewColumn column in _scenarioCommandTemplateGrid.Columns)
        {
            column.ToolTipText = column.DataPropertyName switch
            {
                nameof(ScenarioCommandTemplateCatalogItem.IdHex) => "R/S eex 命令 ID；如加载了 CczString.ini，会与字典命令合并显示。",
                nameof(ScenarioCommandTemplateCatalogItem.DictionaryName) => "旧剧本编辑器命令字典中的名称；空白表示该内置模板暂未在当前字典中找到同 ID。",
                nameof(ScenarioCommandTemplateCatalogItem.TemplateName) => "CCZModStudio 内置的参数槽位模板名称。",
                nameof(ScenarioCommandTemplateCatalogItem.Status) => "已覆盖表示已有专用槽位模板；待补充表示只能先用通用探针和旧工具核对。",
                nameof(ScenarioCommandTemplateCatalogItem.Category) => "按命令名称与用途自动归类，方便创作者按剧情、流程、人物、物品、地图等维度筛选。",
                nameof(ScenarioCommandTemplateCatalogItem.SlotSummary) => "模板中已知参数槽位摘要；仅解释后续 16 位词候选，不代表完整命令长度。",
                nameof(ScenarioCommandTemplateCatalogItem.Purpose) => "命令模板的创作用途说明。",
                nameof(ScenarioCommandTemplateCatalogItem.Risk) => "当前模板的风险边界和核对要求。",
                nameof(ScenarioCommandTemplateCatalogItem.CreatorTip) => "面向 MOD 制作的实际核对建议。",
                _ => string.Empty
            };

            if (column.DataPropertyName == nameof(ScenarioCommandTemplateCatalogItem.Id))
            {
                column.Visible = false;
            }
            else if (column.DataPropertyName == nameof(ScenarioCommandTemplateCatalogItem.IdHex))
            {
                column.HeaderText = "命令ID";
                column.Width = 76;
            }
            else if (column.DataPropertyName == nameof(ScenarioCommandTemplateCatalogItem.DictionaryName))
            {
                column.HeaderText = "字典名称";
                column.Width = 150;
            }
            else if (column.DataPropertyName == nameof(ScenarioCommandTemplateCatalogItem.TemplateName))
            {
                column.HeaderText = "模板名称";
                column.Width = 210;
            }
            else if (column.DataPropertyName == nameof(ScenarioCommandTemplateCatalogItem.Status))
            {
                column.HeaderText = "状态";
                column.Width = 82;
            }
            else if (column.DataPropertyName == nameof(ScenarioCommandTemplateCatalogItem.Category))
            {
                column.HeaderText = "分类";
                column.Width = 120;
            }
            else if (column.DataPropertyName == nameof(ScenarioCommandTemplateCatalogItem.SlotCount))
            {
                column.HeaderText = "槽位";
                column.Width = 64;
            }
            else if (column.DataPropertyName == nameof(ScenarioCommandTemplateCatalogItem.SlotSummary))
            {
                column.HeaderText = "槽位摘要";
                column.Width = 420;
            }
            else if (column.DataPropertyName == nameof(ScenarioCommandTemplateCatalogItem.Purpose))
            {
                column.HeaderText = "用途";
                column.Width = 360;
            }
            else if (column.DataPropertyName == nameof(ScenarioCommandTemplateCatalogItem.Risk))
            {
                column.HeaderText = "风险边界";
                column.Width = 360;
            }
            else if (column.DataPropertyName == nameof(ScenarioCommandTemplateCatalogItem.CreatorTip))
            {
                column.HeaderText = "核对提示";
                column.Width = 420;
            }
            else if (column.DataPropertyName is nameof(ScenarioCommandTemplateCatalogItem.SlotDetails)
                     or nameof(ScenarioCommandTemplateCatalogItem.SafetyNote))
            {
                column.Visible = false;
            }
        }

        foreach (DataGridViewRow row in _scenarioCommandTemplateGrid.Rows)
        {
            if (row.DataBoundItem is not ScenarioCommandTemplateCatalogItem item) continue;
            row.DefaultCellStyle.BackColor = item.Status switch
            {
                "待补充" => Color.MistyRose,
                _ when item.Category == "剧情/文本" => Color.Honeydew,
                _ when item.Category == "人物/战场单位" => Color.AliceBlue,
                _ when item.Category == "地图/坐标/战场" => Color.LemonChiffon,
                _ when item.Category == "流程/变量" => Color.Lavender,
                _ => row.DefaultCellStyle.BackColor
            };
        }
    }

    private ScenarioCommandTemplateCatalogItem? GetSelectedScenarioCommandTemplateItem()
    {
        return _scenarioCommandTemplateGrid.SelectedRows.Count > 0
            ? _scenarioCommandTemplateGrid.SelectedRows[0].DataBoundItem as ScenarioCommandTemplateCatalogItem
            : _scenarioCommandTemplateGrid.CurrentRow?.DataBoundItem as ScenarioCommandTemplateCatalogItem;
    }

    private void ShowSelectedScenarioCommandTemplate()
    {
        var item = GetSelectedScenarioCommandTemplateItem();
        if (item == null)
        {
            _showScenarioCommandTemplateInStructureButton.Enabled = false;
            return;
        }

        _showScenarioCommandTemplateInStructureButton.Enabled = true;
        var targetKey = $"{item.IdHex}#{(string.IsNullOrWhiteSpace(item.DictionaryName) ? item.TemplateName : item.DictionaryName)}";
        var detail = _scenarioCommandParameterTemplateService.BuildCatalogItemDetail(item);
        _scenarioCommandTemplateInfoBox.Text =
            detail;
        SetStatus($"R/S命令模板：{item.IdHex} {item.TemplateName}");
    }

    private void FilterScenarioStructureBySelectedCommandTemplate()
    {
        var item = GetSelectedScenarioCommandTemplateItem();
        if (item == null)
        {
            MessageBox.Show(this, "请先在命令模板目录中选择一条模板。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_currentScenarioStructureResult == null)
        {
            MessageBox.Show(this,
                "当前还没有结构草图。\r\n\r\n请先在上方 R/S eex 文件列表选择一个文件，然后点击“生成结构草图”；之后即可从模板目录反向筛出当前文件中的同 ID 命令。",
                "请先生成结构草图",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var matchedRows = _currentScenarioStructureResult.Rows
            .Where(row => row.NodeType == "Command候选" &&
                          TryParseHex(row.CommandIdHex, out var commandId) &&
                          commandId == item.Id)
            .ToList();
        SelectTabPageByText("结构草图/XML");
        if (matchedRows.Count == 0)
        {
            _scenarioStructureNodeInfoBox.Text =
                $"当前结构草图文件：{_currentScenarioStructureResult.FileName}\r\n" +
                $"模板：{item.IdHex} {item.TemplateName}\r\n\r\n" +
                "未在当前结构草图的命令候选中找到同 ID 命令。\r\n" +
                "提示：可换一个 R/S eex 文件重新生成结构草图，或先把该模板作为待核对研究项记录到外部笔记。";
            SetStatus($"当前R/S未命中模板：{item.IdHex} {item.TemplateName}");
            return;
        }

        _scenarioStructureFilterBox.Text = item.IdHex;
        BindScenarioStructureRows(matchedRows);
        ClearScenarioCommandReferenceTargets();
        _scenarioStructureNodeInfoBox.Text =
            $"已从命令模板目录反向筛出当前 R/S eex 结构草图命令。\r\n" +
            $"文件：{_currentScenarioStructureResult.FileName}\r\n" +
            $"模板：{item.IdHex} {item.TemplateName}    分类：{item.Category}    状态：{item.Status}\r\n" +
            $"命中命令：{matchedRows.Count} / {_currentScenarioStructureResult.CommandCandidateCount}\r\n\r\n" +
            "下一步：选中下方命令行，可继续查看参数模板详情、可跳转引用候选、文本线索。";
        if (_scenarioStructureGrid.Rows.Count > 0)
        {
            _scenarioStructureGrid.ClearSelection();
            _scenarioStructureGrid.Rows[0].Selected = true;
            var firstVisibleCell = _scenarioStructureGrid.Rows[0].Cells.Cast<DataGridViewCell>().FirstOrDefault(cell => cell.Visible);
            if (firstVisibleCell != null)
            {
                _scenarioStructureGrid.CurrentCell = firstVisibleCell;
            }
            ShowSelectedScenarioStructureRow();
        }

        SetStatus($"模板反查当前R/S命令：{item.IdHex} 命中 {matchedRows.Count} 行");
    }

    private static bool ScenarioCommandTemplateMatchesKeyword(ScenarioCommandTemplateCatalogItem item, string keyword)
        => ContainsKeyword(item.IdHex, keyword) ||
           ContainsKeyword(item.DictionaryName, keyword) ||
           ContainsKeyword(item.TemplateName, keyword) ||
           ContainsKeyword(item.Status, keyword) ||
           ContainsKeyword(item.Category, keyword) ||
           ContainsKeyword(item.SlotSummary, keyword) ||
           ContainsKeyword(item.Purpose, keyword) ||
           ContainsKeyword(item.Risk, keyword) ||
           ContainsKeyword(item.SlotDetails, keyword) ||
           ContainsKeyword(item.CreatorTip, keyword) ||
           ContainsKeyword(item.SafetyNote, keyword);

    private void ExportScenarioCommandTemplateCatalog()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe();
            var reportPath = _scenarioCommandParameterTemplateService.WriteTemplateCatalog(_project, dictionary);
            var dictionaryCount = dictionary?.Commands.Count ?? 0;
            _scenarioFileInfoBox.Text =
                $"已导出 R/S eex 命令参数模板目录：\r\n{reportPath}\r\n\r\n" +
                $"内置模板：{_scenarioCommandParameterTemplateService.TemplateCount} 条；命令字典：{dictionaryCount} 条。\r\n" +
                "说明：该目录用于查阅命令参数槽位、中文解释、风险边界和待补模板；只写入 CCZModStudio_Reports，不修改任何游戏文件。";
            System.Diagnostics.Debug.WriteLine("已导出 R/S eex 命令参数模板目录：" + reportPath);
            SetStatus("R/S eex 命令参数模板目录已导出");
            Cursor = Cursors.Default;
            if (MessageBox.Show(this,
                    "已导出 R/S eex 命令参数模板目录：\r\n" + reportPath + "\r\n\r\n是否在资源管理器中定位该文件？",
                    "导出完成",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information) == DialogResult.Yes)
            {
                OpenFileLocation(reportPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("导出 R/S eex 命令参数模板目录失败：" + ex);
            MessageBox.Show(this, ex.Message, "导出 R/S eex 命令参数模板目录失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ExportScenarioCommandReferenceChecklist()
    {
        if (_project == null || _currentScenarioStructureResult == null)
        {
            MessageBox.Show(this, "请先生成结构草图，再导出命令引用核对清单。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var visibleRows = GetGridItems<ScenarioStructureRow>(_scenarioStructureGrid);
            var rows = visibleRows.Count > 0 ? visibleRows : _currentScenarioStructureResult.Rows.ToList();
            var textEntries = _currentScenarioTextEntries;
            if (textEntries.Count == 0)
            {
                try
                {
                    textEntries = _scenarioTextReader.Read(_currentScenarioStructureResult.FilePath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("导出命令引用清单时读取同文件文本线索失败，继续生成不含文本跳转的清单：" + ex.Message);
                    textEntries = Array.Empty<ScenarioTextEntry>();
                }
            }

            var reportPath = _scenarioCommandReferenceChecklistService.WriteReport(
                _project,
                _tables,
                _currentScenarioStructureResult,
                rows,
                textEntries);
            var commandCount = rows.Count(row => row.NodeType == "Command候选");
            var length = new FileInfo(reportPath).Length;
            _scenarioStructureNodeInfoBox.Text =
                $"已导出 R/S eex 命令引用核对清单：\r\n{reportPath}\r\n\r\n" +
                $"范围：当前结构视图命令 {commandCount} 行；文本线索 {textEntries.Count} 条；报告大小 {length:N0} 字节。\r\n" +
                "说明：该 Markdown 只写入 CCZModStudio_Reports，不修改任何游戏文件；候选来自 16 位词窗口扫描，请结合旧工具和实机逐项核对。";
            System.Diagnostics.Debug.WriteLine("已导出 R/S eex 命令引用核对清单：" + reportPath);
            SetStatus("R/S eex 命令引用核对清单已导出");
            Cursor = Cursors.Default;
            if (MessageBox.Show(this,
                    "已导出命令引用核对清单：\r\n" + reportPath + "\r\n\r\n是否在资源管理器中定位该文件？",
                    "导出完成",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information) == DialogResult.Yes)
            {
                OpenFileLocation(reportPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("导出 R/S eex 命令引用核对清单失败：" + ex);
            MessageBox.Show(this, ex.Message, "导出 R/S eex 命令引用核对清单失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ApplyScenarioStructureFilter()
    {
        if (_currentScenarioStructureResult == null)
        {
            return;
        }

        var options = BuildScenarioStructureFilterOptions();
        var filtered = _scenarioStructureFilterService.Filter(_currentScenarioStructureResult.Rows, options);
        BindScenarioStructureRows(filtered);
        ClearScenarioCommandReferenceTargets();
        _scenarioStructureNodeInfoBox.Text =
            _scenarioStructureFilterService.BuildSummary(_currentScenarioStructureResult.Rows, filtered, options) +
            "\r\n点击 Scene/Section/Command 节点可查看同文件文本线索和中文注释。";
        SetStatus($"R/S结构筛选：命令 {filtered.Count(row => row.NodeType == "Command候选")}/{_currentScenarioStructureResult.CommandCandidateCount}");
    }

    private void ClearScenarioStructureFilter()
    {
        _scenarioStructureFilterBox.Clear();
        _scenarioStructureTemplatesOnly.Checked = false;
        _scenarioStructureTextOnly.Checked = false;
        _scenarioStructureMapOnly.Checked = false;
        _scenarioStructureHighRiskOnly.Checked = false;
        ApplyScenarioStructureFilter();
    }

    private ScenarioStructureFilterOptions BuildScenarioStructureFilterOptions() => new()
    {
        Keyword = _scenarioStructureFilterBox.Text,
        TemplatesOnly = _scenarioStructureTemplatesOnly.Checked,
        TextRelatedOnly = _scenarioStructureTextOnly.Checked,
        MapCoordinateOnly = _scenarioStructureMapOnly.Checked,
        HighRiskOnly = _scenarioStructureHighRiskOnly.Checked
    };

    private void BindScenarioStructureRows(IReadOnlyList<ScenarioStructureRow> rows)
    {
        _scenarioStructureGrid.DataSource = new BindingList<ScenarioStructureRow>(rows.ToList());
        ConfigureScenarioStructureGrid();
        if (_currentScenarioStructureResult != null)
        {
        }
        if (_currentScenarioStructureResult != null)
        {
            PopulateScenarioStructureTree(_currentScenarioStructureResult, rows);
        }
    }

    private void ResetScenarioStructureFilterControls()
    {
        _scenarioStructureFilterBox.Clear();
        _scenarioStructureTemplatesOnly.Checked = false;
        _scenarioStructureTextOnly.Checked = false;
        _scenarioStructureMapOnly.Checked = false;
        _scenarioStructureHighRiskOnly.Checked = false;
        EnableScenarioStructureFilterControls(false);
    }

    private void EnableScenarioStructureFilterControls(bool enabled)
    {
        _scenarioStructureFilterBox.Enabled = enabled;
        _filterScenarioStructureButton.Enabled = enabled;
        _clearScenarioStructureFilterButton.Enabled = enabled;
        _scenarioStructureTemplatesOnly.Enabled = enabled;
        _scenarioStructureTextOnly.Enabled = enabled;
        _scenarioStructureMapOnly.Enabled = enabled;
        _scenarioStructureHighRiskOnly.Enabled = enabled;
        _exportScenarioCommandReferenceChecklistButton.Enabled = enabled && _currentScenarioStructureResult != null;
    }

    private void PopulateScenarioStructureTree(ScenarioStructureProbeResult result, IReadOnlyList<ScenarioStructureRow>? rows = null)
    {
        rows ??= result.Rows;
        _scenarioStructureTree.BeginUpdate();
        try
        {
            _scenarioStructureTree.Nodes.Clear();
            _scenarioStructureNodeInfoBox.Text =
                $"事件树：{result.FileName}\r\n" +
                $"{result.Summary}\r\n" +
                "点击 Scene/Section/Command 节点可查看同文件文本线索和中文注释。";
            var visibleSceneCount = rows.Count(row => row.NodeType == "Scene候选");
            var visibleSectionCount = rows.Count(row => row.NodeType == "Section候选");
            var visibleCommandCount = rows.Count(row => row.NodeType == "Command候选");
            var root = new TreeNode($"{result.FileName}｜Scene {visibleSceneCount}/{result.SceneCount}｜Section {visibleSectionCount}/{result.SectionCount}｜Command {visibleCommandCount}/{result.CommandCandidateCount}")
            {
                ToolTipText = result.Summary
            };
            _scenarioStructureTree.Nodes.Add(root);

            var latestByLevel = new Dictionary<int, TreeNode> { [-1] = root };
            foreach (var row in rows)
            {
                var level = Math.Max(0, row.Level);
                var parentLevel = level - 1;
                while (parentLevel >= 0 && !latestByLevel.ContainsKey(parentLevel))
                {
                    parentLevel--;
                }

                var parent = latestByLevel.TryGetValue(parentLevel, out var foundParent) ? foundParent : root;
                var node = new TreeNode(BuildScenarioStructureTreeNodeText(row))
                {
                    Tag = row,
                    ToolTipText = BuildScenarioStructureTreeToolTip(row),
                    ForeColor = row.NodeType switch
                    {
                        "Scene候选" => Color.MidnightBlue,
                        "Section候选" => Color.DarkCyan,
                        _ when row.Confidence == "低" => Color.Firebrick,
                        _ when !string.IsNullOrWhiteSpace(row.ReferenceHint) => Color.DarkGreen,
                        _ => Color.Black
                    }
                };
                parent.Nodes.Add(node);
                latestByLevel[level] = node;
                foreach (var staleLevel in latestByLevel.Keys.Where(key => key > level).ToList())
                {
                    latestByLevel.Remove(staleLevel);
                }
            }

            ExpandTreeToDepth(root, maxDepth: 2);
        }
        finally
        {
            _scenarioStructureTree.EndUpdate();
        }
    }

    private static string BuildScenarioStructureTreeNodeText(ScenarioStructureRow row)
    {
        if (row.NodeType == "Scene候选")
        {
            return $"Scene {row.SceneIndex}｜{row.Annotation}";
        }

        if (row.NodeType == "Section候选")
        {
            return $"Section {row.SectionIndex}｜{row.Annotation}";
        }

        var templateMark = row.HasCommandTemplate ? "｜有参数模板" : string.Empty;
        var referenceMark = string.IsNullOrWhiteSpace(row.ReferenceHint) ? string.Empty : "｜有引用候选";
        return $"#{row.CommandIndex} {row.OffsetHex} {row.CommandIdHex} {row.CommandName}｜{row.Confidence}{templateMark}{referenceMark}";
    }

    private static string BuildScenarioStructureTreeToolTip(ScenarioStructureRow row)
    {
        var parts = new List<string>
        {
            $"节点：{row.NodeType}",
            $"Scene={row.SceneIndex}, Section={row.SectionIndex}, Command={row.CommandIndex}",
            $"偏移={row.OffsetHex}, 命令={row.CommandIdHex} {row.CommandName}",
            $"参数：{row.ParameterPreview}",
            $"模板：{(row.HasCommandTemplate ? row.CommandTemplateHint : "暂无专用参数模板")}",
            $"说明：{row.Annotation}"
        };
        if (!string.IsNullOrWhiteSpace(row.ReferenceHint))
        {
            parts.Add("引用候选：" + row.ReferenceHint);
        }
        return string.Join(Environment.NewLine, parts);
    }

    private void ShowSelectedScenarioStructureRow()
    {
        if (_updatingScenarioStructureSelection)
        {
            return;
        }

        if (_scenarioStructureGrid.CurrentRow?.DataBoundItem is not ScenarioStructureRow row)
        {
            ClearScenarioCommandReferenceTargets();
            return;
        }

        try
        {
            _updatingScenarioStructureSelection = true;
            SelectScenarioStructureTreeNode(row);
        }
        finally
        {
            _updatingScenarioStructureSelection = false;
        }

        _scenarioStructureNodeInfoBox.Text = BuildScenarioStructureNodeDetail(row);
    }

    private void SelectScenarioStructureRowFromTree(ScenarioStructureRow? row)
    {
        if (row == null) return;
        try
        {
            _updatingScenarioStructureSelection = true;
            foreach (DataGridViewRow gridRow in _scenarioStructureGrid.Rows)
            {
                if (gridRow.DataBoundItem is not ScenarioStructureRow candidate || candidate.Index != row.Index) continue;
                gridRow.Selected = true;
                if (gridRow.Cells.Count > 0)
                {
                    _scenarioStructureGrid.CurrentCell = gridRow.Cells[0];
                }
                if (gridRow.Index >= 0 && gridRow.Index < _scenarioStructureGrid.RowCount)
                {
                    _scenarioStructureGrid.FirstDisplayedScrollingRowIndex = gridRow.Index;
                }
                break;
            }
        }
        finally
        {
            _updatingScenarioStructureSelection = false;
        }

        _scenarioStructureNodeInfoBox.Text = BuildScenarioStructureNodeDetail(row);
    }

    private string BuildScenarioStructureNodeDetail(ScenarioStructureRow row)
    {
        if (_currentScenarioStructureResult == null)
        {
            return "尚未生成结构草图。";
        }

        IReadOnlyList<ScenarioTextEntry> textEntries;
        try
        {
            textEntries = _scenarioTextReader.Read(_currentScenarioStructureResult.FilePath);
        }
        catch (Exception ex)
        {
            return "读取同文件文本线索失败：" + ex.Message;
        }
        var referenceTargets = BuildScenarioCommandReferenceTargets(row, textEntries);
        ApplyScenarioCommandReferenceTargets(referenceTargets);
        var detail = _scenarioStructureNodeDetailService.BuildDetail(
            row,
            _currentScenarioStructureResult.FileName,
            textEntries,
            _project,
            _tables);
        var riskReason = _scenarioStructureFilterService.BuildHighRiskReason(row);
        if (!string.IsNullOrWhiteSpace(riskReason))
        {
            detail += "\r\n高风险/需核对原因：\r\n- " + riskReason + "\r\n";
        }

        if (row.NodeType == "Command候选")
        {
            detail += "\r\n" + _scenarioCommandReferenceNavigationService.BuildSummary(referenceTargets) + "\r\n";
        }

        if (row.NodeType == "Command候选")
        {
        }

        return detail;
    }

    private IReadOnlyList<ScenarioCommandReferenceTarget> BuildScenarioCommandReferenceTargets(
        ScenarioStructureRow row,
        IReadOnlyList<ScenarioTextEntry> textEntries)
    {
        if (_project == null || _currentScenarioStructureResult == null)
        {
            return Array.Empty<ScenarioCommandReferenceTarget>();
        }

        try
        {
            return _scenarioCommandReferenceNavigationService.Analyze(
                _project,
                _tables,
                row,
                _currentScenarioStructureResult.FileName,
                textEntries);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("R/S eex 命令引用候选解析失败：" + ex.Message);
            return Array.Empty<ScenarioCommandReferenceTarget>();
        }
    }

    private void ApplyScenarioCommandReferenceTargets(IReadOnlyList<ScenarioCommandReferenceTarget> targets)
    {
        _currentScenarioCommandReferenceTargets = targets;
        _scenarioCommandReferenceCombo.DataSource = null;
        if (targets.Count == 0)
        {
            _scenarioCommandReferenceCombo.Enabled = false;
            _jumpScenarioCommandReferenceButton.Enabled = false;
            return;
        }

        _scenarioCommandReferenceCombo.DisplayMember = nameof(ScenarioCommandReferenceTarget.DisplayText);
        _scenarioCommandReferenceCombo.DataSource = new BindingList<ScenarioCommandReferenceTarget>(targets.ToList());
        _scenarioCommandReferenceCombo.Enabled = true;
        _jumpScenarioCommandReferenceButton.Enabled = targets.Any(target => target.CanNavigate);
    }

    private void ClearScenarioCommandReferenceTargets()
    {
        _currentScenarioCommandReferenceTargets = Array.Empty<ScenarioCommandReferenceTarget>();
        _scenarioCommandReferenceCombo.DataSource = null;
        _scenarioCommandReferenceCombo.Enabled = false;
        _jumpScenarioCommandReferenceButton.Enabled = false;
    }


    private void JumpSelectedScenarioCommandReference()
    {
        var target = _scenarioCommandReferenceCombo.SelectedItem as ScenarioCommandReferenceTarget
                     ?? _currentScenarioCommandReferenceTargets.FirstOrDefault();
        if (target == null || !target.CanNavigate)
        {
            MessageBox.Show(this, "当前命令没有可跳转的引用候选。请先选中结构草图中的命令行。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var jumped = false;
        if (target.CanJumpDataTable)
        {
            jumped = SelectDataTableCell(target.TableName, target.RowId, target.FieldName);
        }
        else if (target.CanJumpScenarioText)
        {
            jumped = JumpScenarioCommandReferenceToText(target);
        }

        if (jumped)
        {
            SetStatus("已跳转到 R/S eex 命令引用候选：" + target.DisplayText);
            return;
        }

        MessageBox.Show(this,
            $"未能定位命令引用候选：{target.DisplayText}\r\n{target.SafetyNote}",
            "未找到引用目标",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private bool JumpScenarioCommandReferenceToText(ScenarioCommandReferenceTarget target)
    {
        if (!string.IsNullOrWhiteSpace(target.ScenarioFileName) && GetSelectedScenarioFileItem()?.FileName != target.ScenarioFileName)
        {
            if (!SelectScenarioFileForNavigation(target.ScenarioFileName))
            {
                return false;
            }
        }

        SelectTabPageByText("R/S eex高级探针");
        SelectTabPageByText("文本线索");
        if (_currentScenarioTextEntries.Count == 0)
        {
            ProbeSelectedScenarioTexts();
        }

        var found = SelectGridRow<ScenarioTextEntry>(_scenarioTextGrid, row =>
            (!target.TextIndex.HasValue || row.Index == target.TextIndex.Value) &&
            (string.IsNullOrWhiteSpace(target.TextOffsetHex) || HexOffsetEquals(row.OffsetHex, target.TextOffsetHex)));
        if (found)
        {
            ShowSelectedScenarioTextEntry();
        }

        return found;
    }


    private static void ExpandTreeToDepth(TreeNode node, int maxDepth)
    {
        if (node.Level >= maxDepth) return;
        node.Expand();
        foreach (TreeNode child in node.Nodes)
        {
            ExpandTreeToDepth(child, maxDepth);
        }
    }

    private void ProbeSelectedScenarioTexts()
    {
        if (_scenarioFileGrid.SelectedRows.Count == 0 || _scenarioFileGrid.SelectedRows[0].DataBoundItem is not ScenarioFileInfo item)
        {
            MessageBox.Show(this, "请先在上方剧本列表选择一个 R/S eex 文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            _currentScenarioTextEntries = _scenarioTextReader.Read(item.Path);
            _scenarioTextFilterBox.Clear();
            _scenarioTextChangedOnly.Checked = false;
            BindScenarioTextEntries(_currentScenarioTextEntries);
            _exportScenarioTextsButton.Enabled = _currentScenarioTextEntries.Count > 0;
            _saveScenarioTextsButton.Enabled = _project != null && _currentScenarioTextEntries.Count > 0;
            _scenarioTextFilterButton.Enabled = _currentScenarioTextEntries.Count > 0;
            _scenarioTextFilterClearButton.Enabled = _currentScenarioTextEntries.Count > 0;
            _scenarioTextChangedOnly.Enabled = _currentScenarioTextEntries.Count > 0;
            var summary = string.Join("，", _currentScenarioTextEntries
                .GroupBy(x => x.Kind)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Select(g => $"{g.Key}:{g.Count()}"));
            _scenarioFileInfoBox.Text =
                $"文件：{item.FileName}    标题/文本首项：{item.TitleHint}\r\n" +
                $"文本线索：{_currentScenarioTextEntries.Count} 条    分类：{summary}\r\n" +
                "说明：Text 列允许原地短写回；GBK 字节数必须不超过原容量，保存前会自动备份并复读验证。\r\n" +
                $"路径：{item.Path}";
            ShowSelectedScenarioTextEntry();
            System.Diagnostics.Debug.WriteLine($"已提取剧本文本线索：{item.FileName}，文本 {_currentScenarioTextEntries.Count} 条。");
            SetStatus($"剧本文本线索：{item.FileName}");
        }
        catch (Exception ex)
        {
            _scenarioFileInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("剧本文本线索提取失败：" + ex);
            MessageBox.Show(this, ex.Message, "剧本文本线索提取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ShowSelectedScenarioTextEntry()
    {
        if (_scenarioTextGrid.CurrentRow?.DataBoundItem is not ScenarioTextEntry entry)
        {
            return;
        }

        var scenarioFile = GetSelectedScenarioFileItem();
        var scenarioName = scenarioFile?.FileName ?? _currentScenarioStructureResult?.FileName ?? "未知RS";
        var isChanged = IsScenarioTextChanged(entry);
        var writeMode = "当前项目可在 Text 列原地短写回；保存前会备份，保存后会复读验证。";
        var originalPreview = BuildPreview(NormalizeScenarioTextForSave(entry.OriginalText), 120);
        var currentPreview = BuildPreview(NormalizeScenarioTextForSave(entry.Text), 120);
        var changeText = isChanged
            ? $"改动状态：已改动（原文：{originalPreview}）"
            : "改动状态：未改动";
        var annotation = string.IsNullOrWhiteSpace(entry.Annotation)
            ? "暂无自动注释；可结合上下文和实机验证补充外部记录。"
            : entry.Annotation;
        var sourcePath = scenarioFile?.Path ?? _currentScenarioStructureResult?.FilePath ?? string.Empty;

        _scenarioFileInfoBox.Text =
            $"文件：{scenarioName}    文本索引：#{entry.Index}    类型：{entry.Kind}\r\n" +
            $"偏移：{entry.OffsetHex}    容量：{entry.ByteLength}B    当前GBK：{entry.GbkByteCount}B    剩余：{entry.RemainingBytes}B    写回状态：{entry.WriteStatus}\r\n" +
            $"中文注释：{annotation}\r\n" +
            $"当前文本：{currentPreview}\r\n" +
            $"{changeText}\r\n" +
            $"安全说明：{writeMode}\r\n" +
            (string.IsNullOrWhiteSpace(sourcePath) ? string.Empty : $"路径：{sourcePath}\r\n");

        SetStatus($"R/S文本 #{entry.Index}：{entry.WriteStatus}，GBK {entry.GbkByteCount}/{entry.ByteLength} 字节");
    }

    private void ExportScenarioTexts()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_currentScenarioTextEntries.Count == 0)
        {
            MessageBox.Show(this, "请先提取选中剧本文本线索。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_scenarioFileGrid.SelectedRows.Count == 0 || _scenarioFileGrid.SelectedRows[0].DataBoundItem is not ScenarioFileInfo item)
        {
            MessageBox.Show(this, "请先在上方剧本列表选择一个 R/S eex 文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var entriesToExport = GetVisibleScenarioTextEntriesFromGrid();
            if (entriesToExport.Count == 0) entriesToExport = _currentScenarioTextEntries.ToList();
            var csv = _scenarioTextExportService.ExportCsv(_project, item.FileName, entriesToExport);
            var txt = _scenarioTextExportService.ExportTxt(_project, item.FileName, entriesToExport);
            _scenarioFileInfoBox.Text =
                $"已导出剧本文本线索：{item.FileName}\r\n" +
                $"导出条数：{entriesToExport.Count}/{_currentScenarioTextEntries.Count}（若当前有筛选，则导出可见行）\r\n" +
                $"CSV：{csv}\r\n" +
                $"TXT：{txt}\r\n" +
                (_project.IsTestCopy
                    ? "当前为测试副本：导出文件位于测试副本 _CCZModStudio_Exports。"
                    : "当前为项目目录：导出文件位于工作区 CCZModStudio_Exports，避免混入游戏发布目录。");
            System.Diagnostics.Debug.WriteLine($"已导出剧本文本线索 CSV/TXT：{item.FileName}");
            SetStatus("剧本文本线索导出完成");
            MessageBox.Show(this, $"导出完成：\r\n{csv}\r\n{txt}", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("剧本文本线索导出失败：" + ex);
            MessageBox.Show(this, ex.Message, "剧本文本线索导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ValidateScenarioTextCell(DataGridViewCellValidatingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var column = _scenarioTextGrid.Columns[e.ColumnIndex];
        if (column.DataPropertyName != nameof(ScenarioTextEntry.Text)) return;
        if (_scenarioTextGrid.Rows[e.RowIndex].DataBoundItem is not ScenarioTextEntry entry) return;

        var value = NormalizeScenarioTextForSave(Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture));
        var cell = _scenarioTextGrid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        cell.ErrorText = string.Empty;

        var error = ValidateScenarioTextValue(entry, value);
        if (error != null)
        {
            cell.ErrorText = error;
            e.Cancel = true;
            SetStatus(error);
        }
    }

    private void SaveScenarioTextsToTestCopy()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_scenarioFileGrid.SelectedRows.Count == 0 || _scenarioFileGrid.SelectedRows[0].DataBoundItem is not ScenarioFileInfo item)
        {
            MessageBox.Show(this, "请先在上方剧本列表选择一个 R/S eex 文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _scenarioTextGrid.EndEdit();
        var entries = GetScenarioTextEntriesFromGrid();
        if (entries.Count == 0)
        {
            MessageBox.Show(this, "请先提取选中剧本文本线索。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var changed = entries
            .Where(x => !string.Equals(NormalizeScenarioTextForSave(x.Text), NormalizeScenarioTextForSave(x.OriginalText), StringComparison.Ordinal))
            .ToList();
        if (changed.Count == 0)
        {
            MessageBox.Show(this, "当前剧本文本没有检测到改动。", "无需保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var validationErrors = changed
            .Select(entry => new { Entry = entry, Error = ValidateScenarioTextValue(entry, NormalizeScenarioTextForSave(entry.Text)) })
            .Where(x => x.Error != null)
            .Take(10)
            .Select(x => $"#{x.Entry.Index} {x.Entry.OffsetHex}：{x.Error}")
            .ToList();
        if (validationErrors.Count > 0)
        {
            MessageBox.Show(this, "存在无法保存的文本：\r\n" + string.Join("\r\n", validationErrors), "校验失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var preview = string.Join("\r\n", changed.Take(12).Select(x =>
        {
            var newText = BuildPreview(NormalizeScenarioTextForSave(x.Text), 48);
            var oldText = BuildPreview(NormalizeScenarioTextForSave(x.OriginalText), 48);
            return $"#{x.Index} {x.OffsetHex} [{x.Kind}] {oldText} -> {newText}";
        }));
        if (changed.Count > 12) preview += $"\r\n……另有 {changed.Count - 12} 条。";

        if (MessageBox.Show(this,
                $"即将把剧本文本写入当前项目：\r\nRS\\{item.FileName}\r\n\r\n变更预览：\r\n{preview}\r\n\r\n" +
                "限制：只支持原地等长/缩短写回，不移动文本区、不扩容；保存前会备份，保存后会复读验证。是否继续？",
                "确认保存剧本文本",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var relativePath = Path.Combine("RS", item.FileName);
            var result = _scenarioTextWriter.SaveInPlace(_project, relativePath, changed, "R/S eex 文本写回前自动备份");
            var reread = _scenarioTextReader.Read(result.FilePath);
            VerifyScenarioTextSave(changed, reread);

            MarkScenarioTextEntriesSaved(changed);
            RefreshScenarioTextRows(changed);
            _exportScenarioTextsButton.Enabled = _currentScenarioTextEntries.Count > 0;
            _saveScenarioTextsButton.Enabled = true;
            _scenarioTextFilterButton.Enabled = _currentScenarioTextEntries.Count > 0;
            _scenarioTextFilterClearButton.Enabled = _currentScenarioTextEntries.Count > 0;
            _scenarioTextChangedOnly.Enabled = _currentScenarioTextEntries.Count > 0;

            _scenarioFileInfoBox.Text =
                $"剧本文本保存完成：RS\\{item.FileName}\r\n" +
                $"写入条数：{result.EntriesWritten}    变化字节：{result.ChangedBytes}\r\n" +
                $"备份：{result.BackupPath}\r\n" +
                $"结构化报告：{result.ReportJsonPath}\r\n" +
                "已重新读取文件并按偏移验证改动；当前仍只支持原地等长/缩短写回。";
            System.Diagnostics.Debug.WriteLine($"已保存剧本文本：{item.FileName}，写入 {result.EntriesWritten} 条，变化 {result.ChangedBytes} 字节。");
            System.Diagnostics.Debug.WriteLine("剧本文本备份：" + result.BackupPath);
            System.Diagnostics.Debug.WriteLine("剧本文本结构化报告：" + result.ReportJsonPath);
            SetStatus($"剧本文本保存完成：{result.ChangedBytes} 字节变化");
            MessageBox.Show(this,
                $"保存完成。\r\n写入条数：{result.EntriesWritten}\r\n变化字节：{result.ChangedBytes}\r\n备份：{result.BackupPath}\r\n结构化报告：{result.ReportJsonPath}",
                "剧本文本保存完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("剧本文本保存失败：" + ex);
            MessageBox.Show(this, ex.Message, "剧本文本保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private List<ScenarioTextEntry> GetScenarioTextEntriesFromGrid()
    {
        if (_currentScenarioTextEntries.Count > 0)
        {
            return _currentScenarioTextEntries.ToList();
        }

        var result = new List<ScenarioTextEntry>();
        foreach (DataGridViewRow row in _scenarioTextGrid.Rows)
        {
            if (row.DataBoundItem is ScenarioTextEntry entry) result.Add(entry);
        }
        return result;
    }

    private List<ScenarioTextEntry> GetVisibleScenarioTextEntriesFromGrid()
    {
        var result = new List<ScenarioTextEntry>();
        foreach (DataGridViewRow row in _scenarioTextGrid.Rows)
        {
            if (row.DataBoundItem is ScenarioTextEntry entry) result.Add(entry);
        }
        return result;
    }

    private static string? ValidateScenarioTextValue(ScenarioTextEntry entry, string value)
    {
        if (!entry.IsWritable) return "该文本候选解码置信度低或来源未确认，当前只读，不能写回。";
        if (string.IsNullOrWhiteSpace(value)) return "文本不能为空；如需删除文本请先手工确认格式后再开放删除能力。";
        if (value.Contains('\0')) return "文本不能包含 NUL/零字节。";
        var byteCount = EncodingService.GetGbkByteCount(value);
        if (byteCount < 4) return $"GBK 字节数 {byteCount} 过短，当前安全写回要求至少 4 字节。";
        if (byteCount > entry.ByteLength) return $"GBK 字节数 {byteCount} 超过原地容量 {entry.ByteLength}，只能等长或缩短。";
        return null;
    }

    private static void VerifyScenarioTextSave(IReadOnlyList<ScenarioTextEntry> changed, IReadOnlyList<ScenarioTextEntry> reread)
    {
        foreach (var entry in changed)
        {
            var expected = NormalizeScenarioTextForSave(entry.Text);
            var actual = reread.FirstOrDefault(x => x.Offset == entry.Offset);
            if (actual == null)
            {
                throw new InvalidOperationException($"保存后复读验证失败：找不到偏移 {entry.OffsetHex} 的文本。");
            }

            if (!string.Equals(NormalizeScenarioTextForSave(actual.Text), expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"保存后复读验证失败：#{entry.Index} {entry.OffsetHex} 期望“{expected}”，实际“{actual.Text}”。");
            }
        }
    }

    private static string NormalizeScenarioTextForSave(string? text)
        => (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();

    private static string BuildPreview(string text, int maxChars)
    {
        text = text.Replace("\n", "\\n", StringComparison.Ordinal);
        return text.Length <= maxChars ? text : text[..maxChars] + "…";
    }

    private void BindScenarioTextEntries(IEnumerable<ScenarioTextEntry> entries)
    {
        _scenarioTextGrid.DataSource = new BindingList<ScenarioTextEntry>(entries.ToList());
        ConfigureScenarioTextGrid();
        var scenarioName = GetSelectedScenarioFileItem()?.FileName ?? _currentScenarioStructureResult?.FileName ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(scenarioName))
        {
        }
    }


    private void ApplyScenarioTextFilter()
    {
        if (_currentScenarioTextEntries.Count == 0)
        {
            return;
        }

        var keyword = _scenarioTextFilterBox.Text.Trim();
        IEnumerable<ScenarioTextEntry> query = _currentScenarioTextEntries;
        if (_scenarioTextChangedOnly.Checked)
        {
            query = query.Where(IsScenarioTextChanged);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(x =>
                ContainsForFilter(x.Text, keyword) ||
                ContainsForFilter(x.Preview, keyword) ||
                ContainsForFilter(x.Kind, keyword) ||
                ContainsForFilter(x.Annotation, keyword) ||
                ContainsForFilter(x.OffsetHex, keyword));
        }

        var filtered = query.ToList();
        BindScenarioTextEntries(filtered);
        if (filtered.Count == 0)
        {
            _scenarioFileInfoBox.Text =
                $"剧本文本筛选无匹配结果。\r\n筛选关键字：{keyword}\r\n仅改动：{_scenarioTextChangedOnly.Checked}\r\n" +
                "提示：清除筛选后可继续查看文本线索详情和文本详情。";
        }
        else
        {
            ShowSelectedScenarioTextEntry();
        }
        SetStatus($"剧本文本筛选：显示 {filtered.Count}/{_currentScenarioTextEntries.Count} 条");
    }

    private void ClearScenarioTextFilter()
    {
        _scenarioTextFilterBox.Clear();
        _scenarioTextChangedOnly.Checked = false;
        BindScenarioTextEntries(_currentScenarioTextEntries);
        ShowSelectedScenarioTextEntry();
        SetStatus($"剧本文本筛选已清除：显示 {_currentScenarioTextEntries.Count} 条");
    }

    private void RefreshScenarioTextRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _scenarioTextGrid.Rows.Count) return;
        if (_scenarioTextGrid.Rows[rowIndex].DataBoundItem is not ScenarioTextEntry entry) return;

        entry.Text = NormalizeScenarioTextForSave(entry.Text);
        entry.CharLength = entry.Text.Length;
        entry.HasNewLines = entry.Text.Contains('\n');
        entry.Preview = BuildPreview(entry.Text, 80);
        _scenarioTextGrid.InvalidateRow(rowIndex);
        ShowSelectedScenarioTextEntry();
        SetStatus($"{entry.OffsetHex}：{entry.WriteStatus}，GBK {entry.GbkByteCount}/{entry.ByteLength} 字节");
    }

    private void RefreshScenarioTextRows(IReadOnlyList<ScenarioTextEntry> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var offsets = entries.Select(entry => entry.Offset).ToHashSet();
        for (var rowIndex = 0; rowIndex < _scenarioTextGrid.Rows.Count; rowIndex++)
        {
            if (_scenarioTextGrid.Rows[rowIndex].DataBoundItem is ScenarioTextEntry entry &&
                offsets.Contains(entry.Offset))
            {
                _scenarioTextGrid.InvalidateRow(rowIndex);
            }
        }

        ShowSelectedScenarioTextEntry();
    }

    private static bool IsScenarioTextChanged(ScenarioTextEntry entry)
        => !string.Equals(NormalizeScenarioTextForSave(entry.Text), NormalizeScenarioTextForSave(entry.OriginalText), StringComparison.Ordinal);

    private static bool ContainsForFilter(string text, string keyword)
        => text.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) >= 0;

    private void ConfigureScenarioTextGrid()
    {
        var canEditText = _project != null && _currentScenarioTextEntries.Count > 0;
        _scenarioTextGrid.ReadOnly = !canEditText;

        foreach (DataGridViewColumn column in _scenarioTextGrid.Columns)
        {
            column.ReadOnly = true;
            column.ToolTipText = column.DataPropertyName switch
            {
                nameof(ScenarioTextEntry.Text) => "可编辑剧本文本。保存时会先备份并复读验证，GBK 字节数不得超过 ByteLength。",
                nameof(ScenarioTextEntry.Annotation) => "工具根据文本类型、偏移和容量自动生成的中文注释，帮助理解该文本在剧本中的作用。",
                nameof(ScenarioTextEntry.ByteLength) => "原二进制连续文本区的 GBK 字节容量；保存时不能超过该容量。",
                nameof(ScenarioTextEntry.GbkByteCount) => "当前 Text 列内容按 GBK 编码后的字节数，会随编辑刷新。",
                nameof(ScenarioTextEntry.RemainingBytes) => "剩余可写字节数；负数表示超长，不能保存。",
                nameof(ScenarioTextEntry.WriteStatus) => "根据 OriginalText、GBK 字节数和容量计算出的写回状态。",
                nameof(ScenarioTextEntry.OffsetHex) => "该文本线索在 R/S eex 文件中的十六进制偏移。",
                nameof(ScenarioTextEntry.Kind) => "按文本内容推断出的用途分类，属于辅助判断而非最终格式结论。",
                _ => string.Empty
            };

            if (column.DataPropertyName == nameof(ScenarioTextEntry.Preview))
            {
                column.HeaderText = "预览";
                column.Width = 420;
            }
            else if (column.DataPropertyName == nameof(ScenarioTextEntry.Text))
            {
                column.HeaderText = "Text（可编辑文本）";
                column.ReadOnly = !canEditText;
                column.Width = 520;
            }
            else if (column.DataPropertyName == nameof(ScenarioTextEntry.OriginalText))
            {
                column.Visible = false;
            }
            else if (column.DataPropertyName == nameof(ScenarioTextEntry.Annotation))
            {
                column.HeaderText = "中文注释";
                column.Width = 440;
            }
            else if (column.DataPropertyName == nameof(ScenarioTextEntry.ByteLength))
            {
                column.HeaderText = "GBK容量";
            }
            else if (column.DataPropertyName == nameof(ScenarioTextEntry.GbkByteCount))
            {
                column.HeaderText = "当前GBK字节";
            }
            else if (column.DataPropertyName == nameof(ScenarioTextEntry.RemainingBytes))
            {
                column.HeaderText = "剩余字节";
            }
            else if (column.DataPropertyName == nameof(ScenarioTextEntry.WriteStatus))
            {
                column.HeaderText = "写回状态";
                column.Width = 140;
            }
            else if (column.DataPropertyName == nameof(ScenarioTextEntry.Kind))
            {
                column.HeaderText = "类型";
            }
            else if (column.DataPropertyName == nameof(ScenarioTextEntry.OffsetHex))
            {
                column.HeaderText = "偏移";
            }
            else if (column.DataPropertyName == nameof(ScenarioTextEntry.CharLength))
            {
                column.HeaderText = "字符数";
            }
            else if (column.DataPropertyName == nameof(ScenarioTextEntry.HasNewLines))
            {
                column.HeaderText = "多行";
            }
        }

        foreach (DataGridViewRow row in _scenarioTextGrid.Rows)
        {
            if (row.DataBoundItem is not ScenarioTextEntry item) continue;
            row.DefaultCellStyle.BackColor = item.Kind switch
            {
                "胜败条件" => Color.Honeydew,
                "标题/场所" => Color.Lavender,
                "提示/奖励" => Color.LemonChiffon,
                _ => row.DefaultCellStyle.BackColor
            };
            if (item.RemainingBytes < 0)
            {
                row.DefaultCellStyle.BackColor = Color.MistyRose;
            }
            else if (IsScenarioTextChanged(item))
            {
                row.DefaultCellStyle.BackColor = Color.LightCyan;
            }
        }
    }

    private void ConfigureScenarioCommandProbeGrid()
    {
        foreach (DataGridViewColumn column in _scenarioCommandProbeGrid.Columns)
        {
            if (column.DataPropertyName == nameof(ScenarioCommandProbeRow.ContextWordsHex))
            {
                column.Width = 260;
            }
            else if (column.DataPropertyName == nameof(ScenarioCommandProbeRow.Note))
            {
                column.HeaderText = "探针说明";
                column.Width = 300;
            }
            else if (column.DataPropertyName == nameof(ScenarioCommandProbeRow.Annotation))
            {
                column.HeaderText = "中文注释";
                column.Width = 420;
                column.ToolTipText = "根据命令字典和置信度自动生成的中文说明，帮助创作者理解候选命令。";
            }
        }

        foreach (DataGridViewRow row in _scenarioCommandProbeGrid.Rows)
        {
            if (row.DataBoundItem is not ScenarioCommandProbeRow item) continue;
            row.DefaultCellStyle.BackColor = item.Confidence switch
            {
                "低" => Color.MistyRose,
                "中" => Color.LemonChiffon,
                _ => row.DefaultCellStyle.BackColor
            };
        }
    }

    private void ConfigureScenarioStructureGrid()
    {
        foreach (DataGridViewColumn column in _scenarioStructureGrid.Columns)
        {
            if (column.DataPropertyName == nameof(ScenarioStructureRow.ParameterPreview))
            {
                column.HeaderText = "参数预览/候选解释";
                column.Width = 300;
            }
            else if (column.DataPropertyName == nameof(ScenarioStructureRow.HasCommandTemplate))
            {
                column.HeaderText = "有模板";
                column.Width = 72;
                column.ToolTipText = "是否命中内置常见 R/S eex 命令参数模板；命中后可在节点详情中查看中文参数槽解释。";
            }
            else if (column.DataPropertyName == nameof(ScenarioStructureRow.CommandTemplateHint))
            {
                column.HeaderText = "命令参数模板提示";
                column.Width = 320;
                column.ToolTipText = "按命令 ID/名称给出的常见参数槽短提示；只读候选，不作为完整命令写回依据。";
            }
            else if (column.DataPropertyName == nameof(ScenarioStructureRow.ReferenceHint))
            {
                column.HeaderText = "跨表/资源候选";
                column.Width = 360;
                column.ToolTipText = "根据命令名称和后续 16 位词自动尝试关联人物、物品、策略、地图文件或文本线索；仅作为候选提示。";
            }
            else if (column.DataPropertyName == nameof(ScenarioStructureRow.Annotation))
            {
                column.HeaderText = "中文注释";
                column.Width = 420;
            }
            else if (column.DataPropertyName == nameof(ScenarioStructureRow.NodeType))
            {
                column.HeaderText = "节点类型";
            }
            else if (column.DataPropertyName == nameof(ScenarioStructureRow.Level))
            {
                column.Visible = false;
            }
        }

        foreach (DataGridViewRow row in _scenarioStructureGrid.Rows)
        {
            if (row.DataBoundItem is not ScenarioStructureRow item) continue;
            row.DefaultCellStyle.BackColor = item.NodeType switch
            {
                "Scene候选" => Color.LightSkyBlue,
                "Section候选" => Color.LightCyan,
                _ when item.Confidence == "低" => Color.MistyRose,
                _ when item.CommandName.Contains("对话", StringComparison.Ordinal)
                        || item.CommandName.Contains("信息", StringComparison.Ordinal)
                        || item.CommandName.Contains("旁白", StringComparison.Ordinal) => Color.Honeydew,
                _ when item.CommandName.Contains("地图", StringComparison.Ordinal)
                        || item.CommandName.Contains("绘图", StringComparison.Ordinal)
                        || item.CommandName.Contains("背景", StringComparison.Ordinal) => Color.LemonChiffon,
                _ when item.HasCommandTemplate => Color.AliceBlue,
                _ => row.DefaultCellStyle.BackColor
            };
        }
    }
}
