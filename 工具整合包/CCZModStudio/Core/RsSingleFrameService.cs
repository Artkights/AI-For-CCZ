using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class RsFrameDescriptor : IDisposable
{
    public RsFrameResourceKind Kind { get; init; }
    public int ImageId { get; init; }
    public string TargetPath { get; init; } = string.Empty;
    public int ImageNumber { get; init; }
    public int StageSlot { get; init; } = 1;
    public int FactionSlot { get; init; } = 1;
    public string Group { get; init; } = string.Empty;
    public int PhysicalFrameIndex { get; init; }
    public Rectangle SourceRectangle { get; init; }
    public string DisplayLabel { get; init; } = string.Empty;
    public bool IsReadable { get; init; }
    public bool IsEditable { get; init; }
    public string Warning { get; init; } = string.Empty;
    public Bitmap? Bitmap { get; init; }
    public long SourceFileLength { get; init; }
    public long SourceFileWriteTicks { get; init; }
    public string SourceEntrySha256 { get; init; } = string.Empty;
    public bool SourceIsRaw { get; init; }
    public string SourceStorageKind { get; init; } = string.Empty;
    public EditableImageStorageInfo StorageInfo { get; init; } = new();
    public EditableImageSourceSnapshot SourceSnapshot { get; init; } = new();
    public CharacterImageTargetDescriptor? CharacterTarget { get; init; }

    public void Dispose() => Bitmap?.Dispose();
}

public sealed class RsFrameCatalog : IDisposable
{
    public string Title { get; init; } = string.Empty;
    public RsFrameResourceKind Kind { get; init; }
    public int ImageId { get; init; }
    public int JobId { get; init; } = -1;
    public int StageSlot { get; init; } = 1;
    public int FactionSlot { get; init; } = 1;
    public IReadOnlyList<int> AvailableStages { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> AvailableFactions { get; init; } = Array.Empty<int>();
    public IReadOnlyList<RsFrameDescriptor> Frames { get; init; } = Array.Empty<RsFrameDescriptor>();
    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();

    public void Dispose()
    {
        foreach (var frame in Frames) frame.Dispose();
    }
}

public sealed class RsSingleFrameCatalogService
{
    private readonly E5ImageReplaceService _entries = new();
    private readonly EditableImageCodecService _codec = new();
    private readonly EditableImageStorageService _storage = new();
    private readonly RsFrameLayoutResolver _layouts = new();

    public RsFrameCatalog BuildResourceEntry(CczProject project, string path, int imageNumber, string displayName)
    {
        var layout = _layouts.Resolve(path);
        var frames = new List<RsFrameDescriptor>();
        var messages = new List<string>();
        var group = layout.FileName switch
        {
            "Pmapobj.e5" => imageNumber % 2 == 1 ? "正图" : "反图",
            "Unit_mov.e5" => "移动",
            "Unit_atk.e5" => "攻击",
            "Unit_spc.e5" => "特技",
            _ => Path.GetFileName(path)
        };
        AddFrames(project, frames, messages, layout.ResourceKind, -1, path, imageNumber, 1, 1, group);
        return new RsFrameCatalog
        {
            Kind = layout.ResourceKind,
            ImageId = -1,
            Title = string.IsNullOrWhiteSpace(displayName)
                ? $"{layout.FileName} #{imageNumber}"
                : displayName,
            Frames = frames,
            Messages = messages
        };
    }

    public RsFrameCatalog BuildR(CczProject project, int rImageId, string displayName)
    {
        if (rImageId < 0) throw new InvalidOperationException($"R 形象编号不能小于 0：{rImageId}");
        var path = CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5");
        var frames = new List<RsFrameDescriptor>();
        var messages = new List<string>();
        AddFrames(project, frames, messages, RsFrameResourceKind.R, rImageId, path, checked(rImageId * 2 + 1), 1, 1, "正图");
        AddFrames(project, frames, messages, RsFrameResourceKind.R, rImageId, path, checked(rImageId * 2 + 2), 1, 1, "反图");
        return new RsFrameCatalog
        {
            Kind = RsFrameResourceKind.R,
            ImageId = rImageId,
            Title = $"R{rImageId} {displayName}".Trim(),
            Frames = frames,
            Messages = messages
        };
    }

