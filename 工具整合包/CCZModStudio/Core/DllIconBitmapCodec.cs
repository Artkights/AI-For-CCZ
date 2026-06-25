using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace CCZModStudio.Core;

public enum CczBitmapDibRowOrder
{
    Auto,
    StandardBottomUp,
    CczTopFirst
}

public sealed class DllIconBitmapCodec
{
    private const int RtBitmap = 2;
    private const int LoadLibraryAsDatafile = 0x00000002;
    private static readonly Color TransparentBlack = Color.FromArgb(0, 0, 0, 0);

    public static bool IsGameIconResourceFile(string fileName)
        => fileName.Equals("Itemicon.dll", StringComparison.OrdinalIgnoreCase) ||
           fileName.Equals("Mgcicon.dll", StringComparison.OrdinalIgnoreCase) ||
           fileName.Equals("Cmdicon.dll", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<DllIconBitmapResource> ReadBitmapResources(string sourcePath)
    {
        try
        {
            var data = File.ReadAllBytes(sourcePath);
            if (data.Length < 0x40 || data[0] != 'M' || data[1] != 'Z') return Array.Empty<DllIconBitmapResource>();
            var peOffset = BitConverter.ToInt32(data, 0x3C);
            if (peOffset <= 0 || peOffset + 248 >= data.Length) return Array.Empty<DllIconBitmapResource>();

            var sectionCount = BitConverter.ToUInt16(data, peOffset + 6);
            var optionalHeaderSize = BitConverter.ToUInt16(data, peOffset + 20);
            var optionalHeaderOffset = peOffset + 24;
            var magic = BitConverter.ToUInt16(data, optionalHeaderOffset);
            var dataDirectoryOffset = magic == 0x20B ? optionalHeaderOffset + 112 : optionalHeaderOffset + 96;
            if (dataDirectoryOffset + 2 * 8 + 8 > data.Length) return Array.Empty<DllIconBitmapResource>();

            var resourceRva = BitConverter.ToInt32(data, dataDirectoryOffset + 2 * 8);
            if (resourceRva <= 0) return Array.Empty<DllIconBitmapResource>();

            var sectionOffset = optionalHeaderOffset + optionalHeaderSize;
            var sections = new List<DllIconPeSection>();
            for (var i = 0; i < sectionCount; i++)
            {
                var offset = sectionOffset + i * 40;
                if (offset + 40 > data.Length) break;
                sections.Add(new DllIconPeSection(
                    BitConverter.ToInt32(data, offset + 12),
                    Math.Max(BitConverter.ToInt32(data, offset + 8), BitConverter.ToInt32(data, offset + 16)),
                    BitConverter.ToInt32(data, offset + 20)));
            }

            var resourceBaseOffset = RvaToFileOffset(resourceRva, sections);
            if (resourceBaseOffset < 0 || resourceBaseOffset + 16 > data.Length) return Array.Empty<DllIconBitmapResource>();

            var result = new List<DllIconBitmapResource>();
            ReadResourceDirectory(data, sections, resourceBaseOffset, resourceBaseOffset, 0, [], result);
            return result
                .Where(x => x.Width > 0 && x.Height > 0)
                .OrderBy(x => x.Id)
                .ThenBy(x => x.LanguageId)
                .ThenBy(x => x.BitCount)
                .ToArray();
        }
        catch
        {
            return Array.Empty<DllIconBitmapResource>();
        }
    }

    public int EstimateIconCount(IReadOnlyList<DllIconBitmapResource> resources)
    {
        if (resources.Count == 0) return 0;
        var minId = resources.Min(x => x.Id);
        var maxId = resources.Max(x => x.Id);
        var distinctIds = resources.Select(x => x.Id).Distinct().OrderBy(x => x).ToArray();
        if (minId >= 100 && distinctIds.Length >= 2)
        {
            return ((maxId - minId) / 2) + 1;
        }

        return distinctIds.Length;
    }

    public IReadOnlyList<DllIconBitmapResource> ResolveIconResources(IReadOnlyList<DllIconBitmapResource> resources, int iconIndex)
    {
        if (resources.Count == 0) throw new InvalidOperationException("目标 DLL 中没有解析到 RT_BITMAP 图标资源。");
        if (iconIndex < 0) throw new InvalidOperationException("图标编号不能小于 0。");

        var minId = resources.Min(x => x.Id);
        if (minId >= 100)
        {
            var smallId = minId + iconIndex * 2;
            var largeId = minId + iconIndex * 2 + 1;
            var pair = resources
                .Where(x => x.Id == smallId || x.Id == largeId)
                .OrderBy(x => x.Id)
                .ThenByDescending(LanguagePriority)
                .ThenBy(x => x.BitCount)
                .ToArray();
            if (pair.Length == 0)
            {
                throw new InvalidOperationException($"图标编号 {iconIndex} 没有匹配 RT_BITMAP ID={smallId}/{largeId}。");
            }

            return pair;
        }

        var resourceIds = resources.Select(x => x.Id).Distinct().OrderBy(x => x).ToArray();
        if (iconIndex >= resourceIds.Length)
        {
            throw new InvalidOperationException($"图标编号 {iconIndex} 超出 DLL 位图资源范围 0-{resourceIds.Length - 1}。");
        }

        var selectedId = resourceIds[iconIndex];
        return resources
            .Where(x => x.Id == selectedId)
            .OrderByDescending(LanguagePriority)
            .ThenBy(x => x.BitCount)
            .ToArray();
    }

    public DllIconGameSlot ResolveGameIconSlot(string sourcePath, IReadOnlyList<DllIconBitmapResource> resources, int iconIndex, string resourceLabel = "")
    {
        if (resources.Count == 0) throw new InvalidOperationException("目标 DLL 中没有解析到 RT_BITMAP 图标资源。");
        if (iconIndex < 0) throw new InvalidOperationException("图标编号不能小于 0。");

        var minId = resources.Min(x => x.Id);
        int smallId;
        int largeId;
        IReadOnlyList<DllIconBitmapResource> variants;
        if (minId >= 100)
        {
            smallId = checked(minId + iconIndex * 2);
            largeId = checked(smallId + 1);
            variants = resources
                .Where(x => x.Id == smallId || x.Id == largeId)
                .OrderBy(x => x.Id)
                .ThenBy(x => x.LanguageId)
                .ThenByDescending(x => x.BitCount)
                .ToArray();
            if (variants.Count == 0)
            {
                throw new InvalidOperationException($"图标编号 {iconIndex} 没有匹配 RT_BITMAP ID={smallId}/{largeId}。");
            }
        }
        else
        {
            var resourceIds = resources.Select(x => x.Id).Distinct().OrderBy(x => x).ToArray();
            if (iconIndex >= resourceIds.Length)
            {
                throw new InvalidOperationException($"图标编号 {iconIndex} 超出 DLL 位图资源范围 0-{resourceIds.Length - 1}。");
            }

            smallId = resourceIds[iconIndex];
            largeId = smallId;
            variants = resources
                .Where(x => x.Id == smallId)
                .OrderBy(x => x.LanguageId)
                .ThenByDescending(x => x.BitCount)
                .ToArray();
        }

        var warnings = new List<string>();
        var smallVariants = variants.Where(x => x.Id == smallId).ToArray();
        var largeVariants = variants.Where(x => x.Id == largeId).ToArray();
        var small = SelectGameVariant(sourcePath, smallVariants, out var smallProbe, warnings);
        var largeProbe = smallProbe;
        var large = largeId == smallId
            ? small
            : SelectGameVariant(sourcePath, largeVariants, out largeProbe, warnings);
        var editable = large ?? small ?? SelectEditableVariant(variants)
                       ?? throw new InvalidOperationException($"图标编号 {iconIndex} 没有可编辑的 RT_BITMAP 资源。");
        var writeTargets = variants
            .OrderBy(x => x.Id)
            .ThenBy(x => x.LanguageId)
            .ThenBy(x => x.BitCount)
            .ToArray();
        var label = string.IsNullOrWhiteSpace(resourceLabel) ? Path.GetFileName(sourcePath) : resourceLabel;
        var selectionMode = (smallProbe || largeProbe) ? "Win32 FindResource" : "Fallback";

        return new DllIconGameSlot(
            sourcePath,
            label,
            iconIndex,
            smallId,
            largeId,
            variants,
            small,
            large,
            editable,
            writeTargets,
            smallProbe,
            largeProbe,
            selectionMode,
            warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    public DllIconBitmapResource? SelectEditableVariant(IEnumerable<DllIconBitmapResource> resources)
        => resources
            .OrderByDescending(x => x.Width * x.Height)
            .ThenByDescending(x => x.LanguageId == 0)
            .ThenByDescending(LanguagePriority)
            .ThenByDescending(x => x.BitCount)
            .FirstOrDefault();

    public DllIconBitmapResource? SelectLargeVariant(IEnumerable<DllIconBitmapResource> resources)
        => SelectEditableVariant(resources);

    public DllIconBitmapResource? SelectSmallVariant(IEnumerable<DllIconBitmapResource> resources)
        => resources
            .OrderBy(x => x.Width * x.Height)
            .ThenByDescending(x => x.LanguageId == 0)
            .ThenByDescending(LanguagePriority)
            .ThenByDescending(x => x.BitCount)
            .FirstOrDefault();

    public bool TryReadWin32SelectedBitmapResource(string sourcePath, int resourceId, out byte[] dibBytes, out string warning)
    {
        dibBytes = Array.Empty<byte>();
        warning = string.Empty;
        var module = IntPtr.Zero;
        try
        {
            module = LoadLibraryEx(sourcePath, IntPtr.Zero, LoadLibraryAsDatafile);
            if (module == IntPtr.Zero)
            {
                warning = $"LoadLibraryEx 失败，Win32Error={Marshal.GetLastWin32Error()}。";
                return false;
            }

            var resourceInfo = FindResource(module, (IntPtr)resourceId, (IntPtr)RtBitmap);
            if (resourceInfo == IntPtr.Zero)
            {
                warning = $"FindResource RT_BITMAP ID={resourceId} 失败，Win32Error={Marshal.GetLastWin32Error()}。";
                return false;
            }

            var size = SizeofResource(module, resourceInfo);
            if (size == 0)
            {
                warning = $"SizeofResource RT_BITMAP ID={resourceId} 返回 0，Win32Error={Marshal.GetLastWin32Error()}。";
                return false;
            }

            var resourceHandle = LoadResource(module, resourceInfo);
            if (resourceHandle == IntPtr.Zero)
            {
                warning = $"LoadResource RT_BITMAP ID={resourceId} 失败，Win32Error={Marshal.GetLastWin32Error()}。";
                return false;
            }

            var resourcePointer = LockResource(resourceHandle);
            if (resourcePointer == IntPtr.Zero)
            {
                warning = $"LockResource RT_BITMAP ID={resourceId} 失败。";
                return false;
            }

            dibBytes = new byte[checked((int)size)];
            Marshal.Copy(resourcePointer, dibBytes, 0, dibBytes.Length);
            return true;
        }
        catch (Exception ex)
        {
            warning = ex.Message;
            return false;
        }
        finally
        {
            if (module != IntPtr.Zero) FreeLibrary(module);
        }
    }

    public Bitmap? DecodeDib(byte[] dibBytes)
    {
        var gameBitmap = DecodeGameBitmapDib(dibBytes, CczBitmapDibRowOrder.Auto);
        if (gameBitmap != null) return gameBitmap;

        var dibHeaderSize = BitConverter.ToInt32(dibBytes, 0);
        if (dibHeaderSize <= 0 || dibHeaderSize > dibBytes.Length) return null;
        var bitCount = dibBytes.Length >= 16 ? BitConverter.ToUInt16(dibBytes, 14) : 0;
        var compression = dibBytes.Length >= 20 ? BitConverter.ToInt32(dibBytes, 16) : 0;
        var colorUsed = dibBytes.Length >= 36 ? BitConverter.ToInt32(dibBytes, 32) : 0;
        var paletteEntries = bitCount <= 8 ? (colorUsed > 0 ? colorUsed : 1 << bitCount) : 0;
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
            return CloneArgb(raw);
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<DllIconBitmapUpdate> BuildUpdatesFromImage(string sourcePath, IReadOnlyList<DllIconBitmapResource> targets)
    {
        using var source = Image.FromFile(sourcePath);
        return BuildUpdatesFromImage(source, targets, useCornerBackgroundKey: true);
    }

    public IReadOnlyList<DllIconBitmapUpdate> BuildUpdatesFromImage(Image source, IReadOnlyList<DllIconBitmapResource> targets)
        => BuildUpdatesFromImage(source, targets, useCornerBackgroundKey: false);

    private IReadOnlyList<DllIconBitmapUpdate> BuildUpdatesFromImage(
        Image source,
        IReadOnlyList<DllIconBitmapResource> targets,
        bool useCornerBackgroundKey)
    {
        if (targets.Count == 0) return Array.Empty<DllIconBitmapUpdate>();
        var large = SelectLargeVariant(targets) ?? targets.OrderByDescending(x => x.Width * x.Height).First();
        var small = SelectSmallVariant(targets) ?? large;
        using var prepared = PrepareIconSource(
            source,
            new Size(Math.Max(1, large.Width), Math.Max(1, large.Height)),
            new Size(Math.Max(1, small.Width), Math.Max(1, small.Height)),
            new IconSourcePrepareOptions(
                UseCornerBackgroundKey: useCornerBackgroundKey,
                CropToOpaqueBounds: useCornerBackgroundKey));
        return BuildUpdatesFromPreparedIcon(prepared, targets);
    }

    public DllIconBitmapUpdate BuildTransparentUpdate(DllIconBitmapResource target)
    {
        using var bitmap = new Bitmap(Math.Max(1, target.Width), Math.Max(1, target.Height), PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(TransparentBlack);
        }

        return BuildUpdateFromPreparedBitmap(
            bitmap,
            target,
            new IconSourcePrepareMetadata(
                Rectangle.Empty,
                bitmap.Width,
                bitmap.Height,
                bitmap.Width * bitmap.Height,
                0,
                0,
                ComputePixelHash(bitmap),
                Array.Empty<string>()));
    }

    public Bitmap BuildBitmapForTargetSize(Image source, int targetWidth, int targetHeight)
    {
        using var prepared = PrepareIconSource(
            source,
            new Size(Math.Max(1, targetWidth), Math.Max(1, targetHeight)),
            new Size(Math.Max(1, targetWidth), Math.Max(1, targetHeight)),
            new IconSourcePrepareOptions(UseCornerBackgroundKey: true));
        return new Bitmap(prepared.LargeBitmap);
    }

    public IconSourcePrepareResult PrepareIconSource(
        Image source,
        Size largeSize,
        Size smallSize,
        IconSourcePrepareOptions? options = null)
    {
        options ??= new IconSourcePrepareOptions();
        using var normalized = NormalizeIconSource(source, options.UseCornerBackgroundKey);
        var warnings = new List<string>(normalized.Warnings);
        var opaqueBounds = FindOpaqueBounds(normalized.Bitmap);
        var largeWidth = Math.Max(1, largeSize.Width);
        var largeHeight = Math.Max(1, largeSize.Height);
        var smallWidth = Math.Max(1, smallSize.Width);
        var smallHeight = Math.Max(1, smallSize.Height);

        Bitmap cropped;
        if (opaqueBounds.Width <= 0 || opaqueBounds.Height <= 0)
        {
            warnings.Add("Icon source had no visible pixels after transparency-key cleanup.");
            cropped = new Bitmap(largeWidth, largeHeight, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(cropped);
            g.Clear(TransparentBlack);
        }
        else
        {
            if (options.CropToOpaqueBounds)
            {
                cropped = CropBitmap(normalized.Bitmap, opaqueBounds);
            }
            else
            {
                cropped = new Bitmap(normalized.Bitmap);
            }

            if (options.CropToOpaqueBounds &&
                (opaqueBounds.Width != normalized.Bitmap.Width || opaqueBounds.Height != normalized.Bitmap.Height))
            {
                warnings.Add($"Icon source was cropped to visible bounds {opaqueBounds.X},{opaqueBounds.Y},{opaqueBounds.Width}x{opaqueBounds.Height} before scaling.");
            }
        }

        using (cropped)
        {
            var largeBitmap = options.CropToOpaqueBounds
                ? RenderPreparedIconCanvas(cropped, largeWidth, largeHeight, options.Padding)
                : ScaleToFit(cropped, largeWidth, largeHeight);
            var smallBitmap = ScaleToFit(largeBitmap, smallWidth, smallHeight);
            return new IconSourcePrepareResult(
                largeBitmap,
                smallBitmap,
                opaqueBounds,
                source.Width,
                source.Height,
                normalized.TransparentPixelCount,
                normalized.MagentaKeyPixelCount,
                normalized.CornerBackgroundPixelCount,
                ComputePixelHash(largeBitmap),
                warnings.Distinct(StringComparer.Ordinal).ToArray());
        }
    }

    public IReadOnlyList<DllIconBitmapUpdate> BuildUpdatesFromPreparedIcon(
        IconSourcePrepareResult prepared,
        IReadOnlyList<DllIconBitmapResource> targets)
        => targets.Select(target =>
        {
            Bitmap? scaled = null;
            var bitmap = SelectPreparedBitmapForTarget(prepared, target, out scaled);
            try
            {
                return BuildUpdateFromPreparedBitmap(bitmap, target, prepared.ToMetadata());
            }
            finally
            {
                scaled?.Dispose();
            }
        }).ToArray();

    private DllIconBitmapUpdate BuildUpdateFromPreparedBitmap(
        Bitmap bitmap,
        DllIconBitmapResource target,
        IconSourcePrepareMetadata metadata)
    {
        var warnings = new List<string>(metadata.Warnings);
        if (target.BitCount != 32)
        {
            warnings.Add($"RT_BITMAP ID={target.Id} Lang={target.LanguageId} original {target.BitCount}bpp was written as 32bpp for game compatibility.");
        }

        var dibBytes = BitmapToGameCompatibleDib(bitmap, target, DllIconWritePolicy.Force32BppTopFirstForDllIcons);
        using var expected = DecodeDib(dibBytes);
        var expectedHash = expected == null ? ComputePixelHash(bitmap) : ComputePixelHash(expected);
        return new DllIconBitmapUpdate(
            target.Id,
            target.LanguageId,
            target.Width,
            target.Height,
            dibBytes,
            expectedHash,
            target.BitCount,
            32,
            "32bpp alpha top-first",
            metadata.TransparentPixelCount,
            metadata.MagentaKeyPixelCount,
            metadata.CornerBackgroundPixelCount,
            DllIconWritePolicy.Force32BppTopFirstForDllIcons.ToString(),
            metadata.SourceOpaqueBounds,
            metadata.PreparedLargeHash,
            warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    public static IconTransparencyNormalizeResult NormalizeIconSource(Image source, bool useCornerBackgroundKey = true)
    {
        var bitmap = CloneArgb(source);
        var transparentPixels = 0;
        var magentaPixels = 0;
        var cornerPixels = 0;
        var warnings = new List<string>();
        var cornerBackground = TransparentBlack;
        var hasCornerBackground = useCornerBackgroundKey && TryGetCornerBackground(bitmap, out cornerBackground);

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A == 0)
                {
                    transparentPixels++;
                    bitmap.SetPixel(x, y, TransparentBlack);
                    continue;
                }

                if (IsMagentaKey(pixel))
                {
                    magentaPixels++;
                    bitmap.SetPixel(x, y, TransparentBlack);
                    continue;
                }

                if (hasCornerBackground && IsNearColor(pixel, cornerBackground, 36))
                {
                    cornerPixels++;
                    bitmap.SetPixel(x, y, TransparentBlack);
                }
            }
        }

        if (magentaPixels > 0)
        {
            warnings.Add($"Magenta key pixels were converted to transparent: {magentaPixels}.");
        }

        if (cornerPixels > 0)
        {
            warnings.Add($"Corner background pixels were converted to transparent: {cornerPixels}.");
        }

        return new IconTransparencyNormalizeResult(bitmap, transparentPixels + magentaPixels + cornerPixels, magentaPixels, cornerPixels, warnings);
    }

    public static Bitmap ScaleToFit(Bitmap source, int targetWidth, int targetHeight)
    {
        var width = Math.Max(1, targetWidth);
        var height = Math.Max(1, targetHeight);
        var output = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        using (var graphics = Graphics.FromImage(output))
        {
            graphics.Clear(TransparentBlack);
        }

        if (source.Width == width && source.Height == height)
        {
            CopyPixels(source, output, 0, 0, width, height);
            return output;
        }

        var scale = Math.Min(width / (float)source.Width, height / (float)source.Height);
        if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0) scale = 1;
        var drawWidth = Math.Clamp((int)Math.Round(source.Width * scale), 1, width);
        var drawHeight = Math.Clamp((int)Math.Round(source.Height * scale), 1, height);
        var drawX = (width - drawWidth) / 2;
        var drawY = (height - drawHeight) / 2;
        CopyPixels(source, output, drawX, drawY, drawWidth, drawHeight);
        return output;
    }

    public static Bitmap RenderPixelPreview(Bitmap source, int canvasSize, bool checker = true)
    {
        var targetSize = Math.Max(32, canvasSize);
        var canvas = new Bitmap(targetSize, targetSize, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(canvas))
        {
            graphics.Clear(TransparentBlack);
            if (checker)
            {
                DrawChecker(graphics, new Rectangle(0, 0, targetSize, targetSize), Math.Max(4, targetSize / 16));
            }
        }

        float scale;
        if (source.Width <= targetSize && source.Height <= targetSize)
        {
            scale = Math.Max(1, Math.Min(targetSize / source.Width, targetSize / source.Height));
        }
        else
        {
            scale = Math.Min(targetSize / (float)source.Width, targetSize / (float)source.Height);
        }

        var drawWidth = Math.Clamp((int)Math.Round(source.Width * scale), 1, targetSize);
        var drawHeight = Math.Clamp((int)Math.Round(source.Height * scale), 1, targetSize);
        var drawX = (targetSize - drawWidth) / 2;
        var drawY = (targetSize - drawHeight) / 2;
        CopyPixels(source, canvas, drawX, drawY, drawWidth, drawHeight);
        return canvas;
    }

    public static Bitmap CloneArgb(Image raw)
    {
        var bitmap = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.DrawImage(raw, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        return bitmap;
    }

    public static string ComputePixelHash(Bitmap bitmap)
    {
        using var memory = new MemoryStream();
        memory.Write(BitConverter.GetBytes(bitmap.Width));
        memory.Write(BitConverter.GetBytes(bitmap.Height));
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                memory.Write(BitConverter.GetBytes(bitmap.GetPixel(x, y).ToArgb()));
            }
        }

        return Convert.ToHexString(SHA256.HashData(memory.ToArray())).ToLowerInvariant();
    }

    public static bool ArePixelEqual(Bitmap left, Bitmap right)
    {
        if (left.Width != right.Width || left.Height != right.Height) return false;
        for (var y = 0; y < left.Height; y++)
        {
            for (var x = 0; x < left.Width; x++)
            {
                if (left.GetPixel(x, y).ToArgb() != right.GetPixel(x, y).ToArgb()) return false;
            }
        }

        return true;
    }

    public byte[] BitmapToDib(Bitmap bitmap, DllIconBitmapResource targetVariant)
        => BitmapToGameCompatibleDib(bitmap, targetVariant, DllIconWritePolicy.Force32BppTopFirstForDllIcons);

    public static byte[] BitmapToDib(Bitmap bitmap)
        => BitmapTo32BppDib(bitmap);

    public static byte[] BitmapToGameCompatibleDib(
        Bitmap bitmap,
        DllIconBitmapResource targetVariant,
        DllIconWritePolicy policy)
        => policy switch
        {
            DllIconWritePolicy.Preserve8BppPalette when targetVariant.BitCount == 8 => BitmapTo8BppDib(bitmap, targetVariant, out _),
            _ => BitmapTo32BppDib(bitmap)
        };

    public static byte[] BitmapTo32BppDib(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var stride = checked(width * 4);
        var imageSize = checked(stride * height);
        var dib = new byte[40 + imageSize];

        BitConverter.GetBytes(40).CopyTo(dib, 0);
        BitConverter.GetBytes(width).CopyTo(dib, 4);
        BitConverter.GetBytes(height).CopyTo(dib, 8);
        BitConverter.GetBytes((ushort)1).CopyTo(dib, 12);
        BitConverter.GetBytes((ushort)32).CopyTo(dib, 14);
        BitConverter.GetBytes(0).CopyTo(dib, 16);
        BitConverter.GetBytes(imageSize).CopyTo(dib, 20);
        BitConverter.GetBytes(0).CopyTo(dib, 24);
        BitConverter.GetBytes(0).CopyTo(dib, 28);
        BitConverter.GetBytes(0).CopyTo(dib, 32);
        BitConverter.GetBytes(0).CopyTo(dib, 36);

        // CCZ 6.5 RT_BITMAP resources are read by the game as top-first rows.
        var offset = 40;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.A == 0)
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
                dib[offset++] = color.A;
            }
        }

