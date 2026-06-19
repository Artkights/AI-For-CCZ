using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ImageResourceCatalogService
{
    private readonly E5ImageReplaceService _e5ImageService = new();
    private readonly E5ImageRenderService _e5ImageRenderService = new();
    private readonly ItemIconPreviewService _iconPreviewService = new();
    private readonly Dictionary<string, IReadOnlyList<E5ImageEntryInfo>> _indexCache = new(StringComparer.OrdinalIgnoreCase);

    public void ClearCache() => _indexCache.Clear();

    public IReadOnlyList<ImageResourceFileInfo> BuildCatalog(CczProject project)
    {
        var result = new List<ImageResourceFileInfo>();
        var knownResources = Ccz66RevisedLayout.Is66(project)
            ? KnownResources.Concat(Known66Resources).Where(resource => !Is66ObsoleteCatalogResource(resource)).ToArray()
            : KnownResources;
        foreach (var resource in knownResources)
        {
            var path = ResolveResourcePath(project, resource);
            var exists = File.Exists(path);
            var entries = exists && resource.Kind == ImageResourceKind.E5Indexed
                ? GetIndex(path)
                : Array.Empty<E5ImageEntryInfo>();
            var externalIconCount = exists && resource.Kind == ImageResourceKind.ExternalIcon
                ? GetExternalIconCount(project, resource)
                : 0;
            var lsInfo = exists && resource.Kind == ImageResourceKind.LsStatusOnly
                ? TryReadLsResource(path, resource.Category)
                : null;
            var entryCount = resource.Kind == ImageResourceKind.ExternalIcon ? externalIconCount : entries.Count;
            var canReplace = entryCount > 0 && resource.CanReplace &&
                             (resource.Kind == ImageResourceKind.ExternalIcon || entries.Count > 0);
            var kindSummary = BuildKindSummary(resource, entries, externalIconCount, lsInfo);
            var status = BuildResourceStatus(resource, exists, entries.Count, externalIconCount, lsInfo);

            result.Add(new ImageResourceFileInfo
            {
                Key = resource.Key,
                Category = resource.Category,
                DisplayName = resource.DisplayName,
                FileName = resource.FileName,
                Aliases = string.Join(" / ", resource.Aliases),
                Usage = resource.Usage,
                RelativePath = resource.ResolvedRelativePath,
                Path = path,
                Exists = exists,
                SizeBytes = exists ? new FileInfo(path).Length : 0,
                EntryCount = entryCount,
                SupportsE5Index = entries.Count > 0,
                SupportsPreview = resource.Kind != ImageResourceKind.LsStatusOnly && entryCount > 0,
                CanReplace = canReplace,
                ResourceFormat = resource.Kind switch
                {
                    ImageResourceKind.ExternalIcon => "DLL图标",
                    ImageResourceKind.LsStatusOnly => "LS状态",
                    _ => "E5索引"
                },
                KindSummary = kindSummary,
                Status = status,
                SafetyNote = BuildSafetyNote(resource, entryCount, entries.Count, canReplace)
            });
        }

        AddDiscoveredE5Resources(project, result);
        return result
            .OrderBy(x => x.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<ImageResourceEntryInfo> ReadEntries(ImageResourceFileInfo resource)
    {
        if (!resource.Exists || !resource.SupportsPreview) return Array.Empty<ImageResourceEntryInfo>();
        if (resource.ResourceFormat.Equals("DLL图标", StringComparison.OrdinalIgnoreCase))
        {
            return Enumerable.Range(0, resource.EntryCount)
                .Select(iconIndex => new ImageResourceEntryInfo
                {
                    ResourceKey = resource.Key,
                    Category = resource.Category,
                    ResourceName = resource.DisplayName,
                    FileName = resource.FileName,
                    Path = resource.Path,
                    ImageNumber = iconIndex,
                    IndexOffset = 0,
                    DataOffset = 0,
                    StoredLength = 0,
                    DecodedLength = 0,
                    IsCompressed = false,
                    Kind = "DLL图标",
                    Usage = BuildEntryUsage(resource, iconIndex),
                    CanReplace = resource.CanReplace
                })
                .ToList();
        }

        if (!resource.SupportsE5Index) return Array.Empty<ImageResourceEntryInfo>();
        return GetIndex(resource.Path)
            .Select(entry => new ImageResourceEntryInfo
            {
                ResourceKey = resource.Key,
                Category = resource.Category,
                ResourceName = resource.DisplayName,
                FileName = resource.FileName,
                Path = resource.Path,
                ImageNumber = entry.ImageNumber,
                IndexOffset = entry.IndexOffset,
                DataOffset = entry.DataOffset,
                StoredLength = entry.StoredLength,
                DecodedLength = entry.DecodedLength,
                IsCompressed = entry.IsCompressed,
                Kind = entry.Kind,
                Usage = BuildEntryUsage(resource, entry.ImageNumber),
                CanReplace = resource.CanReplace
            })
            .ToList();
    }

    public Bitmap? RenderEntryPreview(CczProject project, ImageResourceEntryInfo entry, int canvasWidth = 360, int canvasHeight = 260)
    {
        if (!File.Exists(entry.Path)) return null;

        if (entry.Kind.Equals("DLL图标", StringComparison.OrdinalIgnoreCase))
        {
            var size = Math.Max(96, Math.Min(canvasWidth, canvasHeight));
            return _iconPreviewService.BuildPreview(project, entry.ImageNumber, entry.FileName, ResolveIconDisplayName(entry.FileName), size).Bitmap;
        }

        byte[] bytes;
        try
        {
            bytes = _e5ImageService.ReadEntryBytes(entry.Path, entry.ImageNumber);
        }
        catch
        {
            return null;
        }

        return _e5ImageRenderService.RenderEntry(project, entry.FileName, bytes, canvasWidth, canvasHeight, out _);
    }

    public ImageResourceEntryInfo? TryGetEntry(ImageResourceFileInfo resource, int imageNumber)
        => ReadEntries(resource).FirstOrDefault(x => x.ImageNumber == imageNumber);

    public ImageResourceFileInfo? FindCatalogItem(CczProject project, string keyOrFileName)
    {
        var catalog = BuildCatalog(project);
        return catalog.FirstOrDefault(item =>
            item.Key.Equals(keyOrFileName, StringComparison.OrdinalIgnoreCase) ||
            item.FileName.Equals(keyOrFileName, StringComparison.OrdinalIgnoreCase) ||
            item.DisplayName.Equals(keyOrFileName, StringComparison.OrdinalIgnoreCase));
    }

    public static string BuildEntryUsage(ImageResourceFileInfo resource, int imageNumber)
    {
        var name = resource.FileName;
        if (name.Equals("Pmapobj.e5", StringComparison.OrdinalIgnoreCase))
        {
            var rId = (imageNumber - 1) / 2;
            var side = imageNumber % 2 == 1 ? "正面" : "反面";
            return $"人物 R 形象 R={rId} {side}";
        }

        if (name.Equals("Face.e5", StringComparison.OrdinalIgnoreCase))
        {
            if (imageNumber <= 8) return $"头像组 0 的表情候选 #{imageNumber}";
            return $"Data 头像号 {imageNumber - 8}";
        }

        if (name.StartsWith("Unit_", StringComparison.OrdinalIgnoreCase))
        {
            if (imageNumber <= 180)
            {
                var job = (imageNumber - 1) / 3;
                var slot = ((imageNumber - 1) % 3) + 1;
                return $"普通三转/默认 Unit 图；职业候选={job}，阵营槽={slot}";
            }

            if (imageNumber <= 240)
            {
                var job = (imageNumber - 181) / 3;
                var slot = ((imageNumber - 181) % 3) + 1;
                return $"普通一转 Unit 图；职业候选={job}，阵营槽={slot}";
            }

            if (imageNumber <= 336)
            {
                var sId = ((imageNumber - 241) / 3) + 1;
                return $"S 特殊三转形象 S={sId}";
            }

            return $"S 特殊一转形象 S={imageNumber - 304}";
        }

        if (name.Equals("Hitarea.e5", StringComparison.OrdinalIgnoreCase)) return $"攻击/施法范围字段值 {imageNumber - 1}";
        if (name.Equals("Effarea.e5", StringComparison.OrdinalIgnoreCase)) return $"攻击/策略穿透字段值 {imageNumber - 1}";
        if (name.Equals("Meff.e5", StringComparison.OrdinalIgnoreCase)) return $"策略动画字段值 {imageNumber - 1}";
        if (name.Equals("Tr.e5", StringComparison.OrdinalIgnoreCase)) return $"R 插图候选；脚本图号需结合信息传送28验证";
        if (name.Equals("Item.e5", StringComparison.OrdinalIgnoreCase)) return Build66ItemE5Usage(imageNumber);
        if (name.Equals("Mtem.e5", StringComparison.OrdinalIgnoreCase)) return $"6.6 Mtem.e5 strategy icon field value {imageNumber - 1}";
        if (name.Equals("DT.e5", StringComparison.OrdinalIgnoreCase)) return $"6.6 DT.e5 dynamic image candidate #{imageNumber} for 72-12/72-32";
        if (name.Equals("Fb.e5", StringComparison.OrdinalIgnoreCase)) return $"6.6 Fb.e5 half-body dialogue candidate #{imageNumber}";
        if (name.Equals("Pmap.e5", StringComparison.OrdinalIgnoreCase)) return $"6.6 Pmap.e5 scene terrain image candidate #{imageNumber}";
        if (name.Equals("U_select.e5", StringComparison.OrdinalIgnoreCase))
        {
            return imageNumber switch
            {
                22 => "6.6 U_select #22 custom R numeric image",
                23 => "6.6 U_select #23 half-body white frame",
                25 => "6.6 U_select #25 buff arrow",
                >= 26 and <= 30 => $"6.6 U_select #{imageNumber} custom R numeric image",
                31 => "6.6 U_select #31 command icons",
                32 => "6.6 U_select #32 terrain image",
                _ => resource.Usage
            };
        }

        if (name.Equals("Itemicon.dll", StringComparison.OrdinalIgnoreCase)) return $"物品/道具图标字段值 {imageNumber}";
        if (name.Equals("Mgcicon.dll", StringComparison.OrdinalIgnoreCase)) return $"策略图标字段值 {imageNumber}";
        if (name.Equals("Cmdicon.dll", StringComparison.OrdinalIgnoreCase)) return $"命令图标候选编号 {imageNumber}";
        return resource.Usage;
    }

    private static string Build66ItemE5Usage(int imageNumber)
    {
        return imageNumber switch
        {
            1 => "6.6 Item.e5 blank small icon; field 0 small slot",
            2 => "6.6 Item.e5 blank large icon; field 0 large preview slot",
            > 2 when imageNumber % 2 == 1 => $"6.6 Item.e5 item icon field value {(imageNumber - 1) / 2}; small slot #{imageNumber}",
            > 2 => $"6.6 Item.e5 item icon field value {(imageNumber - 2) / 2}; large preview slot #{imageNumber}",
            _ => "6.6 Item.e5 invalid icon slot"
        };
    }

    public string ResolveResourcePath(CczProject project, string fileName)
        => ResolveResourcePath(project, new ImageResourceDefinition(
            fileName,
            "其它图片",
            Path.GetFileName(fileName),
            fileName,
            Array.Empty<string>(),
            "项目内 E5 图片资源",
            ImageResourceKind.E5Indexed,
            CanReplace: true));

    private IReadOnlyList<E5ImageEntryInfo> GetIndex(string path)
    {
        path = Path.GetFullPath(path);
        if (_indexCache.TryGetValue(path, out var cached)) return cached;
        var entries = _e5ImageService.ReadIndex(path);
        _indexCache[path] = entries;
        return entries;
    }

    private static string BuildKindSummary(
        ImageResourceDefinition resource,
        IReadOnlyList<E5ImageEntryInfo> entries,
        int externalIconCount,
        LsResourceInfo? lsInfo)
    {
        if (resource.Kind == ImageResourceKind.ExternalIcon)
        {
            return externalIconCount > 0 ? $"DLL图标:{externalIconCount}" : string.Empty;
        }

        if (resource.Kind == ImageResourceKind.LsStatusOnly)
        {
            return lsInfo == null ? string.Empty : $"LS:{lsInfo.Magic}";
        }

        return entries.Count > 0
            ? string.Join(" / ", entries
                .GroupBy(x => x.Kind, StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => $"{x.Key}:{x.Count()}"))
            : string.Empty;
    }

    private static string BuildResourceStatus(
        ImageResourceDefinition resource,
        bool exists,
        int e5EntryCount,
        int externalIconCount,
        LsResourceInfo? lsInfo)
    {
        if (!exists) return "未找到";

        return resource.Kind switch
        {
            ImageResourceKind.E5Indexed => e5EntryCount > 0
                ? $"可读取 {e5EntryCount} 个 0x110 图片条目"
                : "文件存在，但未识别为 0x110 图片索引封包",
            ImageResourceKind.ExternalIcon => externalIconCount > 0
                ? $"可读取 {externalIconCount} 个 DLL 图标候选"
                : "文件存在，但未解析到可预览图标候选",
            ImageResourceKind.LsStatusOnly => lsInfo == null
                ? "文件存在，但 LS 探针读取失败"
                : $"LS 状态：magic={lsInfo.Magic}，payload={lsInfo.PayloadLength:N0} 字节，unique={lsInfo.UniqueByteCount}，00占比={lsInfo.ZeroPercent:N1}%",
            _ => "文件存在"
        };
    }

    private static LsResourceInfo? TryReadLsResource(string path, string category)
    {
        try
        {
            return new LsResourceReader().Read(path, category);
        }
        catch
        {
            return null;
        }
    }

    private static void AddDiscoveredE5Resources(CczProject project, List<ImageResourceFileInfo> result)
    {
        var existingPaths = result.Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = new[]
        {
            project.GameRoot,
            Path.Combine(project.GameRoot, "E5")
        };

        var probe = new E5ImageReplaceService();
        foreach (var dir in candidates.Where(Directory.Exists))
        {
            foreach (var path in Directory.GetFiles(dir, "*.e5").OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
            {
                if (existingPaths.Contains(path)) continue;
                var fileName = Path.GetFileName(path);
                if (IsCoreE5File(fileName) || fileName.Equals("Hexzmap.e5", StringComparison.OrdinalIgnoreCase)) continue;

                var entries = probe.ReadIndex(path);
                if (entries.Count == 0) continue;
                var info = new FileInfo(path);
                result.Add(new ImageResourceFileInfo
                {
                    Key = "Discovered:" + fileName,
                    Category = "其它图片",
                    DisplayName = fileName,
                    FileName = fileName,
                    Usage = "项目中发现的 E5 0x110 图片索引资源；用途需结合文件名和实机验证。",
                    RelativePath = Path.GetRelativePath(project.GameRoot, path),
                    Path = path,
                    Exists = true,
                    SizeBytes = info.Length,
                    EntryCount = entries.Count,
                    SupportsE5Index = true,
                    SupportsPreview = true,
                    CanReplace = true,
                    ResourceFormat = "E5索引",
                    KindSummary = string.Join(" / ", entries.GroupBy(x => x.Kind).Select(x => $"{x.Key}:{x.Count()}")),
                    Status = $"可读取 {entries.Count} 个 0x110 图片条目",
                    SafetyNote = "可按单条 E5 图片索引替换；用途未确认时建议先在测试副本验证。"
                });
            }
        }
    }

    private static string ResolveResourcePath(CczProject project, ImageResourceDefinition resource)
    {
        var relative = resource.ResolvedRelativePath.Replace('/', Path.DirectorySeparatorChar);
        var candidates = new List<string>
        {
            Path.Combine(project.GameRoot, relative),
            Path.Combine(project.GameRoot, resource.FileName),
            Path.Combine(project.GameRoot, "E5", resource.FileName),
            Path.Combine(project.GameRoot, "e5", resource.FileName)
        };

        var parentRoot = Directory.GetParent(project.GameRoot)?.FullName;
        if (!string.IsNullOrWhiteSpace(parentRoot))
        {
            candidates.Add(Path.Combine(parentRoot, relative));
            candidates.Add(Path.Combine(parentRoot, "E5", resource.FileName));
        }

        candidates.Add(Path.Combine(project.WorkspaceRoot, relative));
        candidates.Add(Path.Combine(project.WorkspaceRoot, "E5", resource.FileName));

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static Bitmap? TryDecodeStandardImage(byte[] bytes)
    {
        if (bytes.Length < 2) return null;
        var isBmp = bytes[0] == (byte)'B' && bytes[1] == (byte)'M';
        var isJpeg = bytes[0] == 0xFF && bytes[1] == 0xD8;
        var isPng = bytes.Length >= 8 &&
                    bytes[0] == 0x89 &&
                    bytes[1] == (byte)'P' &&
                    bytes[2] == (byte)'N' &&
                    bytes[3] == (byte)'G';
        if (!isBmp && !isJpeg && !isPng) return null;

        try
        {
            using var memory = new MemoryStream(bytes, writable: false);
            using var raw = Image.FromStream(memory, useEmbeddedColorManagement: false, validateImageData: false);
            var bitmap = new Bitmap(raw);
            ApplyMagentaTransparency(bitmap);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? TryRenderRawEntry(CczProject project, ImageResourceEntryInfo entry, byte[] bytes)
    {
        var spec = ResolveRawSpec(entry.FileName);
        if (spec == null) return null;
        var rawLength = bytes.Length - (bytes.Length % spec.Value.Width);
        if (rawLength < spec.Value.Width * spec.Value.FrameHeight) return null;

        var colors = LoadRawPalette(project);
        var rawHeight = rawLength / spec.Value.Width;
        using var strip = new Bitmap(spec.Value.Width, rawHeight, PixelFormat.Format32bppArgb);
        for (var y = 0; y < rawHeight; y++)
        {
            for (var x = 0; x < spec.Value.Width; x++)
            {
                var value = bytes[y * spec.Value.Width + x];
                if (value == 0)
                {
                    strip.SetPixel(x, y, Color.Transparent);
                    continue;
                }

                var gray = Math.Min(255, 48 + value);
                var color = value < colors.Count ? colors[value] : Color.FromArgb(255, gray, gray, gray);
                strip.SetPixel(x, y, IsMagentaKey(color) ? Color.Transparent : color);
            }
        }

        return CropRepresentativeFrame(strip, spec.Value.FrameHeight);
    }

    private static (int Width, int FrameHeight)? ResolveRawSpec(string fileName)
    {
        if (fileName.Equals("Pmapobj.e5", StringComparison.OrdinalIgnoreCase)) return (48, 64);
        if (fileName.Equals("Unit_atk.e5", StringComparison.OrdinalIgnoreCase)) return (64, 64);
        if (fileName.Equals("Unit_mov.e5", StringComparison.OrdinalIgnoreCase)) return (48, 48);
        if (fileName.Equals("Unit_spc.e5", StringComparison.OrdinalIgnoreCase)) return (48, 48);
        return null;
    }

    private static IReadOnlyList<Color> LoadRawPalette(CczProject project)
    {
        var candidates = new[]
        {
            PortableInstallPaths.PaletteTsbPath,
            Path.Combine(project.GameRoot, "tsb")
        };

        var path = candidates.FirstOrDefault(path => File.Exists(path) && new FileInfo(path).Length >= 256 * 4);
        if (path == null) return Array.Empty<Color>();

        var bytes = File.ReadAllBytes(path);
        var colors = new Color[256];
        for (var i = 0; i < colors.Length; i++)
        {
            var offset = i * 4;
            colors[i] = Color.FromArgb(255, bytes[offset + 2], bytes[offset + 1], bytes[offset]);
        }

        return colors;
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

            if (fallback == null) fallback = frame;
            else frame.Dispose();
        }

        return fallback ?? CropFrame(strip, 0, frameHeight);
    }

    private static Bitmap CropFrame(Bitmap strip, int y, int frameHeight)
    {
        var height = Math.Min(frameHeight, strip.Height - y);
        if (height <= 0) height = Math.Min(frameHeight, strip.Height);
        var frame = new Bitmap(strip.Width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(frame);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(strip, new Rectangle(0, 0, frame.Width, frame.Height), new Rectangle(0, y, frame.Width, frame.Height), GraphicsUnit.Pixel);
        ApplyMagentaTransparency(frame);
        return frame;
    }

    private static Bitmap RenderToCanvas(Image source, int canvasWidth, int canvasHeight)
    {
        var width = Math.Max(96, canvasWidth);
        var height = Math.Max(96, canvasHeight);
        var canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(canvas);
        g.Clear(Color.FromArgb(28, 30, 32));
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingQuality = CompositingQuality.HighSpeed;

        using var borderPen = new Pen(Color.FromArgb(90, 100, 108));
        var rect = new Rectangle(8, 8, width - 16, height - 16);
        g.DrawRectangle(borderPen, rect);

        var scale = Math.Min((rect.Width - 8) / (float)source.Width, (rect.Height - 8) / (float)source.Height);
        if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0) scale = 1;
        var drawWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        var drawHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
        var x = rect.Left + (rect.Width - drawWidth) / 2;
        var y = rect.Top + (rect.Height - drawHeight) / 2;
        g.DrawImage(source, new Rectangle(x, y, drawWidth, drawHeight));
        return canvas;
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
    {
        if (pixel.A == 0) return true;
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

    private static bool IsCoreE5File(string fileName)
        => fileName.Equals("Data.e5", StringComparison.OrdinalIgnoreCase) ||
           fileName.Equals("Imsg.e5", StringComparison.OrdinalIgnoreCase) ||
           fileName.Equals("Star.e5", StringComparison.OrdinalIgnoreCase);

    private int GetExternalIconCount(CczProject project, ImageResourceDefinition resource)
    {
        if (resource.Kind != ImageResourceKind.ExternalIcon) return 0;

        try
        {
            return _iconPreviewService.BuildPreview(project, 0, resource.FileName, ResolveIconDisplayName(resource.FileName)).AvailableIconCount;
        }
        catch
        {
            return 0;
        }
    }

    private static string ResolveIconDisplayName(string fileName)
    {
        if (fileName.Equals("Itemicon.dll", StringComparison.OrdinalIgnoreCase)) return "物品图标";
        if (fileName.Equals("Mgcicon.dll", StringComparison.OrdinalIgnoreCase)) return "策略图标";
        if (fileName.Equals("Cmdicon.dll", StringComparison.OrdinalIgnoreCase)) return "命令图标";
        return "DLL图标";
    }

    private static string BuildSafetyNote(ImageResourceDefinition resource, int entryCount, int e5EntryCount, bool canReplace)
    {
        if (resource.Kind == ImageResourceKind.ExternalIcon)
        {
            return entryCount > 0
                ? "DLL 图标资源可按字段编号预览，并可替换对应 RT_BITMAP 位图资源；写入前备份，写后生成报告。"
                : "DLL 文件存在但未解析到候选图标；当前只做定位说明。";
        }

        if (resource.Kind == ImageResourceKind.LsStatusOnly)
        {
            return "LS 封装资源当前只做文件定位和状态探针；未确认帧格式、调用参数和重封包规则前，不开放预览猜帧或替换。";
        }

        if (e5EntryCount <= 0)
        {
            return "未识别 0x110 图片索引表，当前只做定位说明，不开放替换。";
        }

        return canReplace
            ? "可替换单个 0x110 图片索引条目；写入前备份，写后复读。战场地图底图仍走地图制作模块。"
            : "已能读取 0x110 图片索引表，但当前资源写回语义仍需实机确认，默认不开放替换。";
    }

    private static bool Is66ObsoleteCatalogResource(ImageResourceDefinition resource)
        => resource.FileName.Equals("Itemicon.dll", StringComparison.OrdinalIgnoreCase) ||
           resource.FileName.Equals("Mgcicon.dll", StringComparison.OrdinalIgnoreCase) ||
           resource.FileName.Equals("ts.e5", StringComparison.OrdinalIgnoreCase);

    private static readonly ImageResourceDefinition[] Known66Resources =
    [
        new("ItemE5", "Icon", "Item.e5", "Item.e5", ["6.6 item icon", "item icon"], "6.6 revised item icons; field value N maps to small image #2N+1 and large image #2N+2; item/treasure preview defaults to the large image. Field 0 uses blank #1/#2.", ImageResourceKind.E5Indexed, true, Path.Combine("E5", "Item.e5")),
        new("MtemE5", "Icon", "Mtem.e5", "Mtem.e5", ["6.6 strategy icon", "strategy icon"], "6.6 revised strategy icons; strategy families are independent and usually spaced by 6; table field value N maps to E5 image #(N+1).", ImageResourceKind.E5Indexed, true, Path.Combine("E5", "Mtem.e5")),
        new("DT", "Icon", "DT.e5", "DT.e5", ["6.6 dynamic image", "72-12", "72-32"], "6.6 revised dynamic image resource for 72-12/72-32.", ImageResourceKind.E5Indexed, true, Path.Combine("E5", "DT.e5")),
        new("Fb", "Icon", "Fb.e5", "Fb.e5", ["6.6 half-body", "7A"], "6.6 revised half-body dialogue resource for 7A.", ImageResourceKind.E5Indexed, true, Path.Combine("E5", "Fb.e5")),
        new("Pmap", "Icon", "Pmap.e5", "Pmap.e5", ["6.6 Pmap", "scene terrain"], "6.6 revised scene terrain image resource; distinct from Pmapobj.e5 R actor frames.", ImageResourceKind.E5Indexed, true, Path.Combine("E5", "Pmap.e5"))
    ];
    private static readonly ImageResourceDefinition[] KnownResources =
    [
        new("Face", "角色图片", "角色头像 Face.e5", "Face.e5", ["face.e5", "头像"], "人物小头像；Data 头像号映射后读取。", ImageResourceKind.E5Indexed, true, Path.Combine("E5", "Face.e5")),
        new("Pmapobj", "角色图片", "R 形象 Pmapobj.e5", "Pmapobj.e5", ["R形象", "R/S形象"], "人物 R 形象正反面图，R=n -> 2n+1/2n+2。", ImageResourceKind.E5Indexed, true),
        new("UnitMov", "角色图片", "S 移动 Unit_mov.e5", "Unit_mov.e5", ["S移动", "S形象"], "人物 S 移动帧资源。", ImageResourceKind.E5Indexed, true),
        new("UnitAtk", "角色图片", "S 攻击 Unit_atk.e5", "Unit_atk.e5", ["S攻击", "S形象"], "人物 S 攻击帧资源。", ImageResourceKind.E5Indexed, true),
        new("UnitSpc", "角色图片", "S 特技 Unit_spc.e5", "Unit_spc.e5", ["S特技", "S形象"], "人物 S 特技帧资源。", ImageResourceKind.E5Indexed, true),
        new("Hitarea", "范围图片", "攻击范围 Hitarea.e5", "Hitarea.e5", ["hitarea.e5", "攻击范围图"], "攻击/施法范围图，字段值 + 1 映射图号。", ImageResourceKind.E5Indexed, true, Path.Combine("E5", "Hitarea.e5")),
        new("Effarea", "范围图片", "穿透范围 Effarea.e5", "Effarea.e5", ["efffare.e5", "effarea.e5", "穿透范围图"], "攻击/策略穿透范围图，字段值 + 1 映射图号。", ImageResourceKind.E5Indexed, true, Path.Combine("E5", "Effarea.e5")),
        new("Meff", "策略图片", "策略小动画 Meff.e5", "Meff.e5", ["策略小动画", "小动画", "Meff"], "策略小动画候选；MgMeff 字段值 N 映射 Meff.e5 图号 N+1。非标准帧格式仍需旧工具或实机确认。", ImageResourceKind.E5Indexed, true, Path.Combine("E5", "Meff.e5")),
        new("Mcall", "策略图片", "策略大动画 Mcall*.e5", "Mcall00.e5", ["策略大动画", "大动画", "Mcall"], "策略大动画/召唤动画候选；MgMcall 字段值 >=100 时映射 Mcall{值-100}.e5。当前只做 LS 状态探针，不猜测帧格式。", ImageResourceKind.LsStatusOnly, false, Path.Combine("E5", "Mcall00.e5")),
        new("Logo", "背景图片", "封面/背景 Logo.e5", "Logo.e5", ["logo.e5", "封面", "单挑背景", "游戏结束背景"], "封面、单挑背景、游戏结束背景等大图候选。", ImageResourceKind.E5Indexed, true, Path.Combine("E5", "Logo.e5")),
        new("Mmap", "背景图片", "R 背景 Mmap.e5", "Mmap.e5", ["mmap.e5", "R背景图"], "R 场景背景/大图候选；不等同于战场地图底图。", ImageResourceKind.E5Indexed, true, Path.Combine("E5", "Mmap.e5")),
        new("Tr", "背景图片", "R 插图 Tr.e5", "Tr.e5", ["tr.e5", "R插图"], "信息传送28 使用的 R 插图候选；脚本图号仍需实机确认。", ImageResourceKind.E5Indexed, true, Path.Combine("E5", "Tr.e5")),
        new("USelect", "界面图片", "U_select.e5", "U_select.e5", ["U_select", "数字图", "选择框"], "界面选择框、数字图和部分战场动图相关候选。", ImageResourceKind.E5Indexed, true, Path.Combine("E5", "U_select.e5")),
        new("Gate", "界面图片", "Gate.e5", "Gate.e5", ["gete.e5", "gate.e5"], "剧情/过场门类图片候选，旧剧本编辑器源码可见 Gate 引用。", ImageResourceKind.E5Indexed, true, Path.Combine("E5", "Gate.e5")),
        new("Weather", "界面图片", "Weather.e5", "Weather.e5", ["warther.e5", "天气图"], "天气相关图片/动画候选。", ImageResourceKind.E5Indexed, true, Path.Combine("E5", "Weather.e5")),
        new("Mark", "界面图片", "Mark.e5", "Mark.e5", ["mark.e5", "标记图"], "标记、小图标或状态图候选；当前样本不是 0x110 索引封包。", ImageResourceKind.E5Indexed, false, Path.Combine("E5", "Mark.e5")),
        new("ItemIcon", "DLL图标", "道具图标 Itemicon.dll", "Itemicon.dll", ["道具图标", "物品图标"], "道具/物品图标，按 RT_BITMAP 成对候选预览和替换。", ImageResourceKind.ExternalIcon, true),
        new("MgcIcon", "DLL图标", "策略图标 Mgcicon.dll", "Mgcicon.dll", ["策略图标", "法术图标"], "策略/法术图标，按 RT_BITMAP 成对候选预览和替换。", ImageResourceKind.ExternalIcon, true),
        new("CmdIcon", "DLL图标", "命令图标 Cmdicon.dll", "Cmdicon.dll", ["命令图标"], "命令图标候选，按 RT_BITMAP 成对候选预览和替换。", ImageResourceKind.ExternalIcon, true)
    ];

    private sealed record ImageResourceDefinition(
        string Key,
        string Category,
        string DisplayName,
        string FileName,
        IReadOnlyList<string> Aliases,
        string Usage,
        ImageResourceKind Kind,
        bool CanReplace,
        string? RelativePath = null)
    {
        public string ResolvedRelativePath { get; } = RelativePath ?? FileName;
    }

    private enum ImageResourceKind
    {
        E5Indexed,
        ExternalIcon,
        LsStatusOnly
    }
}
