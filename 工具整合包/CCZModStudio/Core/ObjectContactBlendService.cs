using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

internal sealed class ObjectContactBlendService
{
    private readonly ObjectAlphaRepairService _alphaRepairService = new();

    public int Apply(
        Bitmap target,
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        IReadOnlyList<ObjectContactBlendItem> objects)
    {
        if (!profile.UseObjectContactBlend ||
            objects.Count == 0 ||
            profile.ObjectContactShadowStrength <= 0f)
        {
            return 0;
        }

        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        using var targetBuffer = FastBitmapBuffer.FromBitmap(target);
        var changed = 0;
        foreach (var item in objects)
        {
            if ((uint)item.CellIndex >= (uint)draft.CellCount) continue;
            using var objectTile = BuildObjectTile(item.Asset, item.Source, tileSize);
            using var repaired = item.Asset.AssetType.Equals(MaterialAssetTypes.Building, StringComparison.OrdinalIgnoreCase)
                ? _alphaRepairService.Repair(objectTile, profile)
                : new ObjectAlphaRepairResult(new Bitmap(objectTile), false, 0, 0);
            using var objectBuffer = FastBitmapBuffer.FromBitmap(repaired.Bitmap);
            if (!objectBuffer.HasAlphaBelow(250))
            {
                continue;
            }

            changed += ApplyObjectContact(targetBuffer, objectBuffer, draft, profile, item.CellIndex, tileSize);
        }

        if (changed > 0)
        {
            targetBuffer.CopyTo(target);
        }

        return changed;
    }

    private static int ApplyObjectContact(
        FastBitmapBuffer target,
        FastBitmapBuffer objectTile,
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        int cellIndex,
        int tileSize)
    {
        var radius = Math.Clamp(profile.ObjectContactBlendPixels <= 0 ? 5 : profile.ObjectContactBlendPixels, 1, 16);
        var strength = Math.Clamp(profile.ObjectContactShadowStrength, 0f, 1f);
        var cellX = cellIndex % draft.GridWidth;
        var cellY = cellIndex / draft.GridWidth;
        var originX = cellX * tileSize;
        var originY = cellY * tileSize;
        var changed = 0;

        for (var y = Math.Max(0, tileSize / 4); y < tileSize; y++)
        {
            for (var x = 0; x < tileSize; x++)
            {
                var alpha = (objectTile.Pixels[y * objectTile.Width + x] >>> 24) & 0xFF;
                if (alpha >= 224)
                {
                    continue;
                }

                var distance = DistanceToOpaqueObject(objectTile, x, y, radius);
                if (distance < 0)
                {
                    continue;
                }

                var worldX = originX + x;
                var worldY = originY + y;
                if ((uint)worldX >= (uint)target.Width || (uint)worldY >= (uint)target.Height)
                {
                    continue;
                }

                var falloff = 1f - distance / (float)(radius + 1);
                var lowerBias = 0.45f + y / (float)Math.Max(1, tileSize) * 0.55f;
                var amount = strength * falloff * lowerBias;
                if (amount <= 0.015f)
                {
                    continue;
                }

                var offset = worldY * target.Width + worldX;
                target.Pixels[offset] = ApplyGroundContactShade(target.Pixels[offset], amount);
                changed++;
            }
        }

        return changed;
    }

    private static int DistanceToOpaqueObject(FastBitmapBuffer objectTile, int x, int y, int radius)
    {
        var best = int.MaxValue;
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var nx = x + dx;
                var ny = y + dy;
                if ((uint)nx >= (uint)objectTile.Width || (uint)ny >= (uint)objectTile.Height) continue;
                var alpha = (objectTile.Pixels[ny * objectTile.Width + nx] >>> 24) & 0xFF;
                if (alpha < 224) continue;
                var distanceSquared = dx * dx + dy * dy;
                if (distanceSquared < best)
                {
                    best = distanceSquared;
                }
            }
        }

        return best == int.MaxValue ? -1 : (int)MathF.Round(MathF.Sqrt(best));
    }

    private static int ApplyGroundContactShade(int color, float amount)
    {
        var alpha = (color >>> 24) & 0xFF;
        if (alpha == 0) return color;
        var red = (color >>> 16) & 0xFF;
        var green = (color >>> 8) & 0xFF;
        var blue = color & 0xFF;
        var shadow = FastBitmapBuffer.Pack(alpha, (int)(red * 0.68f), (int)(green * 0.70f), (int)(blue * 0.64f));
        return FastBitmapBuffer.LerpArgb(color, shadow, amount);
    }

    private static Bitmap BuildObjectTile(MaterialAsset asset, Bitmap source, int tileSize)
    {
        var tile = new Bitmap(tileSize, tileSize, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(tile);
        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(source, new Rectangle(0, 0, tileSize, tileSize), BuildSourceRect(asset, source), GraphicsUnit.Pixel);
        return tile;
    }

    private static Rectangle BuildSourceRect(MaterialAsset asset, Image image)
    {
        var x = Math.Clamp(asset.SourceX, 0, Math.Max(0, image.Width - 1));
        var y = Math.Clamp(asset.SourceY, 0, Math.Max(0, image.Height - 1));
        var width = asset.SourceWidth <= 0 ? Math.Min(MapResourceItem.MapTilePixelSize, image.Width - x) : asset.SourceWidth;
        var height = asset.SourceHeight <= 0 ? Math.Min(MapResourceItem.MapTilePixelSize, image.Height - y) : asset.SourceHeight;
        width = Math.Clamp(width, 1, Math.Max(1, image.Width - x));
        height = Math.Clamp(height, 1, Math.Max(1, image.Height - y));
        return new Rectangle(x, y, width, height);
    }
}

internal readonly record struct ObjectContactBlendItem(int CellIndex, MaterialAsset Asset, Bitmap Source);
