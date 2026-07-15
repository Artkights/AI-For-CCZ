using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class EditableImageCodecService
{
    private const int RtBitmap = 2;
    private static readonly byte[] PngMagic = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private readonly E5ImageReplaceService _e5 = new();
    private readonly E5RawImageCodec _raw = new();
    private readonly E5RawPaletteService _rawPalette = new();
    private readonly EditableImageStorageService _storage = new();
    private readonly IconResourceReplaceService _icons = new();
    private readonly ItemIconRasterNormalizeService _itemIconNormalizer = new();
    private readonly DllBitmapIconCodecService _dllIconCodec = new();

    public EditableImageDocument Load(CczProject project, EditableImageTarget target)
    {
        var storage = target.StorageInfo ?? _storage.Inspect(target);
        var effectiveKind = ResolveEffectiveKind(target, storage);
        var loadNote = string.Empty;
        var bitmap = effectiveKind switch
        {
            EditableImageTargetKind.DllBitmapIcon => LoadDllBitmap(target),
            EditableImageTargetKind.E5RawStrip => LoadRawStrip(project, target),
            _ when target.IsItemIconPair => LoadItemIconPair(target, storage, out loadNote),
            _ => LoadStandardE5(target, storage)
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

        var relatedEntrySources = target.RelatedEntrySources;
        if (target.IsItemIconPair && relatedEntrySources.Count == 0)
            relatedEntrySources = CaptureItemIconPairSources(project, target);
        var sourceSnapshot = target.SourceSnapshot ?? CaptureSourceSnapshot(
            target,
            bitmap,
            storage,
            rawPalette,
            palettePath);
        var effectiveTarget = CloneTargetWithKind(target, effectiveKind, storage, sourceSnapshot, relatedEntrySources);
        return new EditableImageDocument
        {
            Target = effectiveTarget,
            Bitmap = bitmap,
            OriginalBitmap = CloneArgb(bitmap),
            Palette = rawPalette,
            PalettePath = palettePath,
            RestrictToPalette = (effectiveKind == EditableImageTargetKind.DllBitmapIcon || storage.Kind == EditableImageStorageKind.Raw) && rawPalette.Count > 0,
            PaletteRole = effectiveKind == EditableImageTargetKind.DllBitmapIcon
                ? "Itemicon.dll storage palette"
                : storage.Kind == EditableImageStorageKind.Raw ? "R/S RAW storage palette" : string.Empty,
            FrameWidth = target.FrameWidth,
            FrameHeight = target.FrameHeight,
            LoadDetail = BuildLoadDetail(effectiveTarget, bitmap, loadNote) + $"；原存储 {storage.DisplayKind}",
            StorageInfo = storage,
            SourceSnapshot = sourceSnapshot
        };
    }

    public EditableImageWritePreview PreviewWrite(CczProject project, EditableImageTarget target, Bitmap bitmap)
    {
        var storage = target.StorageInfo ?? _storage.Inspect(target);
        target = CloneTargetWithKind(target, ResolveEffectiveKind(target, storage), storage);
        EnsureBitmapChanged(target, bitmap);
        if (target.Kind == EditableImageTargetKind.DllBitmapIcon)
        {
            EnsureDllSnapshotUnchanged(target);
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
            var e5Requests = BuildItemIconPairRequests(project, target, pair);
            var pairPreview = _e5.PreviewBatchReplacement(project, target.TargetPath, e5Requests);
            var smallNumber = target.SmallImageNumber > 0 ? target.SmallImageNumber : Math.Max(1, target.ImageNumber - 1);
            var largeNumber = target.LargeImageNumber > 0 ? target.LargeImageNumber : target.ImageNumber;
            return new EditableImageWritePreview
            {
                TargetRelativePath = pairPreview.TargetRelativePath,
                Summary = $"E5 item icon pair pixel editor preview: {pairPreview.TargetRelativePath} small #{smallNumber} / large #{largeNumber}",
                Warnings = pairPreview.FormatWarnings,
                E5Preview = pairPreview
            };
        }

        var e5Request = BuildE5Request(project, target, bitmap, storage, out var warnings);
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
        var storage = target.StorageInfo ?? _storage.Inspect(target);
        target = CloneTargetWithKind(target, ResolveEffectiveKind(target, storage), storage);
        EnsureBitmapChanged(target, bitmap);
        if (target.Kind == EditableImageTargetKind.DllBitmapIcon)
        {
            EnsureDllSnapshotUnchanged(target);
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
            var e5Requests = BuildItemIconPairRequests(project, target, pair);
            var pairResult = _e5.ReplaceBatch(project, target.TargetPath, e5Requests);
            var smallNumber = target.SmallImageNumber > 0 ? target.SmallImageNumber : Math.Max(1, target.ImageNumber - 1);
            var largeNumber = target.LargeImageNumber > 0 ? target.LargeImageNumber : target.ImageNumber;
            return new EditableImageWriteResult
            {
                TargetRelativePath = pairResult.TargetRelativePath,
                Summary = $"E5 item icon pair pixel editor writeback complete: {pairResult.TargetRelativePath} small #{smallNumber} / large #{largeNumber}",
                BackupPath = pairResult.BackupPath,
                ReportPath = pairResult.ReportJsonPath,
                E5Result = pairResult
            };
        }

        var e5Request = BuildE5Request(project, target, bitmap, storage, out _);
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

    private Bitmap LoadStandardE5(EditableImageTarget target, EditableImageStorageInfo storage)
    {
        var bytes = _e5.ReadEntryBytes(target.TargetPath, target.ImageNumber);
        using var memory = new MemoryStream(bytes, writable: false);
        using var raw = Image.FromStream(memory, useEmbeddedColorManagement: false, validateImageData: true);
        var bitmap = CloneArgb(raw);
        if (storage.Kind is EditableImageStorageKind.Bmp24 or EditableImageStorageKind.Jpeg)
            ApplyBackgroundKeyTransparency(bitmap, storage.BackgroundKeyColor, storage.Kind == EditableImageStorageKind.Jpeg ? 28 : 2);
        return bitmap;
    }

    public EditableImagePreparedE5Write PrepareE5Write(CczProject project, EditableImageTarget target, Bitmap bitmap)
    {
        var storage = target.StorageInfo ?? _storage.Inspect(target);
        target = CloneTargetWithKind(target, ResolveEffectiveKind(target, storage), storage);
        if (target.Kind == EditableImageTargetKind.DllBitmapIcon)
        {
            throw new InvalidOperationException("DLL 图标不能加入 E5 资源组写回。");
        }

        if (target.IsItemIconPair)
        {
            var pair = _itemIconNormalizer.NormalizePair(bitmap, target.DisplayName);
            return new EditableImagePreparedE5Write
            {
                Target = target,
                TargetPath = target.TargetPath,
                Requests = BuildItemIconPairRequests(project, target, pair)
            };
        }

        var request = BuildE5Request(project, target, bitmap, storage, out var warnings);
        return new EditableImagePreparedE5Write
        {
            Target = target,
            TargetPath = target.TargetPath,
            Requests =
            [
                request
            ],
            Warnings = warnings
        };
    }

    private Bitmap LoadItemIconPair(
        EditableImageTarget target,
        EditableImageStorageInfo storage,
        out string loadNote)
    {
        var smallNumber = target.SmallImageNumber > 0 ? target.SmallImageNumber : Math.Max(1, target.ImageNumber - 1);
        var largeNumber = target.LargeImageNumber > 0 ? target.LargeImageNumber : target.ImageNumber;
        var decoded = LoadStandardE5(target, storage);
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
        var pixels = new int[checked(spec.Width * height)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < spec.Width; x++)
            {
                var value = bytes[y * spec.Width + x];
                if (value == 0)
                {
                    pixels[y * spec.Width + x] = Color.Transparent.ToArgb();
                    continue;
                }

                var color = value < palette.Count ? palette[value] : Color.FromArgb(255, value, value, value);
                pixels[y * spec.Width + x] = (IsMagentaKey(color) ? Color.Transparent : color).ToArgb();
            }
        }

        return BitmapArgbSnapshot.Create(spec.Width, height, pixels);
    }

    private Bitmap LoadDllBitmap(EditableImageTarget target)
    {
        var resources = _dllIconCodec.ParseBitmapResources(target.TargetPath);
        var pair = _dllIconCodec.ResolveBitmapResourcePair(resources, target.IconIndex);
        var unsupported = pair.AllVariants.FirstOrDefault(resource => resource.BitCount is not (8 or 24 or 32));
        if (unsupported != null)
            throw new InvalidOperationException(
                $"RT_BITMAP ID={unsupported.Id} LANG={unsupported.LanguageId} is {unsupported.BitCount}bpp. Pixel editing is read-only for formats other than uncompressed 8/24/32bpp DIB.");
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

    private E5ImageBatchReplaceRequest BuildE5Request(
        CczProject project,
        EditableImageTarget target,
        Bitmap bitmap,
        EditableImageStorageInfo storage,
        out IReadOnlyList<string> warnings)
    {
        if (!storage.CanEdit)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(storage.ReadOnlyReason)
                ? $"当前 {storage.DisplayKind} 条目不支持安全像素写回。"
                : storage.ReadOnlyReason);

        byte[] sourceBytes;
        var sourceIsRaw = false;
        var policy = E5ImageWritePlacementPolicy.RequireExactInPlace;
        var snapshot = target.SourceSnapshot
                       ?? throw new InvalidOperationException("Pixel writeback is missing its source snapshot. Reload the image before saving.");
        if (snapshot.Width != bitmap.Width || snapshot.Height != bitmap.Height)
            throw new InvalidOperationException("The editable image dimensions changed after loading.");
        if (storage.Kind == EditableImageStorageKind.Raw)
        {
            var spec = ResolveRawSpec(target.TargetPath);
            var currentPalette = LoadRawPalette(project, out var currentPalettePath);
            var currentPaletteSha = ComputePaletteSha256(currentPalette, currentPalettePath);
            if (!currentPaletteSha.Equals(snapshot.PaletteSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The RAW palette changed after the pixel editor loaded the image. Reload before saving.");
            var encode = _raw.EncodeBitmapPreservingIndices(
                bitmap,
                target.DisplayName,
                spec,
                snapshot.Palette,
                currentPalettePath,
                snapshot.DecodedBytes,
                snapshot.OriginalArgbPixels,
                snapshot.TrailingByteCount);
            sourceBytes = encode.RawBytes;
            if (sourceBytes.Length != storage.OriginalStoredLength)
                throw new InvalidOperationException($"RAW 写回长度必须保持 {storage.OriginalStoredLength:N0} 字节，当前编码为 {sourceBytes.Length:N0} 字节。");
            warnings = encode.Warnings;
            sourceIsRaw = true;
        }
        else if (storage.Kind == EditableImageStorageKind.Bmp24)
        {
            sourceBytes = _storage.EncodeBmp24PreservingUnchangedPixels(
                storage,
                snapshot.DecodedBytes,
                bitmap,
                snapshot.OriginalArgbPixels);
            if (sourceBytes.Length != storage.OriginalStoredLength)
                throw new InvalidOperationException("BMP 原格式编码改变了条目长度，已拒绝写回。");
            warnings = Array.Empty<string>();
        }
        else if (storage.Kind == EditableImageStorageKind.Png)
        {
            using var memory = new MemoryStream();
            bitmap.Save(memory, ImageFormat.Png);
            sourceBytes = memory.ToArray();
            warnings = Array.Empty<string>();
            policy = E5ImageWritePlacementPolicy.RequireStableOffset;
        }
        else
        {
            throw new InvalidOperationException(storage.ReadOnlyReason);
        }

        return new E5ImageBatchReplaceRequest
        {
            ImageNumber = target.ImageNumber,
            SourceBytes = sourceBytes,
            SourceBytesAreRaw = sourceIsRaw,
            SourceLabel = target.DisplayName,
            OperationKind = target.OperationKind,
            ExpectedTargetKind = storage.ExpectedE5Kind,
            ExpectedTargetSha256 = storage.EntrySha256,
            ExpectedArchiveSha256 = snapshot.ArchiveSha256,
            ExpectedIndexSha256 = snapshot.IndexSha256,
            PlacementPolicy = policy,
            CharacterTarget = target.CharacterTarget
        };
    }

    private IReadOnlyList<E5ImageBatchReplaceRequest> BuildItemIconPairRequests(
        CczProject project,
        EditableImageTarget target,
        ItemIconRasterPair pair)
    {
        var smallNumber = target.SmallImageNumber > 0 ? target.SmallImageNumber : Math.Max(1, target.ImageNumber - 1);
        var largeNumber = target.LargeImageNumber > 0 ? target.LargeImageNumber : target.ImageNumber;
        if (!target.RelatedEntrySources.TryGetValue(smallNumber, out var smallSource) ||
            !target.RelatedEntrySources.TryGetValue(largeNumber, out var largeSource))
            throw new InvalidOperationException("Item.e5 pair writeback is missing the small/large source snapshots. Reload before saving.");

        using var smallBitmap = pair.Small.CreateTransparentBitmap();
        using var largeBitmap = pair.Large.CreateTransparentBitmap();
        var smallTarget = BuildRelatedEntryTarget(target, smallSource, "small 16x16");
        var largeTarget = BuildRelatedEntryTarget(target, largeSource, "large 32x32");
        var requests = new List<E5ImageBatchReplaceRequest>();
        if (!BitmapMatchesSnapshot(smallBitmap, smallSource.Snapshot))
            requests.Add(BuildE5Request(project, smallTarget, smallBitmap, smallSource.StorageInfo, out _));
        if (!BitmapMatchesSnapshot(largeBitmap, largeSource.Snapshot))
            requests.Add(BuildE5Request(project, largeTarget, largeBitmap, largeSource.StorageInfo, out _));
        if (requests.Count == 0)
            throw new InvalidOperationException("The normalized Item.e5 small/large icons contain no pixel changes.");
        return requests;
    }

    private static EditableImageTarget BuildRelatedEntryTarget(
        EditableImageTarget pairTarget,
        EditableImageEntrySource source,
        string role)
        => new()
        {
            Kind = EditableImageTargetKind.E5Standard,
            DisplayName = $"{pairTarget.DisplayName} ({role})",
            TargetPath = pairTarget.TargetPath,
            ImageNumber = source.ImageNumber,
            ResourceFormat = source.StorageInfo.DisplayKind,
            OperationKind = pairTarget.OperationKind,
            CharacterTarget = pairTarget.CharacterTarget,
            StorageInfo = source.StorageInfo,
            SourceSnapshot = source.Snapshot
        };

    private static bool BitmapMatchesSnapshot(Bitmap bitmap, EditableImageSourceSnapshot snapshot)
    {
        if (bitmap.Width != snapshot.Width || bitmap.Height != snapshot.Height ||
            snapshot.OriginalArgbPixels.Length != checked(bitmap.Width * bitmap.Height)) return false;
        var currentPixels = BitmapArgbSnapshot.Capture(bitmap);
        for (var index = 0; index < currentPixels.Length; index++)
        {
            if (!PixelsEquivalent(currentPixels[index], snapshot.OriginalArgbPixels[index])) return false;
        }
        return true;
    }

    private static bool PixelsEquivalent(int currentArgb, int originalArgb)
        => currentArgb == originalArgb ||
           (((uint)currentArgb >> 24) == 0 && ((uint)originalArgb >> 24) == 0);

    private static void EnsureBitmapChanged(EditableImageTarget target, Bitmap bitmap)
    {
        var snapshot = target.SourceSnapshot
                       ?? throw new InvalidOperationException("Pixel writeback is missing its source snapshot. Reload before saving.");
        if (BitmapMatchesSnapshot(bitmap, snapshot))
            throw new InvalidOperationException("No pixel changes were detected; the resource file was not written.");
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
        var storage = target.StorageInfo ?? _storage.Inspect(target);
        return ResolveEffectiveKind(target, storage);
    }

    private EditableImageTargetKind ResolveEffectiveKind(EditableImageTarget target, EditableImageStorageInfo storage)
    {
        if (target.Kind == EditableImageTargetKind.DllBitmapIcon) return target.Kind;
        if (storage.Kind == EditableImageStorageKind.Raw) return EditableImageTargetKind.E5RawStrip;
        if (storage.Kind == EditableImageStorageKind.Ls12)
        {
            var decoded = _e5.ReadEntryBytes(target.TargetPath, target.ImageNumber);
            return IsStandardImage(decoded) ? EditableImageTargetKind.E5Standard : EditableImageTargetKind.E5RawStrip;
        }
        return EditableImageTargetKind.E5Standard;
    }

    private IReadOnlyDictionary<int, EditableImageEntrySource> CaptureItemIconPairSources(
        CczProject project,
        EditableImageTarget target)
    {
        var smallNumber = target.SmallImageNumber > 0 ? target.SmallImageNumber : Math.Max(1, target.ImageNumber - 1);
        var largeNumber = target.LargeImageNumber > 0 ? target.LargeImageNumber : target.ImageNumber;
        var result = new Dictionary<int, EditableImageEntrySource>();
        foreach (var pairEntry in new[] { (Number: smallNumber, Size: ItemIconRasterNormalizeService.SmallIconSize), (Number: largeNumber, Size: ItemIconRasterNormalizeService.LargeIconSize) })
        {
            var entryTarget = new EditableImageTarget
            {
                Kind = EditableImageTargetKind.E5Standard,
                DisplayName = target.DisplayName,
                TargetPath = target.TargetPath,
                ImageNumber = pairEntry.Number,
                OperationKind = target.OperationKind
            };
            var entryStorage = _storage.Inspect(entryTarget);
            if (entryStorage.Kind is not (EditableImageStorageKind.Bmp24 or EditableImageStorageKind.Png) || !entryStorage.CanEdit)
                throw new InvalidOperationException(
                    $"Item.e5 image #{pairEntry.Number} is {entryStorage.DisplayKind}; both icon entries must be editable BMP24 or PNG without format conversion.");
            using var original = LoadStandardE5(entryTarget, entryStorage);
            if (original.Width != pairEntry.Size || original.Height != pairEntry.Size)
                throw new InvalidOperationException(
                    $"Item.e5 image #{pairEntry.Number} must remain {pairEntry.Size}x{pairEntry.Size}; actual {original.Width}x{original.Height}.");
            result[pairEntry.Number] = new EditableImageEntrySource
            {
                ImageNumber = pairEntry.Number,
                StorageInfo = entryStorage,
                Snapshot = CaptureSourceSnapshot(entryTarget, original, entryStorage, Array.Empty<Color>(), string.Empty)
            };
        }
        return result;
    }

    private EditableImageSourceSnapshot CaptureSourceSnapshot(
        EditableImageTarget target,
        Bitmap bitmap,
        EditableImageStorageInfo storage,
        IReadOnlyList<Color> palette,
        string palettePath)
    {
        var originalArgb = CaptureArgb(bitmap);
        if (target.Kind == EditableImageTargetKind.DllBitmapIcon)
        {
            return new EditableImageSourceSnapshot
            {
                OriginalArgbPixels = originalArgb,
                Width = bitmap.Width,
                Height = bitmap.Height,
                ArchiveSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target.TargetPath)))
            };
        }

        var stored = _e5.ReadStoredEntryBytes(target.TargetPath, target.ImageNumber);
        var decoded = _e5.ReadEntryBytes(target.TargetPath, target.ImageNumber);
        var trailing = 0;
        if (storage.Kind == EditableImageStorageKind.Raw)
        {
            var probe = RsStripLayoutService.Probe(target.TargetPath, "RAW", decoded);
            if (!probe.IsSupportedLayout) throw new InvalidOperationException(probe.Detail);
            trailing = probe.TrailingByteCount;
        }

        return new EditableImageSourceSnapshot
        {
            StoredBytes = stored,
            DecodedBytes = decoded,
            OriginalArgbPixels = originalArgb,
            Width = bitmap.Width,
            Height = bitmap.Height,
            TrailingByteCount = trailing,
            ArchiveSha256 = _e5.ComputeArchiveSha256(target.TargetPath),
            IndexSha256 = _e5.ComputeIndexSha256(target.TargetPath),
            PaletteSha256 = ComputePaletteSha256(palette, palettePath),
            Palette = palette.ToArray()
        };
    }

    private static int[] CaptureArgb(Bitmap bitmap)
        => BitmapArgbSnapshot.Capture(bitmap);

    private static string ComputePaletteSha256(IReadOnlyList<Color> palette, string palettePath)
    {
        if (!string.IsNullOrWhiteSpace(palettePath) && File.Exists(palettePath))
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(palettePath)));
        if (palette.Count == 0) return string.Empty;
        var bytes = new byte[checked(palette.Count * sizeof(int))];
        for (var index = 0; index < palette.Count; index++)
            BitConverter.GetBytes(palette[index].ToArgb()).CopyTo(bytes, index * sizeof(int));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static void EnsureDllSnapshotUnchanged(EditableImageTarget target)
    {
        var expected = target.SourceSnapshot?.ArchiveSha256;
        if (string.IsNullOrWhiteSpace(expected))
            throw new InvalidOperationException("DLL pixel writeback is missing its source snapshot. Reload the icon before saving.");
        var current = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target.TargetPath)));
        if (!current.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The DLL changed after the pixel editor loaded it. Reload before saving.");
    }

    private static bool IsStandardImage(byte[] bytes)
        => bytes.Length >= 2 &&
           ((bytes[0] == (byte)'B' && bytes[1] == (byte)'M') ||
            (bytes[0] == 0xFF && bytes[1] == 0xD8) ||
            (bytes.Length >= PngMagic.Length && bytes.AsSpan(0, PngMagic.Length).SequenceEqual(PngMagic)));

    private static EditableImageTarget CloneTargetWithKind(
        EditableImageTarget target,
        EditableImageTargetKind kind,
        EditableImageStorageInfo? storage = null,
        EditableImageSourceSnapshot? sourceSnapshot = null,
        IReadOnlyDictionary<int, EditableImageEntrySource>? relatedEntrySources = null)
        => new()
        {
            Kind = kind,
            DisplayName = target.DisplayName,
            TargetPath = target.TargetPath,
            ImageNumber = target.ImageNumber,
            IconIndex = target.IconIndex,
            ResourceFormat = target.ResourceFormat,
            FrameWidth = target.FrameWidth,
            FrameHeight = target.FrameHeight,
            IsItemIconPair = target.IsItemIconPair,
            SmallImageNumber = target.SmallImageNumber,
            LargeImageNumber = target.LargeImageNumber,
            OperationKind = target.OperationKind,
            StorageInfo = storage ?? target.StorageInfo,
            SourceSnapshot = sourceSnapshot ?? target.SourceSnapshot,
            RelatedEntrySources = relatedEntrySources ?? target.RelatedEntrySources
        };

    private IReadOnlyList<Color> LoadRawPalette(CczProject project, out string palettePath)
    {
        var palette = _rawPalette.Load(project);
        palettePath = palette.Path;
        return palette.Colors;
    }

    private static Bitmap CloneArgb(Image raw)
    {
        var bitmap = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(raw, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        return bitmap;
    }

    private static void ApplyBackgroundKeyTransparency(Bitmap bitmap, Color key, int tolerance)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.A == 0 ||
                    Math.Abs(color.R - key.R) <= tolerance &&
                    Math.Abs(color.G - key.G) <= tolerance &&
                    Math.Abs(color.B - key.B) <= tolerance)
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
