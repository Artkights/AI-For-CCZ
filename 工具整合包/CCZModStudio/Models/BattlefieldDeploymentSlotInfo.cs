namespace CCZModStudio.Models;

public sealed class BattlefieldDeploymentSlotInfo
{
    public string TargetKey { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public int CommandId { get; init; }
    public int RecordIndex { get; init; }
    public int BattlefieldNumber { get; init; }
    public int PersonOrOrder { get; init; }
    public int GridX { get; init; }
    public int GridY { get; init; }
    public bool IsBlank { get; init; }
    public bool WritesPerson { get; init; }
    public bool WritesAi { get; init; }
    public bool IsAllySlot { get; init; }
}
