using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class TerrainMapBeautifyService
{
    public Bitmap Beautify(MapWorkbenchDraft draft, Bitmap baseTerrain)
    {
        var output = new Bitmap(baseTerrain);
        if (!draft.BeautifyGeneratedMap ||
            draft.BeautifyStrength <= 0 ||
            draft.TerrainCells.Length != draft.CellCount ||
            draft.GridWidth <= 0 ||
            draft.GridHeight <= 0)
        {
            return output;
        }

        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var radius = Math.Clamp(draft.FeatherRadius <= 0 ? 8 : draft.FeatherRadius, 1, Math.Max(1, tileSize / 2));
        var strength = Math.Clamp(draft.BeautifyStrength, 1, 3);

        var width = output.Width;
        var height = output.Height;
        var pixels = BitmapBuffer.FromBitmap(output);
        try
        {
            var source = new byte[pixels.Bytes.Length];
            Buffer.BlockCopy(pixels.Bytes, 0, source, 0, source.Length);

            ApplyPhotoshopStyleTerrainFeather(draft, source, pixels.Bytes, width, height, tileSize, radius, strength);
            ApplyRegionColorUnifyAndNoise(draft, pixels.Bytes, width, height, tileSize, strength);
            pixels.CopyBack();
            return output;
        }
        finally
        {
            pixels.Dispose();
        }
    }

    private static void ApplyPhotoshopStyleTerrainFeather(
        MapWorkbenchDraft draft,
        byte[] source,
        byte[] target,
        int width,
        int height,
        int tileSize,
        int radius,
        int strength)
    {
        foreach (var terrain in draft.TerrainCells.Distinct().OrderBy(GetTerrainRank).ThenBy(id => id))
        {
            var mask = BuildTerrainMask(draft, terrain, width, height, tileSize);
            var inside = BuildFeatherDistance(mask, width, height, radius, inside: true);
            var outside = BuildFeatherDistance(mask, width, height, radius, inside: false);

            for (var y = 0; y < draft.GridHeight; y++)
            {
                for (var x = 0; x < draft.GridWidth; x++)
                {
                    var index = y * draft.GridWidth + x;
                    if (draft.TerrainCells[index] != terrain) continue;
                    BlendCellEdges(draft, source, target, mask, inside, outside, x, y, tileSize, radius, strength);
                }
            }
        }
    }

    private static bool[] BuildTerrainMask(MapWorkbenchDraft draft, byte terrain, int width, int height, int tileSize)
    {
        var mask = new bool[width * height];
        for (var y = 0; y < draft.GridHeight; y++)
        {
            for (var x = 0; x < draft.GridWidth; x++)
            {
                var index = y * draft.GridWidth + x;
                if (draft.TerrainCells[index] != terrain) continue;
                var left = x * tileSize;
                var top = y * tileSize;
                var right = Math.Min(width, left + tileSize);
                var bottom = Math.Min(height, top + tileSize);
                for (var py = top; py < bottom; py++)
                {
                    var row = py * width;
                    for (var px = left; px < right; px++)
                    {
                        mask[row + px] = true;
                    }
                }
            }
        }

        return mask;
    }

    private static byte[] BuildFeatherDistance(bool[] mask, int width, int height, int radius, bool inside)
    {
        var distance = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = y * width + x;
                if (mask[offset] != inside) continue;
                var edgeDistance = FindDistanceToMaskEdge(mask, width, height, x, y, radius, inside);
                distance[offset] = (byte)Math.Clamp(edgeDistance, 0, radius);
            }
        }

        return distance;
    }

    private static int FindDistanceToMaskEdge(bool[] mask, int width, int height, int x, int y, int radius, bool inside)
    {
        for (var distance = 0; distance <= radius; distance++)
        {
            for (var dy = -distance; dy <= distance; dy++)
            {
                var py = y + dy;
                if (py < 0 || py >= height) continue;
                var dxMax = distance - Math.Abs(dy);
                for (var dx = -dxMax; dx <= dxMax; dx++)
                {
                    var px = x + dx;
                    if (px < 0 || px >= width) continue;
                    if (mask[py * width + px] != inside)
                    {
                        return distance;
                    }
                }
            }
        }

        return radius;
    }

    private static void BlendCellEdges(
        MapWorkbenchDraft draft,
        byte[] source,
        byte[] target,
        bool[] mask,
        byte[] insideDistance,
        byte[] outsideDistance,
        int cellX,
        int cellY,
        int tileSize,
        int radius,
        int strength)
    {
        var terrain = draft.TerrainCells[cellY * draft.GridWidth + cellX];
        var shape = AnalyzeShape(draft, cellX, cellY, terrain);
        var rank = GetTerrainRank(terrain);
        var baseWeight = Math.Clamp(0.12f + strength * 0.08f + shape.CornerWeight * 0.05f, 0.18f, 0.5f);
        var press = rank >= 4 ? 1.1f : rank <= 1 ? 0.82f : 1f;

        foreach (var direction in CardinalDirections)
        {
            var nx = cellX + direction.Dx;
            var ny = cellY + direction.Dy;
            if (nx < 0 || ny < 0 || nx >= draft.GridWidth || ny >= draft.GridHeight) continue;
            var neighbor = draft.TerrainCells[ny * draft.GridWidth + nx];
            if (neighbor == terrain) continue;

            var neighborRank = GetTerrainRank(neighbor);
            var rankWeight = rank <= neighborRank ? 1f : 0.72f;
            var maxWeight = Math.Clamp(baseWeight * press * rankWeight, 0.08f, 0.58f);
            BlendOneEdge(source, target, mask, insideDistance, outsideDistance, draft, cellX, cellY, nx, ny, direction, tileSize, radius, maxWeight, strength, shape);
        }
    }

    private static void BlendOneEdge(
        byte[] source,
        byte[] target,
        bool[] mask,
        byte[] insideDistance,
        byte[] outsideDistance,
        MapWorkbenchDraft draft,
        int cellX,
        int cellY,
        int neighborX,
        int neighborY,
        Direction direction,
        int tileSize,
        int radius,
        float maxWeight,
        int strength,
        ShapeInfo shape)
    {
        var width = draft.GridWidth * tileSize;
        var height = draft.GridHeight * tileSize;
        var startX = cellX * tileSize;
        var startY = cellY * tileSize;
        var neighborStartX = neighborX * tileSize;
        var neighborStartY = neighborY * tileSize;
        var waveAmplitude = Math.Max(1, strength);

        for (var offset = -radius; offset < radius; offset++)
        {
            for (var span = 0; span < tileSize; span++)
            {
                var wobble = StableNoise(draft.DraftId, cellX * 131 + span, cellY * 197 + offset, waveAmplitude);
                var featherOffset = offset + wobble;
                var currentX = direction.Dx switch
                {
                    -1 => startX + Math.Clamp(featherOffset + radius, 0, tileSize - 1),
                    1 => startX + Math.Clamp(tileSize - 1 - (featherOffset + radius), 0, tileSize - 1),
                    _ => startX + span
                };
                var currentY = direction.Dy switch
                {
                    -1 => startY + Math.Clamp(featherOffset + radius, 0, tileSize - 1),
                    1 => startY + Math.Clamp(tileSize - 1 - (featherOffset + radius), 0, tileSize - 1),
                    _ => startY + span
                };
                if ((uint)currentX >= (uint)width || (uint)currentY >= (uint)height) continue;

                var maskOffset = currentY * width + currentX;
                var distance = mask[maskOffset] ? insideDistance[maskOffset] : outsideDistance[maskOffset];
                if (distance > radius) continue;
                var alpha = FeatherCurve(1f - distance / (float)Math.Max(1, radius));
                if (alpha <= 0f) continue;

                var neighborSampleX = direction.Dx switch
                {
                    -1 => neighborStartX + tileSize - 1 - Math.Clamp(Math.Abs(offset), 0, tileSize - 1),
                    1 => neighborStartX + Math.Clamp(Math.Abs(offset), 0, tileSize - 1),
                    _ => neighborStartX + span
                };
                var neighborSampleY = direction.Dy switch
                {
                    -1 => neighborStartY + tileSize - 1 - Math.Clamp(Math.Abs(offset), 0, tileSize - 1),
                    1 => neighborStartY + Math.Clamp(Math.Abs(offset), 0, tileSize - 1),
                    _ => neighborStartY + span
                };
                if ((uint)neighborSampleX >= (uint)width || (uint)neighborSampleY >= (uint)height) continue;

                var shapeBoost = shape.IsNarrow ? 1.16f : shape.IsIsland ? 1.24f : 1f;
                var weight = Math.Clamp(maxWeight * alpha * shapeBoost, 0f, 0.72f);
                BlendPixel(target, source, maskOffset, neighborSampleY * width + neighborSampleX, weight);
            }
        }
    }

    private static void ApplyRegionColorUnifyAndNoise(MapWorkbenchDraft draft, byte[] pixels, int width, int height, int tileSize, int strength)
    {
        foreach (var terrain in draft.TerrainCells.Distinct())
        {
            var totals = new long[3];
            var count = 0;
            for (var index = 0; index < draft.TerrainCells.Length; index++)
            {
                if (draft.TerrainCells[index] != terrain) continue;
                var x = index % draft.GridWidth;
                var y = index / draft.GridWidth;
                var sampleX = Math.Min(width - 1, x * tileSize + tileSize / 2);
                var sampleY = Math.Min(height - 1, y * tileSize + tileSize / 2);
                var offset = (sampleY * width + sampleX) * 4;
                totals[0] += pixels[offset];
                totals[1] += pixels[offset + 1];
                totals[2] += pixels[offset + 2];
                count++;
            }

            if (count == 0) continue;
            var avgB = (int)(totals[0] / count);
            var avgG = (int)(totals[1] / count);
            var avgR = (int)(totals[2] / count);
            for (var index = 0; index < draft.TerrainCells.Length; index++)
            {
                if (draft.TerrainCells[index] != terrain) continue;
                var cellX = index % draft.GridWidth;
                var cellY = index / draft.GridWidth;
                var left = cellX * tileSize;
                var top = cellY * tileSize;
                var right = Math.Min(width, left + tileSize);
                var bottom = Math.Min(height, top + tileSize);
                for (var y = top; y < bottom; y++)
                {
                    for (var x = left; x < right; x++)
                    {
                        var offset = (y * width + x) * 4;
                        if (pixels[offset + 3] == 0) continue;
                        var noise = StableNoise(draft.DraftId, x, y, 1 + strength);
                        pixels[offset] = ClampByte((int)MathF.Round(pixels[offset] * 0.95f + avgB * 0.05f) + noise);
                        pixels[offset + 1] = ClampByte((int)MathF.Round(pixels[offset + 1] * 0.95f + avgG * 0.05f) + noise);
                        pixels[offset + 2] = ClampByte((int)MathF.Round(pixels[offset + 2] * 0.95f + avgR * 0.05f) + noise);
                    }
                }
            }
        }
    }

    private static ShapeInfo AnalyzeShape(MapWorkbenchDraft draft, int x, int y, byte terrain)
    {
        var sameLeft = IsSame(draft, x - 1, y, terrain);
        var sameRight = IsSame(draft, x + 1, y, terrain);
        var sameTop = IsSame(draft, x, y - 1, terrain);
        var sameBottom = IsSame(draft, x, y + 1, terrain);
        var cardinal = CountTrue(sameLeft, sameRight, sameTop, sameBottom);
        var diagonal = CountTrue(
            IsSame(draft, x - 1, y - 1, terrain),
            IsSame(draft, x + 1, y - 1, terrain),
            IsSame(draft, x - 1, y + 1, terrain),
            IsSame(draft, x + 1, y + 1, terrain));
        var isNarrow = (sameLeft && sameRight && !sameTop && !sameBottom) ||
                       (sameTop && sameBottom && !sameLeft && !sameRight);
        var isIsland = cardinal == 0 && diagonal <= 1;
        var cornerWeight = cardinal switch
        {
            0 => 3,
            1 => 2,
            2 when !isNarrow => 1,
            _ => 0
        };
        return new ShapeInfo(isNarrow, isIsland, cornerWeight);
    }

    private static bool IsSame(MapWorkbenchDraft draft, int x, int y, byte terrain)
        => x >= 0 && y >= 0 && x < draft.GridWidth && y < draft.GridHeight &&
           draft.TerrainCells[y * draft.GridWidth + x] == terrain;

    private static int CountTrue(params bool[] values)
        => values.Count(value => value);

    private static float FeatherCurve(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static void BlendPixel(byte[] target, byte[] source, int targetPixel, int samplePixel, float weight)
    {
        if (weight <= 0f) return;
        var targetOffset = targetPixel * 4;
        var sampleOffset = samplePixel * 4;
        var inverse = 1f - weight;
        target[targetOffset] = ClampByte((int)MathF.Round(target[targetOffset] * inverse + source[sampleOffset] * weight));
        target[targetOffset + 1] = ClampByte((int)MathF.Round(target[targetOffset + 1] * inverse + source[sampleOffset + 1] * weight));
        target[targetOffset + 2] = ClampByte((int)MathF.Round(target[targetOffset + 2] * inverse + source[sampleOffset + 2] * weight));
    }

    private static int StableNoise(string seed, int x, int y, int maxDelta)
    {
        var hash = StableHash(seed, x, y);
        return (int)(hash % (uint)(maxDelta * 2 + 1)) - maxDelta;
    }

    private static byte ClampByte(int value)
        => (byte)Math.Clamp(value, 0, 255);

    private static uint StableHash(string? seed, int x, int y)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var ch in seed ?? string.Empty)
            {
                hash ^= ch;
                hash *= 16777619u;
            }

            hash ^= (uint)x;
            hash *= 16777619u;
            hash ^= (uint)y;
            hash *= 16777619u;
            return hash;
        }
    }

    private static int GetTerrainRank(byte terrain)
        => terrain switch
        {
            12 or 13 => 0,
            9 or 10 => 1,
            0 or 1 or 3 or 7 => 2,
            2 => 3,
            4 or 5 => 4,
            8 or 14 or 16 or 17 or 18 or 20 or 21 or 22 or 23 or 24 or 27 => 5,
            _ => 2
        };

    private static readonly Direction[] CardinalDirections =
    [
        new(-1, 0),
        new(1, 0),
        new(0, -1),
        new(0, 1)
    ];

    private readonly record struct Direction(int Dx, int Dy);
    private readonly record struct ShapeInfo(bool IsNarrow, bool IsIsland, int CornerWeight);

    private sealed class BitmapBuffer : IDisposable
    {
        private readonly Bitmap _bitmap;
        private readonly BitmapData _data;
        private readonly int _byteCount;
        private bool _disposed;

        private BitmapBuffer(Bitmap bitmap, BitmapData data, byte[] bytes, int byteCount)
        {
            _bitmap = bitmap;
            _data = data;
            Bytes = bytes;
            _byteCount = byteCount;
        }

        public byte[] Bytes { get; }

        public static BitmapBuffer FromBitmap(Bitmap bitmap)
        {
            var data = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadWrite,
                PixelFormat.Format32bppArgb);
            var byteCount = Math.Abs(data.Stride) * bitmap.Height;
            var bytes = new byte[byteCount];
            Marshal.Copy(data.Scan0, bytes, 0, byteCount);
            return new BitmapBuffer(bitmap, data, bytes, byteCount);
        }

        public void CopyBack()
        {
            Marshal.Copy(Bytes, 0, _data.Scan0, _byteCount);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _bitmap.UnlockBits(_data);
            _disposed = true;
        }
    }
}
