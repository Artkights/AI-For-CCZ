using System.Drawing;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

internal sealed class TerrainRegionTextureSynthesizer
{
    public Dictionary<int, Bitmap> Build(
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        TerrainVisualSynthesisPlan plan,
        Func<MaterialAsset, int, TileVisualStats, Bitmap> renderMaterialTile,
        Func<MaterialAsset, TileVisualStats> getStats,
        out TerrainRegionTextureSynthesisStats stats,
        CancellationToken cancellationToken = default)
    {
        stats = new TerrainRegionTextureSynthesisStats();
        var result = new Dictionary<int, Bitmap>();
        if (!profile.UseRegionTextureCanvas || !profile.UseInteriorTextureSynthesis)
        {
            return result;
        }

        foreach (var region in plan.Regions.OrderBy(region => region.RegionId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!region.HasMaterial ||
                !TerrainVisualSurfaceClassifier.SupportsInteriorSynthesis(region.SurfaceKind) ||
                region.CellIndexes.Count == 0)
            {
                continue;
            }

            var candidates = region.CandidatePatches
                .Where(TerrainInteriorSynthesisPlanner.IsInteriorPatchCandidate)
                .OrderBy(asset => TileVisualStatsCalculator.Distance(getStats(asset), region.TargetStyleStats))
                .ThenBy(asset => asset.AutoTilePriority)
                .ThenBy(asset => asset.VariantIndex)
                .ThenBy(asset => asset.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (candidates.Count == 0)
            {
                candidates = region.CandidatePatches
                    .OrderBy(asset => asset.AutoTilePriority)
                    .ThenBy(asset => asset.VariantIndex)
                    .ThenBy(asset => asset.FileName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (candidates.Count == 0)
            {
                continue;
            }

            BuildRegionCanvas(draft, profile, region, candidates, renderMaterialTile, getStats, result, stats, cancellationToken);
            stats.RegionTextureCanvasCount++;
        }

        return result;
    }

    private static void BuildRegionCanvas(
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        TerrainVisualRegionPlan region,
        IReadOnlyList<MaterialAsset> candidates,
        Func<MaterialAsset, int, TileVisualStats, Bitmap> renderMaterialTile,
        Func<MaterialAsset, TileVisualStats> getStats,
        Dictionary<int, Bitmap> result,
        TerrainRegionTextureSynthesisStats stats,
        CancellationToken cancellationToken)
    {
        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var cells = region.CellIndexes.Distinct().OrderBy(index => index / draft.GridWidth).ThenBy(index => index % draft.GridWidth).ToList();
        var cellSet = cells.ToHashSet();
        var minX = cells.Min(index => index % draft.GridWidth);
        var maxX = cells.Max(index => index % draft.GridWidth);
        var minY = cells.Min(index => index / draft.GridWidth);
        var maxY = cells.Max(index => index / draft.GridWidth);
        var canvasWidth = (maxX - minX + 1) * tileSize;
        var canvasHeight = (maxY - minY + 1) * tileSize;
        var canvas = new FastBitmapBuffer(canvasWidth, canvasHeight);
        var writtenCells = new HashSet<int>();
        var overlap = Math.Clamp(profile.QuiltingOverlapPixels <= 0 ? profile.OverlapSeamPixels : profile.QuiltingOverlapPixels, 0, tileSize / 2);
        var candidateCount = Math.Clamp(profile.QuiltingCandidateCount <= 0 ? 8 : profile.QuiltingCandidateCount, 1, 32);

        foreach (var index in cells)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selected = SelectPatch(
                draft,
                profile,
                region,
                candidates,
                candidateCount,
                index,
                canvas,
                minX,
                minY,
                tileSize,
                overlap,
                writtenCells,
                cellSet,
                renderMaterialTile,
                getStats,
                stats,
                cancellationToken);
            using var tile = selected.Tile;
            using var tileBuffer = FastBitmapBuffer.FromBitmap(tile);
            var cellX = index % draft.GridWidth;
            var cellY = index / draft.GridWidth;
            var localX = (cellX - minX) * tileSize;
            var localY = (cellY - minY) * tileSize;
            var hasLeft = cellSet.Contains(index - 1) && writtenCells.Contains(index - 1) && cellX > 0;
            var hasTop = cellSet.Contains(index - draft.GridWidth) && writtenCells.Contains(index - draft.GridWidth) && cellY > 0;
            WriteTileToCanvas(
                canvas,
                tileBuffer,
                draft,
                profile,
                region,
                localX,
                localY,
                cellX,
                cellY,
                tileSize,
                overlap,
                hasLeft,
                hasTop,
                stats,
                cancellationToken);
            writtenCells.Add(index);
            stats.QuiltedPatchCount++;
        }

        foreach (var index in cells)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cellX = index % draft.GridWidth;
            var cellY = index / draft.GridWidth;
            var localX = (cellX - minX) * tileSize;
            var localY = (cellY - minY) * tileSize;
            result[index] = CropTile(canvas, localX, localY, tileSize);
        }
    }

    private static SelectedRegionPatch SelectPatch(
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        TerrainVisualRegionPlan region,
        IReadOnlyList<MaterialAsset> candidates,
        int candidateCount,
        int index,
        FastBitmapBuffer canvas,
        int minX,
        int minY,
        int tileSize,
        int overlap,
        IReadOnlySet<int> writtenCells,
        IReadOnlySet<int> regionCells,
        Func<MaterialAsset, int, TileVisualStats, Bitmap> renderMaterialTile,
        Func<MaterialAsset, TileVisualStats> getStats,
        TerrainRegionTextureSynthesisStats stats,
        CancellationToken cancellationToken)
    {
        var ranked = candidates
            .Select(asset => new
            {
                Asset = asset,
                Score = TileVisualStatsCalculator.Distance(getStats(asset), region.TargetStyleStats) +
                        StableNoise01(profile.Seed, index, region.TerrainId, (uint)(asset.VariantIndex + 17)) * 0.15
            })
            .OrderBy(item => item.Score)
            .Take(Math.Min(candidateCount, candidates.Count))
            .ToList();
        Bitmap? bestTile = null;
        var bestScore = double.MaxValue;
        MaterialAsset? bestAsset = null;

        foreach (var item in ranked)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tile = renderMaterialTile(item.Asset, index, region.TargetStyleStats);
            var transform = PickRegionTransform(profile, region.SurfaceKind, index, item.Asset);
            if (ApplyTileTransform(tile, transform))
            {
                stats.TileTransformCount++;
            }

            BlendSecondaryPatchIfAvailable(tile, draft, profile, region, candidates, item.Asset, renderMaterialTile, index, stats, cancellationToken);

            using var tileBuffer = FastBitmapBuffer.FromBitmap(tile);
            var score = item.Score + OverlapScore(
                draft,
                index,
                canvas,
                tileBuffer,
                minX,
                minY,
                tileSize,
                overlap,
                writtenCells,
                regionCells);
            if (score < bestScore)
            {
                bestTile?.Dispose();
                bestTile = tile;
                bestScore = score;
                bestAsset = item.Asset;
            }
            else
            {
                tile.Dispose();
                stats.PatchOverlapRejectedCount++;
            }
        }

        if (bestTile == null)
        {
            bestAsset = candidates[0];
            bestTile = renderMaterialTile(bestAsset, index, region.TargetStyleStats);
        }

        return new SelectedRegionPatch(bestAsset!, bestTile);
    }

