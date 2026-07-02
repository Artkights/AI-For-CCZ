using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CCZModStudio.Core;

public sealed partial class BattlefieldAllyDeploymentSlotService
{
    private const int AllyDeploymentCommandId = 0x4B;
    private const int DeploymentPreviewVariable = 4057;
    private const int MaxFallbackSlots = 40;

    private readonly LegacyScenarioReader _legacyReader = new();

    public IReadOnlyList<BattlefieldAllyDeploymentSlot> Load(
        ScenarioFileInfo scenario,
        SceneStringDocument dictionary,
        IReadOnlyList<BattlefieldUnitPaletteItem> paletteItems)
    {
        if (!File.Exists(scenario.Path)) return Array.Empty<BattlefieldAllyDeploymentSlot>();

        var sDocument = _legacyReader.Read(scenario.Path, dictionary);
        var sSequences = BuildPlainSlotSequences(sDocument).ToList();
        var rSequences = TryReadPairedRScenario(scenario, dictionary, out var rDocument)
            ? BuildPreviewSlotSequences(rDocument!).ToList()
            : new List<SlotSequence>();

        var sSequence = ChoosePrimarySSequence(sSequences);
        var rSequence = ChooseMatchingRSequence(rSequences, sSequence);
        var sourceSequence = sSequence ?? rSequence;
        if (sourceSequence == null || sourceSequence.Slots.Count == 0)
        {
            return Array.Empty<BattlefieldAllyDeploymentSlot>();
        }

        var setup = MergeSetup(rSequence?.Setup, sSequence?.Setup);
        var forcedPersons = setup.ForcedPersons.Count > 0
            ? setup.ForcedPersons
            : Array.Empty<int>();
        var activeCount = setup.Count is > 0
            ? Math.Min(setup.Count.Value, sourceSequence.Slots.Count)
            : Math.Min(sourceSequence.Slots.Count, MaxFallbackSlots);

        var paletteByPersonId = paletteItems
            .GroupBy(item => item.PersonId)
            .ToDictionaryFirstByKey(group => group.Key, group => group.First());
        var forcedByOrder = forcedPersons
            .Take(activeCount)
            .Select((personId, index) => new { OrderIndex = index, PersonId = personId })
            .ToDictionary(item => item.OrderIndex, item => item.PersonId);

        var result = new List<BattlefieldAllyDeploymentSlot>();
        var slots = sourceSequence.Slots
            .OrderBy(slot => slot.Order)
            .ThenBy(slot => slot.Command.CommandOrdinal)
            .Take(activeCount)
            .ToList();

        for (var index = 0; index < slots.Count; index++)
        {
            var slot = slots[index];
            if (slot.Order < 0 || slot.X < 0 || slot.Y < 0) continue;

            forcedByOrder.TryGetValue(index, out var forcedPersonId);
            var hasForcedPerson = forcedByOrder.ContainsKey(index);
            paletteByPersonId.TryGetValue(forcedPersonId, out var palette);
            result.Add(new BattlefieldAllyDeploymentSlot
            {
                Order = slot.Order,
                GridX = slot.X,
                GridY = slot.Y,
                DirectionCode = slot.DirectionCode,
                Direction = DirectionCodeToText(slot.DirectionCode),
                Flag = slot.Flag,
                PersonId = hasForcedPerson ? forcedPersonId : null,
                Name = hasForcedPerson ? palette?.Name ?? $"人物{forcedPersonId}" : string.Empty,
                JobId = hasForcedPerson ? palette?.JobId : null,
                JobName = hasForcedPerson ? palette?.JobName ?? string.Empty : string.Empty,
                SImageId = hasForcedPerson ? palette?.SImageId ?? 0 : null,
                RImageId = hasForcedPerson ? palette?.RImageId ?? 0 : null,
                Source = BuildSourceText(sourceSequence, rSequence, sSequence, setup),
                SourceFileName = sourceSequence.FileName,
                SourceLocator = $"Scene {slot.Command.SceneIndex} / Section {slot.Command.SectionIndex} / Command {slot.Command.CommandIndex} / {HexDisplayFormatter.FormatOffset(slot.Command.FileOffset)}",
                SourceValues = string.Join(" ", slot.Values.Select(value => value.ToString(CultureInfo.InvariantCulture)))
            });
        }

        return result;
    }

    private bool TryReadPairedRScenario(
        ScenarioFileInfo scenario,
        SceneStringDocument dictionary,
        out LegacyScenarioDocument? document)
    {
        document = null;
        var rPath = ResolvePairedRScenarioPath(scenario);
        if (string.IsNullOrWhiteSpace(rPath) || !File.Exists(rPath)) return false;

        try
        {
            document = _legacyReader.Read(rPath, dictionary);
            return true;
        }
        catch
        {
            document = null;
            return false;
        }
    }