    public RsFrameCatalog BuildS(CczProject project, int sImageId, int? jobId, int factionSlot, int stageSlot, string displayName)
    {
        if (sImageId < 0) throw new InvalidOperationException($"S 形象编号不能小于 0：{sImageId}");
        factionSlot = CharacterImageResourceService.NormalizeSPreviewFactionSlot(factionSlot);
        var stageResolution = CharacterImageResourceService.ResolveSPreviewStage(
            project,
            sImageId,
            jobId,
            factionSlot,
            stageSlot);
        var mapping = stageResolution.Mapping;
        var availableStages = CharacterImageResourceService.GetAvailableSImageStageSlots(project, sImageId);
        if (availableStages.Count == 0) availableStages = [1];
        var stage = stageResolution.Target;
        var frames = new List<RsFrameDescriptor>();
        var messages = new List<string>();
        if (stageResolution.IsOneStageFallback)
        {
            messages.Add(stageResolution.FallbackDetail);
        }
        if (stage == null)
        {
            messages.Add("未解析到 S 形象 Unit 图号：" + mapping.Detail);
            AddFrames(project, frames, messages, RsFrameResourceKind.S, sImageId,
                CharacterImageResourceService.ResolveGameFile(project, "Unit_mov.e5"), 0, stageResolution.EffectiveStageSlot, factionSlot, "移动", jobId);
            AddFrames(project, frames, messages, RsFrameResourceKind.S, sImageId,
                CharacterImageResourceService.ResolveGameFile(project, "Unit_atk.e5"), 0, stageResolution.EffectiveStageSlot, factionSlot, "攻击", jobId);
            AddFrames(project, frames, messages, RsFrameResourceKind.S, sImageId,
                CharacterImageResourceService.ResolveGameFile(project, "Unit_spc.e5"), 0, stageResolution.EffectiveStageSlot, factionSlot, "特技", jobId);
        }
        else
        {
            AddFrames(project, frames, messages, RsFrameResourceKind.S, sImageId,
                CharacterImageResourceService.ResolveGameFile(project, "Unit_mov.e5"), stage.ImageNumber, stage.StageSlot, factionSlot, "移动", jobId);
            AddFrames(project, frames, messages, RsFrameResourceKind.S, sImageId,
                CharacterImageResourceService.ResolveGameFile(project, "Unit_atk.e5"), stage.ImageNumber, stage.StageSlot, factionSlot, "攻击", jobId);
            AddFrames(project, frames, messages, RsFrameResourceKind.S, sImageId,
                CharacterImageResourceService.ResolveGameFile(project, "Unit_spc.e5"), stage.ImageNumber, stage.StageSlot, factionSlot, "特技", jobId);
        }

        return new RsFrameCatalog
        {
            Kind = RsFrameResourceKind.S,
            ImageId = sImageId,
            JobId = jobId ?? -1,
            StageSlot = stageResolution.EffectiveStageSlot,
            FactionSlot = factionSlot,
            Title = $"S{sImageId} {displayName}".Trim(),
            AvailableStages = availableStages,
            AvailableFactions = sImageId == 0 ? [1, 2, 3] : Array.Empty<int>(),
            Frames = frames,
            Messages = messages.Append(mapping.Detail).ToArray()
        };
    }

