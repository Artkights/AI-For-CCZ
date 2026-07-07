using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// Preview for person face/R/S image assignments.
/// Face uses the tutorial Data->Face.e5 mapping; R/S are explained through Pmapobj.e5 and Unit_*.e5.
/// </summary>
public sealed class ImageAssignmentPreviewService
{
    private const int PreviewWidth = 420;
    private const int PreviewHeight = 300;
    private const int E5ImageIndexOffset = 0x110;
    private const int E5ImageIndexEntrySize = 12;
    private const int LsHeaderLength = 16;
    private const int LsDictionaryLength = 256;
    private readonly ConcurrentDictionary<string, IReadOnlyList<E5ImageEntry>> _e5ImageIndexCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RawPalette> _rawPaletteCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<int>> _availableImageIdCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly CharacterImageLayoutService _layoutService = new();
    private readonly E5RawPaletteService _paletteService = new();

    public void ClearCache()
    {
        _e5ImageIndexCache.Clear();
        _rawPaletteCache.Clear();
        _availableImageIdCache.Clear();
    }

    public Bitmap RenderResourcePreview(CczProject project, string prefix, int id, string personName, int? faceId = null)
    {
        prefix = NormalizePrefix(prefix);
        var resolver = new CharacterImageResourceService();
        var status = prefix == "S" ? resolver.BuildSStatus(project, id) : resolver.BuildRStatus(project, id);
        var title = prefix == "S" ? $"S 形象 {id}" : $"R 形象 {id}";
        var bitmap = new Bitmap(PreviewWidth, PreviewHeight, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(28, 30, 32));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        using var baseFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        using var titleFont = new Font(baseFont, FontStyle.Bold);
        using var normalFont = new Font(baseFont, FontStyle.Regular);
        using var smallFont = new Font(baseFont.FontFamily, 8, FontStyle.Regular);
        using var titleBrush = new SolidBrush(Color.White);
        using var textBrush = new SolidBrush(Color.Gainsboro);
        using var warnBrush = new SolidBrush(Color.FromArgb(255, 210, 120));
        using var missingBrush = new SolidBrush(Color.FromArgb(255, 140, 140));
        using var accentBrush = new SolidBrush(Color.FromArgb(122, 184, 255));
        using var borderPen = new Pen(Color.FromArgb(110, 120, 128));

        var faceCaption = string.Empty;
        var faceBitmap = faceId.HasValue ? TryLoadMappedFaceImage(project, faceId.Value, out faceCaption) : null;
        var imageBitmap = TryLoadMappedE5Image(project, prefix, id, null, CharacterImageResourceService.DefaultSPreviewFactionSlot, out var imageCaption);
        try
        {
            g.DrawString($"{personName}  {title}", titleFont, titleBrush, 12, 10);
            if (faceBitmap != null)
            {
                using var faceBack = new SolidBrush(Color.FromArgb(16, 18, 20));
                g.FillRectangle(faceBack, 12, 48, 104, 104);
                g.DrawRectangle(borderPen, 12, 48, 104, 104);
                g.DrawImage(faceBitmap, new Rectangle(14, 50, 100, 100));
                g.DrawString(faceCaption, smallFont, accentBrush, 18, 156);
            }

            var textureRect = faceBitmap == null
                ? new Rectangle(12, 48, bitmap.Width - 24, 138)
                : new Rectangle(124, 48, bitmap.Width - 136, 138);

            using var fill = new SolidBrush(CharacterImageResourceService.IsMissingStatus(status.Status)
                ? Color.FromArgb(86, 37, 37)
                : Color.FromArgb(38, 54, 68));
            g.FillRectangle(fill, textureRect);
            g.DrawRectangle(borderPen, textureRect);

            if (imageBitmap != null)
            {
                var imageRect = new Rectangle(textureRect.Left + 6, textureRect.Top + 6, textureRect.Width - 12, textureRect.Height - 12);
                DrawCenteredImage(g, imageBitmap, imageRect, borderPen);
                if (!string.IsNullOrWhiteSpace(imageCaption))
                {
                    g.DrawString(imageCaption, smallFont, accentBrush, textureRect.Left + 10, textureRect.Bottom - 18);
                }
            }
            else
            {
                var statusBrush = CharacterImageResourceService.IsMissingStatus(status.Status) ? missingBrush : accentBrush;
                g.DrawString(status.Status, titleFont, statusBrush, textureRect.Left + 14, textureRect.Top + 12);
                g.DrawString(status.ResourceName, normalFont, titleBrush, new RectangleF(textureRect.Left + 14, textureRect.Top + 42, textureRect.Width - 28, 28));
                g.DrawString(status.Detail, smallFont, textBrush, new RectangleF(textureRect.Left + 14, textureRect.Top + 74, textureRect.Width - 28, 54));
            }

            var fileText = ResourcePathExists(status.Path)
                ? $"已找到：{status.Path}"
                : $"未找到：{status.Path}";
            g.DrawString(fileText, smallFont, CharacterImageResourceService.IsMissingStatus(status.Status) ? warnBrush : textBrush,
                new RectangleF(18, 194, bitmap.Width - 36, 36));
            g.DrawString(prefix == "R"
                    ? "读取口径：R 形象号 n -> Pmapobj.e5 索引图 2n+1；索引表从 0x110 开始。"
                    : "读取口径：S 形象号 n -> Unit_atk/mov/spc.e5 索引图 n；索引表从 0x110 开始。",
                smallFont,
                warnBrush,
                new RectangleF(18, 236, bitmap.Width - 36, 44));
            return bitmap;
        }
        finally
        {
            faceBitmap?.Dispose();
            imageBitmap?.Dispose();
        }
    }

    public Bitmap? TryRenderFaceImage(CczProject project, int dataFaceId)
    {
        return TryLoadMappedFaceImage(project, dataFaceId, out _);
    }

    public Bitmap? TryRenderCharacterResourceImage(CczProject project, string prefix, int id)
        => TryRenderCharacterResourceImage(project, prefix, id, jobId: null, sFactionSlot: CharacterImageResourceService.DefaultSPreviewFactionSlot);

    public Bitmap? TryRenderCharacterResourceImage(CczProject project, string prefix, int id, int? jobId, int sFactionSlot)
    {
        prefix = NormalizePrefix(prefix);
        return TryLoadMappedE5Image(project, prefix, id, jobId, sFactionSlot, out _);
    }

    public IReadOnlyList<int> GetAvailableCharacterImageIds(CczProject project, string prefix, bool includeZero = false)
        => GetAvailableCharacterImageIds(project, prefix, includeZero, out _);

    public IReadOnlyList<int> GetAvailableCharacterImageIds(CczProject project, string prefix, bool includeZero, out bool fromCache)
    {
        fromCache = false;
        var cacheKey = BuildAvailableImageIdCacheKey(project, prefix, includeZero);
        if (_availableImageIdCache.TryGetValue(cacheKey, out var cached))
        {
            fromCache = true;
            return cached;
        }

        IReadOnlyList<int> result;
        if (prefix.Equals("Face", StringComparison.OrdinalIgnoreCase))
        {
            result = GetAvailableFaceIds(project, includeZero);
        }
        else
        {
            prefix = NormalizePrefix(prefix);
            result = prefix == "S"
                ? GetAvailableSImageIds(project, includeZero)
                : GetAvailableRImageIds(project, includeZero);
        }

        _availableImageIdCache[cacheKey] = result;
        return result;
    }

