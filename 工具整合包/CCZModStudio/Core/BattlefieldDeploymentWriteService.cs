using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Globalization;

namespace CCZModStudio.Core;

public sealed class BattlefieldDeploymentWriteService
{
    private readonly LegacyScenarioReader _reader = new();
    private readonly LegacyScenarioWriter _writer = new();

    public BattlefieldDeploymentWriteResult SaveScriptPlacements(
        CczProject project,
        ScenarioFileInfo scenario,
        SceneStringDocument dictionary,
        IEnumerable<BattlefieldPlacedUnit> placements)
    {
        if (!ScenarioFileReader.IsBattlefieldScriptFile(scenario.FileName))
        {
            throw new InvalidOperationException("出场写回只支持 RS\\S_XX.eex 战场剧本。");
        }

        var requested = placements.ToList();
        var writable = new List<WritablePlacement>();
        var skipped = new List<string>();
        foreach (var placement in requested)
        {
            if (!TryBuildWritablePlacement(placement, out var writablePlacement, out var reason))
            {
                skipped.Add($"{DescribePlacement(placement)}：{reason}");
                continue;
            }

            writable.Add(writablePlacement);
        }

        if (writable.Count == 0)
        {
            throw new InvalidOperationException("没有可写回的 S 剧本出场记录。只有已自动绑定或手动绑定到 S 剧本、TargetKey 含 Scene/Section/Command/Record 的摆放项才能写回。");
        }

        var document = _reader.Read(scenario.Path, dictionary);
        var changes = new List<BattlefieldDeploymentWriteChange>();
        foreach (var placement in writable)
        {
            var command = FindCommand(document, placement.Locator);
            if (command == null)
            {
                skipped.Add($"{DescribePlacement(placement.Source)}：未在旧版命令树找到对应命令。");
                continue;
            }

            var definition = DeploymentDefinition.FromCommandId(command.CommandId);
            if (definition == null)
            {
                skipped.Add($"{DescribePlacement(placement.Source)}：命令 {command.CommandIdHex} 不是 46/47/4B 出场设定。");
                continue;
            }

            if (!definition.HasMapCoordinate)
            {
                skipped.Add($"{DescribePlacement(placement.Source)}：{command.CommandIdHex} 不含地图坐标槽，当前不写回地图摆放。");
                continue;
            }

            if (placement.Locator.RecordIndex < 0 || placement.Locator.RecordIndex >= definition.RecordCount)
            {
                skipped.Add($"{DescribePlacement(placement.Source)}：Record={placement.Locator.RecordIndex} 超出 {command.CommandIdHex} 的记录范围。");
                continue;
            }

            var start = placement.Locator.RecordIndex * definition.GroupSize;
            if (start + definition.GroupSize > command.Parameters.Count)
            {
                skipped.Add($"{DescribePlacement(placement.Source)}：命令参数数量不足，无法定位记录槽位。");
                continue;
            }

            if (definition.WritesPerson)
            {
                SetParameterValue(command, start + definition.PersonIndex, placement.Source.PersonId, LegacyScenarioParameterKind.Word16);
            }
            SetCoordinateParameterValue(command, start + definition.XIndex, placement.Source.GridX);
            SetCoordinateParameterValue(command, start + definition.YIndex, placement.Source.GridY);
            int? aiValue = null;
            if (definition.AiIndex >= 0 && TryMapAiMode(placement.Source.AiMode, out var mappedAi))
            {
                aiValue = mappedAi;
                SetParameterValue(command, start + definition.AiIndex, mappedAi, LegacyScenarioParameterKind.Word16);
            }
            int? directionValue = null;
            if (definition.DirectionIndex >= 0 && TryMapDirection(placement.Source.Direction, out var mappedDirection))
            {
                directionValue = mappedDirection;
                SetParameterValue(command, start + definition.DirectionIndex, mappedDirection, LegacyScenarioParameterKind.Word16);
            }
            int? hiddenValue = null;
            if (definition.HiddenIndex >= 0)
            {
                hiddenValue = placement.Source.Hidden ? 1 : 0;
                SetParameterValue(command, start + definition.HiddenIndex, hiddenValue.Value, LegacyScenarioParameterKind.Word16);
            }

            changes.Add(new BattlefieldDeploymentWriteChange
            {
                TargetKey = placement.Source.TargetKey,
                CommandIdHex = command.CommandIdHex,
                SceneIndex = command.SceneIndex,
                SectionIndex = command.SectionIndex,
                CommandIndex = command.CommandIndex,
                RecordIndex = placement.Locator.RecordIndex,
                PersonId = placement.Source.PersonId,
                GridX = placement.Source.GridX,
                GridY = placement.Source.GridY,
                AiMode = aiValue,
                DirectionMode = directionValue,
                HiddenFlag = hiddenValue,
                Summary = $"{command.CommandIdHex} {command.CommandName} Record={placement.Locator.RecordIndex} " +
                          (definition.WritesPerson ? $"人物={placement.Source.PersonId} " : $"我军出战位={GetParameterValue(command, start + definition.PersonIndex)} ") +
                          $"坐标=({placement.Source.GridX},{placement.Source.GridY})" +
                          (aiValue.HasValue ? $" AI={aiValue.Value}" : " AI=保持原值") +
                          (directionValue.HasValue ? $" 方向={directionValue.Value}" : string.Empty) +
                          (hiddenValue.HasValue ? $" 隐藏={hiddenValue.Value}" : string.Empty)
            });
        }

        if (changes.Count == 0)
        {
            throw new InvalidOperationException("可定位的摆放项均未能写回：\r\n" + string.Join("\r\n", skipped));
        }

        var result = _writer.Save(
            project,
            BuildScenarioRelativePath(scenario),
            document,
            dictionary,
            "战场制作页 46/47/4B 出场坐标记录写回");

        var verifyDocument = _reader.Read(scenario.Path, dictionary);
        ValidateChanges(verifyDocument, changes);

        return new BattlefieldDeploymentWriteResult
        {
            FilePath = result.FilePath,
            BackupPath = result.BackupPath,
            ReportJsonPath = result.ReportJsonPath,
            ChangedBytes = result.ChangedBytes,
            RequestedPlacementCount = requested.Count,
            WrittenRecordCount = changes.Count,
            SkippedRecordCount = skipped.Count,
            ValidationSummary = result.ValidationSummary + $"；Deployment reread OK: {changes.Count} record(s)",
            Changes = changes,
            SkippedReasons = skipped
        };
    }

