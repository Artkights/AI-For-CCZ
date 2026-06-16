using CCZModStudio.Core;

namespace CCZModStudio.Models;

public sealed class EexByteHeatmapResult
{
    private string _topBytes = string.Empty;
    private string _topWords = string.Empty;
    private string _textHints = string.Empty;
    private string _annotation = string.Empty;

    public string FileName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string SourceKind { get; init; } = string.Empty;
    public int Offset { get; init; }
    public int Length { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int BytesPerCell { get; init; }
    public byte[] CellValues { get; init; } = [];
    public int UniqueByteCount { get; init; }
    public double ZeroPercent { get; init; }
    public double FFPercent { get; init; }
    public double SmallWordPercent { get; init; }
    public double Entropy { get; init; }
    public string TopBytes { get => _topBytes; init => _topBytes = HexDisplayFormatter.NormalizeText(value); }
    public string TopWords { get => _topWords; init => _topWords = HexDisplayFormatter.NormalizeText(value); }
    public int TextHintCount { get; init; }
    public string TextHints { get => _textHints; init => _textHints = HexDisplayFormatter.NormalizeText(value); }
    public string RoleHint { get; init; } = string.Empty;
    public string Annotation { get => _annotation; init => _annotation = HexDisplayFormatter.NormalizeText(value); }
    public string Path { get; init; } = string.Empty;

    public int CellCount => CellValues.Length;
    public string OffsetHex => HexDisplayFormatter.FormatOffset(Offset);
    public string EndOffsetHex => HexDisplayFormatter.FormatOffset(Offset + Math.Max(Length - 1, 0));
}
