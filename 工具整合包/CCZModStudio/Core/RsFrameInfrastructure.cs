using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CCZModStudio.Core;

public enum RsFrameResourceKind
{
    R,
    S
}

internal enum RsFacing
{
    Front,
    Back,
    Side
}

internal sealed record RsActionSequenceDefinition(
    string Id,
    string SourceFile,
    string ActionLabel,
    RsFacing? Facing,
    IReadOnlyList<int> PhysicalFrameIndices,
    bool MirrorForRight,
    bool Loop);

internal static class RsActionSequenceCatalog
{
    public const string ContractVersion = "s-action-layout-v1";

    public static IReadOnlyList<RsActionSequenceDefinition> SDefinitions { get; } =
    [
        new("move-front", "Unit_mov.e5", "待机/移动", RsFacing.Front, [0, 1], false, true),
        new("move-back", "Unit_mov.e5", "待机/移动", RsFacing.Back, [2, 3], false, true),
        new("move-side", "Unit_mov.e5", "待机/移动", RsFacing.Side, [4, 5], true, true),
        new("guard-front", "Unit_mov.e5", "防御/受击", RsFacing.Front, [6], false, true),
        new("guard-back", "Unit_mov.e5", "防御/受击", RsFacing.Back, [7], false, true),
        new("guard-side", "Unit_mov.e5", "防御/受击", RsFacing.Side, [8], true, true),
        new("defeat", "Unit_mov.e5", "倒地/退场", null, [9, 10], false, true),
        new("attack-front", "Unit_atk.e5", "攻击", RsFacing.Front, [0, 1, 2, 3], false, true),
        new("attack-back", "Unit_atk.e5", "攻击", RsFacing.Back, [4, 5, 6, 7], false, true),
        new("attack-side", "Unit_atk.e5", "攻击", RsFacing.Side, [8, 9, 10, 11], true, true),
        new("special", "Unit_spc.e5", "特技", null, [0, 1, 2, 3, 4], false, true)
    ];

    public static RsActionSequenceDefinition? Find(string sourceFile, int physicalFrameIndex)
        => SDefinitions.FirstOrDefault(definition =>
            definition.SourceFile.Equals(Path.GetFileName(sourceFile), StringComparison.OrdinalIgnoreCase) &&
            definition.PhysicalFrameIndices.Contains(physicalFrameIndex));

    public static RsActionSequenceDefinition Resolve(string sourceFile, string actionLabel, RsFacing? facing)
        => SDefinitions.FirstOrDefault(definition =>
               definition.SourceFile.Equals(Path.GetFileName(sourceFile), StringComparison.OrdinalIgnoreCase) &&
               definition.ActionLabel.Equals(actionLabel, StringComparison.Ordinal) &&
               definition.Facing == facing)
           ?? throw new InvalidOperationException($"没有定义 {sourceFile} / {actionLabel} / {facing?.ToString() ?? "无方向"} 的动作序列。");

    public static RsFacing DirectionToFacing(string direction)
        => direction switch
        {
            "上" => RsFacing.Back,
            "左" or "右" => RsFacing.Side,
            _ => RsFacing.Front
        };

    public static string BuildPhysicalFrameLabel(string sourceFile, int physicalFrameIndex)
    {
        var definition = Find(sourceFile, physicalFrameIndex);
        if (definition == null) return $"物理帧{physicalFrameIndex}（语义未定义）";
        var sequenceIndex = definition.PhysicalFrameIndices.ToList().IndexOf(physicalFrameIndex);
        var facing = BuildFacingText(definition.Facing, includeRightHint: definition.MirrorForRight);
        return string.Join(" / ", new[]
        {
            $"物理帧{physicalFrameIndex}",
            definition.ActionLabel + (string.IsNullOrWhiteSpace(facing) ? string.Empty : $"·{facing}"),
            $"第{sequenceIndex + 1}帧"
        });
    }

    public static string BuildFacingText(RsFacing? facing, bool includeRightHint = false)
        => facing switch
        {
            RsFacing.Front => "正面/下",
            RsFacing.Back => "背面/上",
            RsFacing.Side => includeRightHint ? "侧面/左（右向镜像）" : "侧面/左",
            _ => string.Empty
        };
}

public sealed record RsFrameLayout(
    RsFrameResourceKind ResourceKind,
    string FileName,
    int FrameWidth,
    int FrameHeight,
    int ExpectedFrameCount);

