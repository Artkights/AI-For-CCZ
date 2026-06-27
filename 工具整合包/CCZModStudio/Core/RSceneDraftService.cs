using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class RSceneDraftService
{
    private const string SafetyNoteText = "项目侧 R 场景草稿：保存到 CCZModStudio_Notes。旧版源码已确认 0x30 武将出现的人物/X/Y/方向/动作槽位，可由 R 场景页受控写回；其他视觉命令仍以预览和核对为主。";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string GetStorePath(CczProject project)
    {
        var root = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Notes");
        return Path.Combine(root, $"{MakeSafeFileName(project.Name)}_RSceneDrafts.json");
    }

    public IReadOnlyList<RSceneDraft> LoadAll(CczProject project)
    {
        var path = GetStorePath(project);
        if (!File.Exists(path)) return Array.Empty<RSceneDraft>();

        try
        {
            return (JsonSerializer.Deserialize<List<RSceneDraft>>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                       ?? new List<RSceneDraft>())
                .Where(x => !string.IsNullOrWhiteSpace(x.ScenarioFileName))
                .Select(Normalize)
                .OrderByDescending(x => x.UpdatedAtText, StringComparer.Ordinal)
                .ThenBy(x => x.ScenarioFileName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("R 场景草稿 JSON 解析失败，请先备份并检查文件：" + path, ex);
        }
    }

    public RSceneDraft LoadDraft(CczProject project, string scenarioFileName)
    {
        return LoadAll(project).FirstOrDefault(x => x.ScenarioFileName.Equals(scenarioFileName, StringComparison.OrdinalIgnoreCase))
               ?? new RSceneDraft
               {
                   ScenarioFileName = scenarioFileName,
                   SafetyNote = SafetyNoteText
               };
    }

    public string SaveDraft(CczProject project, string scenarioFileName, int backgroundImageNumber, int gridSize, IEnumerable<RScenePlacedActor> actors)
    {
        var path = GetStorePath(project);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var all = LoadAll(project).Select(Clone).ToList();
        all.RemoveAll(x => x.ScenarioFileName.Equals(scenarioFileName, StringComparison.OrdinalIgnoreCase));
        all.Add(new RSceneDraft
        {
            ScenarioFileName = scenarioFileName,
            BackgroundImageNumber = backgroundImageNumber,
            GridSize = Math.Clamp(gridSize, 4, 128),
            UpdatedAtText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            SafetyNote = SafetyNoteText,
            Actors = actors
                .Where(x => x.GridX >= 0 && x.GridY >= 0)
                .OrderBy(x => x.GridY)
                .ThenBy(x => x.GridX)
                .ThenBy(x => x.PersonId)
                .Select(CloneActor)
                .ToList()
        });

        if (File.Exists(path))
        {
            File.Copy(path, BuildUniqueBackupPath(path), overwrite: false);
        }

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(all.Select(Normalize).OrderByDescending(x => x.UpdatedAtText, StringComparer.Ordinal).ToList(), JsonOptions),
            Encoding.UTF8);
        return path;
    }

    public IReadOnlyList<RSceneCommandCandidate> BuildCommandCandidates(
        LegacyScenarioDocument document,
        Func<LegacyScenarioCommandNode, string>? commandDisplayText = null,
        Func<LegacyScenarioCommandNode, string>? parameterDisplayText = null,
        Func<LegacyScenarioCommandNode, int, int?>? personResolver = null)
    {
        var result = new List<RSceneCommandCandidate>();
        foreach (var command in document.EnumerateCommands())
        {
            if (!IsRSceneVisualCommand(command.CommandId)) continue;
            var values = FlattenValues(command).ToList();
            var displayName = commandDisplayText?.Invoke(command);
            var displayParameters = parameterDisplayText?.Invoke(command);
            var backgroundReference = TryGetBackgroundReference(command.CommandId, values);
            var candidate = new RSceneCommandCandidate
            {
                Index = result.Count + 1,
                TargetKey = BuildCommandTargetKey(command),
                SceneSection = $"Scene={command.SceneIndex};Section={command.SectionIndex};Command={command.CommandIndex}",
                OffsetHex = HexDisplayFormatter.FormatOffset(command.FileOffset),
                CommandId = command.CommandId,
                CommandName = string.IsNullOrWhiteSpace(displayName) ? command.CommandName : displayName,
                RoleHint = BuildRoleHint(command.CommandId),
                ParameterPreview = string.IsNullOrWhiteSpace(displayParameters)
                    ? BuildParameterPreview(command, values)
                    : displayParameters,
                PersonId = TryGetPersonId(command.CommandId, values, command, personResolver),
                X = TryGetCoordinate(command.CommandId, values, xSlot: true),
                Y = TryGetCoordinate(command.CommandId, values, xSlot: false),
                BackgroundImageNumber = backgroundReference?.ResolvedImageNumber,
                BackgroundReference = backgroundReference,
                Annotation = BuildAnnotation(command.CommandId)
            };
            result.Add(candidate);
        }

        return result;
    }

    public IReadOnlyList<RSceneStateCandidate> BuildSceneStateCandidates(
        LegacyScenarioDocument document,
        Func<LegacyScenarioCommandNode, int, ScriptVariableValueSnapshot?>? variableSnapshotProvider = null)
    {
        var result = new List<RSceneStateCandidate>();
        foreach (var section in document.Scenes.SelectMany(scene => scene.Sections))
        {
            var commands = section.EnumerateCommands().ToList();
            for (var i = 0; i < commands.Count; i++)
            {
                var command = commands[i];
                if (command.CommandId != 0x27) continue;

                var nextStart = FindNextRSceneBackgroundCommand(commands, i + 1);
                var endCommandIndex = nextStart?.CommandIndex - 1;
                var snapshot = BuildSceneStartSnapshot(section, command);
                var backgroundText = snapshot.BackgroundReference?.DisplayText
                                     ?? (snapshot.BackgroundImageNumber.HasValue
                                         ? "背景 " + snapshot.BackgroundImageNumber.Value.ToString(CultureInfo.InvariantCulture)
                                         : "背景未识别");
                result.Add(new RSceneStateCandidate
                {
                    Index = result.Count + 1,
                    SceneTitle = $"场景 {result.Count + 1}  S{command.SceneIndex}/{command.SectionIndex}/C{command.CommandIndex}",
                    TargetKey = BuildCommandTargetKey(command),
                    SceneIndex = command.SceneIndex,
                    SectionIndex = command.SectionIndex,
                    StartCommandIndex = command.CommandIndex,
                    CurrentCommandIndex = command.CommandIndex,
                    EndCommandIndex = endCommandIndex ?? commands.LastOrDefault()?.CommandIndex ?? command.CommandIndex,
                    OffsetHex = HexDisplayFormatter.FormatOffset(command.FileOffset),
                    BackgroundImageNumber = snapshot.BackgroundImageNumber,
                    BackgroundReference = snapshot.BackgroundReference,
                    ActorCount = snapshot.Actors.Count,
                    MapFaceCount = snapshot.MapFaces.Count,
                    Summary = $"{backgroundText}；人物 {snapshot.Actors.Count}；地图头像 {snapshot.MapFaces.Count}；Section {command.SectionIndex}；Command {command.CommandIndex}-{(endCommandIndex?.ToString(CultureInfo.InvariantCulture) ?? "末尾")}"
                });
            }
        }

        return result;
    }

    private static LegacyScenarioCommandNode? FindNextRSceneBackgroundCommand(
        IReadOnlyList<LegacyScenarioCommandNode> commands,
        int startIndex)
    {
        for (var i = Math.Max(0, startIndex); i < commands.Count; i++)
        {
            if (commands[i].CommandId == 0x27) return commands[i];
        }

        return null;
    }

    private static RSceneStateSnapshot BuildSceneStartSnapshot(
        LegacyScenarioSection section,
        LegacyScenarioCommandNode command)
    {
        var values = FlattenValues(command).ToList();
        var backgroundReference = TryGetBackgroundReference(command.CommandId, values);
        return new RSceneStateSnapshot
        {
            SceneIndex = section.SceneIndex,
            SectionIndex = section.SectionIndex,
            StartCommandIndex = command.CommandIndex,
            CurrentCommandIndex = command.CommandIndex,
            BackgroundImageNumber = backgroundReference?.ResolvedImageNumber,
            BackgroundReference = backgroundReference,
            Actors = [],
            MapFaces = []
        };
    }

    public RSceneStateSnapshot BuildStateSnapshot(
        LegacyScenarioSection section,
        int currentCommandIndex,
        Func<LegacyScenarioCommandNode, int, ScriptVariableValueSnapshot?>? variableSnapshotProvider = null)
    {
        var actors = new Dictionary<int, MutableActorState>();
        var mapFaces = new Dictionary<int, MutableMapFaceState>();
        int? backgroundImageNumber = null;
        RSceneBackgroundReference? backgroundReference = null;
        int? startCommandIndex = null;

        foreach (var command in section.EnumerateCommands().Where(command => command.CommandIndex <= currentCommandIndex))
        {
            var values = FlattenValues(command).ToList();
            var targetKey = BuildCommandTargetKey(command);
            switch (command.CommandId)
            {
                case 0x27:
                    backgroundReference = TryGetBackgroundReference(command.CommandId, values);
                    backgroundImageNumber = backgroundReference?.ResolvedImageNumber;
                    startCommandIndex = command.CommandIndex;
                    actors.Clear();
                    mapFaces.Clear();
                    break;
                case 0x29:
                    ApplyShowMapFace(mapFaces, command, values, targetKey, variableSnapshotProvider?.Invoke(command, 0));
                    break;
                case 0x2A:
                    ApplyMoveMapFace(mapFaces, command, values, targetKey, variableSnapshotProvider?.Invoke(command, 0));
                    break;
                case 0x2B:
                    ApplyHideMapFace(mapFaces, values, variableSnapshotProvider?.Invoke(command, 0));
                    break;
                case 0x2F:
                    actors.Clear();
                    break;
                case 0x30:
                    ApplyShowActor(actors, command, values, targetKey, variableSnapshotProvider?.Invoke(command, 0));
                    break;
                case 0x31:
                    ApplyHideActor(actors, values, variableSnapshotProvider?.Invoke(command, 1));
                    break;
                case 0x32:
                    ApplyMoveActor(actors, command, values, targetKey, variableSnapshotProvider?.Invoke(command, 1));
                    break;
                case 0x33:
                    ApplyTurnActor(actors, command, values, targetKey, variableSnapshotProvider?.Invoke(command, 0));
                    break;
                case 0x34:
                    ApplyActionActor(actors, command, values, targetKey, variableSnapshotProvider?.Invoke(command, 0));
                    break;
            }
        }

        return new RSceneStateSnapshot
        {
            SceneIndex = section.SceneIndex,
            SectionIndex = section.SectionIndex,
            StartCommandIndex = startCommandIndex,
            CurrentCommandIndex = currentCommandIndex,
            BackgroundImageNumber = backgroundImageNumber,
            BackgroundReference = backgroundReference,
            Actors = actors.Values
                .OrderBy(actor => actor.GridY)
                .ThenBy(actor => actor.GridX)
                .ThenBy(actor => actor.PersonId)
                .Select(actor => new RSceneActorState
                {
                    PersonId = actor.PersonId,
                    PersonReference = actor.PersonReference,
                    PersonVariableAddress = actor.PersonVariableAddress,
                    GridX = actor.GridX,
                    GridY = actor.GridY,
                    Facing = actor.Facing,
                    FrameIndex = actor.FrameIndex,
                    TargetKey = actor.TargetKey,
                    LastActionTargetKey = actor.LastActionTargetKey,
                    Source = actor.Source
                })
                .ToList(),
            MapFaces = mapFaces.Values
                .OrderBy(face => face.Y)
                .ThenBy(face => face.X)
                .ThenBy(face => face.PersonId)
                .Select(face => new RSceneMapFaceState
                {
                    PersonId = face.PersonId,
                    PersonReference = face.PersonReference,
                    PersonVariableAddress = face.PersonVariableAddress,
                    X = face.X,
                    Y = face.Y,
                    TargetKey = face.TargetKey,
                    LastActionTargetKey = face.LastActionTargetKey,
                    Source = face.Source
                })
                .ToList()
        };
    }

    private static bool IsRSceneVisualCommand(int commandId)
        => commandId is 0x1C or 0x1E or 0x27 or 0x29 or 0x2A or 0x2B or 0x2F or 0x30 or 0x31 or 0x32 or 0x33 or 0x34 or 0x35;

    private static string BuildRoleHint(int commandId)
        => commandId switch
        {
            0x1C => "绘图",
            0x1E => "武将重绘",
            0x27 => "背景显示",
            0x29 => "地图头像显示",
            0x2A => "地图头像移动",
            0x2B => "地图头像消失",
            0x2F => "清除人物",
            0x30 => "武将出现",
            0x31 => "武将消失",
            0x32 => "武将移动",
            0x33 => "武将转向",
            0x34 => "武将动作",
            0x35 => "形象改变",
            _ => "R场景"
        };

    private static string BuildAnnotation(int commandId)
        => commandId switch
        {
            0x27 => "27 背景显示按旧 UpdateShow 解释：外场景槽1+1，内场景槽3+41，中国地图/战场地图使用对应槽原值。",
            0x29 => "29 地图头像显示：人物/头像与坐标可用于 R 场景视觉核对。",
            0x2A => "2A 地图头像移动：人物/头像与坐标可用于 R 场景视觉核对。",
            0x2B => "2B 地图头像消失：用于清理地图头像。",
            0x30 => "30 武将出现：旧源码 Dialog_48 确认为人物、X、Y、方向、动作；R 场景页可受控写回 X/Y。",
            0x32 => "32 武将移动：旧源码 Dialog_50 为模式、人物/战场编号、X、Y、方向；当前只做候选解析。",
            0x33 => "33 武将转向：方向按上北下南左西右东；当前作为右侧方向预览依据。",
            0x34 => "34 武将动作：动作对应 Pmapobj.e5 的 R 形象帧条。",
            0x35 => "新引擎 R 形象变化优先通过 77/78 整形变量指定；35 仅保留为旧口径候选。",
            _ => "R 场景视觉相关命令候选；参数语义未完整确认前不直接写回。"
        };

    private static string BuildParameterPreview(LegacyScenarioCommandNode command, IReadOnlyList<int> values)
    {
        var valueText = values.Count == 0 ? "无数值参数" : string.Join("/", values.Take(16));
        var textParameters = command.TextParameters
            .Select(x => x.Text.Length > 32 ? x.Text[..32] : x.Text)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(3)
            .ToList();
        return textParameters.Count == 0 ? valueText : valueText + " | 文本=" + string.Join(" / ", textParameters);
    }

    private static int? TryGetBackgroundImageNumber(int commandId, IReadOnlyList<int> values)
        => TryGetBackgroundReference(commandId, values)?.ResolvedImageNumber;

    private static RSceneBackgroundReference? TryGetBackgroundReference(int commandId, IReadOnlyList<int> values)
    {
        if (commandId != 0x27 || values.Count == 0) return null;
        var category = values[0];
        var valueIndex = category + 1;
        if (valueIndex < 0 || valueIndex >= values.Count) return null;

        var rawValue = values[valueIndex];
        var categoryName = category switch
        {
            0 => "外场景",
            1 => "中国地图",
            2 => "内场景",
            3 => "战场地图",
            _ => "未知背景类别"
        };
        var targetKind = category switch
        {
            0 or 2 => "Mmap",
            1 => "WorldMap",
            3 => "BattlefieldMap",
            _ => "Unknown"
        };
        var resolved = category switch
        {
            0 => rawValue + 1,
            2 => rawValue + 41,
            1 or 3 => rawValue,
            _ => rawValue
        };
        if (rawValue < 0 || resolved < 0 || resolved > 999) return null;

        var warning = category switch
        {
            0 or 2 => string.Empty,
            1 => "中国地图不应从 Mmap.e5 背景候选强行选择",
            3 => "战场地图应走 Map\\Mxxx.jpg 或 HmNN.e5 战场底图解析",
            _ => "未知 0x27 背景类别，暂不自动绑定资源"
        };

        return new RSceneBackgroundReference
        {
            Category = category,
            CategoryName = categoryName,
            RawValue = rawValue,
            ResolvedImageNumber = resolved == 0 && category is 0 or 2 ? 1 : resolved,
            TargetResourceKind = targetKind,
            Warning = warning
        };
    }

    private static int? TryGetPersonId(
        int commandId,
        IReadOnlyList<int> values,
        LegacyScenarioCommandNode? command = null,
        Func<LegacyScenarioCommandNode, int, int?>? personResolver = null)
    {
        var slot = commandId switch
        {
            0x29 or 0x2A or 0x30 or 0x33 or 0x34 or 0x35 when values.Count > 0 => 0,
            0x31 when values.Count > 1 && values[0] == 0 => 1,
            0x32 when values.Count > 1 && values[0] != 1 => 1,
            _ => -1
        };
        if (slot < 0 || slot >= values.Count)
        {
            return null;
        }

        if (command != null && personResolver != null)
        {
            var resolved = personResolver(command, slot);
            if (resolved is >= 0 and <= 1023)
            {
                return resolved;
            }
        }

        var candidate = values[slot];
        return candidate is >= 0 and <= 1023 ? candidate : null;
    }

    private static int? TryGetCoordinate(int commandId, IReadOnlyList<int> values, bool xSlot)
    {
        var index = commandId switch
        {
            0x29 or 0x2A or 0x30 => xSlot ? 1 : 2,
            0x32 => xSlot ? 3 : 4,
            _ => -1
        };
        if (index < 0) return null;
        if (values.Count <= index) return null;
        var value = values[index];
        return value is >= 0 and <= 4096 ? value : null;
    }

    private static IEnumerable<int> FlattenValues(LegacyScenarioCommandNode command)
    {
        foreach (var parameter in command.Parameters)
        {
            if (parameter.Kind == LegacyScenarioParameterKind.Text) continue;
            if (parameter.Kind == LegacyScenarioParameterKind.VariableArray)
            {
                foreach (var value in parameter.Values)
                {
                    yield return value;
                }
                continue;
            }

            yield return parameter.IntValue;
        }
    }

    private static string BuildCommandTargetKey(LegacyScenarioCommandNode command)
        => $"Scene={command.SceneIndex};Section={command.SectionIndex};Command={command.CommandIndex};Offset={HexDisplayFormatter.FormatOffset(command.FileOffset)};Id={HexDisplayFormatter.Format(command.CommandId, 2)}";

    private static void ApplyShowActor(
        IDictionary<int, MutableActorState> actors,
        LegacyScenarioCommandNode command,
        IReadOnlyList<int> values,
        string targetKey,
        ScriptVariableValueSnapshot? variableSnapshot)
    {
        if (values.Count < 3) return;
        if (!ScriptVariableValueResolver.TryResolvePerson2Reference(values[0], variableSnapshot, out var personId, out var variableAddress)) return;
        actors[personId] = new MutableActorState
        {
            PersonId = personId,
            PersonReference = values[0],
            PersonVariableAddress = variableAddress,
            GridX = ClampCoordinate(values[1]),
            GridY = ClampCoordinate(values[2]),
            Facing = DirectionToFacing(values.Count > 3 ? values[3] : -1),
            FrameIndex = GestureToFrame(values.Count > 4 ? values[4] : 0),
            TargetKey = targetKey,
            LastActionTargetKey = targetKey,
            Source = $"{command.CommandIdHex} {command.CommandName}"
        };
    }

    private static void ApplyShowMapFace(
        IDictionary<int, MutableMapFaceState> mapFaces,
        LegacyScenarioCommandNode command,
        IReadOnlyList<int> values,
        string targetKey,
        ScriptVariableValueSnapshot? variableSnapshot)
    {
        if (values.Count < 3) return;
        if (!ScriptVariableValueResolver.TryResolvePerson2Reference(values[0], variableSnapshot, out var personId, out var variableAddress)) return;
        mapFaces[personId] = new MutableMapFaceState
        {
            PersonId = personId,
            PersonReference = values[0],
            PersonVariableAddress = variableAddress,
            X = ClampCoordinate(values[1]),
            Y = ClampCoordinate(values[2]),
            TargetKey = targetKey,
            LastActionTargetKey = targetKey,
            Source = $"{command.CommandIdHex} {command.CommandName}"
        };
    }

    private static void ApplyMoveMapFace(
        IDictionary<int, MutableMapFaceState> mapFaces,
        LegacyScenarioCommandNode command,
        IReadOnlyList<int> values,
        string targetKey,
        ScriptVariableValueSnapshot? variableSnapshot)
    {
        if (values.Count < 3) return;
        if (!ScriptVariableValueResolver.TryResolvePerson2Reference(values[0], variableSnapshot, out var personId, out _)) return;
        if (!mapFaces.TryGetValue(personId, out var mapFace)) return;
        mapFace.X = ClampCoordinate(values[1]);
        mapFace.Y = ClampCoordinate(values[2]);
        mapFace.TargetKey = targetKey;
        mapFace.LastActionTargetKey = targetKey;
        mapFace.Source = $"{command.CommandIdHex} {command.CommandName}";
    }

    private static void ApplyHideMapFace(
        IDictionary<int, MutableMapFaceState> mapFaces,
        IReadOnlyList<int> values,
        ScriptVariableValueSnapshot? variableSnapshot)
    {
        if (values.Count == 0) return;
        if (ScriptVariableValueResolver.TryResolvePerson2Reference(values[0], variableSnapshot, out var personId, out _))
        {
            mapFaces.Remove(personId);
        }
    }

    private static void ApplyHideActor(
        IDictionary<int, MutableActorState> actors,
        IReadOnlyList<int> values,
        ScriptVariableValueSnapshot? variableSnapshot)
    {
        if (values.Count == 0) return;
        if (values[0] == 0 && values.Count > 1)
        {
            if (ScriptVariableValueResolver.TryResolvePerson2Reference(values[1], variableSnapshot, out var personId, out _))
            {
                actors.Remove(personId);
            }
        }
        else if (values[0] != 0)
        {
            actors.Clear();
        }
    }

    private static void ApplyMoveActor(
        IDictionary<int, MutableActorState> actors,
        LegacyScenarioCommandNode command,
        IReadOnlyList<int> values,
        string targetKey,
        ScriptVariableValueSnapshot? variableSnapshot)
    {
        if (values.Count < 5 || values[0] == 1) return;
        if (!ScriptVariableValueResolver.TryResolvePerson2Reference(values[1], variableSnapshot, out var personId, out _)) return;
        if (!actors.TryGetValue(personId, out var actor)) return;
        actor.GridX = ClampCoordinate(values[3]);
        actor.GridY = ClampCoordinate(values[4]);
        if (values.Count > 5)
        {
            actor.Facing = DirectionToFacing(values[5]);
        }
        actor.TargetKey = targetKey;
        actor.LastActionTargetKey = targetKey;
        actor.Source = $"{command.CommandIdHex} {command.CommandName}";
    }

    private static void ApplyTurnActor(
        IDictionary<int, MutableActorState> actors,
        LegacyScenarioCommandNode command,
        IReadOnlyList<int> values,
        string targetKey,
        ScriptVariableValueSnapshot? variableSnapshot)
    {
        if (values.Count < 1) return;
        if (!ScriptVariableValueResolver.TryResolvePerson2Reference(values[0], variableSnapshot, out var personId, out _)) return;
        if (!actors.TryGetValue(personId, out var actor)) return;
        if (values.Count > 1)
        {
            actor.FrameIndex = GestureToFrame(values[1]);
        }
        if (values.Count > 2)
        {
            actor.Facing = DirectionToFacing(values[2]);
        }
        actor.TargetKey = targetKey;
        actor.LastActionTargetKey = targetKey;
        actor.Source = $"{command.CommandIdHex} {command.CommandName}";
    }

    private static void ApplyActionActor(
        IDictionary<int, MutableActorState> actors,
        LegacyScenarioCommandNode command,
        IReadOnlyList<int> values,
        string targetKey,
        ScriptVariableValueSnapshot? variableSnapshot)
    {
        if (values.Count < 1) return;
        if (!ScriptVariableValueResolver.TryResolvePerson2Reference(values[0], variableSnapshot, out var personId, out _)) return;
        if (!actors.TryGetValue(personId, out var actor)) return;
        if (values.Count > 1)
        {
            actor.FrameIndex = GestureToFrame(values[1]);
        }
        actor.TargetKey = targetKey;
        actor.LastActionTargetKey = targetKey;
        actor.Source = $"{command.CommandIdHex} {command.CommandName}";
    }

    private static int ClampCoordinate(int value)
        => Math.Clamp(value, 0, 4096);

    private static int GestureToFrame(int value)
        => Math.Clamp(value < 0 ? 0 : value, 0, 19);

    private static string DirectionToFacing(int value)
        => value switch
        {
            0 => "上",
            1 => "右",
            2 => "下",
            3 => "左",
            _ => "下"
        };

    private static RSceneDraft Normalize(RSceneDraft draft)
    {
        draft.ScenarioFileName = draft.ScenarioFileName?.Trim() ?? string.Empty;
        draft.BackgroundImageNumber = Math.Max(0, draft.BackgroundImageNumber);
        draft.GridSize = Math.Clamp(draft.GridSize <= 0 ? 16 : draft.GridSize, 4, 128);
        draft.UpdatedAtText = draft.UpdatedAtText?.Trim() ?? string.Empty;
        draft.SafetyNote = string.IsNullOrWhiteSpace(draft.SafetyNote) ? SafetyNoteText : draft.SafetyNote.Trim();
        draft.Actors = draft.Actors?.Select(CloneActor).ToList() ?? [];
        return draft;
    }

    private static RSceneDraft Clone(RSceneDraft draft) => new()
    {
        ScenarioFileName = draft.ScenarioFileName,
        BackgroundImageNumber = draft.BackgroundImageNumber,
        GridSize = draft.GridSize,
        UpdatedAtText = draft.UpdatedAtText,
        SafetyNote = draft.SafetyNote,
        Actors = draft.Actors.Select(CloneActor).ToList()
    };

    private static RScenePlacedActor CloneActor(RScenePlacedActor actor) => new()
    {
        TargetKey = actor.TargetKey,
        PersonId = actor.PersonId,
        Name = actor.Name,
        JobId = actor.JobId,
        JobName = actor.JobName,
        RImageId = actor.RImageId,
        SImageId = actor.SImageId,
        Facing = string.IsNullOrWhiteSpace(actor.Facing) ? "下" : actor.Facing,
        FrameIndex = Math.Clamp(actor.FrameIndex, 0, 999),
        GridX = actor.GridX,
        GridY = actor.GridY,
        PixelX = actor.PixelX,
        PixelY = actor.PixelY,
        Source = actor.Source,
        ActorNote = actor.ActorNote,
        LastActionTargetKey = actor.LastActionTargetKey
    };

    private sealed class MutableActorState
    {
        public int PersonId { get; init; }
        public int PersonReference { get; init; }
        public int? PersonVariableAddress { get; init; }
        public int GridX { get; set; }
        public int GridY { get; set; }
        public string Facing { get; set; } = "下";
        public int FrameIndex { get; set; }
        public string TargetKey { get; set; } = string.Empty;
        public string LastActionTargetKey { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }

    private sealed class MutableMapFaceState
    {
        public int PersonId { get; init; }
        public int PersonReference { get; init; }
        public int? PersonVariableAddress { get; init; }
        public int X { get; set; }
        public int Y { get; set; }
        public string TargetKey { get; set; } = string.Empty;
        public string LastActionTargetKey { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }

    private static string BuildUniqueBackupPath(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var baseName = Path.GetFileNameWithoutExtension(path);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var candidate = Path.Combine(directory, $"{baseName}_{stamp}.bak.json");
        for (var index = 1; File.Exists(candidate); index++)
        {
            candidate = Path.Combine(directory, $"{baseName}_{stamp}_{index}.bak.json");
        }
        return candidate;
    }

    private static string MakeSafeFileName(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(ch, '_');
        }
        return string.IsNullOrWhiteSpace(name) ? "Project" : name;
    }
}
