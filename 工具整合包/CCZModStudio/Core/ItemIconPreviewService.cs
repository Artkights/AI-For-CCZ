using CCZModStudio.Models;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CCZModStudio.Core;

public sealed class ItemIconPreviewService
{
    private readonly Dictionary<string, IReadOnlyList<DllIconBitmapResource>> _bitmapResourceCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly DllIconBitmapCodec _dllIconCodec = new();
    private readonly E5ImageReplaceService _e5ImageService = new();
    private readonly E5ImageRenderService _e5ImageRenderService = new();

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

        if (!DllIconBitmapCodec.IsGameIconResourceFile(Path.GetFileName(resourceFileName)))
        {
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
                    : $"来源 {resourceFileName}；字段图标={iconIndex}；可枚举图标={extractIconCount}。当前按 Windows 图标资源顺序预览。";
                return new ItemIconPreviewResult(
                    sourcePath,
                    iconIndex,
                    extractIconCount,
                    iconBitmap,
                    iconMessage,
                    iconBitmap == null ? null : new Bitmap(iconBitmap),
                    null,
                    null,
                    Array.Empty<IconResourceVariantInfo>(),
                    "Windows ICO");
            }
        }

        var bitmapResources = GetBitmapResources(sourcePath);
        var bitmapIconCount = _dllIconCodec.EstimateIconCount(bitmapResources);
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

        DllIconGameSlot slot;
        try
        {
            slot = _dllIconCodec.ResolveGameIconSlot(sourcePath, bitmapResources, iconIndex, resourceFileName);
        }
        catch (InvalidOperationException ex)
        {
            return new ItemIconPreviewResult(
                sourcePath,
                iconIndex,
                bitmapIconCount,
                null,
                ex.Message);
        }

        using var largeRaw = DecodeSelected(slot.LargeSelectedVariant);
        using var smallRaw = DecodeSelected(slot.SmallSelectedVariant);
        Bitmap? native = largeRaw != null ? new Bitmap(largeRaw) : smallRaw != null ? new Bitmap(smallRaw) : null;
        Bitmap? large = largeRaw != null ? new Bitmap(largeRaw) : null;
        Bitmap? small = smallRaw != null ? new Bitmap(smallRaw) : null;
        var bitmap = native == null ? null : DllIconBitmapCodec.RenderPixelPreview(native, canvasSize);
        var variants = ToVariantInfo(slot.Variants);
        var smallVariant = slot.SmallSelectedVariant == null ? null : ToVariantInfo(slot.SmallSelectedVariant);
        var largeVariant = slot.LargeSelectedVariant == null ? null : ToVariantInfo(slot.LargeSelectedVariant);
        var selectionDetail = $"small=ID{slot.SmallResourceId} {FormatSelectedVariant(smallVariant, slot.SmallSelectedByWin32Probe)}；large=ID{slot.LargeResourceId} {FormatSelectedVariant(largeVariant, slot.LargeSelectedByWin32Probe)}";
        var warningText = slot.Warnings.Count == 0 ? string.Empty : " 警告：" + string.Join("；", slot.Warnings);
        var message = bitmap == null
            ? $"{displayName}编号 {iconIndex} 匹配到 {resourceFileName} RT_BITMAP 资源 {string.Join("/", slot.Variants.Select(x => x.Id).Distinct())}，但 DIB 转图像失败。{warningText}"
            : $"来源 {resourceFileName}；字段图标={iconIndex}；{selectionDetail}；候选图标数={bitmapIconCount}。预览按游戏实际 RT_BITMAP 槽重读渲染；选择模式={slot.SelectionMode}。{warningText}";
        return new ItemIconPreviewResult(
            sourcePath,
            iconIndex,
            bitmapIconCount,
            bitmap,
            message,
            native,
            small,
            large,
            variants,
            "DLL RT_BITMAP",
            smallVariant,
            largeVariant,
            slot.SelectionMode,
            slot.Warnings);
    }

    public int GetIconCount(CczProject project)
    {
        var resourceFile = Ccz66RevisedLayout.Is66(project)
            ? Ccz66RevisedLayout.ResolveItemIconResourceFile(project)
            : "Itemicon.dll";
        var sourcePath = Ccz66RevisedLayout.Is66(project)
            ? Ccz66RevisedLayout.ResolveResourcePath(project, resourceFile)
            : ResolveIconDll(project, resourceFile);
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return 0;
        }

        if (Ccz66RevisedLayout.Is66(project))
        {
            return _e5ImageService.ReadIndex(sourcePath).Count;
        }

        if (DllIconBitmapCodec.IsGameIconResourceFile(Path.GetFileName(resourceFile)))
        {
            return _dllIconCodec.EstimateIconCount(GetBitmapResources(sourcePath));
        }

        return GetIconCount(sourcePath);
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
        var imageNumber = isItemE5
            ? Ccz66RevisedLayout.ResolveItemIconPreviewImageNumber(iconIndex)
            : Ccz66RevisedLayout.ResolveStrategyIconImageNumber(iconIndex);
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
        Bitmap? native = null;
        Bitmap? small = null;
        Bitmap? large = null;
        try
        {
            var bytes = _e5ImageService.ReadEntryBytes(sourcePath, imageNumber);
            native = _e5ImageRenderService.TryDecodeStandardImage(bytes);
            if (native != null)
            {
                bitmap = DllIconBitmapCodec.RenderPixelPreview(native, canvasSize);
            }
            else
            {
                bitmap = _e5ImageRenderService.RenderEntry(project, Path.GetFileName(sourcePath), bytes, canvasSize, canvasSize, out _);
                native = bitmap == null ? null : new Bitmap(bitmap);
            }

            if (isItemE5)
            {
                var (smallNumber, largeNumber) = Ccz66RevisedLayout.ResolveItemIconImageNumbers(iconIndex);
                small = TryDecodeE5Image(sourcePath, smallNumber);
                large = TryDecodeE5Image(sourcePath, largeNumber);
            }
        }
        catch
        {
            bitmap = null;
        }

        var note = Path.GetFileName(sourcePath).Equals("Item.e5", StringComparison.OrdinalIgnoreCase)
            ? "6.6 修正版道具图标资源；字段编号按 E5 1-based 图号减 1 预览，#1/#2 常作为空白小/大图标。"
            : "6.6 修正版策略图标资源；字段编号按 E5 1-based 图号减 1 预览，策略四系图标通常间隔 6。";
        if (isItemE5)
        {
            var (smallNumber, largeNumber) = Ccz66RevisedLayout.ResolveItemIconImageNumbers(iconIndex);
            note = $"6.6 revised Item.e5 item icon: field={iconIndex}, small=#{smallNumber}, large=#{largeNumber}; treasure/item preview uses the large image by default.";
        }
        else
        {
            note = "6.6 revised Mtem.e5 strategy icon: field value N maps to E5 image #(N+1); strategy families are usually spaced by 6.";
        }

        var previewMessage = $"Source {Path.GetFileName(sourcePath)}; field={iconIndex}; E5 image #{imageNumber}; available={entries.Count}. {note}";
        if (previewMessage.Length > 0)
        {
            return new ItemIconPreviewResult(
                sourcePath,
                iconIndex,
                entries.Count,
                bitmap,
                previewMessage,
                native,
                small,
                large,
                Array.Empty<IconResourceVariantInfo>(),
                "E5");
        }

        return new ItemIconPreviewResult(
            sourcePath,
            iconIndex,
            entries.Count,
            bitmap,
            $"来源 {Path.GetFileName(sourcePath)}；字段图标={iconIndex}；E5图号=#{imageNumber}；候选图标数={entries.Count}。{note}",
            native,
            small,
            large,
            Array.Empty<IconResourceVariantInfo>(),
            "E5");
    }

    private static string ResolveItemIconDll(CczProject project)
        => ResolveIconDll(project, "Itemicon.dll");

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

    private IReadOnlyList<DllIconBitmapResource> GetBitmapResources(string sourcePath)
    {
        if (_bitmapResourceCache.TryGetValue(sourcePath, out var cached)) return cached;
        var parsed = _dllIconCodec.ReadBitmapResources(sourcePath);
        _bitmapResourceCache[sourcePath] = parsed;
        return parsed;
    }

    internal static Bitmap? RenderDibForSmoke(byte[] dibBytes, int canvasSize)
    {
        var codec = new DllIconBitmapCodec();
        using var raw = codec.DecodeDib(dibBytes);
        return raw == null ? null : DllIconBitmapCodec.RenderPixelPreview(raw, canvasSize);
    }

    private Bitmap? TryDecodeE5Image(string sourcePath, int imageNumber)
    {
        try
        {
            var bytes = _e5ImageService.ReadEntryBytes(sourcePath, imageNumber);
            return _e5ImageRenderService.TryDecodeStandardImage(bytes);
        }
        catch
        {
            return null;
        }
    }

    private Bitmap? DecodeSelected(DllIconBitmapResource? selected)
        => selected == null ? null : _dllIconCodec.DecodeDib(selected.DibBytes);

    private static string FormatSelectedVariant(IconResourceVariantInfo? variant, bool selectedByProbe)
        => variant == null
            ? "无"
            : $"Lang={variant.LanguageId} {variant.Width}x{variant.Height} {variant.BitCount}bpp {(selectedByProbe ? "FindResource" : "fallback")}";

    private static IconResourceVariantInfo ToVariantInfo(DllIconBitmapResource resource)
        => new()
        {
            ResourceId = resource.Id,
            LanguageId = resource.LanguageId,
            Width = resource.Width,
            Height = resource.Height,
            BitCount = resource.BitCount,
            SizeBytes = resource.SizeBytes
        };

    private static IReadOnlyList<IconResourceVariantInfo> ToVariantInfo(IEnumerable<DllIconBitmapResource> resources)
        => resources
            .Select(ToVariantInfo)
            .ToArray();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string szFileName, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
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
