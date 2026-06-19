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
        var radius = Math.Clamp(draft.FeatherRadius <= 0 ? 8 : draft.FeatherRadius, 1, tileSize / 2);
        var strength = Math.Clamp(draft.BeautifyStrength, 1, 3);

        ApplyEdgeFeather(draft, output, tileSize, radius, strength);
        ApplySubtleNoise(draft, output, strength);
        return output;
    }

    private static void ApplyEdgeFeather(MapWorkbenchDraft draft, Bitmap output, int tileSize, int radius, int strength)
    {
        using var source = new Bitmap(output);
        for (var y = 0; y < draft.GridHeight; y++)
        {
            for (var x = 0; x < draft.GridWidth; x++)
            {
                var index = y * draft.GridWidth + x;
                var terrain = draft.TerrainCells[index];
                BlendNeighbor(draft, source, output, x, y, terrain, x - 1, y, EdgeDirection.Left, tileSize, radius, strength);
                BlendNeighbor(draft, source, output, x, y, terrain, x + 1, y, EdgeDirection.Right, tileSize, radius, strength);
                BlendNeighbor(draft, source, output, x, y, terrain, x, y - 1, EdgeDirection.Top, tileSize, radius, strength);
                BlendNeighbor(draft, source, output, x, y, terrain, x, y + 1, EdgeDirection.Bottom, tileSize, radius, strength);
            }
        }
    }

    private static void BlendNeighbor(
        MapWorkbenchDraft draft,
        Bitmap source,
        Bitmap output,
        int x,
        int y,
        byte terrain,
        int neighborX,
        int neighborY,
        EdgeDirection direction,
        int tileSize,
        int radius,
        int strength)
    {
        if (neighborX < 0 || neighborY < 0 || neighborX >= draft.GridWidth || neighborY >= draft.GridHeight) return;
        var neighbor = draft.TerrainCells[neighborY * draft.GridWidth + neighborX];
        if (neighbor == terrain) return;

        var currentRank = GetTerrainRank(terrain);
        var neighborRank = GetTerrainRank(neighbor);
        if (currentRank > neighborRank) return;

        var maxWeight = Math.Clamp(0.16f + strength * 0.08f + Math.Abs(currentRank - neighborRank) * 0.02f, 0.18f, 0.46f);
        var startX = x * tileSize;
        var startY = y * tileSize;
        var neighborStartX = neighborX * tileSize;
        var neighborStartY = neighborY * tileSize;

        for (var offset = 0; offset < radius; offset++)
        {
            var weight = maxWeight * (radius - offset) / radius;
            for (var span = 0; span < tileSize; span++)
            {
                var (targetX, targetY, sampleX, sampleY) = direction switch
                {
                    EdgeDirection.Left => (startX + offset, startY + span, neighborStartX + tileSize - 1 - offset, neighborStartY + span),
                    EdgeDirection.Right => (startX + tileSize - 1 - offset, startY + span, neighborStartX + offset, neighborStartY + span),
                    EdgeDirection.Top => (startX + span, startY + offset, neighborStartX + span, neighborStartY + tileSize - 1 - offset),
                    _ => (startX + span, startY + tileSize - 1 - offset, neighborStartX + span, neighborStartY + offset)
                };
                if ((uint)targetX >= output.Width || (uint)targetY >= output.Height ||
                    (uint)sampleX >= source.Width || (uint)sampleY >= source.Height)
                {
                    continue;
                }

                var current = output.GetPixel(targetX, targetY);
                var sample = source.GetPixel(sampleX, sampleY);
                output.SetPixel(targetX, targetY, Blend(current, sample, weight));
            }
        }
    }

    private static void ApplySubtleNoise(MapWorkbenchDraft draft, Bitmap output, int strength)
    {
        var locked = output.LockBits(
            new Rectangle(0, 0, output.Width, output.Height),
            ImageLockMode.ReadWrite,
            PixelFormat.Format32bppArgb);
        try
        {
            var byteCount = Math.Abs(locked.Stride) * output.Height;
            var buffer = new byte[byteCount];
            Marshal.Copy(locked.Scan0, buffer, 0, byteCount);
            var maxDelta = 2 + strength * 2;
            for (var y = 0; y < output.Height; y++)
            {
                var row = y * locked.Stride;
                for (var x = 0; x < output.Width; x++)
                {
                    var offset = row + x * 4;
                    if (buffer[offset + 3] == 0) continue;
                    var delta = StableNoise(draft.DraftId, x, y, maxDelta);
                    buffer[offset] = ClampByte(buffer[offset] + delta);
                    buffer[offset + 1] = ClampByte(buffer[offset + 1] + delta);
                    buffer[offset + 2] = ClampByte(buffer[offset + 2] + delta);
                }
            }

            Marshal.Copy(buffer, 0, locked.Scan0, byteCount);
        }
        finally
        {
            output.UnlockBits(locked);
        }
    }

    private static int StableNoise(string seed, int x, int y, int maxDelta)
    {
        var hash = StableHash(seed, x, y);
        return (int)(hash % (uint)(maxDelta * 2 + 1)) - maxDelta;
    }

    private static byte ClampByte(int value)
        => (byte)Math.Clamp(value, 0, 255);

    private static Color Blend(Color current, Color sample, float weight)
    {
        var inverse = 1f - weight;
        return Color.FromArgb(
            current.A,
            ClampByte((int)MathF.Round(current.R * inverse + sample.R * weight)),
            ClampByte((int)MathF.Round(current.G * inverse + sample.G * weight)),
            ClampByte((int)MathF.Round(current.B * inverse + sample.B * weight)));
    }

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

    private enum EdgeDirection
    {
        Left,
        Right,
        Top,
        Bottom
    }
}
