using CCZModStudio.Core;

namespace CCZModStudio.Models;

public sealed class ScenarioTextEntry
{
    private string _offsetHex = string.Empty;
    private string _annotation = string.Empty;

    public int Index { get; set; }
    public int Offset { get; set; }
    public string OffsetHex { get => _offsetHex; set => _offsetHex = HexDisplayFormatter.NormalizeText(value); }
    public int ByteLength { get; set; }
    public int CharLength { get; set; }
    public string Kind { get; set; } = string.Empty;
    public bool HasNewLines { get; set; }
    public string Preview { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string OriginalText { get; set; } = string.Empty;
    public string Annotation { get => _annotation; set => _annotation = HexDisplayFormatter.NormalizeText(value); }
    public int GbkByteCount => EncodingService.GetGbkByteCount(Text);
    public int RemainingBytes => ByteLength - GbkByteCount;
    public string WriteStatus
    {
        get
        {
            if (string.Equals(Text, OriginalText, StringComparison.Ordinal)) return "未改动";
            return RemainingBytes >= 0 ? $"可写回（余 {RemainingBytes}B）" : $"超长 {-RemainingBytes}B";
        }
    }
}
