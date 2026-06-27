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
    private enum LegacyScriptContextMenuRole
    {
        Edit,
        AddBefore,
        AddAfter,
        AddSubEventBefore,
        AddSubEventAfter,
        Duplicate,
        Delete,
        MoveUp,
        MoveDown,
        Undo,
        Redo,
        Cut,
        Copy,
        Paste
    }

    private void ConfigureHiddenScriptGrids()
    {
        _scriptCommandGrid.Dock = DockStyle.Fill;
        _scriptCommandGrid.ReadOnly = true;
        _scriptCommandGrid.AllowUserToAddRows = false;
        _scriptCommandGrid.AllowUserToDeleteRows = false;
        _scriptCommandGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _scriptCommandGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _scriptCommandGrid.MultiSelect = false;
        _scriptCommandGrid.RowHeadersVisible = false;
        _scriptCommandGrid.BorderStyle = BorderStyle.FixedSingle;

        _scriptTextGrid.Dock = DockStyle.Fill;
        _scriptTextGrid.ReadOnly = true;
        _scriptTextGrid.AllowUserToAddRows = false;
        _scriptTextGrid.AllowUserToDeleteRows = false;
        _scriptTextGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _scriptTextGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _scriptTextGrid.MultiSelect = false;
        _scriptTextGrid.RowHeadersVisible = false;
        _scriptTextGrid.BorderStyle = BorderStyle.FixedSingle;

        _scriptSearchResultGrid.Dock = DockStyle.Fill;
        _scriptSearchResultGrid.ReadOnly = true;
        _scriptSearchResultGrid.AllowUserToAddRows = false;
        _scriptSearchResultGrid.AllowUserToDeleteRows = false;
        _scriptSearchResultGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _scriptSearchResultGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _scriptSearchResultGrid.MultiSelect = false;
        _scriptSearchResultGrid.RowHeadersVisible = false;
        _scriptSearchResultGrid.BorderStyle = BorderStyle.FixedSingle;
    }

    private void ConfigureScriptTreeContextMenu()
    {
        if (_scriptTreeContextMenu.Items.Count > 0) return;

        _scriptContextAppendSectionItem.Click += (_, _) => AppendLegacyScriptCommandToSection();
        _scriptContextInsertBeforeItem.Click += (_, _) => InsertLegacyScriptCommandNearSelected(beforeSelected: true);
        _scriptContextInsertAfterItem.Click += (_, _) => InsertLegacyScriptCommandNearSelected(beforeSelected: false);
        _scriptContextAppendChildItem.Click += (_, _) => AppendLegacyScriptCommandToChildBlock();
        _scriptContextDeleteItem.Click += (_, _) => DeleteSelectedLegacyScriptCommand();
        _scriptContextEditItem.Click += (_, _) => FocusSelectedScriptObjectEditor();
        _scriptContextApplyParameterItem.Click += (_, _) => EditSelectedLegacyItemDataCommand();
        _scriptContextCopyItem.Click += (_, _) => CopySelectedScriptCommandSummary();
        _scriptContextPreviewPasteItem.Click += (_, _) => PreviewPasteScriptCommandCandidate();
        _scriptContextPasteBeforeItem.Click += (_, _) => PasteCopiedLegacyScriptCommandNearSelected(beforeSelected: true);
        _scriptContextPasteAfterItem.Click += (_, _) => PasteCopiedLegacyScriptCommandNearSelected(beforeSelected: false);
        _scriptContextMoveUpItem.Click += (_, _) => MoveSelectedLegacyScriptCommand(up: true);
        _scriptContextMoveDownItem.Click += (_, _) => MoveSelectedLegacyScriptCommand(up: false);

        _scriptTreeContextMenu.Opening += (_, e) =>
        {
            UpdateScriptTreeContextMenuItems();
            if (_scriptTree.SelectedNode == null)
            {
                e.Cancel = true;
            }
        };

        _scriptTreeContextMenu.Items.AddRange(new ToolStripItem[]
        {
            _scriptContextEditItem,
            new ToolStripSeparator(),
            _scriptContextApplyParameterItem,
            new ToolStripSeparator(),
            _scriptContextAppendSectionItem,
            _scriptContextInsertBeforeItem,
            _scriptContextInsertAfterItem,
            _scriptContextAppendChildItem,
            _scriptContextDeleteItem,
            new ToolStripSeparator(),
            _scriptContextCopyItem,
            _scriptContextPreviewPasteItem,
            _scriptContextPasteBeforeItem,
            _scriptContextPasteAfterItem,
            new ToolStripSeparator(),
            _scriptContextMoveUpItem,
            _scriptContextMoveDownItem,
            new ToolStripSeparator(),
            _scriptContextSaveTextItem
        });
    }

    private void ConfigureLegacyStyleScriptTreeContextMenu()
    {
        if (_legacyScriptTreeContextMenu.Items.Count > 0) return;

        _legacyScriptContextEditItem.Tag = LegacyScriptContextMenuRole.Edit.ToString();
        _legacyScriptContextAddBeforeItem.Tag = LegacyScriptContextMenuRole.AddBefore.ToString();
        _legacyScriptContextAddItem.Tag = LegacyScriptContextMenuRole.AddAfter.ToString();
        _legacyScriptContextAddSubEventBeforeItem.Tag = LegacyScriptContextMenuRole.AddSubEventBefore.ToString();
        _legacyScriptContextAddSubEventItem.Tag = LegacyScriptContextMenuRole.AddSubEventAfter.ToString();
        _legacyScriptContextDuplicateItem.Tag = LegacyScriptContextMenuRole.Duplicate.ToString();
        _legacyScriptContextDeleteItem.Tag = LegacyScriptContextMenuRole.Delete.ToString();
        _legacyScriptContextMoveUpItem.Tag = LegacyScriptContextMenuRole.MoveUp.ToString();
        _legacyScriptContextMoveDownItem.Tag = LegacyScriptContextMenuRole.MoveDown.ToString();
        _legacyScriptContextUndoItem.Tag = LegacyScriptContextMenuRole.Undo.ToString();
        _legacyScriptContextRedoItem.Tag = LegacyScriptContextMenuRole.Redo.ToString();
        _legacyScriptContextCutItem.Tag = LegacyScriptContextMenuRole.Cut.ToString();
        _legacyScriptContextCopyItem.Tag = LegacyScriptContextMenuRole.Copy.ToString();
        _legacyScriptContextPasteItem.Tag = LegacyScriptContextMenuRole.Paste.ToString();

        _legacyScriptContextEditItem.Click += (_, _) => EditSelectedLegacyItemDataCommand(LegacyScriptEditorScope.Script);
        _legacyScriptContextAddBeforeItem.Click += (_, _) => AddLegacyScriptCommandNearSelected(LegacyScriptEditorScope.Script, beforeSelected: true);
        _legacyScriptContextAddItem.Click += (_, _) => AddLegacyScriptCommandNearSelected(LegacyScriptEditorScope.Script, beforeSelected: false);
        _legacyScriptContextAddSubEventBeforeItem.Click += (_, _) => AddLegacyScriptSubEventNearSelected(LegacyScriptEditorScope.Script, beforeSelected: true);
        _legacyScriptContextAddSubEventItem.Click += (_, _) => AddLegacyScriptSubEventNearSelected(LegacyScriptEditorScope.Script, beforeSelected: false);
        _legacyScriptContextDuplicateItem.Click += (_, _) => StepDuplicateSelectedLegacyScriptCommand(LegacyScriptEditorScope.Script);
        _legacyScriptContextDeleteItem.Click += (_, _) => DeleteSelectedLegacyScriptCommand(LegacyScriptEditorScope.Script);
        _legacyScriptContextMoveUpItem.Click += (_, _) => MoveSelectedLegacyScriptCommand(LegacyScriptEditorScope.Script, up: true);
        _legacyScriptContextMoveDownItem.Click += (_, _) => MoveSelectedLegacyScriptCommand(LegacyScriptEditorScope.Script, up: false);
        _legacyScriptContextUndoItem.Click += (_, _) => UndoLegacyScenarioEdit(LegacyScriptEditorScope.Script);
        _legacyScriptContextRedoItem.Click += (_, _) => RedoLegacyScenarioEdit(LegacyScriptEditorScope.Script);
        _legacyScriptContextCutItem.Click += (_, _) => CutSelectedLegacyScriptCommand(LegacyScriptEditorScope.Script);
        _legacyScriptContextCopyItem.Click += (_, _) => CopySelectedScriptCommandSummary(LegacyScriptEditorScope.Script);
        _legacyScriptContextPasteItem.Click += (_, _) => PasteCopiedLegacyScriptCommandAtDefaultTarget(LegacyScriptEditorScope.Script);
        _legacyScriptContextTextImportItem.Click += (_, _) => ImportScenarioTextBelowSelected(LegacyScriptEditorScope.Script);
        _legacyScriptContextExpandItem.Click += (_, _) => ExpandSelectedLegacyScriptTreeNode(LegacyScriptEditorScope.Script);
        _legacyScriptContextJumpItem.Click += (_, _) => JumpSelectedLegacyScriptCommandTarget(LegacyScriptEditorScope.Script);

        _legacyScriptTreeContextMenu.Opening += (_, e) =>
        {
            UpdateLegacyStyleScriptTreeContextMenuItems(LegacyScriptEditorScope.Script);
            if (_scriptTree.SelectedNode == null)
            {
                e.Cancel = true;
            }
        };

        _legacyScriptTreeContextMenu.Items.AddRange(new ToolStripItem[]
        {
            _legacyScriptContextEditItem,
            _legacyScriptContextAddBeforeItem,
            _legacyScriptContextAddItem,
            _legacyScriptContextAddSubEventBeforeItem,
            _legacyScriptContextAddSubEventItem,
            _legacyScriptContextDuplicateItem,
            _legacyScriptContextDeleteItem,
            new ToolStripSeparator(),
            _legacyScriptContextMoveUpItem,
            _legacyScriptContextMoveDownItem,
            new ToolStripSeparator(),
            _legacyScriptContextUndoItem,
            _legacyScriptContextRedoItem,
            new ToolStripSeparator(),
            _legacyScriptContextCutItem,
            _legacyScriptContextCopyItem,
            _legacyScriptContextPasteItem,
            _legacyScriptContextTextImportItem,
            new ToolStripSeparator(),
            _legacyScriptContextExpandItem,
            new ToolStripSeparator(),
            _legacyScriptContextJumpItem
        });
    }

    private void ConfigureLegacyStyleScriptTreeContextMenu(
        ContextMenuStrip menu,
        LegacyScriptEditorScope scope)
    {
        if (menu.Items.Count > 0) return;

        menu.Opening += (_, e) =>
        {
            UpdateLegacyStyleScriptTreeContextMenuItems(scope);
            if (GetLegacyScriptTree(scope).SelectedNode == null)
            {
                e.Cancel = true;
            }
        };

        menu.Items.Add(CreateLegacyScriptContextMenuItem("修改(&E)\tCtrl+E", () => EditSelectedLegacyItemDataCommand(scope), LegacyScriptContextMenuRole.Edit));
        menu.Items.Add(CreateLegacyScriptContextMenuItem("在上方添加(&A)\tCtrl+Shift+I", () => AddLegacyScriptCommandNearSelected(scope, beforeSelected: true), LegacyScriptContextMenuRole.AddBefore));
        menu.Items.Add(CreateLegacyScriptContextMenuItem("在下方添加(&I)\tCtrl+I", () => AddLegacyScriptCommandNearSelected(scope, beforeSelected: false), LegacyScriptContextMenuRole.AddAfter));
        menu.Items.Add(CreateLegacyScriptContextMenuItem("在上方添加子事件(&B)\tCtrl+Shift+O", () => AddLegacyScriptSubEventNearSelected(scope, beforeSelected: true), LegacyScriptContextMenuRole.AddSubEventBefore));
        menu.Items.Add(CreateLegacyScriptContextMenuItem("在下方添加子事件(&S)\tCtrl+O", () => AddLegacyScriptSubEventNearSelected(scope, beforeSelected: false), LegacyScriptContextMenuRole.AddSubEventAfter));
        menu.Items.Add(CreateLegacyScriptContextMenuItem("步进复制(&D)\tCtrl+D", () => StepDuplicateSelectedLegacyScriptCommand(scope), LegacyScriptContextMenuRole.Duplicate));
        menu.Items.Add(CreateLegacyScriptContextMenuItem("删除(&D)\tDelete", () => DeleteSelectedLegacyScriptCommand(scope), LegacyScriptContextMenuRole.Delete));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateLegacyScriptContextMenuItem("上移(&U)\tCtrl+Up", () => MoveSelectedLegacyScriptCommand(scope, up: true), LegacyScriptContextMenuRole.MoveUp));
        menu.Items.Add(CreateLegacyScriptContextMenuItem("下移(&D)\tCtrl+Down", () => MoveSelectedLegacyScriptCommand(scope, up: false), LegacyScriptContextMenuRole.MoveDown));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateLegacyScriptContextMenuItem("撤销(&Z)\tCtrl+Z", () => UndoLegacyScenarioEdit(scope), LegacyScriptContextMenuRole.Undo));
        menu.Items.Add(CreateLegacyScriptContextMenuItem("前进(&Y)\tCtrl+Y", () => RedoLegacyScenarioEdit(scope), LegacyScriptContextMenuRole.Redo));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateLegacyScriptContextMenuItem("剪切(&T)\tCtrl+X", () => CutSelectedLegacyScriptCommand(scope), LegacyScriptContextMenuRole.Cut));
        menu.Items.Add(CreateLegacyScriptContextMenuItem("复制(&C)\tCtrl+C", () => CopySelectedScriptCommandSummary(scope), LegacyScriptContextMenuRole.Copy));
        menu.Items.Add(CreateLegacyScriptContextMenuItem("粘贴(&P)\tCtrl+V", () => PasteCopiedLegacyScriptCommandAtDefaultTarget(scope), LegacyScriptContextMenuRole.Paste));
        menu.Items.Add(CreateLegacyScriptContextMenuItem("文本导入...", () => ImportScenarioTextBelowSelected(scope), "TextImport"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateLegacyScriptContextMenuItem("全部展开\tCtrl+Q", () => ExpandSelectedLegacyScriptTreeNode(scope)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateLegacyScriptContextMenuItem("跳转到...", () => JumpSelectedLegacyScriptCommandTarget(scope)));
    }

    private static ToolStripMenuItem CreateLegacyScriptContextMenuItem(string text, Action action, string? tag = null)
    {
        var item = new ToolStripMenuItem(text) { Tag = tag };
        item.Click += (_, _) => action();
        return item;
    }

    private static ToolStripMenuItem CreateLegacyScriptContextMenuItem(
        string text,
        Action action,
        LegacyScriptContextMenuRole role)
        => CreateLegacyScriptContextMenuItem(text, action, role.ToString());

    private void UpdateLegacyStyleScriptTreeContextMenuItems()
        => UpdateLegacyStyleScriptTreeContextMenuItems(LegacyScriptEditorScope.Script);

    private void UpdateLegacyStyleScriptTreeContextMenuItems(LegacyScriptEditorScope scope)
    {
        EnsureScriptCommandComboReady();
        var menu = GetLegacyScriptContextMenu(scope);
        var selectedItemData = TryGetSelectedLegacyItemData(scope, out var itemData) ? itemData : null;
        var selectedSceneNode = TryGetSelectedLegacyScriptSceneNode(scope, out var selectedScene);
        var selectedSectionNode = TryGetSelectedLegacyScriptSectionNode(scope, out _);
        LegacyScenarioCommandNode command = null!;
        var selectedCommand = !selectedSceneNode && !selectedSectionNode && TryGetSelectedLegacyScriptCommand(scope, out command);
        var checkedCommands = GetCheckedLegacyScriptCommands(scope);
        var copySourceCount = checkedCommands.Count > 0 ? checkedCommands.Count : selectedCommand || selectedSceneNode || selectedSectionNode ? 1 : 0;
        var canEdit = selectedItemData?.Command != null && LegacyCommandEditDispatcher.CanEdit(selectedItemData.Id);
        var canAddBefore = (selectedItemData?.Scene != null || selectedItemData?.Section != null) ||
                           (selectedCommand && CanAddLegacyScriptCommandNearSelected(scope, command, beforeSelected: true, out _));
        var canAddAfter = (selectedItemData?.Scene != null || selectedItemData?.Section != null) ||
                          (selectedCommand && CanAddLegacyScriptCommandNearSelected(scope, command, beforeSelected: false, out _));
        var canAddSubEventBefore = selectedCommand && CanAddLegacySubEventNearSelected(scope, command, beforeSelected: true, out _);
        var canAddSubEventAfter = selectedCommand && CanAddLegacySubEventNearSelected(scope, command, beforeSelected: false, out _);
        var canDuplicate = selectedCommand && CanStepDuplicateLegacyScriptCommand(command, out _);
        var document = GetCurrentLegacyScriptDocument(scope);
        var deleteCommands = GetLegacyScriptCommandDeleteSelection(scope);
        var canDeleteCommandBatch = deleteCommands.Count > 0 &&
                                    deleteCommands.All(candidate => CanDeleteLegacyScriptCommand(scope, candidate, out _));
        var canDelete = deleteCommands.Count > 0
            ? canDeleteCommandBatch
            : selectedSceneNode && document != null
            ? CanDeleteLegacyScriptScene(document, selectedScene, out _)
            : selectedSectionNode && document != null && TryGetSelectedLegacyScriptSectionNode(scope, out var selectedSection)
                ? CanDeleteLegacyScriptSection(document, selectedSection, out _)
                : selectedCommand && CanDeleteLegacyScriptCommand(scope, command, out _);
        var canMoveUp = selectedCommand && CanMoveLegacyScriptCommand(scope, command, up: true, out _);
        var canMoveDown = selectedCommand && CanMoveLegacyScriptCommand(scope, command, up: false, out _);
        var canCopy = checkedCommands.Count > 0
            ? checkedCommands.All(candidate => CanCopyLegacyScriptCommand(candidate, out _))
            : selectedSceneNode || selectedSectionNode || (selectedCommand && CanCopyLegacyScriptCommand(command, out _));
        var canPaste = CanPasteCopiedLegacyScriptCommandNearSelected(scope, beforeSelected: true, out _);
        var pasteSceneCount = GetLegacyScriptClipboardScenesForPaste().Count;
        var pasteSectionCount = GetLegacyScriptClipboardSectionsForPaste().Count;
        var pasteCommandCount = GetLegacyScriptClipboardCommandsForPaste().Count;
        var selectedTree = GetLegacyScriptTree(scope);
        var canCut = CanCutLegacyScriptSelection(scope, out var cutSelection, out _);

        var editItem = FindLegacyScriptMenuItem(menu, _legacyScriptContextEditItem, LegacyScriptContextMenuRole.Edit);
        var addBeforeItem = FindLegacyScriptMenuItem(menu, _legacyScriptContextAddBeforeItem, LegacyScriptContextMenuRole.AddBefore);
        var addAfterItem = FindLegacyScriptMenuItem(menu, _legacyScriptContextAddItem, LegacyScriptContextMenuRole.AddAfter);
        var addSubEventBeforeItem = FindLegacyScriptMenuItem(menu, _legacyScriptContextAddSubEventBeforeItem, LegacyScriptContextMenuRole.AddSubEventBefore);
        var addSubEventAfterItem = FindLegacyScriptMenuItem(menu, _legacyScriptContextAddSubEventItem, LegacyScriptContextMenuRole.AddSubEventAfter);
        var duplicateItem = FindLegacyScriptMenuItem(menu, _legacyScriptContextDuplicateItem, LegacyScriptContextMenuRole.Duplicate);
        var deleteItem = FindLegacyScriptMenuItem(menu, _legacyScriptContextDeleteItem, LegacyScriptContextMenuRole.Delete);
        var moveUpItem = FindLegacyScriptMenuItem(menu, _legacyScriptContextMoveUpItem, LegacyScriptContextMenuRole.MoveUp);
        var moveDownItem = FindLegacyScriptMenuItem(menu, _legacyScriptContextMoveDownItem, LegacyScriptContextMenuRole.MoveDown);
        var undoItem = FindLegacyScriptMenuItem(menu, _legacyScriptContextUndoItem, LegacyScriptContextMenuRole.Undo);
        var redoItem = FindLegacyScriptMenuItem(menu, _legacyScriptContextRedoItem, LegacyScriptContextMenuRole.Redo);
        var cutItem = FindLegacyScriptMenuItem(menu, _legacyScriptContextCutItem, LegacyScriptContextMenuRole.Cut);
        var copyItem = FindLegacyScriptMenuItem(menu, _legacyScriptContextCopyItem, LegacyScriptContextMenuRole.Copy);
        var pasteItem = FindLegacyScriptMenuItem(menu, _legacyScriptContextPasteItem, LegacyScriptContextMenuRole.Paste);

        if (editItem != null)
        {
            editItem.Enabled = canEdit;
        }

        if (addBeforeItem != null)
        {
            addBeforeItem.Enabled = canAddBefore;
            addBeforeItem.Text = selectedSceneNode
                ? "在上方添加 Scene(&A)\tCtrl+Shift+I"
                : selectedSectionNode
                    ? "在上方添加 Section(&A)\tCtrl+Shift+I"
                    : "在上方添加指令(&A)\tCtrl+Shift+I";
        }

        if (addAfterItem != null)
        {
            addAfterItem.Enabled = canAddAfter;
            addAfterItem.Text = selectedSceneNode
                ? "在下方添加 Scene(&I)\tCtrl+I"
                : selectedSectionNode
                    ? "在下方添加 Section(&I)\tCtrl+I"
                    : "在下方添加指令(&I)\tCtrl+I";
        }

        if (addSubEventBeforeItem != null)
        {
            addSubEventBeforeItem.Enabled = canAddSubEventBefore;
            addSubEventBeforeItem.Text = "在上方添加子事件(&B)\tCtrl+Shift+O";
        }

        if (addSubEventAfterItem != null)
        {
            addSubEventAfterItem.Enabled = canAddSubEventAfter;
            addSubEventAfterItem.Text = "在下方添加子事件(&S)\tCtrl+O";
        }

        if (duplicateItem != null)
        {
            duplicateItem.Enabled = canDuplicate;
        }

        if (deleteItem != null)
        {
            deleteItem.Enabled = canDelete;
            deleteItem.Text = deleteCommands.Count > 1
                ? $"删除选中 {deleteCommands.Count} 条命令(&D)\tDelete"
                : deleteCommands.Count == 1
                    ? "删除命令(&D)\tDelete"
                    : selectedSceneNode
                ? "删除 Scene(&D)\tDelete"
                : selectedSectionNode
                    ? "删除 Section(&D)\tDelete"
                    : "删除命令(&D)\tDelete";
        }

        if (moveUpItem != null)
        {
            moveUpItem.Enabled = canMoveUp;
        }

        if (moveDownItem != null)
        {
            moveDownItem.Enabled = canMoveDown;
        }

        if (undoItem != null)
        {
            undoItem.Enabled = CanUndoLegacyScenarioEdit(scope);
        }

        if (redoItem != null)
        {
            redoItem.Enabled = CanRedoLegacyScenarioEdit(scope);
        }

        if (cutItem != null)
        {
            cutItem.Enabled = canCut;
            cutItem.Text = canCut
                ? BuildLegacyScriptCutMenuText(cutSelection)
                : "剪切(&T)\tCtrl+X";
        }

        if (copyItem != null)
        {
            copyItem.Enabled = canCopy;
            copyItem.Text = checkedCommands.Count > 1
                ? $"复制选中 {copySourceCount} 条(&C)\tCtrl+C"
                : selectedSceneNode
                    ? "复制 Scene(&C)\tCtrl+C"
                    : selectedSectionNode
                    ? "复制 Section(&C)\tCtrl+C"
                    : "复制(&C)\tCtrl+C";
        }

        if (pasteItem != null)
        {
            pasteItem.Enabled = canPaste;
            pasteItem.Text = pasteSceneCount > 0
                ? pasteSceneCount > 1
                    ? $"粘贴 {pasteSceneCount} 个 Scene 到后面(&P)\tCtrl+V"
                    : "粘贴 Scene 到后面(&P)\tCtrl+V"
                : pasteSectionCount > 0
                ? pasteSectionCount > 1
                    ? $"粘贴 {pasteSectionCount} 个 Section 到后面(&P)\tCtrl+V"
                    : "粘贴 Section 到后面(&P)\tCtrl+V"
                : pasteCommandCount > 1
                    ? $"粘贴 {pasteCommandCount} 条(&P)\tCtrl+V"
                    : "粘贴(&P)\tCtrl+V";
        }

        var expandItem = FindLegacyScriptMenuItem(menu, _legacyScriptContextExpandItem, "全部展开");
        if (expandItem != null)
        {
            expandItem.Enabled = selectedTree.SelectedNode?.Nodes.Count > 0;
        }

        var jumpItem = FindLegacyScriptMenuItem(menu, _legacyScriptContextJumpItem, "跳转到");
        if (jumpItem != null)
        {
            jumpItem.Enabled = selectedCommand && command.CommandId == 0x76 && command.JumpTargetOrdinal.HasValue;
        }

        var textImportItem = FindLegacyScriptMenuItem(menu, _legacyScriptContextTextImportItem, "TextImport");
        if (textImportItem != null)
        {
            textImportItem.Enabled = selectedCommand &&
                                     TryGetLegacyScriptCommandPasteTarget(scope, beforeSelected: false, out _, out _);
        }
    }

    private static ToolStripMenuItem? FindLegacyScriptMenuItem(
        ContextMenuStrip menu,
        ToolStripMenuItem fallback,
        LegacyScriptContextMenuRole role)
        => FindLegacyScriptMenuItem(menu, fallback, role.ToString());

    private static ToolStripMenuItem? FindLegacyScriptMenuItem(
        ContextMenuStrip menu,
        ToolStripMenuItem fallback,
        string key)
        => menu.Items
               .OfType<ToolStripMenuItem>()
               .FirstOrDefault(item => ReferenceEquals(item, fallback) ||
                                       string.Equals(Convert.ToString(item.Tag, CultureInfo.InvariantCulture), key, StringComparison.OrdinalIgnoreCase) ||
                                       (item.Text ?? string.Empty).Contains(key, StringComparison.OrdinalIgnoreCase));

    private void ImportScenarioTextBelowSelected(LegacyScriptEditorScope scope)
    {
        var document = GetCurrentLegacyScriptDocument(scope);
        if (document == null)
        {
            MessageBox.Show(this, "当前没有可编辑的旧版完整剧本树。", "无法文本导入", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!TryGetSelectedLegacyScriptCommand(scope, out var selected))
        {
            MessageBox.Show(this, "请先选择要作为插入位置的旧版指令。", "无法文本导入", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!TryGetLegacyScriptCommandPasteTarget(scope, beforeSelected: false, out var target, out var reason))
        {
            MessageBox.Show(this, reason, "无法文本导入", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var personNames = ScenarioTextImportService.LoadPersonNames(_project, _tables);
        var service = new ScenarioTextImportService(personNames);
        var templateText = ScenarioTextImportService.LoadTemplateText(_project);
        var targetText = $"Scene {selected.SceneIndex} / Section {selected.SectionIndex} / Command {selected.CommandIndex} 下方";
        using var dialog = new ScenarioTextImportDialog(
            targetText,
            service,
            input => service.Parse(input, target.SceneIndex, target.SectionIndex, CreateScenarioTextImportCommand),
            templateText);

        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.ImportedCommands.Count == 0)
        {
            return;
        }

        var commands = dialog.ImportedCommands.ToList();
        var affectedSection = TryFindLegacySectionForCommandList(document, target.Commands, out var targetSection)
            ? targetSection
            : null;
        var insertIndex = target.InsertIndex;
        ApplyLegacyScriptStructureEdit(
            scope,
            () => target.Commands.InsertRange(insertIndex, commands),
            commands.LastOrDefault(),
            $"已文本导入 {commands.Count} 条指令到 {target.TargetText} 下方。",
            new LegacyScriptStructureEditOptions(affectedSection));
    }

    private void HandleScriptTreeNodeMouseClick(TreeNodeMouseClickEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            _scriptTree.SelectedNode = e.Node;
            ShowSelectedScriptTreeNode();
        }
    }

    private void HandleLegacyScriptTreeNodeMouseClick(LegacyScriptEditorScope scope, TreeNodeMouseClickEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        var tree = GetLegacyScriptTree(scope);
        tree.SelectedNode = e.Node;
        ShowSelectedLegacyScriptTreeNode(scope);
    }

    private void HandleScriptTreeNodeAfterCheck(TreeViewEventArgs e)
    {
        if (_updatingScriptTreeChecks || e.Node == null) return;

        _updatingScriptTreeChecks = true;
        try
        {
            SetScriptTreeChildChecks(e.Node, e.Node.Checked);
            UpdateScriptStructureEditButtons();
        }
        finally
        {
            _updatingScriptTreeChecks = false;
        }
    }

    private static void SetScriptTreeChildChecks(TreeNode node, bool isChecked)
    {
        foreach (TreeNode child in node.Nodes)
        {
            child.Checked = isChecked;
            SetScriptTreeChildChecks(child, isChecked);
        }
    }

    private void HandleScriptTreeNodeMouseDoubleClick(TreeNodeMouseClickEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _scriptTree.SelectedNode = e.Node;
        ShowSelectedScriptTreeNode();
        if (TryGetSelectedLegacyItemData(out var itemData) && itemData.Command != null)
        {
            EditSelectedLegacyItemDataCommand();
            return;
        }

        FocusSelectedScriptObjectEditor();
    }

    private void UpdateScriptTreeContextMenuItems()
    {
        var hasLegacyDocument = _currentLegacyScriptDocument != null;
        var hasCommandTemplate = _scriptNewCommandCombo.SelectedItem is ScriptCommandComboItem;
        var selectedCommand = TryGetSelectedLegacyScriptCommand(out var command);
        var selectedSceneNode = TryGetSelectedLegacyScriptSceneNode(LegacyScriptEditorScope.Script, out var selectedScene);
        var selectedSectionNode = TryGetSelectedLegacyScriptSectionNode(LegacyScriptEditorScope.Script, out _);
        var selectedText = GetSelectedScriptTextEntry();
        var checkedCommands = GetCheckedLegacyScriptCommands();
        var deleteCommands = GetLegacyScriptCommandDeleteSelection(LegacyScriptEditorScope.Script);
        var canInsertNear = hasLegacyDocument &&
                            hasCommandTemplate &&
                            selectedCommand &&
                            CanInsertNearLegacyScriptCommand(command, out _) &&
                            TryFindLegacyCommandList(_currentLegacyScriptDocument!, command, out _, out _);

        _scriptContextEditItem.Text = selectedText != null
            ? "查看文本关联"
            : selectedCommand
                ? "旧版修改指令..."
                : "查看当前节点";
        _scriptContextApplyParameterItem.Text = "旧版修改指令...";
        _scriptContextEditItem.Enabled = selectedCommand || selectedText != null || _scriptTree.SelectedNode?.Tag is ScenarioStructureRow || _scriptTree.SelectedNode?.Tag is LegacyScenarioItemData;
        _scriptContextAppendSectionItem.Enabled = hasLegacyDocument && hasCommandTemplate && selectedSectionNode;
        _scriptContextInsertBeforeItem.Enabled = canInsertNear;
        _scriptContextInsertAfterItem.Enabled = canInsertNear;
        _scriptContextAppendChildItem.Enabled = hasLegacyDocument &&
                                               hasCommandTemplate &&
                                               selectedCommand &&
                                               command.ChildBlock != null;
        _scriptContextDeleteItem.Enabled = deleteCommands.Count > 0
            ? CanDeleteLegacyScriptCommandBatch(LegacyScriptEditorScope.Script, out _)
            : selectedSceneNode && _currentLegacyScriptDocument != null
            ? CanDeleteLegacyScriptScene(_currentLegacyScriptDocument, selectedScene, out _)
            : selectedSectionNode && _currentLegacyScriptDocument != null && TryGetSelectedLegacyScriptSectionNode(LegacyScriptEditorScope.Script, out var selectedSection)
                ? CanDeleteLegacyScriptSection(_currentLegacyScriptDocument, selectedSection, out _)
                : selectedCommand && CanDeleteLegacyScriptCommand(command, out _);
        _scriptContextDeleteItem.Text = deleteCommands.Count > 1
            ? $"删除选中 {deleteCommands.Count} 条命令"
            : deleteCommands.Count == 1
                ? "删除命令"
                : selectedSceneNode
            ? "删除 Scene"
            : selectedSectionNode
                ? "删除 Section"
                : "删除命令";
        _scriptContextApplyParameterItem.Enabled = TryGetSelectedLegacyItemData(out var selectedItemData) && LegacyCommandEditDispatcher.CanEdit(selectedItemData.Id);
        _scriptContextSaveTextItem.Text = "保存当前文本";
        _scriptContextSaveTextItem.Enabled = _saveScriptTextButton.Enabled;
        _scriptContextCopyItem.Enabled = selectedCommand || selectedSceneNode || selectedSectionNode || checkedCommands.Count > 0;
        _scriptContextCopyItem.Text = checkedCommands.Count > 1
            ? $"复制选中 {checkedCommands.Count} 条"
            : selectedSceneNode
                ? "复制 Scene"
                : selectedSectionNode
                ? "复制 Section"
                : "复制命令";
        _scriptContextPreviewPasteItem.Enabled = (selectedCommand || selectedSceneNode || selectedSectionNode) &&
                                                 (_scriptCommandClipboardItem != null ||
                                                  _legacyScriptCommandClipboardItems.Count > 0 ||
                                                  _legacyScriptSceneClipboardItems.Count > 0 ||
                                                  _legacyScriptSectionClipboardItems.Count > 0);
        _scriptContextPasteBeforeItem.Enabled = CanPasteCopiedLegacyScriptCommandNearSelected(beforeSelected: true, out _);
        _scriptContextPasteAfterItem.Enabled = CanPasteCopiedLegacyScriptCommandNearSelected(beforeSelected: false, out _);
        _scriptContextMoveUpItem.Enabled = selectedCommand && CanMoveLegacyScriptCommand(LegacyScriptEditorScope.Script, command, up: true, out _);
        _scriptContextMoveDownItem.Enabled = selectedCommand && CanMoveLegacyScriptCommand(LegacyScriptEditorScope.Script, command, up: false, out _);

        var commandName = FormatScriptNewCommandMenuText();
        _scriptContextAppendSectionItem.Text = string.IsNullOrWhiteSpace(commandName)
            ? "添加到正文末尾"
            : $"添加到正文末尾：{commandName}";
        _scriptContextInsertBeforeItem.Text = string.IsNullOrWhiteSpace(commandName)
            ? "在此命令前插入"
            : $"在此命令前插入：{commandName}";
        _scriptContextInsertAfterItem.Text = string.IsNullOrWhiteSpace(commandName)
            ? "在此命令后插入"
            : $"在此命令后插入：{commandName}";
        _scriptContextAppendChildItem.Text = string.IsNullOrWhiteSpace(commandName)
            ? "追加到当前子块"
            : $"追加到当前子块：{commandName}";
    }

    private void HandleScriptTreeKeyDown(KeyEventArgs e)
    {
        if (e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.Z)
        {
            UndoLegacyScenarioEdit(LegacyScriptEditorScope.Script);
            e.SuppressKeyPress = true;
            return;
        }

        if ((e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.Y) ||
            (e.Control && e.Shift && !e.Alt && e.KeyCode == Keys.Z))
        {
            RedoLegacyScenarioEdit(LegacyScriptEditorScope.Script);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.E)
        {
            EditSelectedLegacyItemDataCommand();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.Shift && e.KeyCode == Keys.I)
        {
            AddLegacyScriptCommandNearSelected(LegacyScriptEditorScope.Script, beforeSelected: true);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && !e.Shift && e.KeyCode == Keys.I)
        {
            AddLegacyScriptCommandNearSelected(LegacyScriptEditorScope.Script, beforeSelected: false);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.Shift && e.KeyCode == Keys.O)
        {
            AddLegacyScriptSubEventNearSelected(LegacyScriptEditorScope.Script, beforeSelected: true);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && !e.Shift && e.KeyCode == Keys.O)
        {
            AddLegacyScriptSubEventNearSelected(LegacyScriptEditorScope.Script, beforeSelected: false);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.D)
        {
            StepDuplicateSelectedLegacyScriptCommand();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.X)
        {
            CutSelectedLegacyScriptCommand();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.C)
        {
            CopySelectedScriptCommandSummary();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.V)
        {
            PasteCopiedLegacyScriptCommandAtDefaultTarget();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.Up)
        {
            MoveSelectedLegacyScriptCommand(up: true);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.Down)
        {
            MoveSelectedLegacyScriptCommand(up: false);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.Q)
        {
            ExpandSelectedLegacyScriptTreeNode();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Delete)
        {
            DeleteSelectedLegacyScriptCommand();
            e.SuppressKeyPress = true;
        }
    }

    private void HandleLegacyScriptTreeKeyDown(LegacyScriptEditorScope scope, KeyEventArgs e)
    {
        if (e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.Z)
        {
            UndoLegacyScenarioEdit(scope);
            e.SuppressKeyPress = true;
            return;
        }

        if ((e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.Y) ||
            (e.Control && e.Shift && !e.Alt && e.KeyCode == Keys.Z))
        {
            RedoLegacyScenarioEdit(scope);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.E)
        {
            EditSelectedLegacyItemDataCommand(scope);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.Shift && e.KeyCode == Keys.I)
        {
            AddLegacyScriptCommandNearSelected(scope, beforeSelected: true);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && !e.Shift && e.KeyCode == Keys.I)
        {
            AddLegacyScriptCommandNearSelected(scope, beforeSelected: false);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.Shift && e.KeyCode == Keys.O)
        {
            AddLegacyScriptSubEventNearSelected(scope, beforeSelected: true);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && !e.Shift && e.KeyCode == Keys.O)
        {
            AddLegacyScriptSubEventNearSelected(scope, beforeSelected: false);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.D)
        {
            StepDuplicateSelectedLegacyScriptCommand(scope);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.X)
        {
            CutSelectedLegacyScriptCommand(scope);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.C)
        {
            CopySelectedScriptCommandSummary(scope);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.V)
        {
            PasteCopiedLegacyScriptCommandAtDefaultTarget(scope);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.Up)
        {
            MoveSelectedLegacyScriptCommand(scope, up: true);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.Down)
        {
            MoveSelectedLegacyScriptCommand(scope, up: false);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.Q)
        {
            ExpandSelectedLegacyScriptTreeNode(scope);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Delete)
        {
            DeleteSelectedLegacyScriptCommand(scope);
            e.SuppressKeyPress = true;
        }
    }

    private void FocusSelectedScriptObjectEditor()
    {
        if (TryGetSelectedLegacyItemData(out var itemData) && itemData.Command != null)
        {
            EditSelectedLegacyItemDataCommand();
            return;
        }

        if (GetSelectedScriptTextEntry() != null)
        {
            _scriptTextEditorBox.Focus();
            _scriptTextEditorBox.SelectAll();
            return;
        }

        if (_scriptParameterGrid.Rows.Count > 0)
        {
            if (_scriptParameterGrid.CurrentCell == null)
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

            ShowSelectedLegacyScriptParameter();
            if (_scriptParameterValueBox.Enabled)
            {
                _scriptParameterValueBox.Focus();
                _scriptParameterValueBox.SelectAll();
            }
            else
            {
                _scriptParameterGrid.Focus();
            }
            return;
        }

        _scriptTree.Focus();
    }

    private string? FormatScriptNewCommandMenuText()
        => _scriptNewCommandCombo.SelectedItem is ScriptCommandComboItem item
            ? item.ToString()
            : null;

    private void AddLegacyScriptCommandBeforeSelected()
        => AddLegacyScriptCommandNearSelected(LegacyScriptEditorScope.Script, beforeSelected: false);

    private void AddLegacyScriptCommandBeforeSelected(LegacyScriptEditorScope scope)
        => AddLegacyScriptCommandNearSelected(scope, beforeSelected: false);

    private void AddLegacyScriptCommandNearSelected(LegacyScriptEditorScope scope, bool beforeSelected)
    {
        var document = GetCurrentLegacyScriptDocument(scope);
        if (document == null)
        {
            MessageBox.Show(this, "当前没有可编辑的旧版完整剧本树。", "无法添加指令", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (TryGetSelectedLegacyItemData(scope, out var itemData) && itemData.Command == null)
        {
            AddLegacyScriptSceneOrSectionNearSelected(scope, document, itemData, beforeSelected);
            return;
        }

        if (!TryGetSelectedLegacyScriptCommand(scope, out var selected))
        {
            MessageBox.Show(this, "请先选择要作为添加位置的旧版 Scene、Section 或指令。", "无法添加指令", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!CanAddLegacyScriptCommandNearSelected(scope, selected, beforeSelected, out var reason))
        {
            MessageBox.Show(this, reason, "无法添加指令", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!TryChooseLegacyScriptCommand(
                "添加指令",
                id => !IsBlockedNewLegacyCommandId(id) && CanUseLegacyScriptCommandAsAddCandidate(scope, selected, id),
                out var item))
        {
            return;
        }

        if (!TryFindLegacyCommandList(document, selected, out var list, out var index))
        {
            MessageBox.Show(this, "没有在当前旧版命令树中定位到插入位置。", "无法添加指令", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var command = CreateLegacyScriptCommand(selected.SceneIndex, selected.SectionIndex, item);
        if (command == null) return;
        var insertIndex = GetLegacyScriptAddInsertIndex(list, index, beforeSelected);

        ApplyLegacyScriptStructureEdit(
            scope,
            () => list.Insert(insertIndex, command),
            command,
            $"已在{(beforeSelected ? "上方" : "下方")}添加指令：{command.CommandIdHex} {command.CommandName}。",
            new LegacyScriptStructureEditOptions(FindLegacyScriptSectionForCommand(document, selected)));
        SelectLegacyScriptCommandComboItem(item.Id);
    }

    private void AddLegacyScriptSceneOrSectionNearSelected(
        LegacyScriptEditorScope scope,
        LegacyScenarioDocument document,
        LegacyScenarioItemData itemData,
        bool beforeSelected)
    {
        if (itemData.Scene != null)
        {
            AddLegacyScriptSceneNearSelected(scope, document, itemData.Scene, beforeSelected);
            return;
        }

        if (itemData.Section != null)
        {
            AddLegacyScriptSectionNearSelected(scope, document, itemData.Section, beforeSelected);
            return;
        }

        MessageBox.Show(this, "旧版规则不允许在根节点处直接创建子节点。请先选择 Scene 或 Section。", "无法添加", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void AddLegacyScriptSceneNearSelected(
        LegacyScriptEditorScope scope,
        LegacyScenarioDocument document,
        LegacyScenarioScene selectedScene,
        bool beforeSelected)
    {
        var index = document.Scenes.IndexOf(selectedScene);
        if (index < 0)
        {
            MessageBox.Show(this, "没有在当前旧版剧本树中定位到 Scene。", "无法添加 Scene", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var insertIndex = beforeSelected ? index : index + 1;
        var scene = CreateLegacyScriptDefaultScene(insertIndex + 1);
        var preferredSelection = scene.Sections.FirstOrDefault()?.Commands.FirstOrDefault();
        ApplyLegacyScriptStructureEdit(
            scope,
            () => document.Scenes.Insert(insertIndex, scene),
            preferredSelection,
            $"已在 Scene {selectedScene.SceneIndex} {(beforeSelected ? "上方" : "下方")}添加 Scene（含默认 Section）。");
    }

    private void AddLegacyScriptSectionNearSelected(
        LegacyScriptEditorScope scope,
        LegacyScenarioDocument document,
        LegacyScenarioSection selectedSection,
        bool beforeSelected)
    {
        var scene = document.Scenes.FirstOrDefault(candidate => candidate.Sections.Contains(selectedSection));
        if (scene == null)
        {
            MessageBox.Show(this, "没有在当前旧版剧本树中定位到 Section。", "无法添加 Section", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var index = scene.Sections.IndexOf(selectedSection);
        if (index < 0)
        {
            MessageBox.Show(this, "没有在当前 Scene 中定位到 Section。", "无法添加 Section", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var insertIndex = beforeSelected ? index : index + 1;
        var section = CreateLegacyScriptDefaultSection(scene.SceneIndex, insertIndex + 1);
        var preferredSelection = section.Commands.FirstOrDefault();
        ApplyLegacyScriptStructureEdit(
            scope,
            () => scene.Sections.Insert(insertIndex, section),
            preferredSelection,
            $"已在 Scene {scene.SceneIndex} / Section {selectedSection.SectionIndex} {(beforeSelected ? "上方" : "下方")}添加 Section。");
    }

    private void AddLegacyScriptSubEventBeforeSelected()
        => AddLegacyScriptSubEventNearSelected(LegacyScriptEditorScope.Script, beforeSelected: false);

    private void AddLegacyScriptSubEventBeforeSelected(LegacyScriptEditorScope scope)
        => AddLegacyScriptSubEventNearSelected(scope, beforeSelected: false);

    private void AddLegacyScriptSubEventNearSelected(LegacyScriptEditorScope scope, bool beforeSelected)
    {
        var document = GetCurrentLegacyScriptDocument(scope);
        if (document == null || !TryGetSelectedLegacyScriptCommand(scope, out var selected))
        {
            MessageBox.Show(this, "请先选择要追加子事件的位置。", "无法添加子事件", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!CanAddLegacySubEventNearSelected(scope, selected, beforeSelected, out var reason))
        {
            MessageBox.Show(this, reason, "无法添加子事件", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!TryChooseLegacyScriptCommand("添加子事件", IsLegacyScriptSubEventCarrierCommandId, out var item))
        {
            return;
        }

        if (!document.Scenes.Any() ||
            !TryFindLegacyCommandList(document, selected, out var list, out var index))
        {
            MessageBox.Show(this, "没有在当前旧版命令树中定位到插入位置。", "无法添加子事件", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var command = CreateLegacyScriptCommand(selected.SceneIndex, selected.SectionIndex, item);
        if (command == null) return;
        command.OpensSubEventBlock = true;
        command.ChildBlock = new LegacyScenarioCommandBlock
        {
            Kind = "SubEvent",
            LengthPrefixOffset = 0,
            FileOffset = 0,
            DeclaredLength = 0
        };
        command.ChildBlock.Commands.Add(CreateLegacyScriptStructuralCommand(0x00, selected.SceneIndex, selected.SectionIndex));

        var marker = CreateLegacyScriptStructuralCommand(0x01, selected.SceneIndex, selected.SectionIndex);
        var insertIndex = GetLegacyScriptAddInsertIndex(list, index, beforeSelected);
        ApplyLegacyScriptStructureEdit(
            scope,
            () =>
            {
                list.Insert(insertIndex, marker);
                list.Insert(insertIndex + 1, command);
            },
            command,
            $"已在{(beforeSelected ? "上方" : "下方")}添加子事件：{command.CommandIdHex} {command.CommandName}。",
            new LegacyScriptStructureEditOptions(FindLegacyScriptSectionForCommand(document, selected)));
        SelectLegacyScriptCommandComboItem(item.Id);
    }

    private bool CanAddLegacyScriptCommandBeforeSelected(LegacyScenarioCommandNode? command, out string reason)
        => CanAddLegacyScriptCommandNearSelected(LegacyScriptEditorScope.Script, command, beforeSelected: false, out reason);

    private bool CanAddLegacyScriptCommandBeforeSelected(LegacyScriptEditorScope scope, LegacyScenarioCommandNode? command, out string reason)
        => CanAddLegacyScriptCommandNearSelected(scope, command, beforeSelected: false, out reason);

    private bool CanAddLegacyScriptCommandNearSelected(
        LegacyScriptEditorScope scope,
        LegacyScenarioCommandNode? command,
        bool beforeSelected,
        out string reason)
    {
        var document = GetCurrentLegacyScriptDocument(scope);
        if (document == null)
        {
            reason = "当前没有可编辑的旧版完整剧本树。";
            return false;
        }

        if (command == null)
        {
            reason = "请先选择要作为添加位置的旧版指令。";
            return false;
        }

        if (!TryFindLegacyCommandList(document, command, out var list, out var index))
        {
            reason = "没有在当前旧版命令树中定位到插入位置。";
            return false;
        }

        if (IsLegacyScriptSectionTopLevelList(document, list) && command.CommandId == 0x02)
        {
            reason = "旧版不允许在 Section 顶层 2 号指令处添加指令。";
            return false;
        }

        if (!beforeSelected && IsLegacyScriptTrailingBoundary(command))
        {
            reason = "不允许在事件/子事件结束边界下方添加指令；请选择上一条正文指令。";
            return false;
        }

        var insertIndex = GetLegacyScriptAddInsertIndex(list, index, beforeSelected);
        if (HasLegacyScriptSubEventMarkerBefore(list, insertIndex))
        {
            reason = "不允许在 0x01 子事件设定标志和其承载指令之间添加指令。";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool CanAddLegacySubEventBeforeSelected(LegacyScenarioCommandNode command, out string reason)
        => CanAddLegacySubEventNearSelected(LegacyScriptEditorScope.Script, command, beforeSelected: false, out reason);

    private bool CanAddLegacySubEventBeforeSelected(LegacyScriptEditorScope scope, LegacyScenarioCommandNode? command, out string reason)
        => CanAddLegacySubEventNearSelected(scope, command, beforeSelected: false, out reason);

    private bool CanAddLegacySubEventNearSelected(
        LegacyScriptEditorScope scope,
        LegacyScenarioCommandNode? command,
        bool beforeSelected,
        out string reason)
    {
        var document = GetCurrentLegacyScriptDocument(scope);
        if (document == null)
        {
            reason = "当前没有可编辑的旧版完整剧本树。";
            return false;
        }

        if (command == null)
        {
            reason = "请先选择要追加子事件的位置。";
            return false;
        }

        if (command.CommandId is 0x00 or 0x01)
        {
            reason = "不能直接在 0/1 号结构指令处创建子事件。";
            return false;
        }

        if (!TryFindLegacyCommandList(document, command, out var list, out var index))
        {
            reason = "没有在当前旧版命令树中定位到插入位置。";
            return false;
        }

        if (IsLegacyScriptSectionTopLevelList(document, list))
        {
            reason = "旧版不允许直接在 Section 顶层创建子事件。";
            return false;
        }

        if (!beforeSelected && IsLegacyScriptTrailingBoundary(command))
        {
            reason = "不允许在事件/子事件结束边界下方创建子事件；请选择上一条正文指令。";
            return false;
        }

        var insertIndex = GetLegacyScriptAddInsertIndex(list, index, beforeSelected);
        if (HasLegacyScriptSubEventMarkerBefore(list, insertIndex))
        {
            reason = "不允许在 0x01 子事件设定标志和其承载指令之间添加子事件。";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private void StepDuplicateSelectedLegacyScriptCommand()
        => StepDuplicateSelectedLegacyScriptCommand(LegacyScriptEditorScope.Script);

    private void StepDuplicateSelectedLegacyScriptCommand(LegacyScriptEditorScope scope)
    {
        var document = GetCurrentLegacyScriptDocument(scope);
        if (document == null || !TryGetSelectedLegacyScriptCommand(scope, out var selected))
        {
            MessageBox.Show(this, "请先选择要步进复制的指令。", "无法步进复制", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!CanStepDuplicateLegacyScriptCommand(selected, out var reason))
        {
            MessageBox.Show(this, reason, "无法步进复制", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!TryShowLegacyStepDuplicateDialog(selected, out var count, out var slotIndex, out var delta))
        {
            return;
        }

        if (!TryGetLegacyCommandIntDataSlot(selected, slotIndex, out var baseValue, out reason))
        {
            MessageBox.Show(this, reason, "无法步进复制", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!TryFindLegacyCommandList(document, selected, out var list, out var index))
        {
            MessageBox.Show(this, "没有在当前旧版命令树中定位到复制位置。", "无法步进复制", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        LegacyScenarioCommandNode? lastClone = null;
        ApplyLegacyScriptStructureEdit(
            scope,
            () =>
            {
                for (var i = 0; i < count; i++)
                {
                    var clone = CloneLegacyScriptCommandForPaste(selected, selected.SceneIndex, selected.SectionIndex);
                    if (!TrySetLegacyCommandIntDataSlot(clone, slotIndex, baseValue + delta * (i + 1), out reason))
                    {
                        throw new InvalidOperationException(reason);
                    }

                    list.Insert(index + 1 + i, clone);
                    lastClone = clone;
                }
            },
            lastClone ?? selected,
            $"已步进复制 {count} 条：{selected.CommandIdHex} {selected.CommandName}。",
            new LegacyScriptStructureEditOptions(FindLegacyScriptSectionForCommand(document, selected)));
    }

    private bool CanStepDuplicateLegacyScriptCommand(LegacyScenarioCommandNode command, out string reason)
    {
        if (command.CommandId is 0x46 or 0x47 or 0x05 or 0x00 or 0x01 or 0x02)
        {
            reason = "旧版步进复制不支持 46/47/5/0/1/2 号指令。";
            return false;
        }

        if (command.ChildBlock != null)
        {
            reason = "旧版步进复制不支持带子事件块的指令。";
            return false;
        }

        if (!TryGetLegacyCommandIntDataSlot(command, 0, out _, out _))
        {
            reason = "该指令没有可步进修改的 int_data 槽。";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private enum LegacyScriptCutSelectionKind
    {
        CommandBatch,
        Scene,
        Section
    }

    private sealed record LegacyScriptCutSelection(
        LegacyScriptCutSelectionKind Kind,
        IReadOnlyList<LegacyScenarioCommandNode> Commands,
        LegacyScenarioScene? Scene,
        LegacyScenarioSection? Section);

    private sealed record LegacyScriptCutClipboardResult(
        string DetailText,
        string StatusText);

    private void CutSelectedLegacyScriptCommand()
        => CutSelectedLegacyScriptCommand(LegacyScriptEditorScope.Script);

    private void CutSelectedLegacyScriptCommand(LegacyScriptEditorScope scope)
    {
        if (!CanCutLegacyScriptSelection(scope, out var selection, out var reason))
        {
            MessageBox.Show(this, reason, "无法剪切", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!TryCopyLegacyScriptCutSelectionToClipboard(scope, selection, out var clipboardResult))
        {
            return;
        }

        var document = GetCurrentLegacyScriptDocument(scope);
        if (document == null)
        {
            MessageBox.Show(this, "当前没有可编辑的旧版完整剧本树。", "无法剪切", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        switch (selection.Kind)
        {
            case LegacyScriptCutSelectionKind.CommandBatch:
                DeleteLegacyScriptCommandBatch(scope, document, selection.Commands);
                break;
            case LegacyScriptCutSelectionKind.Scene when selection.Scene != null:
                DeleteSelectedLegacyScriptScene(scope, document, selection.Scene);
                break;
            case LegacyScriptCutSelectionKind.Section when selection.Section != null:
                DeleteSelectedLegacyScriptSection(scope, document, selection.Section);
                break;
        }

        SetLegacyScriptDetailText(scope, clipboardResult.DetailText);
        SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)}：{clipboardResult.StatusText}");
        if (scope == LegacyScriptEditorScope.Script)
        {
            UpdateScriptStructureEditButtons();
        }
    }

    private bool CanCutLegacyScriptSelection(
        LegacyScriptEditorScope scope,
        out LegacyScriptCutSelection selection,
        out string reason)
        => TryGetLegacyScriptCutSelection(scope, out selection, out reason);

    private bool TryGetLegacyScriptCutSelection(
        LegacyScriptEditorScope scope,
        out LegacyScriptCutSelection selection,
        out string reason)
    {
        selection = null!;
        var document = GetCurrentLegacyScriptDocument(scope);
        if (document == null)
        {
            reason = "当前没有可编辑的旧版完整剧本树。";
            return false;
        }

        var commands = GetLegacyScriptCommandDeleteSelection(scope);
        if (commands.Count > 0)
        {
            foreach (var command in commands)
            {
                if (!CanCopyLegacyScriptCommand(command, out reason))
                {
                    reason = $"选中列表中包含不能剪切的命令：\r\n{command.CommandIndex:000} {command.CommandIdHex} {command.CommandName}\r\n\r\n{reason}";
                    return false;
                }
            }

            if (!CanDeleteLegacyScriptCommandBatch(scope, out reason))
            {
                return false;
            }

            selection = new LegacyScriptCutSelection(
                LegacyScriptCutSelectionKind.CommandBatch,
                commands,
                null,
                null);
            reason = string.Empty;
            return true;
        }

        if (TryGetSelectedLegacyScriptSceneNode(scope, out var scene))
        {
            if (!CanDeleteLegacyScriptScene(document, scene, out reason))
            {
                return false;
            }

            selection = new LegacyScriptCutSelection(
                LegacyScriptCutSelectionKind.Scene,
                Array.Empty<LegacyScenarioCommandNode>(),
                scene,
                null);
            reason = string.Empty;
            return true;
        }

        if (TryGetSelectedLegacyScriptSectionNode(scope, out var section))
        {
            if (!CanDeleteLegacyScriptSection(document, section, out reason))
            {
                return false;
            }

            selection = new LegacyScriptCutSelection(
                LegacyScriptCutSelectionKind.Section,
                Array.Empty<LegacyScenarioCommandNode>(),
                null,
                section);
            reason = string.Empty;
            return true;
        }

        reason = "请先选择要剪切的命令、Section 或 Scene，也可以勾选多条命令后批量剪切。";
        return false;
    }

    private static string BuildLegacyScriptCutMenuText(LegacyScriptCutSelection selection)
        => selection.Kind switch
        {
            LegacyScriptCutSelectionKind.CommandBatch when selection.Commands.Count > 1 => $"剪切选中 {selection.Commands.Count} 条命令(&T)\tCtrl+X",
            LegacyScriptCutSelectionKind.CommandBatch => "剪切命令(&T)\tCtrl+X",
            LegacyScriptCutSelectionKind.Scene => "剪切 Scene(&T)\tCtrl+X",
            LegacyScriptCutSelectionKind.Section => "剪切 Section(&T)\tCtrl+X",
            _ => "剪切(&T)\tCtrl+X"
        };

    private bool TryCopyLegacyScriptCutSelectionToClipboard(
        LegacyScriptEditorScope scope,
        LegacyScriptCutSelection selection,
        out LegacyScriptCutClipboardResult result)
    {
        result = null!;
        return selection.Kind switch
        {
            LegacyScriptCutSelectionKind.CommandBatch => TryCopyLegacyScriptCommandsForCut(scope, selection.Commands, out result),
            LegacyScriptCutSelectionKind.Scene when selection.Scene != null => TryCopyLegacyScriptScenesForCut(scope, new[] { selection.Scene! }, out result),
            LegacyScriptCutSelectionKind.Section when selection.Section != null => TryCopyLegacyScriptSectionsForCut(scope, new[] { selection.Section! }, out result),
            _ => false
        };
    }

    private bool TryCopyLegacyScriptCommandsForCut(
        LegacyScriptEditorScope scope,
        IReadOnlyList<LegacyScenarioCommandNode> commands,
        out LegacyScriptCutClipboardResult result)
    {
        result = null!;
        if (commands.Count == 0)
        {
            return false;
        }

        var scenarioName = GetLegacyScriptClipboardScenarioName(scope);
        var text = BuildLegacyScriptCommandBatchCopyText(scenarioName, commands);
        var clipboardText = BuildLegacyScriptCommandClipboardText(text, scenarioName, commands);
        if (!TrySetLegacyScriptClipboardTextForCut(scope, clipboardText, text))
        {
            return false;
        }

        _scriptCommandClipboardItem = null;
        if (scope == LegacyScriptEditorScope.Script)
        {
            var first = commands[0];
            if (_legacyScriptRowByKey.TryGetValue(BuildLegacyCommandKey(first), out var firstRow))
            {
                _scriptCommandClipboardItem = _scenarioCommandClipboardService.CreateClipboardItem(
                    scenarioName,
                    firstRow,
                    BuildLegacyScriptParameterRows(first));
            }
        }

        var clipboardItems = commands
            .Select(command => CloneLegacyScriptCommandForPaste(command, command.SceneIndex, command.SectionIndex))
            .ToList();
        _legacyScriptCommandClipboardItems = clipboardItems;
        _legacyScriptCommandClipboard = clipboardItems[0];
        _legacyScriptSceneClipboardItems = Array.Empty<LegacyScenarioScene>();
        _legacyScriptSectionClipboardItems = Array.Empty<LegacyScenarioSection>();
        _legacyScriptCommandClipboardScenarioName = scenarioName;
        _legacyScriptCommandClipboardGameRoot = _project?.GameRoot ?? string.Empty;
        _previewPasteScriptCommandButton.Enabled = true;
        if (scope == LegacyScriptEditorScope.Script)
        {
            UpdateScriptStructureEditButtons();
        }

        result = new LegacyScriptCutClipboardResult(
            text + "\r\n\r\n已剪切到剪贴板并删除来源。",
            commands.Count == 1
                ? $"已剪切命令：{commands[0].CommandIdHex} {commands[0].CommandName}"
                : $"已剪切 {commands.Count} 条命令到剪贴板并删除来源");
        return true;
    }

    private bool TryCopyLegacyScriptSectionsForCut(
        LegacyScriptEditorScope scope,
        IReadOnlyList<LegacyScenarioSection> sections,
        out LegacyScriptCutClipboardResult result)
    {
        result = null!;
        if (sections.Count == 0)
        {
            return false;
        }

        var scenarioName = GetLegacyScriptClipboardScenarioName(scope);
        var text = BuildLegacyScriptSectionBatchCopyText(scenarioName, sections);
        var clipboardText = BuildLegacyScriptSectionClipboardText(text, scenarioName, sections);
        if (!TrySetLegacyScriptClipboardTextForCut(scope, clipboardText, text))
        {
            return false;
        }

        _scriptCommandClipboardItem = null;
        _legacyScriptCommandClipboard = null;
        _legacyScriptCommandClipboardItems = Array.Empty<LegacyScenarioCommandNode>();
        _legacyScriptSceneClipboardItems = Array.Empty<LegacyScenarioScene>();
        _legacyScriptSectionClipboardItems = sections
            .Select(section => CloneLegacyScriptSectionForPaste(section, section.SceneIndex, section.SectionIndex))
            .ToList();
        _legacyScriptCommandClipboardScenarioName = scenarioName;
        _legacyScriptCommandClipboardGameRoot = _project?.GameRoot ?? string.Empty;
        _previewPasteScriptCommandButton.Enabled = true;
        if (scope == LegacyScriptEditorScope.Script)
        {
            UpdateScriptStructureEditButtons();
        }

        result = new LegacyScriptCutClipboardResult(
            text + "\r\n\r\n已剪切到剪贴板并删除来源。",
            sections.Count == 1
                ? $"已剪切 Scene {sections[0].SceneIndex} / Section {sections[0].SectionIndex} 到剪贴板并删除来源"
                : $"已剪切 {sections.Count} 个 Section 到剪贴板并删除来源");
        return true;
    }

    private bool TryCopyLegacyScriptScenesForCut(
        LegacyScriptEditorScope scope,
        IReadOnlyList<LegacyScenarioScene> scenes,
        out LegacyScriptCutClipboardResult result)
    {
        result = null!;
        if (scenes.Count == 0)
        {
            return false;
        }

        var scenarioName = GetLegacyScriptClipboardScenarioName(scope);
        var text = BuildLegacyScriptSceneBatchCopyText(scenarioName, scenes);
        var clipboardText = BuildLegacyScriptSceneClipboardText(text, scenarioName, scenes);
        if (!TrySetLegacyScriptClipboardTextForCut(scope, clipboardText, text))
        {
            return false;
        }

        _scriptCommandClipboardItem = null;
        _legacyScriptCommandClipboard = null;
        _legacyScriptCommandClipboardItems = Array.Empty<LegacyScenarioCommandNode>();
        _legacyScriptSectionClipboardItems = Array.Empty<LegacyScenarioSection>();
        _legacyScriptSceneClipboardItems = scenes
            .Select(CloneLegacyScriptSceneForPaste)
            .ToList();
        _legacyScriptCommandClipboardScenarioName = scenarioName;
        _legacyScriptCommandClipboardGameRoot = _project?.GameRoot ?? string.Empty;
        _previewPasteScriptCommandButton.Enabled = true;
        if (scope == LegacyScriptEditorScope.Script)
        {
            UpdateScriptStructureEditButtons();
        }

        result = new LegacyScriptCutClipboardResult(
            text + "\r\n\r\n已剪切到剪贴板并删除来源。",
            scenes.Count == 1
                ? $"已剪切 Scene {scenes[0].SceneIndex} 到剪贴板并删除来源"
                : $"已剪切 {scenes.Count} 个 Scene 到剪贴板并删除来源");
        return true;
    }

    private string GetLegacyScriptClipboardScenarioName(LegacyScriptEditorScope scope)
    {
        var scenarioName = scope == LegacyScriptEditorScope.Script
            ? _currentScriptScenario?.FileName ?? "RS"
            : GetLegacyScriptScenarioName(scope);
        return string.IsNullOrWhiteSpace(scenarioName)
            ? GetLegacyScriptScopeStatusPrefix(scope)
            : scenarioName;
    }

    private bool TrySetLegacyScriptClipboardTextForCut(
        LegacyScriptEditorScope scope,
        string clipboardText,
        string detailText)
    {
        try
        {
            Clipboard.SetText(clipboardText);
            return true;
        }
        catch (Exception ex)
        {
            SetLegacyScriptDetailText(scope, detailText + "\r\n\r\n剪贴板写入失败，已取消剪切，来源未删除。\r\n" + ex.Message);
            SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)}：剪贴板写入失败，已取消剪切");
            MessageBox.Show(this,
                "剪贴板写入失败，已取消剪切，来源未删除。\r\n\r\n" + ex.Message,
                "无法剪切",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }
    }

    private void ExpandSelectedLegacyScriptTreeNode()
        => ExpandSelectedLegacyScriptTreeNode(LegacyScriptEditorScope.Script);

    private void ExpandSelectedLegacyScriptTreeNode(LegacyScriptEditorScope scope)
    {
        var node = GetLegacyScriptTree(scope).SelectedNode;
        if (node == null) return;
        ExpandTreeRecursively(node);
        node.EnsureVisible();
    }

    private static void ExpandTreeRecursively(TreeNode node)
    {
        node.Expand();
        foreach (TreeNode child in node.Nodes)
        {
            ExpandTreeRecursively(child);
        }
    }

    private void JumpSelectedLegacyScriptCommandTarget()
        => JumpSelectedLegacyScriptCommandTarget(LegacyScriptEditorScope.Script);

    private void JumpSelectedLegacyScriptCommandTarget(LegacyScriptEditorScope scope)
    {
        if (!TryGetSelectedLegacyScriptCommand(scope, out var command) || command.CommandId != 0x76)
        {
            MessageBox.Show(this, "只有 0x76 无条件跳转指令可以直接跳转。", "无法跳转", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var document = GetCurrentLegacyScriptDocument(scope);
        if (!command.JumpTargetOrdinal.HasValue || document == null)
        {
            MessageBox.Show(this, "该跳转指令没有解析到目标 ord。", "无法跳转", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var target = document.EnumerateCommands()
            .FirstOrDefault(candidate => candidate.CommandOrdinal == command.JumpTargetOrdinal.Value);
        if (target == null || !TrySelectLegacyScriptCommand(scope, target))
        {
            MessageBox.Show(this, $"没有找到目标 ord {command.JumpTargetOrdinal.Value}。", "无法跳转", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ShowSelectedLegacyScriptTreeNode(scope);
        SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)}：已跳转到 ord {command.JumpTargetOrdinal.Value}");
    }

    private bool TryChooseLegacyScriptCommand(string title, Func<int, bool> idFilter, out ScriptCommandComboItem item)
    {
        item = null!;
        if (!EnsureScriptCommandComboReady())
        {
            MessageBox.Show(this, "当前没有可选命令。请先读取 CczString.ini 剧本字典后再添加。", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        var items = _scriptNewCommandCombo.Items
            .OfType<ScriptCommandComboItem>()
            .Where(candidate => idFilter(candidate.Id))
            .ToList();
        if (items.Count == 0)
        {
            MessageBox.Show(this, "当前没有可选命令。请先读取剧本字典并加载旧版完整树。", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        using var dialog = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(420, 92)
        };
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Left = 12,
            Top = 12,
            Width = 392,
            DataSource = items
        };
        var okButton = new Button
        {
            Text = "确定",
            DialogResult = DialogResult.OK,
            Left = 248,
            Top = 52,
            Width = 75
        };
        var cancelButton = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Left = 329,
            Top = 52,
            Width = 75
        };
        dialog.Controls.AddRange(new Control[] { combo, okButton, cancelButton });
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;
        if (_scriptNewCommandCombo.SelectedItem is ScriptCommandComboItem selected)
        {
            var selectedIndex = items.FindIndex(candidate => candidate.Id == selected.Id);
            if (selectedIndex >= 0 && selectedIndex < combo.Items.Count)
            {
                combo.SelectedIndex = selectedIndex;
            }
            else if (combo.Items.Count > 0 && combo.SelectedIndex < 0)
            {
                combo.SelectedIndex = 0;
            }
        }
        else if (combo.Items.Count > 0 && combo.SelectedIndex < 0)
        {
            combo.SelectedIndex = 0;
        }

        if (dialog.ShowDialog(this) != DialogResult.OK || combo.SelectedItem is not ScriptCommandComboItem selectedItem)
        {
            return false;
        }

        item = selectedItem;
        return true;
    }

    private void SelectLegacyScriptCommandComboItem(int commandId)
    {
        for (var i = 0; i < _scriptNewCommandCombo.Items.Count; i++)
        {
            if (_scriptNewCommandCombo.Items[i] is ScriptCommandComboItem item && item.Id == commandId)
            {
                if (i >= 0 && i < _scriptNewCommandCombo.Items.Count)
                {
                    _scriptNewCommandCombo.SelectedIndex = i;
                }
                return;
            }
        }
    }

    private static bool IsLegacyScriptSubEventCarrierCommandId(int commandId)
        => commandId >= 0 &&
           commandId < LegacyScriptCodeTestTable.Count &&
           LegacyScriptCodeTestTable[commandId] != 0;

    private bool CanUseLegacyScriptCommandAsAddCandidate(LegacyScenarioCommandNode selected, int commandId)
        => CanUseLegacyScriptCommandAsAddCandidate(LegacyScriptEditorScope.Script, selected, commandId);

    private bool CanUseLegacyScriptCommandAsAddCandidate(LegacyScriptEditorScope scope, LegacyScenarioCommandNode selected, int commandId)
    {
        var document = GetCurrentLegacyScriptDocument(scope);
        if (document == null ||
            !TryFindLegacyCommandList(document, selected, out var list, out _))
        {
            return false;
        }

        if (!IsLegacyScriptSectionTopLevelList(document, list))
        {
            return true;
        }

        return IsLegacyScriptTopLevelCommandId(commandId);
    }

    private static bool IsLegacyScriptTopLevelCommandId(int commandId)
        => commandId is 0x77 or 0x78 or 0x72 ||
           (commandId >= 0 &&
            commandId < LegacyScriptCodeTestTable.Count &&
            LegacyScriptCodeTestTable[commandId] >= 2);

    private bool TryShowLegacyStepDuplicateDialog(
        LegacyScenarioCommandNode command,
        out int count,
        out int slotIndex,
        out int delta)
    {
        count = 0;
        slotIndex = 0;
        delta = 0;
        using var dialog = new Form
        {
            Text = $"步进复制 {command.CommandIdHex} {command.CommandName}",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(360, 142)
        };
        var countInput = new NumericUpDown { Left = 112, Top = 10, Width = 210, Minimum = 1, Maximum = 100, Value = 1 };
        var slotInput = new NumericUpDown { Left = 112, Top = 42, Width = 210, Minimum = 0, Maximum = 50, Value = 0 };
        var deltaInput = new NumericUpDown { Left = 112, Top = 74, Width = 210, Minimum = -999999, Maximum = 999999, Value = 0 };
        var okButton = new Button { Text = "确定", DialogResult = DialogResult.OK, Left = 166, Top = 108, Width = 75 };
        var cancelButton = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Left = 247, Top = 108, Width = 75 };
        dialog.Controls.AddRange(new Control[]
        {
            new Label { Text = "复制数量", Left = 12, Top = 14, AutoSize = true },
            countInput,
            new Label { Text = "要修改的编号", Left = 12, Top = 46, AutoSize = true },
            slotInput,
            new Label { Text = "修改量", Left = 12, Top = 78, AutoSize = true },
            deltaInput,
            okButton,
            cancelButton
        });
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return false;
        }

        count = (int)countInput.Value;
        slotIndex = (int)slotInput.Value;
        delta = (int)deltaInput.Value;
        return true;
    }

    private static bool TryGetLegacyCommandIntDataSlot(
        LegacyScenarioCommandNode command,
        int slotIndex,
        out int value,
        out string reason)
    {
        value = 0;
        var current = 0;
        foreach (var parameter in command.Parameters)
        {
            if (parameter.Kind == LegacyScenarioParameterKind.Text)
            {
                continue;
            }

            if (parameter.Kind == LegacyScenarioParameterKind.VariableArray)
            {
                reason = "步进复制暂不支持变量数组槽。";
                return false;
            }

            if (current == slotIndex)
            {
                value = parameter.IntValue;
                reason = string.Empty;
                return true;
            }

            current++;
        }

        reason = $"该指令没有 int_data[{slotIndex}]。";
        return false;
    }

    private static bool TrySetLegacyCommandIntDataSlot(
        LegacyScenarioCommandNode command,
        int slotIndex,
        int value,
        out string reason)
    {
        var current = 0;
        foreach (var parameter in command.Parameters)
        {
            if (parameter.Kind == LegacyScenarioParameterKind.Text)
            {
                continue;
            }

            if (parameter.Kind == LegacyScenarioParameterKind.VariableArray)
            {
                reason = "步进复制暂不支持变量数组槽。";
                return false;
            }

            if (current == slotIndex)
            {
                parameter.IntValue = value;
                parameter.ByteLength = parameter.Kind == LegacyScenarioParameterKind.Dword32 ? 4 : 2;
                reason = string.Empty;
                return true;
            }

            current++;
        }

        reason = $"该指令没有 int_data[{slotIndex}]。";
        return false;
    }
}
