namespace CCZModStudio.Models;

public sealed class BattlefieldDeploymentRecordState
{
    public string TargetKey { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public int CommandId { get; init; }
    public string CommandName { get; init; } = string.Empty;
    public int SceneIndex { get; init; }
    public int SectionIndex { get; init; }
    public int CommandIndex { get; init; }
    public string OffsetHex { get; init; } = string.Empty;
    public int RecordIndex { get; init; }
    public int BattlefieldNumber { get; init; }
    public int PersonRawCode { get; init; }
    public int PersonId { get; init; } = -1;
    public string PersonDisplay { get; init; } = string.Empty;
    public bool IsPersonVariable { get; init; }
    public int? PersonVariableAddress { get; init; }
    public bool Reinforcement { get; init; }
    public bool Hidden { get; init; }
    public int GridX { get; init; }
    public int GridY { get; init; }
    public int DirectionCode { get; init; }
    public string Direction { get; init; } = "下";
    public int LevelOffset { get; init; }
    public int JobLevelCode { get; init; }
    public string JobLevel { get; init; } = string.Empty;
    public int AiPolicyCode { get; init; }
    public string AiMode { get; init; } = string.Empty;
    public int TargetPersonRawCode { get; init; }
    public int TargetPersonId { get; init; } = -1;
    public string TargetPersonDisplay { get; init; } = string.Empty;
    public bool IsTargetPersonVariable { get; init; }
    public int? TargetPersonVariableAddress { get; init; }
    public int TargetX { get; init; }
    public int TargetY { get; init; }
    public bool IsBlank { get; init; }
    public bool IsInitialDeployment { get; init; }
    public IReadOnlyList<int> Values { get; init; } = Array.Empty<int>();
    public bool WritesPerson { get; init; }
    public bool WritesAi { get; init; }
    public bool IsAllySlot { get; init; }
}
