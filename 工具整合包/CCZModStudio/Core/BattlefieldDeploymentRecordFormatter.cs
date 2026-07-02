using CCZModStudio.Models;

namespace CCZModStudio.Core;

internal static class BattlefieldDeploymentRecordFormatter
{
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

        if (values.All(value => value == 0))
        {
            return true;
        }

        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (IsBlankSentinelSlot(definition, index))
            {
                if (value is 0 or -1)
                {
                    continue;
                }

                return false;
            }

            if (value != 0)
            {
                return false;
            }
        }

        return true;
    }

    public static int GetWordOrDefault(IReadOnlyList<int> words, int index)
        => index >= 0 && index < words.Count ? words[index] : 0;

    private static bool IsBlankSentinelSlot(BattlefieldDeploymentRecordDefinition definition, int index)
        => index == definition.PersonIndex ||
           index == definition.DirectionIndex ||
           index == definition.TargetPersonIndex;
}
