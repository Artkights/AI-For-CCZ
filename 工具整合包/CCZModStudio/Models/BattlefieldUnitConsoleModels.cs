namespace CCZModStudio.Models;

public sealed class BattlefieldUnitDataDefaults
{
    public int PersonId { get; init; }
    public string PersonName { get; init; } = string.Empty;
    public bool Found { get; init; }
    public string Source { get; init; } = string.Empty;
    public int? JobId { get; init; }
    public int? Level { get; init; }
    public int? Experience { get; init; }
    public int? AllyFlag { get; init; }
    public int? WeaponId { get; init; }
    public int? WeaponLevel { get; init; }
    public int? WeaponExperience { get; init; }
    public int? ArmorId { get; init; }
    public int? ArmorLevel { get; init; }
    public int? ArmorExperience { get; init; }
    public int? AssistId { get; init; }
    public IReadOnlyDictionary<int, int> Abilities { get; init; } = new Dictionary<int, int>();
    public IReadOnlyDictionary<int, string> ItemNames { get; init; } = new Dictionary<int, string>();
    public IReadOnlyDictionary<int, string> JobNames { get; init; } = new Dictionary<int, string>();

    public int? GetAbility(int abilityId)
        => Abilities.TryGetValue(abilityId, out var value) ? value : null;

    public string FormatJob()
        => JobId.HasValue
            ? $"{JobId.Value} {LookupName(JobNames, JobId.Value, "兵种")}"
            : "未读取";

    public string FormatItem(int? itemId)
    {
        if (itemId == CCZModStudio.Core.BattlefieldUnitDataDefaultService.DataEquipmentUnset)
        {
            return "使用默认配装（Data=255）";
        }

        if (!itemId.HasValue)
        {
            return "未读取";
        }

        return $"{itemId.Value} {LookupName(ItemNames, itemId.Value, "物品")}";
    }

    public string FormatDataEquipment(int? itemId)
        => itemId.HasValue
            ? FormatItem(itemId)
            : "使用默认配装（未读取）";

    private static string LookupName(IReadOnlyDictionary<int, string> names, int id, string fallbackPrefix)
        => names.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : fallbackPrefix + id;
}

public sealed class BattlefieldUnitConsoleState
{
    public BattlefieldPlacedUnit Unit { get; init; } = new();
    public BattlefieldUnitDataDefaults DataDefaults { get; init; } = new();
    public BattlefieldUnitStatusDraft? StatusDraft { get; init; }
    public bool CanEditStatus => StatusDraft != null;
    public string StatusEditDisabledReason { get; init; } = string.Empty;
    public string DeploymentSourceSummary { get; init; } = string.Empty;
}