    public Bitmap? TryRenderSImageFactionStackPreview(CczProject project, int sImageId, int? jobId, out string detail)
    {
        var rows = new List<SImageFactionPreviewRow>();
        var detailLines = new List<string>();
        try
        {
            foreach (var slot in Enumerable.Range(1, 3))
            {
                var factionText = CharacterImageResourceService.BuildSPreviewFactionText(slot);
                var preview = TryRenderSImage(project, sImageId, jobId, slot, out var caption);
                rows.Add(new SImageFactionPreviewRow(factionText, slot, preview, caption));
                detailLines.Add($"{factionText}：{caption}");
            }

            detail = string.Join("\r\n", detailLines);
            return ComposeSImageFactionStackPreview(rows);
        }
        finally
        {
            foreach (var row in rows)
            {
                row.Image?.Dispose();
            }
        }
    }

    public Bitmap? TryRenderRSceneFrame(CczProject project, int rImageId, string facing, int stanceGroup, out string detail)
    {
        detail = string.Empty;
        if (rImageId < 0)
        {
            detail = $"R 形象编号 {rImageId} 无效。";
            return null;
        }

        var normalizedFacing = NormalizeRSceneFacing(facing);
        var gestureIndex = NormalizeRSceneGestureIndex(stanceGroup);
        var frameMapping = ResolveRSceneFrameMapping(rImageId, normalizedFacing, gestureIndex);
        var path = CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5");
        var frame = TryRenderE5EntryFrameAt(
            project,
            path,
            frameMapping.ImageNumber,
            RawPreviewSpecs.PmapObjWidth,
            RawPreviewSpecs.PmapObjFrameHeight,
            frameMapping.FrameIndex,
            frameMapping.FlipHorizontal,
            out var readDetail);
        detail = frame == null
            ? $"R{rImageId} -> Pmapobj.e5 #{frameMapping.ImageNumber} 方向={normalizedFacing} 动作帧={gestureIndex} 条带帧={frameMapping.FrameIndex} 未读取：{readDetail}"
            : $"R{rImageId} -> Pmapobj.e5 #{frameMapping.ImageNumber} 方向={normalizedFacing} 动作帧={gestureIndex} 条带帧={frameMapping.FrameIndex}";
        return frame;
    }

    public Bitmap? TryRenderRSceneFrameByIndex(CczProject project, int rImageId, int frameIndex, string facing, out string detail)
    {
        detail = string.Empty;
        if (rImageId < 0)
        {
            detail = $"R 形象编号 {rImageId} 无效。";
            return null;
        }

        var normalizedFacing = NormalizeRSceneFacing(facing);
        var gestureIndex = NormalizeRSceneGestureIndex(frameIndex);
        var frameMapping = ResolveRSceneFrameMapping(rImageId, normalizedFacing, gestureIndex);
        var path = CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5");
        var frame = TryRenderE5EntryFrameAt(
            project,
            path,
            frameMapping.ImageNumber,
            RawPreviewSpecs.PmapObjWidth,
            RawPreviewSpecs.PmapObjFrameHeight,
            frameMapping.FrameIndex,
            frameMapping.FlipHorizontal,
            out var readDetail);
        detail = frame == null
            ? $"R{rImageId} -> Pmapobj.e5 #{frameMapping.ImageNumber} 动作帧={gestureIndex} 方向={normalizedFacing} 条带帧={frameMapping.FrameIndex} 未读取：{readDetail}"
            : $"R{rImageId} -> Pmapobj.e5 #{frameMapping.ImageNumber} 动作帧={gestureIndex} 方向={normalizedFacing} 条带帧={frameMapping.FrameIndex}";
        return frame;
    }

    public Bitmap? TryRenderRScenePhysicalStripFrame(CczProject project, int rImageId, int stripFrameIndex, string facing, out string detail)
    {
        detail = string.Empty;
        if (rImageId < 0)
        {
            detail = $"R 形象编号 {rImageId} 无效。";
            return null;
        }

        var normalizedFacing = NormalizeRSceneFacing(facing);
        var physicalFrameIndex = Math.Clamp(stripFrameIndex, 0, RawPreviewSpecs.PmapObjFrameCount - 1);
        var frameMapping = ResolveRScenePhysicalFrameMapping(rImageId, normalizedFacing, physicalFrameIndex);
        var path = CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5");
        var frame = TryRenderE5EntryFrameAt(
            project,
            path,
            frameMapping.ImageNumber,
            RawPreviewSpecs.PmapObjWidth,
            RawPreviewSpecs.PmapObjFrameHeight,
            frameMapping.FrameIndex,
            frameMapping.FlipHorizontal,
            out var readDetail);
        detail = frame == null
            ? $"R{rImageId} -> Pmapobj.e5 #{frameMapping.ImageNumber} 方向={normalizedFacing} 条带帧={frameMapping.FrameIndex} 未读取：{readDetail}"
            : $"R{rImageId} -> Pmapobj.e5 #{frameMapping.ImageNumber} 方向={normalizedFacing} 条带帧={frameMapping.FrameIndex}";
        return frame;
    }

    public Bitmap? TryRenderBattlefieldMoveIdleFrame(
        CczProject project,
        int sImageId,
        int? jobId,
        int factionSlot,
        string direction,
        int framePhase,
        out string detail)
        => TryRenderBattlefieldMoveIdleFrame(project, sImageId, jobId, factionSlot, direction, framePhase, "初级", out detail);

    public Bitmap? TryRenderBattlefieldMoveIdleFrame(
        CczProject project,
        int sImageId,
        int? jobId,
        int factionSlot,
        string direction,
        int framePhase,
        string levelMode,
        out string detail)
    {
        detail = string.Empty;
        var mapping = CharacterImageResourceService.ResolveSUnitImageMapping(project, sImageId, jobId, factionSlot);
        if (mapping.ImageNumbers.Count == 0)
        {
            detail = mapping.Detail;
            return null;
        }

        var levelStageIndex = GetBattlefieldLevelStageIndex(levelMode);
        var imageStageIndex = mapping.ImageNumbers.Count >= 3
            ? Math.Clamp(levelStageIndex, 0, 2)
            : 0;
        var imageNumber = mapping.ImageNumbers[imageStageIndex];
        var normalizedLevelMode = NormalizeBattlefieldLevelMode(levelMode);
        var normalizedDirection = NormalizeBattlefieldDirection(direction);
        var phase = Math.Abs(framePhase) % 2;
        var frameIndex = normalizedDirection switch
        {
            "上" => 2 + phase,
            "左" => 4 + phase,
            "右" => 4 + phase,
            _ => phase
        };
        var flipHorizontal = normalizedDirection == "右";
        var path = CharacterImageResourceService.ResolveGameFile(project, "Unit_mov.e5");
        var frame = TryRenderE5EntryFrameAt(project, path, imageNumber, 48, 48, frameIndex, flipHorizontal, out var readDetail);
        detail = frame == null
            ? $"{mapping.ShortText} {normalizedLevelMode} -> Unit_mov.e5 #{imageNumber} 待机{normalizedDirection} 第{phase + 1}帧未读取：{readDetail}"
            : $"{mapping.ShortText} {normalizedLevelMode} -> Unit_mov.e5 #{imageNumber} 待机{normalizedDirection} 第{phase + 1}帧";
        return frame;
    }

    private static void DrawCenteredImage(Graphics g, Image image, Rectangle rect, Pen borderPen)
    {
        using var back = new SolidBrush(Color.FromArgb(16, 18, 20));
        g.FillRectangle(back, rect);
        g.DrawRectangle(borderPen, rect);

        var scale = Math.Min(rect.Width / (float)image.Width, rect.Height / (float)image.Height);
        var w = (int)Math.Round(image.Width * scale);
        var h = (int)Math.Round(image.Height * scale);
        var x = rect.Left + (rect.Width - w) / 2;
        var y = rect.Top + (rect.Height - h) / 2;
        g.DrawImage(image, new Rectangle(x, y, w, h));
    }

