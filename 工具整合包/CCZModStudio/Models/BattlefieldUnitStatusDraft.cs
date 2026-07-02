namespace CCZModStudio.Models;

public sealed class BattlefieldUnitStatusDraft
{
    public string TargetKey { get; set; } = string.Empty;
    public string ScenarioFileName { get; set; } = string.Empty;
    public int PersonId { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public int CommandId { get; set; }
    public int RecordIndex { get; set; }

    public int? LevelBonus { get; set; }
    public int? JobLevel { get; set; }
    public int? AiPolicy { get; set; }

    public int? Weapon { get; set; }
    public int? WeaponLevel { get; set; }
    public int? Armor { get; set; }
    public int? ArmorLevel { get; set; }
    public int? Assist { get; set; }

    public int? JobId { get; set; }
    public bool HasEquipmentCommand { get; set; }
    public bool HasJobCommand { get; set; }
    public BattlefieldUnitDataDefaults? DataDefaults { get; set; }
    public bool RemoveEquipmentOverride { get; set; }
    public bool RemoveJobOverride { get; set; }
    public List<int> RemoveAbilityOverrides { get; } = [];
    public string EquipmentBoundarySummary { get; set; } = string.Empty;

    public List<BattlefieldUnitAbilityDraft> Abilities { get; } =
    [
        new BattlefieldUnitAbilityDraft { AbilityId = 10, Name = "武力" },
        new BattlefieldUnitAbilityDraft { AbilityId = 11, Name = "统率" },
        new BattlefieldUnitAbilityDraft { AbilityId = 12, Name = "智力" },
        new BattlefieldUnitAbilityDraft { AbilityId = 13, Name = "敏捷" },
        new BattlefieldUnitAbilityDraft { AbilityId = 14, Name = "运气" }
    ];

    public string SourceSummary { get; set; } = string.Empty;
    public string CommandPreview { get; set; } = string.Empty;
}

public sealed class BattlefieldUnitAbilityDraft
{
    public int AbilityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? Operation { get; set; }
    public int? Value { get; set; }
    public bool HasCommand { get; set; }
    public int? DataDefaultValue { get; set; }
    public bool RemoveOverride { get; set; }
}

public sealed class BattlefieldUnitStatusWriteResult
{
    public string FilePath { get; init; } = string.Empty;
    public string BackupPath { get; init; } = string.Empty;
    public string ReportJsonPath { get; init; } = string.Empty;
    public int ChangedBytes { get; init; }
    public int UpdatedCommandCount { get; init; }
    public int InsertedCommandCount { get; init; }
    public string ValidationSummary { get; init; } = string.Empty;
    public IReadOnlyList<string> Changes { get; init; } = Array.Empty<string>();
}

public sealed class BattlefieldUnitStatusLookupItem
{
    public int Value { get; init; }
    public string Text { get; init; } = string.Empty;
    public int? EquipmentDefaultItemId { get; init; }
    public override string ToString() => Text;
}
