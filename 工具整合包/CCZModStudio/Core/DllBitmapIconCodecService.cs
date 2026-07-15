using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;

namespace CCZModStudio.Core;

public sealed class DllBitmapIconCodecService
{
    private const int RtBitmap = 2;
    public const int SmallIconSize = 16;
    public const int LargeIconSize = 32;
    public const ushort PreferredLanguageId = 1041;
    public static readonly Color DllTransparentKey = Color.FromArgb(255, 0, 0, 255);
    public static readonly Color E5MagentaKey = Color.FromArgb(255, 247, 0, 255);
    public static readonly Color StandardMagentaKey = Color.FromArgb(255, 255, 0, 255);

    public IReadOnlyList<DllBitmapResourceRecord> ParseBitmapResources(string sourcePath)
    {
        try
        {
            var data = File.ReadAllBytes(sourcePath);
            if (data.Length < 0x40 || data[0] != 'M' || data[1] != 'Z') return Array.Empty<DllBitmapResourceRecord>();
            var peOffset = BitConverter.ToInt32(data, 0x3C);
            if (peOffset <= 0 || peOffset + 248 >= data.Length) return Array.Empty<DllBitmapResourceRecord>();
            var sectionCount = BitConverter.ToUInt16(data, peOffset + 6);
            var optionalHeaderSize = BitConverter.ToUInt16(data, peOffset + 20);
            var optionalHeaderOffset = peOffset + 24;
            var magic = BitConverter.ToUInt16(data, optionalHeaderOffset);
            var dataDirectoryOffset = magic == 0x20B ? optionalHeaderOffset + 112 : optionalHeaderOffset + 96;
            if (dataDirectoryOffset + 2 * 8 + 8 > data.Length) return Array.Empty<DllBitmapResourceRecord>();
            var resourceRva = BitConverter.ToInt32(data, dataDirectoryOffset + 2 * 8);
            if (resourceRva <= 0) return Array.Empty<DllBitmapResourceRecord>();

            var sections = new List<DllPeSection>();
            var sectionOffset = optionalHeaderOffset + optionalHeaderSize;
            for (var i = 0; i < sectionCount; i++)
            {
                var offset = sectionOffset + i * 40;
                if (offset + 40 > data.Length) break;
                sections.Add(new DllPeSection(
                    BitConverter.ToInt32(data, offset + 12),
                    Math.Max(BitConverter.ToInt32(data, offset + 8), BitConverter.ToInt32(data, offset + 16)),
                    BitConverter.ToInt32(data, offset + 20)));
            }

            var resourceBaseOffset = RvaToFileOffset(resourceRva, sections);
            if (resourceBaseOffset < 0 || resourceBaseOffset + 16 > data.Length) return Array.Empty<DllBitmapResourceRecord>();

            var result = new List<DllBitmapResourceRecord>();
            ReadResourceDirectory(data, sections, resourceBaseOffset, resourceBaseOffset, 0, [], result);
            return result
                .OrderBy(resource => resource.Id)
                .ThenBy(resource => resource.LanguageId)
                .ThenByDescending(resource => resource.BitCount)
                .ThenByDescending(resource => resource.SizeBytes)
                .ToArray();
        }
        catch
        {
            return Array.Empty<DllBitmapResourceRecord>();
        }
    }

    public int EstimateBitmapIconCount(IReadOnlyList<DllBitmapResourceRecord> resources)
    {
        if (resources.Count == 0) return 0;
        var minId = resources.Min(resource => resource.Id);
        var maxId = resources.Max(resource => resource.Id);
        return minId >= 100 && resources.Count >= 2
            ? ((maxId - minId) / 2) + 1
            : resources.Select(resource => resource.Id).Distinct().Count();
    }

    public DllBitmapResourcePair ResolveBitmapResourcePair(IReadOnlyList<DllBitmapResourceRecord> resources, int iconIndex)
    {
        if (resources.Count == 0) throw new InvalidOperationException("Target DLL has no RT_BITMAP resources.");
        if (iconIndex < 0) throw new InvalidOperationException("Icon index cannot be negative.");

        var minId = resources.Min(resource => resource.Id);
        if (minId >= 100)
        {
            var smallId = minId + iconIndex * 2;
            var largeId = smallId + 1;
            var small = resources.Where(resource => resource.Id == smallId).ToArray();
            var large = resources.Where(resource => resource.Id == largeId).ToArray();
            if (small.Length == 0 && large.Length == 0)
            {
                throw new InvalidOperationException($"Icon index {iconIndex} has no matching RT_BITMAP ID={smallId}/{largeId}.");
            }

            return new DllBitmapResourcePair(iconIndex, smallId, largeId, small, large);
        }

        var distinctIds = resources.Select(resource => resource.Id).Distinct().OrderBy(id => id).ToArray();
        if (iconIndex >= distinctIds.Length)
        {
            throw new InvalidOperationException($"Icon index {iconIndex} is outside DLL bitmap resource range 0-{distinctIds.Length - 1}.");
        }

        var id = distinctIds[iconIndex];
        var variants = resources.Where(resource => resource.Id == id).ToArray();
        return new DllBitmapResourcePair(iconIndex, id, id, variants, variants);
    }

    public DllBitmapResourceRecord? SelectDisplayVariant(IEnumerable<DllBitmapResourceRecord> variants)
    {
        var resources = variants.ToArray();
        if (resources.Length == 0) return null;

        var majorityLanguage = resources
            .Where(resource => resource.LanguageId != 0)
            .GroupBy(resource => resource.LanguageId)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key == PreferredLanguageId)
            .ThenBy(group => group.Key)
            .Select(group => (ushort?)group.Key)
            .FirstOrDefault();