        return dib;
    }

    public static byte[] BitmapTo8BppDib(
        Bitmap bitmap,
        DllIconBitmapResource targetVariant,
        out IReadOnlyList<string> warnings)
    {
        var warningList = new List<string>();
        var width = bitmap.Width;
        var height = bitmap.Height;
        var palette = ReadDibPalette(targetVariant.DibBytes, targetVariant.BitCount);
        if (palette.Count == 0)
        {
            warningList.Add($"RT_BITMAP ID={targetVariant.Id} Lang={targetVariant.LanguageId} has no readable 8bpp palette; a fallback palette was generated.");
            palette = BuildFallback8BppPalette();
        }

        var paletteArray = palette.Take(256).ToArray();
        if (paletteArray.Length < 256)
        {
            var expanded = BuildFallback8BppPalette();
            Array.Copy(paletteArray, expanded, paletteArray.Length);
            paletteArray = expanded;
        }

        var transparentIndex = FindMagentaPaletteIndex(paletteArray);
        if (transparentIndex < 0)
        {
            transparentIndex = 0;
            warningList.Add($"RT_BITMAP ID={targetVariant.Id} Lang={targetVariant.LanguageId} palette had no magenta key; index 0 was reserved for transparency.");
        }

        paletteArray[transparentIndex] = Color.Magenta;
        var stride = ((width * 8 + 31) / 32) * 4;
        var imageSize = checked(stride * height);
        var pixelOffset = 40 + 256 * 4;
        var dib = new byte[pixelOffset + imageSize];

        BitConverter.GetBytes(40).CopyTo(dib, 0);
        BitConverter.GetBytes(width).CopyTo(dib, 4);
        BitConverter.GetBytes(height).CopyTo(dib, 8);
        BitConverter.GetBytes((ushort)1).CopyTo(dib, 12);
        BitConverter.GetBytes((ushort)8).CopyTo(dib, 14);
        BitConverter.GetBytes(0).CopyTo(dib, 16);
        BitConverter.GetBytes(imageSize).CopyTo(dib, 20);
        BitConverter.GetBytes(0).CopyTo(dib, 24);
        BitConverter.GetBytes(0).CopyTo(dib, 28);
        BitConverter.GetBytes(256).CopyTo(dib, 32);
        BitConverter.GetBytes(0).CopyTo(dib, 36);

        for (var i = 0; i < 256; i++)
        {
            var offset = 40 + i * 4;
            var color = paletteArray[i];
            dib[offset] = color.B;
            dib[offset + 1] = color.G;
            dib[offset + 2] = color.R;
            dib[offset + 3] = 0;
        }

        for (var y = 0; y < height; y++)
        {
            var rowOffset = pixelOffset + (height - 1 - y) * stride;
            for (var x = 0; x < width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                dib[rowOffset + x] = color.A == 0 || IsMagentaKey(color)
                    ? (byte)transparentIndex
                    : (byte)FindNearestPaletteIndex(color, paletteArray, transparentIndex);
            }
        }

        warnings = warningList;
        return dib;
    }

    public static bool IsMagentaKey(Color pixel)
        => pixel.A != 0 &&
           pixel.R >= 210 &&
           pixel.B >= 210 &&
           pixel.G <= 90 &&
           Math.Abs(pixel.R - pixel.B) <= 70;

    private static Rectangle FindOpaqueBounds(Bitmap bitmap)
    {
        var left = bitmap.Width;
        var top = bitmap.Height;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A == 0) continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        return right < left || bottom < top
            ? Rectangle.Empty
            : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    private static Bitmap CropBitmap(Bitmap source, Rectangle bounds)
    {
        var bitmap = new Bitmap(Math.Max(1, bounds.Width), Math.Max(1, bounds.Height), PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        graphics.Clear(TransparentBlack);
        graphics.DrawImage(source, new Rectangle(0, 0, bitmap.Width, bitmap.Height), bounds, GraphicsUnit.Pixel);
        return bitmap;
    }

    private static Bitmap RenderPreparedIconCanvas(Bitmap cropped, int width, int height, int padding)
    {
        var output = new Bitmap(Math.Max(1, width), Math.Max(1, height), PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(output))
        {
            graphics.Clear(TransparentBlack);
        }

        var pad = Math.Clamp(padding, 0, Math.Max(0, Math.Min(output.Width, output.Height) / 3));
        var innerWidth = Math.Max(1, output.Width - pad * 2);
        var innerHeight = Math.Max(1, output.Height - pad * 2);
        using var scaled = ScaleToFit(cropped, innerWidth, innerHeight);
        var drawX = (output.Width - scaled.Width) / 2;
        var drawY = (output.Height - scaled.Height) / 2;
        CopyPixels(scaled, output, drawX, drawY, scaled.Width, scaled.Height);
        return output;
    }

    private static Bitmap SelectPreparedBitmapForTarget(
        IconSourcePrepareResult prepared,
        DllIconBitmapResource target,
        out Bitmap? scaled)
    {
        scaled = null;
        if (target.Width == prepared.LargeBitmap.Width && target.Height == prepared.LargeBitmap.Height)
        {
            return prepared.LargeBitmap;
        }

        if (target.Width == prepared.SmallBitmap.Width && target.Height == prepared.SmallBitmap.Height)
        {
            return prepared.SmallBitmap;
        }

        scaled = ScaleToFit(prepared.LargeBitmap, Math.Max(1, target.Width), Math.Max(1, target.Height));
        return scaled;
    }

    private static IReadOnlyList<Color> ReadDibPalette(byte[] dibBytes, int bitCount)
    {
        if (bitCount > 8 || dibBytes.Length < 40) return Array.Empty<Color>();
        var headerSize = BitConverter.ToInt32(dibBytes, 0);
        if (headerSize <= 0 || headerSize > dibBytes.Length) return Array.Empty<Color>();
        var colorUsed = dibBytes.Length >= 36 ? BitConverter.ToInt32(dibBytes, 32) : 0;
        var paletteEntries = colorUsed > 0 ? colorUsed : 1 << bitCount;
        var paletteOffset = headerSize;
        if (paletteEntries <= 0 || paletteOffset < 0 || paletteOffset + paletteEntries * 4 > dibBytes.Length)
        {
            return Array.Empty<Color>();
        }

        var palette = new Color[paletteEntries];
        for (var i = 0; i < paletteEntries; i++)
        {
            var offset = paletteOffset + i * 4;
            palette[i] = Color.FromArgb(255, dibBytes[offset + 2], dibBytes[offset + 1], dibBytes[offset]);
        }

        return palette;
    }

    private static Color[] BuildFallback8BppPalette()
    {
        var palette = new Color[256];
        palette[0] = Color.Magenta;
        palette[1] = Color.Black;
        palette[2] = Color.White;
        palette[3] = Color.Red;
        palette[4] = Color.Lime;
        palette[5] = Color.Blue;
        palette[6] = Color.Yellow;
        palette[7] = Color.Cyan;
        for (var i = 8; i < palette.Length; i++)
        {
            var value = i;
            palette[i] = Color.FromArgb(255, value, value, value);
        }

        return palette;
    }

    private static int FindMagentaPaletteIndex(IReadOnlyList<Color> palette)
    {
        for (var i = 0; i < palette.Count; i++)
        {
            if (IsMagentaKey(palette[i])) return i;
        }

        return -1;
    }

    private static int FindNearestPaletteIndex(Color color, IReadOnlyList<Color> palette, int transparentIndex)
    {
        var bestIndex = -1;
        var bestDistance = long.MaxValue;
        for (var i = 0; i < palette.Count; i++)
        {
            if (i == transparentIndex) continue;
            var candidate = palette[i];
            if (IsMagentaKey(candidate)) continue;
            var distance = ColorDistanceSquared(color, candidate);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            bestIndex = i;
        }

        return bestIndex >= 0 ? bestIndex : Math.Clamp(transparentIndex == 0 ? 1 : 0, 0, palette.Count - 1);
    }

    private static long ColorDistanceSquared(Color left, Color right)
    {
        var dr = left.R - right.R;
        var dg = left.G - right.G;
        var db = left.B - right.B;
        return dr * dr + dg * dg + db * db;
    }

    private static bool TryGetCornerBackground(Bitmap bitmap, out Color background)
    {
        background = TransparentBlack;
        if (bitmap.Width <= 0 || bitmap.Height <= 0) return false;
        var corners = new[]
        {
            bitmap.GetPixel(0, 0),
            bitmap.GetPixel(bitmap.Width - 1, 0),
            bitmap.GetPixel(0, bitmap.Height - 1),
            bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1)
        }.Where(color => color.A != 0 && !IsMagentaKey(color)).ToArray();
        if (corners.Length < 3) return false;

        var cluster = corners
            .Select(candidate => corners.Where(color => IsNearColor(color, candidate, 36)).ToArray())
            .OrderByDescending(group => group.Length)
            .First();
        if (cluster.Length < 3) return false;

        var red = (int)Math.Round(cluster.Average(color => color.R));
        var green = (int)Math.Round(cluster.Average(color => color.G));
        var blue = (int)Math.Round(cluster.Average(color => color.B));
        background = Color.FromArgb(255, red, green, blue);
        return true;
    }

    private static bool IsNearColor(Color pixel, Color reference, int maxDelta)
        => Math.Abs(pixel.R - reference.R) +
           Math.Abs(pixel.G - reference.G) +
           Math.Abs(pixel.B - reference.B) <= maxDelta;

    private static void CopyPixels(Bitmap source, Bitmap target, int targetX, int targetY, int targetWidth, int targetHeight)
    {
        for (var y = 0; y < targetHeight; y++)
        {
            var sourceY = Math.Clamp(y * source.Height / targetHeight, 0, source.Height - 1);
            var destY = targetY + y;
            if (destY < 0 || destY >= target.Height) continue;
            for (var x = 0; x < targetWidth; x++)
            {
                var sourceX = Math.Clamp(x * source.Width / targetWidth, 0, source.Width - 1);
                var destX = targetX + x;
                if (destX < 0 || destX >= target.Width) continue;
                target.SetPixel(destX, destY, source.GetPixel(sourceX, sourceY));
            }
        }
    }

    private static Bitmap? DecodeGameBitmapDib(byte[] dibBytes, CczBitmapDibRowOrder rowOrder)
    {
        if (dibBytes.Length < 40) return null;
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
        if (stride <= 0 || pixelOffset < 0 || pixelOffset + stride * height > dibBytes.Length) return null;

        var palette = new Color[paletteEntries];
        for (var i = 0; i < paletteEntries; i++)
        {
            var offset = headerSize + i * 4;
            var color = Color.FromArgb(255, dibBytes[offset + 2], dibBytes[offset + 1], dibBytes[offset]);
            palette[i] = IsMagentaKey(color) ? TransparentBlack : color;
        }

        var effectiveRowOrder = ResolveDibRowOrder(signedHeight, bitCount, rowOrder);
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
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

        return bitCount == 32
            ? CczBitmapDibRowOrder.CczTopFirst
            : CczBitmapDibRowOrder.StandardBottomUp;
    }

    private static Color ReadGameDibPixel(byte[] bytes, int rowOffset, int x, int bitCount, IReadOnlyList<Color> palette)
        => bitCount switch
        {
            4 => palette[(bytes[rowOffset + x / 2] >> (x % 2 == 0 ? 4 : 0)) & 0x0F],
            8 => palette[bytes[rowOffset + x]],
            24 => Color.FromArgb(255, bytes[rowOffset + x * 3 + 2], bytes[rowOffset + x * 3 + 1], bytes[rowOffset + x * 3]),
            32 => Read32BppPixel(bytes, rowOffset, x),
            _ => Color.Transparent
        };

    private static Color Read32BppPixel(byte[] bytes, int rowOffset, int x)
    {
        var alpha = bytes[rowOffset + x * 4 + 3];
        if (alpha == 0) return TransparentBlack;
        return Color.FromArgb(alpha, bytes[rowOffset + x * 4 + 2], bytes[rowOffset + x * 4 + 1], bytes[rowOffset + x * 4]);
    }

    private static int LanguagePriority(DllIconBitmapResource resource)
        => resource.LanguageId == 1041 ? 3 : resource.LanguageId == 0 ? 2 : 1;

    private DllIconBitmapResource? SelectGameVariant(
        string sourcePath,
        IReadOnlyList<DllIconBitmapResource> candidates,
        out bool selectedByProbe,
        List<string> warnings)
    {
        selectedByProbe = false;
        if (candidates.Count == 0) return null;

        if (TryReadWin32SelectedBitmapResource(sourcePath, candidates[0].Id, out var dibBytes, out var warning))
        {
            var match = candidates.FirstOrDefault(candidate => candidate.DibBytes.SequenceEqual(dibBytes));
            if (match != null)
            {
                selectedByProbe = true;
                return match;
            }

            warnings.Add($"RT_BITMAP ID={candidates[0].Id} 可由 Win32 FindResource 读取，但未能匹配已枚举语言变体，已使用 fallback 选择。");
        }
        else if (!string.IsNullOrWhiteSpace(warning))
        {
            warnings.Add($"RT_BITMAP ID={candidates[0].Id} 无法用 Win32 FindResource 探测实际语言变体：{warning} 已使用 fallback 选择。");
        }

        return candidates
            .OrderByDescending(x => x.LanguageId == 0)
            .ThenByDescending(LanguagePriority)
            .ThenByDescending(x => x.BitCount)
            .ThenByDescending(x => x.Width * x.Height)
            .ThenByDescending(x => x.SizeBytes)
            .FirstOrDefault();
    }

    private static void DrawChecker(Graphics graphics, Rectangle rect, int size)
    {
        using var light = new SolidBrush(Color.FromArgb(230, 230, 230));
        using var dark = new SolidBrush(Color.FromArgb(190, 190, 190));
        for (var y = rect.Top; y < rect.Bottom; y += size)
        {
            for (var x = rect.Left; x < rect.Right; x += size)
            {
                var even = ((x / size) + (y / size)) % 2 == 0;
                graphics.FillRectangle(even ? light : dark, x, y, size, size);
            }
        }
    }

    private static void ReadResourceDirectory(
        byte[] data,
        IReadOnlyList<DllIconPeSection> sections,
        int resourceBaseOffset,
        int directoryOffset,
        int level,
        List<int> path,
        List<DllIconBitmapResource> output)
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
            if ((nameRaw & unchecked((int)0x80000000)) != 0) continue;

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
            var languageId = checked((ushort)id);
            var dataEntryOffset = resourceBaseOffset + valueOffset;
            if (dataEntryOffset + 16 > data.Length) continue;
            var dataRva = BitConverter.ToInt32(data, dataEntryOffset);
            var size = BitConverter.ToInt32(data, dataEntryOffset + 4);
            var fileOffset = RvaToFileOffset(dataRva, sections);
            if (fileOffset < 0 || size <= 0 || fileOffset + size > data.Length) continue;
            var bytes = new byte[size];
            Buffer.BlockCopy(data, fileOffset, bytes, 0, size);
            if (!TryReadDibInfo(bytes, out var width, out var height, out var bitCount)) continue;
            output.Add(new DllIconBitmapResource(path[1], languageId, width, height, bitCount, size, bytes));
        }
    }

    private static bool TryReadDibInfo(byte[] bytes, out int width, out int height, out int bitCount)
    {
        width = 0;
        height = 0;
        bitCount = 0;
        if (bytes.Length < 16) return false;
        var headerSize = BitConverter.ToInt32(bytes, 0);
        if (headerSize == 12)
        {
            width = BitConverter.ToUInt16(bytes, 4);
            height = BitConverter.ToUInt16(bytes, 6);
            bitCount = BitConverter.ToUInt16(bytes, 10);
            return width > 0 && height > 0 && bitCount is 1 or 4 or 8 or 16 or 24 or 32;
        }

        if (bytes.Length < 40 || headerSize is not (40 or 108 or 124)) return false;
        width = BitConverter.ToInt32(bytes, 4);
        height = Math.Abs(BitConverter.ToInt32(bytes, 8));
        var planes = BitConverter.ToUInt16(bytes, 12);
        bitCount = BitConverter.ToUInt16(bytes, 14);
        return width > 0 && height > 0 && planes == 1 && bitCount is 1 or 4 or 8 or 16 or 24 or 32;
    }

    private static int RvaToFileOffset(int rva, IReadOnlyList<DllIconPeSection> sections)
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

    private sealed record DllIconPeSection(int VirtualAddress, int Size, int RawPointer);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, int dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr FindResource(IntPtr hModule, IntPtr lpName, IntPtr lpType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LockResource(IntPtr hResData);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);
}

