using CCZModStudio.Models;
using System.Globalization;
using System.Windows.Forms;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private sealed record LegacyScriptViewportSnapshot(
        LegacyScriptEditorScope Scope,
        LegacyScriptNodeIdentity? TopNode,
        LegacyScriptNodeIdentity? SelectedNode,
        IReadOnlyList<LegacyScriptNodeIdentity> ExpandedNodes,
        IReadOnlyDictionary<string, int> FirstDisplayedRows);

    private sealed record LegacyScriptNodeIdentity(
        LegacyScenarioCommandNode? Command,
        LegacyScenarioSection? Section,
        LegacyScenarioScene? Scene,
        string? NodeType,
        int SceneIndex,
        int SectionIndex,
        int CommandIndex,
        int? CommandId,
        string OffsetHex,
        int? TextOffset,
        IReadOnlyList<int> Path);

    private LegacyScriptViewportSnapshot CaptureLegacyScriptViewport(LegacyScriptEditorScope scope)
    {
        var tree = GetLegacyScriptTree(scope);
        var expandedNodes = tree.Nodes
            .Cast<TreeNode>()
            .SelectMany(EnumerateScriptTreeNodes)
            .Where(node => node.IsExpanded)
            .Select(CreateLegacyScriptNodeIdentity)
            .OfType<LegacyScriptNodeIdentity>()
            .ToList();

        return new LegacyScriptViewportSnapshot(
            scope,
            CreateLegacyScriptNodeIdentity(tree.TopNode),
            CreateLegacyScriptNodeIdentity(tree.SelectedNode),
            expandedNodes,
            CaptureLegacyScriptGridScrolls(scope));
    }

    private void RestoreLegacyScriptViewport(LegacyScriptViewportSnapshot? snapshot)
    {
        if (snapshot == null)
        {
            return;
        }

        var tree = GetLegacyScriptTree(snapshot.Scope);
        RestoreLegacyScriptExpandedNodes(tree, snapshot);
        RestoreLegacyScriptTreeTopNode(tree, snapshot.TopNode);
        RestoreLegacyScriptGridScrolls(snapshot);
    }

    private void RestoreLegacyScriptExpandedNodes(TreeView tree, LegacyScriptViewportSnapshot snapshot)
    {
        if (tree.Nodes.Count == 0 || snapshot.ExpandedNodes.Count == 0)
        {
            return;
        }

        tree.BeginUpdate();
        try
        {
            foreach (var node in tree.Nodes.Cast<TreeNode>().SelectMany(EnumerateScriptTreeNodes))
            {
                if (node.Nodes.Count == 0)
                {
                    continue;
                }

                var shouldExpand = snapshot.ExpandedNodes.Any(identity => MatchesLegacyScriptNodeIdentity(node, identity));
                if (shouldExpand)
                {
                    node.Expand();
                }
                else
                {
                    node.Collapse();
                }
            }
        }
        finally
        {
            tree.EndUpdate();
        }
    }

    private void RestoreLegacyScriptTreeTopNode(TreeView tree, LegacyScriptNodeIdentity? identity)
    {
        if (identity == null)
        {
            return;
        }

        var node = FindLegacyScriptTreeNode(tree, identity);
        if (node == null)
        {
            return;
        }

        try
        {
            tree.TopNode = node;
        }
        catch (InvalidOperationException)
        {
            // TreeView can reject hidden/collapsed nodes on some handles; selection restore is still valid.
        }
    }

    private bool TryRestoreLegacyScriptSelectedNode(LegacyScriptViewportSnapshot? snapshot)
    {
        if (snapshot?.SelectedNode == null)
        {
            return false;
        }

        var tree = GetLegacyScriptTree(snapshot.Scope);
        var node = FindLegacyScriptTreeNode(tree, snapshot.SelectedNode);
        if (node == null)
        {
            return false;
        }

        tree.SelectedNode = node;
        return true;
    }

    private TreeNode? FindLegacyScriptTreeNode(TreeView tree, LegacyScriptNodeIdentity identity)
    {
        foreach (TreeNode root in tree.Nodes)
        {
            foreach (var node in EnumerateScriptTreeNodes(root))
            {
                if (MatchesLegacyScriptNodeIdentity(node, identity))
                {
                    return node;
                }
            }
        }

        return null;
    }

    private LegacyScriptNodeIdentity? CreateLegacyScriptNodeIdentity(TreeNode? node)
    {
        if (node == null)
        {
            return null;
        }

        LegacyScenarioCommandNode? command = null;
        LegacyScenarioSection? section = null;
        LegacyScenarioScene? scene = null;
        ScenarioStructureRow? row = null;
        int? textOffset = null;

        switch (node.Tag)
        {
            case LegacyScenarioItemData itemData:
                command = itemData.Command;
                section = itemData.Section;
                scene = itemData.Scene;
                row = itemData.UiRow as ScenarioStructureRow;
                break;
            case ScenarioStructureRow structureRow:
                row = structureRow;
                break;
            case ScenarioTextEntry text:
                textOffset = text.Offset;
                break;
        }

        return new LegacyScriptNodeIdentity(
            command,
            section,
            scene,
            row?.NodeType,
            row?.SceneIndex ?? command?.SceneIndex ?? section?.SceneIndex ?? scene?.SceneIndex ?? 0,
            row?.SectionIndex ?? command?.SectionIndex ?? section?.SectionIndex ?? 0,
            row?.CommandIndex ?? command?.CommandIndex ?? 0,
            row?.CommandId ?? command?.CommandId,
            row?.OffsetHex ?? (command == null ? string.Empty : CCZModStudio.Core.HexDisplayFormatter.FormatOffset(command.FileOffset)),
            textOffset,
            GetLegacyScriptNodePath(node));
    }

    private static IReadOnlyList<int> GetLegacyScriptNodePath(TreeNode node)
    {
        var path = new Stack<int>();
        for (var current = node; current != null; current = current.Parent)
        {
            path.Push(current.Index);
        }

        return path.ToArray();
    }

    private bool MatchesLegacyScriptNodeIdentity(TreeNode node, LegacyScriptNodeIdentity identity)
    {
        if (node.Tag is LegacyScenarioItemData itemData)
        {
            if (identity.Command != null && ReferenceEquals(itemData.Command, identity.Command))
            {
                return true;
            }

            if (identity.Section != null && ReferenceEquals(itemData.Section, identity.Section))
            {
                return true;
            }

            if (identity.Scene != null && ReferenceEquals(itemData.Scene, identity.Scene))
            {
                return true;
            }
        }

        if (identity.TextOffset.HasValue &&
            node.Tag is ScenarioTextEntry text &&
            text.Offset == identity.TextOffset.Value)
        {
            return true;
        }

        if (!string.IsNullOrEmpty(identity.NodeType) &&
            TryGetScriptTreeRow(node, out var row) &&
            IsSameLegacyScriptRowIdentity(row, identity))
        {
            return true;
        }

        return identity.Command == null &&
               identity.Section == null &&
               identity.Scene == null &&
               identity.TextOffset == null &&
               HasSameLegacyScriptPath(node, identity.Path);
    }

    private static bool IsSameLegacyScriptRowIdentity(ScenarioStructureRow row, LegacyScriptNodeIdentity identity)
    {
        if (!string.Equals(row.NodeType, identity.NodeType, StringComparison.Ordinal) ||
            row.SceneIndex != identity.SceneIndex ||
            row.SectionIndex != identity.SectionIndex)
        {
            return false;
        }

        if (identity.CommandId.HasValue)
        {
            return row.CommandIndex == identity.CommandIndex &&
                   row.CommandId == identity.CommandId &&
                   string.Equals(row.OffsetHex, identity.OffsetHex, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static bool HasSameLegacyScriptPath(TreeNode node, IReadOnlyList<int> path)
    {
        var currentPath = GetLegacyScriptNodePath(node);
        return currentPath.Count == path.Count && currentPath.SequenceEqual(path);
    }

    private IReadOnlyDictionary<string, int> CaptureLegacyScriptGridScrolls(LegacyScriptEditorScope scope)
    {
        var result = new Dictionary<string, int>();
        switch (scope)
        {
            case LegacyScriptEditorScope.Script:
                CaptureGridFirstDisplayedRow(result, "ScriptCommand", _scriptCommandGrid);
                CaptureGridFirstDisplayedRow(result, "ScriptParameter", _scriptParameterGrid);
                CaptureGridFirstDisplayedRow(result, "ScriptText", _scriptTextGrid);
                CaptureGridFirstDisplayedRow(result, "ScriptSearch", _scriptSearchResultGrid);
                break;
            case LegacyScriptEditorScope.Battlefield:
                CaptureGridFirstDisplayedRow(result, "BattlefieldParameter", _battlefieldScriptParameterGrid);
                CaptureGridFirstDisplayedRow(result, "BattlefieldSearch", _battlefieldScriptSearchResultGrid);
                break;
            case LegacyScriptEditorScope.RScene:
                CaptureGridFirstDisplayedRow(result, "RSceneCommand", _rSceneCommandGrid);
                CaptureGridFirstDisplayedRow(result, "RSceneSearch", _rSceneScriptSearchResultGrid);
                break;
        }

        return result;
    }

    private void RestoreLegacyScriptGridScrolls(LegacyScriptViewportSnapshot snapshot)
    {
        switch (snapshot.Scope)
        {
            case LegacyScriptEditorScope.Script:
                RestoreGridFirstDisplayedRow(snapshot.FirstDisplayedRows, "ScriptCommand", _scriptCommandGrid);
                RestoreGridFirstDisplayedRow(snapshot.FirstDisplayedRows, "ScriptParameter", _scriptParameterGrid);
                RestoreGridFirstDisplayedRow(snapshot.FirstDisplayedRows, "ScriptText", _scriptTextGrid);
                RestoreGridFirstDisplayedRow(snapshot.FirstDisplayedRows, "ScriptSearch", _scriptSearchResultGrid);
                break;
            case LegacyScriptEditorScope.Battlefield:
                RestoreGridFirstDisplayedRow(snapshot.FirstDisplayedRows, "BattlefieldParameter", _battlefieldScriptParameterGrid);
                RestoreGridFirstDisplayedRow(snapshot.FirstDisplayedRows, "BattlefieldSearch", _battlefieldScriptSearchResultGrid);
                break;
            case LegacyScriptEditorScope.RScene:
                RestoreGridFirstDisplayedRow(snapshot.FirstDisplayedRows, "RSceneCommand", _rSceneCommandGrid);
                RestoreGridFirstDisplayedRow(snapshot.FirstDisplayedRows, "RSceneSearch", _rSceneScriptSearchResultGrid);
                break;
        }
    }

    private static void CaptureGridFirstDisplayedRow(Dictionary<string, int> result, string key, DataGridView grid)
    {
        try
        {
            if (grid.Rows.Count > 0)
            {
                result[key] = grid.FirstDisplayedScrollingRowIndex;
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void RestoreGridFirstDisplayedRow(IReadOnlyDictionary<string, int> values, string key, DataGridView grid)
    {
        if (!values.TryGetValue(key, out var rowIndex) || grid.Rows.Count == 0)
        {
            return;
        }

        var target = Math.Clamp(rowIndex, 0, grid.Rows.Count - 1);
        while (target < grid.Rows.Count && !grid.Rows[target].Visible)
        {
            target++;
        }

        if (target >= grid.Rows.Count)
        {
            return;
        }

        try
        {
            grid.FirstDisplayedScrollingRowIndex = target;
        }
        catch (InvalidOperationException)
        {
        }
    }
}
