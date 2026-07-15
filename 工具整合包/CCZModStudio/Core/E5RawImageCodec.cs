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

    private readonly E5RawPaletteService _paletteService = new();

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

    public E5RawEncodeResult EncodeBitmapPreservingIndices(
        Bitmap bitmap,
        string sourceLabel,
        E5RawImageSpec spec,
        IReadOnlyList<Color> palette,
        string palettePath,
        byte[] originalBytes,
        IReadOnlyList<int> originalArgbPixels,
        int trailingByteCount)
    {
        ValidateDimensions(bitmap, spec, strictHeight: false);
        if (palette.Count < 256)
            throw new InvalidOperationException("RAW pixel editing requires the original 256-color palette snapshot.");
        var pixelLength = checked(bitmap.Width * bitmap.Height);
        if (trailingByteCount is not (0 or 2) || originalBytes.Length != pixelLength + trailingByteCount)
            throw new InvalidOperationException("RAW source snapshot length does not match the editable strip and container suffix.");
        if (originalArgbPixels.Count != pixelLength)
            throw new InvalidOperationException("RAW source snapshot pixel count does not match the editable strip.");

        var output = originalBytes.ToArray();
        var exactLookup = BuildExactLookup(palette);
        var transparent = 0;
        var exact = 0;
        var nearest = 0;
        var changed = 0;
        var currentArgbPixels = BitmapArgbSnapshot.Capture(bitmap);
        for (var index = 0; index < currentArgbPixels.Length; index++)
        {
            var currentArgb = currentArgbPixels[index];
            if (PixelsEquivalent(currentArgb, originalArgbPixels[index])) continue;

            changed++;
            var color = Color.FromArgb(currentArgb);
            if (color.A == 0)
            {
                output[index] = 0;
                transparent++;
                continue;
            }

            var key = ToRgbKey(color);
            if (exactLookup.TryGetValue(key, out var paletteIndex))
            {
                output[index] = paletteIndex;
                exact++;
                continue;
            }

            output[index] = FindNearestPaletteIndex(color, palette);
            nearest++;
        }

        var warnings = new List<string>();
        if (nearest > 0)
            warnings.Add($"{nearest:N0} changed pixels did not exactly match the pinned RAW palette and used nearest colors.");
        if (trailingByteCount == 2)
            warnings.Add("The original two-byte RAW container suffix is preserved byte-for-byte.");
        warnings.Add($"RAW incremental encoding patched {changed:N0} pixels; all other palette indices were preserved.");

        return new E5RawEncodeResult
        {
            SourcePath = sourceLabel,
            TargetFileName = spec.FileName,
            SourceWidth = bitmap.Width,
            SourceHeight = bitmap.Height,
            RawBytes = output,
            TransparentPixels = transparent,
            ExactPalettePixels = exact,
            NearestPalettePixels = nearest,
            PalettePath = palettePath,
            Warnings = warnings
        };
    }

    public Bitmap DecodeRawBytes(
        CczProject project,
        byte[] rawBytes,
        string sourceLabel,
        E5RawImageSpec spec,
        bool trimToWholeRows = true)
    {
        if (rawBytes.Length == 0)
        {
            throw new InvalidOperationException($"RAW image is empty: {sourceLabel}");
        }

        if (spec.Width <= 0)
        {
            throw new InvalidOperationException($"Invalid RAW width for {spec.FileName}: {spec.Width}");
        }

        var usableLength = rawBytes.Length;
        var remainder = usableLength % spec.Width;
        if (remainder != 0)
        {
            if (!trimToWholeRows)
            {
                throw new InvalidOperationException(
                    $"RAW length for {sourceLabel} is not a multiple of width {spec.Width}: {rawBytes.Length}");
            }

            usableLength -= remainder;
        }

        var height = usableLength / spec.Width;
        if (spec.StrictStripHeight.HasValue)
        {
            height = spec.StrictStripHeight.Value;
            usableLength = Math.Min(usableLength, checked(spec.Width * height));
        }

        if (height <= 0)
        {
            throw new InvalidOperationException($"RAW image is too short for {spec.FileName}: {sourceLabel}");
        }

        var palette = LoadRequiredRawPalette(project);
        var pixels = new int[checked(spec.Width * height)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < spec.Width; x++)
            {
                var rawIndex = y * spec.Width + x;
                if (rawIndex >= usableLength)
                {
                    pixels[rawIndex] = Color.Transparent.ToArgb();
                    continue;
                }

                var value = rawBytes[rawIndex];
                if (value == 0)
                {
                    pixels[rawIndex] = Color.Transparent.ToArgb();
                    continue;
                }

                var color = value < palette.Count ? palette[value] : Color.Transparent;
                pixels[rawIndex] = (IsMagentaKey(color) ? Color.Transparent : color).ToArgb();
            }
        }

        return BitmapArgbSnapshot.Create(spec.Width, height, pixels);
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
        var paletteInfo = _paletteService.Load(project);
        var palette = EnsureRequiredPalette(paletteInfo);
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
            PalettePath = paletteInfo.Path,
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

    private static bool PixelsEquivalent(int currentArgb, int originalArgb)
        => currentArgb == originalArgb ||
           (((uint)currentArgb >> 24) == 0 && ((uint)originalArgb >> 24) == 0);

    private static bool IsMagentaKey(Color color)
        => color.R >= 248 && color.G <= 8 && color.B >= 248;

    private IReadOnlyList<Color> LoadRequiredRawPalette(CczProject project)
        => EnsureRequiredPalette(_paletteService.Load(project));

    private static IReadOnlyList<Color> EnsureRequiredPalette(E5RawPalette palette)
    {
        if (palette.Colors.Count >= 256) return palette.Colors;
        throw new FileNotFoundException("找不到可用 RAW 调色板：项目 E5\\Spalet.e5、项目 tsb、工具内置 tsb 均不可用。");
    }
}