    private static double OverlapScore(
        MapWorkbenchDraft draft,
        int index,
        FastBitmapBuffer canvas,
        FastBitmapBuffer tile,
        int minX,
        int minY,
        int tileSize,
        int overlap,
        IReadOnlySet<int> writtenCells,
        IReadOnlySet<int> regionCells)
    {
        if (overlap <= 0) return 0;
        var cellX = index % draft.GridWidth;
        var cellY = index / draft.GridWidth;
        var localX = (cellX - minX) * tileSize;
        var localY = (cellY - minY) * tileSize;
        var samples = 0;
        double error = 0;
        if (cellX > 0 && regionCells.Contains(index - 1) && writtenCells.Contains(index - 1))
        {
            for (var y = 0; y < tileSize; y += 2)
            {
                for (var x = 0; x < overlap; x += 2)
                {
                    var canvasX = localX - overlap + x;
                    if (canvasX < 0) continue;
                    error += ColorDistanceSquared(tile.Pixels[y * tile.Width + x], canvas.Pixels[(localY + y) * canvas.Width + canvasX]);
                    samples++;
                }
            }
        }

        if (cellY > 0 && regionCells.Contains(index - draft.GridWidth) && writtenCells.Contains(index - draft.GridWidth))
        {
            for (var y = 0; y < overlap; y += 2)
            {
                for (var x = 0; x < tileSize; x += 2)
                {
                    var canvasY = localY - overlap + y;
                    if (canvasY < 0) continue;
                    error += ColorDistanceSquared(tile.Pixels[y * tile.Width + x], canvas.Pixels[canvasY * canvas.Width + localX + x]);
                    samples++;
                }
            }
        }

        return samples == 0 ? 0 : Math.Sqrt(error / samples) / 255.0;
    }

