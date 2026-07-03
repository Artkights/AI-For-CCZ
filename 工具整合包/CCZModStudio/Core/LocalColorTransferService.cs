using System.Drawing;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

internal sealed class LocalColorTransferService
{
    private const float MaxLumaShift = 18f;
    private const float MaxChromaShift = 14f;
    private const float MinScale = 0.82f;
    private const float MaxScale = 1.18f;

    public int Apply(
        Bitmap output,
        Bitmap? baseImage,
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        IReadOnlySet<int> indexes)
    {
        if (baseImage == null || indexes.Count == 0 || profile.LocalColorTransferStrength <= 0f)
        {
            return 0;
        }

        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var contextIndexes = BuildContextIndexes(draft, indexes, Math.Clamp(profile.StyleContextRadiusCells, 1, 8));
        var objectFootprints = BuildObjectFootprints(draft, tileSize, profile);
        var targetSampleIndexes = contextIndexes
            .Where(index => !indexes.Contains(index))
            .Where(index => !profile.IgnoreBasePixelsUnderObjects || !objectFootprints.Contains(index))
            .ToList();
        if (targetSampleIndexes.Count == 0)
        {
            targetSampleIndexes = contextIndexes.ToList();
        }

        using var outputBuffer = FastBitmapBuffer.FromBitmap(output);
        using var baseBuffer = FastBitmapBuffer.FromBitmap(baseImage);
        var sourceStats = CalculateStats(outputBuffer, indexes, draft, tileSize);
        var targetStats = CalculateStats(baseBuffer, targetSampleIndexes, draft, tileSize);
        if (!sourceStats.HasValue || !targetStats.HasValue)
        {
            return 0;
        }

        var strength = Math.Clamp(profile.LocalColorTransferStrength, 0f, 1f);
        var changed = 0;
        foreach (var index in indexes)
        {
            var rect = GetTileRectangle(draft, index, tileSize);
            for (var y = rect.Top; y < rect.Bottom && y < output.Height; y++)
            {
                for (var x = rect.Left; x < rect.Right && x < output.Width; x++)
                {
                    var pixel = y * outputBuffer.Width + x;
                    var color = outputBuffer.Pixels[pixel];
                    var alpha = (color >>> 24) & 0xFF;
                    if (alpha == 0) continue;
                    var lab = RgbToLab(color);
                    var adjusted = Transfer(lab, sourceStats.Value, targetStats.Value, strength);
                    outputBuffer.Pixels[pixel] = LabToArgb(adjusted, alpha);
                    changed++;
                }
            }
        }

        if (changed > 0)
        {
            outputBuffer.CopyTo(output);
        }

        return changed;
    }

    private static HashSet<int> BuildObjectFootprints(MapWorkbenchDraft draft, int tileSize, TerrainVisualProfile profile)
    {
        var result = ObjectGroundInpaintPlanner.BuildObjectFootprintIndexes(
            draft,
            tileSize,
            includeTerrainObjects: false);
        if (!profile.GroundInpaintIncludesTerrainObjects)
        {
            return result;
        }

        AddTerrainObjectFootprints(result, draft.TerrainCells, draft.CellCount);
        AddTerrainObjectFootprints(result, draft.OriginalTerrainCells, draft.CellCount);
        return result;
    }

    private static void AddTerrainObjectFootprints(HashSet<int> result, byte[] terrainCells, int cellCount)
    {
        if (terrainCells.Length != cellCount) return;
        for (var i = 0; i < terrainCells.Length; i++)
        {
            if (ObjectGroundInpaintPlanner.IsObjectFootprintTerrain(terrainCells[i]))
            {
                result.Add(i);
            }
        }
    }

