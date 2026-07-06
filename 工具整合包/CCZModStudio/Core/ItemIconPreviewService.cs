using CCZModStudio.Models;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CCZModStudio.Core;

public sealed class ItemIconPreviewService
{
    private readonly Dictionary<string, IReadOnlyList<DllBitmapResourceRecord>> _bitmapResourceCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly E5ImageReplaceService _e5ImageService = new();
    private readonly E5ImageRenderService _e5ImageRenderService = new();
    private readonly DllBitmapIconCodecService _dllCodec = new();
    private readonly ItemIconMappingService _iconMapping = new();

    public void ClearCache() => _bitmapResourceCache.Clear();

    public ItemIconPreviewResult BuildPreview(CczProject project, int iconIndex, int canvasSize = 96)
        => BuildPreview(project, iconIndex, Ccz66RevisedLayout.ResolveItemIconResourceFile(project), "物品图标", canvasSize);

    public ItemIconPreviewResult BuildPreview(
        CczProject project,
        int iconIndex,
        string resourceFileName,
        string displayName,
        int canvasSize = 96)
    {
        if (Ccz66RevisedLayout.IsE5IconResource(resourceFileName))
        {
            return BuildE5Preview(project, iconIndex, resourceFileName, displayName, canvasSize);
        }

        var sourcePath = ResolveIconDll(project, resourceFileName);
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return new ItemIconPreviewResult(
                sourcePath ?? Path.Combine(project.GameRoot, resourceFileName),
                iconIndex,
                0,
                null,
                $"未找到 {resourceFileName}，暂无法显示{displayName}。");
        }

        if (IsGameIconResourceFile(Path.GetFileName(resourceFileName)))
        {
            return BuildDllBitmapPreview(sourcePath, iconIndex, resourceFileName, displayName, canvasSize);
        }

        var extractIconCount = GetIconCount(sourcePath);
        if (extractIconCount > 0)
        {
            if (iconIndex < 0 || iconIndex >= extractIconCount)
            {
                return new ItemIconPreviewResult(
                    sourcePath,
                    iconIndex,
                    extractIconCount,
                    null,
                    $"{displayName}编号 {iconIndex} 超出 {resourceFileName} 可枚举范围 0-{extractIconCount - 1}。");
            }

            var iconBitmap = ExtractIconBitmap(sourcePath, iconIndex, canvasSize);
            var iconMessage = iconBitmap == null
                ? $"{displayName}编号 {iconIndex} 在 {resourceFileName} 中枚举到，但提取图像失败。"
                : $"来源 {resourceFileName}；字段图标={iconIndex}；可枚举图标={extractIconCount}。当前按 Windows 图标资源顺序预览，最终对应关系仍建议结合旧工具/实机确认。";
            return new ItemIconPreviewResult(sourcePath, iconIndex, extractIconCount, iconBitmap, iconMessage);
        }

        var bitmapResources = GetBitmapResources(sourcePath);
        var bitmapIconCount = EstimateBitmapIconCount(bitmapResources);
        if (bitmapIconCount <= 0)
        {
            return new ItemIconPreviewResult(
                sourcePath,
                iconIndex,
                0,
                null,
                $"{resourceFileName} 未能枚举到标准图标资源；也未解析到 RT_BITMAP 候选图标。");
        }

        if (iconIndex < 0 || iconIndex >= bitmapIconCount)
        {
            return new ItemIconPreviewResult(
                sourcePath,
                iconIndex,
                bitmapIconCount,
                null,
                $"{displayName}编号 {iconIndex} 超出 {resourceFileName} RT_BITMAP 候选范围 0-{bitmapIconCount - 1}。");
        }

        var resource = ResolveBitmapResource(bitmapResources, iconIndex);
        if (resource == null)
        {
            return new ItemIconPreviewResult(
                sourcePath,
                iconIndex,
                bitmapIconCount,
                null,
                $"{displayName}编号 {iconIndex} 没有匹配的 {resourceFileName} RT_BITMAP 候选资源。");
        }

