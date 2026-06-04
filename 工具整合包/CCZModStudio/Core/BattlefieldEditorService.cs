using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CCZModStudio.Core;

public sealed class BattlefieldEditorService
{
    private readonly ScenarioTextReader _textReader = new();
    private readonly ScenarioStructureProbeReader _structureReader = new();
    private readonly LegacyScenarioReader _legacyReader = new();

    public BattlefieldEditorDocument Load(
        CczProject project,
        ScenarioFileInfo scenario,
        SceneStringDocument? dictionary,
        IReadOnlyList<HexTableDefinition> tables,
        IReadOnlyList<ScenarioMapLinkInfo> mapLinks)
    {
        var texts = File.Exists(scenario.Path)
            ? _textReader.Read(scenario.Path).ToList()
            : new List<ScenarioTextEntry>();
        var titleEntry = PickTitleEntry(texts, scenario.TitleHint);
        var conditionEntry = texts.FirstOrDefault(x => x.Kind == "胜败条件")
                             ?? texts.FirstOrDefault(x => x.Text.Contains("胜利条件", StringComparison.Ordinal)
                                                       || x.Text.Contains("失败条件", StringComparison.Ordinal));
        var mapLink = mapLinks.FirstOrDefault(x => x.ScenarioFileName.Equals(scenario.FileName, StringComparison.OrdinalIgnoreCase));
        var candidates = LoadBattlefieldCommandCandidates(project, scenario, dictionary, tables);
        var unitCandidates = BuildUnitCandidates(candidates);

        return new BattlefieldEditorDocument
        {
            Scenario = scenario,
            MapLink = mapLink,
            TextEntries = texts,
            TitleEntry = titleEntry,
            ConditionEntry = conditionEntry,
            CommandCandidates = candidates,
            UnitCandidates = unitCandidates,
            Summary = BuildSummary(scenario, titleEntry, conditionEntry, mapLink, candidates.Count, unitCandidates.Count),
            Annotation = BuildAnnotation(scenario, titleEntry, conditionEntry, mapLink, candidates.Count, unitCandidates.Count)
        };
    }

    public ScenarioTextSaveResult SaveTitleAndConditions(
        CczProject project,
        BattlefieldEditorDocument document,
        string title,
        string conditions)
    {
        var entries = new List<ScenarioTextEntry>();
        if (document.TitleEntry != null)
        {
            document.TitleEntry.Text = title;
            entries.Add(document.TitleEntry);
        }

        if (document.ConditionEntry != null)
        {
            document.ConditionEntry.Text = conditions;
            entries.Add(document.ConditionEntry);
        }

        if (entries.Count == 0)
        {
            throw new InvalidOperationException("当前关卡没有可安全写回的标题或胜败条件文本。");
        }

        return new ScenarioTextWriter().SaveInPlace(
            project,
            BuildScenarioRelativePath(document.Scenario),
            entries,
            "战场制作页保存标题/胜败条件前自动备份");
    }


    private static string BuildScenarioRelativePath(ScenarioFileInfo scenario)
    {
        if (ScenarioFileReader.IsRsScriptFile(scenario.FileName))
        {
            return Path.Combine("RS", scenario.FileName);
        }

        return Path.Combine("SV", scenario.FileName);
    }

    public static string? ValidateTextForEntry(ScenarioTextEntry? entry, string value, string displayName)
    {
        if (entry == null) return $"{displayName} 没有匹配到可写回文本线索。";
        value = NormalizeText(value);
        if (string.IsNullOrWhiteSpace(value)) return $"{displayName} 不能为空。";
        if (value.Contains('\0')) return $"{displayName} 不能包含 NUL/零字节。";
        var byteCount = EncodingService.GetGbkByteCount(value);
        if (byteCount < 4) return $"{displayName} GBK 字节数 {byteCount} 过短，当前安全写回要求至少 4 字节。";
        if (byteCount > entry.ByteLength) return $"{displayName} GBK 字节数 {byteCount} 超过原地容量 {entry.ByteLength}，只能等长或缩短。";
        return null;
    }

    public static string FormatCapacity(ScenarioTextEntry? entry, string currentText)
    {
        if (entry == null) return "未匹配到可写回文本线索";
        var bytes = EncodingService.GetGbkByteCount(NormalizeText(currentText));
        var remaining = entry.ByteLength - bytes;
        return $"偏移 {entry.OffsetHex}，GBK {bytes}/{entry.ByteLength} 字节，剩余 {remaining} 字节";
    }

