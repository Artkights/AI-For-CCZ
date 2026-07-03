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
    public List<TerrainMaterialPlanItem> TerrainMaterialPlan { get; set; } = new();
    public List<MapCellOverride> MapCellOverrides { get; set; } = new();
    public List<MapCellOverride> TerrainBaseCells { get; set; } = new();
    public List<MapCellOverride> GeneratedMapCells { get; set; } = new();
    public List<MapCellOverride> BuildingOverlayCells { get; set; } = new();
    public List<MapCellOverride> SceneryOverlayCells { get; set; } = new();
    public List<MapSceneryOverlay> SceneryOverlays { get; set; } = new();
    public byte[] OriginalTerrainCells { get; set; } = Array.Empty<byte>();
    public byte[] TerrainCells { get; set; } = Array.Empty<byte>();
    public string GenerationMode { get; set; } = MapWorkbenchGenerationModes.MaterialDriven;
    public TerrainVisualProfile TerrainVisualProfile { get; set; } = new();
    public bool AutoGenerateMapFromTerrain { get; set; } = true;
    public bool BeautifyGeneratedMap { get; set; }
    public int BeautifyStrength { get; set; } = 2;
    public int FeatherRadius { get; set; } = 8;
    public string BeautifyFilterProfile { get; set; } = TerrainBeautifyFilterProfiles.Natural;
    public BeautifyCustomFilterSettings? CustomBeautifyFilter { get; set; }
    public string CreatedAtText { get; set; } = string.Empty;
    public string UpdatedAtText { get; set; } = string.Empty;

    public int CellCount => GridWidth > 0 && GridHeight > 0 ? GridWidth * GridHeight : 0;
    public int PixelWidth => GridWidth > 0 ? GridWidth * TileSize : 0;
    public int PixelHeight => GridHeight > 0 ? GridHeight * TileSize : 0;
}

public sealed class MapSceneryOverlay
{
    public string OverlayId { get; set; } = string.Empty;
    public string MaterialRelativePath { get; set; } = string.Empty;
    public string MaterialCategory { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public float RotationDegrees { get; set; }
    public int ZOrder { get; set; }
}

public static class TerrainBeautifyFilterProfiles
{
    public const string Natural = "Natural";
    public const string Night = "Night";
    public const string Autumn = "Autumn";
    public const string Winter = "Winter";
    public const string WarmSun = "WarmSun";
    public const string Custom = "Custom";
}

public sealed class BeautifyCustomFilterSettings
{
    public float PhotoR { get; set; } = 0.92f;
    public float PhotoG { get; set; } = 0.82f;
    public float PhotoB { get; set; } = 0.64f;
    public float PhotoDensity { get; set; } = 0.12f;
    public float BalanceR { get; set; } = 0.03f;
    public float BalanceG { get; set; } = 0.02f;
    public float BalanceB { get; set; } = -0.03f;
    public float Saturation { get; set; } = 1.04f;
    public float Brightness { get; set; } = 0.01f;
    public float Contrast { get; set; } = 1.04f;
    public float HighlightCompression { get; set; }
    public float ShadowLift { get; set; }
    public float MidtoneGamma { get; set; } = 1f;
    public bool PreserveLuminosity { get; set; } = true;

    public static BeautifyCustomFilterSettings CreateDefault() => new();

    public BeautifyCustomFilterSettings Clone()
        => new()
        {
            PhotoR = PhotoR,
            PhotoG = PhotoG,
            PhotoB = PhotoB,
            PhotoDensity = PhotoDensity,
            BalanceR = BalanceR,
            BalanceG = BalanceG,
            BalanceB = BalanceB,
            Saturation = Saturation,
            Brightness = Brightness,
            Contrast = Contrast,
            HighlightCompression = HighlightCompression,
            ShadowLift = ShadowLift,
            MidtoneGamma = MidtoneGamma,
            PreserveLuminosity = PreserveLuminosity
        };
}
