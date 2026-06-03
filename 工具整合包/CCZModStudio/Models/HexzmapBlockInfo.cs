namespace CCZModStudio.Models;

public sealed class HexzmapBlockInfo
{
    public int Index { get; init; }
    public string MapId { get; init; } = string.Empty;
    public string OffsetHex { get; init; } = string.Empty;
    public int DataOffset { get; init; }
    public int SegmentOffset { get; init; }
    public int SegmentLength { get; init; }
    public int MapPixelWidth { get; init; }
    public int MapPixelHeight { get; init; }
    public bool CanEdit { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int BytesRead { get; init; }
    public int UniqueTerrainCount { get; init; }
    public int DominantTerrainId { get; init; }
    public string DominantTerrainName { get; init; } = string.Empty;
    public int DominantTerrainCount { get; init; }
    public string TopTerrainIds { get; init; } = string.Empty;
    public string TopTerrainNames { get; init; } = string.Empty;
    public int KnownTerrainCount { get; init; }
    public string UnknownTerrainIds { get; init; } = string.Empty;
    public string MapImageName { get; init; } = string.Empty;
    public bool MapImageExists { get; init; }
    public string Annotation { get; init; } = string.Empty;
}
