namespace CCZModStudio.Models;

public sealed class RSceneCommandCandidate
{
    public int Index { get; init; }
    public string TargetKey { get; init; } = string.Empty;
    public string SceneSection { get; init; } = string.Empty;
    public string OffsetHex { get; init; } = string.Empty;
    public int CommandId { get; init; }
    public string CommandIdHex => "0x" + CommandId.ToString("X2");
    public string CommandName { get; init; } = string.Empty;
    public string RoleHint { get; init; } = string.Empty;
    public string ParameterPreview { get; init; } = string.Empty;
    public int? PersonId { get; init; }
    public int? X { get; init; }
    public int? Y { get; init; }
    public int? BackgroundImageNumber { get; init; }
    public string Annotation { get; init; } = string.Empty;
}
