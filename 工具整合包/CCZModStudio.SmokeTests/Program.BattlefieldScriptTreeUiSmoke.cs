using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Reflection;
using System.Windows.Forms;

internal partial class Program
{
    static void RunBattlefieldScriptTreeUiSmoke()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                using var form = new MainForm();
                form.Show();
                Application.DoEvents();
                var smokeProjectRoot = Path.Combine(Path.GetTempPath(), "CCZModStudioBattlefieldUiSmoke");
                Directory.CreateDirectory(smokeProjectRoot);
                SetPrivateField(form, "_project", new CczProject
                {
                    WorkspaceRoot = smokeProjectRoot,
                    GameRoot = smokeProjectRoot,
                    HexTableXmlPath = string.Empty
                });

                var tree = GetPrivateField<TreeView>(form, "_battlefieldScriptTree");
                var infoBox = GetPrivateField<TextBox>(form, "_battlefieldInfoBox");
                var scriptDetailBox = GetPrivateField<TextBox>(form, "_battlefieldScriptDetailBox");
                var scriptPreviewPanel = GetPrivateField<Panel>(form, "_battlefieldScriptPreviewPanel");
                var consolePreviewPanel = GetPrivateField<Panel>(form, "_battlefieldConsolePreviewPanel");
                SelectVisibleTabChain(scriptPreviewPanel);
                Application.DoEvents();
                tree.Nodes.Clear();

                var friendNode = AddBattlefieldScriptSmokeNode(form, tree, commandId: 0x46, commandName: "友军出场设定", commandIndex: 46);
                var enemyNode = AddBattlefieldScriptSmokeNode(form, tree, commandId: 0x47, commandName: "敌军出场设定", commandIndex: 47, tagAsRow: true);
                var sectionNode = new TreeNode("Section 1")
                {
                    Tag = new ScenarioStructureRow
                    {
                        NodeType = "Section候选",
                        SceneIndex = 1,
                        SectionIndex = 1,
                        CommandName = "Section 1"
                    }
                };
                tree.Nodes.Add(sectionNode);
                var intercepted = new List<int>();

