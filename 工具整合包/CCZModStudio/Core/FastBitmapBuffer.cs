using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CCZModStudio.Core;

internal sealed class FastBitmapBuffer : IDisposable
{
    public FastBitmapBuffer(int width, int height)
        : this(width, height, new int[checked(width * height)])
    {
    }

    public FastBitmapBuffer(int width, int height, int[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public int[] Pixels { get; }

    public static FastBitmapBuffer FromBitmap(Bitmap bitmap)
    {
        var clone = bitmap.PixelFormat == PixelFormat.Format32bppArgb
            ? bitmap
            : null;
        using var normalized = clone == null ? new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb) : null;
        if (normalized != null)
        {
            using var g = Graphics.FromImage(normalized);
            g.DrawImage(bitmap, new Rectangle(0, 0, normalized.Width, normalized.Height));
        }

        var source = clone ?? normalized!;
        var pixels = new int[source.Width * source.Height];
        var rect = new Rectangle(0, 0, source.Width, source.Height);
        var data = source.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            CopyFromData(data, source.Width, source.Height, pixels);
        }
        finally
        {
            source.UnlockBits(data);
        }

        return new FastBitmapBuffer(source.Width, source.Height, pixels);
    }

    public Bitmap ToBitmap()
    {
        var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        CopyTo(bitmap);
        return bitmap;
    }

    public void CopyTo(Bitmap bitmap)
    {
        if (bitmap.Width != Width || bitmap.Height != Height)
        {
            throw new InvalidOperationException("Bitmap buffer dimensions do not match the target bitmap.");
        }

        var rect = new Rectangle(0, 0, Width, Height);
        var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            CopyToData(data, Width, Height, Pixels);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    public bool HasAlphaBelow(int threshold)
    {
        foreach (var pixel in Pixels)
        {
            if (((pixel >>> 24) & 0xFF) < threshold)
            {
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
    }

    public static int LerpArgb(int left, int right, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        var inv = 1f - amount;
        var a = (int)MathF.Round(((left >>> 24) & 0xFF) * inv + ((right >>> 24) & 0xFF) * amount);
        var r = (int)MathF.Round(((left >>> 16) & 0xFF) * inv + ((right >>> 16) & 0xFF) * amount);
        var g = (int)MathF.Round(((left >>> 8) & 0xFF) * inv + ((right >>> 8) & 0xFF) * amount);
        var b = (int)MathF.Round((left & 0xFF) * inv + (right & 0xFF) * amount);
        return Pack(a, r, g, b);
    }

    public static int Pack(int alpha, int red, int green, int blue)
        => unchecked((int)(((uint)Math.Clamp(alpha, 0, 255) << 24) |
                           ((uint)Math.Clamp(red, 0, 255) << 16) |
                           ((uint)Math.Clamp(green, 0, 255) << 8) |
                           (uint)Math.Clamp(blue, 0, 255)));

    private static void CopyFromData(BitmapData data, int width, int height, int[] pixels)
    {
        var rowLength = width;
        if (data.Stride == width * 4)
        {
            Marshal.Copy(data.Scan0, pixels, 0, rowLength * height);
            return;
        }

        for (var y = 0; y < height; y++)
        {
            var source = IntPtr.Add(data.Scan0, y * data.Stride);
            Marshal.Copy(source, pixels, y * rowLength, rowLength);
        }
    }

    private static void CopyToData(BitmapData data, int width, int height, int[] pixels)
    {
        var rowLength = width;
        if (data.Stride == width * 4)
        {
            Marshal.Copy(pixels, 0, data.Scan0, rowLength * height);
            return;
        }

        for (var y = 0; y < height; y++)
        {
            var target = IntPtr.Add(data.Scan0, y * data.Stride);
            Marshal.Copy(pixels, y * rowLength, target, rowLength);
        }
    }
}
