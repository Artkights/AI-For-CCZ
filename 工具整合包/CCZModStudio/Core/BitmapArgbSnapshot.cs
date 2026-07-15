using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CCZModStudio.Core;

internal static class BitmapArgbSnapshot
{
    public static int[] Capture(Bitmap bitmap)
    {
        Bitmap? converted = null;
        var source = bitmap;
        if (bitmap.PixelFormat != PixelFormat.Format32bppArgb)
        {
            converted = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(converted);
            graphics.DrawImageUnscaled(bitmap, 0, 0);
            source = converted;
        }

        try
        {
            var result = new int[checked(source.Width * source.Height)];
            var rectangle = new Rectangle(0, 0, source.Width, source.Height);
            var data = source.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (var y = 0; y < source.Height; y++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, checked(y * data.Stride)), result, y * source.Width, source.Width);
            }
            finally
            {
                source.UnlockBits(data);
            }
            return result;
        }
        finally
        {
            converted?.Dispose();
        }
    }

    public static Bitmap Create(int width, int height, IReadOnlyList<int> argbPixels)
    {
        if (argbPixels.Count != checked(width * height))
            throw new InvalidOperationException("ARGB pixel count does not match the bitmap dimensions.");
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var rectangle = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            if (argbPixels is int[] pixels)
            {
                for (var y = 0; y < height; y++)
                    Marshal.Copy(pixels, y * width, IntPtr.Add(data.Scan0, checked(y * data.Stride)), width);
            }
            else
            {
                var row = new int[width];
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++) row[x] = argbPixels[y * width + x];
                    Marshal.Copy(row, 0, IntPtr.Add(data.Scan0, checked(y * data.Stride)), width);
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }
}
