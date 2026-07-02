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

                var tree = GetPrivateField<TreeView>(form, "_battlefieldScriptTree");
                var infoBox = GetPrivateField<TextBox>(form, "_battlefieldInfoBox");
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
                    InvokeBattlefieldScriptDoubleClick(form, friendNode);
                    Application.DoEvents();
                    AssertEqual(0x46, intercepted.LastOrDefault(), "battlefield tree double-click reaches command 46 edit path");
                    AssertTrue(ReferenceEquals(tree.SelectedNode, friendNode), "battlefield tree double-click selects command 46 node");
                    AssertEqual("Script", form.BattlefieldRightPreviewModeForSmoke, "battlefield script tree double-click switches right preview to script mode");
                    AssertTrue(infoBox.Text.Contains("S 剧本指令预览", StringComparison.Ordinal), "battlefield script preview labels command preview");
                    AssertTrue(infoBox.Text.Contains("0x46", StringComparison.Ordinal) || infoBox.Text.Contains("46 ", StringComparison.Ordinal), "battlefield script preview contains command 46");
                    AssertTrue(infoBox.Text.Contains("Dialog_70", StringComparison.Ordinal), "battlefield script preview shows Dialog_70 for command 46");
                    AssertTrue(!infoBox.Text.Contains("当前地图单位", StringComparison.Ordinal), "battlefield script preview does not show console unit title");
                    AssertTrue(!infoBox.Text.Contains("当前出场/坐标候选", StringComparison.Ordinal), "battlefield script preview does not show candidate console title");

                    InvokeBattlefieldScriptDoubleClick(form, enemyNode);
                    Application.DoEvents();
                    AssertEqual(0x47, intercepted.LastOrDefault(), "battlefield tree double-click reaches command 47 edit path");
                    AssertTrue(ReferenceEquals(tree.SelectedNode, enemyNode), "battlefield tree double-click selects command 47 node");
                    AssertEqual("Script", form.BattlefieldRightPreviewModeForSmoke, "battlefield script tree double-click keeps script preview mode for command 47");
                    AssertTrue(infoBox.Text.Contains("敌军出场设定", StringComparison.Ordinal), "battlefield script preview contains command 47 name");
                    AssertTrue(infoBox.Text.Contains("Dialog_70", StringComparison.Ordinal), "battlefield script preview shows Dialog_70 for command 47");

                    var beforeNonCommandCount = intercepted.Count;
                    InvokeBattlefieldScriptDoubleClick(form, sectionNode);
                    Application.DoEvents();
                    AssertEqual(beforeNonCommandCount, intercepted.Count, "battlefield tree double-click on non-command node does not edit");
                    AssertTrue(ReferenceEquals(tree.SelectedNode, sectionNode), "battlefield tree double-click selects non-command node");

                    form.ShowBattlefieldConsolePreviewForSmoke("当前地图单位：\r\n12 preview 坐标=(8,10) 阵营=友军 AI=主动");
                    AssertEqual("Console", form.BattlefieldRightPreviewModeForSmoke, "battlefield candidate/map selection can switch right preview to console mode");
                    AssertTrue(infoBox.Text.Contains("当前地图单位", StringComparison.Ordinal), "battlefield console preview contains unit title");

                    InvokeBattlefieldScriptDoubleClick(form, friendNode);
                    Application.DoEvents();
                    AssertEqual("Script", form.BattlefieldRightPreviewModeForSmoke, "battlefield script tree click switches back from console preview to script preview");
                    AssertTrue(infoBox.Text.Contains("友军出场设定", StringComparison.Ordinal), "battlefield script preview restores command 46 name");
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
