namespace CCZModStudio.Models;

public sealed class RSceneActorState
{
    public int PersonId { get; init; }
    public int GridX { get; init; }
    public int GridY { get; init; }
    public string Facing { get; init; } = "下";
    public int FrameIndex { get; init; }
    public string TargetKey { get; init; } = string.Empty;
    public string LastActionTargetKey { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
}
