namespace CCZModStudio.Models;

public sealed class RSceneMapFaceState
{
    public int PersonId { get; init; }
    public int PersonReference { get; init; }
    public int? PersonVariableAddress { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public string TargetKey { get; init; } = string.Empty;
    public string LastActionTargetKey { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
}