        var bitmap = RenderDib(resource.DibBytes, canvasSize);
        var message = bitmap == null
            ? $"{displayName}编号 {iconIndex} 匹配到 {resourceFileName} RT_BITMAP 资源 ID={resource.Id}，但 DIB 转图像失败。"
            : $"来源 {resourceFileName}；字段图标={iconIndex}；RT_BITMAP 资源ID={resource.Id}；候选图标数={bitmapIconCount}。当前按资源ID成对规则预览，最终对应关系仍建议结合旧工具/实机确认。";
        return new ItemIconPreviewResult(sourcePath, iconIndex, bitmapIconCount, bitmap, message);
    }

    public int GetIconCount(CczProject project)
    {
        var sourcePath = Ccz66RevisedLayout.Is66(project)
            ? Ccz66RevisedLayout.ResolveResourcePath(project, Ccz66RevisedLayout.ResolveItemIconResourceFile(project))
            : ResolveItemIconDll(project);
        return string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)
            ? 0
            : Ccz66RevisedLayout.Is66(project)
                ? _e5ImageService.ReadIndex(sourcePath).Count
                : GetIconCount(sourcePath);
    }

    private ItemIconPreviewResult BuildDllBitmapPreview(
        string sourcePath,
        int iconIndex,
        string resourceFileName,
        string displayName,
        int canvasSize)
    {
        var bitmapResources = GetBitmapResources(sourcePath);
        var bitmapIconCount = EstimateBitmapIconCount(bitmapResources);
        if (bitmapIconCount <= 0)
        {
            return new ItemIconPreviewResult(
                sourcePath,
                iconIndex,
                0,
                null,
                $"{resourceFileName} 未解析到 RT_BITMAP 图标资源。",
                RenderMode: "DLL RT_BITMAP");
        }

        if (iconIndex < 0 || iconIndex >= bitmapIconCount)
        {
            return new ItemIconPreviewResult(
                sourcePath,
                iconIndex,
                bitmapIconCount,
                null,
                $"{displayName}编号 {iconIndex} 超出 {resourceFileName} RT_BITMAP 范围 0-{bitmapIconCount - 1}。",
                RenderMode: "DLL RT_BITMAP");
        }

        var pair = _dllCodec.ResolveBitmapResourcePair(bitmapResources, iconIndex);
        var smallResource = _dllCodec.SelectDisplayVariant(pair.SmallVariants);
        var largeResource = _dllCodec.SelectDisplayVariant(pair.LargeVariants);
        var nativeResource = largeResource ?? smallResource;
        if (nativeResource == null)
        {
            return new ItemIconPreviewResult(
                sourcePath,
                iconIndex,
                bitmapIconCount,
                null,
                $"{displayName}编号 {iconIndex} 没有匹配的 {resourceFileName} RT_BITMAP small/large 资源。",
                RenderMode: "DLL RT_BITMAP");
        }

        using var smallDecoded = smallResource == null ? null : DllBitmapIconCodecService.DecodeStorageDib(smallResource.DibBytes);
        using var largeDecoded = largeResource == null ? null : DllBitmapIconCodecService.DecodeStorageDib(largeResource.DibBytes);
        var smallRaw = smallDecoded?.Bitmap;
        var largeRaw = largeDecoded?.Bitmap;
        var displayRaw = largeRaw ?? smallRaw;
        var previewBitmap = displayRaw == null ? null : RenderBitmapToCanvas(displayRaw, canvasSize);
        var smallBitmap = smallRaw == null ? null : new Bitmap(smallRaw);
        var largeBitmap = largeRaw == null ? null : new Bitmap(largeRaw);
        var nativeBitmap = displayRaw == null ? null : new Bitmap(displayRaw);
        var smallVariant = smallResource == null ? null : ToVariantInfo(smallResource);
        var largeVariant = largeResource == null ? null : ToVariantInfo(largeResource);
        var variants = bitmapResources.Select(ToVariantInfo).ToArray();
        var warnings = pair.AllVariants
            .GroupBy(x => x.Id)
            .Where(group => group.Select(x => x.LanguageId).Distinct().Count() > 1 ||
                            group.Select(x => $"{x.Width}x{x.Height}/{x.BitCount}").Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group => $"RT_BITMAP ID={group.Key} has multiple language/format variants; re-import to normalize them.")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var message =
            $"来源 {resourceFileName}；字段图标={iconIndex}；RT_BITMAP small={smallResource?.Id.ToString() ?? "无"} large={largeResource?.Id.ToString() ?? "无"}；候选图标数={bitmapIconCount}。";

        return new ItemIconPreviewResult(
            sourcePath,
            iconIndex,
            bitmapIconCount,
            previewBitmap,
            message,
            NativeBitmap: nativeBitmap,
            SmallBitmap: smallBitmap,
            LargeBitmap: largeBitmap,
            ResourceVariants: variants,
            RenderMode: "DLL RT_BITMAP",
            SmallVariant: smallVariant,
            LargeVariant: largeVariant,
            SelectionMode: "RT_BITMAP small/large pair",
            Warnings: warnings);
    }

    private ItemIconPreviewResult BuildE5Preview(
        CczProject project,
        int iconIndex,
        string resourceFileName,
        string displayName,
        int canvasSize)
    {
        var sourcePath = Ccz66RevisedLayout.ResolveResourcePath(project, resourceFileName);
        if (!File.Exists(sourcePath))
        {
            return new ItemIconPreviewResult(
                sourcePath,
                iconIndex,
                0,
                null,
                $"未找到 {resourceFileName}，暂无法显示{displayName}。");
        }

        var entries = _e5ImageService.ReadIndex(sourcePath);
        if (entries.Count == 0)
        {
            return new ItemIconPreviewResult(
                sourcePath,
                iconIndex,
                0,
                null,
                $"{resourceFileName} 未识别到 E5 0x110 图片索引。");
        }

        var isItemE5 = Ccz66RevisedLayout.IsItemIconResource(sourcePath);
        var mapping = _iconMapping.Resolve(project, iconIndex, isItemE5 ? "item" : "strategy");
        var imageNumber = mapping.LargeImageNumber;
        if (imageNumber <= 0 || imageNumber > entries.Count)
        {
            var maxFieldValue = isItemE5
                ? Math.Max(0, (entries.Count - 2) / 2)
                : Math.Max(0, entries.Count - 1);
            var rangeMessage = $"{displayName} field value {iconIndex} is outside {resourceFileName} range 0-{maxFieldValue}; calculated E5 image #{imageNumber}.";
            if (rangeMessage.Length > 0)
            {
                return new ItemIconPreviewResult(
                    sourcePath,
                    iconIndex,
                    entries.Count,
                    null,
                    rangeMessage);
            }

            return new ItemIconPreviewResult(
                sourcePath,
                iconIndex,
                entries.Count,
                null,
                $"{displayName}字段编号 {iconIndex} 超出 {resourceFileName} E5 图号范围 0-{entries.Count - 1}。");
        }

        Bitmap? bitmap = null;
        Bitmap? smallBitmap = null;
        Bitmap? largeBitmap = null;
        IconResourceVariantInfo? smallVariant = null;
        IconResourceVariantInfo? largeVariant = null;
        try
        {
            if (isItemE5)
            {
                var smallNumber = mapping.SmallImageNumber ?? 0;
                var largeNumber = mapping.LargeImageNumber;
                smallBitmap = DecodeE5IconBitmap(sourcePath, smallNumber, out var smallBytes);
                largeBitmap = DecodeE5IconBitmap(sourcePath, largeNumber, out var largeBytes);
                smallVariant = smallBitmap == null ? null : new IconResourceVariantInfo
                {
                    ResourceId = smallNumber,
                    Width = smallBitmap.Width,
                    Height = smallBitmap.Height,
                    BitCount = Image.GetPixelFormatSize(smallBitmap.PixelFormat),
                    SizeBytes = smallBytes
                };
                largeVariant = largeBitmap == null ? null : new IconResourceVariantInfo
                {
                    ResourceId = largeNumber,
                    Width = largeBitmap.Width,
                    Height = largeBitmap.Height,
                    BitCount = Image.GetPixelFormatSize(largeBitmap.PixelFormat),
                    SizeBytes = largeBytes
                };
                bitmap = largeBitmap != null
                    ? _e5ImageRenderService.RenderToCanvas(largeBitmap, canvasSize, canvasSize)
                    : smallBitmap == null
                        ? null
                        : _e5ImageRenderService.RenderToCanvas(smallBitmap, canvasSize, canvasSize);
            }
            else
            {
                var bytes = _e5ImageService.ReadEntryBytes(sourcePath, imageNumber);
                using var decoded = _e5ImageRenderService.TryDecodeStandardImage(bytes);
                if (decoded != null)
                {
                    largeBitmap = new Bitmap(decoded);
                    largeVariant = new IconResourceVariantInfo
                    {
                        ResourceId = imageNumber,
                        Width = decoded.Width,
                        Height = decoded.Height,
                        BitCount = Image.GetPixelFormatSize(decoded.PixelFormat),
                        SizeBytes = bytes.Length
                    };
                    bitmap = _e5ImageRenderService.RenderToCanvas(decoded, canvasSize, canvasSize);
                }
                else
                {
                    bitmap = _e5ImageRenderService.RenderEntry(project, Path.GetFileName(sourcePath), bytes, canvasSize, canvasSize, out _);
                }
            }
        }
        catch
        {
            bitmap = null;
        }

        var warnings = new List<string>();
        var note = Path.GetFileName(sourcePath).Equals("Item.e5", StringComparison.OrdinalIgnoreCase)
            ? "6.6 修正版道具图标资源；宝物表“图标”字段 N 映射为小图 #2N+1、大图 #2N+2；#1/#2 是字段 0 的空白小/大图标，#3/#4 是字段 1。"
            : "6.6 修正版策略图标资源；策略图标字段 N 映射为 E5 图号 #N+1。";
        if (isItemE5)
        {
            var small = mapping.SmallImageNumber ?? 0;
            var large = mapping.LargeImageNumber;
            var smallKind = small > 0 && small <= entries.Count ? entries[small - 1].Kind : "missing";
            var largeKind = large > 0 && large <= entries.Count ? entries[large - 1].Kind : "missing";
            var smallText = smallVariant == null
                ? $"#{small} {smallKind} decode failed"
                : $"#{small} {smallVariant.Width}x{smallVariant.Height} {smallKind} {smallVariant.BitCount}bpp {smallVariant.SizeBytes} bytes";
            var largeText = largeVariant == null
                ? $"#{large} {largeKind} decode failed"
                : $"#{large} {largeVariant.Width}x{largeVariant.Height} {largeKind} {largeVariant.BitCount}bpp {largeVariant.SizeBytes} bytes";
            if (smallVariant == null ||
                smallVariant.Width != ItemIconRasterNormalizeService.SmallIconSize ||
                smallVariant.Height != ItemIconRasterNormalizeService.SmallIconSize)
            {
                warnings.Add($"尺寸异常，建议重新规范化导入: Item.e5 small #{small} is {smallText}; expected 16x16 BMP.");
            }

            if (largeVariant == null ||
                largeVariant.Width != ItemIconRasterNormalizeService.LargeIconSize ||
                largeVariant.Height != ItemIconRasterNormalizeService.LargeIconSize)
            {
                warnings.Add($"尺寸异常，建议重新规范化导入: Item.e5 large #{large} is {largeText}; expected 32x32 BMP.");
            }

            note = $"6.6 revised Item.e5 item icon: table field 图标={iconIndex}, small=#{small}, large=#{large}; current small={smallText}; current large={largeText}; treasure/item preview uses the large image by default.";
        }
        else
        {
            note = "6.6 revised Mtem.e5 strategy icon: table field value N maps to E5 image #(N+1); strategy families are usually spaced by 6.";
        }

        var previewMessage = $"Source {Path.GetFileName(sourcePath)}; table field={iconIndex}; preview E5 image #{imageNumber}; available={entries.Count}. {note}";
        if (previewMessage.Length > 0)
        {
            return new ItemIconPreviewResult(
                sourcePath,
                iconIndex,
                entries.Count,
                bitmap,
                previewMessage,
                NativeBitmap: largeBitmap != null ? new Bitmap(largeBitmap) : smallBitmap == null ? null : new Bitmap(smallBitmap),
                SmallBitmap: smallBitmap,
                LargeBitmap: largeBitmap,
                ResourceVariants: new[] { smallVariant, largeVariant }.Where(x => x != null).Cast<IconResourceVariantInfo>().ToArray(),
                RenderMode: isItemE5 ? "E5 item small/large" : "E5 icon",
                SmallVariant: smallVariant,
                LargeVariant: largeVariant,
                SelectionMode: isItemE5 ? "E5 small/large pair" : "E5 single image",
                Warnings: warnings.ToArray());
        }

        return new ItemIconPreviewResult(
            sourcePath,
            iconIndex,
            entries.Count,
            bitmap,
            $"来源 {Path.GetFileName(sourcePath)}；字段图标={iconIndex}；E5图号=#{imageNumber}；候选图标数={entries.Count}。{note}");
    }

    private Bitmap? DecodeE5IconBitmap(string sourcePath, int imageNumber, out int byteCount)
    {
        var bytes = _e5ImageService.ReadEntryBytes(sourcePath, imageNumber);
        byteCount = bytes.Length;
        return _e5ImageRenderService.TryDecodeStandardImage(bytes);
    }

    private static string ResolveItemIconDll(CczProject project)
        => ResolveIconDll(project, "Itemicon.dll");

    private static bool IsGameIconResourceFile(string resourceFileName)
    {
        var name = Path.GetFileName(resourceFileName);
        return name.Equals("Itemicon.dll", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Mgcicon.dll", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Cmdicon.dll", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveIconDll(CczProject project, string resourceFileName)
    {
        var candidates = new[]
        {
            Path.Combine(project.GameRoot, resourceFileName),
            Path.Combine(project.WorkspaceRoot, resourceFileName)
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static int GetIconCount(string sourcePath)
    {
        try
        {
            return checked((int)ExtractIconEx(sourcePath, -1, null, null, 0));
        }
        catch
        {
            return 0;
        }
    }

    private static Bitmap? ExtractIconBitmap(string sourcePath, int iconIndex, int canvasSize)
    {
        var large = new IntPtr[1];
        var small = new IntPtr[1];
        try
        {
            var extracted = ExtractIconEx(sourcePath, iconIndex, large, small, 1);
            if (extracted == 0) return null;

            var handle = large[0] != IntPtr.Zero ? large[0] : small[0];
            if (handle == IntPtr.Zero) return null;

            using var icon = (Icon)Icon.FromHandle(handle).Clone();
            using var raw = icon.ToBitmap();
            var targetSize = Math.Max(32, canvasSize);
            var canvas = new Bitmap(targetSize, targetSize);
            using var g = Graphics.FromImage(canvas);
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.CompositingQuality = CompositingQuality.HighSpeed;
            var scale = Math.Min((targetSize - 12) / (float)raw.Width, (targetSize - 12) / (float)raw.Height);
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0) scale = 1;
            var width = Math.Max(1, (int)Math.Round(raw.Width * scale));
            var height = Math.Max(1, (int)Math.Round(raw.Height * scale));
            var x = (targetSize - width) / 2;
            var y = (targetSize - height) / 2;
            g.DrawImage(raw, new Rectangle(x, y, width, height));
            return canvas;
        }
        finally
        {
            if (large[0] != IntPtr.Zero) DestroyIcon(large[0]);
            if (small[0] != IntPtr.Zero && small[0] != large[0]) DestroyIcon(small[0]);
        }
    }

    private IReadOnlyList<DllBitmapResourceRecord> GetBitmapResources(string sourcePath)
    {
        if (_bitmapResourceCache.TryGetValue(sourcePath, out var cached)) return cached;
        var parsed = _dllCodec.ParseBitmapResources(sourcePath);
        _bitmapResourceCache[sourcePath] = parsed;
        return parsed;
    }

    private int EstimateBitmapIconCount(IReadOnlyList<DllBitmapResourceRecord> resources)
        => _dllCodec.EstimateBitmapIconCount(resources);

    private DllBitmapResourceRecord? ResolveBitmapResource(IReadOnlyList<DllBitmapResourceRecord> resources, int iconIndex)
    {
        if (resources.Count == 0) return null;
        var minId = resources.Min(x => x.Id);
        if (minId >= 100)
        {
            var preferredLargeId = minId + iconIndex * 2 + 1;
            var preferredSmallId = minId + iconIndex * 2;
            return _dllCodec.SelectDisplayVariant(resources.Where(x => x.Id == preferredLargeId))
                   ?? _dllCodec.SelectDisplayVariant(resources.Where(x => x.Id == preferredSmallId));
        }

        var id = resources.Select(x => x.Id).Distinct().OrderBy(x => x).ElementAtOrDefault(iconIndex);
        return id == 0 && iconIndex >= resources.Select(x => x.Id).Distinct().Count()
            ? null
            : _dllCodec.SelectDisplayVariant(resources.Where(x => x.Id == id));
    }

    private static IconResourceVariantInfo ToVariantInfo(DllBitmapResourceRecord resource)
        => new()
        {
            ResourceId = resource.Id,
            LanguageId = resource.LanguageId,
            Width = resource.Width,
            Height = resource.Height,
            BitCount = resource.BitCount,
            SizeBytes = resource.SizeBytes
        };

    internal static Bitmap? RenderDibForSmoke(byte[] dibBytes, int canvasSize)
        => RenderDib(dibBytes, canvasSize);

    private static Bitmap? RenderDib(byte[] dibBytes, int canvasSize)
    {
        if (dibBytes.Length < 40) return null;
        var decoded = DllBitmapIconCodecService.DecodeStorageDib(dibBytes);
        if (decoded != null)
        {
            using (decoded)
            {
                return RenderBitmapToCanvas(decoded.Bitmap, canvasSize);
            }
        }

        var dibHeaderSize = BitConverter.ToInt32(dibBytes, 0);
        if (dibHeaderSize <= 0 || dibHeaderSize > dibBytes.Length) return null;
        var bitCount = BitConverter.ToUInt16(dibBytes, 14);
        var compression = dibBytes.Length >= 20 ? BitConverter.ToInt32(dibBytes, 16) : 0;
        var colorUsed = dibBytes.Length >= 36 ? BitConverter.ToInt32(dibBytes, 32) : 0;
        var paletteEntries = bitCount <= 8
            ? (colorUsed > 0 ? colorUsed : 1 << bitCount)
            : 0;
        var masksBytes = dibHeaderSize == 40 && compression == 3 ? 12 : 0;
        var pixelOffset = 14 + dibHeaderSize + masksBytes + paletteEntries * 4;
        var bmpBytes = new byte[14 + dibBytes.Length];
        bmpBytes[0] = (byte)'B';
        bmpBytes[1] = (byte)'M';
        BitConverter.GetBytes(bmpBytes.Length).CopyTo(bmpBytes, 2);
        BitConverter.GetBytes(pixelOffset).CopyTo(bmpBytes, 10);
        Buffer.BlockCopy(dibBytes, 0, bmpBytes, 14, dibBytes.Length);

        try
        {
            using var stream = new MemoryStream(bmpBytes);
            using var raw = new Bitmap(stream);
            return RenderBitmapToCanvas(raw, canvasSize);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? DecodeBitmapDib(byte[] dibBytes, CczBitmapDibRowOrder rowOrder)
    {
        var headerSize = BitConverter.ToInt32(dibBytes, 0);
        if (headerSize != 40 || dibBytes.Length < headerSize) return null;

        var width = BitConverter.ToInt32(dibBytes, 4);
        var signedHeight = BitConverter.ToInt32(dibBytes, 8);
        var height = Math.Abs(signedHeight);
        var planes = BitConverter.ToUInt16(dibBytes, 12);
        var bitCount = BitConverter.ToUInt16(dibBytes, 14);
        var compression = BitConverter.ToInt32(dibBytes, 16);
        var colorUsed = BitConverter.ToInt32(dibBytes, 32);
        if (width <= 0 || height <= 0 || planes != 1 || compression != 0 || bitCount is not (4 or 8 or 24 or 32))
        {
            return null;
        }

        var paletteEntries = bitCount <= 8 ? (colorUsed > 0 ? colorUsed : 1 << bitCount) : 0;
        var pixelOffset = headerSize + paletteEntries * 4;
        var stride = ((width * bitCount + 31) / 32) * 4;
        if (stride <= 0 || pixelOffset < 0 || pixelOffset + stride * height > dibBytes.Length)
        {
            return null;
        }

        var palette = new Color[paletteEntries];
        for (var i = 0; i < paletteEntries; i++)
        {
            var offset = headerSize + i * 4;
            palette[i] = Color.FromArgb(255, dibBytes[offset + 2], dibBytes[offset + 1], dibBytes[offset]);
        }

        var effectiveRowOrder = ResolveDibRowOrder(signedHeight, bitCount, rowOrder);
        var bitmap = new Bitmap(width, height);
        for (var y = 0; y < height; y++)
        {
            var sourceY = effectiveRowOrder == CczBitmapDibRowOrder.StandardBottomUp && signedHeight > 0
                ? height - 1 - y
                : y;
            var rowOffset = pixelOffset + sourceY * stride;
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, ReadGameDibPixel(dibBytes, rowOffset, x, bitCount, palette));
            }
        }

        return bitmap;
    }

    private static CczBitmapDibRowOrder ResolveDibRowOrder(int signedHeight, int bitCount, CczBitmapDibRowOrder requested)
    {
        if (requested != CczBitmapDibRowOrder.Auto) return requested;
        if (signedHeight < 0) return CczBitmapDibRowOrder.CczTopFirst;

        // Legacy 6.5 DLL icon resources are positive-height 8bpp DIBs in normal BMP
        // bottom-up order. CCZModStudio writes replacement icons as positive-height
        // 32bpp top-first DIBs because the game reads those RT_BITMAP rows top-first.
        return bitCount == 32
            ? CczBitmapDibRowOrder.CczTopFirst
            : CczBitmapDibRowOrder.StandardBottomUp;
    }

    private static Color ReadGameDibPixel(byte[] bytes, int rowOffset, int x, int bitCount, IReadOnlyList<Color> palette)
    {
        return bitCount switch
        {
            4 => palette[(bytes[rowOffset + x / 2] >> (x % 2 == 0 ? 4 : 0)) & 0x0F],
            8 => palette[bytes[rowOffset + x]],
            24 => Color.FromArgb(255, bytes[rowOffset + x * 3 + 2], bytes[rowOffset + x * 3 + 1], bytes[rowOffset + x * 3]),
            32 => Color.FromArgb(bytes[rowOffset + x * 4 + 3], bytes[rowOffset + x * 4 + 2], bytes[rowOffset + x * 4 + 1], bytes[rowOffset + x * 4]),
            _ => Color.Transparent
        };
    }

    private static Bitmap RenderBitmapToCanvas(Bitmap raw, int canvasSize)
        => DllBitmapIconCodecService.RenderPixelPerfectPreview(raw, Math.Max(32, canvasSize));

    private static IReadOnlyList<BitmapResource> ParseBitmapResources(string sourcePath)
    {
        try
        {
            var data = File.ReadAllBytes(sourcePath);
            if (data.Length < 0x40 || data[0] != 'M' || data[1] != 'Z') return Array.Empty<BitmapResource>();
            var peOffset = BitConverter.ToInt32(data, 0x3C);
            if (peOffset <= 0 || peOffset + 248 >= data.Length) return Array.Empty<BitmapResource>();
            var sectionCount = BitConverter.ToUInt16(data, peOffset + 6);
            var optionalHeaderSize = BitConverter.ToUInt16(data, peOffset + 20);
            var optionalHeaderOffset = peOffset + 24;
            var resourceRva = BitConverter.ToInt32(data, optionalHeaderOffset + 96 + 2 * 8);
            if (resourceRva <= 0) return Array.Empty<BitmapResource>();
            var sectionOffset = optionalHeaderOffset + optionalHeaderSize;
            var sections = new List<PeSection>();
            for (var i = 0; i < sectionCount; i++)
            {
                var offset = sectionOffset + i * 40;
                if (offset + 40 > data.Length) break;
                sections.Add(new PeSection(
                    BitConverter.ToInt32(data, offset + 12),
                    Math.Max(BitConverter.ToInt32(data, offset + 8), BitConverter.ToInt32(data, offset + 16)),
                    BitConverter.ToInt32(data, offset + 20)));
            }

            var resourceBaseOffset = RvaToFileOffset(resourceRva, sections);
            if (resourceBaseOffset < 0 || resourceBaseOffset + 16 > data.Length) return Array.Empty<BitmapResource>();
            var result = new List<BitmapResource>();
            ReadResourceDirectory(data, sections, resourceBaseOffset, resourceBaseOffset, 0, new List<int>(), result);
            return result
                .OrderBy(x => x.Id)
                .ThenByDescending(x => x.BitCount)
                .ThenByDescending(x => x.SizeBytes)
                .ToList();
        }
        catch
        {
            return Array.Empty<BitmapResource>();
        }
    }

    private static void ReadResourceDirectory(
        byte[] data,
        IReadOnlyList<PeSection> sections,
        int resourceBaseOffset,
        int directoryOffset,
        int level,
        List<int> path,
        List<BitmapResource> output)
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

            if (path.Count < 2 || path[0] != 2) continue; // RT_BITMAP
            var dataEntryOffset = resourceBaseOffset + valueOffset;
            if (dataEntryOffset + 16 > data.Length) continue;
            var dataRva = BitConverter.ToInt32(data, dataEntryOffset);
            var size = BitConverter.ToInt32(data, dataEntryOffset + 4);
            var fileOffset = RvaToFileOffset(dataRva, sections);
            if (fileOffset < 0 || size <= 0 || fileOffset + size > data.Length) continue;
            var bytes = new byte[size];
            Buffer.BlockCopy(data, fileOffset, bytes, 0, size);
            if (!TryReadBitmapDibInfo(bytes, out var width, out var height, out var bitCount)) continue;
            output.Add(new BitmapResource(path[1], bytes, width, height, bitCount, size));
        }
    }

    private static bool TryReadBitmapDibInfo(byte[] bytes, out int width, out int height, out int bitCount)
    {
        width = 0;
        height = 0;
        bitCount = 0;
        if (bytes.Length < 16) return false;
        var headerSize = BitConverter.ToInt32(bytes, 0);
        if (headerSize == 12)
        {
            width = BitConverter.ToUInt16(bytes, 4);
            height = Math.Abs(BitConverter.ToInt16(bytes, 6));
            bitCount = BitConverter.ToUInt16(bytes, 10);
            return width > 0 && height > 0 && bitCount is 1 or 4 or 8 or 16 or 24 or 32;
        }

        if (bytes.Length < 40 || headerSize is not (40 or 108 or 124)) return false;
        width = BitConverter.ToInt32(bytes, 4);
        height = Math.Abs(BitConverter.ToInt32(bytes, 8));
        var planes = BitConverter.ToUInt16(bytes, 12);
        bitCount = BitConverter.ToUInt16(bytes, 14);
        return width > 0 &&
               height > 0 &&
               planes == 1 &&
               bitCount is 1 or 4 or 8 or 16 or 24 or 32;
    }

    private static int RvaToFileOffset(int rva, IReadOnlyList<PeSection> sections)
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string szFileName, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

internal sealed record PeSection(int VirtualAddress, int Size, int RawPointer);

internal sealed record BitmapResource(
    int Id,
    byte[] DibBytes,
    int Width,
    int Height,
    int BitCount,
    int SizeBytes);

internal enum CczBitmapDibRowOrder
{
    Auto,
    StandardBottomUp,
    CczTopFirst
}

public sealed record ItemIconPreviewResult(
    string SourcePath,
    int IconIndex,
    int AvailableIconCount,
    Bitmap? Bitmap,
    string Message,
    Bitmap? NativeBitmap = null,
    Bitmap? SmallBitmap = null,
    Bitmap? LargeBitmap = null,
    IReadOnlyList<IconResourceVariantInfo>? ResourceVariants = null,
    string RenderMode = "",
    IconResourceVariantInfo? SmallVariant = null,
    IconResourceVariantInfo? LargeVariant = null,
    string SelectionMode = "",
    IReadOnlyList<string>? Warnings = null);

public sealed class IconResourceVariantInfo
{
    public int ResourceId { get; init; }
    public ushort LanguageId { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int BitCount { get; init; }
    public int SizeBytes { get; init; }

    public string DisplayLabel =>
        $"ID={ResourceId} Lang={LanguageId} {Width}x{Height} {BitCount}bpp";
}