        return resources
            .OrderByDescending(resource => resource.LanguageId == PreferredLanguageId)
            .ThenByDescending(resource => majorityLanguage.HasValue && resource.LanguageId == majorityLanguage.Value)
            .ThenBy(resource => resource.LanguageId == 0)
            .ThenByDescending(resource => resource.BitCount)
            .ThenByDescending(resource => resource.Width * resource.Height)
            .ThenByDescending(resource => resource.SizeBytes)
            .FirstOrDefault();
    }

    public DllIconRasterPair NormalizePairFromFile(string sourcePath)
    {
        using var raw = Image.FromFile(sourcePath);
        return NormalizePair(raw, sourcePath);
    }

    public DllIconRasterPair NormalizePair(Image source, string sourceLabel = "")
    {
        using var sourceBitmap = CloneArgb(source);
        var large = NormalizeToBitmap(sourceBitmap, LargeIconSize, "large");
        var small = NormalizeSmallFromLarge(large.Bitmap);
        return new DllIconRasterPair(
            string.IsNullOrWhiteSpace(sourceLabel) ? "<bitmap>" : sourceLabel,
            source.Width,
            source.Height,
            small.Bitmap,
            large.Bitmap,
            small.Info,
            large.Info);
    }

    public Bitmap NormalizeLargeBitmap(Image source)
    {
        using var sourceBitmap = CloneArgb(source);
        var normalized = NormalizeToBitmap(sourceBitmap, LargeIconSize, "large");
        return normalized.Bitmap;
    }

    public DllIconRasterPair BuildPairFromSources(string largeSourcePath, string? smallSourcePath = null)
    {
        using var largeRaw = Image.FromFile(largeSourcePath);
        using var largeBitmap = CloneArgb(largeRaw);
        var large = IsExactBmp(largeSourcePath, largeBitmap, LargeIconSize)
            ? NormalizeExactStorageBitmap(largeBitmap, LargeIconSize, "large")
            : NormalizeToBitmap(largeBitmap, LargeIconSize, "large");

        DllBitmapNormalizeResult small;
        if (!string.IsNullOrWhiteSpace(smallSourcePath))
        {
            using var smallRaw = Image.FromFile(smallSourcePath);
            using var smallBitmap = CloneArgb(smallRaw);
            small = IsExactBmp(smallSourcePath, smallBitmap, SmallIconSize)
                ? NormalizeExactStorageBitmap(smallBitmap, SmallIconSize, "small")
                : NormalizeToBitmap(smallBitmap, SmallIconSize, "small");
        }
        else
        {
            small = NormalizeSmallFromLarge(large.Bitmap);
        }

        return new DllIconRasterPair(
            string.IsNullOrWhiteSpace(smallSourcePath) ? largeSourcePath : $"{smallSourcePath}; {largeSourcePath}",
            largeRaw.Width,
            largeRaw.Height,
            small.Bitmap,
            large.Bitmap,
            small.Info,
            large.Info);
    }

    public DllIconStoragePair? TryBuildExactIndexedStoragePair(string largeSourcePath, string? smallSourcePath = null)
    {
        var large = TryReadExact8BppStorageBmp(largeSourcePath, LargeIconSize, "large");
        if (large == null) return null;

        DllIconStorageImage small;
        if (!string.IsNullOrWhiteSpace(smallSourcePath))
        {
            var exactSmall = TryReadExact8BppStorageBmp(smallSourcePath, SmallIconSize, "small");
            if (exactSmall == null) return null;
            small = exactSmall;
        }
        else
        {
            small = BuildSmallStorageFromLarge(large);
        }

        return new DllIconStoragePair(
            string.IsNullOrWhiteSpace(smallSourcePath) ? largeSourcePath : $"{smallSourcePath}; {largeSourcePath}",
            large.Width,
            large.Height,
            small,
            large,
            string.IsNullOrWhiteSpace(smallSourcePath)
                ? "exact 8bpp BMP large preserved; small generated by indexed 2x hard downsample"
                : "exact 8bpp BMP small/large preserved");
    }

    public DllItemIconBmpImportClassification ClassifyItemIconBmpImport(
        string largeSourcePath,
        DllBitmapResourcePair targetPair,
        IReadOnlyList<DllBitmapResourceRecord> allResources,
        string? smallSourcePath = null)
    {
        var diagnostics = new List<string>();
        if (!Path.GetExtension(largeSourcePath).Equals(".bmp", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add("not-bmp: source will use the visual normalization pipeline.");
            return new DllItemIconBmpImportClassification(false, "visual-bmp-normalized", null, diagnostics);
        }

        var large = TryReadExact8BppStorageBmp(largeSourcePath, LargeIconSize, "large");
        if (large == null)
        {
            diagnostics.Add("not-exact-8bpp: BMP is not 32x32/8bpp/BI_RGB storage layout; using visual normalization.");
            return new DllItemIconBmpImportClassification(false, "visual-bmp-normalized", null, diagnostics);
        }

        if (!HasBlueTransparentPalette0(large.Palette))
        {
            diagnostics.Add("palette-index-0-not-blue: storage BMP requires palette[0]=#0000FF; using visual normalization.");
            return new DllItemIconBmpImportClassification(false, "visual-bmp-normalized", null, diagnostics);
        }

        DllIconStorageImage small;
        if (!string.IsNullOrWhiteSpace(smallSourcePath))
        {
            var exactSmall = TryReadExact8BppStorageBmp(smallSourcePath, SmallIconSize, "small");
            if (exactSmall == null)
            {
                diagnostics.Add("small-not-exact-8bpp: paired small BMP is not 16x16/8bpp/BI_RGB storage layout; using visual normalization.");
                return new DllItemIconBmpImportClassification(false, "visual-bmp-normalized", null, diagnostics);
            }

            if (!HasBlueTransparentPalette0(exactSmall.Palette))
            {
                diagnostics.Add("small-palette-index-0-not-blue: paired small BMP requires palette[0]=#0000FF; using visual normalization.");
                return new DllItemIconBmpImportClassification(false, "visual-bmp-normalized", null, diagnostics);
            }

            small = exactSmall;
        }
        else
        {
            small = BuildSmallStorageFromLarge(large);
        }

        var reference = ResolveReferenceStoragePalette(allResources, targetPair);
        if (reference == null || reference.Palette.Count == 0)
        {
            diagnostics.Add("no-compatible-target-palette: no 8bpp Itemicon.dll palette was available; using visual normalization.");
            return new DllItemIconBmpImportClassification(false, "visual-bmp-normalized", null, diagnostics);
        }

        if (!HasSamePalette(large.Palette, reference.Palette) ||
            !HasSamePalette(small.Palette, reference.Palette))
        {
            diagnostics.Add($"palette-mismatch-fallback: source BMP palette does not match {reference.SourceSummary}; using visual normalization.");
            return new DllItemIconBmpImportClassification(false, "palette-mismatch-fallback", null, diagnostics);
        }

        var storagePair = new DllIconStoragePair(
            string.IsNullOrWhiteSpace(smallSourcePath) ? largeSourcePath : $"{smallSourcePath}; {largeSourcePath}",
            large.Width,
            large.Height,
            small,
            large,
            string.IsNullOrWhiteSpace(smallSourcePath)
                ? $"storage-preserved: exact 8bpp BMP large preserved; small generated by indexed 2x hard downsample; palette={reference.SourceSummary}"
                : $"storage-preserved: exact 8bpp BMP small/large preserved; palette={reference.SourceSummary}");

        diagnostics.Add(storagePair.Summary);
        return new DllItemIconBmpImportClassification(true, "storage-preserved", storagePair, diagnostics);
    }

    public DllIconRasterPair BuildPairFromBitmaps(Bitmap largeBitmap, Bitmap smallBitmap, string sourceLabel = "")
    {
        using var largeSource = CloneArgb(largeBitmap);
        using var smallSource = CloneArgb(smallBitmap);
        var large = largeSource.Width == LargeIconSize && largeSource.Height == LargeIconSize
            ? NormalizeExactStorageBitmap(largeSource, LargeIconSize, "large")
            : NormalizeToBitmap(largeSource, LargeIconSize, "large");
        var small = smallSource.Width == SmallIconSize && smallSource.Height == SmallIconSize
            ? NormalizeExactStorageBitmap(smallSource, SmallIconSize, "small")
            : NormalizeToBitmap(smallSource, SmallIconSize, "small");
        return new DllIconRasterPair(
            string.IsNullOrWhiteSpace(sourceLabel) ? "<bitmap-pair>" : sourceLabel,
            largeBitmap.Width,
            largeBitmap.Height,
            small.Bitmap,
            large.Bitmap,
            small.Info,
            large.Info);
    }

    public DllIconRasterPair QuantizePair(DllIconRasterPair pair, IReadOnlyList<Color> palette)
    {
        var small = QuantizeBitmapToPalette(pair.SmallBitmap, palette);
        var large = QuantizeBitmapToPalette(pair.LargeBitmap, palette);
        return new DllIconRasterPair(
            pair.SourceLabel,
            pair.SourceWidth,
            pair.SourceHeight,
            small,
            large,
            pair.SmallInfo,
            pair.LargeInfo);
    }

    public IReadOnlyList<DllBitmapResourceUpdate> BuildUpdates(DllBitmapResourcePair targetPair, DllIconRasterPair rasterPair)
    {
        var updates = new List<DllBitmapResourceUpdate>();
        foreach (var target in targetPair.SmallVariants)
        {
            updates.Add(new DllBitmapResourceUpdate(target.Id, target.LanguageId, EncodeForTarget(rasterPair.SmallBitmap, target)));
        }

        foreach (var target in targetPair.LargeVariants)
        {
            updates.Add(new DllBitmapResourceUpdate(target.Id, target.LanguageId, EncodeForTarget(rasterPair.LargeBitmap, target)));
        }

        if (updates.Count == 0)
        {
            var fallbackTarget = new DllBitmapResourceRecord(targetPair.LargeId, 0, Array.Empty<byte>(), LargeIconSize, LargeIconSize, 32, 0, 0, "32bpp-alpha");
            updates.Add(new DllBitmapResourceUpdate(fallbackTarget.Id, fallbackTarget.LanguageId, EncodeForTarget(rasterPair.LargeBitmap, fallbackTarget)));
        }

        return updates;
    }

    public IReadOnlyList<DllBitmapResourceUpdate> BuildCanonical8BppUpdates(DllBitmapResourcePair targetPair, DllIconStoragePair storagePair)
    {
        var language = ResolveCanonicalLanguage(targetPair);
        return
        [
            new DllBitmapResourceUpdate(targetPair.SmallId, language, storagePair.Small.DibBytes),
            new DllBitmapResourceUpdate(targetPair.LargeId, language, storagePair.Large.DibBytes)
        ];
    }

    public IReadOnlyList<DllBitmapResourceUpdate> BuildTransparentUpdates(DllBitmapResourcePair targetPair)
    {
        var updates = new List<DllBitmapResourceUpdate>();
        foreach (var target in targetPair.AllVariants)
        {
            using var bitmap = new Bitmap(Math.Max(1, target.Width), Math.Max(1, target.Height), PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
            }

            updates.Add(new DllBitmapResourceUpdate(target.Id, target.LanguageId, EncodeForTarget(bitmap, target)));
        }

        return updates;
    }

    public IReadOnlyList<Color> ResolveStoragePalette(IReadOnlyList<DllBitmapResourceRecord> resources, DllBitmapResourcePair? targetPair = null)
    {
        var reference = ResolveReferenceStoragePalette(resources, targetPair);
        return reference?.Palette ?? Array.Empty<Color>();
    }

    private static DllStoragePaletteReference? ResolveReferenceStoragePalette(
        IReadOnlyList<DllBitmapResourceRecord> resources,
        DllBitmapResourcePair? targetPair)
    {
        var targetCandidates = targetPair == null
            ? Array.Empty<DllStoragePaletteCandidate>()
            : targetPair.AllVariants
                .Where(resource => resource.BitCount == 8)
                .Select(resource => new DllStoragePaletteCandidate(resource, ReadDibPalette(resource.DibBytes)))
                .Where(candidate => candidate.Palette.Count > 0 && HasBlueTransparentPalette0(candidate.Palette))
                .ToArray();

        if (targetCandidates.Length > 0)
        {
            var selected = targetCandidates
                .OrderByDescending(candidate => candidate.Resource.LanguageId == PreferredLanguageId)
                .ThenByDescending(candidate => targetPair!.LargeVariants.Any(large => large.Id == candidate.Resource.Id && large.LanguageId == candidate.Resource.LanguageId))
                .ThenByDescending(candidate => candidate.Resource.Width * candidate.Resource.Height)
                .ThenBy(candidate => candidate.Resource.LanguageId)
                .First();

            return new DllStoragePaletteReference(
                selected.Palette,
                $"target ID={selected.Resource.Id} LANG={selected.Resource.LanguageId} {selected.Resource.Width}x{selected.Resource.Height}/8bpp");
        }

        var majority = resources
            .Where(resource => resource.BitCount == 8)
            .Select(resource => new DllStoragePaletteCandidate(resource, ReadDibPalette(resource.DibBytes)))
            .Where(candidate => candidate.Palette.Count > 0 && HasBlueTransparentPalette0(candidate.Palette))
            .GroupBy(candidate => PaletteSignature(candidate.Palette), StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Any(candidate => candidate.Resource.LanguageId == PreferredLanguageId))
            .ThenByDescending(group => group.Max(candidate => candidate.Resource.Width * candidate.Resource.Height))
            .FirstOrDefault();

        if (majority == null) return null;
        var representative = majority
            .OrderByDescending(candidate => candidate.Resource.LanguageId == PreferredLanguageId)
            .ThenByDescending(candidate => candidate.Resource.Width * candidate.Resource.Height)
            .ThenBy(candidate => candidate.Resource.Id)
            .First();
        return new DllStoragePaletteReference(
            representative.Palette,
            $"DLL majority 8bpp palette x{majority.Count()} (sample ID={representative.Resource.Id} LANG={representative.Resource.LanguageId})");
    }

    public static DllDecodedBitmap? DecodeStorageDib(byte[] dibBytes)
    {
        if (!TryReadDibInfo(dibBytes, out var info)) return null;

        var palette = ReadDibPalette(dibBytes);
        var effectiveRowOrder = ResolveDibRowOrder(info.SignedHeight, info.BitCount);
        var bitmap = new Bitmap(info.Width, info.Height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < info.Height; y++)
        {
            var sourceY = effectiveRowOrder == DllBitmapDibRowOrder.StandardBottomUp && info.SignedHeight > 0
                ? info.Height - 1 - y
                : y;
            var rowOffset = info.PixelOffset + sourceY * info.Stride;
            for (var x = 0; x < info.Width; x++)
            {
                bitmap.SetPixel(x, y, ReadDibPixel(dibBytes, rowOffset, x, info.BitCount, palette, out _));
            }
        }

        return new DllDecodedBitmap(bitmap, info, Array.Empty<string>());
    }

    public byte[] BuildStorageBmpBytes(DllBitmapResourceRecord resource)
    {
        var fileHeaderSize = 14;
        var bmpBytes = new byte[fileHeaderSize + resource.DibBytes.Length];
        bmpBytes[0] = (byte)'B';
        bmpBytes[1] = (byte)'M';
        BitConverter.GetBytes(bmpBytes.Length).CopyTo(bmpBytes, 2);
        var pixelOffset = fileHeaderSize + ResolveDibPixelOffset(resource.DibBytes);
        BitConverter.GetBytes(pixelOffset).CopyTo(bmpBytes, 10);
        Buffer.BlockCopy(resource.DibBytes, 0, bmpBytes, fileHeaderSize, resource.DibBytes.Length);
        return bmpBytes;
    }

    public static DllDecodedBitmap? DecodeDib(byte[] dibBytes)
    {
        if (!TryReadDibInfo(dibBytes, out var info)) return null;

        var palette = new Color[info.PaletteEntries];
        for (var i = 0; i < palette.Length; i++)
        {
            var offset = info.HeaderSize + i * 4;
            palette[i] = Color.FromArgb(255, dibBytes[offset + 2], dibBytes[offset + 1], dibBytes[offset]);
        }

        var effectiveRowOrder = ResolveDibRowOrder(info.SignedHeight, info.BitCount);
        var bitmap = new Bitmap(info.Width, info.Height, PixelFormat.Format32bppArgb);
        var magentaOpaquePixels = 0;
        for (var y = 0; y < info.Height; y++)
        {
            var sourceY = effectiveRowOrder == DllBitmapDibRowOrder.StandardBottomUp && info.SignedHeight > 0
                ? info.Height - 1 - y
                : y;
            var rowOffset = info.PixelOffset + sourceY * info.Stride;
            for (var x = 0; x < info.Width; x++)
            {
                var pixel = ReadDibPixel(dibBytes, rowOffset, x, info.BitCount, palette, out var paletteIndex);
                if (info.BitCount <= 8 && paletteIndex == 0)
                {
                    bitmap.SetPixel(x, y, Color.Transparent);
                    continue;
                }

                if (info.BitCount == 24 && IsDllBlueKey(pixel))
                {
                    bitmap.SetPixel(x, y, Color.Transparent);
                    continue;
                }

                if (pixel.A != 0 && IsMagentaKey(pixel))
                {
                    magentaOpaquePixels++;
                    bitmap.SetPixel(x, y, Color.Transparent);
                    continue;
                }

                bitmap.SetPixel(x, y, pixel);
            }
        }

        var warnings = magentaOpaquePixels > 0
            ? new[] { $"Opaque magenta-like pixels detected: {magentaOpaquePixels:N0}. Re-import to normalize 6.5 transparency." }
            : Array.Empty<string>();
        return new DllDecodedBitmap(bitmap, info, warnings);
    }

    public static Bitmap CompositeTransparentToDllKey(Bitmap source)
    {
        var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var color = source.GetPixel(x, y);
                bitmap.SetPixel(x, y, IsTransparent(color)
                    ? DllTransparentKey
                    : Color.FromArgb(255, color.R, color.G, color.B));
            }
        }

        return bitmap;
    }

    public static void SaveBmpWithDllKey(Bitmap source, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var bitmap = CompositeTransparentToDllKey(source);
        bitmap.Save(path, ImageFormat.Bmp);
    }

    public static Color MapColorToPalette(Color color, IReadOnlyList<Color> palette)
    {
        if (color.A == 0 || palette.Count == 0) return Color.Transparent;
        if (HasSameRgb(color, palette[0]))
        {
            return Color.FromArgb(255, palette[0].R, palette[0].G, palette[0].B);
        }

        var index = FindNearestPaletteIndex(Color.FromArgb(255, color.R, color.G, color.B), palette);
        var mapped = palette[index];
        return Color.FromArgb(255, mapped.R, mapped.G, mapped.B);
    }

    public static Bitmap QuantizeBitmapToPalette(Bitmap source, IReadOnlyList<Color> palette)
    {
        var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                bitmap.SetPixel(x, y, MapColorToPalette(source.GetPixel(x, y), palette));
            }
        }

        return bitmap;
    }

    public static string BuildVariantSummary(DllBitmapResourceRecord resource)
        => $"ID={resource.Id} Lang={resource.LanguageId} {resource.Width}x{resource.Height} {resource.BitCount}bpp {resource.TransparencyMode}";

    private static DllBitmapNormalizeResult NormalizeExactStorageBitmap(Bitmap source, int targetSize, string role)
    {
        using var transparentSource = CloneArgb(source);
        var converted = ApplyInputTransparency(transparentSource);
        var output = new Bitmap(targetSize, targetSize, PixelFormat.Format32bppArgb);
        ClearBitmap(output);
        CopyUnscaled(transparentSource, output, 0, 0);
        var crop = FindVisibleBounds(output);
        var touchesEdge = crop.HasValue &&
                          (crop.Value.Left == 0 ||
                           crop.Value.Top == 0 ||
                           crop.Value.Right == output.Width ||
                           crop.Value.Bottom == output.Height);
        return new DllBitmapNormalizeResult(
            output,
            new DllBitmapNormalizeInfo(role, targetSize, targetSize, crop, true, touchesEdge, converted, CountVisiblePixels(output)));
    }

    private static DllBitmapNormalizeResult NormalizeToBitmap(Bitmap source, int targetSize, string role)
    {
        using var transparentSource = CloneArgb(source);
        var converted = ApplyInputTransparency(transparentSource);
        var crop = FindVisibleBounds(transparentSource);
        var exact = transparentSource.Width == targetSize && transparentSource.Height == targetSize;
        var touchesEdge = crop.HasValue &&
                          (crop.Value.Left == 0 ||
                           crop.Value.Top == 0 ||
                           crop.Value.Right == transparentSource.Width ||
                           crop.Value.Bottom == transparentSource.Height);

        var output = new Bitmap(targetSize, targetSize, PixelFormat.Format32bppArgb);
        ClearBitmap(output);

        if (exact)
        {
            CopyUnscaled(transparentSource, output, 0, 0);
        }
        else if (crop.HasValue)
        {
            var sourceRect = crop.Value;
            var scale = Math.Min(targetSize / (float)sourceRect.Width, targetSize / (float)sourceRect.Height);
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0) scale = 1;
            var drawWidth = Math.Max(1, (int)Math.Round(sourceRect.Width * scale));
            var drawHeight = Math.Max(1, (int)Math.Round(sourceRect.Height * scale));
            var x = (targetSize - drawWidth) / 2;
            var y = (targetSize - drawHeight) / 2;
            CopyNearest(transparentSource, sourceRect, output, new Rectangle(x, y, drawWidth, drawHeight));
        }

        return new DllBitmapNormalizeResult(
            output,
            new DllBitmapNormalizeInfo(role, targetSize, targetSize, crop, exact, touchesEdge, converted, CountVisiblePixels(output)));
    }

    private static DllBitmapNormalizeResult NormalizeSmallFromLarge(Bitmap large)
    {
        var output = new Bitmap(SmallIconSize, SmallIconSize, PixelFormat.Format32bppArgb);
        ClearBitmap(output);

        if (large.Width == LargeIconSize && large.Height == LargeIconSize)
        {
            Downsample2xHard(large, output);
        }
        else
        {
            CopyNearest(large, new Rectangle(0, 0, large.Width, large.Height), output, new Rectangle(0, 0, SmallIconSize, SmallIconSize));
        }

        var crop = FindVisibleBounds(large);
        return new DllBitmapNormalizeResult(
            output,
            new DllBitmapNormalizeInfo("small", SmallIconSize, SmallIconSize, crop, false, false, 0, CountVisiblePixels(output)));
    }

    private static byte[] EncodeForTarget(Bitmap source, DllBitmapResourceRecord target)
        => target.BitCount switch
        {
            32 => Encode32BppPreservingDib(source, target),
            24 => Encode24BppPreservingDib(source, target),
            8 => Encode8BppPreservingDib(source, target),
            _ => throw new InvalidOperationException(
                $"RT_BITMAP ID={target.Id} LANG={target.LanguageId} is {target.BitCount}bpp; pixel editing supports only uncompressed 8/24/32bpp DIB resources.")
        };

    private static byte[] Encode8BppPreservingDib(Bitmap source, DllBitmapResourceRecord target)
    {
        if (!TryReadDibInfo(target.DibBytes, out var info) || info.BitCount != 8 ||
            source.Width != info.Width || source.Height != info.Height)
            throw new InvalidOperationException("The target 8bpp DIB layout does not match the editable icon.");
        var palette = ReadDibPalette(target.DibBytes);
        if (palette.Length < 2) throw new InvalidOperationException("The target 8bpp DIB has no usable palette.");
        using var original = DecodeStorageDib(target.DibBytes)
                             ?? throw new InvalidOperationException("The target 8bpp DIB cannot be decoded.");
        var output = target.DibBytes.ToArray();
        for (var y = 0; y < info.Height; y++)
        for (var x = 0; x < info.Width; x++)
        {
            var color = source.GetPixel(x, y);
            if (PixelsEquivalent(color, original.Bitmap.GetPixel(x, y))) continue;
            var storedY = ResolveStoredY(info, y);
            output[info.PixelOffset + storedY * info.Stride + x] =
                IsTransparent(color) || HasSameRgb(color, palette[0])
                    ? (byte)0
                    : FindNearestPaletteIndex(Color.FromArgb(255, color.R, color.G, color.B), palette);
        }
        return output;
    }

    private static byte[] Encode24BppPreservingDib(Bitmap source, DllBitmapResourceRecord target)
    {
        if (!TryReadDibInfo(target.DibBytes, out var info) || info.BitCount != 24 ||
            source.Width != info.Width || source.Height != info.Height)
            throw new InvalidOperationException("The target 24bpp DIB layout does not match the editable icon.");
        using var original = DecodeStorageDib(target.DibBytes)
                             ?? throw new InvalidOperationException("The target 24bpp DIB cannot be decoded.");
        var output = target.DibBytes.ToArray();
        for (var y = 0; y < info.Height; y++)
        for (var x = 0; x < info.Width; x++)
        {
            var color = source.GetPixel(x, y);
            if (PixelsEquivalent(color, original.Bitmap.GetPixel(x, y))) continue;
            if (IsTransparent(color)) color = DllTransparentKey;
            var offset = info.PixelOffset + ResolveStoredY(info, y) * info.Stride + x * 3;
            output[offset] = color.B;
            output[offset + 1] = color.G;
            output[offset + 2] = color.R;
        }
        return output;
    }

    private static byte[] Encode32BppPreservingDib(Bitmap source, DllBitmapResourceRecord target)
    {
        if (!TryReadDibInfo(target.DibBytes, out var info) || info.BitCount != 32 ||
            source.Width != info.Width || source.Height != info.Height)
            throw new InvalidOperationException("The target 32bpp DIB layout does not match the editable icon.");
        using var original = DecodeStorageDib(target.DibBytes)
                             ?? throw new InvalidOperationException("The target 32bpp DIB cannot be decoded.");
        var output = target.DibBytes.ToArray();
        for (var y = 0; y < info.Height; y++)
        for (var x = 0; x < info.Width; x++)
        {
            var color = source.GetPixel(x, y);
            if (PixelsEquivalent(color, original.Bitmap.GetPixel(x, y))) continue;
            var offset = info.PixelOffset + ResolveStoredY(info, y) * info.Stride + x * 4;
            output[offset] = color.B;
            output[offset + 1] = color.G;
            output[offset + 2] = color.R;
            output[offset + 3] = color.A;
        }
        return output;
    }

    private static int ResolveStoredY(DllBitmapDibInfo info, int y)
        => ResolveDibRowOrder(info.SignedHeight, info.BitCount) == DllBitmapDibRowOrder.StandardBottomUp &&
           info.SignedHeight > 0
            ? info.Height - 1 - y
            : y;

    private static bool PixelsEquivalent(Color left, Color right)
        => left.ToArgb() == right.ToArgb() || (left.A == 0 && right.A == 0);

    private static byte[] Encode8BppBottomUpDib(Bitmap source, DllBitmapResourceRecord target)
    {
        var palette = ReadDibPalette(target.DibBytes);
        if (palette.Length < 2)
        {
            return Encode24BppBottomUpDib(source);
        }

        var paletteEntries = target.ColorUsed > 0
            ? Math.Clamp(target.ColorUsed, 2, 256)
            : 256;
        var encodedPalette = new Color[paletteEntries];
        for (var i = 0; i < encodedPalette.Length; i++)
        {
            encodedPalette[i] = i < palette.Length ? palette[i] : Color.Black;
        }

        var width = source.Width;
        var height = source.Height;
        var stride = ((width * 8 + 31) / 32) * 4;
        var imageSize = checked(stride * height);
        var dib = new byte[40 + encodedPalette.Length * 4 + imageSize];

        WriteBitmapInfoHeader(dib, width, height, 8, imageSize, target.ColorUsed > 0 ? paletteEntries : 0);
        for (var i = 0; i < encodedPalette.Length; i++)
        {
            var offset = 40 + i * 4;
            var color = encodedPalette[i];
            dib[offset] = color.B;
            dib[offset + 1] = color.G;
            dib[offset + 2] = color.R;
            dib[offset + 3] = 0;
        }

        for (var y = 0; y < height; y++)
        {
            var storedY = height - 1 - y;
            var rowOffset = 40 + encodedPalette.Length * 4 + storedY * stride;
            for (var x = 0; x < width; x++)
            {
                var color = source.GetPixel(x, y);
                dib[rowOffset + x] = IsTransparent(color) || HasSameRgb(color, encodedPalette[0])
                    ? (byte)0
                    : FindNearestPaletteIndex(Color.FromArgb(255, color.R, color.G, color.B), encodedPalette);
            }
        }

        return dib;
    }

    private static byte[] Encode24BppBottomUpDib(Bitmap source)
    {
        var width = source.Width;
        var height = source.Height;
        var stride = ((width * 24 + 31) / 32) * 4;
        var imageSize = checked(stride * height);
        var dib = new byte[40 + imageSize];

        WriteBitmapInfoHeader(dib, width, height, 24, imageSize);
        for (var y = 0; y < height; y++)
        {
            var storedY = height - 1 - y;
            var rowOffset = 40 + storedY * stride;
            for (var x = 0; x < width; x++)
            {
                var color = source.GetPixel(x, y);
                if (IsTransparent(color))
                {
                    color = DllTransparentKey;
                }
                else
                {
                    color = Color.FromArgb(255, color.R, color.G, color.B);
                }

                dib[rowOffset + x * 3] = color.B;
                dib[rowOffset + x * 3 + 1] = color.G;
                dib[rowOffset + x * 3 + 2] = color.R;
            }
        }

        return dib;
    }

    private static byte[] Encode32BppTopFirstDib(Bitmap source)
    {
        var width = source.Width;
        var height = source.Height;
        var stride = checked(width * 4);
        var imageSize = checked(stride * height);
        var dib = new byte[40 + imageSize];

        WriteBitmapInfoHeader(dib, width, height, 32, imageSize);
        var offset = 40;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = source.GetPixel(x, y);
                if (IsTransparent(color))
                {
                    dib[offset++] = 0;
                    dib[offset++] = 0;
                    dib[offset++] = 0;
                    dib[offset++] = 0;
                    continue;
                }

                dib[offset++] = color.B;
                dib[offset++] = color.G;
                dib[offset++] = color.R;
                dib[offset++] = 255;
            }
        }

        return dib;
    }

    private static DllIconStorageImage? TryReadExact8BppStorageBmp(string sourcePath, int expectedSize, string role)
    {
        if (!Path.GetExtension(sourcePath).Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(sourcePath))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(sourcePath);
        if (bytes.Length < 14 + 40 ||
            bytes[0] != (byte)'B' ||
            bytes[1] != (byte)'M')
        {
            return null;
        }

        var fileSize = BitConverter.ToInt32(bytes, 2);
        var pixelOffset = BitConverter.ToInt32(bytes, 10);
        var headerOffset = 14;
        var headerSize = BitConverter.ToInt32(bytes, headerOffset);
        if (headerSize != 40 ||
            fileSize > bytes.Length ||
            pixelOffset <= headerOffset + headerSize ||
            pixelOffset > bytes.Length)
        {
            return null;
        }

        var width = BitConverter.ToInt32(bytes, headerOffset + 4);
        var signedHeight = BitConverter.ToInt32(bytes, headerOffset + 8);
        var height = Math.Abs(signedHeight);
        var planes = BitConverter.ToUInt16(bytes, headerOffset + 12);
        var bitCount = BitConverter.ToUInt16(bytes, headerOffset + 14);
        var compression = BitConverter.ToInt32(bytes, headerOffset + 16);
        var colorUsed = BitConverter.ToInt32(bytes, headerOffset + 32);
        if (width != expectedSize ||
            height != expectedSize ||
            signedHeight != expectedSize ||
            planes != 1 ||
            bitCount != 8 ||
            compression != 0)
        {
            return null;
        }

        var paletteEntries = colorUsed > 0 ? colorUsed : 256;
        if (paletteEntries is < 2 or > 256) return null;
        var expectedPixelOffset = headerOffset + headerSize + paletteEntries * 4;
        if (pixelOffset != expectedPixelOffset) return null;
        var stride = ((width * bitCount + 31) / 32) * 4;
        if (fileSize > 0 && fileSize < pixelOffset + stride * height) return null;
        if (pixelOffset + stride * height > bytes.Length) return null;

        var dibLength = checked(headerSize + paletteEntries * 4 + stride * height);
        var dib = new byte[dibLength];
        Buffer.BlockCopy(bytes, headerOffset, dib, 0, dib.Length);
        var palette = ReadDibPalette(dib);
        if (palette.Length < 2) return null;

        return new DllIconStorageImage(role, width, height, dib, palette, signedHeight, stride);
    }

    private static DllIconStorageImage BuildSmallStorageFromLarge(DllIconStorageImage large)
    {
        if (large.Width != LargeIconSize || large.Height != LargeIconSize)
        {
            throw new InvalidOperationException("Indexed item icon large source must be 32x32 to generate small storage.");
        }

        var paletteEntries = large.Palette.Count;
        var width = SmallIconSize;
        var height = SmallIconSize;
        var stride = ((width * 8 + 31) / 32) * 4;
        var imageSize = checked(stride * height);
        var dib = new byte[40 + paletteEntries * 4 + imageSize];
        WriteBitmapInfoHeader(dib, width, height, 8, imageSize, large.Palette.Count == 256 ? 0 : paletteEntries);
        for (var i = 0; i < paletteEntries; i++)
        {
            var offset = 40 + i * 4;
            var color = large.Palette[i];
            dib[offset] = color.B;
            dib[offset + 1] = color.G;
            dib[offset + 2] = color.R;
            dib[offset + 3] = 0;
        }

        for (var y = 0; y < height; y++)
        {
            var storedY = height - 1 - y;
            var rowOffset = 40 + paletteEntries * 4 + storedY * stride;
            for (var x = 0; x < width; x++)
            {
                dib[rowOffset + x] = PickHardDownsampleIndex(large, x * 2, y * 2);
            }
        }

        return new DllIconStorageImage("small", width, height, dib, large.Palette, height, stride);
    }

    private static byte PickHardDownsampleIndex(DllIconStorageImage source, int left, int top)
    {
        ReadOnlySpan<(int X, int Y)> priority =
        [
            (1, 1),
            (1, 0),
            (0, 1),
            (0, 0)
        ];

        byte fallback = 0;
        foreach (var (offsetX, offsetY) in priority)
        {
            var x = Math.Min(source.Width - 1, left + offsetX);
            var y = Math.Min(source.Height - 1, top + offsetY);
            var index = ReadStorageIndex(source, x, y);
            if (index != 0) return index;
            fallback = index;
        }

        return fallback;
    }

    private static byte ReadStorageIndex(DllIconStorageImage source, int x, int y)
    {
        var sourceY = source.SignedHeight > 0 ? source.Height - 1 - y : y;
        var offset = 40 + source.Palette.Count * 4 + sourceY * source.Stride + x;
        return source.DibBytes[offset];
    }

    private static ushort ResolveCanonicalLanguage(DllBitmapResourcePair targetPair)
    {
        var languages = targetPair.AllVariants.Select(resource => resource.LanguageId).ToArray();
        if (languages.Contains(PreferredLanguageId)) return PreferredLanguageId;
        var majority = languages
            .Where(language => language != 0)
            .GroupBy(language => language)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => (ushort?)group.Key)
            .FirstOrDefault();
        return majority ?? (languages.Length > 0 ? languages[0] : (ushort)0);
    }

    private static void WriteBitmapInfoHeader(byte[] dib, int width, int height, ushort bitCount, int imageSize, int colorUsed = 0)
    {
        BitConverter.GetBytes(40).CopyTo(dib, 0);
        BitConverter.GetBytes(width).CopyTo(dib, 4);
        BitConverter.GetBytes(height).CopyTo(dib, 8);
        BitConverter.GetBytes((ushort)1).CopyTo(dib, 12);
        BitConverter.GetBytes(bitCount).CopyTo(dib, 14);
        BitConverter.GetBytes(0).CopyTo(dib, 16);
        BitConverter.GetBytes(imageSize).CopyTo(dib, 20);
        BitConverter.GetBytes(0).CopyTo(dib, 24);
        BitConverter.GetBytes(0).CopyTo(dib, 28);
        BitConverter.GetBytes(colorUsed).CopyTo(dib, 32);
        BitConverter.GetBytes(0).CopyTo(dib, 36);
    }

    public static Bitmap RenderPixelPerfectPreview(Bitmap raw, int canvasSize)
    {
        var targetSize = Math.Max(1, canvasSize);
        var scale = Math.Max(1, Math.Min(targetSize / raw.Width, targetSize / raw.Height));
        var width = raw.Width * scale;
        var height = raw.Height * scale;
        var x = Math.Max(0, (targetSize - width) / 2);
        var y = Math.Max(0, (targetSize - height) / 2);
        var canvas = new Bitmap(targetSize, targetSize, PixelFormat.Format32bppArgb);
        ClearBitmap(canvas);
        ScaleNearest(raw, new Rectangle(0, 0, raw.Width, raw.Height), canvas, new Rectangle(x, y, width, height));
        return canvas;
    }

    private static void ClearBitmap(Bitmap bitmap)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                bitmap.SetPixel(x, y, Color.Transparent);
            }
        }
    }

    private static void CopyUnscaled(Bitmap source, Bitmap target, int targetX, int targetY)
    {
        var width = Math.Min(source.Width, target.Width - targetX);
        var height = Math.Min(source.Height, target.Height - targetY);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                target.SetPixel(targetX + x, targetY + y, source.GetPixel(x, y));
            }
        }
    }

    private static void CopyNearest(Bitmap source, Rectangle sourceRect, Bitmap target, Rectangle targetRect)
    {
        ScaleNearest(source, sourceRect, target, targetRect);
    }

    private static void ScaleNearest(Bitmap source, Rectangle sourceRect, Bitmap target, Rectangle targetRect)
    {
        if (sourceRect.Width <= 0 || sourceRect.Height <= 0 || targetRect.Width <= 0 || targetRect.Height <= 0) return;
        for (var y = 0; y < targetRect.Height; y++)
        {
            var sourceY = sourceRect.Top + Math.Min(sourceRect.Height - 1, (int)Math.Floor((y + 0.5) * sourceRect.Height / targetRect.Height));
            var targetY = targetRect.Top + y;
            if (targetY < 0 || targetY >= target.Height) continue;
            for (var x = 0; x < targetRect.Width; x++)
            {
                var sourceX = sourceRect.Left + Math.Min(sourceRect.Width - 1, (int)Math.Floor((x + 0.5) * sourceRect.Width / targetRect.Width));
                var targetX = targetRect.Left + x;
                if (targetX < 0 || targetX >= target.Width) continue;
                target.SetPixel(targetX, targetY, source.GetPixel(sourceX, sourceY));
            }
        }
    }

    private static void Downsample2xHard(Bitmap source, Bitmap target)
    {
        for (var y = 0; y < target.Height; y++)
        {
            for (var x = 0; x < target.Width; x++)
            {
                target.SetPixel(x, y, PickHardDownsamplePixel(source, x * 2, y * 2));
            }
        }
    }

    private static Color PickHardDownsamplePixel(Bitmap source, int left, int top)
    {
        ReadOnlySpan<(int X, int Y)> priority =
        [
            (1, 1),
            (1, 0),
            (0, 1),
            (0, 0)
        ];

        Color? fallback = null;
        foreach (var (offsetX, offsetY) in priority)
        {
            var x = Math.Min(source.Width - 1, left + offsetX);
            var y = Math.Min(source.Height - 1, top + offsetY);
            var color = source.GetPixel(x, y);
            if (color.A != 0) return color;
            fallback ??= color;
        }

        return fallback ?? Color.Transparent;
    }

    private static Color[] ReadDibPalette(byte[] dibBytes)
    {
        if (!TryReadDibInfo(dibBytes, out var info) || info.PaletteEntries <= 0)
        {
            return Array.Empty<Color>();
        }

        var palette = new Color[info.PaletteEntries];
        for (var i = 0; i < palette.Length; i++)
        {
            var offset = info.HeaderSize + i * 4;
            palette[i] = Color.FromArgb(255, dibBytes[offset + 2], dibBytes[offset + 1], dibBytes[offset]);
        }

        return palette;
    }

    private static bool HasBlueTransparentPalette0(IReadOnlyList<Color> palette)
        => palette.Count > 0 &&
           palette[0].R == DllTransparentKey.R &&
           palette[0].G == DllTransparentKey.G &&
           palette[0].B == DllTransparentKey.B;

    private static bool HasSamePalette(IReadOnlyList<Color> left, IReadOnlyList<Color> right)
    {
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
        {
            if (left[i].R != right[i].R ||
                left[i].G != right[i].G ||
                left[i].B != right[i].B)
            {
                return false;
            }
        }

        return true;
    }

    private static string PaletteSignature(IReadOnlyList<Color> palette)
        => string.Join("|", palette.Select(color => $"{color.R:X2}{color.G:X2}{color.B:X2}"));

    private static byte FindNearestPaletteIndex(Color color, IReadOnlyList<Color> palette)
    {
        var bestIndex = palette.Count > 1 ? 1 : 0;
        var bestDistance = int.MaxValue;
        for (var i = bestIndex; i < palette.Count && i <= byte.MaxValue; i++)
        {
            var candidate = palette[i];
            var dr = color.R - candidate.R;
            var dg = color.G - candidate.G;
            var db = color.B - candidate.B;
            var distance = dr * dr + dg * dg + db * db;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            bestIndex = i;
            if (distance == 0) break;
        }

        return (byte)bestIndex;
    }

    private static bool HasSameRgb(Color left, Color right)
        => left.A != 0 &&
           left.R == right.R &&
           left.G == right.G &&
           left.B == right.B;

    private static Bitmap CloneArgb(Image raw)
    {
        var bitmap = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.DrawImage(raw, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        return bitmap;
    }

    private static int ApplyInputTransparency(Bitmap bitmap)
    {
        var converted = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (!IsInputTransparent(pixel)) continue;
                bitmap.SetPixel(x, y, Color.Transparent);
                converted++;
            }
        }

        return converted;
    }

    private static bool IsInputTransparent(Color pixel)
        => pixel.A == 0 || IsDllBlueKey(pixel) || IsMagentaKey(pixel);

    private static bool IsTransparent(Color pixel)
        => pixel.A == 0;

    private static bool IsExactBmp(string path, Image image, int size)
        => image.Width == size &&
           image.Height == size &&
           Path.GetExtension(path).Equals(".bmp", StringComparison.OrdinalIgnoreCase);

    public static bool IsDllBlueKey(Color pixel)
        => pixel.A != 0 && pixel.R == 0 && pixel.G == 0 && pixel.B == 255;

    public static bool IsMagentaKey(Color pixel)
    {
        if (pixel.A == 0) return true;
        return pixel.R >= 210 &&
               pixel.B >= 210 &&
               pixel.G <= 90 &&
               Math.Abs(pixel.R - pixel.B) <= 70;
    }

    private static Rectangle? FindVisibleBounds(Bitmap bitmap)
    {
        var minX = bitmap.Width;
        var minY = bitmap.Height;
        var maxX = -1;
        var maxY = -1;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A == 0) continue;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        return maxX < 0 ? null : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static int CountVisiblePixels(Bitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A != 0) count++;
            }
        }

        return count;
    }

    private static bool TryReadDibInfo(byte[] dibBytes, out DllBitmapDibInfo info)
    {
        info = new DllBitmapDibInfo();
        if (dibBytes.Length < 40) return false;
        var headerSize = BitConverter.ToInt32(dibBytes, 0);
        if (headerSize != 40 || dibBytes.Length < headerSize) return false;

        var width = BitConverter.ToInt32(dibBytes, 4);
        var signedHeight = BitConverter.ToInt32(dibBytes, 8);
        var height = Math.Abs(signedHeight);
        var planes = BitConverter.ToUInt16(dibBytes, 12);
        var bitCount = BitConverter.ToUInt16(dibBytes, 14);
        var compression = BitConverter.ToInt32(dibBytes, 16);
        var colorUsed = BitConverter.ToInt32(dibBytes, 32);
        if (width <= 0 || height <= 0 || planes != 1 || compression != 0 || bitCount is not (4 or 8 or 24 or 32))
        {
            return false;
        }

        var paletteEntries = bitCount <= 8 ? (colorUsed > 0 ? colorUsed : 1 << bitCount) : 0;
        var pixelOffset = headerSize + paletteEntries * 4;
        var stride = ((width * bitCount + 31) / 32) * 4;
        if (stride <= 0 || pixelOffset < 0 || pixelOffset + stride * height > dibBytes.Length)
        {
            return false;
        }

        info = new DllBitmapDibInfo(headerSize, width, signedHeight, height, bitCount, colorUsed, paletteEntries, pixelOffset, stride);
        return true;
    }

    private static int ResolveDibPixelOffset(byte[] dibBytes)
        => TryReadDibInfo(dibBytes, out var info) ? info.PixelOffset : 40;

    private static bool TryReadDibResourceInfo(byte[] dibBytes, out int width, out int height, out int bitCount, out int colorUsed, out string transparencyMode)
    {
        width = 0;
        height = 0;
        bitCount = 0;
        colorUsed = 0;
        transparencyMode = string.Empty;
        if (!TryReadDibInfo(dibBytes, out var info)) return false;

        width = info.Width;
        height = info.Height;
        bitCount = info.BitCount;
        colorUsed = info.ColorUsed;
        transparencyMode = bitCount == 32
            ? "32bpp-alpha"
            : bitCount <= 8
                ? "palette-index-0"
                : "blue-key";
        return true;
    }

    private static DllBitmapDibRowOrder ResolveDibRowOrder(int signedHeight, int bitCount)
    {
        if (signedHeight < 0) return DllBitmapDibRowOrder.CczTopFirst;
        return bitCount == 32 ? DllBitmapDibRowOrder.CczTopFirst : DllBitmapDibRowOrder.StandardBottomUp;
    }

    private static Color ReadDibPixel(
        byte[] bytes,
        int rowOffset,
        int x,
        int bitCount,
        IReadOnlyList<Color> palette,
        out int paletteIndex)
    {
        paletteIndex = -1;
        switch (bitCount)
        {
            case 4:
                paletteIndex = (bytes[rowOffset + x / 2] >> (x % 2 == 0 ? 4 : 0)) & 0x0F;
                return palette[paletteIndex];
            case 8:
                paletteIndex = bytes[rowOffset + x];
                return palette[paletteIndex];
            case 24:
                return Color.FromArgb(255, bytes[rowOffset + x * 3 + 2], bytes[rowOffset + x * 3 + 1], bytes[rowOffset + x * 3]);
            case 32:
                return Color.FromArgb(bytes[rowOffset + x * 4 + 3], bytes[rowOffset + x * 4 + 2], bytes[rowOffset + x * 4 + 1], bytes[rowOffset + x * 4]);
            default:
                return Color.Transparent;
        }
    }

    private static void ReadResourceDirectory(
        byte[] data,
        IReadOnlyList<DllPeSection> sections,
        int resourceBaseOffset,
        int directoryOffset,
        int level,
        List<int> path,
        List<DllBitmapResourceRecord> output)
    {
        if (directoryOffset < 0 || directoryOffset + 16 > data.Length || level > 3) return;
        var namedCount = BitConverter.ToUInt16(data, directoryOffset + 12);
        var idCount = BitConverter.ToUInt16(data, directoryOffset + 14);
        var entryCount = namedCount + idCount;
        var entriesOffset = directoryOffset + 16;
        for (var i = 0; i < entryCount; i++)
        {
            var entryOffset = entriesOffset + i * 8;
            if (entryOffset + 8 > data.Length) return;
            var nameRaw = BitConverter.ToInt32(data, entryOffset);
            var valueRaw = BitConverter.ToInt32(data, entryOffset + 4);
            var nameIsString = (nameRaw & unchecked((int)0x80000000)) != 0;
            if (nameIsString) continue;
            var id = nameRaw & 0x7FFFFFFF;
            var valueOffset = valueRaw & 0x7FFFFFFF;
            var isDirectory = (valueRaw & unchecked((int)0x80000000)) != 0;
            if (isDirectory)
            {
                path.Add(id);
                ReadResourceDirectory(data, sections, resourceBaseOffset, resourceBaseOffset + valueOffset, level + 1, path, output);
                path.RemoveAt(path.Count - 1);
                continue;
            }

            if (path.Count < 2 || path[0] != RtBitmap) continue;
            var dataEntryOffset = resourceBaseOffset + valueOffset;
            if (dataEntryOffset + 16 > data.Length) continue;
            var dataRva = BitConverter.ToInt32(data, dataEntryOffset);
            var size = BitConverter.ToInt32(data, dataEntryOffset + 4);
            var fileOffset = RvaToFileOffset(dataRva, sections);
            if (fileOffset < 0 || size <= 0 || fileOffset + size > data.Length) continue;

            var bytes = new byte[size];
            Buffer.BlockCopy(data, fileOffset, bytes, 0, size);
            if (!TryReadDibResourceInfo(bytes, out var width, out var height, out var bitCount, out var colorUsed, out var transparencyMode))
            {
                continue;
            }

            output.Add(new DllBitmapResourceRecord(path[1], (ushort)id, bytes, width, height, bitCount, size, colorUsed, transparencyMode));
        }
    }

    private static int RvaToFileOffset(int rva, IReadOnlyList<DllPeSection> sections)
    {
        foreach (var section in sections)
        {
            if (rva >= section.VirtualAddress && rva < section.VirtualAddress + section.Size)
            {
                return section.RawPointer + (rva - section.VirtualAddress);
            }
        }

        return -1;
    }

    private sealed record DllBitmapNormalizeResult(Bitmap Bitmap, DllBitmapNormalizeInfo Info);
    private sealed record DllStoragePaletteCandidate(DllBitmapResourceRecord Resource, IReadOnlyList<Color> Palette);
    private sealed record DllStoragePaletteReference(IReadOnlyList<Color> Palette, string SourceSummary);
    private sealed record DllPeSection(int VirtualAddress, int Size, int RawPointer);
}

