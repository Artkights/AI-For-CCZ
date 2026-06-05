using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class RSceneDraftService
{
    private const string SafetyNoteText = "项目侧 R 场景草稿：保存到 CCZModStudio_Notes，不直接写入 R_XX.eex。30/33 等命令参数槽确认前，仅作为制作预览和旧工具核对依据。";

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

    public IReadOnlyList<RSceneCommandCandidate> BuildCommandCandidates(LegacyScenarioDocument document)
    {
        var result = new List<RSceneCommandCandidate>();
        foreach (var command in document.EnumerateCommands())
        {
            if (!IsRSceneVisualCommand(command.CommandId)) continue;
            var values = FlattenValues(command).ToList();
            var candidate = new RSceneCommandCandidate
            {
                Index = result.Count + 1,
                TargetKey = BuildCommandTargetKey(command),
                SceneSection = $"Scene={command.SceneIndex};Section={command.SectionIndex};Command={command.CommandIndex}",
                OffsetHex = "0x" + command.FileOffset.ToString("X6", CultureInfo.InvariantCulture),
                CommandId = command.CommandId,
                CommandName = command.CommandName,
                RoleHint = BuildRoleHint(command.CommandId),
                ParameterPreview = BuildParameterPreview(command, values),
                PersonId = TryGetPersonId(command.CommandId, values),
                X = TryGetCoordinate(command.CommandId, values, xSlot: true),
                Y = TryGetCoordinate(command.CommandId, values, xSlot: false),
                BackgroundImageNumber = TryGetBackgroundImageNumber(command.CommandId, values),
                Annotation = BuildAnnotation(command.CommandId)
            };
            result.Add(candidate);
        }

        return result;
    }

    private static bool IsRSceneVisualCommand(int commandId)
        => commandId is 0x1C or 0x1E or 0x27 or 0x2F or 0x30 or 0x31 or 0x32 or 0x33 or 0x34 or 0x35;

    private static string BuildRoleHint(int commandId)
        => commandId switch
        {
            0x1C => "绘图",
            0x1E => "武将重绘",
            0x27 => "背景显示",
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
            0x27 => "27 背景显示通常需要配套 1c 绘图；R 场景背景来自 Mmap.e5，图号仍需结合旧工具和实机核对。",
            0x30 => "30 武将出现：常见参数包含人物、坐标、朝向；当前只做候选解析，不强写。",
            0x32 => "32 武将移动：R 场景和 S 战场均可出现；坐标含义需按当前背景核对。",
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
    {
        if (commandId != 0x27 || values.Count == 0) return null;
        var candidate = values.LastOrDefault(x => x is > 0 and <= 999);
        if (candidate > 0) return candidate;
        candidate = values.FirstOrDefault(x => x is > 0 and <= 999);
        return candidate > 0 ? candidate : null;
    }

    private static int? TryGetPersonId(int commandId, IReadOnlyList<int> values)
    {
        if (commandId is not (0x30 or 0x31 or 0x32 or 0x33 or 0x34 or 0x35)) return null;
        if (values.Count == 0) return null;
        var candidate = values[0];
        return candidate is >= 0 and <= 1023 ? candidate : null;
    }

    private static int? TryGetCoordinate(int commandId, IReadOnlyList<int> values, bool xSlot)
    {
        if (commandId is not (0x30 or 0x32)) return null;
        var index = xSlot ? 1 : 2;
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
        => $"Scene={command.SceneIndex};Section={command.SectionIndex};Command={command.CommandIndex};Offset=0x{command.FileOffset:X6};Id=0x{command.CommandId:X2}";

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
        Memo = actor.Memo
    };

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
