using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

internal partial class Program
{
    static void RunScriptVariableUsageSmoke()
    {
        var document = new LegacyScenarioDocument
        {
            FilePath = Path.Combine("RS", "R_99.eex")
        };
        var scene = new LegacyScenarioScene
        {
            SceneIndex = 1,
            FileOffset = 0x20
        };
        var section = new LegacyScenarioSection
        {
            SceneIndex = 1,
            SectionIndex = 1,
            FileOffset = 0x24,
            LengthPrefixOffset = 0x22,
            DeclaredLength = 0x80
        };
        scene.Sections.Add(section);
        document.Scenes.Add(scene);

        section.Commands.Add(CreateCommand(1, 0, 0x05, "变量测试", 0x30,
            CreateArrayParameter(0, 0x34, 101, 102),
            CreateArrayParameter(1, 0x3A, 201)));
        section.Commands.Add(CreateCommand(1, 1, 0x0B, "变量赋值", 0x44,
            CreateWordParameter(0, 0x48, 301),
            CreateWordParameter(1, 0x4C, 1)));
        section.Commands.Add(CreateCommand(1, 2, 0x77, "变量运算", 0x50,
            CreateWordParameter(0, 0x54, 2),
            CreateWordParameter(1, 0x58, 3000),
            CreateWordParameter(2, 0x5C, 2),
            CreateWordParameter(3, 0x60, 0),
            CreateWordParameter(4, 0x64, 99)));
        section.Commands.Add(CreateCommand(1, 3, 0x77, "变量运算", 0x68,
            CreateWordParameter(0, 0x6C, 1),
            CreateWordParameter(1, 0x70, 4096),
            CreateWordParameter(2, 0x74, 0),
            CreateWordParameter(3, 0x78, 4),
            CreateWordParameter(4, 0x7C, 4000)));
        section.Commands.Add(CreateCommand(1, 4, 0x78, "整型变量赋值", 0x80,
            CreateWordParameter(0, 0x84, 5000),
            CreateWordParameter(1, 0x88, 1),
            CreateWordParameter(2, 0x8C, 2),
            CreateWordParameter(3, 0x90, 3)));
        section.Commands.Add(CreateCommand(1, 5, 0x79, "变量测试", 0x94,
            CreateWordParameter(0, 0x98, 4),
            CreateWordParameter(1, 0x9C, 6000),
            CreateWordParameter(2, 0xA0, 0),
            CreateWordParameter(3, 0xA4, 0),
            CreateWordParameter(4, 0xA8, 7)));
        section.Commands.Add(CreateCommand(1, 6, 0x79, "变量测试", 0xAC,
            CreateWordParameter(0, 0xB0, 5),
            CreateWordParameter(1, 0xB4, 7000),
            CreateWordParameter(2, 0xB8, 1),
            CreateWordParameter(3, 0xBC, 2),
            CreateWordParameter(4, 0xC0, 7001)));
        section.Commands.Add(CreateCommand(1, 7, 0x77, "变量运算", 0xC4,
            CreateWordParameter(0, 0xC8, 0),
            CreateWordParameter(1, 0xCC, 2048),
            CreateWordParameter(2, 0xD0, 1),
            CreateWordParameter(3, 0xD4, 0),
            CreateWordParameter(4, 0xD8, 123)));

        var service = new ScriptVariableUsageService();
        var snapshot = service.BuildCurrentScenarioSnapshot(document);
        AssertScriptVariableEqual(12, snapshot.Occurrences.Count, "occurrence count");
        AssertScriptVariableEqual(12, snapshot.Summaries.Count, "summary count");
        AssertContains(snapshot.Occurrences, "布尔变量", 101, "测试(true)");
        AssertContains(snapshot.Occurrences, "布尔变量", 201, "测试(false)");
        AssertContains(snapshot.Occurrences, "布尔变量", 301, "赋值");
        AssertContains(snapshot.Occurrences, "整型变量", 3000, "运算写入");
        AssertDoesNotContain(snapshot.Occurrences, "常数", 99);
        AssertContains(snapshot.Occurrences, "指针变量(p)", 4096, "运算写入");
        AssertContains(snapshot.Occurrences, "整型变量(a)", 4000, "运算读取");
        AssertContains(snapshot.Occurrences, "整型变量", 5000, "赋值");
        AssertContains(snapshot.Occurrences, "整型变量(a)", 6000, "测试读取");
        AssertDoesNotContain(snapshot.Occurrences, "常数", 7);
        AssertContains(snapshot.Occurrences, "整型变量(&a)", 7000, "测试读取");
        AssertContains(snapshot.Occurrences, "指针变量(p)", 7001, "测试读取");
        AssertContains(snapshot.Occurrences, "指针变量(*p)", 2048, "运算写入");
        AssertDoesNotContain(snapshot.Occurrences, "常数", 123);

        var battlefieldDocument = new LegacyScenarioDocument
        {
            FilePath = Path.Combine("RS", "S_99.eex")
        };
        var battlefieldScene = new LegacyScenarioScene
        {
            SceneIndex = 1,
            FileOffset = 0x20
        };
        var battlefieldSection = new LegacyScenarioSection
        {
            SceneIndex = 1,
            SectionIndex = 1,
            FileOffset = 0x24,
            LengthPrefixOffset = 0x22,
            DeclaredLength = 0x20
        };
        battlefieldScene.Sections.Add(battlefieldSection);
        battlefieldDocument.Scenes.Add(battlefieldScene);
        battlefieldSection.Commands.Add(CreateCommand(1, 0, 0x0B, "变量赋值", 0x30,
            CreateWordParameter(0, 0x34, 301),
            CreateWordParameter(1, 0x38, 0)));

        var scopedSnapshot = ScriptVariableUsageService.BuildSnapshot(
            snapshot.Occurrences.Concat(service.ExtractOccurrences(battlefieldDocument)).ToList(),
            "scope-test");
        AssertScriptVariableEqual(2, scopedSnapshot.Summaries.Count(x => x.VariableType == "布尔变量" && x.VariableAddress == 301), "scoped summary count");
        AssertScriptVariableEqual(1, scopedSnapshot.Summaries.Count(x => x.VariableType == "布尔变量" && x.VariableAddress == 301 && x.Scope == "R剧本"), "R scope summary count");
        AssertScriptVariableEqual(1, scopedSnapshot.Summaries.Count(x => x.VariableType == "布尔变量" && x.VariableAddress == 301 && x.Scope == "S战场"), "S scope summary count");

        var target = snapshot.Occurrences.First(x => x.VariableAddress == 5000);
        AssertScriptVariableEqual("R_99.eex", target.ScenarioFileName, "scenario filename");
        AssertScriptVariableEqual("R剧本", target.Scope, "scope");
        AssertScriptVariableEqual(1, target.SceneIndex, "scene index");
        AssertScriptVariableEqual(1, target.SectionIndex, "section index");
        AssertScriptVariableEqual(5, target.CommandIndex, "command index");
        AssertScriptVariableEqual(4, target.CommandOrdinal, "command ordinal");
        AssertScriptVariableEqual(0x80, target.CommandOffset, "command offset");
        AssertScriptVariableEqual(0x84, target.ParameterOffset, "parameter offset");
        AssertScriptVariableEqual("78", target.CommandIdHex, "command id hex display");
        AssertScriptVariableEqual("5000 / 1388", target.VariableAddressText, "variable address display");
        AssertScriptVariableEqual("000080", target.CommandOffsetHex, "command offset hex display");
        AssertScriptVariableEqual("000084", target.ParameterOffsetHex, "parameter offset hex display");

        var targetSummary = snapshot.Summaries.First(x => x.VariableAddress == 5000);
        AssertScriptVariableEqual("5000 / 1388", targetSummary.VariableAddressText, "summary variable address display");
        if (snapshot.Occurrences.Any(x => x.CommandIdHex.Contains("0x", StringComparison.OrdinalIgnoreCase)) ||
            snapshot.Summaries.Any(x => x.CommandIds.Contains("0x", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Script variable hex display should not include 0x prefix.");
        }

        var project = new ProjectDetector().DetectDefaultProject();
        var dictionaryPath = ProjectDetector.FindSceneDictionaryPath(project);
        var dictionary = new SceneStringParser().Parse(dictionaryPath);
        var projectScan = service.ScanProject(project, dictionary);
        AssertScriptVariableEqual(0, projectScan.FailedScenarios.Count, "project scan failure count");

        Console.WriteLine($"SCRIPT_VARIABLE_USAGE_OK summaries={snapshot.Summaries.Count} occurrences={snapshot.Occurrences.Count} projectScanned={projectScan.ScannedScenarioCount}");
    }

    private static LegacyScenarioCommandNode CreateCommand(
        int sceneIndex,
        int ordinal,
        int commandId,
        string commandName,
        int fileOffset,
        params LegacyScenarioCommandParameter[] parameters)
    {
        var command = new LegacyScenarioCommandNode
        {
            SceneIndex = sceneIndex,
            SectionIndex = 1,
            CommandIndex = ordinal + 1,
            CommandOrdinal = ordinal,
            CommandId = commandId,
            CommandName = commandName,
            FileOffset = fileOffset,
            ConsumedBytes = 2 + parameters.Sum(parameter => parameter.ByteLength + 2)
        };
        command.Parameters.AddRange(parameters);
        return command;
    }

    private static LegacyScenarioCommandParameter CreateWordParameter(int index, int fileOffset, int value)
        => new()
        {
            Index = index,
            LayoutCode = 0x02,
            Tag = 0x02,
            FileOffset = fileOffset,
            Kind = LegacyScenarioParameterKind.Word16,
            IntValue = value,
            ByteLength = 2
        };

    private static LegacyScenarioCommandParameter CreateArrayParameter(int index, int fileOffset, params int[] values)
    {
        var parameter = new LegacyScenarioCommandParameter
        {
            Index = index,
            LayoutCode = 0x35,
            Tag = 0x35,
            FileOffset = fileOffset,
            Kind = LegacyScenarioParameterKind.VariableArray,
            IntValue = values.Length,
            ByteLength = 2 + values.Length * 2
        };
        parameter.Values.AddRange(values);
        return parameter;
    }

    private static void AssertContains(
        IReadOnlyList<ScriptVariableOccurrence> occurrences,
        string variableType,
        int address,
        string accessKind)
    {
        if (!occurrences.Any(x => x.VariableType == variableType && x.VariableAddress == address && x.AccessKind == accessKind))
        {
            throw new InvalidOperationException($"Expected variable occurrence was not found: {variableType} {address} {accessKind}");
        }
    }

    private static void AssertDoesNotContain(
        IReadOnlyList<ScriptVariableOccurrence> occurrences,
        string variableType,
        int address)
    {
        if (occurrences.Any(x => x.VariableType == variableType && x.VariableAddress == address))
        {
            throw new InvalidOperationException($"Unexpected variable occurrence was found: {variableType} {address}");
        }
    }

    private static void AssertScriptVariableEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
        }
    }
}
