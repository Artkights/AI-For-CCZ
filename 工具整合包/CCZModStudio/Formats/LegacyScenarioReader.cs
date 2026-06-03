using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class LegacyScenarioReader
{
    private const int MaxNestedBlockDepth = 512;

    public LegacyScenarioDocument Read(string path, SceneStringDocument dictionary)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("R/S eex file does not exist.", path);
        }

        return ReadFromBytes(path, File.ReadAllBytes(path), dictionary);
    }

    internal LegacyScenarioDocument ReadFromBytes(string path, byte[] bytes, SceneStringDocument dictionary)
    {
        EncodingService.EnsureCodePages();
        var sceneOffsets = ReadSceneOffsets(bytes);
        var document = new LegacyScenarioDocument
        {
            FilePath = path,
            SceneOffsets = sceneOffsets
        };

        for (var sceneIndex = 0; sceneIndex < sceneOffsets.Count; sceneIndex++)
        {
            var sceneOffset = sceneOffsets[sceneIndex];
            if (sceneOffset + 2 > bytes.Length)
            {
                throw new InvalidDataException($"Scene {sceneIndex + 1} offset is outside the file.");
            }

            var scene = new LegacyScenarioScene
            {
                SceneIndex = sceneIndex + 1,
                FileOffset = sceneOffset
            };
            document.Scenes.Add(scene);

            var sectionCount = ReadUInt16(bytes, sceneOffset);
            var cursor = sceneOffset + 2;
            for (var sectionIndex = 0; sectionIndex < sectionCount; sectionIndex++)
            {
                if (cursor + 2 > bytes.Length)
                {
                    throw new InvalidDataException($"Scene {sceneIndex + 1} Section {sectionIndex + 1} length prefix is outside the file.");
                }

                var lengthPrefixOffset = cursor;
                var sectionLength = ReadUInt16(bytes, cursor);
                cursor += 2;
                if (sectionLength < 0 || cursor + sectionLength > bytes.Length)
                {
                    throw new InvalidDataException($"Scene {sceneIndex + 1} Section {sectionIndex + 1} length is invalid.");
                }

                var section = new LegacyScenarioSection
                {
                    SceneIndex = sceneIndex + 1,
                    SectionIndex = sectionIndex + 1,
                    LengthPrefixOffset = lengthPrefixOffset,
                    FileOffset = cursor,
                    DeclaredLength = sectionLength
                };
                scene.Sections.Add(section);

                var commandIndex = 0;
                section.Commands.AddRange(ParseCommandBlock(
                    bytes,
                    cursor,
                    sectionLength,
                    sceneIndex + 1,
                    sectionIndex + 1,
                    ref commandIndex,
                    dictionary,
                    isSectionRoot: true,
                    blockKind: "Section",
                    nestedDepth: 0));
                cursor += sectionLength;
            }
        }

        AssignOrdinalsAndResolveJumps(document);
        return document;
    }

    private static IReadOnlyList<int> ReadSceneOffsets(byte[] bytes)
    {
        if (bytes.Length < 14 ||
            bytes[0] != (byte)'E' ||
            bytes[1] != (byte)'E' ||
            bytes[2] != (byte)'X' ||
            bytes[3] != 0)
        {
            throw new InvalidDataException("The file is not an old CczSceneEditor EEX scenario.");
        }

        var firstSceneOffset = ReadInt32(bytes, 10);
        if (firstSceneOffset < 14 || firstSceneOffset > bytes.Length || (firstSceneOffset - 10) % 4 != 0)
        {
            throw new InvalidDataException("The EEX scene offset table is invalid.");
        }

        var sceneOffsets = new List<int>();
        for (var offset = 10; offset < firstSceneOffset; offset += 4)
        {
            var sceneOffset = ReadInt32(bytes, offset);
            if (sceneOffset <= 0 || sceneOffset > bytes.Length)
            {
                throw new InvalidDataException("The EEX scene offset table points outside the file.");
            }

            sceneOffsets.Add(sceneOffset);
        }

        if (sceneOffsets.Count == 0)
        {
            throw new InvalidDataException("The EEX scene offset table is empty.");
        }

        return sceneOffsets;
    }

    private static List<LegacyScenarioCommandNode> ParseCommandBlock(
        byte[] bytes,
        int blockStart,
        int blockLength,
        int sceneIndex,
        int sectionIndex,
        ref int commandIndex,
        SceneStringDocument dictionary,
        bool isSectionRoot,
        string blockKind,
        int nestedDepth)
    {
        if (nestedDepth > MaxNestedBlockDepth)
        {
            throw new InvalidDataException($"Scenario command nesting exceeds {MaxNestedBlockDepth} levels at 0x{blockStart:X6}.");
        }

        var commands = new List<LegacyScenarioCommandNode>();
        var cursor = blockStart;
        var blockEnd = checked(blockStart + blockLength);
        var head = isSectionRoot;
        var pendingSubEventMarker = false;

        while (cursor < blockEnd)
        {
            var command = ParseCommand(bytes, cursor, blockEnd, sceneIndex, sectionIndex, ++commandIndex, dictionary);
            cursor += command.ConsumedBytes;

            if (head && command.CommandId == 0)
            {
                command.StartsBodyBlock = true;
                command.ChildBlock = ParseLengthPrefixedChildBlock(
                    bytes,
                    cursor,
                    sceneIndex,
                    sectionIndex,
                    ref commandIndex,
                    dictionary,
                    "Body",
                    nestedDepth + 1);
                cursor = command.ChildBlock.FileOffset + command.ChildBlock.DeclaredLength;
                head = false;
                pendingSubEventMarker = false;
            }
            else if (pendingSubEventMarker && CanOpenSubEvent(command.CommandId))
            {
                command.OpensSubEventBlock = true;
                command.ChildBlock = ParseLengthPrefixedChildBlock(
                    bytes,
                    cursor,
                    sceneIndex,
                    sectionIndex,
                    ref commandIndex,
                    dictionary,
                    "SubEvent",
                    nestedDepth + 1);
                cursor = command.ChildBlock.FileOffset + command.ChildBlock.DeclaredLength;
                pendingSubEventMarker = false;
            }
            else if (command.CommandId != 1)
            {
                pendingSubEventMarker = false;
            }

            if (command.CommandId == 1)
            {
                command.IsSubEventMarker = true;
                pendingSubEventMarker = true;
            }
            else if (command.CommandId == 0 && !command.StartsBodyBlock && blockKind != "Section")
            {
                command.EndsSubEventBlock = true;
            }

            commands.Add(command);
        }

        if (cursor != blockEnd)
        {
            throw new InvalidDataException($"Command block length mismatch at 0x{blockStart:X6}.");
        }

        return commands;
    }

    private static LegacyScenarioCommandBlock ParseLengthPrefixedChildBlock(
        byte[] bytes,
        int lengthPrefixOffset,
        int sceneIndex,
        int sectionIndex,
        ref int commandIndex,
        SceneStringDocument dictionary,
        string kind,
        int nestedDepth)
    {
        if (nestedDepth > MaxNestedBlockDepth)
        {
            throw new InvalidDataException($"Scenario child block nesting exceeds {MaxNestedBlockDepth} levels at 0x{lengthPrefixOffset:X6}.");
        }

        if (lengthPrefixOffset + 2 > bytes.Length)
        {
            throw new InvalidDataException($"Child block length prefix is outside the file at 0x{lengthPrefixOffset:X6}.");
        }

        var length = ReadUInt16(bytes, lengthPrefixOffset);
        var fileOffset = lengthPrefixOffset + 2;
        if (fileOffset + length > bytes.Length)
        {
            throw new InvalidDataException($"Child block length is invalid at 0x{lengthPrefixOffset:X6}.");
        }

        var block = new LegacyScenarioCommandBlock
        {
            Kind = kind,
            LengthPrefixOffset = lengthPrefixOffset,
            FileOffset = fileOffset,
            DeclaredLength = length
        };
        block.Commands.AddRange(ParseCommandBlock(
            bytes,
            fileOffset,
            length,
            sceneIndex,
            sectionIndex,
            ref commandIndex,
            dictionary,
            isSectionRoot: false,
            blockKind: kind,
            nestedDepth: nestedDepth));
        return block;
    }

    private static LegacyScenarioCommandNode ParseCommand(
        byte[] bytes,
        int offset,
        int boundary,
        int sceneIndex,
        int sectionIndex,
        int commandIndex,
        SceneStringDocument dictionary)
    {
        if (offset + 2 > boundary)
        {
            throw new InvalidDataException($"Command id is outside the block at 0x{offset:X6}.");
        }

        var commandId = ReadUInt16(bytes, offset);
        var instructions = GetInstructions(commandId);
        var cursor = offset + 2;
        var command = new LegacyScenarioCommandNode
        {
            SceneIndex = sceneIndex,
            SectionIndex = sectionIndex,
            CommandIndex = commandIndex,
            CommandId = commandId,
            CommandName = ResolveCommandName(dictionary, commandId),
            FileOffset = offset
        };

        var parameterCount = GetInstructionCount(commandId);
        for (var index = 0; index < parameterCount; index++)
        {
            var layoutCode = GetInstructionAt(commandId, instructions, index);
            if (layoutCode == -1) break;
            if (cursor + 2 > boundary)
            {
                throw new InvalidDataException($"Command 0x{commandId:X2} parameter tag is outside the block at 0x{cursor:X6}.");
            }

            var tag = ReadUInt16(bytes, cursor);
            cursor += 2;
            var valueOffset = cursor;
            var parameter = new LegacyScenarioCommandParameter
            {
                Index = command.Parameters.Count,
                LayoutCode = layoutCode,
                Tag = tag,
                FileOffset = valueOffset
            };

            switch (layoutCode)
            {
                case 0x05:
                    {
                        var stringStart = cursor;
                        while (cursor < boundary && bytes[cursor] != 0)
                        {
                            cursor++;
                        }

                        if (cursor >= boundary)
                        {
                            throw new InvalidDataException($"Command 0x{commandId:X2} text parameter has no terminator.");
                        }

                        var byteLength = cursor - stringStart + 1;
                        parameter.Kind = LegacyScenarioParameterKind.Text;
                        parameter.Text = DecodeString(bytes, stringStart, byteLength);
                        parameter.ByteLength = byteLength;
                        cursor++;
                        break;
                    }
                case 0x35:
                    {
                        if (cursor + 2 > boundary)
                        {
                            throw new InvalidDataException($"Command 0x{commandId:X2} variable array count is outside the block.");
                        }

                        var count = ReadUInt16(bytes, cursor);
                        cursor += 2;
                        parameter.Kind = LegacyScenarioParameterKind.VariableArray;
                        parameter.IntValue = count;
                        parameter.ByteLength = 2 + count * 2;
                        for (var i = 0; i < count; i++)
                        {
                            if (cursor + 2 > boundary)
                            {
                                throw new InvalidDataException($"Command 0x{commandId:X2} variable array item is outside the block.");
                            }

                            parameter.Values.Add(ReadInt16(bytes, cursor));
                            cursor += 2;
                        }

                        break;
                    }
                case 0x04:
                    {
                        if (cursor + 4 > boundary)
                        {
                            throw new InvalidDataException($"Command 0x{commandId:X2} dword parameter is outside the block.");
                        }

                        parameter.Kind = LegacyScenarioParameterKind.Dword32;
                        parameter.IntValue = ReadInt32(bytes, cursor);
                        parameter.ByteLength = 4;
                        cursor += 4;
                        break;
                    }
                default:
                    {
                        if (cursor + 2 > boundary)
                        {
                            throw new InvalidDataException($"Command 0x{commandId:X2} word parameter is outside the block.");
                        }

                        parameter.Kind = LegacyScenarioParameterKind.Word16;
                        parameter.IntValue = ReadInt16(bytes, cursor);
                        parameter.ByteLength = 2;
                        cursor += 2;
                        break;
                    }
            }

            command.Parameters.Add(parameter);
        }

        return new LegacyScenarioCommandNode
        {
            SceneIndex = command.SceneIndex,
            SectionIndex = command.SectionIndex,
            CommandIndex = command.CommandIndex,
            CommandId = command.CommandId,
            CommandName = command.CommandName,
            FileOffset = command.FileOffset,
            ConsumedBytes = cursor - offset
        }.CopyRuntimeStateFrom(command);
    }

    private static void AssignOrdinalsAndResolveJumps(LegacyScenarioDocument document)
    {
        var commands = document.EnumerateCommands().ToList();
        var offsetToCommand = new Dictionary<int, LegacyScenarioCommandNode>();
        for (var i = 0; i < commands.Count; i++)
        {
            commands[i].CommandOrdinal = i;
            offsetToCommand[commands[i].FileOffset] = commands[i];
        }

        foreach (var command in commands.Where(command => command.CommandId == 0x76))
        {
            var displacementParameter = command.Parameters.FirstOrDefault(parameter => parameter.Kind == LegacyScenarioParameterKind.Dword32);
            if (displacementParameter == null) continue;

            command.OriginalJumpDisplacement = displacementParameter.IntValue;
            var targetOffset = command.FileOffset + displacementParameter.IntValue + 8;
            if (offsetToCommand.TryGetValue(targetOffset, out var target))
            {
                command.JumpTargetOrdinal = target.CommandOrdinal;
                command.JumpTargetCommandIndex = target.CommandIndex;
            }
        }
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

    private static bool CanOpenSubEvent(int commandId)
    {
        var table = ScenarioStructureProbeReader.LegacyCommandCanOpenSubEventTable;
        return commandId >= 0 && commandId < table.Count && table[commandId] != 0;
    }

    private static string ResolveCommandName(SceneStringDocument dictionary, int id)
        => dictionary.Commands.FirstOrDefault(command => command.Id == id)?.Name ?? $"Command 0x{id:X2}";

    private static string DecodeString(byte[] bytes, int offset, int byteLength)
    {
        var text = EncodingService.Gbk.GetString(bytes, offset, byteLength);
        var terminator = text.IndexOf('\0', StringComparison.Ordinal);
        if (terminator >= 0) text = text[..terminator];
        return text;
    }

    private static int ReadUInt16(byte[] bytes, int offset)
        => bytes[offset] | (bytes[offset + 1] << 8);

    private static int ReadInt16(byte[] bytes, int offset)
    {
        var value = ReadUInt16(bytes, offset);
        if (value > 60000 && value <= 65536) value -= 65536;
        return value;
    }

    private static int ReadInt32(byte[] bytes, int offset)
        => bytes[offset]
           | (bytes[offset + 1] << 8)
           | (bytes[offset + 2] << 16)
           | (bytes[offset + 3] << 24);
}

internal static class LegacyScenarioCommandNodeExtensions
{
    public static LegacyScenarioCommandNode CopyRuntimeStateFrom(this LegacyScenarioCommandNode target, LegacyScenarioCommandNode source)
    {
        target.StartsBodyBlock = source.StartsBodyBlock;
        target.IsSubEventMarker = source.IsSubEventMarker;
        target.OpensSubEventBlock = source.OpensSubEventBlock;
        target.EndsSubEventBlock = source.EndsSubEventBlock;
        target.JumpTargetOrdinal = source.JumpTargetOrdinal;
        target.JumpTargetCommandIndex = source.JumpTargetCommandIndex;
        target.OriginalJumpDisplacement = source.OriginalJumpDisplacement;
        target.Parameters.AddRange(source.Parameters);
        target.ChildBlock = source.ChildBlock;
        return target;
    }
}
