namespace CCZModStudio.Models;

public sealed class MapResourceItem
{
    public const int MapTilePixelSize = 48;

    public string Category { get; init; } = "地图图片";
    public string Id { get; init; } = string.Empty;
    public string MapId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Extension { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string Magic { get; init; } = string.Empty;
    public string FormatHint { get; init; } = string.Empty;
    public string Annotation { get; init; } = string.Empty;
    public string SourceKind { get; init; } = "JpegMap";
    public int Width { get; init; }
    public int Height { get; init; }
    public int GridWidthOverride { get; init; }
    public int GridHeightOverride { get; init; }
    public int GridWidth => GridWidthOverride > 0 ? GridWidthOverride : Width > 0 && Width % MapTilePixelSize == 0 ? Width / MapTilePixelSize : 0;
    public int GridHeight => GridHeightOverride > 0 ? GridHeightOverride : Height > 0 && Height % MapTilePixelSize == 0 ? Height / MapTilePixelSize : 0;
    public int GridCellCount => GridWidth > 0 && GridHeight > 0 ? GridWidth * GridHeight : 0;
    public string Path { get; init; } = string.Empty;
}
