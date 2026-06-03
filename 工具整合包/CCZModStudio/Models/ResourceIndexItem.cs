namespace CCZModStudio.Models;

public sealed class ResourceIndexItem
{
    public const int MapTilePixelSize = 48;

    public string Category { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Extension { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string Magic { get; init; } = string.Empty;
    public string FormatHint { get; init; } = string.Empty;
    public string Annotation { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public int GridWidth => Width > 0 && Width % MapTilePixelSize == 0 ? Width / MapTilePixelSize : 0;
    public int GridHeight => Height > 0 && Height % MapTilePixelSize == 0 ? Height / MapTilePixelSize : 0;
    public int GridCellCount => GridWidth > 0 && GridHeight > 0 ? GridWidth * GridHeight : 0;
    public string Path { get; init; } = string.Empty;
}
