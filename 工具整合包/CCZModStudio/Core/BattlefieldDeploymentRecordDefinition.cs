namespace CCZModStudio.Core;

public sealed class BattlefieldDeploymentRecordDefinition
{
    public static readonly BattlefieldDeploymentRecordDefinition Friend = new()
    {
        CommandId = 0x46,
        Title = "友军出场设定",
        Category = "友军出场",
        FactionDisplay = "友军",
        FactionHint = "阵营候选：友军",
        BattlefieldNumberBase = 20,
        RecordCount = 20,
        GroupSize = 11,
        PersonIndex = 0,
        HiddenIndex = 1,
        XIndex = 2,
        YIndex = 3,
        DirectionIndex = 4,
        LevelIndex = 5,
        JobLevelIndex = 6,
        AiIndex = 7,
        TargetPersonIndex = 8,
        TargetXIndex = 9,
        TargetYIndex = 10,
        StateIndexes = [1, 4, 5, 6, 8, 9, 10],
        SkipBlankRecords = true,
        WritesPerson = true
    };

    public static readonly BattlefieldDeploymentRecordDefinition Enemy = new()
    {
        CommandId = 0x47,
        Title = "敌军出场设定",
        Category = "敌军出场",
        FactionDisplay = "敌军",
        FactionHint = "阵营候选：敌军",
        BattlefieldNumberBase = 60,
        RecordCount = 80,
        GroupSize = 12,
        PersonIndex = 0,
        ReinforcementIndex = 1,
        HiddenIndex = 2,
        XIndex = 3,
        YIndex = 4,
        DirectionIndex = 5,
        LevelIndex = 6,
        JobLevelIndex = 7,
        AiIndex = 8,
        TargetPersonIndex = 9,
        TargetXIndex = 10,
        TargetYIndex = 11,
        StateIndexes = [1, 2, 5, 6, 7, 9, 10, 11],
        SkipBlankRecords = true,
        WritesPerson = true
    };

    public static readonly BattlefieldDeploymentRecordDefinition Ally = new()
    {
        CommandId = 0x4B,
        Title = "我军出场设定",
        Category = "我军出场",
        FactionDisplay = "我军",
        FactionHint = "阵营候选：我军",
        BattlefieldNumberBase = 0,
        RecordCount = 1,
        GroupSize = 5,
        PersonIndex = 0,
        XIndex = 1,
        YIndex = 2,
        DirectionIndex = 3,
        HiddenIndex = 4,
        LevelIndex = -1,
        JobLevelIndex = -1,
        AiIndex = -1,
        StateIndexes = [3, 4],
        SkipBlankRecords = false,
        WritesPerson = false
    };

    public static readonly IReadOnlyList<string> JobLevelItems = ["初级", "中级", "高级"];

    public required int CommandId { get; init; }
    public required string Title { get; init; }
    public required string Category { get; init; }
    public required string FactionDisplay { get; init; }
    public required string FactionHint { get; init; }
    public required int BattlefieldNumberBase { get; init; }
    public required int RecordCount { get; init; }
    public required int GroupSize { get; init; }
    public required int PersonIndex { get; init; }
    public int ReinforcementIndex { get; init; } = -1;
    public required int HiddenIndex { get; init; }
    public required int XIndex { get; init; }
    public required int YIndex { get; init; }
    public required int DirectionIndex { get; init; }
    public required int LevelIndex { get; init; }
    public required int JobLevelIndex { get; init; }
    public required int AiIndex { get; init; }
    public int TargetPersonIndex { get; init; } = -1;
    public int TargetXIndex { get; init; } = -1;
    public int TargetYIndex { get; init; } = -1;
    public IReadOnlyList<int> StateIndexes { get; init; } = Array.Empty<int>();
    public required bool SkipBlankRecords { get; init; }
    public required bool WritesPerson { get; init; }
    public bool HasReinforcement => ReinforcementIndex >= 0;
    public bool HasMapCoordinate => XIndex >= 0 && YIndex >= 0;

    public static BattlefieldDeploymentRecordDefinition? FromCommandId(int commandId)
        => commandId switch
        {
            0x46 => Friend,
            0x47 => Enemy,
            0x4B => Ally,
            _ => null
        };

    public string SlotName(int slot)
    {
        if (slot == PersonIndex) return "武将码";
        if (slot == ReinforcementIndex) return "援军";
        if (slot == HiddenIndex) return "隐藏";
        if (slot == XIndex) return "X";
        if (slot == YIndex) return "Y";
        if (slot == DirectionIndex) return "朝向";
        if (slot == LevelIndex) return "等级";
        if (slot == JobLevelIndex) return "兵种级别";
        if (slot == AiIndex) return "AI";
        if (slot == TargetPersonIndex) return "目标武将";
        if (slot == TargetXIndex) return "目标X";
        if (slot == TargetYIndex) return "目标Y";
        return "原始槽";
    }
}
