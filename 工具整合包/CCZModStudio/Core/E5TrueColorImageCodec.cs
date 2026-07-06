using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class E5TrueColorImageCodec
{
    public E5TrueColorEncodeResult EncodeFile(CczProject project, string sourcePath, E5RawImageSpec spec, bool strictHeight = true)
    {
        _ = project;
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("找不到待导入图片。", sourcePath);
        }

        using var bitmap = LoadBitmap(sourcePath);
        return EncodeBitmapCore(bitmap, sourcePath, spec, strictHeight);
    }

    public E5TrueColorEncodeResult EncodeBitmap(CczProject project, Bitmap bitmap, string sourceLabel, E5RawImageSpec spec, bool strictHeight = true)
    {
        _ = project;
        return EncodeBitmapCore(bitmap, sourceLabel, spec, strictHeight);
    }

    private static E5TrueColorEncodeResult EncodeBitmapCore(Bitmap bitmap, string sourceLabel, E5RawImageSpec spec, bool strictHeight)
    {
        ValidateDimensions(bitmap, spec, strictHeight);

        using var normalized = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
        var transparent = 0;
        var magenta = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.A == 0)
                {
                    normalized.SetPixel(x, y, Color.Transparent);
                    transparent++;
                    continue;
                }

                if (IsMagentaKey(color))
                {
                    normalized.SetPixel(x, y, Color.Transparent);
                    transparent++;
                    magenta++;
                    continue;
                }

                normalized.SetPixel(x, y, Color.FromArgb(color.A, color.R, color.G, color.B));
            }
        }

        using var memory = new MemoryStream();
        normalized.Save(memory, ImageFormat.Png);
        return new E5TrueColorEncodeResult
        {
            SourcePath = sourceLabel,
            TargetFileName = spec.FileName,
            SourceWidth = bitmap.Width,
            SourceHeight = bitmap.Height,
            NormalizedWidth = normalized.Width,
            NormalizedHeight = normalized.Height,
            StorageFormat = "PNG",
            ColorDepth = 32,
            ImageBytes = memory.ToArray(),
            TransparentPixels = transparent,
            MagentaKeyPixels = magenta,
            Warnings = Array.Empty<string>()
        };
    }

    private static Bitmap LoadBitmap(string sourcePath)
    {
        using var stream = File.OpenRead(sourcePath);
        using var raw = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
        return CloneArgb(raw);
    }

    private static Bitmap CloneArgb(Image raw)
    {
        var bitmap = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.DrawImage(raw, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        return bitmap;
    }

    private static void ValidateDimensions(Bitmap bitmap, E5RawImageSpec spec, bool strictHeight)
    {
        if (bitmap.Width != spec.Width)
        {
            throw new InvalidOperationException($"{Path.GetFileName(spec.FileName)} 真彩导入需要宽度 {spec.Width}，当前图片为 {bitmap.Width}x{bitmap.Height}。");
        }

        if (strictHeight && spec.StrictStripHeight.HasValue && bitmap.Height != spec.StrictStripHeight.Value)
        {
            throw new InvalidOperationException($"{Path.GetFileName(spec.FileName)} 真彩导入需要尺寸 {spec.Width}x{spec.StrictStripHeight.Value}，当前图片为 {bitmap.Width}x{bitmap.Height}。");
        }

        if (!strictHeight && bitmap.Height % spec.FrameHeight != 0)
        {
            throw new InvalidOperationException($"{Path.GetFileName(spec.FileName)} 真彩导入高度必须是帧高 {spec.FrameHeight} 的整数倍，当前图片高度为 {bitmap.Height}。");
        }
    }

    private static bool IsMagentaKey(Color color)
        => color.R >= 247 && color.G <= 8 && color.B >= 248;
}
