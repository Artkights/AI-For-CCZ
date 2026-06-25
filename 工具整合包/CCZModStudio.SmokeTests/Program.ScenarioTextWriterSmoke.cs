using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

internal partial class Program
{
    static void RunScenarioTextWriterSmoke(CczProject project)
    {
        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "ScenarioTextWriterSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        var rsRoot = Path.Combine(smokeRoot, "RS");
        Directory.CreateDirectory(rsRoot);
        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"), "Scenario text writer smoke.");

        var scenarioPath = Path.Combine(rsRoot, "S_99.eex");
        var bytes = EncodingService.Gbk.GetBytes("&郭昕\0敌军退后\0");
        File.WriteAllBytes(scenarioPath, bytes);

        var testProject = new CczProject
        {
            WorkspaceRoot = project.WorkspaceRoot,
            GameRoot = smokeRoot,
            HexTableXmlPath = project.HexTableXmlPath,
            SceneDictionaryPath = project.SceneDictionaryPath,
            SceneEditorDirectory = project.SceneEditorDirectory,
            ImageAssignerDirectory = project.ImageAssignerDirectory,
            ImageAssignerSystemIniPath = project.ImageAssignerSystemIniPath,
            MaterialLibraryRoot = project.MaterialLibraryRoot,
            PatchConfigRoot = project.PatchConfigRoot,
            PathDiagnostics = project.PathDiagnostics
        };

        var reader = new ScenarioTextReader();
        var entries = reader.Read(scenarioPath, maxItems: 8).ToList();
        var prefixed = entries.First(entry => entry.Text == "郭昕");
        prefixed.Text = "李晟";
        new ScenarioTextWriter().SaveInPlace(testProject, Path.Combine("RS", "S_99.eex"), new[] { prefixed }, "scenario text writer smoke");

        var afterPrefixBytes = File.ReadAllBytes(scenarioPath);
        var afterPrefixText = EncodingService.Gbk.GetString(afterPrefixBytes.AsSpan(0, prefixed.ByteLength));
        if (!afterPrefixText.StartsWith("&李晟", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("ScenarioTextWriter did not preserve the raw speaker prefix.");
        }

        if (afterPrefixBytes[prefixed.Offset + prefixed.ByteLength] != 0x00)
        {
            throw new InvalidOperationException("ScenarioTextWriter moved or overwrote the original null terminator.");
        }

        var second = reader.Read(scenarioPath, maxItems: 8).First(entry => entry.Text == "敌军退后");
        var originalTerminator = second.Offset + second.ByteLength;
        second.Text = "敌退";
        new ScenarioTextWriter().SaveInPlace(testProject, Path.Combine("RS", "S_99.eex"), new[] { second }, "scenario text writer smoke");

        var afterShortBytes = File.ReadAllBytes(scenarioPath);
        if (afterShortBytes[second.Offset + EncodingService.GetGbkByteCount("敌退")] != 0x20)
        {
            throw new InvalidOperationException("ScenarioTextWriter did not pad a shortened text entry with 0x20.");
        }

        if (afterShortBytes[originalTerminator] != 0x00)
        {
            throw new InvalidOperationException("ScenarioTextWriter wrote an early null before the original text terminator.");
        }

        second.Text = "这是一条肯定超过原始容量的超长文本";
        try
        {
            new ScenarioTextWriter().SaveInPlace(testProject, Path.Combine("RS", "S_99.eex"), new[] { second }, "scenario text writer smoke");
            throw new InvalidOperationException("ScenarioTextWriter accepted an over-capacity GBK text update.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("exceeds original GBK capacity", StringComparison.Ordinal))
        {
        }

        Console.WriteLine($"SCENARIO_TEXT_WRITER_SMOKE_OK root={smokeRoot}");
    }
}
