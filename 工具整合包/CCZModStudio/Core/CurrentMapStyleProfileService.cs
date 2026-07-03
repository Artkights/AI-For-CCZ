using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class CurrentMapStyleProfileService
{
    private const int MaxSamplesPerTerrain = 8;

    public CurrentMapStyleProfile BuildProfile(MapWorkbenchDraft draft, string sampleRoot, bool writeSamples)
    {
        if (draft.GridWidth <= 0 ||
            draft.GridHeight <= 0 ||
            draft.TerrainCells.Length != draft.CellCount ||
            string.IsNullOrWhiteSpace(draft.BaseLayerPath) ||
            !File.Exists(draft.BaseLayerPath))
        {
            return new CurrentMapStyleProfile
            {
                SourceMapPath = draft.BaseLayerPath ?? string.Empty,
                SampleRoot = sampleRoot ?? string.Empty,
                GridWidth = draft.GridWidth,
                GridHeight = draft.GridHeight,
                TileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize
            };
        }

        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        using var source = Image.FromFile(draft.BaseLayerPath);
        if (source.Width != draft.GridWidth * tileSize || source.Height != draft.GridHeight * tileSize)
        {
            return new CurrentMapStyleProfile
            {
                SourceMapPath = draft.BaseLayerPath,
                SampleRoot = sampleRoot ?? string.Empty,
                GridWidth = draft.GridWidth,
                GridHeight = draft.GridHeight,
                TileSize = tileSize
            };
        }

        var sampleTerrainCells = draft.OriginalTerrainCells.Length == draft.CellCount
            ? draft.OriginalTerrainCells
            : draft.TerrainCells;
        var objectFootprints = BuildObjectFootprints(draft, tileSize, sampleTerrainCells);
        var changedTerrainCells = BuildChangedTerrainCells(draft);
        var minPureSamples = Math.Clamp(draft.TerrainVisualProfile.MinPureSamplesPerTerrain <= 0 ? 4 : draft.TerrainVisualProfile.MinPureSamplesPerTerrain, 1, MaxSamplesPerTerrain);
        var terrains = new List<CurrentMapStyleTerrain>();
        foreach (var terrainGroup in sampleTerrainCells
                     .Select((terrain, index) => (Terrain: terrain, Index: index))
                     .GroupBy(item => item.Terrain)
                     .OrderBy(group => group.Key))
        {
            var cells = terrainGroup.Select(item => item.Index).ToList();
            var cleanPureSamples = new List<CurrentMapStyleTileSample>();
            var cleanBoundarySamples = new List<CurrentMapStyleTileSample>();
            var contaminatedSamples = new List<CurrentMapStyleTileSample>();
            var samples = new List<CurrentMapStyleTileSample>();
            foreach (var index in cells.OrderBy(index => StableHash(draft.DraftId, index, terrainGroup.Key, 0xA341316Cu)))
            {
                var x = index % draft.GridWidth;
                var y = index / draft.GridWidth;
                using var tile = CropTile(source, tileSize, x, y);
                var stats = TileVisualStatsCalculator.Calculate(tile);
                var isBoundary = IsBoundaryCell(draft, sampleTerrainCells, index);
                var contaminationReason = GetContaminationReason(
                    draft,
                    tile,
                    stats,
                    index,
                    objectFootprints,
                    changedTerrainCells);
                var isContaminated = contaminationReason.Length > 0;
                var samplePath = string.Empty;
                if (!isContaminated && writeSamples && !string.IsNullOrWhiteSpace(sampleRoot))
                {
                    var terrainDir = Path.Combine(
                        sampleRoot,
                        "terrain_" + terrainGroup.Key.ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
                        isBoundary ? "boundary" : "pure");
                    Directory.CreateDirectory(terrainDir);
                    samplePath = Path.Combine(
                        terrainDir,
                        $"{index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture)}.png");
                    tile.Save(samplePath, ImageFormat.Png);
                }

                var sample = new CurrentMapStyleTileSample
                {
                    TerrainId = terrainGroup.Key,
                    CellIndex = index,
                    CellX = x,
                    CellY = y,
                    IsBoundary = isBoundary,
                    IsContaminated = isContaminated,
                    ContaminationReason = contaminationReason,
                    FilePath = samplePath,
                    Stats = stats
                };
                if (isContaminated)
                {
                    contaminatedSamples.Add(sample);
                }
                else if (isBoundary)
                {
                    cleanBoundarySamples.Add(sample);
                }
                else
                {
                    cleanPureSamples.Add(sample);
                }
            }

            samples.AddRange(cleanPureSamples.Take(MaxSamplesPerTerrain));
            if (samples.Count < minPureSamples)
            {
                samples.AddRange(cleanBoundarySamples.Take(MaxSamplesPerTerrain - samples.Count));
            }

            terrains.Add(new CurrentMapStyleTerrain
            {
                TerrainId = terrainGroup.Key,
                TerrainName = MaterialLibraryIndexer.GetBuiltInTerrainName(terrainGroup.Key),
                Stats = TileVisualStatsCalculator.Combine(samples.Select(sample => sample.Stats)),
                Samples = samples,
                PureSamples = cleanPureSamples.Take(MaxSamplesPerTerrain).ToList(),
                BoundarySamples = cleanBoundarySamples.Take(MaxSamplesPerTerrain).ToList(),
                ContaminatedSamples = contaminatedSamples.ToList(),
                PureSampleCount = cleanPureSamples.Count,
                BoundarySampleCount = cleanBoundarySamples.Count
            });
        }

        return new CurrentMapStyleProfile
        {
            SourceMapPath = draft.BaseLayerPath,
            SampleRoot = sampleRoot ?? string.Empty,
            GridWidth = draft.GridWidth,
            GridHeight = draft.GridHeight,
            TileSize = tileSize,
            Terrains = terrains
        };
    }

    private static bool IsBoundaryCell(MapWorkbenchDraft draft, byte[] terrainCells, int index)
    {
        var terrain = terrainCells[index];
        var x = index % draft.GridWidth;
        var y = index / draft.GridWidth;
        return IsDifferent(draft, terrainCells, x, y - 1, terrain) ||
               IsDifferent(draft, terrainCells, x + 1, y, terrain) ||
               IsDifferent(draft, terrainCells, x, y + 1, terrain) ||
               IsDifferent(draft, terrainCells, x - 1, y, terrain);
    }

    private static bool IsDifferent(MapWorkbenchDraft draft, byte[] terrainCells, int x, int y, byte terrain)
        => x < 0 ||
           y < 0 ||
           x >= draft.GridWidth ||
           y >= draft.GridHeight ||
           terrainCells[y * draft.GridWidth + x] != terrain;

    private static HashSet<int> BuildObjectFootprints(MapWorkbenchDraft draft, int tileSize, byte[] sampleTerrainCells)
    {
        var result = ObjectGroundInpaintPlanner.BuildObjectFootprintIndexes(
            draft,
            tileSize,
            includeTerrainObjects: false);
        if (draft.TerrainVisualProfile.GroundInpaintIncludesTerrainObjects && sampleTerrainCells.Length == draft.CellCount)
        {
            for (var i = 0; i < sampleTerrainCells.Length; i++)
            {
                if (ObjectGroundInpaintPlanner.IsObjectFootprintTerrain(sampleTerrainCells[i]))
                {
                    result.Add(i);
                }
            }
        }

        return result;
    }

    private static HashSet<int> BuildChangedTerrainCells(MapWorkbenchDraft draft)
    {
        var result = new HashSet<int>();
        if (draft.OriginalTerrainCells.Length != draft.CellCount || draft.TerrainCells.Length != draft.CellCount)
        {
            return result;
        }

        for (var i = 0; i < draft.CellCount; i++)
        {
            if (draft.OriginalTerrainCells[i] != draft.TerrainCells[i])
            {
                result.Add(i);
            }
        }

        return result;
    }

    private static string GetContaminationReason(
        MapWorkbenchDraft draft,
        Bitmap tile,
        TileVisualStats stats,
        int index,
        IReadOnlySet<int> objectFootprints,
        IReadOnlySet<int> changedTerrainCells)
    {
        if (draft.TerrainVisualProfile.IgnoreBasePixelsUnderObjects && objectFootprints.Contains(index))
        {
            return "object-footprint";
        }

        if (changedTerrainCells.Contains(index))
        {
            return "changed-terrain";
        }

        if (HasEdgeConnectedBlackBackground(tile, draft.TerrainVisualProfile.AlphaRepairBlackThreshold))
        {
            return "edge-black-background";
        }

        if (stats.Contrast > 56f && stats.EdgeStrength > 36f)
        {
            return "high-object-detail";
        }

        return string.Empty;
    }

    private static bool HasEdgeConnectedBlackBackground(Bitmap tile, int threshold)
    {
        using var buffer = FastBitmapBuffer.FromBitmap(tile);
        threshold = Math.Clamp(threshold <= 0 ? 24 : threshold, 1, 96);
        var nearBlack = new bool[buffer.Pixels.Length];
        for (var i = 0; i < buffer.Pixels.Length; i++)
        {
            nearBlack[i] = IsNearBlack(buffer.Pixels[i], threshold);
        }

        var visited = new bool[nearBlack.Length];
        var queue = new Queue<int>();
        void Enqueue(int x, int y)
        {
            if ((uint)x >= (uint)buffer.Width || (uint)y >= (uint)buffer.Height) return;
            var pixel = y * buffer.Width + x;
            if (!nearBlack[pixel] || visited[pixel]) return;
            visited[pixel] = true;
            queue.Enqueue(pixel);
        }

        for (var x = 0; x < buffer.Width; x++)
        {
            Enqueue(x, 0);
            Enqueue(x, buffer.Height - 1);
        }

        for (var y = 1; y + 1 < buffer.Height; y++)
        {
            Enqueue(0, y);
            Enqueue(buffer.Width - 1, y);
        }

        var count = 0;
        while (queue.Count > 0)
        {
            var pixel = queue.Dequeue();
            count++;
            if (count > buffer.Pixels.Length / 18)
            {
                return true;
            }

            var x = pixel % buffer.Width;
            var y = pixel / buffer.Width;
            Enqueue(x - 1, y);
            Enqueue(x + 1, y);
            Enqueue(x, y - 1);
            Enqueue(x, y + 1);
        }

        return false;
    }

    private static bool IsNearBlack(int color, int threshold)
    {
        var alpha = (color >>> 24) & 0xFF;
        if (alpha < 12) return false;
        var red = (color >>> 16) & 0xFF;
        var green = (color >>> 8) & 0xFF;
        var blue = color & 0xFF;
        var luma = red * 0.2126 + green * 0.7152 + blue * 0.0722;
        return red <= threshold && green <= threshold && blue <= threshold && luma <= threshold;
    }

    private static Bitmap CropTile(Image source, int tileSize, int cellX, int cellY)
    {
        var bitmap = new Bitmap(MapResourceItem.MapTilePixelSize, MapResourceItem.MapTilePixelSize, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(
            source,
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            new Rectangle(cellX * tileSize, cellY * tileSize, tileSize, tileSize),
            GraphicsUnit.Pixel);
        return bitmap;
    }

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
}

public static class TileVisualStatsCalculator
{
    public static TileVisualStats Calculate(Bitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0) return TileVisualStats.Empty;

        using var buffer = FastBitmapBuffer.FromBitmap(bitmap);
        var count = 0;
        double totalR = 0;
        double totalG = 0;
        double totalB = 0;
        double totalLum = 0;
        double totalSat = 0;
        var luminance = new double[buffer.Width * buffer.Height];

        for (var y = 0; y < buffer.Height; y++)
        {
            for (var x = 0; x < buffer.Width; x++)
            {
                var color = buffer.Pixels[y * buffer.Width + x];
                if (((color >>> 24) & 0xFF) == 0) continue;
                var r = (color >>> 16) & 0xFF;
                var g = (color >>> 8) & 0xFF;
                var b = color & 0xFF;
                var lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                var max = Math.Max(r, Math.Max(g, b));
                var min = Math.Min(r, Math.Min(g, b));
                var sat = max == 0 ? 0 : (max - min) / (double)max;
                totalR += r;
                totalG += g;
                totalB += b;
                totalLum += lum;
                totalSat += sat;
                luminance[y * buffer.Width + x] = lum;
                count++;
            }
        }

        if (count == 0) return TileVisualStats.Empty;

        var avgLum = totalLum / count;
        double variance = 0;
        double edge = 0;
        var edgeCount = 0;
        for (var y = 0; y < buffer.Height; y++)
        {
            for (var x = 0; x < buffer.Width; x++)
            {
                var lum = luminance[y * buffer.Width + x];
                variance += (lum - avgLum) * (lum - avgLum);
                if (x + 1 < buffer.Width)
                {
                    edge += Math.Abs(lum - luminance[y * buffer.Width + x + 1]);
                    edgeCount++;
                }

                if (y + 1 < buffer.Height)
                {
                    edge += Math.Abs(lum - luminance[(y + 1) * buffer.Width + x]);
                    edgeCount++;
                }
            }
        }

        var contrast = Math.Sqrt(variance / count);
        var edgeStrength = edgeCount == 0 ? 0 : edge / edgeCount;
        return new TileVisualStats(
            (float)(totalR / count),
            (float)(totalG / count),
            (float)(totalB / count),
            (float)avgLum,
            (float)(totalSat / count),
            (float)contrast,
            (float)edgeStrength,
            (float)((contrast + edgeStrength) * 0.5));
    }

    public static TileVisualStats Combine(IEnumerable<TileVisualStats> stats)
    {
        var list = stats.Where(item => item != TileVisualStats.Empty).ToList();
        if (list.Count == 0) return TileVisualStats.Empty;
        return new TileVisualStats(
            list.Average(item => item.AverageR),
            list.Average(item => item.AverageG),
            list.Average(item => item.AverageB),
            list.Average(item => item.Luminance),
            list.Average(item => item.Saturation),
            list.Average(item => item.Contrast),
            list.Average(item => item.EdgeStrength),
            list.Average(item => item.Texture));
    }

    public static double Distance(TileVisualStats left, TileVisualStats right)
    {
        if (left == TileVisualStats.Empty || right == TileVisualStats.Empty) return 0;
        static double Square(double value) => value * value;
        return Square((left.AverageR - right.AverageR) / 255.0) * 1.4 +
               Square((left.AverageG - right.AverageG) / 255.0) * 1.4 +
               Square((left.AverageB - right.AverageB) / 255.0) * 1.4 +
               Square((left.Luminance - right.Luminance) / 255.0) * 1.2 +
               Square(left.Saturation - right.Saturation) * 0.8 +
               Square((left.Contrast - right.Contrast) / 128.0) * 0.7 +
               Square((left.EdgeStrength - right.EdgeStrength) / 128.0) * 0.6 +
               Square((left.Texture - right.Texture) / 128.0) * 0.6;
    }
}
