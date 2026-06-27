namespace CCZModStudio.Models;

public sealed class RSceneCommandCandidate
{
    private string _offsetHex = string.Empty;
    private string _annotation = string.Empty;

    public int Index { get; init; }
    public string TargetKey { get; init; } = string.Empty;
    public string SceneSection { get; init; } = string.Empty;
    public string OffsetHex { get => _offsetHex; init => _offsetHex = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public int CommandId { get; init; }
    public string CommandIdHex => CCZModStudio.Core.HexDisplayFormatter.Format(CommandId, 2);
    public string CommandName { get; init; } = string.Empty;
    public string RoleHint { get; init; } = string.Empty;
    public string ParameterPreview { get; init; } = string.Empty;
    public int? PersonId { get; init; }
    public int? X { get; init; }
    public int? Y { get; init; }
    public int? BackgroundImageNumber { get; init; }
    public RSceneBackgroundReference? BackgroundReference { get; init; }
    public string Annotation { get => _annotation; init => _annotation = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
}
