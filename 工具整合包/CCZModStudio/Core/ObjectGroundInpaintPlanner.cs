using CCZModStudio.Models;

namespace CCZModStudio.Core;

internal sealed class ObjectGroundInpaintPlanner
{
    public ObjectGroundInpaintPlan Build(MapWorkbenchDraft draft, TerrainVisualProfile profile)
    {
        var cellCount = Math.Max(0, draft.CellCount);
        var visualGroundTerrainCells = new byte[cellCount];
        if (draft.TerrainCells.Length == cellCount)
        {
            Array.Copy(draft.TerrainCells, visualGroundTerrainCells, cellCount);
        }

        if (!profile.UseObjectGroundInpaint || cellCount == 0 || draft.GridWidth <= 0 || draft.GridHeight <= 0)
        {
            return new ObjectGroundInpaintPlan(
                new HashSet<int>(),
                new HashSet<int>(),
                new HashSet<int>(),
                new Dictionary<int, byte>(),
                visualGroundTerrainCells,
                terrainObjectOverlayCellCount: 0,
                fallbackCellCount: 0);
        }

        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var footprintIndexes = BuildObjectFootprintIndexes(
            draft,
            tileSize,
            includeTerrainObjects: profile.GroundInpaintIncludesTerrainObjects);
        var terrainObjectIndexes = BuildTerrainObjectFootprintIndexes(draft, includeTerrainObjects: profile.GroundInpaintIncludesTerrainObjects);
        var contextIndexes = ExpandIndexes(
            draft,
            footprintIndexes,
            Math.Clamp(profile.ObjectGroundContextRadiusCells, 0, 8));
        contextIndexes.ExceptWith(footprintIndexes);

        var forcedRedrawIndexes = new HashSet<int>(footprintIndexes);
        forcedRedrawIndexes.UnionWith(contextIndexes);

        var inferredByCell = new Dictionary<int, byte>();
        var fallbackCount = 0;
        var inferenceRadius = Math.Clamp(profile.ObjectGroundInferenceRadiusCells <= 0 ? 3 : profile.ObjectGroundInferenceRadiusCells, 1, 8);
        foreach (var index in footprintIndexes.OrderBy(index => index))
        {
            if ((uint)index >= (uint)cellCount) continue;
            var originalTerrain = draft.TerrainCells.Length == cellCount ? draft.TerrainCells[index] : (byte)0;
            var inferred = InferGroundTerrain(draft, footprintIndexes, index, originalTerrain, inferenceRadius, out var usedFallback);
            visualGroundTerrainCells[index] = inferred;
            if (usedFallback)
            {
                fallbackCount++;
            }
            else
            {
                inferredByCell[index] = inferred;
            }
        }

        return new ObjectGroundInpaintPlan(
            footprintIndexes,
            contextIndexes,
            forcedRedrawIndexes,
            inferredByCell,
            visualGroundTerrainCells,
            terrainObjectIndexes.Count,
            fallbackCount);
    }

    public static HashSet<int> BuildObjectFootprintIndexes(
        MapWorkbenchDraft draft,
        int tileSize,
        bool includeTerrainObjects)
    {
        var result = draft.BuildingOverlayCells
            .Concat(draft.SceneryOverlayCells)
            .Select(cell => cell.Index)
            .Where(index => (uint)index < (uint)draft.CellCount)
            .ToHashSet();

        foreach (var overlay in draft.SceneryOverlays)
        {
            var left = Math.Clamp(overlay.X / tileSize, 0, Math.Max(0, draft.GridWidth - 1));
            var top = Math.Clamp(overlay.Y / tileSize, 0, Math.Max(0, draft.GridHeight - 1));
            var right = Math.Clamp((overlay.X + Math.Max(1, overlay.Width) - 1) / tileSize, 0, Math.Max(0, draft.GridWidth - 1));
            var bottom = Math.Clamp((overlay.Y + Math.Max(1, overlay.Height) - 1) / tileSize, 0, Math.Max(0, draft.GridHeight - 1));
            for (var y = top; y <= bottom; y++)
            {
                for (var x = left; x <= right; x++)
                {
                    result.Add(y * draft.GridWidth + x);
                }
            }
        }

        result.UnionWith(BuildTerrainObjectFootprintIndexes(draft, includeTerrainObjects));
        return result;
    }

    public static HashSet<int> BuildTerrainObjectFootprintIndexes(MapWorkbenchDraft draft, bool includeTerrainObjects)
    {
        var result = new HashSet<int>();
        if (!includeTerrainObjects || draft.TerrainCells.Length != draft.CellCount)
        {
            return result;
        }

        for (var i = 0; i < draft.TerrainCells.Length; i++)
        {
            if (IsObjectFootprintTerrain(draft.TerrainCells[i]))
            {
                result.Add(i);
            }
        }

        return result;
    }