internal sealed record RsStripProbeResult(
    RsFrameLayout Layout,
    string StorageKind,
    int DecodedWidth,
    int DecodedHeight,
    int AvailableFrameCount,
    int TrailingByteCount,
    bool IsSupportedLayout,
    string Detail);

internal sealed record RsEntryProbeResult(
    RsStripProbeResult Strip,
    int ImageNumber,
    int Offset,
    int StoredLength,
    int DecodedLength,
    string IndexSha256,
    string DecodedSha256)
{
    public string CacheIdentity => string.Join(":",
        ImageNumber,
        Offset,
        StoredLength,
        DecodedLength,
        IndexSha256,
        DecodedSha256,
        Strip.Layout.FrameWidth,
        Strip.Layout.FrameHeight,
        Strip.Layout.ExpectedFrameCount,
        Strip.TrailingByteCount,
        Strip.IsSupportedLayout ? "supported" : "unsupported");
}

internal sealed record ImageAssignmentCandidatePreview(
    CachedPreviewImage? Representative,
    string RepresentativeLabel,
    bool SelectedStageAvailable,
    string StatusText);

internal sealed record ImageAssignmentRejectedCandidate(
    int Id,
    IReadOnlyList<string> Reasons);

internal sealed record ImageAssignmentResourceFingerprint(
    string Path,
    long Length,
    long LastWriteTimeUtcTicks,
    string IndexSha256,
    int EntryCount);

internal sealed record ImageAssignmentAvailabilityReport(
    IReadOnlyList<int> AvailableIds,
    IReadOnlyList<ImageAssignmentRejectedCandidate> RejectedCandidates,
    IReadOnlyList<ImageAssignmentResourceFingerprint> SourceFingerprints,
    bool FromCache)
{
    public bool IsArchiveIntegrityValid { get; init; } = true;
    public IReadOnlyList<string> IntegrityDiagnostics { get; init; } = Array.Empty<string>();
}

/// <summary>
/// The single source of truth for R/S strip validation and physical-frame cropping.
/// A two-byte suffix occurs in several valid 6.5 RAW archives; it belongs to the
/// entry container and must never be interpreted as another partial pixel row.
/// </summary>
internal static class RsStripLayoutService
{
    public const string ContractVersion = "rs-strip-v3";

    public static RsStripProbeResult Probe(string pathOrFileName, string storageKind, byte[] decodedBytes)
    {
        var layout = new RsFrameLayoutResolver().Resolve(pathOrFileName);
        var expectedHeight = checked(layout.FrameHeight * layout.ExpectedFrameCount);
        var expectedPixelLength = checked(layout.FrameWidth * expectedHeight);
        storageKind = string.IsNullOrWhiteSpace(storageKind) ? "未知" : storageKind;

        if (LooksLikeStandardImage(decodedBytes))
        {
            try
            {
                using var stream = new MemoryStream(decodedBytes, writable: false);
                using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
                var supported = image.Width == layout.FrameWidth && image.Height == expectedHeight;
                var detail = supported
                    ? $"{storageKind} {image.Width}x{image.Height}，{layout.ExpectedFrameCount} 帧"
                    : $"{storageKind} 条目布局不受支持：实际 {image.Width}x{image.Height}，要求 {layout.FrameWidth}x{expectedHeight}（{layout.ExpectedFrameCount}×{layout.FrameHeight}）。";
                return new RsStripProbeResult(
                    layout,
                    storageKind,
                    image.Width,
                    image.Height,
                    supported ? layout.ExpectedFrameCount : Math.Max(0, image.Height / layout.FrameHeight),
                    0,
                    supported,
                    detail);
            }
            catch (Exception ex) when (ex is ArgumentException or ExternalException)
            {
                return Unsupported(layout, storageKind, $"{storageKind} 图片解码失败：{ex.Message}");
            }
        }

        var trailingBytes = decodedBytes.Length - expectedPixelLength;
        var rawSupported = trailingBytes is 0 or 2;
        var rawHeight = Math.Max(0, Math.Min(decodedBytes.Length, expectedPixelLength) / layout.FrameWidth);
        var rawFrames = Math.Max(0, rawHeight / layout.FrameHeight);
        var rawDetail = rawSupported
            ? trailingBytes == 0
                ? $"{storageKind} {layout.FrameWidth}x{expectedHeight}，{layout.ExpectedFrameCount} 帧"
                : $"{storageKind} {layout.FrameWidth}x{expectedHeight}，{layout.ExpectedFrameCount} 帧；保留 2 字节容器尾部"
            : $"{storageKind} 条目布局不受支持：decoded={decodedBytes.Length:N0}，要求像素区 {expectedPixelLength:N0} 字节，且只允许额外 0 或 2 字节容器尾部。";
        return new RsStripProbeResult(
            layout,
            storageKind,
            layout.FrameWidth,
            rawHeight,
            rawFrames,
            Math.Max(0, trailingBytes),
            rawSupported,
            rawDetail);
    }

