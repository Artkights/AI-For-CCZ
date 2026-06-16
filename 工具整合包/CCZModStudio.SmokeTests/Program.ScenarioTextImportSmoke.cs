using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

internal partial class Program
{
    static void RunScenarioTextImportSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var dictionaryPath = ProjectDetector.FindSceneDictionaryPath(project);
        if (!File.Exists(dictionaryPath))
        {
            throw new FileNotFoundException("Scenario text import smoke requires CczString.ini.", dictionaryPath);
        }

        var dictionary = new SceneStringParser().Parse(dictionaryPath);
        var personNames = ScenarioTextImportService.LoadPersonNames(project, tables);
        var service = new ScenarioTextImportService(personNames);
        var input = """
@dialog speaker=曹操
孟德在此，诸军听令。

@narration
夜色渐深，营中火光连成一片。

@text x=10 y=6 mode=0
粮草已运抵前线。

@appear actor=曹操 x=12 y=8 dir=down action=0
@move actor=曹操 x=15 y=8 dir=right mode=0 battle=0
@turn actor=曹操 dir=left
@action actor=曹操 action=6
@battle-turn actor=曹操 dir=right
@battle-action actor=曹操 action=3 wait=1
""";

        var result = service.Parse(input, 1, 1, (commandId, sceneIndex, sectionIndex) =>
            CreateScenarioTextImportSmokeCommand(dictionary, commandId, sceneIndex, sectionIndex));
        if (!result.Success)
        {
            throw new InvalidOperationException("Scenario text import smoke failed: " + string.Join(" | ", result.Errors.Select(error => error.DisplayText)));
        }

        var expectedIds = new[] { 0x14, 0x14, 0x2C, 0x30, 0x32, 0x33, 0x34, 0x4F, 0x50 };
        var actualIds = result.Commands.Select(command => command.CommandId).ToArray();
        if (!actualIds.SequenceEqual(expectedIds))
        {
            throw new InvalidOperationException($"Unexpected command IDs: {string.Join(",", actualIds.Select(id => $"{id:X2}"))}");
        }

        AssertText(result.Commands[0], "&曹操\n孟德在此，诸军听令。");
        AssertText(result.Commands[1], "夜色渐深，营中火光连成一片。");
        AssertText(result.Commands[2], "粮草已运抵前线。");
        AssertInts(result.Commands[2], 10, 6, 0);
        AssertInts(result.Commands[3], 0, 12, 8, 2, 0);
        AssertInts(result.Commands[4], 0, 0, 0, 15, 8, 1);
        AssertInts(result.Commands[5], 0, 0, 3);
        AssertInts(result.Commands[6], 0, 6);
        AssertInts(result.Commands[7], 0, 0, 1, 0, 0, 0);
        AssertInts(result.Commands[8], 0, 3, 0, 1);
        AssertRejectsUnsupportedNarrationCoordinates(service, dictionary);

        var templateText = ScenarioTextImportService.LoadTemplateText(project);
        AssertScenarioTextImportTemplate(templateText);

