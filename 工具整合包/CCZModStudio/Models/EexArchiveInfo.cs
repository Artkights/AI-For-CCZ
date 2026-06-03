namespace CCZModStudio.Models;

public sealed class EexArchiveInfo
{
    public string Category { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public long Length { get; init; }
    public bool MagicValid { get; init; }
    public string VersionHex { get; init; } = string.Empty;
    public int EntryCount { get; init; }
    public string Header14Hex { get; init; } = string.Empty;
    public string Header18Hex { get; init; } = string.Empty;
    public string Header22Hex { get; init; } = string.Empty;
    public string Header26Hex { get; init; } = string.Empty;
    public int TextHintCount { get; init; }
    public string TextHints { get; init; } = string.Empty;
    public string Annotation { get; init; } = string.Empty;
    public string HeaderAnnotation { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
}