public sealed class DllIconRasterPair : IDisposable
{
    public DllIconRasterPair(
        string sourceLabel,
        int sourceWidth,
        int sourceHeight,
        Bitmap smallBitmap,
        Bitmap largeBitmap,
        DllBitmapNormalizeInfo smallInfo,
        DllBitmapNormalizeInfo largeInfo)
    {
        SourceLabel = sourceLabel;
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        SmallBitmap = smallBitmap;
        LargeBitmap = largeBitmap;
        SmallInfo = smallInfo;
        LargeInfo = largeInfo;
    }

    public string SourceLabel { get; }
    public int SourceWidth { get; }
    public int SourceHeight { get; }
    public Bitmap SmallBitmap { get; }
    public Bitmap LargeBitmap { get; }
    public DllBitmapNormalizeInfo SmallInfo { get; }
    public DllBitmapNormalizeInfo LargeInfo { get; }

    public string Summary =>
        $"source={SourceWidth}x{SourceHeight}; small={SmallBitmap.Width}x{SmallBitmap.Height}; large={LargeBitmap.Width}x{LargeBitmap.Height}; " +
        $"largeCrop={LargeInfo.CropSummary}; keyPixels={LargeInfo.TransparentKeyPixels:N0}; exact={LargeInfo.ExactSizePreserved}; edgeTouch={LargeInfo.TouchesSourceEdge}";

