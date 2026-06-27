using System.Drawing;

namespace CCZModStudio.Models;

public enum MapMaterialExtractionSource
{
    CurrentComposite,
    OriginalBaseMap,
    GeneratedTerrainBase
}

public enum MapMaterialExtractionTargetType
{
    Terrain,
    Building,
    Scenery
}

public sealed class MapMaterialExtractionRequest
{
    public MapWorkbenchDraft Draft { get; init; } = null!;
    public string MaterialRoot { get; init; } = string.Empty;
    public Rectangle CellRange { get; init; }
    public MapMaterialExtractionTargetType TargetType { get; init; } = MapMaterialExtractionTargetType.Terrain;
    public byte? TerrainId { get; init; }
    public MapMaterialExtractionSource Source { get; init; } = MapMaterialExtractionSource.CurrentComposite;
    public IReadOnlyList<MaterialAsset> Materials { get; init; } = Array.Empty<MaterialAsset>();
}

public sealed class MapMaterialExtractionPreview
{
    public string TargetDirectory { get; init; } = string.Empty;
    public int StartSequence { get; init; }
    public int EndSequence { get; init; }
    public int FileCount { get; init; }
    public IReadOnlyList<string> PlannedPaths { get; init; } = Array.Empty<string>();
}

public sealed class MapMaterialExtractionResult
{
    public string TargetDirectory { get; init; } = string.Empty;
    public int StartSequence { get; init; }
    public int EndSequence { get; init; }
    public IReadOnlyList<MapMaterialExtractionFile> Files { get; init; } = Array.Empty<MapMaterialExtractionFile>();
}

public sealed class MapMaterialExtractionFile
{
    public int CellIndex { get; init; }
    public int CellX { get; init; }
    public int CellY { get; init; }
    public int Sequence { get; init; }
    public string Path { get; init; } = string.Empty;
}
