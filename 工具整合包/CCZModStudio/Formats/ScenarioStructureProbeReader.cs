using System.Data;
using System.Globalization;
using System.Text;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

/// <summary>
/// R/S eex 结构只读草图。
/// 优先按旧 C++ 剧本编辑器已确认规则解析 Scene/Section/命令边界；解析失败时退回旧的命令词探针。
/// </summary>
public sealed class ScenarioStructureProbeReader
{
    private readonly ScenarioCommandProbeReader _commandProbeReader = new();
    private readonly ScenarioCommandParameterTemplateService _commandParameterTemplateService = new();

    internal static IReadOnlyList<byte> LegacyCommandCanOpenSubEventTable => LegacyCommandCanOpenSubEvent;
    internal static IReadOnlyList<int[]> LegacyCommandInstructionTable => LegacyCommandInstructions;

    public ScenarioStructureProbeResult Build(
        string path,
        SceneStringDocument sceneDictionary,
        int maxCommandRows = 800,
        CczProject? project = null,
        IReadOnlyList<HexTableDefinition>? tables = null)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("R/S eex 或旧 E5S 文件不存在。", path);
        if (sceneDictionary.Commands.Count == 0) throw new InvalidOperationException("命令字典为空，无法生成剧本结构草图。");

        var bytes = File.ReadAllBytes(path);
        var referenceLookup = ReferenceLookup.TryCreate(project, tables);

        var legacyRows = TryBuildWithLegacyParser(bytes, path, sceneDictionary, referenceLookup, maxCommandRows);
        if (legacyRows.Count > 0)
        {
            return BuildResult(path, legacyRows, usedLegacyParser: true);
        }