    private Bitmap? TryLoadMappedE5Image(CczProject project, string prefix, int id, int? jobId, int sFactionSlot, out string caption)
    {
        prefix = NormalizePrefix(prefix);
        if (prefix == "S")
        {
            var preview = TryRenderSImage(project, id, jobId, sFactionSlot, out caption);
            if (preview != null) return preview;
            return null;
        }

        return TryRenderRImage(project, id, out caption);
    }

    private Bitmap? TryRenderRImage(CczProject project, int rImageId, out string caption)
    {
        caption = "Pmapobj.e5 未定位";
        if (rImageId < 0) return null;

        var imageNumber = checked(rImageId * 2 + 1);
        var path = CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5");
        var frame = TryRenderE5EntryFrame(project, path, imageNumber, RawPreviewSpecs.PmapObjWidth, RawPreviewSpecs.PmapObjFrameHeight, out var detail);
        caption = frame == null
            ? $"R{rImageId} -> Pmapobj.e5 #{imageNumber} 未读取：{detail}"
            : $"R{rImageId} -> Pmapobj.e5 #{imageNumber}";
        return frame;
    }

    private Bitmap? TryRenderSImage(CczProject project, int sImageId, int? jobId, int sFactionSlot, out string caption)
    {
        var mapping = CharacterImageResourceService.ResolveSUnitImageMapping(project, sImageId, jobId, sFactionSlot);
        caption = mapping.Detail;
        if (mapping.ImageNumbers.Count == 0) return null;

        var groups = new List<UnitImagePreviewGroup>();
        var errors = new List<string>();
        try
        {
            foreach (var imageNumber in mapping.ImageNumbers)
            {
                var frames = new List<UnitFramePreview>();
                foreach (var spec in UnitPreviewSpecs)
                {
                    var path = CharacterImageResourceService.ResolveGameFile(project, spec.FileName);
                    var frame = TryRenderE5EntryFrame(project, path, imageNumber, spec.RawWidth, spec.FrameHeight, out var detail);
                    if (frame == null)
                    {
                        errors.Add($"图{imageNumber}{spec.Label}:{detail}");
                        continue;
                    }

                    frames.Add(new UnitFramePreview($"{spec.Label}#{imageNumber}", frame));
                }

                if (frames.Count > 0)
                {
                    groups.Add(new UnitImagePreviewGroup(imageNumber, frames));
                }
            }

            if (groups.Count == 0)
            {
                caption = $"S{sImageId} 未读取：{mapping.Detail}；{string.Join(" / ", errors)}";
                return null;
            }

            caption = $"{mapping.ShortText}";
            return ComposeUnitFrameGroups(groups);
        }
        finally
        {
            foreach (var group in groups)
            {
                foreach (var frame in group.Frames)
                {
                    frame.Image.Dispose();
                }
            }
        }
    }

    private Bitmap? TryRenderE5EntryFrame(CczProject project, string path, int imageNumber, int rawWidth, int frameHeight, out string detail)
    {
        if (!File.Exists(path))
        {
            detail = "文件不存在";
            return null;
        }

        var entries = GetE5ImageIndex(path);
        if (imageNumber <= 0 || imageNumber > entries.Count)
        {
            detail = $"索引越界 #{imageNumber}/{entries.Count}";
            return null;
        }

        var entry = entries[imageNumber - 1];
        var bytes = TryReadEntryBytes(path, entry, out var readDetail);
        if (bytes == null)
        {
            detail = readDetail;
            return null;
        }
        using var decoded = TryDecodeStandardImage(bytes);
        if (decoded != null)
        {
            detail = $"{entry.Kind}{BuildEntryLocationText(entry)}";
            return CropRepresentativeFrame(decoded, frameHeight);
        }

        if (LooksLikeRawIndexedStrip(bytes, rawWidth, frameHeight))
        {
            var rawPalette = LoadRawPalette(project);
            var raw = TryRenderRawIndexedStrip(bytes, rawWidth, frameHeight, rawPalette.Colors, rawPalette.Mode, out var paletteMode);
            if (raw != null)
            {
                detail = $"{paletteMode}{BuildEntryLocationText(entry)}";
                return raw;
            }
        }

        detail = $"未知格式 first={HexDisplayFormatter.Format(bytes.Length == 0 ? 0 : bytes[0], 2)}{BuildEntryLocationText(entry)}";
        return null;
    }

    private Bitmap? TryRenderE5EntryFrameAt(
        CczProject project,
        string path,
        int imageNumber,
        int rawWidth,
        int frameHeight,
        int frameIndex,
        bool flipHorizontal,
        out string detail)
    {
        if (!File.Exists(path))
        {
            detail = "文件不存在";
            return null;
        }

        var entries = GetE5ImageIndex(path);
        if (imageNumber <= 0 || imageNumber > entries.Count)
        {
            detail = $"索引越界 #{imageNumber}/{entries.Count}";
            return null;
        }

        var entry = entries[imageNumber - 1];
        var bytes = TryReadEntryBytes(path, entry, out var readDetail);
        if (bytes == null)
        {
            detail = readDetail;
            return null;
        }

        Bitmap? strip = null;
        using var decoded = TryDecodeStandardImage(bytes);
        if (decoded != null)
        {
            strip = new Bitmap(decoded);
            detail = $"{entry.Kind}{BuildEntryLocationText(entry)}";
        }
        else if (LooksLikeRawIndexedStrip(bytes, rawWidth, frameHeight))
        {
            var rawPalette = LoadRawPalette(project);
            strip = TryRenderRawIndexedStripBitmap(bytes, rawWidth, frameHeight, rawPalette.Colors, rawPalette.Mode, out var paletteMode);
            detail = $"{paletteMode}{BuildEntryLocationText(entry)}";
        }
        else
        {
            detail = $"未知格式 first={HexDisplayFormatter.Format(bytes.Length == 0 ? 0 : bytes[0], 2)}{BuildEntryLocationText(entry)}";
            return null;
        }

        using (strip)
        {
            if (strip == null)
            {
                detail = $"未能解码{BuildEntryLocationText(entry)}";
                return null;
            }

            var frameCount = Math.Max(1, strip.Height / Math.Max(1, frameHeight));
            var safeIndex = Math.Clamp(frameIndex, 0, frameCount - 1);
            var frame = CropFrame(strip, safeIndex * frameHeight, frameHeight);
            if (flipHorizontal)
            {
                frame.RotateFlip(RotateFlipType.RotateNoneFlipX);
            }

            return frame;
        }
    }

    private IReadOnlyList<E5ImageEntry> GetE5ImageIndex(string path)
    {
        path = Path.GetFullPath(path);
        var key = BuildFileCacheKey(path);
        if (_e5ImageIndexCache.TryGetValue(key, out var cached)) return cached;
        if (!File.Exists(path))
        {
            var empty = Array.Empty<E5ImageEntry>();
            _e5ImageIndexCache[key] = empty;
            return empty;
        }

        var data = File.ReadAllBytes(path);
        var result = new List<E5ImageEntry>();
        uint firstDataOffset = 0;
        for (var offset = E5ImageIndexOffset; offset + E5ImageIndexEntrySize <= data.Length; offset += E5ImageIndexEntrySize)
        {
            if (firstDataOffset > 0 && offset >= firstDataOffset)
            {
                break;
            }

            var storedSize = ReadBigEndianUInt32(data, offset);
            var decodedSize = ReadBigEndianUInt32(data, offset + 4);
            var imageOffset = ReadBigEndianUInt32(data, offset + 8);
            if (storedSize == 0 ||
                decodedSize == 0 ||
                imageOffset >= data.Length ||
                storedSize > data.Length - imageOffset ||
                storedSize > int.MaxValue ||
                decodedSize > int.MaxValue)
            {
                break;
            }

            if (firstDataOffset == 0)
            {
                firstDataOffset = imageOffset;
            }

            var compressed = storedSize != decodedSize;
            result.Add(new E5ImageEntry(
                result.Count + 1,
                checked((int)imageOffset),
                checked((int)storedSize),
                checked((int)decodedSize),
                compressed
                    ? "LS12"
                    : DetectE5ImageKind(data, checked((int)imageOffset), checked((int)storedSize))));
        }

        _e5ImageIndexCache[key] = result;
        return result;
    }