public sealed record DllIconBitmapResource(
    int Id,
    ushort LanguageId,
    int Width,
    int Height,
    int BitCount,
    int SizeBytes,
    byte[] DibBytes);

public sealed record DllIconBitmapUpdate(
    int ResourceId,
    ushort LanguageId,
    int Width,
    int Height,
    byte[] DibBytes,
    string ExpectedPixelHash,
    int SourceBitCount,
    int WrittenBitCount,
    string TransparencyMode,
    int TransparentPixelCount,
    int MagentaKeyPixelCount,
    int CornerBackgroundPixelCount,
    string VariantWritePolicy,
    Rectangle SourceOpaqueBounds,
    string PreparedLargeHash,
    IReadOnlyList<string> Warnings);

public enum DllIconWritePolicy
{
    Force32BppTopFirstForDllIcons,
    Preserve8BppPalette
}

public sealed record IconSourcePrepareOptions(
    bool UseCornerBackgroundKey = true,
    int Padding = 1,
    bool CropToOpaqueBounds = true);

public sealed record IconSourcePrepareMetadata(
    Rectangle SourceOpaqueBounds,
    int SourceWidth,
    int SourceHeight,
    int TransparentPixelCount,
    int MagentaKeyPixelCount,
    int CornerBackgroundPixelCount,
    string PreparedLargeHash,
    IReadOnlyList<string> Warnings);

