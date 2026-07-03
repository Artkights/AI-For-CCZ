using CCZModStudio.Models;

namespace CCZModStudio.Core;

internal sealed class TerrainTransitionMaskBuilder
{
    public TerrainTransitionMask Build(MapWorkbenchDraft draft, int index, TerrainVisualProfile profile)
    {
        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var neighbors = BuildNeighbors(draft, index, tileSize, profile).ToList();
        if (neighbors.Count == 0)
        {
            return new TerrainTransitionMask(tileSize, Array.Empty<TerrainTransitionNeighbor>(), 0);
        }

        var pixelCount = tileSize * tileSize;
        var totals = new float[pixelCount];
        foreach (var neighbor in neighbors)
        {
            for (var i = 0; i < pixelCount; i++)
            {
                totals[i] += neighbor.Weights[i];
            }
        }

        var maxNeighborWeight = Math.Clamp(profile.NeighborMaxWeight, 0f, 0.9f);
        var mixedPixels = 0;
        for (var i = 0; i < pixelCount; i++)
        {
            if (totals[i] > maxNeighborWeight && totals[i] > 0)
            {
                var scale = maxNeighborWeight / totals[i];
                foreach (var neighbor in neighbors)
                {
                    neighbor.Weights[i] *= scale;
                }

                totals[i] = maxNeighborWeight;
            }

            if (totals[i] > 0.015f)
            {
                mixedPixels++;
            }
        }

        return new TerrainTransitionMask(tileSize, neighbors, mixedPixels);
    }

    private static IEnumerable<TerrainTransitionNeighbor> BuildNeighbors(
        MapWorkbenchDraft draft,
        int index,
        int tileSize,
        TerrainVisualProfile profile)
    {
        var terrain = draft.TerrainCells[index];
        var cellX = index % draft.GridWidth;
        var cellY = index / draft.GridWidth;
        foreach (var direction in Directions)
        {
            var nx = cellX + direction.Dx;
            var ny = cellY + direction.Dy;
            if (nx < 0 || ny < 0 || nx >= draft.GridWidth || ny >= draft.GridHeight) continue;
            var neighborIndex = ny * draft.GridWidth + nx;
            var neighborTerrain = draft.TerrainCells[neighborIndex];
            if (neighborTerrain == terrain) continue;

            var weights = BuildWeights(tileSize, profile, index, terrain, direction);
            if (weights.Any(weight => weight > 0.015f))
            {
                yield return new TerrainTransitionNeighbor(neighborIndex, neighborTerrain, direction.Dx, direction.Dy, weights);
            }
        }
    }

    private static float[] BuildWeights(int tileSize, TerrainVisualProfile profile, int index, byte terrain, GridDirection direction)
    {
        var weights = new float[tileSize * tileSize];
        var feather = Math.Clamp(profile.BoundaryFeatherPixels <= 0 ? profile.EdgeFeatherRadius : profile.BoundaryFeatherPixels, 1, tileSize);
        var jitter = Math.Clamp(profile.BoundaryJitterPixels, 0, tileSize / 2);
        var noiseScale = Math.Clamp(profile.BoundaryNoiseScale, 1, tileSize);
        for (var y = 0; y < tileSize; y++)
        {
            for (var x = 0; x < tileSize; x++)
            {
                var distance = DistanceToDirection(x, y, tileSize, direction);
                var noise = jitter == 0 ? 0f : StableNoise(profile.Seed, index, terrain, x / noiseScale, y / noiseScale, direction) * jitter;
                var t = 1f - Math.Clamp((distance + noise) / Math.Max(1f, feather), 0f, 1f);
                var weight = SmoothStep(t);
                if (direction.Dx != 0 && direction.Dy != 0)
                {
                    weight *= 0.85f;
                }

                weights[y * tileSize + x] = weight;
            }
        }

        return weights;
    }

    private static float DistanceToDirection(int x, int y, int tileSize, GridDirection direction)
    {
        var horizontal = direction.Dx switch
        {
            < 0 => x,
            > 0 => tileSize - 1 - x,
            _ => 0
        };
        var vertical = direction.Dy switch
        {
            < 0 => y,
            > 0 => tileSize - 1 - y,
            _ => 0
        };
        return direction.Dx != 0 && direction.Dy != 0
            ? Math.Max(horizontal, vertical)
            : Math.Max(horizontal, vertical);
    }

    private static float SmoothStep(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return value * value * (3f - 2f * value);
    }

    private static float StableNoise(string? seed, int index, byte terrain, int x, int y, GridDirection direction)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var ch in seed ?? string.Empty)
            {
                hash ^= ch;
                hash *= 16777619u;
            }

            hash ^= (uint)index;
            hash *= 16777619u;
            hash ^= terrain;
            hash *= 16777619u;
            hash ^= (uint)(x * 73856093);
            hash *= 16777619u;
            hash ^= (uint)(y * 19349663);
            hash *= 16777619u;
            hash ^= (uint)((direction.Dx + 2) * 37 + (direction.Dy + 2) * 131);
            hash *= 16777619u;
            return (hash / (float)uint.MaxValue) * 2f - 1f;
        }
    }

    private static readonly GridDirection[] Directions =
    [
        new(0, -1),
        new(1, -1),
        new(1, 0),
        new(1, 1),
        new(0, 1),
        new(-1, 1),
        new(-1, 0),
        new(-1, -1)
    ];

    private readonly record struct GridDirection(int Dx, int Dy);
}

internal sealed record TerrainTransitionMask(int TileSize, IReadOnlyList<TerrainTransitionNeighbor> Neighbors, int MixedPixelCount);

internal sealed record TerrainTransitionNeighbor(int CellIndex, byte TerrainId, int Dx, int Dy, float[] Weights);
