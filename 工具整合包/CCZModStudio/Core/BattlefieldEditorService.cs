using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CCZModStudio.Core;

public sealed class BattlefieldEditorService
{
    private readonly ScenarioTextReader _textReader = new();
    private readonly ScenarioStructureProbeReader _structureReader = new();
    private readonly LegacyScenarioReader _legacyReader = new();
    private readonly LegacyScenarioWriter _legacyWriter = new();

    public BattlefieldEditorDocument Load(
        CczProject project,
        ScenarioFileInfo scenario,
        SceneStringDocument? dictionary,
        IReadOnlyList<HexTableDefinition> tables)
    {
        var texts = File.Exists(scenario.Path)
            ? _textReader.Read(scenario.Path).ToList()
            : new List<ScenarioTextEntry>();
        var titleEntry = PickTitleEntry(texts, scenario.TitleHint);
        var conditionEntry = texts.FirstOrDefault(x => x.Kind == "胜败条件")
                             ?? texts.FirstOrDefault(x => x.Text.Contains("胜利条件", StringComparison.Ordinal)
                                                       || x.Text.Contains("失败条件", StringComparison.Ordinal));
        var candidates = LoadBattlefieldCommandCandidates(project, scenario, dictionary, tables);
        var lookups = BuildDisplayLookups(project, tables);
        var deploymentRecords = BuildDeploymentRecordStates(candidates, lookups);
        var unitCandidates = BuildUnitCandidates(deploymentRecords);

        return new BattlefieldEditorDocument
        {
            Scenario = scenario,
            TextEntries = texts,
            TitleEntry = titleEntry,
            ConditionEntry = conditionEntry,
            CommandCandidates = candidates,
            DeploymentRecords = deploymentRecords,
            UnitCandidates = unitCandidates,
            Summary = BuildSummary(scenario, titleEntry, conditionEntry, candidates.Count, unitCandidates.Count),
            Annotation = BuildAnnotation(scenario, titleEntry, conditionEntry, candidates.Count, unitCandidates.Count)
        };
    }

    public static BattlefieldEditorDocument RebuildFromLegacyDocument(
        BattlefieldEditorDocument current,
        LegacyScenarioDocument legacyDocument,
        CczProject? project = null,
        IReadOnlyList<HexTableDefinition>? tables = null)
    {
        var candidates = BuildBattlefieldCommandCandidates(legacyDocument);
        var lookups = BuildDisplayLookups(project, tables);
        var deploymentRecords = BuildDeploymentRecordStates(candidates, lookups);
        var unitCandidates = BuildUnitCandidates(deploymentRecords);
        return new BattlefieldEditorDocument
        {
            Scenario = current.Scenario,
            TextEntries = current.TextEntries,
            TitleEntry = current.TitleEntry,
            ConditionEntry = current.ConditionEntry,
            CommandCandidates = candidates,
            DeploymentRecords = deploymentRecords,
            UnitCandidates = unitCandidates,
            Summary = BuildSummary(current.Scenario, current.TitleEntry, current.ConditionEntry, candidates.Count, unitCandidates.Count),
            Annotation = BuildAnnotation(current.Scenario, current.TitleEntry, current.ConditionEntry, candidates.Count, unitCandidates.Count)
        };
    }

    public ScenarioTextSaveResult SaveTitleAndConditions(
        CczProject project,
        BattlefieldEditorDocument document,
        string title,
        string conditions,
        SceneStringDocument? dictionary = null)
    {
        if (ScenarioFileReader.IsRsScriptFile(document.Scenario.FileName) && dictionary != null)
        {
            return SaveTitleAndConditionsWithLegacyTree(project, document, title, conditions, dictionary);
        }

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

        var result = new ScenarioTextWriter().SaveInPlace(
            project,
            BuildScenarioRelativePath(document.Scenario),
            entries,
            "战场制作页保存标题/胜败条件前自动备份");
        return BuildTextSaveResult(
            document.TitleEntry == null ? null : result,
            document.ConditionEntry == null ? null : result,
            result.EntriesWritten,
            result.ChangedBytes);
    }

    private ScenarioTextSaveResult SaveTitleAndConditionsWithLegacyTree(
        CczProject project,
        BattlefieldEditorDocument document,
        string title,
        string conditions,
        SceneStringDocument dictionary)
    {
        var legacyDocument = _legacyReader.Read(document.Scenario.Path, dictionary);
        var changed = 0;
        if (document.TitleEntry != null)
        {
            var parameter = FindLegacyTextParameter(legacyDocument, document.TitleEntry.Offset)
                ?? throw new InvalidOperationException($"未能在旧版 S 剧本树中定位标题文本参数：{document.TitleEntry.OffsetHex}。");
            parameter.Text = NormalizeText(title);
            changed++;
        }

        if (document.ConditionEntry != null)
        {
            var parameter = FindLegacyTextParameter(legacyDocument, document.ConditionEntry.Offset)
                ?? throw new InvalidOperationException($"未能在旧版 S 剧本树中定位胜败条件文本参数：{document.ConditionEntry.OffsetHex}。");
            parameter.Text = NormalizeText(conditions);
            changed++;
        }

        if (changed == 0)
        {
            throw new InvalidOperationException("当前关卡没有可安全写回的标题或胜败条件文本。");
        }

        var result = _legacyWriter.Save(
            project,
            BuildScenarioRelativePath(document.Scenario),
            legacyDocument,
            dictionary,
            "战场制作页标题/胜败条件完整结构保存");
        var textSave = new ScenarioTextSaveResult
        {
            FilePath = result.FilePath,
            BackupPath = result.BackupPath,
            ReportJsonPath = result.ReportJsonPath,
            EntriesWritten = changed,
            ChangedBytes = result.ChangedBytes
        };
        return BuildTextSaveResult(
            document.TitleEntry == null ? null : textSave,
            document.ConditionEntry == null ? null : textSave,
            changed,
            result.ChangedBytes);
    }

