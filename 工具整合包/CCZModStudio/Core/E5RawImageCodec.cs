using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class E5RawImageCodec
{
    public static readonly E5RawImageSpec PmapobjSpec = new("Pmapobj.e5", 48, 64, null);
    public static readonly E5RawImageSpec UnitMovSpec = new("Unit_mov.e5", 48, 48, 528);
    public static readonly E5RawImageSpec UnitAtkSpec = new("Unit_atk.e5", 64, 64, 768);
    public static readonly E5RawImageSpec UnitSpcSpec = new("Unit_spc.e5", 48, 48, 240);

    private static readonly E5RawImageSpec[] KnownSpecs =
    [
        PmapobjSpec,
        UnitMovSpec,
        UnitAtkSpec,
        UnitSpcSpec
    ];

    public E5RawImageSpec ResolveSpec(string fileName)
    {
        var name = Path.GetFileName(fileName);
        return KnownSpecs.FirstOrDefault(spec => spec.FileName.Equals(name, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"不支持转换为 RAW 的 E5 资源：{fileName}");
    }

    public E5RawEncodeResult EncodeFile(CczProject project, string sourcePath, E5RawImageSpec spec, bool strictHeight = true)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("找不到待转换图片。", sourcePath);
        }

        using var image = LoadBitmap(sourcePath);
        return EncodeBitmap(project, image, sourcePath, spec, strictHeight);
    }

    public E5RawEncodeResult EncodeBitmap(CczProject project, Bitmap bitmap, string sourceLabel, E5RawImageSpec spec, bool strictHeight = true)
        => EncodeBitmapCore(project, bitmap, sourceLabel, spec, strictHeight);

    public E5RawEncodeResult EncodeEntryBytes(CczProject project, byte[] sourceBytes, string sourceLabel, E5RawImageSpec spec, bool strictHeight)
    {
        using var image = LoadBitmap(sourceBytes, sourceLabel);
        return EncodeBitmapCore(project, image, sourceLabel, spec, strictHeight);
    }

    public bool TryDecodeStandardImage(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        try
        {
            using var bitmap = LoadBitmap(bytes, "<entry>");
            width = bitmap.Width;
            height = bitmap.Height;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private E5RawEncodeResult EncodeBitmapCore(CczProject project, Bitmap bitmap, string sourceLabel, E5RawImageSpec spec, bool strictHeight)
    {
        ValidateDimensions(bitmap, spec, strictHeight);
        var palette = LoadRawPalette(project);
        var exactLookup = BuildExactLookup(palette);
        var raw = new byte[checked(bitmap.Width * bitmap.Height)];
        var transparent = 0;
        var exact = 0;
        var nearest = 0;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                var rawIndex = y * bitmap.Width + x;
                if (color.A == 0 || IsMagentaKey(color))
                {
                    raw[rawIndex] = 0;
                    transparent++;
                    continue;
                }

                var key = ToRgbKey(color);
                if (exactLookup.TryGetValue(key, out var paletteIndex))
                {
                    raw[rawIndex] = paletteIndex;
                    exact++;
                    continue;
                }

                raw[rawIndex] = FindNearestPaletteIndex(color, palette);
                nearest++;
            }
        }

        var warnings = new List<string>();
        if (nearest > 0)
        {
            warnings.Add($"有 {nearest:N0} 个像素未精确命中 tsb 调色板，已使用最近色。");
        }

        return new E5RawEncodeResult
        {
            SourcePath = sourceLabel,
            TargetFileName = spec.FileName,
            SourceWidth = bitmap.Width,
            SourceHeight = bitmap.Height,
            RawBytes = raw,
            TransparentPixels = transparent,
            ExactPalettePixels = exact,
            NearestPalettePixels = nearest,
            PalettePath = ResolvePalettePath(project),
            Warnings = warnings
        };
    }

    private static Bitmap LoadBitmap(string sourcePath)
    {
        using var stream = File.OpenRead(sourcePath);
        using var raw = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
        return CloneArgb(raw);
    }

    private static Bitmap LoadBitmap(byte[] sourceBytes, string sourceLabel)
    {
        try
        {
            using var memory = new MemoryStream(sourceBytes, writable: false);
            using var raw = Image.FromStream(memory, useEmbeddedColorManagement: false, validateImageData: true);
            return CloneArgb(raw);
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException)
        {
            throw new InvalidOperationException($"图片条目无法按 BMP/JPG/PNG 解码：{sourceLabel}", ex);
        }
    }

    private static Bitmap CloneArgb(Image raw)
    {
        var bitmap = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.DrawImage(raw, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        return bitmap;
    }

    private static void ValidateDimensions(Bitmap bitmap, E5RawImageSpec spec, bool strictHeight)
    {
        if (bitmap.Width != spec.Width)
        {
            throw new InvalidOperationException($"{Path.GetFileName(spec.FileName)} RAW 需要宽度 {spec.Width}，当前图片为 {bitmap.Width}x{bitmap.Height}。");
        }

        if (strictHeight && spec.StrictStripHeight.HasValue && bitmap.Height != spec.StrictStripHeight.Value)
        {
            throw new InvalidOperationException($"{Path.GetFileName(spec.FileName)} RAW 需要尺寸 {spec.Width}x{spec.StrictStripHeight.Value}，当前图片为 {bitmap.Width}x{bitmap.Height}。");
        }

        if (!strictHeight && bitmap.Height % spec.FrameHeight != 0)
        {
            throw new InvalidOperationException($"{Path.GetFileName(spec.FileName)} RAW 高度必须是帧高 {spec.FrameHeight} 的整数倍，当前图片高度为 {bitmap.Height}。");
        }
    }

    private static IReadOnlyList<Color> LoadRawPalette(CczProject project)
    {
        var path = ResolvePalettePath(project);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("找不到 tsb 调色板，无法编码 RAW。", path);
        }

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 256 * 4)
        {
            throw new InvalidOperationException($"tsb 调色板长度不足：{path}");
        }

        var colors = new Color[256];
        for (var i = 0; i < colors.Length; i++)
        {
            var offset = i * 4;
            colors[i] = Color.FromArgb(255, bytes[offset + 2], bytes[offset + 1], bytes[offset]);
        }

        return colors;
    }

    private static string ResolvePalettePath(CczProject project)
    {
        var candidates = new[]
        {
            PortableInstallPaths.PaletteTsbPath,
            Path.Combine(project.GameRoot, "tsb")
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static Dictionary<int, byte> BuildExactLookup(IReadOnlyList<Color> palette)
    {
        var lookup = new Dictionary<int, byte>();
        for (var i = 0; i < Math.Min(256, palette.Count); i++)
        {
            if (i == 0) continue;
            lookup.TryAdd(ToRgbKey(palette[i]), (byte)i);
        }

        return lookup;
    }

    private static byte FindNearestPaletteIndex(Color color, IReadOnlyList<Color> palette)
    {
        long bestDistance = long.MaxValue;
        byte bestIndex = 0;
        for (var i = 1; i < Math.Min(256, palette.Count); i++)
        {
            var candidate = palette[i];
            var dr = color.R - candidate.R;
            var dg = color.G - candidate.G;
            var db = color.B - candidate.B;
            var distance = dr * dr + dg * dg + db * db;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            bestIndex = (byte)i;
            if (distance == 0) break;
        }

        return bestIndex;
    }

    private static int ToRgbKey(Color color)
        => (color.R << 16) | (color.G << 8) | color.B;

    private static bool IsMagentaKey(Color color)
        => color.R >= 248 && color.G <= 8 && color.B >= 248;
}
