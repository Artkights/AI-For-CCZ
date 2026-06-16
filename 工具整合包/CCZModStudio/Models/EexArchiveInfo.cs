namespace CCZModStudio.Models;

public sealed class EexArchiveInfo
{
    private string _versionHex = string.Empty;
    private string _header14Hex = string.Empty;
    private string _header18Hex = string.Empty;
    private string _header22Hex = string.Empty;
    private string _header26Hex = string.Empty;
    private string _annotation = string.Empty;
    private string _headerAnnotation = string.Empty;

    public string Category { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public long Length { get; init; }
    public bool MagicValid { get; init; }
    public string VersionHex { get => _versionHex; init => _versionHex = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public int EntryCount { get; init; }
    public string Header14Hex { get => _header14Hex; init => _header14Hex = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string Header18Hex { get => _header18Hex; init => _header18Hex = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string Header22Hex { get => _header22Hex; init => _header22Hex = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string Header26Hex { get => _header26Hex; init => _header26Hex = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public int TextHintCount { get; init; }
    public string TextHints { get; init; } = string.Empty;
    public string Annotation { get => _annotation; init => _annotation = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string HeaderAnnotation { get => _headerAnnotation; init => _headerAnnotation = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string Path { get; init; } = string.Empty;
}