    private void AddFrames(
        CczProject project,
        List<RsFrameDescriptor> output,
        List<string> messages,
        RsFrameResourceKind kind,
        int imageId,
        string path,
        int imageNumber,
        int stageSlot,
        int factionSlot,
        string group,
        int? jobId = null)
    {
        var layout = _layouts.Resolve(path);
        Bitmap? strip = null;
        byte[]? entryBytes = null;
        string warning = string.Empty;
        long fileLength = 0;
        long fileTicks = 0;
        var isRaw = false;
        var storageKind = string.Empty;
        var storageInfo = new EditableImageStorageInfo
        {
            Kind = EditableImageStorageKind.Unknown,
            ReadOnlyReason = "条目尚未成功读取。"
        };
        var sourceSnapshot = new EditableImageSourceSnapshot();
        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException("资源文件不存在。", path);
            var fileInfo = new FileInfo(path);
            fileLength = fileInfo.Length;
            fileTicks = fileInfo.LastWriteTimeUtc.Ticks;
            var index = _entries.ReadIndex(path);
            if (imageNumber <= 0 || imageNumber > index.Count)
                throw new InvalidOperationException($"E5 图号越界：#{imageNumber}/{index.Count}。");
            storageKind = index[imageNumber - 1].Kind;
            entryBytes = _entries.ReadEntryBytes(path, imageNumber);
            var stripProbe = RsStripLayoutService.Probe(path, storageKind, entryBytes);
            var target = BuildTarget(path, imageNumber, $"{kind}{imageId} {group}");
            storageInfo = _storage.Inspect(target);
            isRaw = storageInfo.Kind == EditableImageStorageKind.Raw;
            if (!stripProbe.IsSupportedLayout)
                throw new InvalidOperationException(stripProbe.Detail);
            using var document = _codec.Load(project, target);
            strip = new Bitmap(document.Bitmap);
            sourceSnapshot = document.SourceSnapshot;
            warning = stripProbe.TrailingByteCount > 0
                ? $"条目含 {stripProbe.TrailingByteCount} 字节容器尾部；预览忽略像素区之外的数据，保存时原样保留。"
                : string.Empty;
        }
        catch (Exception ex)
        {
            warning = ex.Message;
            messages.Add($"{group} #{imageNumber}：{warning}");
        }

        try
        {
            var widthIsValid = strip?.Width == layout.FrameWidth;
            var availableFrameCount = widthIsValid ? strip!.Height / layout.FrameHeight : 0;
            var characterTarget = ResolveCharacterTarget(
                project, kind, imageId, jobId, stageSlot, factionSlot, path, group);
            for (var index = 0; index < layout.ExpectedFrameCount; index++)
            {
                Bitmap? frame = null;
                var readable = strip != null && index < availableFrameCount;
                if (readable)
                {
                    frame = RsStripLayoutService.CropFrame(strip!, layout, index);
                }

                var editReason = storageInfo.CanEdit ? string.Empty : storageInfo.ReadOnlyReason;
                var frameWarning = readable
                    ? string.Join("；", new[] { warning, editReason }.Where(text => !string.IsNullOrWhiteSpace(text)))
                    : string.IsNullOrWhiteSpace(warning) ? "该物理帧不存在或条带高度不足。" : warning;
                output.Add(new RsFrameDescriptor
                {
                    Kind = kind,
                    ImageId = imageId,
                    TargetPath = path,
                    ImageNumber = imageNumber,
                    StageSlot = stageSlot,
                    FactionSlot = factionSlot,
                    Group = group,
                    PhysicalFrameIndex = index,
                    SourceRectangle = new Rectangle(0, index * layout.FrameHeight, layout.FrameWidth, layout.FrameHeight),
                    DisplayLabel = BuildFrameLabel(kind, group, index),
                    IsReadable = readable,
                    IsEditable = readable && widthIsValid && storageInfo.CanEdit,
                    Warning = frameWarning,
                    Bitmap = frame,
                    SourceFileLength = fileLength,
                    SourceFileWriteTicks = fileTicks,
                    SourceEntrySha256 = storageInfo.EntrySha256,
                    SourceIsRaw = isRaw,
                    SourceStorageKind = storageKind,
                    StorageInfo = storageInfo,
                    SourceSnapshot = sourceSnapshot,
                    CharacterTarget = characterTarget
                });
            }
        }
        finally
        {
            strip?.Dispose();
        }
    }

    private static CharacterImageTargetDescriptor? ResolveCharacterTarget(
        CczProject project,
        RsFrameResourceKind kind,
        int imageId,
        int? jobId,
        int stageSlot,
        int factionSlot,
        string path,
        string group)
    {
        if (imageId < 0) return null;
        if (kind == RsFrameResourceKind.R)
            return CharacterImageTargetResolver.ResolveR(
                project, imageId, group.Contains("反", StringComparison.Ordinal), group);
        if (kind != RsFrameResourceKind.S) return null;
        return CharacterImageTargetResolver.ResolveS(
            project,
            imageId,
            jobId,
            factionSlot,
            stageSlot,
            Path.GetFileName(path),
            group);
    }

