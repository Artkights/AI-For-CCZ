namespace CCZModStudio.Models;

public sealed class EexEntryProbeRow
{
    public string FileName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public int Index { get; init; }
    public string NodeType { get; init; } = string.Empty;
    public string OffsetHex { get; init; } = string.Empty;
    public int Length { get; init; }
    public string ValueHex { get; init; } = string.Empty;
    public string RoleHint { get; init; } = string.Empty;
    public int TextHintCount { get; init; }
    public string TextHints { get; init; } = string.Empty;
    public int UniqueByteCount { get; init; }
    public double ZeroPercent { get; init; }
    public double SmallWordPercent { get; init; }
    public string FirstBytesHex { get; init; } = string.Empty;
    public string Annotation { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
}
