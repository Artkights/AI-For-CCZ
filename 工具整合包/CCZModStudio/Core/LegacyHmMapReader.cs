using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class LegacyHmMapReader
{
    private const int BytesPerPixel = 2;
    private static readonly (int GridWidth, int GridHeight)[] PreferredGridSizes =
    [
        (20, 10),
        (24, 12),
        (30, 15),
        (32, 16),
        (36, 18),
        (40, 20),
        (45, 22),
        (48, 24),
        (50, 25),
        (54, 27),
        (60, 30),
        (64, 32),
        (70, 35),
        (72, 36),
        (80, 40),
        (85, 42)
    ];

    public static bool HasLegacyHmLayout(CczProject project)
        => !Directory.Exists(project.ResolveGameFile("Map")) &&
           File.Exists(ResolveGameFileWithE5Fallback(project, "Hexzmap.e5")) &&
           File.Exists(ResolveGameFileWithE5Fallback(project, "Spalet.e5")) &&
           EnumerateHmFiles(project).Any();

    public IReadOnlyList<MapResourceItem> Index(CczProject project)
    {
        var files = EnumerateHmFiles(project)
            .OrderBy(path => ExtractHmNumber(path))
            .ThenBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (files.Count == 0) return Array.Empty<MapResourceItem>();

        var result = new List<MapResourceItem>();
        foreach (var path in files)
        {
            var info = new FileInfo(path);
            var index = ExtractHmNumber(path);
            var size = InferGridSize(info.Length);
            var width = size.GridWidth * MapResourceItem.MapTilePixelSize;
            var height = size.GridHeight * MapResourceItem.MapTilePixelSize;
            result.Add(new MapResourceItem
            {
                Category = "地图图片",
                Id = index.ToString("000", CultureInfo.InvariantCulture),
                MapId = "M" + index.ToString("000", CultureInfo.InvariantCulture),
                Name = info.Name,
                Extension = info.Extension,
                SizeBytes = info.Length,
                Magic = "RAW16",
                FormatHint = "LegacyHmRaw",
                SourceKind = "LegacyHmRaw",
                Annotation = $"旧式 Hm 战场底图：{info.Name}，按文件长度推断格数 {size.GridWidth}x{size.GridHeight}，当前以只读预览方式显示；完整导入/重封包需结合 Hexzmap.e5 与 Spalet.e5 继续确认。",
                Width = width,
                Height = height,
                GridWidthOverride = size.GridWidth,
                GridHeightOverride = size.GridHeight,
                Path = path
            });
        }

        return result;
    }

    public Bitmap RenderPreview(CczProject project, MapResourceItem item)
    {
        var gridWidth = item.GridWidth > 0 ? item.GridWidth : InferGridSize(new FileInfo(item.Path).Length).GridWidth;
        var gridHeight = item.GridHeight > 0 ? item.GridHeight : InferGridSize(new FileInfo(item.Path).Length).GridHeight;
        var width = Math.Max(MapResourceItem.MapTilePixelSize, gridWidth * MapResourceItem.MapTilePixelSize);
        var height = Math.Max(MapResourceItem.MapTilePixelSize, gridHeight * MapResourceItem.MapTilePixelSize);

        var bytes = File.Exists(item.Path) ? File.ReadAllBytes(item.Path) : Array.Empty<byte>();
        var palette = TryLoadPalette(project);
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        if (bytes.Length < BytesPerPixel)
        {
            using var emptyGraphics = Graphics.FromImage(bitmap);
            emptyGraphics.Clear(Color.FromArgb(35, 38, 42));
            return bitmap;
        }

        var maxPixels = Math.Min(width * height, bytes.Length / BytesPerPixel);
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var pixelIndex = row + x;
                if (pixelIndex >= maxPixels)
                {
                    bitmap.SetPixel(x, y, Color.FromArgb(36, 40, 44));
                    continue;
                }

                var offset = pixelIndex * BytesPerPixel;
                var value = bytes[offset] | (bytes[offset + 1] << 8);
                bitmap.SetPixel(x, y, ResolveColor(value, palette));
            }
        }

        return bitmap;
    }

    private static Color ResolveColor(int value, IReadOnlyList<Color> palette)
    {
        if (palette.Count > 0)
        {
            var index = value & 0xFF;
            if (index >= 0 && index < palette.Count) return palette[index];
        }

        var r = 48 + ((value >> 7) & 0x7C);
        var g = 50 + ((value >> 2) & 0x7C);
        var b = 48 + ((value << 3) & 0x78);
        return Color.FromArgb(255, Math.Min(255, r), Math.Min(255, g), Math.Min(255, b));
    }

    private static IReadOnlyList<Color> TryLoadPalette(CczProject project)
    {
        var path = ResolveGameFileWithE5Fallback(project, "Spalet.e5");
        if (!File.Exists(path)) return Array.Empty<Color>();

        try
        {
            var bytes = File.ReadAllBytes(path);
            var start = bytes.Length >= 16 && IsLsHeader(bytes) ? 16 : 0;
            var colors = new List<Color>();
            for (var offset = start; offset + 2 < bytes.Length && colors.Count < 256; offset += 3)
            {
                colors.Add(Color.FromArgb(255, bytes[offset], bytes[offset + 1], bytes[offset + 2]));
            }

            return colors;
        }
        catch
        {
            return Array.Empty<Color>();
        }
    }

    private static (int GridWidth, int GridHeight) InferGridSize(long length)
    {
        if (length <= 0) return (20, 10);
        var pixelCount = length / BytesPerPixel;
        foreach (var candidate in PreferredGridSizes)
        {
            var expectedPixels = (long)candidate.GridWidth * MapResourceItem.MapTilePixelSize *
                                 candidate.GridHeight * MapResourceItem.MapTilePixelSize;
            if (expectedPixels == pixelCount)
            {
                return candidate;
            }
        }

        var mapTilePixels = MapResourceItem.MapTilePixelSize * MapResourceItem.MapTilePixelSize;
        var cellCount = Math.Max(1, pixelCount / mapTilePixels);
        var bestWidth = 20;
        var bestHeight = Math.Max(1, (int)Math.Ceiling(cellCount / (double)bestWidth));
        var bestScore = double.MaxValue;
        for (var width = 10; width <= 85; width++)
        {
            if (cellCount % width != 0) continue;
            var height = (int)(cellCount / width);
            if (height < 1 || height > 85) continue;
            var ratio = width / (double)Math.Max(1, height);
            var score = Math.Abs(ratio - 2.0) + Math.Abs(width - 40) / 100.0;
            if (score >= bestScore) continue;
            bestScore = score;
            bestWidth = width;
            bestHeight = height;
        }

        return (bestWidth, bestHeight);
    }

    private static bool IsHmFile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.Length == 4 &&
               name.StartsWith("Hm", StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(name[2..], NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }

    private static IEnumerable<string> EnumerateHmFiles(CczProject project)
    {
        var seen = new HashSet<int>();
        foreach (var dir in new[] { project.GameRoot, Path.Combine(project.GameRoot, "E5") }.Where(Directory.Exists))
        {
            foreach (var path in Directory.GetFiles(dir, "Hm*.e5", SearchOption.TopDirectoryOnly).Where(IsHmFile))
            {
                if (!seen.Add(ExtractHmNumber(path))) continue;
                yield return path;
            }
        }
    }

    private static int ExtractHmNumber(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return int.TryParse(name.Length >= 4 ? name[2..] : string.Empty, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : int.MaxValue;
    }

    private static bool IsLsHeader(byte[] bytes)
        => bytes.Length >= 4 &&
           bytes[0] == (byte)'L' &&
           bytes[1] == (byte)'s' &&
           bytes[2] is (byte)'1' &&
           bytes[3] is (byte)'0' or (byte)'1' or (byte)'2';

    private static string ResolveGameFileWithE5Fallback(CczProject project, string fileName)
    {
        var root = project.ResolveGameFile(fileName);
        if (File.Exists(root)) return root;
        var e5 = Path.Combine(project.GameRoot, "E5", fileName);
        return File.Exists(e5) ? e5 : root;
    }
}
