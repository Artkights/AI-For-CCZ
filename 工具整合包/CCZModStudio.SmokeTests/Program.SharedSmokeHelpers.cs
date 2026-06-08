using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using CCZModStudio;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

internal partial class Program
{
    static void AssertSMapping(int sImageId, int? jobId, int factionSlot, params int[] expected)
    {
        var mapping = CharacterImageResourceService.ResolveSUnitImageMapping(sImageId, jobId, factionSlot);
        var actual = mapping.ImageNumbers.ToArray();
        if (!actual.SequenceEqual(expected))
        {
            throw new InvalidOperationException($"S 映射不符合预期：S={sImageId}, job={jobId?.ToString(CultureInfo.InvariantCulture) ?? "null"}, faction={factionSlot}, expected={string.Join("/", expected)}, actual={string.Join("/", actual)}");
        }
    }
    
    static LegacyScenarioCommandNode? FindLegacyBattlefieldCommand(LegacyScenarioDocument document, BattlefieldUnitCandidate candidate)
    {
        if (!BattlefieldEditorService.TryParseScriptCommandLocator(candidate, out var scene, out var section, out var commandIndex, out var offsetHex, out var commandIdHex))
        {
            return null;
        }
    
        return document.EnumerateCommands().FirstOrDefault(command =>
            command.SceneIndex == scene &&
            command.SectionIndex == section &&
            command.CommandIndex == commandIndex &&
            (string.IsNullOrWhiteSpace(offsetHex) || string.Equals("0x" + command.FileOffset.ToString("X6", CultureInfo.InvariantCulture), offsetHex, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(commandIdHex) || string.Equals(command.CommandIdHex, commandIdHex, StringComparison.OrdinalIgnoreCase)));
    }
    
    static (int SectionTextGroupCount, int SceneFallbackGroupCount, int UnassignedGroupCount, int AttachedTextNodeCount) BuildScriptEditorTreeSummary(
        ScenarioStructureProbeResult structure,
        IReadOnlyList<ScenarioTextEntry> texts)
    {
        var buildScriptTree = typeof(MainForm).GetMethod("BuildScriptTree", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingMethodException("MainForm.BuildScriptTree");
        var scriptTreeField = typeof(MainForm).GetField("_scriptTree", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_scriptTree");
    
        buildScriptTree.Invoke(smokeForm, new object[] { structure, texts });
        var tree = scriptTreeField.GetValue(smokeForm) as TreeView
            ?? throw new InvalidOperationException("Unable to read script editor tree control.");
    
        var sectionTextGroups = 0;
        var sceneFallbackGroups = 0;
        var unassignedGroups = 0;
        var attachedTextNodeCount = 0;
    
        foreach (TreeNode root in tree.Nodes)
        {
            foreach (var node in EnumerateTreeNodes(root))
            {
                if (node.Text.StartsWith("Section文本线索", StringComparison.Ordinal))
                {
                    sectionTextGroups++;
                    attachedTextNodeCount += node.Nodes.Count;
                }
                else if (node.Text.StartsWith("Scene补充文本线索", StringComparison.Ordinal))
                {
                    sceneFallbackGroups++;
                    attachedTextNodeCount += node.Nodes.Count;
                }
                else if (node.Text.StartsWith("未归属文本线索", StringComparison.Ordinal))
                {
                    unassignedGroups++;
                    attachedTextNodeCount += node.Nodes.Count;
                }
            }
        }
    
        return (sectionTextGroups, sceneFallbackGroups, unassignedGroups, attachedTextNodeCount);
    }
    
    static IEnumerable<TreeNode> EnumerateTreeNodes(TreeNode root)
    {
        yield return root;
        foreach (TreeNode child in root.Nodes)
        {
            foreach (var nested in EnumerateTreeNodes(child))
            {
                yield return nested;
            }
        }
    }
    
    static string? FindFirstJpegMap(string mapRoot)
    {
        if (!Directory.Exists(mapRoot)) return null;
        return Directory
            .EnumerateFiles(mapRoot, "M*.jpg", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(mapRoot, "M*.jpeg", SearchOption.TopDirectoryOnly))
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault();
    }
    
    static DataRow FindSmokeRowById(DataTable table, int id)
    {
        foreach (DataRow row in table.Rows)
        {
            if (Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) == id) return row;
        }
        throw new InvalidOperationException($"烟测表 {table.TableName} 没有找到 ID={id}。");
    }
    
    static void VerifyJobEffectNamesAreVisible(CczProject project, IReadOnlyList<HexTableDefinition> tables, HexTableReader reader)
    {
        var nameTable = tables.Single(t => t.TableName == "6.5-7 兵种特效");
        var assignmentTable = tables.Single(t => t.TableName == "6.5-7-2 兵种特效分配");
        var personalTable = tables.Single(t => t.TableName == "6.5-7-3 人物专属、套装专属");
    
        if (!JobEffectNameReader.IsJobEffectNameTable(nameTable))
        {
            throw new InvalidOperationException("兵种特效名称表未识别为 Bytes=0 专用名称区。");
        }
    
        var nameRead = reader.Read(project, nameTable, tables);
        var assignmentRead = reader.Read(project, assignmentTable, tables);
        var personalRead = reader.Read(project, personalTable, tables);
        if (!HasVisibleEffectNameInTable(nameRead.Data) ||
            !HasVisibleEffectNameInTable(assignmentRead.Data) ||
            !HasVisibleEffectNameInTable(personalRead.Data))
        {
            throw new InvalidOperationException("兵种特效/人物专属/套装专属读取后名称列仍为空或只剩 #编号。");
        }
    
        var service = new EffectPackageService();
        if (!service.ListEffects(project, tables, "job", null, 20).Any(x => IsVisibleEffectName(x.Name)) ||
            !service.ListEffects(project, tables, "personal", null, 20).Any(x => IsVisibleEffectName(x.Name)))
        {
            throw new InvalidOperationException("EffectPackageService job/personal 列表未解析出兵种特效名称。");
        }
    }
    
    static bool HasVisibleEffectNameInTable(DataTable table)
        => table.Columns.Contains("名称") &&
           table.Rows.Cast<DataRow>().Any(row => IsVisibleEffectName(Convert.ToString(row["名称"], CultureInfo.InvariantCulture)));
    
    static bool IsVisibleEffectName(string? value)
        => !string.IsNullOrWhiteSpace(value) && !value.TrimStart().StartsWith("#", StringComparison.Ordinal);
    
    static void VerifyEffectApplyResult(EffectPackageApplyResult result, string expectedDomain, int minChanges)
    {
        if (!string.Equals(result.Domain, expectedDomain, StringComparison.OrdinalIgnoreCase) ||
            result.ChangeCount < minChanges ||
            string.IsNullOrWhiteSpace(result.ManifestPath) ||
            !File.Exists(result.ManifestPath) ||
            result.BackupPaths.Count == 0 ||
            result.BackupPaths.Any(path => string.IsNullOrWhiteSpace(path) || !File.Exists(path)) ||
            result.ReportPaths.Any(path => string.IsNullOrWhiteSpace(path) || !File.Exists(path)))
        {
            throw new InvalidOperationException($"EffectPackage apply result validation failed: domain={result.Domain}, changes={result.ChangeCount}, manifest={result.ManifestPath}, backups={result.BackupPaths.Count}, reports={result.ReportPaths.Count}");
        }
    }
}
