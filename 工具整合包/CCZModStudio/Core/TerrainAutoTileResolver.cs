using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public static class TerrainAutoTileResolver
{
    public static int BuildSameTerrainMask(MapWorkbenchDraft draft, int index)
    {
        if (draft.TerrainCells.Length != draft.CellCount || index < 0 || index >= draft.CellCount)
        {
            return MaterialAutoTileMasks.None;
        }

        var terrain = draft.TerrainCells[index];
        var x = index % draft.GridWidth;
        var y = index / draft.GridWidth;
        var mask = 0;
        if (IsSameTerrainAt(draft, x, y - 1, terrain)) mask |= MaterialAutoTileMasks.North;
        if (IsSameTerrainAt(draft, x + 1, y, terrain)) mask |= MaterialAutoTileMasks.East;
        if (IsSameTerrainAt(draft, x, y + 1, terrain)) mask |= MaterialAutoTileMasks.South;
        if (IsSameTerrainAt(draft, x - 1, y, terrain)) mask |= MaterialAutoTileMasks.West;
        if (HasAll(mask, MaterialAutoTileMasks.North | MaterialAutoTileMasks.East) && IsSameTerrainAt(draft, x + 1, y - 1, terrain))
        {
            mask |= MaterialAutoTileMasks.NorthEast;
        }

        if (HasAll(mask, MaterialAutoTileMasks.South | MaterialAutoTileMasks.East) && IsSameTerrainAt(draft, x + 1, y + 1, terrain))
        {
            mask |= MaterialAutoTileMasks.SouthEast;
        }

        if (HasAll(mask, MaterialAutoTileMasks.South | MaterialAutoTileMasks.West) && IsSameTerrainAt(draft, x - 1, y + 1, terrain))
        {
            mask |= MaterialAutoTileMasks.SouthWest;
        }

        if (HasAll(mask, MaterialAutoTileMasks.North | MaterialAutoTileMasks.West) && IsSameTerrainAt(draft, x - 1, y - 1, terrain))
        {
            mask |= MaterialAutoTileMasks.NorthWest;
        }

        return mask;
    }

    public static MaterialAsset? SelectAutoTileAssetForMask(IReadOnlyList<MaterialAsset> assets, int mask)
    {
        if (assets.Count == 0) return null;
        var normalized = NormalizeEightWayMask(mask);
        foreach (var candidateMask in BuildAutoTileMaskFallbacks(normalized))
        {
            var exact = SelectAutoTileAssetByMask(assets, candidateMask);
            if (exact != null) return exact;
        }

        return SelectAutoTileDefaultAsset(assets);
    }

    public static int GetAssetMask(MaterialAsset asset)
        => asset.AutoTileMask ?? MaterialLibraryIndexer.RoleToMask(asset.AutoTileRole);

    public static int NormalizeEightWayMask(int mask)
    {
        var cardinal = mask & MaterialAutoTileMasks.CardinalMask;
        var diagonal = mask & MaterialAutoTileMasks.DiagonalMask;
        var normalizedDiagonal = 0;
        if (HasAll(cardinal, MaterialAutoTileMasks.North | MaterialAutoTileMasks.East) &&
            HasDiagonal(diagonal, MaterialAutoTileMasks.NorthEast))
        {
            normalizedDiagonal |= MaterialAutoTileMasks.NorthEast;
        }

        if (HasAll(cardinal, MaterialAutoTileMasks.South | MaterialAutoTileMasks.East) &&
            HasDiagonal(diagonal, MaterialAutoTileMasks.SouthEast))
        {
            normalizedDiagonal |= MaterialAutoTileMasks.SouthEast;
        }

        if (HasAll(cardinal, MaterialAutoTileMasks.South | MaterialAutoTileMasks.West) &&
            HasDiagonal(diagonal, MaterialAutoTileMasks.SouthWest))
        {
            normalizedDiagonal |= MaterialAutoTileMasks.SouthWest;
        }

        if (HasAll(cardinal, MaterialAutoTileMasks.North | MaterialAutoTileMasks.West) &&
            HasDiagonal(diagonal, MaterialAutoTileMasks.NorthWest))
        {
            normalizedDiagonal |= MaterialAutoTileMasks.NorthWest;
        }

        return cardinal | normalizedDiagonal;
    }

    public static IEnumerable<int> BuildAutoTileMaskFallbacks(int mask)
    {
        yield return mask;

        var cardinal = mask & MaterialAutoTileMasks.CardinalMask;
        if (cardinal != mask)
        {
            yield return cardinal;
        }

        if (cardinal == MaterialAutoTileMasks.Cross)
        {
            foreach (var innerCornerMask in BuildMissingDiagonalInnerCornerMasks(mask))
            {
                yield return innerCornerMask;
            }

            yield return MaterialAutoTileMasks.Cross;
        }

        if (BitCount(cardinal) == 3)
        {
            yield return cardinal switch
            {
                MaterialAutoTileMasks.TeeN => MaterialAutoTileMasks.StraightH,
                MaterialAutoTileMasks.TeeS => MaterialAutoTileMasks.StraightH,
                MaterialAutoTileMasks.TeeE => MaterialAutoTileMasks.StraightV,
                MaterialAutoTileMasks.TeeW => MaterialAutoTileMasks.StraightV,
                _ => MaterialAutoTileMasks.None
            };
        }
        else if (BitCount(cardinal) == 2)
        {
            if (cardinal is MaterialAutoTileMasks.CornerSE or MaterialAutoTileMasks.CornerSW)
            {
                yield return MaterialAutoTileMasks.North;
            }
            else if (cardinal is MaterialAutoTileMasks.CornerNE or MaterialAutoTileMasks.CornerNW)
            {
                yield return MaterialAutoTileMasks.South;
            }

            if (cardinal is MaterialAutoTileMasks.CornerNW or MaterialAutoTileMasks.CornerSW)
            {
                yield return MaterialAutoTileMasks.East;
            }
            else if (cardinal is MaterialAutoTileMasks.CornerNE or MaterialAutoTileMasks.CornerSE)
            {
                yield return MaterialAutoTileMasks.West;
            }
        }
        else if (BitCount(cardinal) == 1)
        {
            yield return cardinal switch
            {
                MaterialAutoTileMasks.North or MaterialAutoTileMasks.South => MaterialAutoTileMasks.StraightV,
                MaterialAutoTileMasks.East or MaterialAutoTileMasks.West => MaterialAutoTileMasks.StraightH,
                _ => MaterialAutoTileMasks.None
            };
        }

        yield return MaterialAutoTileMasks.None;
    }

    private static MaterialAsset? SelectAutoTileAssetByMask(IEnumerable<MaterialAsset> assets, int mask)
        => assets
            .Where(asset => !asset.AutoTileRole.Equals(MaterialAutoTileRoles.Fill, StringComparison.OrdinalIgnoreCase))
            .Where(asset => NormalizeEightWayMask(GetAssetMask(asset)) == mask)
            .OrderBy(asset => asset.AutoTilePriority)
            .ThenBy(asset => asset.VariantIndex)
            .FirstOrDefault();

    private static MaterialAsset? SelectAutoTileDefaultAsset(IEnumerable<MaterialAsset> assets)
        => assets
               .Where(asset => GetAssetMask(asset) == MaterialAutoTileMasks.None &&
                               !asset.AutoTileRole.Equals(MaterialAutoTileRoles.Fill, StringComparison.OrdinalIgnoreCase))
               .OrderBy(asset => asset.AutoTilePriority)
               .ThenBy(asset => asset.VariantIndex)
               .FirstOrDefault() ??
           assets
               .OrderBy(asset => asset.AutoTilePriority)
               .ThenBy(asset => asset.VariantIndex)
               .FirstOrDefault();

    private static IEnumerable<int> BuildMissingDiagonalInnerCornerMasks(int mask)
    {
        var filled = MaterialAutoTileMasks.Filled;
        if (!HasDiagonal(mask, MaterialAutoTileMasks.NorthEast)) yield return filled & ~MaterialAutoTileMasks.NorthEast;
        if (!HasDiagonal(mask, MaterialAutoTileMasks.SouthEast)) yield return filled & ~MaterialAutoTileMasks.SouthEast;
        if (!HasDiagonal(mask, MaterialAutoTileMasks.SouthWest)) yield return filled & ~MaterialAutoTileMasks.SouthWest;
        if (!HasDiagonal(mask, MaterialAutoTileMasks.NorthWest)) yield return filled & ~MaterialAutoTileMasks.NorthWest;
    }

    private static bool IsSameTerrainAt(MapWorkbenchDraft draft, int x, int y, byte terrain)
        => x >= 0 &&
           y >= 0 &&
           x < draft.GridWidth &&
           y < draft.GridHeight &&
           draft.TerrainCells[y * draft.GridWidth + x] == terrain;

    private static bool HasAll(int mask, int bits)
        => (mask & bits) == bits;

    private static bool HasDiagonal(int mask, int diagonal)
        => (mask & diagonal) == diagonal;

    private static int BitCount(int value)
    {
        value &= MaterialAutoTileMasks.CardinalMask;
        var count = 0;
        while (value != 0)
        {
            count += value & 1;
            value >>= 1;
        }

        return count;
    }
}