    public static bool IsScriptPlacementWritable(BattlefieldPlacedUnit placement)
        => TryBuildWritablePlacement(placement, out _, out _);

    private static void ValidateChanges(LegacyScenarioDocument verifyDocument, IReadOnlyList<BattlefieldDeploymentWriteChange> changes)
    {
        foreach (var change in changes)
        {
            var locator = new ScriptCommandLocator(
                change.SceneIndex,
                change.SectionIndex,
                change.CommandIndex,
                string.Empty,
                change.CommandIdHex,
                change.RecordIndex);
            var command = FindCommand(verifyDocument, locator)
                ?? throw new InvalidDataException($"出场写回复读失败：找不到命令 {change.CommandIdHex} Scene={change.SceneIndex} Section={change.SectionIndex} Command={change.CommandIndex}。");
            var definition = DeploymentDefinition.FromCommandId(command.CommandId)
                ?? throw new InvalidDataException($"出场写回复读失败：命令 {command.CommandIdHex} 不是部署命令。");
            if (!definition.HasMapCoordinate)
            {
                throw new InvalidDataException($"出场写回复读失败：命令 {command.CommandIdHex} 不含地图坐标槽。");
            }
            var start = change.RecordIndex * definition.GroupSize;
            if (definition.WritesPerson)
            {
                AssertParameterValue(command, start + definition.PersonIndex, change.PersonId, "人物");
            }
            AssertParameterValue(command, start + definition.XIndex, change.GridX, "X坐标");
            AssertParameterValue(command, start + definition.YIndex, change.GridY, "Y坐标");
            if (change.AiMode.HasValue && definition.AiIndex >= 0)
            {
                AssertParameterValue(command, start + definition.AiIndex, change.AiMode.Value, "AI");
            }
            if (change.DirectionMode.HasValue && definition.DirectionIndex >= 0)
            {
                AssertParameterValue(command, start + definition.DirectionIndex, change.DirectionMode.Value, "方向");
            }
            if (change.HiddenFlag.HasValue && definition.HiddenIndex >= 0)
            {
                AssertParameterValue(command, start + definition.HiddenIndex, change.HiddenFlag.Value, "隐藏标志");
            }
        }
    }