                MainForm.BattlefieldScriptCommandEditInterceptForSmoke = command => intercepted.Add(command.CommandId);
                try
                {
                    var commitAttemptsBeforeDoubleClick = GetPrivateField<int>(form, "_battlefieldConsoleCommitAttemptCountForSmoke");
                    InvokeBattlefieldScriptDoubleClick(form, friendNode);
                    Application.DoEvents();
                    AssertEqual(commitAttemptsBeforeDoubleClick + 1, GetPrivateField<int>(form, "_battlefieldConsoleCommitAttemptCountForSmoke"), "battlefield tree direct double-click invokes the commit gate once");
                    AssertEqual(0x46, intercepted.LastOrDefault(), "battlefield tree double-click reaches command 46 edit path");
                    AssertTrue(ReferenceEquals(tree.SelectedNode, friendNode), "battlefield tree double-click selects command 46 node");
                    AssertEqual("Script", form.BattlefieldRightPreviewModeForSmoke, "battlefield script tree double-click switches right preview to script mode");
                    AssertTrue(scriptPreviewPanel.Visible, "battlefield script preview panel is visible");
                    AssertTrue(!consolePreviewPanel.Visible, "battlefield console preview panel is hidden for script selection");
                    AssertTrue(!infoBox.Visible || infoBox.Parent == null, "battlefield legacy text preview box is not visible in right preview");
                    AssertTrue(scriptDetailBox.Text.Contains("S 剧本指令预览", StringComparison.Ordinal), "battlefield script preview labels command preview");
                    AssertTrue(scriptDetailBox.Text.Contains("0x46", StringComparison.Ordinal) || scriptDetailBox.Text.Contains("46 ", StringComparison.Ordinal), "battlefield script preview contains command 46");
                    AssertTrue(scriptDetailBox.Text.Contains("Dialog_70", StringComparison.Ordinal), "battlefield script preview shows Dialog_70 for command 46");
                    AssertTrue(!scriptDetailBox.Text.Contains("当前地图单位", StringComparison.Ordinal), "battlefield script preview does not show console unit title");
                    AssertTrue(!scriptDetailBox.Text.Contains("当前出场/坐标候选", StringComparison.Ordinal), "battlefield script preview does not show candidate console title");
                    AssertTrue(!scriptDetailBox.Text.Contains("说明：点击", StringComparison.Ordinal), "battlefield script preview omits instructional prose");

                    SetPrivateField(form, "_battlefieldScriptSelectionCommitSatisfiedNode", enemyNode);
                    SetPrivateField(form, "_battlefieldScriptSelectionCommitUtc", DateTime.UtcNow);
                    tree.SelectedNode = enemyNode;
                    var commitAttemptsAfterSelection = GetPrivateField<int>(form, "_battlefieldConsoleCommitAttemptCountForSmoke");
                    InvokeBattlefieldScriptDoubleClick(form, enemyNode);
                    Application.DoEvents();
                    AssertEqual(commitAttemptsAfterSelection, GetPrivateField<int>(form, "_battlefieldConsoleCommitAttemptCountForSmoke"), "battlefield tree selection plus double-click does not invoke the commit gate twice");
                    AssertEqual(0x47, intercepted.LastOrDefault(), "battlefield tree double-click reaches command 47 edit path");
                    AssertTrue(ReferenceEquals(tree.SelectedNode, enemyNode), "battlefield tree double-click selects command 47 node");
                    AssertEqual("Script", form.BattlefieldRightPreviewModeForSmoke, "battlefield script tree double-click keeps script preview mode for command 47");
                    AssertTrue(scriptDetailBox.Text.Contains("敌军出场设定", StringComparison.Ordinal), "battlefield script preview contains command 47 name");
                    AssertTrue(scriptDetailBox.Text.Contains("Dialog_70", StringComparison.Ordinal), "battlefield script preview shows Dialog_70 for command 47");

                    var beforeNonCommandCount = intercepted.Count;
                    InvokeBattlefieldScriptDoubleClick(form, sectionNode);
                    Application.DoEvents();
                    AssertEqual(beforeNonCommandCount, intercepted.Count, "battlefield tree double-click on non-command node does not edit");
                    AssertTrue(ReferenceEquals(tree.SelectedNode, sectionNode), "battlefield tree double-click selects non-command node");

                    form.ShowBattlefieldConsolePreviewForSmoke("当前地图单位：\r\n12 preview 坐标=(8,10) 阵营=友军 AI=主动");
                    AssertEqual("Console", form.BattlefieldRightPreviewModeForSmoke, "battlefield candidate/map selection can switch right preview to console mode");
                    AssertTrue(consolePreviewPanel.Visible, "battlefield console preview panel is visible");
                    AssertTrue(!scriptPreviewPanel.Visible, "battlefield script preview panel is hidden for console selection");
                    AssertTrue(infoBox.Text.Contains("当前地图单位", StringComparison.Ordinal), "battlefield console keeps unit detail in its hidden state text");

                    InvokeBattlefieldScriptDoubleClick(form, friendNode);
                    Application.DoEvents();
                    AssertEqual("Script", form.BattlefieldRightPreviewModeForSmoke, "battlefield script tree click switches back from console preview to script preview");
                    AssertTrue(scriptDetailBox.Text.Contains("友军出场设定", StringComparison.Ordinal), "battlefield script preview restores command 46 name");

                    form.ShowBattlefieldPaletteConsolePreviewForSmoke(new BattlefieldUnitPaletteItem
                    {
                        PersonId = 0,
                        Name = "孙策",
                        JobId = 1,
                        JobName = "君主",
                        RImageId = 10,
                        SImageId = 20
                    });
                    Application.DoEvents();
                    AssertEqual("Console", form.BattlefieldRightPreviewModeForSmoke, "battlefield palette selection switches right preview to console mode");
                    AssertTrue(infoBox.Text.Contains("角色默认预览", StringComparison.Ordinal), "battlefield palette console keeps readonly role detail");
                    AssertTrue(infoBox.Text.Contains("孙策", StringComparison.Ordinal), "battlefield palette console detail contains selected role");
                    AssertTrue(GetPrivateField<object?>(form, "_selectedBattlefieldPlacedUnit") == null, "battlefield palette console does not bind a placed unit");
                    AssertTrue(!GetPrivateField<ComboBox>(form, "_battlefieldConsoleWeaponCombo").Enabled, "battlefield palette console disables equipment write controls");
                    AssertTrue(!GetPrivateField<NumericUpDown>(form, "_battlefieldLevelOffsetInput").Enabled, "battlefield palette console disables placement write controls");

                    InvokeBattlefieldScriptDoubleClick(form, friendNode);
                    Application.DoEvents();
                    AssertEqual("Script", form.BattlefieldRightPreviewModeForSmoke, "battlefield batch smoke starts from script preview mode");

                    var batchUnits = new List<BattlefieldPlacedUnit>
                    {
                        new()
                        {
                            TargetKey = "BatchSmoke#Friend",
                            PersonId = 12,
                            Name = "友军甲",
                            Faction = "友军",
                            AiMode = "主动",
                            Hidden = false,
                            GridX = 8,
                            GridY = 10
                        },
                        new()
                        {
                            TargetKey = "BatchSmoke#Enemy",
                            PersonId = 60,
                            Name = "敌军乙",
                            Faction = "敌军",
                            AiMode = "坚守",
                            Hidden = true,
                            GridX = 9,
                            GridY = 10
                        }
                    };
                    SetPrivateField(form, "_currentBattlefieldDocument", new BattlefieldEditorDocument
                    {
                        Scenario = new ScenarioFileInfo { FileName = "S_BATCH_SMOKE.eex" }
                    });
                    GetPrivateField<List<BattlefieldPlacedUnit>>(form, "_battlefieldPlacedUnits").AddRange(batchUnits);
                    InvokePrivate(form, "SelectBattlefieldBatchUnits", batchUnits);
                    Application.DoEvents();

                    AssertEqual("Console", form.BattlefieldRightPreviewModeForSmoke, "battlefield multi-selection switches right preview to console mode");
                    AssertTrue(consolePreviewPanel.Visible, "battlefield console preview is visible for multi-selection");
                    AssertTrue(!scriptPreviewPanel.Visible, "battlefield script preview is hidden for multi-selection");
                    AssertTrue(infoBox.Text.Contains("批量编辑：2 个单位", StringComparison.Ordinal), "battlefield multi-selection keeps batch detail");
                    AssertEqual(2, GetPrivateField<HashSet<string>>(form, "_batchEditingBattlefieldTargetKeys").Count, "battlefield multi-selection keeps both target keys");
                    AssertTrue(!GetPrivateField<RadioButton>(form, "_battlefieldFactionAllyRadio").Checked, "mixed batch faction does not select ally");
                    AssertTrue(!GetPrivateField<RadioButton>(form, "_battlefieldFactionFriendRadio").Checked, "mixed batch faction does not select friend");
                    AssertTrue(!GetPrivateField<RadioButton>(form, "_battlefieldFactionEnemyRadio").Checked, "mixed batch faction does not select enemy");
                    AssertEqual(CheckState.Indeterminate, GetPrivateField<CheckBox>(form, "_battlefieldHiddenCheckBox").CheckState, "mixed batch hidden state is indeterminate");
                    AssertEqual("多值", GetPrivateField<ComboBox>(form, "_battlefieldAiModeCombo").SelectedItem?.ToString(), "mixed batch AI displays multiple values");
                }
                finally
                {
                    MainForm.BattlefieldScriptCommandEditInterceptForSmoke = null;
                }

