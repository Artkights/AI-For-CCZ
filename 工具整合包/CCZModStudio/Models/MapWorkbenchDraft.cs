namespace CCZModStudio.Models;

public sealed class MapWorkbenchDraft
{
    public string DraftId { get; set; } = string.Empty;
    public string BoundMapId { get; set; } = string.Empty;
    public int GridWidth { get; set; } = 30;
    public int GridHeight { get; set; } = 30;
    public int TileSize { get; set; } = MapResourceItem.MapTilePixelSize;
    public string BaseLayerPath { get; set; } = string.Empty;
    public string MaterialRoot { get; set; } = string.Empty;
    public List<MapCellOverride> MapCellOverrides { get; set; } = new();
    public List<MapCellOverride> GeneratedMapCells { get; set; } = new();
    public List<MapCellOverride> BuildingOverlayCells { get; set; } = new();
    public byte[] TerrainCells { get; set; } = Array.Empty<byte>();
    public bool AutoGenerateMapFromTerrain { get; set; } = true;
    public bool BeautifyGeneratedMap { get; set; }
    public int BeautifyStrength { get; set; } = 2;
    public int FeatherRadius { get; set; } = 8;
    public string CreatedAtText { get; set; } = string.Empty;
    public string UpdatedAtText { get; set; } = string.Empty;

    public int CellCount => GridWidth > 0 && GridHeight > 0 ? GridWidth * GridHeight : 0;
    public int PixelWidth => GridWidth > 0 ? GridWidth * TileSize : 0;
    public int PixelHeight => GridHeight > 0 ? GridHeight * TileSize : 0;
}
