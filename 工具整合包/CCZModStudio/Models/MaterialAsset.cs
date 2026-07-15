namespace CCZModStudio.Models;

public sealed class MaterialAsset
{
    public string Category { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string HexTag { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string AssetType { get; init; } = string.Empty;
    public byte? TerrainId { get; init; }
    public string TerrainName { get; init; } = string.Empty;
    public string GroupKey { get; init; } = string.Empty;
    public string AutoTileSetKey { get; init; } = string.Empty;
    public int VariantIndex { get; init; }
    public string AutoTileRole { get; init; } = string.Empty;
    public int? AutoTileMask { get; init; }
    public string AutoTileMode { get; init; } = string.Empty;
    public int AutoTilePriority { get; init; }
    public int SourceX { get; init; }
    public int SourceY { get; init; }
    public int SourceWidth { get; init; }
    public int SourceHeight { get; init; }
    public MaterialSamplingMode SamplingMode { get; init; } = MaterialSamplingMode.FixedRegion;
    public int SampleBoundsX { get; init; }
    public int SampleBoundsY { get; init; }
    public int SampleBoundsWidth { get; init; }
    public int SampleBoundsHeight { get; init; }
    public int SafeBorder { get; init; }
    public int PreferredPatchWidth { get; init; } = MapResourceItem.MapTilePixelSize;
    public int PreferredPatchHeight { get; init; } = MapResourceItem.MapTilePixelSize;
    public string StylePackId { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public long SizeBytes { get; init; }
    public string FilePath { get; init; } = string.Empty;
}

public enum MaterialSamplingMode
{
    FixedRegion,
    FullCanvasPatches,
    AutoTileStrip
}
