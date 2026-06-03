using System.Globalization;

namespace CCZModStudio.Models;

public sealed class EexByteHeatmapResult
{
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
    public string TopBytes { get; init; } = string.Empty;
    public string TopWords { get; init; } = string.Empty;
    public int TextHintCount { get; init; }
    public string TextHints { get; init; } = string.Empty;
    public string RoleHint { get; init; } = string.Empty;
    public string Annotation { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;

    public int CellCount => CellValues.Length;
    public string OffsetHex => "0x" + Offset.ToString("X6", CultureInfo.InvariantCulture);
    public string EndOffsetHex => "0x" + (Offset + Math.Max(Length - 1, 0)).ToString("X6", CultureInfo.InvariantCulture);
}