    public static bool IsObjectFootprintTerrain(byte terrainId)
        => terrainId is 8
            or 14
            or 15
            or 16
            or 17
            or 18
            or 19
            or 20
            or 21
            or 22
            or 23
            or 24
            or 27
            or 28;

    public static bool IsWaterLikeGroundTerrain(byte terrainId)
        => terrainId is 9 or 11 or 12 or 13 or 25;

    private static byte InferGroundTerrain(
        MapWorkbenchDraft draft,
        IReadOnlySet<int> objectFootprints,
        int index,
        byte originalTerrain,
        int radius,
        out bool usedFallback)
    {
        usedFallback = false;
        if (!IsObjectFootprintTerrain(originalTerrain))
        {
            return originalTerrain;
        }

        var scores = new Dictionary<byte, double>();
        var waterScores = new Dictionary<byte, double>();
        var centerX = index % draft.GridWidth;
        var centerY = index / draft.GridWidth;
        var prefersWater = originalTerrain is 8 or 27;
        for (var distance = 1; distance <= radius; distance++)
        {
            for (var dy = -distance; dy <= distance; dy++)
            {
                for (var dx = -distance; dx <= distance; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != distance) continue;
                    var nx = centerX + dx;
                    var ny = centerY + dy;
                    if (nx < 0 || ny < 0 || nx >= draft.GridWidth || ny >= draft.GridHeight) continue;
                    var neighborIndex = ny * draft.GridWidth + nx;
                    if (objectFootprints.Contains(neighborIndex)) continue;
                    var terrain = draft.TerrainCells[neighborIndex];
                    if (IsObjectFootprintTerrain(terrain)) continue;

                    var cardinalWeight = dx == 0 || dy == 0 ? 4.0 : 2.0;
                    var distanceWeight = radius - distance + 1;
                    var weight = cardinalWeight * distanceWeight;
                    scores[terrain] = scores.GetValueOrDefault(terrain) + weight;
                    if (prefersWater && IsWaterLikeGroundTerrain(terrain))
                    {
                        waterScores[terrain] = waterScores.GetValueOrDefault(terrain) + weight + 1000.0;
                    }
                }
            }
        }

        if (waterScores.Count > 0)
        {
            return SelectBestTerrain(waterScores);
        }

        if (scores.Count > 0)
        {
            return SelectBestTerrain(scores);
        }

        usedFallback = true;
        return 0;
    }

    private static byte SelectBestTerrain(IReadOnlyDictionary<byte, double> scores)
        => scores
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key)
            .First()
            .Key;

    private static HashSet<int> ExpandIndexes(MapWorkbenchDraft draft, IReadOnlySet<int> indexes, int radius)
    {
        var result = new HashSet<int>();
        foreach (var index in indexes)
        {
            if ((uint)index >= (uint)draft.CellCount) continue;
            var x = index % draft.GridWidth;
            var y = index / draft.GridWidth;
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= draft.GridWidth || ny >= draft.GridHeight) continue;
                    result.Add(ny * draft.GridWidth + nx);
                }
            }
        }

        return result;
    }
}

internal sealed class ObjectGroundInpaintPlan
{
    public ObjectGroundInpaintPlan(
        HashSet<int> objectFootprintIndexes,
        HashSet<int> objectGroundContextIndexes,
        HashSet<int> forcedGroundRedrawIndexes,
        Dictionary<int, byte> inferredGroundTerrainByCell,
        byte[] visualGroundTerrainCells,
        int terrainObjectOverlayCellCount,
        int fallbackCellCount)
    {
        ObjectFootprintIndexes = objectFootprintIndexes;
        ObjectGroundContextIndexes = objectGroundContextIndexes;
        ForcedGroundRedrawIndexes = forcedGroundRedrawIndexes;
        InferredGroundTerrainByCell = inferredGroundTerrainByCell;
        VisualGroundTerrainCells = visualGroundTerrainCells;
        TerrainObjectOverlayCellCount = terrainObjectOverlayCellCount;
        FallbackCellCount = fallbackCellCount;
    }

    public HashSet<int> ObjectFootprintIndexes { get; }
    public HashSet<int> ObjectGroundContextIndexes { get; }
    public HashSet<int> ForcedGroundRedrawIndexes { get; }
    public Dictionary<int, byte> InferredGroundTerrainByCell { get; }
    public byte[] VisualGroundTerrainCells { get; }
    public int TerrainObjectOverlayCellCount { get; }
    public int FallbackCellCount { get; }
}