    public static Bitmap CropFrame(Bitmap strip, RsFrameLayout layout, int frameIndex)
    {
        if (strip.Width != layout.FrameWidth)
            throw new InvalidOperationException($"条带宽度 {strip.Width} 不等于标准帧宽 {layout.FrameWidth}。");
        if (frameIndex < 0 || frameIndex >= layout.ExpectedFrameCount)
            throw new ArgumentOutOfRangeException(nameof(frameIndex), $"物理帧索引 {frameIndex} 超出 0-{layout.ExpectedFrameCount - 1}。");

        var source = new Rectangle(
            0,
            checked(frameIndex * layout.FrameHeight),
            layout.FrameWidth,
            layout.FrameHeight);
        if (source.Bottom > strip.Height)
            throw new InvalidOperationException($"条带高度 {strip.Height} 不足以读取物理帧 {frameIndex}（需要到底部 {source.Bottom}）。");

        var frame = new Bitmap(layout.FrameWidth, layout.FrameHeight, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(frame);
        graphics.Clear(Color.Transparent);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(strip, new Rectangle(Point.Empty, frame.Size), source, GraphicsUnit.Pixel);
        return frame;
    }

    private static bool LooksLikeStandardImage(byte[] bytes)
        => bytes.Length >= 2 &&
           ((bytes[0] == (byte)'B' && bytes[1] == (byte)'M') ||
            (bytes[0] == 0xFF && bytes[1] == 0xD8) ||
            (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A })));

    private static RsStripProbeResult Unsupported(RsFrameLayout layout, string storageKind, string detail)
        => new(layout, storageKind, 0, 0, 0, 0, false, detail);
}

public sealed class RsFrameLayoutResolver
{
    private static readonly IReadOnlyDictionary<string, RsFrameLayout> Layouts =
        new Dictionary<string, RsFrameLayout>(StringComparer.OrdinalIgnoreCase)
        {
            ["Pmapobj.e5"] = new(RsFrameResourceKind.R, "Pmapobj.e5", 48, 64, 20),
            ["Unit_mov.e5"] = new(RsFrameResourceKind.S, "Unit_mov.e5", 48, 48, 11),
            ["Unit_atk.e5"] = new(RsFrameResourceKind.S, "Unit_atk.e5", 64, 64, 12),
            ["Unit_spc.e5"] = new(RsFrameResourceKind.S, "Unit_spc.e5", 48, 48, 5)
        };

    public RsFrameLayout? TryResolve(string? pathOrFileName)
    {
        if (string.IsNullOrWhiteSpace(pathOrFileName)) return null;
        return Layouts.TryGetValue(Path.GetFileName(pathOrFileName), out var layout) ? layout : null;
    }

    public RsFrameLayout Resolve(string pathOrFileName)
        => TryResolve(pathOrFileName)
           ?? throw new InvalidOperationException("不是已知的 R/S 形象帧资源：" + pathOrFileName);
}

public enum RsFrameShiftDirection
{
    Left,
    Right,
    Up,
    Down
}

public static class RsFrameShiftService
{
    public static Bitmap Shift(Bitmap source, RsFrameShiftDirection direction)
    {
        if (source.Width <= 0 || source.Height <= 0) throw new ArgumentException("单帧图片尺寸无效。", nameof(source));
        var output = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var sourceX = direction switch
                {
                    RsFrameShiftDirection.Left => x + 1,
                    RsFrameShiftDirection.Right => x - 1,
                    _ => x
                };
                var sourceY = direction switch
                {
                    RsFrameShiftDirection.Up => y + 1,
                    RsFrameShiftDirection.Down => y - 1,
                    _ => y
                };
                output.SetPixel(x, y,
                    sourceX >= 0 && sourceX < source.Width && sourceY >= 0 && sourceY < source.Height
                        ? source.GetPixel(sourceX, sourceY)
                        : Color.Transparent);
            }
        }

        return output;
    }
}