    private static HashSet<int> BuildContextIndexes(MapWorkbenchDraft draft, IReadOnlySet<int> indexes, int radius)
    {
        var result = new HashSet<int>();
        foreach (var index in indexes)
        {
            var x = index % draft.GridWidth;
            var y = index / draft.GridWidth;
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= draft.GridWidth || ny >= draft.GridHeight) continue;
                    result.Add(ny * draft.GridWidth + nx);
                }
            }
        }

        return result;
    }

    private static LabStats? CalculateStats(FastBitmapBuffer bitmap, IReadOnlyCollection<int> indexes, MapWorkbenchDraft draft, int tileSize)
    {
        if (indexes.Count == 0) return null;
        var values = new List<LabColor>();
        foreach (var index in indexes)
        {
            if ((uint)index >= (uint)draft.CellCount) continue;
            var rect = GetTileRectangle(draft, index, tileSize);
            var step = Math.Max(1, tileSize / 12);
            for (var y = rect.Top; y < rect.Bottom && y < bitmap.Height; y += step)
            {
                for (var x = rect.Left; x < rect.Right && x < bitmap.Width; x += step)
                {
                    var color = bitmap.Pixels[y * bitmap.Width + x];
                    if (((color >>> 24) & 0xFF) == 0) continue;
                    values.Add(RgbToLab(color));
                }
            }
        }

        if (values.Count == 0) return null;
        var meanL = values.Average(value => value.L);
        var meanA = values.Average(value => value.A);
        var meanB = values.Average(value => value.B);
        static double Std(IReadOnlyList<LabColor> values, Func<LabColor, double> selector, double mean)
            => Math.Sqrt(values.Sum(value =>
            {
                var delta = selector(value) - mean;
                return delta * delta;
            }) / Math.Max(1, values.Count));

        return new LabStats(
            (float)meanL,
            (float)meanA,
            (float)meanB,
            Math.Max(0.001f, (float)Std(values, value => value.L, meanL)),
            Math.Max(0.001f, (float)Std(values, value => value.A, meanA)),
            Math.Max(0.001f, (float)Std(values, value => value.B, meanB)));
    }

    private static LabColor Transfer(LabColor value, LabStats source, LabStats target, float strength)
    {
        var scaleL = Math.Clamp(target.StdL / source.StdL, MinScale, MaxScale);
        var scaleA = Math.Clamp(target.StdA / source.StdA, MinScale, MaxScale);
        var scaleB = Math.Clamp(target.StdB / source.StdB, MinScale, MaxScale);
        var transferredL = target.MeanL + (value.L - source.MeanL) * scaleL;
        var transferredA = target.MeanA + (value.A - source.MeanA) * scaleA;
        var transferredB = target.MeanB + (value.B - source.MeanB) * scaleB;
        transferredL = ClampShift(value.L, transferredL, MaxLumaShift);
        transferredA = ClampShift(value.A, transferredA, MaxChromaShift);
        transferredB = ClampShift(value.B, transferredB, MaxChromaShift);
        return new LabColor(
            Lerp(value.L, transferredL, strength),
            Lerp(value.A, transferredA, strength),
            Lerp(value.B, transferredB, strength));
    }

    private static float ClampShift(float original, float adjusted, float maxShift)
        => Math.Clamp(adjusted, original - maxShift, original + maxShift);

    private static float Lerp(float left, float right, float amount)
        => left + (right - left) * amount;

    private static Rectangle GetTileRectangle(MapWorkbenchDraft draft, int index, int tileSize)
    {
        var x = index % draft.GridWidth;
        var y = index / draft.GridWidth;
        return new Rectangle(x * tileSize, y * tileSize, tileSize, tileSize);
    }

    private static LabColor RgbToLab(int color)
    {
        static double PivotRgb(double value)
        {
            value /= 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        var r = PivotRgb((color >>> 16) & 0xFF);
        var g = PivotRgb((color >>> 8) & 0xFF);
        var b = PivotRgb(color & 0xFF);
        var x = (r * 0.4124 + g * 0.3576 + b * 0.1805) / 0.95047;
        var y = (r * 0.2126 + g * 0.7152 + b * 0.0722) / 1.00000;
        var z = (r * 0.0193 + g * 0.1192 + b * 0.9505) / 1.08883;

        static double PivotXyz(double value)
            => value > 0.008856 ? Math.Pow(value, 1.0 / 3.0) : 7.787 * value + 16.0 / 116.0;

        var fx = PivotXyz(x);
        var fy = PivotXyz(y);
        var fz = PivotXyz(z);
        return new LabColor(
            (float)(116 * fy - 16),
            (float)(500 * (fx - fy)),
            (float)(200 * (fy - fz)));
    }

    private static int LabToArgb(LabColor lab, int alpha)
    {
        var fy = (lab.L + 16) / 116.0;
        var fx = lab.A / 500.0 + fy;
        var fz = fy - lab.B / 200.0;

        static double PivotLab(double value)
        {
            var cube = value * value * value;
            return cube > 0.008856 ? cube : (value - 16.0 / 116.0) / 7.787;
        }

        var x = 0.95047 * PivotLab(fx);
        var y = 1.00000 * PivotLab(fy);
        var z = 1.08883 * PivotLab(fz);

        var r = x * 3.2406 + y * -1.5372 + z * -0.4986;
        var g = x * -0.9689 + y * 1.8758 + z * 0.0415;
        var b = x * 0.0557 + y * -0.2040 + z * 1.0570;

        static int PivotRgb(double value)
        {
            value = value <= 0.0031308 ? 12.92 * value : 1.055 * Math.Pow(value, 1.0 / 2.4) - 0.055;
            return Math.Clamp((int)Math.Round(value * 255), 0, 255);
        }

        return FastBitmapBuffer.Pack(alpha, PivotRgb(r), PivotRgb(g), PivotRgb(b));
    }

    private readonly record struct LabColor(float L, float A, float B);
    private readonly record struct LabStats(float MeanL, float MeanA, float MeanB, float StdL, float StdA, float StdB);
}
