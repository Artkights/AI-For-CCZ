namespace CCZModStudio.Models;

public sealed class HexzmapBlockInfo
{
    private string _offsetHex = string.Empty;
    private string _topTerrainIds = string.Empty;
    private string _topTerrainNames = string.Empty;
    private string _unknownTerrainIds = string.Empty;
    private string _annotation = string.Empty;

    public int Index { get; init; }
    public string MapId { get; init; } = string.Empty;
    public string OffsetHex { get => _offsetHex; init => _offsetHex = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public int DataOffset { get; init; }
    public int SegmentOffset { get; init; }
    public int SegmentLength { get; init; }
    public int DecodedLength { get; init; }
    public int DataPrefixLength { get; init; }
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
    public string TopTerrainIds { get => _topTerrainIds; init => _topTerrainIds = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string TopTerrainNames { get => _topTerrainNames; init => _topTerrainNames = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public int KnownTerrainCount { get; init; }
    public string UnknownTerrainIds { get => _unknownTerrainIds; init => _unknownTerrainIds = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string MapImageName { get; init; } = string.Empty;
    public bool MapImageExists { get; init; }
    public string Annotation { get => _annotation; init => _annotation = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
}