    private static ScenarioTextSaveResult BuildTextSaveResult(
        ScenarioTextSaveResult? titleSave,
        ScenarioTextSaveResult? conditionSave,
        int entriesWritten,
        int changedBytes)
    {
        var primary = titleSave ?? conditionSave ?? new ScenarioTextSaveResult();
        return new ScenarioTextSaveResult
        {
            FilePath = primary.FilePath,
            BackupPath = primary.BackupPath,
            ReportJsonPath = primary.ReportJsonPath,
            EntriesWritten = entriesWritten,
            ChangedBytes = changedBytes,
            TitleSave = titleSave,
            ConditionSave = conditionSave,
            BackupPaths = new[] { titleSave?.BackupPath, conditionSave?.BackupPath }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToList(),
            ReportJsonPaths = new[] { titleSave?.ReportJsonPath, conditionSave?.ReportJsonPath }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToList()
        };
    }

    private static LegacyScenarioCommandParameter? FindLegacyTextParameter(LegacyScenarioDocument document, int offset)
        => document
            .EnumerateCommands()
            .SelectMany(command => command.TextParameters)
            .FirstOrDefault(parameter => parameter.FileOffset == offset);

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
        if (!entry.IsWritable) return $"{displayName} 的文本候选解码置信度低或来源未确认，当前只读，不能写回。";
        value = NormalizeText(value);
        if (string.IsNullOrWhiteSpace(value)) return $"{displayName} 不能为空。";
        if (value.Contains('\0')) return $"{displayName} 不能包含 NUL/零字节。";
        var byteCount = EncodingService.GetGbkByteCount(value);
        if (byteCount < 4) return $"{displayName} GBK 字节数 {byteCount} 过短，当前安全写回要求至少 4 字节。";
        if (byteCount > entry.ByteLength) return $"{displayName} GBK 字节数 {byteCount} 超过原地容量 {entry.ByteLength}，只能等长或缩短。";
        return null;
    }

    public static string? ValidateStructuredScenarioText(
        ScenarioTextEntry? entry,
        string value,
        string displayName,
        bool allowExpansion)
        => allowExpansion ? ValidateExpandableText(entry, value, displayName) : ValidateTextForEntry(entry, value, displayName);

    private static string? ValidateExpandableText(ScenarioTextEntry? entry, string value, string displayName)
    {
        if (entry == null) return $"{displayName} 没有匹配到可写回文本线索。";
        if (!entry.IsWritable) return $"{displayName} 的文本候选解码置信度低或来源未确认，当前只读，不能写回。";
        value = NormalizeText(value);
        if (string.IsNullOrWhiteSpace(value)) return $"{displayName} 不能为空。";
        if (value.Contains('\0')) return $"{displayName} 不能包含 NUL/零字节。";
        var byteCount = EncodingService.GetGbkByteCount(value);
        if (byteCount < 4) return $"{displayName} GBK 字节数 {byteCount} 过短，当前安全写回要求至少 4 字节。";
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
        var coordinateText = !string.IsNullOrWhiteSpace(candidate.CoordinateDisplay)
            ? candidate.CoordinateDisplay
            : candidate.CoordinateHint;
        var match = Regex.Match(coordinateText ?? string.Empty, @"\((\d{1,3}),\s*(\d{1,3})\)");
        if (!match.Success) return false;
        if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out x)) return false;
        if (!int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out y)) return false;
        return x >= 0 && y >= 0;
    }

    public static bool TryExtractPersonId(BattlefieldUnitCandidate candidate, out int personId)
    {
        if (candidate.IsPersonVariable || candidate.PersonHint.Contains("空出场槽", StringComparison.Ordinal))
        {
            personId = 0;
            return false;
        }

        if (candidate.PersonId >= 0)
        {
            personId = candidate.PersonId;
            return true;
        }

        return TryExtractPersonId(candidate.PersonHint, out personId);
    }

    public static bool TryExtractPersonId(string text, out int personId)
    {
        personId = 0;
        text ??= string.Empty;
        var colon = Math.Max(text.LastIndexOf('：'), text.LastIndexOf(':'));
        var valueText = colon >= 0 && colon + 1 < text.Length ? text[(colon + 1)..] : text;
        var match = Regex.Match(valueText, @"-?\d+");
        if (!match.Success ||
            !int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out personId) ||
            personId < 0)
        {
            return false;
        }

        return true;
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

    public static IReadOnlyList<BattlefieldDeploymentSlotInfo> BuildDeploymentSlotInfos(BattlefieldEditorDocument document)
    {
        if (document.DeploymentRecords.Count > 0)
        {
            return document.DeploymentRecords
                .Select(BuildDeploymentSlotInfo)
                .ToList();
        }

        return BuildDeploymentRecordStates(document.CommandCandidates)
            .Select(BuildDeploymentSlotInfo)
            .ToList();
    }

    public static IReadOnlyList<BattlefieldDeploymentSlotInfo> BuildDeploymentSlotInfos(BattlefieldCommandCandidate command)
        => BuildDeploymentRecordStates(new[] { command })
            .Select(BuildDeploymentSlotInfo)
            .ToList();

    public static IReadOnlyList<BattlefieldDeploymentRecordState> BuildDeploymentRecordStates(IReadOnlyList<BattlefieldCommandCandidate> commands)
        => BuildDeploymentRecordStates(commands, BattlefieldDisplayLookups.Empty);

    private static IReadOnlyList<BattlefieldDeploymentRecordState> BuildDeploymentRecordStates(
        IReadOnlyList<BattlefieldCommandCandidate> commands,
        BattlefieldDisplayLookups lookups)
    {
        var rows = new List<BattlefieldDeploymentRecordState>();
        var sameCommandCounts = new Dictionary<int, int>();
        foreach (var command in commands)
        {
            var commandId = ParseCommandId(command.CommandIdHex);
            var definition = BattlefieldDeploymentRecordDefinition.FromCommandId(commandId);
            if (definition == null)
            {
                continue;
            }

            sameCommandCounts.TryGetValue(commandId, out var precedingSameCommandCount);
            sameCommandCounts[commandId] = precedingSameCommandCount + 1;

            var words = ExtractCommandWords(command).ToList();
            if (words.Count == 0) continue;

            for (var recordIndex = 0; recordIndex < definition.RecordCount; recordIndex++)
            {
                var start = recordIndex * definition.GroupSize;
                if (start + definition.GroupSize > words.Count) break;

                var recordWords = words.Skip(start).Take(definition.GroupSize).ToList();
                rows.Add(BuildDeploymentRecordState(command, definition, commandId, recordIndex, precedingSameCommandCount, recordWords, lookups));
            }
        }

        return rows;
    }

    private static BattlefieldDeploymentSlotInfo BuildDeploymentSlotInfo(BattlefieldDeploymentRecordState state)
        => new()
        {
            TargetKey = state.TargetKey,
            Category = state.Category,
            CommandId = state.CommandId,
            RecordIndex = state.RecordIndex,
            BattlefieldNumber = state.BattlefieldNumber,
            PersonOrOrder = state.IsAllySlot ? state.PersonRawCode : state.PersonId,
            PersonRawCode = state.PersonRawCode,
            PersonId = state.PersonId,
            GridX = state.GridX,
            GridY = state.GridY,
            Hidden = state.Hidden,
            Reinforcement = state.Reinforcement,
            DirectionCode = state.DirectionCode,
            Direction = state.Direction,
            LevelOffset = state.LevelOffset,
            JobLevelCode = state.JobLevelCode,
            JobLevel = state.JobLevel,
            AiPolicyCode = state.AiPolicyCode,
            AiMode = state.AiMode,
            IsBlank = state.IsBlank,
            IsInitialDeployment = state.IsInitialDeployment,
            WritesPerson = state.WritesPerson,
            WritesAi = state.WritesAi,
            IsAllySlot = state.IsAllySlot
        };

    private static BattlefieldDeploymentRecordState BuildDeploymentRecordState(
        BattlefieldCommandCandidate command,
        BattlefieldDeploymentRecordDefinition definition,
        int commandId,
        int recordIndex,
        int precedingSameCommandCount,
        IReadOnlyList<int> words,
        BattlefieldDisplayLookups lookups)
    {
        var personRawCode = GetWordOrDefault(words, definition.PersonIndex);
        var person = DecodeDeploymentPerson(definition, personRawCode, lookups);
        var targetPersonRawCode = GetWordOrDefault(words, definition.TargetPersonIndex);
        var targetPerson = definition.TargetPersonIndex >= 0
            ? DecodeDeploymentPerson(definition, targetPersonRawCode, lookups)
            : DeploymentPersonDecode.Empty;
        var directionCode = GetWordOrDefault(words, definition.DirectionIndex);
        var jobLevelCode = GetWordOrDefault(words, definition.JobLevelIndex);
        var aiPolicyCode = GetWordOrDefault(words, definition.AiIndex);
        var isAllySlot = ReferenceEquals(definition, BattlefieldDeploymentRecordDefinition.Ally);

        return new BattlefieldDeploymentRecordState
        {
            TargetKey = BuildUnitTargetKey(command, recordIndex),
            Category = definition.Category,
            CommandId = commandId,
            CommandName = command.CommandName,
            SceneIndex = command.SceneIndex,
            SectionIndex = command.SectionIndex,
            CommandIndex = command.CommandIndex,
            OffsetHex = command.OffsetHex,
            RecordIndex = recordIndex,
            BattlefieldNumber = BuildBattlefieldNumber(definition, recordIndex, precedingSameCommandCount, words),
            PersonRawCode = personRawCode,
            PersonId = person.PersonId,
            PersonDisplay = person.Display,
            IsPersonVariable = person.IsVariable,
            PersonVariableAddress = person.VariableAddress,
            Reinforcement = definition.HasReinforcement && GetWordOrDefault(words, definition.ReinforcementIndex) != 0,
            Hidden = definition.HiddenIndex >= 0 && GetWordOrDefault(words, definition.HiddenIndex) != 0,
            GridX = GetWordOrDefault(words, definition.XIndex),
            GridY = GetWordOrDefault(words, definition.YIndex),
            DirectionCode = directionCode,
            Direction = FormatDirectionName(directionCode),
            LevelOffset = GetWordOrDefault(words, definition.LevelIndex),
            JobLevelCode = jobLevelCode,
            JobLevel = definition.JobLevelIndex >= 0 ? FormatJobLevelName(jobLevelCode) : string.Empty,
            AiPolicyCode = aiPolicyCode,
            AiMode = definition.AiIndex >= 0 ? FormatAiModeName(aiPolicyCode) : string.Empty,
            TargetPersonRawCode = targetPersonRawCode,
            TargetPersonId = targetPerson.PersonId,
            TargetPersonDisplay = targetPerson.Display,
            IsTargetPersonVariable = targetPerson.IsVariable,
            TargetPersonVariableAddress = targetPerson.VariableAddress,
            TargetX = GetWordOrDefault(words, definition.TargetXIndex),
            TargetY = GetWordOrDefault(words, definition.TargetYIndex),
            IsBlank = LooksLikeBlankDeploymentRecord(definition, words),
            IsInitialDeployment = command.SceneIndex == 1,
            Values = words.ToArray(),
            WritesPerson = definition.WritesPerson,
            WritesAi = definition.AiIndex >= 0,
            IsAllySlot = isAllySlot
        };
    }

    private static IReadOnlyList<BattlefieldCommandCandidate> BuildBattlefieldCommandCandidates(LegacyScenarioDocument document)
        => document
            .EnumerateCommands()
            .Select(BuildLegacyBattlefieldStructureRow)
            .Where(IsBattlefieldRelated)
            .Select(BuildBattlefieldCommandCandidate)
            .ToList();

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
            return BuildBattlefieldCommandCandidates(document);
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
            OffsetHex = HexDisplayFormatter.FormatOffset(command.FileOffset),
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
            Annotation = $"旧版完整树战场命令候选：{command.CommandName}，位置 {HexDisplayFormatter.FormatOffset(command.FileOffset)}。"
        };
    }

    private static string BuildLegacyBattlefieldParameterPreview(LegacyScenarioCommandNode command)
    {
        if (command.Parameters.Count == 0) return "无参数";
        return string.Join(" ", command.Parameters.Take(16).Select(parameter => parameter.Kind switch
        {
            LegacyScenarioParameterKind.Text => $"T{parameter.Index}=\"{TrimForBattlefieldPreview(parameter.Text, 16)}\"",
            LegacyScenarioParameterKind.VariableArray => $"V{parameter.Index}[{parameter.Values.Count}]=" + string.Join("/", parameter.Values.Take(8).Select(value => HexDisplayFormatter.FormatWord(value))),
            LegacyScenarioParameterKind.Dword32 => $"D{parameter.Index}={HexDisplayFormatter.FormatDword(parameter.IntValue)}",
            _ => $"P{parameter.Index}={HexDisplayFormatter.FormatWord(parameter.IntValue)}"
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
        var words = new List<string> { HexDisplayFormatter.FormatWord(command.CommandId) };
        foreach (var parameter in command.Parameters)
        {
            if (parameter.Kind == LegacyScenarioParameterKind.Word16)
            {
                words.Add(HexDisplayFormatter.FormatWord(unchecked((ushort)parameter.IntValue)));
            }
            else if (parameter.Kind == LegacyScenarioParameterKind.Dword32)
            {
                words.Add(HexDisplayFormatter.FormatDword(unchecked((uint)parameter.IntValue)));
            }
            else if (parameter.Kind == LegacyScenarioParameterKind.VariableArray)
            {
                words.AddRange(parameter.Values.Select(value => HexDisplayFormatter.FormatWord(unchecked((ushort)value))));
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

    private static IReadOnlyList<BattlefieldUnitCandidate> BuildUnitCandidates(
        IReadOnlyList<BattlefieldDeploymentRecordState> deploymentRecords)
    {
        var rows = new List<BattlefieldUnitCandidate>();
        foreach (var state in deploymentRecords)
        {
            var definition = BattlefieldDeploymentRecordDefinition.FromCommandId(state.CommandId);
            if (definition == null || definition.SkipBlankRecords && state.IsBlank) continue;
            if (!IsAllowedBattlefieldNumber(definition, state.BattlefieldNumber)) continue;
            rows.Add(BuildDeploymentRecordCandidate(state, definition, rows.Count + 1));
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

    private static BattlefieldUnitCandidate BuildDeploymentRecordCandidate(
        BattlefieldDeploymentRecordState state,
        BattlefieldDeploymentRecordDefinition definition,
        int index)
    {
        var slotLabel = definition.RecordCount == 1
            ? "单条"
            : $"第 {(state.RecordIndex + 1).ToString(CultureInfo.InvariantCulture)} 条";
        var commandIdHex = HexDisplayFormatter.Format(state.CommandId, 2);
        var sourceCommand = $"{commandIdHex} {state.CommandName} {slotLabel}";
        var sceneSection = $"Scene {state.SceneIndex} / Section {state.SectionIndex} / Cmd {state.CommandIndex} / {slotLabel}";

        return new BattlefieldUnitCandidate
        {
            Index = index,
            BattlefieldNumber = state.BattlefieldNumber,
            PersonId = state.PersonId,
            PersonRawCode = state.PersonRawCode,
            IsPersonVariable = state.IsPersonVariable,
            SourceCommandDisplay = $"{BuildDeploymentSourceCommandDisplay(state.CommandName, definition)} {slotLabel}",
            PersonDisplay = state.IsBlank && !state.IsAllySlot ? string.Empty : state.PersonDisplay,
            CoordinateDisplay = state.GridX >= 0 && state.GridY >= 0 ? $"({state.GridX},{state.GridY})" : $"({FormatScriptValue(state.GridX)},{FormatScriptValue(state.GridY)})",
            FactionDisplay = definition.FactionDisplay,
            AiDisplay = state.AiMode,
            LevelJobDisplay = BuildDeploymentLevelJobDisplay(state),
            DeploymentStatusDisplay = BuildDeploymentStatusDisplay(state),
            PersonRawCodeDisplay = state.IsAllySlot
                ? $"顺序={FormatScriptValue(state.PersonRawCode)}"
                : $"raw={FormatScriptValue(state.PersonRawCode)}",
            DirectionDisplay = state.Direction,
            HiddenDisplay = state.Hidden ? "隐藏" : "正常",
            ReinforcementDisplay = state.Reinforcement ? "援军" : string.Empty,
            Category = definition.Category,
            SourceCommand = sourceCommand,
            SceneSection = sceneSection,
            OffsetHex = state.OffsetHex,
            PersonHint = BuildDeploymentPersonHint(state),
            CoordinateHint = BuildDeploymentCoordinateHint(state),
            FactionHint = definition.FactionHint,
            AiHint = BuildDeploymentAiHint(state),
            LevelOrStateHint = BuildDeploymentLevelOrStateHint(state),
            Annotation = BuildDeploymentAnnotation(state, definition),
            TargetKey = state.TargetKey
        };
    }

    private static bool LooksLikeBlankDeploymentRecord(BattlefieldDeploymentRecordDefinition definition, IReadOnlyList<int> words)
        => BattlefieldDeploymentRecordFormatter.IsBlankRecord(definition, words);

    private static int BuildBattlefieldNumber(
        BattlefieldDeploymentRecordDefinition definition,
        int recordIndex,
        int precedingSameCommandCount,
        IReadOnlyList<int> words)
    {
        if (ReferenceEquals(definition, BattlefieldDeploymentRecordDefinition.Ally))
        {
            return GetWordOrDefault(words, definition.PersonIndex);
        }

        return definition.BattlefieldNumberBase + recordIndex + precedingSameCommandCount * definition.RecordCount;
    }

    private static bool IsAllowedBattlefieldNumber(BattlefieldDeploymentRecordDefinition definition, int battlefieldNumber)
    {
        if (ReferenceEquals(definition, BattlefieldDeploymentRecordDefinition.Ally)) return battlefieldNumber is >= 0 and <= 19;
        if (ReferenceEquals(definition, BattlefieldDeploymentRecordDefinition.Friend)) return battlefieldNumber is >= 20 and <= 59;
        if (ReferenceEquals(definition, BattlefieldDeploymentRecordDefinition.Enemy)) return battlefieldNumber is >= 60 and <= 299;
        return false;
    }

    private static string BuildDeploymentSourceCommandDisplay(string commandName, BattlefieldDeploymentRecordDefinition definition)
    {
        var text = Regex.Replace(commandName ?? string.Empty, @"\b0x[0-9A-Fa-f]+\b", string.Empty, RegexOptions.CultureInvariant);
        text = Regex.Replace(text, @"^\s*[0-9A-Fa-f]{2}\s+", string.Empty, RegexOptions.CultureInvariant).Trim();
        return string.IsNullOrWhiteSpace(text) || text.Equals("Command", StringComparison.OrdinalIgnoreCase)
            ? definition.Category + "设定"
            : text;
    }

    public static int EncodePerson2ScriptCode(int personId)
        => Person2ListToCode(personId);

    public static bool TryDecodePerson2ScriptCode(int code, out int personId)
    {
        personId = -1;
        if (ScriptVariableValueResolver.TryDecodePerson2VariableReference(code, out _)) return false;

        var listIndex = Person2CodeToList(code);
        if (listIndex is < 0 or > 1023) return false;

        personId = listIndex;
        return true;
    }

    private static DeploymentPersonDecode DecodeDeploymentPerson(
        BattlefieldDeploymentRecordDefinition definition,
        int rawCode,
        BattlefieldDisplayLookups lookups)
        => ReferenceEquals(definition, BattlefieldDeploymentRecordDefinition.Ally)
            ? new DeploymentPersonDecode(-1, $"第 {Math.Max(0, rawCode + 1).ToString(CultureInfo.InvariantCulture)} 位", false, null)
            : DecodePerson2(rawCode, lookups);

    private static DeploymentPersonDecode DecodePerson2(int rawCode, BattlefieldDisplayLookups lookups)
    {
        if (ScriptVariableValueResolver.TryDecodePerson2VariableReference(rawCode, out var variableAddress))
        {
            return new DeploymentPersonDecode(
                -1,
                "V" + variableAddress.ToString(CultureInfo.InvariantCulture),
                true,
                variableAddress);
        }

        var personId = Person2CodeToList(rawCode);
        if (personId is < 0 or > 1023)
        {
            return new DeploymentPersonDecode(-1, FormatScriptValue(rawCode), false, null);
        }

        var display = lookups.PersonNames.TryGetValue(personId, out var name) && !string.IsNullOrWhiteSpace(name)
            ? $"{personId.ToString(CultureInfo.InvariantCulture)} {name}"
            : personId.ToString(CultureInfo.InvariantCulture);
        return new DeploymentPersonDecode(personId, display, false, null);
    }

    private static int Person2CodeToList(int value)
        => value >= 0 ? value : value == -1 ? 5374 : 1276 - value;

    private static int Person2ListToCode(int value)
        => value < 1278 ? value : value == 5374 ? -1 : 1276 - value;

    private static string FormatDirectionName(int value)
        => value switch
        {
            0 => "上",
            1 => "右",
            2 => "下",
            3 => "左",
            _ => "未知(" + value.ToString(CultureInfo.InvariantCulture) + ")"
        };

    private static string FormatAiModeName(int value)
        => value switch
        {
            1 => "主动",
            2 => "坚守",
            3 => "攻击",
            4 => "到点",
            5 => "跟随",
            6 => "逃离",
            _ => "AI" + FormatScriptValue(value)
        };

    private static string BuildDeploymentLevelJobDisplay(BattlefieldDeploymentRecordState state)
    {
        var levelName = state.CommandId is 0x46 or 0x47
            ? FormatLevelOffsetName(state.LevelOffset)
            : string.Empty;
        var jobLevelName = state.JobLevel;
        return string.Join(' ', new[] { levelName, jobLevelName }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string BuildDeploymentStatusDisplay(BattlefieldDeploymentRecordState state)
    {
        var parts = new List<string>();
        if (!state.IsInitialDeployment) parts.Add("剧情");
        if (state.Reinforcement) parts.Add("援");
        if (state.Hidden) parts.Add("隐");
        if (state.IsBlank) parts.Add("空");
        return parts.Count == 0 ? "初始" : string.Join("/", parts);
    }

    private static string FormatLevelOffsetName(int value)
        => value >= 0
            ? "+" + value.ToString(CultureInfo.InvariantCulture) + "级"
            : value.ToString(CultureInfo.InvariantCulture) + "级";

    private static string FormatJobLevelName(int value)
        => value switch
        {
            0 => "初级",
            1 => "中级",
            2 => "高级",
            _ => "兵种级" + FormatScriptValue(value)
        };

    private static BattlefieldDisplayLookups BuildDisplayLookups(CczProject? project, IReadOnlyList<HexTableDefinition>? tables)
    {
        if (project == null || tables == null || tables.Count == 0)
        {
            return BattlefieldDisplayLookups.Empty;
        }

        var reader = new HexTableReader();
        var personNames = new Dictionary<int, string>();

        try
        {
            if (HexTableNameResolver.TryResolveForProject(project, tables, "6.5-0 人物", out var personTable))
            {
                var personRead = reader.Read(project, personTable, tables);
                if (personRead.Validation.IsUsable && personRead.Data.Columns.Contains("ID"))
                {
                    var nameColumn = FindNameColumn(personRead.Data);
                    var hasName = !string.IsNullOrWhiteSpace(nameColumn) && personRead.Data.Columns.Contains(nameColumn);
                    foreach (DataRow row in personRead.Data.Rows)
                    {
                        var id = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
                        if (hasName)
                        {
                            var name = Convert.ToString(row[nameColumn], CultureInfo.InvariantCulture)?.Trim();
                            if (!string.IsNullOrWhiteSpace(name)) personNames[id] = name;
                        }
                    }
                }
            }
        }
        catch
        {
            return BattlefieldDisplayLookups.Empty;
        }

        return new BattlefieldDisplayLookups(personNames);
    }

    private static string FindNameColumn(DataTable data)
    {
        foreach (DataColumn column in data.Columns)
        {
            if (column.ColumnName.Contains("名称", StringComparison.Ordinal) ||
                column.ColumnName.Contains("名字", StringComparison.Ordinal) ||
                column.ColumnName.Contains("姓名", StringComparison.Ordinal))
            {
                return column.ColumnName;
            }
        }

        return data.Columns.Count > 1 ? data.Columns[1].ColumnName : string.Empty;
    }

    private static string BuildDeploymentPersonHint(BattlefieldDeploymentRecordState state)
    {
        if (state.IsAllySlot)
        {
            return $"我军出战顺序：{FormatScriptValue(state.PersonRawCode)}（地图标注显示为第 {Math.Max(0, state.PersonRawCode + 1).ToString(CultureInfo.InvariantCulture)} 位）";
        }

        var decoded = state.IsPersonVariable
            ? $"变量引用 V{state.PersonVariableAddress?.ToString(CultureInfo.InvariantCulture) ?? "?"}，不自动绑定具体人物"
            : state.PersonId >= 0 ? $"解析={state.PersonId.ToString(CultureInfo.InvariantCulture)} {state.PersonDisplay}" : "未解析为普通人物";
        return $"{state.Category.Replace("出场", "人物/部队", StringComparison.Ordinal)}槽：{state.PersonId}；原始 Person2 码={FormatScriptValue(state.PersonRawCode)}；{decoded}";
    }

    private static string BuildDeploymentCoordinateHint(BattlefieldDeploymentRecordState state)
    {
        var definition = BattlefieldDeploymentRecordDefinition.FromCommandId(state.CommandId);
        if (definition == null || definition.XIndex < 0 || definition.YIndex < 0) return "该命令没有直接坐标槽；需结合出场位/战前选择核对。";
        var slotText = $"X槽{definition.XIndex + 1}={FormatScriptValue(state.GridX)}，Y槽{definition.YIndex + 1}={FormatScriptValue(state.GridY)}";
        if (state.IsAllySlot)
        {
            return state.GridX >= 0 && state.GridY >= 0
                ? $"我军候选出战位：({state.GridX},{state.GridY})；{slotText}；绑定到 S 侧 4B 时可写回坐标/方向/隐藏，不改出战顺序槽。"
                : $"我军候选出战位含整型变量：({FormatScriptValue(state.GridX)},{FormatScriptValue(state.GridY)})；6.5 坐标负数按整型变量引用处理。";
        }

        if (state.GridX >= 0 && state.GridY >= 0 && state.GridX <= 60 && state.GridY <= 60)
        {
            return $"坐标候选：({state.GridX},{state.GridY})；{slotText}";
        }

        if (state.GridX < 0 || state.GridY < 0)
        {
            return $"坐标候选含整型变量：({FormatScriptValue(state.GridX)},{FormatScriptValue(state.GridY)})；6.5 坐标负数按整型变量引用处理。";
        }

        return $"坐标槽候选：({FormatScriptValue(state.GridX)},{FormatScriptValue(state.GridY)})；{slotText}；超出常规地图范围，请结合 58/无效坐标技巧或旧工具核对。";
    }

    private static string BuildDeploymentAiHint(BattlefieldDeploymentRecordState state)
    {
        var definition = BattlefieldDeploymentRecordDefinition.FromCommandId(state.CommandId);
        if (definition == null) return "无直接 AI 方针槽。";
        if (definition.AiIndex < 0) return "无直接 AI 方针槽。";
        var text = state.AiPolicyCode switch
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
        return $"AI/方针槽{definition.AiIndex + 1}：{FormatScriptValue(state.AiPolicyCode)}（{text}）";
    }

    private static string BuildDeploymentLevelOrStateHint(BattlefieldDeploymentRecordState state)
    {
        var definition = BattlefieldDeploymentRecordDefinition.FromCommandId(state.CommandId);
        if (definition == null) return "无直接等级/状态槽。";

        var parts = new List<string>
        {
            $"隐藏槽{definition.HiddenIndex + 1}={(state.Hidden ? 1 : 0)}",
            $"朝向槽{definition.DirectionIndex + 1}={FormatScriptValue(state.DirectionCode)}({state.Direction})"
        };
        if (definition.HasReinforcement) parts.Insert(0, $"援军槽{definition.ReinforcementIndex + 1}={(state.Reinforcement ? 1 : 0)}");
        if (definition.LevelIndex >= 0) parts.Add($"等级槽{definition.LevelIndex + 1}={FormatScriptValue(state.LevelOffset)}({FormatLevelOffsetName(state.LevelOffset)})");
        if (definition.JobLevelIndex >= 0) parts.Add($"兵种级槽{definition.JobLevelIndex + 1}={FormatScriptValue(state.JobLevelCode)}({state.JobLevel})");
        if (definition.AiIndex >= 0) parts.Add($"AI槽{definition.AiIndex + 1}={FormatScriptValue(state.AiPolicyCode)}({state.AiMode})");
        if (definition.TargetPersonIndex >= 0) parts.Add($"目标人物槽{definition.TargetPersonIndex + 1}={FormatScriptValue(state.TargetPersonRawCode)}({state.TargetPersonDisplay})");
        if (definition.TargetXIndex >= 0) parts.Add($"目标X槽{definition.TargetXIndex + 1}={FormatScriptValue(state.TargetX)}");
        if (definition.TargetYIndex >= 0) parts.Add($"目标Y槽{definition.TargetYIndex + 1}={FormatScriptValue(state.TargetY)}");

        return parts.Count == 0
            ? "无直接等级/状态槽。"
            : "结构化部署状态：" + string.Join(" / ", parts);
    }

    private static string BuildDeploymentAnnotation(
        BattlefieldDeploymentRecordState state,
        BattlefieldDeploymentRecordDefinition definition)
    {
        var slotLabel = definition.RecordCount == 1
            ? "单条出场记录"
            : $"第 {(state.RecordIndex + 1).ToString(CultureInfo.InvariantCulture)}/{definition.RecordCount.ToString(CultureInfo.InvariantCulture)} 条出场记录";
        var previewScope = state.IsInitialDeployment ? "默认进入初始战场预览" : "Scene2+ 剧情/后续记录，不默认混入初始战场预览";
        var legacyAllZeroNote = LooksLikeLegacyAllZeroDeploymentRecord(definition, state.Values)
            ? " Legacy all-zero 46/47 row: interpreted as person id 0; clear the slot to convert it to -1 empty."
            : string.Empty;
        return $"{definition.Category}：来源 {HexDisplayFormatter.Format(state.CommandId, 2)} {state.CommandName}，位置 {state.OffsetHex}，{slotLabel}。旧源码确认本命令按 {definition.GroupSize} 个逻辑参数为一组" +
               (definition.RecordCount > 1 ? $"循环 {definition.RecordCount} 组" : string.Empty) +
               $"；{previewScope}；拖放绑定后可按已确认槽位受控写回，未确认的装备/能力状态槽保持原值。{legacyAllZeroNote}原始槽值：{string.Join(' ', state.Values.Select(FormatScriptValue))}。";
    }

    private static bool LooksLikeLegacyAllZeroDeploymentRecord(
        BattlefieldDeploymentRecordDefinition definition,
        IReadOnlyList<int> values)
        => (ReferenceEquals(definition, BattlefieldDeploymentRecordDefinition.Friend) ||
            ReferenceEquals(definition, BattlefieldDeploymentRecordDefinition.Enemy)) &&
           values.Count > 0 &&
           values.All(value => value == 0);

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
        foreach (Match match in Regex.Matches(raw, @"(?<![0-9A-Fa-f])[0-9A-Fa-f]{4}(?:[0-9A-Fa-f]{4})?(?![0-9A-Fa-f])"))
        {
            if (!long.TryParse(match.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) continue;
            if (match.Value.Length > 4)
            {
                values.Add(value > int.MaxValue ? unchecked((int)(uint)value) : (int)value);
            }
            else
            {
                values.Add(value > 60000 && value <= 65536 ? (int)value - 65536 : (int)value);
            }
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
        int candidateCount,
        int unitCandidateCount)
    {
        var title = titleEntry?.Text ?? scenario.TitleHint;
        return $"关卡 {scenario.FileName}：标题={title}，胜败条件={(conditionEntry == null ? "未匹配" : conditionEntry.OffsetHex)}，战场命令候选={candidateCount}，出场/坐标候选={unitCandidateCount}。";
    }

    private static string BuildAnnotation(
        ScenarioFileInfo scenario,
        ScenarioTextEntry? titleEntry,
        ScenarioTextEntry? conditionEntry,
        int candidateCount,
        int unitCandidateCount)
    {
        var titleStatus = titleEntry == null ? "未找到标题文本线索" : $"标题偏移 {titleEntry.OffsetHex}，容量 {titleEntry.ByteLength}B";
        var conditionStatus = conditionEntry == null ? "未找到胜败条件文本线索" : $"胜败条件偏移 {conditionEntry.OffsetHex}，容量 {conditionEntry.ByteLength}B";
        return $"战场制作文档：{scenario.FileName}。{titleStatus}；{conditionStatus}；战场命令候选 {candidateCount} 条，出场/坐标候选 {unitCandidateCount} 条。已知文本支持原地短写回，未知 R/S eex 命令结构保留原样。";
    }

    private sealed record BattlefieldDisplayLookups(IReadOnlyDictionary<int, string> PersonNames)
    {
        public static readonly BattlefieldDisplayLookups Empty = new(new Dictionary<int, string>());
    }

    private readonly record struct DeploymentPersonDecode(
        int PersonId,
        string Display,
        bool IsVariable,
        int? VariableAddress)
    {
        public static readonly DeploymentPersonDecode Empty = new(-1, string.Empty, false, null);
    }
}

file static class BattlefieldCommandCandidateExtensions
{
    public static string SourceCommandText(this BattlefieldCommandCandidate command)
        => $"{command.CommandIdHex} {command.CommandName}";
}

