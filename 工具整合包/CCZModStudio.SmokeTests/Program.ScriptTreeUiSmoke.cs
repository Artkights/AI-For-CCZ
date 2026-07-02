using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Reflection;
using System.Windows.Forms;

internal partial class Program
{
    static void RunScriptTreeUiSmoke()
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

                var tree = GetPrivateField<TreeView>(form, "_scriptTree");
                tree.Nodes.Clear();

                var friendNode = AddScriptTreeSmokeCommandNode(tree, commandId: 0x46, commandName: "友军出场设定", commandIndex: 46);
                var enemyNode = AddScriptTreeSmokeCommandNode(tree, commandId: 0x47, commandName: "敌军出场设定", commandIndex: 47);
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
                MainForm.ScriptCommandEditInterceptForSmoke = command => intercepted.Add(command.CommandId);
                try
                {
                    InvokeScriptTreeDoubleClick(form, friendNode);
                    Application.DoEvents();
                    AssertEqual(0x46, intercepted.LastOrDefault(), "script tree double-click reaches command 46 edit path");
                    AssertTrue(ReferenceEquals(tree.SelectedNode, friendNode), "script tree double-click selects command 46 node");

                    InvokeScriptTreeDoubleClick(form, enemyNode);
                    Application.DoEvents();
                    AssertEqual(0x47, intercepted.LastOrDefault(), "script tree double-click reaches command 47 edit path");
                    AssertTrue(ReferenceEquals(tree.SelectedNode, enemyNode), "script tree double-click selects command 47 node");

                    var beforeCount = intercepted.Count;
                    InvokeScriptTreeDoubleClick(form, sectionNode);
                    Application.DoEvents();
                    AssertEqual(beforeCount, intercepted.Count, "script tree double-click on non-command node does not edit");
                    AssertTrue(ReferenceEquals(tree.SelectedNode, sectionNode), "script tree double-click selects non-command node");
                }
                finally
                {
                    MainForm.ScriptCommandEditInterceptForSmoke = null;
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
            throw new InvalidOperationException("Script tree UI smoke failed.", failure);
        }

        Console.WriteLine("SCRIPT_TREE_UI_SMOKE_OK");
    }

    private static TreeNode AddScriptTreeSmokeCommandNode(
        TreeView tree,
        int commandId,
        string commandName,
        int commandIndex)
    {
        var command = new LegacyScenarioCommandNode
        {
            SceneIndex = 1,
            SectionIndex = 1,
            CommandIndex = commandIndex,
            CommandOrdinal = commandIndex,
            CommandId = commandId,
            CommandName = commandName,
            FileOffset = 0x3000 + commandIndex
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
        var node = new TreeNode(command.CommandIdHex + ":" + commandName)
        {
            Tag = itemData
        };
        tree.Nodes.Add(node);
        return node;
    }

    private static void InvokeScriptTreeDoubleClick(MainForm form, TreeNode node)
    {
        var method = typeof(MainForm).GetMethod("HandleScriptTreeNodeMouseDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing MainForm.HandleScriptTreeNodeMouseDoubleClick.");
        var args = new TreeNodeMouseClickEventArgs(node, MouseButtons.Left, clicks: 2, x: 0, y: 0);
        method.Invoke(form, new object[] { args });
    }
}
