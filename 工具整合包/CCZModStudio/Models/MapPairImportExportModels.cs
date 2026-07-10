namespace CCZModStudio.Models;

public sealed class HexzmapTerrainBmpData
{
    public required string Path { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int PixelOffset { get; init; }
    public int BitsPerPixel { get; init; }
    public byte[] PrefixBytes { get; init; } = Array.Empty<byte>();
    public byte[] TerrainCells { get; init; } = Array.Empty<byte>();
    public byte[] PaletteBytes { get; init; } = Array.Empty<byte>();
    public string Sha256 { get; init; } = string.Empty;
    public List<string> Warnings { get; init; } = new();
}

public sealed class MapPairExportResult
{
    public required string MapId { get; init; }
    public required string JpegPath { get; init; }
    public required string TerrainBmpPath { get; init; }
    public int GridWidth { get; init; }
    public int GridHeight { get; init; }
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }
    public long JpegSizeBytes { get; init; }
    public long TerrainBmpSizeBytes { get; init; }
    public required string JpegSha256 { get; init; }
    public required string TerrainBmpSha256 { get; init; }
    public byte[] TerrainPrefixBytes { get; init; } = Array.Empty<byte>();
    public int TerrainCellCount { get; init; }
}

public sealed class MapPairImportCandidate
{
    public required string MapId { get; init; }
    public required string JpegPath { get; init; }
    public required string TerrainBmpPath { get; init; }

    public override string ToString()
        => $"{MapId} ({Path.GetFileName(JpegPath)} + {Path.GetFileName(TerrainBmpPath)})";
}

public sealed class MapPairImportPreview
{
    public required string FolderPath { get; init; }
    public required string MapId { get; init; }
    public required string JpegPath { get; init; }
    public required string TerrainBmpPath { get; init; }
    public required MapResourceItem TargetMap { get; init; }
    public required HexzmapBlockInfo TargetBlock { get; init; }
    public required HexzmapTerrainBmpData TerrainBmp { get; init; }
    public int JpegWidth { get; init; }
    public int JpegHeight { get; init; }
    public int GridWidth { get; init; }
    public int GridHeight { get; init; }
    public required string SourceJpegSha256 { get; init; }
    public required string TargetJpegSha256 { get; init; }
    public required string SourceTerrainSha256 { get; init; }
    public required string TargetTerrainSha256 { get; init; }
    public byte[] TargetPrefixBytes { get; init; } = Array.Empty<byte>();
    public int ChangedTerrainCells { get; init; }
    public bool JpegIdentical { get; init; }
    public bool TerrainIdentical { get; init; }
    public List<string> Warnings { get; init; } = new();
}

public sealed class MapPairImportResult
{
    public required string MapId { get; init; }
    public MapImageSaveResult? MapImageResult { get; init; }
    public HexzmapSaveResult? TerrainResult { get; init; }
    public bool SkippedJpeg { get; init; }
    public bool SkippedTerrain { get; init; }
}
