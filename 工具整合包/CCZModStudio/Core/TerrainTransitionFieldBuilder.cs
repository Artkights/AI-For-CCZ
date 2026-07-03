using CCZModStudio.Models;

namespace CCZModStudio.Core;

internal sealed class TerrainTransitionFieldBuilder
{
    public TerrainTransitionField Build(
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        TerrainVisualSynthesisPlan plan)
    {
        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var width = draft.GridWidth * tileSize;
        var height = draft.GridHeight * tileSize;
        var feather = Math.Clamp(
            profile.TransitionFieldFeatherPixels <= 0 ? profile.BoundaryFeatherPixels : profile.TransitionFieldFeatherPixels,
            1,
            tileSize);
        var jitterPixels = Math.Clamp(profile.TransitionFieldJitterPixels, 0, Math.Max(0, feather - 1));
        var pixels = new List<TerrainTransitionPixel>(Math.Max(1024, plan.ExpandedRedrawIndexes.Count * tileSize * Math.Min(tileSize, feather)));
        var multiTerrainJunctionPixels = 0;
        var repeatedBoundaryBlendPrevented = 0;

        foreach (var index in plan.ExpandedRedrawIndexes.OrderBy(index => index))
        {
            if ((uint)index >= (uint)draft.CellCount) continue;
            var x = index % draft.GridWidth;
            var y = index / draft.GridWidth;
            var terrain = draft.TerrainCells[index];
            if (!CanParticipateInGlobalTransition(terrain))
            {
                continue;
            }

            var left = x * tileSize;
            var top = y * tileSize;
            for (var py = 0; py < tileSize; py++)
            {
                var worldY = top + py;
                for (var px = 0; px < tileSize; px++)
                {
                    var worldX = left + px;
                    var candidateCount = 0;
                    BoundaryCandidate best = default;
                    TryAddVerticalCandidate(
                        draft,
                        profile,
                        x,
                        y,
                        px,
                        py,
                        worldX,
                        worldY,
                        width,
                        height,
                        tileSize,
                        feather,
                        jitterPixels,
                        dx: -1,
                        ref candidateCount,
                        ref best);
                    TryAddVerticalCandidate(
                        draft,
                        profile,
                        x,
                        y,
                        px,
                        py,
                        worldX,
                        worldY,
                        width,
                        height,
                        tileSize,
                        feather,
                        jitterPixels,
                        dx: 1,
                        ref candidateCount,
                        ref best);
                    TryAddHorizontalCandidate(
                        draft,
                        profile,
                        x,
                        y,
                        px,
                        py,
                        worldX,
                        worldY,
                        width,
                        height,
                        tileSize,
                        feather,
                        jitterPixels,
                        dy: -1,
                        ref candidateCount,
                        ref best);
                    TryAddHorizontalCandidate(
                        draft,
                        profile,
                        x,
                        y,
                        px,
                        py,
                        worldX,
                        worldY,
                        width,
                        height,
                        tileSize,
                        feather,
                        jitterPixels,
                        dy: 1,
                        ref candidateCount,
                        ref best);

                    if (!best.HasValue)
                    {
                        continue;
                    }

                    if (candidateCount > 1)
                    {
                        multiTerrainJunctionPixels++;
                        repeatedBoundaryBlendPrevented += candidateCount - 1;
                    }

                    pixels.Add(new TerrainTransitionPixel(
                        worldY * width + worldX,
                        best.FirstSampleOffset,
                        best.SecondSampleOffset,
                        best.SecondWeight));
                }
            }
        }

        return new TerrainTransitionField(
            pixels,
            multiTerrainJunctionPixels,
            repeatedBoundaryBlendPrevented);
    }

    private static void TryAddVerticalCandidate(
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        int cellX,
        int cellY,
        int px,
        int py,
        int worldX,
        int worldY,
        int width,
        int height,
        int tileSize,
        int feather,
        int jitterPixels,
        int dx,
        ref int candidateCount,
        ref BoundaryCandidate best)
    {
        var neighborX = cellX + dx;
        if (neighborX < 0 || neighborX >= draft.GridWidth) return;
        var currentIndex = cellY * draft.GridWidth + cellX;
        var neighborIndex = cellY * draft.GridWidth + neighborX;
        if (!CanBlendTerrains(draft.TerrainCells[currentIndex], draft.TerrainCells[neighborIndex])) return;

        var borderX = dx < 0 ? cellX * tileSize : (cellX + 1) * tileSize;
        var boundaryKey = Math.Min(currentIndex, neighborIndex) * 397 ^ Math.Max(currentIndex, neighborIndex);
        var jitter = StableJitter(profile.Seed, worldY, draft.TerrainCells[currentIndex], draft.TerrainCells[neighborIndex], boundaryKey, jitterPixels);
        var signedDistance = worldX - (borderX + jitter);
        var distance = MathF.Abs(signedDistance);
        if (distance > feather) return;

        var firstX = dx < 0 ? worldX - tileSize : worldX;
        var secondX = dx < 0 ? worldX : worldX + tileSize;
        if ((uint)firstX >= (uint)width || (uint)secondX >= (uint)width) return;
        var firstOffset = worldY * width + firstX;
        var secondOffset = worldY * width + secondX;
        var secondWeight = SmoothStep((signedDistance + feather) / (2f * feather));
        AddCandidate(firstOffset, secondOffset, secondWeight, distance, ref candidateCount, ref best);
    }

