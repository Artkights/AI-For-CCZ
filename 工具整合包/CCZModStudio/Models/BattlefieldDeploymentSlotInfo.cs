namespace CCZModStudio.Models;

public sealed class BattlefieldDeploymentSlotInfo
{
    public string TargetKey { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public int CommandId { get; init; }
    public int RecordIndex { get; init; }
    public int BattlefieldNumber { get; init; }
    public int PersonOrOrder { get; init; }
    public int PersonRawCode { get; init; }
    public int PersonId { get; init; } = -1;
    public int GridX { get; init; }
    public int GridY { get; init; }
    public bool Hidden { get; init; }
    public bool Reinforcement { get; init; }
    public int DirectionCode { get; init; }
    public string Direction { get; init; } = string.Empty;
    public int LevelOffset { get; init; }
    public int JobLevelCode { get; init; }
    public string JobLevel { get; init; } = string.Empty;
    public int AiPolicyCode { get; init; }
    public string AiMode { get; init; } = string.Empty;
    public bool IsBlank { get; init; }
    public bool IsInitialDeployment { get; init; }
    public bool WritesPerson { get; init; }
    public bool WritesAi { get; init; }
    public bool IsAllySlot { get; init; }
}