    public void Dispose()
    {
        SmallBitmap.Dispose();
        LargeBitmap.Dispose();
    }
}

public sealed record DllBitmapNormalizeInfo(
    string Role,
    int Width,
    int Height,
    Rectangle? VisibleCrop,
    bool ExactSizePreserved,
    bool TouchesSourceEdge,
    int TransparentKeyPixels,
    int VisiblePixels)
{
    public string CropSummary => VisibleCrop.HasValue
        ? $"{VisibleCrop.Value.X},{VisibleCrop.Value.Y},{VisibleCrop.Value.Width}x{VisibleCrop.Value.Height}"
        : "empty";
}

public sealed record DllBitmapResourcePair(
    int IconIndex,
    int SmallId,
    int LargeId,
    IReadOnlyList<DllBitmapResourceRecord> SmallVariants,
    IReadOnlyList<DllBitmapResourceRecord> LargeVariants)
{
    public IReadOnlyList<DllBitmapResourceRecord> AllVariants =>
        SmallVariants.Concat(LargeVariants)
            .DistinctBy(resource => (resource.Id, resource.LanguageId))
            .ToArray();

    public IReadOnlyList<int> ResourceIds =>
        AllVariants.Select(resource => resource.Id)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

    public IReadOnlyList<string> VariantSummaries =>
        AllVariants.Select(DllBitmapIconCodecService.BuildVariantSummary).ToArray();
}