    private static void TryAddHorizontalCandidate(
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        int cellX,
        int cellY,
        int px,
        int py,
        int worldX,
        int worldY,
        int width,
        int height,
        int tileSize,
        int feather,
        int jitterPixels,
        int dy,
        ref int candidateCount,
        ref BoundaryCandidate best)
    {
        var neighborY = cellY + dy;
        if (neighborY < 0 || neighborY >= draft.GridHeight) return;
        var currentIndex = cellY * draft.GridWidth + cellX;
        var neighborIndex = neighborY * draft.GridWidth + cellX;
        if (!CanBlendTerrains(draft.TerrainCells[currentIndex], draft.TerrainCells[neighborIndex])) return;

        var borderY = dy < 0 ? cellY * tileSize : (cellY + 1) * tileSize;
        var boundaryKey = Math.Min(currentIndex, neighborIndex) * 397 ^ Math.Max(currentIndex, neighborIndex);
        var jitter = StableJitter(profile.Seed, worldX, draft.TerrainCells[currentIndex], draft.TerrainCells[neighborIndex], boundaryKey, jitterPixels);
        var signedDistance = worldY - (borderY + jitter);
        var distance = MathF.Abs(signedDistance);
        if (distance > feather) return;

        var firstY = dy < 0 ? worldY - tileSize : worldY;
        var secondY = dy < 0 ? worldY : worldY + tileSize;
        if ((uint)firstY >= (uint)height || (uint)secondY >= (uint)height) return;
        var firstOffset = firstY * width + worldX;
        var secondOffset = secondY * width + worldX;
        var secondWeight = SmoothStep((signedDistance + feather) / (2f * feather));
        AddCandidate(firstOffset, secondOffset, secondWeight, distance, ref candidateCount, ref best);
    }

    private static bool CanBlendTerrains(byte first, byte second)
    {
        if (first == second) return false;
        return CanParticipateInGlobalTransition(first) && CanParticipateInGlobalTransition(second);
    }

    private static bool CanParticipateInGlobalTransition(byte terrain)
    {
        var kind = TerrainVisualSurfaceClassifier.Classify(terrain);
        return kind is not TerrainVisualSurfaceKind.StructureTerrain
            and not TerrainVisualSurfaceKind.BuildingOverlay;
    }

    private static void AddCandidate(
        int firstOffset,
        int secondOffset,
        float secondWeight,
        float distance,
        ref int candidateCount,
        ref BoundaryCandidate best)
    {
        candidateCount++;
        if (best.HasValue && distance >= best.Distance)
        {
            return;
        }

        best = new BoundaryCandidate(true, firstOffset, secondOffset, secondWeight, distance);
    }

    private static float StableJitter(string? seed, int coordinate, byte firstTerrain, byte secondTerrain, int boundaryKey, int jitterPixels)
    {
        if (jitterPixels <= 0) return 0f;
        var scale = 12f;
        var x = coordinate / scale;
        var x0 = (int)MathF.Floor(x);
        var tx = SmoothStep(x - x0);
        var a = StableNoise01(seed, x0, firstTerrain, secondTerrain, boundaryKey);
        var b = StableNoise01(seed, x0 + 1, firstTerrain, secondTerrain, boundaryKey);
        return ((a + (b - a) * tx) * 2f - 1f) * jitterPixels;
    }

    private static float StableNoise01(string? seed, int coordinate, byte firstTerrain, byte secondTerrain, int boundaryKey)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var ch in seed ?? string.Empty)
            {
                hash ^= ch;
                hash *= 16777619u;
            }

            hash ^= (uint)coordinate;
            hash *= 16777619u;
            hash ^= (uint)boundaryKey;
            hash *= 16777619u;
            hash ^= firstTerrain;
            hash *= 16777619u;
            hash ^= secondTerrain;
            hash *= 16777619u;
            return hash / (float)uint.MaxValue;
        }
    }

    private static float SmoothStep(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return value * value * (3f - 2f * value);
    }

    private readonly record struct BoundaryCandidate(
        bool HasValue,
        int FirstSampleOffset,
        int SecondSampleOffset,
        float SecondWeight,
        float Distance);
}

internal sealed record TerrainTransitionField(
    IReadOnlyList<TerrainTransitionPixel> Pixels,
    int MultiTerrainJunctionPixels,
    int RepeatedBoundaryBlendPreventedCount);

internal readonly record struct TerrainTransitionPixel(
    int Offset,
    int FirstSampleOffset,
    int SecondSampleOffset,
    float SecondWeight);
