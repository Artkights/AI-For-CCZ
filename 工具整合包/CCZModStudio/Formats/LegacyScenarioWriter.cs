using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class LegacyScenarioWriter
{
    private const int MaxNestedBlockDepth = 512;
    private static readonly byte[] Header = { 0x45, 0x45, 0x58, 0, 1, 2, 0, 0, 0, 0 };

    private readonly WriteOperationReportService _reportService = new();

    public LegacyScenarioWriteResult Save(
        CczProject project,
        string relativeScenarioPath,
        LegacyScenarioDocument document,
        SceneStringDocument dictionary,
        string sourceAction = "Legacy scenario full-structure write")
    {
        var filePath = ResolveScenarioPath(project, relativeScenarioPath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Target scenario file does not exist.", filePath);
        }

        var original = File.ReadAllBytes(filePath);
        var output = Serialize(document);
        var validationSummary = ValidateSerializedDocument(document, output, dictionary);
        var changedBytes = CountChangedBytes(original, output);
        var backupPath = CreateBeforeSaveBackup(project, filePath);
        var tempPath = filePath + ".cczmodstudio.tmp";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        File.WriteAllBytes(tempPath, output);
        File.Move(tempPath, filePath, overwrite: true);

        var reportJsonPath = WriteReport(
            project,
            relativeScenarioPath,
            filePath,
            backupPath,
            original,
            output,
            changedBytes,
            document,
            validationSummary,
            sourceAction);

        return new LegacyScenarioWriteResult
        {
            FilePath = filePath,
            BackupPath = backupPath,
            ReportJsonPath = reportJsonPath,
            ChangedBytes = changedBytes,
            SceneCount = document.SceneCount,
            SectionCount = document.SectionCount,
            CommandCount = document.CommandCount,
            ValidationSummary = validationSummary
        };
    }

    internal byte[] Serialize(LegacyScenarioDocument document)
    {
        EncodingService.EnsureCodePages();
        AssignWriteOrdinals(document);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var commandOffsets = new Dictionary<int, int>();
        var jumpPatches = new List<JumpPatch>();

        writer.Write(Header);
        var sceneOffsetTableStart = stream.Position;
        foreach (var _ in document.Scenes)
        {
            writer.Write(0);
        }

        for (var sceneIndex = 0; sceneIndex < document.Scenes.Count; sceneIndex++)
        {
            var scene = document.Scenes[sceneIndex];
            var sceneStart = checked((int)stream.Position);
            PatchInt32(stream, sceneOffsetTableStart + sceneIndex * 4, sceneStart);
            WriteUInt16(writer, scene.Sections.Count);
            foreach (var section in scene.Sections)
            {
                var sectionLengthPosition = stream.Position;
                WriteUInt16(writer, 0);
                var sectionStart = stream.Position;
                WriteCommandSequence(writer, stream, section.Commands, commandOffsets, jumpPatches, depth: 0);
                var sectionLength = checked((int)(stream.Position - sectionStart));
                if (sectionLength > ushort.MaxValue)
                {
                    throw new InvalidDataException($"Section {scene.SceneIndex}/{section.SectionIndex} exceeds the old editor 16-bit length limit.");
                }

                PatchUInt16(stream, sectionLengthPosition, sectionLength);
            }
        }

        var endPosition = stream.Position;
        foreach (var patch in jumpPatches)
        {
            var displacement = patch.FallbackDisplacement;
            if (patch.TargetOrdinal.HasValue && commandOffsets.TryGetValue(patch.TargetOrdinal.Value, out var targetOffset))
            {
                displacement = targetOffset - patch.CommandOffset - 8;
            }

            PatchInt32(stream, patch.ValueOffset, displacement);
        }

        stream.Position = endPosition;
        return stream.ToArray();
    }

    private static void WriteCommandSequence(
        BinaryWriter writer,
        MemoryStream stream,
        IReadOnlyList<LegacyScenarioCommandNode> commands,
        Dictionary<int, int> commandOffsets,
        List<JumpPatch> jumpPatches,
        int depth)
    {
        if (depth > MaxNestedBlockDepth)
        {
            throw new InvalidDataException($"Scenario command nesting exceeds {MaxNestedBlockDepth} levels while writing.");
        }

        foreach (var command in commands)
        {
            WriteCommand(writer, stream, command, commandOffsets, jumpPatches, depth);
        }
    }

    private static void WriteCommand(
        BinaryWriter writer,
        MemoryStream stream,
        LegacyScenarioCommandNode command,
        Dictionary<int, int> commandOffsets,
        List<JumpPatch> jumpPatches,
        int depth)
    {
        var commandOffset = checked((int)stream.Position);
        commandOffsets[command.CommandOrdinal] = commandOffset;
        WriteUInt16(writer, command.CommandId);

        var instructions = GetInstructions(command.CommandId);
        var parameterIndex = 0;
        var jumpParameterWritten = false;
        var parameterCount = GetInstructionCount(command.CommandId);
        for (var index = 0; index < parameterCount; index++)
        {
            var layoutCode = GetInstructionAt(command.CommandId, instructions, index);
            if (layoutCode == -1) break;

            var parameter = parameterIndex < command.Parameters.Count ? command.Parameters[parameterIndex] : null;
            parameterIndex++;
            WriteUInt16(writer, layoutCode);

            switch (layoutCode)
            {
                case 0x05:
                    {
                        var text = parameter?.Text ?? string.Empty;
                        writer.Write(EncodingService.Gbk.GetBytes(text));
                        writer.Write((byte)0);
                        break;
                    }
                case 0x35:
                    {
                        var values = parameter?.Values ?? [];
                        WriteUInt16(writer, values.Count);
                        foreach (var value in values)
                        {
                            WriteUInt16(writer, value);
                        }

                        break;
                    }
                case 0x04:
                    {
                        if (command.CommandId == 0x76 && !jumpParameterWritten)
                        {
                            var valueOffset = checked((int)stream.Position);
                            writer.Write(0);
                            jumpPatches.Add(new JumpPatch(
                                valueOffset,
                                commandOffset,
                                command.JumpTargetOrdinal,
                                command.OriginalJumpDisplacement ?? parameter?.IntValue ?? 0));
                            jumpParameterWritten = true;
                        }
                        else
                        {
                            writer.Write(parameter?.IntValue ?? 0);
                        }

                        break;
                    }
                default:
                    WriteUInt16(writer, parameter?.IntValue ?? 0);
                    break;
            }
        }

        if (command.ChildBlock == null) return;

        var blockLengthPosition = stream.Position;
        WriteUInt16(writer, 0);
        var blockStart = stream.Position;
        WriteCommandSequence(writer, stream, command.ChildBlock.Commands, commandOffsets, jumpPatches, depth + 1);
        var blockLength = checked((int)(stream.Position - blockStart));
        if (blockLength > ushort.MaxValue)
        {
            throw new InvalidDataException($"Child block under {command.DisplayText} exceeds the old editor 16-bit length limit.");
        }

        PatchUInt16(stream, blockLengthPosition, blockLength);
    }

    private static void AssignWriteOrdinals(LegacyScenarioDocument document)
    {
        var ordinal = 0;
        foreach (var scene in document.Scenes)
        {
            foreach (var section in scene.Sections)
            {
                var commandIndex = 0;
                AssignSectionOrdinals(section.Commands, ref commandIndex, ref ordinal, depth: 0);
            }
        }
    }

    private static void AssignSectionOrdinals(IReadOnlyList<LegacyScenarioCommandNode> commands, ref int commandIndex, ref int ordinal, int depth)
    {
        if (depth > MaxNestedBlockDepth)
        {
            throw new InvalidDataException($"Scenario command nesting exceeds {MaxNestedBlockDepth} levels while assigning ordinals.");
        }

        foreach (var command in commands)
        {
            command.CommandIndex = ++commandIndex;
            command.CommandOrdinal = ordinal++;
            if (command.ChildBlock != null)
            {
                AssignSectionOrdinals(command.ChildBlock.Commands, ref commandIndex, ref ordinal, depth + 1);
            }
        }
    }

    private static string ValidateSerializedDocument(LegacyScenarioDocument expected, byte[] output, SceneStringDocument dictionary)
    {
        var actual = new LegacyScenarioReader().ReadFromBytes(expected.FilePath, output, dictionary);
        var expectedCommandIds = expected.EnumerateCommands().Select(command => command.CommandId).ToList();
        var actualCommandIds = actual.EnumerateCommands().Select(command => command.CommandId).ToList();
        if (expected.SceneCount != actual.SceneCount ||
            expected.SectionCount != actual.SectionCount ||
            expectedCommandIds.Count != actualCommandIds.Count ||
            !expectedCommandIds.SequenceEqual(actualCommandIds))
        {
            throw new InvalidDataException("Serialized scenario failed round-trip validation.");
        }

        var expectedJumps = expected.EnumerateCommands()
            .Where(command => command.CommandId == 0x76)
            .Select(command => command.JumpTargetOrdinal)
            .ToList();
        var actualJumps = actual.EnumerateCommands()
            .Where(command => command.CommandId == 0x76)
            .Select(command => command.JumpTargetOrdinal)
            .ToList();
        if (expectedJumps.Count != actualJumps.Count || !expectedJumps.SequenceEqual(actualJumps))
        {
            throw new InvalidDataException("Serialized scenario failed jump target validation.");
        }

        return $"Round-trip OK: Scene={actual.SceneCount}, Section={actual.SectionCount}, Command={actual.CommandCount}, Jump={actualJumps.Count}";
    }

    private string WriteReport(
        CczProject project,
        string relativeScenarioPath,
        string filePath,
        string backupPath,
        byte[] original,
        byte[] output,
        int changedBytes,
        LegacyScenarioDocument document,
        string validationSummary,
        string sourceAction)
    {
        var normalizedRelativePath = relativeScenarioPath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        var report = new WriteOperationReport
        {
            OperationKind = "Legacy R/S eex full-structure write",
            SourceAction = sourceAction,
            ProjectRoot = project.GameRoot,
            TargetRelativePath = normalizedRelativePath,
            TargetPath = filePath,
            BackupPath = backupPath,
            BeforeSha256 = WriteOperationReportService.ComputeSha256(original),
            AfterSha256 = WriteOperationReportService.ComputeSha256(output),
            ChangedBytes = changedBytes,
            Summary = $"Rebuilt {normalizedRelativePath} with old CczSceneEditor2 structure: Scene={document.SceneCount}, Section={document.SectionCount}, Command={document.CommandCount}.",
            SafetyNotes = "Full structure write uses automatic backup, temp-file replacement, and old-rule round-trip validation before replacing the target file.",
            FormatCheckSummary = validationSummary,
            Changes =
            {
                new WriteOperationChange
                {
                    Category = "LegacyScenarioStructure",
                    TableName = Path.GetFileName(relativeScenarioPath),
                    ColumnName = "FullFile",
                    ByteLength = output.Length,
                    OldValue = original.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    NewValue = output.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Annotation = validationSummary
                }
            },
            Metadata =
            {
                ["RelativeScenarioPath"] = normalizedRelativePath,
                ["SceneCount"] = document.SceneCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["SectionCount"] = document.SectionCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["CommandCount"] = document.CommandCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }
        };
        return _reportService.WriteJsonReport(report, backupPath);
    }

    private static string ResolveScenarioPath(CczProject project, string relativeScenarioPath)
    {
        var filePath = Path.GetFullPath(Path.Combine(project.GameRoot, relativeScenarioPath));
        var gameRoot = Path.GetFullPath(project.GameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!filePath.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Target scenario file is outside the current project: " + filePath);
        }

        return filePath;
    }

    private static string CreateBeforeSaveBackup(CczProject project, string filePath)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(backupRoot, $"{stamp}_{Path.GetFileName(filePath)}");
        var suffix = 1;
        while (File.Exists(backupPath))
        {
            backupPath = Path.Combine(backupRoot, $"{stamp}_{suffix++}_{Path.GetFileName(filePath)}");
        }

        File.Copy(filePath, backupPath, overwrite: false);
        return backupPath;
    }

    private static int CountChangedBytes(byte[] original, byte[] output)
    {
        var count = Math.Abs(original.Length - output.Length);
        var common = Math.Min(original.Length, output.Length);
        for (var i = 0; i < common; i++)
        {
            if (original[i] != output[i]) count++;
        }

        return count;
    }

    private static int GetInstructionCount(int commandId)
        => commandId switch
        {
            0x46 => 11 * 20,
            0x47 => 12 * 80,
            _ => 13
        };

    private static int GetInstructionAt(int commandId, IReadOnlyList<int> instructions, int index)
        => commandId switch
        {
            0x46 => instructions[index % 11],
            0x47 => instructions[index % 12],
            _ => instructions[index]
        };

    private static IReadOnlyList<int> GetInstructions(int commandId)
    {
        var table = ScenarioStructureProbeReader.LegacyCommandInstructionTable;
        if (commandId < 0 || commandId >= table.Count)
        {
            throw new InvalidDataException($"Unknown old scenario command id 0x{commandId:X2}.");
        }

        return table[commandId];
    }

    private static void WriteUInt16(BinaryWriter writer, int value)
        => writer.Write(unchecked((ushort)value));

    private static void PatchUInt16(MemoryStream stream, long offset, int value)
    {
        var current = stream.Position;
        stream.Position = offset;
        stream.WriteByte((byte)(value & 0xFF));
        stream.WriteByte((byte)((value >> 8) & 0xFF));
        stream.Position = current;
    }

    private static void PatchInt32(MemoryStream stream, long offset, int value)
    {
        var current = stream.Position;
        stream.Position = offset;
        stream.WriteByte((byte)(value & 0xFF));
        stream.WriteByte((byte)((value >> 8) & 0xFF));
        stream.WriteByte((byte)((value >> 16) & 0xFF));
        stream.WriteByte((byte)((value >> 24) & 0xFF));
        stream.Position = current;
    }

    private sealed record JumpPatch(int ValueOffset, int CommandOffset, int? TargetOrdinal, int FallbackDisplacement);
}