        Console.WriteLine($"SCENARIO_TEXT_IMPORT_OK commands={result.Commands.Count} templateChars={templateText.Length}");
    }

    private static void AssertScenarioTextImportTemplate(string templateText)
    {
        var requiredFragments = new[]
        {
            "## 你的任务",
            "## 输入理解规则",
            "## 输出硬性规则",
            "## 剧情到命令的转换规则",
            "## 地图坐标理解规则",
            "## 人物引用规则",
            "## 方向规则",
            "## 普通动作编号",
            "0 | 普通 | 常规站立、入场、移动后待机",
            "20 | 无 | 不显示普通姿态",
            "## 战场动作编号",
            "0 | 静止 | 战场待机",
            "13 | 无 | 不显示战场动作",
            "## 等待与延迟规则",
            "## 命令格式规范",
            "## 生成前自检清单",
            "@battle-action actor=<人物ID或名称> action=<战场动作编号> preDelay=0 postDelay=1",
            "@narration` 命令行后面不能带任何参数",
            "所有 `@narration` 命令行都没有任何参数",
            "只有 `@text/@appear/@move` 使用了 `x/y`",
            "缺少信息："
        };

        foreach (var fragment in requiredFragments)
        {
            if (!templateText.Contains(fragment, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Scenario text import template missing fragment: {fragment}");
            }
        }

        if (templateText.Contains("## 完整示例", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Scenario text import template should not contain the old full example section.");
        }

        var forbiddenFragments = new[]
        {
            "建议向 AI 提供",
            "可复用提示词骨架",
            "如何构筑提示词"
        };

        foreach (var fragment in forbiddenFragments)
        {
            if (templateText.Contains(fragment, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Scenario text import template should not contain author-facing fragment: {fragment}");
            }
        }
    }

    private static void AssertRejectsUnsupportedNarrationCoordinates(
        ScenarioTextImportService service,
        SceneStringDocument dictionary)
    {
        var result = service.Parse(
            """
@narration x=0 y=0 mode=0
旁白不应携带坐标。
""",
            1,
            1,
            (commandId, sceneIndex, sectionIndex) => CreateScenarioTextImportSmokeCommand(dictionary, commandId, sceneIndex, sectionIndex));
        if (result.Success)
        {
            throw new InvalidOperationException("Scenario text import should reject @narration with x/y/mode parameters.");
        }

        var errorText = string.Join(" | ", result.Errors.Select(error => error.DisplayText));
        if (!errorText.Contains("不支持参数", StringComparison.Ordinal) ||
            !errorText.Contains("x", StringComparison.Ordinal) ||
            !errorText.Contains("y", StringComparison.Ordinal) ||
            !errorText.Contains("mode", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unexpected @narration coordinate rejection error: " + errorText);
        }
    }

    private static LegacyScenarioCommandNode? CreateScenarioTextImportSmokeCommand(
        SceneStringDocument dictionary,
        int commandId,
        int sceneIndex,
        int sectionIndex)
    {
        if (commandId < 0 || commandId >= ScenarioStructureProbeReader.LegacyCommandInstructionTable.Count)
        {
            return null;
        }

        var command = new LegacyScenarioCommandNode
        {
            SceneIndex = sceneIndex,
            SectionIndex = sectionIndex,
            CommandId = commandId,
            CommandName = dictionary.Commands.FirstOrDefault(command => command.Id == commandId)?.Name ?? $"Command {commandId:X2}",
            FileOffset = 0,
            ConsumedBytes = 0
        };

        foreach (var layoutCode in ScenarioStructureProbeReader.LegacyCommandInstructionTable[commandId].TakeWhile(value => value != -1))
        {
            var kind = layoutCode switch
            {
                0x05 => LegacyScenarioParameterKind.Text,
                0x35 => LegacyScenarioParameterKind.VariableArray,
                0x04 => LegacyScenarioParameterKind.Dword32,
                _ => LegacyScenarioParameterKind.Word16
            };
            command.Parameters.Add(new LegacyScenarioCommandParameter
            {
                Index = command.Parameters.Count,
                LayoutCode = layoutCode,
                Tag = layoutCode,
                FileOffset = 0,
                Kind = kind,
                ByteLength = kind switch
                {
                    LegacyScenarioParameterKind.Dword32 => 4,
                    LegacyScenarioParameterKind.Text => 1,
                    LegacyScenarioParameterKind.VariableArray => 2,
                    _ => 2
                }
            });
        }

        return command;
    }

    private static void AssertText(LegacyScenarioCommandNode command, string expected)
    {
        var actual = command.TextParameters.FirstOrDefault()?.Text ?? string.Empty;
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected text for {command.CommandIdHex}: {actual}");
        }
    }

    private static void AssertInts(LegacyScenarioCommandNode command, params int[] expected)
    {
        var actual = command.Parameters
            .Where(parameter => parameter.Kind is LegacyScenarioParameterKind.Word16 or LegacyScenarioParameterKind.Dword32)
            .Select(parameter => parameter.IntValue)
            .ToArray();
        if (!actual.SequenceEqual(expected))
        {
            throw new InvalidOperationException($"Unexpected ints for {command.CommandIdHex}: {string.Join(",", actual)}");
        }
    }
}
