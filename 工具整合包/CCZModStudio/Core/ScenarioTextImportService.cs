using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ScenarioTextImportService
{
    private const int CommandDialog = 0x14;
    private const int CommandMapText = 0x2C;
    private const int CommandAppear = 0x30;
    private const int CommandMove = 0x32;
    private const int CommandTurn = 0x33;
    private const int CommandAction = 0x34;
    private const int CommandBattleTurn = 0x4F;
    private const int CommandBattleAction = 0x50;

    private static readonly Regex HeaderRegex = new(
        @"^\s*@(?<name>[A-Za-z0-9_\-\u4e00-\u9fff]+)(?<args>.*)$",
        RegexOptions.Compiled);

    private readonly IReadOnlyDictionary<int, string> _personNames;
    private readonly Dictionary<string, List<int>> _personIdsByName;
    private readonly Encoding _gbk;

    public ScenarioTextImportService(IReadOnlyDictionary<int, string>? personNames = null)
    {
        _personNames = personNames ?? new Dictionary<int, string>();
        _personIdsByName = BuildPersonNameIndex(_personNames);
        _gbk = EncodingService.Gbk;
    }

    public ScenarioTextImportParseResult Parse(
        string input,
        int sceneIndex,
        int sectionIndex,
        Func<int, int, int, LegacyScenarioCommandNode?> createCommand)
    {
        var blocks = ParseBlocks(input);
        var errors = new List<ScenarioTextImportError>();
        var commands = new List<LegacyScenarioCommandNode>();
        var previews = new List<ScenarioTextImportPreviewRow>();

        if (blocks.Count == 0)
        {
            errors.Add(new ScenarioTextImportError(1, "没有找到任何 @命令 块。"));
            return new ScenarioTextImportParseResult(commands, previews, errors);
        }

        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var command = BuildCommand(block, sceneIndex, sectionIndex, createCommand, errors);
            if (command == null) continue;

            commands.Add(command);
            previews.Add(new ScenarioTextImportPreviewRow(
                previews.Count + 1,
                block.StartLine,
                block.CommandName,
                command.CommandIdHex,
                command.CommandName,
                BuildPreview(command)));
        }

        return new ScenarioTextImportParseResult(commands, previews, errors);
    }

    public static string LoadTemplateText(CczProject? project)
    {
        var candidates = new List<string>();
        if (project != null)
        {
            candidates.Add(Path.Combine(project.WorkspaceRoot, "工具整合包", "本地知识库", "08-剧本与战场", "剧本文本导入AI说明模板.md"));
            candidates.Add(Path.Combine(project.GameRoot, "工具整合包", "本地知识库", "08-剧本与战场", "剧本文本导入AI说明模板.md"));
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "剧本文本导入AI说明模板.md"));

        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(path))
            {
                return File.ReadAllText(path, Encoding.UTF8);
            }
        }

        return ScenarioTextImportTemplate.DefaultText;
    }

    public static IReadOnlyDictionary<int, string> LoadPersonNames(CczProject? project, IReadOnlyList<HexTableDefinition>? tables)
    {
        var result = new Dictionary<int, string>();
        if (project == null)
        {
            return result;
        }

        if (tables != null && tables.Count > 0)
        {
            try
            {
                if (HexTableNameResolver.TryResolveForProject(project, tables, "6.5-0 人物", out var table))
                {
                    var read = new HexTableReader().Read(project, table, tables);
                    if (read.Validation.IsUsable && read.Data.Columns.Contains("ID"))
                    {
                        var nameColumn = FindNameColumn(read.Data);
                        if (!string.IsNullOrWhiteSpace(nameColumn))
                        {
                            foreach (DataRow row in read.Data.Rows)
                            {
                                var id = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
                                var name = Convert.ToString(row[nameColumn], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
                                if (!string.IsNullOrWhiteSpace(name))
                                {
                                    result[id] = name;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fall through to the legacy Data.e5 name block below.
            }
        }

        foreach (var pair in LoadLegacyDataPersonNames(project))
        {
            result.TryAdd(pair.Key, pair.Value);
        }

        return result;
    }

    private LegacyScenarioCommandNode? BuildCommand(
        ScenarioTextImportBlock block,
        int sceneIndex,
        int sectionIndex,
        Func<int, int, int, LegacyScenarioCommandNode?> createCommand,
        List<ScenarioTextImportError> errors)
    {
        var normalized = NormalizeCommandName(block.CommandName);
        if (!ValidateAllowedArguments(block, normalized, errors))
        {
            return null;
        }

        return normalized switch
        {
            "dialog" => BuildDialog(block, sceneIndex, sectionIndex, createCommand, errors),
            "narration" => BuildNarration(block, sceneIndex, sectionIndex, createCommand, errors),
            "text" => BuildMapText(block, sceneIndex, sectionIndex, createCommand, errors),
            "appear" => BuildAppear(block, sceneIndex, sectionIndex, createCommand, errors),
            "move" => BuildMove(block, sceneIndex, sectionIndex, createCommand, errors),
            "turn" => BuildTurn(block, sceneIndex, sectionIndex, createCommand, errors),
            "action" => BuildAction(block, sceneIndex, sectionIndex, createCommand, errors),
            "battle-turn" => BuildBattleTurn(block, sceneIndex, sectionIndex, createCommand, errors),
            "battle-action" => BuildBattleAction(block, sceneIndex, sectionIndex, createCommand, errors),
            _ => Error<LegacyScenarioCommandNode>(errors, block.StartLine, $"未知导入命令：@{block.CommandName}。")
        };
    }

    private static bool ValidateAllowedArguments(
        ScenarioTextImportBlock block,
        string normalizedCommandName,
        List<ScenarioTextImportError> errors)
    {
        var allowed = normalizedCommandName switch
        {
            "dialog" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "speaker", "actor", "person", "武将", "人物", "说话人"
            },
            "narration" => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            "text" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "x", "y", "mode", "方式", "显示方式"
            },
            "appear" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "actor", "person", "武将", "人物", "x", "y", "dir", "direction", "朝向", "方向", "action", "gesture", "动作"
            },
            "move" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "mode", "source", "来源", "actor", "person", "武将", "人物", "battle", "battleNo", "战场", "战场编号", "x", "y", "dir", "direction", "朝向", "方向"
            },
            "turn" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "actor", "person", "武将", "人物", "dir", "direction", "朝向", "方向", "action", "gesture", "动作"
            },
            "action" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "actor", "person", "武将", "人物", "action", "gesture", "动作"
            },
            "battle-turn" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "actor", "person", "武将", "人物", "target", "目标", "dir", "direction", "朝向", "方向", "turnDelay", "转向延迟", "preDelay", "before", "动作前延迟", "postDelay", "after", "动作后延迟"
            },
            "battle-action" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "actor", "person", "武将", "人物", "action", "gesture", "动作", "preDelay", "before", "动作前延迟", "postDelay", "after", "动作后延迟", "wait", "等待"
            },
            _ => null
        };

        if (allowed == null)
        {
            return true;
        }

        var unsupported = block.Arguments.Keys
            .Where(key => !allowed.Contains(key))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (unsupported.Count == 0)
        {
            return true;
        }

        errors.Add(new ScenarioTextImportError(
            block.StartLine,
            $"@{block.CommandName} 不支持参数：{string.Join(", ", unsupported)}。请按 AI 说明模板的命令格式生成，不要给旁白/对白添加 x/y/mode 坐标参数。"));
        return false;
    }

    private LegacyScenarioCommandNode? BuildDialog(
        ScenarioTextImportBlock block,
        int sceneIndex,
        int sectionIndex,
        Func<int, int, int, LegacyScenarioCommandNode?> createCommand,
        List<ScenarioTextImportError> errors)
    {
        var body = NormalizeBody(block.Body);
        if (!RequireBody(block, body, errors)) return null;

        var speaker = GetOptionalArgument(block, "speaker", "actor", "person", "武将", "人物", "说话人");
        if (!string.IsNullOrWhiteSpace(speaker))
        {
            if (!TryResolvePerson(speaker!, block.StartLine, "speaker", errors, out var speakerId))
            {
                return null;
            }

            var speakerText = _personNames.TryGetValue(speakerId, out var speakerName) && !string.IsNullOrWhiteSpace(speakerName)
                ? speakerName
                : speaker!.Trim();
            body = $"&{speakerText}\n{body}";
        }

        if (!ValidateGbkText(body, block.StartLine, errors)) return null;
        var command = CreateImportCommand(CommandDialog, sceneIndex, sectionIndex, createCommand, block, errors);
        if (command == null) return null;
        return SetText(command, 0, body, block, errors) ? command : null;
    }

    private LegacyScenarioCommandNode? BuildNarration(
        ScenarioTextImportBlock block,
        int sceneIndex,
        int sectionIndex,
        Func<int, int, int, LegacyScenarioCommandNode?> createCommand,
        List<ScenarioTextImportError> errors)
    {
        var body = NormalizeBody(block.Body);
        if (!RequireBody(block, body, errors)) return null;
        if (!ValidateGbkText(body, block.StartLine, errors)) return null;

        var command = CreateImportCommand(CommandDialog, sceneIndex, sectionIndex, createCommand, block, errors);
        if (command == null) return null;
        return SetText(command, 0, body, block, errors) ? command : null;
    }

    private LegacyScenarioCommandNode? BuildMapText(
        ScenarioTextImportBlock block,
        int sceneIndex,
        int sectionIndex,
        Func<int, int, int, LegacyScenarioCommandNode?> createCommand,
        List<ScenarioTextImportError> errors)
    {
        var body = NormalizeBody(block.Body);
        if (!RequireBody(block, body, errors)) return null;
        if (!ValidateGbkText(body, block.StartLine, errors)) return null;

        if (!TryGetInt(block, errors, out var x, "x") ||
            !TryGetInt(block, errors, out var y, "y") ||
            !TryGetInt(block, errors, out var mode, "mode", "方式", "显示方式"))
        {
            return null;
        }

        var command = CreateImportCommand(CommandMapText, sceneIndex, sectionIndex, createCommand, block, errors);
        if (command == null) return null;
        return SetText(command, 0, body, block, errors) &&
               SetInt(command, 0, x, block, errors) &&
               SetInt(command, 1, y, block, errors) &&
               SetInt(command, 2, mode, block, errors)
            ? command
            : null;
    }

    private LegacyScenarioCommandNode? BuildAppear(
        ScenarioTextImportBlock block,
        int sceneIndex,
        int sectionIndex,
        Func<int, int, int, LegacyScenarioCommandNode?> createCommand,
        List<ScenarioTextImportError> errors)
    {
        if (!TryGetPerson(block, errors, out var actor) ||
            !TryGetInt(block, errors, out var x, "x") ||
            !TryGetInt(block, errors, out var y, "y") ||
            !TryGetDirection(block, errors, out var dir))
        {
            return null;
        }

        var optionalErrorStart = errors.Count;
        var action = TryGetOptionalInt(block, errors, out var parsedAction, "action", "gesture", "动作")
            ? parsedAction
            : 0;
        if (errors.Count > optionalErrorStart) return null;

        var command = CreateImportCommand(CommandAppear, sceneIndex, sectionIndex, createCommand, block, errors);
        if (command == null) return null;
        return SetInt(command, 0, actor, block, errors) &&
               SetInt(command, 1, x, block, errors) &&
               SetInt(command, 2, y, block, errors) &&
               SetInt(command, 3, dir, block, errors) &&
               SetInt(command, 4, action, block, errors)
            ? command
            : null;
    }

    private LegacyScenarioCommandNode? BuildMove(
        ScenarioTextImportBlock block,
        int sceneIndex,
        int sectionIndex,
        Func<int, int, int, LegacyScenarioCommandNode?> createCommand,
        List<ScenarioTextImportError> errors)
    {
        var optionalErrorStart = errors.Count;
        var mode = TryGetOptionalInt(block, errors, out var parsedMode, "mode", "source", "来源") ? parsedMode : 0;
        var actor = 0;
        var hasBattle = TryGetOptionalInt(block, errors, out var battle, "battle", "battleIndex", "battle_id", "战场编号");
        if (errors.Count > optionalErrorStart) return null;
        if (!hasBattle)
        {
            battle = 0;
        }

        if (mode == 1)
        {
            if (!hasBattle)
            {
                errors.Add(new ScenarioTextImportError(block.StartLine, "@move mode=1 时必须提供 battle。"));
                return null;
            }
        }
        else if (!TryGetPerson(block, errors, out actor))
        {
            return null;
        }

        if (!TryGetInt(block, errors, out var x, "x") ||
            !TryGetInt(block, errors, out var y, "y") ||
            !TryGetDirection(block, errors, out var dir))
        {
            return null;
        }

        var command = CreateImportCommand(CommandMove, sceneIndex, sectionIndex, createCommand, block, errors);
        if (command == null) return null;
        return SetInt(command, 0, mode, block, errors) &&
               SetInt(command, 1, actor, block, errors) &&
               SetInt(command, 2, battle, block, errors) &&
               SetInt(command, 3, x, block, errors) &&
               SetInt(command, 4, y, block, errors) &&
               SetInt(command, 5, dir, block, errors)
            ? command
            : null;
    }

    private LegacyScenarioCommandNode? BuildTurn(
        ScenarioTextImportBlock block,
        int sceneIndex,
        int sectionIndex,
        Func<int, int, int, LegacyScenarioCommandNode?> createCommand,
        List<ScenarioTextImportError> errors)
    {
        if (!TryGetPerson(block, errors, out var actor) ||
            !TryGetDirection(block, errors, out var dir))
        {
            return null;
        }

        var optionalErrorStart = errors.Count;
        var action = TryGetOptionalInt(block, errors, out var parsedAction, "action", "gesture", "动作")
            ? parsedAction
            : 0;
        if (errors.Count > optionalErrorStart) return null;

        var command = CreateImportCommand(CommandTurn, sceneIndex, sectionIndex, createCommand, block, errors);
        if (command == null) return null;
        return SetInt(command, 0, actor, block, errors) &&
               SetInt(command, 1, action, block, errors) &&
               SetInt(command, 2, dir, block, errors)
            ? command
            : null;
    }

    private LegacyScenarioCommandNode? BuildAction(
        ScenarioTextImportBlock block,
        int sceneIndex,
        int sectionIndex,
        Func<int, int, int, LegacyScenarioCommandNode?> createCommand,
        List<ScenarioTextImportError> errors)
    {
        if (!TryGetPerson(block, errors, out var actor) ||
            !TryGetInt(block, errors, out var action, "action", "gesture", "动作"))
        {
            return null;
        }

        var command = CreateImportCommand(CommandAction, sceneIndex, sectionIndex, createCommand, block, errors);
        if (command == null) return null;
        return SetInt(command, 0, actor, block, errors) &&
               SetInt(command, 1, action, block, errors)
            ? command
            : null;
    }

    private LegacyScenarioCommandNode? BuildBattleTurn(
        ScenarioTextImportBlock block,
        int sceneIndex,
        int sectionIndex,
        Func<int, int, int, LegacyScenarioCommandNode?> createCommand,
        List<ScenarioTextImportError> errors)
    {
        if (!TryGetPerson(block, errors, out var actor) ||
            !TryGetDirection(block, errors, out var dir))
        {
            return null;
        }

        var optionalErrorStart = errors.Count;
        var target = TryGetOptionalPerson(block, errors, out var parsedTarget, "target", "target_actor", "目标")
            ? parsedTarget
            : actor;
        var turnDelay = TryGetOptionalInt(block, errors, out var parsedTurnDelay, "turnDelay", "turn", "转向延迟") ? parsedTurnDelay : 0;
        var preDelay = TryGetOptionalInt(block, errors, out var parsedPreDelay, "preDelay", "before", "动作前延迟") ? parsedPreDelay : 0;
        var postDelay = TryGetOptionalInt(block, errors, out var parsedPostDelay, "postDelay", "after", "动作后延迟") ? parsedPostDelay : 0;
        if (errors.Count > optionalErrorStart) return null;

        var command = CreateImportCommand(CommandBattleTurn, sceneIndex, sectionIndex, createCommand, block, errors);
        if (command == null) return null;
        return SetInt(command, 0, actor, block, errors) &&
               SetInt(command, 1, target, block, errors) &&
               SetInt(command, 2, dir, block, errors) &&
               SetInt(command, 3, turnDelay, block, errors) &&
               SetInt(command, 4, preDelay, block, errors) &&
               SetInt(command, 5, postDelay, block, errors)
            ? command
            : null;
    }

    private LegacyScenarioCommandNode? BuildBattleAction(
        ScenarioTextImportBlock block,
        int sceneIndex,
        int sectionIndex,
        Func<int, int, int, LegacyScenarioCommandNode?> createCommand,
        List<ScenarioTextImportError> errors)
    {
        if (!TryGetPerson(block, errors, out var actor) ||
            !TryGetInt(block, errors, out var action, "action", "gesture", "动作"))
        {
            return null;
        }

        var optionalErrorStart = errors.Count;
        var preDelay = TryGetOptionalInt(block, errors, out var parsedPreDelay, "preDelay", "before", "动作前延迟") ? parsedPreDelay : 0;
        var postDelay = TryGetOptionalInt(block, errors, out var parsedPostDelay, "postDelay", "after", "动作后延迟") ? parsedPostDelay : 0;
        if (TryGetOptionalInt(block, errors, out var wait, "wait", "等待") && wait != 0)
        {
            postDelay = wait;
        }
        if (errors.Count > optionalErrorStart) return null;

        var command = CreateImportCommand(CommandBattleAction, sceneIndex, sectionIndex, createCommand, block, errors);
        if (command == null) return null;
        return SetInt(command, 0, actor, block, errors) &&
               SetInt(command, 1, action, block, errors) &&
               SetInt(command, 2, preDelay, block, errors) &&
               SetInt(command, 3, postDelay, block, errors)
            ? command
            : null;
    }

    private LegacyScenarioCommandNode? CreateImportCommand(
        int commandId,
        int sceneIndex,
        int sectionIndex,
        Func<int, int, int, LegacyScenarioCommandNode?> createCommand,
        ScenarioTextImportBlock block,
        List<ScenarioTextImportError> errors)
    {
        var command = createCommand(commandId, sceneIndex, sectionIndex);
        if (command != null)
        {
            return command;
        }

        errors.Add(new ScenarioTextImportError(block.StartLine, $"无法创建命令 0x{commandId:X2}。请确认 CczString.ini 已加载且该命令有旧版参数布局。"));
        return null;
    }

    private static bool SetText(
        LegacyScenarioCommandNode command,
        int textParameterOrdinal,
        string text,
        ScenarioTextImportBlock block,
        List<ScenarioTextImportError> errors)
    {
        var parameter = command.Parameters
            .Where(candidate => candidate.Kind == LegacyScenarioParameterKind.Text)
            .ElementAtOrDefault(textParameterOrdinal);
        if (parameter == null)
        {
            errors.Add(new ScenarioTextImportError(block.StartLine, $"{command.CommandIdHex} {command.CommandName} 缺少第 {textParameterOrdinal + 1} 个文本参数。"));
            return false;
        }

        parameter.Text = text;
        parameter.ByteLength = Math.Max(1, EncodingService.GetGbkByteCount(text) + 1);
        return true;
    }

    private static bool SetInt(
        LegacyScenarioCommandNode command,
        int scalarIndex,
        int value,
        ScenarioTextImportBlock block,
        List<ScenarioTextImportError> errors)
    {
        var parameter = command.Parameters
            .Where(candidate => candidate.Kind is LegacyScenarioParameterKind.Word16 or LegacyScenarioParameterKind.Dword32)
            .ElementAtOrDefault(scalarIndex);
        if (parameter == null)
        {
            errors.Add(new ScenarioTextImportError(block.StartLine, $"{command.CommandIdHex} {command.CommandName} 缺少第 {scalarIndex + 1} 个整数参数。"));
            return false;
        }

        if (parameter.Kind == LegacyScenarioParameterKind.Word16 && (value < short.MinValue || value > ushort.MaxValue))
        {
            errors.Add(new ScenarioTextImportError(block.StartLine, $"{command.CommandIdHex} {command.CommandName} 第 {scalarIndex + 1} 个整数参数超出 16 位范围：{value}。"));
            return false;
        }

        parameter.IntValue = value;
        parameter.ByteLength = parameter.Kind == LegacyScenarioParameterKind.Dword32 ? 4 : 2;
        return true;
    }

    private static List<ScenarioTextImportBlock> ParseBlocks(string input)
    {
        var result = new List<ScenarioTextImportBlock>();
        var normalized = (input ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        ScenarioTextImportBlockBuilder? current = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var match = HeaderRegex.Match(line);
            if (match.Success)
            {
                if (current != null)
                {
                    result.Add(current.Build());
                }

                current = new ScenarioTextImportBlockBuilder(
                    i + 1,
                    match.Groups["name"].Value.Trim(),
                    ParseArguments(match.Groups["args"].Value));
                continue;
            }

            if (current == null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                result.Add(new ScenarioTextImportBlock(i + 1, string.Empty, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), line));
                continue;
            }

            current.AppendBodyLine(line);
        }

        if (current != null)
        {
            result.Add(current.Build());
        }

        return result.Where(block => !string.IsNullOrWhiteSpace(block.CommandName)).ToList();
    }

    private static Dictionary<string, string> ParseArguments(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(text ?? string.Empty, @"(?<key>[A-Za-z0-9_\-\u4e00-\u9fff]+)\s*=\s*(?:""(?<q>[^""]*)""|'(?<s>[^']*)'|(?<v>\S+))"))
        {
            var key = match.Groups["key"].Value.Trim();
            var value = match.Groups["q"].Success
                ? match.Groups["q"].Value
                : match.Groups["s"].Success
                    ? match.Groups["s"].Value
                    : match.Groups["v"].Value;
            result[key] = value.Trim();
        }

        return result;
    }

    private static string NormalizeCommandName(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Replace('_', '-');
        return normalized switch
        {
            "对白" or "对话" or "dialogue" or "say" => "dialog",
            "旁白" or "narrator" or "aside" => "narration",
            "地图文字" or "背景文字" or "map-text" or "background-text" => "text",
            "出场" or "武将出场" or "武将出现" => "appear",
            "移动" or "武将移动" => "move",
            "转向" or "武将转向" => "turn",
            "动作" or "武将动作" => "action",
            "战场转向" or "battleturn" => "battle-turn",
            "战场动作" or "battleaction" => "battle-action",
            _ => normalized
        };
    }

    private bool TryGetPerson(ScenarioTextImportBlock block, List<ScenarioTextImportError> errors, out int value)
    {
        var text = GetOptionalArgument(block, "actor", "person", "speaker", "武将", "人物");
        if (string.IsNullOrWhiteSpace(text))
        {
            errors.Add(new ScenarioTextImportError(block.StartLine, $"@{block.CommandName} 缺少 actor 参数。"));
            value = 0;
            return false;
        }

        return TryResolvePerson(text!, block.StartLine, "actor", errors, out value);
    }

    private bool TryGetOptionalPerson(
        ScenarioTextImportBlock block,
        List<ScenarioTextImportError> errors,
        out int value,
        params string[] keys)
    {
        value = 0;
        var text = GetOptionalArgument(block, keys);
        return !string.IsNullOrWhiteSpace(text) && TryResolvePerson(text!, block.StartLine, keys.FirstOrDefault() ?? "actor", errors, out value);
    }

    private bool TryResolvePerson(string text, int line, string label, List<ScenarioTextImportError> errors, out int value)
    {
        if (TryParseInteger(text, LegacyScenarioParameterKind.Word16, out value, out _))
        {
            return true;
        }

        var name = text.Trim();
        if (!_personIdsByName.TryGetValue(name, out var ids) || ids.Count == 0)
        {
            errors.Add(new ScenarioTextImportError(line, $"{label} 未找到人物名称“{name}”。可改用人物 ID。"));
            value = 0;
            return false;
        }

        if (ids.Count > 1)
        {
            errors.Add(new ScenarioTextImportError(line, $"{label} 人物名称“{name}”对应多个 ID：{string.Join(", ", ids.Take(8))}。请改用人物 ID。"));
            value = 0;
            return false;
        }

        value = ids[0];
        return true;
    }

    private static bool TryGetInt(
        ScenarioTextImportBlock block,
        List<ScenarioTextImportError> errors,
        out int value,
        params string[] keys)
    {
        var text = GetOptionalArgument(block, keys);
        if (string.IsNullOrWhiteSpace(text))
        {
            errors.Add(new ScenarioTextImportError(block.StartLine, $"@{block.CommandName} 缺少 {keys[0]} 参数。"));
            value = 0;
            return false;
        }

        if (TryParseInteger(text!, LegacyScenarioParameterKind.Dword32, out value, out var error))
        {
            return true;
        }

        errors.Add(new ScenarioTextImportError(block.StartLine, $"{keys[0]} 参数无效：{error}"));
        return false;
    }

    private static bool TryGetOptionalInt(
        ScenarioTextImportBlock block,
        List<ScenarioTextImportError> errors,
        out int value,
        params string[] keys)
    {
        value = 0;
        var text = GetOptionalArgument(block, keys);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (TryParseInteger(text!, LegacyScenarioParameterKind.Dword32, out value, out var error))
        {
            return true;
        }

        errors.Add(new ScenarioTextImportError(block.StartLine, $"{keys[0]} 参数无效：{error}"));
        return false;
    }

    private static bool TryGetDirection(ScenarioTextImportBlock block, List<ScenarioTextImportError> errors, out int value)
    {
        var text = GetOptionalArgument(block, "dir", "direction", "朝向", "方向");
        if (string.IsNullOrWhiteSpace(text))
        {
            errors.Add(new ScenarioTextImportError(block.StartLine, $"@{block.CommandName} 缺少 dir 参数。"));
            value = 0;
            return false;
        }

        if (TryParseDirection(text!, out value))
        {
            return true;
        }

        errors.Add(new ScenarioTextImportError(block.StartLine, $"dir 参数无效：{text}。可用 up/down/left/right、上/下/左/右、北/南/西/东或数字。"));
        return false;
    }

    private static bool TryParseDirection(string text, out int value)
    {
        var normalized = text.Trim().ToLowerInvariant();
        if (TryParseInteger(normalized, LegacyScenarioParameterKind.Word16, out value, out _))
        {
            return true;
        }

        return normalized switch
        {
            "up" or "u" or "north" or "n" or "上" or "北" => Set(out value, 0),
            "right" or "r" or "east" or "e" or "右" or "东" => Set(out value, 1),
            "down" or "d" or "south" or "s" or "下" or "南" => Set(out value, 2),
            "left" or "l" or "west" or "w" or "左" or "西" => Set(out value, 3),
            _ => false
        };
    }

    private static bool Set(out int target, int value)
    {
        target = value;
        return true;
    }

    private static string? GetOptionalArgument(ScenarioTextImportBlock block, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (block.Arguments.TryGetValue(key, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private bool ValidateGbkText(string text, int line, List<ScenarioTextImportError> errors)
    {
        try
        {
            var bytes = _gbk.GetBytes(text);
            var roundTrip = _gbk.GetString(bytes);
            if (!string.Equals(roundTrip, text, StringComparison.Ordinal))
            {
                errors.Add(new ScenarioTextImportError(line, "正文包含 GBK 无法精确保留的字符，请替换为中文/英文/常用标点。"));
                return false;
            }

            return true;
        }
        catch (EncoderFallbackException ex)
        {
            errors.Add(new ScenarioTextImportError(line, "正文包含 GBK 无法编码的字符：" + ex.Message));
            return false;
        }
    }

    private static bool RequireBody(ScenarioTextImportBlock block, string body, List<ScenarioTextImportError> errors)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            return true;
        }

        errors.Add(new ScenarioTextImportError(block.StartLine, $"@{block.CommandName} 缺少正文。"));
        return false;
    }

    private static string NormalizeBody(string body)
        => (body ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim('\n', '\r');

    private static bool TryParseInteger(string text, LegacyScenarioParameterKind kind, out int value, out string error)
    {
        value = 0;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            error = "请输入整数。";
            return false;
        }

        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var hex = trimmed[2..];
            if (kind == LegacyScenarioParameterKind.Dword32)
            {
                if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
                {
                    error = "十六进制值应为 0x00000000 到 0xFFFFFFFF。";
                    return false;
                }

                value = unchecked((int)parsed);
                error = string.Empty;
                return true;
            }

            if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var word) || word > ushort.MaxValue)
            {
                error = "十六进制值应为 0x0000 到 0xFFFF。";
                return false;
            }

            value = word > 60000 ? unchecked((ushort)word) - 65536 : (int)word;
            error = string.Empty;
            return true;
        }

        if (kind == LegacyScenarioParameterKind.Dword32)
        {
            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signedDword))
            {
                value = signedDword;
                error = string.Empty;
                return true;
            }

            if (uint.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unsignedDword))
            {
                value = unchecked((int)unsignedDword);
                error = string.Empty;
                return true;
            }

            error = "只能填写整数，或 0x 开头的十六进制。";
            return false;
        }

        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var decimalValue))
        {
            error = "只能填写整数，或 0x 开头的十六进制。";
            return false;
        }

        if (kind == LegacyScenarioParameterKind.Word16 && (decimalValue < short.MinValue || decimalValue > ushort.MaxValue))
        {
            error = "16 位整数范围是 -32768 到 65535。";
            return false;
        }

        value = decimalValue;
        error = string.Empty;
        return true;
    }

    private static string BuildPreview(LegacyScenarioCommandNode command)
    {
        var parts = command.Parameters.Select(parameter => parameter.Kind switch
        {
            LegacyScenarioParameterKind.Text => $"T{parameter.Index}=\"{TrimPreview(parameter.Text, 32)}\"",
            LegacyScenarioParameterKind.VariableArray => $"V{parameter.Index}[{parameter.Values.Count}]",
            _ => $"P{parameter.Index}={parameter.IntValue.ToString(CultureInfo.InvariantCulture)}"
        });
        return string.Join(" ", parts);
    }

    private static string TrimPreview(string value, int maxLength)
    {
        var normalized = (value ?? string.Empty)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
        return normalized.Length <= maxLength ? normalized : normalized[..Math.Max(0, maxLength - 1)] + "…";
    }

    private static T? Error<T>(List<ScenarioTextImportError> errors, int line, string message)
        where T : class
    {
        errors.Add(new ScenarioTextImportError(line, message));
        return null;
    }

    private static Dictionary<string, List<int>> BuildPersonNameIndex(IReadOnlyDictionary<int, string> personNames)
    {
        var result = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in personNames)
        {
            var name = pair.Value.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!result.TryGetValue(name, out var ids))
            {
                ids = [];
                result[name] = ids;
            }

            ids.Add(pair.Key);
        }

        foreach (var ids in result.Values)
        {
            ids.Sort();
        }

        return result;
    }

    private static IReadOnlyDictionary<int, string> LoadLegacyDataPersonNames(CczProject project)
    {
        var result = new Dictionary<int, string>();
        var path = Path.Combine(project.GameRoot, "Data.e5");
        if (!File.Exists(path)) return result;

        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[12];
            for (var i = 0; i < 1024; i++)
            {
                var offset = 0x18C + i * 0x20;
                if (stream.Length < offset + buffer.Length) break;
                stream.Position = offset;
                Array.Clear(buffer);
                _ = stream.Read(buffer, 0, buffer.Length);
                var name = EncodingService.DecodeFixedString(buffer).Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    result[i] = name;
                }
            }
        }
        catch
        {
            return new Dictionary<int, string>();
        }

        return result;
    }

    private static string? FindNameColumn(DataTable data)
        => data.Columns.Contains("名称")
            ? "名称"
            : data.Columns.Cast<DataColumn>().FirstOrDefault(column => column.ColumnName.Contains("名", StringComparison.Ordinal))?.ColumnName;

    private sealed class ScenarioTextImportBlockBuilder
    {
        private readonly List<string> _bodyLines = [];

        public ScenarioTextImportBlockBuilder(int startLine, string commandName, IReadOnlyDictionary<string, string> arguments)
        {
            StartLine = startLine;
            CommandName = commandName;
            Arguments = arguments;
        }

        private int StartLine { get; }

        private string CommandName { get; }

        private IReadOnlyDictionary<string, string> Arguments { get; }

        public void AppendBodyLine(string line)
            => _bodyLines.Add(line);

        public ScenarioTextImportBlock Build()
            => new(StartLine, CommandName, Arguments, string.Join("\n", _bodyLines));
    }
}

