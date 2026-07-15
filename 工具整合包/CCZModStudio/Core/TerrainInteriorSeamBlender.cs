using System.Drawing;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

internal sealed class TerrainInteriorSeamBlender
{
    public int Blend(
        Bitmap currentTile,
        Bitmap? leftTile,
        Bitmap? topTile,
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        int index,
        TerrainVisualRegionPlan? region,
        CancellationToken cancellationToken = default)
    {
        if (!profile.UseInteriorSeamBlend ||
            region == null ||
            !TerrainVisualSurfaceClassifier.SupportsInteriorSynthesis(region.SurfaceKind) ||
            currentTile.Width <= 1 ||
            currentTile.Height <= 1)
        {
            return 0;
        }

        var seam = Math.Clamp(profile.InteriorSeamPixels <= 0 ? 8 : profile.InteriorSeamPixels, 1, Math.Min(currentTile.Width, currentTile.Height) / 2);
        var jitter = Math.Clamp(profile.InteriorSeamJitterPixels, 0, seam);
        var current = FastBitmapBuffer.FromBitmap(currentTile);
        var changed = 0;
        if (leftTile != null && leftTile.Width == currentTile.Width && leftTile.Height == currentTile.Height)
        {
            using var left = FastBitmapBuffer.FromBitmap(leftTile);
            changed += BlendLeft(current, left, seam, jitter, profile.Seed, index, region.TerrainId, cancellationToken);
        }

        if (topTile != null && topTile.Width == currentTile.Width && topTile.Height == currentTile.Height)
        {
            using var top = FastBitmapBuffer.FromBitmap(topTile);
            changed += BlendTop(current, top, seam, jitter, profile.Seed, index, region.TerrainId, cancellationToken);
        }

        if (changed > 0)
        {
            current.CopyTo(currentTile);
        }

        return changed;
    }

    private static int BlendLeft(FastBitmapBuffer current, FastBitmapBuffer left, int seam, int jitter, string seed, int index, byte terrainId, CancellationToken cancellationToken)
    {
        var changed = 0;
        for (var y = 0; y < current.Height; y++)
        {
            if ((y & 7) == 0) cancellationToken.ThrowIfCancellationRequested();
            var localSeam = ComputeLocalSeam(seam, jitter, seed, index, terrainId, y, 0xA24BAED5u);
            for (var x = 0; x < localSeam; x++)
            {
                var amount = SmoothStep(1f - x / (float)Math.Max(1, localSeam));
                amount *= 0.45f;
                var sourceX = Math.Clamp(left.Width - localSeam + x, 0, left.Width - 1);
                var currentIndex = y * current.Width + x;
                var leftIndex = y * left.Width + sourceX;
                current.Pixels[currentIndex] = FastBitmapBuffer.LerpArgb(current.Pixels[currentIndex], left.Pixels[leftIndex], amount);
                changed++;
            }
        }

        return changed;
    }

    private static int BlendTop(FastBitmapBuffer current, FastBitmapBuffer top, int seam, int jitter, string seed, int index, byte terrainId, CancellationToken cancellationToken)
    {
        var changed = 0;
        for (var x = 0; x < current.Width; x++)
        {
            if ((x & 7) == 0) cancellationToken.ThrowIfCancellationRequested();
            var localSeam = ComputeLocalSeam(seam, jitter, seed, index, terrainId, x, 0xC2B2AE35u);
            for (var y = 0; y < localSeam; y++)
            {
                var amount = SmoothStep(1f - y / (float)Math.Max(1, localSeam));
                amount *= 0.45f;
                var sourceY = Math.Clamp(top.Height - localSeam + y, 0, top.Height - 1);
                var currentIndex = y * current.Width + x;
                var topIndex = sourceY * top.Width + x;
                current.Pixels[currentIndex] = FastBitmapBuffer.LerpArgb(current.Pixels[currentIndex], top.Pixels[topIndex], amount);
                changed++;
            }
        }

        return changed;
    }

    private static int ComputeLocalSeam(int seam, int jitter, string seed, int index, byte terrainId, int offset, uint salt)
    {
        if (jitter <= 0) return seam;
        var hash = StableHash(seed, index + offset * 31, terrainId, salt);
        var delta = (int)(hash % (uint)(jitter * 2 + 1)) - jitter;
        return Math.Max(1, seam + delta);
    }

    private static float SmoothStep(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return value * value * (3f - 2f * value);
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