    private static string? ResolvePairedRScenarioPath(ScenarioFileInfo scenario)
    {
        var directory = Path.GetDirectoryName(scenario.Path);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return null;

        var pairedName = SScriptNameRegex().Replace(scenario.FileName, match => "R_" + match.Groups[1].Value + ".eex", 1);
        if (pairedName.Equals(scenario.FileName, StringComparison.OrdinalIgnoreCase)) return null;

        return Directory
            .EnumerateFiles(directory, "*.eex", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => Path.GetFileName(path).Equals(pairedName, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<SlotSequence> BuildPreviewSlotSequences(LegacyScenarioDocument document)
    {
        var commands = document.EnumerateCommands().ToList();
        for (var i = 0; i < commands.Count; i++)
        {
            var marker = commands[i];
            if (marker.CommandId != 0x77 || !CommandContainsValue(marker, DeploymentPreviewVariable))
            {
                continue;
            }

            var slots = CollectContiguousSlots(commands, i + 1);
            if (slots.Count == 0) continue;

            yield return new SlotSequence(
                document.FileName,
                "R剧本4057出战位置预览",
                marker,
                slots,
                FindNearestDeploymentSetup(commands, i));
        }
    }

    private static IEnumerable<SlotSequence> BuildPlainSlotSequences(LegacyScenarioDocument document)
    {
        var commands = document.EnumerateCommands().ToList();
        for (var i = 0; i < commands.Count; i++)
        {
            if (commands[i].CommandId != AllyDeploymentCommandId ||
                i > 0 && commands[i - 1].CommandId == AllyDeploymentCommandId)
            {
                continue;
            }

            var slots = CollectContiguousSlots(commands, i);
            if (slots.Count == 0) continue;

            yield return new SlotSequence(
                document.FileName,
                "S剧本4B出战位置",
                null,
                slots,
                FindNearestDeploymentSetup(commands, i));
        }
    }

    private static List<SlotSeed> CollectContiguousSlots(IReadOnlyList<LegacyScenarioCommandNode> commands, int startIndex)
    {
        var slots = new List<SlotSeed>();
        if (startIndex < 0 || startIndex >= commands.Count) return slots;

        var scene = commands[startIndex].SceneIndex;
        var section = commands[startIndex].SectionIndex;
        for (var i = startIndex; i < commands.Count; i++)
        {
            var command = commands[i];
            if (command.CommandId != AllyDeploymentCommandId ||
                command.SceneIndex != scene ||
                command.SectionIndex != section)
            {
                break;
            }

            var values = GetCommandValues(command).ToList();
            if (values.Count < 3) break;
            slots.Add(new SlotSeed(
                command,
                values,
                values[0],
                values[1],
                values[2],
                values.Count > 3 ? values[3] : 0,
                values.Count > 4 ? values[4] : 0));
        }

        return slots;
    }

    private static DeploymentSetup FindNearestDeploymentSetup(IReadOnlyList<LegacyScenarioCommandNode> commands, int startIndex)
    {
        int? count = null;
        var forcedPersons = new List<int>();
        string source = string.Empty;
        var scene = startIndex >= 0 && startIndex < commands.Count ? commands[startIndex].SceneIndex : 0;

        for (var i = startIndex - 1; i >= 0; i--)
        {
            var command = commands[i];
            if (scene > 0 && command.SceneIndex != scene) continue;
            if (!TryReadDeploymentSetup(command, out var setup)) continue;

            if (count is null && setup.Count is > 0)
            {
                count = setup.Count;
            }

            if (forcedPersons.Count == 0 && setup.ForcedPersons.Count > 0)
            {
                forcedPersons.AddRange(setup.ForcedPersons);
                source = setup.Source;
            }

            if (count is not null && forcedPersons.Count > 0)
            {
                break;
            }
        }

        return new DeploymentSetup(count, forcedPersons, source);
    }

    private static bool TryReadDeploymentSetup(LegacyScenarioCommandNode command, out DeploymentSetup setup)
    {
        setup = DeploymentSetup.Empty;
        var values = GetCommandValues(command).ToList();
        if (command.CommandId == 0x06)
        {
            if (values.Count < 7 || values[0] == 0) return false;
            setup = new DeploymentSetup(
                values[1] > 0 ? values[1] : null,
                NormalizeForcedPersons(values.Skip(2).Take(5)),
                $"{command.CommandIdHex} Scene {command.SceneIndex} / Section {command.SectionIndex} / Command {command.CommandIndex}");
            return true;
        }

        if (command.CommandId == 0x4A)
        {
            if (values.Count < 6) return false;
            setup = new DeploymentSetup(
                values[0] > 0 ? values[0] : null,
                NormalizeForcedPersons(values.Skip(1).Take(5)),
                $"{command.CommandIdHex} Scene {command.SceneIndex} / Section {command.SectionIndex} / Command {command.CommandIndex}");
            return true;
        }

        return false;
    }

    private static IReadOnlyList<int> NormalizeForcedPersons(IEnumerable<int> values)
    {
        var seen = new HashSet<int>();
        var result = new List<int>();
        foreach (var value in values)
        {
            if (value < 0 || !seen.Add(value)) continue;
            result.Add(value);
        }

        return result;
    }

    private static DeploymentSetup MergeSetup(DeploymentSetup? rSetup, DeploymentSetup? sSetup)
    {
        var count = rSetup?.Count ?? sSetup?.Count;
        var forced = rSetup?.ForcedPersons.Count > 0
            ? rSetup.ForcedPersons
            : sSetup?.ForcedPersons ?? Array.Empty<int>();
        var source = !string.IsNullOrWhiteSpace(rSetup?.Source)
            ? rSetup!.Source
            : sSetup?.Source ?? string.Empty;
        return new DeploymentSetup(count, forced, source);
    }

    private static SlotSequence? ChoosePrimarySSequence(IReadOnlyList<SlotSequence> sequences)
        => sequences
            .OrderByDescending(sequence => sequence.Slots.Count)
            .ThenBy(sequence => sequence.Slots.First().Command.CommandOrdinal)
            .FirstOrDefault();

    private static SlotSequence? ChooseMatchingRSequence(IReadOnlyList<SlotSequence> sequences, SlotSequence? sSequence)
    {
        if (sequences.Count == 0) return null;
        if (sSequence == null) return sequences.First();

        return sequences
            .OrderByDescending(sequence => ScoreSlotSequenceMatch(sequence.Slots, sSequence.Slots))
            .ThenBy(sequence => sequence.Marker?.CommandOrdinal ?? int.MaxValue)
            .First();
    }

    private static int ScoreSlotSequenceMatch(IReadOnlyList<SlotSeed> left, IReadOnlyList<SlotSeed> right)
    {
        var rightByOrder = right
            .GroupBy(slot => slot.Order)
            .ToDictionaryFirstByKey(group => group.Key, group => group.First());
        var score = 0;
        foreach (var slot in left)
        {
            if (!rightByOrder.TryGetValue(slot.Order, out var match)) continue;
            if (slot.X == match.X && slot.Y == match.Y) score += 4;
            else if (slot.X == match.X || slot.Y == match.Y) score++;
        }

        return score;
    }

    private static bool CommandContainsValue(LegacyScenarioCommandNode command, int value)
        => command.Parameters.Any(parameter => parameter.IntValue == value || parameter.Values.Contains(value));

    private static IEnumerable<int> GetCommandValues(LegacyScenarioCommandNode command)
    {
        foreach (var parameter in command.Parameters)
        {
            if (parameter.Kind == LegacyScenarioParameterKind.VariableArray)
            {
                foreach (var value in parameter.Values) yield return value;
            }
            else if (parameter.Kind is LegacyScenarioParameterKind.Word16 or LegacyScenarioParameterKind.Dword32)
            {
                yield return parameter.IntValue;
            }
        }
    }

    private static string DirectionCodeToText(int value)
        => value switch
        {
            0 => "上",
            1 => "右",
            2 => "下",
            3 => "左",
            _ => "下"
        };

    private static string BuildSourceText(
        SlotSequence sourceSequence,
        SlotSequence? rSequence,
        SlotSequence? sSequence,
        DeploymentSetup setup)
    {
        var source = sourceSequence.Kind;
        if (rSequence != null && sSequence != null)
        {
            var score = ScoreSlotSequenceMatch(rSequence.Slots, sSequence.Slots);
            source += score > 0 ? "；R 4057 已匹配" : "；R 4057 与 S 4B 未完全匹配";
        }

        if (!string.IsNullOrWhiteSpace(setup.Source))
        {
            source += "；强制列表：" + setup.Source;
        }

        return source;
    }

    private sealed record SlotSequence(
        string FileName,
        string Kind,
        LegacyScenarioCommandNode? Marker,
        IReadOnlyList<SlotSeed> Slots,
        DeploymentSetup Setup);

    private sealed record SlotSeed(
        LegacyScenarioCommandNode Command,
        IReadOnlyList<int> Values,
        int Order,
        int X,
        int Y,
        int DirectionCode,
        int Flag);

    private sealed record DeploymentSetup(int? Count, IReadOnlyList<int> ForcedPersons, string Source)
    {
        public static readonly DeploymentSetup Empty = new(null, Array.Empty<int>(), string.Empty);
    }

    [GeneratedRegex(@"^S_(\d{2,3})\.eex$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SScriptNameRegex();
}