        var fallbackRows = BuildWithWordProbe(path, sceneDictionary, referenceLookup, maxCommandRows);
        return BuildResult(path, fallbackRows, usedLegacyParser: false);
    }

    private ScenarioStructureProbeResult BuildResult(string path, List<ScenarioStructureRow> rows, bool usedLegacyParser)
    {
        var sceneCount = rows.Where(x => x.NodeType == "Scene候选").Select(x => x.SceneIndex).Distinct().Count();
        var sectionCount = rows.Count(x => x.NodeType == "Section候选");
        var commandCount = rows.Count(x => x.NodeType == "Command候选");
        var resolvedReferenceCount = rows.Count(x => !string.IsNullOrWhiteSpace(x.ReferenceHint));
        var commandTemplateCount = rows.Count(x => x.NodeType == "Command候选" && x.HasCommandTemplate);
        var parserText = usedLegacyParser
            ? "已按旧 C++ 剧本编辑器规则解析 Scene/Section/命令边界，并保留正文根/子事件块信号。"
            : "旧规则解析未完全成立，当前退回命令字典探针结果，仅供定位和注释。";
        var summary =
            $"文件 {Path.GetFileName(path)}：生成 {sceneCount} 个 Scene、{sectionCount} 个 Section、{commandCount} 个 Command，" +
            $"其中 {resolvedReferenceCount} 条带跨表/资源候选解释，{commandTemplateCount} 条命中常见命令参数模板。{parserText}";

        return new ScenarioStructureProbeResult
        {
            FilePath = path,
            FileName = Path.GetFileName(path),
            CommandCandidateCount = commandCount,
            SceneCount = sceneCount,
            SectionCount = sectionCount,
            UsedLegacyParser = usedLegacyParser,
            Summary = summary,
            Rows = rows,
            XmlText = BuildXml(path, summary, rows)
        };
    }

    private List<ScenarioStructureRow> TryBuildWithLegacyParser(
        byte[] bytes,
        string path,
        SceneStringDocument dictionary,
        ReferenceLookup? lookup,
        int maxCommandRows)
    {
        if (!TryReadHeader(bytes, out var sceneOffsets))
        {
            return new List<ScenarioStructureRow>();
        }

        var rows = new List<ScenarioStructureRow>();
        var nextRowIndex = 1;
        var commandBudget = 0;

        for (var sceneIndex = 0; sceneIndex < sceneOffsets.Count; sceneIndex++)
        {
            var sceneStart = sceneOffsets[sceneIndex];
            if (sceneStart < 0 || sceneStart + 2 > bytes.Length)
            {
                return new List<ScenarioStructureRow>();
            }

            AddSceneRow(rows, ref nextRowIndex, sceneIndex + 1, "按旧 C++ 剧本编辑器 Scene 偏移表解析。");
            var sectionCount = ReadInt16(bytes, sceneStart, signed: false);
            var cursor = sceneStart + 2;

            for (var sectionIndex = 0; sectionIndex < sectionCount; sectionIndex++)
            {
                if (cursor + 2 > bytes.Length)
                {
                    return new List<ScenarioStructureRow>();
                }

                var sectionLength = ReadInt16(bytes, cursor, signed: false);
                cursor += 2;
                if (sectionLength < 0 || cursor + sectionLength > bytes.Length)
                {
                    return new List<ScenarioStructureRow>();
                }

                AddSectionRow(rows, ref nextRowIndex, sceneIndex + 1, sectionIndex + 1, "按旧 C++ 剧本编辑器 Section 长度前缀解析。");
                var sectionBytes = bytes.AsSpan(cursor, sectionLength).ToArray();
                cursor += sectionLength;

                var sectionRows = ParseSectionCommands(
                    sectionBytes,
                    sceneIndex + 1,
                    sectionIndex + 1,
                    cursor - sectionLength,
                    dictionary,
                    lookup,
                    ref nextRowIndex,
                    maxCommandRows - commandBudget);
                if (sectionRows == null)
                {
                    return new List<ScenarioStructureRow>();
                }

                rows.AddRange(sectionRows);
                commandBudget += sectionRows.Count;
                if (commandBudget >= maxCommandRows)
                {
                    return rows;
                }
            }
        }

        return rows;
    }

    private List<ScenarioStructureRow>? ParseSectionCommands(
        byte[] sectionBytes,
        int sceneIndex,
        int sectionIndex,
        int sectionFileOffset,
        SceneStringDocument dictionary,
        ReferenceLookup? lookup,
        ref int nextRowIndex,
        int remainingBudget)
    {
        var rows = new List<ScenarioStructureRow>();
        var commandIndex = 0;
        var offset = 0;
        var head = true;
        var pendingSubEventMarker = false;
        var subEventDepth = 0;

        while (offset < sectionBytes.Length && remainingBudget > 0)
        {
            if (offset + 2 > sectionBytes.Length) return null;

            var commandOffsetInFile = sectionFileOffset + offset;
            var id = ReadInt16(sectionBytes, offset, signed: false);
            if (id < 0 || id >= LegacyCommandInstructions.Length) return null;

            var parse = TryParseCommand(sectionBytes, offset, id);
            if (parse == null) return null;

            commandIndex++;
            remainingBudget--;
            var hasTemplate = _commandParameterTemplateService.HasTemplate(id);
            var commandName = ResolveCommandName(dictionary, id);

            var startsBodyBlock = false;
            var opensSubEventBlock = false;
            var endsSubEventBlock = false;
            var annotationHints = new List<string>();

            if (id == 0 && head)
            {
                head = false;
                startsBodyBlock = true;
                annotationHints.Add("Section 头部结束；旧编辑器在这里切入正文块。");
            }
            else if (id == 1)
            {
                pendingSubEventMarker = true;
                annotationHints.Add("子事件设定标志；真正承载子块的是其后第一条可嵌套命令。");
            }
            else if (pendingSubEventMarker && LegacyCommandOpensSubEvent(id))
            {
                pendingSubEventMarker = false;
                opensSubEventBlock = true;
                subEventDepth++;
                annotationHints.Add($"旧编辑器会把该命令作为子事件载体并进入深度 {subEventDepth}。");
            }
            else
            {
                pendingSubEventMarker = false;
            }

            if (id == 0 && subEventDepth > 0 && !startsBodyBlock)
            {
                endsSubEventBlock = true;
                annotationHints.Add($"旧编辑器会在这里结束最近的子事件块；当前深度 {subEventDepth} -> {Math.Max(0, subEventDepth - 1)}。");
                subEventDepth--;
            }

            var probeRow = new ScenarioCommandProbeRow
            {
                Index = commandIndex,
                WordIndex = commandOffsetInFile / 2,
                OffsetHex = "0x" + commandOffsetInFile.ToString("X6", CultureInfo.InvariantCulture),
                CommandId = id,
                CommandIdHex = "0x" + id.ToString("X", CultureInfo.InvariantCulture),
                CommandName = commandName,
                ContextWordsHex = string.Join(" ", parse.ContextWords.Select(word => word.ToString("X4", CultureInfo.InvariantCulture))),
                Confidence = "高",
                Note = "按旧 C++ 剧本编辑器参数布局解析。",
                Annotation = string.Join(" ", annotationHints)
            };

            rows.Add(new ScenarioStructureRow
            {
                Index = nextRowIndex++,
                Level = 2,
                NodeType = "Command候选",
                SceneIndex = sceneIndex,
                SectionIndex = sectionIndex,
                CommandIndex = commandIndex,
                OffsetHex = probeRow.OffsetHex,
                CommandId = id,
                CommandIdHex = probeRow.CommandIdHex,
                CommandName = commandName,
                ParameterPreview = BuildLegacyParameterPreview(parse),
                RawContextWordsHex = probeRow.ContextWordsHex,
                LegacyParameterLayout = parse.LayoutText,
                StartsBodyBlock = startsBodyBlock,
                OpensSubEventBlock = opensSubEventBlock,
                EndsSubEventBlock = endsSubEventBlock,
                HasCommandTemplate = hasTemplate,
                CommandTemplateHint = _commandParameterTemplateService.BuildShortHint(id, commandName),
                ReferenceHint = BuildReferenceHint(probeRow, lookup),
                Confidence = "源码对照",
                Annotation = BuildLegacyStructureAnnotation(commandName, id, annotationHints, parse)
            });

            offset += parse.ConsumedBytes;
        }

        return rows;
    }

    private static string BuildLegacyParameterPreview(LegacyParsedCommand parse)
    {
        if (parse.LogicalParameters.Count == 0)
        {
            return "无后续参数预览";
        }

        return "后续16位词：" + string.Join(' ', parse.LogicalParameters.Select(value => value.ToString("X4", CultureInfo.InvariantCulture)));
    }

    private static string BuildLegacyStructureAnnotation(string commandName, int id, IReadOnlyList<string> hints, LegacyParsedCommand parse)
    {
        var builder = new StringBuilder();
        builder.Append($"旧规则命令：0x{id:X2} {commandName}。");
        if (!string.IsNullOrWhiteSpace(parse.LayoutText))
        {
            builder.Append($" 参数布局：{parse.LayoutText}。");
        }
        foreach (var hint in hints)
        {
            builder.Append(hint);
        }
        builder.Append(" 当前仍为只读解析结果，不作为完整命令树写回依据。");
        return builder.ToString();
    }

    private List<ScenarioStructureRow> BuildWithWordProbe(
        string path,
        SceneStringDocument sceneDictionary,
        ReferenceLookup? referenceLookup,
        int maxCommandRows)
    {
        var commandRows = _commandProbeReader.Probe(path, sceneDictionary, maxCommandRows);
        var rows = new List<ScenarioStructureRow>();
        var sceneIndex = 1;
        var sectionIndex = 1;
        var commandIndex = 0;
        var nextRowIndex = 1;

        AddSceneRow(rows, ref nextRowIndex, sceneIndex, "从文件开头/扫描区开头开始的候选 Scene。");
        AddSectionRow(rows, ref nextRowIndex, sceneIndex, sectionIndex, "候选 Section；由命令流起点或结束标记推断。");

        foreach (var command in commandRows)
        {
            commandIndex++;
            var hasCommandTemplate = _commandParameterTemplateService.HasTemplate(command.CommandId);
            rows.Add(new ScenarioStructureRow
            {
                Index = nextRowIndex++,
                Level = 2,
                NodeType = "Command候选",
                SceneIndex = sceneIndex,
                SectionIndex = sectionIndex,
                CommandIndex = commandIndex,
                OffsetHex = command.OffsetHex,
                CommandId = command.CommandId,
                CommandIdHex = command.CommandIdHex,
                CommandName = command.CommandName,
                ParameterPreview = BuildParameterPreview(command),
                RawContextWordsHex = command.ContextWordsHex,
                HasCommandTemplate = hasCommandTemplate,
                CommandTemplateHint = _commandParameterTemplateService.BuildShortHint(command.CommandId, command.CommandName),
                ReferenceHint = BuildReferenceHint(command, referenceLookup),
                Confidence = command.Confidence,
                Annotation = BuildFallbackStructureAnnotation(command)
            });

            if (command.CommandId == 0x0C)
            {
                sectionIndex++;
                commandIndex = 0;
                AddSectionRow(rows, ref nextRowIndex, sceneIndex, sectionIndex, "上一命令为“结束Section”，这里开始新的候选 Section。");
            }
            else if (command.CommandId == 0x0D)
            {
                sceneIndex++;
                sectionIndex = 1;
                commandIndex = 0;
                AddSceneRow(rows, ref nextRowIndex, sceneIndex, "上一命令为“结束Scene”，这里开始新的候选 Scene。");
                AddSectionRow(rows, ref nextRowIndex, sceneIndex, sectionIndex, "新 Scene 的第一个候选 Section。");
            }
        }

        return rows;
    }

    private static void AddSceneRow(List<ScenarioStructureRow> rows, ref int nextRowIndex, int sceneIndex, string annotation)
    {
        rows.Add(new ScenarioStructureRow
        {
            Index = nextRowIndex++,
            Level = 0,
            NodeType = "Scene候选",
            SceneIndex = sceneIndex,
            SectionIndex = 0,
            CommandIndex = 0,
            CommandName = $"Scene {sceneIndex}",
            Confidence = "草图",
            Annotation = annotation
        });
    }

    private static void AddSectionRow(List<ScenarioStructureRow> rows, ref int nextRowIndex, int sceneIndex, int sectionIndex, string annotation)
    {
        rows.Add(new ScenarioStructureRow
        {
            Index = nextRowIndex++,
            Level = 1,
            NodeType = "Section候选",
            SceneIndex = sceneIndex,
            SectionIndex = sectionIndex,
            CommandIndex = 0,
            CommandName = $"Section {sectionIndex}",
            Confidence = "草图",
            Annotation = annotation
        });
    }

    private static bool TryReadHeader(byte[] bytes, out List<int> sceneOffsets)
    {
        sceneOffsets = new List<int>();
        if (bytes.Length < 14) return false;
        if (bytes[0] != (byte)'E' || bytes[1] != (byte)'E' || bytes[2] != (byte)'X' || bytes[3] != 0) return false;

        var firstSceneOffset = ReadInt32(bytes, 10);
        if (firstSceneOffset < 14 || firstSceneOffset > bytes.Length) return false;

        for (var offset = 10; offset < firstSceneOffset; offset += 4)
        {
            if (offset + 4 > bytes.Length) return false;
            var sceneOffset = ReadInt32(bytes, offset);
            if (sceneOffset <= 0 || sceneOffset > bytes.Length) return false;
            sceneOffsets.Add(sceneOffset);
        }

        return sceneOffsets.Count > 0;
    }

    private LegacyParsedCommand? TryParseCommand(byte[] sectionBytes, int offset, int commandId)
    {
        if (offset + 2 > sectionBytes.Length) return null;

        var instructions = LegacyCommandInstructions[commandId];
        var logicalParameters = new List<int>();
        var contextWords = new List<int> { commandId };
        var layoutParts = new List<string>();
        var cursor = offset + 2;
        var longCharCount = 0;
        var variableBlockIndex = 0;

        var instructionCount = GetLegacyInstructionCount(commandId);
        for (var instructionIndex = 0; instructionIndex < instructionCount; instructionIndex++)
        {
            var ins = GetLegacyInstructionAt(commandId, instructions, instructionIndex);
            if (ins == -1) break;

            if (cursor + 2 > sectionBytes.Length) return null;

            var tag = ReadInt16(sectionBytes, cursor, signed: false);
            cursor += 2;
            contextWords.Add(tag);

            switch (ins)
            {
                case 0x05:
                    {
                        var stringStart = cursor;
                        while (cursor < sectionBytes.Length && sectionBytes[cursor] != 0)
                        {
                            cursor++;
                        }
                        if (cursor >= sectionBytes.Length) return null;
                        var textBytes = sectionBytes.AsSpan(stringStart, cursor - stringStart + 1).ToArray();
                        var text = DecodeLegacyString(textBytes);
                        cursor += 1;
                        longCharCount++;
                        logicalParameters.Add(Math.Min(text.Length, 0xFFFF));
                        layoutParts.Add($"STR[{tag:X2}]={TrimPreview(text, 24)}");
                        break;
                    }
                case 0x35:
                    {
                        if (cursor + 2 > sectionBytes.Length) return null;
                        var count = ReadInt16(sectionBytes, cursor, signed: false);
                        cursor += 2;
                        logicalParameters.Add(count);
                        contextWords.Add(count);
                        var values = new List<int>();
                        for (var i = 0; i < count; i++)
                        {
                            if (cursor + 2 > sectionBytes.Length) return null;
                            var value = ReadInt16(sectionBytes, cursor, signed: true);
                            cursor += 2;
                            values.Add(value);
                            logicalParameters.Add(value);
                            contextWords.Add(unchecked((ushort)value));
                        }
                        layoutParts.Add($"VAR{variableBlockIndex++}[{tag:X2}]={count}:{string.Join("/", values.Take(6))}");
                        break;
                    }
                case 0x04:
                    {
                        if (cursor + 4 > sectionBytes.Length) return null;
                        var value = ReadInt32(sectionBytes, cursor);
                        cursor += 4;
                        logicalParameters.Add(value);
                        contextWords.Add(unchecked((ushort)(value & 0xFFFF)));
                        contextWords.Add(unchecked((ushort)((value >> 16) & 0xFFFF)));
                        layoutParts.Add($"D32[{tag:X2}]={value}");
                        break;
                    }
                default:
                    {
                        if (cursor + 2 > sectionBytes.Length) return null;
                        var value = ReadInt16(sectionBytes, cursor, signed: true);
                        cursor += 2;
                        logicalParameters.Add(value);
                        contextWords.Add(unchecked((ushort)value));
                        layoutParts.Add($"W16[{tag:X2}]={value}");
                        break;
                    }
            }
        }

        return new LegacyParsedCommand(cursor - offset, logicalParameters, contextWords, string.Join("；", layoutParts));
    }

    private static string DecodeLegacyString(byte[] bytes)
    {
        EncodingService.EnsureCodePages();
        var text = EncodingService.Gbk.GetString(bytes);
        var terminator = text.IndexOf('\0');
        if (terminator >= 0) text = text[..terminator];
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    private static string TrimPreview(string text, int maxChars)
    {
        text = text.Replace("\n", "\\n", StringComparison.Ordinal);
        return text.Length > maxChars ? text[..maxChars] + "…" : text;
    }

    private static bool LegacyCommandOpensSubEvent(int commandId)
        => commandId >= 0
           && commandId < LegacyCommandCanOpenSubEvent.Length
           && LegacyCommandCanOpenSubEvent[commandId] != 0;

    private static int GetLegacyInstructionCount(int commandId)
        => commandId switch
        {
            0x46 => 11 * 20,
            0x47 => 12 * 80,
            _ => LegacyCommandInstructions[commandId].Length
        };

    private static int GetLegacyInstructionAt(int commandId, IReadOnlyList<int> instructions, int index)
        => commandId switch
        {
            0x46 => instructions[index % 11],
            0x47 => instructions[index % 12],
            _ => instructions[index]
        };

    private static string ResolveCommandName(SceneStringDocument dictionary, int id)
        => dictionary.Commands.FirstOrDefault(command => command.Id == id)?.Name ?? $"命令 0x{id:X2}";

    private static int ReadInt16(byte[] bytes, int offset, bool signed)
    {
        var value = bytes[offset] | (bytes[offset + 1] << 8);
        if (!signed) return value;
        if (value > 60000 && value <= 65536) value -= 65536;
        return value;
    }

    private static int ReadInt32(byte[] bytes, int offset)
        => bytes[offset]
           | (bytes[offset + 1] << 8)
           | (bytes[offset + 2] << 16)
           | (bytes[offset + 3] << 24);

    private static string BuildParameterPreview(ScenarioCommandProbeRow command)
    {
        var words = ParseContextWords(command.ContextWordsHex).Skip(1).Take(8).Select(x => x.ToString("X4", CultureInfo.InvariantCulture)).ToList();
        return words.Count == 0 ? "无后续参数预览" : "后续16位词：" + string.Join(' ', words);
    }

    private static string BuildReferenceHint(ScenarioCommandProbeRow command, ReferenceLookup? lookup)
    {
        var words = ParseContextWords(command.ContextWordsHex).Skip(1).Take(16).ToList();
        if (words.Count == 0) return string.Empty;

        var parts = new List<string>();
        if (lookup != null)
        {
            AddKnownReferences(parts, "人物候选", words, lookup.PersonNames, 4);
            AddKnownReferences(parts, "物品候选", words, lookup.ItemNames, 4);
            AddKnownReferences(parts, "策略候选", words, lookup.StrategyNames, 4);
            AddMapReferences(parts, words, lookup.Project);
        }

        var coordinatePairs = words
            .Zip(words.Skip(1), (x, y) => new { X = x, Y = y })
            .Where(p => p.X is >= 0 and <= 60 && p.Y is >= 0 and <= 60)
            .Take(4)
            .Select(p => $"({p.X},{p.Y})")
            .ToList();
        if (coordinatePairs.Count > 0)
        {
            parts.Add("坐标候选：" + string.Join(" / ", coordinatePairs));
        }

        return string.Join("；", parts.Distinct());
    }

    private static string BuildFallbackStructureAnnotation(ScenarioCommandProbeRow command)
    {
        var category = "普通命令候选";
        if (command.CommandId == 0x0C) category = "Section 结束标记候选";
        else if (command.CommandId == 0x0D) category = "Scene 结束标记候选";
        else if (command.CommandId == 0) category = "事件结束/分隔标记候选";
        return $"{category}：{command.Annotation} 参数长度和含义仍需结合旧剧本编辑器与实机验证；当前仅做中文注释和定位。";
    }

    private static void AddKnownReferences(List<string> parts, string label, IReadOnlyList<int> words, IReadOnlyDictionary<int, string> names, int maxItems)
    {
        if (names.Count == 0) return;
        var refs = words
            .Where(names.ContainsKey)
            .Distinct()
            .Take(maxItems)
            .Select(id => $"{id}:{names[id]}")
            .ToList();
        if (refs.Count > 0)
        {
            parts.Add($"{label}：" + string.Join(" / ", refs));
        }
    }

    private static void AddMapReferences(List<string> parts, IReadOnlyList<int> words, CczProject? project)
    {
        if (project == null) return;
        var refs = words
            .Where(id => id is >= 0 and <= 999)
            .Distinct()
            .Select(id => new { Id = id, Path = FindMapPath(project, id) })
            .Where(x => x.Path != null)
            .Take(4)
            .Select(x => $"M{x.Id:000}:{Path.GetFileName(x.Path!)}")
            .ToList();
        if (refs.Count > 0)
        {
            parts.Add("地图文件候选：" + string.Join(" / ", refs));
        }
    }

    private static string? FindMapPath(CczProject project, int id)
    {
        var mapRoot = project.ResolveGameFile("Map");
        foreach (var ext in new[] { ".jpg", ".JPG", ".jpeg", ".JPEG", ".png", ".PNG" })
        {
            var path = Path.Combine(mapRoot, $"M{id:000}{ext}");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static IReadOnlyList<int> ParseContextWords(string contextWordsHex)
    {
        var result = new List<int>();
        foreach (var part in contextWordsHex.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            {
                result.Add(value);
            }
        }
        return result;
    }

    private static string BuildXml(string path, string summary, IReadOnlyList<ScenarioStructureRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<!-- CCZModStudio 只读剧本结构草图：优先按旧 C++ 编辑器规则解析；仍不作为完整命令树写回依据。 -->");
        sb.AppendLine($"<ScenarioStructure file=\"{XmlEscape(Path.GetFileName(path))}\" path=\"{XmlEscape(path)}\">");
        sb.AppendLine($"  <Summary>{XmlEscape(summary)}</Summary>");
        var currentScene = -1;
        var currentSection = -1;
        foreach (var row in rows)
        {
            if (row.NodeType == "Scene候选")
            {
                if (currentSection >= 0)
                {
                    sb.AppendLine("    </Section>");
                    currentSection = -1;
                }
                if (currentScene >= 0) sb.AppendLine("  </Scene>");
                currentScene = row.SceneIndex;
                sb.AppendLine($"  <Scene index=\"{row.SceneIndex}\" confidence=\"{XmlEscape(row.Confidence)}\" annotation=\"{XmlEscape(row.Annotation)}\">");
                continue;
            }

            if (row.NodeType == "Section候选")
            {
                if (currentSection >= 0) sb.AppendLine("    </Section>");
                currentSection = row.SectionIndex;
                sb.AppendLine($"    <Section index=\"{row.SectionIndex}\" confidence=\"{XmlEscape(row.Confidence)}\" annotation=\"{XmlEscape(row.Annotation)}\">");
                continue;
            }

            sb.AppendLine(
                $"      <Command index=\"{row.CommandIndex}\" offset=\"{XmlEscape(row.OffsetHex)}\" id=\"{XmlEscape(row.CommandIdHex)}\" name=\"{XmlEscape(row.CommandName)}\" confidence=\"{XmlEscape(row.Confidence)}\">");
            sb.AppendLine($"        <ParameterPreview>{XmlEscape(row.ParameterPreview)}</ParameterPreview>");
            if (!string.IsNullOrWhiteSpace(row.LegacyParameterLayout))
            {
                sb.AppendLine($"        <LegacyParameterLayout>{XmlEscape(row.LegacyParameterLayout)}</LegacyParameterLayout>");
            }
            if (!string.IsNullOrWhiteSpace(row.ReferenceHint))
            {
                sb.AppendLine($"        <ReferenceHint>{XmlEscape(row.ReferenceHint)}</ReferenceHint>");
            }
            sb.AppendLine($"        <Annotation>{XmlEscape(row.Annotation)}</Annotation>");
            sb.AppendLine("      </Command>");
        }

        if (currentSection >= 0) sb.AppendLine("    </Section>");
        if (currentScene >= 0) sb.AppendLine("  </Scene>");
        sb.AppendLine("</ScenarioStructure>");
        return sb.ToString();
    }

    private static string XmlEscape(string value)
        => value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);

    private sealed record LegacyParsedCommand(
        int ConsumedBytes,
        IReadOnlyList<int> LogicalParameters,
        IReadOnlyList<int> ContextWords,
        string LayoutText);

    private sealed class ReferenceLookup
    {
        public CczProject? Project { get; init; }
        public IReadOnlyDictionary<int, string> PersonNames { get; init; } = new Dictionary<int, string>();
        public IReadOnlyDictionary<int, string> ItemNames { get; init; } = new Dictionary<int, string>();
        public IReadOnlyDictionary<int, string> StrategyNames { get; init; } = new Dictionary<int, string>();

        public static ReferenceLookup? TryCreate(CczProject? project, IReadOnlyList<HexTableDefinition>? tables)
        {
            if (project == null || tables == null || tables.Count == 0) return project == null ? null : new ReferenceLookup { Project = project };

            var reader = new HexTableReader();
            var items = new Dictionary<int, string>();
            foreach (var itemTable in HexTableNameResolver.ResolveItemTables(tables))
            {
                foreach (var pair in LoadNameMap(project, tables, reader, itemTable.TableName))
                {
                    items[pair.Key] = pair.Value;
                }
            }

            return new ReferenceLookup
            {
                Project = project,
                PersonNames = LoadNameMap(project, tables, reader, "6.5-0 人物"),
                ItemNames = items,
                StrategyNames = LoadNameMap(project, tables, reader, "6.5-5 策略")
            };
        }

        private static IReadOnlyDictionary<int, string> LoadNameMap(CczProject project, IReadOnlyList<HexTableDefinition> tables, HexTableReader reader, string tableName)
        {
            try
            {
                if (!HexTableNameResolver.TryResolve(tables, tableName, out var table)) return new Dictionary<int, string>();
                var result = reader.Read(project, table, tables);
                if (!result.Validation.IsUsable || !result.Data.Columns.Contains("ID")) return new Dictionary<int, string>();

                var nameColumn = result.Data.Columns.Contains("名称")
                    ? "名称"
                    : result.Data.Columns.Cast<DataColumn>().FirstOrDefault(c => c.ColumnName.Contains("名", StringComparison.Ordinal))?.ColumnName;
                if (string.IsNullOrWhiteSpace(nameColumn)) return new Dictionary<int, string>();

                var map = new Dictionary<int, string>();
                foreach (DataRow row in result.Data.Rows)
                {
                    var id = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
                    var name = Convert.ToString(row[nameColumn], CultureInfo.InvariantCulture) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        map[id] = name.Trim();
                    }
                }
                return map;
            }
            catch
            {
                return new Dictionary<int, string>();
            }
        }
    }

    private static readonly byte[] LegacyCommandCanOpenSubEvent =
    {
        0,0,2,1,2,2,0,2,0,0,0,0,0,0,0,0,0,0,1,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,2,2,0,0,0,0,0,0,2,2,0,0,0,0,0,0,0,2,2,0,0,0,0,2,0,0,2,2,2,2,2,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,2,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,2,0,0,0,0,2,0,0,0,0,0,2,0,2
    };

    private static readonly int[][] LegacyCommandInstructions =
    {
        new[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x5,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x26,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x35,0x35,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2E,0x4,0x38,0x38,0x38,0x38,0x38,0x39,0x39,0x39,0x39,0x39,-1},
        new[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2E,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x4,0x27,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x12,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x37,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x5,0x2,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x5,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0x2,0x5,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x5,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x5,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x5,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x5,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x5,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0x27,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x4,0x4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x4A,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x4,0x4,0x10,0x26,0x26,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x1B,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x1E,0x4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x9,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0x4,0x4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0x4,0x4,0x4,0x4,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2D,0xC,0x1A,0x1C,0x15,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x36,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0x4,0x4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0x4,0x4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x5,0x26,0x26,0x26,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0x2,0x26,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0x4,0x4,0x2B,0xD,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2C,0x2,0x4,0x4,0x4,0x4,0x3,-1,-1,-1,-1,-1,-1},
        new[] {0x40,0x2,0x4,0x4,0x4,0x2B,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0xD,0x2B,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0xD,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0x13,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0x23,0x4,0x24,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x28,0x4,0x24,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0x23,0x34,0x4,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0x4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x28,0x34,0x4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0xE,0x3E,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0xE,0x3A,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x17,0x49,0x26,0x2,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0x3B,0x49,0x3C,0x49,0x3D,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x4,0x24,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x48,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x3,0x4,0x24,0x3F,0x4,0x4,0x4,0x4,-1,-1,-1,-1,-1},
        new[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x26,0x26,0x4,0x3E,0x32,0x2,0x32,0x2,0x47,0x22,-1,-1,-1},
        new[] {0x2,0x26,0x4,0x4,0x2B,0x3E,0x45,0x7,0x2,0x4,0x4,-1,-1},
        new[] {0x2,0x26,0x26,0x4,0x4,0x2B,0x3E,0x45,0x7,0x2,0x4,0x4,-1},
        new[] {0x2,0x3B,0x49,0x3C,0x49,0x3D,-1,-1,-1,-1,-1,-1,-1},
        new[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x4,0x38,0x38,0x38,0x38,0x38,0x39,0x39,0x39,0x39,0x39,-1,-1},
        new[] {0x4,0x4,0x4,0x2B,0x26,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x40,0x2,0x4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x41,0x2,0x4,0x4,0x4,0x4,0x4,0x3,0x2F,0x18,0x30,0x4,0x4},
        new[] {0x2C,0x2,0x4,0x4,0x4,0x4,0x3,0x7,0x2,0x4,0x4,-1,-1},
        new[] {0x2,0x2,0x2B,0x26,0x26,0x26,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0x46,0x26,0x26,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0x3,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2C,0x2,0x4,0x4,0x4,0x4,0x3,0x26,-1,-1,-1,-1,-1},
        new[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x40,0x2,0x4,0x4,0x4,0x2B,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x47,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x22,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x42,0x43,0x44,0x4,0x4,0x26,0x26,-1,-1,-1,-1,-1,-1},
        new[] {0x4,0x17,0x49,0x17,0x49,0x17,0x49,0x26,-1,-1,-1,-1,-1},
        new[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x4,0x4,0x4,0x4,0x26,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x34,0x4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0x2,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x26,0x5,0x4C,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x26,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x26,0x5,0x26,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x26,0x4C,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x26,0x4D,0x26,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x26,0x4E,0x26,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x4,0x5,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0x2,0x4F,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x5,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x4,0x4,0x4B,0x26,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0x2,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x40,0x2,0x4,0x2,0x4,0x4,0x2B,0x26,-1,-1,-1,-1,-1},
        new[] {0x4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x17,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0x2,0x4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x4,0x5,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0x4,0x24,0x26,0x26,0x26,0x26,0x26,-1,-1,-1,-1,-1},
        new[] {0x26,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x2,0x50,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x51,0x4,0x53,0x52,0x4,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x4,0x54,0x2,0x55,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x52,0x4,0x56,0x52,0x4,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x5,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        new[] {0x4,0x5,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1}
    };
}
