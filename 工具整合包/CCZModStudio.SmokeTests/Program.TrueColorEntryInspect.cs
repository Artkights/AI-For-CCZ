using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing;

internal partial class Program
{
    static void RunTrueColorEntryInspect(CczProject project, string[] args)
    {
        var path = CharacterImageResourceService.ResolveGameFile(project, ReadRequiredArg(args, "--target-relative-path"));
        var imageNumber = ParseRequiredIntArg(args, "--image-number");
        var expectedWidth = ParseRequiredIntArg(args, "--expected-width");
        var expectedHeight = ParseRequiredIntArg(args, "--expected-height");
        var expectedColorRaw = ReadOptionalArg(args, "--sample-color");

        var service = new E5ImageReplaceService();
        VerifyPngEntry(service, path, imageNumber, expectedWidth, expectedHeight);

        var bytes = service.ReadEntryBytes(path, imageNumber);
        using var memory = new MemoryStream(bytes, writable: false);
        using var image = Image.FromStream(memory, useEmbeddedColorManagement: false, validateImageData: true);
        using var bitmap = new Bitmap(image);

        var entry = service.ReadIndex(path)[imageNumber - 1];
        Console.WriteLine($"ENTRY target={Path.GetFileName(path)} image={imageNumber} kind={entry.Kind} size={bytes.Length} dimensions={bitmap.Width}x{bitmap.Height}");

        if (expectedColorRaw == null) return;

        var expectedColor = ParseHexColor(expectedColorRaw);
        var match = FindNearestOpaquePixel(bitmap, expectedColor);
        Console.WriteLine($"SAMPLE expected=#{expectedColor.R:X2}{expectedColor.G:X2}{expectedColor.B:X2} nearest=({match.X},{match.Y}) color=#{match.Color.R:X2}{match.Color.G:X2}{match.Color.B:X2} distance={match.Distance:F2}");
    }

    private static (int X, int Y, Color Color, double Distance) FindNearestOpaquePixel(Bitmap bitmap, Color expected)
    {
        var bestX = 0;
        var bestY = 0;
        var bestColor = Color.Empty;
        var bestDistance = double.MaxValue;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.A == 0) continue;
                var dr = color.R - expected.R;
                var dg = color.G - expected.G;
                var db = color.B - expected.B;
                var distance = Math.Sqrt((dr * dr) + (dg * dg) + (db * db));
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                bestX = x;
                bestY = y;
                bestColor = color;
            }
        }

        return (bestX, bestY, bestColor, bestDistance);
    }

    private static Color ParseHexColor(string raw)
    {
        var value = raw.Trim().TrimStart('#');
        if (value.Length != 6)
        {
            throw new InvalidOperationException("--sample-color must be #RRGGBB.");
        }

        return Color.FromArgb(
            255,
            Convert.ToInt32(value[..2], 16),
            Convert.ToInt32(value.Substring(2, 2), 16),
            Convert.ToInt32(value.Substring(4, 2), 16));
    }
}
