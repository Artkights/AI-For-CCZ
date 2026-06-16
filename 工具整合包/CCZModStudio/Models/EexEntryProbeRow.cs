namespace CCZModStudio.Models;

public sealed class EexEntryProbeRow
{
    private string _offsetHex = string.Empty;
    private string _valueHex = string.Empty;
    private string _textHints = string.Empty;
    private string _annotation = string.Empty;

    public string FileName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public int Index { get; init; }
    public string NodeType { get; init; } = string.Empty;
    public string OffsetHex { get => _offsetHex; init => _offsetHex = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public int Length { get; init; }
    public string ValueHex { get => _valueHex; init => _valueHex = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string RoleHint { get; init; } = string.Empty;
    public int TextHintCount { get; init; }
    public string TextHints { get => _textHints; init => _textHints = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public int UniqueByteCount { get; init; }
    public double ZeroPercent { get; init; }
    public double SmallWordPercent { get; init; }
    public string FirstBytesHex { get; init; } = string.Empty;
    public string Annotation { get => _annotation; init => _annotation = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string Path { get; init; } = string.Empty;
}
