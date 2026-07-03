using CCZModStudio.Models;

namespace CCZModStudio.Core;

internal static class TerrainVisualSurfaceClassifier
{
    public static TerrainVisualSurfaceKind Classify(byte terrainId)
        => terrainId switch
        {
            0 or 1 or 2 or 3 or 4 or 5 or 7 or 10 or 29 => TerrainVisualSurfaceKind.NaturalArea,
            9 or 11 or 25 => TerrainVisualSurfaceKind.LiquidArea,
            6 or 12 or 13 or 27 => TerrainVisualSurfaceKind.LinearTerrain,
            8 or 14 or 15 or 17 => TerrainVisualSurfaceKind.StructureTerrain,
            16 or 18 or 19 or 20 or 21 or 22 or 23 or 24 or 28 => TerrainVisualSurfaceKind.BuildingOverlay,
            _ => TerrainVisualSurfaceKind.FallbackColor
        };

    public static bool UsesStructureConnection(TerrainVisualSurfaceKind kind)
        => kind is TerrainVisualSurfaceKind.LinearTerrain
            or TerrainVisualSurfaceKind.StructureTerrain
            or TerrainVisualSurfaceKind.BuildingOverlay;

    public static bool SupportsInteriorSynthesis(TerrainVisualSurfaceKind kind)
        => kind is TerrainVisualSurfaceKind.NaturalArea or TerrainVisualSurfaceKind.LiquidArea;

    public static bool SupportsRandomTransforms(TerrainVisualSurfaceKind kind)
        => kind is TerrainVisualSurfaceKind.NaturalArea or TerrainVisualSurfaceKind.LiquidArea;
}
