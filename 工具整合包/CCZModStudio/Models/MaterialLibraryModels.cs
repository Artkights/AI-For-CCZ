using System.Drawing;

namespace CCZModStudio.Models;

public static class MaterialAssetTypes
{
    public const string Terrain = "Terrain";
    public const string Building = "Building";
    public const string Scenery = "Scenery";
}

public static class MapWorkbenchLayerSources
{
    public const string TerrainBase = "TerrainBase";
    public const string BuildingOverlay = "BuildingOverlay";
    public const string SceneryOverlay = "SceneryOverlay";
}

public static class MaterialAutoTileRoles
{
    public const string Default = "default";
    public const string StraightH = "straightH";
    public const string StraightV = "straightV";
    public const string CornerNE = "cornerNE";
    public const string CornerNW = "cornerNW";
    public const string CornerSE = "cornerSE";
    public const string CornerSW = "cornerSW";
    public const string EndN = "endN";
    public const string EndE = "endE";
    public const string EndS = "endS";
    public const string EndW = "endW";
    public const string TeeN = "teeN";
    public const string TeeE = "teeE";
    public const string TeeS = "teeS";
    public const string TeeW = "teeW";
    public const string Cross = "cross";
    public const string InnerCornerNE = "innerCornerNE";
    public const string InnerCornerNW = "innerCornerNW";
    public const string InnerCornerSE = "innerCornerSE";
    public const string InnerCornerSW = "innerCornerSW";
}

public static class MaterialAutoTileModes
{
    public const string Mask = "mask";
    public const string Default = "default";
}

public static class MaterialAutoTileMasks
{
    public const int None = 0;
    public const int North = 1;
    public const int East = 2;
    public const int South = 4;
    public const int West = 8;
    public const int NorthEast = 16;
    public const int SouthEast = 32;
    public const int SouthWest = 64;
    public const int NorthWest = 128;
    public const int CardinalMask = North | East | South | West;
    public const int DiagonalMask = NorthEast | SouthEast | SouthWest | NorthWest;
    public const int StraightH = East | West;
    public const int StraightV = North | South;
    public const int CornerNE = North | East;
    public const int CornerNW = North | West;
    public const int CornerSE = South | East;
    public const int CornerSW = South | West;
    public const int TeeN = East | South | West;
    public const int TeeE = North | South | West;
    public const int TeeS = North | East | West;
    public const int TeeW = North | East | South;
    public const int Cross = North | East | South | West;
    public const int Filled = Cross | DiagonalMask;
    public const int InnerCornerNE = Filled & ~NorthEast;
    public const int InnerCornerSE = Filled & ~SouthEast;
    public const int InnerCornerSW = Filled & ~SouthWest;
    public const int InnerCornerNW = Filled & ~NorthWest;
}

public sealed class MaterialAutoTileVariant
{
    public string FileName { get; set; } = string.Empty;
    public string Role { get; set; } = MaterialAutoTileRoles.Default;
    public int? Mask { get; set; }
    public string Mode { get; set; } = MaterialAutoTileModes.Mask;
    public int Priority { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = MapResourceItem.MapTilePixelSize;
    public int Height { get; set; } = MapResourceItem.MapTilePixelSize;

    public Rectangle SourceRect => new(X, Y, Width, Height);
}