                form.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
        {
            throw new InvalidOperationException("Battlefield script tree UI smoke failed.", failure);
        }

        Console.WriteLine("BATTLEFIELD_SCRIPT_TREE_UI_SMOKE_OK");
    }

    private static void SelectVisibleTabChain(Control control)
    {
        var pages = new Stack<TabPage>();
        for (Control? current = control; current != null; current = current.Parent)
        {
            if (current is TabPage page)
            {
                pages.Push(page);
            }
        }

        while (pages.Count > 0)
        {
            var page = pages.Pop();
            if (page.Parent is TabControl tabs)
            {
                tabs.SelectedTab = page;
            }
        }
    }

    private static TreeNode AddBattlefieldScriptSmokeNode(
        MainForm form,
        TreeView tree,
        int commandId,
        string commandName,
        int commandIndex,
        bool tagAsRow = false)
    {
        var command = new LegacyScenarioCommandNode
        {
            SceneIndex = 1,
            SectionIndex = 1,
            CommandIndex = commandIndex,
            CommandOrdinal = commandIndex,
            CommandId = commandId,
            CommandName = commandName,
            FileOffset = 0x2000 + commandIndex
        };
        var definition = BattlefieldDeploymentRecordDefinition.FromCommandId(commandId)
            ?? throw new InvalidOperationException("Missing deployment definition for smoke command " + commandId.ToString("X2"));
        for (var i = 0; i < definition.GroupSize * definition.RecordCount; i++)
        {
            command.Parameters.Add(new LegacyScenarioCommandParameter
            {
                Index = i,
                Kind = LegacyScenarioParameterKind.Word16,
                IntValue = 0
            });
        }

        var row = new ScenarioStructureRow
        {
            NodeType = "Command候选",
            SceneIndex = command.SceneIndex,
            SectionIndex = command.SectionIndex,
            CommandIndex = command.CommandIndex,
            OffsetHex = HexDisplayFormatter.FormatOffset(command.FileOffset),
            CommandId = command.CommandId,
            CommandIdHex = command.CommandIdHex,
            CommandName = command.CommandName
        };
        var itemData = new LegacyScenarioItemData
        {
            Id = command.CommandId,
            Ord = command.CommandOrdinal,
            Command = command,
            UiRow = row
        };
        RegisterBattlefieldSmokeItemData(form, command, row);
        var node = new TreeNode(command.CommandIdHex + ":" + commandName)
        {
            Tag = tagAsRow ? row : itemData
        };
        tree.Nodes.Add(node);
        return node;
    }

    private static void RegisterBattlefieldSmokeItemData(MainForm form, LegacyScenarioCommandNode command, ScenarioStructureRow row)
    {
        var scopeType = typeof(MainForm).GetNestedType("LegacyScriptEditorScope", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing MainForm.LegacyScriptEditorScope.");
        var battlefieldScope = Enum.Parse(scopeType, "Battlefield");
        var method = typeof(MainForm).GetMethod("GetLegacyEditorItemData", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing MainForm.GetLegacyEditorItemData.");
        method.Invoke(form, new[] { battlefieldScope, command, row });
    }

    private static void InvokeBattlefieldScriptDoubleClick(MainForm form, TreeNode node)
    {
        var method = typeof(MainForm).GetMethod("HandleBattlefieldScriptTreeNodeMouseDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing MainForm.HandleBattlefieldScriptTreeNodeMouseDoubleClick.");
        var args = new TreeNodeMouseClickEventArgs(node, MouseButtons.Left, clicks: 2, x: 0, y: 0);
        method.Invoke(form, new object[] { args });
    }
}