public sealed class IconSourcePrepareResult : IDisposable
{
    public IconSourcePrepareResult(
        Bitmap largeBitmap,
        Bitmap smallBitmap,
        Rectangle sourceOpaqueBounds,
        int sourceWidth,
        int sourceHeight,
        int transparentPixelCount,
        int magentaKeyPixelCount,
        int cornerBackgroundPixelCount,
        string preparedLargeHash,
        IReadOnlyList<string> warnings)
    {
        LargeBitmap = largeBitmap;
        SmallBitmap = smallBitmap;
        SourceOpaqueBounds = sourceOpaqueBounds;
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        TransparentPixelCount = transparentPixelCount;
        MagentaKeyPixelCount = magentaKeyPixelCount;
        CornerBackgroundPixelCount = cornerBackgroundPixelCount;
        PreparedLargeHash = preparedLargeHash;
        Warnings = warnings;
    }

    public Bitmap LargeBitmap { get; }
    public Bitmap SmallBitmap { get; }
    public Rectangle SourceOpaqueBounds { get; }
    public int SourceWidth { get; }
    public int SourceHeight { get; }
    public int TransparentPixelCount { get; }
    public int MagentaKeyPixelCount { get; }
    public int CornerBackgroundPixelCount { get; }
    public string PreparedLargeHash { get; }
    public IReadOnlyList<string> Warnings { get; }