    private static string BuildAvailableImageIdCacheKey(CczProject project, string prefix, bool includeZero)
    {
        var normalizedPrefix = prefix.Equals("Face", StringComparison.OrdinalIgnoreCase)
            ? "Face"
            : NormalizePrefix(prefix);
        var paths = normalizedPrefix switch
        {
            "Face" => new[] { ResolveFaceFile(project) ?? Path.Combine(project.GameRoot, "E5", "Face.e5") },
            "S" => UnitPreviewSpecs
                .Select(spec => CharacterImageResourceService.ResolveGameFile(project, spec.FileName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            _ => new[] { CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5") }
        };

        return string.Join("|",
            new[] { "available", Path.GetFullPath(project.GameRoot), normalizedPrefix, includeZero ? "1" : "0" }
                .Concat(paths.Select(BuildFileCacheKey)));
    }

    private static string BuildFileCacheKey(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return fullPath + "|missing";
        }

        try
        {
            var info = new FileInfo(fullPath);
            return $"{fullPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }
        catch
        {
            return fullPath + "|unknown";
        }
    }

    private IReadOnlyList<int> GetAvailableRImageIds(CczProject project, bool includeZero)
    {
        var path = CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5");
        if (!File.Exists(path)) return Array.Empty<int>();

        var entries = GetE5ImageIndex(path);
        var result = new List<int>();
        for (var rImageId = includeZero ? 0 : 1; ; rImageId++)
        {
            var frontNumber = checked(rImageId * 2 + 1);
            var backNumber = checked(rImageId * 2 + 2);
            if (backNumber > entries.Count) break;

            if (CanRenderE5EntryFrame(project, path, frontNumber, RawPreviewSpecs.PmapObjWidth, RawPreviewSpecs.PmapObjFrameHeight))
            {
                result.Add(rImageId);
            }
        }

        return result;
    }

    private IReadOnlyList<int> GetAvailableFaceIds(CczProject project, bool includeZero)
    {
        var path = CharacterImageResourceService.ResolveFaceFile(project) ?? Path.Combine(project.GameRoot, "E5", "Face.e5");
        if (!File.Exists(path)) return Array.Empty<int>();

        var entries = GetE5ImageIndex(path);
        var result = new List<int>();
        for (var faceId = includeZero ? 0 : 1; ; faceId++)
        {
            var mapping = new CharacterImageResourceService().MapFaceId(faceId);
            if (mapping.FaceImageNumbers.Count == 0) break;

            var maxFaceImageNumber = mapping.FaceImageNumbers.Max();
            if (maxFaceImageNumber > entries.Count) break;

            var preferredFaceNumber = mapping.FaceImageNumbers.First();
            if (CanRenderE5EntryFrame(project, path, preferredFaceNumber, 64, 80))
            {
                result.Add(faceId);
            }
        }

        return result;
    }

    private IReadOnlyList<int> GetAvailableSImageIds(CczProject project, bool includeZero)
    {
        var unitPaths = UnitPreviewSpecs
            .Select(spec => CharacterImageResourceService.ResolveGameFile(project, spec.FileName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unitPaths.Length == 0 || unitPaths.Any(path => !File.Exists(path))) return Array.Empty<int>();

        var entryCounts = unitPaths
            .Select(path => GetE5ImageIndex(path).Count)
            .ToArray();
        if (entryCounts.Length == 0 || entryCounts.Any(count => count <= 0)) return Array.Empty<int>();

        var maxSharedUnitImageNumber = entryCounts.Min();
        var layout = _layoutService.Resolve(project);
        var maxSImageId = layout.UnitEntryCount > 0 ? layout.SMaxId : 0;
        var result = new List<int>();
        for (var sImageId = includeZero ? 0 : 1; sImageId <= maxSImageId; sImageId++)
        {
            if (sImageId == 0)
            {
                continue;
            }

            var mapping = CharacterImageResourceService.ResolveSUnitImageMapping(project, sImageId, jobId: null, CharacterImageResourceService.DefaultSPreviewFactionSlot);
            if (mapping.ImageNumbers.Count == 0 ||
                mapping.ImageNumbers.Any(number => number <= 0 || number > maxSharedUnitImageNumber))
            {
                continue;
            }

            var canRenderAll = true;
            foreach (var imageNumber in mapping.ImageNumbers)
            {
                foreach (var spec in UnitPreviewSpecs)
                {
                    var path = CharacterImageResourceService.ResolveGameFile(project, spec.FileName);
                    if (CanRenderE5EntryFrame(project, path, imageNumber, spec.RawWidth, spec.FrameHeight))
                    {
                        continue;
                    }

                    canRenderAll = false;
                    break;
                }

                if (!canRenderAll) break;
            }

            if (canRenderAll)
            {
                result.Add(sImageId);
            }
        }

        return result;
    }

    private bool CanRenderE5EntryFrame(CczProject project, string path, int imageNumber, int rawWidth, int frameHeight)
    {
        using var frame = TryRenderE5EntryFrame(project, path, imageNumber, rawWidth, frameHeight, out _);
        return frame != null;
    }

    private static byte[]? TryReadEntryBytes(string path, E5ImageEntry entry, out string detail)
    {
        detail = string.Empty;
        var bytes = new byte[entry.StoredLength];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        stream.Position = entry.Offset;
        stream.ReadExactly(bytes);

        if (!entry.IsCompressed)
        {
            return bytes;
        }

        var dictionary = ReadLsDictionary(path);
        if (dictionary == null)
        {
            detail = $"LS12 decode failed: dictionary missing{BuildEntryLocationText(entry)}";
            return null;
        }

        if (!TryDecodeLsEntry(dictionary, bytes, entry.DecodedLength, out var decoded))
        {
            detail = $"LS12 decode failed{BuildEntryLocationText(entry)}";
            return null;
        }

        return decoded;
    }

    private static byte[]? ReadLsDictionary(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < LsHeaderLength + LsDictionaryLength) return null;

        var dictionary = new byte[LsDictionaryLength];
        stream.Position = LsHeaderLength;
        stream.ReadExactly(dictionary);
        return dictionary;
    }

    private static bool TryDecodeLsEntry(byte[] dictionary, byte[] encoded, int decodedLength, out byte[] decoded)
    {
        decoded = new byte[decodedLength];
        if (encoded.Length == decodedLength)
        {
            Buffer.BlockCopy(encoded, 0, decoded, 0, decodedLength);
            return true;
        }

        var inputIndex = 0;
        var bitPosition = 7;
        var outputIndex = 0;
        var backDistance = 0;

        while (outputIndex < decodedLength)
        {
            if (inputIndex >= encoded.Length) return false;

            uint code = 0;
            var bitLength = 0;
            int bitSet;
            do
            {
                bitSet = (encoded[inputIndex] >> bitPosition) & 0x01;
                code = (code << 1) | (uint)bitSet;
                bitLength++;
                bitPosition--;
                if (bitPosition < 0)
                {
                    bitPosition = 7;
                    inputIndex++;
                }
            } while (bitSet != 0);

            uint mask = 0;
            while (bitLength-- > 0)
            {
                if (inputIndex >= encoded.Length) return false;
                bitSet = (encoded[inputIndex] >> bitPosition) & 0x01;
                mask = (mask << 1) | (uint)bitSet;
                bitPosition--;
                if (bitPosition < 0)
                {
                    bitPosition = 7;
                    inputIndex++;
                }
            }

            code += mask;
            if (backDistance == 0 && code >= LsDictionaryLength)
            {
                backDistance = checked((int)(code - LsDictionaryLength));
                if (backDistance == 0) return false;
                continue;
            }

            if (backDistance == 0)
            {
                if (code >= LsDictionaryLength) return false;
                decoded[outputIndex++] = dictionary[(int)code];
                continue;
            }

            var copyCount = checked((int)code + 3);
            while (copyCount-- > 0)
            {
                if (outputIndex >= decodedLength) return false;
                var sourceIndex = outputIndex - backDistance;
                if (sourceIndex < 0) return false;
                decoded[outputIndex++] = decoded[sourceIndex];
            }

            backDistance = 0;
        }

        return true;
    }

    private static string BuildEntryLocationText(E5ImageEntry entry)
    {
        return entry.IsCompressed
            ? $" offset={HexDisplayFormatter.FormatOffset(entry.Offset)} stored={entry.StoredLength:N0} decoded={entry.DecodedLength:N0}"
            : $" offset={HexDisplayFormatter.FormatOffset(entry.Offset)} size={entry.DecodedLength:N0}";
    }

    private static Bitmap? TryDecodeStandardImage(byte[] bytes)
    {
        if (bytes.Length < 2) return null;
        var isBmp = bytes[0] == (byte)'B' && bytes[1] == (byte)'M';
        var isJpeg = bytes[0] == 0xFF && bytes[1] == 0xD8;
        var isPng = bytes.Length >= PngMagic.Length && Matches(bytes, 0, PngMagic);
        if (!isBmp && !isJpeg && !isPng) return null;

        try
        {
            using var memory = new MemoryStream(bytes, writable: false);
            using var image = Image.FromStream(memory, useEmbeddedColorManagement: false, validateImageData: false);
            return new Bitmap(image);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (ExternalException)
        {
            return null;
        }
    }

    private static string DetectE5ImageKind(byte[] data, int offset, int length)
    {
        if (length <= 0 || offset < 0 || offset >= data.Length) return "EMPTY";
        if (offset + 1 < data.Length && data[offset] == (byte)'B' && data[offset + 1] == (byte)'M') return "BMP";
        if (offset + 1 < data.Length && data[offset] == 0xFF && data[offset + 1] == 0xD8) return "JPG";
        if (offset + PngMagic.Length <= data.Length && Matches(data, offset, PngMagic)) return "PNG";
        if (data[offset] == 0) return "RAW";
        return HexDisplayFormatter.FormatByte(data[offset]);
    }

    private static bool LooksLikeRawIndexedStrip(byte[] bytes, int rawWidth, int frameHeight)
    {
        if (rawWidth <= 0 || frameHeight <= 0) return false;
        var rawLength = bytes.Length - (bytes.Length % rawWidth);
        return rawLength >= rawWidth * frameHeight;
    }

    private static Bitmap? TryRenderRawIndexedStrip(byte[] bytes, int rawWidth, int frameHeight, IReadOnlyList<Color> palette, string paletteSourceMode, out string paletteMode)
    {
        using var strip = TryRenderRawIndexedStripBitmap(bytes, rawWidth, frameHeight, palette, paletteSourceMode, out paletteMode);
        return strip == null ? null : CropRepresentativeFrame(strip, frameHeight);
    }

    private static Bitmap? TryRenderRawIndexedStripBitmap(byte[] bytes, int rawWidth, int frameHeight, IReadOnlyList<Color> palette, string paletteSourceMode, out string paletteMode)
    {
        paletteMode = palette.Count >= 256 ? paletteSourceMode : "RAW grayscale";
        if (rawWidth <= 0 || frameHeight <= 0) return null;
        var rawLength = bytes.Length - (bytes.Length % rawWidth);
        if (rawLength < rawWidth * frameHeight) return null;

        var rawHeight = rawLength / rawWidth;
        var strip = new Bitmap(rawWidth, rawHeight, PixelFormat.Format32bppArgb);
        for (var y = 0; y < rawHeight; y++)
        {
            for (var x = 0; x < rawWidth; x++)
            {
                var value = bytes[y * rawWidth + x];
                if (value == 0)
                {
                    strip.SetPixel(x, y, Color.Transparent);
                    continue;
                }

                var gray = Math.Min(255, 48 + value);
                var color = value < palette.Count
                    ? palette[value]
                    : Color.FromArgb(255, gray, gray, gray);
                strip.SetPixel(x, y, IsMagentaKey(color) ? Color.Transparent : color);
            }
        }

        return strip;
    }

    private static string NormalizeBattlefieldDirection(string direction)
        => direction switch
        {
            "上" => "上",
            "左" => "左",
            "右" => "右",
            _ => "下"
        };

    private static string NormalizeBattlefieldLevelMode(string levelMode)
        => levelMode switch
        {
            "中级" => "中级",
            "高级" => "高级",
            _ => "初级"
        };

    private static int GetBattlefieldLevelStageIndex(string levelMode)
        => NormalizeBattlefieldLevelMode(levelMode) switch
        {
            "中级" => 1,
            "高级" => 2,
            _ => 0
        };

    private RawPalette LoadRawPalette(CczProject project)
    {
        var paletteInfo = _paletteService.Load(project);
        var key = $"{paletteInfo.Mode}:{paletteInfo.Path}";
        return _rawPaletteCache.GetOrAdd(key, _ => new RawPalette(paletteInfo.Colors, paletteInfo.Mode, paletteInfo.Path));
    }

    private static Bitmap CropRepresentativeFrame(Bitmap strip, int frameHeight)
    {
        var frameCount = Math.Max(1, strip.Height / Math.Max(1, frameHeight));
        Bitmap? fallback = null;
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var frame = CropFrame(strip, frameIndex * frameHeight, frameHeight);
            if (CountVisiblePixels(frame) > Math.Max(12, frame.Width * frame.Height / 80))
            {
                fallback?.Dispose();
                return frame;
            }

            if (fallback == null)
            {
                fallback = frame;
            }
            else
            {
                frame.Dispose();
            }
        }

        return fallback ?? CropFrame(strip, 0, frameHeight);
    }

    private static Bitmap CropFrame(Bitmap strip, int y, int frameHeight)
    {
        if (strip.Width <= 0 || strip.Height <= 0)
        {
            return new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        }

        var safeY = Math.Clamp(y, 0, Math.Max(0, strip.Height - 1));
        var height = Math.Min(frameHeight, strip.Height - safeY);
        if (height <= 0) height = Math.Min(frameHeight, strip.Height);
        height = Math.Max(1, height);
        var frame = new Bitmap(strip.Width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(frame))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(strip, new Rectangle(0, 0, frame.Width, frame.Height), new Rectangle(0, safeY, frame.Width, frame.Height), GraphicsUnit.Pixel);
        }

        ApplyMagentaTransparency(frame);
        return frame;
    }

    private static void ApplyMagentaTransparency(Bitmap bitmap)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (IsMagentaKey(pixel))
                {
                    bitmap.SetPixel(x, y, Color.Transparent);
                }
            }
        }
    }

    private static bool IsMagentaKey(Color pixel)
    {
        if (pixel.A == 0) return true;

        // Pmapobj.e5 的 JPG 帧会把透明底色压缩成一组接近洋红的像素，不能只匹配 FF00FF。
        return pixel.R >= 210 &&
               pixel.B >= 210 &&
               pixel.G <= 90 &&
               Math.Abs(pixel.R - pixel.B) <= 70;
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

    private static Bitmap ComposeUnitFrameGroups(IReadOnlyList<UnitImagePreviewGroup> groups)
    {
        const int cellWidth = 92;
        const int imageHeight = 84;
        const int rowPadding = 8;
        var maxColumns = Math.Max(1, groups.Max(group => group.Frames.Count));
        var rowHeight = imageHeight + rowPadding;
        var bitmap = new Bitmap(Math.Max(cellWidth, maxColumns * cellWidth), Math.Max(rowHeight, groups.Count * rowHeight), PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(24, 26, 28));
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        using var borderPen = new Pen(Color.FromArgb(95, 105, 112));

        for (var row = 0; row < groups.Count; row++)
        {
            var top = row * rowHeight;
            var frames = groups[row].Frames;
            for (var i = 0; i < frames.Count; i++)
            {
                var left = i * cellWidth;
                var imageRect = new Rectangle(left + 8, top + 6, cellWidth - 16, imageHeight);
                g.DrawRectangle(borderPen, imageRect);

                var image = frames[i].Image;
                var scale = Math.Min(imageRect.Width / (float)image.Width, imageRect.Height / (float)image.Height);
                var width = Math.Max(1, (int)Math.Round(image.Width * scale));
                var height = Math.Max(1, (int)Math.Round(image.Height * scale));
                var x = imageRect.Left + (imageRect.Width - width) / 2;
                var y = imageRect.Top + (imageRect.Height - height) / 2;
                g.DrawImage(image, new Rectangle(x, y, width, height));
            }
        }

        return bitmap;
    }

    private static Bitmap ComposeSImageFactionStackPreview(IReadOnlyList<SImageFactionPreviewRow> rows)
    {
        const int outerPadding = 8;
        const int labelWidth = 70;
        const int minPreviewWidth = 276;
        const int minRowHeight = 92;
        const int rowGap = 10;

        if (rows.Count == 0)
        {
            return new Bitmap(outerPadding * 2 + labelWidth + minPreviewWidth, outerPadding * 2 + minRowHeight, PixelFormat.Format32bppArgb);
        }

        var previewWidth = Math.Max(minPreviewWidth, rows.Max(row => row.Image?.Width ?? minPreviewWidth));
        var rowHeights = rows
            .Select(row => Math.Max(minRowHeight, row.Image?.Height ?? minRowHeight))
            .ToArray();
        var width = outerPadding * 2 + labelWidth + previewWidth;
        var height = outerPadding * 2 + rowHeights.Sum() + Math.Max(0, rows.Count - 1) * rowGap;
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(24, 26, 28));
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        using var borderPen = new Pen(Color.FromArgb(95, 105, 112));
        using var rowBrush = new SolidBrush(Color.FromArgb(31, 34, 38));
        using var labelBrush = new SolidBrush(Color.FromArgb(229, 234, 240));
        using var placeholderBrush = new SolidBrush(Color.FromArgb(175, 183, 191));
        using var labelFont = new Font(SystemFonts.DefaultFont, FontStyle.Bold);
        using var labelFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        using var placeholderFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisWord
        };

        var top = outerPadding;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowHeight = rowHeights[i];
            var rowRect = new Rectangle(outerPadding, top, labelWidth + previewWidth, rowHeight);
            var labelRect = new Rectangle(outerPadding, top, labelWidth, rowHeight);
            var imageRect = new Rectangle(outerPadding + labelWidth, top, previewWidth, rowHeight);

            g.FillRectangle(rowBrush, rowRect);
            g.DrawRectangle(borderPen, rowRect);
            g.DrawString(row.FactionText, labelFont, labelBrush, labelRect, labelFormat);

            if (row.Image != null)
            {
                DrawCenteredImage(g, row.Image, imageRect, borderPen);
            }
            else
            {
                g.DrawRectangle(borderPen, imageRect);
                g.DrawString("未能读取\r\n" + row.Caption, SystemFonts.DefaultFont, placeholderBrush, imageRect, placeholderFormat);
            }

            top += rowHeight + rowGap;
        }

        return bitmap;
    }

    public string BuildResourceInfo(CczProject project, string prefix, int id, string personName, int? faceId = null, int? jobId = null, int sFactionSlot = CharacterImageResourceService.DefaultSPreviewFactionSlot)
    {
        prefix = NormalizePrefix(prefix);
        var resolver = new CharacterImageResourceService();
        var status = prefix == "S" ? resolver.BuildSStatus(project, id) : resolver.BuildRStatus(project, id);
        var toolDir = ResolveImageAssignerToolDirectory(project);
        var assignerConfig = LoadImageAssignerConfig(project);
        var sceneConfig = LoadSceneEditorConfig(project);
        var sb = new StringBuilder();
        sb.AppendLine($"{personName}  {prefix} 形象编号：{id}");
        if (faceId.HasValue)
        {
            var facePath = ResolveFaceFile(project);
            var faceCount = facePath != null ? GetE5ImageIndex(facePath).Count : 0;
            sb.AppendLine("头像映射：" + resolver.BuildFaceHint(project, faceId.Value));
            sb.AppendLine($"头像预览：{(facePath == null ? "未找到 E5\\Face.e5" : $"E5\\Face.e5 0x110 索引表内条目 {faceCount} 张；按教程映射取图")}");
        }
        sb.AppendLine($"资源定位：{status.Status}：{status.ResourceName}");
        sb.AppendLine($"资源路径：{status.Path}");
        sb.AppendLine($"解释：{status.Detail}");
        sb.AppendLine("人物 R/S 编号来源：Ekd5.exe 中的人物 R/S 指定表，不是 E5S 存档信息，也不是 RS\\R_XX.eex / S_XX.eex 人物图像。");
        sb.AppendLine($"B形象指定器目录：{toolDir ?? "未找到 B形象指定器\\6.X形象指定器"}");
        sb.AppendLine($"B形象指定器配置：FileHead={assignerConfig.GetValueOrDefault("FileHead", "未找到")}，RFileHead={assignerConfig.GetValueOrDefault("RFileHead", "未找到")}，UserPath2={assignerConfig.GetValueOrDefault("UserPath2", "未找到")}");
        sb.AppendLine($"新剧本编辑器配置：RSMax={sceneConfig.GetValueOrDefault("RSMax", "未找到")}，ExePath={sceneConfig.GetValueOrDefault("ExePath", "未找到")}");

        sb.AppendLine(BuildCharacterPreviewStatus(project, prefix, id, jobId, sFactionSlot));

        if (!ResourcePathExists(status.Path))
        {
            sb.AppendLine("资源文件未定位：请检查 Pmapobj.e5 / Unit_atk.e5 / Unit_mov.e5 / Unit_spc.e5 是否在项目根目录。");
            return sb.ToString();
        }

        sb.AppendLine("文件状态：" + BuildResourceFileStateText(status.Path));
        sb.AppendLine(prefix == "R"
            ? "说明：R 形象当前按 Pmapobj.e5 的 0x110 索引表读取，R=n 取正面图号 2n+1；封包内图片重排/替换尚未开放写入。"
            : "说明：S 形象当前按紧凑编号映射到 Unit_*.e5：S=0 由职业和预览阵营计算，S=1..32 对应三转特殊三张图，S>=33 对应一转特殊单张图。");
        return sb.ToString();
    }

    private string BuildCharacterPreviewStatus(CczProject project, string prefix, int id, int? jobId, int sFactionSlot)
    {
        prefix = NormalizePrefix(prefix);
        if (prefix == "R")
        {
            using var rPreview = TryRenderRImage(project, id, out var rCaption);
            return rPreview == null
                ? $"实图预览：{rCaption}。"
                : $"实图预览：可显示 {rCaption} 的代表帧。";
        }

        using var sPreview = TryRenderSImage(project, id, jobId, sFactionSlot, out var sCaption);
        return sPreview == null
            ? $"实图预览：{sCaption}。"
            : $"实图预览：可显示 {sCaption} 的移动/攻击/特技代表帧。";
    }

    private static bool ResourcePathExists(string path)
    {
        var parts = path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? File.Exists(path) : parts.Any(File.Exists);
    }

    private static string BuildResourceFileStateText(string path)
    {
        var parts = path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            var info = new FileInfo(path);
            return info.Exists
                ? $"已找到，大小 {info.Length:N0} 字节，修改时间 {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}"
                : "未找到";
        }

        return string.Join("；", parts.Select(part =>
        {
            var info = new FileInfo(part);
            return info.Exists
                ? $"{Path.GetFileName(part)} 已找到，大小 {info.Length:N0} 字节，修改时间 {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}"
                : $"{Path.GetFileName(part)} 未找到";
        }));
    }

    private static void DrawByteTexture(Graphics g, byte[] bytes, EexPreviewMetadata meta, IReadOnlyList<Color> palette, Rectangle rect)
    {
        using var frameBack = new SolidBrush(Color.FromArgb(18, 20, 22));
        using var borderPen = new Pen(Color.FromArgb(120, 128, 136));
        g.FillRectangle(frameBack, rect);
        g.DrawRectangle(borderPen, rect);
        if (bytes.Length == 0) return;

        var dataStart = meta.PlausibleOffsets.FirstOrDefault(x => x >= meta.HeaderSize && x < bytes.Length);
        if (dataStart <= 0) dataStart = Math.Min(Math.Max(meta.HeaderSize, 14), bytes.Length - 1);
        var dataLength = Math.Max(1, bytes.Length - dataStart);

        using var texture = new Bitmap(140, 72, PixelFormat.Format32bppArgb);
        for (var y = 0; y < texture.Height; y++)
        {
            for (var x = 0; x < texture.Width; x++)
            {
                var sample = dataStart + (int)(((long)(y * texture.Width + x) * dataLength) / (texture.Width * texture.Height));
                if (sample >= bytes.Length) sample = bytes.Length - 1;
                var value = bytes[sample];
                var color = palette.Count > 0 ? palette[value % palette.Count] : DefaultColor(value);
                texture.SetPixel(x, y, Color.FromArgb(232, color));
            }
        }

        g.DrawImage(texture, rect);
    }

    private static void DrawSectionBar(Graphics g, EexPreviewMetadata meta, int length, Rectangle rect)
    {
        using var back = new SolidBrush(Color.FromArgb(44, 48, 52));
        using var border = new Pen(Color.FromArgb(120, 128, 136));
        g.FillRectangle(back, rect);
        if (length <= 0)
        {
            g.DrawRectangle(border, rect);
            return;
        }

        var colors = new[]
        {
            Color.FromArgb(86, 156, 214),
            Color.FromArgb(197, 134, 192),
            Color.FromArgb(78, 201, 176),
            Color.FromArgb(220, 220, 170),
            Color.FromArgb(206, 145, 120)
        };
        var offsets = meta.PlausibleOffsets.Where(x => x >= 0 && x < length).Distinct().OrderBy(x => x).ToList();
        if (offsets.Count == 0) offsets.Add(Math.Min(meta.HeaderSize, Math.Max(0, length - 1)));
        offsets.Add(length);
        var start = 0;
        for (var i = 0; i < offsets.Count; i++)
        {
            var end = offsets[i];
            if (end <= start) continue;
            var x = rect.Left + (int)Math.Round(start * rect.Width / (double)length);
            var w = Math.Max(1, (int)Math.Round((end - start) * rect.Width / (double)length));
            using var brush = new SolidBrush(Color.FromArgb(210, colors[i % colors.Length]));
            g.FillRectangle(brush, x, rect.Top, Math.Min(w, rect.Right - x), rect.Height);
            start = end;
        }
        g.DrawRectangle(border, rect);
    }

    private static Color DefaultColor(byte value)
    {
        var r = (value * 73 + 40) % 256;
        var gr = (value * 37 + 90) % 256;
        var b = (value * 19 + 140) % 256;
        return Color.FromArgb(r, gr, b);
    }

    private static EexPreviewMetadata ReadMetadata(byte[] bytes)
    {
        var magic = bytes.Length >= 14 && bytes[0] == (byte)'E' && bytes[1] == (byte)'E' && bytes[2] == (byte)'X' && bytes[3] == 0;
        var headerSize = 14;
        var offsets = new List<int>();
        if (magic)
        {
            headerSize = checked((int)BitConverter.ToUInt32(bytes, 10));
            if (headerSize < 14 || headerSize > 256 || headerSize > bytes.Length)
            {
                headerSize = 14;
            }

            for (var offset = 14; offset + 4 <= headerSize && offset + 4 <= bytes.Length; offset += 4)
            {
                var value = checked((int)BitConverter.ToUInt32(bytes, offset));
                if (value >= headerSize && value < bytes.Length)
                {
                    offsets.Add(value);
                }
            }
        }

        var textHints = BinaryTextScanner
            .ScanGbkNullTerminatedStrings(bytes, minByteLength: 5, maxItems: 5)
            .Select(x => x.Length > 24 ? x[..24] + "…" : x)
            .ToList();

        return new EexPreviewMetadata(
            magic,
            bytes.Length >= 6 ? HexDisplayFormatter.FormatWord(BitConverter.ToUInt16(bytes, 4)) : "??",
            headerSize,
            offsets.Distinct().OrderBy(x => x).ToList(),
            textHints);
    }

    private static IReadOnlyList<Color> LoadSceneEditorPalette(CczProject project)
    {
        var toolDir = ResolveSceneEditorToolDirectory(project);
        if (toolDir == null) return Array.Empty<Color>();
        var path = Path.Combine(toolDir, "CczCustom.ini");
        if (!File.Exists(path)) return Array.Empty<Color>();

        var result = new List<Color>();
        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 3) continue;
            if (byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) &&
                byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var g) &&
                byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
            {
                result.Add(Color.FromArgb(r, g, b));
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> LoadSceneEditorConfig(CczProject project)
    {
        var toolDir = ResolveSceneEditorToolDirectory(project);
        if (toolDir == null) return new Dictionary<string, string>();
        return LoadIniKeyValues(Path.Combine(toolDir, "CczSceneEditor2.ini"));
    }

    private static IReadOnlyDictionary<string, string> LoadImageAssignerConfig(CczProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.ImageAssignerSystemIniPath) &&
            File.Exists(project.ImageAssignerSystemIniPath))
        {
            return LoadIniKeyValues(project.ImageAssignerSystemIniPath);
        }

        var toolDir = ResolveImageAssignerToolDirectory(project);
        if (toolDir == null) return new Dictionary<string, string>();
        return LoadIniKeyValues(Path.Combine(toolDir, "System.ini"));
    }

    private static IReadOnlyDictionary<string, string> LoadIniKeyValues(string path)
    {
        if (!File.Exists(path)) return new Dictionary<string, string>();

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("[", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal)) continue;
            var comment = line.IndexOf(';');
            if (comment >= 0) line = line[..comment].Trim();
            var index = line.IndexOf('=', StringComparison.Ordinal);
            if (index <= 0) continue;
            result[line[..index].Trim()] = line[(index + 1)..].Trim();
        }

        return result;
    }

    private Bitmap? TryLoadFaceImage(CczProject project, int faceId)
    {
        var facePath = ResolveFaceFile(project);
        if (facePath == null) return null;
        return TryLoadE5EntryImage(facePath, faceId + 1, out _);
    }

    private Bitmap? TryLoadMappedFaceImage(CczProject project, int dataFaceId, out string caption)
    {
        caption = $"头像号 {dataFaceId}";
        var facePath = ResolveFaceFile(project);
        if (facePath == null) return null;

        var mapping = new CharacterImageResourceService().MapFaceId(dataFaceId);
        var entries = GetE5ImageIndex(facePath);
        if (entries.Count == 0) return null;

        // 教程口径：Face.e5 图号为 1-based。
        var preferredFaceNumber = mapping.FaceImageNumbers.FirstOrDefault();
        if (preferredFaceNumber <= 0 || preferredFaceNumber > entries.Count) preferredFaceNumber = 1;

        caption = mapping.FaceImageNumbers.Count == 1
            ? $"头像号 {dataFaceId} -> Face#{preferredFaceNumber}"
            : $"头像号 {dataFaceId} -> Face#{mapping.FaceImageNumbers.First()}-{mapping.FaceImageNumbers.Last()}";

        return TryLoadE5EntryImage(facePath, preferredFaceNumber, out _);
    }

    private Bitmap? TryLoadE5EntryImage(string path, int imageNumber, out string detail)
    {
        detail = "未读取";
        if (!File.Exists(path))
        {
            detail = "文件不存在";
            return null;
        }

        var entries = GetE5ImageIndex(path);
        if (imageNumber <= 0 || imageNumber > entries.Count)
        {
            detail = $"索引越界 #{imageNumber}/{entries.Count}";
            return null;
        }

        var entry = entries[imageNumber - 1];
        var bytes = TryReadEntryBytes(path, entry, out var readDetail);
        if (bytes == null)
        {
            detail = readDetail;
            return null;
        }

        var image = TryDecodeStandardImage(bytes);
        if (image != null)
        {
            detail = $"{entry.Kind}{BuildEntryLocationText(entry)}";
            return image;
        }

        detail = $"未知格式 first={HexDisplayFormatter.Format(bytes.Length == 0 ? 0 : bytes[0], 2)}{BuildEntryLocationText(entry)}";
        return null;
    }

    private static string? ResolveFaceFile(CczProject project)
    {
        var candidates = new[]
        {
            Path.Combine(project.GameRoot, "E5", "Face.e5"),
            Path.Combine(project.GameRoot, "Face.e5"),
            Path.Combine(Directory.GetParent(project.GameRoot)?.FullName ?? project.WorkspaceRoot, "E5", "Face.e5")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset)
        => (bytes[offset] << 24) |
           (bytes[offset + 1] << 16) |
           (bytes[offset + 2] << 8) |
           bytes[offset + 3];

    private static uint ReadBigEndianUInt32(byte[] bytes, int offset)
        => ((uint)bytes[offset] << 24) |
           ((uint)bytes[offset + 1] << 16) |
           ((uint)bytes[offset + 2] << 8) |
           bytes[offset + 3];

    private static bool Matches(byte[] bytes, int offset, byte[] magic)
    {
        for (var i = 0; i < magic.Length; i++)
        {
            if (bytes[offset + i] != magic[i]) return false;
        }

        return true;
    }

    private static string? ResolveSceneEditorToolDirectory(CczProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.SceneEditorDirectory) &&
            Directory.Exists(project.SceneEditorDirectory))
        {
            return project.SceneEditorDirectory;
        }

        return ProjectDetector.FindPortableDirectory(
            project,
            "a新剧本编辑器v0.23",
            Path.Combine("老版游戏制作工具", "a新剧本编辑器v0.23"),
            "a新剧本编辑器v0.23");
    }

    private static string? ResolveImageAssignerToolDirectory(CczProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.ImageAssignerDirectory) &&
            Directory.Exists(project.ImageAssignerDirectory))
        {
            return project.ImageAssignerDirectory;
        }

        return ProjectDetector.FindPortableDirectory(
            project,
            "形象指定器6.5",
            Path.Combine("老版游戏制作工具", "B形象指定器", "6.6x形象指定器"),
            Path.Combine("B形象指定器", "6.6x形象指定器"),
            Path.Combine("老版游戏制作工具", "B形象指定器", "形象指定器6.5"),
            Path.Combine("B形象指定器", "形象指定器6.5"));
    }

    private static string NormalizePrefix(string prefix)
    {
        if (prefix.Equals("S", StringComparison.OrdinalIgnoreCase)) return "S";
        return "R";
    }

    private static string NormalizeRSceneFacing(string facing)
        => facing switch
        {
            "上" => "上",
            "左" => "左",
            "右" => "右",
            _ => "下"
        };

    private static RSceneFrameMapping ResolveRSceneFrameMapping(int rImageId, string facing, int gestureIndex)
    {
        var frame = RSceneGestureToPmapObjStripFrame(gestureIndex);
        return ResolveRScenePhysicalFrameMapping(rImageId, facing, frame);
    }

    private static RSceneFrameMapping ResolveRScenePhysicalFrameMapping(int rImageId, string facing, int frame)
    {
        var frontImageNumber = checked(rImageId * 2 + 1);
        var backImageNumber = checked(rImageId * 2 + 2);
        return facing switch
        {
            "上" => new RSceneFrameMapping(backImageNumber, frame, false),
            "左" => new RSceneFrameMapping(backImageNumber, frame, true),
            "右" => new RSceneFrameMapping(frontImageNumber, frame, true),
            _ => new RSceneFrameMapping(frontImageNumber, frame, false)
        };
    }

    private static int NormalizeRSceneGestureIndex(int value)
        => Math.Clamp(value < 0 ? 0 : value, 0, RawPreviewSpecs.RSceneGestureCount - 1);

    private static int RSceneGestureToPmapObjStripFrame(int gestureIndex)
        => gestureIndex <= 0
            ? 0
            : Math.Clamp(gestureIndex + 2, 0, RawPreviewSpecs.PmapObjFrameCount - 1);

    private static readonly byte[] PngMagic = { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };

    private sealed record E5ImageEntry(int Number, int Offset, int StoredLength, int DecodedLength, string Kind)
    {
        public bool IsCompressed => StoredLength != DecodedLength;
    }

    private sealed record RawPalette(IReadOnlyList<Color> Colors, string Mode, string Path);

    private sealed record UnitPreviewSpec(string FileName, int RawWidth, int FrameHeight, string Label);

    private sealed record UnitFramePreview(string Label, Bitmap Image);

    private sealed record UnitImagePreviewGroup(int ImageNumber, IReadOnlyList<UnitFramePreview> Frames);

    private sealed record SImageFactionPreviewRow(string FactionText, int FactionSlot, Bitmap? Image, string Caption);

    private sealed record RSceneFrameMapping(int ImageNumber, int FrameIndex, bool FlipHorizontal);

    private sealed record EexPreviewMetadata(
        bool MagicValid,
        string VersionHex,
        int HeaderSize,
        IReadOnlyList<int> PlausibleOffsets,
        IReadOnlyList<string> TextHints);

    private static readonly UnitPreviewSpec[] UnitPreviewSpecs =
    [
        new("Unit_mov.e5", 48, 48, "移动"),
        new("Unit_atk.e5", 64, 64, "攻击"),
        new("Unit_spc.e5", 48, 48, "特技")
    ];

    private static class RawPreviewSpecs
    {
        public const int PmapObjWidth = 48;
        public const int PmapObjFrameHeight = 64;
        public const int PmapObjFrameCount = 20;
        public const int RSceneGestureCount = 20;
    }
}