public sealed record DllBitmapResourceRecord(
    int Id,
    ushort LanguageId,
    byte[] DibBytes,
    int Width,
    int Height,
    int BitCount,
    int SizeBytes,
    int ColorUsed,
    string TransparencyMode);

public sealed record DllBitmapResourceUpdate(int ResourceId, ushort LanguageId, byte[] DibBytes);

public sealed record DllItemIconBmpImportClassification(
    bool PreserveStorage,
    string Mode,
    DllIconStoragePair? StoragePair,
    IReadOnlyList<string> Diagnostics);

public sealed record DllIconStoragePair(
    string SourceLabel,
    int SourceWidth,
    int SourceHeight,
    DllIconStorageImage Small,
    DllIconStorageImage Large,
    string Summary);

public sealed record DllIconStorageImage(
    string Role,
    int Width,
    int Height,
    byte[] DibBytes,
    IReadOnlyList<Color> Palette,
    int SignedHeight,
    int Stride);

public sealed record DllDecodedBitmap(Bitmap Bitmap, DllBitmapDibInfo Info, IReadOnlyList<string> Warnings) : IDisposable
{
    public void Dispose()
        => Bitmap.Dispose();
}

public sealed record DllBitmapDibInfo(
    int HeaderSize,
    int Width,
    int SignedHeight,
    int Height,
    int BitCount,
    int ColorUsed,
    int PaletteEntries,
    int PixelOffset,
    int Stride)
{
    public DllBitmapDibInfo()
        : this(0, 0, 0, 0, 0, 0, 0, 0, 0)
    {
    }
}

public enum DllBitmapDibRowOrder
{
    StandardBottomUp,
    CczTopFirst
}
