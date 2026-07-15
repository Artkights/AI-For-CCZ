using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
    private readonly ConcurrentDictionary<string, IReadOnlyList<E5ImageEntry>> _e5ImageIndexCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RawPalette> _rawPaletteCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ImageAssignmentAvailabilityReport> _availabilityReportCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly CharacterImageLayoutService _layoutService = new();
    private readonly E5RawPaletteService _paletteService = new();
    private readonly E5ImageReadSessionPool _readSessions;
    private readonly ImagePreviewCache _previewCache;

    public ImageAssignmentPreviewService()
        : this(E5ImageReadSessionPool.Shared, ImagePreviewCache.Shared)
    {
    }

    internal ImageAssignmentPreviewService(E5ImageReadSessionPool readSessions, ImagePreviewCache previewCache)
    {
        _readSessions = readSessions;
        _previewCache = previewCache;
    }

    public void ClearCache()
    {
        _e5ImageIndexCache.Clear();
        _rawPaletteCache.Clear();
        _availabilityReportCache.Clear();
        _previewCache.ClearMemory();
    }

    public void InvalidateResources(IEnumerable<string> paths)
    {
        var resourcePaths = paths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath).ToArray();
        _e5ImageIndexCache.Clear();
        _availabilityReportCache.Clear();
        _readSessions.Invalidate(resourcePaths);
        _previewCache.Invalidate(resourcePaths);
    }

    internal bool TryProbeRsEntry(string path, int imageNumber, out RsEntryProbeResult? result, out string detail)
    {
        result = null;
        detail = string.Empty;
        if (!File.Exists(path))
        {
            detail = "文件不存在：" + path;
            return false;
        }

        var entries = GetE5ImageIndex(path);
        if (imageNumber <= 0 || imageNumber > entries.Count)
        {
            detail = $"索引越界 #{imageNumber}/{entries.Count}";
            return false;
        }

        var entry = entries[imageNumber - 1];
        var bytes = TryReadEntryBytes(path, entry, out var readDetail);
        if (bytes == null)
        {
            detail = readDetail;
            return false;
        }

        RsStripProbeResult strip;
        try
        {
            strip = RsStripLayoutService.Probe(path, entry.Kind, bytes);
        }
        catch (InvalidOperationException ex)
        {
            detail = ex.Message;
            return false;
        }

        var fingerprint = _readSessions.GetSession(path).Fingerprint;
        result = new RsEntryProbeResult(
            strip,
            imageNumber,
            entry.Offset,
            entry.StoredLength,
            entry.DecodedLength,
            fingerprint.IndexSha256,
            Convert.ToHexString(SHA256.HashData(bytes)));
        detail = strip.Detail + BuildEntryLocationText(entry);
        return true;
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

    public Bitmap? TryRenderCharacterResourceImage(
        CczProject project,
        string prefix,
        int id,
        int? jobId,
        int sFactionSlot)
    {
        prefix = NormalizePrefix(prefix);
        return TryLoadMappedE5Image(project, prefix, id, jobId, sFactionSlot, out _);
    }

    public IReadOnlyList<int> GetAvailableCharacterImageIds(CczProject project, string prefix, bool includeZero = false)
        => GetAvailableCharacterImageIds(project, prefix, includeZero, out _);

    public IReadOnlyList<int> GetAvailableCharacterImageIds(CczProject project, string prefix, bool includeZero, out bool fromCache)
    {
        var report = ScanAvailableCharacterImageIds(project, prefix, includeZero, forceFresh: false);
        fromCache = report.FromCache;
        return report.AvailableIds;
    }

    internal ImageAssignmentAvailabilityReport ScanAvailableCharacterImageIds(
        CczProject project,
        string prefix,
        bool includeZero,
        bool forceFresh)
    {
        var normalizedPrefix = prefix.Equals("Face", StringComparison.OrdinalIgnoreCase)
            ? "Face"
            : NormalizePrefix(prefix);
        var paths = ResolveAvailabilityPaths(project, normalizedPrefix);
        if (forceFresh)
        {
            _e5ImageIndexCache.Clear();
            _availabilityReportCache.Clear();
            _readSessions.Invalidate(paths);
        }

        var fingerprints = BuildAvailabilityFingerprints(paths);
        var cacheKey = BuildAvailableImageIdCacheKey(project, normalizedPrefix, includeZero, fingerprints);
        if (_availabilityReportCache.TryGetValue(cacheKey, out var cached))
            return cached with { FromCache = true };

        ImageAssignmentAvailabilityReport report;
        if (normalizedPrefix == "Face")
        {
            report = new ImageAssignmentAvailabilityReport(
                GetAvailableFaceIds(project, includeZero),
                Array.Empty<ImageAssignmentRejectedCandidate>(),
                fingerprints,
                false);
        }
        else
        {
            report = normalizedPrefix == "S"
                ? ScanAvailableSImageIds(project, includeZero, fingerprints)
                : ScanAvailableRImageIds(project, includeZero, fingerprints);
        }

        _availabilityReportCache[cacheKey] = report with { FromCache = false };
        return report;
    }

    public Bitmap? TryRenderSImageFactionStackPreview(
        CczProject project,
        int sImageId,
        int? jobId,
        out string detail)
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

        if (stripFrameIndex < 0 || stripFrameIndex >= RawPreviewSpecs.PmapObjFrameCount)
        {
            detail = $"物理帧索引越界：请求 {stripFrameIndex}，可用范围 0-{RawPreviewSpecs.PmapObjFrameCount - 1}。";
            return null;
        }

        var normalizedFacing = NormalizeRSceneFacing(facing);
        var frameMapping = ResolveRScenePhysicalFrameMapping(rImageId, normalizedFacing, stripFrameIndex);
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
        int stageSlot,
        out string detail)
    {
        detail = string.Empty;
        var stage = CharacterImageResourceService.ResolveSPreviewStage(project, sImageId, jobId, factionSlot, stageSlot);
        var mapping = stage.Mapping;
        var stageTarget = stage.Target;
        if (stageTarget == null)
        {
            detail = mapping.Detail;
            return null;
        }

        var normalizedDirection = NormalizeBattlefieldDirection(direction);
        var definition = RsActionSequenceCatalog.Resolve(
            "Unit_mov.e5",
            "待机/移动",
            RsActionSequenceCatalog.DirectionToFacing(normalizedDirection));
        var phase = Math.Abs(framePhase) % definition.PhysicalFrameIndices.Count;
        var frameIndex = definition.PhysicalFrameIndices[phase];
        var flipHorizontal = normalizedDirection == "右";
        var path = CharacterImageResourceService.ResolveGameFile(project, "Unit_mov.e5");
        var frame = TryRenderE5EntryFrameAt(project, path, stageTarget.ImageNumber, 48, 48, frameIndex, flipHorizontal, out var readDetail);
        var fallbackDetail = string.IsNullOrWhiteSpace(stage.FallbackDetail) ? string.Empty : stage.FallbackDetail + " ";
        detail = frame == null
            ? $"{fallbackDetail}{mapping.ShortText} {stageTarget.DisplayName} -> Unit_mov.e5 #{stageTarget.ImageNumber} 待机{normalizedDirection} 第{phase + 1}帧未读取：{readDetail}"
            : $"{fallbackDetail}{mapping.ShortText} {stageTarget.DisplayName} -> Unit_mov.e5 #{stageTarget.ImageNumber} 待机{normalizedDirection} 第{phase + 1}帧";
        return frame;
    }

    internal Bitmap? TryRenderSAssignmentRepresentativeFrame(
        CczProject project,
        int sImageId,
        int? jobId,
        int factionSlot,
        string direction,
        int requestedStageSlot,
        out SImagePreviewStageResolution stage,
        out string sourceLabel,
        out string detail)
    {
        stage = CharacterImageResourceService.ResolveSPreviewStage(
            project,
            sImageId,
            jobId,
            factionSlot,
            requestedStageSlot);
        sourceLabel = string.Empty;
        if (stage.Target == null)
        {
            detail = stage.Mapping.Detail;
            return null;
        }

        var normalizedDirection = NormalizeBattlefieldDirection(direction);
        var flipHorizontal = normalizedDirection == "右";
        var facing = RsActionSequenceCatalog.DirectionToFacing(normalizedDirection);
        var move = RsActionSequenceCatalog.Resolve("Unit_mov.e5", "待机/移动", facing);
        var attack = RsActionSequenceCatalog.Resolve("Unit_atk.e5", "攻击", facing);
        var special = RsActionSequenceCatalog.Resolve("Unit_spc.e5", "特技", null);
        var candidates = new[]
        {
            new SRepresentativeSpec("Unit_mov.e5", 48, 48, move.PhysicalFrameIndices[0], "移动"),
            new SRepresentativeSpec("Unit_atk.e5", 64, 64, attack.PhysicalFrameIndices[0], "攻击"),
            new SRepresentativeSpec("Unit_spc.e5", 48, 48, special.PhysicalFrameIndices[0], "特技")
        };
        var errors = new List<string>();
        foreach (var candidate in candidates)
        {
            var path = CharacterImageResourceService.ResolveGameFile(project, candidate.FileName);
            var frame = TryRenderE5EntryFrameAt(
                project,
                path,
                stage.Target.ImageNumber,
                candidate.FrameWidth,
                candidate.FrameHeight,
                candidate.FrameIndex,
                flipHorizontal,
                out var readDetail);
            if (frame != null)
            {
                sourceLabel = candidate.Label;
                detail = string.Join(" ", new[]
                {
                    stage.FallbackDetail,
                    $"代表帧：{candidate.FileName} #{stage.Target.ImageNumber} 物理帧 {candidate.FrameIndex}。"
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
                return frame;
            }

            errors.Add($"{candidate.FileName}: {readDetail}");
        }

        detail = string.Join(" ", new[]
        {
            stage.FallbackDetail,
            "移动、攻击、特技代表帧均不可读取：" + string.Join("；", errors)
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return null;
    }

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
        var definition = RsActionSequenceCatalog.Resolve(
            "Unit_mov.e5",
            "待机/移动",
            RsActionSequenceCatalog.DirectionToFacing(normalizedDirection));
        var phase = Math.Abs(framePhase) % definition.PhysicalFrameIndices.Count;
        var frameIndex = definition.PhysicalFrameIndices[phase];
        var flipHorizontal = normalizedDirection == "右";
        var path = CharacterImageResourceService.ResolveGameFile(project, "Unit_mov.e5");
        var frame = TryRenderE5EntryFrameAt(project, path, imageNumber, 48, 48, frameIndex, flipHorizontal, out var readDetail);
        detail = frame == null
            ? $"{mapping.ShortText} {normalizedLevelMode} -> Unit_mov.e5 #{imageNumber} 待机{normalizedDirection} 第{phase + 1}帧未读取：{readDetail}"
            : $"{mapping.ShortText} {normalizedLevelMode} -> Unit_mov.e5 #{imageNumber} 待机{normalizedDirection} 第{phase + 1}帧";
        return frame;
    }

    internal CharacterImageAnimationPreview BuildRAnimationPreview(CczProject project, int rImageId)
    {
        var cells = new List<CharacterImageAnimationCell>();
        var messages = new List<string>();
        if (rImageId < 0)
        {
            messages.Add($"R形象编号不能小于0：{rImageId}");
            return new CharacterImageAnimationPreview(
                ImageAssignmentResourceKind.R,
                rImageId,
                1,
                "R形象动画",
                2,
                1,
                RawPreviewSpecs.PmapObjWidth,
                RawPreviewSpecs.PmapObjFrameHeight,
                cells,
                messages);
        }

        var path = CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5");
        if (!File.Exists(path))
        {
            messages.Add("缺少 Pmapobj.e5：" + path);
        }

        cells.Add(BuildRAnimationCell(
            project,
            rImageId,
            row: 0,
            column: 0,
            label: "正面",
            path,
            checked(rImageId * 2 + 1),
            flipHorizontal: false,
            messages));
        cells.Add(BuildRAnimationCell(
            project,
            rImageId,
            row: 0,
            column: 1,
            label: "背面",
            path,
            checked(rImageId * 2 + 2),
            flipHorizontal: false,
            messages));

        return new CharacterImageAnimationPreview(
            ImageAssignmentResourceKind.R,
            rImageId,
            1,
            $"R{rImageId}动画",
            2,
            1,
            RawPreviewSpecs.PmapObjWidth,
            RawPreviewSpecs.PmapObjFrameHeight,
            cells,
            messages);
    }

    internal CharacterImageAnimationPreview BuildSAnimationPreview(
        CczProject project,
        int sImageId,
        int? jobId,
        int factionSlot,
        int stageSlot = 1)
    {
        var sequences = new List<CharacterImageAnimationSequence>();
        var messages = new List<string>();
        if (sImageId < 0)
        {
            messages.Add($"S形象编号不能小于0：{sImageId}");
            return new CharacterImageAnimationPreview(
                ImageAssignmentResourceKind.S,
                sImageId,
                1,
                "S形象动画",
                3,
                3,
                64,
                64,
                Array.Empty<CharacterImageAnimationCell>(),
                messages);
        }

        var stageResolution = CharacterImageResourceService.ResolveSPreviewStage(project, sImageId, jobId, factionSlot, stageSlot);
        var mapping = stageResolution.Mapping;
        if (mapping.ImageNumbers.Count == 0)
        {
            messages.Add(mapping.Detail);
        }

        var stageTarget = stageResolution.Target;
        var imageNumber = stageTarget?.ImageNumber ?? 0;
        if (imageNumber <= 0)
        {
            messages.Add("未解析到战场图号。");
        }
        if (stageResolution.IsOneStageFallback)
        {
            messages.Add(stageResolution.FallbackDetail);
        }

        foreach (var resourceGroup in RsActionSequenceCatalog.SDefinitions.GroupBy(definition => definition.SourceFile))
        {
            var path = CharacterImageResourceService.ResolveGameFile(project, resourceGroup.Key);
            if (!File.Exists(path))
            {
                messages.Add("缺少 " + resourceGroup.Key + "：" + path);
            }

            using var strip = TryLoadE5EntryStrip(
                project,
                path,
                imageNumber,
                new RsFrameLayoutResolver().Resolve(path).FrameWidth,
                new RsFrameLayoutResolver().Resolve(path).FrameHeight,
                out var stripDetail);
            var layout = new RsFrameLayoutResolver().Resolve(path);
            foreach (var definition in resourceGroup)
            {
                sequences.Add(BuildSAnimationSequenceFromStrip(
                    sImageId,
                    imageNumber,
                    stageSlot,
                    stageResolution.EffectiveStageSlot,
                    definition,
                    path,
                    strip,
                    layout,
                    messages,
                    stripDetail));
            }
        }

        return new CharacterImageAnimationPreview(
            ImageAssignmentResourceKind.S,
            sImageId,
            stageResolution.EffectiveStageSlot,
            $"S{sImageId}动画",
            3,
            3,
            64,
            64,
            Array.Empty<CharacterImageAnimationCell>(),
            messages)
        {
            Sequences = sequences
        };
    }

    internal async Task<CharacterImageAnimationPreview> LoadAnimationAsync(
        CczProject project,
        ImageAssignmentResourceKind kind,
        int imageId,
        int? jobId,
        int factionSlot,
        int stageSlot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var paths = kind == ImageAssignmentResourceKind.S
            ? new[] { "Unit_atk.e5", "Unit_mov.e5", "Unit_spc.e5" }
                .Select(fileName => CharacterImageResourceService.ResolveGameFile(project, fileName))
                .ToArray()
            : new[] { CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5") };
        var identity = string.Join("|", paths.Select(path =>
        {
            var fullPath = Path.GetFullPath(path);
            return File.Exists(fullPath)
                ? $"{fullPath.ToUpperInvariant()}|{_readSessions.GetSession(fullPath).Fingerprint}"
                : fullPath.ToUpperInvariant() + "|missing";
        }));
        var stageResolution = kind == ImageAssignmentResourceKind.S
            ? CharacterImageResourceService.ResolveSPreviewStage(project, imageId, jobId, factionSlot, stageSlot)
            : null;
        var effectiveStageSlot = stageResolution?.EffectiveStageSlot ?? stageSlot;
        var baseKey = string.Join("|", "animation-v4", RsActionSequenceCatalog.ContractVersion, identity, kind, imageId, jobId, factionSlot,
            $"requested={stageSlot}", $"effective={effectiveStageSlot}");
        using var livePreview = await Task.Run(() =>
            kind == ImageAssignmentResourceKind.S
                ? BuildSAnimationPreview(project, imageId, jobId, factionSlot, stageSlot)
                : BuildRAnimationPreview(project, imageId),
            cancellationToken).ConfigureAwait(false);

        if (kind == ImageAssignmentResourceKind.S)
        {
            var cachedSequences = new List<CharacterImageAnimationSequence>(livePreview.Sequences.Count);
            var cachedFrames = new List<CachedPreviewImage>();
            foreach (var sequence in livePreview.Sequences)
            {
                var entryIdentity = TryProbeRsEntry(
                    sequence.SourcePath,
                    sequence.ImageNumber,
                    out var entryProbe,
                    out var entryDetail)
                    ? entryProbe!.CacheIdentity
                    : "unreadable:" + entryDetail;
                var frames = new List<CharacterImageAnimationSequenceFrame>(sequence.Frames.Count);
                foreach (var sourceFrame in sequence.Frames)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (sourceFrame.Bitmap == null)
                    {
                        frames.Add(new CharacterImageAnimationSequenceFrame(sourceFrame.PhysicalFrameIndex, null));
                        continue;
                    }

                    var key = string.Join("|",
                        baseKey,
                        $"action={sequence.Definition.Id}",
                        $"facing={sequence.Definition.Facing?.ToString() ?? "none"}",
                        $"physical={sourceFrame.PhysicalFrameIndex}",
                        $"image={sequence.ImageNumber}",
                        $"entry={entryIdentity}");
                    var sourceBitmap = sourceFrame.Bitmap;
                    var cached = await _previewCache.GetOrCreateAsync(
                        key,
                        () => Task.Run(() =>
                        {
                            using var output = new MemoryStream();
                            sourceBitmap.Save(output, ImageFormat.Png);
                            return (byte[]?)output.ToArray();
                        }, CancellationToken.None),
                        cancellationToken).ConfigureAwait(false);
                    if (cached == null)
                    {
                        frames.Add(new CharacterImageAnimationSequenceFrame(sourceFrame.PhysicalFrameIndex, null));
                        continue;
                    }

                    using var stream = new MemoryStream(cached.Bytes, writable: false);
                    using var image = Image.FromStream(stream, false, false);
                    frames.Add(new CharacterImageAnimationSequenceFrame(sourceFrame.PhysicalFrameIndex, new Bitmap(image)));
                    cachedFrames.Add(new CachedPreviewImage(cached.Bytes, image.Size, key, $"cache={cached.Source}"));
                }

                cachedSequences.Add(new CharacterImageAnimationSequence(
                    sequence.Definition,
                    sequence.SourcePath,
                    sequence.ImageNumber,
                    sequence.RequestedStageSlot,
                    sequence.EffectiveStageSlot,
                    frames));
            }

            return new CharacterImageAnimationPreview(
                kind,
                imageId,
                effectiveStageSlot,
                $"S{imageId}动画",
                1,
                1,
                64,
                64,
                Array.Empty<CharacterImageAnimationCell>(),
                livePreview.Messages)
            {
                Sequences = cachedSequences,
                PrecomposedFrames = cachedFrames
            };
        }

        var precomposed = new List<CachedPreviewImage>(RawPreviewSpecs.PmapObjFrameCount);
        for (var frameIndex = 0; frameIndex < RawPreviewSpecs.PmapObjFrameCount; frameIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentFrame = frameIndex;
            var key = $"{baseKey}|physical={currentFrame}";
            var cached = await _previewCache.GetOrCreateAsync(
                key,
                () => Task.Run(() =>
                {
                    using var canvas = BuildAnimationCanvas(livePreview, currentFrame);
                    using var output = new MemoryStream();
                    canvas.Save(output, ImageFormat.Png);
                    return (byte[]?)output.ToArray();
                }, CancellationToken.None),
                cancellationToken).ConfigureAwait(false);
            if (cached == null) continue;
            using var stream = new MemoryStream(cached.Bytes, writable: false);
            using var image = Image.FromStream(stream, false, false);
            precomposed.Add(new CachedPreviewImage(cached.Bytes, image.Size, key, $"cache={cached.Source}"));
        }

        return new CharacterImageAnimationPreview(
            kind,
            imageId,
            effectiveStageSlot,
            $"R{imageId}动画",
            2,
            1,
            RawPreviewSpecs.PmapObjWidth,
            RawPreviewSpecs.PmapObjFrameHeight,
            Array.Empty<CharacterImageAnimationCell>(),
            livePreview.Messages)
        {
            PrecomposedFrames = precomposed
        };
    }

    internal Bitmap BuildAnimationCanvas(CharacterImageAnimationPreview preview, int frameIndex)
    {
        if (preview.Sequences.Count > 0)
        {
            return BuildAnimationSequenceFrame(preview, 0, frameIndex, mirrorHorizontal: false);
        }

        if (preview.PrecomposedFrames.Count > 0)
        {
            return preview.PrecomposedFrames[Math.Abs(frameIndex) % preview.PrecomposedFrames.Count].CreateBitmap();
        }

        const int padding = 0;
        const int gap = 2;
        var cellWidth = Math.Max(48, preview.CellWidth);
        var cellHeight = Math.Max(48, preview.CellHeight);
        var width = padding * 2 + preview.Columns * cellWidth + Math.Max(0, preview.Columns - 1) * gap;
        var height = padding * 2 + preview.Rows * cellHeight + Math.Max(0, preview.Rows - 1) * gap;
        var bitmap = new Bitmap(Math.Max(1, width), Math.Max(1, height), PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(24, 26, 28));
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        using var borderPen = new Pen(Color.FromArgb(92, 102, 112));
        using var cellBrush = new SolidBrush(Color.FromArgb(32, 35, 39));

        for (var row = 0; row < preview.Rows; row++)
        {
            for (var column = 0; column < preview.Columns; column++)
            {
                var left = padding + column * (cellWidth + gap);
                var top = padding + row * (cellHeight + gap);
                var cellRect = new Rectangle(left, top, cellWidth, cellHeight);
                var imageRect = cellRect;

                g.FillRectangle(cellBrush, cellRect);

                var cell = preview.Cells.FirstOrDefault(item => item.Row == row && item.Column == column);

                if (cell?.Frames.Count > 0)
                {
                    var frame = cell.Frames[Math.Abs(frameIndex) % cell.Frames.Count];
                    if (frame != null)
                    {
                        DrawCenteredImage(g, frame, imageRect, borderPen);
                        continue;
                    }
                }

                g.DrawRectangle(borderPen, imageRect);
            }
        }

        return bitmap;
    }

    internal Bitmap BuildAnimationSequenceFrame(
        CharacterImageAnimationPreview preview,
        int sequenceIndex,
        int frameIndex,
        bool mirrorHorizontal)
    {
        if (sequenceIndex < 0 || sequenceIndex >= preview.Sequences.Count)
            return new Bitmap(Math.Max(1, preview.CellWidth), Math.Max(1, preview.CellHeight), PixelFormat.Format32bppArgb);
        var sequence = preview.Sequences[sequenceIndex];
        if (sequence.Frames.Count == 0)
            return new Bitmap(Math.Max(1, preview.CellWidth), Math.Max(1, preview.CellHeight), PixelFormat.Format32bppArgb);

        var normalizedIndex = Math.Abs(frameIndex) % sequence.Frames.Count;
        var source = sequence.Frames[normalizedIndex].Bitmap;
        if (source == null)
            return new Bitmap(Math.Max(1, preview.CellWidth), Math.Max(1, preview.CellHeight), PixelFormat.Format32bppArgb);

        var output = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(output))
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(source, new Rectangle(Point.Empty, output.Size), new Rectangle(Point.Empty, source.Size), GraphicsUnit.Pixel);
        }
        if (mirrorHorizontal) output.RotateFlip(RotateFlipType.RotateNoneFlipX);
        return output;
    }

    private static Rectangle DrawCenteredImage(Graphics g, Image image, Rectangle rect, Pen borderPen)
    {
        using var back = new SolidBrush(Color.FromArgb(16, 18, 20));
        g.FillRectangle(back, rect);
        g.DrawRectangle(borderPen, rect);

        var scale = Math.Min(rect.Width / (float)image.Width, rect.Height / (float)image.Height);
        var w = (int)Math.Round(image.Width * scale);
        var h = (int)Math.Round(image.Height * scale);
        var x = rect.Left + (rect.Width - w) / 2;
        var y = rect.Top + (rect.Height - h) / 2;
        var destination = new Rectangle(x, y, w, h);
        g.DrawImage(image, destination);
        return destination;
    }

    private Bitmap? TryLoadMappedE5Image(
        CczProject project,
        string prefix,
        int id,
        int? jobId,
        int sFactionSlot,
        out string caption)
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

    private CharacterImageAnimationCell BuildRAnimationCell(
        CczProject project,
        int rImageId,
        int row,
        int column,
        string label,
        string path,
        int imageNumber,
        bool flipHorizontal,
        List<string> messages)
    {
        var frames = new List<Bitmap?>();
        for (var frameIndex = 0; frameIndex < RawPreviewSpecs.PmapObjFrameCount; frameIndex++)
        {
            var frame = TryRenderE5EntryFrameAt(
                project,
                path,
                imageNumber,
                RawPreviewSpecs.PmapObjWidth,
                RawPreviewSpecs.PmapObjFrameHeight,
                frameIndex,
                flipHorizontal,
                out var detail);
            frames.Add(frame);
            if (frame == null && frameIndex == 0)
            {
                messages.Add($"R{rImageId} {label} #{imageNumber}: {detail}");
            }
        }

        return new CharacterImageAnimationCell(row, column, label, frames);
    }

    private static CharacterImageAnimationSequence BuildSAnimationSequenceFromStrip(
        int sImageId,
        int imageNumber,
        int requestedStageSlot,
        int effectiveStageSlot,
        RsActionSequenceDefinition definition,
        string path,
        Bitmap? strip,
        RsFrameLayout layout,
        List<string> messages,
        string loadDetail)
    {
        var frames = new List<CharacterImageAnimationSequenceFrame>();
        if (imageNumber <= 0 || strip == null)
        {
            messages.Add($"S{sImageId} {definition.ActionLabel} #{imageNumber}: {loadDetail}");
            frames.AddRange(definition.PhysicalFrameIndices.Select(index => new CharacterImageAnimationSequenceFrame(index, null)));
            return new CharacterImageAnimationSequence(
                definition,
                path,
                imageNumber,
                requestedStageSlot,
                effectiveStageSlot,
                frames);
        }

        foreach (var frameIndex in definition.PhysicalFrameIndices)
        {
            Bitmap? frame = null;
            try
            {
                frame = RsStripLayoutService.CropFrame(strip, layout, frameIndex);
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or InvalidOperationException)
            {
                messages.Add($"S{sImageId} {definition.ActionLabel} #{imageNumber} 物理帧 {frameIndex}: {ex.Message}");
            }
            frames.Add(new CharacterImageAnimationSequenceFrame(frameIndex, frame));
        }

        return new CharacterImageAnimationSequence(
            definition,
            path,
            imageNumber,
            requestedStageSlot,
            effectiveStageSlot,
            frames);
    }

    private Bitmap? TryLoadE5EntryStrip(
        CczProject project,
        string path,
        int imageNumber,
        int rawWidth,
        int frameHeight,
        out string detail)
    {
        if (!File.Exists(path))
        {
            detail = "文件不存在";
            return null;
        }

        var indexProbe = _readSessions.GetSession(path).ProbeIndex();
        if (!indexProbe.IsComplete)
        {
            detail = indexProbe.Diagnostic;
            return null;
        }

        var entries = GetE5ImageIndex(path);
        if (imageNumber <= 0 || imageNumber > entries.Count)
        {
            detail = $"索引越界 #{imageNumber}/{entries.Count}";
            return null;
        }

        if (!TryProbeRsEntry(path, imageNumber, out var entryProbe, out detail) || entryProbe == null)
        {
            return null;
        }

        if (!entryProbe.Strip.IsSupportedLayout)
        {
            detail = entryProbe.Strip.Detail;
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
            detail = $"{entryProbe.Strip.Detail}{BuildEntryLocationText(entry)}";
            return new Bitmap(decoded);
        }

        if (entryProbe.Strip.IsSupportedLayout)
        {
            var palette = LoadRawPalette(project);
            var strip = TryRenderRawIndexedStripBitmap(
                bytes,
                entryProbe.Strip.Layout.FrameWidth,
                entryProbe.Strip.Layout.FrameHeight,
                palette.Colors,
                palette.Mode,
                out var paletteMode);
            detail = $"{paletteMode}；{entryProbe.Strip.Detail}{BuildEntryLocationText(entry)}";
            return strip;
        }

        detail = $"未知格式 first={HexDisplayFormatter.Format(bytes.Length == 0 ? 0 : bytes[0], 2)}{BuildEntryLocationText(entry)}";
        return null;
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

    private Bitmap? TryRenderSImage(
        CczProject project,
        int sImageId,
        int? jobId,
        int sFactionSlot,
        out string caption)
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

        if (!TryProbeRsEntry(path, imageNumber, out var entryProbe, out detail) || entryProbe == null)
        {
            return null;
        }

        if (!entryProbe.Strip.IsSupportedLayout)
        {
            detail = entryProbe.Strip.Detail;
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
            detail = $"{entryProbe.Strip.Detail}{BuildEntryLocationText(entry)}";
            return CropRepresentativeFrame(decoded, entryProbe.Strip.Layout);
        }

        if (entryProbe.Strip.IsSupportedLayout)
        {
            var rawPalette = LoadRawPalette(project);
            var raw = TryRenderRawIndexedStrip(
                bytes,
                entryProbe.Strip.Layout,
                rawPalette.Colors,
                rawPalette.Mode,
                out var paletteMode);
            if (raw != null)
            {
                detail = $"{paletteMode}；{entryProbe.Strip.Detail}{BuildEntryLocationText(entry)}";
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

        if (!TryProbeRsEntry(path, imageNumber, out var entryProbe, out detail) || entryProbe == null)
        {
            return null;
        }

        if (!entryProbe.Strip.IsSupportedLayout)
        {
            detail = entryProbe.Strip.Detail;
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
            detail = $"{entryProbe.Strip.Detail}{BuildEntryLocationText(entry)}";
        }
        else if (entryProbe.Strip.IsSupportedLayout)
        {
            var rawPalette = LoadRawPalette(project);
            strip = TryRenderRawIndexedStripBitmap(
                bytes,
                entryProbe.Strip.Layout.FrameWidth,
                entryProbe.Strip.Layout.FrameHeight,
                rawPalette.Colors,
                rawPalette.Mode,
                out var paletteMode);
            detail = $"{paletteMode}；{entryProbe.Strip.Detail}{BuildEntryLocationText(entry)}";
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

            var frameCount = entryProbe.Strip.AvailableFrameCount;
            if (frameIndex < 0 || frameIndex >= frameCount)
            {
                detail = $"帧越界 {frameIndex + 1}/{frameCount}{BuildEntryLocationText(entry)}";
                return null;
            }

            var frame = RsStripLayoutService.CropFrame(strip, entryProbe.Strip.Layout, frameIndex);
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

        var result = _readSessions.GetSession(path)
            .ReadIndex()
            .Select(entry => new E5ImageEntry(
                entry.ImageNumber,
                entry.DataOffset,
                entry.StoredLength,
                entry.DecodedLength,
                entry.Kind))
            .ToArray();

        _e5ImageIndexCache[key] = result;
        return result;
    }

    private static string BuildAvailableImageIdCacheKey(
        CczProject project,
        string prefix,
        bool includeZero,
        IReadOnlyList<ImageAssignmentResourceFingerprint> fingerprints)
    {
        return string.Join("|",
            new[]
            {
                "availability-v2",
                RsStripLayoutService.ContractVersion,
                Path.GetFullPath(project.GameRoot),
                prefix,
                includeZero ? "1" : "0"
            }.Concat(fingerprints.Select(fingerprint => string.Join(":",
                fingerprint.Path.ToUpperInvariant(),
                fingerprint.Length,
                fingerprint.LastWriteTimeUtcTicks,
                fingerprint.IndexSha256,
                fingerprint.EntryCount))));
    }

    private static IReadOnlyList<string> ResolveAvailabilityPaths(CczProject project, string normalizedPrefix)
        => normalizedPrefix switch
        {
            "Face" => new[] { ResolveFaceFile(project) ?? Path.Combine(project.GameRoot, "E5", "Face.e5") },
            "S" => UnitPreviewSpecs
                .Select(spec => CharacterImageResourceService.ResolveGameFile(project, spec.FileName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            _ => new[] { CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5") }
        };

    private IReadOnlyList<ImageAssignmentResourceFingerprint> BuildAvailabilityFingerprints(IEnumerable<string> paths)
    {
        var result = new List<ImageAssignmentResourceFingerprint>();
        foreach (var path in paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
            {
                result.Add(new ImageAssignmentResourceFingerprint(path, 0, 0, "missing", 0));
                continue;
            }

            var session = _readSessions.GetSession(path);
            var fingerprint = session.Fingerprint;
            result.Add(new ImageAssignmentResourceFingerprint(
                path,
                fingerprint.Length,
                fingerprint.LastWriteTimeUtcTicks,
                fingerprint.IndexSha256,
                session.ReadIndex().Count));
        }
        return result;
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

    private ImageAssignmentAvailabilityReport ScanAvailableRImageIds(
        CczProject project,
        bool includeZero,
        IReadOnlyList<ImageAssignmentResourceFingerprint> fingerprints)
    {
        var path = CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5");
        if (!File.Exists(path))
            return new ImageAssignmentAvailabilityReport(
                Array.Empty<int>(),
                Array.Empty<ImageAssignmentRejectedCandidate>(),
                fingerprints,
                false);

        var indexProbe = _readSessions.GetSession(path).ProbeIndex();
        if (!indexProbe.IsComplete)
        {
            return new ImageAssignmentAvailabilityReport(
                Array.Empty<int>(),
                Array.Empty<ImageAssignmentRejectedCandidate>(),
                fingerprints,
                false)
            {
                IsArchiveIntegrityValid = false,
                IntegrityDiagnostics = new[] { $"{Path.GetFileName(path)}: {indexProbe.Diagnostic}" }
            };
        }

        var entries = GetE5ImageIndex(path);
        var result = new List<int>();
        var rejected = new List<ImageAssignmentRejectedCandidate>();
        var candidateCount = checked((entries.Count + 1) / 2);
        for (var rImageId = includeZero ? 0 : 1; rImageId < candidateCount; rImageId++)
        {
            var frontNumber = checked(rImageId * 2 + 1);
            var backNumber = checked(rImageId * 2 + 2);
            var frontReadable = TryReadAvailability(path, frontNumber, entries.Count, "正图", out var frontReason);
            var backReadable = TryReadAvailability(path, backNumber, entries.Count, "反图", out var backReason);
            if (frontReadable || backReadable)
            {
                result.Add(rImageId);
            }
            else
            {
                rejected.Add(new ImageAssignmentRejectedCandidate(
                    rImageId,
                    new[] { frontReason, backReason }.Where(reason => !string.IsNullOrWhiteSpace(reason)).ToArray()));
            }
        }

        return new ImageAssignmentAvailabilityReport(result, rejected, fingerprints, false);
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
            if (IsStructurallyUsable(entries[preferredFaceNumber - 1], 1, 1))
            {
                result.Add(faceId);
            }
        }

        return result;
    }

    private ImageAssignmentAvailabilityReport ScanAvailableSImageIds(
        CczProject project,
        bool includeZero,
        IReadOnlyList<ImageAssignmentResourceFingerprint> fingerprints)
    {
        var resources = UnitPreviewSpecs
            .Select(spec => new
            {
                spec.FileName,
                Path = CharacterImageResourceService.ResolveGameFile(project, spec.FileName)
            })
            .Where(resource => File.Exists(resource.Path))
            .Select(resource => new
            {
                resource.FileName,
                resource.Path,
                Probe = _readSessions.GetSession(resource.Path).ProbeIndex()
            })
            .ToArray();
        var integrityDiagnostics = resources
            .Where(resource => !resource.Probe.IsComplete)
            .Select(resource => $"{resource.FileName}: {resource.Probe.Diagnostic}")
            .ToArray();
        if (integrityDiagnostics.Length > 0)
        {
            return new ImageAssignmentAvailabilityReport(
                Array.Empty<int>(),
                Array.Empty<ImageAssignmentRejectedCandidate>(),
                fingerprints,
                false)
            {
                IsArchiveIntegrityValid = false,
                IntegrityDiagnostics = integrityDiagnostics
            };
        }
        var completeResources = resources
            .Where(resource => resource.Probe.ParsedEntryCount > 0)
            .Select(resource => new
            {
                resource.FileName,
                resource.Path,
                EntryCount = resource.Probe.ParsedEntryCount
            })
            .ToArray();
        if (completeResources.Length == 0)
            return new ImageAssignmentAvailabilityReport(
                Array.Empty<int>(),
                Array.Empty<ImageAssignmentRejectedCandidate>(),
                fingerprints,
                false);

        var layout = _layoutService.Resolve(project);
        var maxUnitImageNumber = completeResources.Max(resource => resource.EntryCount);
        var maxSImageId = CalculateSImageIdUpperBound(layout, maxUnitImageNumber);
        var result = new List<int>();
        var rejected = new List<ImageAssignmentRejectedCandidate>();
        for (var sImageId = includeZero ? 0 : 1; sImageId <= maxSImageId; sImageId++)
        {
            if (sImageId == 0)
            {
                continue;
            }

            var mapping = CharacterImageResourceService.ResolveSUnitImageMapping(project, sImageId, jobId: null, CharacterImageResourceService.DefaultSPreviewFactionSlot);
            if (mapping.ImageNumbers.Count == 0)
            {
                rejected.Add(new ImageAssignmentRejectedCandidate(sImageId, new[] { mapping.Detail }));
                continue;
            }

            var reasons = new List<string>();
            var anyReadable = false;
            foreach (var imageNumber in mapping.ImageNumbers.Where(number => number > 0))
            {
                foreach (var resource in completeResources)
                {
                    if (TryReadAvailability(
                            resource.Path,
                            imageNumber,
                            resource.EntryCount,
                            Path.GetFileName(resource.Path),
                            out var reason))
                    {
                        anyReadable = true;
                        break;
                    }
                    reasons.Add(reason);
                }
                if (anyReadable) break;
            }
            if (anyReadable)
            {
                result.Add(sImageId);
            }
            else
            {
                rejected.Add(new ImageAssignmentRejectedCandidate(sImageId, reasons.Distinct().ToArray()));
            }
        }

        return new ImageAssignmentAvailabilityReport(result, rejected, fingerprints, false);
    }

    private static int CalculateSImageIdUpperBound(CharacterImageLayout layout, int maxUnitImageNumber)
    {
        if (maxUnitImageNumber >= layout.OneStageSpecialStartImageNumber)
        {
            return checked(layout.ThreeStageSpecialCount +
                           maxUnitImageNumber -
                           layout.OneStageSpecialStartImageNumber +
                           1);
        }

        var firstThreeStageImageNumber = layout.DefaultUnitImageCount + 1;
        if (maxUnitImageNumber < firstThreeStageImageNumber) return 0;
        var availableImageCount = maxUnitImageNumber - firstThreeStageImageNumber + 1;
        return Math.Min(layout.ThreeStageSpecialCount, checked((availableImageCount + 2) / 3));
    }

    private bool TryReadAvailability(
        string path,
        int imageNumber,
        int entryCount,
        string label,
        out string reason)
    {
        if (imageNumber <= 0 || imageNumber > entryCount)
        {
            reason = $"{label} #{imageNumber}：图号越界，条目数 {entryCount}。";
            return false;
        }

        if (!TryProbeRsEntry(path, imageNumber, out var probe, out var detail) || probe == null)
        {
            reason = $"{label} #{imageNumber}：{detail}";
            return false;
        }
        if (!probe.Strip.IsSupportedLayout)
        {
            reason = $"{label} #{imageNumber}：{probe.Strip.Detail}";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool IsStructurallyUsable(E5ImageEntry entry, int rawWidth, int frameHeight)
    {
        if (entry.StoredLength <= 0 || entry.DecodedLength <= 0 || entry.Offset < 0) return false;
        if (entry.Kind is "PNG" or "BMP" or "JPG" or "LS12") return true;
        if (rawWidth <= 1 || frameHeight <= 1) return true;
        return entry.DecodedLength >= rawWidth * frameHeight && entry.DecodedLength % rawWidth == 0;
    }

    private bool CanRenderE5EntryFrame(CczProject project, string path, int imageNumber, int rawWidth, int frameHeight)
    {
        using var frame = TryRenderE5EntryFrame(project, path, imageNumber, rawWidth, frameHeight, out _);
        return frame != null;
    }

    private byte[]? TryReadEntryBytes(string path, E5ImageEntry entry, out string detail)
    {
        detail = string.Empty;
        try
        {
            return _readSessions.GetSession(path).ReadDecodedEntry(entry.Number);
        }
        catch (Exception ex)
        {
            detail = ex.Message + BuildEntryLocationText(entry);
            return null;
        }
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
            var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImage(image, new Rectangle(Point.Empty, bitmap.Size));
            if (!isPng) ApplyMagentaTransparency(bitmap);
            return bitmap;
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

    private static Bitmap? TryRenderRawIndexedStrip(byte[] bytes, RsFrameLayout layout, IReadOnlyList<Color> palette, string paletteSourceMode, out string paletteMode)
    {
        using var strip = TryRenderRawIndexedStripBitmap(bytes, layout.FrameWidth, layout.FrameHeight, palette, paletteSourceMode, out paletteMode);
        return strip == null ? null : CropRepresentativeFrame(strip, layout);
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

    private static Bitmap CropRepresentativeFrame(Bitmap strip, RsFrameLayout layout)
    {
        Bitmap? fallback = null;
        for (var frameIndex = 0; frameIndex < layout.ExpectedFrameCount; frameIndex++)
        {
            var frame = RsStripLayoutService.CropFrame(strip, layout, frameIndex);
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

        return fallback ?? RsStripLayoutService.CropFrame(strip, layout, 0);
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
                var destination = new Rectangle(x, y, width, height);
                g.DrawImage(image, destination);
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

    private sealed record SRepresentativeSpec(
        string FileName,
        int FrameWidth,
        int FrameHeight,
        int FrameIndex,
        string Label);

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

internal sealed record CharacterImageAnimationPreview(
    ImageAssignmentResourceKind Kind,
    int ImageId,
    int StageSlot,
    string Title,
    int Columns,
    int Rows,
    int CellWidth,
    int CellHeight,
    IReadOnlyList<CharacterImageAnimationCell> Cells,
    IReadOnlyList<string> Messages) : IDisposable
{
    public IReadOnlyList<CachedPreviewImage> PrecomposedFrames { get; init; } = Array.Empty<CachedPreviewImage>();

    public IReadOnlyList<CharacterImageAnimationSequence> Sequences { get; init; } = Array.Empty<CharacterImageAnimationSequence>();

    public int ReadableFrameCount => Sequences.Count > 0
        ? Sequences.Sum(sequence => sequence.Frames.Count(frame => frame.Bitmap != null))
        : Cells.Sum(cell => cell.Frames.Count(frame => frame != null));

    public int MissingFrameCount => Sequences.Count > 0
        ? Sequences.Sum(sequence => sequence.Frames.Count(frame => frame.Bitmap == null))
        : Cells.Sum(cell => cell.Frames.Count(frame => frame == null));

    public int MaxFrameCount => Sequences.Count > 0
        ? Math.Max(1, Sequences.Max(sequence => sequence.Frames.Count))
        : PrecomposedFrames.Count > 0 ? PrecomposedFrames.Count
        : Cells.Count == 0 ? 1 : Math.Max(1, Cells.Max(cell => cell.Frames.Count));

    public void Dispose()
    {
        foreach (var cell in Cells)
        {
            foreach (var frame in cell.Frames)
            {
                frame?.Dispose();
            }
        }

        foreach (var sequence in Sequences) sequence.Dispose();
    }
}

internal sealed record CharacterImageAnimationCell(
    int Row,
    int Column,
    string Label,
    IReadOnlyList<Bitmap?> Frames);

internal sealed record CharacterImageAnimationSequenceFrame(
    int PhysicalFrameIndex,
    Bitmap? Bitmap);

internal sealed record CharacterImageAnimationSequence(
    RsActionSequenceDefinition Definition,
    string SourcePath,
    int ImageNumber,
    int RequestedStageSlot,
    int EffectiveStageSlot,
    IReadOnlyList<CharacterImageAnimationSequenceFrame> Frames) : IDisposable
{
    public void Dispose()
    {
        foreach (var frame in Frames) frame.Bitmap?.Dispose();
    }
}
