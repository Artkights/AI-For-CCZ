using CCZModStudio.Models;

namespace CCZModStudio.Core;

internal static class BattlefieldDeploymentRecordFormatter
{
    public const int EmptyPerson2Code = -1;

    public const string EmptySlotText = "无";

    public static IReadOnlyList<int> ReadRecordValues(
        LegacyScenarioCommandNode command,
        BattlefieldDeploymentRecordDefinition definition,
        int recordIndex)
    {
        var values = new int[definition.GroupSize];
        var baseIndex = recordIndex * definition.GroupSize;
        for (var slot = 0; slot < definition.GroupSize; slot++)
        {
            var parameterIndex = baseIndex + slot;
            values[slot] = parameterIndex >= 0 && parameterIndex < command.Parameters.Count
                ? command.Parameters[parameterIndex].IntValue
                : 0;
        }

        return values;
    }

    public static bool IsBlankRecord(BattlefieldDeploymentRecordDefinition definition, IReadOnlyList<int> values)
    {
        if (values.Count == 0)
        {
            return true;
        }

        return definition.WritesPerson &&
               definition.PersonIndex >= 0 &&
               definition.PersonIndex < values.Count &&
               values[definition.PersonIndex] == EmptyPerson2Code;
    }

    public static int GetWordOrDefault(IReadOnlyList<int> words, int index)
        => index >= 0 && index < words.Count ? words[index] : 0;
}