    internal static EditableImageTarget BuildTarget(string path, int imageNumber, string displayName)
    {
        var layout = new RsFrameLayoutResolver().Resolve(path);
        return new EditableImageTarget
        {
            Kind = EditableImageTargetKind.E5RawStrip,
            DisplayName = displayName,
            TargetPath = path,
            ImageNumber = imageNumber,
            ResourceFormat = "E5 R/S 帧条",
            FrameWidth = layout.FrameWidth,
            FrameHeight = layout.FrameHeight,
            OperationKind = "R/S 单帧像素编辑"
        };
    }

    private static string BuildFrameLabel(RsFrameResourceKind kind, string group, int index)
    {
        if (kind == RsFrameResourceKind.R)
        {
            return index switch
            {
                0 => "物理帧0 / 普通",
                1 => "物理帧1 / 移动1",
                2 => "物理帧2 / 移动2",
                _ => $"物理帧{index}（语义待验证）"
            };
        }

        var sourceFile = group switch
        {
            "移动" => "Unit_mov.e5",
            "攻击" => "Unit_atk.e5",
            "特技" => "Unit_spc.e5",
            _ => string.Empty
        };
        return string.IsNullOrWhiteSpace(sourceFile)
            ? $"物理帧{index}（语义未定义）"
            : RsActionSequenceCatalog.BuildPhysicalFrameLabel(sourceFile, index);
    }

