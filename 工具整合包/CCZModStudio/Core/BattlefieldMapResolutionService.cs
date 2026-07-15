using System.Globalization;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public static class BattlefieldMapResolutionService
{
    private const int BattlefieldBackgroundCategory = 3;
    private const int MaximumMapNumber = 999;

    public static BattlefieldMapReference Resolve(ScenarioFileInfo scenario, LegacyScenarioDocument? legacyDocument)
    {
        if (legacyDocument != null)
        {
            foreach (var command in legacyDocument.EnumerateCommands())
            {
                if (!TryResolveBackgroundCommand(command, out var mapNumber)) continue;

                return new BattlefieldMapReference
                {
                    MapNumber = mapNumber,
                    MapId = FormatMapId(mapNumber),
                    SourceKind = BattlefieldMapReferenceSource.BackgroundCommand27,
                    ScenarioFileName = scenario.FileName,
                    SceneIndex = command.SceneIndex,
                    SectionIndex = command.SectionIndex,
                    CommandIndex = command.CommandIndex,
                    OffsetHex = HexDisplayFormatter.FormatOffset(command.FileOffset)
                };
            }
        }

        return TryResolveScenarioNumber(scenario, out var fallbackMapNumber)
            ? new BattlefieldMapReference
            {
                MapNumber = fallbackMapNumber,
                MapId = FormatMapId(fallbackMapNumber),
                SourceKind = BattlefieldMapReferenceSource.ScenarioNumberFallback,
                ScenarioFileName = scenario.FileName
            }
            : BattlefieldMapReference.Unresolved;
    }

    private static bool TryResolveBackgroundCommand(LegacyScenarioCommandNode command, out int mapNumber)
    {
        mapNumber = -1;
        if (command.CommandId != 0x27) return false;

        var values = FlattenIntData(command);
        if (values.Count == 0 || values[0] != BattlefieldBackgroundCategory) return false;

        var mapValueIndex = values[0] + 1;
        if (mapValueIndex < 0 || mapValueIndex >= values.Count) return false;

        var candidate = values[mapValueIndex];
        if (candidate < 0 || candidate > MaximumMapNumber) return false;

        mapNumber = candidate;
        return true;
    }

    private static IReadOnlyList<int> FlattenIntData(LegacyScenarioCommandNode command)
    {
        var values = new List<int>();
        foreach (var parameter in command.Parameters)
        {
            switch (parameter.Kind)
            {
                case LegacyScenarioParameterKind.Text:
                    break;
                case LegacyScenarioParameterKind.VariableArray:
                    while (values.Count % 25 != 0) values.Add(0);
                    values.AddRange(parameter.Values.Take(25));
                    while (values.Count % 25 != 0) values.Add(-1);
                    break;
                default:
                    values.Add(parameter.IntValue);
                    break;
            }
        }

        return values;
    }

    private static bool TryResolveScenarioNumber(ScenarioFileInfo scenario, out int mapNumber)
    {
        if (!string.IsNullOrWhiteSpace(scenario.Id) &&
            int.TryParse(scenario.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out mapNumber) &&
            mapNumber is >= 0 and <= MaximumMapNumber)
        {
            return true;
        }

        var digits = new string((scenario.FileName ?? string.Empty).Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out mapNumber) &&
               mapNumber is >= 0 and <= MaximumMapNumber;
    }

    private static string FormatMapId(int mapNumber)
        => "M" + mapNumber.ToString("000", CultureInfo.InvariantCulture);
}