    public IconSourcePrepareMetadata ToMetadata()
        => new(
            SourceOpaqueBounds,
            SourceWidth,
            SourceHeight,
            TransparentPixelCount,
            MagentaKeyPixelCount,
            CornerBackgroundPixelCount,
            PreparedLargeHash,
            Warnings);

    public void Dispose()
    {
        LargeBitmap.Dispose();
        SmallBitmap.Dispose();
    }
}

public sealed record IconTransparencyNormalizeResult(
    Bitmap Bitmap,
    int TransparentPixelCount,
    int MagentaKeyPixelCount,
    int CornerBackgroundPixelCount,
    IReadOnlyList<string> Warnings) : IDisposable
{
    public static IconTransparencyNormalizeResult Empty(Bitmap bitmap)
        => new(bitmap, 0, 0, 0, Array.Empty<string>());

    public void Dispose()
    {
        Bitmap.Dispose();
    }
}

public sealed record DllIconGameSlot(
    string SourcePath,
    string ResourceLabel,
    int IconIndex,
    int SmallResourceId,
    int LargeResourceId,
    IReadOnlyList<DllIconBitmapResource> Variants,
    DllIconBitmapResource? SmallSelectedVariant,
    DllIconBitmapResource? LargeSelectedVariant,
    DllIconBitmapResource EditableVariant,
    IReadOnlyList<DllIconBitmapResource> WritableVariants,
    bool SmallSelectedByWin32Probe,
    bool LargeSelectedByWin32Probe,
    string SelectionMode,
    IReadOnlyList<string> Warnings);
