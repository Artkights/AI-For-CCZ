namespace CCZModStudio.Models;

public sealed class LsResourceInfo
{
    public string Category { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public long Length { get; init; }
    public bool MagicValid { get; init; }
    public string Magic { get; init; } = string.Empty;
    public string HeaderText { get; init; } = string.Empty;
    public int PayloadOffset { get; init; }
    public long PayloadLength { get; init; }
    public int UniqueByteCount { get; init; }
    public double ZeroPercent { get; init; }
    public string TopBytesHex { get; init; } = string.Empty;
    public string FirstPayloadBytesHex { get; init; } = string.Empty;
    public string RoleHint { get; init; } = string.Empty;
    public int TextHintCount { get; init; }
    public string TextHints { get; init; } = string.Empty;
    public string Annotation { get; init; } = string.Empty;
    public string RoleReason { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
}