    private static bool IsStandardImage(byte[] bytes)
        => bytes.Length >= 2 &&
           ((bytes[0] == (byte)'B' && bytes[1] == (byte)'M') ||
            (bytes[0] == 0xFF && bytes[1] == 0xD8) ||
            (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G'));
}

public sealed class RsSingleFrameWritePreview : IDisposable
{
    public required RsFrameDescriptor Descriptor { get; init; }
    public required byte[] SourceBytes { get; init; }
    public required bool SourceBytesAreRaw { get; init; }
    public required E5ImageBatchReplacePreviewResult E5Preview { get; init; }
    public required Bitmap ExpectedStoredFrame { get; init; }
    public int ChangedPixels { get; init; }
    public int TransparentDelta { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public string BuildText()
        => $"R/S 单帧写回预览\r\n" +
           $"资源：{Path.GetFileName(Descriptor.TargetPath)} #{Descriptor.ImageNumber}\r\n" +
           $"帧：{Descriptor.Group} / 物理索引 {Descriptor.PhysicalFrameIndex} / {Descriptor.DisplayLabel} / {Descriptor.SourceRectangle.Width}x{Descriptor.SourceRectangle.Height}\r\n" +
           $"修改像素：{ChangedPixels:N0}\r\n" +
           $"透明像素变化：{TransparentDelta:+#;-#;0}\r\n" +
           $"存储：{BuildStorageText()}\r\n" +
           (Warnings.Count == 0 ? string.Empty : "提示：\r\n" + string.Join("\r\n", Warnings.Select(x => "- " + x)));

    public void Dispose() => ExpectedStoredFrame.Dispose();

    private string BuildStorageText()
        => Descriptor.StorageInfo.Kind switch
        {
            EditableImageStorageKind.Raw => "保持 RAW；仅替换当前帧字节区间，其他字节逐字节不变",
            EditableImageStorageKind.Bmp24 => "保持 24 位 BI_RGB BMP；透明像素恢复为原背景键，容器与条目长度不变",
            EditableImageStorageKind.Png => "保持 PNG 和原偏移；编码结果超过原槽位时拒绝普通像素保存",
            _ => Descriptor.StorageInfo.ReadOnlyReason
        };
}

public sealed class RsSingleFrameEditWriteService
{
    private readonly E5ImageReplaceService _e5 = new();
    private readonly E5RawImageCodec _rawCodec = new();
    private readonly E5RawPaletteService _palette = new();
    private readonly EditableImageStorageService _storage = new();
    private readonly EditableImageCodecService _editableCodec = new();
    public RsSingleFrameWritePreview Preview(CczProject project, RsFrameDescriptor descriptor, Bitmap editedFrame)
    {
        ValidateDescriptor(descriptor, editedFrame);
        EnsureResourceUnchanged(descriptor);
        var changed = CountChangedPixels(descriptor.Bitmap!, editedFrame, out var transparentDelta);
        if (changed == 0)
            throw new InvalidOperationException("No pixel changes were detected; the E5 archive was not written.");
        var currentBytes = _e5.ReadEntryBytes(descriptor.TargetPath, descriptor.ImageNumber);
        var warnings = new List<string>();
        byte[] sourceBytes;
        Bitmap expectedStoredFrame;
        if (descriptor.SourceIsRaw)
        {
            var spec = _rawCodec.ResolveSpec(descriptor.TargetPath);
            var offset = checked(descriptor.PhysicalFrameIndex * spec.Width * spec.FrameHeight);
            var frameLength = checked(spec.Width * spec.FrameHeight);
            if (offset < 0 || offset + frameLength > currentBytes.Length)
                throw new InvalidOperationException("The RAW physical-frame byte range is outside the entry.");
            EnsurePaletteUnchanged(project, descriptor.SourceSnapshot);
            var originalFrameBytes = currentBytes.AsSpan(offset, frameLength).ToArray();
            var encoded = _rawCodec.EncodeBitmapPreservingIndices(
                editedFrame,
                descriptor.DisplayLabel,
                new E5RawImageSpec(spec.FileName, spec.Width, spec.FrameHeight, StrictStripHeight: null),
                descriptor.SourceSnapshot.Palette,
                _palette.Load(project).Path,
                originalFrameBytes,
                CaptureArgb(descriptor.Bitmap!),
                trailingByteCount: 0);
            warnings.AddRange(encoded.Warnings);
            sourceBytes = currentBytes.ToArray();
            if (offset < 0 || offset + encoded.RawBytes.Length > sourceBytes.Length)
                throw new InvalidOperationException("当前物理帧的 RAW 字节区间超出条目边界。已拒绝写回。");
            Buffer.BlockCopy(encoded.RawBytes, 0, sourceBytes, offset, encoded.RawBytes.Length);
            VerifyRawBytesOutsideFrameUnchanged(currentBytes, sourceBytes, offset, encoded.RawBytes.Length);
            expectedStoredFrame = _rawCodec.DecodeRawBytes(
                project,
                encoded.RawBytes,
                descriptor.DisplayLabel,
                new E5RawImageSpec(spec.FileName, spec.Width, spec.FrameHeight, StrictStripHeight: null),
                trimToWholeRows: false);
        }
        else if (descriptor.StorageInfo.Kind is EditableImageStorageKind.Bmp24 or EditableImageStorageKind.Png)
        {
            var target = RsSingleFrameCatalogService.BuildTarget(descriptor.TargetPath, descriptor.ImageNumber, descriptor.DisplayLabel);
            using var document = _editableCodec.Load(project, target);
            if (document.Bitmap.Width != descriptor.SourceRectangle.Width ||
                document.Bitmap.Height < descriptor.SourceRectangle.Bottom)
                throw new InvalidOperationException("原始条带尺寸已不足以容纳当前物理帧，已拒绝写回。");
            using var merged = new Bitmap(document.Bitmap);
            using (var graphics = Graphics.FromImage(merged))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.DrawImageUnscaled(editedFrame, descriptor.SourceRectangle.Location);
            }
            if (descriptor.StorageInfo.Kind == EditableImageStorageKind.Bmp24)
            {
                sourceBytes = _storage.EncodeBmp24PreservingUnchangedPixels(
                    descriptor.StorageInfo,
                    currentBytes,
                    merged,
                    document.SourceSnapshot.OriginalArgbPixels);
            }
            else
            {
                using var memory = new MemoryStream();
                merged.Save(memory, ImageFormat.Png);
                sourceBytes = memory.ToArray();
            }
            VerifyStandardImageOutsideFrameUnchanged(document.Bitmap, sourceBytes, descriptor.SourceRectangle, editedFrame);
            expectedStoredFrame = CloneNormalizedFrameFromEncodedStrip(sourceBytes, descriptor.SourceRectangle);
        }
        else throw new InvalidOperationException(descriptor.StorageInfo.ReadOnlyReason);

        var request = BuildRequest(descriptor, sourceBytes);
        E5ImageBatchReplacePreviewResult e5Preview;
        try
        {
            e5Preview = _e5.PreviewBatchReplacement(project, descriptor.TargetPath, [request]);
        }
        catch
        {
            expectedStoredFrame.Dispose();
            throw;
        }
        return new RsSingleFrameWritePreview
        {
            Descriptor = descriptor,
            SourceBytes = sourceBytes,
            SourceBytesAreRaw = descriptor.SourceIsRaw,
            E5Preview = e5Preview,
            ExpectedStoredFrame = expectedStoredFrame,
            ChangedPixels = changed,
            TransparentDelta = transparentDelta,
            Warnings = warnings.Concat(e5Preview.FormatWarnings).Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    public E5ImageBatchReplaceResult Write(CczProject project, RsSingleFrameWritePreview preview)
    {
        EnsureResourceUnchanged(preview.Descriptor);
        var request = BuildRequest(preview.Descriptor, preview.SourceBytes);
        var result = _e5.ReplaceBatch(project, preview.Descriptor.TargetPath, [request]);
        try
        {
            VerifyWrittenFrame(project, preview);
        }
        catch
        {
            _e5.RestoreVerifiedBackup(result.BackupPath, result.TargetPath, result.OldFileSha256);
            throw;
        }
        return result;
    }

    public EditableImageDocument CreateEditDocument(CczProject project, RsFrameDescriptor descriptor)
    {
        if (!descriptor.IsEditable || descriptor.Bitmap == null) throw new InvalidOperationException(descriptor.Warning);
        var target = new EditableImageTarget
        {
            Kind = EditableImageTargetKind.E5Standard,
            DisplayName = $"{descriptor.Kind}{descriptor.ImageId} {descriptor.Group} {descriptor.DisplayLabel} / E5#{descriptor.ImageNumber}",
            TargetPath = descriptor.TargetPath,
            ImageNumber = descriptor.ImageNumber,
            ResourceFormat = "R/S 单帧",
            OperationKind = "R/S 单帧像素编辑",
            CharacterTarget = descriptor.CharacterTarget
        };
        using var sourceDocument = _editableCodec.Load(project, RsSingleFrameCatalogService.BuildTarget(
            descriptor.TargetPath, descriptor.ImageNumber, descriptor.DisplayLabel));
        return new EditableImageDocument
        {
            Target = target,
            Bitmap = new Bitmap(descriptor.Bitmap),
            OriginalBitmap = new Bitmap(descriptor.Bitmap),
            Palette = sourceDocument.Palette.ToArray(),
            PalettePath = sourceDocument.PalettePath,
            RestrictToPalette = descriptor.StorageInfo.Kind == EditableImageStorageKind.Raw && sourceDocument.Palette.Count > 0,
            PaletteRole = descriptor.StorageInfo.Kind == EditableImageStorageKind.Raw ? "R/S RAW storage palette" : string.Empty,
            LoadDetail = $"单帧模式；{Path.GetFileName(descriptor.TargetPath)} #{descriptor.ImageNumber}；{descriptor.Group}；{descriptor.DisplayLabel}；原存储 {descriptor.StorageInfo.DisplayKind}",
            StorageInfo = descriptor.StorageInfo
        };
    }

    private static E5ImageBatchReplaceRequest BuildRequest(RsFrameDescriptor descriptor, byte[] sourceBytes)
        => new()
        {
            ImageNumber = descriptor.ImageNumber,
            SourceBytes = sourceBytes,
            SourceBytesAreRaw = descriptor.StorageInfo.Kind == EditableImageStorageKind.Raw,
            SourceLabel = $"{descriptor.Group} {descriptor.DisplayLabel}",
            OperationKind = "R/S 单帧像素编辑",
            ExpectedTargetKind = descriptor.StorageInfo.ExpectedE5Kind,
            ExpectedTargetSha256 = descriptor.StorageInfo.EntrySha256,
            ExpectedArchiveSha256 = descriptor.SourceSnapshot.ArchiveSha256,
            ExpectedIndexSha256 = descriptor.SourceSnapshot.IndexSha256,
            PlacementPolicy = descriptor.StorageInfo.Kind == EditableImageStorageKind.Png
                ? E5ImageWritePlacementPolicy.RequireStableOffset
                : E5ImageWritePlacementPolicy.RequireExactInPlace,
            CharacterTarget = descriptor.CharacterTarget
        };

    private static void VerifyStandardImageOutsideFrameUnchanged(
        Bitmap originalStrip,
        byte[] encodedBytes,
        Rectangle frameRectangle,
        Bitmap editedFrame)
    {
        using var memory = new MemoryStream(encodedBytes, writable: false);
        using var decodedImage = Image.FromStream(memory, useEmbeddedColorManagement: false, validateImageData: true);
        using var decoded = new Bitmap(decodedImage);
        if (decoded.Size != originalStrip.Size)
            throw new InvalidOperationException("标准图片单帧合并后的条带尺寸发生变化，已拒绝写回。");

        for (var y = 0; y < originalStrip.Height; y++)
        for (var x = 0; x < originalStrip.Width; x++)
        {
            var actual = NormalizeTransparent(decoded.GetPixel(x, y));
            if (frameRectangle.Contains(x, y))
            {
                var expected = NormalizeTransparent(editedFrame.GetPixel(x - frameRectangle.X, y - frameRectangle.Y));
                if (actual.ToArgb() != expected.ToArgb())
                    throw new InvalidOperationException("标准图片编码复读后当前帧与编辑结果不一致，已拒绝写回。");
                continue;
            }

            var original = NormalizeTransparent(originalStrip.GetPixel(x, y));
            if (actual.ToArgb() != original.ToArgb())
                throw new InvalidOperationException("标准图片单帧合并改变了目标帧以外的解码像素，已拒绝写回。");
        }
    }

    private static void VerifyRawBytesOutsideFrameUnchanged(byte[] before, byte[] after, int offset, int length)
    {
        if (before.Length != after.Length)
            throw new InvalidOperationException("RAW 单帧合并改变了条目总字节长度，已拒绝写回。");
        for (var index = 0; index < before.Length; index++)
        {
            if (index >= offset && index < offset + length) continue;
            if (before[index] != after[index])
                throw new InvalidOperationException("RAW 单帧合并改变了目标帧以外的字节，已拒绝写回。");
        }
    }

    private void VerifyWrittenFrame(CczProject project, RsSingleFrameWritePreview preview)
    {
        var descriptor = preview.Descriptor;
        var target = RsSingleFrameCatalogService.BuildTarget(descriptor.TargetPath, descriptor.ImageNumber, descriptor.DisplayLabel);
        using var document = _editableCodec.Load(project, target);
        if (document.Bitmap.Width < descriptor.SourceRectangle.Right ||
            document.Bitmap.Height < descriptor.SourceRectangle.Bottom)
            throw new InvalidOperationException("单帧写回后复读条带尺寸不足，无法裁出目标物理帧。");
        using var actual = CloneFrame(document.Bitmap, descriptor.SourceRectangle);
        if (!PixelsEqualNormalized(actual, preview.ExpectedStoredFrame))
            throw new InvalidOperationException("单帧写回后复读校验失败：目标物理帧与预期存储结果不一致。备份和失败报告已保留。");
    }

    private static Bitmap CloneNormalizedFrameFromEncodedStrip(byte[] bytes, Rectangle rectangle)
    {
        using var memory = new MemoryStream(bytes, writable: false);
        using var image = Image.FromStream(memory, useEmbeddedColorManagement: false, validateImageData: true);
        using var strip = new Bitmap(image);
        return CloneFrame(strip, rectangle);
    }

    private static Bitmap CloneFrame(Bitmap strip, Rectangle rectangle)
    {
        var frame = new Bitmap(rectangle.Width, rectangle.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(frame);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.DrawImage(strip, new Rectangle(Point.Empty, frame.Size), rectangle, GraphicsUnit.Pixel);
        return frame;
    }

    private static bool PixelsEqualNormalized(Bitmap left, Bitmap right)
    {
        if (left.Size != right.Size) return false;
        for (var y = 0; y < left.Height; y++)
        for (var x = 0; x < left.Width; x++)
        {
            if (NormalizeTransparent(left.GetPixel(x, y)).ToArgb() !=
                NormalizeTransparent(right.GetPixel(x, y)).ToArgb()) return false;
        }
        return true;
    }

    private static Color NormalizeTransparent(Color color)
        => color.A == 0 || (color.R >= 247 && color.G <= 8 && color.B >= 248)
            ? Color.Transparent
            : color;

    private static int CountChangedPixels(Bitmap before, Bitmap after, out int transparentDelta)
    {
        var changed = 0;
        var beforeTransparent = 0;
        var afterTransparent = 0;
        for (var y = 0; y < before.Height; y++)
        for (var x = 0; x < before.Width; x++)
        {
            var a = before.GetPixel(x, y);
            var b = after.GetPixel(x, y);
            if (a.A == 0) beforeTransparent++;
            if (b.A == 0) afterTransparent++;
            if (a.ToArgb() != b.ToArgb() && !(a.A == 0 && b.A == 0)) changed++;
        }
        transparentDelta = afterTransparent - beforeTransparent;
        return changed;
    }

    private void EnsurePaletteUnchanged(CczProject project, EditableImageSourceSnapshot snapshot)
    {
        var palette = _palette.Load(project);
        string currentSha;
        if (!string.IsNullOrWhiteSpace(palette.Path) && File.Exists(palette.Path))
        {
            currentSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(palette.Path)));
        }
        else
        {
            var bytes = new byte[checked(palette.Colors.Count * sizeof(int))];
            for (var index = 0; index < palette.Colors.Count; index++)
                BitConverter.GetBytes(palette.Colors[index].ToArgb()).CopyTo(bytes, index * sizeof(int));
            currentSha = Convert.ToHexString(SHA256.HashData(bytes));
        }
        if (!currentSha.Equals(snapshot.PaletteSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The RAW palette changed after this frame was loaded. Reload before saving.");
    }

    private static int[] CaptureArgb(Bitmap bitmap)
    {
        var pixels = new int[checked(bitmap.Width * bitmap.Height)];
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            pixels[y * bitmap.Width + x] = bitmap.GetPixel(x, y).ToArgb();
        return pixels;
    }

    private static void ValidateDescriptor(RsFrameDescriptor descriptor, Bitmap editedFrame)
    {
        if (!descriptor.IsEditable || descriptor.Bitmap == null) throw new InvalidOperationException(descriptor.Warning);
        if (editedFrame.Size != descriptor.SourceRectangle.Size)
            throw new InvalidOperationException($"单帧尺寸必须保持 {descriptor.SourceRectangle.Width}x{descriptor.SourceRectangle.Height}，当前为 {editedFrame.Width}x{editedFrame.Height}。");
    }

    private void EnsureResourceUnchanged(RsFrameDescriptor descriptor)
    {
        var info = new FileInfo(descriptor.TargetPath);
        if (!info.Exists || info.Length != descriptor.SourceFileLength || info.LastWriteTimeUtc.Ticks != descriptor.SourceFileWriteTicks)
            throw new InvalidOperationException("编辑期间目标 E5 文件已被修改。请关闭编辑器并重新载入单帧后再保存。");
        var bytes = _e5.ReadStoredEntryBytes(descriptor.TargetPath, descriptor.ImageNumber);
        var sha = Convert.ToHexString(SHA256.HashData(bytes));
        if (!sha.Equals(descriptor.SourceEntrySha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("编辑期间目标 E5 条目内容已变化。请重新载入单帧后再保存。");
    }
}
