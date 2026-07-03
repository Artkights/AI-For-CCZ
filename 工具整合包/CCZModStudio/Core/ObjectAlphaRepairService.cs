using System.Drawing;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

internal sealed class ObjectAlphaRepairService
{
    public ObjectAlphaRepairResult Repair(Bitmap source, TerrainVisualProfile profile)
    {
        using var input = FastBitmapBuffer.FromBitmap(source);
        var output = new FastBitmapBuffer(input.Width, input.Height, (int[])input.Pixels.Clone());
        var threshold = Math.Clamp(profile.AlphaRepairBlackThreshold <= 0 ? 24 : profile.AlphaRepairBlackThreshold, 1, 96);
        var edgeConnected = profile.AlphaRepairEdgeConnectivity;
        var nearBlack = new bool[input.Pixels.Length];
        var nearBlackCount = 0;
        for (var i = 0; i < input.Pixels.Length; i++)
        {
            if (IsNearBlack(input.Pixels[i], threshold))
            {
                nearBlack[i] = true;
                nearBlackCount++;
            }
        }

        if (nearBlackCount == 0)
        {
            return new ObjectAlphaRepairResult(new Bitmap(source), false, 0, 0);
        }

        var repairMask = edgeConnected
            ? BuildEdgeConnectedMask(input.Width, input.Height, nearBlack)
            : nearBlack;
        var repairCount = repairMask.Count(value => value);

        // Avoid turning intentionally dark full-tile structures into transparent blanks.
        if (repairCount == 0 || repairCount > input.Pixels.Length * 0.88)
        {
            return new ObjectAlphaRepairResult(new Bitmap(source), false, 0, nearBlackCount);
        }

        var rejectedNearBlack = nearBlackCount - repairCount;
        for (var i = 0; i < output.Pixels.Length; i++)
        {
            if (!repairMask[i]) continue;
            var color = output.Pixels[i];
            output.Pixels[i] = FastBitmapBuffer.Pack(0, (color >>> 16) & 0xFF, (color >>> 8) & 0xFF, color & 0xFF);
        }

        return new ObjectAlphaRepairResult(output.ToBitmap(), true, repairCount, rejectedNearBlack);
    }

    private static bool[] BuildEdgeConnectedMask(int width, int height, IReadOnlyList<bool> nearBlack)
    {
        var result = new bool[nearBlack.Count];
        var queue = new Queue<int>();

        void EnqueueIfNearBlack(int x, int y)
        {
            if ((uint)x >= (uint)width || (uint)y >= (uint)height) return;
            var index = y * width + x;
            if (!nearBlack[index] || result[index]) return;
            result[index] = true;
            queue.Enqueue(index);
        }

        for (var x = 0; x < width; x++)
        {
            EnqueueIfNearBlack(x, 0);
            EnqueueIfNearBlack(x, height - 1);
        }

        for (var y = 1; y + 1 < height; y++)
        {
            EnqueueIfNearBlack(0, y);
            EnqueueIfNearBlack(width - 1, y);
        }

        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            var x = index % width;
            var y = index / width;
            EnqueueIfNearBlack(x - 1, y);
            EnqueueIfNearBlack(x + 1, y);
            EnqueueIfNearBlack(x, y - 1);
            EnqueueIfNearBlack(x, y + 1);
        }

        return result;
    }

    private static bool IsNearBlack(int color, int threshold)
    {
        var alpha = (color >>> 24) & 0xFF;
        if (alpha < 12) return false;
        var red = (color >>> 16) & 0xFF;
        var green = (color >>> 8) & 0xFF;
        var blue = color & 0xFF;
        var luma = red * 0.2126 + green * 0.7152 + blue * 0.0722;
        return red <= threshold &&
               green <= threshold &&
               blue <= threshold &&
               luma <= threshold;
    }
}

internal sealed record ObjectAlphaRepairResult(
    Bitmap Bitmap,
    bool Repaired,
    int RepairedPixelCount,
    int RejectedPixelCount) : IDisposable
{
    public void Dispose()
    {
        Bitmap.Dispose();
    }
}
