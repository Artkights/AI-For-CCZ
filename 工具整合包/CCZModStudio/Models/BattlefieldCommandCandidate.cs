namespace CCZModStudio.Models;

public sealed class BattlefieldCommandCandidate
{
    public int Index { get; init; }
    public int SceneIndex { get; init; }
    public int SectionIndex { get; init; }
    public int CommandIndex { get; init; }
    public string OffsetHex { get; init; } = string.Empty;
    public string CommandIdHex { get; init; } = string.Empty;
    public string CommandName { get; init; } = string.Empty;
    public string RoleHint { get; init; } = string.Empty;
    public string ParameterPreview { get; init; } = string.Empty;
    public string CommandTemplateHint { get; init; } = string.Empty;
    public string ReferenceHint { get; init; } = string.Empty;
    public string Annotation { get; init; } = string.Empty;
}