    private static void WriteTileToCanvas(
        FastBitmapBuffer canvas,
        FastBitmapBuffer tile,
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        TerrainVisualRegionPlan region,
        int localX,
        int localY,
        int cellX,
        int cellY,
        int tileSize,
        int overlap,
        bool hasLeft,
        bool hasTop,
        TerrainRegionTextureSynthesisStats stats,
        CancellationToken cancellationToken)
    {
        var scale = Math.Max(tileSize, profile.RegionNoiseScalePixels <= 0 ? 96 : profile.RegionNoiseScalePixels);
        var noiseStrength = Math.Clamp(profile.MacroNoiseStrength <= 0f ? profile.RegionTextureUnifyStrength : profile.MacroNoiseStrength, 0f, 0.5f);
        var maxDelta = 24f * noiseStrength;
        for (var y = 0; y < tileSize; y++)
        {
            if ((y & 7) == 0) cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < tileSize; x++)
            {
                var color = tile.Pixels[y * tile.Width + x];
                if (overlap > 0 && hasLeft && x < overlap)
                {
                    var neighborX = localX - overlap + x;
                    if (neighborX >= 0)
                    {
                        var amount = SmoothStep((x + 1) / (float)(overlap + 1));
                        color = FastBitmapBuffer.LerpArgb(canvas.Pixels[(localY + y) * canvas.Width + neighborX], color, amount);
                        stats.InteriorSeamBlendPixelCount++;
                    }
                }

                if (overlap > 0 && hasTop && y < overlap)
                {
                    var neighborY = localY - overlap + y;
                    if (neighborY >= 0)
                    {
                        var amount = SmoothStep((y + 1) / (float)(overlap + 1));
                        color = FastBitmapBuffer.LerpArgb(canvas.Pixels[neighborY * canvas.Width + localX + x], color, amount);
                        stats.InteriorSeamBlendPixelCount++;
                    }
                }

                if (maxDelta > 0.01f && ((color >>> 24) & 0xFF) > 0)
                {
                    var worldX = cellX * tileSize + x;
                    var worldY = cellY * tileSize + y;
                    var noise = ValueNoise(profile.Seed, worldX / (float)scale, worldY / (float)scale, region.TerrainId, 0x9E3779B9u) * 2f - 1f;
                    color = AdjustLuma(color, noise * maxDelta);
                    stats.MacroNoiseAppliedPixels++;
                }

                canvas.Pixels[(localY + y) * canvas.Width + localX + x] = color;
            }
        }
    }

    private static void BlendSecondaryPatchIfAvailable(
        Bitmap tile,
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        TerrainVisualRegionPlan region,
        IReadOnlyList<MaterialAsset> candidates,
        MaterialAsset primary,
        Func<MaterialAsset, int, TileVisualStats, Bitmap> renderMaterialTile,
        int index,
        TerrainRegionTextureSynthesisStats stats,
        CancellationToken cancellationToken)
    {
        if (profile.InteriorSecondaryBlendStrength <= 0.001f || candidates.Count <= 1)
        {
            return;
        }

        var alternatives = candidates
            .Where(asset => !ReferenceEquals(asset, primary) && !asset.FilePath.Equals(primary.FilePath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (alternatives.Count == 0)
        {
            return;
        }

        var hash = StableHash(profile.Seed, index, region.TerrainId, 0xA24BAED5u);
        var secondary = alternatives[(int)(hash % (uint)alternatives.Count)];
        using var secondaryTile = renderMaterialTile(secondary, index, region.TargetStyleStats);
        var transform = PickRegionTransform(profile, region.SurfaceKind, index ^ 0x417, secondary);
        if (ApplyTileTransform(secondaryTile, transform))
        {
            stats.TileTransformCount++;
        }

        using var primaryBuffer = FastBitmapBuffer.FromBitmap(tile);
        using var secondaryBuffer = FastBitmapBuffer.FromBitmap(secondaryTile);
        var strength = Math.Clamp(profile.InteriorSecondaryBlendStrength, 0f, 0.32f);
        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var cellX = index % draft.GridWidth;
        var cellY = index / draft.GridWidth;
        var scale = Math.Max(tileSize, profile.RegionNoiseScalePixels <= 0 ? 96 : profile.RegionNoiseScalePixels);
        var changed = 0;
        for (var y = 0; y < primaryBuffer.Height; y++)
        {
            if ((y & 7) == 0) cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < primaryBuffer.Width; x++)
            {
                var pixel = y * primaryBuffer.Width + x;
                var color = primaryBuffer.Pixels[pixel];
                if (((color >>> 24) & 0xFF) == 0) continue;
                var worldX = cellX * tileSize + x;
                var worldY = cellY * tileSize + y;
                var noise = ValueNoise(profile.Seed, worldX / (float)scale, worldY / (float)scale, region.TerrainId, 0xC2B2AE35u);
                var amount = strength * (0.45f + noise * 0.55f);
                primaryBuffer.Pixels[pixel] = FastBitmapBuffer.LerpArgb(color, secondaryBuffer.Pixels[pixel], amount);
                changed++;
            }
        }

        if (changed > 0)
        {
            primaryBuffer.CopyTo(tile);
            stats.SecondaryPatchBlendPixelCount += changed;
        }
    }

    private static Bitmap CropTile(FastBitmapBuffer canvas, int localX, int localY, int tileSize)
    {
        var tile = new FastBitmapBuffer(tileSize, tileSize);
        for (var y = 0; y < tileSize; y++)
        {
            Array.Copy(canvas.Pixels, (localY + y) * canvas.Width + localX, tile.Pixels, y * tile.Width, tileSize);
        }

        return tile.ToBitmap();
    }

    private static TerrainTileTransform PickRegionTransform(
        TerrainVisualProfile profile,
        TerrainVisualSurfaceKind surfaceKind,
        int index,
        MaterialAsset material)
    {
        if (!profile.EnableNaturalTileTransforms ||
            !TerrainVisualSurfaceClassifier.SupportsRandomTransforms(surfaceKind) ||
            !TerrainInteriorSynthesisPlanner.IsInteriorPatchCandidate(material))
        {
            return TerrainTileTransform.None;
        }

        var hash = StableHash(profile.Seed, index, material.TerrainId ?? 0, (uint)(material.VariantIndex + 0x632BE5ABu));
        if (surfaceKind == TerrainVisualSurfaceKind.LiquidArea)
        {
            return (hash % 8) switch
            {
                0 => TerrainTileTransform.FlipX,
                1 => TerrainTileTransform.FlipY,
                _ => TerrainTileTransform.None
            };
        }

        return (hash % 10) switch
        {
            0 => TerrainTileTransform.FlipX,
            1 => TerrainTileTransform.FlipY,
            2 => TerrainTileTransform.Rotate180,
            _ => TerrainTileTransform.None
        };
    }

    private static bool ApplyTileTransform(Bitmap tile, TerrainTileTransform transform)
    {
        if (transform == TerrainTileTransform.None) return false;
        using var source = FastBitmapBuffer.FromBitmap(tile);
        var target = new FastBitmapBuffer(source.Width, source.Height);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var sourceIndex = y * source.Width + x;
                var (tx, ty) = transform switch
                {
                    TerrainTileTransform.Rotate180 => (source.Width - 1 - x, source.Height - 1 - y),
                    TerrainTileTransform.FlipX => (source.Width - 1 - x, y),
                    TerrainTileTransform.FlipY => (x, source.Height - 1 - y),
                    _ => (x, y)
                };
                target.Pixels[ty * target.Width + tx] = source.Pixels[sourceIndex];
            }
        }

        target.CopyTo(tile);
        return true;
    }

    private static double ColorDistanceSquared(int left, int right)
    {
        var dr = ((left >>> 16) & 0xFF) - ((right >>> 16) & 0xFF);
        var dg = ((left >>> 8) & 0xFF) - ((right >>> 8) & 0xFF);
        var db = (left & 0xFF) - (right & 0xFF);
        return dr * dr + dg * dg + db * db;
    }

    private static int AdjustLuma(int color, float delta)
    {
        var alpha = (color >>> 24) & 0xFF;
        var red = Math.Clamp((int)MathF.Round(((color >>> 16) & 0xFF) + delta), 0, 255);
        var green = Math.Clamp((int)MathF.Round(((color >>> 8) & 0xFF) + delta * 0.9f), 0, 255);
        var blue = Math.Clamp((int)MathF.Round((color & 0xFF) + delta * 0.75f), 0, 255);
        return FastBitmapBuffer.Pack(alpha, red, green, blue);
    }

    private static float ValueNoise(string? seed, float x, float y, byte terrainId, uint salt)
    {
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var tx = SmoothStep(x - x0);
        var ty = SmoothStep(y - y0);
        var n00 = StableNoise01(seed, x0, y0, terrainId, salt);
        var n10 = StableNoise01(seed, x0 + 1, y0, terrainId, salt);
        var n01 = StableNoise01(seed, x0, y0 + 1, terrainId, salt);
        var n11 = StableNoise01(seed, x0 + 1, y0 + 1, terrainId, salt);
        var top = n00 + (n10 - n00) * tx;
        var bottom = n01 + (n11 - n01) * tx;
        return top + (bottom - top) * ty;
    }

    private static float SmoothStep(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return value * value * (3f - 2f * value);
    }

    private static float StableNoise01(string? seed, int x, int y, byte terrainId, uint salt)
    {
        unchecked
        {
            var hash = 2166136261u ^ salt;
            foreach (var ch in seed ?? string.Empty)
            {
                hash ^= ch;
                hash *= 16777619u;
            }

            hash ^= (uint)(x * 73856093);
            hash *= 16777619u;
            hash ^= (uint)(y * 19349663);
            hash *= 16777619u;
            hash ^= terrainId;
            hash *= 16777619u;
            return hash / (float)uint.MaxValue;
        }
    }

    private static float StableNoise01(string? seed, int index, byte terrainId, uint salt)
        => StableHash(seed, index, terrainId, salt) / (float)uint.MaxValue;

    private static uint StableHash(string? seed, int index, byte terrainId, uint salt)
    {
        unchecked
        {
            var hash = 2166136261u ^ salt;
            foreach (var ch in seed ?? string.Empty)
            {
                hash ^= ch;
                hash *= 16777619u;
            }

            hash ^= (uint)index;
            hash *= 16777619u;
            hash ^= terrainId;
            hash *= 16777619u;
            return hash;
        }
    }

    private sealed record SelectedRegionPatch(MaterialAsset Asset, Bitmap Tile);
}

internal sealed class TerrainRegionTextureSynthesisStats
{
    public int RegionTextureCanvasCount { get; set; }
    public int QuiltedPatchCount { get; set; }
    public int PatchOverlapRejectedCount { get; set; }
    public int MacroNoiseAppliedPixels { get; set; }
    public int InteriorSeamBlendPixelCount { get; set; }
    public int SecondaryPatchBlendPixelCount { get; set; }
    public int TileTransformCount { get; set; }
}
