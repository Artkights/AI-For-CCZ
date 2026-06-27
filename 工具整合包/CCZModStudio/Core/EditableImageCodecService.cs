using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class EditableImageCodecService
{
    private const int RtBitmap = 2;
    private static readonly byte[] PngMagic = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private readonly E5ImageReplaceService _e5 = new();
    private readonly E5RawImageCodec _raw = new();
    private readonly IconResourceReplaceService _icons = new();
    private readonly ItemIconRasterNormalizeService _itemIconNormalizer = new();
    private readonly DllBitmapIconCodecService _dllIconCodec = new();

    public EditableImageDocument Load(CczProject project, EditableImageTarget target)
    {
        var effectiveKind = ResolveEffectiveKind(target);
        var loadNote = string.Empty;
        var bitmap = effectiveKind switch
        {
            EditableImageTargetKind.DllBitmapIcon => LoadDllBitmap(target),
            EditableImageTargetKind.E5RawStrip => LoadRawStrip(project, target),
            _ when target.IsItemIconPair => LoadItemIconPair(target, out loadNote),
            _ => LoadStandardE5(target)
        };
        var palettePath = string.Empty;
        IReadOnlyList<Color> rawPalette = Array.Empty<Color>();
        if (effectiveKind == EditableImageTargetKind.E5RawStrip)
        {
            rawPalette = LoadRawPalette(project, out palettePath);
        }
        else if (effectiveKind == EditableImageTargetKind.DllBitmapIcon)
        {
            rawPalette = LoadDllIconPalette(target, out palettePath);
        }

        if (rawPalette.Count < 256) palettePath = string.Empty;

        var effectiveTarget = CloneTargetWithKind(target, effectiveKind);
        return new EditableImageDocument
        {
            Target = effectiveTarget,
            Bitmap = bitmap,
            OriginalBitmap = CloneArgb(bitmap),
            Palette = rawPalette,
            PalettePath = palettePath,
            RestrictToPalette = effectiveKind == EditableImageTargetKind.DllBitmapIcon && rawPalette.Count > 0,
            PaletteRole = effectiveKind == EditableImageTargetKind.DllBitmapIcon ? "Itemicon.dll storage palette" : string.Empty,
            FrameWidth = effectiveKind == EditableImageTargetKind.E5RawStrip ? target.FrameWidth : null,
            FrameHeight = effectiveKind == EditableImageTargetKind.E5RawStrip ? target.FrameHeight : null,
            LoadDetail = BuildLoadDetail(effectiveTarget, bitmap, loadNote)
        };
    }

    public EditableImageWritePreview PreviewWrite(CczProject project, EditableImageTarget target, Bitmap bitmap)
    {
        target = CloneTargetWithKind(target, ResolveEffectiveKind(target));
        if (target.Kind == EditableImageTargetKind.DllBitmapIcon)
        {
            var palette = LoadDllIconPalette(target, out _);
            using var writeBitmap = palette.Count > 0
                ? DllBitmapIconCodecService.QuantizeBitmapToPalette(bitmap, palette)
                : new Bitmap(bitmap);
            var request = new IconResourceBitmapReplaceRequest
            {
                IconIndex = target.IconIndex,
                Bitmap = writeBitmap,
                SourceLabel = target.DisplayName,
                OperationKind = target.OperationKind
            };
            var preview = _icons.PreviewReplaceBitmapIconsFromBitmaps(project, target.TargetPath, [request]);
            return new EditableImageWritePreview
            {
                TargetRelativePath = preview.TargetRelativePath,
                Summary = $"DLL 图标像素编辑预览：{preview.TargetRelativePath} #{target.IconIndex}",
                Warnings = preview.FormatWarnings,
                DllPreview = preview
            };
        }

        if (target.IsItemIconPair)
        {
            var pair = _itemIconNormalizer.NormalizePair(bitmap, target.DisplayName);
            var e5Requests = BuildItemIconPairRequests(target, pair);
            var pairPreview = _e5.PreviewBatchReplacement(project, target.TargetPath, e5Requests);
            var smallNumber = e5Requests[0].ImageNumber;
            var largeNumber = e5Requests[1].ImageNumber;
            return new EditableImageWritePreview
            {
                TargetRelativePath = pairPreview.TargetRelativePath,
                Summary = $"E5 item icon pair pixel editor preview: {pairPreview.TargetRelativePath} small #{smallNumber} / large #{largeNumber}",
                Warnings = pairPreview.FormatWarnings,
                E5Preview = pairPreview
            };
        }

        var sourceBytes = BuildE5SourceBytes(project, target, bitmap, out var warnings);
        var e5Request = new E5ImageBatchReplaceRequest
        {
            ImageNumber = target.ImageNumber,
            SourceBytes = sourceBytes,
            SourceBytesAreRaw = target.Kind == EditableImageTargetKind.E5RawStrip,
            SourceLabel = target.DisplayName,
            OperationKind = target.OperationKind
        };
        var e5Preview = _e5.PreviewBatchReplacement(project, target.TargetPath, [e5Request]);
        return new EditableImageWritePreview
        {
            TargetRelativePath = e5Preview.TargetRelativePath,
            Summary = $"E5 图像像素编辑预览：{e5Preview.TargetRelativePath} #{target.ImageNumber}",
            Warnings = warnings.Concat(e5Preview.FormatWarnings).Distinct(StringComparer.Ordinal).ToArray(),
            E5Preview = e5Preview
        };
    }

    public EditableImageWriteResult Write(CczProject project, EditableImageTarget target, Bitmap bitmap)
    {
        target = CloneTargetWithKind(target, ResolveEffectiveKind(target));
        if (target.Kind == EditableImageTargetKind.DllBitmapIcon)
        {
            var palette = LoadDllIconPalette(target, out _);
            using var writeBitmap = palette.Count > 0
                ? DllBitmapIconCodecService.QuantizeBitmapToPalette(bitmap, palette)
                : new Bitmap(bitmap);
            var request = new IconResourceBitmapReplaceRequest
            {
                IconIndex = target.IconIndex,
                Bitmap = writeBitmap,
                SourceLabel = target.DisplayName,
                OperationKind = target.OperationKind
            };
            var result = _icons.ReplaceBitmapIconsFromBitmaps(project, target.TargetPath, [request]);
            return new EditableImageWriteResult
            {
                TargetRelativePath = result.TargetRelativePath,
                Summary = $"DLL 图标像素编辑完成：{result.TargetRelativePath} #{target.IconIndex}",
                BackupPath = result.BackupPath,
                ReportPath = result.ReportJsonPath,
                DllResult = result
            };
        }

        if (target.IsItemIconPair)
        {
            var pair = _itemIconNormalizer.NormalizePair(bitmap, target.DisplayName);
            var e5Requests = BuildItemIconPairRequests(target, pair);
            var pairResult = _e5.ReplaceBatch(project, target.TargetPath, e5Requests);
            var smallNumber = e5Requests[0].ImageNumber;
            var largeNumber = e5Requests[1].ImageNumber;
            return new EditableImageWriteResult
            {
                TargetRelativePath = pairResult.TargetRelativePath,
                Summary = $"E5 item icon pair pixel editor writeback complete: {pairResult.TargetRelativePath} small #{smallNumber} / large #{largeNumber}",
                BackupPath = pairResult.BackupPath,
                ReportPath = pairResult.ReportJsonPath,
                E5Result = pairResult
            };
        }

        var sourceBytes = BuildE5SourceBytes(project, target, bitmap, out _);
        var e5Request = new E5ImageBatchReplaceRequest
        {
            ImageNumber = target.ImageNumber,
            SourceBytes = sourceBytes,
            SourceBytesAreRaw = target.Kind == EditableImageTargetKind.E5RawStrip,
            SourceLabel = target.DisplayName,
            OperationKind = target.OperationKind
        };
        var e5Result = _e5.ReplaceBatch(project, target.TargetPath, [e5Request]);
        return new EditableImageWriteResult
        {
            TargetRelativePath = e5Result.TargetRelativePath,
            Summary = $"E5 图像像素编辑完成：{e5Result.TargetRelativePath} #{target.ImageNumber}",
            BackupPath = e5Result.BackupPath,
            ReportPath = e5Result.ReportJsonPath,
            E5Result = e5Result
        };
    }

    private Bitmap LoadStandardE5(EditableImageTarget target)
    {
        var bytes = _e5.ReadEntryBytes(target.TargetPath, target.ImageNumber);
        using var memory = new MemoryStream(bytes, writable: false);
        using var raw = Image.FromStream(memory, useEmbeddedColorManagement: false, validateImageData: true);
        var bitmap = CloneArgb(raw);
        ApplyMagentaTransparency(bitmap);
        return bitmap;
    }

    private Bitmap LoadItemIconPair(EditableImageTarget target, out string loadNote)
    {
        var smallNumber = target.SmallImageNumber > 0 ? target.SmallImageNumber : Math.Max(1, target.ImageNumber - 1);
        var largeNumber = target.LargeImageNumber > 0 ? target.LargeImageNumber : target.ImageNumber;
        var bytes = _e5.ReadEntryBytes(target.TargetPath, largeNumber);
        var decoded = ItemIconRasterNormalizeService.DecodeGameIconBmp(bytes);
        if (decoded.Width == ItemIconRasterNormalizeService.LargeIconSize &&
            decoded.Height == ItemIconRasterNormalizeService.LargeIconSize)
        {
            loadNote = $"Item.e5 pair edit: large #{largeNumber} is 32x32; save writes small #{smallNumber} and large #{largeNumber}.";
            return decoded;
        }

        var originalSize = $"{decoded.Width}x{decoded.Height}";
        using (decoded)
        {
            var normalized = _itemIconNormalizer.NormalizeLargeBitmap(decoded);
            loadNote = $"Item.e5 pair edit: large #{largeNumber} was {originalSize}; editor canvas normalized to 32x32; save writes small #{smallNumber} and large #{largeNumber}.";
            return normalized;
        }
    }

    private Bitmap LoadRawStrip(CczProject project, EditableImageTarget target)
    {
        var bytes = _e5.ReadEntryBytes(target.TargetPath, target.ImageNumber);
        var spec = ResolveRawSpec(target.TargetPath);
        var palette = LoadRawPalette(project, out _);
        var rawLength = bytes.Length - bytes.Length % spec.Width;
        if (rawLength < spec.Width * spec.FrameHeight)
        {
            throw new InvalidOperationException($"RAW 条目长度不足，无法按 {spec.Width}x{spec.FrameHeight} 帧条读取。");
        }

        var height = rawLength / spec.Width;
        var bitmap = new Bitmap(spec.Width, height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < spec.Width; x++)
            {
                var value = bytes[y * spec.Width + x];
                if (value == 0)
                {
                    bitmap.SetPixel(x, y, Color.Transparent);
                    continue;
                }

                var color = value < palette.Count ? palette[value] : Color.FromArgb(255, value, value, value);
                bitmap.SetPixel(x, y, IsMagentaKey(color) ? Color.Transparent : color);
            }
        }

        return bitmap;
    }

    private Bitmap LoadDllBitmap(EditableImageTarget target)
    {
        var resources = _dllIconCodec.ParseBitmapResources(target.TargetPath);
        var pair = _dllIconCodec.ResolveBitmapResourcePair(resources, target.IconIndex);
        var selected = _dllIconCodec.SelectDisplayVariant(pair.LargeVariants)
                       ?? _dllIconCodec.SelectDisplayVariant(pair.SmallVariants)
                       ?? throw new InvalidOperationException($"图标编号 {target.IconIndex} 没有可编辑的 RT_BITMAP 资源。");
        using var decoded = DllBitmapIconCodecService.DecodeStorageDib(selected.DibBytes)
                            ?? throw new InvalidOperationException("DLL 图标 DIB 无法解码。");
        if (decoded.Bitmap.Width == DllBitmapIconCodecService.LargeIconSize &&
            decoded.Bitmap.Height == DllBitmapIconCodecService.LargeIconSize)
        {
            return new Bitmap(decoded.Bitmap);
        }

        return _dllIconCodec.NormalizeLargeBitmap(decoded.Bitmap);
    }

    private IReadOnlyList<Color> LoadDllIconPalette(EditableImageTarget target, out string palettePath)
    {
        palettePath = target.TargetPath;
        var resources = _dllIconCodec.ParseBitmapResources(target.TargetPath);
        var pair = _dllIconCodec.ResolveBitmapResourcePair(resources, target.IconIndex);
        return _dllIconCodec.ResolveStoragePalette(resources, pair);
    }

    private byte[] BuildE5SourceBytes(CczProject project, EditableImageTarget target, Bitmap bitmap, out IReadOnlyList<string> warnings)
    {
        if (target.Kind == EditableImageTargetKind.E5RawStrip)
        {
            var spec = ResolveRawSpec(target.TargetPath);
            var encode = _raw.EncodeBitmap(project, bitmap, target.DisplayName, spec, strictHeight: false);
            warnings = encode.Warnings;
            return encode.RawBytes;
        }

        warnings = Array.Empty<string>();
        using var memory = new MemoryStream();
        bitmap.Save(memory, ImageFormat.Png);
        return memory.ToArray();
    }

    private static IReadOnlyList<E5ImageBatchReplaceRequest> BuildItemIconPairRequests(
        EditableImageTarget target,
        ItemIconRasterPair pair)
    {
        var smallNumber = target.SmallImageNumber > 0 ? target.SmallImageNumber : Math.Max(1, target.ImageNumber - 1);
        var largeNumber = target.LargeImageNumber > 0 ? target.LargeImageNumber : target.ImageNumber;
        return
        [
            new E5ImageBatchReplaceRequest
            {
                ImageNumber = smallNumber,
                SourceBytes = pair.Small.BmpBytes,
                SourceLabel = $"{target.DisplayName} (normalized small 16x16)",
                OperationKind = "item icon pixel edit small normalized"
            },
            new E5ImageBatchReplaceRequest
            {
                ImageNumber = largeNumber,
                SourceBytes = pair.Large.BmpBytes,
                SourceLabel = $"{target.DisplayName} (normalized large 32x32)",
                OperationKind = "item icon pixel edit large normalized"
            }
        ];
    }

    private static string BuildLoadDetail(EditableImageTarget target, Bitmap bitmap, string loadNote = "")
    {
        var frame = target.FrameWidth.HasValue && target.FrameHeight.HasValue
            ? $"；帧 {target.FrameWidth}x{target.FrameHeight}"
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(loadNote))
        {
            frame += "; " + loadNote;
        }
        return $"{target.DisplayName}；画布 {bitmap.Width}x{bitmap.Height}{frame}";
    }

    private static E5RawImageSpec ResolveRawSpec(string targetPath)
    {
        var name = Path.GetFileName(targetPath);
        if (name.Equals("Pmapobj.e5", StringComparison.OrdinalIgnoreCase)) return E5RawImageCodec.PmapobjSpec;
        if (name.Equals("Unit_mov.e5", StringComparison.OrdinalIgnoreCase)) return E5RawImageCodec.UnitMovSpec;
        if (name.Equals("Unit_atk.e5", StringComparison.OrdinalIgnoreCase)) return E5RawImageCodec.UnitAtkSpec;
        if (name.Equals("Unit_spc.e5", StringComparison.OrdinalIgnoreCase)) return E5RawImageCodec.UnitSpcSpec;
        throw new InvalidOperationException("不是已知 R/S RAW E5 资源：" + targetPath);
    }

    public static (int Width, int FrameHeight)? TryResolveRawFrameSpec(string targetPath)
    {
        var name = Path.GetFileName(targetPath);
        if (name.Equals("Pmapobj.e5", StringComparison.OrdinalIgnoreCase)) return (48, 64);
        if (name.Equals("Unit_mov.e5", StringComparison.OrdinalIgnoreCase)) return (48, 48);
        if (name.Equals("Unit_atk.e5", StringComparison.OrdinalIgnoreCase)) return (64, 64);
        if (name.Equals("Unit_spc.e5", StringComparison.OrdinalIgnoreCase)) return (48, 48);
        return null;
    }

    public EditableImageTargetKind ResolveEffectiveKind(EditableImageTarget target)
    {
        if (target.Kind != EditableImageTargetKind.E5RawStrip) return target.Kind;

        var bytes = _e5.ReadEntryBytes(target.TargetPath, target.ImageNumber);
        return IsStandardImage(bytes) ? EditableImageTargetKind.E5Standard : EditableImageTargetKind.E5RawStrip;
    }

    private static bool IsStandardImage(byte[] bytes)
        => bytes.Length >= 2 &&
           ((bytes[0] == (byte)'B' && bytes[1] == (byte)'M') ||
            (bytes[0] == 0xFF && bytes[1] == 0xD8) ||
            (bytes.Length >= PngMagic.Length && bytes.AsSpan(0, PngMagic.Length).SequenceEqual(PngMagic)));

    private static EditableImageTarget CloneTargetWithKind(EditableImageTarget target, EditableImageTargetKind kind)
        => new()
        {
            Kind = kind,
            DisplayName = target.DisplayName,
            TargetPath = target.TargetPath,
            ImageNumber = target.ImageNumber,
            IconIndex = target.IconIndex,
            ResourceFormat = target.ResourceFormat,
            FrameWidth = kind == EditableImageTargetKind.E5RawStrip ? target.FrameWidth : null,
            FrameHeight = kind == EditableImageTargetKind.E5RawStrip ? target.FrameHeight : null,
            IsItemIconPair = target.IsItemIconPair,
            SmallImageNumber = target.SmallImageNumber,
            LargeImageNumber = target.LargeImageNumber,
            OperationKind = target.OperationKind
        };

    private static IReadOnlyList<Color> LoadRawPalette(CczProject project, out string palettePath)
    {
        var candidates = new[]
        {
            PortableInstallPaths.PaletteTsbPath,
            Path.Combine(project.GameRoot, "tsb")
        };

        palettePath = candidates.FirstOrDefault(path => File.Exists(path) && new FileInfo(path).Length >= 256 * 4) ?? candidates[0];
        if (!File.Exists(palettePath)) return Array.Empty<Color>();

        var bytes = File.ReadAllBytes(palettePath);
        var colors = new Color[256];
        for (var i = 0; i < colors.Length; i++)
        {
            var offset = i * 4;
            colors[i] = Color.FromArgb(255, bytes[offset + 2], bytes[offset + 1], bytes[offset]);
        }

        return colors;
    }

    private static Bitmap CloneArgb(Image raw)
    {
        var bitmap = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.DrawImage(raw, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        return bitmap;
    }

    private static void ApplyMagentaTransparency(Bitmap bitmap)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (IsMagentaKey(bitmap.GetPixel(x, y)))
                {
                    bitmap.SetPixel(x, y, Color.Transparent);
                }
            }
        }
    }

    private static bool IsMagentaKey(Color pixel)
        => pixel.A == 0 ||
           pixel.R >= 210 &&
           pixel.B >= 210 &&
           pixel.G <= 90 &&
           Math.Abs(pixel.R - pixel.B) <= 70;

    private static IReadOnlyList<DllBitmapResource> ParseBitmapResources(string sourcePath)
    {
        try
        {
            var data = File.ReadAllBytes(sourcePath);
            if (data.Length < 0x40 || data[0] != 'M' || data[1] != 'Z') return Array.Empty<DllBitmapResource>();
            var peOffset = BitConverter.ToInt32(data, 0x3C);
            if (peOffset <= 0 || peOffset + 248 >= data.Length) return Array.Empty<DllBitmapResource>();
            var sectionCount = BitConverter.ToUInt16(data, peOffset + 6);
            var optionalHeaderSize = BitConverter.ToUInt16(data, peOffset + 20);
            var optionalHeaderOffset = peOffset + 24;
            var magic = BitConverter.ToUInt16(data, optionalHeaderOffset);
            var dataDirectoryOffset = magic == 0x20B ? optionalHeaderOffset + 112 : optionalHeaderOffset + 96;
            if (dataDirectoryOffset + 2 * 8 + 8 > data.Length) return Array.Empty<DllBitmapResource>();
            var resourceRva = BitConverter.ToInt32(data, dataDirectoryOffset + 2 * 8);
            if (resourceRva <= 0) return Array.Empty<DllBitmapResource>();
            var sectionOffset = optionalHeaderOffset + optionalHeaderSize;
            var sections = new List<PeSectionInfo>();
            for (var i = 0; i < sectionCount; i++)
            {
                var offset = sectionOffset + i * 40;
                if (offset + 40 > data.Length) break;
                sections.Add(new PeSectionInfo(
                    BitConverter.ToInt32(data, offset + 12),
                    Math.Max(BitConverter.ToInt32(data, offset + 8), BitConverter.ToInt32(data, offset + 16)),
                    BitConverter.ToInt32(data, offset + 20)));
            }

            var resourceBaseOffset = RvaToFileOffset(resourceRva, sections);
            if (resourceBaseOffset < 0 || resourceBaseOffset + 16 > data.Length) return Array.Empty<DllBitmapResource>();
            var result = new List<DllBitmapResource>();
            ReadResourceDirectory(data, sections, resourceBaseOffset, resourceBaseOffset, 0, [], result);
            return result.OrderBy(x => x.Id).ToArray();
        }
        catch
        {
            return Array.Empty<DllBitmapResource>();
        }
    }

    private static void ReadResourceDirectory(
        byte[] data,
        IReadOnlyList<PeSectionInfo> sections,
        int resourceBaseOffset,
        int directoryOffset,
        int level,
        List<int> path,
        List<DllBitmapResource> output)
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
            var dataEntryOffset = resourceBaseOffset + valueOffset;
            if (dataEntryOffset + 16 > data.Length) continue;
            var dataRva = BitConverter.ToInt32(data, dataEntryOffset);
            var size = BitConverter.ToInt32(data, dataEntryOffset + 4);
            var fileOffset = RvaToFileOffset(dataRva, sections);
            if (fileOffset < 0 || size <= 0 || fileOffset + size > data.Length) continue;
            var bytes = new byte[size];
            Buffer.BlockCopy(data, fileOffset, bytes, 0, size);
            if (!TryReadDibDimensions(bytes, out var width, out var height)) continue;
            output.Add(new DllBitmapResource(path[1], width, height, bytes));
        }
    }

    private static IReadOnlyList<DllBitmapResource> ResolveBitmapResourcePair(IReadOnlyList<DllBitmapResource> resources, int iconIndex)
    {
        if (resources.Count == 0) throw new InvalidOperationException("目标 DLL 中没有解析到 RT_BITMAP 图标资源。");
        if (iconIndex < 0) throw new InvalidOperationException("图标编号不能小于 0。");

        var minId = resources.Min(x => x.Id);
        if (minId >= 100)
        {
            var smallId = minId + iconIndex * 2;
            var largeId = minId + iconIndex * 2 + 1;
            return resources
                .Where(x => x.Id == smallId || x.Id == largeId)
                .OrderBy(x => x.Width * x.Height)
                .ToArray();
        }

        if (iconIndex >= resources.Count)
        {
            throw new InvalidOperationException($"图标编号 {iconIndex} 超出 DLL 位图资源范围 0-{resources.Count - 1}。");
        }

        return new[] { resources.OrderBy(x => x.Id).ElementAt(iconIndex) };
    }

    private static bool TryReadDibDimensions(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length < 16) return false;
        var headerSize = BitConverter.ToInt32(bytes, 0);
        if (headerSize == 12)
        {
            width = BitConverter.ToUInt16(bytes, 4);
            height = BitConverter.ToUInt16(bytes, 6);
            return width > 0 && height > 0;
        }

        if (headerSize is not (40 or 108 or 124)) return false;
        width = BitConverter.ToInt32(bytes, 4);
        height = Math.Abs(BitConverter.ToInt32(bytes, 8));
        return width > 0 && height > 0;
    }

    private static Bitmap? DecodeDib(byte[] dibBytes)
    {
        var gameBitmap = DecodeGameBitmapDib(dibBytes, CczBitmapDibRowOrder.Auto);
        if (gameBitmap != null) return gameBitmap;

        var dibHeaderSize = BitConverter.ToInt32(dibBytes, 0);
        if (dibHeaderSize <= 0 || dibHeaderSize > dibBytes.Length) return null;
        var bitCount = BitConverter.ToUInt16(dibBytes, 14);
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

    internal static Bitmap? DecodeDibForSmoke(byte[] dibBytes)
    {
        var decoded = DllBitmapIconCodecService.DecodeDib(dibBytes);
        if (decoded == null) return null;
        using (decoded)
        {
            return new Bitmap(decoded.Bitmap);
        }
    }

    private static Bitmap? DecodeGameBitmapDib(byte[] dibBytes, CczBitmapDibRowOrder rowOrder)
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
        if (stride <= 0 || pixelOffset < 0 || pixelOffset + stride * height > dibBytes.Length) return null;

        var palette = new Color[paletteEntries];
        for (var i = 0; i < paletteEntries; i++)
        {
            var offset = headerSize + i * 4;
            palette[i] = Color.FromArgb(255, dibBytes[offset + 2], dibBytes[offset + 1], dibBytes[offset]);
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
            32 => Color.FromArgb(bytes[rowOffset + x * 4 + 3], bytes[rowOffset + x * 4 + 2], bytes[rowOffset + x * 4 + 1], bytes[rowOffset + x * 4]),
            _ => Color.Transparent
        };

    private static int RvaToFileOffset(int rva, IReadOnlyList<PeSectionInfo> sections)
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

    private sealed record DllBitmapResource(int Id, int Width, int Height, byte[] DibBytes);
    private sealed record PeSectionInfo(int VirtualAddress, int Size, int RawPointer);
}
