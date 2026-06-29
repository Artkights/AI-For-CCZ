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
    static void RunLegacyScenarioDepthSmoke(CczProject project)
    {
        var sceneStringPath = ProjectDetector.FindSceneDictionaryPath(project);
        if (!File.Exists(sceneStringPath))
        {
            throw new FileNotFoundException("Legacy scenario depth smoke requires CczString.ini.", sceneStringPath);
        }
    
        var sceneDoc = new SceneStringParser().Parse(sceneStringPath);
        var scenarios = new ScenarioFileReader()
            .ReadAllIndex(project)
            .Where(scenario => ScenarioFileReader.IsRsScriptFile(scenario.FileName))
            .ToList();
        if (scenarios.Count == 0)
        {
            throw new InvalidOperationException("Legacy scenario depth smoke found no R/S eex files.");
        }
    
        var reader = new LegacyScenarioReader();
        var readable = 0;
        var failed = 0;
        long totalCommands = 0;
        var maxDepth = 0;
        var maxDepthFile = string.Empty;
        foreach (var scenario in scenarios)
        {
            try
            {
                var document = reader.Read(scenario.Path, sceneDoc);
                var analysis = AnalyzeLegacyScenarioDepth(document);
                readable++;
                totalCommands += analysis.CommandCount;
                if (analysis.MaxDepth > maxDepth)
                {
                    maxDepth = analysis.MaxDepth;
                    maxDepthFile = scenario.FileName;
                }
    
                if (analysis.MaxDepth > 64)
                {
                    Console.WriteLine($"LEGACY_SCENARIO_DEPTH_FOLD file={scenario.FileName} commands={analysis.CommandCount} maxDepth={analysis.MaxDepth}");
                }
            }
            catch (InvalidDataException ex)
            {
                failed++;
                Console.WriteLine($"LEGACY_SCENARIO_DEPTH_FALLBACK file={scenario.FileName} reason={ex.Message}");
            }
        }
    
        if (readable == 0)
        {
            throw new InvalidOperationException("Legacy scenario depth smoke could not read any R/S eex file with the legacy parser.");
        }
    
        Console.WriteLine($"LEGACY_SCENARIO_DEPTH_OK files={readable}/{scenarios.Count} failed={failed} commands={totalCommands} maxDepth={maxDepth} maxFile={maxDepthFile} uiFoldDepth=64");
    }
    
    static (int CommandCount, int MaxDepth) AnalyzeLegacyScenarioDepth(LegacyScenarioDocument document)
    {
        var commandCount = 0;
        var maxDepth = 0;
        foreach (var scene in document.Scenes)
        {
            foreach (var section in scene.Sections)
            {
                var activeBlocks = new HashSet<LegacyScenarioCommandBlock>();
                var stack = new Stack<(IReadOnlyList<LegacyScenarioCommandNode> Commands, int Index, int Depth, LegacyScenarioCommandBlock? Owner)>();
                stack.Push((section.Commands, 0, 0, null));
    
                while (stack.Count > 0)
                {
                    var frame = stack.Pop();
                    if (frame.Index >= frame.Commands.Count)
                    {
                        if (frame.Owner != null)
                        {
                            activeBlocks.Remove(frame.Owner);
                        }
                        continue;
                    }
    
                    var command = frame.Commands[frame.Index];
                    frame.Index++;
                    stack.Push(frame);
    
                    commandCount++;
                    maxDepth = Math.Max(maxDepth, frame.Depth);
    
                    var childBlock = command.ChildBlock;
                    if (childBlock != null && childBlock.Commands.Count > 0 && activeBlocks.Add(childBlock))
                    {
                        stack.Push((childBlock.Commands, 0, frame.Depth + 1, childBlock));
                    }
                }
            }
        }
    
        return (commandCount, maxDepth);
    }

    static void RunLegacyVariableTestDisplaySmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var formatter = new LegacyScenarioCommandDisplayFormatter(LegacyMfcDialogDataSources.Create(project, tables));
        AssertLegacyVariableTestDisplay(formatter, "true-only", new[] { 20 }, Array.Empty<int>(), "Var20;无");
        AssertLegacyVariableTestDisplay(formatter, "false-only", Array.Empty<int>(), new[] { 20 }, "无;Var20");
        AssertLegacyVariableTestDisplay(formatter, "both", new[] { 1, 2 }, new[] { 3 }, "Var1 Var2;Var3");
        AssertLegacyVariableTestDisplay(formatter, "both-empty", Array.Empty<int>(), Array.Empty<int>(), "无;无");

        var sceneStringPath = ProjectDetector.FindSceneDictionaryPath(project);
        if (File.Exists(sceneStringPath))
        {
            var sceneDoc = new SceneStringParser().Parse(sceneStringPath);
            var scenarios = new ScenarioFileReader()
                .ReadAllIndex(project)
                .Where(scenario => ScenarioFileReader.IsRsScriptFile(scenario.FileName))
                .ToList();
            var reader = new LegacyScenarioReader();
            var formatted = 0;
            foreach (var scenario in scenarios)
            {
                LegacyScenarioDocument document;
                try
                {
                    document = reader.Read(scenario.Path, sceneDoc);
                }
                catch (InvalidDataException)
                {
                    continue;
                }

                foreach (var command in document.EnumerateCommands().Where(command => command.CommandId == 0x05))
                {
                    var display = formatter.FormatCommand(command);
                    var semicolonCount = display.Count(ch => ch == ';');
                    if (semicolonCount != 1)
                    {
                        throw new InvalidOperationException($"0x05 display should contain one semicolon, got {semicolonCount}: {display}");
                    }

                    formatted++;
                }
            }

            Console.WriteLine($"LEGACY_VARIABLE_TEST_DISPLAY_SCAN formatted={formatted}");
        }

        Console.WriteLine("LEGACY_VARIABLE_TEST_DISPLAY_OK");
    }

    static void AssertLegacyVariableTestDisplay(
        LegacyScenarioCommandDisplayFormatter formatter,
        string caseName,
        IReadOnlyList<int> trueValues,
        IReadOnlyList<int> falseValues,
        string expected)
    {
        var command = CreateLegacyVariableTestCommand(trueValues, falseValues);
        var actual = formatter.FormatValuesPreview(command, 8);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Legacy variable test display mismatch for {caseName}: expected '{expected}', actual '{actual}'.");
        }
    }

    static LegacyScenarioCommandNode CreateLegacyVariableTestCommand(IReadOnlyList<int> trueValues, IReadOnlyList<int> falseValues)
    {
        var command = new LegacyScenarioCommandNode
        {
            CommandId = 0x05,
            CommandName = "变量测试"
        };
        command.Parameters.Add(CreateLegacyVariableArrayParameter(0, trueValues));
        command.Parameters.Add(CreateLegacyVariableArrayParameter(1, falseValues));
        return command;
    }

    static LegacyScenarioCommandParameter CreateLegacyVariableArrayParameter(int index, IReadOnlyList<int> values)
    {
        var parameter = new LegacyScenarioCommandParameter
        {
            Index = index,
            LayoutCode = 0x35,
            Tag = 0x35,
            Kind = LegacyScenarioParameterKind.VariableArray,
            IntValue = values.Count,
            ByteLength = 2 + values.Count * 2
        };
        parameter.Values.AddRange(values);
        return parameter;
    }
    
    static void RunLegacyScriptEditSmoke(CczProject project)
    {
        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "LegacyScriptEditSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(smokeRoot);
        foreach (var coreFile in new[] { "Ekd5.exe", "Data.e5", "Star.e5", "Imsg.e5" })
        {
            var source = Path.Combine(project.GameRoot, coreFile);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(smokeRoot, coreFile), overwrite: false);
            }
        }
    
        var rsRoot = Path.Combine(smokeRoot, "RS");
        Directory.CreateDirectory(rsRoot);
        var sourceScenarioPath = Path.Combine(project.GameRoot, "RS", "R_00.eex");
        if (!File.Exists(sourceScenarioPath))
        {
            sourceScenarioPath = Directory.GetFiles(Path.Combine(project.GameRoot, "RS"), "R_*.eex", SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault()
                ?? throw new FileNotFoundException("剧本结构编辑烟测找不到 R_*.eex。", Path.Combine(project.GameRoot, "RS", "R_*.eex"));
        }
    
        var scenarioFileName = Path.GetFileName(sourceScenarioPath);
        var testScenarioPath = Path.Combine(rsRoot, scenarioFileName);
        File.Copy(sourceScenarioPath, testScenarioPath, overwrite: false);
        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=Legacy script edit smoke\r\n");
    
        var sceneStringPath = ProjectDetector.FindSceneDictionaryPath(project);
        if (!File.Exists(sceneStringPath))
        {
            throw new FileNotFoundException("剧本结构编辑烟测需要 CczString.ini。", sceneStringPath);
        }
    
        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        var dictionary = new SceneStringParser().Parse(sceneStringPath);
        var writer = new LegacyScenarioWriter();
        var reader = new LegacyScenarioReader();
        var document = reader.Read(testScenarioPath, dictionary);
        var originalCommandCount = document.CommandCount;
    
        var targetSection = document.Scenes
            .SelectMany(scene => scene.Sections)
            .Where(section => section.DeclaredLength < 65000)
            .FirstOrDefault(section => section.Commands.Any(command => command.StartsBodyBlock && command.ChildBlock != null))
            ?? throw new InvalidOperationException("剧本结构编辑烟测找不到可追加普通命令的正文 Section。");
        var bodyRoot = targetSection.Commands.First(command => command.StartsBodyBlock && command.ChildBlock != null);
        var bodyCommands = bodyRoot.ChildBlock!.Commands;
        var insertedCommandId = 0x09;
        var insertedCommandName = dictionary.Commands.FirstOrDefault(command => command.Id == insertedCommandId)?.Name ?? $"Command {insertedCommandId:X2}";
        var inserted = new LegacyScenarioCommandNode
        {
            SceneIndex = targetSection.SceneIndex,
            SectionIndex = targetSection.SectionIndex,
            CommandId = insertedCommandId,
            CommandName = insertedCommandName,
            FileOffset = 0,
            ConsumedBytes = 0
        };
        inserted.Parameters.Add(new LegacyScenarioCommandParameter
        {
            Index = 0,
            LayoutCode = 0x04,
            Tag = 0x04,
            FileOffset = 0,
            Kind = LegacyScenarioParameterKind.Dword32,
            ByteLength = 4,
            IntValue = 0
        });
    
        var tailTerminatorIndex = bodyCommands.Count - 1;
        if (tailTerminatorIndex < 0 ||
            bodyCommands[tailTerminatorIndex].CommandId != 0x00 ||
            !bodyCommands[tailTerminatorIndex].EndsSubEventBlock)
        {
            throw new InvalidOperationException("剧本结构编辑烟测找不到尾部 00 事件结束标记。");
        }

        var insertIndex = GetLegacyScriptEditAppendIndex(bodyCommands);
        var jumpTargets = CaptureLegacyScriptEditJumpTargets(document);
        bodyCommands.Insert(insertIndex, inserted);
        ReindexLegacyScriptEditDocument(document);
        RestoreLegacyScriptEditJumpTargets(document, jumpTargets);
    
        var addSave = writer.Save(
            testProject,
            Path.Combine("RS", scenarioFileName),
            document,
            dictionary,
            "Legacy script edit smoke add command");
        var addVerify = reader.Read(testScenarioPath, dictionary);
        if (addVerify.CommandCount != originalCommandCount + 1 ||
            string.IsNullOrWhiteSpace(addSave.BackupPath) ||
            !File.Exists(addSave.BackupPath) ||
            string.IsNullOrWhiteSpace(addSave.ReportJsonPath) ||
            !File.Exists(addSave.ReportJsonPath))
        {
            throw new InvalidOperationException("新增剧本命令后的完整保存、复读、备份或报告验证失败。");
        }
        var addedCommandCount = addVerify.CommandCount;
    
        var verifySection = addVerify.Scenes
            .First(scene => scene.SceneIndex == targetSection.SceneIndex)
            .Sections.First(section => section.SectionIndex == targetSection.SectionIndex);
        var verifyBody = verifySection.Commands.First(command => command.StartsBodyBlock && command.ChildBlock != null).ChildBlock!;
        AssertLegacyScriptTailTerminator(verifyBody.Commands, "新增剧本命令复读后");
        var deleteIndex = GetLegacyScriptEditAppendIndex(verifyBody.Commands) - 1;
        if (deleteIndex < 0 || verifyBody.Commands[deleteIndex].CommandId != insertedCommandId)
        {
            throw new InvalidOperationException("新增剧本命令复读后未出现在正文追加区。");
        }
    
        var insertedVerify = verifyBody.Commands[deleteIndex];
        var editableParameter = insertedVerify.Parameters.FirstOrDefault(parameter => parameter.Kind == LegacyScenarioParameterKind.Dword32)
            ?? throw new InvalidOperationException("新增剧本命令复读后缺少可编辑普通参数。");
        const int editedParameterValue = 12345;
        editableParameter.IntValue = editedParameterValue;
        var paramSave = writer.Save(
            testProject,
            Path.Combine("RS", scenarioFileName),
            addVerify,
            dictionary,
            "Legacy script edit smoke edit numeric parameter");
        var paramVerify = reader.Read(testScenarioPath, dictionary);
        var paramVerifySection = paramVerify.Scenes
            .First(scene => scene.SceneIndex == targetSection.SceneIndex)
            .Sections.First(section => section.SectionIndex == targetSection.SectionIndex);
        var paramVerifyBody = paramVerifySection.Commands.First(command => command.StartsBodyBlock && command.ChildBlock != null).ChildBlock!;
        AssertLegacyScriptTailTerminator(paramVerifyBody.Commands, "修改剧本命令参数复读后");
        var paramVerifyIndex = GetLegacyScriptEditAppendIndex(paramVerifyBody.Commands) - 1;
        if (paramVerifyIndex < 0 ||
            paramVerifyBody.Commands[paramVerifyIndex].CommandId != insertedCommandId ||
            paramVerifyBody.Commands[paramVerifyIndex].Parameters.FirstOrDefault(parameter => parameter.Kind == LegacyScenarioParameterKind.Dword32)?.IntValue != editedParameterValue ||
            string.IsNullOrWhiteSpace(paramSave.BackupPath) ||
            !File.Exists(paramSave.BackupPath) ||
            string.IsNullOrWhiteSpace(paramSave.ReportJsonPath) ||
            !File.Exists(paramSave.ReportJsonPath))
        {
            throw new InvalidOperationException("修改剧本命令普通参数后的完整保存、复读、备份或报告验证失败。");
        }
    
        jumpTargets = CaptureLegacyScriptEditJumpTargets(paramVerify);
        paramVerifyBody.Commands.RemoveAt(paramVerifyIndex);
        ReindexLegacyScriptEditDocument(paramVerify);
        RestoreLegacyScriptEditJumpTargets(paramVerify, jumpTargets);
    
        var deleteSave = writer.Save(
            testProject,
            Path.Combine("RS", scenarioFileName),
            paramVerify,
            dictionary,
            "Legacy script edit smoke delete command");
        var deleteVerify = reader.Read(testScenarioPath, dictionary);
        if (deleteVerify.CommandCount != originalCommandCount ||
            string.IsNullOrWhiteSpace(deleteSave.BackupPath) ||
            !File.Exists(deleteSave.BackupPath) ||
            string.IsNullOrWhiteSpace(deleteSave.ReportJsonPath) ||
            !File.Exists(deleteSave.ReportJsonPath))
        {
            throw new InvalidOperationException("删除剧本命令后的完整保存、复读、备份或报告验证失败。");
        }
        var deleteVerifySection = deleteVerify.Scenes
            .First(scene => scene.SceneIndex == targetSection.SceneIndex)
            .Sections.First(section => section.SectionIndex == targetSection.SectionIndex);
        var deleteVerifyBody = deleteVerifySection.Commands.First(command => command.StartsBodyBlock && command.ChildBlock != null).ChildBlock!;
        AssertLegacyScriptTailTerminator(deleteVerifyBody.Commands, "删除剧本命令复读后");
    
        var sectionEdit = reader.Read(testScenarioPath, dictionary);
        var sectionScene = sectionEdit.Scenes.FirstOrDefault(scene => scene.Sections.Count > 0)
            ?? throw new InvalidOperationException("剧本结构编辑烟测找不到可插入 Section 的 Scene。");
        var originalSectionCount = sectionEdit.SectionCount;
        var insertSectionIndex = 0;
        var defaultSection = CreateLegacyScriptEditDefaultSection(sectionScene.SceneIndex, insertSectionIndex + 1, dictionary);
        jumpTargets = CaptureLegacyScriptEditJumpTargets(sectionEdit);
        sectionScene.Sections.Insert(insertSectionIndex, defaultSection);
        ReindexLegacyScriptEditDocument(sectionEdit);
        RestoreLegacyScriptEditJumpTargets(sectionEdit, jumpTargets);
        var sectionSave = writer.Save(
            testProject,
            Path.Combine("RS", scenarioFileName),
            sectionEdit,
            dictionary,
            "Legacy script edit smoke add default section");
        var sectionVerify = reader.Read(testScenarioPath, dictionary);
        var verifyInsertedSection = sectionVerify.Scenes
            .First(scene => scene.SceneIndex == sectionScene.SceneIndex)
            .Sections.First(section => section.SectionIndex == insertSectionIndex + 1);
        if (sectionVerify.SectionCount != originalSectionCount + 1 ||
            verifyInsertedSection.Commands.Count != 2 ||
            verifyInsertedSection.Commands[0].CommandId != 0x02 ||
            verifyInsertedSection.Commands[1].CommandId != 0x00 ||
            !verifyInsertedSection.Commands[1].StartsBodyBlock ||
            verifyInsertedSection.Commands[1].ChildBlock?.Commands.Count != 1 ||
            verifyInsertedSection.Commands[1].ChildBlock!.Commands[0].CommandId != 0x00 ||
            string.IsNullOrWhiteSpace(sectionSave.BackupPath) ||
            !File.Exists(sectionSave.BackupPath) ||
            string.IsNullOrWhiteSpace(sectionSave.ReportJsonPath) ||
            !File.Exists(sectionSave.ReportJsonPath))
        {
            throw new InvalidOperationException("新增默认 Section 后的完整保存、复读、结构骨架、备份或报告验证失败。");
        }
    
        Console.WriteLine($"LEGACY_SCRIPT_EDIT_SMOKE_OK file={scenarioFileName} section={targetSection.SceneIndex}/{targetSection.SectionIndex} command={insertedCommandId:X2}/{insertedCommandName} param={editedParameterValue} count={originalCommandCount}->{addedCommandCount}->{deleteVerify.CommandCount} sections={originalSectionCount}->{sectionVerify.SectionCount} addBackup={Path.GetFileName(addSave.BackupPath)} paramBackup={Path.GetFileName(paramSave.BackupPath)} deleteBackup={Path.GetFileName(deleteSave.BackupPath)} sectionBackup={Path.GetFileName(sectionSave.BackupPath)}");
    }
    
    static LegacyScenarioSection CreateLegacyScriptEditDefaultSection(int sceneIndex, int sectionIndex, SceneStringDocument dictionary)
    {
        var section = new LegacyScenarioSection
        {
            SceneIndex = sceneIndex,
            SectionIndex = sectionIndex,
            FileOffset = 0,
            LengthPrefixOffset = 0,
            DeclaredLength = 0
        };
    
        section.Commands.Add(CreateLegacyScriptEditStructuralCommand(0x02, sceneIndex, sectionIndex, dictionary));
        var bodyRoot = CreateLegacyScriptEditStructuralCommand(0x00, sceneIndex, sectionIndex, dictionary);
        bodyRoot.StartsBodyBlock = true;
        bodyRoot.EndsSubEventBlock = false;
        bodyRoot.ChildBlock = new LegacyScenarioCommandBlock
        {
            Kind = "Body",
            LengthPrefixOffset = 0,
            FileOffset = 0,
            DeclaredLength = 0
        };
        bodyRoot.ChildBlock.Commands.Add(CreateLegacyScriptEditStructuralCommand(0x00, sceneIndex, sectionIndex, dictionary));
        section.Commands.Add(bodyRoot);
        return section;
    }
    
    static LegacyScenarioCommandNode CreateLegacyScriptEditStructuralCommand(
        int commandId,
        int sceneIndex,
        int sectionIndex,
        SceneStringDocument dictionary)
    {
        var command = new LegacyScenarioCommandNode
        {
            SceneIndex = sceneIndex,
            SectionIndex = sectionIndex,
            CommandId = commandId,
            CommandName = dictionary.Commands.FirstOrDefault(item => item.Id == commandId)?.Name ?? $"Command {commandId:X2}",
            FileOffset = 0,
            ConsumedBytes = 0,
            IsSubEventMarker = commandId == 0x01,
            EndsSubEventBlock = commandId == 0x00
        };
        var instructions = ScenarioStructureProbeReader.LegacyCommandInstructionTable[commandId];
        for (var index = 0; index < 13; index++)
        {
            var layoutCode = instructions[index];
            if (layoutCode == -1) break;
            command.Parameters.Add(new LegacyScenarioCommandParameter
            {
                Index = command.Parameters.Count,
                LayoutCode = layoutCode,
                Tag = layoutCode,
                FileOffset = 0,
                Kind = layoutCode switch
                {
                    0x05 => LegacyScenarioParameterKind.Text,
                    0x35 => LegacyScenarioParameterKind.VariableArray,
                    0x04 => LegacyScenarioParameterKind.Dword32,
                    _ => LegacyScenarioParameterKind.Word16
                },
                ByteLength = layoutCode switch
                {
                    0x05 => 1,
                    0x35 => 2,
                    0x04 => 4,
                    _ => 2
                }
            });
        }
    
        return command;
    }
    
    static int GetLegacyScriptEditAppendIndex(IReadOnlyList<LegacyScenarioCommandNode> commands)
    {
        var index = commands.Count;
        while (index > 0 && IsLegacyScriptEditTrailingBoundary(commands[index - 1]))
        {
            index--;
        }
    
        return index;
    }
    
    static bool IsLegacyScriptEditTrailingBoundary(LegacyScenarioCommandNode command)
        => command.EndsSubEventBlock || command.CommandId is 0x0C or 0x0D;

    static void AssertLegacyScriptTailTerminator(IReadOnlyList<LegacyScenarioCommandNode> commands, string context)
    {
        if (commands.Count == 0 ||
            commands[^1].CommandId != 0x00 ||
            !commands[^1].EndsSubEventBlock)
        {
            throw new InvalidOperationException($"{context}尾部事件结束标记未保持在最后。");
        }
    }
    
    static Dictionary<LegacyScenarioCommandNode, LegacyScenarioCommandNode> CaptureLegacyScriptEditJumpTargets(LegacyScenarioDocument document)
    {
        var byOrdinal = document.EnumerateCommands().ToDictionary(command => command.CommandOrdinal);
        var result = new Dictionary<LegacyScenarioCommandNode, LegacyScenarioCommandNode>();
        foreach (var command in byOrdinal.Values.Where(command => command.CommandId == 0x76))
        {
            if (command.JumpTargetOrdinal.HasValue && byOrdinal.TryGetValue(command.JumpTargetOrdinal.Value, out var target))
            {
                result[command] = target;
            }
        }
    
        return result;
    }
    
    static void RestoreLegacyScriptEditJumpTargets(
        LegacyScenarioDocument document,
        IReadOnlyDictionary<LegacyScenarioCommandNode, LegacyScenarioCommandNode> jumpTargets)
    {
        var activeCommands = document.EnumerateCommands().ToHashSet();
        foreach (var pair in jumpTargets)
        {
            if (!activeCommands.Contains(pair.Key)) continue;
            if (activeCommands.Contains(pair.Value))
            {
                pair.Key.JumpTargetOrdinal = pair.Value.CommandOrdinal;
                pair.Key.JumpTargetCommandIndex = pair.Value.CommandIndex;
            }
            else
            {
                pair.Key.JumpTargetOrdinal = null;
                pair.Key.JumpTargetCommandIndex = null;
            }
        }
    }
    
    static void ReindexLegacyScriptEditDocument(LegacyScenarioDocument document)
    {
        var ordinal = 0;
        for (var sceneIndex = 0; sceneIndex < document.Scenes.Count; sceneIndex++)
        {
            var scene = document.Scenes[sceneIndex];
            scene.SceneIndex = sceneIndex + 1;
            for (var sectionIndex = 0; sectionIndex < scene.Sections.Count; sectionIndex++)
            {
                var section = scene.Sections[sectionIndex];
                section.SceneIndex = scene.SceneIndex;
                section.SectionIndex = sectionIndex + 1;
                var commandIndex = 0;
                ReindexLegacyScriptEditCommands(section.Commands, section.SceneIndex, section.SectionIndex, ref commandIndex, ref ordinal);
            }
        }
    }
    
    static void ReindexLegacyScriptEditCommands(
        IReadOnlyList<LegacyScenarioCommandNode> commands,
        int sceneIndex,
        int sectionIndex,
        ref int commandIndex,
        ref int ordinal)
    {
        foreach (var command in commands)
        {
            command.SceneIndex = sceneIndex;
            command.SectionIndex = sectionIndex;
            command.CommandIndex = ++commandIndex;
            command.CommandOrdinal = ordinal++;
            if (command.ChildBlock != null)
            {
                ReindexLegacyScriptEditCommands(command.ChildBlock.Commands, sceneIndex, sectionIndex, ref commandIndex, ref ordinal);
            }
        }
    }
}
