namespace CCZModStudio.Models;

public sealed class CreatorNoteNavigationTarget
{
    public bool IsRecognized { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public string TargetKey { get; init; } = string.Empty;
    public string DisplayText { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string MapId { get; init; } = string.Empty;
    public string OffsetHex { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Rule { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string BaseFileName { get; init; } = string.Empty;
    public string PeerKind { get; init; } = string.Empty;
    public string RoleHint { get; init; } = string.Empty;
    public string TableName { get; init; } = string.Empty;
    public string RowId { get; init; } = string.Empty;
    public string FieldName { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public int? SceneIndex { get; init; }
    public int? SectionIndex { get; init; }
    public int? CommandIndex { get; init; }
    public int? TextIndex { get; init; }
}
