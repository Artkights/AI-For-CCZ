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
    private async Task LoadRSceneScenariosAsync()
    {
        if (_loadingRSceneScenarioList) return;
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _loadingRSceneScenarioList = true;
            _loadRSceneButton.Enabled = false;
            _rSceneScenarioCombo.Enabled = false;
            Cursor = Cursors.WaitCursor;
            SetStatus("R场景制作：正在读取 R 剧情索引...");
            await EnsureRSceneBaseDataLoadedAsync();

            var rows = _currentScenarioFiles
                .Where(x => ScenarioFileReader.IsRsScriptFile(x.FileName) && !ScenarioFileReader.IsBattlefieldScriptFile(x.FileName))
                .OrderBy(x => int.TryParse(x.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : int.MaxValue)
                .ThenBy(x => x.FileName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            _updatingRSceneScenarioSelection = true;
            _rSceneScenarioCombo.DataSource = new BindingList<ScenarioFileInfo>(rows);
            _rSceneScenarioCombo.DisplayMember = nameof(ScenarioFileInfo.FileName);
            _rSceneScenarioCombo.ValueMember = nameof(ScenarioFileInfo.FileName);
            _rSceneScenarioCombo.SelectedIndex = rows.Count == 0 ? -1 : 0;
            _updatingRSceneScenarioSelection = false;

            ClearRSceneDocumentView(keepScenarioList: true);
            PopulateRSceneBackgroundCombo();
            LoadRSceneActorPalette();

            if (rows.Count > 0)
            {
                await LoadSelectedRSceneScenarioAsync();
            }
            else
            {
                _rSceneScriptDetailBox.Text = "R场景制作：没有找到 R_XX.eex 剧情文件。";
                RenderRSceneCanvas();
            }

            SetStatus($"R场景制作：已读取 R 剧情 {rows.Count} 个");
        }
        catch (Exception ex)
        {
            _updatingRSceneScenarioSelection = false;
            _rSceneScriptDetailBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("Load R scene scenarios failed: " + ex);
            MessageBox.Show(this, ex.Message, "读取 R 场景失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _loadingRSceneScenarioList = false;
            _loadRSceneButton.Enabled = true;
            _rSceneScenarioCombo.Enabled = true;
            Cursor = Cursors.Default;
        }
    }

    private async Task EnsureRSceneBaseDataLoadedAsync()
    {
        if (_project == null) throw new InvalidOperationException("请先加载项目。");
        var project = _project;

        if (_currentScenarioFiles.Count == 0)
        {
            _currentScenarioFiles = await Task.Run(() => new ScenarioFileReader().ReadAllIndex(project));
        }

        if (_currentImageResourceFiles.Count == 0)
        {
            _currentImageResourceFiles = await Task.Run(() => _imageResourceCatalogService.BuildCatalog(project));
        }

        if (_currentRSceneBackgroundEntries.Count == 0)
        {
            var mmap = _currentImageResourceFiles.FirstOrDefault(x => x.FileName.Equals("Mmap.e5", StringComparison.OrdinalIgnoreCase));
            _currentRSceneBackgroundEntries = mmap == null
                ? Array.Empty<ImageResourceEntryInfo>()
                : await Task.Run(() => _imageResourceCatalogService.ReadEntries(mmap));
        }
    }

    private async Task LoadSelectedRSceneScenarioAsync()
    {
        if (_updatingRSceneScenarioSelection || _loadingRSceneScenarioDocument) return;
        if (_project == null) return;
        if (_rSceneScenarioCombo.SelectedItem is not ScenarioFileInfo scenario) return;

        try
        {
            _loadingRSceneScenarioDocument = true;
            _rSceneScenarioCombo.Enabled = false;
            ClearRScenePreviewLock();
            _rScenePreviewCurrentRow = null;
            Cursor = Cursors.WaitCursor;
            SetStatus($"R场景制作：正在读取 {scenario.FileName}...");
            await EnsureRSceneBaseDataLoadedAsync();
            var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe();
            if (dictionary == null)
            {
                MessageBox.Show(this, "未找到 CczString.ini，无法按旧版命令表解析 R 剧情。", "缺少剧本字典", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var project = _project;
            var tables = _tables;
            var displayFormatter = GetLegacyScenarioCommandDisplayFormatter();
            var result = await Task.Run(() =>
            {
                LegacyScenarioDocument? legacy = null;
                try
                {
                    legacy = new LegacyScenarioReader().Read(scenario.Path, dictionary);
                }
                catch
                {
                    // Non-standard scripts still get a probe tree so the page remains usable.
                }

                if (legacy != null)
                {
                    var precedingVariableDocuments = _scriptVariableValueResolver.ReadPrecedingProjectDocuments(project, dictionary, scenario.FileName, legacy);
                    return (
                        Legacy: legacy,
                        Structure: (ScenarioStructureProbeResult?)BuildRSceneLegacyScriptStructureResult(legacy),
                        Texts: (IReadOnlyList<ScenarioTextEntry>)BuildRSceneLegacyScriptTextEntries(legacy),
                        PrecedingVariableDocuments: precedingVariableDocuments,
                        Commands: (IReadOnlyList<RSceneCommandCandidate>)Array.Empty<RSceneCommandCandidate>(),
                        StateCandidates: (IReadOnlyList<RSceneStateCandidate>)Array.Empty<RSceneStateCandidate>());
                }

                var structure = new ScenarioStructureProbeReader().Build(scenario.Path, dictionary, maxCommandRows: 600, project: project, tables: tables);
                var texts = new ScenarioTextReader().Read(scenario.Path);
                return (
                    Legacy: (LegacyScenarioDocument?)null,
                    Structure: (ScenarioStructureProbeResult?)structure,
                    Texts: (IReadOnlyList<ScenarioTextEntry>)texts,
                    PrecedingVariableDocuments: (IReadOnlyList<LegacyScenarioDocument>)Array.Empty<LegacyScenarioDocument>(),
                    Commands: (IReadOnlyList<RSceneCommandCandidate>)Array.Empty<RSceneCommandCandidate>(),
                    StateCandidates: (IReadOnlyList<RSceneStateCandidate>)Array.Empty<RSceneStateCandidate>());
            });

            _currentRSceneScenario = scenario;
            _currentRSceneLegacyScriptDocument = result.Legacy;
            _currentRScenePrecedingVariableDocuments = result.PrecedingVariableDocuments;
            ClearLegacyScenarioHistory(LegacyScriptEditorScope.RScene);
            _currentRSceneScriptStructure = result.Structure ?? throw new InvalidOperationException("R 剧情结构读取失败。");
            _currentRSceneScriptTextEntries = result.Texts;
            _currentRSceneCommandCandidates = _currentRSceneLegacyScriptDocument == null
                ? result.Commands
                : BuildRSceneCommandCandidates(_currentRSceneLegacyScriptDocument, displayFormatter);
            _currentRSceneStateCandidates = _currentRSceneLegacyScriptDocument == null
                ? result.StateCandidates
                : _rSceneDraftService.BuildSceneStateCandidates(_currentRSceneLegacyScriptDocument, BuildRSceneVariableSnapshotForCommand);
            _selectedRScenePlacedActor = null;
            ResetRScenePlayback();

            BuildRSceneScriptTree(_currentRSceneScriptStructure);
            BindRSceneStateCandidates(_currentRSceneStateCandidates);
            ApplyRSceneDraftForScenario(scenario);
            _saveRSceneDraftButton.Enabled = true;
            _saveRSceneScriptStructureButton.Enabled = _currentRSceneLegacyScriptDocument != null;
            _showRSceneVariablesButton.Enabled = _currentRSceneLegacyScriptDocument != null;
            _jumpRSceneScriptButton.Enabled = true;
            _rScenePlaybackButton.Enabled = _currentRSceneLegacyScriptDocument != null;
            UpdateRScenePreviewLockButton();
            RenderRSceneCanvas();
            SetStatus($"R场景制作：{scenario.FileName}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Load R scene document failed: " + ex);
            MessageBox.Show(this, ex.Message, "读取 R 场景失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _loadingRSceneScenarioDocument = false;
            _rSceneScenarioCombo.Enabled = true;
            Cursor = Cursors.Default;
        }
    }

    private void ClearRSceneDocumentView(bool keepScenarioList = false)
    {
        ResetRScenePlayback();
        _currentRSceneScenario = null;
        _currentRSceneLegacyScriptDocument = null;
        _currentRScenePrecedingVariableDocuments = Array.Empty<LegacyScenarioDocument>();
        ClearLegacyScenarioHistory(LegacyScriptEditorScope.RScene);
        _currentRSceneScriptStructure = null;
        _currentRSceneScriptTextEntries = Array.Empty<ScenarioTextEntry>();
        _currentRSceneCommandCandidates = Array.Empty<RSceneCommandCandidate>();
        _currentRSceneStateCandidates = Array.Empty<RSceneStateCandidate>();
        _currentRSceneDialoguePreviewCommand = null;
        _currentRSceneDialoguePreviewMessage = string.Empty;
        ClearRScenePreviewLock();
        _rScenePreviewCurrentRow = null;
        _rScenePlacedActors.Clear();
        _rSceneMapFaces.Clear();
        _selectedRScenePaletteItem = null;
        _selectedRScenePlacedActor = null;
        _editingRScenePlacedActor = null;
        _draggingRScenePlacedActor = null;
        _rSceneFrameDragStart = null;
        _rSceneFrameDragPayload = null;
        _rSceneDragPreviewPayload = null;
        _rSceneDragPreviewGrid = null;
        _rScenePlacedActorDragStart = null;
        _rScenePlacedActorDragMoved = false;
        ClearRSceneMovePreview();
        _rSceneScriptTree.Nodes.Clear();
        _rSceneCommandGrid.DataSource = null;
        if (!keepScenarioList)
        {
            _rSceneScenarioCombo.DataSource = null;
        }

        _saveRSceneDraftButton.Enabled = false;
        _saveRSceneScriptStructureButton.Enabled = false;
        _showRSceneVariablesButton.Enabled = false;
        _jumpRSceneScriptButton.Enabled = false;
        _rScenePlaybackButton.Enabled = false;
        UpdateRScenePreviewLockButton();
        _rSceneScriptDetailBox.Text = "读取 R 剧情后显示对应 R 剧本树。";
        ClearInlineRSceneScriptDialog();
    }

    private ScenarioStructureProbeResult BuildRSceneLegacyScriptStructureResult(LegacyScenarioDocument document)
    {
        _rSceneScriptCommandByKey.Clear();
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
                    rows.Add(row);
                    _rSceneScriptCommandByKey[BuildLegacyCommandKey(row)] = command;
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
            Summary = document.Summary,
            Rows = rows,
            XmlText = BuildLegacyScriptXml(document)
        };
    }

    private static IReadOnlyList<ScenarioTextEntry> BuildRSceneLegacyScriptTextEntries(LegacyScenarioDocument document)
    {
        var entries = new List<ScenarioTextEntry>();
        var index = 1;
        foreach (var command in document.EnumerateCommands())
        {
            foreach (var parameter in command.TextParameters)
            {
                entries.Add(new ScenarioTextEntry
                {
                    Index = index++,
                    Offset = parameter.FileOffset,
                    OffsetHex = FormatLegacyScriptOffset(parameter.FileOffset, index),
                    ByteLength = Math.Max(0, parameter.ByteLength - 1),
                    CharLength = parameter.Text.Length,
                    Kind = $"旧版文本参数 {command.CommandIdHex}",
                    HasNewLines = parameter.Text.Contains('\n') || parameter.Text.Contains('\r'),
                    Preview = parameter.Text.Length > 60 ? parameter.Text[..60] : parameter.Text,
                    Text = parameter.Text,
                    OriginalText = parameter.Text,
                    Annotation = $"Scene {command.SceneIndex} / Section {command.SectionIndex} / Command {command.CommandIndex} {command.CommandName} 参数槽 {parameter.Index}。"
                });
            }
        }

        return entries;
    }

    private void BuildRSceneScriptTree(ScenarioStructureProbeResult structure)
    {
        if (_currentRSceneLegacyScriptDocument != null)
        {
            BuildLegacyEditorScriptTree(
                _rSceneScriptTree,
                _currentRSceneLegacyScriptDocument,
                structure,
                _rSceneScriptItemDataByCommand,
                _rSceneScriptItemDataByRow);
            return;
        }

        _rSceneScriptTree.BeginUpdate();
        try
        {
            _rSceneScriptTree.Nodes.Clear();
            var root = new TreeNode(structure.FileName) { ToolTipText = structure.FilePath };
            foreach (var scene in structure.Rows.Where(row => row.NodeType == "Scene候选").OrderBy(row => row.SceneIndex))
            {
                var sceneNode = new TreeNode(scene.CommandName) { Tag = scene, ToolTipText = scene.Annotation };
                foreach (var section in structure.Rows
                             .Where(row => row.NodeType == "Section候选" && row.SceneIndex == scene.SceneIndex)
                             .OrderBy(row => row.SectionIndex))
                {
                    var sectionNode = new TreeNode(section.CommandName) { Tag = section, ToolTipText = section.Annotation };
                    foreach (var command in structure.Rows
                                 .Where(row => row.NodeType == "Command候选" && row.SceneIndex == section.SceneIndex && row.SectionIndex == section.SectionIndex)
                                 .OrderBy(row => row.CommandIndex))
                    {
                        sectionNode.Nodes.Add(new TreeNode(BuildScriptCommandSummary(command, includeIdentity: true, maxVisibleValues: 6))
                        {
                            Tag = command,
                            ToolTipText = BuildScriptCommandTreeToolTip(command),
                            ForeColor = GetScriptCommandColor(command.CommandId)
                        });
                    }
                    sceneNode.Nodes.Add(sectionNode);
                }
                root.Nodes.Add(sceneNode);
            }

            _rSceneScriptTree.Nodes.Add(root);
            root.Expand();
            if (root.Nodes.Count > 0) root.Nodes[0].Expand();
        }
        finally
        {
            _rSceneScriptTree.EndUpdate();
        }
    }

    private void ShowSelectedRSceneScriptNode()
    {
        if (_bindingRSceneScriptTree) return;
        if (_rSceneScriptTree.SelectedNode?.Tag is LegacyScenarioItemData { UiRow: ScenarioStructureRow itemRow } itemData)
        {
            _rSceneScriptDetailBox.Text = itemData.Command != null
                ? BuildLegacyScriptRowDetail(itemRow, itemData.Command)
                : BuildRSceneScriptRowDetail(itemRow);
            LoadInlineRSceneScriptDialogForSelection();
            SetStatus(BuildRSceneSelectedItemDataStatus(itemData));
            if (itemRow.NodeType == "Command候选")
            {
                RequestRScenePreviewToCommand(itemRow);
                SelectRSceneStateCandidateForCommand(itemRow);
            }
            return;
        }

        if (_rSceneScriptTree.SelectedNode?.Tag is not ScenarioStructureRow row)
        {
            _rScenePreviewCurrentRow = null;
            if (!_rScenePreviewLocked)
            {
                UpdateRSceneDialoguePreviewCommand((LegacyScenarioCommandNode?)null, render: true);
            }
            _rSceneScriptDetailBox.Text = BuildRSceneInfoText();
            ClearInlineRSceneScriptDialog("请选择左侧 R 剧本指令。");
            SetStatus("R场景制作：未选择旧版命令");
            return;
        }

        _rSceneScriptDetailBox.Text = BuildRSceneScriptRowDetail(row);
        LoadInlineRSceneScriptDialogForSelection();
        SetStatus(row.CommandName);
        if (row.NodeType == "Command候选")
        {
            RequestRScenePreviewToCommand(row);
            SelectRSceneStateCandidateForCommand(row);
        }
        else if (!_rScenePreviewLocked)
        {
            UpdateRSceneDialoguePreviewCommand((LegacyScenarioCommandNode?)null, render: true);
        }
    }

    private void ClearInlineRSceneScriptDialog(string message = "请选择左侧 R 剧本指令。")
    {
        _applyRSceneInlineDialogButton.Enabled = false;
        _resetRSceneInlineDialogButton.Enabled = false;
        _rSceneInlineDialogHost.ClearDialog(message);
    }

    private void LoadInlineRSceneScriptDialogForSelection()
    {
        _applyRSceneInlineDialogButton.Enabled = false;
        _resetRSceneInlineDialogButton.Enabled = false;

        if (!TryGetSelectedRSceneLegacyItemData(out var itemData) || itemData.Command == null)
        {
            _rSceneInlineDialogHost.ClearDialog("请选择左侧 R 剧本指令。");
            return;
        }

        if (!LegacyCommandEditDispatcher.CanEdit(itemData.Id))
        {
            _rSceneInlineDialogHost.ClearDialog("该命令在旧版源码中没有修改窗口。");
            return;
        }

        var dialogName = LegacyCommandEditDispatcher.GetDialogName(itemData.Id);
        if (!LegacyMfcDialogCatalog.TryGet(dialogName, out var spec))
        {
            _rSceneInlineDialogHost.ClearDialog("该旧版 Dialog 尚未迁移为 MFC 控件。");
            return;
        }

        var dialogDataSources = LegacyMfcDialogDataSources.Create(_project, _tables);
        var precedingSameCommandCount = CountPrecedingSameLegacyCommands(_currentRSceneLegacyScriptDocument, itemData.Command);
        _rSceneInlineDialogHost.LoadDialog(
            itemData,
            spec,
            dialogDataSources,
            _currentRSceneLegacyScriptDocument?.CommandCount ?? 0,
            precedingSameCommandCount,
            includeDialogButtons: false);
        _applyRSceneInlineDialogButton.Enabled = true;
        _resetRSceneInlineDialogButton.Enabled = true;
    }

    private void ApplyInlineRSceneScriptDialog()
    {
        if (_currentRSceneLegacyScriptDocument == null)
        {
            MessageBox.Show(this, "当前 R 剧情没有进入旧版完整树模式，无法修改参数。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!TryGetSelectedRSceneLegacyItemData(out var itemData) || itemData.Command == null)
        {
            MessageBox.Show(this, "请先在左侧 R 剧本树中选择一条命令。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var command = itemData.Command;
        var oldSummary = BuildLegacyScriptParameterPreview(command);
        var beforeEdit = CaptureLegacyScenarioHistorySnapshot(LegacyScriptEditorScope.RScene, _currentRSceneLegacyScriptDocument);
        var error = _rSceneInlineDialogHost.CommitToTarget();
        if (!string.IsNullOrWhiteSpace(error))
        {
            MessageBox.Show(this, error, "参数值无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        CopyLegacyItemDataToCommand(itemData);
        NormalizeEditedRSceneJumpCommand(command);
        if (oldSummary == BuildLegacyScriptParameterPreview(command))
        {
            SetStatus($"R场景参数：{command.CommandIdHex} {command.CommandName} 未检测到改动");
            LoadInlineRSceneScriptDialogForSelection();
            return;
        }

        PushLegacyScenarioUndoSnapshot(LegacyScriptEditorScope.RScene, beforeEdit);
        var refreshedCommand = RefreshLegacyEditorCommandInPlace(LegacyScriptEditorScope.RScene, command)
            ? command
            : RefreshRSceneLegacyScriptViewAndSelect(command);
        _saveRSceneScriptStructureButton.Enabled = true;
        SetStatus($"R场景参数：{refreshedCommand.CommandIdHex} {refreshedCommand.CommandName}，{oldSummary} -> {BuildLegacyScriptParameterPreview(refreshedCommand)}，需完整保存R剧本");
    }

    private string BuildRSceneSelectedItemDataStatus(LegacyScenarioItemData itemData)
    {
        if (itemData.Command == null)
        {
            return itemData.UiRow is ScenarioStructureRow row ? row.CommandName : "R场景制作：旧版结构节点";
        }

        var suffix = itemData.Command.CommandId == 0x76
            ? itemData.Command.JumpTargetOrdinal.HasValue
                ? $" target ord {itemData.Command.JumpTargetOrdinal.Value}"
                : " target ord 未解析"
            : string.Empty;
        return $"旧版 ItemData：ord {itemData.Ord} id {HexDisplayFormatter.Format(itemData.Id, 2)}{suffix}";
    }

    private void NormalizeEditedRSceneJumpCommand(LegacyScenarioCommandNode command)
    {
        if (command.CommandId != 0x76 || _currentRSceneLegacyScriptDocument == null)
        {
            return;
        }

        var targetOrdinal = command.JumpTargetOrdinal;
        if (!targetOrdinal.HasValue)
        {
            return;
        }

        var target = _currentRSceneLegacyScriptDocument.EnumerateCommands()
            .FirstOrDefault(candidate => candidate.CommandOrdinal == targetOrdinal.Value);
        command.JumpTargetCommandIndex = target?.CommandIndex;
        if (target == null)
        {
            command.OriginalJumpDisplacement = null;
        }
    }

    private LegacyScenarioCommandNode RefreshRSceneLegacyScriptViewAndSelect(LegacyScenarioCommandNode command)
    {
        RefreshRSceneLegacyScriptView(command);
        if (TryGetSelectedRSceneLegacyItemData(out var selected) && selected.Command != null)
        {
            return selected.Command;
        }

        return command;
    }

    private string BuildRSceneScriptRowDetail(ScenarioStructureRow row)
    {
        if (row.NodeType != "Command候选")
        {
            return $"{row.CommandName}\r\n类型：{row.NodeType}\r\n位置：Scene {row.SceneIndex} / Section {row.SectionIndex}\r\n中文注释：{row.Annotation}";
        }

        return
            $"命令：{row.CommandIdHex} {row.CommandName}\r\n" +
            $"位置：Scene {row.SceneIndex} / Section {row.SectionIndex} / Command {row.CommandIndex} / {row.OffsetHex}\r\n" +
            $"参数：\r\n{BuildValueDetailBlock(BuildScenarioStructurePreviewValueLines(row))}\r\n" +
            $"模板：{row.CommandTemplateHint}\r\n" +
            $"引用：{row.ReferenceHint}\r\n" +
            $"中文注释：{row.Annotation}";
    }

    private void BindRSceneStateCandidates(IReadOnlyList<RSceneStateCandidate> rows)
    {
        _rSceneCommandGrid.DataSource = new BindingList<RSceneStateCandidate>(rows.ToList());
        foreach (DataGridViewColumn column in _rSceneCommandGrid.Columns)
        {
            column.HeaderText = column.DataPropertyName switch
            {
                nameof(RSceneStateCandidate.Index) => "序号",
                nameof(RSceneStateCandidate.SceneTitle) => "R场景",
                nameof(RSceneStateCandidate.SceneIndex) => "Scene",
                nameof(RSceneStateCandidate.SectionIndex) => "Section",
                nameof(RSceneStateCandidate.StartCommandIndex) => "起始命令",
                nameof(RSceneStateCandidate.EndCommandIndex) => "结束命令",
                nameof(RSceneStateCandidate.OffsetHex) => "偏移",
                nameof(RSceneStateCandidate.BackgroundImageNumber) => "背景",
                nameof(RSceneStateCandidate.ActorCount) => "人数",
                nameof(RSceneStateCandidate.MapFaceCount) => "头像",
                nameof(RSceneStateCandidate.Summary) => "摘要",
                nameof(RSceneStateCandidate.TargetKey) => "内部键",
                _ => column.HeaderText
            };
            if (column.DataPropertyName is nameof(RSceneStateCandidate.TargetKey) or nameof(RSceneStateCandidate.CurrentCommandIndex))
            {
                column.Visible = false;
            }
            if (column.DataPropertyName is nameof(RSceneStateCandidate.SceneTitle) or nameof(RSceneStateCandidate.Summary))
            {
                column.Width = 220;
            }
        }
    }

    private void ShowSelectedRSceneCommandCandidate()
    {
        var candidate = GetSelectedRSceneStateCandidate();
        if (candidate == null) return;
        if (!_bindingRSceneCommandSelection)
        {
            _bindingRSceneCommandSelection = true;
            try
            {
                SelectRSceneStateCandidateInScriptTree(-1);
            }
            finally
            {
                _bindingRSceneCommandSelection = false;
            }
        }
    }

    private void SelectRSceneStateCandidateInScriptTree(int rowIndex)
    {
        var candidate = rowIndex >= 0 && rowIndex < _rSceneCommandGrid.Rows.Count
            ? _rSceneCommandGrid.Rows[rowIndex].DataBoundItem as RSceneStateCandidate
            : GetSelectedRSceneStateCandidate();
        if (candidate == null) return;
        var row = FindRSceneScriptRow(candidate);
        if (row == null)
        {
            SetStatus("R场景制作：没有在左侧剧本树找到对应命令。");
            return;
        }

        SelectRSceneScriptTreeNode(row);
        SetStatus($"R场景制作：已定位 {candidate.SceneTitle}");
    }

    private void SelectRSceneCommandCandidateInScriptTree(int rowIndex)
        => SelectRSceneStateCandidateInScriptTree(rowIndex);

    private RSceneStateCandidate? GetSelectedRSceneStateCandidate()
    {
        if (_rSceneCommandGrid.CurrentRow?.DataBoundItem is RSceneStateCandidate current) return current;
        if (_rSceneCommandGrid.SelectedRows.Count > 0 && _rSceneCommandGrid.SelectedRows[0].DataBoundItem is RSceneStateCandidate selected) return selected;
        return null;
    }

    private ScenarioStructureRow? FindRSceneScriptRow(RSceneStateCandidate candidate)
    {
        if (_currentRSceneScriptStructure == null) return null;
        return _currentRSceneScriptStructure.Rows.FirstOrDefault(row =>
            row.NodeType == "Command候选" &&
            row.SceneIndex == candidate.SceneIndex &&
            row.SectionIndex == candidate.SectionIndex &&
            row.CommandIndex == candidate.StartCommandIndex &&
            row.CommandId == 0x27 &&
            row.OffsetHex.Equals(candidate.OffsetHex, StringComparison.OrdinalIgnoreCase));
    }

    private ScenarioStructureRow? FindRSceneScriptRowForCommand(LegacyScenarioCommandNode command)
    {
        if (_currentRSceneScriptStructure == null) return null;
        return _currentRSceneScriptStructure.Rows.FirstOrDefault(row =>
            row.NodeType == "Command候选" &&
            row.SceneIndex == command.SceneIndex &&
            row.SectionIndex == command.SectionIndex &&
            row.CommandIndex == command.CommandIndex &&
            row.CommandId == command.CommandId);
    }

    private static string BuildRSceneCommandTargetKey(LegacyScenarioCommandNode command)
        => $"Scene={command.SceneIndex};Section={command.SectionIndex};Command={command.CommandIndex};Offset={HexDisplayFormatter.FormatOffset(command.FileOffset)};Id={HexDisplayFormatter.NormalizeText(command.CommandIdHex)}";

    private static string BuildRSceneCommandTargetKey(ScenarioStructureRow row)
        => $"Scene={row.SceneIndex};Section={row.SectionIndex};Command={row.CommandIndex};Offset={row.OffsetHex};Id={row.CommandIdHex}";

    private static bool TryParseRSceneTargetKey(string targetKey, out int scene, out int section, out int command, out string offsetHex, out string commandIdHex)
    {
        scene = section = command = -1;
        offsetHex = string.Empty;
        commandIdHex = string.Empty;
        if (string.IsNullOrWhiteSpace(targetKey)) return false;

        foreach (var part in targetKey.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2) continue;
            var key = pair[0].Trim();
            var value = pair[1].Trim();
            if (key.Equals("Scene", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out scene);
            }
            else if (key.Equals("Section", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out section);
            }
            else if (key.Equals("Command", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out command);
            }
            else if (key.Equals("Offset", StringComparison.OrdinalIgnoreCase))
            {
                offsetHex = value;
            }
            else if (key.Equals("Id", StringComparison.OrdinalIgnoreCase))
            {
                commandIdHex = value;
            }
        }

        return scene >= 0 && section >= 0 && command >= 0;
    }

    private bool SelectRSceneScriptTreeNode(ScenarioStructureRow target, bool ensureVisible = true)
    {
        foreach (TreeNode root in _rSceneScriptTree.Nodes)
        {
            var found = FindScriptTreeNode(root, target);
            if (found == null) continue;
            if (ReferenceEquals(_rSceneScriptTree.SelectedNode, found))
            {
                return true;
            }

            _rSceneScriptTree.SelectedNode = found;
            if (ensureVisible)
            {
                found.EnsureVisible();
            }
            return true;
        }

        return false;
    }

    private void PopulateRSceneBackgroundCombo()
    {
        var selectedNumber = (_rSceneBackgroundCombo.SelectedItem as RSceneBackgroundComboItem)?.ImageNumber;
        var rows = _currentRSceneBackgroundEntries
            .OrderBy(x => x.ImageNumber)
            .Select(x => new RSceneBackgroundComboItem(x))
            .ToList();
        _rSceneBackgroundCombo.DataSource = new BindingList<RSceneBackgroundComboItem>(rows);
        _rSceneBackgroundCombo.DisplayMember = nameof(RSceneBackgroundComboItem.DisplayText);
        _rSceneBackgroundCombo.ValueMember = nameof(RSceneBackgroundComboItem.ImageNumber);
        if (rows.Count == 0)
        {
            _rSceneBackgroundCombo.SelectedIndex = -1;
            return;
        }

        var index = selectedNumber.HasValue
            ? rows.FindIndex(x => x.ImageNumber == selectedNumber.Value)
            : -1;
        _rSceneBackgroundCombo.SelectedIndex = Math.Max(0, index);
    }

    private void SelectRSceneBackgroundImageNumber(int imageNumber)
    {
        if (imageNumber <= 0) return;
        for (var i = 0; i < _rSceneBackgroundCombo.Items.Count; i++)
        {
            if (_rSceneBackgroundCombo.Items[i] is not RSceneBackgroundComboItem item || item.ImageNumber != imageNumber) continue;
            _rSceneBackgroundCombo.SelectedIndex = i;
            return;
        }
    }

    private void ToggleRScenePreviewLock()
    {
        if (_rScenePreviewLocked)
        {
            _rScenePreviewLocked = false;
            _rScenePreviewLockedRow = null;
            _rScenePreviewLockedCommand = null;
            UpdateRScenePreviewLockButton();
            var selected = GetSelectedRSceneScriptCommandRow();
            if (selected != null)
            {
                RefreshRScenePreviewToCommand(selected);
                SetStatus($"R场景预览：已解锁，跟随当前指令 {selected.CommandIdHex} {selected.CommandName}");
            }
            else
            {
                SetStatus("R场景预览：已解锁，等待选择指令。");
            }
            return;
        }

        var target = _rScenePreviewCurrentRow ?? GetSelectedRSceneScriptCommandRow();
        if (target == null || target.NodeType != "Command候选")
        {
            MessageBox.Show(this, "请先选择一条 R 剧本指令，再锁定预览。", "无法锁定预览", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _rScenePreviewLocked = true;
        SetRScenePreviewLockTarget(target);
        UpdateRScenePreviewLockButton();
        SetStatus($"R场景预览：已锁定到 {target.CommandIdHex} {target.CommandName} / Scene {target.SceneIndex} / Section {target.SectionIndex} / Command {target.CommandIndex}");
    }

    private void ClearRScenePreviewLock()
    {
        _rScenePreviewLocked = false;
        _rScenePreviewLockedRow = null;
        _rScenePreviewLockedCommand = null;
        UpdateRScenePreviewLockButton();
    }

    private void UpdateRScenePreviewLockButton()
    {
        _rScenePreviewLockButton.Enabled = _currentRSceneLegacyScriptDocument != null;
        _rScenePreviewLockButton.Text = _rScenePreviewLocked ? "解锁预览" : "锁定预览";
    }

    private void SetRScenePreviewLockTarget(ScenarioStructureRow row)
    {
        _rScenePreviewLockedRow = row;
        _rScenePreviewLockedCommand = TryGetRSceneLegacyCommandForRow(row, out var command) ? command : null;
    }

    private void RequestRScenePreviewToCommand(ScenarioStructureRow row)
    {
        if (row.NodeType != "Command候选") return;
        if (_rScenePreviewLocked)
        {
            ReapplyRSceneLockedPreview();
            return;
        }

        RefreshRScenePreviewToCommand(row);
    }

    private void ReapplyRSceneLockedPreview()
    {
        if (!_rScenePreviewLocked) return;
        var target = ResolveRScenePreviewLockedRow();
        if (target == null)
        {
            ClearRScenePreviewLock();
            SetStatus("R场景预览：锁定目标已不存在，已自动解锁。");
            return;
        }

        RefreshRScenePreviewToCommand(target);
    }

    private ScenarioStructureRow? ResolveRScenePreviewLockedRow()
    {
        if (_currentRSceneScriptStructure == null) return null;

        if (_rScenePreviewLockedCommand != null)
        {
            var target = FindRSceneScriptRowByCommandReference(_rScenePreviewLockedCommand);
            if (target != null)
            {
                _rScenePreviewLockedRow = target;
                return target;
            }
        }

        var lockedRow = _rScenePreviewLockedRow;
        if (lockedRow == null) return null;
        var row = FindMatchingRSceneScriptRow(lockedRow);
        if (row != null)
        {
            _rScenePreviewLockedRow = row;
            _rScenePreviewLockedCommand = TryGetRSceneLegacyCommandForRow(row, out var command) ? command : null;
        }

        return row;
    }

    private ScenarioStructureRow? FindMatchingRSceneScriptRow(ScenarioStructureRow target)
    {
        if (_currentRSceneScriptStructure == null) return null;
        var rows = _currentRSceneScriptStructure.Rows.Where(row =>
            row.NodeType == "Command候选" &&
            row.SceneIndex == target.SceneIndex &&
            row.SectionIndex == target.SectionIndex &&
            row.CommandId == target.CommandId).ToList();
        var exact = rows.FirstOrDefault(row =>
            row.CommandIndex == target.CommandIndex &&
            row.OffsetHex.Equals(target.OffsetHex, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
        {
            return exact;
        }

        var hasStableOffset = !string.IsNullOrWhiteSpace(target.OffsetHex) &&
                              !target.OffsetHex.Equals("0x000000", StringComparison.OrdinalIgnoreCase);
        if (hasStableOffset)
        {
            return rows.FirstOrDefault(row => row.OffsetHex.Equals(target.OffsetHex, StringComparison.OrdinalIgnoreCase));
        }

        return rows.FirstOrDefault(row => row.CommandIndex == target.CommandIndex);
    }

    private bool TryGetRSceneLegacyCommandForRow(ScenarioStructureRow row, out LegacyScenarioCommandNode command)
        => _rSceneScriptCommandByKey.TryGetValue(BuildLegacyCommandKey(row), out command!);

    private void RefreshRScenePreviewToCommand(ScenarioStructureRow row)
    {
        if (_currentRSceneLegacyScriptDocument == null || row.NodeType != "Command候选") return;
        _rScenePreviewCurrentRow = row;
        UpdateRSceneDialoguePreviewCommand(row);
        var section = FindRSceneSection(row.SceneIndex, row.SectionIndex);
        if (section == null) return;

        var snapshot = _rSceneDraftService.BuildStateSnapshot(section, row.CommandIndex, BuildRSceneVariableSnapshotForCommand);
        ApplyRSceneStateSnapshot(snapshot, row);
    }

    private void UpdateRSceneDialoguePreviewCommand(ScenarioStructureRow row)
    {
        if (_rSceneScriptCommandByKey.TryGetValue(BuildLegacyCommandKey(row), out var command))
        {
            UpdateRSceneDialoguePreviewCommand(command);
            return;
        }

        UpdateRSceneDialoguePreviewCommand((LegacyScenarioCommandNode?)null);
    }

    private void UpdateRSceneDialoguePreviewCommand(LegacyScenarioCommandNode? command, bool render = false)
    {
        _currentRSceneDialoguePreviewCommand = command != null && RSceneDialoguePreviewService.IsPreviewCommand(command.CommandId)
            ? command
            : null;
        _currentRSceneDialoguePreviewMessage = _currentRSceneDialoguePreviewCommand == null
            ? string.Empty
            : _rSceneDialoguePreviewService.BuildPreviewModel(
                _currentRSceneDialoguePreviewCommand,
                BuildRSceneDialoguePreviewPeople(),
                ResolveRScenePersonReference)?.Detail ?? string.Empty;
        if (render)
        {
            RenderRSceneCanvas();
        }
    }

    private LegacyScenarioSection? FindRSceneSection(int sceneIndex, int sectionIndex)
        => _currentRSceneLegacyScriptDocument?.Scenes
            .FirstOrDefault(scene => scene.SceneIndex == sceneIndex)?
            .Sections.FirstOrDefault(section => section.SectionIndex == sectionIndex);

    private ScriptVariableValueSnapshot? BuildRSceneVariableSnapshotForCommand(LegacyScenarioCommandNode command, int parameterIndex)
    {
        if (_currentRSceneLegacyScriptDocument == null)
        {
            return null;
        }

        return _scriptVariableValueResolver.BuildSnapshotToCommand(
            _currentRSceneLegacyScriptDocument,
            command,
            _currentRScenePrecedingVariableDocuments.Where(document => !ReferenceEquals(document, _currentRSceneLegacyScriptDocument)));
    }

    private int? ResolveRScenePersonReference(LegacyScenarioCommandNode command, int parameterIndex)
    {
        if (parameterIndex < 0 || parameterIndex >= command.Parameters.Count)
        {
            return null;
        }

        var parameter = command.Parameters[parameterIndex];
        if (parameter.Kind is not (LegacyScenarioParameterKind.Word16 or LegacyScenarioParameterKind.Dword32))
        {
            return null;
        }

        return ScriptVariableValueResolver.TryResolvePerson2Reference(
            parameter.IntValue,
            BuildRSceneVariableSnapshotForCommand(command, parameterIndex),
            out var personId,
            out _)
            ? personId
            : null;
    }

    private void ApplyRSceneStateSnapshot(RSceneStateSnapshot snapshot, ScenarioStructureRow? selectedCommandRow = null)
    {
        _rScenePlacedActors.Clear();
        _rSceneMapFaces.Clear();
        _selectedRScenePlacedActor = null;
        _editingRScenePlacedActor = null;
        _draggingRScenePlacedActor = null;
        _rScenePlacedActorDragStart = null;
        _rScenePlacedActorDragMoved = false;
        ClearRSceneMovePreview();

        using (SuppressRSceneCanvasRender())
        {
            if (snapshot.BackgroundImageNumber.HasValue)
            {
                SelectRSceneBackgroundImageNumber(snapshot.BackgroundImageNumber.Value);
            }
        }

        var byPersonId = _rSceneActorPaletteItems
            .GroupBy(x => x.PersonId)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var state in snapshot.Actors)
        {
            if (!byPersonId.TryGetValue(state.PersonId, out var item)) continue;
            var actor = BuildRScenePlacedActor(item, state.GridX, state.GridY, string.IsNullOrWhiteSpace(state.Source) ? "R剧本状态预览" : state.Source);
            actor.TargetKey = state.TargetKey;
            actor.LastActionTargetKey = string.IsNullOrWhiteSpace(state.LastActionTargetKey) ? state.TargetKey : state.LastActionTargetKey;
            actor.Facing = NormalizeRSceneFacing(state.Facing);
            actor.FrameIndex = Math.Clamp(state.FrameIndex, 0, RSceneFrameCount - 1);
            actor.ActorNote = $"从当前 Section 状态推演：人物={state.PersonId} 坐标=({state.GridX},{state.GridY}) 方向={actor.Facing} 动作帧={actor.FrameIndex}。";
            _rScenePlacedActors.Add(actor);
        }

        _rSceneMapFaces.AddRange(snapshot.MapFaces);

        SelectRSceneActorForCommandRow(selectedCommandRow);
        RenderRSceneCanvas();
    }

    private void SelectRSceneActorForCommandRow(ScenarioStructureRow? row)
    {
        if (row == null || row.NodeType != "Command候选")
        {
            return;
        }

        var targetKey = BuildRSceneCommandTargetKey(row);
        var actor = _rScenePlacedActors.FirstOrDefault(candidate =>
                        string.Equals(candidate.LastActionTargetKey, targetKey, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(candidate.TargetKey, targetKey, StringComparison.OrdinalIgnoreCase))
                    ?? FindRSceneActorByCommandPerson(row);
        if (actor == null)
        {
            return;
        }

        _selectedRScenePlacedActor = actor;
        _editingRScenePlacedActor = null;
        SyncRSceneControlPanelFromPlacedActor(actor);
    }

    private RScenePlacedActor? FindRSceneActorByCommandPerson(ScenarioStructureRow row)
    {
        if (!_rSceneScriptCommandByKey.TryGetValue(BuildLegacyCommandKey(row), out var command))
        {
            return null;
        }

        var personId = TryGetRSceneCommandPersonId(command);
        return personId.HasValue
            ? _rScenePlacedActors.FirstOrDefault(actor => actor.PersonId == personId.Value)
            : null;
    }

    private int? TryGetRSceneCommandPersonId(LegacyScenarioCommandNode command)
    {
        var values = EnumerateRSceneCommandNumericValues(command).ToList();
        var slot = command.CommandId switch
        {
            0x29 or 0x2A or 0x30 or 0x33 or 0x34 or 0x35 when values.Count > 0 => 0,
            0x31 when values.Count > 1 && values[0] == 0 => 1,
            0x32 when values.Count > 1 && values[0] != 1 => 1,
            _ => -1
        };
        if (slot < 0)
        {
            return null;
        }

        var resolved = ResolveRScenePersonReference(command, slot);
        if (resolved.HasValue)
        {
            return resolved;
        }

        var personId = values[slot];
        return personId is >= 0 and <= 1023 ? personId : null;
    }

    private static IEnumerable<int> EnumerateRSceneCommandNumericValues(LegacyScenarioCommandNode command)
    {
        foreach (var parameter in command.Parameters)
        {
            if (parameter.Kind == LegacyScenarioParameterKind.Text)
            {
                continue;
            }

            if (parameter.Kind == LegacyScenarioParameterKind.VariableArray)
            {
                foreach (var value in parameter.Values)
                {
                    yield return value;
                }
                continue;
            }

            yield return parameter.IntValue;
        }
    }

    private void SelectRSceneStateCandidateForCommand(ScenarioStructureRow row)
    {
        if (_bindingRSceneCommandSelection) return;
        var candidate = _currentRSceneStateCandidates
            .Where(candidate => candidate.SceneIndex == row.SceneIndex &&
                                candidate.SectionIndex == row.SectionIndex &&
                                candidate.StartCommandIndex <= row.CommandIndex &&
                                row.CommandIndex <= candidate.EndCommandIndex)
            .OrderByDescending(candidate => candidate.StartCommandIndex)
            .FirstOrDefault();
        if (candidate == null) return;

        _bindingRSceneCommandSelection = true;
        try
        {
            SelectGridRow<RSceneStateCandidate>(_rSceneCommandGrid, item => ReferenceEquals(item, candidate) ||
                item.SceneIndex == candidate.SceneIndex &&
                item.SectionIndex == candidate.SectionIndex &&
                item.StartCommandIndex == candidate.StartCommandIndex);
        }
        finally
        {
            _bindingRSceneCommandSelection = false;
        }
    }

    private void ToggleRScenePlayback()
    {
        if (_rScenePlaybackTimer.Enabled)
        {
            PauseRScenePlayback("已暂停");
            return;
        }

        StartRScenePlayback();
    }

    private void StartRScenePlayback()
    {
        if (_currentRSceneLegacyScriptDocument == null || _currentRSceneScriptStructure == null)
        {
            MessageBox.Show(this, "请先读取一个旧版 R 剧情。", "无法播放", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var startRow = GetSelectedRSceneScriptCommandRow();
        if (startRow == null)
        {
            MessageBox.Show(this, "请先在左侧 R 剧本树中选择一条开始指令。", "无法播放", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _rScenePlaybackRows = BuildRScenePlaybackRows(startRow);
        if (_rScenePlaybackRows.Count == 0)
        {
            MessageBox.Show(this, "当前 Section 中没有可影响 R 场景预览的后续指令。", "无法播放", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _rScenePlaybackIndex = 0;
        UpdateRScenePlaybackTimerInterval();
        _rScenePlaybackTimer.Start();
        _rScenePlaybackButton.Text = "暂停";
        _rScenePlaybackStatusLabel.Text = $"播放：{_rScenePlaybackIndex + 1}/{_rScenePlaybackRows.Count}";
        SelectRScenePlaybackRow(_rScenePlaybackRows[_rScenePlaybackIndex]);
        SetStatus($"R场景制作：开始播放 Scene {startRow.SceneIndex} / Section {startRow.SectionIndex}，共 {_rScenePlaybackRows.Count} 条指令");
    }

    private IReadOnlyList<ScenarioStructureRow> BuildRScenePlaybackRows(ScenarioStructureRow startRow)
    {
        if (_currentRSceneScriptStructure == null) return Array.Empty<ScenarioStructureRow>();
        return _currentRSceneScriptStructure.Rows
            .Where(row => row.NodeType == "Command候选" &&
                          row.SceneIndex == startRow.SceneIndex &&
                          row.SectionIndex == startRow.SectionIndex &&
                          row.CommandIndex >= startRow.CommandIndex &&
                          IsRScenePlaybackCommand(row.CommandId))
            .OrderBy(row => row.CommandIndex)
            .ToList();
    }

    private static bool IsRScenePlaybackCommand(int commandId)
        => commandId is 0x27 or 0x29 or 0x2A or 0x2B or 0x2F or 0x30 or 0x31 or 0x32 or 0x33 or 0x34 ||
           RSceneDialoguePreviewService.IsPreviewCommand(commandId);

    private void AdvanceRScenePlayback()
    {
        if (_rScenePlaybackRows.Count == 0)
        {
            PauseRScenePlayback("无播放指令");
            return;
        }

        _rScenePlaybackIndex++;
        if (_rScenePlaybackIndex >= _rScenePlaybackRows.Count)
        {
            _rScenePlaybackIndex = _rScenePlaybackRows.Count - 1;
            PauseRScenePlayback("已结束");
            return;
        }

        _rScenePlaybackStatusLabel.Text = $"播放：{_rScenePlaybackIndex + 1}/{_rScenePlaybackRows.Count}";
        SelectRScenePlaybackRow(_rScenePlaybackRows[_rScenePlaybackIndex]);
    }

    private void SelectRScenePlaybackRow(ScenarioStructureRow row)
    {
        if (!SelectRSceneScriptTreeNode(row))
        {
            RequestRScenePreviewToCommand(row);
        }
    }

    private void PauseRScenePlayback(string status)
    {
        _rScenePlaybackTimer.Stop();
        _rScenePlaybackButton.Text = "开始";
        _rScenePlaybackStatusLabel.Text = $"播放：{status}";
        SetStatus("R场景制作：播放" + status);
    }

    private void ResetRScenePlayback()
    {
        _rScenePlaybackTimer.Stop();
        _rScenePlaybackRows = Array.Empty<ScenarioStructureRow>();
        _rScenePlaybackIndex = -1;
        _rScenePlaybackButton.Text = "开始";
        _rScenePlaybackStatusLabel.Text = "播放：未开始";
    }

    private void UpdateRScenePlaybackTimerInterval()
    {
        _rScenePlaybackTimer.Interval = Math.Clamp((int)_rScenePlaybackDelayInput.Value, 50, 10000);
    }

    private void LoadRSceneActorPalette()
    {
        _rSceneActorPaletteItems = Array.Empty<RSceneActorPaletteItem>();
        if (_project == null || _tables.Count == 0)
        {
            BindRSceneActorPalette(_rSceneActorPaletteItems);
            return;
        }

        try
        {
            var personTable = FindTable(_tables, "6.5-0 人物");
            var rTable = FindTable(_tables, "6.5-0-4 R形象");
            var sTable = FindTable(_tables, "6.5-0-5 S形象");
            var jobTable = FindTable(_tables, "6.5-4 详细兵种");
            var personRead = _tableReader.Read(_project, personTable, _tables);
            var rRead = _tableReader.Read(_project, rTable, _tables);
            var sRead = _tableReader.Read(_project, sTable, _tables);
            var jobRead = _tableReader.Read(_project, jobTable, _tables);
            if (!personRead.Validation.IsUsable || !rRead.Validation.IsUsable || !sRead.Validation.IsUsable)
            {
                BindRSceneActorPalette(_rSceneActorPaletteItems);
                return;
            }

            var jobNames = new Dictionary<int, string>();
            if (jobRead.Validation.IsUsable)
            {
                foreach (DataRow jobRow in jobRead.Data.Rows)
                {
                    if (!jobRead.Data.Columns.Contains("ID") || !jobRead.Data.Columns.Contains("名称")) break;
                    var jobId = Convert.ToInt32(jobRow["ID"], CultureInfo.InvariantCulture);
                    var jobName = Convert.ToString(jobRow["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
                    jobNames[jobId] = string.IsNullOrWhiteSpace(jobName) ? $"职业{jobId}" : jobName;
                }
            }

            var count = Math.Min(personRead.Data.Rows.Count, Math.Min(rRead.Data.Rows.Count, sRead.Data.Rows.Count));
            var rows = new List<RSceneActorPaletteItem>();
            for (var i = 0; i < count; i++)
            {
                var personRow = personRead.Data.Rows[i];
                var id = personRead.Data.Columns.Contains("ID")
                    ? Convert.ToInt32(personRow["ID"], CultureInfo.InvariantCulture)
                    : i;
                var name = personRead.Data.Columns.Contains("名称")
                    ? Convert.ToString(personRow["名称"], CultureInfo.InvariantCulture) ?? string.Empty
                    : string.Empty;
                var faceId = personRead.Data.Columns.Contains("头像")
                    ? Convert.ToInt32(personRow["头像"], CultureInfo.InvariantCulture)
                    : (int?)null;
                var jobId = personRead.Data.Columns.Contains("职业")
                    ? Convert.ToInt32(personRow["职业"], CultureInfo.InvariantCulture)
                    : (int?)null;
                var rId = Convert.ToInt32(rRead.Data.Rows[i]["R形象编号"], CultureInfo.InvariantCulture);
                var sId = Convert.ToInt32(sRead.Data.Rows[i]["S形象编号"], CultureInfo.InvariantCulture);
                rows.Add(new RSceneActorPaletteItem
                {
                    Index = rows.Count + 1,
                    PersonId = id,
                    Name = string.IsNullOrWhiteSpace(name) ? $"人物{id}" : name,
                    FaceId = faceId,
                    JobId = jobId,
                    JobName = jobId.HasValue ? jobNames.GetValueOrDefault(jobId.Value, $"职业{jobId.Value}") : string.Empty,
                    RImageId = rId,
                    SImageId = sId
                });
            }

            _rSceneActorPaletteItems = rows;
            BindRSceneActorPalette(rows);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("读取 R 场景角色列表失败：" + ex.Message);
            BindRSceneActorPalette(_rSceneActorPaletteItems);
        }
    }

    private void BindRSceneActorPalette(IEnumerable<RSceneActorPaletteItem> rows)
    {
        var list = rows.ToList();
        var selectedPersonId = _selectedRScenePaletteItem?.PersonId;
        _rSceneActorListBox.DataSource = null;
        _rSceneActorListBox.DataSource = new BindingList<RSceneActorPaletteItem>(list);
        _rSceneActorListBox.DisplayMember = nameof(RSceneActorPaletteItem.DisplayText);
        if (list.Count == 0)
        {
            RefreshRScenePaletteActorPreview(null);
            return;
        }

        var selectedIndex = selectedPersonId.HasValue
            ? list.FindIndex(item => item.PersonId == selectedPersonId.Value)
            : 0;
        _rSceneActorListBox.SelectedIndex = Math.Max(0, selectedIndex);
        RefreshRScenePaletteActorPreview(_rSceneActorListBox.SelectedItem as RSceneActorPaletteItem);
    }

    private void ApplyRSceneActorPaletteFilter()
    {
        var keyword = _rSceneActorFilterBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            BindRSceneActorPalette(_rSceneActorPaletteItems);
            return;
        }

        BindRSceneActorPalette(_rSceneActorPaletteItems.Where(item =>
            item.PersonId.ToString(CultureInfo.InvariantCulture).Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            item.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
            item.JobName.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
            item.RImageId.ToString(CultureInfo.InvariantCulture).Contains(keyword, StringComparison.OrdinalIgnoreCase)));
    }

    private void ShowSelectedRScenePaletteActor()
    {
        if (_bindingRSceneControlPanel) return;
        RefreshRScenePaletteActorPreview(_rSceneActorListBox.SelectedItem as RSceneActorPaletteItem);
    }

    private void RefreshRScenePaletteActorPreview(RSceneActorPaletteItem? item)
    {
        if (item == null || _project == null)
        {
            _selectedRScenePaletteItem = null;
            ClearRSceneFrameList();
            return;
        }

        _selectedRScenePaletteItem = item;
        var frameIndex = GetSelectedRSceneFrameIndex();
        RefreshRSceneFrameList(item, frameIndex);
    }

    private void RefreshRSceneFrameList(RSceneActorPaletteItem item, int selectedFrameIndex)
    {
        if (_bindingRSceneFrameSelection) return;
        _bindingRSceneFrameSelection = true;
        try
        {
            var oldList = _rSceneFrameImageList;
            _rSceneFrameListView.BeginUpdate();
            _rSceneFrameListView.Items.Clear();
            _rSceneFrameListView.LargeImageList = null;
            oldList?.Dispose();

            var imageList = new ImageList
            {
                ImageSize = new Size(48, 64),
                ColorDepth = ColorDepth.Depth32Bit
            };

            for (var frameIndex = 0; frameIndex < RSceneFrameCount; frameIndex++)
            {
                Bitmap? frame = null;
                try
                {
                    frame = _project == null
                        ? null
                        : GetCachedRSceneActorFrame(item.RImageId, frameIndex, GetSelectedRSceneFacing());
                    using var thumbnail = CreateRSceneFrameThumbnail(frame, frameIndex, imageList.ImageSize);
                    AddRSceneFrameImage(imageList, thumbnail, frameIndex);
                }
                catch (Exception ex) when (ex is ArgumentException or ExternalException or InvalidOperationException)
                {
                    System.Diagnostics.Debug.WriteLine($"R 场景帧缩略图生成失败：R={item.RImageId} frame={frameIndex} {ex.Message}");
                    using var placeholder = CreateRSceneFramePlaceholder(frameIndex);
                    AddRSceneFrameImage(imageList, placeholder, frameIndex);
                }
                _rSceneFrameListView.Items.Add(new ListViewItem(GetRSceneGestureLabel(frameIndex), frameIndex) { Tag = frameIndex });
            }

            for (var movementIndex = 0; movementIndex < RSceneMovementStripFrames.Length; movementIndex++)
            {
                var stripFrameIndex = RSceneMovementStripFrames[movementIndex];
                var imageIndex = RSceneFrameCount + movementIndex;
                Bitmap? frame = null;
                try
                {
                    frame = _project == null
                        ? null
                        : GetCachedRScenePhysicalStripFrame(item.RImageId, stripFrameIndex, GetSelectedRSceneFacing());
                    using var thumbnail = CreateRSceneFrameThumbnail(frame, stripFrameIndex, imageList.ImageSize);
                    AddRSceneFrameImage(imageList, thumbnail, imageIndex);
                }
                catch (Exception ex) when (ex is ArgumentException or ExternalException or InvalidOperationException)
                {
                    System.Diagnostics.Debug.WriteLine($"R 场景移动帧缩略图生成失败：R={item.RImageId} stripFrame={stripFrameIndex} {ex.Message}");
                    using var placeholder = CreateRSceneFramePlaceholder(stripFrameIndex);
                    AddRSceneFrameImage(imageList, placeholder, imageIndex);
                }

                _rSceneFrameListView.Items.Add(new ListViewItem(GetRSceneMovementFrameLabel(stripFrameIndex), imageIndex)
                {
                    Tag = new RScenePhysicalFramePreviewTag(stripFrameIndex)
                });
            }

            _rSceneFrameImageList = imageList;
            _rSceneFrameListView.LargeImageList = imageList;
            var safeIndex = Math.Clamp(selectedFrameIndex, 0, RSceneFrameCount - 1);
            if (_rSceneFrameListView.Items.Count > 0)
            {
                _rSceneFrameListView.Items[safeIndex].Selected = true;
                _rSceneFrameListView.Items[safeIndex].Focused = true;
                _rSceneFrameListView.EnsureVisible(safeIndex);
            }
        }
        finally
        {
            _rSceneFrameListView.EndUpdate();
            _bindingRSceneFrameSelection = false;
        }
    }

    private void ClearRSceneFrameList()
    {
        _bindingRSceneFrameSelection = true;
        try
        {
            _rSceneFrameListView.Items.Clear();
            _rSceneFrameListView.LargeImageList = null;
            _rSceneFrameImageList?.Dispose();
            _rSceneFrameImageList = null;
        }
        finally
        {
            _bindingRSceneFrameSelection = false;
        }
    }

    private static readonly int[] RSceneMovementStripFrames = [1, 2];

    private string GetRSceneGestureLabel(int frameIndex)
        => GetLegacyMfcDialogDataSources().GestureLabel(frameIndex);

    private static string GetRSceneMovementFrameLabel(int stripFrameIndex)
        => "移动" + stripFrameIndex.ToString(CultureInfo.InvariantCulture);

    private static Bitmap CreateRSceneFramePlaceholder(int frameIndex)
    {
        var image = new Bitmap(48, 64);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.FromArgb(46, 48, 52));
        using var pen = new Pen(Color.FromArgb(120, Color.White));
        graphics.DrawRectangle(pen, 0, 0, image.Width - 1, image.Height - 1);
        TextRenderer.DrawText(graphics, frameIndex.ToString(CultureInfo.InvariantCulture), SystemFonts.MessageBoxFont, new Rectangle(0, 0, image.Width, image.Height), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        return image;
    }

    private static Bitmap CreateRSceneFrameThumbnail(Bitmap? frame, int frameIndex, Size imageSize)
    {
        var safeWidth = Math.Max(1, imageSize.Width);
        var safeHeight = Math.Max(1, imageSize.Height);
        var image = new Bitmap(safeWidth, safeHeight, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.FromArgb(46, 48, 52));

        if (frame == null || frame.Width <= 0 || frame.Height <= 0)
        {
            DrawRSceneFramePlaceholder(graphics, frameIndex, new Rectangle(0, 0, safeWidth, safeHeight));
            return image;
        }

        var scale = Math.Min(safeWidth / (float)frame.Width, safeHeight / (float)frame.Height);
        var width = Math.Max(1, (int)Math.Round(frame.Width * scale));
        var height = Math.Max(1, (int)Math.Round(frame.Height * scale));
        var rect = new Rectangle((safeWidth - width) / 2, safeHeight - height, width, height);
        graphics.DrawImage(frame, rect);
        return image;
    }

    private static void DrawRSceneFramePlaceholder(Graphics graphics, int frameIndex, Rectangle bounds)
    {
        using var pen = new Pen(Color.FromArgb(120, Color.White));
        graphics.DrawRectangle(pen, bounds.Left, bounds.Top, Math.Max(1, bounds.Width - 1), Math.Max(1, bounds.Height - 1));
        TextRenderer.DrawText(
            graphics,
            frameIndex.ToString(CultureInfo.InvariantCulture),
            SystemFonts.MessageBoxFont,
            bounds,
            Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private static void AddRSceneFrameImage(ImageList imageList, Bitmap image, int frameIndex)
    {
        Bitmap? stored = null;
        try
        {
            stored = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(stored))
            {
                graphics.Clear(Color.Transparent);
                graphics.DrawImage(image, new Rectangle(0, 0, stored.Width, stored.Height));
            }

            imageList.Images.Add("frame-" + frameIndex.ToString(CultureInfo.InvariantCulture), stored);
            stored = null;
        }
        finally
        {
            stored?.Dispose();
        }
    }

    private void SelectRSceneFrameFromList()
    {
        if (_bindingRSceneFrameSelection) return;
        if (_rSceneFrameListView.SelectedItems.Count == 0) return;
        if (_rSceneFrameListView.SelectedItems[0].Tag is RScenePhysicalFramePreviewTag physicalFrame)
        {
            SetStatus($"R场景制作：移动{physicalFrame.StripFrameIndex} 是 Pmapobj.e5 物理移动帧，只读预览，不写入脚本动作参数。");
            return;
        }

        if (_rSceneFrameListView.SelectedItems[0].Tag is not int frameIndex) return;
        _bindingRSceneFrameSelection = true;
        try
        {
            _rSceneStanceInput.Value = Math.Clamp(frameIndex, (int)_rSceneStanceInput.Minimum, (int)_rSceneStanceInput.Maximum);
        }
        finally
        {
            _bindingRSceneFrameSelection = false;
        }

        ApplyRSceneControlPanelToSelectedActor();
        RefreshRScenePaletteActorPreview(_rSceneActorListBox.SelectedItem as RSceneActorPaletteItem);
    }

    private sealed record RScenePhysicalFramePreviewTag(int StripFrameIndex);

    private sealed record RSceneFrameDragPayload(RSceneActorPaletteItem Actor, int FrameIndex, string Facing);

    private void BeginRSceneFrameDrag(Point location)
    {
        _rSceneFrameDragStart = null;
        _rSceneFrameDragPayload = null;
        var hit = _rSceneFrameListView.HitTest(location);
        if (hit.Item?.Tag is not int frameIndex) return;
        if (_rSceneActorListBox.SelectedItem is not RSceneActorPaletteItem item) return;

        _selectedRScenePaletteItem = item;
        _rSceneFrameDragStart = location;
        _rSceneFrameDragPayload = new RSceneFrameDragPayload(item, frameIndex, GetSelectedRSceneFacing());
    }

    private void ContinueRSceneFrameDrag(Point location, MouseButtons buttons)
    {
        if (buttons != MouseButtons.Left || _rSceneFrameDragStart == null || _rSceneFrameDragPayload == null) return;

        var start = _rSceneFrameDragStart.Value;
        var dragSize = SystemInformation.DragSize;
        var dragRect = new Rectangle(
            start.X - dragSize.Width / 2,
            start.Y - dragSize.Height / 2,
            dragSize.Width,
            dragSize.Height);
        if (dragRect.Contains(location)) return;

        var payload = _rSceneFrameDragPayload;
        ClearRSceneFrameDrag();
        _rSceneFrameListView.DoDragDrop(payload, DragDropEffects.Copy);
    }

    private void ClearRSceneFrameDrag()
    {
        _rSceneFrameDragStart = null;
        _rSceneFrameDragPayload = null;
    }

    private static void HandleRSceneCanvasDragEnter(DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(typeof(RSceneFrameDragPayload)) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void HandleRSceneCanvasDragOver(DragEventArgs e)
    {
        if (e.Data?.GetData(typeof(RSceneFrameDragPayload)) is not RSceneFrameDragPayload payload)
        {
            e.Effect = DragDropEffects.None;
            ClearRSceneCanvasDragPreview();
            return;
        }

        e.Effect = DragDropEffects.Copy;
        var point = _rSceneCanvasBox.PointToClient(new Point(e.X, e.Y));
        if (!TryRSceneCanvasPointToGrid(point, out var gridX, out var gridY))
        {
            ClearRSceneCanvasDragPreview();
            return;
        }

        var nextGrid = new Point(Math.Max(0, gridX), Math.Max(0, gridY));
        if (Equals(_rSceneDragPreviewPayload, payload) && _rSceneDragPreviewGrid == nextGrid)
        {
            return;
        }

        _rSceneDragPreviewPayload = payload;
        _rSceneDragPreviewGrid = nextGrid;
        RenderRSceneCanvas();
    }

    private void ClearRSceneCanvasDragPreview(bool render = true)
    {
        if (_rSceneDragPreviewPayload == null && !_rSceneDragPreviewGrid.HasValue) return;
        _rSceneDragPreviewPayload = null;
        _rSceneDragPreviewGrid = null;
        if (render)
        {
            RenderRSceneCanvas();
        }
    }

    private void HandleRSceneCanvasDragDrop(DragEventArgs e)
    {
        if (_currentRSceneScenario == null) return;
        if (e.Data?.GetData(typeof(RSceneFrameDragPayload)) is not RSceneFrameDragPayload payload) return;
        var point = _rSceneCanvasBox.PointToClient(new Point(e.X, e.Y));
        if (!TryRSceneCanvasPointToGrid(point, out var gridX, out var gridY))
        {
            ClearRSceneCanvasDragPreview();
            SetStatus("R场景制作：拖放位置不在画布内。");
            return;
        }

        ClearRSceneCanvasDragPreview(render: false);
        if (!TryInsertRSceneActorShowCommand(payload, gridX, gridY, out var insertedCommand, out var insertMessage))
        {
            RenderRSceneCanvas();
            MessageBox.Show(this, insertMessage, "无法写入 R 剧本", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SetStatus("R场景制作：" + insertMessage);
            return;
        }

        SetStatus($"R场景制作：已在当前指令后插入 0x30 {payload.Actor.DisplayText} 动作帧={payload.FrameIndex} -> ({gridX},{gridY})，需完整保存R剧本");
    }

    private RScenePlacedActor BuildRScenePlacedActor(RSceneActorPaletteItem item, int gridX, int gridY, string source)
    {
        var anchor = RSceneCoordinateToPixel(gridX, gridY);
        return new RScenePlacedActor
        {
            TargetKey = $"RSceneDraft#{_currentRSceneScenario?.FileName ?? "R"}#{item.PersonId}#{gridX},{gridY}#{_rScenePlacedActors.Count + 1}",
            PersonId = item.PersonId,
            Name = item.Name,
            JobId = item.JobId,
            JobName = item.JobName,
            RImageId = item.RImageId,
            SImageId = item.SImageId,
            Facing = GetSelectedRSceneFacing(),
            FrameIndex = GetSelectedRSceneFrameIndex(),
            GridX = gridX,
            GridY = gridY,
            PixelX = (int)Math.Round(anchor.X),
            PixelY = (int)Math.Round(anchor.Y),
            Source = source,
            ActorNote = $"R 场景预览摆放：{item.PersonId} {item.Name} 坐标=({gridX},{gridY})，方向={GetSelectedRSceneFacing()}，动作帧={GetSelectedRSceneFrameIndex()}。"
        };
    }

    private bool TryInsertRSceneActorShowCommand(
        RSceneFrameDragPayload payload,
        int gridX,
        int gridY,
        out LegacyScenarioCommandNode command,
        out string message)
    {
        command = null!;
        message = string.Empty;
        if (_currentRSceneLegacyScriptDocument == null)
        {
            message = "当前 R 剧情没有进入旧版完整树模式，拖放不会写入脚本。";
            return false;
        }

        if (!TryGetSelectedRSceneLegacyCommand(out var selected))
        {
            message = "请先在左侧 R 剧本树中选择一条普通命令，拖放角色会插入到该命令之后。";
            return false;
        }

        if (!CanInsertNearLegacyScriptCommand(selected, out message))
        {
            return false;
        }

        if (!TryFindLegacyCommandList(_currentRSceneLegacyScriptDocument, selected, out var list, out var selectedIndex))
        {
            message = "没有在当前 R 剧本结构中定位到插入点。";
            return false;
        }

        PushLegacyScenarioUndoSnapshot(LegacyScriptEditorScope.RScene, _currentRSceneLegacyScriptDocument);
        var jumpTargets = CaptureLegacyJumpTargets(_currentRSceneLegacyScriptDocument);
        command = CreateRSceneActorShowCommand(selected.SceneIndex, selected.SectionIndex, payload, gridX, gridY);
        list.Insert(GetLegacyScriptNearInsertIndex(list, selectedIndex, beforeSelected: false), command);
        ReindexLegacyScriptDocument(_currentRSceneLegacyScriptDocument);
        RestoreLegacyJumpTargets(_currentRSceneLegacyScriptDocument, jumpTargets);
        if (!TryRefreshLegacyScriptSectionInPlace(
                LegacyScriptEditorScope.RScene,
                FindLegacyScriptSectionForCommand(_currentRSceneLegacyScriptDocument, selected)!,
                command,
                CaptureLegacyScriptViewport(LegacyScriptEditorScope.RScene)))
        {
            RefreshRSceneLegacyScriptView(command);
        }
        _saveRSceneScriptStructureButton.Enabled = true;
        message = $"已在当前命令后插入 0x30 武将出现：人物={payload.Actor.PersonId} 坐标=({gridX},{gridY}) 动作帧={payload.FrameIndex}。";
        return true;
    }

    private bool TryGetSelectedRSceneLegacyCommand(out LegacyScenarioCommandNode command)
    {
        command = null!;
        if (_currentRSceneLegacyScriptDocument == null) return false;
        if (_rSceneScriptTree.SelectedNode?.Tag is LegacyScenarioItemData { Command: { } itemCommand })
        {
            command = itemCommand;
            return true;
        }

        if (_rSceneScriptTree.SelectedNode?.Tag is not ScenarioStructureRow { NodeType: "Command候选" } row)
        {
            return false;
        }

        return _rSceneScriptCommandByKey.TryGetValue(BuildLegacyCommandKey(row), out command!);
    }

    private LegacyScenarioCommandNode CreateRSceneActorShowCommand(
        int sceneIndex,
        int sectionIndex,
        RSceneFrameDragPayload payload,
        int gridX,
        int gridY)
    {
        var command = new LegacyScenarioCommandNode
        {
            SceneIndex = sceneIndex,
            SectionIndex = sectionIndex,
            CommandId = 0x30,
            CommandName = ResolveLegacyScriptCommandName(0x30),
            FileOffset = 0,
            ConsumedBytes = 0
        };

        foreach (var parameter in CreateDefaultLegacyScriptParameters(0x30))
        {
            command.Parameters.Add(parameter);
        }

        SetRSceneCommandParameterValue(command, 0, payload.Actor.PersonId);
        SetRSceneCommandParameterValue(command, 1, gridX);
        SetRSceneCommandParameterValue(command, 2, gridY);
        SetRSceneCommandParameterValue(command, 3, FacingToDirectionValue(payload.Facing));
        SetRSceneCommandParameterValue(command, 4, payload.FrameIndex);
        return command;
    }

    private static void SetRSceneCommandParameterValue(LegacyScenarioCommandNode command, int index, int value)
    {
        if (index < 0 || index >= command.Parameters.Count) return;
        command.Parameters[index].IntValue = value;
    }

    private static int FacingToDirectionValue(string facing)
        => NormalizeRSceneFacing(facing) switch
        {
            "上" => 0,
            "右" => 1,
            "下" => 2,
            "左" => 3,
            _ => 2
        };

    private void BeginRSceneCanvasActorInteraction(MouseEventArgs e)
    {
        if (e.Button is not (MouseButtons.Left or MouseButtons.Right)) return;

        if (!TryHitRScenePlacedActor(e.Location, out var actor))
        {
            if (e.Button == MouseButtons.Left)
            {
                if (_selectedRScenePlacedActor != null || _editingRScenePlacedActor != null)
                {
                    _selectedRScenePlacedActor = null;
                    _editingRScenePlacedActor = null;
                    RenderRSceneCanvas();
                }
            }

            if (TryRSceneCanvasPointToGrid(e.Location, out var gridX, out var gridY))
            {
                SetStatus($"R场景制作：({gridX},{gridY}) 没有已摆放角色。");
            }
            else
            {
                SetStatus("R场景制作：点击位置不在画布内。");
            }
            return;
        }

        var enterEdit = e.Button == MouseButtons.Right;
        SelectRScenePlacedActor(actor, enterEdit, render: !enterEdit);

        if (ReferenceEquals(_editingRScenePlacedActor, actor))
        {
            _draggingRScenePlacedActor = actor;
            _rScenePlacedActorDragStart = e.Location;
            _rScenePlacedActorOriginalGrid = new Point(actor.GridX, actor.GridY);
            _rScenePlacedActorDragMoved = false;
            _rSceneMovePreviewActor = actor;
            _rSceneMovePreviewGrid = new Point(actor.GridX, actor.GridY);
            _rSceneCanvasBox.Capture = true;
            _rSceneCanvasBox.Cursor = Cursors.SizeAll;
            RenderRSceneCanvas();
        }
    }

    private void ContinueRSceneCanvasActorInteraction(MouseEventArgs e)
    {
        if (_draggingRScenePlacedActor == null || _rScenePlacedActorDragStart == null) return;
        if ((e.Button & (MouseButtons.Left | MouseButtons.Right)) == 0) return;
        if (!TryRSceneCanvasPointToGrid(e.Location, out var gridX, out var gridY))
        {
            if (_rSceneMovePreviewGrid.HasValue)
            {
                _rSceneMovePreviewGrid = null;
                RenderRSceneCanvas();
            }
            return;
        }

        gridX = Math.Max(0, gridX);
        gridY = Math.Max(0, gridY);
        if (_rSceneMovePreviewGrid == new Point(gridX, gridY) &&
            _draggingRScenePlacedActor.GridX == gridX &&
            _draggingRScenePlacedActor.GridY == gridY)
        {
            return;
        }

        _draggingRScenePlacedActor.GridX = gridX;
        _draggingRScenePlacedActor.GridY = gridY;
        var anchor = RSceneCoordinateToPixel(gridX, gridY);
        _draggingRScenePlacedActor.PixelX = (int)Math.Round(anchor.X);
        _draggingRScenePlacedActor.PixelY = (int)Math.Round(anchor.Y);
        _rSceneMovePreviewActor = _draggingRScenePlacedActor;
        _rSceneMovePreviewGrid = new Point(gridX, gridY);
        _rScenePlacedActorDragMoved = true;
        RenderRSceneCanvas();
        SetStatus($"R场景制作：拖动 {_draggingRScenePlacedActor.Name} -> ({gridX},{gridY})");
    }

    private void EndRSceneCanvasActorInteraction()
    {
        if (_draggingRScenePlacedActor == null)
        {
            _rSceneCanvasBox.Cursor = Cursors.Default;
            return;
        }

        var actor = _draggingRScenePlacedActor;
        var oldGrid = _rScenePlacedActorOriginalGrid;
        var moved = _rScenePlacedActorDragMoved && (actor.GridX != oldGrid.X || actor.GridY != oldGrid.Y);
        _draggingRScenePlacedActor = null;
        _rScenePlacedActorDragStart = null;
        _rScenePlacedActorDragMoved = false;
        ClearRSceneMovePreview();
        _rSceneCanvasBox.Capture = false;
        _rSceneCanvasBox.Cursor = Cursors.Default;

        if (!moved)
        {
            RenderRSceneCanvas();
            return;
        }

        actor.ActorNote = BattlefieldUnitReviewService.AppendReviewLine(
            actor.ActorNote,
            $"地图拖拽：({oldGrid.X},{oldGrid.Y}) -> ({actor.GridX},{actor.GridY})。");
        _saveRSceneDraftButton.Enabled = true;

        var scriptSync = TrySyncRSceneActorPositionToScriptCommand(actor, out var syncMessage);
        RenderRSceneCanvas();
        SetStatus(scriptSync
            ? $"R场景制作：已同步 {actor.Name} 到 R 剧本命令，需完整保存R剧本"
            : $"R场景制作：已移动 {actor.Name}，{syncMessage.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal)}");
    }

    private void HandleRSceneCanvasActorDoubleClick(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (!TryHitRScenePlacedActor(e.Location, out var actor)) return;

        SelectRScenePlacedActor(actor, enterEdit: false, render: false);
        if (!SelectRScenePlacedActorScriptCommand(actor, out var message))
        {
            SetStatus("R场景制作：" + message);
        }
    }

    private void SelectRScenePlacedActor(RScenePlacedActor actor, bool enterEdit, bool render = true)
    {
        _selectedRScenePlacedActor = actor;
        if (enterEdit)
        {
            _editingRScenePlacedActor = actor;
        }
        else if (!ReferenceEquals(_editingRScenePlacedActor, actor))
        {
            _editingRScenePlacedActor = null;
        }
        SyncRSceneControlPanelFromPlacedActor(actor);
        if (render)
        {
            RenderRSceneCanvas();
        }
        SetStatus(enterEdit
            ? $"R场景制作：{actor.Name} 已进入编辑状态"
            : $"R场景制作：已选中 {actor.Name} ({actor.GridX},{actor.GridY})");
    }

    private bool TryHitRScenePlacedActor(Point location, out RScenePlacedActor actor)
    {
        actor = null!;
        if (!TryRSceneCanvasPointToImagePoint(location, out var imagePoint)) return false;

        var drawOrder = _rScenePlacedActors
            .OrderBy(item => item.GridY)
            .ThenBy(item => item.GridX)
            .ToList();
        for (var index = drawOrder.Count - 1; index >= 0; index--)
        {
            var candidate = drawOrder[index];
            if (!GetRScenePlacedActorHitBounds(candidate).Contains(Point.Round(imagePoint))) continue;
            actor = candidate;
            return true;
        }

        return false;
    }

    private Rectangle GetRScenePlacedActorHitBounds(RScenePlacedActor actor)
    {
        var anchor = RSceneCoordinateToPixel(actor.GridX, actor.GridY);
        var size = GetRScenePlacedActorRenderSize(actor);
        var left = (int)Math.Round(anchor.X - size.Width / 2f);
        var top = (int)Math.Round(anchor.Y - size.Height + RSceneTileHeight / 2f);
        var spriteRect = new Rectangle(left, top, size.Width, size.Height);
        var labelRect = new Rectangle(spriteRect.Left, spriteRect.Top + spriteRect.Height, Math.Max(spriteRect.Width, 72), 18);
        return Rectangle.Union(spriteRect, labelRect);
    }

    private Size GetRScenePlacedActorRenderSize(RScenePlacedActor actor)
    {
        var frame = GetCachedRSceneActorFrame(actor.RImageId, actor.FrameIndex, actor.Facing);
        if (frame != null) return frame.Size;

        return new Size(48, 64);
    }

    private bool TryRSceneCanvasPointToGrid(Point point, out int gridX, out int gridY)
    {
        gridX = gridY = 0;
        if (!TryRSceneCanvasPointToImagePoint(point, out var imagePoint)) return false;
        var coordinate = RScenePixelToCoordinate(imagePoint.X, imagePoint.Y);
        gridX = coordinate.X;
        gridY = coordinate.Y;
        return true;
    }

    private bool TryRSceneCanvasPointToImagePoint(Point point, out PointF imagePoint)
    {
        imagePoint = PointF.Empty;
        var image = _rSceneCanvasBox.Image;
        if (image == null || _rSceneCanvasBox.Width <= 0 || _rSceneCanvasBox.Height <= 0) return false;
        if (point.X < 0 || point.Y < 0 || point.X >= _rSceneCanvasBox.Width || point.Y >= _rSceneCanvasBox.Height) return false;

        var scaleX = image.Width / Math.Max(1f, _rSceneCanvasBox.Width);
        var scaleY = image.Height / Math.Max(1f, _rSceneCanvasBox.Height);
        var imageX = point.X * scaleX;
        var imageY = point.Y * scaleY;
        if (imageX < 0 || imageY < 0 || imageX >= image.Width || imageY >= image.Height) return false;

        imagePoint = new PointF(imageX, imageY);
        return true;
    }

    private bool TrySyncRSceneActorPositionToScriptCommand(RScenePlacedActor actor, out string message)
    {
        message = "该角色不是从 R 剧本命令预加载，已作为项目侧草稿移动；如需新增出场指令，请在左侧 R 剧本树中添加/编辑命令。";
        if (!TryFindRSceneScriptCommandForActor(actor, out var command, out var row))
        {
            return false;
        }

        if (command.CommandId != 0x30)
        {
            message = $"该角色绑定的是 {command.CommandIdHex} {command.CommandName}，当前拖拽只同步 0x30 武将出现的 X/Y 坐标；本次移动已保存在场景草稿中。";
            return false;
        }

        var beforeEdit = CaptureLegacyScenarioHistorySnapshot(LegacyScriptEditorScope.RScene, _currentRSceneLegacyScriptDocument!);
        if (!TrySetRSceneCommandCoordinateParameter(command, parameterIndex: 1, actor.GridX, out var error) ||
            !TrySetRSceneCommandCoordinateParameter(command, parameterIndex: 2, actor.GridY, out error))
        {
            message = error;
            return false;
        }

        PushLegacyScenarioUndoSnapshot(LegacyScriptEditorScope.RScene, beforeEdit);
        actor.ActorNote = BattlefieldUnitReviewService.AppendReviewLine(
            actor.ActorNote,
            $"已同步到 R 剧本内存：{command.CommandIdHex} {command.CommandName} 槽1/2=({actor.GridX},{actor.GridY})。");
        _saveRSceneScriptStructureButton.Enabled = true;
        if (!RefreshLegacyEditorCommandInPlace(LegacyScriptEditorScope.RScene, command))
        {
            RefreshRSceneLegacyScriptView(command);
        }
        message = $"已同步到 R 剧本命令：{command.CommandIdHex} {command.CommandName} / Scene {command.SceneIndex} / Section {command.SectionIndex} / Command {command.CommandIndex}。\r\n需要点击“完整保存R剧本”后才会写入 {(_currentRSceneScenario?.FileName ?? "R_XX.eex")}。";
        return true;
    }

    private static bool TrySetRSceneCommandCoordinateParameter(
        LegacyScenarioCommandNode command,
        int parameterIndex,
        int value,
        out string error)
    {
        error = string.Empty;
        if (value is < 0 or > 4096)
        {
            error = $"坐标值超出当前 R 场景安全范围：{value}。";
            return false;
        }

        if (parameterIndex < 0 || parameterIndex >= command.Parameters.Count)
        {
            error = $"{command.CommandIdHex} {command.CommandName} 缺少参数槽 {parameterIndex}，未写回。";
            return false;
        }

        var parameter = command.Parameters[parameterIndex];
        if (parameter.Kind != LegacyScenarioParameterKind.Dword32)
        {
            error = $"{command.CommandIdHex} {command.CommandName} 参数槽 {parameterIndex} 不是旧源码 0x04/32位坐标槽，未写回。";
            return false;
        }

        parameter.IntValue = value;
        return true;
    }

    private bool TrySyncRSceneActorPoseToScriptCommand(RScenePlacedActor actor, out string message)
    {
        message = string.Empty;
        if (!TryFindRSceneScriptCommandForActor(actor, out var command, out _))
        {
            message = "该角色没有绑定到 R 剧本命令。";
            return false;
        }

        var synced = false;
        switch (command.CommandId)
        {
            case 0x30:
                synced |= TrySetRSceneCommandParameter(command, 3, FacingToDirectionValue(actor.Facing), out _);
                synced |= TrySetRSceneCommandParameter(command, 4, actor.FrameIndex, out _);
                break;
            case 0x33:
                synced |= TrySetRSceneCommandParameter(command, 1, actor.FrameIndex, out _);
                synced |= TrySetRSceneCommandParameter(command, 2, FacingToDirectionValue(actor.Facing), out _);
                break;
            case 0x34:
                synced |= TrySetRSceneCommandParameter(command, 1, actor.FrameIndex, out _);
                break;
        }

        if (!synced)
        {
            message = $"该角色最近绑定的是 {command.CommandIdHex} {command.CommandName}，没有可直接同步的方向/动作帧槽。";
            return false;
        }

        _saveRSceneScriptStructureButton.Enabled = true;
        if (FindRSceneScriptRowForCommand(command) is { } row)
        {
            _rSceneScriptDetailBox.Text = BuildLegacyScriptRowDetail(row, command);
        }
        message = $"已同步方向/动作帧到 {command.CommandIdHex} {command.CommandName}。";
        return true;
    }

    private static bool TrySetRSceneCommandParameter(
        LegacyScenarioCommandNode command,
        int parameterIndex,
        int value,
        out string error)
    {
        error = string.Empty;
        if (parameterIndex < 0 || parameterIndex >= command.Parameters.Count)
        {
            error = $"{command.CommandIdHex} {command.CommandName} 缺少参数槽 {parameterIndex}，未写回。";
            return false;
        }

        var parameter = command.Parameters[parameterIndex];
        if (parameter.Kind is not (LegacyScenarioParameterKind.Word16 or LegacyScenarioParameterKind.Dword32))
        {
            error = $"{command.CommandIdHex} {command.CommandName} 参数槽 {parameterIndex} 不是数值参数，未写回。";
            return false;
        }

        parameter.IntValue = value;
        return true;
    }

    private bool SelectRScenePlacedActorScriptCommand(RScenePlacedActor actor, out string message)
    {
        message = "该角色没有绑定到 R 剧本命令。";
        if (!TryFindRSceneScriptCommandForActor(actor, out var command, out var row) || row == null)
        {
            return false;
        }

        SelectRSceneScriptTreeNode(row);
        message = $"已定位左侧指令：{command.CommandIdHex} {command.CommandName} / Scene {command.SceneIndex} / Section {command.SectionIndex} / Command {command.CommandIndex}。";
        SetStatus($"R场景制作：已定位 {actor.Name} 对应指令 {command.CommandIdHex} {command.CommandName}");
        return true;
    }

    private bool TryFindRSceneScriptCommandForActor(
        RScenePlacedActor actor,
        out LegacyScenarioCommandNode command,
        out ScenarioStructureRow? row)
    {
        command = null!;
        row = null;
        if (_currentRSceneLegacyScriptDocument == null || _currentRSceneScriptStructure == null) return false;
        var targetKey = string.IsNullOrWhiteSpace(actor.LastActionTargetKey) ? actor.TargetKey : actor.LastActionTargetKey;
        if (!TryParseRSceneTargetKey(targetKey, out var scene, out var section, out var commandIndex, out var offsetHex, out var commandIdHex))
        {
            return false;
        }

        row = _currentRSceneScriptStructure.Rows.FirstOrDefault(candidate =>
            candidate.NodeType == "Command候选" &&
            candidate.SceneIndex == scene &&
            candidate.SectionIndex == section &&
            candidate.CommandIndex == commandIndex &&
            (string.IsNullOrWhiteSpace(offsetHex) || candidate.OffsetHex.Equals(offsetHex, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(commandIdHex) || candidate.CommandIdHex.Equals(commandIdHex, StringComparison.OrdinalIgnoreCase)));
        if (row != null && _rSceneScriptCommandByKey.TryGetValue(BuildLegacyCommandKey(row), out command!))
        {
            return true;
        }

        command = _currentRSceneLegacyScriptDocument.EnumerateCommands().FirstOrDefault(candidate =>
            candidate.SceneIndex == scene &&
            candidate.SectionIndex == section &&
            candidate.CommandIndex == commandIndex &&
            (string.IsNullOrWhiteSpace(offsetHex) || HexDisplayFormatter.EqualsText(HexDisplayFormatter.FormatOffset(candidate.FileOffset), offsetHex)) &&
            (string.IsNullOrWhiteSpace(commandIdHex) || candidate.CommandIdHex.Equals(commandIdHex, StringComparison.OrdinalIgnoreCase)))!;
        return command != null;
    }

    private void ApplyRSceneControlPanelToSelectedActor()
    {
        if (_bindingRSceneControlPanel) return;
        if (_selectedRScenePlacedActor == null) return;

        _selectedRScenePlacedActor.Facing = GetSelectedRSceneFacing();
        _selectedRScenePlacedActor.FrameIndex = GetSelectedRSceneFrameIndex();
        _selectedRScenePlacedActor.ActorNote = BattlefieldUnitReviewService.AppendReviewLine(
            _selectedRScenePlacedActor.ActorNote,
            $"控制面板调整：方向={_selectedRScenePlacedActor.Facing}，动作帧={_selectedRScenePlacedActor.FrameIndex}。");
        _saveRSceneDraftButton.Enabled = true;
        TrySyncRSceneActorPoseToScriptCommand(_selectedRScenePlacedActor, out _);
        RenderRSceneCanvas();
    }

    private void SyncRSceneControlPanelFromPlacedActor(RScenePlacedActor actor)
    {
        _bindingRSceneControlPanel = true;
        try
        {
            SelectComboText(_rSceneFacingCombo, NormalizeRSceneFacing(actor.Facing));
            _rSceneStanceInput.Value = Math.Clamp(actor.FrameIndex, (int)_rSceneStanceInput.Minimum, (int)_rSceneStanceInput.Maximum);
            SelectRScenePaletteActorForPlacedActor(actor);
        }
        finally
        {
            _bindingRSceneControlPanel = false;
        }

        RefreshRScenePaletteActorPreview(_rSceneActorListBox.SelectedItem as RSceneActorPaletteItem);
    }

    private void SelectRScenePaletteActorForPlacedActor(RScenePlacedActor actor)
    {
        var selected = SelectRSceneActorListItemByPersonId(actor.PersonId);
        if (!selected && _rSceneActorPaletteItems.Any(item => item.PersonId == actor.PersonId))
        {
            _rSceneActorFilterBox.Clear();
            BindRSceneActorPalette(_rSceneActorPaletteItems);
            SelectRSceneActorListItemByPersonId(actor.PersonId);
        }
    }

    private bool SelectRSceneActorListItemByPersonId(int personId)
    {
        for (var i = 0; i < _rSceneActorListBox.Items.Count; i++)
        {
            if (_rSceneActorListBox.Items[i] is not RSceneActorPaletteItem item || item.PersonId != personId) continue;
            _rSceneActorListBox.SelectedIndex = i;
            _selectedRScenePaletteItem = item;
            return true;
        }

        return false;
    }

    private void ClearRSceneMovePreview()
    {
        _rSceneMovePreviewActor = null;
        _rSceneMovePreviewGrid = null;
    }

    private void RenderRSceneCanvasIfNotSuppressed()
    {
        if (_suppressRSceneCanvasRender) return;
        RenderRSceneCanvas();
    }

    private IDisposable SuppressRSceneCanvasRender()
    {
        var previous = _suppressRSceneCanvasRender;
        _suppressRSceneCanvasRender = true;
        return new RSceneCanvasRenderSuppression(this, previous);
    }

    private sealed class RSceneCanvasRenderSuppression(MainForm owner, bool previous) : IDisposable
    {
        private MainForm? _owner = owner;

        public void Dispose()
        {
            if (_owner == null) return;
            _owner._suppressRSceneCanvasRender = previous;
            _owner = null;
        }
    }

    private void ClearRSceneImageCache()
    {
        foreach (var image in _rSceneImageCache.Values)
        {
            image.Dispose();
        }

        _rSceneImageCache.Clear();
    }

    private void CompactRSceneImageCacheIfNeeded()
    {
        if (_rSceneImageCache.Count < RSceneImageCacheLimit) return;
        ClearRSceneImageCache();
    }

    private Bitmap? GetCachedRSceneImage(string key, Func<Bitmap?> factory)
    {
        if (_rSceneImageCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var image = factory();
        if (image == null) return null;

        CompactRSceneImageCacheIfNeeded();
        _rSceneImageCache[key] = image;
        return image;
    }

    private Bitmap? GetCachedRSceneActorFrame(int rImageId, int frameIndex, string facing)
    {
        if (_project == null) return null;
        var key = string.Create(CultureInfo.InvariantCulture, $"actor:{rImageId}:{frameIndex}:{NormalizeRSceneFacing(facing)}");
        return GetCachedRSceneImage(key, () =>
        {
            try
            {
                return _imageAssignmentPreviewService.TryRenderRSceneFrameByIndex(_project, rImageId, frameIndex, facing, out _);
            }
            catch
            {
                return null;
            }
        });
    }

    private Bitmap? GetCachedRScenePhysicalStripFrame(int rImageId, int stripFrameIndex, string facing)
    {
        if (_project == null) return null;
        var key = string.Create(CultureInfo.InvariantCulture, $"actor-strip:{rImageId}:{stripFrameIndex}:{NormalizeRSceneFacing(facing)}");
        return GetCachedRSceneImage(key, () =>
        {
            try
            {
                return _imageAssignmentPreviewService.TryRenderRScenePhysicalStripFrame(_project, rImageId, stripFrameIndex, facing, out _);
            }
            catch
            {
                return null;
            }
        });
    }

    private Bitmap? GetCachedRSceneFace(int dataFaceId)
    {
        if (_project == null || dataFaceId < 0) return null;
        var key = string.Create(CultureInfo.InvariantCulture, $"face:{dataFaceId}");
        return GetCachedRSceneImage(key, () =>
        {
            try
            {
                return _imageAssignmentPreviewService.TryRenderFaceImage(_project, dataFaceId);
            }
            catch
            {
                return null;
            }
        });
    }

    private Bitmap? GetCachedRSceneBackground(RSceneBackgroundComboItem item)
    {
        if (_project == null) return null;
        var entry = item.Entry;
        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"background:{entry.Path}:{entry.ImageNumber}:{entry.DataOffset}:{entry.StoredLength}:{entry.DecodedLength}");
        return GetCachedRSceneImage(key, () =>
        {
            try
            {
                return _imageResourceCatalogService.RenderEntryPreview(
                    _project,
                    entry,
                    canvasWidth: RSceneCanvasWidth,
                    canvasHeight: RSceneCanvasHeight);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("R scene background preview failed: " + ex.Message);
                return null;
            }
        });
    }

    private void RenderRSceneCanvas()
    {
        var old = _rSceneCanvasBox.Image;
        _rSceneCanvasBox.Image = null;
        old?.Dispose();

        var image = RenderRSceneBackgroundImage();
        using (var graphics = Graphics.FromImage(image))
        {
            if (_rSceneShowGridCheckBox.Checked)
            {
                DrawRSceneGrid(graphics, image.Size);
            }

            foreach (var actor in _rScenePlacedActors.OrderBy(item => item.GridY).ThenBy(item => item.GridX))
            {
                DrawRScenePlacedActor(graphics, actor);
            }
            foreach (var mapFace in _rSceneMapFaces.OrderBy(item => item.Y).ThenBy(item => item.X))
            {
                DrawRSceneMapFace(graphics, mapFace);
            }
            DrawRSceneMovePreview(graphics);
            DrawRSceneDragPreview(graphics);
            DrawRSceneDialoguePreview(graphics);
        }

        _rSceneCanvasBox.Image = image;
        ApplyRSceneCanvasZoom();
        var previewText = _rSceneDragPreviewPayload != null && _rSceneDragPreviewGrid.HasValue
            ? $"；拖放预览 {_rSceneDragPreviewPayload.Actor.Name} 动作帧={_rSceneDragPreviewPayload.FrameIndex} ({_rSceneDragPreviewGrid.Value.X},{_rSceneDragPreviewGrid.Value.Y})"
            : _rSceneMovePreviewActor != null && _rSceneMovePreviewGrid.HasValue
                ? $"；移动预览 {_rSceneMovePreviewActor.Name} ({_rSceneMovePreviewGrid.Value.X},{_rSceneMovePreviewGrid.Value.Y})"
            : string.Empty;
        var dialogueText = _rSceneDialoguePreviewCheckBox.Checked && !string.IsNullOrWhiteSpace(_currentRSceneDialoguePreviewMessage)
            ? $"；对白：{_currentRSceneDialoguePreviewMessage}"
            : string.Empty;
        _rSceneCanvasHintLabel.Text = $"背景：{GetSelectedRSceneBackgroundText()}；菱形坐标 16x8；角色 {_rScenePlacedActors.Count} 个；地图头像 {_rSceneMapFaces.Count} 个{previewText}{dialogueText}；右键编辑，双击定位指令。";
    }

    private void DrawRSceneDialoguePreview(Graphics graphics)
    {
        if (_project == null || !_rSceneDialoguePreviewCheckBox.Checked || _currentRSceneDialoguePreviewCommand == null) return;
        try
        {
            var result = _rSceneDialoguePreviewService.DrawPreview(
                graphics,
                _project,
                _currentRSceneDialoguePreviewCommand,
                BuildRSceneDialoguePreviewPeople(),
                ResolveRScenePersonReference);
            _currentRSceneDialoguePreviewMessage = result.Applied ? result.Message : string.Empty;
        }
        catch (Exception ex)
        {
            _currentRSceneDialoguePreviewMessage = "对白预览失败：" + ex.Message;
            System.Diagnostics.Debug.WriteLine("R 场景对白预览失败：" + ex.Message);
        }
    }

    private IReadOnlyDictionary<int, RSceneDialoguePreviewPerson> BuildRSceneDialoguePreviewPeople()
        => _rSceneActorPaletteItems
            .GroupBy(item => item.PersonId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var item = group.First();
                    return new RSceneDialoguePreviewPerson(item.Name, item.FaceId);
                });

    private void HandleRSceneCanvasMouseWheel(MouseEventArgs e)
    {
        if (_rSceneCanvasBox.Image == null || e.Delta == 0) return;

        var oldZoom = Math.Max(0.01, _rSceneCanvasZoomPercent / 100.0);
        var panelPoint = _rSceneCanvasScrollPanel.PointToClient(Control.MousePosition);
        var imagePointX = (panelPoint.X - _rSceneCanvasBox.Left) / oldZoom;
        var imagePointY = (panelPoint.Y - _rSceneCanvasBox.Top) / oldZoom;
        var step = ModifierKeys.HasFlag(Keys.Control) ? 25 : 10;
        var nextZoom = _rSceneCanvasZoomPercent + (e.Delta > 0 ? step : -step);
        _rSceneCanvasZoomPercent = Math.Clamp(nextZoom, 25, 800);
        ApplyRSceneCanvasZoom();

        var newZoom = _rSceneCanvasZoomPercent / 100.0;
        var scrollX = Math.Max(0, (int)Math.Round(imagePointX * newZoom - panelPoint.X));
        var scrollY = Math.Max(0, (int)Math.Round(imagePointY * newZoom - panelPoint.Y));
        _rSceneCanvasScrollPanel.AutoScrollPosition = new Point(scrollX, scrollY);
    }

    private void ResetRSceneCanvasZoom()
    {
        _rSceneCanvasZoomPercent = 100;
        ApplyRSceneCanvasZoom();
        _rSceneCanvasScrollPanel.AutoScrollPosition = Point.Empty;
    }

    private void ApplyRSceneCanvasZoom()
    {
        var image = _rSceneCanvasBox.Image;
        if (image == null)
        {
            _rSceneZoomLabel.Text = "缩放 100%";
            return;
        }

        var zoom = Math.Clamp(_rSceneCanvasZoomPercent, 25, 800) / 100.0;
        _rSceneCanvasZoomPercent = (int)Math.Round(zoom * 100);
        _rSceneCanvasBox.Size = new Size(
            Math.Max(1, (int)Math.Round(image.Width * zoom)),
            Math.Max(1, (int)Math.Round(image.Height * zoom)));
        _rSceneZoomLabel.Text = $"缩放 {_rSceneCanvasZoomPercent}%";
    }

    private Bitmap RenderRSceneBackgroundImage()
    {
        if (_project != null && _rSceneBackgroundCombo.SelectedItem is RSceneBackgroundComboItem item)
        {
            var cachedPreview = GetCachedRSceneBackground(item);
            if (cachedPreview != null) return new Bitmap(cachedPreview);

            try
            {
                var preview = _imageResourceCatalogService.RenderEntryPreview(_project, item.Entry, canvasWidth: RSceneCanvasWidth, canvasHeight: RSceneCanvasHeight);
                if (preview != null) return preview;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("R 场景背景预览失败：" + ex.Message);
            }
        }

        var fallback = new Bitmap(RSceneCanvasWidth, RSceneCanvasHeight);
        using var graphics = Graphics.FromImage(fallback);
        graphics.Clear(Color.FromArgb(28, 30, 34));
        using var brush = new SolidBrush(Color.FromArgb(210, 220, 220, 220));
        graphics.DrawString("未选择或无法读取 Mmap.e5 背景", Font, brush, 24, 24);
        return fallback;
    }

    private void DrawRScenePlacedActor(Graphics graphics, RScenePlacedActor actor)
    {
        var anchor = RSceneCoordinateToPixel(actor.GridX, actor.GridY);
        var selected = ReferenceEquals(actor, _selectedRScenePlacedActor);
        var editing = ReferenceEquals(actor, _editingRScenePlacedActor);
        var frame = GetCachedRSceneActorFrame(actor.RImageId, actor.FrameIndex, actor.Facing);

        var width = frame?.Width ?? 48;
        var height = frame?.Height ?? 64;
        var left = (int)Math.Round(anchor.X - width / 2f);
        var top = (int)Math.Round(anchor.Y - height + RSceneTileHeight / 2f);
        actor.PixelX = left;
        actor.PixelY = top;
        var rect = new Rectangle(left, top, width, height);
        using var backBrush = new SolidBrush(Color.FromArgb(editing ? 105 : selected ? 90 : 55, Color.Black));
        using var borderPen = new Pen(editing ? Color.OrangeRed : selected ? Color.DeepSkyBlue : Color.Gold, editing || selected ? 3 : 2);
        graphics.FillRectangle(backBrush, rect);
        if (frame != null)
        {
            graphics.DrawImage(frame, rect);
        }
        else
        {
            TextRenderer.DrawText(graphics, actor.Name, Font, rect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }
        graphics.DrawRectangle(borderPen, rect);

        var labelRect = new Rectangle(rect.Left, rect.Top + rect.Height, Math.Max(rect.Width, 72), 18);
        using var labelBack = new SolidBrush(editing ? Color.FromArgb(210, 82, 31, 18) : Color.FromArgb(185, Color.Black));
        graphics.FillRectangle(labelBack, labelRect);
        TextRenderer.DrawText(graphics, $"{actor.PersonId} {actor.Name}", Font, labelRect, Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void DrawRSceneMapFace(Graphics graphics, RSceneMapFaceState mapFace)
    {
        var paletteItem = _rSceneActorPaletteItems.FirstOrDefault(item => item.PersonId == mapFace.PersonId);
        var name = string.IsNullOrWhiteSpace(paletteItem?.Name)
            ? $"头像/人物{mapFace.PersonId}"
            : paletteItem.Name;
        var dataFaceId = paletteItem?.FaceId ?? mapFace.PersonId;

        var face = GetCachedRSceneFace(dataFaceId);

        var size = GetRSceneMapFaceRenderSize(face);
        var rect = new Rectangle(mapFace.X, mapFace.Y, size.Width, size.Height);
        using var backBrush = new SolidBrush(Color.FromArgb(175, 248, 246, 238));
        using var borderPen = new Pen(Color.MediumPurple, 2);
        graphics.FillRectangle(backBrush, rect);
        if (face != null)
        {
            graphics.DrawImage(face, rect);
        }
        else
        {
            TextRenderer.DrawText(
                graphics,
                name,
                Font,
                rect,
                Color.FromArgb(255, 28, 28, 32),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }
        graphics.DrawRectangle(borderPen, rect);

        var labelWidth = Math.Max(rect.Width, 88);
        var labelRect = new Rectangle(
            Math.Clamp(rect.Left, 0, Math.Max(0, RSceneCanvasWidth - labelWidth)),
            Math.Clamp(rect.Bottom, 0, Math.Max(0, RSceneCanvasHeight - 18)),
            labelWidth,
            18);
        using var labelBack = new SolidBrush(Color.FromArgb(205, 62, 35, 92));
        graphics.FillRectangle(labelBack, labelRect);
        TextRenderer.DrawText(
            graphics,
            $"{mapFace.PersonId} {name}",
            Font,
            labelRect,
            Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static Size GetRSceneMapFaceRenderSize(Bitmap? face)
    {
        if (face == null)
        {
            return new Size(RSceneMapFaceFallbackSize, RSceneMapFaceFallbackSize);
        }

        var scale = Math.Min(
            RSceneMapFaceMaxWidth / (double)Math.Max(1, face.Width),
            RSceneMapFaceMaxHeight / (double)Math.Max(1, face.Height));
        scale = Math.Min(1.0, scale);
        return new Size(
            Math.Max(1, (int)Math.Round(face.Width * scale)),
            Math.Max(1, (int)Math.Round(face.Height * scale)));
    }

    private void DrawRSceneDragPreview(Graphics graphics)
    {
        if (_rSceneDragPreviewPayload == null || !_rSceneDragPreviewGrid.HasValue) return;
        var payload = _rSceneDragPreviewPayload;
        var grid = _rSceneDragPreviewGrid.Value;
        var anchor = RSceneCoordinateToPixel(grid.X, grid.Y);
        using var tileBrush = new SolidBrush(Color.FromArgb(90, Color.DeepSkyBlue));
        using var tilePen = new Pen(Color.FromArgb(230, Color.DeepSkyBlue), 2);
        var diamond = new[]
        {
            new PointF(anchor.X, anchor.Y - RSceneTileHeight / 2f),
            new PointF(anchor.X + RSceneTileWidth / 2f, anchor.Y),
            new PointF(anchor.X, anchor.Y + RSceneTileHeight / 2f),
            new PointF(anchor.X - RSceneTileWidth / 2f, anchor.Y)
        };
        graphics.FillPolygon(tileBrush, diamond);
        graphics.DrawPolygon(tilePen, diamond);

        var frame = GetCachedRSceneActorFrame(payload.Actor.RImageId, payload.FrameIndex, payload.Facing);

        var width = frame?.Width ?? 48;
        var height = frame?.Height ?? 64;
        var left = (int)Math.Round(anchor.X - width / 2f);
        var top = (int)Math.Round(anchor.Y - height + RSceneTileHeight / 2f);
        var rect = new Rectangle(left, top, width, height);
        using var ghostBrush = new SolidBrush(Color.FromArgb(70, Color.White));
        graphics.FillRectangle(ghostBrush, rect);
        if (frame != null)
        {
            var state = graphics.Save();
            var matrix = new ColorMatrix { Matrix33 = 0.58f };
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            graphics.DrawImage(frame, rect, 0, 0, frame.Width, frame.Height, GraphicsUnit.Pixel, attributes);
            graphics.Restore(state);
        }
        else
        {
            TextRenderer.DrawText(graphics, payload.Actor.Name, Font, rect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }

        using var borderPen = new Pen(Color.DeepSkyBlue, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        graphics.DrawRectangle(borderPen, rect);
        DrawRSceneCoordinateLabel(graphics, anchor, $"{payload.Actor.PersonId} {payload.Actor.Name}  帧{payload.FrameIndex}  ({grid.X},{grid.Y})", Color.DeepSkyBlue);
    }

    private void DrawRSceneMovePreview(Graphics graphics)
    {
        if (_rSceneMovePreviewActor == null || !_rSceneMovePreviewGrid.HasValue) return;
        var grid = _rSceneMovePreviewGrid.Value;
        var anchor = RSceneCoordinateToPixel(grid.X, grid.Y);
        using var tileBrush = new SolidBrush(Color.FromArgb(80, Color.Orange));
        using var tilePen = new Pen(Color.FromArgb(235, Color.Orange), 2);
        var diamond = new[]
        {
            new PointF(anchor.X, anchor.Y - RSceneTileHeight / 2f),
            new PointF(anchor.X + RSceneTileWidth / 2f, anchor.Y),
            new PointF(anchor.X, anchor.Y + RSceneTileHeight / 2f),
            new PointF(anchor.X - RSceneTileWidth / 2f, anchor.Y)
        };
        graphics.FillPolygon(tileBrush, diamond);
        graphics.DrawPolygon(tilePen, diamond);
        DrawRSceneCoordinateLabel(graphics, anchor, $"{_rSceneMovePreviewActor.PersonId} {_rSceneMovePreviewActor.Name}  ({grid.X},{grid.Y})", Color.Orange);
    }

    private void DrawRSceneCoordinateLabel(Graphics graphics, PointF anchor, string label, Color borderColor)
    {
        var labelSize = TextRenderer.MeasureText(label, Font);
        var labelRect = new Rectangle(
            Math.Clamp((int)Math.Round(anchor.X + 10), 0, Math.Max(0, RSceneCanvasWidth - labelSize.Width - 12)),
            Math.Clamp((int)Math.Round(anchor.Y - 28), 0, Math.Max(0, RSceneCanvasHeight - labelSize.Height - 8)),
            labelSize.Width + 12,
            labelSize.Height + 6);
        using var labelBack = new SolidBrush(Color.FromArgb(225, 15, 30, 42));
        graphics.FillRectangle(labelBack, labelRect);
        using var borderPen = new Pen(borderColor);
        graphics.DrawRectangle(borderPen, labelRect);
        TextRenderer.DrawText(graphics, label, Font, labelRect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private static void DrawRSceneGrid(Graphics graphics, Size size)
    {
        using var pen = new Pen(Color.FromArgb(65, Color.White));
        using var axisPen = new Pen(Color.FromArgb(120, Color.LightSkyBlue));
        for (var x = -8; x <= 104; x++)
        {
            for (var y = -8; y <= 112; y++)
            {
                var center = RSceneCoordinateToPixel(x, y);
                if (center.X < -RSceneTileWidth || center.X > size.Width + RSceneTileWidth ||
                    center.Y < -RSceneTileHeight || center.Y > size.Height + RSceneTileHeight)
                {
                    continue;
                }

                var points = new[]
                {
                    new PointF(center.X, center.Y - RSceneTileHeight / 2f),
                    new PointF(center.X + RSceneTileWidth / 2f, center.Y),
                    new PointF(center.X, center.Y + RSceneTileHeight / 2f),
                    new PointF(center.X - RSceneTileWidth / 2f, center.Y)
                };
                graphics.DrawPolygon((x % 10 == 0 || y % 10 == 0) ? axisPen : pen, points);
            }
        }
    }

    private int GetRSceneGridSize()
        => RSceneTileWidth;

    private int GetSelectedRSceneFrameIndex()
        => Math.Clamp((int)_rSceneStanceInput.Value, 0, RSceneFrameCount - 1);

    private static PointF RSceneCoordinateToPixel(int x, int y)
        => new(
            8f * (x - y + RSceneCoordinateXPixelOffset),
            4f * (x + y - RSceneCoordinateYPixelOffset));

    private static Point RScenePixelToCoordinate(float px, float py)
        => new(
            (int)Math.Floor(px / RSceneTileWidth + py / RSceneTileHeight + 4),
            (int)Math.Floor(py / RSceneTileHeight - px / RSceneTileWidth + 46));

    private string GetSelectedRSceneFacing()
        => NormalizeRSceneFacing(_rSceneFacingCombo.SelectedItem?.ToString() ?? "下");

    private static string NormalizeRSceneFacing(string facing)
        => facing switch
        {
            "上" => "上",
            "左" => "左",
            "右" => "右",
            _ => "下"
        };

    private string GetSelectedRSceneBackgroundText()
        => _rSceneBackgroundCombo.SelectedItem is RSceneBackgroundComboItem item
            ? item.DisplayText
            : "未选择 Mmap.e5 背景";

    private void ApplyRSceneDraftForScenario(ScenarioFileInfo scenario)
    {
        if (_project == null) return;
        _rScenePlacedActors.Clear();
        _rSceneMapFaces.Clear();
        _selectedRScenePlacedActor = null;
        _editingRScenePlacedActor = null;
        _draggingRScenePlacedActor = null;
        _rScenePlacedActorDragStart = null;
        _rScenePlacedActorDragMoved = false;
        ClearRSceneMovePreview();
        var draft = _rSceneDraftService.LoadDraft(_project, scenario.FileName);
        using (SuppressRSceneCanvasRender())
        {
            _rSceneGridSizeInput.Value = Math.Clamp(draft.GridSize, (int)_rSceneGridSizeInput.Minimum, (int)_rSceneGridSizeInput.Maximum);
        }
        var firstScene = _currentRSceneStateCandidates.FirstOrDefault();
        var firstRow = firstScene == null ? null : FindRSceneScriptRow(firstScene);
        if (firstRow != null)
        {
            SelectRSceneScriptTreeNode(firstRow);
            return;
        }

        if (draft.BackgroundImageNumber > 0)
        {
            SelectRSceneBackgroundImageNumber(draft.BackgroundImageNumber);
        }
    }

    private void SaveRSceneDraft()
    {
        if (_project == null || _currentRSceneScenario == null)
        {
            MessageBox.Show(this, "请先读取一个 R 剧情。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var backgroundNumber = (_rSceneBackgroundCombo.SelectedItem as RSceneBackgroundComboItem)?.ImageNumber ?? 0;
            var path = _rSceneDraftService.SaveDraft(
                _project,
                _currentRSceneScenario.FileName,
                backgroundNumber,
                GetRSceneGridSize(),
                _rScenePlacedActors);
            SetStatus($"R场景制作：草稿已保存 {_currentRSceneScenario.FileName}");
            MessageBox.Show(this, "R 场景草稿已保存到项目侧，不会修改 R 剧本原文件。\r\n" + path, "保存完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("保存 R 场景草稿失败：" + ex);
            MessageBox.Show(this, ex.Message, "保存 R 场景草稿失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task JumpRSceneScriptAsync()
    {
        if (_currentRSceneScenario == null)
        {
            MessageBox.Show(this, "请先读取一个 R 剧情。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var rSceneRow = GetSelectedRSceneScriptCommandRow();
        if (!await SelectScriptScenarioByNameAsync(_currentRSceneScenario.FileName))
        {
            MessageBox.Show(this, "剧本制作页没有找到对应 R 剧情：" + _currentRSceneScenario.FileName, "无法跳转", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (rSceneRow == null)
        {
            SetStatus($"R场景制作：已跳到剧本制作 {_currentRSceneScenario.FileName}");
            return;
        }

        var scriptRow = FindScriptRowMatchingRSceneRow(rSceneRow);
        if (scriptRow == null)
        {
            SetStatus($"R场景制作：剧本制作页未找到对应指令 {rSceneRow.CommandIdHex} {rSceneRow.OffsetHex}");
            return;
        }

        SelectScriptTreeNode(scriptRow);
        _scriptDetailBox.Text =
            "从 R 场景制作跳转：\r\n" +
            BuildRSceneScriptRowDetail(rSceneRow) +
            "\r\n\r\n剧本制作页对应指令：\r\n" +
            BuildScriptRowDetail(scriptRow);
        SetStatus($"R场景制作：已跳到剧本指令 {scriptRow.CommandName} {scriptRow.OffsetHex}");
    }

    private ScenarioStructureRow? GetSelectedRSceneScriptCommandRow()
    {
        if (_rSceneScriptTree.SelectedNode?.Tag is LegacyScenarioItemData { UiRow: ScenarioStructureRow itemRow } &&
            itemRow.NodeType == "Command候选")
        {
            return itemRow;
        }

        if (_rSceneScriptTree.SelectedNode?.Tag is ScenarioStructureRow { NodeType: "Command候选" } selectedRow)
        {
            return selectedRow;
        }

        var candidate = GetSelectedRSceneStateCandidate();
        return candidate == null ? null : FindRSceneScriptRow(candidate);
    }

    private bool TryGetSelectedRSceneLegacyItemData(out LegacyScenarioItemData itemData)
    {
        if (_rSceneScriptTree.SelectedNode?.Tag is LegacyScenarioItemData selected)
        {
            itemData = selected;
            return true;
        }

        var row = GetSelectedRSceneScriptCommandRow();
        if (row != null &&
            _rSceneScriptItemDataByRow.TryGetValue(row, out itemData!) &&
            itemData.Command != null)
        {
            return true;
        }

        itemData = null!;
        return false;
    }

    private void EditSelectedRSceneScriptCommand()
    {
        if (!TryGetSelectedRSceneLegacyItemData(out var itemData) || itemData.Command == null)
        {
            MessageBox.Show(this, "请先在 R 剧本树中选择一条旧版命令。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!LegacyCommandEditDispatcher.CanEdit(itemData.Id))
        {
            MessageBox.Show(this, "旧版源码的 OnEditModify() 没有为该命令提供修改窗口。", "该命令暂不可修改", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var command = itemData.Command;
        var oldSummary = BuildLegacyScriptParameterPreview(command);
        var commandTitle = $"{command.CommandIdHex} {command.CommandName} / ord {itemData.Ord}";
        var dialogDataSources = LegacyMfcDialogDataSources.Create(_project, _tables);
        var precedingSameCommandCount = CountPrecedingSameLegacyCommands(_currentRSceneLegacyScriptDocument, command);
        var beforeEdit = CaptureLegacyScenarioHistorySnapshot(LegacyScriptEditorScope.RScene, _currentRSceneLegacyScriptDocument!);
        if (!LegacyCommandEditDispatcher.Edit(this, itemData, commandTitle, _currentRSceneLegacyScriptDocument?.CommandCount ?? 0, precedingSameCommandCount, dialogDataSources))
        {
            return;
        }

        CopyLegacyItemDataToCommand(itemData);
        NormalizeEditedRSceneJumpCommand(command);
        if (oldSummary != BuildLegacyScriptParameterPreview(command))
        {
            PushLegacyScenarioUndoSnapshot(LegacyScriptEditorScope.RScene, beforeEdit);
        }
        var refreshedCommand = RefreshLegacyEditorCommandInPlace(LegacyScriptEditorScope.RScene, command)
            ? command
            : RefreshRSceneLegacyScriptViewAndSelect(command);
        _saveRSceneScriptStructureButton.Enabled = true;
        SetStatus($"R场景旧版修改指令：{commandTitle}，{oldSummary} -> {BuildLegacyScriptParameterPreview(refreshedCommand)}，需完整保存R剧本");
    }

    private ScenarioStructureRow? FindScriptRowMatchingRSceneRow(ScenarioStructureRow rSceneRow)
    {
        if (_currentScriptStructure == null) return null;
        return _currentScriptStructure.Rows.FirstOrDefault(row =>
            row.NodeType == "Command候选" &&
            row.SceneIndex == rSceneRow.SceneIndex &&
            row.SectionIndex == rSceneRow.SectionIndex &&
            row.CommandIndex == rSceneRow.CommandIndex &&
            row.OffsetHex.Equals(rSceneRow.OffsetHex, StringComparison.OrdinalIgnoreCase) &&
            row.CommandIdHex.Equals(rSceneRow.CommandIdHex, StringComparison.OrdinalIgnoreCase));
    }

    private string BuildRSceneInfoText()
    {
        if (_currentRSceneScenario == null)
        {
            return "R 场景制作：未读取 R 剧情。";
        }

        var mode = _currentRSceneLegacyScriptDocument == null ? "兼容探针" : "旧版完整树";
        return
            $"R剧情：{_currentRSceneScenario.FileName}\r\n" +
            $"模式：{mode}\r\n" +
            $"Scene：{_currentRSceneScriptStructure?.SceneCount ?? 0}  Section：{_currentRSceneScriptStructure?.SectionCount ?? 0}  Command：{_currentRSceneScriptStructure?.CommandCandidateCount ?? 0}  文本：{_currentRSceneScriptTextEntries.Count}\r\n" +
            $"R场景视觉命令：{_currentRSceneCommandCandidates.Count} 条；背景候选：{_currentRSceneBackgroundEntries.Count} 张；角色列表：{_rSceneActorPaletteItems.Count} 人。\r\n" +
            $"当前画布：{GetSelectedRSceneBackgroundText()}；画布角色：{_rScenePlacedActors.Count} 个。\r\n" +
            "说明：右键角色进入编辑态，拖拽可同步 0x30 武将出现的 X/Y 坐标；完整保存R剧本后写入 R_XX.eex。其他角色摆放仍保存为项目侧草稿。";
    }
}