    private static LegacyScenarioCommandNode? FindCommand(LegacyScenarioDocument document, ScriptCommandLocator locator)
    {
        return document.EnumerateCommands().FirstOrDefault(command =>
            command.SceneIndex == locator.SceneIndex &&
            command.SectionIndex == locator.SectionIndex &&
            command.CommandIndex == locator.CommandIndex &&
            (string.IsNullOrWhiteSpace(locator.OffsetHex) || string.Equals("0x" + command.FileOffset.ToString("X6", CultureInfo.InvariantCulture), locator.OffsetHex, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(locator.CommandIdHex) || string.Equals(command.CommandIdHex, locator.CommandIdHex, StringComparison.OrdinalIgnoreCase)));
    }

    private static void SetParameterValue(
        LegacyScenarioCommandNode command,
        int parameterIndex,
        int value,
        LegacyScenarioParameterKind expectedKind)
    {
        if (parameterIndex < 0 || parameterIndex >= command.Parameters.Count)
        {
            throw new InvalidDataException($"{command.CommandIdHex} {command.CommandName} 缺少参数槽 {parameterIndex}。");
        }

        var parameter = command.Parameters[parameterIndex];
        if (parameter.Kind != expectedKind)
        {
            throw new InvalidDataException($"{command.CommandIdHex} {command.CommandName} 参数槽 {parameterIndex} 不是 {DescribeParameterKind(expectedKind)} 参数，当前不写回。");
        }

        if (expectedKind == LegacyScenarioParameterKind.Word16 && (value < -5535 || value > 60000))
        {
            throw new InvalidDataException($"16 位参数值超出当前安全范围：{value}。");
        }

        parameter.IntValue = value;
    }

    private static void SetCoordinateParameterValue(LegacyScenarioCommandNode command, int parameterIndex, int value)
    {
        if (parameterIndex < 0 || parameterIndex >= command.Parameters.Count)
        {
            throw new InvalidDataException($"{command.CommandIdHex} {command.CommandName} 缺少坐标参数槽 {parameterIndex}。");
        }

        var kind = command.Parameters[parameterIndex].Kind;
        if (kind is not (LegacyScenarioParameterKind.Word16 or LegacyScenarioParameterKind.Dword32))
        {
            throw new InvalidDataException($"{command.CommandIdHex} {command.CommandName} 坐标参数槽 {parameterIndex} 不是数值参数，当前不写回。");
        }

        SetParameterValue(command, parameterIndex, value, kind);
    }

    private static void AssertParameterValue(LegacyScenarioCommandNode command, int parameterIndex, int expected, string label)
    {
        if (parameterIndex < 0 || parameterIndex >= command.Parameters.Count)
        {
            throw new InvalidDataException($"出场写回复读失败：{command.CommandIdHex} 缺少 {label} 槽。");
        }

        var actual = command.Parameters[parameterIndex].IntValue;
        if (actual != expected)
        {
            throw new InvalidDataException($"出场写回复读失败：{command.CommandIdHex} {label} expected={expected}, actual={actual}。");
        }
    }

    private static string DescribeParameterKind(LegacyScenarioParameterKind kind)
        => kind switch
        {
            LegacyScenarioParameterKind.Word16 => "16 位",
            LegacyScenarioParameterKind.Dword32 => "32 位",
            LegacyScenarioParameterKind.Text => "文本",
            LegacyScenarioParameterKind.VariableArray => "数组",
            _ => kind.ToString()
        };

    private static bool TryBuildWritablePlacement(BattlefieldPlacedUnit placement, out WritablePlacement writable, out string reason)
    {
        writable = default;
        reason = string.Empty;
        if (placement.GridX < 0 || placement.GridY < 0)
        {
            reason = "坐标无效。";
            return false;
        }

        if (placement.PersonId < 0 || placement.PersonId > 60000)
        {
            reason = "人物编号超出 16 位安全范围。";
            return false;
        }

        if (!TryParseLocator(placement.TargetKey, out var locator))
        {
            reason = "TargetKey 不是 S 剧本候选记录。";
            return false;
        }

        if (!TryParseCommandId(locator.CommandIdHex, out var commandId) ||
            DeploymentDefinition.FromCommandId(commandId)?.HasMapCoordinate != true)
        {
            reason = "TargetKey 不是 46/47/4B 地图坐标出场记录。";
            return false;
        }

        if (locator.RecordIndex < 0)
        {
            reason = "TargetKey 缺少 Record=N。";
            return false;
        }

        writable = new WritablePlacement(placement, locator);
        return true;
    }

    private static bool TryParseLocator(string targetKey, out ScriptCommandLocator locator)
    {
        locator = default;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in (targetKey ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var index = part.IndexOf('=');
            if (index <= 0) continue;
            values[part[..index].Trim()] = part[(index + 1)..].Trim();
        }

        if (!TryGetInt(values, "Scene", out var scene) ||
            !TryGetInt(values, "Section", out var section) ||
            !TryGetInt(values, "Command", out var command) ||
            !TryGetInt(values, "Record", out var record))
        {
            return false;
        }

        values.TryGetValue("Offset", out var offsetHex);
        values.TryGetValue("Id", out var commandIdHex);
        locator = new ScriptCommandLocator(scene, section, command, offsetHex ?? string.Empty, commandIdHex ?? string.Empty, record);
        return true;
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, string> values, string key, out int value)
    {
        value = 0;
        return values.TryGetValue(key, out var text) &&
               int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseCommandId(string commandIdHex, out int commandId)
    {
        commandId = 0;
        var text = (commandIdHex ?? string.Empty).Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out commandId);
    }

    private static bool TryMapAiMode(string aiMode, out int value)
    {
        value = aiMode switch
        {
            "主动" => 1,
            "坚守" => 2,
            "攻击" => 3,
            "到点" => 4,
            "跟随" => 5,
            "逃离" => 6,
            "被动" or "" or null => 0,
            _ => -1
        };
        return value >= 0;
    }

    private static bool TryMapDirection(string direction, out int value)
    {
        value = direction switch
        {
            "上" => 0,
            "右" => 1,
            "下" => 2,
            "左" => 3,
            _ => -1
        };
        return value >= 0;
    }

    private static int GetParameterValue(LegacyScenarioCommandNode command, int parameterIndex)
        => parameterIndex >= 0 && parameterIndex < command.Parameters.Count
            ? command.Parameters[parameterIndex].IntValue
            : 0;

    private static string DescribePlacement(BattlefieldPlacedUnit placement)
        => $"{placement.Name}({placement.PersonId}) @ ({placement.GridX},{placement.GridY}) {placement.TargetKey}";

    private static string BuildScenarioRelativePath(ScenarioFileInfo scenario)
        => Path.Combine("RS", scenario.FileName);

    private readonly record struct WritablePlacement(BattlefieldPlacedUnit Source, ScriptCommandLocator Locator);

    private readonly record struct ScriptCommandLocator(
        int SceneIndex,
        int SectionIndex,
        int CommandIndex,
        string OffsetHex,
        string CommandIdHex,
        int RecordIndex);

    private sealed class DeploymentDefinition
    {
        public static readonly DeploymentDefinition Friend = new()
        {
            GroupSize = 11,
            RecordCount = 20,
            PersonIndex = 0,
            XIndex = 2,
            YIndex = 3,
            AiIndex = 7,
            DirectionIndex = -1,
            HiddenIndex = -1,
            WritesPerson = true
        };

        public static readonly DeploymentDefinition Enemy = new()
        {
            GroupSize = 12,
            RecordCount = 80,
            PersonIndex = 0,
            XIndex = 3,
            YIndex = 4,
            AiIndex = 8,
            DirectionIndex = -1,
            HiddenIndex = -1,
            WritesPerson = true
        };

        public static readonly DeploymentDefinition Ally = new()
        {
            GroupSize = 5,
            RecordCount = 1,
            PersonIndex = 0,
            XIndex = 1,
            YIndex = 2,
            AiIndex = -1,
            DirectionIndex = 3,
            HiddenIndex = 4,
            WritesPerson = false
        };

        public int GroupSize { get; init; }
        public int RecordCount { get; init; }
        public int PersonIndex { get; init; }
        public int XIndex { get; init; }
        public int YIndex { get; init; }
        public int AiIndex { get; init; }
        public int DirectionIndex { get; init; }
        public int HiddenIndex { get; init; }
        public bool WritesPerson { get; init; }
        public bool HasMapCoordinate => XIndex >= 0 && YIndex >= 0;

        public static DeploymentDefinition? FromCommandId(int commandId)
            => commandId switch
            {
                0x46 => Friend,
                0x47 => Enemy,
                0x4B => Ally,
                _ => null
            };
    }
}