    public static string NormalizeText(string? text)
        => (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();

    public static bool TryExtractFirstCoordinate(BattlefieldUnitCandidate candidate, out int x, out int y)
    {
        x = 0;
        y = 0;
        var match = Regex.Match(candidate.CoordinateHint ?? string.Empty, @"\((\d{1,3}),\s*(\d{1,3})\)");
        if (!match.Success) return false;
        if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out x)) return false;
        if (!int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out y)) return false;
        return x >= 0 && y >= 0;
    }

    public static ScenarioStructureRow? FindScriptCommandForCandidate(IEnumerable<ScenarioStructureRow> rows, BattlefieldUnitCandidate candidate)
    {
        if (!TryParseScriptCommandLocator(candidate, out var scene, out var section, out var command, out var offsetHex, out var commandIdHex))
        {
            return null;
        }

        return rows.FirstOrDefault(row =>
            row.NodeType == "Command候选" &&
            row.SceneIndex == scene &&
            row.SectionIndex == section &&
            row.CommandIndex == command &&
            (string.IsNullOrWhiteSpace(offsetHex) || row.OffsetHex.Equals(offsetHex, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(commandIdHex) || row.CommandIdHex.Equals(commandIdHex, StringComparison.OrdinalIgnoreCase)));
    }

    public static bool TryParseScriptCommandLocator(
        BattlefieldUnitCandidate candidate,
        out int scene,
        out int section,
        out int command,
        out string offsetHex,
        out string commandIdHex)
    {
        scene = 0;
        section = 0;
        command = 0;
        offsetHex = candidate.OffsetHex ?? string.Empty;
        commandIdHex = string.Empty;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in (candidate.TargetKey ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var index = part.IndexOf('=');
            if (index <= 0) continue;
            values[part[..index].Trim()] = part[(index + 1)..].Trim();
        }

        if (values.TryGetValue("Scene", out var sceneText) &&
            int.TryParse(sceneText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sceneValue))
        {
            scene = sceneValue;
        }

        if (values.TryGetValue("Section", out var sectionText) &&
            int.TryParse(sectionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sectionValue))
        {
            section = sectionValue;
        }

        if (values.TryGetValue("Command", out var commandText) &&
            int.TryParse(commandText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var commandValue))
        {
            command = commandValue;
        }

        if (values.TryGetValue("Offset", out var offsetValue))
        {
            offsetHex = offsetValue;
        }

        if (values.TryGetValue("Id", out var idValue))
        {
            commandIdHex = idValue;
        }

        if (string.IsNullOrWhiteSpace(commandIdHex))
        {
            var match = Regex.Match(candidate.SourceCommand ?? string.Empty, @"0x[0-9A-Fa-f]+");
            if (match.Success) commandIdHex = match.Value;
        }

        return scene > 0 && section > 0 && command > 0;
    }

    private IReadOnlyList<BattlefieldCommandCandidate> LoadBattlefieldCommandCandidates(
        CczProject project,
        ScenarioFileInfo scenario,
        SceneStringDocument? dictionary,
        IReadOnlyList<HexTableDefinition> tables)
    {
        if (dictionary == null || !File.Exists(scenario.Path)) return Array.Empty<BattlefieldCommandCandidate>();
        var legacyCandidates = LoadLegacyBattlefieldCommandCandidates(scenario, dictionary);
        if (legacyCandidates.Count > 0)
        {
            return legacyCandidates;
        }

        try
        {
            var structure = _structureReader.Build(scenario.Path, dictionary, maxCommandRows: 260, project: project, tables: tables);
            return structure.Rows
                .Where(IsBattlefieldRelated)
                .Select(BuildBattlefieldCommandCandidate)
                .ToList();
        }
        catch
        {
            return Array.Empty<BattlefieldCommandCandidate>();
        }
    }

    private IReadOnlyList<BattlefieldCommandCandidate> LoadLegacyBattlefieldCommandCandidates(
        ScenarioFileInfo scenario,
        SceneStringDocument dictionary)
    {
        try
        {
            var document = _legacyReader.Read(scenario.Path, dictionary);
            return document
                .EnumerateCommands()
                .Select(BuildLegacyBattlefieldStructureRow)
                .Where(IsBattlefieldRelated)
                .Select(BuildBattlefieldCommandCandidate)
                .ToList();
        }
        catch
        {
            return Array.Empty<BattlefieldCommandCandidate>();
        }
    }

    private static BattlefieldCommandCandidate BuildBattlefieldCommandCandidate(ScenarioStructureRow row, int index)
        => new()
        {
            Index = index + 1,
            SceneIndex = row.SceneIndex,
            SectionIndex = row.SectionIndex,
            CommandIndex = row.CommandIndex,
            OffsetHex = row.OffsetHex,
            CommandIdHex = row.CommandIdHex,
            CommandName = row.CommandName,
            RoleHint = BuildRoleHint(row),
            ParameterPreview = row.ParameterPreview,
            RawContextWordsHex = row.RawContextWordsHex,
            LegacyParameterLayout = row.LegacyParameterLayout,
            CommandTemplateHint = row.CommandTemplateHint,
            ReferenceHint = row.ReferenceHint,
            Annotation = BuildCommandAnnotation(row)
        };

    private static ScenarioStructureRow BuildLegacyBattlefieldStructureRow(LegacyScenarioCommandNode command)
    {
        var textCount = command.TextParameters.Count();
        var referenceParts = new List<string>();
        if (textCount > 0) referenceParts.Add($"文本参数 {textCount} 条");
        if (command.JumpTargetOrdinal.HasValue) referenceParts.Add($"0x76 跳转目标 ord={command.JumpTargetOrdinal.Value}");
        if (command.Parameters.Any(parameter => parameter.Values.Count > 0)) referenceParts.Add("可变数组参数");

        return new ScenarioStructureRow
        {
            Index = command.CommandOrdinal + 1,
            Level = 2,
            NodeType = "Command候选",
            SceneIndex = command.SceneIndex,
            SectionIndex = command.SectionIndex,
            CommandIndex = command.CommandIndex,
            OffsetHex = "0x" + command.FileOffset.ToString("X6", CultureInfo.InvariantCulture),
            CommandId = command.CommandId,
            CommandIdHex = command.CommandIdHex,
            CommandName = command.CommandName,
            ParameterPreview = BuildLegacyBattlefieldParameterPreview(command),
            RawContextWordsHex = BuildLegacyBattlefieldRawWords(command),
            LegacyParameterLayout = BuildLegacyBattlefieldLayout(command),
            StartsBodyBlock = command.StartsBodyBlock,
            OpensSubEventBlock = command.OpensSubEventBlock,
            EndsSubEventBlock = command.EndsSubEventBlock,
            CommandTemplateHint = BuildLegacyBattlefieldParameterHint(command),
            ReferenceHint = string.Join("；", referenceParts),
            Confidence = "旧版源码",
            Annotation = $"旧版完整树战场命令候选：{command.CommandName}，位置 0x{command.FileOffset:X6}。"
        };
    }

    private static string BuildLegacyBattlefieldParameterPreview(LegacyScenarioCommandNode command)
    {
        if (command.Parameters.Count == 0) return "无参数";
        return string.Join(" ", command.Parameters.Take(16).Select(parameter => parameter.Kind switch
        {
            LegacyScenarioParameterKind.Text => $"T{parameter.Index}=\"{TrimForBattlefieldPreview(parameter.Text, 16)}\"",
            LegacyScenarioParameterKind.VariableArray => $"V{parameter.Index}[{parameter.Values.Count}]=" + string.Join("/", parameter.Values.Take(8).Select(value => value.ToString("X4", CultureInfo.InvariantCulture))),
            LegacyScenarioParameterKind.Dword32 => $"D{parameter.Index}=0x{parameter.IntValue:X8}",
            _ => $"P{parameter.Index}={parameter.IntValue:X4}"
        }));
    }

    private static string BuildLegacyBattlefieldParameterHint(LegacyScenarioCommandNode command)
    {
        if (command.Parameters.Count == 0) return "旧版参数：无";
        return "旧版参数：" + string.Join(" / ", command.Parameters.Take(12).Select(parameter =>
            parameter.Kind switch
            {
                LegacyScenarioParameterKind.Text => $"槽{parameter.Index}=文本({parameter.ByteLength}B)",
                LegacyScenarioParameterKind.VariableArray => $"槽{parameter.Index}=数组({parameter.Values.Count})",
                LegacyScenarioParameterKind.Dword32 => $"槽{parameter.Index}=32位({parameter.IntValue})",
                _ => $"槽{parameter.Index}=16位({parameter.IntValue})"
            }));
    }

    private static string BuildLegacyBattlefieldRawWords(LegacyScenarioCommandNode command)
    {
        var words = new List<string> { command.CommandId.ToString("X4", CultureInfo.InvariantCulture) };
        foreach (var parameter in command.Parameters)
        {
            if (parameter.Kind == LegacyScenarioParameterKind.Word16)
            {
                words.Add(unchecked((ushort)parameter.IntValue).ToString("X4", CultureInfo.InvariantCulture));
            }
            else if (parameter.Kind == LegacyScenarioParameterKind.VariableArray)
            {
                words.AddRange(parameter.Values.Select(value => unchecked((ushort)value).ToString("X4", CultureInfo.InvariantCulture)));
            }
        }

        return string.Join(" ", words);
    }

    private static string BuildLegacyBattlefieldLayout(LegacyScenarioCommandNode command)
        => command.Parameters.Count == 0
            ? string.Empty
            : string.Join(" ", command.Parameters.Select(parameter => parameter.LayoutCodeHex));

    private static string TrimForBattlefieldPreview(string text, int maxLength)
    {
        var normalized = (text ?? string.Empty).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static ScenarioTextEntry? PickTitleEntry(IReadOnlyList<ScenarioTextEntry> texts, string titleHint)
    {
        if (!string.IsNullOrWhiteSpace(titleHint))
        {
            var exact = texts.FirstOrDefault(x => x.Text.Equals(titleHint, StringComparison.Ordinal));
            if (exact != null) return exact;
        }

        return texts.FirstOrDefault(x => x.Kind == "标题/场所")
               ?? texts.FirstOrDefault(x => x.Kind == "短文本/标题候选")
               ?? texts.FirstOrDefault(x => x.Text.Length <= 24 && !x.HasNewLines);
    }

    private static bool IsBattlefieldRelated(ScenarioStructureRow row)
    {
        if (row.NodeType != "Command候选") return false;
        var text = string.Join(' ', row.CommandName, row.CommandTemplateHint, row.ReferenceHint, row.Annotation);
        string[] keywords =
        {
            "出场", "进入", "坐标", "地点", "地图", "阵营", "等级", "AI", "方针", "武将", "单位",
            "我军", "敌军", "友军", "撤退", "胜利", "失败", "兵种", "能力", "限制", "回合"
        };
        return keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildRoleHint(ScenarioStructureRow row)
    {
        var text = string.Join(' ', row.CommandName, row.CommandTemplateHint, row.ReferenceHint);
        if (ContainsAny(text, "出场", "进入", "坐标", "地点")) return "出场/坐标";
        if (ContainsAny(text, "我军", "敌军", "友军", "阵营")) return "阵营/单位";
        if (ContainsAny(text, "AI", "方针", "等级", "能力", "兵种")) return "AI/等级/能力";
        if (ContainsAny(text, "胜利", "失败", "回合", "撤退")) return "胜败/流程";
        if (ContainsAny(text, "地图")) return "地图/资源";
        return "战场相关";
    }

    private static string BuildCommandAnnotation(ScenarioStructureRow row)
    {
        var role = BuildRoleHint(row);
        return $"{role}命令候选：{row.CommandName}，位置 {row.OffsetHex}。当前用于战场制作定位和人工核对；完整命令长度/参数写回尚未全部确认，暂不强写未知命令结构。";
    }

    private static IReadOnlyList<BattlefieldUnitCandidate> BuildUnitCandidates(IReadOnlyList<BattlefieldCommandCandidate> commands)
    {
        var rows = new List<BattlefieldUnitCandidate>();
        foreach (var command in commands)
        {
            var deploymentRecords = BuildDeploymentRecordCandidates(command, rows.Count);
            if (deploymentRecords.Count > 0)
            {
                rows.AddRange(deploymentRecords);
                continue;
            }

            if (!LooksLikeUnitCandidate(command)) continue;

            rows.Add(BuildGeneralUnitCandidate(command, rows.Count + 1));
        }

        return rows;
    }

    private static string BuildUnitTargetKey(BattlefieldCommandCandidate command)
        => $"Scene={command.SceneIndex};Section={command.SectionIndex};Command={command.CommandIndex};Offset={command.OffsetHex};Id={command.CommandIdHex}";

    private static string BuildUnitTargetKey(BattlefieldCommandCandidate command, int recordIndex)
        => BuildUnitTargetKey(command) + $";Record={recordIndex.ToString(CultureInfo.InvariantCulture)}";

    private static BattlefieldUnitCandidate BuildGeneralUnitCandidate(BattlefieldCommandCandidate command, int index)
        => new()
        {
            Index = index,
            Category = BuildUnitCategory(command),
            SourceCommand = $"{command.CommandIdHex} {command.CommandName}",
            SceneSection = $"Scene {command.SceneIndex} / Section {command.SectionIndex} / Cmd {command.CommandIndex}",
            OffsetHex = command.OffsetHex,
            PersonHint = BuildPersonHint(command),
            CoordinateHint = BuildCoordinateHint(command),
            FactionHint = BuildFactionHint(command),
            AiHint = BuildAiHint(command),
            LevelOrStateHint = BuildLevelOrStateHint(command),
            Annotation = BuildUnitAnnotation(command),
            TargetKey = BuildUnitTargetKey(command)
        };

    private static IReadOnlyList<BattlefieldUnitCandidate> BuildDeploymentRecordCandidates(BattlefieldCommandCandidate command, int existingCount)
    {
        var commandId = ParseCommandId(command.CommandIdHex);
        var definition = commandId switch
        {
            0x46 => DeploymentCommandDefinition.Friend,
            0x47 => DeploymentCommandDefinition.Enemy,
            0x4B => DeploymentCommandDefinition.Ally,
            _ => null
        };
        if (definition == null) return Array.Empty<BattlefieldUnitCandidate>();

        var words = ExtractCommandWords(command).ToList();
        if (words.Count == 0) return Array.Empty<BattlefieldUnitCandidate>();

        var rows = new List<BattlefieldUnitCandidate>();
        for (var recordIndex = 0; recordIndex < definition.RecordCount; recordIndex++)
        {
            var start = recordIndex * definition.GroupSize;
            if (start + definition.GroupSize > words.Count) break;
            var recordWords = words.Skip(start).Take(definition.GroupSize).ToList();
            if (definition.SkipBlankRecords && LooksLikeBlankDeploymentRecord(recordWords)) continue;

            rows.Add(BuildDeploymentRecordCandidate(command, definition, recordIndex, recordWords, existingCount + rows.Count + 1));
        }

        return rows;
    }

    private static BattlefieldUnitCandidate BuildDeploymentRecordCandidate(
        BattlefieldCommandCandidate command,
        DeploymentCommandDefinition definition,
        int recordIndex,
        IReadOnlyList<int> words,
        int index)
    {
        var slotLabel = definition.RecordCount == 1
            ? "单条"
            : $"第 {(recordIndex + 1).ToString(CultureInfo.InvariantCulture)} 条";
        var sourceCommand = $"{command.CommandIdHex} {command.CommandName} {slotLabel}";
        var sceneSection = $"Scene {command.SceneIndex} / Section {command.SectionIndex} / Cmd {command.CommandIndex} / {slotLabel}";

        return new BattlefieldUnitCandidate
        {
            Index = index,
            Category = definition.Category,
            SourceCommand = sourceCommand,
            SceneSection = sceneSection,
            OffsetHex = command.OffsetHex,
            PersonHint = BuildDeploymentPersonHint(definition, words),
            CoordinateHint = BuildDeploymentCoordinateHint(definition, words),
            FactionHint = definition.FactionHint,
            AiHint = BuildDeploymentAiHint(definition, words),
            LevelOrStateHint = BuildDeploymentLevelOrStateHint(definition, words),
            Annotation = BuildDeploymentAnnotation(command, definition, recordIndex, words),
            TargetKey = BuildUnitTargetKey(command, recordIndex)
        };
    }

    private static bool LooksLikeBlankDeploymentRecord(IReadOnlyList<int> words)
    {
        if (words.Count == 0) return true;
        if (words.All(value => value == 0)) return true;
        return words[0] is 0 or -1 && words.Skip(1).All(value => value == 0);
    }

    private static string BuildDeploymentPersonHint(DeploymentCommandDefinition definition, IReadOnlyList<int> words)
    {
        var value = GetWordOrDefault(words, definition.PersonIndex);
        var role = definition.Category.Replace("出场", "人物/部队", StringComparison.Ordinal);
        return $"{role}槽{definition.PersonIndex + 1}：{FormatScriptValue(value)}";
    }

    private static string BuildDeploymentCoordinateHint(DeploymentCommandDefinition definition, IReadOnlyList<int> words)
    {
        if (definition.XIndex < 0 || definition.YIndex < 0) return "该命令没有直接坐标槽；需结合出场位/战前选择核对。";
        var x = GetWordOrDefault(words, definition.XIndex);
        var y = GetWordOrDefault(words, definition.YIndex);
        var slotText = $"X槽{definition.XIndex + 1}={FormatScriptValue(x)}，Y槽{definition.YIndex + 1}={FormatScriptValue(y)}";
        if (x >= 0 && y >= 0 && x <= 60 && y <= 60)
        {
            return $"坐标候选：({x},{y})；{slotText}";
        }

        if (x < 0 || y < 0)
        {
            return $"坐标候选含整型变量：({FormatScriptValue(x)},{FormatScriptValue(y)})；6.5 坐标负数按整型变量引用处理。";
        }

        return $"坐标槽候选：({FormatScriptValue(x)},{FormatScriptValue(y)})；{slotText}；超出常规地图范围，请结合 58/无效坐标技巧或旧工具核对。";
    }

    private static string BuildDeploymentAiHint(DeploymentCommandDefinition definition, IReadOnlyList<int> words)
    {
        if (definition.AiIndex < 0) return "无直接 AI 方针槽。";
        var value = GetWordOrDefault(words, definition.AiIndex);
        var text = value switch
        {
            0 => "被动/默认",
            1 => "主动候选",
            2 => "坚守候选",
            3 => "攻击指定对象候选",
            4 => "到指定点候选",
            5 => "跟随候选",
            6 => "撤离/逃到指定点候选",
            _ => "需旧工具核对"
        };
        return $"AI/方针槽{definition.AiIndex + 1}：{FormatScriptValue(value)}（{text}）";
    }

    private static string BuildDeploymentLevelOrStateHint(DeploymentCommandDefinition definition, IReadOnlyList<int> words)
    {
        var parts = new List<string>();
        foreach (var index in definition.StateIndexes)
        {
            if (index < 0 || index >= words.Count) continue;
            parts.Add($"槽{index + 1}={FormatScriptValue(words[index])}");
        }

        return parts.Count == 0
            ? "无直接等级/状态槽。"
            : "等级/状态/装备候选：" + string.Join(" / ", parts);
    }

    private static string BuildDeploymentAnnotation(
        BattlefieldCommandCandidate command,
        DeploymentCommandDefinition definition,
        int recordIndex,
        IReadOnlyList<int> words)
    {
        var slotLabel = definition.RecordCount == 1
            ? "单条出场记录"
            : $"第 {(recordIndex + 1).ToString(CultureInfo.InvariantCulture)}/{definition.RecordCount.ToString(CultureInfo.InvariantCulture)} 条出场记录";
        return $"{definition.Category}：来源 {command.SourceCommandText()}，位置 {command.OffsetHex}，{slotLabel}。旧源码确认本命令按 {definition.GroupSize} 个 16 位参数为一组" +
               (definition.RecordCount > 1 ? $"循环 {definition.RecordCount} 组" : string.Empty) +
               $"；当前按槽位候选展示，不直接写回坐标/AI/等级。原始槽值：{string.Join(' ', words.Select(FormatScriptValue))}。";
    }

    private static int GetWordOrDefault(IReadOnlyList<int> words, int index)
        => index >= 0 && index < words.Count ? words[index] : 0;

    private static int ParseCommandId(string commandIdHex)
    {
        var text = commandIdHex.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value : -1;
    }

    private static bool LooksLikeUnitCandidate(BattlefieldCommandCandidate command)
    {
        var text = string.Join(' ', command.RoleHint, command.CommandName, command.ParameterPreview, command.CommandTemplateHint, command.ReferenceHint);
        return ContainsAny(text, "出场", "武将出现", "隐藏武将出现", "复活", "坐标", "阵营", "友军", "敌军", "我军", "AI", "方针", "等级", "能力", "兵种", "状态", "撤退");
    }

    private static string BuildUnitCategory(BattlefieldCommandCandidate command)
    {
        var text = string.Join(' ', command.RoleHint, command.CommandName, command.CommandTemplateHint);
        if (ContainsAny(text, "敌军出场")) return "敌军出场";
        if (ContainsAny(text, "友军出场")) return "友军出场";
        if (ContainsAny(text, "我军出场")) return "我军出场";
        if (ContainsAny(text, "隐藏", "伏兵", "复活")) return "伏兵/复活";
        if (ContainsAny(text, "方针", "AI")) return "AI方针";
        if (ContainsAny(text, "等级", "能力", "兵种", "状态")) return "等级/状态";
        if (ContainsAny(text, "坐标", "地点", "移动")) return "坐标/移动";
        return "单位候选";
    }

    private static string BuildPersonHint(BattlefieldCommandCandidate command)
    {
        if (TryExtractReferencePart(command.ReferenceHint, "人物候选", out var personPart)) return personPart;
        if (ContainsAny(command.CommandTemplateHint, "人物", "武将", "部队"))
        {
            var words = ExtractPreviewWords(command).Take(3).Select(x => $"{x}").ToList();
            if (words.Count > 0) return "人物/部队编号候选：" + string.Join(" / ", words);
        }

        return "未解析到人物编号；请结合命令模板和旧剧本编辑器核对。";
    }

    private static string BuildCoordinateHint(BattlefieldCommandCandidate command)
    {
        if (TryExtractReferencePart(command.ReferenceHint, "坐标候选", out var coordinatePart)) return coordinatePart;

        var words = ExtractPreviewWords(command).ToList();
        if (words.Count < 2) return "无坐标候选。";

        var pairs = words
            .Zip(words.Skip(1), (x, y) => new { X = x, Y = y })
            .Where(pair => pair.X <= 60 && pair.Y <= 60)
            .Take(3)
            .Select(pair => $"({pair.X},{pair.Y})")
            .ToList();
        return pairs.Count == 0
            ? "未发现 0..60 范围内的相邻坐标对。"
            : "坐标候选：" + string.Join(" / ", pairs);
    }

    private static string BuildFactionHint(BattlefieldCommandCandidate command)
    {
        var text = string.Join(' ', command.CommandName, command.CommandTemplateHint, command.ReferenceHint);
        if (text.Contains("敌军", StringComparison.Ordinal)) return "阵营候选：敌军";
        if (text.Contains("友军", StringComparison.Ordinal)) return "阵营候选：友军";
        if (text.Contains("我军", StringComparison.Ordinal)) return "阵营候选：我军";
        if (text.Contains("阵营", StringComparison.Ordinal)) return "含阵营槽位；需按模板核对。";
        return "未直接标出阵营。";
    }

    private static string BuildAiHint(BattlefieldCommandCandidate command)
    {
        var text = string.Join(' ', command.CommandName, command.CommandTemplateHint, command.ReferenceHint);
        if (!ContainsAny(text, "AI", "方针")) return "无 AI 方针候选。";

        var word = ExtractPreviewWords(command).Skip(1).FirstOrDefault();
        if (word <= 0 && !ExtractPreviewWords(command).Any()) return "含 AI/方针槽位；请查看参数模板。";
        return word switch
        {
            0 => "AI 方针候选：被动出击",
            1 => "AI 方针候选：主动出击",
            2 => "AI 方针候选：坚守原地",
            3 => "AI 方针候选：攻击武将",
            4 => "AI 方针候选：到指定点",
            5 => "AI 方针候选：跟随武将",
            6 => "AI 方针候选：逃到指定点",
            _ => $"AI/方针参数候选：{word}"
        };
    }

    private static string BuildLevelOrStateHint(BattlefieldCommandCandidate command)
    {
        var text = string.Join(' ', command.CommandName, command.CommandTemplateHint, command.ReferenceHint);
        var words = ExtractPreviewWords(command).Take(5).ToList();
        if (ContainsAny(text, "等级", "经验")) return "等级/经验候选：" + (words.Count == 0 ? "请查看参数模板。" : string.Join(" / ", words));
        if (ContainsAny(text, "能力", "兵种", "状态")) return "能力/兵种/状态候选：" + (words.Count == 0 ? "请查看参数模板。" : string.Join(" / ", words));
        return "无等级/状态候选。";
    }

    private static string BuildUnitAnnotation(BattlefieldCommandCandidate command)
    {
        return $"{BuildUnitCategory(command)}：来源 {command.SourceCommandText()}，位置 {command.OffsetHex}。本表把已知命令名、模板槽位和引用提示整理成战场制作视图，方便核对出场单位、坐标、阵营和 AI；当前不重写未知 R/S eex 命令结构。";
    }

    private static bool TryExtractReferencePart(string text, string label, out string part)
    {
        part = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var segments = text.Split('；', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var match = segments.FirstOrDefault(x => x.StartsWith(label, StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(match)) return false;

        part = match;
        return true;
    }

    private static IEnumerable<int> ExtractPreviewWords(BattlefieldCommandCandidate command)
    {
        foreach (Match match in Regex.Matches(command.ParameterPreview, @"(?<![0-9A-Fa-f])[0-9A-Fa-f]{4}(?![0-9A-Fa-f])"))
        {
            if (int.TryParse(match.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<int> ExtractCommandWords(BattlefieldCommandCandidate command)
    {
        var raw = command.RawContextWordsHex ?? string.Empty;
        var values = new List<int>();
        foreach (Match match in Regex.Matches(raw, @"(?<![0-9A-Fa-f])[0-9A-Fa-f]{4}(?![0-9A-Fa-f])"))
        {
            if (!int.TryParse(match.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) continue;
            values.Add(value > 60000 && value <= 65536 ? value - 65536 : value);
        }

        if (values.Count > 1 && ParseCommandId(command.CommandIdHex) == values[0])
        {
            return values.Skip(1);
        }

        if (values.Count > 0) return values;
        return ExtractPreviewWords(command).Select(value => value > 60000 && value <= 65536 ? value - 65536 : value);
    }

    private static string FormatScriptValue(int value)
    {
        if (value < 0)
        {
            return $"{value.ToString(CultureInfo.InvariantCulture)}(整型变量{Math.Abs(value).ToString(CultureInfo.InvariantCulture)})";
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static bool ContainsAny(string text, params string[] keywords)
        => keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static string BuildSummary(
        ScenarioFileInfo scenario,
        ScenarioTextEntry? titleEntry,
        ScenarioTextEntry? conditionEntry,
        ScenarioMapLinkInfo? mapLink,
        int candidateCount,
        int unitCandidateCount)
    {
        var title = titleEntry?.Text ?? scenario.TitleHint;
        var map = mapLink == null
            ? "未建立地图联动"
            : $"{mapLink.MapId} / {(mapLink.MapImageExists ? mapLink.MapImageName : "缺地图图片")} / {(mapLink.HexzmapBlockExists ? "有地形块" : "缺地形块")}";
        return $"关卡 {scenario.FileName}：标题={title}，地图={map}，胜败条件={(conditionEntry == null ? "未匹配" : conditionEntry.OffsetHex)}，战场命令候选={candidateCount}，出场/坐标候选={unitCandidateCount}。";
    }

    private static string BuildAnnotation(
        ScenarioFileInfo scenario,
        ScenarioTextEntry? titleEntry,
        ScenarioTextEntry? conditionEntry,
        ScenarioMapLinkInfo? mapLink,
        int candidateCount,
        int unitCandidateCount)
    {
        var titleStatus = titleEntry == null ? "未找到标题文本线索" : $"标题偏移 {titleEntry.OffsetHex}，容量 {titleEntry.ByteLength}B";
        var conditionStatus = conditionEntry == null ? "未找到胜败条件文本线索" : $"胜败条件偏移 {conditionEntry.OffsetHex}，容量 {conditionEntry.ByteLength}B";
        var mapStatus = mapLink == null
            ? "未在关卡地图联动中找到同名关卡"
            : $"联动 {mapLink.MapId}，状态 {mapLink.Status}，主地形 {mapLink.DominantTerrain}";
        return $"战场制作文档：{scenario.FileName}。{titleStatus}；{conditionStatus}；{mapStatus}；战场命令候选 {candidateCount} 条，出场/坐标候选 {unitCandidateCount} 条。已知文本支持原地短写回，未知 R/S eex 命令结构保留原样。";
    }

    private sealed class DeploymentCommandDefinition
    {
        public static readonly DeploymentCommandDefinition Friend = new()
        {
            Category = "友军出场",
            FactionHint = "阵营候选：友军",
            GroupSize = 11,
            RecordCount = 20,
            PersonIndex = 0,
            XIndex = 1,
            YIndex = 2,
            AiIndex = 7,
            StateIndexes = [3, 4, 5, 6, 8, 9, 10],
            SkipBlankRecords = true
        };

        public static readonly DeploymentCommandDefinition Enemy = new()
        {
            Category = "敌军出场",
            FactionHint = "阵营候选：敌军",
            GroupSize = 12,
            RecordCount = 80,
            PersonIndex = 0,
            XIndex = 1,
            YIndex = 2,
            AiIndex = 8,
            StateIndexes = [3, 4, 5, 6, 7, 9, 10, 11],
            SkipBlankRecords = true
        };

        public static readonly DeploymentCommandDefinition Ally = new()
        {
            Category = "我军出场",
            FactionHint = "阵营候选：我军",
            GroupSize = 5,
            RecordCount = 1,
            PersonIndex = 0,
            XIndex = 1,
            YIndex = 2,
            AiIndex = -1,
            StateIndexes = [3, 4],
            SkipBlankRecords = false
        };

        public string Category { get; init; } = string.Empty;
        public string FactionHint { get; init; } = string.Empty;
        public int GroupSize { get; init; }
        public int RecordCount { get; init; }
        public int PersonIndex { get; init; }
        public int XIndex { get; init; }
        public int YIndex { get; init; }
        public int AiIndex { get; init; }
        public IReadOnlyList<int> StateIndexes { get; init; } = Array.Empty<int>();
        public bool SkipBlankRecords { get; init; }
    }
}

file static class BattlefieldCommandCandidateExtensions
{
    public static string SourceCommandText(this BattlefieldCommandCandidate command)
        => $"{command.CommandIdHex} {command.CommandName}";
}