public sealed record ScenarioTextImportParseResult(
    IReadOnlyList<LegacyScenarioCommandNode> Commands,
    IReadOnlyList<ScenarioTextImportPreviewRow> PreviewRows,
    IReadOnlyList<ScenarioTextImportError> Errors)
{
    public bool Success => Errors.Count == 0 && Commands.Count > 0;
}

public sealed record ScenarioTextImportPreviewRow(
    int Index,
    int Line,
    string SourceCommand,
    string CommandId,
    string CommandName,
    string Parameters);

public sealed record ScenarioTextImportError(int Line, string Message)
{
    public string DisplayText => $"第 {Line} 行：{Message}";
}

internal sealed record ScenarioTextImportBlock(
    int StartLine,
    string CommandName,
    IReadOnlyDictionary<string, string> Arguments,
    string Body);

internal static class ScenarioTextImportTemplate
{
    public const string DefaultText = """
# 剧本文本导入 AI 外部知识

## 你的任务

你是 CCZModStudio 剧本文本导入格式生成器。你会收到作者的剧情要求、人物信息、地图信息、坐标信息或动作意图。你必须把这些输入转成 CCZModStudio“文本导入”功能可直接解析的结构化文本。

如果作者提供的信息足够，你的最终答案只能输出可导入文本，不要输出解释、标题、列表、Markdown、代码围栏或注释。如果作者缺少生成合法指令所必需的信息，你必须输出 `缺少信息：...`，列出最少必要补充项，不要编造人物、坐标、方向或动作编号。

## 输入理解规则

- 先识别作者要求生成的是 R 场景剧情、S 战场事件，还是普通剧本段落。
- 从作者输入中抽取人物、说话人、目标人物、地点、坐标、方向、动作、对白、旁白、地图文字和节奏等待。
- 作者给出人物 ID 时优先使用 ID；只给人物名称时使用名称；存在重名风险或作者明确要求 ID 时必须使用 ID。
- 作者给出地点名但没有坐标时，不要自行估算坐标；输出 `缺少信息：地点“...”的坐标`。
- 作者描述动作但语义无法判断时，输出 `缺少信息：...的动作编号或动作意图`。

## 输出硬性规则

- 每个指令块以 `@命令` 开头，参数写成 `key=value`。
- 正文类命令正文写在下一行；块之间可用空行分隔。
- 最终可导入文本中不要保留尖括号占位符。
- 不要输出本文未列出的命令。
- 每种命令只能使用“命令格式规范”中列出的参数；不要给命令附加无关参数。
- `@narration` 命令行后面不能带任何参数，尤其不能带 `x`、`y`、`mode`。
- `@dialog` 只能带 `speaker`，不能带 `x`、`y`、`mode`。
- 只有 `@text`、`@appear`、`@move` 可以使用 `x/y` 坐标参数。
- 只有 `@text` 可以使用地图文字显示参数 `mode`。
- 正文必须使用 GBK 可保存字符；避免 emoji、罕见符号、特殊数学符号和外文扩展字符。

## 剧情到命令的转换规则

- 作者写人物对白时，生成 `@dialog speaker=<人物>`。
- 作者写环境描述、心理旁白、剧情叙述、无明确说话人的说明时，生成 `@narration`。
- 只有作者明确要求在地图上显示文字、背景文字或提示文字时，才生成 `@text`。
- 作者写“出现”“登场”“现身”“来到某处并站定”时，生成 `@appear`。
- 作者写“走到”“移动到”“退到”“上前到某坐标”时，生成 `@move`。
- 作者写“转身”“面向”“朝向”时，普通剧情生成 `@turn`；战场单位即时朝向设置生成 `@battle-turn`。
- 作者写普通 R 场景姿态，如“下跪”“作揖”“倒下”“哭”“举手”，生成 `@action`。
- 作者写战场单位动作，如“攻击”“受击”“防御”“晕倒”“举起武器”，生成 `@battle-action`。

## 地图坐标理解规则

坐标是地图格子坐标，不是像素。左上角为 `(0,0)`，X 向右增加，Y 向下增加。`@appear` 的 `x/y` 是出场位置，`@move` 的 `x/y` 是目标位置，`@text` 的 `x/y` 是地图文字位置。`@narration` 是无坐标旁白，绝对不要写 `x/y/mode`；`@dialog` 是人物对白，也绝对不要写 `x/y/mode`。不要生成负数坐标；若作者提供地图宽高，生成前必须检查坐标在范围内。

## 人物引用规则

`speaker` 表示对白说话人；`actor` 表示执行出场、移动、转向或动作的人物；`target` 表示战场转向目标人物。`speaker`、`actor`、`target` 均可写人物 ID 或人物名称。推荐使用作者提供的人物 ID；名称重复时必须使用 ID。

## 方向规则

推荐输出英文方向：`up=0=北/上`，`right=1=东/右`，`down=2=南/下`，`left=3=西/左`。`dir` 可写 `up/down/left/right`、`上/下/左/右`、`北/南/西/东` 或数字。旧工具中的“默认方向”不要主动用于文本导入。

## 普通动作编号

普通动作编号用于 `@appear`、`@turn`、`@action`。

| 编号 | 动作 | 推荐语义 |
| --- | --- | --- |
| 0 | 普通 | 常规站立、入场、移动后待机 |
| 1 | 下跪 | 请罪、跪拜、臣服 |
| 2 | 脸红 | 羞愧、尴尬、激动 |
| 3 | 举手 | 示意、响应、举手发言 |
| 4 | 哭 | 悲伤、痛哭 |
| 5 | 伸手 | 递物、阻拦、指向 |
| 6 | 作揖 | 礼节、拜见、军令回应 |
| 7 | 盘坐脸红 | 坐姿羞愧或尴尬 |
| 8 | 盘坐举手 | 坐姿示意 |
| 9 | 盘坐哭 | 坐姿悲伤 |
| 10 | 倒下 | 死亡、重伤、昏倒 |
| 11 | 单膝跪地 | 受伤跪地、宣誓 |
| 12 | 被缚 | 被俘、束缚 |
| 13 | 挥剑扬起 | 举剑、蓄势 |
| 14 | 挥剑劈下 | 劈砍、处决、斩击 |
| 15 | 活埋 | 特殊剧情表现 |
| 16 | 起身 | 从倒地或坐姿恢复 |
| 17 | 单手举起 | 高举物品、宣告 |
| 18 | 未知 | 仅作者明确指定时使用 |
| 19 | 变量 | 仅作者明确指定变量动作时使用 |
| 20 | 无 | 不显示普通姿态 |

## 战场动作编号

战场动作编号用于 `@battle-action`。

| 编号 | 动作 | 推荐语义 |
| --- | --- | --- |
| 0 | 静止 | 战场待机 |
| 1 | 举起武器 | 威吓、准备攻击 |
| 2 | 防御 | 格挡、防守 |
| 3 | 受攻击 | 受击反馈 |
| 4 | 虚弱 | 濒危、疲惫 |
| 5 | 攻击预备 | 攻击前摇 |
| 6 | 攻击 | 普通攻击动作 |
| 7 | 二次攻击 | 连击、追击 |
| 8 | 慢速转圈 | 特殊表现、眩晕式慢转 |
| 9 | 喘气 | 疲劳、虚弱喘息 |
| 10 | 晕倒 | 击倒、昏迷 |
| 11 | 快速转圈 | 特殊表现、快速旋转 |
| 12 | 中速转圈 | 特殊表现、中速旋转 |
| 13 | 无 | 不显示战场动作 |

## 等待与延迟规则

`preDelay=0` 表示动作前无延迟；非 0 表示动作前延迟。`postDelay=0` 表示动作后无延迟；非 0 表示动作后延迟。生成 `@battle-action` 时优先使用 `preDelay` 和 `postDelay`，不要主动使用 `wait`。`wait` 非 0 时等价写入 `postDelay`。`@battle-turn` 的 `turnDelay=0` 表示执行转向；`turnDelay>0` 表示不转向。

## 命令格式规范

只使用以下格式骨架。实际输出时必须替换所有占位符。

| 剧情意图 | 输出格式 | 生成指令 |
| --- | --- | --- |
| 人物对白 | `@dialog speaker=<人物ID或名称>` + 下一行正文 | `0x14 对话` |
| 旁白叙述 | `@narration` + 下一行正文；禁止 `x/y/mode` | `0x14 对话` |
| 地图文字 | `@text x=<X> y=<Y> mode=<显示方式编号>` + 下一行正文；只有地图文字使用坐标 | `0x2C 地图文字显示` |
| 武将出场 | `@appear actor=<人物ID或名称> x=<X> y=<Y> dir=<方向> action=<普通动作编号>` | `0x30 武将出现` |
| 武将移动 | `@move actor=<人物ID或名称> x=<X> y=<Y> dir=<方向> mode=0 battle=0` | `0x32 武将移动` |
| 武将转向 | `@turn actor=<人物ID或名称> dir=<方向> action=<普通动作编号>` | `0x33 武将转向` |
| 武将动作 | `@action actor=<人物ID或名称> action=<普通动作编号>` | `0x34 武将动作` |
| 战场转向 | `@battle-turn actor=<人物ID或名称> target=<人物ID或名称> dir=<方向> turnDelay=0 preDelay=0 postDelay=0` | `0x4F 战场转向设置` |
| 战场动作 | `@battle-action actor=<人物ID或名称> action=<战场动作编号> preDelay=0 postDelay=1` | `0x50 战场动作设定` |

## 生成前自检清单

生成最终答案前检查：人物是否明确；坐标是否来自作者输入且不越界；方向是否有效；动作编号是否来自动作表；旁白是否使用 `@narration`；地图显示文字才使用 `@text`；所有 `@narration` 命令行都没有任何参数，尤其没有 `x=0 y=0 mode=0`；所有 `@dialog` 命令行都没有 `x/y/mode`；只有 `@text/@appear/@move` 使用了 `x/y`；战场动作使用 `@battle-action`；普通场景姿态使用 `@action`；没有输出解释、标题、Markdown、代码围栏、注释或 `<...>` 占位符。
""";
}
